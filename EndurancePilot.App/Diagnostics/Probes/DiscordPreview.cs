using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndurancePilot.App;

internal static class DiscordPreview
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
            Brush("PilotWarning", dark ? "#FFB578" : "#A44714");
            using var remote = new DiscordRemote((_, _, _) => Task.FromResult(new RemoteResult("Preview")), Path.Combine(Path.GetFullPath(output), "preview-connection.bin"));
            var dialog = new DiscordRemoteDialog(remote);
            var content = (FrameworkElement)dialog.Content;
            foreach (string state in new[] { "setup", "paired", "advanced" })
            {
                if (state == "paired")
                {
                    ((FrameworkElement)dialog.FindName("Setup")).Visibility = Visibility.Collapsed;
                    ((FrameworkElement)dialog.FindName("PairButton")).Visibility = Visibility.Collapsed;
                    foreach (string name in new[] { "ConnectionCard", "DisconnectButton", "DoneButton" })
                        ((FrameworkElement)dialog.FindName(name)).Visibility = Visibility.Visible;
                    ((System.Windows.Controls.TextBlock)dialog.FindName("Heading")).Text = "Pilot is paired.";
                    ((System.Windows.Controls.TextBlock)dialog.FindName("Intro")).Text = "Keep Pilot open on this PC to use your Discord commands.";
                    ((System.Windows.Controls.TextBlock)dialog.FindName("ConnectionTitle")).Text = "Connected to Discord";
                    ((System.Windows.Controls.TextBlock)dialog.FindName("ConnectionDetail")).Text = "Ready for /pilot accounts, /pilot progress and more.";
                    ((System.Windows.Controls.TextBlock)dialog.FindName("PermissionHint")).Text = "To change permissions, disconnect and pair again.";
                    foreach (string name in new[] { "AllowControl", "AllowScreenshots", "Advanced" })
                        ((System.Windows.Controls.Control)dialog.FindName(name)).IsEnabled = false;
                }
                if (state == "advanced") ((System.Windows.Controls.Expander)dialog.FindName("Advanced")).IsExpanded = true;
                int height = state == "advanced" ? 1100 : 780;
                content.Measure(new System.Windows.Size(524, height));
                content.Arrange(new Rect(0, 0, 524, height));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(524, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"discord-{state}-{(dark ? "dark" : "light")}.png"));
                encoder.Save(file);
            }
            dialog.Close();
        }
        app.Shutdown();
    }
}
