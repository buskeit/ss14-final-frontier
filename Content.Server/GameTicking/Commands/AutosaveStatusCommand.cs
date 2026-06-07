using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server.GameTicking.Commands;

[AdminCommand(AdminFlags.Server)]
public sealed class AutosaveStatusCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override string Command => "autosavestatus";
    public override string Description => "Print the current persistent autosave state.";
    public override string Help => "autosavestatus";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        var gameTicker = _entManager.System<GameTicker>();
        shell.WriteLine(gameTicker.GetAutosaveStatus());
    }
}
