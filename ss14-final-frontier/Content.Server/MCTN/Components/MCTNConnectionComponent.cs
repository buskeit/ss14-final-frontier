using Content.Server.MCTN.Systems;

namespace Content.Server.MCTN.Components;

[RegisterComponent, Access(typeof(MCTNSystem))]
public sealed partial class MCTNConnectionComponent : Component
{
    [DataField]
    public EntityUid AnchorA { get; set; }
    [DataField]
    public EntityUid AnchorB { get; set; }

    // Runtime only.
    public TimeSpan NextUpdate;
}
