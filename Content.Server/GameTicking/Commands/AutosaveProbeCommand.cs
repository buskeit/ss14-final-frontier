using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.GameTicking.Commands;

[AdminCommand(AdminFlags.Server)]
public sealed class AutosaveProbeCommand : LocalizedCommands
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override string Command => "autosaveprobe";
    public override string Description => "Log autosave status after an optional delay, optionally enabling persistence first.";
    public override string Help => "autosaveprobe <delaySeconds> [enablePersistence]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!float.TryParse(args[0], out var delaySeconds) || delaySeconds < 0)
        {
            shell.WriteError("Delay must be a non-negative number of seconds.");
            return;
        }

        var enablePersistence = false;
        if (args.Length == 2 && !bool.TryParse(args[1], out enablePersistence))
        {
            shell.WriteError("enablePersistence must be true or false.");
            return;
        }

        var gameTicker = _entManager.System<GameTicker>();

        if (enablePersistence)
            _cfg.SetCVar(CCVars.UsePersistence, true);

        var now = _gameTiming.CurTime;
        var triggerAt = now + TimeSpan.FromSeconds(delaySeconds);

        shell.WriteLine($"Scheduled autosave probe for {triggerAt} (delay {delaySeconds:F1}s).");
        gameTicker.LogAutosaveProbe(triggerAt, enablePersistence);
    }
}
