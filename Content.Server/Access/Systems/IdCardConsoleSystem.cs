using Content.Server.Chat.Systems;
using Content.Server.Containers;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Construction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Paper;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using Content.Shared.Throwing;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Content.Shared.Access.Components.IdCardConsoleComponent;

namespace Content.Server.Access.Systems;

[UsedImplicitly]
public sealed class IdCardConsoleSystem : SharedIdCardConsoleSystem
{
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly StationRecordsSystem _record = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly AccessSystem _access = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly PaperSystem _paperSystem = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    private const int MaxRecordContentLength = 4096;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdCardConsoleComponent, RegisterTargetIdMessage>(OnRegisterTargetIdMessage);
        SubscribeLocalEvent<IdCardConsoleComponent, WriteToTargetIdMessage>(OnWriteToTargetIdMessage);
        SubscribeLocalEvent<IdCardConsoleComponent, SearchRecord>(OnSearchRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, ChangeAssignment>(OnChangeAssignment);
        SubscribeLocalEvent<IdCardConsoleComponent, AccountModResetSpending>(OnResetSpending);
        SubscribeLocalEvent<IdCardConsoleComponent, SaveGeneralRecord>(OnSaveGeneralRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, PrintGeneralRecord>(OnPrintGeneralRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, SaveMedicalRecord>(OnSaveMedicalRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, PrintMedicalRecord>(OnPrintMedicalRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, SaveCriminalRecord>(OnSaveCriminalRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, PrintCriminalRecord>(OnPrintCriminalRecord);
        // one day, maybe bound user interfaces can be shared too.
        SubscribeLocalEvent<IdCardConsoleComponent, ComponentStartup>(UpdateUserInterface);
        SubscribeLocalEvent<IdCardConsoleComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
        SubscribeLocalEvent<IdCardConsoleComponent, EntRemovedFromContainerMessage>(UpdateUserInterface);
        SubscribeLocalEvent<IdCardConsoleComponent, DamageChangedEvent>(OnDamageChanged);

        // Intercept the event before anyone can do anything with it!
        SubscribeLocalEvent<IdCardConsoleComponent, MachineDeconstructedEvent>(OnMachineDeconstructed,
            before: [typeof(EmptyOnMachineDeconstructSystem), typeof(ItemSlotsSystem)]);
    }

    private bool TryNormalizeRecordName(string? recordName, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(recordName))
            return false;

        normalized = recordName.Trim();
        return normalized.Length <= _cfgManager.GetCVar(CCVars.MaxNameLength);
    }

    private static string ClampRecordContent(string content)
    {
        return content.Length > MaxRecordContentLength
            ? content[..MaxRecordContentLength]
            : content;
    }

    private CrewRecord? TryEnsureRecord(EntityUid uid, string recordName)
    {
        if (!TryNormalizeRecordName(recordName, out var normalizedName))
            return null;

        var station = _station.GetOwningStation(uid);
        if (station == null) return null;
        if (!TryComp(station, out CrewRecordsComponent? stationData))
        {
            stationData = null;
            return null;
        }
        if (stationData == null) return null;
        stationData.TryEnsureRecord(normalizedName, out var record, EntityManager);
        return record;
    }

    private CrewRecord? TryGetRecord(EntityUid uid, string recordName)
    {
        if (!TryNormalizeRecordName(recordName, out var normalizedName))
            return null;

        var station = _station.GetOwningStation(uid);
        if (station == null)
            return null;

        if (!TryComp(station, out CrewRecordsComponent? crewRecords) ||
            !crewRecords.TryGetRecord(normalizedName, out var record))
        {
            return null;
        }

        return record;
    }

    private bool TryGetStationRecordKey(EntityUid station, string name, out StationRecordKey key)
    {
        key = StationRecordKey.Invalid;
        var id = _record.GetRecordByName(station, name);
        if (id == null)
            return false;

        key = new StationRecordKey(id.Value, station);
        return true;
    }

    private bool TryGetVerifiedCrewRecordForId(
        EntityUid console,
        EntityUid id,
        [NotNullWhen(true)] out CrewRecord? record,
        out CrewAssignment? assignment)
    {
        record = null;
        assignment = null;

        var station = _station.GetOwningStation(console);
        if (station == null)
            return false;

        if (!TryComp<IdCardComponent>(id, out var idCard) ||
            string.IsNullOrWhiteSpace(idCard.FullName))
        {
            return false;
        }

        if (!TryComp<StationRecordKeyStorageComponent>(id, out var keyStorage) ||
            keyStorage.Key is not { } key ||
            key.OriginStation != station.Value)
        {
            return false;
        }

        if (!_record.TryGetRecord<GeneralStationRecord>(key, out var generalRecord) ||
            generalRecord.Name != idCard.FullName)
        {
            return false;
        }

        if (!TryComp(station.Value, out CrewRecordsComponent? crewRecords) ||
            !crewRecords.TryGetRecord(idCard.FullName, out record) ||
            record == null)
        {
            return false;
        }

        if (TryComp(station.Value, out CrewAssignmentsComponent? assignments))
            assignments.TryGetAssignment(record.AssignmentID, out assignment);

        return true;
    }

    private bool IsVerifiedStationOwner(EntityUid console, EntityUid id)
    {
        var station = _station.GetOwningStation(console);
        if (station == null ||
            !TryComp(station.Value, out StationDataComponent? stationData) ||
            !TryComp<IdCardComponent>(id, out var idCard) ||
            string.IsNullOrWhiteSpace(idCard.FullName))
        {
            return false;
        }

        if (!stationData.Owners.Contains(idCard.FullName))
            return false;

        if (!TryComp<StationRecordKeyStorageComponent>(id, out var keyStorage) ||
            keyStorage.Key is not { } key ||
            key.OriginStation != station.Value)
        {
            return false;
        }

        return _record.TryGetRecord<GeneralStationRecord>(key, out var generalRecord) &&
               generalRecord.Name == idCard.FullName;
    }

    private bool PrivilegedIdHasAnyAccess(EntityUid console, EntityUid id, params string[] accessNames)
    {
        if (TryGetVerifiedCrewRecordForId(console, id, out _, out var assignment))
        {
            if (IsVerifiedStationOwner(console, id))
                return true;

            return assignment != null && accessNames.Any(access => assignment.AccessIDs.Contains(access));
        }

        var tags = _accessReader.FindAccessTags(id);
        return accessNames.Any(access => tags.Contains(access));
    }

    private bool PrivilegedIdCanManageIds(EntityUid console, IdCardConsoleComponent component, [NotNullWhen(true)] out EntityUid? id)
    {
        id = null;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId)
            return false;

        id = privilegedId;
        return PrivilegedIdHasAnyAccess(console, privilegedId, "Command", "HeadOfPersonnel");
    }

    private bool PrivilegedIdCanEditGeneralRecords(EntityUid console, EntityUid id)
    {
        if (!TryGetVerifiedCrewRecordForId(console, id, out _, out var assignment))
            return false;

        return IsVerifiedStationOwner(console, id) || assignment?.CanEditGeneralRecord == true;
    }

    private static CrewRecord GetVisibleRecord(CrewRecord source, bool canViewGeneral, bool canViewMedical, bool canViewCriminal)
    {
        return new CrewRecord(source.Name)
        {
            AssignmentID = source.AssignmentID,
            Spent = source.Spent,
            GeneralRecord = canViewGeneral ? source.GeneralRecord : string.Empty,
            MedicalRecord = canViewMedical ? source.MedicalRecord : string.Empty,
            CriminalRecord = canViewCriminal ? source.CriminalRecord : string.Empty,
            SecurityStatus = canViewCriminal ? source.SecurityStatus : Content.Shared.Security.SecurityStatus.None,
            WantedReason = canViewCriminal ? source.WantedReason : null,
            CrimeHistory = canViewCriminal ? new List<Content.Shared.CriminalRecords.CrimeHistory>(source.CrimeHistory) : new()
        };
    }

    private void OnEntInserted(EntityUid uid, IdCardConsoleComponent component, EntInsertedIntoContainerMessage args)
    {
        if (component.TargetIdSlot.Item == args.Entity)
        {
            if (component.TargetIdSlot.Item is { Valid: true } targetId) // targetID lsot occupied
            {
                var idComponent = Comp<IdCardComponent>(targetId);
                if (idComponent != null && idComponent.FullName != null)
                    component.SelectedRecord = TryEnsureRecord(uid, idComponent.FullName);
            }
        }

        UpdateUserInterface(uid, component, args);
    }

    private void OnRegisterTargetIdMessage(EntityUid uid, IdCardConsoleComponent component, RegisterTargetIdMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.Item is not { Valid: true } targetId)
            return;

        if (!PrivilegedIdCanManageIds(uid, component, out _))
            return;

        if (component.SelectedRecord == null)
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null)
            return;

        if (!TryComp(station, out CrewAssignmentsComponent? stationData))
            return;

        if (!TryComp(station, out StationDataComponent? sD))
            return;

        var targetCard = Comp<IdCardComponent>(targetId);
        if (!string.IsNullOrWhiteSpace(targetCard.FullName))
            return;

        if (!TryEnsureStationRecord(station.Value, component.SelectedRecord, stationData))
            return;

        if (!TryGetStationRecordKey(station.Value, component.SelectedRecord.Name, out var recordKey))
            return;

        targetCard.stationID = sD.UID;
        _record.SetIdKey(targetId, recordKey);
        _idCard.TryChangeFullName(targetId, component.SelectedRecord.Name, targetCard, player);

        if (stationData.CrewAssignments.TryGetValue(component.SelectedRecord.AssignmentID, out var crewAssignment) && crewAssignment != null)
        {
            _idCard.TryChangeJobTitle(targetId, crewAssignment.Name, targetCard, player);
            var convertedAccess = crewAssignment.AccessIDs.Select(id => new ProtoId<AccessLevelPrototype>(id)).ToList();
            _access.TrySetTags(targetId, convertedAccess);
        }

        Dirty(targetId, targetCard);
        _idCard.RebuildJob(targetId, targetCard);
        _idCard.UpdateEntityName(targetId, targetCard);

        _adminLogger.Add(LogType.Action,
            $"{player} registered card {targetId} to record {component.SelectedRecord.Name} with assignment {targetCard.LocalizedJobTitle}");

        UpdateUserInterface(uid, component, args);
    }

    private bool TryEnsureStationRecord(EntityUid station, CrewRecord crewRecord, CrewAssignmentsComponent? assignments)
    {
        if (_record.GetRecordByName(station, crewRecord.Name) != null)
            return true;

        CrewAssignment? assignment = null;
        assignments?.TryGetAssignment(crewRecord.AssignmentID, out assignment);

        var record = new GeneralStationRecord
        {
            Name = crewRecord.Name,
            JobTitle = assignment?.Name ?? string.Empty,
            DisplayPriority = assignment?.Clevel ?? 0,
        };

        var key = _record.AddRecordEntry(station, record);
        if (!key.IsValid())
        {
            Log.Warning($"Failed to create station record entry for persistent crew record {crewRecord.Name}");
            return false;
        }

        _record.Synchronize(key);
        return true;
    }

    private void OnSaveGeneralRecord(EntityUid uid, IdCardConsoleComponent component, SaveGeneralRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;
        var station = _station.GetOwningStation(uid);
        if (station == null) return;
        if (!PrivilegedIdCanEditGeneralRecords(uid, privilegedId)) return;


        component.SelectedRecord.GeneralRecord = ClampRecordContent(args.Content);

        UpdateUserInterface(uid, component, args);
    }

    private void OnPrintGeneralRecord(EntityUid uid, IdCardConsoleComponent component, PrintGeneralRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;
        if (!PrivilegedIdCanEditGeneralRecords(uid, privilegedId)) return;

        SpawnPaper(uid, component.SelectedRecord.GeneralRecord, $"{component.SelectedRecord.Name} General Record");
    }

    private void OnSaveMedicalRecord(EntityUid uid, IdCardConsoleComponent component, SaveMedicalRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;
        var station = _station.GetOwningStation(uid);
        if (station == null) return;

        if (!PrivilegedIdHasAnyAccess(uid, privilegedId, "Medical", "ChiefMedicalOfficer", "Command")) return;

        if (!PrivilegedIdCanEditGeneralRecords(uid, privilegedId)) return;

        component.SelectedRecord.MedicalRecord = ClampRecordContent(args.Content);

        UpdateUserInterface(uid, component, args);
    }

    private void OnPrintMedicalRecord(EntityUid uid, IdCardConsoleComponent component, PrintMedicalRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;

        if (!PrivilegedIdHasAnyAccess(uid, privilegedId, "Medical", "ChiefMedicalOfficer", "Command")) return;

        SpawnPaper(uid, component.SelectedRecord.MedicalRecord, $"{component.SelectedRecord.Name} Medical Record");
    }

    private void OnSaveCriminalRecord(EntityUid uid, IdCardConsoleComponent component, SaveCriminalRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;
        var station = _station.GetOwningStation(uid);
        if (station == null) return;

        if (!PrivilegedIdHasAnyAccess(uid, privilegedId, "Security", "HeadOfSecurity", "Command")) return;

        if (!PrivilegedIdCanEditGeneralRecords(uid, privilegedId)) return;

        component.SelectedRecord.CriminalRecord = ClampRecordContent(args.Content);

        UpdateUserInterface(uid, component, args);
    }

    private void OnPrintCriminalRecord(EntityUid uid, IdCardConsoleComponent component, PrintCriminalRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;

        if (!PrivilegedIdHasAnyAccess(uid, privilegedId, "Security", "HeadOfSecurity", "Command")) return;

        SpawnPaper(uid, component.SelectedRecord.CriminalRecord, $"{component.SelectedRecord.Name} Criminal Record");
    }


    private void OnSearchRecord(EntityUid uid, IdCardConsoleComponent component, SearchRecord args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId)
            return;

        if (!PrivilegedIdCanManageIds(uid, component, out _) &&
            !PrivilegedIdHasAnyAccess(uid, privilegedId, "Medical", "ChiefMedicalOfficer", "Security", "HeadOfSecurity", "Command"))
        {
            return;
        }

        if (component.SelectedRecord == null || component.SelectedRecord.Name != args.FullName)
        {
            component.SelectedRecord = TryGetRecord(uid, args.FullName);
        }

        UpdateUserInterface(uid, component, args);
    }

