using Robust.Shared.GameStates;

namespace Content.Shared.Paper;

[RegisterComponent]
public sealed partial class PenInkComponent : Component
{
    [DataField]
    public string SolutionName = "ink";

    [DataField]
    public bool Refillable;
}