using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Access.Systems;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.CrewRecords.Components;
using Content.Shared.Preferences;
using Content.Shared.Station;
using Content.Shared.StationRecords;
using NUnit.Framework;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.Shared.Access.Components.IdCardConsoleComponent;

namespace Content.IntegrationTests.Tests.Access;

[TestFixture]
public sealed class IdCardConsoleRecordsTest
{
    [Test]
    public async Task RecordSavesShowImmediatelyAndPersistAcrossReload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var stationSystem = entMan.System<StationSystem>();
        var stationRecords = entMan.System<StationRecordsSystem>();
        var idSystem = entMan.System<IdCardSystem>();
        var accessSystem = entMan.System<AccessSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var uiSystem = entMan.System<UserInterfaceSystem>();

        Assert.That(cfg.GetCVar(CCVars.GridFill), Is.False);

        EntityUid console = default;
        EntityUid targetId = default;
        EntityUid hopId = default;
        EntityUid medicalId = default;
        EntityUid securityId = default;
        EntityUid actor = default;
        CrewRecord targetRecord = default!;
        IdCardConsoleComponent consoleComp = default!;

        await server.WaitAssertion(() =>
        {
            var (station, gridUid) = SetupStation(mapMan, entMan, stationSystem);

            console = entMan.SpawnEntity("ComputerId", new EntityCoordinates(gridUid, 0, 0));
            targetId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            hopId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            medicalId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            securityId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            actor = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));

            idSystem.TryChangeFullName(targetId, "Alice Passenger");
            idSystem.TryChangeFullName(hopId, "Harper Hop");
            idSystem.TryChangeFullName(medicalId, "Morgan Medic");
            idSystem.TryChangeFullName(securityId, "Sam Security");

            accessSystem.TrySetTags(hopId, new List<ProtoId<AccessLevelPrototype>> { "HeadOfPersonnel" });
            accessSystem.TrySetTags(medicalId, new List<ProtoId<AccessLevelPrototype>> { "Medical" });
            accessSystem.TrySetTags(securityId, new List<ProtoId<AccessLevelPrototype>> { "Security" });

            Assert.That(entMan.TryGetComponent(console, out consoleComp));

