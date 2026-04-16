using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Shiptest.SpaceBiomes;

/// <summary>
/// Synchronizes parallax with space biomes:
/// - looks up current biome at player map position (via SpaceBiomeSystem)
/// - switches map ParallaxComponent to the biome's configured parallax.
/// For asteroid belt, this uses the TrainStation parallax.
/// </summary>
public sealed class SpaceBiomeParallaxSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SpaceBiomeSystem _spaceBiomes = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    private TimeSpan _nextUpdate;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _nextUpdate = _timing.CurTime;
        // nothing to reset beyond next update
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        // nothing to reset beyond next update
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        // For each player, ensure they have the correct per-entity parallax override
        // for the biome they are currently in.
        foreach (var session in _playerMan.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                session.AttachedEntity is not { } playerUid)
            {
                continue;
            }

            if (!_xformQuery.TryGetComponent(playerUid, out var xform))
                continue;

            var mapCoords = _transform.GetMapCoordinates(playerUid);
            var mapId = mapCoords.MapId;
            if (mapId == MapId.Nullspace)
                continue;

            var biomeId = _spaceBiomes.GetBiomeAt(mapId, mapCoords.Position);
            var parallaxId = GetParallaxForBiome(biomeId);

            if (!TryComp<BiomeParallaxComponent>(playerUid, out var biomeParallax))
            {
                biomeParallax = EnsureComp<BiomeParallaxComponent>(playerUid);
            }

            // Avoid unnecessary network updates if nothing changed.
            if (biomeParallax.ParallaxId == parallaxId)
                continue;

            biomeParallax.ParallaxId = parallaxId;
            Dirty(playerUid, biomeParallax);
        }
    }

    private string? GetParallaxForBiome(string? biomeId)
    {
        if (string.IsNullOrEmpty(biomeId) || biomeId == "default")
            return null;

        if (!_prototype.TryIndex<SpaceBiomePrototype>(biomeId, out var proto))
            return null;

        var id = proto.ParallaxId;
        // Empty or null means "use whatever is already set / default".
        return string.IsNullOrEmpty(id) ? null : id;
    }
}

