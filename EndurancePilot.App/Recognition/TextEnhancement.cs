using System.Drawing.Imaging;
using OpenCvSharp;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal enum TextTreatment
{
    Original, LocalContrast, WhiteText, Threshold
}

internal static class TextEnhancement
{
    public static Rectangle[] Crops(Observation frame)
    {
        var crops = new List<Rectangle>();
        void Add(Rectangle crop)
        {
            crop = Rectangle.Intersect(new(0, 0, frame.Width, frame.Height), crop);
            var box = new Box(crop.X, crop.Y, crop.Width, crop.Height);
            if (crop.Width > 0 && crop.Height > 0 && !crops.Any(c => box.IntersectionOverUnion(new(c.X, c.Y, c.Width, c.Height)) > .35))
            {
                crops.Add(crop);
            }
        }
        foreach (var line in frame.Find(@"\b(ENDURANCE|STORM|SHIELD|ACTIVATE|WAVE|LAUNCH|REJOIN|ABANDON)\b")
            .Where(r => r.Bounds.Height < 30 && !r.Pass.EndsWith("-word")).OrderBy(r => r.Bounds.Height))
        {
            var b = line.Bounds;
            double h = Math.Max(8, b.Height);
            Add(Rectangle.FromLTRB((int)(b.X - h * 2), (int)(b.Y - h), (int)(b.X + Math.Max(b.Width + h * 2, h * 28)), (int)(b.Bottom + h)));
        }
        foreach (var timer in frame.Find(@"^\d{1,2}:\d{2}$").Where(r => r.Bounds.Height < 24))
        {
            var b = timer.Bounds;
            double h = Math.Max(8, b.Height);
            Add(new((int)(b.CenterX - h * 14), (int)(b.Y - h * 2), (int)(h * 28), (int)(h * 2)));
        }
        return crops.Take(4).ToArray();
    }

    public static byte[] Encode(Bitmap image, TextTreatment treatment)
    {
        using var encoded = new MemoryStream();
        image.Save(encoded, ImageFormat.Png);
        if (treatment == TextTreatment.Original)
        {
            return encoded.ToArray();
        }

        using var color = Cv2.ImDecode(encoded.ToArray(), ImreadModes.Color);
        using var gray = new Mat();
        if (treatment == TextTreatment.WhiteText)
        {
            var channels = Cv2.Split(color);
            try
            {
                Cv2.Min(channels[0], channels[1], gray);
                Cv2.Min(gray, channels[2], gray);
            }
            finally
            {
                foreach (var channel in channels)
                {
                    channel.Dispose();
                }
            }

            gray.ConvertTo(gray, MatType.CV_8U, -2.5, 255 + 110 * 2.5);
        }
        else
        {
            Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Normalize(gray, gray, 0, 255, NormTypes.MinMax);
            using var clahe = Cv2.CreateCLAHE(2, new OpenCvSharp.Size(8, 8));
            clahe.Apply(gray, gray);
            if (treatment == TextTreatment.Threshold)
            {
                Cv2.Threshold(gray, gray, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            }
        }
        return gray.ToBytes(".png");
    }
}
