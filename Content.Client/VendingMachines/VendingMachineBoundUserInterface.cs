using Content.Client.UserInterface.Controls;
using Content.Client.VendingMachines.UI;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using System.Linq;

namespace Content.Client.VendingMachines
{
    public sealed class VendingMachineBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private VendingMachineMenu? _menu;

        [ViewVariables]
        private List<VendingMachineInventoryEntry> _cachedInventory = new();
        private Dictionary<string, int> _prices = new();
        private bool _requiresCash;
        private int? _balance;
        private int _cashSlot;

        public VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _menu = this.CreateWindowCenteredLeft<VendingMachineMenu>();
            _menu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
            _menu.OnItemSelected += OnItemSelected;
            _menu.OnCashEject += OnCashEject;
        }

        public void Refresh()
        {
            var enabled = EntMan.TryGetComponent(Owner, out VendingMachineComponent? bendy) && !bendy.Ejecting;
            _menu?.Populate(_cachedInventory, _prices, _requiresCash, _balance, _cashSlot, enabled);
        }

        public void UpdateAmounts()
        {
            var enabled = EntMan.TryGetComponent(Owner, out VendingMachineComponent? bendy) && !bendy.Ejecting;
            _menu?.UpdateAmounts(_cachedInventory, _prices, _requiresCash, _balance, _cashSlot, enabled);
        }

        private void OnCashEject()
        {
            SendMessage(new VendingMachineEjectCashMessage());
        }

        private void OnItemSelected(GUIBoundKeyEventArgs args, ListData data)
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            if (data is not VendorItemsListData { ItemIndex: var itemIndex })
                return;

            if (_cachedInventory.Count == 0)
                return;

            var selectedItem = _cachedInventory.ElementAtOrDefault(itemIndex);

            if (selectedItem == null)
                return;

            SendPredictedMessage(new VendingMachineEjectMessage(selectedItem.Type, selectedItem.ID));
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not VendingMachineUpdateState vendingState)
                return;

            _cachedInventory = vendingState.Inventory;
            _prices = vendingState.Prices;
            _requiresCash = vendingState.RequiresCash;
            _balance = vendingState.Balance ?? _balance;
            _cashSlot = vendingState.CashSlot;
            if (!vendingState.RequiresCash)
                _balance = null;
            Refresh();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            if (_menu == null)
                return;

            _menu.OnItemSelected -= OnItemSelected;
            _menu.OnCashEject -= OnCashEject;
            _menu.OnClose -= Close;
            _menu.Dispose();
        }
    }
}
