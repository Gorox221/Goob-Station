using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Shiptest.SpaceBiomes;

[AdminCommand(AdminFlags.Mapping)]
public sealed class RegenerateSpaceBiomeChunksCommand : IConsoleCommand
{
    public string Command => "sb_genchunks";
    public string Description => "Regenerates space biome chunk mappings from all active biome sources.";
    public string Help => "No arguments required.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        EntitySystem.Get<SpaceBiomeSystem>().RegenerateChunks();
        shell.WriteLine("Space biome chunks regenerated.");
    }
}
