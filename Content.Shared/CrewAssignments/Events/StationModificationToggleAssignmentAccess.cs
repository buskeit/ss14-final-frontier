using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.Events;

/// <summary>
///     Set order in database as approved.
/// </summary>
[Serializable, NetSerializable]
public sealed class StationModificationToggleAssignmentAccess : BoundUserInterfaceMessage
{
    public int AccessID;
    public bool ToggleState;
    public string Access;

    public StationModificationToggleAssignmentAccess(int id, bool state, string access)
    {
        AccessID = id;
        ToggleState = state;
        Access = access;
    }
}

[Serializable, NetSerializable]
public sealed class StationModificationToggleChannelAccess : BoundUserInterfaceMessage
{
    public ProtoId<RadioChannelPrototype> ChannelID;
    public bool ToggleState;
    public string Access;

    public StationModificationToggleChannelAccess(ProtoId<RadioChannelPrototype> id, bool state, string access)
    {
        ChannelID = id;
        ToggleState = state;
        Access = access;
    }
}
