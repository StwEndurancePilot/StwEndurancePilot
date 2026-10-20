using System.Windows;

namespace EndurancePilot.App;

public partial class UpdateBanner : System.Windows.Controls.UserControl
{
    public UpdateBanner() => InitializeComponent();
    public event EventHandler? DownloadRequested;
    internal void SetUpdate(UpdateCheckResult result)
    {
        Visibility = result.Status == UpdateStatus.Available ? Visibility.Visible : Visibility.Collapsed;
        VersionText.Text = $"Version {result.Version}";
        VersionText.ToolTip = result.Message;
        DownloadButton.Content = result.DirectDownload ? "Download" : "View release";
        System.Windows.Automation.AutomationProperties.SetName(DownloadButton,
            result.DirectDownload ? $"Download version {result.Version}" : $"View release {result.Version}");
    }
    private void DownloadClicked(object sender, RoutedEventArgs e) => DownloadRequested?.Invoke(this, EventArgs.Empty);
}
