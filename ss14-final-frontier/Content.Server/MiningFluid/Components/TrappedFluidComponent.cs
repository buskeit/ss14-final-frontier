
using Content.Shared.Atmos;
using Content.Shared.MiningFluid.Components;

namespace Content.Server.MiningFluid.Components;

[RegisterComponent, Access(typeof(TrappedFluidSystem))]
public sealed partial class TrappedFluidComponent : Component
{
    [DataField("air")]
    public GasMixture Air { get; set; } = new();

    [DataField("staticMixture")]
    public GasMixture StaticMixture { get; set; } = new();

    [DataField("variableMixture")]
    public Dictionary<Gas, VariableFluidDefinition> VariableMixture { get; set; } = new();

    [DataField]
    public float SeedLowerBound { get; set; } = 1f;
    [DataField]
    public float SeedUpperBound { get; set; } = 1f;

    [DataField]
    public bool Seeded { get; set; } = false;
}
