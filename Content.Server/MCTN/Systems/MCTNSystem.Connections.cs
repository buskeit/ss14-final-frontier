using Content.Server.MCTN.Components;
using Content.Shared.Coordinates;
using Robust.Shared.Timing;

namespace Content.Server.MCTN.Systems;

public sealed partial class MCTNSystem : EntitySystem
{
    private static readonly TimeSpan UpdateDelay = TimeSpan.FromSeconds(1);

    private void InitializeConnections()
    {
        SubscribeLocalEvent<MCTNConnectionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MCTNConnectionComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<MCTNConnectionComponent> ent, ref ComponentStartup args)
    {
        CheckConnection(ent);
        BeginTrackingConnection(ent);
    }
    private void OnShutdown(Entity<MCTNConnectionComponent> ent, ref ComponentShutdown args)
    {
        // Ensure clean connection severence.
        if (ent.Comp.AnchorA.Valid)
            CheckConnection(ent.Comp.AnchorA);
        if (ent.Comp.AnchorB.Valid)
            CheckConnection(ent.Comp.AnchorB);

        StopTrackingConnection(ent);
    }

    private void BeginTrackingConnection(Entity<MCTNConnectionComponent> ent)
    {
        SetupTethering(ent);
        Timer.Spawn((int)(ent.Comp.NextUpdate - _gameTiming.CurTime).TotalMilliseconds, () => TimerFired(ent));
    }

    private void TimerFired(Entity<MCTNConnectionComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!CheckConnection(ent))
            return;

        UpdateTethering(ent);

        ent.Comp.NextUpdate += UpdateDelay;

