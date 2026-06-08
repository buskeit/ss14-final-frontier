using Content.Server.Botany.Components;
using Content.Server.Power.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Robust.Shared.Physics.Components;

namespace Content.Server.Kitchen.ButcheringMachine;

public sealed class ButcheringMachineSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ButcheringMachineComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<ButcheringMachineComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<ApcPowerReceiverComponent>(ent, out var power) && !power.Powered)
            return;

        if (!CanProcess(ent, args.Used))
            return;

        Process(ent, args.Used);
        args.Handled = true;
    }

    private bool CanProcess(Entity<ButcheringMachineComponent> machine, EntityUid target)
    {
        var isPlant = HasComp<ProduceComponent>(target);
        if (!isPlant && !HasComp<MobStateComponent>(target))
            return false;

        if (!isPlant && machine.Comp.SafetyEnabled && !_mobState.IsDead(target))
            return false;

        return TryComp<ButcherableComponent>(target, out _);
    }

    private void Process(Entity<ButcheringMachineComponent> machine, EntityUid target)
    {
        if (TryComp<ButcherableComponent>(target, out var butcherable))
        {
            foreach (var entry in butcherable.SpawnedEntities)
            {
                for (var i = 0; i < entry.Amount; i++)
                {
                    var spawned = Spawn(entry.PrototypeId, Transform(machine).Coordinates);
                    _transform.DropNextTo(spawned, machine.Owner);
                }
            }
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities(target))
        {
            _transform.DropNextTo(item, machine.Owner);
        }

        QueueDel(target);
        _popup.PopupEntity(Loc.GetString("generic-success"), machine.Owner);
    }
}
