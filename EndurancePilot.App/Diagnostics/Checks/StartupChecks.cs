using OpenCvSharp;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace EndurancePilot.App;

internal sealed record StartupCheck(string Name, bool Passed, string Detail);
internal static class StartupChecks
{
    public static IReadOnlyList<StartupCheck> Run()
    {
        var checks = new List<StartupCheck>();
        void Check(string name, Action action, string success)
        {
            try
            {
                action();
                checks.Add(new(name, true, success));
            }
            catch (Exception error) { checks.Add(new(name, false, error.Message)); }
        }
        Check("English OCR", () =>
        {
            if (OcrEngine.TryCreateFromLanguage(new Language("en-US")) is null)
            {
                throw new InvalidOperationException("Install the English OCR language component in Windows Settings.");
            }
        }, "Available");
        Check("Visual recognition", () => { _ = Cv2.GetVersionString(); }, "Native OpenCV loaded");
        Check("Navigation images", () =>
        {
            foreach (var name in new[] { "assistance-checked", "assistance-unchecked", "homebase-icon", "launch-control",
                "next-zone", "own-homebase-selected", "quest-log-prompt", "select-control", "stw-discovery-tile", "hub-quest-board", "hub-xp-door",
                "discover-chevron", "stw-search-tile" })
            {
                using var bitmap = new Bitmap(Path.Combine(AppContext.BaseDirectory, "assets", name + ".png"));
                if (bitmap.Width < 1 || bitmap.Height < 1)
                {
                    throw new InvalidDataException("Invalid navigation image: " + name);
                }
            }
        }, "All thirteen navigation images available");
        return checks;
    }
}
