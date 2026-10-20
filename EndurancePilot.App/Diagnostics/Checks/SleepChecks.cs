using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class SleepChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var reports = new List<object>();
        var failures = new List<string>();
        using var original = new Bitmap(Path.Combine(captures, "sleep-mode-user.png"));
        foreach (var (width, height) in new[] { (1650, 928), (800, 450), (1280, 720), (1920, 1200), (2560, 1440), (3440, 1440) })
        {
            double scale = Math.Min(width / (double)original.Width, height / (double)original.Height);
            float x = (float)(width - original.Width * scale) / 2, y = (float)(height - original.Height * scale) / 2;
            using var image = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original, x, y, (float)(original.Width * scale), (float)(original.Height * scale));
            }
            var frame = await ocr.ReadAsync(image);
            var screen = new ScreenRecognizer().Recognize(frame);
            var controller = new AutomationController();
            controller.Start();
            controller.Observe(frame, screen);
            var action = controller.Observe(frame with
            {
                CapturedAt = frame.CapturedAt.AddSeconds(.6)
            }, screen);
            var button = new Box(x + 713 * scale, y + 405 * scale, 225 * scale, 48 * scale);
            bool passed = screen.Kind == ScreenKind.SleepMode && action?.Step == NavigationStep.KeepPlaying
                && action.Target.CenterX > button.X && action.Target.CenterX < button.Right
                && action.Target.CenterY > button.Y && action.Target.CenterY < button.Bottom;
            string name = $"sleep-{width}x{height}";
            reports.Add(new
            {
                Name = name,
                Passed = passed,
                screen.Kind,
                action
            });
            if (!passed)
            {
                failures.Add(name);
            }

            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                Observation = frame,
                Screen = screen
            }, Inspection.Json));
        }
        using (var hidden = new Bitmap(original))
        {
            using (var graphics = Graphics.FromImage(hidden))
            {
                graphics.FillRectangle(Brushes.Black, 710, 400, 235, 60);
            }

            var frame = await ocr.ReadAsync(hidden);
            var screen = new ScreenRecognizer().Recognize(frame);
            bool passed = screen.Kind == ScreenKind.SleepMode && NavigationPlanner.Decide(frame, screen, new()).Plan is null;
            reports.Add(new
            {
                Name = "missing-keep-playing",
                Passed = passed,
                screen.Kind
            });
            if (!passed)
            {
                failures.Add("missing-keep-playing");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "sleep-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Sleep checks failed: " + string.Join(", ", failures));
        }
    }
}
