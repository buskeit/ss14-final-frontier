using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Bed.Sleep;

/// <summary>
/// Marker for entities whose persistent sleep state should pause background survival systems.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class PersistentSleepPauseComponent : Component
{
    /// <summary>
    /// When this mob first became eligible for persistent sleep pauses.
    /// </summary>
    [DataField("sleepStartedAt", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan SleepStartedAt;

    /// <summary>
    /// How long the mob must remain eligible before hunger/thirst are paused.
    /// Respiration pauses immediately while the marker is present.
    /// </summary>
    [DataField]
    public TimeSpan NutritionPauseAfter = TimeSpan.FromHours(8);
}
