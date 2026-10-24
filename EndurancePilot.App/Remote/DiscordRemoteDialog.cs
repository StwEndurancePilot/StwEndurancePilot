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
        Origin.Text = remote.Configuration?.Origin ?? defaults?.WorkerOrigin ?? "";
        ApplicationId.Text = remote.Configuration?.ApplicationId ?? defaults?.ApplicationId ?? "";
        PublicKey.Text = remote.Configuration?.DiscordPublicKey ?? defaults?.PublicKey ?? "";
        AllowControl.IsChecked = remote.Configuration?.AllowControl ?? false;
        AllowScreenshots.IsChecked = remote.Configuration?.AllowScreenshots ?? false;
        Advanced.IsExpanded = defaults is null || (remote.Configuration is { } saved &&
            (saved.Origin != defaults.WorkerOrigin || saved.ApplicationId != defaults.ApplicationId || !saved.DiscordPublicKey.Equals(defaults.PublicKey, StringComparison.OrdinalIgnoreCase)));
        ResetService.IsEnabled = defaults is not null;
        timer.Tick += (_, _) => Refresh();
        Closed += (_, _) => { timer.Stop(); lifetime.Cancel(); PairingCode.Clear(); };
        Refresh();
        timer.Start();
    }

    private void Refresh()
    {
        bool paired = remote.Configuration is not null;
        Setup.Visibility = PairButton.Visibility = paired ? Visibility.Collapsed : Visibility.Visible;
        ConnectionCard.Visibility = DisconnectButton.Visibility = DoneButton.Visibility = paired ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = paired ? "Pilot is paired." : "Your Pilot, from anywhere.";
        Intro.Text = paired ? "Keep Pilot open on this PC to use your Discord commands." : "Pair this PC to check your run and control Pilot from Discord.";
        ConnectionTitle.Text = remote.Status == "Connected" ? "Connected to Discord" : "Connection status";
        ConnectionDetail.Text = remote.Status == "Connected" ? "Ready for /pilot accounts, /pilot progress and more." : remote.Status;
        PermissionHint.Text = paired ? "To change permissions, disconnect and pair again." : "Optional permissions stay off until you choose them.";
        UserId.IsEnabled = PairingCode.IsEnabled = AllowControl.IsEnabled = AllowScreenshots.IsEnabled = Advanced.IsEnabled = !paired && !busy;
        PairButton.IsEnabled = DisconnectButton.IsEnabled = !busy;
        PairButton.Content = busy ? "Connecting…" : "Pair this PC";
        TransportStatus.Text = remote.Status;
        LastCommand.Text = remote.LastCommandStatus;
    }

    private void Message(string text) { Feedback.Text = text; Feedback.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible; }

    private async void PairClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(UserId.Text.Trim(), @"\A[0-9]{17,20}\z"))
        { Message("Copy your Discord user ID from the /pilot pair message."); UserId.Focus(); return; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(PairingCode.Password.Trim(), @"\A[a-fA-F0-9]{32}\z"))
        { Message("Paste the complete pairing code from /pilot pair."); PairingCode.Focus(); return; }
        busy = true; Message(""); Refresh();
        try
        {
            await remote.PairAsync(Origin.Text, UserId.Text.Trim(), PairingCode.Password.Trim(), AllowControl.IsChecked == true,
                AllowScreenshots.IsChecked == true, ApplicationId.Text, PublicKey.Text, lifetime.Token);
            PairingCode.Clear();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { Message("Couldn’t pair this PC. Check your user ID and use a fresh /pilot pair code. If already paired, run /pilot unlink first."); }
        finally { busy = false; Refresh(); }
    }

    private async void DisconnectClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true; Message(""); Refresh();
        try { await remote.DisconnectAsync(); Message(remote.Status == "Disabled" ? "Disconnected. You can pair again whenever you’re ready." : remote.Status); }
        catch { Message("Could not remove saved settings. Run /pilot unlink in Discord and close Pilot to revoke access."); }
        finally { busy = false; Refresh(); }
    }

    private void ResetServiceClicked(object sender, RoutedEventArgs e)
    {
        if (defaults is null) return;
        Origin.Text = defaults.WorkerOrigin; ApplicationId.Text = defaults.ApplicationId; PublicKey.Text = defaults.PublicKey;
        Advanced.IsExpanded = false;
    }
    private void DoneClicked(object sender, RoutedEventArgs e) => Close();
}
