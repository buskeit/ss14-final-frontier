using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;
using Content.Shared.MedicalRecords;
using Content.Shared.MedicalRecords.Components;
using Robust.Server.GameObjects;
using System.Linq;

namespace Content.Server.MedicalRecords.Systems;

public sealed class MedicalRecordsConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<MedicalRecordsConsoleComponent>(MedicalRecordsConsoleKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBuiOpened);
            subs.Event<SelectMedicalRecord>(OnSelectPatient);
            subs.Event<SaveMedicalRecordMessage>(OnSaveMedicalRecord);
        });
    }

    private void OnBuiOpened(EntityUid uid, MedicalRecordsConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnSelectPatient(EntityUid uid, MedicalRecordsConsoleComponent component, SelectMedicalRecord args)
    {
        component.ActivePatient = args.PatientName;
        UpdateUserInterface(uid, component);
    }

    private void OnSaveMedicalRecord(EntityUid uid, MedicalRecordsConsoleComponent component, SaveMedicalRecordMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Strict Access Control: verify player has Medical, ChiefMedicalOfficer, or Command access
        if (!IsAuthorized(player, uid))
            return;

        if (component.ActivePatient == null)
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null)
            return;

        if (TryComp<CrewRecordsComponent>(station, out var crewRecords))
        {
            if (crewRecords.TryGetRecord(component.ActivePatient, out var record) && record != null)
            {
                record.MedicalRecord = args.Content;
                Dirty(station.Value, crewRecords);
            }
        }

        UpdateUserInterface(uid, component);
    }

    private void UpdateUserInterface(EntityUid uid, MedicalRecordsConsoleComponent component)
    {
        var station = _station.GetOwningStation(uid);
        if (station == null || !TryComp<CrewRecordsComponent>(station, out var crewRecords))
        {
            _ui.SetUiState(uid, MedicalRecordsConsoleKey.Key, new MedicalRecordsConsoleState(null, null, null, null));
            return;
        }

        // Build listing of patient name -> job
        var patientListing = new Dictionary<string, string>();
        TryComp<CrewAssignmentsComponent>(station, out var crewAssignments);

        foreach (var (name, record) in crewRecords.CrewRecords)
        {
            var job = "*Unassigned*";
            if (crewAssignments != null && crewAssignments.TryGetAssignment(record.AssignmentID, out var crewAssignment) && crewAssignment != null)
            {
                job = crewAssignment.Name;
            }
            patientListing[name] = job;
        }

        string? selectedPatient = component.ActivePatient;
        if (selectedPatient != null && !crewRecords.CrewRecords.ContainsKey(selectedPatient))
        {
            selectedPatient = null;
        }

        if (selectedPatient == null && patientListing.Count > 0)
        {
            selectedPatient = patientListing.Keys.OrderBy(x => x).First();
            component.ActivePatient = selectedPatient;
        }

        string? patientJob = null;
        string? medicalRecord = null;

        if (selectedPatient != null && crewRecords.TryGetRecord(selectedPatient, out var selectedRecord) && selectedRecord != null)
        {
            if (crewAssignments != null && crewAssignments.TryGetAssignment(selectedRecord.AssignmentID, out var crewAssignment) && crewAssignment != null)
            {
                patientJob = crewAssignment.Name;
            }
            medicalRecord = selectedRecord.MedicalRecord;
        }

        var state = new MedicalRecordsConsoleState(selectedPatient, patientJob, medicalRecord, patientListing);
        _ui.SetUiState(uid, MedicalRecordsConsoleKey.Key, state);
    }

    private bool IsAuthorized(EntityUid player, EntityUid console)
    {
        // Require AccessReader allow or manual fallback to access tags
        if (TryComp<AccessReaderComponent>(console, out var reader))
        {
            if (_accessReader.IsAllowed(player, console, reader))
                return true;
        }

        var tags = _accessReader.FindAccessTags(player);
        return tags.Contains("Medical") || tags.Contains("ChiefMedicalOfficer") || tags.Contains("Command");
    }
}
