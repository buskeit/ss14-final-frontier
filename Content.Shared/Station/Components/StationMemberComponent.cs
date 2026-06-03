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
    [AutoNetworkedField]
    public EntityUid Station = EntityUid.Invalid;

    /// <summary>
    /// Stable station ID used to restore <see cref="Station"/> after loading a map.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int? StationUID = null;
}
