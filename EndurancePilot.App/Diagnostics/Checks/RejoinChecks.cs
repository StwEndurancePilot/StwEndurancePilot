using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class RejoinChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        using var original = new Bitmap(Path.Combine(captures, "rejoin-dialog-user.png"));
        var ocr = new OcrService();
        var reports = new List<object>();
        var failures = new List<string>();
        foreach (var (width, height) in new[] { (1047, 664), (800, 600), (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440) })
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
            var plan = NavigationPlanner.Decide(frame, screen, new()).Plan;
            var button = new Box(x + 749 * scale, y + 538 * scale, 211 * scale, 45 * scale);
            bool passed = screen.Kind == ScreenKind.RejoinDialog && plan?.Step == NavigationStep.AbandonRejoin
                && plan.Target.CenterX > button.X && plan.Target.CenterX < button.Right
                && plan.Target.CenterY > button.Y && plan.Target.CenterY < button.Bottom;
            string name = $"rejoin-{width}x{height}";
            reports.Add(new
            {
                name,
                Passed = passed,
                screen.Kind,
                plan
            });
            if (!passed)
            {
                failures.Add(name);
            }

            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                Observation = frame,
                Screen = screen,
                plan
            }, Inspection.Json));
        }

        using (var missing = new Bitmap(original))
        {
            using (var graphics = Graphics.FromImage(missing))
            {
                graphics.FillRectangle(Brushes.Black, 745, 530, 225, 60);
            }

            var frame = await ocr.ReadAsync(missing);
            var screen = new ScreenRecognizer().Recognize(frame);
            bool passed = screen.Kind == ScreenKind.RejoinDialog && NavigationPlanner.Decide(frame, screen, new()).Plan is null;
            reports.Add(new
            {
                name = "missing-abandon-button",
                Passed = passed,
                screen.Kind
            });
            if (!passed)
            {
                failures.Add("missing-abandon-button");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "rejoin-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Rejoin recognition failed: " + string.Join(", ", failures));
        }
    }
}
