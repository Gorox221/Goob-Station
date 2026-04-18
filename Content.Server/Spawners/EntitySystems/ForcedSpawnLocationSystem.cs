using Content.Server.Station.Systems;

namespace Content.Server.Spawners.EntitySystems;

/// <summary>
/// Honors <see cref="PlayerSpawningEvent.ForceSpawn"/> before container or generic spawn points run.
/// </summary>
public sealed class ForcedSpawnLocationSystem : EntitySystem
{
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning, before: [typeof(ContainerSpawnPointSystem)]);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.ForceSpawn is not { } coords)
            return;

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            coords,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }
}
