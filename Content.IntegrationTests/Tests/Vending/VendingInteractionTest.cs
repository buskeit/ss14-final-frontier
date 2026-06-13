using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server._NF.Bank;
using Content.Server.VendingMachines;
using Content.Shared.Cargo.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared._NF.Bank.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;
using System;
using System.Linq;
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
        Assert.That(GetBankBalance(), Is.EqualTo(25));
        Assert.That(SEntMan.EntityQuery<CashComponent>().Any(), Is.False);
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

        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        var vendorEnt = SEntMan.GetEntity(Target.Value);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "BUI failed to open.");

        await SendBui(VendingMachineUiKey.Key, new VendingMachineEjectMessage(InventoryType.Regular, PaidVendedItemProtoId));

        Assert.That(vendingSystem.GetAllInventory(vendorEnt).Single().Amount, Is.EqualTo(0), "Vend did not reduce stock.");
        await AssertEntityLookup(("APCBasic", 1), (PaidVendedItemProtoId, 1));
        Assert.That(GetBankBalance(), Is.EqualTo(9), "Vend did not use the item's static price fallback.");
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
}
