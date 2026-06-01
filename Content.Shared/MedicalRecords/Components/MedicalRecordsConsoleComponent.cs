using Robust.Shared.GameStates;

namespace Content.Shared.MedicalRecords.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class MedicalRecordsConsoleComponent : Component
{
    [ViewVariables]
    public string? ActivePatient;
}
