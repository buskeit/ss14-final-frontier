using System.Linq;
using System.Threading;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Lobby;

[TestFixture]
public sealed class PersistentJoinFlowTest
{
    [Test]
    public async Task NoCharacterForcesSetupAndFinalizeJoinsSafely()
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

        Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<LobbyState>());
        await PoolManager.WaitUntil(server, async () =>
        {
            var prefs = await db.GetPlayerPreferencesAsync(user, CancellationToken.None);
            return prefs is { Profiles.Count: 0 };
        }, maxTicks: 60);

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var prefs = serverPrefManager.GetPreferences(user);
            Assert.That(prefs.Characters, Is.Empty);
            Assert.That(prefs.SelectedCharacter, Is.Null);
        });

        await client.WaitAssertion(() =>
        {
            var lobby = client.Resolve<IStateManager>().CurrentState as LobbyState;
            Assert.That(lobby, Is.Not.Null);
            Assert.That(lobby!.Lobby, Is.Not.Null);
            Assert.That(lobby.Lobby!.CharacterSetupState.Visible, Is.True);
            Assert.That(lobby.Lobby.ReadyButton.Visible, Is.False);
            Assert.That(lobby.Lobby.ObserveButton.Visible, Is.False);
            Assert.That(lobby.Lobby.CharacterPreview.CharacterSetupButton.Visible, Is.False);
            Assert.That(lobby.Lobby.StartTime.Text, Is.EqualTo(string.Empty));
            var setup = lobby.Lobby.CharacterSetupState.Children.Single() as Content.Client.Lobby.UI.CharacterSetupGui;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.CloseButton.Visible, Is.False);
        });

        await server.WaitAssertion(() =>
        {
            var player = serverPlayers.Sessions.Single();
            Assert.That(player.AttachedEntity, Is.Null);
            Assert.That(gameTicker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
        });

        await client.WaitPost(() => clientPrefManager.FinalizeCharacter(HumanoidCharacterProfile.Random(), 0));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await server.WaitAssertion(() =>
        {
            var prefs = serverPrefManager.GetPreferences(user);
            Assert.That(prefs.Characters.ContainsKey(0), Is.True);
            Assert.That(prefs.SelectedCharacter, Is.Not.Null);
        });

        await AssertAttachedEntitySafe(server, serverPlayers.Sessions.Single());
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExistingCharacterReconnectsIntoSafeBody()
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

        await client.WaitPost(() => clientPrefManager.FinalizeCharacter(HumanoidCharacterProfile.Random(), 0));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var prefs = serverPrefManager.GetPreferences(user);
            Assert.That(prefs.Characters.ContainsKey(0), Is.True);
        });

        await PoolManager.WaitUntil(server, async () =>
        {
            var prefs = await db.GetPlayerPreferencesAsync(user, CancellationToken.None);
            return prefs is { Profiles.Count: > 0 } && prefs.Profiles.Any(profile => profile.Slot == 0);
        }, maxTicks: 60);

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var prefs = serverPrefManager.GetPreferences(user);
            Assert.That(prefs.Characters.ContainsKey(0), Is.True);
            Assert.That(prefs.SelectedCharacter, Is.Not.Null);
        });

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await AssertAttachedEntitySafe(server, serverPlayers.Sessions.Single());
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InvalidSavedBodyFallsBackToSafeStationSpawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var user = pair.Client.User!.Value;
        var clientPrefManager = client.Resolve<IClientPreferencesManager>();
        var serverPlayers = server.ResolveDependency<IPlayerManager>();
        var gameTicker = server.System<GameTicker>();

        await client.WaitPost(() => clientPrefManager.FinalizeCharacter(HumanoidCharacterProfile.Random(), 0));
        await pair.RunTicksSync(10);

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await server.WaitPost(() =>
        {
            gameTicker.RestartRound();
        });
        await pair.RunTicksSync(10);
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        await AssertAttachedEntitySafe(server, serverPlayers.Sessions.Single());

        await server.WaitPost(() =>
        {
            var attached = serverPlayers.Sessions.Single().AttachedEntity!.Value;
            var entMan = server.EntMan;
            var mobStateSystem = entMan.System<MobStateSystem>();
            var mapLoader = entMan.System<MapLoaderSystem>();
            var mobState = entMan.GetComponent<MobStateComponent>(attached);

            mobStateSystem.ChangeMobState(attached, MobState.Dead, mobState);
            Assert.That(mapLoader.TrySaveGeneric(attached, PersistentCharacterSavePath.ForPlayer(user), out _), Is.True);
        });

        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await AssertAttachedEntitySafe(server, serverPlayers.Sessions.Single());
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NonPersistentLobbyBehaviourIsUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var client = pair.Client;
        var server = pair.Server;
        var user = pair.Client.User!.Value;
        var db = server.Resolve<IServerDbManager>();

        await PoolManager.WaitUntil(server, async () =>
        {
            var prefs = await db.GetPlayerPreferencesAsync(user, CancellationToken.None);
            return prefs is { Profiles.Count: 0 };
        }, maxTicks: 60);

        await client.WaitAssertion(() =>
        {
            var lobby = client.Resolve<IStateManager>().CurrentState as LobbyState;
            Assert.That(lobby, Is.Not.Null);
            Assert.That(lobby!.Lobby, Is.Not.Null);
            Assert.That(lobby.Lobby!.ReadyButton.Visible, Is.True);
            Assert.That(lobby.Lobby.ObserveButton.Visible, Is.True);
            Assert.That(lobby.Lobby.CharacterPreview.CharacterSetupButton.Visible, Is.True);
            Assert.That(lobby.Lobby.CharacterSetupState.Visible, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PersistentBodyMapRestoreSuccessAfterReload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var user = pair.Client.User!.Value;
        var clientPrefManager = client.Resolve<IClientPreferencesManager>();
        var serverPlayers = server.ResolveDependency<IPlayerManager>();
        var gameTicker = server.System<GameTicker>();

        await client.WaitPost(() => clientPrefManager.FinalizeCharacter(HumanoidCharacterProfile.Random(), 0));
        await pair.RunTicksSync(10);

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        // Verify player has spawned and is alive
        await AssertAttachedEntitySafe(server, serverPlayers.Sessions.Single());

        // Save the map and the player body (which will write PersistentLocationComponent)
        await server.WaitPost(() =>
        {
            var attached = serverPlayers.Sessions.Single().AttachedEntity!.Value;
            var mapLoader = server.EntMan.System<MapLoaderSystem>();
            
            // Trigger saving the character
            gameTicker.UpdatePersistentLocationComponent(attached);
            Assert.That(mapLoader.TrySaveGeneric(attached, PersistentCharacterSavePath.ForPlayer(user), out _), Is.True);
        });

        // Simulate server restart / round restart that reloads the map
        await server.WaitPost(() => gameTicker.RestartRound());
        await pair.RunTicksSync(10);
        
        // Reconnect player
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        // Verify player is attached to the restored body on the valid map
        await AssertAttachedEntitySafe(server, serverPlayers.Sessions.Single());
        
        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await pair.CleanReturnAsync();
    }

    private static async Task AssertAttachedEntitySafe(RobustIntegrationTest.ServerIntegrationInstance server, ICommonSession player)
    {
        await server.WaitAssertion(() =>
        {
            Assert.That(player.AttachedEntity, Is.Not.Null);

            var entMan = server.EntMan;
            var mobStateSystem = entMan.System<MobStateSystem>();
            var attached = player.AttachedEntity!.Value;
            var xform = entMan.GetComponent<TransformComponent>(attached);

            Assert.That(entMan.EntityExists(attached), Is.True);
            Assert.That(entMan.TryGetComponent<MobStateComponent>(attached, out var mobState), Is.True);
            Assert.That(mobStateSystem.IsAlive(attached, mobState), Is.True);
            Assert.That(mobStateSystem.IsCritical(attached, mobState), Is.False);
            Assert.That(xform.MapUid, Is.Not.Null);
            Assert.That(xform.GridUid, Is.Not.Null);
            Assert.That(entMan.EntityExists(xform.MapUid!.Value), Is.True);
            Assert.That(entMan.EntityExists(xform.GridUid!.Value), Is.True);
        });
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
