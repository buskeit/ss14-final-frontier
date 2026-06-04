using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking;

public static class PersistentCharacterSavePath
{
    private static readonly ResPath Root = new("/persistent-characters");
    private static readonly ResPath NpcRoot = Root / "npc";

    public static ResPath ForPlayer(NetUserId userId)
    {
        return Root / $"{userId.UserId:N}.yml";
    }

    public static ResPath ForNpc(EntityUid uid)
    {
        return NpcRoot / $"{uid}.yml";
    }
}
