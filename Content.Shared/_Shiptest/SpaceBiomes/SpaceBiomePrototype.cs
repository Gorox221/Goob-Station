using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Shiptest.SpaceBiomes;

/// <summary>
/// Defines a single entity type that can spawn inside a biome.
/// </summary>
[DataDefinition]
public sealed partial class BiomeSpawnEntryPrototype
{
    /// <summary>
    /// Whether this spawn entry is enabled.
    /// </summary>
    [DataField("enabled")]
    public bool Enabled = true;

    /// <summary>
    /// The entity prototype ID to spawn.
    /// </summary>
    [DataField("entity", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string EntityId = "";

    /// <summary>
    /// How often (in seconds) to attempt spawning entities of this type.
    /// Only triggers if a player is within range of the biome.
    /// Set to 0 or negative to disable periodic spawning.
    /// </summary>
    [DataField("interval")]
    public float SpawnInterval = 120f;
}

/// <summary>
/// Configures periodic lightning strikes for a biome.
/// </summary>
[DataDefinition]
public sealed partial class BiomeLightningStormPrototype
{
    /// <summary>
    /// Enables or disables storm strikes for this biome.
    /// </summary>
    [DataField]
    public bool Enabled = false;

    /// <summary>
    /// Minimum time between strike attempts in seconds.
    /// </summary>
    [DataField]
    public float IntervalMin = 3f;

    /// <summary>
    /// Maximum time between strike attempts in seconds.
    /// </summary>
    [DataField]
    public float IntervalMax = 6f;

    /// <summary>
    /// Extra radius around biome center where nearby players activate storm processing.
    /// </summary>
    [DataField]
    public float PlayerActivationRange = 250f;

    /// <summary>
    /// Local strike range on the selected grid.
    /// </summary>
    [DataField]
    public float ArcRangeOnGrid = 14f;

    /// <summary>
    /// Minimum amount of arcs after the primary strike.
    /// </summary>
    [DataField]
    public int MinBolts = 1;

    /// <summary>
    /// Maximum amount of arcs after the primary strike.
    /// </summary>
    [DataField]
    public int MaxBolts = 3;

    /// <summary>
    /// Recursive arc depth used by LightningSystem.
    /// </summary>
    [DataField]
    public int ArcDepth = 1;

    /// <summary>
    /// Lightning prototype to use.
    /// </summary>
    [DataField]
    public string LightningPrototype = "Lightning";
}

/// <summary>
/// Configures periodic EMP pulses for a biome.
/// </summary>
[DataDefinition]
public sealed partial class BiomeEmpStormPrototype
{
    /// <summary>
    /// Enables or disables EMP pulses for this biome.
    /// </summary>
    [DataField]
    public bool Enabled = false;

    /// <summary>
    /// Minimum time between EMP pulse attempts in seconds.
    /// </summary>
    [DataField]
    public float IntervalMin = 6f;

    /// <summary>
    /// Maximum time between EMP pulse attempts in seconds.
    /// </summary>
    [DataField]
    public float IntervalMax = 12f;

    /// <summary>
    /// Extra radius around biome center where nearby players activate EMP processing.
    /// </summary>
    [DataField]
    public float PlayerActivationRange = 250f;

    /// <summary>
    /// Minimum number of EMP pulses per trigger.
    /// </summary>
    [DataField]
    public int MinPulses = 1;

    /// <summary>
    /// Maximum number of EMP pulses per trigger.
    /// </summary>
    [DataField]
    public int MaxPulses = 2;

    /// <summary>
    /// EMP pulse radius.
    /// </summary>
    [DataField]
    public float PulseRadius = 6f;

    /// <summary>
    /// EMP energy consumption value used by EmpSystem.
    /// </summary>
    [DataField]
    public float EnergyConsumption = 20000f;

    /// <summary>
    /// EMP disabled duration in seconds.
    /// </summary>
    [DataField]
    public float DisableDuration = 10f;
}

/// <summary>
/// Configures periodic meteor waves for a biome.
/// </summary>
[DataDefinition]
public sealed partial class BiomeMeteorStormPrototype
{
    /// <summary>
    /// Enables or disables meteor waves for this biome.
    /// </summary>
    [DataField]
    public bool Enabled = false;

    /// <summary>
    /// Minimum time between meteor waves in seconds.
    /// </summary>
    [DataField]
    public float IntervalMin = 20f;

    /// <summary>
    /// Maximum time between meteor waves in seconds.
    /// </summary>
    [DataField]
    public float IntervalMax = 40f;

    /// <summary>
    /// Extra radius around biome center where nearby players activate meteor processing.
    /// </summary>
    [DataField]
    public float PlayerActivationRange = 500f;

    /// <summary>
    /// Minimum number of meteors per wave.
    /// </summary>
    [DataField]
    public int MinMeteorsPerWave = 3;

    /// <summary>
    /// Maximum number of meteors per wave.
    /// </summary>
    [DataField]
    public int MaxMeteorsPerWave = 8;

    /// <summary>
    /// Meteor prototype IDs to spawn. If empty, nothing happens.
    /// </summary>
    [DataField]
    public List<string> MeteorPrototypes = new();

    /// <summary>
    /// Base meteor velocity (applied as impulse) in m/s.
    /// </summary>
    [DataField]
    public float MeteorVelocity = 10f;

    /// <summary>
    /// Additional distance beyond grid bounds for meteor spawn ring.
    /// </summary>
    [DataField]
    public float SpawnRingPadding = 50f;

    /// <summary>
    /// How far from players (in meters) meteors must spawn to avoid popping into view.
    /// </summary>
    [DataField]
    public float PlayerVisibilityRadius = 40f;
}

/// <summary>
/// Defines a space biome with its display name and map color.
/// </summary>
[Prototype("spaceFactionBiome")]
public sealed class SpaceBiomePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Display name of the biome.
    /// </summary>
    [DataField(required: true)]
    public string Name = "";

    /// <summary>
    /// Color used to display this biome on shuttle console maps and mass scanners.
    /// Default space (empty cosmos) should use Color.Transparent or a very dim color.
    /// </summary>
    [DataField]
    public Color MapColor = Color.White;

    /// <summary>
    /// Optional parallax prototype ID to use when entities are inside this biome.
    /// If null or empty, fallback parallax is used.
    /// </summary>
    [DataField("parallax")]
    public string? ParallaxId;

    /// <summary>
    /// Whether this biome blocks shuttle console scanning.
    /// When true, consoles inside this biome cannot scan or display grids and map objects.
    /// </summary>
    [DataField]
    public bool BlocksScanning = false;

    /// <summary>
    /// Entities that spawn within this biome.
    /// Multiple entries can be defined for variety.
    /// </summary>
    [DataField("spawns")]
    public List<BiomeSpawnEntryPrototype> Spawns = new();

    /// <summary>
    /// Optional lightning storm behavior for this biome.
    /// </summary>
    [DataField]
    public BiomeLightningStormPrototype? LightningStorm;

    /// <summary>
    /// Optional EMP storm behavior for this biome.
    /// </summary>
    [DataField]
    public BiomeEmpStormPrototype? EmpStorm;

    /// <summary>
    /// Optional meteor storm behavior for this biome.
    /// </summary>
    [DataField]
    public BiomeMeteorStormPrototype? MeteorStorm;
}