            targetRecord = EnsureCrewRecord(station, targetId, "Alice Passenger", entMan, stationRecords);

            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, targetId, null));
            Assert.That(itemSlots.TryInsert(console, consoleComp.PrivilegedIdSlot, hopId, null));
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var saveGeneral = new SaveGeneralRecord("General text");
            saveGeneral.Actor = actor;
            entMan.EventBus.RaiseLocalEvent(console, saveGeneral, true);

            Assert.That(targetRecord.GeneralRecord, Is.EqualTo("General text"));
            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CanEditGeneral, Is.True);
                Assert.That(state.CrewRecord?.GeneralRecord, Is.EqualTo("General text"));
            });

            Assert.That(itemSlots.TryEject(console, consoleComp.TargetIdSlot, null, out var ejectedTarget));
            Assert.That(ejectedTarget, Is.EqualTo(targetId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, targetId, null));

            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CrewRecord?.GeneralRecord, Is.EqualTo("General text"));
            });

            Assert.That(itemSlots.TryEject(console, consoleComp.PrivilegedIdSlot, null, out var oldPriv));
            Assert.That(oldPriv, Is.EqualTo(hopId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.PrivilegedIdSlot, medicalId, null));

            var saveMedical = new SaveMedicalRecord("Medical text");
            saveMedical.Actor = actor;
            entMan.EventBus.RaiseLocalEvent(console, saveMedical, true);

            Assert.That(targetRecord.MedicalRecord, Is.EqualTo("Medical text"));
            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CanEditMedical, Is.True);
                Assert.That(state.CrewRecord?.MedicalRecord, Is.EqualTo("Medical text"));
            });

            Assert.That(itemSlots.TryEject(console, consoleComp.TargetIdSlot, null, out ejectedTarget));
            Assert.That(ejectedTarget, Is.EqualTo(targetId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, targetId, null));

            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CrewRecord?.MedicalRecord, Is.EqualTo("Medical text"));
            });

            Assert.That(itemSlots.TryEject(console, consoleComp.PrivilegedIdSlot, null, out oldPriv));
            Assert.That(oldPriv, Is.EqualTo(medicalId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.PrivilegedIdSlot, securityId, null));

            var saveCriminal = new SaveCriminalRecord("Criminal text");
            saveCriminal.Actor = actor;
            entMan.EventBus.RaiseLocalEvent(console, saveCriminal, true);

            Assert.That(targetRecord.CriminalRecord, Is.EqualTo("Criminal text"));
            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CanEditCriminal, Is.True);
                Assert.That(state.CrewRecord?.CriminalRecord, Is.EqualTo("Criminal text"));
            });

            Assert.That(itemSlots.TryEject(console, consoleComp.TargetIdSlot, null, out ejectedTarget));
            Assert.That(ejectedTarget, Is.EqualTo(targetId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, targetId, null));

            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CrewRecord?.CriminalRecord, Is.EqualTo("Criminal text"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EditPermissionsAreSplitAndUnauthorizedSavesAreRejected()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var stationSystem = entMan.System<StationSystem>();
        var stationRecords = entMan.System<StationRecordsSystem>();
        var idSystem = entMan.System<IdCardSystem>();
        var accessSystem = entMan.System<AccessSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var uiSystem = entMan.System<UserInterfaceSystem>();

        EntityUid console = default;
        EntityUid targetId = default;
        EntityUid hopId = default;
        EntityUid medicalId = default;
        EntityUid securityId = default;
        EntityUid actor = default;
        CrewRecord targetRecord = default!;
        IdCardConsoleComponent consoleComp = default!;

        await server.WaitAssertion(() =>
        {
            var (station, gridUid) = SetupStation(mapMan, entMan, stationSystem);

            console = entMan.SpawnEntity("ComputerId", new EntityCoordinates(gridUid, 0, 0));
            targetId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            hopId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            medicalId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            securityId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));
            actor = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(gridUid, 0, 0));

            idSystem.TryChangeFullName(targetId, "Alice Passenger");
            accessSystem.TrySetTags(hopId, new List<ProtoId<AccessLevelPrototype>> { "HeadOfPersonnel" });
            accessSystem.TrySetTags(medicalId, new List<ProtoId<AccessLevelPrototype>> { "Medical" });
            accessSystem.TrySetTags(securityId, new List<ProtoId<AccessLevelPrototype>> { "Security" });

            Assert.That(entMan.TryGetComponent(console, out consoleComp));

            targetRecord = EnsureCrewRecord(station, targetId, "Alice Passenger", entMan, stationRecords);

            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, targetId, null));
            Assert.That(itemSlots.TryInsert(console, consoleComp.PrivilegedIdSlot, hopId, null));
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CanEditGeneral, Is.True);
                Assert.That(state.CanAccessMedical, Is.False);
                Assert.That(state.CanEditMedical, Is.False);
                Assert.That(state.CanAccessCriminal, Is.False);
                Assert.That(state.CanEditCriminal, Is.False);
            });

            var saveMedical = new SaveMedicalRecord("Should not save");
            saveMedical.Actor = actor;
            entMan.EventBus.RaiseLocalEvent(console, saveMedical, true);

            var saveCriminal = new SaveCriminalRecord("Should not save");
            saveCriminal.Actor = actor;
            entMan.EventBus.RaiseLocalEvent(console, saveCriminal, true);

            Assert.That(targetRecord.MedicalRecord, Is.Empty);
            Assert.That(targetRecord.CriminalRecord, Is.Empty);

            Assert.That(itemSlots.TryEject(console, consoleComp.PrivilegedIdSlot, null, out var oldPriv));
            Assert.That(oldPriv, Is.EqualTo(hopId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.PrivilegedIdSlot, medicalId, null));

            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CanAccessMedical, Is.True);
                Assert.That(state.CanEditMedical, Is.True);
                Assert.That(state.CanAccessCriminal, Is.False);
                Assert.That(state.CanEditCriminal, Is.False);
            });

            Assert.That(itemSlots.TryEject(console, consoleComp.PrivilegedIdSlot, null, out oldPriv));
            Assert.That(oldPriv, Is.EqualTo(medicalId));
            Assert.That(itemSlots.TryInsert(console, consoleComp.PrivilegedIdSlot, securityId, null));

            AssertUiState(uiSystem, console, state =>
            {
                Assert.That(state.CanAccessCriminal, Is.True);
                Assert.That(state.CanEditCriminal, Is.True);
                Assert.That(state.CanAccessMedical, Is.False);
                Assert.That(state.CanEditMedical, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    private static (EntityUid Station, EntityUid Grid) SetupStation(
        IMapManager mapMan,
        IEntityManager entMan,
        StationSystem stationSystem)
    {
        var mapSystem = entMan.System<SharedMapSystem>();
        mapSystem.CreateMap(out var mapId);
        var gridUid = mapMan.CreateGridEntity(mapId);
        mapSystem.SetTile(gridUid, Vector2i.Zero, new Tile(1));

        var station = stationSystem.InitializeNewStation(
            new StationConfig { StationPrototype = "StandardNanotrasenStation" },
            new[] { gridUid.Owner });

        return (station, gridUid.Owner);
    }

    private static CrewRecord EnsureCrewRecord(
        EntityUid station,
        EntityUid card,
        string name,
        IEntityManager entMan,
        StationRecordsSystem stationRecords)
    {
        Assert.That(entMan.TryGetComponent<StationRecordsComponent>(station, out var stationData));
        stationRecords.CreateGeneralRecord(
            station,
            card,
            name,
            25,
            "Human",
            Gender.Female,
            "Passenger",
            null,
            null,
            new HumanoidCharacterProfile(),
            stationData);

        Assert.That(entMan.TryGetComponent<CrewRecordsComponent>(station, out var crewRecords));
        crewRecords.TryEnsureRecord(name, out var record);
        Assert.That(record, Is.Not.Null);
        return record!;
    }

    private static void AssertUiState(
        UserInterfaceSystem uiSystem,
        EntityUid console,
        System.Action<IdCardConsoleBoundUserInterfaceState> assertions)
    {
        Assert.That(uiSystem.TryGetUiState<IdCardConsoleBoundUserInterfaceState>((console, null), IdCardConsoleUiKey.Key, out var state));
        assertions(state!);
    }
}
