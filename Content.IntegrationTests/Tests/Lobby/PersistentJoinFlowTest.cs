using System.Linq;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Server.Preferences.Managers;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests.Lobby;

[TestFixture]
public sealed class PersistentJoinFlowTest
{
    [Test]
    public async Task NoCharacterForcesSetupAndFinalizeJoins()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var user = pair.Client.User!.Value;
        var clientPrefManager = client.Resolve<IClientPreferencesManager>();
        var serverPrefManager = server.Resolve<IServerPreferencesManager>();
        var serverPlayers = server.ResolveDependency<IPlayerManager>();

        Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<LobbyState>());

        await client.WaitPost(() =>
        {
            foreach (var slot in clientPrefManager.Preferences!.Characters.Keys.ToArray())
            {
                clientPrefManager.DeleteCharacter(slot);
            }
        });

        await pair.RunTicksSync(5);
        await PoolManager.WaitUntil(server, () => serverPrefManager.GetPreferences(user).Characters.Count == 0, maxTicks: 60);

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

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
            Assert.That(setup!.CharacterList.Visible, Is.False);
            Assert.That(setup.CloseButton.Visible, Is.False);
        });

        await server.WaitAssertion(() =>
        {
            var player = serverPlayers.Sessions.Single();
            Assert.That(player.AttachedEntity, Is.Null);
        });

        await client.WaitPost(() => clientPrefManager.FinalizeCharacter(HumanoidCharacterProfile.Random(), 0));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await PoolManager.WaitUntil(server, () => serverPlayers.Sessions.Single().AttachedEntity != null, maxTicks: 60);
        Assert.That(serverPrefManager.GetPreferences(user).Characters.Count, Is.EqualTo(1));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExistingCharacterBypassesLobbyAndReconnectsSameBody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var serverPlayers = server.ResolveDependency<IPlayerManager>();

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.UsePersistence, true));
        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        EntityUid body = default;

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await server.WaitAssertion(() =>
        {
            var player = serverPlayers.Sessions.Single();
            Assert.That(player.AttachedEntity, Is.Not.Null);
            body = player.AttachedEntity!.Value;
        });

        await DisconnectReconnect(pair);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<GameplayState>());
        });

        await server.WaitAssertion(() =>
        {
            var player = serverPlayers.Sessions.Single();
            Assert.That(player.AttachedEntity, Is.EqualTo(body));
        });

        await pair.CleanReturnAsync();
    }

    private static async Task DisconnectReconnect(Pair.TestPair pair)
    {
        var clientNetManager = pair.Client.ResolveDependency<IClientNetManager>();
        var serverPlayerManager = pair.Server.ResolveDependency<IPlayerManager>();
        var name = serverPlayerManager.Sessions.Single().Name;

        await pair.Client.WaitPost(() => clientNetManager.ClientDisconnect("Persistent join flow test"));
        await pair.RunTicksSync(10);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(serverPlayerManager.Sessions.Single().Status, Is.EqualTo(SessionStatus.Disconnected));
        });

        pair.Client.SetConnectTarget(pair.Server);
        await pair.Client.WaitPost(() => clientNetManager.ClientConnect(null!, 0, name));
        await pair.RunTicksSync(10);
    }
}
