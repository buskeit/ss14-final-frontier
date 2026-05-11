using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.BUI;

[NetSerializable, Serializable]
public sealed class CargoConsoleInterfaceState : BoundUserInterfaceState
{
    public string Name;
    public int Count;
    public int Capacity;
    public NetEntity Station;
    public List<CargoOrderData> Orders;
    public List<ProtoId<CargoProductPrototype>> Products;
    public bool PersonalMode = false;
    public int Tax;
    public Dictionary<int, string> PossibleTrades;
    public int? SelectedTrade;
    public int? OwnedTrade;

    public CargoConsoleInterfaceState(string name, int count, int capacity, NetEntity station, List<CargoOrderData> orders, List<ProtoId<CargoProductPrototype>> products, Dictionary<int, string> possibleTrades, int? selectedTrade, bool personalMode = false, int tax = 0, int? ownedTrade = 0)
    {
        Name = name;
        Count = count;
        Capacity = capacity;
        Station = station;
        Orders = orders;
        Products = products;
        PossibleTrades = possibleTrades;
        SelectedTrade = selectedTrade;
        PersonalMode = personalMode;
        Tax = tax;
        OwnedTrade = ownedTrade;
    }
}
