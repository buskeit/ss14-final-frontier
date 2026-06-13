using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server._NF.Bank;
using Content.Server.VendingMachines;
using Content.Shared.Cargo.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;
using System;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace Content.IntegrationTests.Tests.Vending;

public sealed class VendingInteractionTest : InteractionTest
{
    private const string VendingMachineProtoId = "InteractionTestVendingMachine";
    private const string YamlPricedVendingMachineProtoId = "InteractionTestYamlPricedVendingMachine";
    private const string StaticPricedVendingMachineProtoId = "InteractionTestStaticPricedVendingMachine";
    private const string DefaultPricedVendingMachineProtoId = "InteractionTestDefaultPricedVendingMachine";
    private const string FreePricedVendingMachineProtoId = "InteractionTestFreePricedVendingMachine";
    private const string AccessLockedPaidVendingMachineProtoId = "InteractionTestAccessLockedPaidVendingMachine";

    private const string VendedItemProtoId = "InteractionTestItem";
    private const string PaidVendedItemProtoId = "InteractionTestPaidItem";
    private const string UnpricedVendedItemProtoId = "InteractionTestUnpricedItem";

    private const string RestockBoxProtoId = "InteractionTestRestockBox";

    private const string RestockBoxOtherProtoId = "InteractionTestRestockBoxOther";
    private static readonly ProtoId<DamageTypePrototype> TestDamageType = "Blunt";

    [TestPrototypes]
    private const string TestPrototypes = $@"
- type: entity
  parent: BaseItem
  id: {VendedItemProtoId}
  name: {VendedItemProtoId}

- type: entity
  parent: BaseItem
  id: {PaidVendedItemProtoId}
  name: {PaidVendedItemProtoId}
  components:
  - type: StaticPrice
    price: 11

- type: entity
  parent: BaseItem
  id: {UnpricedVendedItemProtoId}
  name: {UnpricedVendedItemProtoId}

- type: vendingMachineInventory
  id: InteractionTestVendingInventory
  startingInventory:
    {VendedItemProtoId}: 5

- type: vendingMachineInventory
  id: InteractionTestPaidVendingInventory
  startingInventory:
    {PaidVendedItemProtoId}: 1

- type: vendingMachineInventory
  id: InteractionTestUnpricedVendingInventory
  startingInventory:
    {UnpricedVendedItemProtoId}: 1

- type: vendingMachineInventory
  id: InteractionTestVendingInventoryOther
  startingInventory:
    {VendedItemProtoId}: 5

- type: entity
  parent: BaseVendingMachineRestock
  id: {RestockBoxProtoId}
  components:
  - type: VendingMachineRestock
    canRestock:
    - InteractionTestVendingInventory

- type: entity
  parent: BaseVendingMachineRestock
  id: {RestockBoxOtherProtoId}
  components:
  - type: VendingMachineRestock
    canRestock:
    - InteractionTestVendingInventoryOther

- type: entity
  id: {VendingMachineProtoId}
  parent: VendingMachine
  components:
  - type: VendingMachine
    pack: InteractionTestVendingInventory
    requiresCash: false
    ejectDelay: 0 # no delay to speed up tests
  - type: Sprite
    sprite: error.rsi

- type: entity
  id: {YamlPricedVendingMachineProtoId}
  parent: VendingMachine
  components:
  - type: VendingMachine
    pack: InteractionTestPaidVendingInventory
    requiresCash: true
    prices:
      {PaidVendedItemProtoId}: 7
    ejectDelay: 0
  - type: Sprite
    sprite: error.rsi

- type: entity
  id: {StaticPricedVendingMachineProtoId}
  parent: VendingMachine
  components:
  - type: VendingMachine
    pack: InteractionTestPaidVendingInventory
    requiresCash: true
    ejectDelay: 0
  - type: Sprite
    sprite: error.rsi

- type: entity
  id: {DefaultPricedVendingMachineProtoId}
  parent: VendingMachine
  components:
  - type: VendingMachine
    pack: InteractionTestUnpricedVendingInventory
    requiresCash: true
    defaultPrice: 9
    ejectDelay: 0
  - type: Sprite
    sprite: error.rsi

- type: entity
  id: {FreePricedVendingMachineProtoId}
  parent: VendingMachine
  components:
  - type: VendingMachine
    pack: InteractionTestPaidVendingInventory
    requiresCash: false
    ejectDelay: 0
  - type: Sprite
    sprite: error.rsi

- type: entity
  id: {AccessLockedPaidVendingMachineProtoId}
  parent: VendingMachine
  components:
  - type: VendingMachine
    pack: InteractionTestPaidVendingInventory
    requiresCash: true
    ejectDelay: 0
  - type: AccessReader
    access: [[""Command""]]
  - type: Sprite
    sprite: error.rsi
";

