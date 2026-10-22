using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class RouteChecks
{
    public static async Task Run(string captures, string output, bool selectedOnly = false)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        var failures = new List<string>();
        var ocr = new OcrService();
        var cases = new[] {
            ("reported-play-with-others-map", NavigationStep.SelectHomebase, (NavigationKey?)null, new Box(1580,980,90,90)),
            ("live-mission-launch-stall-1920x1080", NavigationStep.LaunchLoadedHomebase, (NavigationKey?)null, new Box(1480,865,275,96)),
            ("live-post-reward-hub-1920x1080", NavigationStep.OpenQuestMenu, (NavigationKey?)NavigationKey.Tab, new Box(1085,425,420,300)),
            ("live-selected-homebase-1920x1080", NavigationStep.SelectHomebase, (NavigationKey?)null, new Box(925,630,75,75)),
            ("live-discovery-1920x1080", NavigationStep.SelectSaveTheWorld, (NavigationKey?)null, new Box(80,885,260,150)),
            ("live-defeat-matchmake-1920x1080", NavigationStep.ReturnToHomebase, (NavigationKey?)NavigationKey.C, new Box(750,900,420,55)),
            ("live-people-xp-1920x1080", NavigationStep.CollectMissionRewards, (NavigationKey?)NavigationKey.C, new Box(835,1010,220,55)),
            ("twine-homebase-ready-native", NavigationStep.LaunchLoadedHomebase, (NavigationKey?)null, new Box(1990,1110,365,130)),
            ("homebase-ready-native", NavigationStep.LaunchLoadedHomebase, (NavigationKey?)null, new Box(1990,1110,365,130)),
            ("live-homebase-choice-1918x1030", NavigationStep.LaunchHomebase, (NavigationKey?)null, new Box(1040,884,245,44)),
            ("live-stw-wrapped-title-1918x1030", NavigationStep.EnterSaveTheWorld, (NavigationKey?)null, new Box(92,832,312,76)),
            ("live-twine-missions-1918x1030", NavigationStep.SelectHomebase, (NavigationKey?)null, new Box(1175,703,61,62)),
            ("live-twine-triangle-stall-1918x1030", NavigationStep.SelectHomebase, (NavigationKey?)null, new Box(1170,697,70,73)),
            ("live-twine-radar-selected-1918x1030", NavigationStep.SelectHomebase, (NavigationKey?)null, new Box(925,598,65,65)),
            ("mode-discovery-stw-native", NavigationStep.SelectSaveTheWorld, (NavigationKey?)null, new Box(170,1170,220,130)),
            ("stw-hub-quest-prompt-native", NavigationStep.OpenQuestMenu, (NavigationKey?)NavigationKey.Tab, new Box(1660,640,350,110)) };
        foreach (var (capture, expectedStep, expectedKey, nativeTarget) in cases.Where(_ => !selectedOnly))
        {
            using var original = new Bitmap(Path.Combine(captures, capture + ".png"));
            foreach (var (width, height) in new[] { (2560, 1392), (1280, 720), (1920, 1080), (3440, 1440) })
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
                frame = new VisualRecognizer().Enrich(image, frame, screen.Kind);
                screen = new ScreenRecognizer().Recognize(frame);
                var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                var expected = new Box(x + nativeTarget.X * scale, y + nativeTarget.Y * scale, nativeTarget.Width * scale, nativeTarget.Height * scale);
                if (MenuGeometry.UsesFixedTarget(expectedStep))
                {
                    expected = MenuGeometry.Target(expectedStep, frame);
                }
                bool passed = plan?.Step == expectedStep && plan.Key == expectedKey
                    && plan.Target.CenterX >= expected.X && plan.Target.CenterX <= expected.Right
                    && plan.Target.CenterY >= expected.Y && plan.Target.CenterY <= expected.Bottom;
                string name = $"{capture}-{width}x{height}";
                Box? pointerDestination = null;
                if (plan?.Step == NavigationStep.SelectHomebase)
                {
                    var click = new ClickAction(Guid.NewGuid(), plan.Description, screen.Kind, plan.Target,
                        width, height, frame.CapturedAt, Step: plan.Step);
                    pointerDestination = NavigationPlanner.PostClickPointerDestination(frame, click);
                    passed &= pointerDestination is not null;
                }
                if (!passed)
                {
                    failures.Add(name);
                }

                reports.Add(new
                {
                    name,
                    Passed = passed,
                    screen.Kind,
                    plan,
                    PointerDestination = pointerDestination
                });
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen
                }, Inspection.Json));
            }
        }
        using (var original = new Bitmap(Path.Combine(captures, "twine-homebase-selected-native.png")))
        {
            foreach (var (width, height) in new[] { (2560, 1392), (1280, 720), (1920, 1080), (3440, 1440) })
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
                var readingClock = System.Diagnostics.Stopwatch.StartNew();
                var frame = await ocr.ReadAsync(image);
                var screen = new ScreenRecognizer().Recognize(frame);
                double ocrMilliseconds = readingClock.Elapsed.TotalMilliseconds;
                frame = new VisualRecognizer().Enrich(image, frame, screen.Kind, requireHomebaseIcon: false);
                var action = new ClickAction(Guid.NewGuid(), "Select homebase", ScreenKind.MissionMap,
                    new(x + 1240 * scale, y + 810 * scale, 80 * scale, 90 * scale), width, height, frame.CapturedAt, Step: NavigationStep.SelectHomebase);
                var destination = NavigationPlanner.PostClickPointerDestination(frame, action);
                var selectDecision = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true), homebaseClickConfirmed: true);
                var selectPlan = selectDecision.Plan;
                bool passed = screen.Kind == ScreenKind.MissionMap && destination is { } box
                    && selectPlan?.Step == NavigationStep.OpenHomebase
                    && selectPlan.Target == MenuGeometry.Target(NavigationStep.OpenHomebase, frame)
                    && box.CenterX >= x + 1990 * scale && box.CenterX <= x + 2545 * scale
                    && box.CenterY >= y + 1130 * scale && box.CenterY <= y + 1220 * scale;
                string name = $"homebase-pointer-visible-select-{width}x{height}";
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen,
                    Decision = selectDecision
                }, Inspection.Json));
                if (!passed)
                {
                    failures.Add(name);
                }

                reports.Add(new
                {
                    name,
                    Passed = passed,
                    PointerDestination = destination,
                    SelectPlan = selectPlan,
                    OcrMilliseconds = ocrMilliseconds,
                    Passes = frame.Regions.Select(r => r.Pass).Distinct().ToArray()
                });
            }
        }

        if (selectedOnly)
        {
            await SaveReport();
            return;
        }
        foreach (var missing in new[] { new Rectangle(1080, 420, 430, 310), new Rectangle(1585, 295, 195, 440) })
        {
            using var image = new Bitmap(Path.Combine(captures, "live-post-reward-hub-1920x1080.png"));
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.FillRectangle(Brushes.Black, missing);
            }

            CheckNotHub(image, "hub-missing-landmark-" + missing.X);
        }
        foreach (var capture in new[] { "twine-wave3-active-native", "live-hud25-2560x1392-wave8", "live-endurance-solo-1920x1080" })
        {
            using var image = new Bitmap(Path.Combine(captures, capture + ".png"));
            CheckNotHub(image, "hub-negative-" + capture);
        }
        using (var image = new Bitmap(Path.Combine(captures, "live-mission-launch-stall-1920x1080.png")))
        {
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.FillRectangle(Brushes.Black, new Rectangle(1480, 865, 275, 96));
            }

            var frame = await ocr.ReadAsync(image);
            var screen = new ScreenRecognizer().Recognize(frame);
            var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
            bool passed = screen.Kind == ScreenKind.HomebasePreparing && plan is null;
            const string name = "loadout-missing-launch-does-not-click-footer";
            if (!passed)
            {
                failures.Add(name);
            }

            reports.Add(new
            {
                name,
                Passed = passed,
                screen.Kind,
                plan
            });
        }
        void CheckNotHub(Bitmap image, string name)
        {

            var frame = new Observation(DateTimeOffset.UtcNow, image.Width, image.Height, []);
            frame = new VisualRecognizer().Enrich(image, frame, ScreenKind.Unknown);
            bool passed = new ScreenRecognizer().Recognize(frame).Kind != ScreenKind.SaveTheWorldHub;
            if (!passed)
            {
                failures.Add(name);
            }

            reports.Add(new
            {
                name,
                Passed = passed,
                frame.Visuals
            });
        }
        await SaveReport();
        async Task SaveReport()
        {
            await File.WriteAllTextAsync(Path.Combine(output, "route-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
            if (failures.Count > 0)
            {
                throw new Exception("Route checks failed: " + string.Join(", ", failures));
            }
        }
    }
}
