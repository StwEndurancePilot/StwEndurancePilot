using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class ConsoleChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        var failures = new List<string>();
        var ocr = new OcrService();
        var visual = new VisualRecognizer();
        var cases = new[] {
            ("twine-console-menu-native", NavigationStep.SelectEndurance, new Box(580,775,400,60)),
            ("twine-endurance-setup-assistance-native", NavigationStep.DisableAssistance, new Box(1120,820,70,70)),
            ("twine-endurance-setup-solo-native", NavigationStep.StartEndurance, new Box(1090,950,380,70)),
            ("live-endurance-solo-1920x1080", NavigationStep.StartEndurance, new Box(760,772,400,62)) };
        foreach (var (capture, expectedStep, nativeTarget) in cases)
        {
            using var original = new Bitmap(Path.Combine(captures, capture + ".png"));
            foreach (var (width, height) in new[] { (2560, 1392), (1280, 720), (1918, 1030), (1920, 1080), (3440, 1440) })
            {
                using var image = new Bitmap(width, height);
                double scale = Math.Min((double)width / original.Width, (double)height / original.Height);
                float x = (float)(width - original.Width * scale) / 2, y = (float)(height - original.Height * scale) / 2;
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, x, y, (float)(original.Width * scale), (float)(original.Height * scale));
                }
                await Check(image, $"{capture}-{width}x{height}", expectedStep,
                    new(x + nativeTarget.X * scale, y + nativeTarget.Y * scale, nativeTarget.Width * scale, nativeTarget.Height * scale));
            }
            if (capture == "twine-endurance-setup-assistance-native")
            {
                await Check(original, capture + "-assistance-on", NavigationStep.StartEndurance,
                    new Box(1090, 950, 380, 70), waitForAssistance: true);
            }
            if (capture == "twine-endurance-setup-solo-native")
            {
                await Check(original, capture + "-assistance-on", NavigationStep.EnableAssistance,
                    new Box(1120, 820, 70, 70), waitForAssistance: true);
            }
            if (expectedStep != NavigationStep.SelectEndurance)
            {
                using var obscured = new Bitmap(original);
                using (var graphics = Graphics.FromImage(obscured))
                {
                    graphics.FillRectangle(Brushes.Black, capture.StartsWith("live-")
                        ? new Rectangle(825, 675, 70, 65) : new Rectangle(1110, 810, 80, 80));
                }

                await Check(obscured, capture + "-missing-checkbox", null, default);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "console-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Console recognition failed: " + string.Join(", ", failures));
        }

        async Task Check(Bitmap image, string name, NavigationStep? expected, Box target, bool waitForAssistance = false)
        {
            var frame = await ocr.ReadAsync(image);
            var screen = new ScreenRecognizer().Recognize(frame);
            frame = visual.Enrich(image, frame, screen.Kind);
            var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true, WaitForAssistance: waitForAssistance)).Plan;
            if (expected is { } fixedStep && MenuGeometry.UsesFixedTarget(fixedStep))
            {
                target = MenuGeometry.Target(fixedStep, frame);
            }
            bool passed = expected is null ? plan is null : plan is not null && plan.Step == expected
                && plan.Target.CenterX >= target.X && plan.Target.CenterX <= target.Right
                && plan.Target.CenterY >= target.Y && plan.Target.CenterY <= target.Bottom;
            reports.Add(new
            {
                name,
                Passed = passed,
                screen.Kind,
                plan,
                frame.Visuals
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
    }
}
