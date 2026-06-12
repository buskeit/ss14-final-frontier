using Content.Server.Cargo.Components;
using Content.Server.Chat.Systems;
using Content.Server.CrewManifest;
using Content.Server.CrewRecords.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;
using Content.Shared.GridControl.Components;
using Content.Shared.Maps;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Collections;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Server.Station.Systems;

/// <summary>
/// System that manages stations.
/// A station is, by default, just a name, optional map prototype, and optional grids.
/// For jobs, look at StationJobSystem. For spawning, look at StationSpawningSystem.
/// </summary>
[PublicAPI]
public sealed partial class StationSystem : SharedStationSystem
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly CrewMetaRecordsSystem _metaRecords = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly CrewManifestSystem _crewManifest = default!;

    private ISawmill _sawmill = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private ValueList<MapId> _mapIds;
    private ValueList<(Box2Rotated Bounds, MapId MapId)> _gridBounds;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _stationGridSerializationSnapshots = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("station");

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRoundEnd);
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<StationDataComponent, ComponentStartup>(OnStationAdd);
        SubscribeLocalEvent<StationDataComponent, ComponentShutdown>(OnStationDeleted);
        SubscribeLocalEvent<StationMemberComponent, ComponentShutdown>(OnStationGridDeleted);
        SubscribeLocalEvent<StationMemberComponent, PostGridSplitEvent>(OnStationSplitEvent);
        SubscribeLocalEvent<StationMemberComponent, ComponentStartup>(OnStationMemberStartup);
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization);
        SubscribeLocalEvent<AfterSerializationEvent>(OnAfterSerialization);


        SubscribeLocalEvent<StationGridAddedEvent>(OnStationGridAdded);
        SubscribeLocalEvent<StationGridRemovedEvent>(OnStationGridRemoved);

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public int GetStationID(EntityUid station)
    {
        if (TryComp<StationDataComponent>(station, out var sD) && sD != null)
        {
            return sD.UID;
        }
        return 0;
    }
    public void ClockOutEmployees(EntityUid station)
    {
        var id = GetStationID(station);
        if (id == 0) return;
        var query = _entManager.AllEntityQueryEnumerator<JobNetComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.WorkingFor == id)
            {
                comp.WorkingFor = 0;
            }
        }
        _crewManifest.BuildCrewManifest(station);
    }
    public bool GetJobNetStatus(EntityUid station)
    {
        if (TryComp<StationDataComponent>(station, out var sD) && sD != null)
        {
            return sD.JobNetEnabled;
        }
        return false;
    }
    public void ResetSpending(string userName, EntityUid station)
    {
        if (TryComp<CrewRecordsComponent>(station, out var crewRecords) && crewRecords != null)
        {
            crewRecords.TryGetRecord(userName, out var crewRecord);
            if (crewRecord != null)
            {
                crewRecord.Spent = 0;
            }
        }
    }
    public void TrackSpending(string userName, EntityUid station, int toSpend)
    {
        if (TryComp<StationDataComponent>(station, out var sD) && sD != null)
        {
            if (sD.Owners.Contains(userName))
            {
                return;
            }
        }
        if (TryComp<CrewRecordsComponent>(station, out var crewRecords) && crewRecords != null)
        {
            crewRecords.TryGetRecord(userName, out var crewRecord);
            if (crewRecord != null)
            {
                crewRecord.Spent += toSpend;
            }
        }
    }


    public int GetPersonalTileCount(string realName)
    {
        var count = 0;
        var query = _entManager.AllEntityQueryEnumerator<PersonalMemberComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out var member, out var mapgrid))
        {
            if (member.OwnerName == realName)
            {
                var tiles = _mapSystem.GetAllTiles(gridUid, mapgrid).Count();
                count += tiles;
            }

        }
        return count;
    }

    public int GetStationTileCount(EntityUid uid)
    {
        var count = 0;
        var query = _entManager.AllEntityQueryEnumerator<StationMemberComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out var member, out var mapgrid))
        {
            if (member.Station == uid)
            {
                var tiles = _mapSystem.GetAllTiles(gridUid, mapgrid).Count();
                count += tiles;
            }

        }
        return count;
    }
    public EntityUid? GetStationTradeStation(EntityUid uid)
    {
        var query = _entManager.AllEntityQueryEnumerator<StationMemberComponent, TradeStationComponent>();
        while (query.MoveNext(out var gridUid, out var member, out var mapgrid))
        {
            if (member.Station == uid)
            {
                return gridUid;
            }

        }
        return null;
    }

    private void OnStationSplitEvent(EntityUid uid, StationMemberComponent component, ref PostGridSplitEvent args)
    {
        AddGridToStation(component.Station, args.Grid); // Add the new grid as a member.
    }

    private void OnStationGridDeleted(EntityUid uid, StationMemberComponent component, ComponentShutdown args)
    {
        var station = component.Station;
        if (!TryComp<StationDataComponent>(station, out var stationData))
            return;

        stationData.Grids.Remove(uid);
        Dirty(station, stationData);
    }

    private void OnBeforeSerialization(BeforeSerializationEvent ev)
    {
        _stationGridSerializationSnapshots.Clear();

        var query = EntityQueryEnumerator<StationDataComponent>();
        while (query.MoveNext(out var station, out var stationData))
        {
            if (stationData.Grids.Count == 0)
                continue;

            var serializableGrids = new HashSet<EntityUid>();
            var restoredGrids = new HashSet<EntityUid>();

            foreach (var grid in stationData.Grids)
            {
                if (!CanSaveStationGrid(grid, out var mapId))
                    continue;

                restoredGrids.Add(grid);

                if (ev.MapIds.Contains(mapId))
                    serializableGrids.Add(grid);
            }

            if (serializableGrids.SetEquals(stationData.Grids))
                continue;

            if (!restoredGrids.SetEquals(serializableGrids))
                _stationGridSerializationSnapshots[station] = restoredGrids;

            stationData.Grids.Clear();
            stationData.Grids.UnionWith(serializableGrids);
            Dirty(station, stationData);
        }
    }

    private void OnAfterSerialization(AfterSerializationEvent ev)
    {
        foreach (var (station, grids) in _stationGridSerializationSnapshots)
        {
            if (!TryComp<StationDataComponent>(station, out var stationData))
                continue;

            stationData.Grids.Clear();
            stationData.Grids.UnionWith(grids);
            Dirty(station, stationData);
        }

        _stationGridSerializationSnapshots.Clear();
    }

    private bool CanSaveStationGrid(EntityUid grid, out MapId mapId)
    {
        mapId = MapId.Nullspace;

        if (!grid.IsValid() || !Exists(grid) || TerminatingOrDeleted(grid))
            return false;

        if (!_gridQuery.HasComp(grid) || !_xformQuery.TryGetComponent(grid, out var xform))
            return false;

        mapId = xform.MapID;
        return true;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Connected)
        {
            RaiseNetworkEvent(new StationsUpdatedEvent(GetStationNames()), e.Session);
        }
    }

    private void UpdateTrackersOnGrid(EntityUid gridId, EntityUid? station)
    {
        var query = EntityQueryEnumerator<StationTrackerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tracker, out var xform))
        {
            if (xform.GridUid == gridId)
            {
                SetStation((uid, tracker), station);
            }
        }
    }

    #region Event handlers

    private void OnStationMemberStartup(EntityUid uid, StationMemberComponent component, ComponentStartup args)
    {
        if (component.StationUID != null)
        {
            var station = GetStationByID(component.StationUID.Value);
            if (station != null)
            {
                component.Station = station.Value;
            }
        }
    }

    private void OnStationAdd(EntityUid uid, StationDataComponent component, ComponentStartup args)
    {
        RaiseNetworkEvent(new StationsUpdatedEvent(GetStationNames()), Filter.Broadcast());

        if (component.StationName != null)
        {
            RenameStation(uid, component.StationName, false);

        }
        var metaData = MetaData(uid);
        RaiseLocalEvent(new StationInitializedEvent(uid));
        _sawmill.Info($"Set up station {metaData.EntityName} ({uid}).");
        _pvsOverride.AddGlobalOverride(uid);
        if (component.UID == 0)
        {
            var stations = EntityManager.AllComponents<StationDataComponent>();
            int tryUID = 1;
            while (component.UID == 0)
            {
                bool success = true;
                foreach (var station in stations)
                {
                    if (station.Component.UID == tryUID)
                    {
                        success = false;
                        tryUID++;
                        break;
                    }
                }
                if (success) component.UID = tryUID;
            }
        }

        _metaRecords.EnsureMetaRecordsAction((metaRecords) =>
        {
            metaRecords.Stations.TryAdd(component.UID, uid);
        });
    }

    private void OnStationDeleted(EntityUid uid, StationDataComponent component, ComponentShutdown args)
    {
        foreach (var grid in component.Grids)
        {
            RemComp<StationMemberComponent>(grid);

            // If the station gets deleted, we raise the event for every grid that was a part of it
            RaiseLocalEvent(new StationGridRemovedEvent(grid, uid));
        }

        _metaRecords.EnsureMetaRecordsAction((metaRecords) =>
        {
            metaRecords.Stations.Remove(component.UID);
        });

        RaiseNetworkEvent(new StationsUpdatedEvent(GetStationNames()), Filter.Broadcast());
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        InitializeStationsForLoadedMap(ev.GameMap, ev.Grids, ev.StationName, true);
        RepairStationGridOwnership(logDiagnostics: false, candidateGrids: ev.Grids);
    }

    public bool RestoreStationsAfterPersistenceLoad(
        GameMapPrototype gameMap,
        IReadOnlyList<EntityUid> grids,
        string? stationName = null)
    {
        _sawmill.Info("Restoring station controllers for persisted map {Map} from {GridCount} loaded grids.",
            gameMap.ID,
            grids.Count);
        InitializeStationsForLoadedMap(gameMap, grids, stationName, false);
        return RepairStationGridOwnership(logDiagnostics: true, candidateGrids: grids);
    }

    private void InitializeStationsForLoadedMap(
        GameMapPrototype gameMap,
        IReadOnlyList<EntityUid> grids,
        string? stationName,
        bool raisePostInit)
    {
        var dict = new Dictionary<string, List<EntityUid>>();

        // Iterate over all BecomesStation
        foreach (var grid in grids)
        {
            // We still setup the grid
            if (TryComp<BecomesStationComponent>(grid, out var becomesStation))
                dict.GetOrNew(becomesStation.Id).Add(grid);
        }

        if (!dict.Any())
        {
            // Oh jeez, no stations got loaded.
            // We'll yell about it, but the thing this used to do with creating a dummy is kinda pointless now.
            _sawmill.Error($"There were no station grids for {gameMap.ID}!");
        }

        foreach (var (id, gridIds) in dict)
        {
            StationConfig stationConfig;

            if (gameMap.Stations.ContainsKey(id))
                stationConfig = gameMap.Stations[id];
            else
            {
                _sawmill.Error($"The station {id} in map {gameMap.ID} does not have an associated station config!");
                continue;
            }

            var existingStation = FindExistingStationForGrids(gridIds);
            if (existingStation is { } station)
            {
                foreach (var grid in gridIds)
                {
                    if (GetOwningStation(grid) != station)
                        AddGridToStation(station, grid);
                }

                continue;
            }

            var stableStationId = GetPersistedStationId(gridIds);
            var restoredStationName = stationName;
            if (!raisePostInit && restoredStationName == null && gridIds.Count > 0)
                restoredStationName = Name(gridIds[0]);

            InitializeNewStationInternal(
                stationConfig,
                gridIds,
                restoredStationName,
                null,
                raisePostInit,
                stableStationId);
        }
    }

    private int? GetPersistedStationId(IEnumerable<EntityUid> grids)
    {
        int? stableStationId = null;

        foreach (var grid in grids)
        {
            if (!TryComp<StationMemberComponent>(grid, out var member) || member.StationUID is not { } candidate)
                continue;

            if (stableStationId == null)
            {
                stableStationId = candidate;
                continue;
            }

            if (stableStationId != candidate)
            {
                _sawmill.Warning(
                    "Loaded main station grids have conflicting stable station IDs {FirstStationId} and {SecondStationId}; a new station ID will be assigned.",
                    stableStationId,
                    candidate);
                return null;
            }
        }

        return stableStationId;
    }

    private EntityUid? FindExistingStationForGrids(IEnumerable<EntityUid> grids)
    {
        var candidates = new HashSet<EntityUid>();
        foreach (var grid in grids)
        {
            if (TryComp<StationMemberComponent>(grid, out var member))
            {
                if (member.Station.IsValid() && HasComp<StationDataComponent>(member.Station))
                    candidates.Add(member.Station);

                if (member.StationUID is { } stationId && GetStationByID(stationId) is { } stableStation)
                    candidates.Add(stableStation);
            }

            var stationQuery = EntityQueryEnumerator<StationDataComponent>();
            while (stationQuery.MoveNext(out var station, out var stationData))
            {
                if (stationData.Grids.Contains(grid))
                    candidates.Add(station);
            }
        }

        return candidates.Count == 1 ? candidates.First() : null;
    }

    /// <summary>
    /// Reconciles both sides of station/grid ownership after map deserialization.
    /// </summary>
    public bool RepairStationGridOwnership(
        EntityUid? focusGrid = null,
        bool logDiagnostics = true,
        IReadOnlyCollection<EntityUid>? candidateGrids = null)
    {
        var changed = false;
        var stations = new List<(EntityUid Uid, StationDataComponent Data)>();
        var stationsByStableId = new Dictionary<int, EntityUid>();
        var ambiguousStableIds = new HashSet<int>();
        var stationQuery = EntityQueryEnumerator<StationDataComponent>();

        while (stationQuery.MoveNext(out var station, out var stationData))
        {
            stations.Add((station, stationData));
            if (stationData.UID != 0 &&
                !ambiguousStableIds.Contains(stationData.UID) &&
                !stationsByStableId.TryAdd(stationData.UID, station))
            {
                var firstStation = stationsByStableId[stationData.UID];
                stationsByStableId.Remove(stationData.UID);
                ambiguousStableIds.Add(stationData.UID);
                _sawmill.Warning(
                    "Station ownership repair found duplicate stable station ID {StationId} on {FirstStation} and {SecondStation}.",
                    stationData.UID,
                    firstStation,
                    station);
            }

            if (logDiagnostics)
            {
                _sawmill.Info(
                    "Station ownership before repair: station={Station} name={StationName} hasJobs={HasJobs} hasSpawning={HasSpawning} grids=[{Grids}].",
                    station,
                    Name(station),
                    HasComp<StationJobsComponent>(station),
                    HasComp<StationSpawningComponent>(station),
                    string.Join(", ", stationData.Grids));
            }
        }

        var grids = new HashSet<EntityUid>();
        if (candidateGrids != null)
        {
            foreach (var grid in candidateGrids)
            {
                if (grid.IsValid() && !TerminatingOrDeleted(grid) && HasComp<MapGridComponent>(grid))
                    grids.Add(grid);
            }
        }
        else if (focusGrid is { } focused &&
                 focused.IsValid() &&
                 !TerminatingOrDeleted(focused) &&
                 HasComp<MapGridComponent>(focused))
        {
            grids.Add(focused);
        }
        else
        {
            var gridQuery = EntityQueryEnumerator<MapGridComponent>();
            while (gridQuery.MoveNext(out var grid, out _))
            {
                if (!TerminatingOrDeleted(grid))
                    grids.Add(grid);
            }
        }

        if (logDiagnostics)
        {
            foreach (var grid in grids)
            {
                var memberDescription = TryComp<StationMemberComponent>(grid, out var member)
                    ? $"station={member.Station}, stableStationId={member.StationUID?.ToString() ?? "none"}"
                    : "no station member";
                _sawmill.Info(
                    "Active loaded grid before station repair: grid={Grid} name={GridName} {MemberDescription}.",
                    grid,
                    Name(grid),
                    memberDescription);
            }
        }

        foreach (var (station, stationData) in stations)
        {
            var stationChanged = false;
            foreach (var grid in stationData.Grids.ToArray())
            {
                if (!grid.IsValid() || TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
                {
                    stationData.Grids.Remove(grid);
                    stationChanged = true;
                    continue;
                }

                if ((candidateGrids != null || focusGrid != null) && !grids.Contains(grid))
                    continue;

                if (TryComp<StationMemberComponent>(grid, out var member) &&
                    member.Station.IsValid() &&
                    member.Station != station &&
                    HasComp<StationDataComponent>(member.Station))
                {
                    stationData.Grids.Remove(grid);
                    stationChanged = true;
                    continue;
                }

                if (GetOwningStation(grid) != station)
                {
                    AddGridToStation(station, grid);
                    stationChanged = true;
                }
            }

            if (stationChanged)
            {
                Dirty(station, stationData);
                changed = true;
            }
        }

        // Stable station IDs survive map serialization even though station entity UIDs do not.
        foreach (var grid in grids)
        {
            if (!TryComp<StationMemberComponent>(grid, out var member))
                continue;

            EntityUid? station = null;
            if (member.Station.IsValid() && HasComp<StationDataComponent>(member.Station))
                station = member.Station;
            else if (member.StationUID is { } stationId &&
                     !ambiguousStableIds.Contains(stationId) &&
                     stationsByStableId.TryGetValue(stationId, out var stableStation))
                station = stableStation;

            if (station == null)
                continue;

            var stationData = Comp<StationDataComponent>(station.Value);
            if (member.Station != station || member.StationUID != stationData.UID || !stationData.Grids.Contains(grid))
            {
                AddGridToStation(station.Value, grid);
                changed = true;
            }
        }

        var spawnableStations = stations
            .Where(x => HasComp<StationJobsComponent>(x.Uid) && HasComp<StationSpawningComponent>(x.Uid))
            .Select(x => x.Uid)
            .ToList();
        var mainStationGrids = grids.Where(HasComp<BecomesStationComponent>).ToList();

        if (spawnableStations.Count == 1 &&
            mainStationGrids.Count == 1 &&
            GetOwningStation(mainStationGrids[0]) == null)
        {
            AddGridToStation(spawnableStations[0], mainStationGrids[0]);
            changed = true;
            _sawmill.Warning(
                "Station ownership repair associated the sole main station grid {Grid} with the sole spawnable station {Station}.",
                mainStationGrids[0],
                spawnableStations[0]);
        }

        if (focusGrid is { } targetGrid && logDiagnostics)
        {
            var owner = GetOwningStation(targetGrid);
            var inStationList = stations.Any(x => x.Data.Grids.Contains(targetGrid));
            _sawmill.Info(
                "Station ownership repair result for grid {Grid}: changed={Changed} inStationGridList={InStationGridList} owningStation={OwningStation}.",
                targetGrid,
                changed,
                inStationList,
                owner?.ToString() ?? "none");
        }

        return changed;
    }

    private void OnRoundEnd(GameRunLevelChangedEvent eventArgs)
    {
        if (eventArgs.New != GameRunLevel.PreRoundLobby)
            return;

        var query = EntityQueryEnumerator<StationDataComponent>();
        while (query.MoveNext(out var station, out _))
        {
            QueueDel(station);
        }
    }

    private void OnStationGridAdded(StationGridAddedEvent ev)
    {
        // When a grid is added to a station, update all trackers on that grid
        UpdateTrackersOnGrid(ev.GridId, ev.Station);
    }

    private void OnStationGridRemoved(StationGridRemovedEvent ev)
    {
        // When a grid is removed from a station, update all trackers on that grid to null
        UpdateTrackersOnGrid(ev.GridId, null);
    }

    #endregion Event handlers

    /// <summary>
    /// Tries to retrieve a filter for everything in the station the source is on.
    /// </summary>
    /// <param name="source">The entity to use to find the station.</param>
    /// <param name="range">The range around the station</param>
    /// <returns></returns>
    public Filter GetInOwningStation(EntityUid source, float range = 32f)
    {
        var station = GetOwningStation(source);

        if (TryComp<StationDataComponent>(station, out var data))
        {
            return GetInStation(data);
        }

        return Filter.Empty();
    }

    /// <summary>
    /// Retrieves a filter for everything in a particular station or near its member grids.
    /// </summary>
    public Filter GetInStation(StationDataComponent dataComponent, float range = 32f)
    {
        var filter = Filter.Empty();
        _mapIds.Clear();

        // First collect all valid map IDs where station grids exist
        foreach (var gridUid in dataComponent.Grids)
        {
            if (!_xformQuery.TryGetComponent(gridUid, out var xform))
                continue;

            var mapId = xform.MapID;
            if (!_mapIds.Contains(mapId))
                _mapIds.Add(mapId);
        }

        // Cache the rotated bounds for each grid
        _gridBounds.Clear();

        foreach (var gridUid in dataComponent.Grids)
        {
            if (!_gridQuery.TryComp(gridUid, out var grid) ||
                !_xformQuery.TryGetComponent(gridUid, out var gridXform))
            {
                continue;
            }

            var (worldPos, worldRot) = _transform.GetWorldPositionRotation(gridXform);
            var localBounds = grid.LocalAABB.Enlarged(range);

            // Create a rotated box using the grid's transform
            var rotatedBounds = new Box2Rotated(
                localBounds,
                worldRot,
                worldPos);

            _gridBounds.Add((rotatedBounds, gridXform.MapID));
        }

        foreach (var session in Filter.GetAllPlayers(_player))
        {
            var entity = session.AttachedEntity;
            if (entity == null || !_xformQuery.TryGetComponent(entity, out var xform))
                continue;

            var mapId = xform.MapID;

            if (!_mapIds.Contains(mapId))
                continue;

            // Check if the player is directly on any station grid
            var gridUid = xform.GridUid;
            if (gridUid != null && dataComponent.Grids.Contains(gridUid.Value))
            {
                filter.AddPlayer(session);
                continue;
            }

            // If not directly on a grid, check against cached rotated bounds
            var position = _transform.GetWorldPosition(xform);

            foreach (var (bounds, boundsMapId) in _gridBounds)
            {
                // Skip bounds on different maps
                if (boundsMapId != mapId)
                    continue;

                if (!bounds.Contains(position))
                    continue;

                filter.AddPlayer(session);
                break;
            }
        }

        return filter;
    }

    /// <summary>
    /// Initializes a new station with the given information.
    /// </summary>
    /// <param name="stationConfig">The game map prototype used, if any.</param>
    /// <param name="gridIds">All grids that should be added to the station.</param>
    /// <param name="name">Optional override for the station name.</param>
    /// <remarks>This is for ease of use, manually spawning the entity works just fine.</remarks>
    /// <returns>The initialized station.</returns>
    public EntityUid InitializeNewStation(
        StationConfig stationConfig,
        IEnumerable<EntityUid>? gridIds,
        string? name = null,
        string? owner = null)
    {
        return InitializeNewStationInternal(stationConfig, gridIds, name, owner, true, null);
    }

    private EntityUid InitializeNewStationInternal(
        StationConfig stationConfig,
        IEnumerable<EntityUid>? gridIds,
        string? name,
        string? owner,
        bool raisePostInit,
        int? stableStationId)
    {
        // Use overrides for setup.
        var station = EntityManager.SpawnEntity(stationConfig.StationPrototype, MapCoordinates.Nullspace, stationConfig.StationComponentOverrides);
        DebugTools.Assert(HasComp<StationDataComponent>(station), "Stations should have StationData in their prototype.");

        var data = Comp<StationDataComponent>(station);
        if (stableStationId is { } persistedId && persistedId > 0 && data.UID != persistedId)
        {
            var conflictingStation = GetStationByID(persistedId);
            if (conflictingStation is null || conflictingStation == station)
            {
                var generatedId = data.UID;
                data.UID = persistedId;
                Dirty(station, data);
                _metaRecords.EnsureMetaRecordsAction(metaRecords =>
                {
                    if (metaRecords.Stations.TryGetValue(generatedId, out var registered) && registered == station)
                        metaRecords.Stations.Remove(generatedId);

                    metaRecords.Stations[persistedId] = station;
                });
                _sawmill.Info(
                    "Restored stable station ID {StationId} on station {Station} (replacing generated ID {GeneratedId}).",
                    persistedId,
                    station,
                    generatedId);
            }
            else
            {
                _sawmill.Warning(
                    "Could not restore stable station ID {StationId} on station {Station}; it is already used by station {ConflictingStation}.",
                    persistedId,
                    station,
                    conflictingStation);
            }
        }

        if (name is not null && data.StationName is null)
            RenameStation(station, name, false);
        if (owner is not null)
            data.AddOwner(owner);

        name ??= MetaData(station).EntityName;

        foreach (var grid in gridIds ?? Array.Empty<EntityUid>())
        {
            AddGridToStation(station, grid, null, data, name);
        }

        if (raisePostInit)
        {
            var ev = new StationPostInitEvent((station, data));
            RaiseLocalEvent(station, ref ev, true);
        }

        return station;
    }

    /// <summary>
    /// Adds the given grid to a station.
    /// </summary>
    /// <param name="mapGrid">Grid to attach.</param>
    /// <param name="station">Station to attach the grid to.</param>
    /// <param name="gridComponent">Resolve pattern, grid component of mapGrid.</param>
    /// <param name="stationData">Resolve pattern, station data component of station.</param>
    /// <param name="name">The name to assign to the grid if any.</param>
    /// <exception cref="ArgumentException">Thrown when mapGrid or station are not a grid or station, respectively.</exception>
    public void AddGridToStation(EntityUid station, EntityUid mapGrid, MapGridComponent? gridComponent = null, StationDataComponent? stationData = null, string? name = null)
    {
        if (!Resolve(mapGrid, ref gridComponent))
            throw new ArgumentException("Tried to initialize a station on a non-grid entity!", nameof(mapGrid));
        if (!Resolve(station, ref stationData))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        if (!string.IsNullOrEmpty(name))
            _metaData.SetEntityName(mapGrid, name);

        var stationMember = EnsureComp<StationMemberComponent>(mapGrid);
        stationMember.Station = station;
        stationMember.StationUID = stationData.UID;
        stationData.Grids.Add(mapGrid);
        Dirty(station, stationData);
        Dirty(mapGrid, stationMember);

        RaiseLocalEvent(station, new StationGridAddedEvent(mapGrid, station, false), true);

        _sawmill.Info($"Adding grid {mapGrid} to station {Name(station)} ({station})");
        OnGridClaimed(mapGrid);
    }

    public void AddGridToPerson(string owner, EntityUid mapGrid, MapGridComponent? gridComponent = null, StationDataComponent? stationData = null, string? name = null)
    {
        if (!Resolve(mapGrid, ref gridComponent))
            throw new ArgumentException("Tried to initialize a station on a non-grid entity!", nameof(mapGrid));

        if (!string.IsNullOrEmpty(name))
            _metaData.SetEntityName(mapGrid, name);

        var stationMember = EnsureComp<PersonalMemberComponent>(mapGrid);
        stationMember.OwnerName = owner;

        OnGridClaimed(mapGrid);
    }

    private void OnGridClaimed(EntityUid mapGrid)
    {
        RemComp<OwnedDebrisComponent>(mapGrid);
    }


    /// <summary>
    /// Removes the given grid from a station.
    /// </summary>
    /// <param name="station">Station to remove the grid from.</param>
    /// <param name="mapGrid">Grid to remove</param>
    /// <param name="gridComponent">Resolve pattern, grid component of mapGrid.</param>
    /// <param name="stationData">Resolve pattern, station data component of station.</param>
    /// <exception cref="ArgumentException">Thrown when mapGrid or station are not a grid or station, respectively.</exception>
    public void RemoveGridFromStation(EntityUid station, EntityUid mapGrid, MapGridComponent? gridComponent = null, StationDataComponent? stationData = null)
    {
        if (!Resolve(mapGrid, ref gridComponent))
            throw new ArgumentException("Tried to initialize a station on a non-grid entity!", nameof(mapGrid));
        if (!Resolve(station, ref stationData))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        RemComp<StationMemberComponent>(mapGrid);
        stationData.Grids.Remove(mapGrid);
        Dirty(station, stationData);

        RaiseLocalEvent(station, new StationGridRemovedEvent(mapGrid, station), true);
        _sawmill.Info($"Removing grid {mapGrid} from station {Name(station)} ({station})");
    }

    public void RemoveGridFromPerson(EntityUid mapGrid, MapGridComponent? gridComponent = null, StationDataComponent? stationData = null)
    {
        if (!Resolve(mapGrid, ref gridComponent))
            throw new ArgumentException("Tried to initialize a station on a non-grid entity!", nameof(mapGrid));

        RemComp<PersonalMemberComponent>(mapGrid);
    }

    /// <summary>
    /// Renames the given station.
    /// </summary>
    /// <param name="station">Station to rename.</param>
    /// <param name="name">The new name to apply.</param>
    /// <param name="loud">Whether or not to announce the rename.</param>
    /// <param name="stationData">Resolve pattern, station data component of station.</param>
    /// <param name="metaData">Resolve pattern, metadata component of station.</param>
    /// <exception cref="ArgumentException">Thrown when the given station is not a station.</exception>
    public void RenameStation(EntityUid station, string name, bool loud = true, StationDataComponent? stationData = null, MetaDataComponent? metaData = null)
    {
        if (!Resolve(station, ref stationData, ref metaData))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        var oldName = metaData.EntityName;
        _metaData.SetEntityName(station, name, metaData);
        if (TryComp<StationDataComponent>(station, out var data))
        {
            data.StationName = name;
        }
        if (loud)
        {
            _chatSystem.DispatchStationAnnouncement(station, $"The station {oldName} has been renamed to {name}.");
        }

        RaiseLocalEvent(station, new StationRenamedEvent(oldName, name), true);
    }

    /// <summary>
    /// Deletes the given station.
    /// </summary>
    /// <param name="station">Station to delete.</param>
    /// <param name="stationData">Resolve pattern, station data component of station.</param>
    /// <exception cref="ArgumentException">Thrown when the given station is not a station.</exception>
    public void DeleteStation(EntityUid station, StationDataComponent? stationData = null)
    {
        if (!Resolve(station, ref stationData))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        QueueDel(station);
    }
}

