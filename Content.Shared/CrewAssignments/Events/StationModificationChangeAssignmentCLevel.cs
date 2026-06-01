using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.Events;

/// <summary>
///     Set order in database as approved.
/// </summary>
[Serializable, NetSerializable]
public sealed class StationModificationChangeAssignmentCLevel : BoundUserInterfaceMessage
{
    public int AccessID;
    public int Level;

    public StationModificationChangeAssignmentCLevel(int id, int clevel)
    {
        AccessID = id;
        Level = clevel;
    }
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeImportTax : BoundUserInterfaceMessage
{
    public int Level;

    public StationModificationChangeImportTax(int tlevel)
    {
        Level = tlevel;
    }
}


[Serializable, NetSerializable]
public sealed class StationModificationChangeExportTax : BoundUserInterfaceMessage
{
    public int Level;

    public StationModificationChangeExportTax(int tlevel)
    {
        Level = tlevel;
    }
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeSalesTax : BoundUserInterfaceMessage
{
    public int Level;

    public StationModificationChangeSalesTax(int tlevel)
    {
        Level = tlevel;
    }
}

