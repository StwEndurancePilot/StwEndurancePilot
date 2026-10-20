using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class HudDiagnostics
{
    public static async Task Run(string input, string output)
    {
        Directory.CreateDirectory(output);
        using var original = new Bitmap(input);
        var ocr = new OcrService();
        var frame = await ocr.ReadAsync(original);
        var timers = frame.Find(@"^\d{1,2}:\d{2}$").Where(r => r.Bounds.Height < 24).Take(4).ToArray();
        var reports = new List<object>();
        foreach (var timer in timers)
        {
            double h = Math.Max(8, timer.Bounds.Height);
            var bounds = Rectangle.Intersect(new(0, 0, original.Width, original.Height),
                new((int)(timer.Bounds.CenterX - h * 7), (int)(timer.Bounds.Y - h * 1.4), (int)(h * 14), (int)(h * 1.4)));
            foreach (int threshold in new[] { 0, 90, 110, 130, 150, 170, 190, 210 })
            {
                foreach (int scale in new[] { 3, 4, 6 })
                {
                    using var patch = original.Clone(bounds, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    if (threshold > 0)
                    {
                        for (int y = 0; y < patch.Height; y++)
                        {
                            for (int x = 0; x < patch.Width; x++)
                            {
                                var pixel = patch.GetPixel(x, y);
                                int white = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                                int shade = 255 - Math.Clamp((white - threshold) * 8, 0, 255);
                                patch.SetPixel(x, y, Color.FromArgb(shade, shade, shade));
                            }
                        }
                    }

                    var regions = new List<TextRegion>();
                    using (var enlarged = new Bitmap(patch.Width * scale, patch.Height * scale))
                    {
                        using var graphics = Graphics.FromImage(enlarged);
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        graphics.DrawImage(patch, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
                        enlarged.Save(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(input)}-{reports.Count}-{threshold}-{scale}.png"));
                    }
                    await ocr.ReadCrop(patch, new(0, 0, patch.Width, patch.Height), scale, "text-anchor-probe", regions, CancellationToken.None);
                    reports.Add(new
                    {
                        bounds,
                        threshold,
                        scale,
                        Text = regions.Where(r => !r.Pass.EndsWith("-word")).Select(r => r.Text).ToArray()
                    });
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, Path.GetFileNameWithoutExtension(input) + "-probe.json"), JsonSerializer.Serialize(reports, Inspection.Json));
    }
}
