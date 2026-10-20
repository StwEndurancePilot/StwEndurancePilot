using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using EndurancePilot.Core;
using OpenCvSharp;

namespace EndurancePilot.App;

internal static class PaddedHudProbe
{
    public static async Task Run(string input, string output)
    {
        Directory.CreateDirectory(output);
        using var original = new Bitmap(input);
        var ocr = new OcrService();
        var observation = await ocr.ReadAsync(original);
        var timers = new List<TextRegion>();
        foreach (var timer in observation.Find(@"^\d{1,2}:\d{2}$").Where(r => r.Bounds.Height < 24))
        {
            if (!timers.Any(t => t.Bounds.IntersectionOverUnion(timer.Bounds) > .3))
            {
                timers.Add(timer);
            }
        }

        var reports = new List<object>();
        foreach (var timer in timers.Take(2))
        {
            foreach (int trim in new[] { 0, 2, 4 })
            {
                foreach (int threshold in new[] { 0, 110, 150, 180, -3, -5 })
                {
                    foreach (int scale in new[] { 4, 6 })
                    {
                        double h = Math.Max(8, timer.Bounds.Height);
                        var bounds = Rectangle.Intersect(new(0, 0, original.Width, original.Height),
                            Rectangle.FromLTRB((int)(timer.Bounds.CenterX - h * (7 - trim)), (int)(timer.Bounds.Y - h * 1.4),
                                (int)(timer.Bounds.CenterX + h * 7), (int)timer.Bounds.Y));
                        if (bounds.IsEmpty)
                        {
                            continue;
                        }

                        using var crop = original.Clone(bounds, PixelFormat.Format32bppArgb);
                        if (threshold < 0)
                        {
                            using var encoded = new MemoryStream();
                            crop.Save(encoded, ImageFormat.Png);
                            using var color = Cv2.ImDecode(encoded.ToArray(), ImreadModes.Color);
                            using var green = new Mat();
                            Cv2.ExtractChannel(color, green, 1);
                            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(-threshold, 3));
                            using var strokes = new Mat();
                            Cv2.MorphologyEx(green, strokes, MorphTypes.TopHat, kernel);
                            Cv2.Normalize(strokes, strokes, 0, 255, NormTypes.MinMax);
                            Cv2.BitwiseNot(strokes, strokes);
                            using var processedStream = new MemoryStream(strokes.ToBytes(".png"));
                            using var processed = new Bitmap(processedStream);
                            using var graphics = Graphics.FromImage(crop);
                            graphics.DrawImageUnscaled(processed, 0, 0);
                        }
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

                        using var padded = new Bitmap(crop.Width * scale + 64, crop.Height * scale + 64);
                        using (var g = Graphics.FromImage(padded))
                        {
                            g.Clear(threshold != 0 ? Color.White : crop.GetPixel(crop.Width - 1, 0));
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(crop, new Rectangle(32, 32, crop.Width * scale, crop.Height * scale));
                        }
                        var regions = new List<TextRegion>();
                        await ocr.ReadCrop(padded, new(0, 0, padded.Width, padded.Height), 1, "padded-probe", regions, CancellationToken.None);
                        var result = new Observation(observation.CapturedAt, padded.Width, padded.Height, regions);
                        reports.Add(new
                        {
                            bounds,
                            trim,
                            threshold,
                            scale,
                            Text = regions.Where(r => !r.Pass.EndsWith("-word")).Select(r => r.Text).ToArray(),
                            ActiveWaves = WaveReader.Read(result).Select(r => r.Wave).Distinct().ToArray(),
                            CountdownWaves = WaveReader.ReadCountdown(result).Select(r => r.Wave).Distinct().ToArray()
                        });
                    }
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(output, Path.GetFileNameWithoutExtension(input) + "-padded.json"),
            JsonSerializer.Serialize(reports, Inspection.Json));
    }
}
