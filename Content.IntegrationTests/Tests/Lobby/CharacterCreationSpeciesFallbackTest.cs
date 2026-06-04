using Content.Client.Lobby;
using Content.Server.Preferences.Managers;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests.Lobby;

[TestFixture]
[TestOf(typeof(ClientPreferencesManager))]
[TestOf(typeof(ServerPreferencesManager))]
public sealed class CharacterCreationSpeciesFallbackTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: species
  id: BrokenRoundStartSpeciesLobbyTest
  name: species-name-human
  roundStart: true
  prototype: MobHuman
  dollPrototype: AppearanceHuman
  skinColoration: MissingSkinColoration
""";

    [Test]
    public async Task OpenCharacterSetupWithBrokenRoundStartSpeciesPresent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var client = pair.Client;

        Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<LobbyState>());

        await client.WaitPost(() =>
        {
            var ui = client.Resolve<IUserInterfaceManager>();
            ui.GetUIController<LobbyUIController>().ReloadCharacterSetup();
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CreateCharacterWithBrokenRoundStartSpeciesPresent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var user = pair.Client.User!.Value;
        var clientPrefManager = client.Resolve<IClientPreferencesManager>();
        var serverPrefManager = server.Resolve<IServerPreferencesManager>();

        Assert.That(client.Resolve<IStateManager>().CurrentState, Is.TypeOf<LobbyState>());

        await pair.RunTicksSync(5);

        var initialCount = clientPrefManager.Preferences?.Characters.Count ?? 0;
        await client.WaitPost(() => clientPrefManager.CreateCharacter(HumanoidCharacterProfile.DefaultWithSpecies()));
        await pair.RunTicksSync(5);

        var clientCharacters = clientPrefManager.Preferences?.Characters;
        Assert.That(clientCharacters, Is.Not.Null);
        Assert.That(clientCharacters, Has.Count.EqualTo(initialCount + 1));

        var createdProfile = clientCharacters[clientCharacters.Count - 1];
        Assert.That(createdProfile.Species, Is.EqualTo(HumanoidCharacterProfile.DefaultSpecies));

        await PoolManager.WaitUntil(server, () => serverPrefManager.GetPreferences(user).Characters.Count == initialCount + 1, maxTicks: 60);

        var serverCharacters = serverPrefManager.GetPreferences(user).Characters;
        Assert.That(serverCharacters, Has.Count.EqualTo(initialCount + 1));
        Assert.That(serverCharacters[serverCharacters.Count - 1].Species, Is.EqualTo(HumanoidCharacterProfile.DefaultSpecies));

        await pair.CleanReturnAsync();
    }
}
