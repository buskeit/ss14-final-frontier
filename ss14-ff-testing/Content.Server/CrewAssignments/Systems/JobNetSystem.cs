using Content.Server._NF.Bank;
using Content.Server.Access.Systems;
using Content.Server.Chat.Managers;
using Content.Server.CrewManifest;
using Content.Shared.Cargo;
using Content.Shared.CrewAssignments;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewAssignments.Prototypes;
using Content.Shared.CrewAssignments.Systems;
using Content.Shared.CrewRecords.Components;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;
using Content.Shared.Precursor;
using Content.Shared.Station.Components;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Linq;
using static System.Collections.Specialized.BitVector32;

namespace Content.Server.CrewAssignments.Systems;

/// <summary>
/// Manages general interactions with a store and different entities,
/// getting listings for stores, and interfacing with the store UI.
/// </summary>
public sealed partial class JobNetSystem : SharedJobNetSystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedCargoSystem _cargo = default!;
    [Dependency] private readonly CrewManifestSystem _crewManifest = default!;
    [Dependency] private readonly IdCardSystem _card = default!;

    public override void ReagentObjectiveComplete(JobNetComponent component, ProtoId<PrecursorObjectivePrototype> objective)
    {
        if (_proto.TryIndex(objective, out PrecursorObjectivePrototype? proto) && proto != null)
        {
            component.PrecursorObjectives.Remove(objective);
            component.Precursor += proto.Reward;
            component.XP += proto.Reward;
            if (_proto.TryIndex(component.RogueLevel, out var rogueLevel))
            {
                if (rogueLevel.Next != null && _proto.TryIndex(rogueLevel.Next, out var nextLevel))
                {
                    if (component.XP >= nextLevel.Cost)
                    {
                        component.RogueLevel = nextLevel.ID;
                    }
                }
            }

            EntityUid? player = null;
#pragma warning disable RA0030 // Consider using the non-generic variant of this method
            if (TryComp<TransformComponent>(component.Owner, out var comp) && comp != null)
            {
                player = comp.ParentUid;
            }
#pragma warning restore RA0030 // Consider using the non-generic variant of this method
            if (player == null) return;
            _audio.PlayEntity(component.PaySuccessSound, player.Value, player.Value);
            if (TryComp<ActorComponent>(player, out var actor) && actor != null && actor.PlayerSession != null)
            {
                var msg = $"Precusor Objective Complete! {proto.Reward} precursor gained.";
                _chatManager.ChatMessageToOne(Shared.Chat.ChatChannel.Notifications,
                    msg,
                    msg,
                    player.Value,
                    false,
                    actor.PlayerSession.Channel
                    );
            }
        }
    }
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JobNetComponent, ActivatableUIOpenAttemptEvent>(OnJobNetOpenAttempt);
        SubscribeLocalEvent<JobNetComponent, BeforeActivatableUIOpenEvent>(BeforeActivatableUiOpen);

        SubscribeLocalEvent<JobNetComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<JobNetComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<JobNetComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<JobNetComponent, OpenJobNetImplantEvent>(OnImplantActivate);
        SubscribeLocalEvent<JobNetComponent, JobNetSelectMessage>(OnSelect);
        SubscribeLocalEvent<JobNetComponent, JobNetPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<JobNetComponent, JobNetSelectRogueNetMessage>(OnSelectRogueNet);



        InitializeUi();
    }


    private void OnPurchase(EntityUid uid, JobNetComponent component, JobNetPurchaseMessage args)
    {
        ProtoId<NetworkLevelPrototype> currentLevel = "NetworkLevel1";
        if (_meta.MetaRecords == null) return;
        if (_meta.MetaRecords.TryGetRecord(Name(args.Actor), out var record) && record != null)
        {
            currentLevel = record.Level;
        }
        else return;
        _proto.Resolve(currentLevel, out var currentProto);
        if (currentProto == null) return;
        if (currentProto.Next == null) return;
        _proto.Resolve(currentProto.Next, out var nextProto);
        if (nextProto == null) return;
        int cost = nextProto.Cost;
        if (_bank.TryGetBalance(args.Actor, out var balance))
        {
            if (cost > balance) return;
            if (_bank.TryBankWithdraw(args.Actor, cost))
            {
                record.Level = nextProto.ID;
            }
        }
        UpdateUserInterface(args.Actor, uid, component);

    }

    private void OnSelect(EntityUid uid, JobNetComponent component, JobNetSelectMessage args)
    {
        var station = _station.GetStationByID(args.ID);
        if (station == null || args.ID == 0)
        {
            var currentWorkingFor = component.WorkingFor;
            component.WorkingFor = 0;
            if (currentWorkingFor != 0 && currentWorkingFor != null)
            {
                var sId = _station.GetStationByID(currentWorkingFor.Value);
                if (sId != null) _crewManifest.BuildCrewManifest(sId.Value);
            }
            if (component.WorkingFor != 0 && component.WorkingFor != null)
            {
                var sId = _station.GetStationByID(component.WorkingFor.Value);
                if (sId != null) _crewManifest.BuildCrewManifest(sId.Value);
            }
            _card.UpdateIDAssignment(Name(args.Actor), args.ID);
            UpdateUserInterface(args.Actor, uid, component);
            return;
        }

        if (TryComp<CrewRecordsComponent>(station, out var crewRecord) && crewRecord != null)
        {
            if (crewRecord.TryGetRecord(Name(args.Actor), out var record) && record != null)
            {
                if (TryComp<StationDataComponent>(station, out var stationData))
                {
                    if (TryComp<CrewAssignmentsComponent>(station, out var crewAssignments))
                    {
                        if (crewAssignments.TryGetAssignment(record.AssignmentID, out var assignment) && assignment != null)
                        {
                            if (component.LastWorkedFor != stationData.UID)
                                component.WorkedTime = TimeSpan.Zero;
                            var currentWorkingFor = component.WorkingFor;
                            component.WorkingFor = stationData.UID;
                            if (currentWorkingFor != 0 && currentWorkingFor != null)
                            {
                                var sId = _station.GetStationByID(currentWorkingFor.Value);
                                if (sId != null) _crewManifest.BuildCrewManifest(sId.Value);
                            }
                            if (component.WorkingFor != 0 && component.WorkingFor != null)
                            {
                                var sId = _station.GetStationByID(component.WorkingFor.Value);
                                if (sId != null) _crewManifest.BuildCrewManifest(sId.Value);
                            }
                            _card.UpdateIDAssignment(Name(args.Actor), args.ID);
                            UpdateUserInterface(args.Actor, uid, component);
                        }
                    }
                }
            }
        }

    }

    private void OnSelectRogueNet(EntityUid uid, JobNetComponent component, JobNetSelectRogueNetMessage args)
    {
        if (component.NetworkType != RogueNetworkType.None) return;
        if (component.RogueLevel == "RogueLevel1") return;
        component.NetworkType = args.Net;
        UpdateUserInterface(args.Actor, uid, component);
    }

    private void OnJobNetOpenAttempt(EntityUid uid, JobNetComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (!_mind.TryGetMind(args.User, out var mind, out _))
            return;

        _popup.PopupEntity("Job Network Not Available.", uid, args.User);
        args.Cancel();
    }

    private void OnMapInit(EntityUid uid, JobNetComponent component, MapInitEvent args)
    {

    }

    private void OnStartup(EntityUid uid, JobNetComponent component, ComponentStartup args)
    {
        var currentWorkingFor = component.WorkingFor;
        if (currentWorkingFor != 0 && currentWorkingFor != null)
        {
            var sId = _station.GetStationByID(currentWorkingFor.Value);
            if (sId == null) return;
            var jobNetEnabled = _station.GetJobNetStatus(sId.Value);
            if (!jobNetEnabled)
            {
                component.WorkingFor = 0;
            }
            else
            {
                _crewManifest.BuildCrewManifest(sId.Value);
            }
        }
    }

    private void OnShutdown(EntityUid uid, JobNetComponent component, ComponentShutdown args)
    {
        var currentWorkingFor = component.WorkingFor;
        component.WorkingFor = 0;
        if (currentWorkingFor != 0 && currentWorkingFor != null)
        {
            var sId = _station.GetStationByID(currentWorkingFor.Value);
            if (sId != null) _crewManifest.BuildCrewManifest(sId.Value);
        }
        component.WorkingFor = currentWorkingFor;
    }

    private void OnImplantActivate(EntityUid uid, JobNetComponent component, OpenJobNetImplantEvent args)
    {
        ToggleUi(args.Performer, uid, component);
    }

    public void TryAssignPrecursorObjective(EntityUid user, JobNetComponent component)
    {
        component.PrecursorObjectives.Clear();
        var precursorObjectives = _proto.EnumeratePrototypes<PrecursorObjectivePrototype>();
        var form = precursorObjectives.ToList().Shuffle();
        component.PrecursorObjectives.AddRange(form.First().ID);
    }
    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<JobNetComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.WorkingFor != null && comp.WorkingFor != 0)
            {
                comp.WorkedTime += TimeSpan.FromSeconds(frameTime);
                if (comp.WorkedTime > TimeSpan.FromMinutes(20))
                {
                    comp.WorkedTime = TimeSpan.Zero;
                    TryPay(comp.Owner, comp);
                }
            }

            comp.PrecursorResetTime -= TimeSpan.FromSeconds(frameTime);
            if (comp.PrecursorResetTime <= TimeSpan.Zero)
            {
                comp.PrecursorResetTime = TimeSpan.FromMinutes(30);
                TryAssignPrecursorObjective(uid, comp);

            }

        }
        base.Update(frameTime);
    }
    public void TryPay(EntityUid user, JobNetComponent component)
    {

        if (component.WorkingFor == null || component.WorkingFor == 0) return;
        var station = _station.GetStationByID(component.WorkingFor.Value);
        if (station == null)
        {
            component.WorkingFor = 0;
            return;
        }
        EntityUid? player = null;
#pragma warning disable RA0030 // Consider using the non-generic variant of this method
        if (TryComp<TransformComponent>(user, out var comp) && comp != null)
        {
            player = comp.ParentUid;
        }
#pragma warning restore RA0030 // Consider using the non-generic variant of this method
        if (player == null) return;
        var name = Name(player.Value);
        if (TryComp<CrewRecordsComponent>(station, out var crewRecord) && crewRecord != null)
        {
            if (crewRecord.TryGetRecord(name, out var record) && record != null)
            {
                if (TryComp<StationDataComponent>(station, out var stationData))
                {
                    if (TryComp<CrewAssignmentsComponent>(station, out var crewAssignments))
                    {
                        if (crewAssignments.TryGetAssignment(record.AssignmentID, out var assignment) && assignment != null)
                        {
                            if (assignment.Wage > 0)
                            {
                                if (TryComp<ActorComponent>(player, out var actor) && actor != null && actor.PlayerSession != null)
                                {
                                    var bank = _bank.GetMoneyAccountsComponent();
                                    if (bank == null) return;
                                    if (_cargo.TryGetAccount(station.Value, "Cargo", out var money))
                                    {
                                        if (money < assignment.Wage)
                                        {
                                            _audio.PlayEntity(component.ErrorSound, player.Value, player.Value);
                                            var msg = $"{stationData.StationName} has failed to pay you your ${assignment.Wage} due to insufficient funds.";
                                            if (msg != null)
                                                _chatManager.ChatMessageToOne(Shared.Chat.ChatChannel.Notifications,
                                                    msg,
                                                    msg,
                                                    station.Value,
                                                    false,
                                                    actor.PlayerSession.Channel
                                                    );
                                            return;
                                        }
                                        if (bank.TryGetAccount(name, out var account) && account != null)
                                        {
                                            _audio.PlayEntity(component.PaySuccessSound, player.Value, player.Value);
                                            account.Balance += assignment.Wage;
                                            _cargo.TryAdjustBankAccount(station.Value, "Cargo", -assignment.Wage);
                                            var msg = $"You have received ${assignment.Wage} for working as a {assignment.Name} for {stationData.StationName}.";
                                            if (msg != null)
                                                _chatManager.ChatMessageToOne(Shared.Chat.ChatChannel.Notifications,
                                                    msg,
                                                    msg,
                                                    station.Value,
                                                    false,
                                                    actor.PlayerSession.Channel
                                                    );
                                            _bank.DirtyMoneyAccountsComponent();
                                        }
                                    }
                                    else
                                    {
                                        _audio.PlayEntity(component.ErrorSound, player.Value, player.Value);
                                        var msg = $"{stationData.StationName} has failed to pay you your ${assignment.Wage} due to an invalid account.";
                                        if (msg != null)
                                            _chatManager.ChatMessageToOne(Shared.Chat.ChatChannel.Notifications,
                                                msg,
                                                msg,
                                                station.Value,
                                                false,
                                                actor.PlayerSession.Channel
                                                );
                                        return;
                                    }

                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
