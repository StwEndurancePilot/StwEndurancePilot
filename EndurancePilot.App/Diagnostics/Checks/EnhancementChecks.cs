using System.Drawing.Imaging;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class EnhancementChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var reports = new List<object>();
        var failures = new List<string>();
        foreach (var (name, kind, step) in new[]{("rejoin-dialog-user",ScreenKind.RejoinDialog,NavigationStep.AbandonRejoin),
            ("live-console-menu-1918x1030",ScreenKind.StormShieldConsole,NavigationStep.SelectEndurance)})
        {
            using var original = new Bitmap(Path.Combine(captures, name + ".png"));
            var clean = await ocr.ReadAsync(original);
            var cleanScreen = new ScreenRecognizer().Recognize(clean);
            var cleanPlan = NavigationPlanner.Decide(clean, cleanScreen, new(HomebaseZone.TwinePeaks, true)).Plan!;
            Check(name + "-fast-path", cleanScreen.Kind == kind && !clean.Regions.Any(r => r.Pass.StartsWith("contrast-")));
            foreach (float contrast in new[] { .03f, .08f, .2f })
            {
                using var washed = new Bitmap(original.Width, original.Height);
                using (var graphics = Graphics.FromImage(washed))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix(new[]{new[]{contrast,0f,0f,0f,0f},new[]{0f,contrast,0f,0f,0f},
                        new[]{0f,0f,contrast,0f,0f},new[]{0f,0f,0f,1f,0f},new[]{.45f,.45f,.45f,0f,1f}}));
                    graphics.DrawImage(original, new Rectangle(0, 0, washed.Width, washed.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
                var baseline = await ocr.ReadAsync(washed, enhance: false);
                var frame = await ocr.ReadAsync(washed);
                var screen = new ScreenRecognizer().Recognize(frame);
                var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                bool unreadable = name.Contains("console") && contrast == .03f;
                bool passed = unreadable ? plan is null : screen.Kind == kind && plan?.Step == step && plan.Target.IntersectionOverUnion(cleanPlan.Target) > .4;
                string id = name + "-contrast" + contrast;
                reports.Add(new
                {
                    Name = id,
                    Passed = passed,
                    Baseline = new ScreenRecognizer().Recognize(baseline).Kind,
                    Enhanced = screen.Kind,
                    RecognizedCorrectly = screen.Kind == kind,
                    plan,
                    Passes = frame.Regions.Select(r => r.Pass).Distinct().ToArray()
                });
                if (!passed)
                {
                    failures.Add(id);
                }

                await File.WriteAllTextAsync(Path.Combine(output, id + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen
                }, Inspection.Json));
                if (name == "rejoin-dialog-user" && contrast == .08f)
                {
                    washed.Save(Path.Combine(output, "washed-out-original.png"));
                    await File.WriteAllBytesAsync(Path.Combine(output, "contrast-enhanced.png"), TextEnhancement.Encode(washed, TextTreatment.LocalContrast));
                }
            }
        }
        using (var blank = new Bitmap(1280, 720))
        {
            using (var graphics = Graphics.FromImage(blank))
            {
                graphics.Clear(Color.Gray);
            }

            var frame = await ocr.ReadAsync(blank);
            var screen = new ScreenRecognizer().Recognize(frame);
            Check("Blank image cannot create an action", screen.Kind == ScreenKind.Unknown && NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan is null);
        }
        foreach (var (name, wave, countdown) in new[]{("live-hud25-2560x1392-wave7",7,false),
            ("live-hud25-2560x1392-wave8",8,false),("live-hud25-2560x1392-wave8-countdown",8,true)})
        {
            using var source = new Bitmap(Path.Combine(captures, name + ".png"));
            var frame = await ocr.ReadAsync(source);
            var screen = new ScreenRecognizer().Recognize(frame);
            var active = WaveReader.Read(frame).Select(r => r.Wave).Distinct().ToArray();
            var upcoming = WaveReader.ReadCountdown(frame).Select(r => r.Wave).Distinct().ToArray();
            bool passed = active.All(w => w == wave) && upcoming.All(w => w == wave)
                && (!countdown || active.Length == 0) && NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan is null;
            reports.Add(new
            {
                Name = name,
                Passed = passed,
                RecognizedCorrectly = screen.Kind == (countdown ? ScreenKind.EnduranceCountdown : ScreenKind.ActiveEndurance),
                screen.Kind,
                active,
                upcoming
            });
            if (!passed)
            {
                failures.Add(name);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "enhancement-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Enhancement checks failed: " + string.Join(", ", failures));
        }

        void Check(string name, bool passed)
        {
            reports.Add(new
            {
                Name = name,
                Passed = passed
            });
            if (!passed)
            {
                failures.Add(name);
            }
        }
    }
}
