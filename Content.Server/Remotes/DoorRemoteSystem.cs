using Content.Shared.Access.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
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
    private const string LinkSourcePort = "Pressed";
    private const string LinkSinkPort = "Toggle";

    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedDoorSystem _doorSystem = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorRemoteComponent, UseInHandEvent>(OnUseInHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<DoorRemoteComponent, LinkAttemptEvent>(OnLinkAttempt);
        SubscribeLocalEvent<DoorRemoteComponent, NewLinkEvent>(OnNewLink);
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

        if (args.Target is not { } target ||
            !TryComp<DoorComponent>(target, out _) ||
            remote.Comp.RequireTagWhitelist && !_tagSystem.HasTag(target, remote.Comp.TargetTag))
        {
            Popup(args.User, "door-remote-link-invalid-target");
            return;
        }

        Popup(args.User, "door-remote-link-requires-multitool");
    }

    private void OnLinkAttempt(Entity<DoorRemoteComponent> remote, ref LinkAttemptEvent args)
    {
        if (!remote.Comp.Linkable ||
            args.Source != remote.Owner ||
            args.SourcePort != LinkSourcePort ||
            args.SinkPort != LinkSinkPort)
        {
            args.Cancel();
            return;
        }

        if (!TryComp<DoorComponent>(args.Sink, out var door) ||
            remote.Comp.RequireTagWhitelist && !_tagSystem.HasTag(args.Sink, remote.Comp.TargetTag))
        {
            if (args.User is { } user)
                Popup(user, "door-remote-link-invalid-target");
            args.Cancel();
            return;
        }

        if (args.User is not { } actor)
        {
            args.Cancel();
            return;
        }

        TryComp<AccessReaderComponent>(args.Sink, out var access);
        if (!_doorSystem.HasAccess(args.Sink, actor, door, access))
        {
            Popup(actor, "door-remote-link-denied");
            args.Cancel();
        }
    }

    private void OnNewLink(Entity<DoorRemoteComponent> remote, ref NewLinkEvent args)
    {
        if (!remote.Comp.Linkable ||
            args.Source != remote.Owner ||
            args.SourcePort != LinkSourcePort ||
            args.SinkPort != LinkSinkPort)
            return;

        if (TryComp<DeviceLinkSourceComponent>(remote, out var source))
        {
            foreach (var oldTarget in _deviceLink.GetLinkedSinks((remote.Owner, source), LinkSourcePort))
            {
                if (oldTarget != args.Sink)
                    _deviceLink.RemoveSinkFromSource(remote.Owner, oldTarget, source);
            }
        }

        var link = EnsureComp<DoorRemoteLinkComponent>(args.Sink);
        var alreadyLinked = !string.IsNullOrWhiteSpace(remote.Comp.LinkId) &&
                            string.Equals(remote.Comp.LinkId, link.LinkId, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(link.LinkId))
        {
            link.LinkId = Guid.NewGuid().ToString("N");
            Dirty(args.Sink, link);
        }

        remote.Comp.LinkId = link.LinkId;
        Dirty(remote);

        if (args.User is { } actor)
        {
            _popup.PopupEntity(
                Loc.GetString(alreadyLinked
                    ? "door-remote-link-already-linked"
                    : "door-remote-link-success", ("door", args.Sink)),
                remote,
                actor);

            _adminLogger.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(actor):player} linked {ToPrettyString(remote):remote} to {ToPrettyString(args.Sink):door}");
        }
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

        if (!TryFindLinkedDoor(remote, out var target, out var door))
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

    private bool TryFindLinkedDoor(Entity<DoorRemoteComponent> remote, out EntityUid target, out DoorComponent door)
    {
        target = default;
        door = default!;

        if (TryComp<DeviceLinkSourceComponent>(remote, out var source))
        {
            var linked = _deviceLink.GetLinkedSinks((remote.Owner, source), LinkSourcePort);
            if (linked.Count == 1)
            {
                foreach (var linkedTarget in linked)
                {
                    if (!TryComp<DoorComponent>(linkedTarget, out var linkedDoor))
                        break;

                    target = linkedTarget;
                    door = linkedDoor;
                    return true;
                }
            }
        }

        var linkId = remote.Comp.LinkId;
        if (string.IsNullOrWhiteSpace(linkId))
            return false;

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
