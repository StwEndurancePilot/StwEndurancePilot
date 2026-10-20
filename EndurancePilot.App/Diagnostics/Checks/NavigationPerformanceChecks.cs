using System.Diagnostics;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class NavigationPerformanceChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var vision = new VisualRecognizer();
        var recognizer = new ScreenRecognizer();
        var reports = new List<object>();
        foreach (var name in new[] { "world-map-stonewood-native", "live-canny-chevron-1918x1030",
            "world-map-twine-unlocked-native", "live-twine-triangle-stall-1918x1030",
            "twine-homebase-selected-native", "live-homebase-choice-1918x1030",
            "twine-homebase-ready-native", "live-console-menu-1918x1030" })
        {
            using var image = new Bitmap(Path.Combine(captures, name + ".png"));
            for (int repetition = 0; repetition < 3; repetition++)
            {
                var clock = Stopwatch.StartNew();
                var frame = await ocr.ReadAsync(image);
                var textMs = clock.Elapsed.TotalMilliseconds;
                var screen = recognizer.Recognize(frame);
                frame = vision.Enrich(image, frame, screen.Kind);
                screen = recognizer.Recognize(frame);
                var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                reports.Add(new
                {
                    Name = name,
                    Repetition = repetition,
                    OcrMilliseconds = textMs,
                    TotalMilliseconds = clock.Elapsed.TotalMilliseconds,
                    screen.Kind,
                    Step = plan?.Step,
                    Target = plan?.Target,
                    Passes = frame.Regions.Select(r => r.Pass).Distinct().ToArray()
                });
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "navigation-performance.json"), JsonSerializer.Serialize(reports, Inspection.Json));
    }
}
