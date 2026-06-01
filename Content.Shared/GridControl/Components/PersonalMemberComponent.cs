using Robust.Shared.GameStates;

namespace Content.Shared.GridControl.Components;

/// <summary>
/// Indicates that a grid is a member of the given station.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PersonalMemberComponent : Component
{
    /// <summary>
    /// Station that this grid is a part of.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string OwnerName { get; set; } = string.Empty;


}
