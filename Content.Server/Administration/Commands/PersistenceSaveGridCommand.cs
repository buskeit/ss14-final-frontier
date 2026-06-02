using Content.Server.Persistence.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Server)]
public sealed class PersistenceSaveGridCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly PersistenceSystem _persistence = default!;

    public override string Command => "persistencesavegrid";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
        {
            shell.WriteError("Usage: persistencesavegrid <gridUid> <path> [delete]");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var uidNet))
        {
            shell.WriteError("Not a valid entity ID.");
            return;
        }

        var uid = _ent.GetEntity(uidNet);

        var deleteGrid = false;
        if (args.Length == 3 && !TryParseDeleteArg(args[2], out deleteGrid))
        {
            shell.WriteError("Delete argument must be true, false, or delete.");
            return;
        }

        if (_persistence.SaveGrid(uid, new ResPath(args[1]), out var errorMessage, dumpSpecialEntities: true, deleteGrid: deleteGrid))
        {
            shell.WriteLine("Save successful. Look in the user data directory.");
        }
        else
        {
            shell.WriteError("Save unsuccessful!");
            if (!string.IsNullOrWhiteSpace(errorMessage))
                shell.WriteError(errorMessage);
        }
    }

    private static bool TryParseDeleteArg(string arg, out bool deleteGrid)
    {
        if (bool.TryParse(arg, out deleteGrid))
            return true;

        deleteGrid = arg.Equals("delete", StringComparison.OrdinalIgnoreCase);
        return deleteGrid;
    }
}
