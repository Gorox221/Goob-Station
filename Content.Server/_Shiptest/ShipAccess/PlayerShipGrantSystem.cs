using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared._Shiptest.Access;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Popups;

namespace Content.Server._Shiptest.ShipAccess;

/// <summary>
/// Crew get hull tokens on spawn. Shuttle console: use ID/PDA in hand to add this ship's token;
/// use the same card again when it already has that token to remove it.
/// </summary>
public sealed class PlayerShipHullGrantSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<ShuttleConsoleComponent, InteractUsingEvent>(OnShuttleConsoleInteractUsing);
    }

    private void OnShuttleConsoleInteractUsing(EntityUid uid, ShuttleConsoleComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetIdCardFromHeldItem(args.Used, out var idCard))
            return;

        var owning = _station.GetOwningStation(uid);
        if (owning == null || !TryComp<PlayerShipHullAccessComponent>(owning.Value, out var hull)
            || string.IsNullOrEmpty(hull.Token))
            return;

        var token = hull.Token;
        var grant = EnsureComp<PlayerShipHullGrantComponent>(idCard);

        if (grant.Tokens.Contains(token))
        {
            grant.Tokens.Remove(token);
            Dirty(idCard, grant);
            if (grant.Tokens.Count == 0)
                RemComp<PlayerShipHullGrantComponent>(idCard);

            _popup.PopupEntity(Loc.GetString("player-ship-hull-token-console-cleared"), uid, args.User);
        }
        else
        {
            grant.Tokens.Add(token);
            Dirty(idCard, grant);
            _popup.PopupEntity(Loc.GetString("player-ship-hull-token-console-added"), uid, args.User);
        }

        args.Handled = true;
    }

    private bool TryGetIdCardFromHeldItem(EntityUid item, out EntityUid idCard)
    {
        if (HasComp<IdCardComponent>(item))
        {
            idCard = item;
            return true;
        }

        if (TryComp<PdaComponent>(item, out var pda) && pda.ContainedId is { } contained && HasComp<IdCardComponent>(contained))
        {
            idCard = contained;
            return true;
        }

        idCard = default;
        return false;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryComp<PlayerShipHullAccessComponent>(args.Station, out var hull) || string.IsNullOrEmpty(hull.Token))
            return;

        TryGrantHullOnId(args.Mob, hull.Token);
    }

    public void TryGrantHullOnId(EntityUid mob, string token)
    {
        if (!_inventory.TryGetSlotEntity(mob, "id", out var idUid))
            return;

        var card = idUid.Value;
        if (TryComp<PdaComponent>(idUid, out var pda) && pda.ContainedId != null)
            card = pda.ContainedId.Value;

        var grant = EnsureComp<PlayerShipHullGrantComponent>(card);
        if (!grant.Tokens.Contains(token))
            grant.Tokens.Add(token);

        Dirty(card, grant);
    }
}
