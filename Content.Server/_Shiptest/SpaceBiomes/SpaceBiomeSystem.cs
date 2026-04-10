using System.Numerics;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Server.GameObjects;
using Robust.Shared.Maths;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

/// <summary>
/// Geometry helper for biome chunk calculations.
/// </summary>
internal static class SpaceBiomeHelpers
{
    public static bool RectCircleIntersect(Box2 rect, Vector2 circPos, float circRadius)
    {
        Vector2 delta = circPos - rect.Center;
        if (delta.X > rect.Width / 2 + circRadius || delta.Y > rect.Height / 2 + circRadius)
            return false;
        if (delta.X < rect.Width / 2 || delta.Y < rect.Height / 2)
            return true;
        delta.X -= rect.Width / 2;
        delta.Y -= rect.Height / 2;
        return delta.Length() < circRadius;
    }
}

/// <summary>
/// Manages chunk-based space biome detection.
/// Space is divided into configurable chunks. Each biome source entity registers
/// its coverage area. Players get assigned the highest-priority biome they're in.
/// </summary>
public sealed class SpaceBiomeSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transform = default!;

    // Chunk-to-biome-sources mapping
    private readonly Dictionary<Vector2, HashSet<EntityUid>> _chunks = new();

    /// <summary>
    /// Size of each chunk in meters. Larger chunks = less memory but lower precision.
    /// </summary>
    private const int ChunkSize = 1000;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpaceBiomeSourceComponent, ComponentInit>(OnSourceInit);
        SubscribeLocalEvent<SpaceBiomeSourceComponent, ComponentShutdown>(OnSourceShutdown);
    }

    private void OnSourceInit(Entity<SpaceBiomeSourceComponent> uid, ref ComponentInit args)
    {
        RegisterBiomeSource(uid, uid.Comp);
    }

    private void OnSourceShutdown(Entity<SpaceBiomeSourceComponent> uid, ref ComponentShutdown args)
    {
        UnregisterBiomeSource(uid, uid.Comp);
    }

    public void RegisterBiomeSource(EntityUid uid, SpaceBiomeSourceComponent source)
    {
        var pos = _transform.GetWorldPosition(uid);
        foreach (var chunkPos in GetCoveredChunks(pos, source.SwapDistance))
        {
            if (!_chunks.TryGetValue(chunkPos, out var sources))
                _chunks[chunkPos] = sources = new HashSet<EntityUid>();
            sources.Add(uid);
        }
    }

    public void UnregisterBiomeSource(EntityUid uid, SpaceBiomeSourceComponent source)
    {
        var pos = _transform.GetWorldPosition(uid);
        foreach (var chunkPos in GetCoveredChunks(pos, source.SwapDistance))
        {
            if (!_chunks.TryGetValue(chunkPos, out var sources))
                continue;

            sources.Remove(uid);
            if (sources.Count == 0)
                _chunks.Remove(chunkPos);
        }
    }

    private static Vector2 GetChunkKey(Vector2 pos)
    {
        return (pos / ChunkSize).Floored() * ChunkSize;
    }

    private static List<Vector2> GetCoveredChunks(Vector2 pos, int radius)
    {
        var result = new List<Vector2>();
        var posFloor = GetChunkKey(pos);
        var chunks = (radius + ChunkSize - 1) / ChunkSize; // ceiling division

        for (var y = -chunks; y <= chunks; y++)
        {
            for (var x = -chunks; x <= chunks; x++)
            {
                var chunkPos = new Vector2(x * ChunkSize, y * ChunkSize) + posFloor;
                var chunkRect = new Box2(chunkPos, chunkPos + new Vector2(ChunkSize));

                if (SpaceBiomeHelpers.RectCircleIntersect(chunkRect, pos, radius))
                    result.Add(chunkPos);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the biome ID at a given world position.
    /// Returns the highest-priority biome that contains this position.
    /// </summary>
    public string GetBiomeAt(Vector2 worldPos)
    {
        var chunkKey = GetChunkKey(worldPos);
        if (!_chunks.TryGetValue(chunkKey, out var sourceUids))
            return "default";

        EntityUid? bestSource = null;
        var bestPriority = int.MinValue;

        foreach (var sourceUid in sourceUids)
        {
            var source = Comp<SpaceBiomeSourceComponent>(sourceUid);
            var sourcePos = _transform.GetWorldPosition(sourceUid);
            var relativePos = worldPos - sourcePos;

            if (!source.ContainsPoint(relativePos))
                continue;

            if (source.Priority > bestPriority ||
                (source.Priority == bestPriority && sourceUid == bestSource))
            {
                bestSource = sourceUid;
                bestPriority = source.Priority;
            }
        }

        return bestSource != null ? Comp<SpaceBiomeSourceComponent>(bestSource.Value).Biome : "default";
    }

    public void RegenerateChunks()
    {
        _chunks.Clear();
        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent>();

        while (query.MoveNext(out var uid, out var source))
        {
            RegisterBiomeSource(uid, source);
        }
    }
}
