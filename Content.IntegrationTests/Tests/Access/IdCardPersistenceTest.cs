using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Server.Access.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewRecords.Components;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using NUnit.Framework;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Access;

[TestFixture]
public sealed class IdCardPersistenceTest
{
    [Test]
    public async Task TestIdCardPersistenceAndAccess()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var user = pair.Client.User!.Value;
        var clientPrefManager = client.Resolve<IClientPreferencesManager>();
        var db = server.Resolve<IServerDbManager>();
        var serverPrefManager = server.Resolve<IServerPreferencesManager>();
        var serverPlayers = server.ResolveDependency<IPlayerManager>();
        var gameTicker = server.System<GameTicker>();
        var entMan = server.EntMan;

        // Configure client profile with Chief of Police
        HumanoidCharacterProfile profile = null!;
        await client.WaitPost(() =>
        {
            profile = HumanoidCharacterProfile.Random()
                .WithJobPriority("ChiefOfPolice", JobPriority.High);
            clientPrefManager.FinalizeCharacter(profile, 0);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var prefs = serverPrefManager.GetPreferences(user);
            Assert.That(prefs.Characters.ContainsKey(0), Is.True);
        });

        // Set UsePersistence to true and restart round to reload the persistent station map
        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);

        // Make ChiefOfPolice job unlimited on all stations so the player can spawn as ChiefOfPolice
        await server.WaitPost(() =>
        {
            var stationJobsSystem = entMan.System<StationJobsSystem>();
            var stationSystem = entMan.System<StationSystem>();
            var stations = stationSystem.GetStations();
            foreach (var station in stations)
            {
                stationJobsSystem.MakeJobUnlimited(station, "ChiefOfPolice");
            }
        });

        // Reconnect player to join
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        // Verify player is attached and alive
        var session = serverPlayers.Sessions.Single();
        Assert.That(session.AttachedEntity, Is.Not.Null);
        var originalBody = session.AttachedEntity!.Value;

        // Get access systems
        var accessReaderSystem = entMan.System<AccessReaderSystem>();
        var stationSystem = entMan.System<StationSystem>();

        EntityUid idCardUid = default;
        IdCardComponent idCardComp = null!;
        AccessComponent accessComp = null!;

        await server.WaitAssertion(() =>
        {
            var accessItems = accessReaderSystem.FindPotentialAccessItems(originalBody);
            foreach (var item in accessItems)
            {
                if (entMan.TryGetComponent<IdCardComponent>(item, out var id))
                {
                    idCardUid = item;
                    idCardComp = id;
                    break;
                }
            }

            Assert.That(idCardUid, Is.Not.EqualTo(default(EntityUid)), "ID card should be found on the spawned player.");
            Assert.That(entMan.TryGetComponent<AccessComponent>(idCardUid, out accessComp!), "ID card should have AccessComponent.");

            // Verify initial access/tags
            Assert.That(idCardComp.FullName, Is.EqualTo(profile.Name));
            Assert.That(idCardComp.LocalizedJobTitle, Is.EqualTo("job-name-chief-of-police").Or.EqualTo("Chief of Police"));
            Assert.That(idCardComp.stationID, Is.Not.Null);

            // Access check using tags / records
            Assert.That(accessComp.Tags, Contains.Item("HeadOfSecurity").Or.Contains("Security"));
        });

        // Test door access before restart
        EntityUid doorAccessReader = default;
        await server.WaitPost(() =>
        {
            doorAccessReader = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var readerComp = entMan.AddComponent<AccessReaderComponent>(doorAccessReader);
            readerComp.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "HeadOfSecurity" });
        });

        await server.WaitAssertion(() =>
        {
            var allowed = accessReaderSystem.IsAllowed(originalBody, doorAccessReader);
            Assert.That(allowed, Is.True, "Chief of Police should have access to HeadOfSecurity doors before restart.");
        });

        // Trigger persistence save for character and map
        await server.WaitPost(() =>
        {
            var mapLoader = entMan.System<MapLoaderSystem>();
            gameTicker.UpdatePersistentLocationComponent(originalBody, user);
            Assert.That(mapLoader.TrySaveGeneric(originalBody, PersistentCharacterSavePath.ForPlayer(user), out _), Is.True);

            var mapId = gameTicker.DefaultMap;
            var currentSavePath = gameTicker.GetLatestAutosavePath();
            Assert.That(mapLoader.TrySaveMap(mapId, currentSavePath, GameTicker.PersistentMapSaveOptions), Is.True);
        });

        // Simulate restart
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);

        // Reconnect player
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        // Verify player is attached to a valid body
        session = serverPlayers.Sessions.Single();
        Assert.That(session.AttachedEntity, Is.Not.Null);
        var postRestartBody = session.AttachedEntity!.Value;

        EntityUid postIdCardUid = default;
        IdCardComponent postIdCardComp = null!;
        AccessComponent postAccessComp = null!;

        await server.WaitAssertion(() =>
        {
            var accessItems = accessReaderSystem.FindPotentialAccessItems(postRestartBody);
            foreach (var item in accessItems)
            {
                if (entMan.TryGetComponent<IdCardComponent>(item, out var id))
                {
                    postIdCardUid = item;
                    postIdCardComp = id;
                    break;
                }
            }

            Assert.That(postIdCardUid, Is.Not.EqualTo(default(EntityUid)), "ID card should be found on player after restart.");
            Assert.That(entMan.TryGetComponent<AccessComponent>(postIdCardUid, out postAccessComp!), "ID card should have AccessComponent after restart.");

            // Check if job title got reset to Off Duty or access failed
            Assert.That(postIdCardComp.FullName, Is.EqualTo(profile.Name));
            Assert.That(postIdCardComp.LocalizedJobTitle, Is.Not.EqualTo("Off Duty"), "Job title should NOT reset to Off Duty.");
            Assert.That(postIdCardComp.stationID, Is.Not.Null, "stationID should NOT be null.");

            // Check if tags disappeared
            Assert.That(postAccessComp.Tags, Contains.Item("HeadOfSecurity").Or.Contains("Security"), "Access tags should be preserved.");
        });

        // Test door access after restart
        await server.WaitAssertion(() =>
        {
            var allowed = accessReaderSystem.IsAllowed(postRestartBody, doorAccessReader);
            Assert.That(allowed, Is.True, "Chief of Police should still have access to HeadOfSecurity doors after restart.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestIdCardOnMapPersistence()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var gameTicker = server.System<GameTicker>();
        var entMan = server.EntMan;

        // Set UsePersistence to true and restart round to reload the persistent station map
        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);

        EntityUid station = default;
        EntityUid idCard = default;

        await server.WaitPost(() =>
        {
            var stationSystem = entMan.System<StationSystem>();
            var stations = stationSystem.GetStations();
            Assert.That(stations.Count, Is.GreaterThan(0), "A station should exist after loading persistent map.");
            station = stations.First();

            var stationData = entMan.GetComponent<StationDataComponent>(station);
            var gridUid = stationData.Grids.First();

            idCard = entMan.SpawnEntity("CoPIDCard", new EntityCoordinates(gridUid, 0, 0));
            var idCardComp = entMan.GetComponent<IdCardComponent>(idCard);
            idCardComp.FullName = "Chief of Police Person";
            idCardComp.stationID = entMan.GetComponent<StationDataComponent>(station).UID;

            var crewRecords = entMan.GetComponent<CrewRecordsComponent>(station);
            var crewAssignments = entMan.GetComponent<CrewAssignmentsComponent>(station);

            crewAssignments.CreateAssignment("Chief of Police");
            var assignmentId = crewAssignments.NextID - 1;
            var assignment = crewAssignments.CrewAssignments[assignmentId];
            assignment.AccessIDs.Add("HeadOfSecurity");

            crewRecords.CreateRecord("Chief of Police Person", out var record);
            record!.AssignmentID = assignmentId;

            var idSystem = entMan.System<IdCardSystem>();
            idSystem.RebuildJob(idCard, idCardComp);
            Assert.That(idCardComp.LocalizedJobTitle, Is.EqualTo("Chief of Police"), "Job title should initially resolve correctly.");

            // Save the map
            var mapLoader = entMan.System<MapLoaderSystem>();
            var mapId = gameTicker.DefaultMap;
            var currentSavePath = gameTicker.GetLatestAutosavePath();
            Assert.That(mapLoader.TrySaveMap(mapId, currentSavePath, GameTicker.PersistentMapSaveOptions), Is.True);
        });

        // Restart round to simulate server restart / reload
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);

        // Find the reloaded ID card and assert its job title is preserved
        await server.WaitAssertion(() =>
        {
            EntityUid reloadedIdCard = default;
            var idQuery = entMan.EntityQueryEnumerator<IdCardComponent>();
            while (idQuery.MoveNext(out var uid, out var comp))
            {
                if (comp.FullName == "Chief of Police Person")
                {
                    reloadedIdCard = uid;
                    break;
                }
            }

            Assert.That(reloadedIdCard, Is.Not.EqualTo(default(EntityUid)), "Reloaded ID card should exist on the map.");
            var reloadedIdComp = entMan.GetComponent<IdCardComponent>(reloadedIdCard);
            Assert.That(reloadedIdComp.LocalizedJobTitle, Is.EqualTo("Chief of Police"), "Job title should resolve correctly after reload.");
        });

        await pair.CleanReturnAsync();
    }

    private static async Task DisconnectReconnect(Pair.TestPair pair)
    {
        var clientNetManager = pair.Client.ResolveDependency<IClientNetManager>();
        var serverPlayerManager = pair.Server.ResolveDependency<IPlayerManager>();
        var originalSession = serverPlayerManager.Sessions.Single();
        var name = originalSession.Name;
        var userId = originalSession.UserId;

        await pair.Client.WaitPost(() => clientNetManager.ClientDisconnect("Persistent join flow test"));
        await pair.RunTicksSync(10);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(serverPlayerManager.PlayerCount, Is.EqualTo(0));
        });

        pair.Client.SetConnectTarget(pair.Server);
        await pair.Client.WaitPost(() => clientNetManager.ClientConnect(null!, 0, name));
        await pair.RunTicksSync(10);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(serverPlayerManager.PlayerCount, Is.EqualTo(1));
            Assert.That(serverPlayerManager.Sessions.Single().UserId, Is.EqualTo(userId));
            Assert.That(serverPlayerManager.Sessions.Single().Status, Is.EqualTo(SessionStatus.InGame));
        });
    }
}
