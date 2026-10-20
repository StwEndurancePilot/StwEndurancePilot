using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class WaveChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var reports = new List<object>();
        var failures = new List<string>();
        foreach (var (phase, expectedWave) in new[] { ("active", 2), ("countdown", 2), ("active", 3) })
        {
            using var original = new Bitmap(Path.Combine(captures, $"twine-wave{expectedWave}-{phase}-native.png"));
            foreach (var (width, height) in new[] { (2560, 1392), (1280, 720), (1920, 1080), (3440, 1440) })
            {
                using var image = new Bitmap(width, height);
                double scale = Math.Min((double)width / original.Width, (double)height / original.Height);
                float offsetX = (float)(width - original.Width * scale) / 2, offsetY = (float)(height - original.Height * scale) / 2;
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, offsetX, offsetY, (float)(original.Width * scale), (float)(original.Height * scale));
                }
                var frame = await ocr.ReadAsync(image);
                var screen = new ScreenRecognizer().Recognize(frame);
                var readings = (phase == "active" ? WaveReader.Read(frame) : WaveReader.ReadCountdown(frame)).Select(r => r.Wave).Distinct().ToArray();
                var tracker = new RunTracker();
                tracker.Observe(frame, screen);
                tracker.Observe(frame with
                {
                    CapturedAt = frame.CapturedAt.AddSeconds(2)
                }, screen);
                bool passed = readings.SequenceEqual(new[] { expectedWave }) && (phase == "active"
                    ? screen.Kind == ScreenKind.ActiveEndurance && tracker.ConfirmedWave == expectedWave
                    : screen.Kind == ScreenKind.EnduranceCountdown && tracker.Current is null);
                string name = $"wave{expectedWave}-{phase}-{width}x{height}";
                if (!passed)
                {
                    failures.Add(name);
                }

                reports.Add(new
                {
                    name,
                    Synthetic = width != original.Width,
                    Passed = passed,
                    screen.Kind,
                    readings
                });
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen
                }, Inspection.Json));
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "wave-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Wave recognition failed: " + string.Join(", ", failures));
        }
    }
}
