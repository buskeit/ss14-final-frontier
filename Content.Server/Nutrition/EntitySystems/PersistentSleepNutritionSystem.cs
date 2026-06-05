using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Nutrition.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Nutrition.EntitySystems;

public sealed class PersistentSleepNutritionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<SleepingComponent> _sleepingQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<PersistentSleepLocationComponent> _sleepLocationQuery;

    public override void Initialize()
    {
        base.Initialize();

        _actorQuery = GetEntityQuery<ActorComponent>();
        _sleepingQuery = GetEntityQuery<SleepingComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _sleepLocationQuery = GetEntityQuery<PersistentSleepLocationComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HungerComponent, ThirstComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            var eligible = IsEligibleForPersistentSleepPause(uid);

            if (!eligible)
            {
                RemCompDeferred<PersistentSleepPauseComponent>(uid);
                RemCompDeferred<PersistentSleepNutritionComponent>(uid);
                continue;
            }

            var pause = EnsureComp<PersistentSleepPauseComponent>(uid);
            EnsureComp<PersistentSleepNutritionComponent>(uid);

            if (pause.SleepStartedAt == default)
                pause.SleepStartedAt = _timing.CurTime;
        }
    }

    private bool IsEligibleForPersistentSleepPause(EntityUid uid)
    {
        // Must be SSD/offline. Online sleeping players still consume normally.
        if (_actorQuery.HasComp(uid))
            return false;

        // Must be actually sleeping, i.e. real sleep state / bed icon.
        if (!_sleepingQuery.HasComp(uid))
            return false;

        // Must be buckled to something.
        if (!_buckleQuery.TryComp(uid, out var buckle))
            return false;

        // Must be buckled to a valid persistent sleep location.
        if (buckle.BuckledTo is not { Valid: true } bed)
            return false;

        return _sleepLocationQuery.HasComp(bed);
    }
}