/// <summary>
/// Broadcast event fired when a station is first set up.
/// This is the ideal point to add components to it.
/// </summary>
[PublicAPI]
public sealed class StationInitializedEvent : EntityEventArgs
{
    /// <summary>
    /// Station this event is for.
    /// </summary>
    public EntityUid Station;

    public StationInitializedEvent(EntityUid station)
    {
        Station = station;
    }
}

/// <summary>
/// Directed event fired on a station when a grid becomes a member of the station.
/// </summary>
[PublicAPI]
public sealed class StationGridAddedEvent : EntityEventArgs
{
    /// <summary>
    /// ID of the grid added to the station.
    /// </summary>
    public EntityUid GridId;

    /// <summary>
    /// EntityUid of the station this grid was added to.
    /// </summary>
    public EntityUid Station;

    /// <summary>
    /// Indicates that the event was fired during station setup,
    /// so that it can be ignored if StationInitializedEvent was already handled.
    /// </summary>
    public bool IsSetup;

    public StationGridAddedEvent(EntityUid gridId, EntityUid station, bool isSetup)
    {
        GridId = gridId;
        Station = station;
        IsSetup = isSetup;
    }
}

/// <summary>
/// Directed event fired on a station when a grid is no longer a member of the station.
/// </summary>
[PublicAPI]
public sealed class StationGridRemovedEvent : EntityEventArgs
{
    /// <summary>
    /// ID of the grid removed from the station.
    /// </summary>
    public EntityUid GridId;

    /// <summary>
    /// EntityUid of the station this grid was added to.
    /// </summary>
    public EntityUid Station;

    public StationGridRemovedEvent(EntityUid gridId, EntityUid station)
    {
        GridId = gridId;
        Station = station;
    }
}

/// <summary>
/// Directed event fired on a station when it is renamed.
/// </summary>
[PublicAPI]
public sealed class StationRenamedEvent : EntityEventArgs
{
    /// <summary>
    /// Prior name of the station.
    /// </summary>
    public string OldName;

    /// <summary>
    /// New name of the station.
    /// </summary>
    public string NewName;

    public StationRenamedEvent(string oldName, string newName)
    {
        OldName = oldName;
        NewName = newName;
    }
}

