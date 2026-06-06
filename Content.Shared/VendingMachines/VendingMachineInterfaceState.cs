using Robust.Shared.Serialization;
using Content.Shared.UserInterface;

namespace Content.Shared.VendingMachines
{
    [Serializable, NetSerializable]
    public sealed class VendingMachineUpdateState : BoundUserInterfaceState
    {
        public List<VendingMachineInventoryEntry> Inventory;
        public Dictionary<string, int> Prices;
        public bool RequiresCash;
        public int? Balance;

        public VendingMachineUpdateState(List<VendingMachineInventoryEntry> inventory, Dictionary<string, int> prices, bool requiresCash, int? balance = null)
        {
            Inventory = inventory;
            Prices = prices;
            RequiresCash = requiresCash;
            Balance = balance;
        }
    }

    [Serializable, NetSerializable]
    public sealed class VendingMachineEjectMessage : BoundUserInterfaceMessage
    {
        public readonly InventoryType Type;
        public readonly string ID;
        public VendingMachineEjectMessage(InventoryType type, string id)
        {
            Type = type;
            ID = id;
        }
    }

    [Serializable, NetSerializable]
    public enum VendingMachineUiKey
    {
        Key,
    }
}
