using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class RecoveryChecks
{

    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        var ocr = new OcrService();
        foreach (string name in new[] { "installation-error-native", "installation-error-ultrawide" })
        {
            using var original = new Bitmap(Path.Combine(captures, name + ".png"));
            foreach (double scale in new[] { 1d, .75, .5 })
            {
                using var sample = new Bitmap((int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale));
                using (var graphics = Graphics.FromImage(sample))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, new Rectangle(Point.Empty, sample.Size));
                }
                var observation = await ocr.ReadAsync(sample);
                var evidence = new ScreenRecognizer().Recognize(observation);
                if (evidence.Kind != ScreenKind.NeedsAttention)
                {
                    throw new Exception($"Installation error was not recognized: {name}, scale {scale}");
                }

                var controller = new AutomationController();
                controller.Start();
                controller.Observe(observation, evidence);
                var action = controller.Observe(observation with
                {
                    CapturedAt = observation.CapturedAt.AddSeconds(1)
                }, evidence)
                    ?? throw new Exception($"No unambiguous dismissal target: {name}, scale {scale}");
                if (!GameInput.TargetStillMatches(sample, sample, action.Target))
                {
                    throw new Exception("Unchanged target rejected");
                }

                using var changed = new Bitmap(sample);
                using (var graphics = Graphics.FromImage(changed))
                {
                    graphics.FillRectangle(Brushes.Black, (float)action.Target.X - 5, (float)action.Target.Y - 5,
                        (float)action.Target.Width + 10, (float)action.Target.Height + 10);
                }

                if (GameInput.TargetStillMatches(sample, changed, action.Target))
                {
                    throw new Exception("Removed target was accepted");
                }

                using var wrongSize = new Bitmap(sample.Width + 10, sample.Height);
                if (GameInput.TargetStillMatches(sample, wrongSize, action.Target))
                {
                    throw new Exception("Resized window was accepted");
                }

                reports.Add(new
                {
                    Source = name,
                    Scale = scale,
                    sample.Width,
                    sample.Height,
                    evidence.Kind,
                    action.Target,
                    UnchangedAccepted = true,
                    RemovedRejected = true,
                    ResizedRejected = true
                });
            }
        }
        using (var original = new Bitmap(Path.Combine(captures, "idle-disconnect-error-user.png")))
        {
            foreach (var (width, height) in new[] { (1916, 1080), (1280, 720), (1920, 1200), (2560, 1440), (3440, 1440) })
            {
                using var sample = new Bitmap(width, height);
                double scale = Math.Min(width / (double)original.Width, height / (double)original.Height);
                float x = (float)(width - original.Width * scale) / 2, y = (float)(height - original.Height * scale) / 2;
                using (var graphics = Graphics.FromImage(sample))
                {
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, x, y, (float)(original.Width * scale), (float)(original.Height * scale));
                }
                var frame = await ocr.ReadAsync(sample);
                var screen = new ScreenRecognizer().Recognize(frame);
                var controller = new AutomationController();
                controller.Start();
                controller.Observe(frame, screen);
                var action = controller.Observe(frame with
                {
                    CapturedAt = frame.CapturedAt.AddSeconds(.6)
                }, screen);
                var button = new Box(x + 864 * scale, y + 642 * scale, 193 * scale, 56 * scale);
                bool passed = screen.Kind == ScreenKind.ErrorDialog && action is { PauseAfterDismissal: false }
                    && action.Target.CenterX > button.X && action.Target.CenterX < button.Right
                    && action.Target.CenterY > button.Y && action.Target.CenterY < button.Bottom;
                reports.Add(new
                {
                    Source = "idle-disconnect-error-user",
                    width,
                    height,
                    Passed = passed,
                    screen.Kind,
                    Target = action?.Target
                });
                await File.WriteAllTextAsync(Path.Combine(output, $"idle-error-{width}x{height}.json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen,
                    action
                }, Inspection.Json));
                if (!passed)
                {
                    throw new Exception($"Idle error Continue failed at {width}x{height}");
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, "recovery-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
    }
}
