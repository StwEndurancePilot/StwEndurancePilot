using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace EndurancePilot.App;

internal static class NotificationScreenshot
{
    internal const int MaxBytes = 2 * 1024 * 1024;

    internal static byte[]? Encode(Bitmap frame)
    {
        try
        {
            double scale = Math.Min(1, 1920d / Math.Max(frame.Width, frame.Height));
            using var image = new Bitmap(Math.Max(1, (int)(frame.Width * scale)), Math.Max(1, (int)(frame.Height * scale)), PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(frame, new Rectangle(0, 0, image.Width, image.Height));
            }
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            foreach (long quality in new[] { 85L, 60L })
            {
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                using var stream = new MemoryStream();
                image.Save(stream, codec, parameters);
                if (stream.Length <= MaxBytes)
                {
                    return stream.ToArray();
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
        }
        return null;
    }
}
