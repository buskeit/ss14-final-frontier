using Robust.Shared.Serialization;

namespace Content.Shared.Power.Generation.GasGenerator;

/// <summary>
/// Contains the UI state for the gas generator BUI.
/// </summary>
[Serializable, NetSerializable]
public sealed class GasGeneratorBoundUserInterfaceState : BoundUserInterfaceState
{
    public float CurrentPowerOutput;
    public float MaxPowerOutput;
    public float CurrentEfficiency;
    public float CompositionEfficiency;
    public float TemperatureEfficiency;
    public float CurrentConsumptionRate;
    public float FuelTemperature;
    public float FuelPressure;
    public bool Powered;

    public GasGeneratorBoundUserInterfaceState(
        float currentPowerOutput,
        float maxPowerOutput,
        float currentEfficiency,
        float compositionEfficiency,
        float temperatureEfficiency,
        float currentConsumptionRate,
        float fuelTemperature,
        float fuelPressure,
        bool powered)
    {
        CurrentPowerOutput = currentPowerOutput;
        MaxPowerOutput = maxPowerOutput;
        CurrentEfficiency = currentEfficiency;
        CompositionEfficiency = compositionEfficiency;
        TemperatureEfficiency = temperatureEfficiency;
        CurrentConsumptionRate = currentConsumptionRate;
        FuelTemperature = fuelTemperature;
        FuelPressure = fuelPressure;
        Powered = powered;
    }
}

/// <summary>
/// UI key for the gas generator.
/// </summary>
[Serializable, NetSerializable]
public enum GasGeneratorUiKey
{
    Key
}
