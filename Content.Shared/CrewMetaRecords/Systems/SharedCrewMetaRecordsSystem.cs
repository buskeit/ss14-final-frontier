namespace Content.Shared.CrewMetaRecords;

public abstract partial class SharedCrewMetaRecordsSystem : EntitySystem
{
    private Queue<Action<CrewMetaRecordsComponent>> _pendingActions = new();
    public CrewMetaRecordsComponent? MetaRecords
    {
        get
        {
            var result = GetMetaRecordsComponent();
            return result;
        }
    }
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CrewMetaRecordsComponent>();
        if (query.MoveNext(out var uid, out var metaRecords))
        {
            var dirtied = false;
            while (_pendingActions.TryDequeue(out var action))
            {
                action.Invoke(metaRecords);
                dirtied = true;
            }
            if (dirtied)
                Dirty(uid, metaRecords);
        }
    }

    public CrewMetaRecordsComponent? GetMetaRecordsComponent()
    {
        var entityQuery = EntityQueryEnumerator<CrewMetaRecordsComponent>();
        if (!entityQuery.MoveNext(out _, out var metaRecords))
            return null;
        return metaRecords;
    }

    public void EnsureMetaRecordsAction(Action<CrewMetaRecordsComponent> action)
    {
        var query = EntityQueryEnumerator<CrewMetaRecordsComponent>();
        if (!query.MoveNext(out var uid, out var metaRecords))
        {
            _pendingActions.Enqueue(action);
        }
        else
        {
            action.Invoke(metaRecords);
            Dirty(uid, metaRecords);
        }
    }
}

