using Content.Shared.Parallax;
using Robust.Shared.GameStates;

namespace Content.Shared._Shiptest.SpaceBiomes;

/// <summary>
/// Per-entity parallax override used for space biomes.
/// The server sets this on player entities based on the biome they are currently in,
/// and the client-side parallax overlay uses it to pick the active parallax.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BiomeParallaxComponent : Component
{
    /// <summary>
    /// Optional parallax prototype ID to use for this viewer.
    /// When null or empty, the map's default parallax is used.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? ParallaxId;
}
