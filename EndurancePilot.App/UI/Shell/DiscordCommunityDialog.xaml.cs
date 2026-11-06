using System.Windows;

namespace EndurancePilot.App;

internal partial class DiscordCommunityDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly DiscordCommunity community;

    internal DiscordCommunityDialog(DiscordCommunity community)
    {
        this.community = community;
        InitializeComponent();
        DismissButton.Visibility = community.InviteOpened ? Visibility.Visible : Visibility.Collapsed;
        Closing += (_, e) => { if (!community.InviteOpened) e.Cancel = true; };
        Loaded += (_, _) => JoinButton.Focus();
    }

    private void JoinClicked(object sender, RoutedEventArgs e)
    {
        JoinButton.IsEnabled = false;
        try { community.Join(); Close(); }
        catch (Exception error) { ErrorText.Text = error.Message; ErrorText.Visibility = Visibility.Visible; }
        finally { JoinButton.IsEnabled = true; }
    }

    private void DismissClicked(object sender, RoutedEventArgs e) => Close();
}
