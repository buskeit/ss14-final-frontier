
using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class MapBoundsComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float Radius { get; set; } = 5000f;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float BaseImpulseVelocity { get; set; } = 5f;
}