    private void OnResetSpending(EntityUid uid, IdCardConsoleComponent component, AccountModResetSpending args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null) return;
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId) return;
        var station = _station.GetOwningStation(uid);
        if (station == null) return;
        if (!TryGetVerifiedCrewRecordForId(uid, privilegedId, out var privRecord, out _))
            return;

        if (_station.CanSpend(privRecord.Name, station.Value, component.SelectedRecord.Spent))
        {
            _station.TrackSpending(privRecord.Name, station.Value, component.SelectedRecord.Spent);
            _station.ResetSpending(component.SelectedRecord.Name, station.Value);
        }

        UpdateUserInterface(uid, component, args);
    }

    private void OnChangeAssignment(EntityUid uid, IdCardConsoleComponent component, ChangeAssignment args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (component.SelectedRecord == null)
        {
            return;
        }
        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId)
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null) return;
        if (!TryComp(station, out CrewAssignmentsComponent? cW))
        {
            return;
        }

        var possibleAssignments = cW.CrewAssignments;
        CrewAssignment? currentTargetAssignment;
        possibleAssignments.TryGetValue(component.SelectedRecord.AssignmentID, out currentTargetAssignment);
        var currentPrivAssignment = TryGetVerifiedCrewRecordForId(uid, privilegedId, out _, out var verifiedPrivAssignment)
            ? verifiedPrivAssignment
            : null;
        CrewAssignment? newTargetAssignment;
        possibleAssignments.TryGetValue(args.ID, out newTargetAssignment);
        if (newTargetAssignment == null) return;
        var owner = PrivilegedIdCanManageIds(uid, component, out _);
        if (!TryComp(station, out StationDataComponent? sD))
            return;

        if (!owner && (currentTargetAssignment != null && (currentPrivAssignment == null || currentTargetAssignment.Clevel >= currentPrivAssignment.Clevel)))
        {
            return;
        }
        if (!owner && (currentPrivAssignment == null || newTargetAssignment.Clevel >= currentPrivAssignment.Clevel))
        {
            return;
        }
        component.SelectedRecord.AssignmentID = args.ID;
        if (TryComp(station, out CrewRecordsComponent? stationData))
        {
            Dirty((EntityUid)station, stationData);
        }
        var query = EntityQueryEnumerator<IdCardComponent>();
        var convertedAccess = newTargetAssignment.AccessIDs.Select(id => new ProtoId<AccessLevelPrototype>(id)).ToList();
        while (query.MoveNext(out var carde, out var card))
        {
            if (card.FullName == component.SelectedRecord.Name && card.stationID == sD.UID)
            {
                _idCard.TryChangeJobTitle(carde, newTargetAssignment.Name, card, player);
                _access.TrySetTags(carde, convertedAccess);
            }
        }
        UpdateUserInterface(uid, component, args);
    }
    private void OnWriteToTargetIdMessage(EntityUid uid, IdCardConsoleComponent component, WriteToTargetIdMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!PrivilegedIdCanManageIds(uid, component, out _))
            return;

        TryWriteToTargetId(uid, args.FullName, args.JobTitle, args.AccessList, args.JobPrototype, player, component);

        UpdateUserInterface(uid, component, args);
    }

    private void UpdateUserInterface(EntityUid uid, IdCardConsoleComponent component, EntityEventArgs args)
    {
        if (!component.Initialized)
            return;
        IdCardConsoleBoundUserInterfaceState newState;
        var station = _station.GetOwningStation(uid);
        if (station == null) return;
        if (!TryComp(station, out CrewAssignmentsComponent? stationData))
        {
            stationData = null;
            return;
        }
        var possibleAssignments = stationData.CrewAssignments;

        var privilegedIdName = string.Empty;
        var privFullName = string.Empty;
        var targetIdName = string.Empty;
        var targetIdFullName = string.Empty;
        CrewAssignment? assignment = null;
        CrewAssignment? privassignment = null;
        var owner = false;
        var canEditGeneral = false;

        if (component.TargetIdSlot.Item is { Valid: true } targetId) // targetID lsot occupied
        {
            var targetIdComponent = Comp<IdCardComponent>(targetId);
            targetIdFullName = targetIdComponent.FullName ?? string.Empty;
            targetIdName = Comp<MetaDataComponent>(targetId).EntityName;
        }
        var canAccessCriminal = false;
        var canAccessMedical = false;

        if (component.PrivilegedIdSlot.Item is { Valid: true } privId) // targetID lsot occupied
        {
            privilegedIdName = Comp<MetaDataComponent>(privId).EntityName;
            if (TryGetVerifiedCrewRecordForId(uid, privId, out var verifiedPrivRecord, out var verifiedPrivAssignment))
            {
                component.PrivRecord = verifiedPrivRecord;
                privassignment = verifiedPrivAssignment;
            }
            else
                component.PrivRecord = null;

            canAccessCriminal = PrivilegedIdHasAnyAccess(uid, privId, "Security", "HeadOfSecurity", "Command");
            canAccessMedical = PrivilegedIdHasAnyAccess(uid, privId, "Medical", "ChiefMedicalOfficer", "Command");
            canEditGeneral = PrivilegedIdCanEditGeneralRecords(uid, privId);
            owner = PrivilegedIdCanManageIds(uid, component, out _);
        }
        else
        {
            component.PrivRecord = null;
        }

        if (component.SelectedRecord == null)
        {


            newState = new IdCardConsoleBoundUserInterfaceState(
                component.PrivilegedIdSlot.HasItem,
                owner,
                component.TargetIdSlot.HasItem,
                targetIdFullName,
                targetIdName,
                privilegedIdName,
                privFullName,
                assignment,
                privassignment,
                possibleAssignments,
                owner,
                0,
                null,
                canAccessCriminal,
                canAccessMedical);


        }
        else
        {

            possibleAssignments.TryGetValue(component.SelectedRecord.AssignmentID, out assignment);
            var visibleRecord = GetVisibleRecord(component.SelectedRecord, canEditGeneral || owner, canAccessMedical || owner, canAccessCriminal || owner);

            newState = new IdCardConsoleBoundUserInterfaceState(
                component.PrivilegedIdSlot.HasItem,
                owner,
                component.TargetIdSlot.HasItem,
                targetIdFullName,
                targetIdName,
                privilegedIdName,
                privFullName,
                assignment,
                privassignment,
                possibleAssignments,
                owner,
                component.SelectedRecord.Spent,
                visibleRecord,
                canAccessCriminal,
                canAccessMedical);

        }

        _userInterface.SetUiState(uid, IdCardConsoleUiKey.Key, newState);
    }

    /// <summary>
    /// Called whenever an access button is pressed, adding or removing that access from the target ID card.
    /// Writes data passed from the UI into the ID stored in <see cref="IdCardConsoleComponent.TargetIdSlot"/>, if present.
    /// </summary>
    private void TryWriteToTargetId(EntityUid uid,
        string newFullName,
        string newJobTitle,
        List<ProtoId<AccessLevelPrototype>> newAccessList,
        ProtoId<JobPrototype> newJobProto,
        EntityUid player,
        IdCardConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.TargetIdSlot.Item is not { Valid: true } targetId || !PrivilegedIdCanManageIds(uid, component, out var privilegedId))
            return;

        // Limit name and job title lengths
        var maxNameLength = _cfgManager.GetCVar(CCVars.MaxNameLength);
        var maxIdJobLength = _cfgManager.GetCVar(CCVars.MaxIdJobLength);

        if (newFullName.Length > maxNameLength)
            newFullName = newFullName[..maxNameLength];

        if (newJobTitle.Length > maxIdJobLength)
            newJobTitle = newJobTitle[..maxIdJobLength];

        _idCard.TryChangeFullName(targetId, newFullName, player: player);
        _idCard.TryChangeJobTitle(targetId, newJobTitle, player: player);

        if (_prototype.Resolve(newJobProto, out var job)
            && _prototype.Resolve(job.Icon, out var jobIcon))
        {
            _idCard.TryChangeJobIcon(targetId, jobIcon, player: player);
            _idCard.TryChangeJobDepartment(targetId, job);
        }

        UpdateStationRecord(uid, targetId, newFullName, newJobTitle, job);
        if ((!TryComp<StationRecordKeyStorageComponent>(targetId, out var keyStorage)
            || keyStorage.Key is not { } key
            || !_record.TryGetRecord<GeneralStationRecord>(key, out _))
            && newJobProto != string.Empty)
        {
            Comp<IdCardComponent>(targetId).JobPrototype = newJobProto;
        }



        var oldTags = _access.TryGetTags(targetId)?.ToList() ?? new List<ProtoId<AccessLevelPrototype>>();

        if (oldTags.SequenceEqual(newAccessList))
            return;

        // I hate that C# doesn't have an option for this and don't desire to write this out the hard way.
        // var difference = newAccessList.Difference(oldTags);
        var difference = newAccessList.Union(oldTags).Except(newAccessList.Intersect(oldTags)).ToHashSet();
        var privilegedPerms = _accessReader.FindAccessTags(privilegedId.Value);
        if (!difference.IsSubsetOf(privilegedPerms))
        {
            _sawmill.Warning($"User {ToPrettyString(uid)} tried to modify permissions they could not give/take!");
            return;
        }

        var addedTags = newAccessList.Except(oldTags).Select(tag => "+" + tag).ToList();
        var removedTags = oldTags.Except(newAccessList).Select(tag => "-" + tag).ToList();
        _access.TrySetTags(targetId, newAccessList);

        /*TODO: ECS SharedIdCardConsoleComponent and then log on card ejection, together with the save.
        This current implementation is pretty shit as it logs 27 entries (27 lines) if someone decides to give themselves AA*/
        _adminLogger.Add(LogType.Action,
            $"{player} has modified {targetId} with the following accesses: [{string.Join(", ", addedTags.Union(removedTags))}] [{string.Join(", ", newAccessList)}]");
    }

    private void UpdateStationRecord(EntityUid uid, EntityUid targetId, string newFullName, ProtoId<AccessLevelPrototype> newJobTitle, JobPrototype? newJobProto)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(targetId, out var keyStorage)
            || keyStorage.Key is not { } key
            || !_record.TryGetRecord<GeneralStationRecord>(key, out var record))
        {
            return;
        }

        record.Name = newFullName;
        record.JobTitle = newJobTitle;

        if (newJobProto != null)
        {
            record.JobPrototype = newJobProto.ID;
            record.JobIcon = newJobProto.Icon;
        }

        _record.Synchronize(key);
    }

    private void OnMachineDeconstructed(Entity<IdCardConsoleComponent> entity, ref MachineDeconstructedEvent args)
    {
        TryDropAndThrowIds(entity.AsNullable());
    }

    private void OnDamageChanged(Entity<IdCardConsoleComponent> entity, ref DamageChangedEvent args)
    {
        if (TryDropAndThrowIds(entity.AsNullable()))
            _chat.TrySendInGameICMessage(entity, Loc.GetString("id-card-console-damaged"), InGameICChatType.Speak, true);
    }


    private void SpawnPaper(EntityUid uid, string content, string title)
    {

        var entityToSpawn = "Paper";
        var printed = Spawn(entityToSpawn, Transform(uid).Coordinates);

        if (TryComp<PaperComponent>(printed, out var paper))
        {
            _paperSystem.SetContent((printed, paper), content);
            paper.EditingDisabled = true;
        }

        _metaData.SetEntityName(printed, title);
    }


    #region PublicAPI

    /// <summary>
    ///     Tries to drop any IDs stored in the console, and then tries to throw them away.
    ///     Returns true if anything was ejected and false otherwise.
    /// </summary>
    public bool TryDropAndThrowIds(Entity<IdCardConsoleComponent?, ItemSlotsComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp1, ref ent.Comp2))
            return false;

        var didEject = false;

        foreach (var slot in ent.Comp2.Slots.Values)
        {
            if (slot.Item == null || slot.ContainerSlot == null)
                continue;

            var item = slot.Item.Value;
            if (_container.Remove(item, slot.ContainerSlot))
            {
                _throwing.TryThrow(item, _random.NextVector2(), baseThrowSpeed: 5f);
                didEject = true;
            }
        }

        return didEject;
    }

    #endregion
}
