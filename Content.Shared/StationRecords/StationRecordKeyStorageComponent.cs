using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.StationRecords;

// This key is authoritative server-side data. Keeping it off the client prevents
// access prediction from treating an unresolved station record as a hard denial
// before the server confirms that the ID is valid.
[RegisterComponent]
public sealed partial class StationRecordKeyStorageComponent : Component
{
    /// <summary>
    ///     The key stored in this component.
    /// </summary>
    [ViewVariables]
    public StationRecordKey? Key;
}

[Serializable, NetSerializable]
public sealed class StationRecordKeyStorageComponentState : ComponentState
{
    public (NetEntity, uint)? Key;

    public StationRecordKeyStorageComponentState((NetEntity, uint)? key)
    {
        Key = key;
    }
}
