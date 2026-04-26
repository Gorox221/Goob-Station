using Content.Shared._Shiptest.ShipWeapon;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._Shiptest.ShipWeapon;

/// <summary>
/// Applies <see cref="TravelDistanceDamageComponent"/> scaling when a projectile hits a target.
/// </summary>
public sealed class TravelDistanceDamageSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(PhysicsSystem));
        SubscribeLocalEvent<TravelDistanceDamageComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TravelDistanceDamageComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnMapInit(EntityUid uid, TravelDistanceDamageComponent comp, MapInitEvent args)
    {
        if (TerminatingOrDeleted(uid) || !TryComp<TransformComponent>(uid, out var xform))
            return;

        if (xform.MapID == MapId.Nullspace)
            return;

        comp.DistanceTraveled = 0f;
        comp.LastWorldPos = _xform.GetWorldPosition(xform);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var q = EntityQueryEnumerator<TravelDistanceDamageComponent, TransformComponent, ProjectileComponent>();
        while (q.MoveNext(out _, out var comp, out var xform, out var proj))
        {
            if (proj.ProjectileSpent)
                continue;

            if (xform.MapID == MapId.Nullspace)
            {
                comp.LastWorldPos = null;
                continue;
            }

            var pos = _xform.GetWorldPosition(xform);
            if (comp.LastWorldPos is { } last)
            {
                var delta = pos - last;
                if (delta.LengthSquared() > 0f)
                    comp.DistanceTraveled += delta.Length();
            }

            comp.LastWorldPos = pos;
        }
    }

    private void OnProjectileHit(EntityUid uid, TravelDistanceDamageComponent comp, ref ProjectileHitEvent args)
    {
        if (comp.MaxRampingDistance <= 0f)
            return;

        var t = Math.Clamp(comp.DistanceTraveled / comp.MaxRampingDistance, 0f, 1f);
        var min = Math.Clamp(comp.MinDamageFactor, 0f, 1f);
        var factor = min + (1f - min) * t;
        args.Damage *= factor;
    }
}
