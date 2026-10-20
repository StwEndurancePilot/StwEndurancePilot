using System.Windows;

namespace EndurancePilot.App;

public partial class SettingsPane
{
    internal event EventHandler? CheckUpdatesRequested;
    internal event EventHandler? OpenUpdateRequested;
    internal void SetUpdates(UpdateCheckResult result, bool checking = false)
    {
        InstalledVersionText.Text = $"Version {UpdateChecker.InstalledVersion}";
        UpdateStatusText.Text = checking ? "Checking GitHub…" : result.Message;
        CheckUpdatesButton.IsEnabled = !checking;
        CheckUpdatesButton.Content = checking ? "Checking…" : "Check for updates";
        DownloadUpdateButton.Visibility = result.Status == UpdateStatus.Available ? Visibility.Visible : Visibility.Collapsed;
        DownloadUpdateButton.Content = result.Installer ? "Download installer" : result.DirectDownload ? "Download update" : "View release";
    }
    private void CheckUpdatesClicked(object sender, RoutedEventArgs e) => CheckUpdatesRequested?.Invoke(this, EventArgs.Empty);
    private void DownloadUpdateClicked(object sender, RoutedEventArgs e) => OpenUpdateRequested?.Invoke(this, EventArgs.Empty);
}
