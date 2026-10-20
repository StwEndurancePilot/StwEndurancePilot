using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class DiscoveryChecks
{
    internal static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        int failures = 0;
        var ocr = new OcrService();
        var recognizer = new ScreenRecognizer();
        var visual = new VisualRecognizer();
        NavigationStep[] steps = [NavigationStep.EnterSaveTheWorld,NavigationStep.SelectSaveTheWorld,NavigationStep.FilterByEpic,
            NavigationStep.SelectSaveTheWorld,NavigationStep.ConfirmSaveTheWorld,NavigationStep.EnterSaveTheWorld];
        Box[] boxes = [new(90, 860, 340, 90), new(70, 870, 280, 160), new(1300, 125, 160, 85), new(68, 405, 315, 176), new(78, 881, 320, 65), new(95, 870, 335, 85)];
        for (int n = 1; n <= 9; n++)
        {
            using var source = new Bitmap(Path.Combine(captures, $"discover-fallback-{(n == 7 ? 2 : n == 8 ? 4 : n == 9 ? 3 : n)}.png"));
            if (n is 7 or 8)
            {
                using var g = Graphics.FromImage(source);
                g.FillRectangle(Brushes.Black, n == 7 ? new Rectangle(70, 870, 280, 160) : new Rectangle(68, 405, 315, 176));
            }
            if (n == 9)
            {

                using var selected = new Bitmap(Path.Combine(captures, "discover-fallback-4.png"));
                using var g = Graphics.FromImage(source);
                g.DrawImage(selected, new Rectangle(1303, 128, 146, 72), new Rectangle(1307, 136, 146, 72), GraphicsUnit.Pixel);
            }
            foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (1920, 1200), (3440, 1440) })
            {
                using var bitmap = new Bitmap(width, height);
                double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
                float x = (float)(width - source.Width * scale) / 2, y = (float)(height - source.Height * scale) / 2;
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, x, y, (float)(source.Width * scale), (float)(source.Height * scale));
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var frame = await ocr.ReadAsync(bitmap);
                var screen = recognizer.Recognize(frame);
                frame = visual.Enrich(bitmap, frame, screen.Kind);
                screen = recognizer.Recognize(frame);
                var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                var expected = n <= 6 ? boxes[n - 1] : n == 9 ? new Box(76, 132, 813, 64) : new Box(974, 1038, 54, 31);
                var box = new Box(x + expected.X * scale, y + expected.Y * scale, expected.Width * scale, expected.Height * scale);
                bool passed = n == 8 ? plan is null : plan?.Step == (n == 7 ? NavigationStep.OpenDiscover : n == 9 ? NavigationStep.SearchSaveTheWorld : steps[n - 1])
                    && plan.Target.CenterX >= box.X && plan.Target.CenterX <= box.Right && plan.Target.CenterY >= box.Y && plan.Target.CenterY <= box.Bottom;
                string name = $"discover-{n}-{width}x{height}";
                if (!passed)
                {
                    failures++;
                }

                reports.Add(new
                {
                    name,
                    Passed = passed,
                    screen.Kind,
                    plan,
                    Milliseconds = clock.ElapsedMilliseconds
                });
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen,
                    Plan = plan
                }, Inspection.Json));
            }
        }
        foreach (string variant in new[] { "visible", "removed", "duplicate" })
        {
            foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (2246, 1326), (3440, 1440) })
            {
                using var source = new Bitmap(Path.Combine(captures, "discover-horde-carousel.png"));
                var tileBounds = new Rectangle(58, 1096, 342, 194);
                if (variant == "removed")
                {
                    using var graphics = Graphics.FromImage(source);
                    graphics.FillRectangle(Brushes.Black, tileBounds);
                }
                if (variant == "duplicate")
                {
                    using var tile = source.Clone(tileBounds, source.PixelFormat);
                    using var graphics = Graphics.FromImage(source);
                    graphics.DrawImageUnscaled(tile, 470, tileBounds.Y);
                }
                using var bitmap = new Bitmap(width, height);
                double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
                float x = (float)(width - source.Width * scale) / 2, y = (float)(height - source.Height * scale) / 2;
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, x, y, (float)(source.Width * scale), (float)(source.Height * scale));
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var frame = await ocr.ReadAsync(bitmap);
                var screen = recognizer.Recognize(frame);
                frame = visual.Enrich(bitmap, frame, screen.Kind);
                screen = recognizer.Recognize(frame);
                var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                var box = new Box(x + tileBounds.X * scale, y + tileBounds.Y * scale, tileBounds.Width * scale, tileBounds.Height * scale);
                bool passed = variant == "visible" ? plan?.Step == NavigationStep.SelectSaveTheWorld
                    && plan.Target.CenterX >= box.X && plan.Target.CenterX <= box.Right && plan.Target.CenterY >= box.Y && plan.Target.CenterY <= box.Bottom
                    : plan?.Step != NavigationStep.SelectSaveTheWorld && plan?.Step != NavigationStep.EnterSaveTheWorld;
                string name = $"horde-carousel-{variant}-{width}x{height}";
                if (!passed)
                {
                    failures++;
                }

                reports.Add(new
                {
                    name,
                    Passed = passed,
                    screen.Kind,
                    plan,
                    Milliseconds = clock.ElapsedMilliseconds
                });
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = screen,
                    Plan = plan
                }, Inspection.Json));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            Checks = reports.Count,
            Failures = failures,
            Reports = reports
        }, Inspection.Json));
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }
}
