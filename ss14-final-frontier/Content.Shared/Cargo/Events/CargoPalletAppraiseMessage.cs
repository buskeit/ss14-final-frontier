using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.Events;

/// <summary>
/// Raised on a client request to refresh the pallet console
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoPalletAppraiseMessage : BoundUserInterfaceMessage
{

}


[Serializable, NetSerializable]
public sealed class CargoPalletStationSelectMessage : BoundUserInterfaceMessage
{
    public int Target;
    public CargoPalletStationSelectMessage(int target)
    {
        Target = target;
    }

}
