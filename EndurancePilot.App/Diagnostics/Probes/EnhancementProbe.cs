using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class EnhancementProbe
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var reports = new List<object>();
        foreach (string name in new[]{"live-hud25-2560x1392-wave7","live-hud25-2560x1392-wave8",
            "live-hud25-2560x1392-wave8-countdown","live-inactive-objective-small-1918x1030","live-smallhud-wave2-1918x1030"})
        {
            using var image = new Bitmap(Path.Combine(captures, name + ".png"));
            var baseline = await ocr.ReadAsync(image, enhance: false);
            int index = 0;
            foreach (var crop in TextEnhancement.Crops(baseline))
            {
                foreach (var treatment in Enum.GetValues<TextTreatment>())
                {
                    var regions = new List<TextRegion>();
                    await ocr.ReadCrop(image, crop, 4, "text-anchor-enhancement-probe", regions, CancellationToken.None, treatment);
                    var frame = baseline with
                    {
                        Regions = [.. baseline.Regions, .. regions]
                    };
                    reports.Add(new
                    {
                        Name = name,
                        Crop = index,
                        Treatment = treatment.ToString(),
                        Bounds = crop,
                        Text = regions.Where(r => !r.Pass.EndsWith("-word")).Select(r => r.Text).ToArray(),
                        Kind = new ScreenRecognizer().Recognize(frame).Kind,
                        Waves = WaveReader.Read(frame).Select(r => r.Wave).Distinct().ToArray(),
                        Countdown = WaveReader.ReadCountdown(frame).Select(r => r.Wave).Distinct().ToArray()
                    });
                }
                index++;
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "enhancement-probe.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        var contrastReports = new List<object>();
        foreach (var name in new[] { "rejoin-dialog-user", "live-console-menu-1918x1030" })
        {
            foreach (float contrast in new[] { .03f, .08f, .2f })
            {
                using var original = new Bitmap(Path.Combine(captures, name + ".png"));
                using var washed = new Bitmap(original.Width, original.Height);
                using (var graphics = Graphics.FromImage(washed))
                using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                {
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]{
                    new[]{contrast,0f,0f,0f,0f},new[]{0f,contrast,0f,0f,0f},new[]{0f,0f,contrast,0f,0f},
                    new[]{0f,0f,0f,1f,0f},new[]{.45f,.45f,.45f,0f,1f}}));
                    graphics.DrawImage(original, new Rectangle(0, 0, washed.Width, washed.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
                var baseline = await ocr.ReadAsync(washed, enhance: false);
                var enhanced = new List<TextRegion>();
                await ocr.ReadCrop(washed, new(0, 0, washed.Width, washed.Height), Math.Min(1, 1920d / washed.Width), "enhanced-overview", enhanced, CancellationToken.None, TextTreatment.LocalContrast);
                var improved = baseline with
                {
                    Regions = [.. baseline.Regions, .. enhanced]
                };
                contrastReports.Add(new
                {
                    Name = name,
                    Contrast = contrast,
                    Baseline = new ScreenRecognizer().Recognize(baseline).Kind,
                    Enhanced = new ScreenRecognizer().Recognize(improved).Kind
                });
                washed.Save(Path.Combine(output, $"{name}-contrast{contrast}.png"));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, "contrast-probe.json"), JsonSerializer.Serialize(contrastReports, Inspection.Json));
    }
}

