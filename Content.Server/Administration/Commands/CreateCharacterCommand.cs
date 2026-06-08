using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using System.Linq;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class CreateCharacterCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    public override string Command => "createcharacter";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(LocalizationManager.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!_cfg.GetCVar(CCVars.UsePersistence))
        {
            shell.WriteError("createcharacter requires persistence to be enabled.");
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var target))
        {
            shell.WriteError(LocalizationManager.GetString("shell-target-player-does-not-exist"));
            return;
        }

        _mind.WipeMind(target);
        _gameTicker.PlayerJoinLobby(target, forceCharacterSetup: true);

        shell.WriteLine($"Forced character creation for {target.Name}.");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = _playerManager.Sessions.OrderBy(c => c.Name).Select(c => c.Name).ToArray();
        return CompletionResult.FromHintOptions(options, "Player to force into character creation");
    }
}
