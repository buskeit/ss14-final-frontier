using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.DeltaV.Cargo.Components;
using Content.Server.DeltaV.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Server.CartridgeLoader;
using Content.Shared.Cargo.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;

namespace Content.Server.DeltaV.CartridgeLoader.Cartridges;

public sealed class StockTradingCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StockTradingCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<StockMarketUpdatedEvent>(OnStockMarketUpdated);
        SubscribeLocalEvent<StationStockMarketComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<StockTradingCartridgeComponent, BankBalanceUpdatedEvent>(OnBalanceUpdated);
    }

    private void OnBalanceUpdated(Entity<StockTradingCartridgeComponent> ent, ref BankBalanceUpdatedEvent args)
    {
        UpdateAllCartridges(args.Station);
    }

    private void OnUiReady(Entity<StockTradingCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUI(ent, args.Loader);
    }

    private void OnStockMarketUpdated(ref StockMarketUpdatedEvent args)
    {
        UpdateAllCartridges(args.Station);
    }

    private void OnMapInit(Entity<StationStockMarketComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Companies.Count == 0)
        {
            ent.Comp.Companies.Add(new StockCompany { LocalizedDisplayName = "Nanotrasen", BasePrice = 1500f, CurrentPrice = 1500f });
            ent.Comp.Companies.Add(new StockCompany { LocalizedDisplayName = "CyberSun Industries", BasePrice = 1200f, CurrentPrice = 1200f });
            ent.Comp.Companies.Add(new StockCompany { LocalizedDisplayName = "Einstein Engines", BasePrice = 900f, CurrentPrice = 900f });
            ent.Comp.Companies.Add(new StockCompany { LocalizedDisplayName = "DeForest Biotech", BasePrice = 800f, CurrentPrice = 800f });
            ent.Comp.Companies.Add(new StockCompany { LocalizedDisplayName = "Waffle Corporation", BasePrice = 500f, CurrentPrice = 500f });
            ent.Comp.Companies.Add(new StockCompany { LocalizedDisplayName = "Donk Co.", BasePrice = 350f, CurrentPrice = 350f });
        }

        // Initialize price history for each company
        for (var i = 0; i < ent.Comp.Companies.Count; i++)
        {
            var company = ent.Comp.Companies[i];

            // Create initial price history using base price
            company.PriceHistory = new List<float>();
            for (var j = 0; j < 5; j++)
            {
                company.PriceHistory.Add(company.BasePrice);
            }

            ent.Comp.Companies[i] = company;
        }

        if (_station.GetOwningStation(ent.Owner) is { } station)
            UpdateAllCartridges(station);
    }

    private void UpdateAllCartridges(EntityUid station)
    {
        var query = EntityQueryEnumerator<StockTradingCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cartridge))
        {
            if (cartridge.LoaderUid is not { } loader || comp.Station != station)
                continue;
            UpdateUI((uid, comp), loader);
        }
    }

    private void UpdateUI(Entity<StockTradingCartridgeComponent> ent, EntityUid loader)
    {
        var station = _station.GetOwningStation(loader);
        if (station == null)
        {
            // Try to find any station with the stock market
            var query = EntityQueryEnumerator<StationStockMarketComponent>();
            if (query.MoveNext(out var fallbackStation, out _))
            {
                station = fallbackStation;
            }
            else
            {
                // Fallback for sandbox/test environments: use the grid or map of the loader as the "station"
                var transform = Transform(loader);
                if (transform.GridUid is { } gridUid)
                {
                    station = gridUid;
                }
                else if (transform.MapUid is { } mapUid)
                {
                    station = mapUid;
                }
            }
        }

        if (station != null)
        {
            ent.Comp.Station = station;
            var stockMarket = EnsureComp<StationStockMarketComponent>(station.Value);
            var bankAccount = EnsureComp<StationBankAccountComponent>(station.Value);

            if (stockMarket.Companies.Count == 0)
            {
                stockMarket.Companies.Add(new StockCompany { LocalizedDisplayName = "Nanotrasen", BasePrice = 1500f, CurrentPrice = 1500f });
                stockMarket.Companies.Add(new StockCompany { LocalizedDisplayName = "CyberSun Industries", BasePrice = 1200f, CurrentPrice = 1200f });
                stockMarket.Companies.Add(new StockCompany { LocalizedDisplayName = "Einstein Engines", BasePrice = 900f, CurrentPrice = 900f });
                stockMarket.Companies.Add(new StockCompany { LocalizedDisplayName = "DeForest Biotech", BasePrice = 800f, CurrentPrice = 800f });
                stockMarket.Companies.Add(new StockCompany { LocalizedDisplayName = "Waffle Corporation", BasePrice = 500f, CurrentPrice = 500f });
                stockMarket.Companies.Add(new StockCompany { LocalizedDisplayName = "Donk Co.", BasePrice = 350f, CurrentPrice = 350f });

                for (var i = 0; i < stockMarket.Companies.Count; i++)
                {
                    var company = stockMarket.Companies[i];
                    company.PriceHistory = new List<float>();
                    for (var j = 0; j < 5; j++)
                    {
                        company.PriceHistory.Add(company.BasePrice);
                    }
                    stockMarket.Companies[i] = company;
                }
            }
        }

        if (ent.Comp.Station == null ||
            !TryComp<StationStockMarketComponent>(ent.Comp.Station, out var activeStockMarket) ||
            !TryComp<StationBankAccountComponent>(ent.Comp.Station, out var activeBankAccount))
            return;

        // Send the UI state with balance and owned stocks
        var state = new StockTradingUiState(
            entries: activeStockMarket.Companies,
            ownedStocks: activeStockMarket.StockOwnership,
            balance: _cargo.GetBalanceFromAccount(new Entity<StationBankAccountComponent?>(ent.Comp.Station.Value, activeBankAccount), activeBankAccount.PrimaryAccount)
        );

        _cartridgeLoader.UpdateCartridgeUiState(loader, state);
    }
}
