using System.Drawing.Imaging;
using System.Text.Json;
using EndurancePilot.Core;
using OpenCvSharp;

namespace EndurancePilot.App;

internal static class ObjectiveProbe
{
    public static async Task Run(string input, string output)
    {
        Directory.CreateDirectory(output);
        using var source = new Bitmap(input);
        using var buffer = new MemoryStream();
        source.Save(buffer, ImageFormat.Png);
        using var color = Cv2.ImDecode(buffer.ToArray(), ImreadModes.Color);
        using var hsv = new Mat();
        Cv2.CvtColor(color, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(35, 140, 120), new Scalar(90, 255, 255), mask);
        Cv2.FindContours(mask, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var bars = contours.Select(c => (Bounds: Cv2.BoundingRect(c), Area: Cv2.ContourArea(c)))
            .Where(c => c.Bounds.Width >= 30 && c.Bounds.Height >= 2 && c.Bounds.Width >= c.Bounds.Height * 8
                && c.Bounds.Width < source.Width * .3 && c.Bounds.Height < source.Height * .04
                && c.Area >= c.Bounds.Width * c.Bounds.Height * .55)
            .OrderByDescending(c => c.Bounds.Width / (double)c.Bounds.Height).Take(8).ToArray();
        var ocr = new OcrService();
        var reports = new List<object>();
        foreach (var (bar, _) in bars)
        {
            var crop = Rectangle.Intersect(new(0, 0, source.Width, source.Height),
                new(bar.X - bar.Height * 8, bar.Y - bar.Height * 8, bar.Width + bar.Height * 16, bar.Height * 8));
            if (crop.Width == 0 || crop.Height == 0)
            {
                continue;
            }

            using var patch = source.Clone(crop, PixelFormat.Format32bppArgb);
            patch.Save(Path.Combine(output, $"candidate-{bar.X}-{bar.Y}.png"), ImageFormat.Png);
            foreach (int threshold in new[] { 0, 100, 130, 160, 190 })
            {
                foreach (int scale in new[] { 4, 6 })
                {
                    using var processed = new Bitmap(patch);
                    if (threshold > 0)
                    {
                        for (int y = 0; y < processed.Height; y++)
                        {
                            for (int x = 0; x < processed.Width; x++)
                            {
                                var pixel = processed.GetPixel(x, y);
                                int shade = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) >= threshold ? 0 : 255;
                                processed.SetPixel(x, y, Color.FromArgb(shade, shade, shade));
                            }
                        }
                    }

                    var regions = new List<TextRegion>();
                    double boundedScale = Math.Min(scale, 2600d / Math.Max(crop.Width, crop.Height));
                    using (var enlarged = new Bitmap((int)Math.Round(processed.Width * boundedScale), (int)Math.Round(processed.Height * boundedScale)))
                    {
                        using var graphics = Graphics.FromImage(enlarged);
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(processed, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
                        enlarged.Save(Path.Combine(output, $"variant-{bar.X}-{bar.Y}-{threshold}-{scale}.png"), ImageFormat.Png);
                    }
                    await ocr.ReadCrop(processed, new(0, 0, processed.Width, processed.Height), boundedScale, "objective-probe", regions, CancellationToken.None);
                    reports.Add(new
                    {
                        Bar = new
                        {
                            bar.X,
                            bar.Y,
                            bar.Width,
                            bar.Height
                        },
                        Crop = crop,
                        Scale = boundedScale,
                        Threshold = threshold,
                        Text = regions.Where(r => !r.Pass.EndsWith("-word")).Select(r => r.Text).ToArray()
                    });
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "objective-probe.json"), JsonSerializer.Serialize(reports, Inspection.Json));
    }
}
