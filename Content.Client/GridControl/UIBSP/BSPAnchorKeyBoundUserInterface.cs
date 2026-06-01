using Content.Shared.Containers.ItemSlots;
using Content.Shared.GridControl.Components;
using Robust.Client.UserInterface;

namespace Content.Client.GridControl.UIBSP;

public sealed class BSPAnchorKeyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private BSPAnchorKeyWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<BSPAnchorKeyWindow>();
        _window.PrivilegedIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(BSPAnchorKeyComponent.PrivilegedIdCardSlotId));
        _window.BUI = this;
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        var castState = (BSPAnchorKeyBoundUserInterfaceState)state;
        _window?.UpdateState(castState);
    }

}
