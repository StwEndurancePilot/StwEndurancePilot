using System.Drawing.Imaging;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class SmallTextDiagnostics
{
    public static async Task Run(string input, string output)
    {
        Directory.CreateDirectory(output);
        using var original = new Bitmap(input);
        var ocr = new OcrService();
        var frame = await ocr.ReadAsync(original);
        var anchors = frame.Regions.Where(r => !r.Pass.EndsWith("-word") && r.Bounds.Height is >= 5 and < 12
            && r.Normalized.Length >= 3).Take(16).ToArray();
        var reports = new List<object>();
        foreach (var anchor in anchors)
        {
            foreach (int columns in new[] { 18, 38 })
            {
                foreach (int rows in new[] { 3, 6 })
                {
                    foreach (int scale in new[] { 4, 6 })
                    {
                        foreach (int stretch in new[] { 1, 2 })
                        {
                            foreach (int threshold in new[] { 0, 150, 180 })
                            {
                                double h = Math.Max(8, anchor.Bounds.Height);
                                var bounds = Rectangle.Intersect(new(0, 0, original.Width, original.Height),
                                    new((int)(anchor.Bounds.X - h * 2), (int)(anchor.Bounds.Y - h), (int)(h * columns), (int)(h * rows)));
                                using var crop = original.Clone(bounds, PixelFormat.Format32bppArgb);
                                if (threshold > 0)
                                {
                                    for (int y = 0; y < crop.Height; y++)
                                    {
                                        for (int x = 0; x < crop.Width; x++)
                                        {
                                            var p = crop.GetPixel(x, y);
                                            int shade = 255 - Math.Clamp((Math.Min(p.R, Math.Min(p.G, p.B)) - threshold) * 8, 0, 255);
                                            crop.SetPixel(x, y, Color.FromArgb(shade, shade, shade));
                                        }
                                    }
                                }

                                var regions = new List<TextRegion>();
                                using var widened = new Bitmap(crop.Width * stretch, crop.Height);
                                using (var graphics = Graphics.FromImage(widened))
                                {
                                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                                    graphics.DrawImage(crop, new Rectangle(0, 0, widened.Width, widened.Height));
                                }
                                await ocr.ReadCrop(widened, new(0, 0, widened.Width, widened.Height), scale, "text-anchor-probe", regions, CancellationToken.None);
                                var text = regions.Where(r => !r.Pass.EndsWith("-word")).Select(r => r.Text).ToArray();
                                reports.Add(new
                                {
                                    Anchor = anchor.Text,
                                    bounds,
                                    columns,
                                    rows,
                                    scale,
                                    stretch,
                                    threshold,
                                    Text = text
                                });
                            }
                        }
                    }
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, Path.GetFileNameWithoutExtension(input) + "-smalltext.json"),
            JsonSerializer.Serialize(reports, Inspection.Json));
    }
}