        var ms = (int)(ent.Comp.NextUpdate - _gameTiming.CurTime).TotalMilliseconds;
        Timer.Spawn(ms, () => TimerFired(ent));
    }

    private void StopTrackingConnection(Entity<MCTNConnectionComponent> ent)
    {
        BreakdownTethering(ent);
    }

    public bool IsConnected(Entity<MCTNComponent> ent) => ent.Comp.Connection != default && !TerminatingOrDeleted(ent.Comp.Connection);

    public EntityUid GetConnectionCounterpart(EntityUid source, MCTNConnectionComponent conn)
    {
        return source == conn.AnchorA ? conn.AnchorB : conn.AnchorA;
    }

    public bool CanKeepConnection(Entity<MCTNComponent> ent)
    {
        if (!IsConnected(ent) || !TryComp<MCTNConnectionComponent>(ent.Comp.Connection, out var conn)) return false;
        return CanKeepConnection((ent.Comp.Connection.GetValueOrDefault(), conn));
    }

    public bool CanKeepConnection(Entity<MCTNConnectionComponent> connection)
    {
        if (!TryComp<MCTNComponent>(connection.Comp.AnchorA, out var mctnA)) return false;
        if (!TryComp<MCTNComponent>(connection.Comp.AnchorB, out var mctnB)) return false;
        return CanKeepConnection((connection.Comp.AnchorA, mctnA), (connection.Comp.AnchorB, mctnB));
    }

    public bool CanKeepConnection(Entity<MCTNComponent> anchorA, Entity<MCTNComponent> anchorB)
    {
        var xformA = Comp<TransformComponent>(anchorA);
        var xformB = Comp<TransformComponent>(anchorB);

        if (!xformA.Anchored || !xformB.Anchored) return false;

        var maxDistance = MathF.Min(anchorA.Comp.MaxRange, anchorB.Comp.MaxRange);

        return _transform.InRange(anchorA.Owner.ToCoordinates(), anchorB.Owner.ToCoordinates(), maxDistance);
    }

    public bool CanConnect(Entity<MCTNComponent> anchorA, Entity<MCTNComponent> anchorB)
    {
        if (anchorA.Comp.Connection.HasValue || anchorB.Comp.Connection.HasValue) return false;
        return CanKeepConnection(anchorA, anchorB);
    }

    public void Connect(EntityUid anchorA, EntityUid anchorB)
    {
        if (!TryComp<MCTNComponent>(anchorA, out var mctnA)) return;
        if (!TryComp<MCTNComponent>(anchorB, out var mctnB)) return;
        Connect((anchorA, mctnA), (anchorB, mctnB));
    }

    public void Connect(Entity<MCTNComponent> anchorA, Entity<MCTNComponent> anchorB)
    {
        if (anchorA.Comp.Connection.HasValue && TryComp<MCTNConnectionComponent>(anchorA.Comp.Connection, out var connectionA))
            Disconnect((anchorA.Comp.Connection.Value, connectionA));
        if (anchorB.Comp.Connection.HasValue && TryComp<MCTNConnectionComponent>(anchorB.Comp.Connection, out var connectionB))
            Disconnect((anchorB.Comp.Connection.Value, connectionB));

        // Ensure anchors are reset.
        ResetNode(anchorA);
        ResetNode(anchorB);

        var newConnectionEid = SpawnAttachedTo(null, anchorA.Owner.ToCoordinates());
        var newConnection = EnsureComp<MCTNConnectionComponent>(newConnectionEid);

        anchorA.Comp.Connection = anchorB.Comp.Connection = newConnectionEid;
        newConnection.AnchorA = anchorA;
        newConnection.AnchorB = anchorB;

        OnConnectionEstablished((newConnectionEid, newConnection), anchorA, anchorB);
    }

    private void OnConnectionEstablished(Entity<MCTNConnectionComponent> newConnection, Entity<MCTNComponent> anchorA, Entity<MCTNComponent> anchorB)
    {
        var evA = new MCTNConnected(anchorA, anchorA.Comp, newConnection);
        RaiseLocalEvent(anchorA, ref evA, true);
        OnConnectionChanged(anchorA, newConnection);

        var evB = new MCTNConnected(anchorB, anchorB.Comp, newConnection);
        RaiseLocalEvent(anchorB, ref evB, true);
        OnConnectionChanged(anchorB, newConnection);
    }

    private void OnConnectionSeverance(Entity<MCTNConnectionComponent> connection, Entity<MCTNComponent> anchorA, Entity<MCTNComponent> anchorB)
    {
        var evA = new MCTNDisconnected(anchorA, anchorA.Comp);
        RaiseLocalEvent(anchorA, ref evA, true);
        OnConnectionChanged(anchorA, null, connection);

        var evB = new MCTNDisconnected(anchorB, anchorB.Comp);
        RaiseLocalEvent(anchorB, ref evB, true);
        OnConnectionChanged(anchorB, null, connection);
    }

    private void OnConnectionChanged(Entity<MCTNComponent> entity, Entity<MCTNConnectionComponent>? newConnection = null, Entity<MCTNConnectionComponent>? previousConnection = null)
    {
        UpdateUserInterface(entity.Owner, entity.Comp);

        var ev = new MCTNConnectionChange(entity, entity.Comp, previousConnection, newConnection);
        RaiseLocalEvent(entity, ref ev, true);
    }

    public void Disconnect(Entity<MCTNConnectionComponent> entity)
    {

        if (TryComp<MCTNComponent>(entity.Comp.AnchorA, out var mctnA))
            ResetNode((entity.Comp.AnchorA, mctnA));
        if (TryComp<MCTNComponent>(entity.Comp.AnchorB, out var mctnB))
            ResetNode((entity.Comp.AnchorB, mctnB));

        if (mctnA != null && mctnB != null) // Which should always be the case...
            OnConnectionSeverance(entity, (entity.Comp.AnchorA, mctnA), (entity.Comp.AnchorB, mctnB));
        QueueDel(entity);
    }

    public void Disconnect(Entity<MCTNComponent> entity)
    {
        var connectionEntity = entity.Comp.Connection.GetValueOrDefault();
        if (connectionEntity == default || TerminatingOrDeleted(connectionEntity))
        {
            ResetNode(entity);
        }
        else if (TryComp<MCTNConnectionComponent>(connectionEntity, out var connection))
            Disconnect((connectionEntity, connection));
    }

    public bool CheckConnection(EntityUid entity)
    {
        if (TryComp<MCTNComponent>(entity, out var mctNode))
            return CheckConnection((entity, mctNode));
        if (TryComp<MCTNConnectionComponent>(entity, out var conn))
            return CheckConnection((entity, conn));
        return false;
    }

    public bool CheckConnection(Entity<MCTNComponent> entity)
    {
        var result = IsConnected(entity) && CanKeepConnection(entity);
        if (!result)
            Disconnect(entity);
        return result;
    }

    public bool CheckConnection(Entity<MCTNConnectionComponent> entity)
    {
        // Probably spawning, skip.
        if (!entity.Comp.AnchorA.Valid || !entity.Comp.AnchorA.Valid) return true;

        var result = CanKeepConnection(entity);
        if (!result)
            Disconnect(entity);
        return result;
    }

    private IEnumerable<Entity<MCTNComponent>> GetAvailableConnections(Entity<MCTNComponent> entity)
    {
        var enumerator = EntityQueryEnumerator<MCTNComponent>();
        while (enumerator.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);
            if (uid == entity.Owner) continue;
            if (CanKeepConnection(entity, ent))
                yield return ent;
        }
    }
}
