using Content.Server.MCTN.Components;
using Content.Shared.NodeContainer;

namespace Content.Server.MCTN.Systems;

public sealed partial class MCTNSystem : EntitySystem
{
    private void InitializePlugs()
    {
        SubscribeLocalEvent<MCTNComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MCTNComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MCTNComponent, AnchorStateChangedEvent>(OnAnchorStateChange);
    }

    private void OnStartup(Entity<MCTNComponent> ent, ref ComponentStartup args)
    {
        CheckConnection(ent);
    }

    private void OnShutdown(Entity<MCTNComponent> ent, ref ComponentShutdown args)
    {
        Disconnect(ent);
    }

    private void OnAnchorStateChange(Entity<MCTNComponent> ent, ref AnchorStateChangedEvent args)
    {
        CheckConnection(ent);
    }

    private void ResetNode(Entity<MCTNComponent> ent)
    {
        ent.Comp.Connection = default;
        ent.Comp.EnabledPlugs.Clear();

        if (TryComp<NodeContainerComponent>(ent, out var container) && container != null)
        {
            foreach (var node in container.Nodes)
            {
                _nodeGroup.QueueReflood(node.Value);
            }
        }
    }
}
