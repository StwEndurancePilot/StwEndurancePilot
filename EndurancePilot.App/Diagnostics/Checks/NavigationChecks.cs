using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class NavigationChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        var failures = new List<string>();
        var ocr = new OcrService();
        var vision = new VisualRecognizer();
        foreach (var (name, id) in new[]
        {
            ("live-discovery-1920x1080", "stw-discovery-tile"),
            ("stonewood-missions-native", "homebase-icon"),
            ("live-twine-triangle-stall-1918x1030", "homebase-icon"),
            ("homebase-launch-native", "own-homebase-selected"),
            ("world-map-stonewood-native", "next-zone"),
            ("live-canny-chevron-1918x1030", "next-zone"),
            ("live-canny-chevron-1918x1030", "previous-zone")
        })
        {
            using var original = new Bitmap(Path.Combine(captures, name + ".png"));
            foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (3440, 1440) })
            {
                using var image = new Bitmap(width, height);
                double scale = Math.Min(width / (double)original.Width, height / (double)original.Height);
                var destination = new Rectangle((width - (int)Math.Round(original.Width * scale)) / 2,
                    (height - (int)Math.Round(original.Height * scale)) / 2,
                    (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale));
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.FromArgb(15, 20, 30));
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, destination);
                }
                await Verify(image, $"{name}-{id}-{width}x{height}", id, expectPresent: true);
            }
            var observed = await ocr.ReadAsync(original);
            var screen = new ScreenRecognizer().Recognize(observed);
            var enriched = vision.Enrich(original, observed, screen.Kind);
            var target = NavigationPlanner.ReliableVisual(enriched, id)
                ?? throw new Exception("Source anchor missing: " + name);
            using var absent = new Bitmap(original);
            using (var graphics = Graphics.FromImage(absent))
            {
                graphics.FillRectangle(id == "homebase-icon" ? Brushes.Black : Brushes.White,
                    (float)target.X - 3, (float)target.Y - 3, (float)target.Width + 6, (float)target.Height + 6);
            }

            await Verify(absent, name + "-" + id + "-removed", id, expectPresent: false);
            if (id == "homebase-icon")
            {
                using var duplicate = new Bitmap(original);
                using var crop = original.Clone(Rectangle.Round(new RectangleF((float)target.X, (float)target.Y,
                    (float)target.Width, (float)target.Height)), PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(duplicate))
                {
                    graphics.DrawImageUnscaled(crop, 1500, 900);
                }

                await Verify(duplicate, name + "-ambiguous", id, expectPresent: false);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "navigation-checks.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception(string.Join(Environment.NewLine, failures));
        }

        async Task Verify(Bitmap image, string name, string id, bool expectPresent)
        {
            var started = DateTimeOffset.UtcNow;
            var observed = await ocr.ReadAsync(image);
            var screen = new ScreenRecognizer().Recognize(observed);
            observed = vision.Enrich(image, observed, screen.Kind);
            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                Observation = observed,
                Screen = screen
            }, Inspection.Json));
            bool present = NavigationPlanner.ReliableVisual(observed, id) is not null;
            bool passed = present == expectPresent;
            bool? mergedOwnRowLaunch = null;
            bool? missingLaunchRecovered = null;
            bool? erasedLaunchRejected = null;
            if (id == "own-homebase-selected" && expectPresent)
            {

                var merged = observed with
                {
                    Regions = observed.Regions.Select(r => r.Normalized == "MY STORM SHIELD"
                    ? r with
                    {
                        Text = "MY STORM SHIELD V Access your own Storm Shield"
                    } : r).ToArray(),
                    Visuals = null
                };
                var mergedScreen = new ScreenRecognizer().Recognize(merged);
                merged = vision.Enrich(image, merged, mergedScreen.Kind);
                mergedOwnRowLaunch = NavigationPlanner.Decide(merged, mergedScreen, new(HomebaseZone.TwinePeaks, true)).Plan?.Step
                    == NavigationStep.LaunchHomebase;
                passed &= mergedOwnRowLaunch == true;

                var missed = observed with
                {
                    Regions = observed.Regions.Except(observed.Find(NavigationPlanner.MissionLaunchLabel)).ToArray()
                };
                var recovered = missed.Regions.ToList();
                await ocr.ReadChoiceControls(image, missed, recovered);
                var recoveredFrame = missed with
                {
                    Regions = recovered
                };
                missingLaunchRecovered = NavigationPlanner.Decide(recoveredFrame, screen, new(HomebaseZone.TwinePeaks, true)).Plan?.Step == NavigationStep.LaunchHomebase;
                using var erased = new Bitmap(image);
                using (var g = Graphics.FromImage(erased))
                {
                    foreach (var launch in observed.Find(NavigationPlanner.MissionLaunchLabel))
                    {
                        g.FillRectangle(Brushes.Black, (float)launch.Bounds.X - 3, (float)launch.Bounds.Y - 3,
                            (float)launch.Bounds.Width + 6, (float)launch.Bounds.Height + 6);
                    }
                }

                var absentLaunch = missed.Regions.ToList();
                await ocr.ReadChoiceControls(erased, missed, absentLaunch);
                erasedLaunchRejected = AutomationController.UniqueTarget((missed with { Regions = absentLaunch }).Find(NavigationPlanner.MissionLaunchLabel).ToArray()) is null;
                passed &= missingLaunchRecovered == true && erasedLaunchRejected == true;
            }
            bool? hoverAccepted = null, hoverPixelsChanged = null;
            if (id == "homebase-icon" && expectPresent && NavigationPlanner.ReliableVisual(observed, id) is { } icon)
            {
                using var hover = new Bitmap(image);
                var bounds = Rectangle.Round(new RectangleF((float)icon.X, (float)icon.Y, (float)icon.Width, (float)icon.Height));
                using var source = image.Clone(bounds, PixelFormat.Format32bppArgb);
                using var attributes = new ImageAttributes();

                attributes.SetColorMatrix(new ColorMatrix(new[] {
                    new[] {.6f,0,0,0,0}, new[] {0,.6f,0,0,0}, new[] {0,0,.6f,0,0},
                    new[] {0f,0,0,1,0}, new[] {.3f,.3f,.3f,0,1} }));
                using (var graphics = Graphics.FromImage(hover))
                {
                    graphics.DrawImage(source, bounds, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }

                hoverPixelsChanged = !GameInput.TargetStillMatches(image, hover, icon);
                hoverAccepted = vision.HomebaseTargetStillPresent(hover, icon);
                using var removed = new Bitmap(hover);
                using (var graphics = Graphics.FromImage(removed))
                {
                    graphics.FillRectangle(Brushes.Black, bounds);
                }

                passed &= hoverPixelsChanged == true && hoverAccepted == true
                    && !vision.HomebaseTargetStillPresent(removed, icon);
            }
            var profile = new RunnerProfile(id == "next-zone" ? HomebaseZone.TwinePeaks : HomebaseZone.Stonewood);
            var decision = NavigationPlanner.Decide(observed, screen, profile);
            if (expectPresent)
            {
                var expectedStep = id switch
                {
                    "stw-discovery-tile" => NavigationStep.SelectSaveTheWorld,
                    "next-zone" or "previous-zone" => NavigationStep.ChangeZone,
                    "homebase-icon" => NavigationStep.SelectHomebase,
                    _ => NavigationStep.LaunchHomebase
                };
                passed &= decision.Plan?.Step == expectedStep;
            }
            if (!passed)
            {
                failures.Add(name + ": expected target " + (expectPresent ? "present" : "rejected"));
            }

            reports.Add(new
            {
                Name = name,
                Synthetic = true,
                image.Width,
                image.Height,
                ExpectedPresent = expectPresent,
                Passed = passed,
                HoverPixelsChanged = hoverPixelsChanged,
                HoverAccepted = hoverAccepted,
                MergedOwnRowLaunch = mergedOwnRowLaunch,
                MissingLaunchRecovered = missingLaunchRecovered,
                ErasedLaunchRejected = erasedLaunchRejected,
                Milliseconds = (DateTimeOffset.UtcNow - started).TotalMilliseconds,
                screen.Kind,
                observed.Visuals,
                decision.Plan
            });
            using var annotated = OcrService.Annotate(image, observed);
            annotated.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        }
    }
}
