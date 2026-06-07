using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server.GameTicking.Commands;

[AdminCommand(AdminFlags.Server)]
public sealed class PreparePersistenceMapCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override string Command => "preparepersistencemap";
    public override string Description => "Select and load a round map into DefaultMap for persistence save testing.";
    public override string Help => "preparepersistencemap <mapPrototype>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            return;
        }

        var ticker = _entManager.System<GameTicker>();
        if (ticker.PreparePersistenceMap(args[0], out var result))
        {
            shell.WriteLine(result);
            return;
        }

        shell.WriteError(result);
    }
}
