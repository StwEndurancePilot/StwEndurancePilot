using System.Drawing.Imaging;
using EndurancePilot.Core;
using OpenCvSharp;

namespace EndurancePilot.App;

internal static class HealthBarDetector
{

    internal static IReadOnlyList<VisualRegion> Find(Bitmap image, List<object>? trace = null)
    {
        using var bitmap = image.Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
        using var bgr = new Mat();
        var pixels = bitmap.LockBits(new Rectangle(System.Drawing.Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, pixels.Scan0, pixels.Stride);
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        }
        finally { bitmap.UnlockBits(pixels); }
        using var hsv = new Mat();
        using var green = new Mat();
        using var white = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var found = new List<VisualRegion>();

        foreach (var brightness in new[] { (Bar: 95, Cross: 195), (Bar: 40, Cross: 65) })
        {
            foreach (int hue in new[] { 35, 0, 15, 55, 75, 95, 115, 135, 155 })
            {
                Cv2.InRange(hsv, new Scalar(hue, 95, brightness.Bar), new Scalar(Math.Min(179, hue + 37), 255, 255), green);
                Cv2.InRange(hsv, new Scalar(0, 0, brightness.Cross), new Scalar(179, 60, 255), white);
                Cv2.FindContours(green, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                foreach (var contour in contours)
                {
                    var bar = Cv2.BoundingRect(contour);
                    if (bar.Height < 3 || bar.Width < 30 || bar.Width < bar.Height * 4 || bar.Width > bar.Height * 60
                        || Cv2.ContourArea(contour) < bar.Width * bar.Height * .65)
                    {
                        continue;
                    }

                    trace?.Add(new
                    {
                        Bar = bar
                    });
                    int left = Math.Max(0, bar.X - bar.Height * 2), top = Math.Max(0, bar.Y - bar.Height / 2);
                    var search = new Rect(left, top, bar.X - left, Math.Min(image.Height - top, bar.Height * 2));
                    if (search.Width < 3 || search.Height < 3)
                    {
                        continue;
                    }

                    using var crosses = new Mat(white, search);
                    Cv2.FindContours(crosses, out OpenCvSharp.Point[][] shapes, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    foreach (var shape in shapes)
                    {
                        var cross = Cv2.BoundingRect(shape);
                        if (cross.Height < 3 || cross.Width < 3 || cross.Height < bar.Height * .35 || cross.Height > bar.Height * 1.3
                            || cross.Width < cross.Height * .7 || cross.Width > cross.Height * 1.4
                            || Math.Abs(top + cross.Y + cross.Height / 2d - bar.Y - bar.Height / 2d) > bar.Height * .45)
                        {
                            continue;
                        }

                        using var glyph = new Mat(crosses, cross);
                        double Fill(Rect area)
                        {
                            using var part = new Mat(glyph, area);
                            return (double)Cv2.CountNonZero(part) / (area.Width * area.Height);
                        }
                        int thirdX = Math.Max(1, cross.Width / 3), thirdY = Math.Max(1, cross.Height / 3);
                        double vertical = Enumerable.Range(thirdX, Math.Max(1, cross.Width - 2 * thirdX)).Max(x => Fill(new(x, 0, 1, cross.Height)));
                        double horizontal = Enumerable.Range(thirdY, Math.Max(1, cross.Height - 2 * thirdY)).Max(y => Fill(new(0, y, cross.Width, 1)));
                        double corners = (Fill(new(0, 0, thirdX, thirdY)) + Fill(new(cross.Width - thirdX, 0, thirdX, thirdY))
                            + Fill(new(0, cross.Height - thirdY, thirdX, thirdY)) + Fill(new(cross.Width - thirdX, cross.Height - thirdY, thirdX, thirdY))) / 4;
                        trace?.Add(new
                        {
                            Bar = bar,
                            Cross = cross,
                            vertical,
                            horizontal,
                            corners
                        });
                        if (vertical < .65 || horizontal < .65 || corners > .28)
                        {
                            continue;
                        }

                        int x = left + cross.X, y = Math.Min(top + cross.Y, bar.Y), bottom = Math.Max(top + cross.Bottom, bar.Bottom);
                        var bounds = new Box(x, y, bar.Right - x, bottom - y);
                        if (!found.Any(prior => prior.Bounds.IntersectionOverUnion(bounds) > .6))
                        {
                            found.Add(new("player-health-bar", bounds, 1, 1));
                        }

                        break;
                    }
                }
            }
        }

        return found.Where(candidate => !found.Any(lower =>
                    lower.Bounds.Y >= candidate.Bounds.Bottom
                    && lower.Bounds.Y - candidate.Bounds.Bottom < candidate.Bounds.Height * 2
                    && Math.Abs(lower.Bounds.X - candidate.Bounds.X) < candidate.Bounds.Height
                    && Math.Abs(lower.Bounds.Right - candidate.Bounds.Right) < candidate.Bounds.Height)).ToArray();
    }
}
