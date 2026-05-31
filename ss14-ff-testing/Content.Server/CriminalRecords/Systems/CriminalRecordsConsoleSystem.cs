using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Components;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;

namespace Content.Server.CriminalRecords.Systems;

/// <summary>
/// Handles all UI for criminal records console
/// </summary>
public sealed class CriminalRecordsConsoleSystem : SharedCriminalRecordsConsoleSystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        Subs.BuiEvents<CriminalRecordsConsoleComponent>(CriminalRecordsConsoleKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(UpdateUserInterface);
            subs.Event<SelectStationRecord>(OnKeySelected);
            subs.Event<SetStationRecordFilter>(OnFiltersChanged);
            subs.Event<CriminalRecordChangeStatus>(OnChangeStatus);
            subs.Event<CriminalRecordAddHistory>(OnAddHistory);
            subs.Event<CriminalRecordDeleteHistory>(OnDeleteHistory);
            subs.Event<CriminalRecordSetStatusFilter>(OnStatusFilterPressed);
        });
    }

    private uint GetNameHash(string name)
    {
        return (uint)name.GetHashCode();
    }

    private CrewRecord? GetRecordFromKey(CrewRecordsComponent crewRecords, uint key, out string? name)
    {
        foreach (var (cName, record) in crewRecords.CrewRecords)
        {
            if (GetNameHash(cName) == key)
            {
                name = cName;
                return record;
            }
        }
        name = null;
        return null;
    }

    private string FormatCriminalRecordText(SecurityStatus status, string? reason, List<CrimeHistory> history)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[bold]Security Status:[/bold] {status.ToString().ToUpper()}");
        if (!string.IsNullOrEmpty(reason))
        {
            sb.AppendLine($"[bold]Reason:[/bold] {reason}");
        }
        sb.AppendLine();
        sb.AppendLine("[bold]Crime History Log:[/bold]");
        if (history.Count == 0)
        {
            sb.AppendLine("- No prior records.");
        }
        else
        {
            foreach (var log in history)
            {
                var initiator = log.InitiatorName ?? "Unknown";
                sb.AppendLine($"- {log.Crime} (Added by {initiator})");
            }
        }
        return sb.ToString();
    }

    private void UpdateUserInterface<T>(Entity<CriminalRecordsConsoleComponent> ent, ref T args)
    {
        UpdateUserInterface(ent);
    }

    private void OnKeySelected(Entity<CriminalRecordsConsoleComponent> ent, ref SelectStationRecord msg)
    {
        ent.Comp.ActiveKey = msg.SelectedKey;
        UpdateUserInterface(ent);
    }

    private void OnStatusFilterPressed(Entity<CriminalRecordsConsoleComponent> ent, ref CriminalRecordSetStatusFilter msg)
    {
        ent.Comp.FilterStatus = msg.FilterStatus;
        UpdateUserInterface(ent);
    }

    private void OnFiltersChanged(Entity<CriminalRecordsConsoleComponent> ent, ref SetStationRecordFilter msg)
    {
        if (ent.Comp.Filter == null ||
            ent.Comp.Filter.Type != msg.Type || ent.Comp.Filter.Value != msg.Value)
        {
            ent.Comp.Filter = new StationRecordsFilter(msg.Type, msg.Value);
            UpdateUserInterface(ent);
        }
    }

    private void GetOfficer(EntityUid uid, out string officer)
    {
        var tryGetIdentityShortInfoEvent = new TryGetIdentityShortInfoEvent(null, uid);
        RaiseLocalEvent(tryGetIdentityShortInfoEvent);
        officer = tryGetIdentityShortInfoEvent.Title ?? Loc.GetString("criminal-records-console-unknown-officer");
    }

    private void OnChangeStatus(Entity<CriminalRecordsConsoleComponent> ent, ref CriminalRecordChangeStatus msg)
    {
        // prevent malf client violating wanted/reason nullability
        if (msg.Status == SecurityStatus.Wanted != (msg.Reason != null) &&
            msg.Status == SecurityStatus.Suspected != (msg.Reason != null) &&
            msg.Status == SecurityStatus.Hostile != (msg.Reason != null))
            return;

        if (!CheckSelected(ent, msg.Actor, out var mob, out var record, out var recordName))
            return;

        if (record.SecurityStatus == msg.Status)
            return;

        // validate the reason
        string? reason = null;
        if (msg.Reason != null)
        {
            reason = msg.Reason.Trim();
            if (reason.Length < 1 || reason.Length > ent.Comp.MaxStringLength)
                return;
        }

        var oldStatus = record.SecurityStatus;
        GetOfficer(mob.Value, out var officer);

        // when arresting someone add it to history automatically
        if (msg.Status == SecurityStatus.Detained)
        {
            var oldReason = record.WantedReason ?? Loc.GetString("criminal-records-console-unspecified-reason");
            var historyText = Loc.GetString("criminal-records-console-auto-history", ("reason", oldReason));
            record.CrimeHistory.Add(new CrimeHistory(TimeSpan.FromSeconds(DateTime.Now.Ticks / 10000000), historyText, officer));
        }

        record.SecurityStatus = msg.Status;
        record.WantedReason = reason;
        record.CriminalRecord = FormatCriminalRecordText(record.SecurityStatus, record.WantedReason, record.CrimeHistory);

        var jobName = "Unknown";
        if (TryComp<CrewAssignmentsComponent>(_station.GetOwningStation(ent), out var crewAssignments) && crewAssignments != null)
        {
            if (crewAssignments.TryGetAssignment(record.AssignmentID, out var crewAssignment) && crewAssignment != null)
            {
                jobName = crewAssignment.Name;
            }
        }

        (string, object)[] args;
        if (reason != null)
            args = new (string, object)[] { ("name", recordName), ("officer", officer), ("reason", reason), ("job", jobName) };
        else
            args = new (string, object)[] { ("name", recordName), ("officer", officer), ("job", jobName) };

        // figure out which radio message to send depending on transition
        var statusString = (oldStatus, msg.Status) switch
        {
            (_, SecurityStatus.Hostile) => "hostile",
            (_, SecurityStatus.Eliminated) => "eliminated",
            (_, SecurityStatus.Detained) => "detained",
            (_, SecurityStatus.Suspected) => "suspected",
            (_, SecurityStatus.Paroled) => "paroled",
            (_, SecurityStatus.Discharged) => "released",
            (_, SecurityStatus.Wanted) => "wanted",
            (SecurityStatus.Hostile, SecurityStatus.None) => "not-hostile",
            (SecurityStatus.Eliminated, SecurityStatus.None) => "not-eliminated",
            (SecurityStatus.Suspected, SecurityStatus.None) => "not-suspected",
            (SecurityStatus.Wanted, SecurityStatus.None) => "not-wanted",
            (SecurityStatus.Detained, SecurityStatus.None) => "released",
            (SecurityStatus.Paroled, SecurityStatus.None) => "not-parole",
            _ => "not-wanted"
        };

        _radio.SendRadioMessage(ent,
            Loc.GetString($"criminal-records-console-{statusString}", args),
            ent.Comp.SecurityChannel,
            ent);

        _adminLogger.Add(LogType.Identity, LogImpact.Low, $"{ToPrettyString(mob.Value):name} changed criminal status for {recordName} to \"{statusString}\"");

        if (_station.GetOwningStation(ent) is { } stationUid && TryComp<CrewRecordsComponent>(stationUid, out var crewRecordsComp))
        {
            Dirty(stationUid, crewRecordsComp);
        }

        UpdateUserInterface(ent);
    }

    private void OnAddHistory(Entity<CriminalRecordsConsoleComponent> ent, ref CriminalRecordAddHistory msg)
    {
        if (!CheckSelected(ent, msg.Actor, out var mob, out var record, out var recordName))
            return;

        var line = msg.Line.Trim();
        if (line.Length < 1 || line.Length > ent.Comp.MaxStringLength)
            return;

        GetOfficer(mob.Value, out var officer);

        record.CrimeHistory.Add(new CrimeHistory(TimeSpan.FromSeconds(DateTime.Now.Ticks / 10000000), line, officer));
        record.CriminalRecord = FormatCriminalRecordText(record.SecurityStatus, record.WantedReason, record.CrimeHistory);

        if (_station.GetOwningStation(ent) is { } stationUid && TryComp<CrewRecordsComponent>(stationUid, out var crewRecordsComp))
        {
            Dirty(stationUid, crewRecordsComp);
        }

        UpdateUserInterface(ent);
    }

    private void OnDeleteHistory(Entity<CriminalRecordsConsoleComponent> ent, ref CriminalRecordDeleteHistory msg)
    {
        if (!CheckSelected(ent, msg.Actor, out _, out var record, out _))
            return;

        if (msg.Index >= record.CrimeHistory.Count)
            return;

        record.CrimeHistory.RemoveAt((int)msg.Index);
        record.CriminalRecord = FormatCriminalRecordText(record.SecurityStatus, record.WantedReason, record.CrimeHistory);

        if (_station.GetOwningStation(ent) is { } stationUid && TryComp<CrewRecordsComponent>(stationUid, out var crewRecordsComp))
        {
            Dirty(stationUid, crewRecordsComp);
        }

        UpdateUserInterface(ent);
    }

    private void UpdateUserInterface(Entity<CriminalRecordsConsoleComponent> ent)
    {
        var (uid, console) = ent;
        var owningStation = _station.GetOwningStation(uid);

        if (owningStation == null || !TryComp<CrewRecordsComponent>(owningStation, out var crewRecords))
        {
            _ui.SetUiState(uid, CriminalRecordsConsoleKey.Key, new CriminalRecordsConsoleState());
            return;
        }

        // Build list of patients/records
        var listing = new Dictionary<uint, string>();
        foreach (var (name, record) in crewRecords.CrewRecords)
        {
            if (console.FilterStatus != SecurityStatus.None && record.SecurityStatus != console.FilterStatus)
                continue;

            if (console.Filter != null && !string.IsNullOrEmpty(console.Filter.Value))
            {
                var val = console.Filter.Value.ToLower();
                if (console.Filter.Type == StationRecordFilterType.Name && !name.ToLower().Contains(val))
                    continue;

                if (console.Filter.Type == StationRecordFilterType.Job)
                {
                    var job = "*Unassigned*";
                    if (TryComp<CrewAssignmentsComponent>(owningStation, out var crewAssignments) && crewAssignments != null)
                    {
                        if (crewAssignments.TryGetAssignment(record.AssignmentID, out var crewAssignment) && crewAssignment != null)
                        {
                            job = crewAssignment.Name;
                        }
                    }
                    if (!job.ToLower().Contains(val))
                        continue;
                }

                if (console.Filter.Type == StationRecordFilterType.Prints || console.Filter.Type == StationRecordFilterType.DNA)
                    continue;
            }

            listing[GetNameHash(name)] = name;
        }

        var state = new CriminalRecordsConsoleState(listing, console.Filter);
        if (console.ActiveKey is { } id)
        {
            var record = GetRecordFromKey(crewRecords, id, out var name);
            if (record != null && name != null)
            {
                state.SelectedKey = id;
                state.StationRecord = new GeneralStationRecord
                {
                    Name = record.Name,
                    JobTitle = "*Unassigned*",
                    JobIcon = "JobIconUnknown"
                };

                if (TryComp<CrewAssignmentsComponent>(owningStation, out var crewAssignments) && crewAssignments != null)
                {
                    if (crewAssignments.TryGetAssignment(record.AssignmentID, out var crewAssignment) && crewAssignment != null)
                    {
                        state.StationRecord.JobTitle = crewAssignment.Name;
                    }
                }

                state.CriminalRecord = new CriminalRecord
                {
                    Status = record.SecurityStatus,
                    Reason = record.WantedReason,
                    History = record.CrimeHistory
                };
            }
        }

        state.FilterStatus = console.FilterStatus;

        _ui.SetUiState(uid, CriminalRecordsConsoleKey.Key, state);
    }

    private bool CheckSelected(Entity<CriminalRecordsConsoleComponent> ent, EntityUid user,
        [NotNullWhen(true)] out EntityUid? mob, [NotNullWhen(true)] out CrewRecord? record, [NotNullWhen(true)] out string? recordName)
    {
        record = null;
        recordName = null;
        mob = null;

        if (!_access.IsAllowed(user, ent))
        {
            _popup.PopupEntity(Loc.GetString("criminal-records-permission-denied"), ent, user);
            return false;
        }

        if (ent.Comp.ActiveKey is not { } id)
            return false;

        if (_station.GetOwningStation(ent) is not { } station)
            return false;

        if (!TryComp<CrewRecordsComponent>(station, out var crewRecords))
            return false;

        record = GetRecordFromKey(crewRecords, id, out recordName);
        if (record == null || recordName == null)
            return false;

        mob = user;
        return true;
    }
}
