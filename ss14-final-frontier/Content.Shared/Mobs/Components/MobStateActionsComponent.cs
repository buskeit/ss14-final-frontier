using Content.Shared.Mobs.Systems;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Mobs.Components;

/// <summary>
///     Used for specifying actions that should be automatically added/removed on mob state transitions
/// </summary>
/// <remarks>
///     Mostly for crit-specific actions.
/// </remarks>
/// <see cref="MobStateActionsSystem"/>
[RegisterComponent]
public sealed partial class MobStateActionsComponent : Component
{
    /// <summary>
    ///     Specifies a list of actions that should be available if a mob is in a given state.
    /// </summary>
    /// <example>
    /// actions:
    ///   Critical:
    ///   - ActionCritSuccumb
    ///   Alive:
    ///   - ActionAnimalLayEgg
    /// </example>
    ///
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan AcceptDeathCooldown = TimeSpan.Zero;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan SOSCooldown = TimeSpan.Zero;
    [DataField("actions")]
    public Dictionary<MobState, List<string>> Actions = new();

    [DataField] public List<EntityUid> GrantedActions = new();
}
