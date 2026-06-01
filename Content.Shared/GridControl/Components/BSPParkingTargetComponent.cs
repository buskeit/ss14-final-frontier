using Content.Shared.GridControl.Systems;

namespace Content.Shared.GridControl.Components;

[RegisterComponent]
[Access(typeof(SharedBluespaceParkingSystem))]
public sealed partial class BSPParkingTargetComponent : Component
{
    public EntityUid Source { get; set; }

    public bool RampingUp { get; set; } = false;

    public EntityUid? StartupStream { get; set; }

}