    [Test]
    public async Task InteractUITest()
    {
        await SpawnTarget(VendingMachineProtoId);

        // Should start with no BUI open
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "BUI was open unexpectedly.");

        // Unpowered vending machine does not open BUI
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "BUI opened without power.");

        // Power the vending machine
        var apc = await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicks(1);

        // Interacting with powered vending machine opens BUI
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        // Interacting with it again closes the BUI
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "BUI failed to close on interaction.");

        // Reopen BUI for the next check
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to reopen.");

        // Remove power
        await Delete(apc);
        await RunTicks(1);

        // The BUI should close when power is lost
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "BUI failed to close on power loss.");
    }

    [Test]
    public async Task DispenseItemTest()
    {
        await SpawnTarget(VendingMachineProtoId);
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var items = vendingSystem.GetAllInventory(vendorEnt);

        // Verify initial item count
        Assert.That(items, Is.Not.Empty, $"{VendingMachineProtoId} spawned with no items.");
        Assert.That(items.First().Amount, Is.EqualTo(5), $"{VendingMachineProtoId} spawned with unexpected item count.");

        // Power the vending machine
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicks(1);

        // Open the BUI
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        // Request an item be dispensed
        var ev = new VendingMachineEjectMessage(InventoryType.Regular, VendedItemProtoId);
        await SendBui(VendingMachineUiKey.Key, ev);

        // Make sure the stock decreased
        Assert.That(items.First().Amount, Is.EqualTo(4), "Stocked item count did not decrease.");
        // Make sure the dispensed item was spawned in to the world
        await AssertEntityLookup(
            ("APCBasic", 1),
            (VendedItemProtoId, 1)
        );
    }

    [Test]
    public async Task RestockTest()
    {
        var vendingSystem = SEntMan.System<VendingMachineSystem>();

        await SpawnTarget(VendingMachineProtoId);
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        var items = vendingSystem.GetAllInventory(vendorEnt);

        Assert.That(items, Is.Not.Empty, $"{VendingMachineProtoId} spawned with no items.");
        Assert.That(items.First().Amount, Is.EqualTo(5), $"{VendingMachineProtoId} spawned with unexpected item count.");

        // Try to restock with the maintenance panel closed (nothing happens)
        await InteractUsing(RestockBoxProtoId);

        Assert.That(items.First().Amount, Is.EqualTo(5), "Restocked without opening maintenance panel.");

        // Open the maintenance panel
        await InteractUsing(Screw);

        // Try to restock using the wrong restock box (nothing happens)
        await InteractUsing(RestockBoxOtherProtoId);

        Assert.That(items.First().Amount, Is.EqualTo(5), "Restocked with wrong restock box.");

        // Restock the machine
        await InteractUsing(RestockBoxProtoId);

        Assert.That(items.First().Amount, Is.EqualTo(10), "Restocking resulted in unexpected item count.");
    }

    [Test]
    public async Task RepairTest()
    {
        await SpawnTarget(VendingMachineProtoId);

        // Power the vending machine
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicks(1);

        // Break it
        await BreakVendor();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "BUI did not close when vending machine broke.");

        // Make sure we can't open the BUI while it's broken
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "Opened BUI of broken vending machine.");

        // Repair the vending machine
        await InteractUsing(Weld);

        // Make sure the BUI can open now that the machine has been repaired
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "Failed to open BUI after repair.");
    }

    [Test]
    public async Task PaidVendRequiresFunds()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(0);
        await RunTicks(1);

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");
        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), "Opening the UI inserted unexpected cash.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(1), "Paid vend dispensed without funds.");
        await AssertEntityLookup(("APCBasic", 1));
        Assert.That(GetBankBalance(), Is.EqualTo(0), "Balance changed on failed vend.");
    }

    [Test]
    public async Task PaidVendorAcceptsCash()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(0);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 25);
        Assert.That(GetBankBalance(), Is.EqualTo(0), "Inserted cash was deposited into the player's account.");
        Assert.That(GetCashSlotBalance(), Is.EqualTo(25), "Inserted cash was not retained in the vending cash slot.");
        Assert.That(GetTotalCash(), Is.EqualTo(25), "Inserting cash duplicated or deleted money.");
    }

    [Test]
    public async Task UnpoweredVendorDoesNotTrapInsertedCash()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SetBankBalance(0);
        await InteractUsing("SpaceCash", 10);

        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), "Unpowered vending machine accepted cash into escrow.");
        Assert.That(GetBankBalance(), Is.EqualTo(0), "Unpowered vending machine deposited cash into the account.");
        Assert.That(GetTotalCash(), Is.EqualTo(10), "Unpowered cash insertion duplicated or deleted money.");
    }

    [TestCase("VendingMachineMedical")]
    [TestCase("VendingMachineMedicalBase")]
    public async Task ProductionVendorsHoldAndEjectInsertedCash(string prototype)
    {
        await SpawnTarget(prototype);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(50);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 13);

        Assert.That(GetCashSlotBalance(), Is.EqualTo(13), $"{prototype} did not hold inserted cash in escrow.");
        Assert.That(GetBankBalance(), Is.EqualTo(50), $"{prototype} incorrectly deposited escrow into the account.");

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), $"{prototype} BUI failed to open.");
        await AssertCashSlotUiState(13);
        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectCashMessage());

        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), $"{prototype} did not eject its escrow.");
        Assert.That(GetTotalCash(), Is.EqualTo(13), $"{prototype} duplicated or deleted ejected cash.");
    }

    [Test]
    public async Task PaidVendorEjectsCash()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(0);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 25);

        await Activate();
        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectCashMessage());

        Assert.That(GetBankBalance(), Is.EqualTo(0), "Ejected cash changed the player's account balance.");
        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), "Cash remained in the vending cash slot after ejection.");
        Assert.That(GetTotalCash(), Is.EqualTo(25), "Ejecting cash duplicated or deleted money.");
    }

    [Test]
    public async Task PaidVendUsesInsertedCashFirst()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(100);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 10);

        await Activate();
        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(GetBankBalance(), Is.EqualTo(100), "Vend withdrew from the account before using inserted cash.");
        Assert.That(GetCashSlotBalance(), Is.EqualTo(3), "Vend did not deduct the correct amount from inserted cash.");
        Assert.That(GetTotalCash(), Is.EqualTo(3), "Purchase duplicated or deleted the remaining inserted cash.");
        await AssertEntityLookup(("APCBasic", 1), (PaidVendedItemProtoId, 1));
    }

    [Test]
    public async Task PaidVendUsesAccountForCashSlotRemainder()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(10);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 5);

        await Activate();
        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), "Vend did not consume all inserted cash first.");
        Assert.That(GetBankBalance(), Is.EqualTo(8), "Vend did not withdraw only the remaining price from the account.");
        Assert.That(GetTotalCash(), Is.EqualTo(0), "Purchase duplicated inserted cash.");
        await AssertEntityLookup(("APCBasic", 1), (PaidVendedItemProtoId, 1));
    }

    [Test]
    public async Task PaidVendCashSlotPersistsAcrossUiReopen()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(10);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 25);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");
        await AssertCashSlotUiState(25);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), Is.False, "BUI failed to close.");
        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to reopen.");
        await AssertCashSlotUiState(25);
        Assert.That(GetBankBalance(), Is.EqualTo(10), "Reopening the UI moved cash into the account.");
        Assert.That(GetTotalCash(), Is.EqualTo(25), "Reopening the UI duplicated or deleted cash.");
    }

    [Test]
    public async Task PaidVendCashSlotPersistsAcrossMapSaveLoad()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(10);
        await RunTicks(1);
        await InteractUsing("SpaceCash", 25);

        var savePath = new ResPath("/Maps/Test/VendingCashSlotPersistence.yml");
        MapId savedMap = default;
        await Server.WaitAssertion(() =>
        {
            var resources = Server.ResolveDependency<IResourceManager>();
            resources.UserData.CreateDir(savePath.Directory);

            MapSystem.CreateMap(out savedMap);
            var grid = MapMan.CreateGridEntity(savedMap);
            MapSystem.SetTile(grid, Vector2i.Zero, new Tile(1));
            Transform.SetCoordinates(STarget!.Value, new EntityCoordinates(grid, Vector2.Zero));

            var loader = SEntMan.System<MapLoaderSystem>();
            Assert.That(loader.TrySaveMap(savedMap, savePath), "Failed to save vending escrow test map.");
            MapSystem.DeleteMap(savedMap);
        });

        await Server.WaitIdleAsync();

        MapId loadedMap = default;
        await Server.WaitAssertion(() =>
        {
            var loader = SEntMan.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadMap(savePath, out var map, out _), "Failed to reload vending escrow test map.");
            loadedMap = map!.Value.Comp.MapId;
        });

        await Server.WaitIdleAsync();
        await Server.WaitAssertion(() =>
        {
            EntityUid? loadedVendor = null;
            var query = SEntMan.EntityQueryEnumerator<VendingMachineComponent, MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var metadata, out var transform))
            {
                if (transform.MapID == loadedMap && metadata.EntityPrototype?.ID == YamlPricedVendingMachineProtoId)
                {
                    loadedVendor = uid;
                    break;
                }
            }

            Assert.That(loadedVendor, Is.Not.Null, "Reloaded map did not contain the vending machine.");
            Assert.That(GetCashSlotBalance(loadedVendor!.Value), Is.EqualTo(25), "Reloaded vending machine lost its cash escrow.");
            Assert.That(GetBankBalance(), Is.EqualTo(10), "Saving or loading moved escrow into the player's account.");
            MapSystem.DeleteMap(loadedMap);
        });
    }

    [Test]
    public async Task YamlPricedVendWithdrawsOnSuccess()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(10);
        await RunTicks(1);

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);
        const int expectedPrice = 7;

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(0), "Paid vend did not reduce stock.");
        await AssertEntityLookup(("APCBasic", 1), (PaidVendedItemProtoId, 1));
        Assert.That(GetBankBalance(), Is.EqualTo(10 - expectedPrice), "Paid vend did not withdraw the correct amount.");
    }

    [Test]
    public async Task StaticPriceFallbackUsedWhenYamlPriceMissing()
    {
        await SpawnTarget(StaticPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(20);
        await RunTicks(1);
        Assert.That(GetBankBalance(), Is.EqualTo(20), "Test account did not start at the requested balance.");
        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), "New vending machine inherited stale cash slot contents.");

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");
        Assert.That(GetCashSlotBalance(), Is.EqualTo(0), "Opening the UI inserted unexpected cash.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(0), "Vend did not reduce stock.");
        await AssertEntityLookup(("APCBasic", 1), (PaidVendedItemProtoId, 1));
        var pricing = SEntMan.System<PricingSystem>();
        var prototype = ProtoMan.Index<EntityPrototype>(PaidVendedItemProtoId);
        var expectedPrice = (int) Math.Ceiling(pricing.GetEstimatedPrice(prototype));
        Assert.That(GetBankBalance(), Is.EqualTo(20 - expectedPrice), "Vend did not use the item's static price fallback.");
    }

    [Test]
    public async Task DefaultPriceFallbackUsedWhenNoOtherPriceExists()
    {
        await SpawnTarget(DefaultPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(20);
        await RunTicks(1);

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, UnpricedVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(0), "Vend did not reduce stock.");
        await AssertEntityLookup(("APCBasic", 1), (UnpricedVendedItemProtoId, 1));
        Assert.That(GetBankBalance(), Is.EqualTo(11), "Vend did not use the YAML default price fallback.");
    }

    [Test]
    public async Task FreeVendDoesNotWithdraw()
    {
        await SpawnTarget(FreePricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(10);
        await RunTicks(1);

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(0), "Free vend did not reduce stock.");
        await AssertEntityLookup(("APCBasic", 1), (PaidVendedItemProtoId, 1));
        Assert.That(GetBankBalance(), Is.EqualTo(10), "Free vend should not withdraw funds.");
    }

    [Test]
    public async Task PaidVendUiStateShowsYamlPriceAndBalance()
    {
        await SpawnTarget(YamlPricedVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(10);
        await RunTicks(1);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        await Server.WaitPost(() =>
        {
            var uiSystem = SEntMan.System<UserInterfaceSystem>();
            Assert.That(uiSystem.TryGetUiState<VendingMachineUpdateState>((STarget!.Value, null), VendingMachineUiKey.Key, out var state), Is.True);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.RequiresCash, Is.True);
            Assert.That(state.Balance, Is.EqualTo(10));
            Assert.That(state.CashSlot, Is.EqualTo(0));
            Assert.That(state.Prices.TryGetValue(PaidVendedItemProtoId, out var price), Is.True);
            Assert.That(price, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task AccessLockedPaidVendDoesNotCharge()
    {
        await SpawnTarget(AccessLockedPaidVendingMachineProtoId);
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await SetBankBalance(100);
        await RunTicks(1);

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(1), "Access-locked paid vend should not reduce stock.");
        await AssertEntityLookup(("APCBasic", 1));
        Assert.That(GetBankBalance(), Is.EqualTo(100), "Access-locked paid vend should not charge funds.");
    }

    private async Task BreakVendor()
    {
        var damageableSys = SEntMan.System<DamageableSystem>();
        Assert.That(HasComp<DamageableComponent>(), $"{VendingMachineProtoId} does not have DamageableComponent.");
        Assert.That(damageableSys.GetAllDamage(STarget!.Value).GetTotal(), Is.EqualTo(FixedPoint2.Zero), $"{VendingMachineProtoId} started with unexpected damage.");

        // Damage the vending machine to the point that it breaks
        var damageType = ProtoMan.Index(TestDamageType);
        var damage = new DamageSpecifier(damageType, FixedPoint2.New(100));
        await Server.WaitPost(() => damageableSys.TryChangeDamage(SEntMan.GetEntity(Target).Value, damage, ignoreResistances: true));
        await RunTicks(5);
        Assert.That(damageableSys.GetAllDamage(STarget!.Value).GetTotal(), Is.GreaterThan(FixedPoint2.Zero), $"{VendingMachineProtoId} did not take damage.");
    }

    private async Task SetBankBalance(int amount)
    {
        await Server.WaitPost(() =>
        {
            var bank = SEntMan.System<BankSystem>();
            var ticker = SEntMan.System<GameTicker>();
            typeof(GameTicker).GetProperty(nameof(GameTicker.DefaultMap), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(ticker, MapData.MapId);

            var mapUid = MapSystem.GetMapOrInvalid(ticker.DefaultMap);
            SEntMan.EnsureComponent<MoneyAccountsComponent>(mapUid);
            var accountName = SEntMan.GetComponent<MetaDataComponent>(SPlayer).EntityName;
            bank.EnsureAccount(accountName, amount);

            if (!bank.TryGetBalance(SPlayer, out var currentBalance))
                return;

            var delta = amount - currentBalance;
            if (delta > 0)
                bank.TryBankDeposit(SPlayer, delta);
            else if (delta < 0)
                bank.TryBankWithdraw(SPlayer, -delta);
        });
    }

    private int GetBankBalance()
    {
        var bank = SEntMan.System<BankSystem>();
        Assert.That(bank.TryGetBalance(SPlayer, out var balance), "Player bank balance was unavailable.");
        return balance;
    }

    private int GetCashSlotBalance(EntityUid? vendor = null)
    {
        var slots = SEntMan.System<ItemSlotsSystem>();
        if (!slots.TryGetSlot(vendor ?? STarget!.Value, VendingMachineComponent.CashSlotId, out var cashSlot) ||
            cashSlot.Item is not { Valid: true } cash)
            return 0;

        return SEntMan.GetComponent<StackComponent>(cash).Count;
    }

    private int GetTotalCash()
    {
        return SEntMan.EntityQuery<CashComponent, StackComponent>()
            .Sum(entry => entry.Item2.Count);
    }

    private async Task AssertCashSlotUiState(int expected)
    {
        await Server.WaitPost(() =>
        {
            var uiSystem = SEntMan.System<UserInterfaceSystem>();
            Assert.That(uiSystem.TryGetUiState<VendingMachineUpdateState>((STarget!.Value, null), VendingMachineUiKey.Key, out var state), Is.True);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.CashSlot, Is.EqualTo(expected));
        });
    }
}
