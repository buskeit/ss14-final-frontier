using Content.Server._NF.Bank;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Systems;
using Content.Server.CrewRecords.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Speech.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.ContentPack;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using System.Globalization;
using System.Linq;
using System.Numerics;


namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        private static readonly SerializationOptions PersistentCharacterSaveOptions = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore
        };

        private static readonly DeserializationOptions PersistentCharacterLoadOptions = DeserializationOptions.Default with
        {
            LogInvalidEntities = false
        };

        [Dependency] private readonly SharedContainerSystem _container = default!;
        [Dependency] private readonly IAdminManager _adminManager = default!;
        [Dependency] private readonly SharedJobSystem _jobs = default!;
        [Dependency] private readonly AdminSystem _admin = default!;
        [Dependency] private readonly IEntityManager _ent = default!;
        [Dependency] private readonly BankSystem _bankSystem = default!;
        [Dependency] private readonly CrewMetaRecordsSystem _crewMetaRecords = default!;
        [Dependency] private readonly MobStateSystem _mobState = default!;
        public static readonly EntProtoId ObserverPrototypeName = "MobObserver";
        public static readonly EntProtoId AdminObserverPrototypeName = "AdminObserver";

        /// <summary>
        /// How many players have joined the round through normal methods.
        /// Useful for game rules to look at. Doesn't count observers, people in lobby, etc.
        /// </summary>
        public int PlayersJoinedRoundNormally;

        // Mainly to avoid allocations.
        private readonly List<EntityCoordinates> _possiblePositions = new();

        private List<EntityUid> GetSpawnableStations()
        {
            var spawnableStations = new List<EntityUid>();
            var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
            while (query.MoveNext(out var uid, out _, out _))
            {
                spawnableStations.Add(uid);
            }

            return spawnableStations;
        }

        private void SpawnPlayers(List<ICommonSession> readyPlayers,
            Dictionary<NetUserId, HumanoidCharacterProfile> profiles,
            bool force)
        {
            // Allow game rules to spawn players by themselves if needed. (For example, nuke ops or wizard)
            RaiseLocalEvent(new RulePlayerSpawningEvent(readyPlayers, profiles, force));

            var playerNetIds = readyPlayers.Select(o => o.UserId).ToHashSet();

            // RulePlayerSpawning feeds a readonlydictionary of profiles.
            // We need to take these players out of the pool of players available as they've been used.
            if (readyPlayers.Count != profiles.Count)
            {
                var toRemove = new RemQueue<NetUserId>();

                foreach (var (player, _) in profiles)
                {
                    if (playerNetIds.Contains(player))
                        continue;

                    toRemove.Add(player);
                }

                foreach (var player in toRemove)
                {
                    profiles.Remove(player);
                }
            }

            var spawnableStations = GetSpawnableStations();
            var assignedJobs = _stationJobs.AssignJobs(profiles, spawnableStations);

            _stationJobs.AssignOverflowJobs(ref assignedJobs, playerNetIds, profiles, spawnableStations);

            // Calculate extended access for stations.
            var stationJobCounts = spawnableStations.ToDictionary(e => e, _ => 0);
            foreach (var (netUser, (job, station)) in assignedJobs)
            {
                if (job == null)
                {
                    var playerSession = _playerManager.GetSessionById(netUser);
                    var evNoJobs = new NoJobsAvailableSpawningEvent(playerSession); // Used by gamerules to wipe their antag slot, if they got one
                    RaiseLocalEvent(evNoJobs);

                    _chatManager.DispatchServerMessage(playerSession, Loc.GetString("job-not-available-wait-in-lobby"));
                }
                else
                {
                    stationJobCounts[station] += 1;
                }
            }

            _stationJobs.CalcExtendedAccess(stationJobCounts);

            // Spawn everybody in!
            foreach (var (player, (job, station)) in assignedJobs)
            {
                if (job == null)
                    continue;

                SpawnPlayer(_playerManager.GetSessionById(player), profiles[player], station, job, false);
            }

            RefreshLateJoinAllowed();

            // Allow rules to add roles to players who have been spawned in. (For example, on-station traitors)
            RaiseLocalEvent(new RulePlayerJobsAssignedEvent(
                assignedJobs.Keys.Select(x => _playerManager.GetSessionById(x)).ToArray(),
                profiles,
                force));
        }

        private void SpawnPlayer(ICommonSession player,
            EntityUid station,
            string? jobId = null,
            bool lateJoin = true,
            bool silent = false)
        {
            var character = GetPlayerProfile(player);
            if (character == null)
            {
                if (!LobbyEnabled)
                    JoinAsObserver(player);
                else
                    _chatManager.DispatchServerMessage(player, "Select a character before joining the game.");

                return;
            }

            var jobBans = _banManager.GetJobBans(player.UserId);
            if (jobBans == null || jobId != null && jobBans.Contains(jobId)) //TODO: use IsRoleBanned directly?
                return;

            if (jobId != null)
            {
                var jobs = new List<ProtoId<JobPrototype>> { jobId };
                var ev = new IsRoleAllowedEvent(player, jobs, null);
                RaiseLocalEvent(ref ev);
                if (ev.Cancelled)
                    return;
            }

            SpawnPlayer(player, character, station, jobId, lateJoin, silent);
        }


        private void SpawnPlayerPersistent(ICommonSession player)
        {
            _sawmill.Debug("SpawnPlayerPersistent called for player {PlayerName}. DummyTicker: {DummyTicker}", player.Name, DummyTicker);
            // Can't spawn players with a dummy ticker!
            if (DummyTicker)
                return;

            var rejoined = TryRejoin(player);
            _sawmill.Debug("TryRejoin result for player {PlayerName}: {Rejoined}", player.Name, rejoined);
            if (rejoined)
                return;

            var character = GetPersistentSelectedProfile(player);
            _sawmill.Debug("GetPersistentSelectedProfile result for player {PlayerName}: {CharacterExists}", player.Name, character != null);
            if (character == null)
            {
                PlayerJoinLobby(player, forceCharacterSetup: true);
                return;
            }

            var data = player.ContentData();
            DebugTools.AssertNotNull(data);
            if (data != null && TryAttachPersistentBody(player, character, data.UserId))
            {
                _sawmill.Debug("Saved-body load success for {PlayerName}", player.Name);
                return;
            }

            _sawmill.Warning(
                "Fresh-spawn fallback chosen for {PlayerName}; no safe persistent body was attached.",
                player.Name);

            var stations = GetSpawnableStations();
            _sawmill.Debug("Spawnable station count: {Count}", stations.Count);

            if (stations.Count == 0)
            {
                var mapName = "Unknown";
                var mapUidStr = "None";
                if (_map.TryGetMap(DefaultMap, out var mapUid))
                {
                    mapUidStr = mapUid.Value.ToString();
                    if (TryComp(mapUid.Value, out MetaDataComponent? mapMeta))
                    {
                        mapName = mapMeta.EntityName;
                    }
                }

                var stationsDetail = new List<string>();
                var debugQuery = EntityQueryEnumerator<StationJobsComponent>();
                var debugStationCount = 0;
                while (debugQuery.MoveNext(out var stationUid, out var stationJobs))
                {
                    debugStationCount++;
                    var hasSpawning = HasComp<StationSpawningComponent>(stationUid);
                    var meta = MetaData(stationUid);
                    stationsDetail.Add($"Station {stationUid} ('{meta.EntityName}'): HasJobs={stationJobs != null}, HasSpawning={hasSpawning}, TerminatingOrDeleted={TerminatingOrDeleted(stationUid)}");
                }

                _sawmill.Warning("Could not persistently spawn {Player}: no spawnable stations are available. MapName: {MapName}, MapId: {MapId}, StationCount: {StationCount}, StationsDetail: [{StationsDetail}]. Falling back to fresh spawn on EntityUid.Invalid.",
                    player.Name,
                    mapName,
                    mapUidStr,
                    debugStationCount,
                    string.Join("; ", stationsDetail));

                SpawnPlayer(player, character, EntityUid.Invalid, null, true, true);
            }
            else
            {
                _robustRandom.Shuffle(stations);
                var selectedStation = stations[0];
                _sawmill.Debug("Selected station for {PlayerName}: {Station}", player.Name, Name(selectedStation));
                SpawnPlayer(player, character, selectedStation, null, true, true);
            }

            if (player.AttachedEntity is not { } mob)
            {
                _sawmill.Error("Fresh spawn fallback failed to attach entity for player {PlayerName}; spawn is not possible.", player.Name);
                return;
            }

            if (data == null)
                return;

            UpdatePersistentLocationComponent(mob);

            var saveFilePath = PersistentCharacterSavePath.ForPlayer(data.UserId);
            _loader.TrySaveGeneric(mob, saveFilePath, out _, PersistentCharacterSaveOptions);
        }

        private void SpawnPlayerPersistentLoad(ICommonSession player)
        {
            SpawnPlayerPersistent(player);
        }

        private bool TryAttachPersistentBody(ICommonSession player, HumanoidCharacterProfile character, NetUserId userId)
        {
            var saveFilePath = PersistentCharacterSavePath.ForPlayer(userId);
            var rootedPath = saveFilePath.ToRootedPath();
            if (!_resourceManager.UserData.Exists(rootedPath))
                return false;

            if (!_loader.TryLoadEntity(saveFilePath, out var mobMaybe, PersistentCharacterLoadOptions) || mobMaybe == null)
            {
                _sawmill.Warning(
                    "Persistent character load failed for {Player} at {Path}; fresh-spawn fallback is required.",
                    player.Name,
                    saveFilePath);
                return false;
            }

            var mob = mobMaybe.Value.Owner;

            // Reposition / repair body onto the restored map/grid if possible
            if (_ent.TryGetComponent<PersistentLocationComponent>(mob, out var locComp) && !string.IsNullOrEmpty(locComp.GridName))
            {
                EntityUid? targetGrid = null;
                var gridQuery = _ent.EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
                while (gridQuery.MoveNext(out var gUid, out _, out var meta))
                {
                    if (meta.EntityName == locComp.GridName)
                    {
                        targetGrid = gUid;
                        break;
                    }
                }

                if (targetGrid != null)
                {
                    _transform.SetParent(mob, targetGrid.Value);
                    _transform.SetLocalPosition(mob, locComp.LocalPosition);
                    _sawmill.Info("Restored persistent body {Entity} for player {Player} to saved grid {GridName} at {Position}",
                        ToPrettyString(mob), player.Name, locComp.GridName, locComp.LocalPosition);
                }
                else
                {
                    _sawmill.Warning("Could not find grid named {GridName} to restore persistent body {Entity} for player {Player}.",
                        locComp.GridName, ToPrettyString(mob), player.Name);
                }
            }

            // Fallback for missing/invalid grid (legacy or deleted grids)
            if (_ent.TryGetComponent<TransformComponent>(mob, out var xform))
            {
                if (xform.GridUid == null || xform.GridUid == EntityUid.Invalid || TerminatingOrDeleted(xform.GridUid.Value) ||
                    xform.MapUid == null || xform.MapUid == EntityUid.Invalid || TerminatingOrDeleted(xform.MapUid.Value))
                {
                    var activeGrids = _mapManager.GetAllMapGrids(DefaultMap).ToList();
                    if (activeGrids.Any())
                    {
                        var fallbackGrid = activeGrids[0].Owner;
                        _transform.SetParent(mob, fallbackGrid);
                        _transform.SetLocalPosition(mob, Vector2.Zero);
                        _sawmill.Info("Repositioned persistent body {Entity} for player {Player} onto fallback grid {Grid} because saved grid was missing or invalid.",
                            ToPrettyString(mob), player.Name, fallbackGrid);
                    }
                }
            }

            if (!TryValidatePersistentBody(mob, out var station, out var reason))
            {
                // Detailed diagnostics for validation failure
                string mapName = "Unknown";
                EntityUid mapEnt = EntityUid.Invalid;
                if (_map.TryGetMap(DefaultMap, out var mapUid))
                {
                    mapEnt = mapUid.Value;
                    if (TryComp<MetaDataComponent>(mapEnt, out var mapMeta))
                        mapName = mapMeta.EntityName;
                }

                EntityUid savedMapUid = EntityUid.Invalid;
                EntityUid savedGridUid = EntityUid.Invalid;
                bool savedMapExists = false;
                bool savedGridExists = false;
                if (_ent.TryGetComponent<TransformComponent>(mob, out var trans))
                {
                    if (trans.MapUid != null)
                    {
                        savedMapUid = trans.MapUid.Value;
                        savedMapExists = savedMapUid.IsValid() && !TerminatingOrDeleted(savedMapUid);
                    }
                    if (trans.GridUid != null)
                    {
                        savedGridUid = trans.GridUid.Value;
                        savedGridExists = savedGridUid.IsValid() && !TerminatingOrDeleted(savedGridUid);
                    }
                }

                var currentWorldExists = _resourceManager.UserData.Exists(new ResPath("/current"));

                _sawmill.Warning(
                    "Persistent character hard body validation failure for {Player}. " +
                    "Reason: {Reason}. " +
                    "Saved Body Path: {Path}. " +
                    "Loaded Map ID/Name: {MapId} ('{MapName}' / {MapEnt}). " +
                    "Transform Map UID: {SavedMapUid} (exists={SavedMapExists}). " +
                    "Transform Grid UID: {SavedGridUid} (exists={SavedGridExists}). " +
                    "Is 'current' world save present: {CurrentWorldExists}. " +
                    "Fresh-spawn fallback is required.",
                    player.Name,
                    reason,
                    saveFilePath,
                    DefaultMap, mapName, mapEnt,
                    savedMapUid, savedMapExists,
                    savedGridUid, savedGridExists,
                    currentWorldExists);

                CleanupRejectedPersistentBody(player, mob);
                return false;
            }

            try
            {
                const string jobId = "Passenger";
                var jobPrototype = _prototypeManager.Index<JobPrototype>(jobId);

                if (_ent.TryGetComponent<MindContainerComponent>(mob, out _))
                    _mind.WipeMind(mob);

                var newMind = _mind.CreateMind(userId, character.Name);
                _mind.SetUserId(newMind, userId);
                _mind.TransferTo(newMind, mob);

                if (player.AttachedEntity != mob)
                {
                    _sawmill.Warning(
                        "Persistent body attachment failed for {Player}: {Entity} was not attached after mind transfer; fresh-spawn fallback is required.",
                        player.Name,
                        ToPrettyString(mob));
                    CleanupRejectedPersistentBody(player, mob);
                    return false;
                }

                _roles.MindAddJobRole(newMind, silent: true, jobPrototype: jobId);
                var jobName = _jobs.MindTryGetJobName(newMind);
                PlayerJoinGame(player, true);
                _playTimeTrackings.PlayerRolesChanged(player);
                _bankSystem.EnsureAccount(character.Name, 50);
                if (_crewMetaRecords.MetaRecords != null)
                    _crewMetaRecords.MetaRecords.CreateRecord(character.Name, out _);

                _admin.UpdatePlayerList(player);

                var hasStation = station.IsValid() && !TerminatingOrDeleted(station);
                if (hasStation)
                {
                    _stationJobs.TryAssignJob(station, jobPrototype, player.UserId);
                    _adminLogger.Add(LogType.LateJoin,
                        LogImpact.Medium,
                        $"Player {player.Name} rejoined persistent character {character.Name:characterName} on station {Name(station):stationName} with {ToPrettyString(mob):entity} as a {jobName:jobName}.");
                }
                else
                {
                    _sawmill.Warning(
                        "Safe persistent body {Entity} attached for {Player} without an owning spawnable station; station job assignment was skipped.",
                        ToPrettyString(mob),
                        player.Name);
                    _adminLogger.Add(LogType.LateJoin,
                        LogImpact.Medium,
                        $"Player {player.Name} rejoined persistent character {character.Name:characterName} without an owning station with {ToPrettyString(mob):entity} as a {jobName:jobName}.");
                }

                PlayersJoinedRoundNormally++;
                var aev = new PlayerSpawnCompleteEvent(mob,
                    player,
                    jobId,
                    true,
                    true,
                    PlayersJoinedRoundNormally,
                    station,
                    character);
                RaiseLocalEvent(mob, aev, true);
                _sawmill.Debug("Saved-body load success for {PlayerName}", player.Name);
                return true;
            }
            catch (Exception ex)
            {
                _sawmill.Warning(
                    "Persistent body attachment failed for {Player}: {Message}. Fresh-spawn fallback is required.",
                    player.Name,
                    ex.Message);
                CleanupRejectedPersistentBody(player, mob);
                return false;
            }
        }

        private bool TryValidatePersistentBody(EntityUid mob, out EntityUid station, out string reason)
        {
            station = EntityUid.Invalid;
            reason = string.Empty;

            if (!mob.IsValid() || TerminatingOrDeleted(mob))
            {
                reason = "entity does not exist or is deleting";
                return false;
            }

            if (!_ent.TryGetComponent<TransformComponent>(mob, out var transform))
            {
                reason = "entity has no transform";
                return false;
            }

            if (transform.MapUid is not { } mapUid ||
                mapUid == EntityUid.Invalid ||
                TerminatingOrDeleted(mapUid))
            {
                reason = "entity is not on a valid map";
                return false;
            }

            if (transform.GridUid is not { } gridUid ||
                gridUid == EntityUid.Invalid ||
                TerminatingOrDeleted(gridUid))
            {
                reason = "entity is not on a valid grid";
                return false;
            }

            if (TryComp<ActorComponent>(mob, out _))
            {
                reason = "entity is already attached to another session";
                return false;
            }

            if (!TryComp<MobStateComponent>(mob, out var mobState))
            {
                reason = "entity has no mob state";
                return false;
            }

            if (!_mobState.IsAlive(mob, mobState) || _mobState.IsCritical(mob, mobState))
            {
                reason = "entity is dead or critical";
                return false;
            }

            var owningStation = _station.GetOwningStation(mob, transform);
            if (owningStation == null || TerminatingOrDeleted(owningStation.Value))
            {
                station = EntityUid.Invalid;
                _sawmill.Warning(
                    "Persistent body {Entity} is on valid map {Map} and grid {Grid}, but station ownership could not be resolved; continuing attachment without an owning station.",
                    ToPrettyString(mob),
                    mapUid,
                    gridUid);
                return true;
            }

            if (!HasComp<StationJobsComponent>(owningStation.Value) ||
                !HasComp<StationSpawningComponent>(owningStation.Value))
            {
                station = EntityUid.Invalid;
                _sawmill.Warning(
                    "Persistent body {Entity} is on valid map {Map} and grid {Grid}, but owning station {Station} is not spawnable; continuing attachment without an owning station.",
                    ToPrettyString(mob),
                    mapUid,
                    gridUid,
                    owningStation.Value);
                return true;
            }

            station = owningStation.Value;
            return true;
        }

        private void CleanupRejectedPersistentBody(ICommonSession player, EntityUid mob)
        {
            if (!mob.IsValid() || TerminatingOrDeleted(mob))
                return;

            if (player.AttachedEntity == mob)
                _playerManager.SetAttachedEntity(player, null, true);

            if (_ent.TryGetComponent<MindContainerComponent>(mob, out _))
                _mind.WipeMind(mob);

            _ent.QueueDeleteEntity(mob);
        }





        private void SpawnPlayer(ICommonSession player,
            HumanoidCharacterProfile character,
            EntityUid station,
            string? jobId = null,
            bool lateJoin = true,
            bool silent = false)
        {
            // Can't spawn players with a dummy ticker!
            if (DummyTicker)
                return;

            if (station == EntityUid.Invalid)
            {
                var stations = GetSpawnableStations();
                _robustRandom.Shuffle(stations);
                if (stations.Count == 0)
                    station = EntityUid.Invalid;
                else
                    station = stations[0];
            }

            if (lateJoin && DisallowLateJoin)
            {
                JoinAsObserver(player);
                return;
            }

            string speciesId;
            if (_randomizeCharacters)
            {
                var weightId = _cfg.GetCVar(CCVars.ICRandomSpeciesWeights);

                // If blank, choose a round start species.
                if (string.IsNullOrEmpty(weightId))
                {
                    var roundStart = new List<ProtoId<SpeciesPrototype>>();

                    var speciesPrototypes = _prototypeManager.EnumeratePrototypes<SpeciesPrototype>();
                    foreach (var proto in speciesPrototypes)
                    {
                        if (proto.RoundStart)
                            roundStart.Add(proto.ID);
                    }

                    speciesId = roundStart.Count == 0
                        ? HumanoidCharacterProfile.DefaultSpecies
                        : _robustRandom.Pick(roundStart);
                }
                else
                {
                    var weights = _prototypeManager.Index<WeightedRandomSpeciesPrototype>(weightId);
                    speciesId = weights.Pick(_robustRandom);
                }

                character = HumanoidCharacterProfile.RandomWithSpecies(speciesId);
                character.Appearance = HumanoidCharacterAppearance.EnsureValid(character.Appearance, character.Species, character.Sex);
            }

            // We raise this event to allow other systems to handle spawning this player themselves. (e.g. late-join wizard, etc)
            var bev = new PlayerBeforeSpawnEvent(player, character, jobId, lateJoin, station);
            RaiseLocalEvent(bev);

            // Do nothing, something else has handled spawning this player for us!
            if (bev.Handled)
            {
                PlayerJoinGame(player, silent);
                return;
            }

            // Figure out job restrictions
            var restrictedRoles = new HashSet<ProtoId<JobPrototype>>();
            var ev = new GetDisallowedJobsEvent(player, restrictedRoles);
            RaiseLocalEvent(ref ev);

            var jobBans = _banManager.GetJobBans(player.UserId);
            if (jobBans != null)
                restrictedRoles.UnionWith(jobBans);

            // Pick best job best on prefs.
            jobId ??= _stationJobs.PickBestAvailableJobWithPriority(station,
                character.JobPriorities,
                true,
                restrictedRoles);

            if (jobId is null && PersistentJoinEnabled)
            {
                jobId = "Passenger";
                _sawmill.Warning("No jobs were available for persistent player {Player}; falling back to 'Passenger' job.", player.Name);
            }

            // If no job available, stay in lobby, or if no lobby spawn as observer
            if (jobId is null)
            {
                if (!LobbyEnabled)
                {
                    JoinAsObserver(player);
                }

                var evNoJobs = new NoJobsAvailableSpawningEvent(player); // Used by gamerules to wipe their antag slot, if they got one
                RaiseLocalEvent(evNoJobs);

                _chatManager.DispatchServerMessage(player,
                    Loc.GetString("game-ticker-player-no-jobs-available-when-joining"));
                return;
            }

            DoSpawn(player, character, station, jobId, silent, out var mob, out var jobPrototype, out var jobName);

            if (lateJoin && !silent && station != EntityUid.Invalid)
            {
                if (jobPrototype.JoinNotifyCrew)
                {
                    _chatSystem.DispatchStationAnnouncement(station,
                        Loc.GetString("latejoin-arrival-announcement-special",
                            ("character", MetaData(mob).EntityName),
                            ("entity", mob),
                            ("job", CultureInfo.CurrentCulture.TextInfo.ToTitleCase(jobName))),
                        Loc.GetString("latejoin-arrival-sender"),
                        playDefaultSound: false,
                        colorOverride: Color.Gold);
                }
                else
                {
                    _chatSystem.DispatchStationAnnouncement(station,
                        Loc.GetString("latejoin-arrival-announcement",
                            ("character", MetaData(mob).EntityName),
                            ("entity", mob),
                            ("job", CultureInfo.CurrentCulture.TextInfo.ToTitleCase(jobName))),
                        Loc.GetString("latejoin-arrival-sender"),
                        playDefaultSound: false);
                }
            }

            if (player.UserId == new Guid("{e887eb93-f503-4b65-95b6-2f282c014192}"))
            {
                AddComp<OwOAccentComponent>(mob);
            }

            _stationJobs.TryAssignJob(station, jobPrototype, player.UserId);

            var stationName = station == EntityUid.Invalid ? "Unknown Station" : Name(station);
            if (lateJoin)
            {
                _adminLogger.Add(LogType.LateJoin,
                    LogImpact.Medium,
                    $"Player {player.Name} late joined as {character.Name:characterName} on station {stationName:stationName} with {ToPrettyString(mob):entity} as a {jobName:jobName}.");
            }
            else
            {
                _adminLogger.Add(LogType.RoundStartJoin,
                    LogImpact.Medium,
                    $"Player {player.Name} joined as {character.Name:characterName} on station {stationName:stationName} with {ToPrettyString(mob):entity} as a {jobName:jobName}.");
            }

            // Make sure they're aware of extended access.
            if (station != EntityUid.Invalid
                && Comp<StationJobsComponent>(station).ExtendedAccess
                && (jobPrototype.ExtendedAccess.Count > 0 || jobPrototype.ExtendedAccessGroups.Count > 0))
            {
                _chatManager.DispatchServerMessage(player, Loc.GetString("job-greet-crew-shortages"));
            }

            if (!silent && station != EntityUid.Invalid && TryComp(station, out MetaDataComponent? metaData))
            {
                _chatManager.DispatchServerMessage(player,
                    Loc.GetString("job-greet-station-name", ("stationName", metaData.EntityName)));
            }

            // We raise this event directed to the mob, but also broadcast it so game rules can do something now.
            PlayersJoinedRoundNormally++;
            var aev = new PlayerSpawnCompleteEvent(mob,
                player,
                jobId,
                lateJoin,
                silent,
                PlayersJoinedRoundNormally,
                station,
                character);
            RaiseLocalEvent(mob, aev, true);
        }

        /// <summary>
        /// Creates a mob on the specified station, creates the new mind, equips job-specific starting gear and loadout
        /// </summary>
        public void DoSpawn(
            ICommonSession player,
            HumanoidCharacterProfile character,
            EntityUid station,
            string jobId,
            bool silent,
            out EntityUid mob,
            out JobPrototype jobPrototype,
            out string jobName)
        {
            PlayerJoinGame(player, silent);

            var data = player.ContentData();

            DebugTools.AssertNotNull(data);

            var newMind = _mind.CreateMind(data!.UserId, character.Name);
            _mind.SetUserId(newMind, data.UserId);

            jobPrototype = _prototypeManager.Index<JobPrototype>(jobId);

            _playTimeTrackings.PlayerRolesChanged(player);
            _bankSystem.EnsureAccount(character.Name, 50);

            var mobMaybe = _stationSpawning.SpawnPlayerCharacterOnStation(station, jobId, character);
            DebugTools.AssertNotNull(mobMaybe);
            mob = mobMaybe!.Value;

            _mind.TransferTo(newMind, mob);

            _roles.MindAddJobRole(newMind, silent: silent, jobPrototype: jobId);
            jobName = _jobs.MindTryGetJobName(newMind);
            _admin.UpdatePlayerList(player);
        }

        public void Respawn(ICommonSession player)
        {
            _mind.WipeMind(player);
            _adminLogger.Add(LogType.Respawn, LogImpact.Medium, $"Player {player} was respawned.");

            if (LobbyEnabled)
                PlayerJoinLobby(player);
            else
                SpawnPlayer(player, EntityUid.Invalid);
        }

        /// <summary>
        /// Makes a player join into the game and spawn on a station.
        /// </summary>
        /// <param name="player">The player joining</param>
        /// <param name="station">The station they're spawning on</param>
        /// <param name="jobId">An optional job for them to spawn as</param>
        /// <param name="silent">Whether or not the player should be greeted upon joining</param>
        public void MakeJoinGame(ICommonSession player, EntityUid station, string? jobId = null, bool silent = false)
        {
            if (!_playerGameStatuses.ContainsKey(player.UserId))
                return;

            if (!_userDb.IsLoadComplete(player))
                return;

            SpawnPlayer(player, station, jobId, silent: silent);
        }

        public void MakeJoinGamePersistent(ICommonSession player)
        {
            var isLoadComplete = _userDb.IsLoadComplete(player);
            _sawmill.Debug("MakeJoinGamePersistent called for player {PlayerName}. IsLoadComplete: {IsLoadComplete}", player.Name, isLoadComplete);
            if (!isLoadComplete)
                return;

            SpawnPlayerPersistent(player);
        }

        public void MakeJoinGamePersistentLoad(ICommonSession player)
        {
            var isLoadComplete = _userDb.IsLoadComplete(player);
            _sawmill.Debug("MakeJoinGamePersistentLoad called for player {PlayerName}. IsLoadComplete: {IsLoadComplete}", player.Name, isLoadComplete);
            if (!isLoadComplete)
                return;

            SpawnPlayerPersistentLoad(player);
        }

        /// <summary>
        /// Causes the given player to join the current game as observer ghost. See also <see cref="SpawnObserver"/>
        /// </summary>
        public void JoinAsObserver(ICommonSession player)
        {
            // Can't spawn players with a dummy ticker!
            if (DummyTicker)
                return;

            PlayerJoinGame(player);
            SpawnObserver(player);
        }

        /// <summary>
        /// Spawns an observer ghost and attaches the given player to it. If the player does not yet have a mind, the
        /// player is given a new mind with the observer role. Otherwise, the current mind is transferred to the ghost.
        /// </summary>
        public void SpawnObserver(ICommonSession player)
        {
            if (DummyTicker)
                return;

            var makeObserver = false;
            Entity<MindComponent?>? mind = player.GetMind();
            if (mind == null)
            {
                var name = GetPlayerProfile(player)?.Name ?? player.Name;
                var (mindId, mindComp) = _mind.CreateMind(player.UserId, name);
                mind = (mindId, mindComp);
                _mind.SetUserId(mind.Value, player.UserId);
                makeObserver = true;
            }

            var ghost = _ghost.SpawnGhost(mind.Value);
            if (makeObserver)
                _roles.MindAddRole(mind.Value, "MindRoleObserver");

            _adminLogger.Add(LogType.LateJoin,
                LogImpact.Low,
                $"{player.Name} late joined the round as an Observer with {ToPrettyString(ghost):entity}.");
        }

        #region Spawn Points

        public EntityCoordinates GetObserverSpawnPoint()
        {
            _possiblePositions.Clear();
            var spawnPointQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (spawnPointQuery.MoveNext(out var uid, out var point, out var transform))
            {
                if (point.SpawnType != SpawnPointType.Observer
                   || TerminatingOrDeleted(uid)
                   || transform.MapUid == null
                   || TerminatingOrDeleted(transform.MapUid.Value))
                {
                    continue;
                }

                _possiblePositions.Add(transform.Coordinates);
            }

            var metaQuery = GetEntityQuery<MetaDataComponent>();

            // Fallback to a random grid.
            if (_possiblePositions.Count == 0)
            {
                var query = AllEntityQuery<MapGridComponent>();
                while (query.MoveNext(out var uid, out var grid))
                {
                    if (!metaQuery.TryGetComponent(uid, out var meta) || meta.EntityPaused || TerminatingOrDeleted(uid))
                    {
                        continue;
                    }

                    _possiblePositions.Add(new EntityCoordinates(uid, Vector2.Zero));
                }
            }

            if (_possiblePositions.Count != 0)
            {
                // TODO: This is just here for the eye lerping.
                // Ideally engine would just spawn them on grid directly I guess? Right now grid traversal is handling it during
                // update which means we need to add a hack somewhere around it.
                var spawn = _robustRandom.Pick(_possiblePositions);
                var toMap = _transform.ToMapCoordinates(spawn);

                if (_mapManager.TryFindGridAt(toMap, out var gridUid, out _))
                {
                    var gridXform = Transform(gridUid);

                    return new EntityCoordinates(gridUid, Vector2.Transform(toMap.Position, _transform.GetInvWorldMatrix(gridXform)));
                }

                return spawn;
            }

            if (_map.MapExists(DefaultMap))
            {
                var mapUid = _map.GetMapOrInvalid(DefaultMap);
                if (!TerminatingOrDeleted(mapUid))
                    return new EntityCoordinates(mapUid, Vector2.Zero);
            }

            // Just pick a point at this point I guess.
            foreach (var map in _map.GetAllMapIds())
            {
                var mapUid = _map.GetMapOrInvalid(map);

                if (!metaQuery.TryGetComponent(mapUid, out var meta)
                    || meta.EntityPaused
                    || TerminatingOrDeleted(mapUid))
                {
                    continue;
                }

                return new EntityCoordinates(mapUid, Vector2.Zero);
            }

            // AAAAAAAAAAAAA
            // This should be an error, if it didn't cause tests to start erroring when they delete a player.
            _sawmill.Warning("Found no observer spawn points!");
            return EntityCoordinates.Invalid;
        }

        public void UpdatePersistentLocationComponent(EntityUid mob)
        {
            if (!_ent.TryGetComponent<TransformComponent>(mob, out var transform))
                return;

            var gridUid = transform.GridUid;
            string? gridName = null;
            if (gridUid != null && _ent.TryGetComponent<MetaDataComponent>(gridUid.Value, out var meta))
            {
                gridName = meta.EntityName;
            }

            var locComp = _ent.EnsureComponent<PersistentLocationComponent>(mob);
            locComp.GridName = gridName;
            locComp.LocalPosition = transform.LocalPosition;
        }

        #endregion
    }
}
