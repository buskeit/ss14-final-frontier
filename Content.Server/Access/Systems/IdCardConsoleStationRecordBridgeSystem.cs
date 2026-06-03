using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using System.Linq;
using static Content.Shared.Access.Components.IdCardConsoleComponent;

namespace Content.Server.Access.Systems;

/// <summary>
/// Bridges persistent crew records into vanilla station records before an ID is registered.
/// </summary>
public sealed class IdCardConsoleStationRecordBridgeSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdCardConsoleComponent, RegisterTargetIdMessage>(OnRegisterTargetIdMessage,
            before: [typeof(IdCardConsoleSystem)]);
    }

    private void OnRegisterTargetIdMessage(EntityUid uid, IdCardConsoleComponent component, RegisterTargetIdMessage args)
    {
        if (args.Actor is not { Valid: true })
            return;

        if (component.TargetIdSlot.Item is not { Valid: true } targetId)
            return;

        if (component.SelectedRecord == null)
            return;

        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId)
            return;

        if (!PrivilegedIdCanManageIds(uid, privilegedId))
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null)
            return;

        if (!TryComp<IdCardComponent>(targetId, out var targetCard) ||
            !string.IsNullOrWhiteSpace(targetCard.FullName))
        {
            return;
        }

        if (_records.GetRecordByName(station.Value, component.SelectedRecord.Name) != null)
            return;

        TryComp(station.Value, out CrewAssignmentsComponent? assignments);
        var record = BuildGeneralStationRecord(component.SelectedRecord, assignments);
        var key = _records.AddRecordEntry(station.Value, record);
        if (!key.IsValid())
        {
            Log.Warning($"Failed to create station record entry for persistent crew record {component.SelectedRecord.Name}");
            return;
        }

        _records.Synchronize(key);
    }

    private bool PrivilegedIdCanManageIds(EntityUid console, EntityUid id)
    {
        return PrivilegedIdHasAnyAccess(console, id, "Command", "HeadOfPersonnel");
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

    private bool TryGetVerifiedCrewRecordForId(
        EntityUid console,
        EntityUid id,
        out CrewRecord? record,
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

        if (!_records.TryGetRecord<GeneralStationRecord>(key, out var generalRecord) ||
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

        return _records.TryGetRecord<GeneralStationRecord>(key, out var generalRecord) &&
               generalRecord.Name == idCard.FullName;
    }

    private static GeneralStationRecord BuildGeneralStationRecord(CrewRecord crewRecord, CrewAssignmentsComponent? assignments)
    {
        CrewAssignment? assignment = null;
        assignments?.TryGetAssignment(crewRecord.AssignmentID, out assignment);

        return new GeneralStationRecord
        {
            Name = crewRecord.Name,
            JobTitle = assignment?.Name ?? string.Empty,
            DisplayPriority = assignment?.Clevel ?? 0,
        };
    }
}
