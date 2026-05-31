using Robust.Shared.GameStates;

namespace Content.Shared.Station.Components;

/// <summary>
/// Component that tracks which station an entity is currently on.
/// Mainly used for UI purposes on the client to easily get station-specific data like alert levels.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationTrackerComponent : Component
{
    /// <summary>
    /// The station this entity is currently on, if any.
    /// Null when in space or not on any grid.
    /// </summary>
    [DataField(readOnly: true), AutoNetworkedField]
    public EntityUid? Station;

    [DataField]
    public int stationUID = 0;

    [DataField]
    public bool locked = true;
}
