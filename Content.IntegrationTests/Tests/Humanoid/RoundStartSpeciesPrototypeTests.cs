using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.IntegrationTests.Tests.Humanoid;

[TestFixture]
public sealed class RoundStartSpeciesPrototypeTests
{
    private const string BrokenRoundStartSpecies = "BrokenRoundStartSpeciesHumanoidTest";
    private const string BrokenRoundStartSpeciesPrefix = "BrokenRoundStartSpecies";

    [TestPrototypes]
    private const string Prototypes = """
- type: species
  id: BrokenRoundStartSpeciesHumanoidTest
  name: species-name-human
  roundStart: true
  prototype: MobHuman
  dollPrototype: AppearanceHuman
  skinColoration: MissingSkinColoration
""";

    [Test]
    public async Task RoundStartSpeciesDependenciesResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();

            Assert.Multiple(() =>
            {
                foreach (var species in protoMan.EnumeratePrototypes<SpeciesPrototype>()
                             .Where(species => species.RoundStart && !species.ID.StartsWith(BrokenRoundStartSpeciesPrefix)))
                {
                    Assert.That(protoMan.HasIndex<EntityPrototype>(species.Prototype),
                        $"Species {species.ID} references missing prototype {species.Prototype}");
                    Assert.That(protoMan.HasIndex<EntityPrototype>(species.DollPrototype),
                        $"Species {species.ID} references missing dollPrototype {species.DollPrototype}");
                    Assert.That(protoMan.HasIndex<SkinColorationPrototype>(species.SkinColoration),
                        $"Species {species.ID} references missing skinColoration {species.SkinColoration}");

                    var sex = species.Sexes[0];
                    Assert.DoesNotThrow(() => HumanoidCharacterAppearance.DefaultWithSpecies(species.ID, sex),
                        $"Species {species.ID} could not create a default appearance");
                    Assert.DoesNotThrow(() => HumanoidCharacterProfile.DefaultWithSpecies(species.ID, sex),
                        $"Species {species.ID} could not create a default profile");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrokenSpeciesFallsBackToHuman()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(BrokenRoundStartSpecies, Sex.Male);
            Assert.DoesNotThrow(() => HumanoidCharacterAppearance.DefaultWithSpecies(BrokenRoundStartSpecies, Sex.Male));

            profile.EnsureValid(pair.Player!, IoCManager.Instance!);

            Assert.That(profile.Species, Is.EqualTo(HumanoidCharacterProfile.DefaultSpecies));
        });

        await pair.CleanReturnAsync();
    }
}
