namespace Content.Client.Lobby.UI
{
    public sealed partial class HumanoidProfileEditor
    {
        public void ForceCreateMode()
        {
            IsDirty = true;
            TabContainer.Visible = true;
            SaveButton.Visible = true;
            JoinGameButton.Visible = false;
            RandomizeEverythingButton.Visible = true;
            NameRandomize.Visible = true;
            NameEdit.Editable = true;
        }
    }
}
