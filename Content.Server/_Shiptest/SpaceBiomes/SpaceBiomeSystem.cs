using System.Numerics;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

/// <summary>
/// Geometry helper for biome chunk calculations.
/// </summary>
internal static class SpaceBiomeHelpers
{
    public const float BiomeHalfSize = SpaceBiomeGridCellComponent.CellSize / 2f; // 750 (1500x1500)

    public static bool RectCircleIntersect(Box2 rect, Vector2 circPos, float circRadius)
    {
        var delta = circPos - rect.Center;
        var absDelta = new Vector2(MathF.Abs(delta.X), MathF.Abs(delta.Y));
        if (absDelta.X > rect.Width / 2 + circRadius || absDelta.Y > rect.Height / 2 + circRadius)
            return false;
        if (absDelta.X < rect.Width / 2 || absDelta.Y < rect.Height / 2)
            return true;

        var cornerDelta = absDelta - new Vector2(rect.Width / 2, rect.Height / 2);
        return cornerDelta.Length() < circRadius;
    }

    public static bool IsPointInBiome(Vector2 relativePos)
    {
        return MathF.Abs(relativePos.X) <= BiomeHalfSize &&
               MathF.Abs(relativePos.Y) <= BiomeHalfSize;
    }

    public static float GetEffectiveRadius()
    {
        return BiomeHalfSize * MathF.Sqrt(2f);
    }

    public static float GetCoverageRadius()
    {
        return GetEffectiveRadius();
    }

    public static bool IntersectsBiomeInfluence(Vector2 sourcePos, Box2 targetAabb)
    {
        var half = BiomeHalfSize;
        var biomeAabb = new Box2(sourcePos - new Vector2(half, half), sourcePos + new Vector2(half, half));
        return biomeAabb.Intersects(targetAabb);
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
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

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
        var coverageRadius = SpaceBiomeHelpers.GetCoverageRadius();
        foreach (var chunkPos in GetCoveredChunks(pos, coverageRadius))
        {
            if (!_chunks.TryGetValue(chunkPos, out var sources))
                _chunks[chunkPos] = sources = new HashSet<EntityUid>();
            sources.Add(uid);
        }
    }

    public void UnregisterBiomeSource(EntityUid uid, SpaceBiomeSourceComponent source)
    {
        var pos = _transform.GetWorldPosition(uid);
        var coverageRadius = SpaceBiomeHelpers.GetCoverageRadius();
        foreach (var chunkPos in GetCoveredChunks(pos, coverageRadius))
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

    private static List<Vector2> GetCoveredChunks(Vector2 pos, float radius)
    {
        var result = new List<Vector2>();
        var posFloor = GetChunkKey(pos);
        var chunks = (int) MathF.Ceiling(radius / ChunkSize);

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
    public string GetBiomeAt(MapId mapId, Vector2 worldPos)
    {
        if (!TryGetBiomeSourceAt(mapId, worldPos, out var biomeSource))
            return "default";

        return biomeSource.Comp.Biome;
    }

    /// <summary>
    /// Gets the highest-priority biome source at the given world position.
    /// </summary>
    public bool TryGetBiomeSourceAt(MapId mapId, Vector2 worldPos, out Entity<SpaceBiomeSourceComponent> biomeSource)
    {
        biomeSource = default;
        var chunkKey = GetChunkKey(worldPos);
        if (!_chunks.TryGetValue(chunkKey, out var sourceUids))
            return false;

        EntityUid? bestSourceUid = null;
        SpaceBiomeSourceComponent? bestSourceComp = null;
        var bestPriority = int.MinValue;

        foreach (var sourceUid in sourceUids)
        {
            if (!TryComp<SpaceBiomeSourceComponent>(sourceUid, out var source) ||
                !TryComp<TransformComponent>(sourceUid, out var sourceXform) ||
                sourceXform.MapID != mapId)
                continue;

            var sourcePos = _transform.GetWorldPosition(sourceUid);
            var relativePos = worldPos - sourcePos;
            if (!SpaceBiomeHelpers.IsPointInBiome(relativePos))
                continue;

            if (source.Priority < bestPriority)
                continue;

            bestPriority = source.Priority;
            bestSourceUid = sourceUid;
            bestSourceComp = source;
        }

        if (bestSourceUid == null || bestSourceComp == null)
            return false;

        biomeSource = (bestSourceUid.Value, bestSourceComp);
        return true;
    }

    /// <summary>
    /// Checks if scanning is blocked at a given world position.
    /// Returns true if the position is inside a biome that has BlocksScanning = true.
    /// </summary>
    public bool IsScanningBlocked(MapId mapId, Vector2 worldPos)
    {
        var biomeId = GetBiomeAt(mapId, worldPos);
        if (biomeId == "default")
            return false;

        if (_prototypeManager.TryIndex<SpaceBiomePrototype>(biomeId, out var proto))
        {
            return proto.BlocksScanning;
        }

        return false;
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
