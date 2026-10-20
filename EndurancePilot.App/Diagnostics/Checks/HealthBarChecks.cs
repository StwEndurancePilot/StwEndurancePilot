using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class HealthBarChecks
{
    internal static void Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        int failures = 0;
        void Check(string name, Bitmap bitmap, bool expected)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var trace = new List<object>();
            var found = HealthBarDetector.Find(bitmap, trace);
            bool passed = (found.Count == 1) == expected;
            if (!passed)
            {
                failures++;
            }

            reports.Add(new
            {
                name,
                Passed = passed,
                Found = found,
                Milliseconds = clock.ElapsedMilliseconds
            });
            if (!passed)
            {
                File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(trace, Inspection.Json));
            }
        }
        using var reference = new Bitmap(Path.Combine(captures, "health-bar-reference.png"));
        foreach (double scale in new[] { .25, .5, 1, 1.5, 2 })
        {
            foreach (string variant in new[] { "bar", "half-full", "no-cross", "no-colour", "duplicate", "blue", "dim" })
            {
                using var crop = new Bitmap(reference);
                using (var g = Graphics.FromImage(crop))
                {
                    if (variant == "no-cross")
                    {
                        g.FillRectangle(Brushes.Black, 0, 0, 32, crop.Height);
                    }

                    if (variant == "no-colour")
                    {
                        g.FillRectangle(Brushes.Black, 32, 0, crop.Width - 32, crop.Height);
                    }

                    if (variant == "half-full")
                    {
                        g.FillRectangle(Brushes.Black, crop.Width / 2, 0, crop.Width / 2 + 1, crop.Height);
                    }
                }
                if (variant is "blue" or "dim")
                {
                    for (int y = 0; y < crop.Height; y++)
                    {
                        for (int x = 0; x < crop.Width; x++)
                        {
                            var pixel = crop.GetPixel(x, y);
                            crop.SetPixel(x, y, variant == "blue" ? Color.FromArgb(pixel.G, pixel.B, pixel.R)
                                : Color.FromArgb(pixel.R / 2, pixel.G / 2, pixel.B / 2));
                        }
                    }
                }

                using var bitmap = new Bitmap(1920, 1080);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.FromArgb(16, 20, 40));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(crop, 80, 800, (float)(crop.Width * scale), (float)(crop.Height * scale));
                    if (variant == "duplicate")
                    {
                        g.DrawImage(crop, 80, 600, (float)(crop.Width * scale), (float)(crop.Height * scale));
                    }
                }
                Check($"reference-{variant}-{scale}", bitmap, variant is "bar" or "half-full" or "blue" or "dim");
            }
        }

        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (1920, 1200), (3440, 1440) })
        {
            using var source = new Bitmap(Path.Combine(captures, "stw-hub-native.png"));
            using var bitmap = new Bitmap(width, height);
            double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, (float)(width - source.Width * scale) / 2, (float)(height - source.Height * scale) / 2,
                    (float)(source.Width * scale), (float)(source.Height * scale));
            }
            Check($"hub-{width}x{height}", bitmap, true);
        }
        foreach (string name in new[]{"stw-hub-native","stw-hub-quest-prompt-native","live-post-reward-hub-1920x1080",
            "twine-homebase-spawn-native","live-inactive-objective-small-1918x1030","twine-wave2-active-native",
            "live-hud25-2560x1392-wave7","live-hud50-1920x1080-wave10","live-hud125-1920x1200-wave9"})
        {
            using var bitmap = new Bitmap(Path.Combine(captures, name + ".png"));
            Check(name, bitmap, true);
        }
        foreach (string name in new[] { "discover-horde-carousel", "discover-fallback-1", "discover-fallback-3", "twine-homebase-selected-native", "homebase-launch-native" })
        {
            using var bitmap = new Bitmap(Path.Combine(captures, name + ".png"));
            Check(name, bitmap, false);
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            Checks = reports.Count,
            Failures = failures,
            Reports = reports
        }, Inspection.Json));
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }
}
