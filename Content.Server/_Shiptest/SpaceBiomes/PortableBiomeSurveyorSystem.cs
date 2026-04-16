using System;
using Content.Server.Popups;
using Content.Server.Research.Disk;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;

namespace Content.Server._Shiptest.SpaceBiomes;

public sealed class PortableBiomeSurveyorSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpaceBiomeSystem _spaceBiomes = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PortableBiomeSurveyorComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PortableBiomeSurveyorComponent, PortableBiomeSurveyDoAfterEvent>(OnSurveyDoAfter);
    }

    private void OnUseInHand(Entity<PortableBiomeSurveyorComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var userPos = _transform.GetMapCoordinates(args.User);
        if (!_spaceBiomes.TryGetBiomeSourceAt(userPos.MapId, userPos.Position, out var sourceEnt))
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-no-biome"), ent, args.User);
            return;
        }

        var source = sourceEnt.Comp;
        if (source.RemainingPortableScans <= 0)
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-depleted"), ent, args.User);
            return;
        }

        var doAfterEvent = new PortableBiomeSurveyDoAfterEvent(GetNetEntity(sourceEnt.Owner));
        var doAfter = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.ScanDuration), doAfterEvent, ent,
            target: args.User,
            used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-start-failed"), ent, args.User);
            return;
        }

        _audio.PlayPredicted(ent.Comp.ScanStartSound, ent, args.User);
        _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-start"), ent, args.User);
        args.Handled = true;
    }

    private void OnSurveyDoAfter(Entity<PortableBiomeSurveyorComponent> ent, ref PortableBiomeSurveyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.Args.User;
        var userPos = _transform.GetMapCoordinates(user);
        if (!_spaceBiomes.TryGetBiomeSourceAt(userPos.MapId, userPos.Position, out var currentSource))
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-no-biome"), ent, user);
            return;
        }

        var expectedSourceUid = args.SourceUid == null ? EntityUid.Invalid : GetEntity(args.SourceUid.Value);
        if (expectedSourceUid == EntityUid.Invalid || currentSource.Owner != expectedSourceUid)
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-biome-changed"), ent, user);
            return;
        }

        var source = currentSource.Comp;
        if (source.RemainingPortableScans <= 0)
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-depleted"), ent, user);
            return;
        }

        if (!_prototype.TryIndex<SpaceBiomePrototype>(source.Biome, out var biomeProto))
        {
            _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-invalid-biome"), ent, user);
            return;
        }

        source.RemainingPortableScans--;
        Dirty(currentSource.Owner, source);

        var disk = Spawn(ent.Comp.DiskPrototype, Transform(user).Coordinates);
        if (TryComp<ResearchDiskComponent>(disk, out var diskComp))
        {
            diskComp.Points = biomeProto.ResearchPoints;
            Dirty(disk, diskComp);
            _metaData.SetEntityName(disk, Loc.GetString("portable-biome-surveyor-disk-name", ("points", diskComp.Points)));
        }

        _hands.PickupOrDrop(user, disk);
        _audio.PlayPredicted(ent.Comp.ScanCompleteSound, ent, user);
        _popup.PopupEntity(Loc.GetString("portable-biome-surveyor-success",
            ("biome", biomeProto.Name),
            ("points", biomeProto.ResearchPoints),
            ("remaining", source.RemainingPortableScans)), ent, user);

        args.Handled = true;
    }
}
