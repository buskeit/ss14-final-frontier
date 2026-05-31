using Robust.Shared.GameStates;

namespace Content.Shared.Station.Components;

/// <summary>
/// Indicates that a grid is a member of the given station.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationMemberComponent : Component
{
    /// <summary>
    /// Station that this grid is a part of.
    /// </summary>
    [DataField(readOnly: true), AutoNetworkedField]
    public EntityUid Station = EntityUid.Invalid;

    /// Station that this grid is a part of.
    /// </summary>
    /// <summary>
    [DataField, AutoNetworkedField]
    public int? StationUID = null;



}
