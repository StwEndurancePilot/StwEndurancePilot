using System.Drawing.Imaging;
using EndurancePilot.Core;
using OpenCvSharp;

namespace EndurancePilot.App;

internal static class RewardCardDetector
{

    internal static VisualRegion? Find(Bitmap image, List<object>? trace = null)
    {
        using var bitmap = image.Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
        using var hsv = new Mat();
        var pixels = bitmap.LockBits(new Rectangle(System.Drawing.Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, pixels.Scan0, pixels.Stride);
            using var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        }
        finally { bitmap.UnlockBits(pixels); }
        var cards = new List<VisualRegion>();
        foreach (bool grey in new[] { false, true })
        {
            using var mask = new Mat();
            Cv2.InRange(hsv, grey ? new Scalar(0, 0, 60) : new Scalar(20, 140, 140),
                grey ? new Scalar(179, 55, 210) : new Scalar(40, 255, 255), mask);
            Cv2.FindContours(mask, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours)
            {
                var box = Cv2.BoundingRect(contour);
                if (box.Width > image.Width * .08)
                {
                    trace?.Add(new
                    {
                        Box = box,
                        Area = Cv2.ContourArea(contour)
                    });
                }

                if (box.Width < image.Width * .08 || box.Width > image.Width * .4
                    || box.Height < box.Width * .8 || box.Height > box.Width * 1.3
                    || Cv2.ContourArea(contour) < box.Width * box.Height * .9)
                {
                    continue;
                }

                int edge = Math.Max(1, box.Width / 100);

                int inset = Math.Max(1, box.Width / 200);
                var edges = new[]{new Rect(box.X+inset,box.Y+inset,box.Width-2*inset,edge),
                new Rect(box.X+inset,box.Bottom-inset-edge,box.Width-2*inset,edge),
                new Rect(box.X+inset,box.Y+inset,edge,box.Height-2*inset),
                new Rect(box.Right-inset-edge,box.Y+inset,edge,box.Height-2*inset)};
                if (edges.Any(rect =>
                {
                    using var side = new Mat(mask, rect);
                    return Cv2.CountNonZero(side) < side.Width * side.Height * .85;
                }))
                {
                    continue;
                }

                using var inside = new Mat(mask, new Rect(box.X + box.Width / 4, box.Y + box.Height / 4, box.Width / 2, box.Height / 2));
                trace?.Add(new
                {
                    Inside = (double)Cv2.CountNonZero(inside) / (inside.Width * inside.Height)
                });
                if (!grey && Cv2.CountNonZero(inside) > inside.Width * inside.Height * .4)
                {
                    continue;
                }

                var bounds = new Box(box.X, box.Y, box.Width, box.Height);
                if (!cards.Any(c => c.Bounds.IntersectionOverUnion(bounds) > .8))
                {
                    cards.Add(new("reward-card", bounds, 1, 1));
                }
            }
        }
        return cards.Count == 1 ? cards[0] : null;
    }
}
