using Content.Client.MCTN.UI;
using Content.Shared.MCTN.BUIStates;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.MCTN.BUI;

[UsedImplicitly]
public sealed class MCTNBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MCTNConsoleWindow? _window;

    public MCTNBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MCTNConsoleWindow>();
        _window.BUI = this;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not MCTNBoundUserInterfaceState cState)
            return;

        _window?.UpdateState(cState);
    }
}
