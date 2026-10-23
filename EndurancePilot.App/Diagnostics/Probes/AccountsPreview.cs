using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndurancePilot.App;

internal static class AccountsPreview
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
            var content = new AccountsPane();
            content.Measure(new System.Windows.Size(480, 656));
            content.Arrange(new Rect(0, 0, 480, 656));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(480, 656, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"accounts-{(dark ? "dark" : "light")}.png"));
            encoder.Save(file);
            var advanced = (FrameworkElement)content.FindName("AdvancedPanel");
            advanced.Visibility = Visibility.Visible;
            content.Measure(new System.Windows.Size(480, 900));
            content.Arrange(new Rect(0, 0, 480, 900));
            content.UpdateLayout();
            var advancedBitmap = new RenderTargetBitmap(480, 900, 96, 96, PixelFormats.Pbgra32);
            advancedBitmap.Render(content);
            var advancedEncoder = new PngBitmapEncoder();
            advancedEncoder.Frames.Add(BitmapFrame.Create(advancedBitmap));
            using var advancedFile = File.Create(Path.Combine(output, $"accounts-advanced-{(dark ? "dark" : "light")}.png"));
            advancedEncoder.Save(advancedFile);
            content.CancelPending();
        }
        app.Shutdown();
    }
}
