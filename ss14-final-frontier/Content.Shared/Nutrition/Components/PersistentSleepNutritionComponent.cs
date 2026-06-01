using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Nutrition.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class PersistentSleepNutritionComponent : Component
{
    /// <summary>
    /// When this mob first became eligible for persistent sleep nutrition pause.
    /// </summary>
    [DataField("sleepStartedAt", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan SleepStartedAt;

    /// <summary>
    /// How long the mob must remain eligible before hunger/thirst pause.
    /// </summary>
    [DataField]
    public TimeSpan PauseAfter = TimeSpan.FromHours(8);
}
