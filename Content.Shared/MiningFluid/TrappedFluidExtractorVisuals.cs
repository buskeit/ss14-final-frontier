using Robust.Shared.Serialization;

namespace Content.Shared.MiningFluid.Visuals
{
    [Serializable, NetSerializable]
    public enum TrappedFluidExtractorVisuals : byte
    {
        State,
    }

    [Serializable, NetSerializable]
    public enum GasVolumePumpVisualLayers : byte
    {
        Base,
        Drill,
        PowerIndicator,
    }

    [Serializable, NetSerializable]
    public enum TrappedFluidExtractorState : byte
    {
        Off,
        On,
    }
}
