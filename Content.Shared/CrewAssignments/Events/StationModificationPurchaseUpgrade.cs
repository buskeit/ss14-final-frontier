using Robust.Shared.Serialization;

namespace Content.Shared.CrewAssignments.Events;

/// <summary>
///     Set order in database as approved.
/// </summary>
[Serializable, NetSerializable]
public sealed class StationModificationPurchaseUpgrade : BoundUserInterfaceMessage
{

    public StationModificationPurchaseUpgrade()
    {
    }
}
