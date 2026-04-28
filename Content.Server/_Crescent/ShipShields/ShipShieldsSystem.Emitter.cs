using Content.Shared._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Audio.Systems;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects; // Rat
using System.Linq; // Rat
using System.Diagnostics.CodeAnalysis; // Rat

namespace Content.Server._Crescent.ShipShields;
public partial class ShipShieldsSystem
{
    private const float MAX_EMP_DAMAGE = 10000f;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
	[Dependency] private readonly EntityLookupSystem _lookup = default!; // Rat
    public void InitializeEmitters()
    {
        SubscribeLocalEvent<ShipShieldEmitterComponent, ShieldDeflectedEvent>(OnShieldDeflected);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentRemove>(OnRemoved);
		SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentStartup>(OnEmitterStartup); // Rat
    }

    // Rat-start
    private void OnEmitterStartup(EntityUid uid, ShipShieldEmitterComponent component, ComponentStartup args)
    {
        _pvsSys.AddGlobalOverride(uid);
    }
    // Rat-end

    private void OnRemoved(Entity<ShipShieldEmitterComponent> ent, ref ComponentRemove args)
    {
        _pvsSys.RemoveGlobalOverride(ent);
        TryUnshieldFromEmitter(ent, ent.Comp);
    }

    private void OnShieldDeflected(EntityUid uid, ShipShieldEmitterComponent component, ShieldDeflectedEvent args)
    {
        if (TryComp<EmpOnTriggerComponent>(args.Deflected, out var emp))
        {
            component.Damage += Math.Clamp(emp.EnergyConsumption, 0f, MAX_EMP_DAMAGE);
            _trigger.Trigger(args.Deflected);
        }

        if (TryComp<ExplosiveComponent>(args.Deflected, out var exp))
        {
            // Explosions are relatively soft on shield load compared to kinetics (see projectile branch below).
            component.Damage += (exp.TotalIntensity / 15f) * 0.5f; //after mlg intensity explosion changes, 1 intensity = 1 dmg, instead of 1 intensity = 15 dmg;
        }

        if (TryComp<ProjectileComponent>(args.Deflected, out var proj))
        {
            component.Damage += GetShieldDeflectionDamageFromProjectile(proj);
        }
        else if (TryComp<PhysicsComponent>(args.Deflected, out var phys))
        {
            component.Damage += phys.FixturesMass;
        }

        Dirty(uid, component);
		QueueDel(args.Deflected);
    }

    /// <summary>
    /// Per-type weighting for shield overload: piercing stresses the field more, blunt/explosive less (explosive uses ExplosiveComponent branch).
    /// Matches universal projectile damage modifier for kinetic contributions.
    /// </summary>
    private float GetShieldDeflectionDamageFromProjectile(ProjectileComponent proj)
    {
        var sum = 0f;
        foreach (var (type, val) in proj.Damage.DamageDict)
        {
            if (val <= 0)
                continue;
            var v = (float) val;
            sum += type switch
            {
                "Piercing" => v * 1.5f,
                "Blunt" => v * 0.5f,
                _ => v,
            };
        }

        return sum * _damageableSystem.UniversalProjectileDamageModifier;
    }

    private void OnExamined(EntityUid uid, ShipShieldEmitterComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (component.Damage == 0f)
        {
            args.PushMarkup(Loc.GetString("shield-emitter-examine-undamaged"));
            return;
        }

        var ratio = component.Damage / component.DamageLimit;

        args.PushMarkup(Loc.GetString("shield-emitter-examine-damaged", ("percent", ratio)));
    }

    // Rat-start
    public bool TryGetShieldEmitter(EntityUid grid, [NotNullWhen(true)] out EntityUid? emitter, [NotNullWhen(true)] out ShipShieldEmitterComponent? emitterComp)
    {
        emitter = null;
        emitterComp = null;

        if (TryComp<ShipShieldedComponent>(grid, out var shielded)
            && shielded.Source != null
            && TryComp(shielded.Source, out emitterComp))
        {
            emitter = shielded.Source.Value;
            return true;
        }

        var ents = new HashSet<Entity<ShipShieldEmitterComponent>>();
        _lookup.GetGridEntities(grid, ents);

        if (ents.Count < 1)
            return false;

        var emitterEnt = ents.First();
        emitter = emitterEnt;
        emitterComp = emitterEnt.Comp;
        return true;
    }
    // Rat-end

    // .2 - 2025. commented out because shields draw a fixed amount of power now
    // private void AdjustEmitterLoad(EntityUid uid, ShipShieldEmitterComponent? emitter = null, ApcPowerReceiverComponent? receiver = null)
    // {
    //     if (!Resolve(uid, ref emitter, ref receiver))
    //         return;

    //     /// Raise damage to the power of the growth exponent
    //     var additionalLoad = (float) Math.Clamp(Math.Pow(emitter.Damage, emitter.DamageExp), 0f, emitter.MaxDraw);

    //     receiver.Load = emitter.BaseDraw + additionalLoad;
    // }
}
