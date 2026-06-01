using Content.Server.MCTN.Systems;
using Robust.Shared.Utility;

namespace Content.Server.MCTN.Components;

[RegisterComponent, Access(typeof(MCTNSystem))]
public sealed partial class MCTNComponent : Component
{
    [DataField, ViewVariables]
    public EntityUid? Connection = null;

    [DataField, ViewVariables]
    public float MaxRange = 4f;

    [DataField]
    public Dictionary<string, bool> EnabledPlugs = new();

    [DataField, ViewVariables, AutoNetworkedField]
    public SpriteSpecifier? LinkSprite;

}

[ByRefEvent]
public readonly struct MCTNConnected(
    EntityUid entity,
    MCTNComponent component,
    Entity<MCTNConnectionComponent> connection)
{
    public EntityUid Entity { get; } = entity;
    public readonly MCTNComponent Component = component;
    public readonly Entity<MCTNConnectionComponent> NewConnection = connection;
}

[ByRefEvent]
public readonly struct MCTNDisconnected(
    EntityUid entity,
    MCTNComponent component)
{
    public EntityUid Entity { get; } = entity;
    public readonly MCTNComponent Component = component;
}

[ByRefEvent]
public readonly struct MCTNConnectionChange(
    EntityUid entity,
    MCTNComponent component,
    Entity<MCTNConnectionComponent>? prevConn,
    Entity<MCTNConnectionComponent>? newConn)
{
    public EntityUid Entity { get; } = entity;
    public readonly MCTNComponent Component = component;
    public readonly Entity<MCTNConnectionComponent>? PreviousConnection = prevConn;
    public readonly Entity<MCTNConnectionComponent>? NewConnection = newConn;
}
