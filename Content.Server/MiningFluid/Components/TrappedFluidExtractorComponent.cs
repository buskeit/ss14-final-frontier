
using Content.Shared.Atmos;

namespace Content.Server.MiningFluid.Components;

[RegisterComponent, Access(typeof(TrappedFluidExtractionSystem))]
public sealed partial class TrappedFluidExtractorComponent : Component
{
    // whether the power switch is in "on"
    [DataField]
    public bool IsOn;

    // Whether the power switch is on AND the machine has enough power
    [DataField]
    public bool IsPowered;

    /// <summary>
    /// The current amount of power being used.
    /// </summary>
    [DataField]
    public int PowerUseActive = 600;

    [DataField("outlet")]
    public string OutletName { get; set; } = "pipe";

    /// <summary>
    ///     Target volume to transfer.
    /// </summary>
    [DataField]
    public float TransferRate
    {
        get => _transferRate;
        set => _transferRate = Math.Clamp(value, 0f, MaxTransferRate);
    }

    private float _transferRate = Atmospherics.MaxTransferRate;

    [DataField]
    public float MaxTransferRate = Atmospherics.MaxTransferRate;

    /// <summary>
    ///     As pressure difference approaches this number, the effective volume rate may be smaller than <see
    ///     cref="TransferRate"/>
    /// </summary>
    [DataField]
    public float MaxPressure = Atmospherics.MaxOutputPressure;
}
