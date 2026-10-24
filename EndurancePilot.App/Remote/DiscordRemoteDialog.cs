using System.Windows;
using System.Windows.Threading;

namespace EndurancePilot.App;

internal partial class DiscordRemoteDialog : Window
{
    private readonly DiscordRemote remote;
    private readonly DiscordPublicSettings? defaults;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool busy;

    internal DiscordRemoteDialog(DiscordRemote remote)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        this.remote = remote;
        defaults = DiscordPublicSettings.Load();
        UserId.Text = remote.Configuration?.UserId ?? "";
        timer.Tick += (_, _) => Refresh();
        Closed += (_, _) => { timer.Stop(); lifetime.Cancel(); PairingCode.Clear(); };
        Refresh();
        if (defaults is null) Message("This build is missing its Discord configuration. Install a configured build.");
        timer.Start();
    }

    private void Refresh()
    {
        bool paired = remote.Configuration is not null;
        Setup.Visibility = PairButton.Visibility = PairingNote.Visibility = paired ? Visibility.Collapsed : Visibility.Visible;
        ConnectionCard.Visibility = DisconnectButton.Visibility = DoneButton.Visibility = paired ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = paired ? "Discord connected" : "Connect Discord";
        Intro.Text = paired ? "Keep Pilot open to use your commands." : "Run /pair in Discord, then paste your details.";
        ConnectionTitle.Text = remote.Status == "Connected" ? "Connected to Discord" : "Connection status";
        ConnectionDetail.Text = remote.Status == "Connected" ? "Ready for /pilot accounts, /pilot progress and more." : remote.Status;
        UserId.IsEnabled = PairingCode.IsEnabled = !paired && !busy;
        PairButton.IsEnabled = !busy && defaults is not null;
        DisconnectButton.IsEnabled = !busy;
        PairButton.Content = busy ? "Connecting…" : "Pair this PC";
        if (remote.LastCommandStatus.Contains("failed", StringComparison.OrdinalIgnoreCase) || remote.LastCommandStatus.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            ConnectionDetail.Text = remote.LastCommandStatus;
    }

    private void Message(string text) { Feedback.Text = text; Feedback.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible; }

    private async void PairClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (defaults is null) { Message("This build is missing its Discord configuration. Install a configured build."); return; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(UserId.Text.Trim(), @"\A[0-9]{17,20}\z"))
        { Message("Copy your Discord user ID from the /pair message."); UserId.Focus(); return; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(PairingCode.Password.Trim(), @"\A[a-fA-F0-9]{32}\z"))
        { Message("Paste the complete pairing code from /pair."); PairingCode.Focus(); return; }
        busy = true; Message(""); Refresh();
        try
        {
            await remote.PairAsync(defaults.WorkerOrigin, UserId.Text.Trim(), PairingCode.Password.Trim(), true,
                true, defaults.ApplicationId, defaults.PublicKey, lifetime.Token);
            PairingCode.Clear();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { Message("Couldn’t pair this PC. Check your user ID and use a fresh /pair code. If already paired, run /unpair first."); }
        finally { busy = false; Refresh(); }
    }

    private async void DisconnectClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true; Message(""); Refresh();
        try { await remote.DisconnectAsync(); Message(remote.Status == "Disabled" ? "Disconnected. You can pair again whenever you’re ready." : remote.Status); }
        catch { Message("Could not remove saved settings. Run /unpair in Discord and close Pilot to revoke access."); }
        finally { busy = false; Refresh(); }
    }

    private void DoneClicked(object sender, RoutedEventArgs e) => Close();
}
