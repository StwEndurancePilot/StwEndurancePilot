namespace EndurancePilot.App;

public partial class PilotWindow
{
    private readonly DiscordCommunity community = new();
    private DiscordCommunityDialog? communityDialog;

    private void WelcomeToCommunity()
    {
        if (loaded && IsVisible && !community.InviteOpened) OpenCommunity();
    }

    private void OpenCommunity()
    {
        if (communityDialog is not null) { communityDialog.Activate(); return; }
        AccountPopup.IsOpen = false;
        communityDialog = new DiscordCommunityDialog(community) { Owner = this, Icon = Icon };
        try { communityDialog.ShowDialog(); }
        finally { communityDialog = null; }
    }
}
