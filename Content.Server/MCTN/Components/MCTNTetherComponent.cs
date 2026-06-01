using Content.Server.MCTN.Systems;

namespace Content.Server.MCTN.Components;

[RegisterComponent, Access(typeof(MCTNSystem))]
public sealed partial class MCTNTetherComponent : Component
{
    [DataField]
    public EntityUid Connection;

    [DataField]
    public string NodeIdentifier;
}
