using Content.Shared.Access.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Remotes.Components;
using Content.Shared.Remotes.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.UserInterface;

namespace Content.Server.Remotes;

public sealed class DoorRemoteSystem : SharedDoorRemoteSystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedDoorSystem _doorSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorRemoteComponent, UseInHandEvent>(OnUseInHand, before: [typeof(ActivatableUISystem)]);
    }

    protected override void OnBeforeInteract(Entity<DoorRemoteComponent> remote, ref BeforeRangedInteractEvent args)
    {
        if (!remote.Comp.Linkable)
        {
            base.OnBeforeInteract(remote, ref args);
            return;
        }

        if (args.Handled)
            return;

        args.Handled = true;

        if (args.Target is not { } target || !TryComp<DoorComponent>(target, out var door))
        {
            Popup(args.User, "door-remote-link-invalid-target");
            return;
        }

        if (!args.CanReach)
        {
            Popup(args.User, "door-remote-link-out-of-range");
            return;
        }

        if (remote.Comp.RequireTagWhitelist && !_tagSystem.HasTag(target, remote.Comp.TargetTag))
        {
            Popup(args.User, "door-remote-link-invalid-target");
            return;
        }

        TryComp<AccessReaderComponent>(target, out var access);
        if (!_doorSystem.HasAccess(target, args.User, door, access))
        {
            Popup(args.User, "door-remote-link-denied");
            return;
        }

        var link = EnsureComp<DoorRemoteLinkComponent>(target);
        if (string.IsNullOrWhiteSpace(link.LinkId))
        {
            link.LinkId = Guid.NewGuid().ToString("N");
            Dirty(target, link);
        }

        remote.Comp.LinkId = link.LinkId;
        Dirty(remote);

        _popup.PopupEntity(
            Loc.GetString("door-remote-link-success", ("door", target)),
            remote,
            args.User);
        _adminLogger.Add(
            LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(args.User):player} linked {ToPrettyString(remote):remote} to {ToPrettyString(target):door}");
    }

    private void OnUseInHand(Entity<DoorRemoteComponent> remote, ref UseInHandEvent args)
    {
        if (args.Handled || !remote.Comp.Linkable)
            return;

        args.Handled = true;

        if (string.IsNullOrWhiteSpace(remote.Comp.LinkId))
        {
            Popup(args.User, "door-remote-link-unlinked");
            return;
        }

        if (!TryFindLinkedDoor(remote.Comp.LinkId, out var target, out var door))
        {
            Popup(args.User, "door-remote-link-unavailable");
            return;
        }

        if (!_examine.InRangeUnOccluded(
                args.User,
                target,
                SharedInteractionSystem.MaxRaycastRange,
                null))
        {
            Popup(args.User, "door-remote-link-out-of-range");
            return;
        }

        if (!_powerReceiver.IsPowered(target))
        {
            Popup(args.User, "door-remote-no-power");
            return;
        }

        if (_doorSystem.TryToggleDoor(target, door))
        {
            _adminLogger.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(args.User):player} activated {ToPrettyString(remote):remote} for {ToPrettyString(target):door}: {door.State}");
        }
    }

    private bool TryFindLinkedDoor(string linkId, out EntityUid target, out DoorComponent door)
    {
        target = default;
        door = default!;
        var found = false;
        var query = EntityQueryEnumerator<DoorRemoteLinkComponent, DoorComponent>();

        while (query.MoveNext(out var uid, out var link, out var doorComp))
        {
            if (!string.Equals(link.LinkId, linkId, StringComparison.Ordinal))
                continue;

            if (found)
                return false;

            target = uid;
            door = doorComp;
            found = true;
        }

        return found;
    }

    private void Popup(EntityUid user, string message)
    {
        _popup.PopupEntity(Loc.GetString(message), user, user);
    }
}
