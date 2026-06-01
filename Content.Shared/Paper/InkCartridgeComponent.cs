using Robust.Shared.GameStates;

namespace Content.Shared.Paper;

[RegisterComponent]
public sealed partial class InkCartridgeComponent : Component
{
    [DataField]
    public string SolutionName = "reagents";
}
