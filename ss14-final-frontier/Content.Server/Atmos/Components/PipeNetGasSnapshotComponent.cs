using Content.Shared.Atmos;
namespace Content.Server.Atmos.Components;

/// <summary>
/// Stores a snapshot of a PipeNet's gas mixture on a single pipe node owner for map persistence.
/// If the chosen owner is deleted or its node joins a different net between save/load, the gas
/// may be lost or restored into the wrong net. This edge behavior is accepted.
/// </summary>
[RegisterComponent]
public sealed partial class PipeNetGasSnapshotComponent : Component
{
    /// <summary>
    /// Saved gas mixtures keyed by the node name on this entity.
    /// </summary>
    [DataField("nodeAir")]
    public Dictionary<string, GasMixture> NodeAir { get; set; } = new();
}
