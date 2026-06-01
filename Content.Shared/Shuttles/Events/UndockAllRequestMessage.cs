using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.Events;

/// <summary>
/// Raised on the client when it wishes to not have any docking ports docked.
/// </summary>
[Serializable, NetSerializable]
public sealed class UndockAllRequestMessage : BoundUserInterfaceMessage
{
}
