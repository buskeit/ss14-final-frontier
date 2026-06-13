using Content.Server._NF.Bank;
using Content.Server.Cargo.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Labels.Components;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Server.Cargo.Systems
{
    public sealed partial class CargoSystem
    {
        [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
        [Dependency] private readonly EmagSystem _emag = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly BankSystem _bank = default!;

        private void InitializeConsole()
        {
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleChangeAccountType>(OnChangeAccountType);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleAddOrderMessage>(OnAddOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleRemoveOrderMessage>(OnRemoveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleApproveOrderMessage>(OnApproveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleSelectTradeMessage>(OnSelectTrade);

            SubscribeLocalEvent<CargoOrderConsoleComponent, BoundUIOpenedEvent>(OnOrderUIOpened);
            SubscribeLocalEvent<CargoOrderConsoleComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<CargoOrderConsoleComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<CargoOrderConsoleComponent, GotEmaggedEvent>(OnEmagged);

            SubscribeLocalEvent<TradeStationComponent, ComponentStartup>(RegisterTS);
        }
        private void EnsureUniqueTradeStationUid(EntityUid uid, TradeStationComponent component)
        {
            var existingUids = new HashSet<int>();
            var query = EntityQueryEnumerator<TradeStationComponent>();
            while (query.MoveNext(out var otherUid, out var otherComp))
            {
                if (otherUid == uid)
                    continue;
                if (otherComp.UID > 0)
                {
                    existingUids.Add(otherComp.UID);
                }
            }

            if (component.UID <= 0 || existingUids.Contains(component.UID))
            {
                int tryUID = 1;
                while (existingUids.Contains(tryUID))
                {
                    tryUID++;
                }
                component.UID = tryUID;
            }
        }

        private void RegisterTS(EntityUid uid, TradeStationComponent component, ref ComponentStartup args)
        {
            EnsureUniqueTradeStationUid(uid, component);
        }

        public EntityUid? GetTradeStationByID(int uid)
        {
            var stations = new List<EntityUid>();
            var query = EntityQueryEnumerator<TradeStationComponent>();
            while (query.MoveNext(out var ent, out var comp))
            {
                if (comp.UID == uid) return ent;
            }

            return null;
        }
        private void OnInteractUsingCash(EntityUid uid, CargoOrderConsoleComponent component, ref InteractUsingEvent args)
        {
            var price = _pricing.GetPrice(args.Used);

            if (price == 0)
                return;

            var stationUid = _station.GetOwningStation(uid);

            if (!TryComp(stationUid, out StationBankAccountComponent? bank))
                return;

            _audio.PlayPvs(ApproveSound, uid);
            UpdateBankAccount((stationUid.Value, bank), (int)price, component.Account);
            QueueDel(args.Used);
            args.Handled = true;
        }

        private void OnInteractUsingSlip(Entity<CargoOrderConsoleComponent> ent, ref InteractUsingEvent args, CargoSlipComponent slip)
        {
            if (slip.OrderQuantity <= 0)
                return;

            var stationUid = _station.GetOwningStation(ent);

            if (!TryGetOrderDatabase(stationUid, out var orderDatabase))
                return;

            if (!_protoMan.TryIndex(slip.Product, out var product))
            {
                Log.Error($"Tried to add invalid cargo product {slip.Product} as order!");
                return;
            }

            if (!ent.Comp.AllowedGroups.Contains(product.Group))
                return;

            var orderId = GenerateOrderId(orderDatabase);
            var data = new CargoOrderData(orderId, product, slip.OrderQuantity, slip.Requester, slip.Reason, slip.Account);

            if (!TryAddOrder(stationUid.Value, ent.Comp.Account, data, orderDatabase))
            {
                PlayDenySound(ent, ent.Comp);
                return;
            }

            // Log order addition
            _audio.PlayPvs(ent.Comp.ScanSound, ent);
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(args.User):user} inserted order slip [orderId:{data.OrderId}, quantity:{data.OrderQuantity}, product:{data.Product}, requester:{data.Requester}, reason:{data.Reason}]");
            QueueDel(args.Used);
            args.Handled = true;
        }

        private void OnInteractUsing(EntityUid uid, CargoOrderConsoleComponent component, ref InteractUsingEvent args)
        {
            if (HasComp<CashComponent>(args.Used))
            {
                OnInteractUsingCash(uid, component, ref args);
            }
            else if (TryComp<CargoSlipComponent>(args.Used, out var slip) && component.Mode == CargoOrderConsoleMode.DirectOrder)
            {
                OnInteractUsingSlip((uid, component), ref args, slip);
            }
        }

        private void OnInit(EntityUid uid, CargoOrderConsoleComponent orderConsole, ComponentInit args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, station);
        }

        private void OnEmagged(Entity<CargoOrderConsoleComponent> ent, ref GotEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (_emag.CheckFlag(ent, EmagType.Interaction))
                return;

            args.Handled = true;
        }

        private void UpdateConsole()
        {
            var stationQuery = EntityQueryEnumerator<StationBankAccountComponent>();
            while (stationQuery.MoveNext(out var uid, out var bank))
            {
                if (Timing.CurTime < bank.NextIncomeTime)
                    continue;
                bank.NextIncomeTime += bank.IncomeDelay;

                var balanceToAdd = (int)Math.Round(bank.IncreasePerSecond * bank.IncomeDelay.TotalSeconds);
                UpdateBankAccount((uid, bank), balanceToAdd, bank.RevenueDistribution);
            }
        }

        #region Interface


        private void OnSelectTrade(EntityUid uid, CargoBountyConsoleComponent component, CargoConsoleSelectTradeMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;
            component.SelectedTradeGrid = args.TradeUID;
            UiUpdate(uid, component);
        }

        private void OnSelectTrade(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleSelectTradeMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;
            component.SelectedTradeGrid = args.TradeUID;
            var station = _station.GetOwningStation(uid);
            if (station != null)
                UpdateOrders(station.Value);
            else
                UpdateOrderState(uid, null);
        }

        private static List<CargoOrderData> GetOrCreateOrderList(StationCargoOrderDatabaseComponent db, ProtoId<CargoAccountPrototype> account)
        {
            if (!db.Orders.TryGetValue(account, out var list))
            {
                list = new List<CargoOrderData>();
                db.Orders[account] = list;
            }
            return list;
        }

        private void OnApproveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleApproveOrderMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;

            if (component.Mode != CargoOrderConsoleMode.DirectOrder)
                return;

            if (component.PersonalAccountMode)
            {
                var station = _station.GetOwningStation(uid);
                // No station to deduct from.
                if (!TryComp(station, out StationBankAccountComponent? bank) ||
                    !TryComp(station, out StationDataComponent? stationData) ||
                    !TryGetOrderDatabase(station, out var orderDatabase))
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-station-not-found"));
                    PlayDenySound(uid, component);
                    return;
                }
                // Find our order again. It might have been dispatched or approved already
                var order = GetOrCreateOrderList(orderDatabase, component.Account).Find(order => args.OrderId == order.OrderId && !order.Approved);
                if (order == null || !_protoMan.Resolve(order.Account, out var account))
                {
                    return;
                }
                // Invalid order
                if (!_protoMan.Resolve(order.Product, out var product))
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-invalid-product"));
                    PlayDenySound(uid, component);
                    return;
                }

                var amount = GetOutstandingOrderCount((station.Value, orderDatabase), order.Account);
                var capacity = orderDatabase.Capacity;

                // Too many orders, avoid them getting spammed in the UI.
                if (amount >= capacity)
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-too-many"));
                    PlayDenySound(uid, component);
                    return;
                }

                // Cap orders so someone can't spam thousands.
                var cappedAmount = Math.Min(capacity - amount, order.OrderQuantity);

                if (cappedAmount != order.OrderQuantity)
                {
                    order.OrderQuantity = cappedAmount;
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-snip-snip"));
                    PlayDenySound(uid, component);
                }
                var cost = product.Cost * order.OrderQuantity;
                var taxRate = order.Tax;
                var taxPaid = (int)Math.Round((float)cost * ((float)taxRate / 100f));
                cost += taxPaid;

                int accountBalance = 0;
                _bank.TryGetBalance(args.Actor, out accountBalance);

                // Not enough balance
                if (cost > accountBalance)
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)));
                    PlayDenySound(uid, component);
                    return;
                }

                var ev = new FulfillCargoOrderEvent((station.Value, stationData), order, (uid, component));
                RaiseLocalEvent(ref ev);
                ev.FulfillmentEntity ??= station.Value;

                if (!ev.Handled)
                {
                    ev.FulfillmentEntity = TryFulfillOrder((station.Value, stationData), order.Account, order, orderDatabase, Name(args.Actor));

                    if (ev.FulfillmentEntity == null)
                    {
                        ConsolePopup(args.Actor, Loc.GetString("cargo-console-unfulfilled"));
                        PlayDenySound(uid, component);
                        return;
                    }
                }
                if (!_bank.TryBankWithdraw(args.Actor, cost))
                {
                    ConsolePopup(args.Actor, "Withdraw error!");
                    PlayDenySound(uid, component);
                    return;
                }
                if (taxPaid > 0)
                {
                    var oStation = GetTradeStationByID(order.TradeStation);
                    if (oStation != null)
                    {
                        var owningStation = _station.GetOwningStation(oStation.Value);
                        if (owningStation != null)
                        {
                            TryComp<StationBankAccountComponent>(owningStation, out var owningBank);
                            UpdateBankAccount((owningStation.Value, owningBank), taxPaid, order.Account);
                        }
                    }
                }
                order.Approved = true;
                _audio.PlayPvs(ApproveSound, uid);

                ConsolePopup(args.Actor, Loc.GetString("cargo-console-trade-station", ("destination", MetaData(ev.FulfillmentEntity.Value).EntityName)));

                // Log order approval
                _adminLogger.Add(LogType.Action,
                    LogImpact.Low,
                    $"{ToPrettyString(player):user} approved order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.Product}, requester:{order.Requester}, reason:{order.Reason}] on account {order.Account} with balance at {accountBalance}");

                GetOrCreateOrderList(orderDatabase, component.Account).Remove(order);
                UpdateOrders(station.Value);

            }
            else
            {
                var station = _station.GetOwningStation(uid);

                // No station to deduct from.
                if (!TryComp(station, out StationBankAccountComponent? bank) ||
                    !TryComp(station, out StationDataComponent? stationData) ||
                    !TryGetOrderDatabase(station, out var orderDatabase))
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-station-not-found"));
                    PlayDenySound(uid, component);
                    return;
                }
                // Find our order again. It might have been dispatched or approved already
                var order = GetOrCreateOrderList(orderDatabase, component.Account).Find(order => args.OrderId == order.OrderId && !order.Approved);
                if (order == null || !_protoMan.Resolve(order.Account, out var account))
                {
                    return;
                }
                // Invalid order
                if (!_protoMan.Resolve(order.Product, out var product))
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-invalid-product"));
                    PlayDenySound(uid, component);
                    return;
                }
                var cost = product.Cost * order.OrderQuantity;
                if (!_accessReaderSystem.IsAllowed(player, uid) || !_accessReaderSystem.CanSpend(player, uid, null, cost))
                {
                    ConsolePopup(args.Actor, "Insufficent Spending Limit");
                    PlayDenySound(uid, component);
                    return;
                }
                
                var amount = GetOutstandingOrderCount((station.Value, orderDatabase), order.Account);
                var capacity = orderDatabase.Capacity;

                // Too many orders, avoid them getting spammed in the UI.
                if (amount >= capacity)
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-too-many"));
                    PlayDenySound(uid, component);
                    return;
                }

                // Cap orders so someone can't spam thousands.
                var cappedAmount = Math.Min(capacity - amount, order.OrderQuantity);

                if (cappedAmount != order.OrderQuantity)
                {
                    order.OrderQuantity = cappedAmount;
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-snip-snip"));
                    PlayDenySound(uid, component);
                }


                var taxRate = order.Tax;

                var oStation = GetTradeStationByID(order.TradeStation);
                if (oStation != null)
                {
                    var owningStation = _station.GetOwningStation(oStation.Value);
                    if (owningStation != null)
                    {
                        if (owningStation == station) taxRate = 0;
                    }
                }
                var taxPaid = (int)Math.Round((float)cost * ((float)taxRate / 100f));
                cost += taxPaid;
                var accountBalance = GetBalanceFromAccount((station.Value, bank), order.Account);

                // Not enough balance
                if (cost > accountBalance)
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)));
                    PlayDenySound(uid, component);
                    return;
                }

                var ev = new FulfillCargoOrderEvent((station.Value, stationData), order, (uid, component));
                RaiseLocalEvent(ref ev);
                ev.FulfillmentEntity ??= station.Value;

                if (!ev.Handled)
                {
                    ev.FulfillmentEntity = TryFulfillOrder((station.Value, stationData), order.Account, order, orderDatabase);

                    if (ev.FulfillmentEntity == null)
                    {
                        ConsolePopup(args.Actor, Loc.GetString("cargo-console-unfulfilled"));
                        PlayDenySound(uid, component);
                        return;
                    }
                }

                order.Approved = true;
                _audio.PlayPvs(ApproveSound, uid);

                if (!_emag.CheckFlag(uid, EmagType.Interaction))
                {
                    var tryGetIdentityShortInfoEvent = new TryGetIdentityShortInfoEvent(uid, player);
                    RaiseLocalEvent(tryGetIdentityShortInfoEvent);
                    order.SetApproverData(tryGetIdentityShortInfoEvent.Title);

                    var message = Loc.GetString("cargo-console-unlock-approved-order-broadcast",
                        ("productName", Loc.GetString(product.Name)),
                        ("orderAmount", order.OrderQuantity),
                        ("approver", order.Approver ?? string.Empty),
                        ("cost", cost));
                    _radio.SendRadioMessage(uid, message, account.RadioChannel, uid, escapeMarkup: false);
                    if (CargoOrderConsoleComponent.BaseAnnouncementChannel != account.RadioChannel)
                        _radio.SendRadioMessage(uid, message, CargoOrderConsoleComponent.BaseAnnouncementChannel, uid, escapeMarkup: false);
                }

                ConsolePopup(args.Actor, Loc.GetString("cargo-console-trade-station", ("destination", MetaData(ev.FulfillmentEntity.Value).EntityName)));

                // Log order approval
                _adminLogger.Add(LogType.Action,
                    LogImpact.Low,
                    $"{ToPrettyString(player):user} approved order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.Product}, requester:{order.Requester}, reason:{order.Reason}] on account {order.Account} with balance at {accountBalance}");

                GetOrCreateOrderList(orderDatabase, component.Account).Remove(order);
                UpdateBankAccount((station.Value, bank), -cost, order.Account);
                var idName = _accessReaderSystem.GetIdName(player);
                if (idName != null)
                {
                    _station.TrackSpending(idName, station.Value, cost);
                }
                if (taxPaid > 0)
                {
                    var owStation = GetTradeStationByID(order.TradeStation);
                    if (owStation != null)
                    {
                        var owningStation = _station.GetOwningStation(owStation.Value);
                        if (owningStation != null)
                        {
                            TryComp<StationBankAccountComponent>(owningStation, out var owningBank);
                            UpdateBankAccount((owningStation.Value, owningBank), taxPaid, order.Account);
                        }
                    }
                }
                UpdateOrders(station.Value);
            }

        }

        private EntityUid? TryFulfillOrder(Entity<StationDataComponent> stationData, ProtoId<CargoAccountPrototype> account, CargoOrderData order, StationCargoOrderDatabaseComponent orderDatabase, string? personalAccount = null)
        {
            var trade = GetTradeStationByID(order.TradeStation);
            if (trade == null) return null;
            EntityUid? tradeDestination = null;
            var tradePads = GetCargoPallets(trade.Value, BuySellType.Buy);
            _random.Shuffle(tradePads);

            var freePads = GetFreeCargoPallets(trade.Value, tradePads);
            if (freePads.Count >= order.OrderQuantity) //check if the station has enough free pallets
            {
                foreach (var pad in freePads)
                {
                    var coordinates = new EntityCoordinates(trade.Value, pad.Transform.LocalPosition);

                    if (FulfillOrder(order, account, coordinates, orderDatabase.PrinterOutput, personalAccount))
                    {
                        tradeDestination = trade;
                        order.NumDispatched++;
                        if (order.OrderQuantity <= order.NumDispatched) //Spawn a crate on free pellets until the order is fulfilled.
                            break;
                    }
                }
            }

            return tradeDestination;
        }

        private void GetTradeStations(StationDataComponent data, ref List<EntityUid> ents)
        {
            foreach (var gridUid in data.Grids)
            {
                if (!_tradeQuery.HasComponent(gridUid))
                    continue;

                ents.Add(gridUid);
            }
        }

        private void GetAllTradeStations(ref Dictionary<int, string> ents, EntityUid? owningStation, out int ownedTradeStation)
        {
            ownedTradeStation = 0;
            var query = EntityQueryEnumerator<TradeStationComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                EnsureUniqueTradeStationUid(uid, comp);
                var station = _station.GetOwningStation(uid);
                int? stationUID = null;
                if (owningStation != null && station == owningStation) ownedTradeStation = comp.UID;
                if (TryComp<StationDataComponent>(station, out var sD) && sD != null)
                {
                    stationUID = sD.UID;
                }
                ents[comp.UID] = Name(uid);
            }
        }

        private void OnRemoveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleRemoveOrderMessage args)
        {
            var station = _station.GetOwningStation(uid);

            if (component.Mode != CargoOrderConsoleMode.DirectOrder)
                return;

            if (!TryGetOrderDatabase(station, out var orderDatabase))
                return;

            RemoveOrder(station.Value, component.Account, args.OrderId, orderDatabase);
        }

        private void OnAddOrderMessageSlipPrinter(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleAddOrderMessage args, CargoProductPrototype product)
        {
            if (!_protoMan.Resolve(component.Account, out var account))
                return;

            if (Timing.CurTime < component.NextPrintTime)
                return;

            var label = Spawn(account.AcquisitionSlip, Transform(uid).Coordinates);
            component.NextPrintTime = Timing.CurTime + component.PrintDelay;
            _audio.PlayPvs(component.PrintSound, uid);

            var paper = EnsureComp<PaperComponent>(label);
            var msg = new FormattedMessage();

            msg.AddMarkupPermissive(Loc.GetString("cargo-acquisition-slip-body",
                ("product", product.Name),
                ("description", product.Description),
                ("unit", product.Cost),
                ("amount", args.Amount),
                ("cost", product.Cost * args.Amount),
                ("orderer", args.Requester),
                ("reason", args.Reason)));
            _paperSystem.SetContent((label, paper), msg.ToMarkup());

            var slip = EnsureComp<CargoSlipComponent>(label);
            slip.Product = product.ID;
            slip.Requester = args.Requester;
            slip.Reason = args.Reason;
            slip.OrderQuantity = args.Amount;
            slip.Account = component.Account;
        }

        private void OnChangeAccountType(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleChangeAccountType args)
        {
            if (args.Actor is not { Valid: true } player)
                return;
            component.PersonalAccountMode = !component.PersonalAccountMode;
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, station);
        }
        private void OnAddOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleAddOrderMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;

            if (args.Amount <= 0)
                return;

            if (component.SelectedTradeGrid == 0)
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-unfulfilled"));
                PlayDenySound(uid, component);
                return;
            }

            var stationUid = _station.GetOwningStation(uid);

            if (stationUid == null || !TryGetOrderDatabase(stationUid, out var orderDatabase))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-station-not-found"));
                PlayDenySound(uid, component);
                return;
            }

            if (!TryComp<StationBankAccountComponent>(stationUid, out var bank))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-station-not-found"));
                PlayDenySound(uid, component);
                return;
            }

            if (!_protoMan.TryIndex<CargoProductPrototype>(args.CargoProductId, out var product))
            {
                Log.Error($"Tried to add invalid cargo product {args.CargoProductId} as order!");
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-invalid-product"));
                PlayDenySound(uid, component);
                return;
            }

            if (!GetAvailableProducts((uid, component)).Contains(args.CargoProductId))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-invalid-product"));
                PlayDenySound(uid, component);
                return;
            }

            if (component.Mode == CargoOrderConsoleMode.PrintSlip)
            {
                OnAddOrderMessageSlipPrinter(uid, component, args, product);
                return;
            }

            var targetAccount = component.Mode == CargoOrderConsoleMode.SendToPrimary ? bank.PrimaryAccount : component.Account;

            var data = GetOrderData(args, product, GenerateOrderId(orderDatabase), component.Account);
            data.TradeStation = component.SelectedTradeGrid;
            var oStation = GetTradeStationByID(component.SelectedTradeGrid);
            if (oStation == null)
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-unfulfilled"));
                PlayDenySound(uid, component);
                return;
            }
            var owningStation = _station.GetOwningStation(oStation.Value);
            int tax = 25;
            if (owningStation != null)
            {
                if (TryComp<StationDataComponent>(owningStation, out var oSD) && oSD != null)
                {
                    tax = oSD.ImportTax;
                }
            }
            data.Tax = tax;
            if (!TryAddOrder(stationUid.Value, targetAccount, data, orderDatabase))
            {
                PlayDenySound(uid, component);
                return;
            }

            // Log order addition
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(player):user} added order [orderId:{data.OrderId}, quantity:{data.OrderQuantity}, product:{data.Product}, requester:{data.Requester}, reason:{data.Reason}]");

        }

        private void OnOrderUIOpened(EntityUid uid, CargoOrderConsoleComponent component, BoundUIOpenedEvent args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, station);
        }

        #endregion

        private void UpdateOrderState(EntityUid consoleUid, EntityUid? station)
        {
            if (!TryComp<CargoOrderConsoleComponent>(consoleUid, out var console))
                return;

            TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDatabase);
            int tax = 25;
            var oStation = GetTradeStationByID(console.SelectedTradeGrid);
            var ownedTrade = oStation != null ? _station.GetOwningStation(oStation.Value) : null;
            if (ownedTrade != null)
            {
                if (TryComp<StationDataComponent>(ownedTrade, out var sD) && sD != null)
                {
                    tax = sD.ImportTax;
                }
            }

            Dictionary<int, string> possibleTrades = new();
            int ownedTradeStation = 0;
            if (station != null)
            {
                GetAllTradeStations(ref possibleTrades, station.Value, out ownedTradeStation);
            }
            else
            {
                GetAllTradeStations(ref possibleTrades, null, out ownedTradeStation);
            }

            if (console.SelectedTradeGrid != 0)
            {
                var tstation = GetTradeStationByID(console.SelectedTradeGrid);
                if (tstation == null || !HasComp<TradeStationComponent>(tstation))
                {
                    console.SelectedTradeGrid = 0;
                }
            }

            if (console.SelectedTradeGrid == 0)
            {
                if (ownedTradeStation != 0)
                {
                    console.SelectedTradeGrid = ownedTradeStation;
                }
                else if (possibleTrades.Count > 0)
                {
                    console.SelectedTradeGrid = possibleTrades.Keys.First();
                }
            }

            int? selectedTrade = null;
            if (console.SelectedTradeGrid != 0)
            {
                var tstation = GetTradeStationByID(console.SelectedTradeGrid);
                if (TryComp<TradeStationComponent>(tstation, out var comp) && comp != null)
                {
                    selectedTrade = comp.UID;
                }
            }

            if (_uiSystem.HasUi(consoleUid, CargoConsoleUiKey.Orders))
            {
                var stationName = station != null ? MetaData(station.Value).EntityName : MetaData(consoleUid).EntityName;
                var count = (station != null && orderDatabase != null) ? GetOutstandingOrderCount((station.Value, orderDatabase), console.Account) : 0;
                var capacity = orderDatabase != null ? orderDatabase.Capacity : 0;
                var stationNetEnt = station != null ? GetNetEntity(station.Value) : NetEntity.Invalid;
                var orders = (station != null && orderDatabase != null) ? RelevantOrders((station.Value, orderDatabase), (consoleUid, console)) : new List<CargoOrderData>();

                _uiSystem.SetUiState(consoleUid,
                    CargoConsoleUiKey.Orders,
                    new CargoConsoleInterfaceState(
                        stationName,
                        count,
                        capacity,
                        stationNetEnt,
                        orders,
                        GetAvailableProducts((consoleUid, console)),
                        possibleTrades,
                        selectedTrade,
                        console.PersonalAccountMode,
                        tax,
                        ownedTradeStation
                    ));
            }
        }

        /// <summary>
        /// Gets orders relevant to this account, i.e. orders on the account directly or orders on behalf of the account in the primary account.
        /// </summary>
        private List<CargoOrderData> RelevantOrders(Entity<StationCargoOrderDatabaseComponent> station, Entity<CargoOrderConsoleComponent> console)
        {
            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                return [];

            var ourOrders = GetOrCreateOrderList(station.Comp, console.Comp.Account);

            if (console.Comp.Account == bank.PrimaryAccount)
                return ourOrders;

            var otherOrders = GetOrCreateOrderList(station.Comp, bank.PrimaryAccount).Where(order => order.Account == console.Comp.Account);

            return ourOrders.Concat(otherOrders).ToList();
        }

        private void ConsolePopup(EntityUid actor, string text)
        {
            _popup.PopupCursor(text, actor);
        }

        private void PlayDenySound(EntityUid uid, CargoOrderConsoleComponent component)
        {
            if (_timing.CurTime >= component.NextDenySoundTime)
            {
                component.NextDenySoundTime = _timing.CurTime + component.DenySoundDelay;
                _audio.PlayPvs(_audio.ResolveSound(component.ErrorSound), uid);
            }
        }

        private static CargoOrderData GetOrderData(CargoConsoleAddOrderMessage args, CargoProductPrototype cargoProduct, int id, ProtoId<CargoAccountPrototype> account)
        {

            return new CargoOrderData(id, cargoProduct, args.Amount, args.Requester, args.Reason, account);
        }

        public int GetOutstandingOrderCount(Entity<StationCargoOrderDatabaseComponent> station, ProtoId<CargoAccountPrototype> account)
        {
            var amount = 0;

            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                return amount;

            foreach (var order in GetOrCreateOrderList(station.Comp, account))
            {
                if (!order.Approved)
                    continue;
                amount += order.OrderQuantity - order.NumDispatched;
            }

            if (account == bank.PrimaryAccount)
                return amount;

            foreach (var order in GetOrCreateOrderList(station.Comp, bank.PrimaryAccount))
            {
                if (order.Account != account)
                    continue;
                if (!order.Approved)
                    continue;
                amount += order.OrderQuantity - order.NumDispatched;
            }

            return amount;
        }

        /// <summary>
        /// Updates all of the cargo-related consoles for a particular station.
        /// This should be called whenever orders change.
        /// </summary>
        private void UpdateOrders(EntityUid dbUid)
        {
            // Order added so all consoles need updating.
            var orderQuery = AllEntityQuery<CargoOrderConsoleComponent>();

            while (orderQuery.MoveNext(out var uid, out var _))
            {
                var station = _station.GetOwningStation(uid);
                if (station != dbUid)
                    continue;

                UpdateOrderState(uid, station);
            }
        }

        public bool AddAndApproveOrder(
            EntityUid dbUid,
            CargoProductPrototype product,
            int qty,
            string sender,
            string description,
            string dest,
            StationCargoOrderDatabaseComponent component,
            ProtoId<CargoAccountPrototype> account,
            Entity<StationDataComponent> stationData
        )
        {
            // Make an order
            var id = GenerateOrderId(component);
            var order = new CargoOrderData(id, product, qty, sender, description, account);

            // Approve it now
            order.SetApproverData(dest, sender);
            order.Approved = true;

            // Log order addition
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"AddAndApproveOrder {description} added order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.Product}, requester:{order.Requester}, reason:{order.Reason}]");

            // Add it to the list
            return TryAddOrder(dbUid, account, order, component) && TryFulfillOrder(stationData, account, order, component).HasValue;
        }

        private bool TryAddOrder(EntityUid dbUid, ProtoId<CargoAccountPrototype> account, CargoOrderData data, StationCargoOrderDatabaseComponent component)
        {
            GetOrCreateOrderList(component, account).Add(data);
            UpdateOrders(dbUid);
            return true;
        }

        private static int GenerateOrderId(StationCargoOrderDatabaseComponent orderDB)
        {
            // We need an arbitrary unique ID to identify orders, since they may
            // want to be cancelled later.
            return ++orderDB.NumOrdersCreated;
        }

        public void RemoveOrder(EntityUid dbUid, ProtoId<CargoAccountPrototype> account, int index, StationCargoOrderDatabaseComponent orderDB)
        {
            var list = GetOrCreateOrderList(orderDB, account);
            var sequenceIdx = list.FindIndex(order => order.OrderId == index);
            if (sequenceIdx != -1)
            {
                list.RemoveAt(sequenceIdx);
            }
            UpdateOrders(dbUid);
        }

        public void ClearOrders(StationCargoOrderDatabaseComponent component)
        {
            if (component.Orders.Count == 0)
                return;

            component.Orders.Clear();
        }

        private static bool PopFrontOrder(StationCargoOrderDatabaseComponent orderDB, ProtoId<CargoAccountPrototype> account, [NotNullWhen(true)] out CargoOrderData? orderOut)
        {
            var list = GetOrCreateOrderList(orderDB, account);
            var orderIdx = list.FindIndex(order => order.Approved);
            if (orderIdx == -1)
            {
                orderOut = null;
                return false;
            }

            orderOut = list[orderIdx];
            orderOut.NumDispatched++;

            if (orderOut.NumDispatched >= orderOut.OrderQuantity)
            {
                // Order is complete. Remove from the queue.
                list.RemoveAt(orderIdx);
            }
            return true;
        }

        /// <summary>
        /// Tries to fulfill the next outstanding order.
        /// </summary>
        [PublicAPI]
        private bool FulfillNextOrder(StationCargoOrderDatabaseComponent orderDB, ProtoId<CargoAccountPrototype> account, EntityCoordinates spawn, string? paperProto)
        {
            if (!PopFrontOrder(orderDB, account, out var order))
                return false;

            return FulfillOrder(order, account, spawn, paperProto);
        }

        /// <summary>
        /// Fulfills the specified cargo order and spawns paper attached to it.
        /// </summary>
        private bool FulfillOrder(CargoOrderData order, ProtoId<CargoAccountPrototype> account, EntityCoordinates spawn, string? paperProto, string? personalAccount = null)
        {
            if (!_protoMan.Resolve(order.Product, out var product))
                return false;

            // Create the item itself
            var item = Spawn(product.Product, spawn);
            var itemXForm = Transform(item);

            // Ensure the item doesn't start anchored
            _transformSystem.Unanchor(item, itemXForm);

            // Spawn container and insert the item into it if a container is defined.
            if (product.Container is { } productContainer)
            {
                var containerEntity = Spawn(productContainer.Entity, itemXForm.Coordinates);
                _transformSystem.SetLocalRotation(containerEntity, itemXForm.LocalRotation);

                if (!_container.TryGetContainer(containerEntity, productContainer.ContainerId, out var container1) ||
                    !_container.Insert(item, container1, force: true))
                {
                    DebugTools.Assert(
                        $"Failed to insert cargo product into its specified container. This indicates an error in the cargo product definition's YAML as the product should be insertable into its container. {nameof(CargoProductPrototype)}: {(ProtoId<CargoProductPrototype>)order.Product.Id}");
                    QueueDel(containerEntity);
                }
                else
                {
                    item = containerEntity;
                }
            }

            // Create a sheet of paper to write the order details on
            var printed = Spawn(paperProto, spawn);
            if (TryComp<PaperComponent>(printed, out var paper))
            {
                // fill in the order data
                var val = Loc.GetString("cargo-console-paper-print-name", ("orderNumber", order.OrderId));
                _metaSystem.SetEntityName(printed, val);

                var accountProto = _protoMan.Index(account);
                var paccount = Loc.GetString(accountProto.Name);
                var paccountcode = Loc.GetString(accountProto.Code);

                if (personalAccount != null)
                {
                    paccount = personalAccount;
                    paccountcode = "[YOU]";

                }

                _paperSystem.SetContent((printed, paper),
                    Loc.GetString(
                        "cargo-console-paper-print-text",
                        ("orderNumber", order.OrderId),
                        ("itemName", product.Name),
                        ("orderQuantity", order.OrderQuantity),
                        ("requester", order.Requester),
                        ("reason", string.IsNullOrWhiteSpace(order.Reason) ? Loc.GetString("cargo-console-paper-reason-default") : order.Reason),
                        ("account", paccount),
                        ("accountcode", paccountcode),
                        ("approver", string.IsNullOrWhiteSpace(order.Approver) ? Loc.GetString("cargo-console-paper-approver-default") : order.Approver)));

                // attempt to attach the label to the item
                if (TryComp<PaperLabelComponent>(item, out var label))
                {
                    _slots.TryInsert(item, label.LabelSlot, printed, null);
                }
            }

            return true;

        }

        public List<ProtoId<CargoProductPrototype>> GetAvailableProducts(Entity<CargoOrderConsoleComponent> ent)
        {
            if (_station.GetOwningStation(ent) is not { } station ||
                !TryComp<StationCargoOrderDatabaseComponent>(station, out var db))
            {
                return new List<ProtoId<CargoProductPrototype>>();
            }
            if (ent.Comp.SelectedTradeGrid == 0) return new List<ProtoId<CargoProductPrototype>>();
            var oStation = GetTradeStationByID(ent.Comp.SelectedTradeGrid);
            if (oStation == null) return new List<ProtoId<CargoProductPrototype>>();
            if (!TryComp<TradeStationComponent>(oStation, out var tradeComp) || tradeComp == null) return new List<ProtoId<CargoProductPrototype>>();

            var products = new List<ProtoId<CargoProductPrototype>>();

            // Note that a market must be both on the station and on the console to be available.
            var markets = tradeComp.Markets; //ent.Comp.AllowedGroups.Intersect(tradeComp.Markets).ToList();
            InfrastructureLevelPrototype? levelProto = GetTradeStationLevel(oStation.Value, tradeComp);
            if (levelProto != null)
            {
                markets = levelProto.Markets;
            }
            foreach (var product in _protoMan.EnumeratePrototypes<CargoProductPrototype>())
            {
                if (!markets.Contains(product.Group))
                    continue;

                products.Add(product.ID);
            }

            return products;
        }

        #region Station

        private bool TryGetOrderDatabase([NotNullWhen(true)] EntityUid? stationUid, [MaybeNullWhen(false)] out StationCargoOrderDatabaseComponent dbComp)
        {
            return TryComp(stationUid, out dbComp);
        }

        #endregion
    }
}
