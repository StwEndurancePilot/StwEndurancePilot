using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndurancePilot.App;

internal static class SettingsPreview
{
    internal static void Render(string output)
    {
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        foreach (bool dark in new[] { true, false })
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
            void Brush(string key, string hex) => app.Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            Brush("PilotBackground", dark ? "#121618" : "#F5F5EF");
            Brush("PilotSurface", dark ? "#1C2326" : "#EAEDE3");
            Brush("PilotBorder", dark ? "#343F42" : "#D2D8CB");
            Brush("PilotText", dark ? "#F3F6ED" : "#172221");
            Brush("PilotSecondary", dark ? "#B5BFBA" : "#4D6057");
            Brush("PilotAccent", dark ? "#FF8088" : "#B52739");
            Brush("PilotOnAccent", dark ? "#161616" : "#FFFFFF");
            Brush("PilotAccentHover", dark ? "#FF9AA0" : "#A02333");
            Brush("PilotAccentPressed", dark ? "#E5737A" : "#8D1E2D");
            int width = dark ? 480 : 420;
            var rewards = new RewardPanel();
            rewards.Update(true,
                WebhookSamples.RewardItems,
                "Total quantity per reward across completed receipts this session.", "Session earnings");
            var rewardFrame = new System.Windows.Controls.Border
            {
                Background = (System.Windows.Media.Brush)app.Resources["PilotBackground"],
                Padding = new Thickness(28),
                Child = rewards
            };
            rewardFrame.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            int rewardHeight = (int)Math.Ceiling(rewardFrame.DesiredSize.Height);
            rewardFrame.Arrange(new Rect(0, 0, width, rewardHeight));
            rewardFrame.UpdateLayout();
            var rewardBitmap = new RenderTargetBitmap(width, rewardHeight, 96, 96, PixelFormats.Pbgra32);
            rewardBitmap.Render(rewardFrame);
            var rewardEncoder = new PngBitmapEncoder();
            rewardEncoder.Frames.Add(BitmapFrame.Create(rewardBitmap));
            using (var file = File.Create(Path.Combine(output, $"rewards-{(dark ? "dark" : "light")}-{width}.png")))
            {
                rewardEncoder.Save(file);
            }

            var banner = new UpdateBanner();
            banner.SetUpdate(new(UpdateStatus.Available, "Version 0.3.13 is available.", new Version(0, 3, 13), DirectDownload: true, Installer: true));
            var bannerFrame = new System.Windows.Controls.Border { Background = (System.Windows.Media.Brush)app.Resources["PilotBackground"], Padding = new Thickness(28, 12, 28, 12), Child = banner };
            bannerFrame.Measure(new System.Windows.Size(width, 100));
            bannerFrame.Arrange(new Rect(0, 0, width, 100));
            bannerFrame.UpdateLayout();
            var bannerBitmap = new RenderTargetBitmap(width, 100, 96, 96, PixelFormats.Pbgra32);
            bannerBitmap.Render(bannerFrame);
            var bannerEncoder = new PngBitmapEncoder();
            bannerEncoder.Frames.Add(BitmapFrame.Create(bannerBitmap));
            using (var file = File.Create(Path.Combine(output, $"update-banner-{(dark ? "dark" : "light")}-{width}.png")))
            {
                bannerEncoder.Save(file);
            }

            var pane = new SettingsPane(new(), _ => null, _ => null, _ => null, _ => null, _ => null, _ => Task.FromResult("Offline preview"), _ => null, _ => null);
            pane.SetRunning(false);
            pane.SetWebhookStatus("Notifications are off.");
            pane.Measure(new System.Windows.Size(width, 650));
            pane.Arrange(new Rect(0, 0, width, 650));
            pane.UpdateLayout();
            var scroll = (ScrollViewer)pane.FindName("SettingsScrollViewer");
            pane.SetUpdates(new(UpdateStatus.Available, "Version 0.3.11 is available.", new Version(0, 3, 11), new Uri(UpdateChecker.Repository + "/releases/tag/v0.3.11"), true));
            pane.UpdateLayout();
            Save("updates");
            var runOptions = (FrameworkElement)pane.FindName("RunOptions");
            scroll.ScrollToVerticalOffset(runOptions.TransformToAncestor(scroll).Transform(new System.Windows.Point()).Y - 35);
            pane.UpdateLayout();
            Save("run-options");
            scroll.ScrollToTop();
            pane.UpdateLayout();
            var toggle = (FrameworkElement)pane.FindName("WebhookToggle");
            double offset = toggle.TransformToAncestor(scroll).Transform(new System.Windows.Point()).Y - 55;
            scroll.ScrollToVerticalOffset(offset);
            pane.UpdateLayout();
            Save("discord-top");
            scroll.ScrollToEnd();
            pane.UpdateLayout();
            Save("discord-events");
            void Save(string name)
            {
                var bitmap = new RenderTargetBitmap(width, 650, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(pane);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"{name}-{(dark ? "dark" : "light")}-{width}.png"));
                encoder.Save(file);
            }
        }
        app.Shutdown();
    }
}
