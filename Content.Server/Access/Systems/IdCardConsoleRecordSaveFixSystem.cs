using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;
using static Content.Shared.Access.Components.IdCardConsoleComponent;

namespace Content.Server.Access.Systems;

/// <summary>
/// Repairs ID console free-text record saves so authorized edits are persisted and reflected in the UI.
/// </summary>
public sealed class IdCardConsoleRecordSaveFixSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    private const int MaxRecordContentLength = 4096;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdCardConsoleComponent, SaveGeneralRecord>(OnSaveGeneralRecord,
            before: [typeof(IdCardConsoleSystem)]);
        SubscribeLocalEvent<IdCardConsoleComponent, SaveMedicalRecord>(OnSaveMedicalRecord,
            before: [typeof(IdCardConsoleSystem)]);
        SubscribeLocalEvent<IdCardConsoleComponent, SaveCriminalRecord>(OnSaveCriminalRecord,
            before: [typeof(IdCardConsoleSystem)]);
    }

    private void OnSaveGeneralRecord(EntityUid uid, IdCardConsoleComponent component, SaveGeneralRecord args)
    {
        if (component.SelectedRecord == null ||
            component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId ||
            !PrivilegedIdCanEditGeneralRecords(privilegedId))
        {
            return;
        }

        component.SelectedRecord.GeneralRecord = ClampRecordContent(args.Content);
        DirtyCrewRecords(uid);
        UpdateUserInterface(uid, component);
    }

    private void OnSaveMedicalRecord(EntityUid uid, IdCardConsoleComponent component, SaveMedicalRecord args)
    {
        if (component.SelectedRecord == null ||
            component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId ||
            !PrivilegedIdCanEditMedicalRecords(privilegedId))
        {
            return;
        }

        component.SelectedRecord.MedicalRecord = ClampRecordContent(args.Content);
        DirtyCrewRecords(uid);
        UpdateUserInterface(uid, component);
    }

    private void OnSaveCriminalRecord(EntityUid uid, IdCardConsoleComponent component, SaveCriminalRecord args)
    {
        if (component.SelectedRecord == null ||
            component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId ||
            !PrivilegedIdCanEditCriminalRecords(privilegedId))
        {
            return;
        }

        component.SelectedRecord.CriminalRecord = ClampRecordContent(args.Content);
        DirtyCrewRecords(uid);
        UpdateUserInterface(uid, component);
    }

    private void DirtyCrewRecords(EntityUid console)
    {
        var station = _station.GetOwningStation(console);
        if (station == null || !TryComp(station.Value, out CrewRecordsComponent? crewRecords))
            return;

        Dirty(station.Value, crewRecords);
    }

    private bool PrivilegedIdCanEditGeneralRecords(EntityUid privilegedId)
    {
        return HasAnyAccess(privilegedId,
            "Captain",
            "Command",
            "HeadOfPersonnel",
            "HeadOfSecurity",
            "ChiefMedicalOfficer",
            "ChiefEngineer",
            "ResearchDirector",
            "Quartermaster");
    }

    private bool PrivilegedIdCanEditMedicalRecords(EntityUid privilegedId)
    {
        return HasAnyAccess(privilegedId, "Medical", "ChiefMedicalOfficer", "Captain", "Command");
    }

    private bool PrivilegedIdCanEditCriminalRecords(EntityUid privilegedId)
    {
        return HasAnyAccess(privilegedId, "Security", "HeadOfSecurity", "Captain", "Command");
    }

    private bool HasAnyAccess(EntityUid id, params string[] accessNames)
    {
        var tags = _accessReader.FindAccessTags(id);
        foreach (var access in accessNames)
        {
            if (tags.Contains(access))
                return true;
        }

        return false;
    }

    private static string ClampRecordContent(string content)
    {
        return content.Length > MaxRecordContentLength
            ? content[..MaxRecordContentLength]
            : content;
    }

    private void UpdateUserInterface(EntityUid uid, IdCardConsoleComponent component)
    {
        if (!component.Initialized)
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null || !TryComp(station.Value, out CrewAssignmentsComponent? assignments))
            return;

        var possibleAssignments = assignments.CrewAssignments;
        var privilegedIdName = string.Empty;
        var targetIdName = string.Empty;
        var targetIdFullName = string.Empty;
        CrewAssignment? assignment = null;
        CrewAssignment? privAssignment = null;
        var canEditGeneral = false;
        var canAccessMedical = false;
        var canAccessCriminal = false;
        var canManageIds = false;

        if (component.TargetIdSlot.Item is { Valid: true } targetId)
        {
            targetIdFullName = Comp<IdCardComponent>(targetId).FullName ?? string.Empty;
            targetIdName = Comp<MetaDataComponent>(targetId).EntityName;
        }

        if (component.PrivilegedIdSlot.Item is { Valid: true } privId)
        {
            privilegedIdName = Comp<MetaDataComponent>(privId).EntityName;
            canEditGeneral = PrivilegedIdCanEditGeneralRecords(privId);
            canAccessMedical = PrivilegedIdCanEditMedicalRecords(privId);
            canAccessCriminal = PrivilegedIdCanEditCriminalRecords(privId);
            canManageIds = HasAnyAccess(privId, "Captain", "Command", "HeadOfPersonnel");

            if (TryComp<IdCardComponent>(privId, out var privIdCard) &&
                !string.IsNullOrWhiteSpace(privIdCard.FullName) &&
                TryComp(station.Value, out CrewRecordsComponent? crewRecords) &&
                crewRecords.TryGetRecord(privIdCard.FullName, out var privRecord) &&
                privRecord != null)
            {
                assignments.CrewAssignments.TryGetValue(privRecord.AssignmentID, out privAssignment);
            }
        }

        CrewRecord? visibleRecord = null;
        var spent = 0;
        if (component.SelectedRecord != null)
        {
            possibleAssignments.TryGetValue(component.SelectedRecord.AssignmentID, out assignment);
            spent = component.SelectedRecord.Spent;
            visibleRecord = GetVisibleRecord(
                component.SelectedRecord,
                canEditGeneral || canManageIds,
                canAccessMedical || canManageIds,
                canAccessCriminal || canManageIds);
        }

        var newState = new IdCardConsoleBoundUserInterfaceState(
            component.PrivilegedIdSlot.HasItem,
            canManageIds,
            component.TargetIdSlot.HasItem,
            targetIdFullName,
            targetIdName,
            privilegedIdName,
            string.Empty,
            assignment,
            privAssignment,
            possibleAssignments,
            canManageIds,
            spent,
            visibleRecord,
            canAccessCriminal,
            canAccessMedical);

        _userInterface.SetUiState(uid, IdCardConsoleUiKey.Key, newState);
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
}