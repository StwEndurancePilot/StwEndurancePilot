using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class BackpackChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var reports = new List<object>();
        var failures = new List<string>();
        var cases = new[]
        {
            ("reported-shield-entry-2559x1439",ScreenKind.Homebase,(NavigationStep?)NavigationStep.OpenBackpack,(NavigationKey?)NavigationKey.Tab,new Box(20,225,500,70)),
            ("live-inactive-objective-small-1918x1030",ScreenKind.Homebase,(NavigationStep?)NavigationStep.OpenBackpack,(NavigationKey?)NavigationKey.Tab,new Box(5,60,160,32)),
            ("live-console-menu-1918x1030",ScreenKind.StormShieldConsole,(NavigationStep?)NavigationStep.SelectEndurance,(NavigationKey?)null,new Box(440,610,300,45)),
            ("twine-homebase-spawn-native",ScreenKind.Homebase,(NavigationStep?)NavigationStep.OpenBackpack,(NavigationKey?)NavigationKey.Tab,new Box(15,180,420,70)),
            ("backpack-storm-shield-tab-2048x1152",ScreenKind.Backpack,(NavigationStep?)NavigationStep.SelectStormShieldTab,(NavigationKey?)null,new Box(620,15,205,60)),
            ("backpack-console-active-2048x1152",ScreenKind.StormShieldConsole,(NavigationStep?)NavigationStep.SelectEndurance,(NavigationKey?)null,new Box(500,680,210,100)),
            ("backpack-endurance-running-2048x1152",ScreenKind.EnduranceStatus,(NavigationStep?)null,(NavigationKey?)null,new Box())
        };
        foreach (var (capture, kind, step, key, target) in cases)
        {
            using var original = new Bitmap(Path.Combine(captures, capture + ".png"));
            var sizes = capture == "live-inactive-objective-small-1918x1030"
                ? new[] { (original.Width, original.Height) }
                : new[] { (original.Width, original.Height), (1280, 720), (1918, 1030), (1920, 1200), (3440, 1440) };
            foreach (var (width, height) in sizes.Distinct())
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
                var frame = await ocr.ReadAsync(image);
                var screen = new ScreenRecognizer().Recognize(frame);
                var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
                var plan = NavigationPlanner.Decide(frame, screen, profile).Plan;
                var expected = step is { } fixedStep && MenuGeometry.UsesFixedTarget(fixedStep)
                    ? MenuGeometry.Target(fixedStep, frame)
                    : new Box(x + target.X * scale, y + target.Y * scale, target.Width * scale, target.Height * scale);
                bool passed = screen.Kind == kind && plan?.Step == step && plan?.Key == key;
                if (plan is not null)
                {
                    passed &= plan.Target.CenterX >= expected.X && plan.Target.CenterX <= expected.Right
                    && plan.Target.CenterY >= expected.Y && plan.Target.CenterY <= expected.Bottom;
                }

                var controller = new AutomationController { Profile = profile, ErrorRecoveryOnly = false };
                controller.Start(runInProgress: true);
                controller.Observe(frame, screen);
                bool activeRunInert = controller.Observe(frame with { CapturedAt = frame.CapturedAt.AddSeconds(2) }, screen) is null;
                passed &= activeRunInert;
                string name = $"{capture}-{width}x{height}";
                if (!passed)
                {
                    failures.Add(name);
                }

                reports.Add(new
                {
                    name,
                    Synthetic = width != original.Width || height != original.Height,
                    Passed = passed,
                    screen.Kind,
                    plan,
                    ActiveRunInert = activeRunInert
                });
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen
                }, Inspection.Json));
            }
        }

        using (var original = new Bitmap(Path.Combine(captures, "twine-homebase-spawn-native.png")))
        using (var objective = original.Clone(new Rectangle(16, 142, 435, 100), original.PixelFormat))
        {
            foreach (var scale in new[] { .45, .6, .85 })
            {
                foreach (var location in new[] { new Point(20, 60), new Point(850, 220) })
                {
                    using var image = new Bitmap(1918, 1030);
                    var target = new Rectangle(location.X, location.Y, (int)(objective.Width * scale), (int)(objective.Height * scale));
                    using (var graphics = Graphics.FromImage(image))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(original, 0, 0, image.Width, image.Height);
                        graphics.FillRectangle(Brushes.DarkSlateGray, 10, 100, 340, 85);
                        graphics.DrawImage(objective, target);
                    }
                    var frame = await ocr.ReadAsync(image);
                    var screen = new ScreenRecognizer().Recognize(frame);
                    var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                    bool passed = screen.Kind == ScreenKind.Homebase && plan is { Step: NavigationStep.OpenBackpack, Key: NavigationKey.Tab }
                        && target.Contains((int)plan.Target.CenterX, (int)plan.Target.CenterY);
                    string name = $"objective-scale-{scale}-{location.X}-{location.Y}";
                    reports.Add(new
                    {
                        name,
                        Synthetic = true,
                        Passed = passed,
                        screen.Kind,
                        plan
                    });
                    if (!passed)
                    {
                        failures.Add(name);
                    }

                    image.Save(Path.Combine(output, name + ".png"));
                    await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                    {
                        Observation = frame,
                        Screen = screen
                    }, Inspection.Json));
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, "backpack-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception("Backpack checks failed: " + string.Join(", ", failures));
        }
    }
}
