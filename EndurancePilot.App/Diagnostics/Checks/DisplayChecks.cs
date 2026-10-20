using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class DisplayChecks
{

    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var reports = new List<object>();
        var failures = new List<string>();
        var cases = new[]
        {
            (25,2560,1392,7,false), (25,2560,1392,8,false), (25,2560,1392,8,true),
            (125,2560,1392,9,false), (125,1920,1200,9,false),
            (50,1920,1080,10,true), (50,1920,1080,10,false)
        };
        foreach (var (hud, width, height, wave, countdown) in cases)
        {
            string name = $"live-hud{hud}-{width}x{height}-wave{wave}" + (countdown ? "-countdown" : "");
            using var bitmap = new Bitmap(Path.Combine(captures, name + ".png"));
            var frame = await ocr.ReadAsync(bitmap);
            var evidence = new ScreenRecognizer().Recognize(frame);
            var readings = (countdown ? WaveReader.ReadCountdown(frame) : WaveReader.Read(frame)).Select(r => r.Wave).Distinct().ToArray();
            var tracker = new RunTracker();
            tracker.Observe(frame, evidence);
            tracker.Observe(frame with
            {
                CapturedAt = frame.CapturedAt.AddSeconds(2)
            }, evidence);
            bool passed = bitmap.Width == width && bitmap.Height == height && readings.SequenceEqual(new[] { wave })
                && evidence.Kind == (countdown ? ScreenKind.EnduranceCountdown : ScreenKind.ActiveEndurance)
                && (countdown ? tracker.Current is null : tracker.ConfirmedWave == wave);
            var navigation = NavigationPlanner.Decide(frame, evidence, new(HomebaseZone.TwinePeaks));
            bool noUnexpectedInput = navigation.Plan is null;
            passed &= noUnexpectedInput;
            if (!passed)
            {
                failures.Add(name);
            }

            reports.Add(new
            {
                name,
                Synthetic = false,
                HudPercent = hud,
                width,
                height,
                ExpectedWave = wave,
                Countdown = countdown,
                Passed = passed,
                evidence.Kind,
                readings,
                NoUnexpectedInput = noUnexpectedInput
            });
            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                Observation = frame,
                Screen = evidence
            }, Inspection.Json));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "display-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Live display coverage failed: " + string.Join(", ", failures));
        }
    }
}
