using System.Collections.Generic;
using System.Numerics;
using Content.Server._Shiptest.ShipWeapon;
using Content.Server.Projectiles;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Mono.SpaceArtillery;

/// <summary>
/// Fast ship-kinetic rounds can move far enough in one physics tick to skip thin wall fixtures (discrete collision only).
/// After physics, cast the travel segment and apply the same projectile hit logic for any hard fixtures hit in order.
/// </summary>
public sealed class ShipWeaponProjectileSweptSystem : EntitySystem
{
    [Dependency] private readonly ProjectileSystem _projectile = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, Vector2> _lastWorldPos = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(PhysicsSystem));
        UpdatesAfter.Add(typeof(TravelDistanceDamageSystem));
        SubscribeLocalEvent<ShipWeaponProjectileComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnTerminating(EntityUid uid, ShipWeaponProjectileComponent _, ref EntityTerminatingEvent args)
    {
        _lastWorldPos.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipWeaponProjectileComponent, ProjectileComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out var projectile, out var xform, out var body))
        {
            if (projectile.ProjectileSpent)
            {
                _lastWorldPos.Remove(uid);
                continue;
            }

            if (xform.MapID == MapId.Nullspace)
            {
                _lastWorldPos.Remove(uid);
                continue;
            }

            var pos = _transform.GetWorldPosition(xform);
            if (!_lastWorldPos.TryGetValue(uid, out var last))
            {
                _lastWorldPos[uid] = pos;
                continue;
            }

            var delta = pos - last;
            var dist = delta.Length();
            if (dist < 0.0001f)
            {
                _lastWorldPos[uid] = pos;
                continue;
            }

            var dir = delta / dist;
            var ray = new CollisionRay(last, dir, body.CollisionMask);
            // True = ignore this hit. Re-resolve projectile each time so pen-through updates apply if we ever re-query.
            static bool RaySkip(EntityUid hit, EntityUid projUid, IEntityManager entMan)
            {
                if (hit == projUid)
                    return true;
                if (!entMan.TryGetComponent<ProjectileComponent>(projUid, out var p))
                    return true;
                if (p.IgnoredEntities.Contains(hit))
                    return true;
                if (p.IgnoreShooter && (hit == p.Shooter || hit == p.Weapon))
                    return true;
                return false;
            }

            var ent = EntityManager;
            IEnumerable<RayCastResults> hitEnumerable = _physics.IntersectRayWithPredicate(
                xform.MapID,
                ray,
                uid,
                (h, puid) => RaySkip(h, puid, ent),
                dist,
                returnOnFirstHit: false);

            var seen = new HashSet<EntityUid>();
            var vel = body.LinearVelocity;

            foreach (var hit in hitEnumerable)
            {
                if (TerminatingOrDeleted(uid) || !TryComp<ProjectileComponent>(uid, out var p))
                    break;

                if (p.ProjectileSpent)
                    break;

                if (!seen.Add(hit.HitEntity))
                    continue;

                var layer = GetOtherFixtureLayerBits(hit.HitEntity);
                _projectile.ProcessProjectileHit(uid, p, hit.HitEntity, vel, layer);
            }

            if (Exists(uid))
                _lastWorldPos[uid] = pos;
        }
    }

    private int GetOtherFixtureLayerBits(EntityUid target)
    {
        if (TryComp<FixturesComponent>(target, out var fixtures))
        {
            var bits = 0;
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard)
                    bits |= fixture.CollisionLayer;
            }
            if (bits != 0)
                return bits;
        }

        if (TryComp<PhysicsComponent>(target, out var phys))
            return phys.CollisionLayer;

        return 0;
    }
}
