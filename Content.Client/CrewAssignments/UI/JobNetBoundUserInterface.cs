using Content.Client.CrewAssignments.UI;
using Content.Shared.CrewAssignments;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.Store;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.OptionButton;

namespace Content.Client.Store.Ui;

[UsedImplicitly]
public sealed class JobNetBoundUserInterface : BoundUserInterface
{

    [ViewVariables]
    private JobNetMenu? _menu;

    [ViewVariables]
    public CodexEntryMenu? CodexMenu;


    public JobNetBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<JobNetMenu>();
        _menu.Owner = this;
        _menu.PossibleJobs.OnItemSelected += OnJobPressed;
        _menu.LevelPurchaseButton.OnPressed += OnLevelPurchase;
        _menu.DealerSelect.OnPressed += DealerSelect_OnPressed;
        _menu.AssassinSelect.OnPressed += AssassinSelect_OnPressed;
        _menu.BountyHSelect.OnPressed += BountyHSelect_OnPressed;
        CodexMenu = new();

    }

    private void BountyHSelect_OnPressed(ButtonEventArgs obj)
    {
        SendMessage(new JobNetSelectRogueNetMessage(RogueNetworkType.BountyHunter));
    }

    private void AssassinSelect_OnPressed(ButtonEventArgs obj)
    {
        SendMessage(new JobNetSelectRogueNetMessage(RogueNetworkType.Assassin));
    }

    private void DealerSelect_OnPressed(ButtonEventArgs obj)
    {
        SendMessage(new JobNetSelectRogueNetMessage(RogueNetworkType.Dealer));
    }

    public void OnLevelPurchase(ButtonEventArgs args)
    {
        SendMessage(new JobNetPurchaseMessage());
    }

    public void OnJobPressed(ItemSelectedEventArgs args)
    {
        SendMessage(new JobNetSelectMessage(args.Id));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_menu == null) return;
        if (state is not JobNetUpdateState cState)
            return;
        _menu.UpdateState(cState);


    }
}
