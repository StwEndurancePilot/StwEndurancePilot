using EndurancePilot.Core;

namespace EndurancePilot.App;

public partial class RewardPanel : System.Windows.Controls.UserControl
{
    private string? signature;
    public RewardPanel() => InitializeComponent();

    internal void Update(bool visible, IReadOnlyList<MissionReward> items, string status, string title = "Mission rewards",
        long? completedRuns = null, string runScope = "")
    {
        PanelTitle.Text = title;
        RunCountPanel.Visibility = completedRuns is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        CompletedRunCount.Text = completedRuns?.ToString("N0") ?? "";
        RunCountScope.Text = runScope;
        Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        var next = string.Join("|", items.Select(r => $"{r.Name}:{r.Quantity}"));
        if (signature != next)
        {
            RewardItems.ItemsSource = items.Select(item =>
            {
                System.Windows.Media.Imaging.BitmapImage? image = null;
                if (RewardAssets.Read(item.Name) is { } bytes)
                {
                    using var stream = new MemoryStream(bytes);
                    image = new();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                }
                return new
                {
                    item.Name,
                    item.QuantityLabel,
                    Image = image,
                    Fallback = image is null ? item.Icon : ""
                };
            }).ToArray();
            signature = next;
        }
        RewardStatus.Text = status;
    }
}
