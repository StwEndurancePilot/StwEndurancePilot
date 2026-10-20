using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class RewardThumbnails
{
    private Guid receipt;
    internal Dictionary<string, byte[]> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal void Observe(Guid id, Observation observation, Bitmap frame)
    {
        if (receipt != id)
        {
            receipt = id;
            Images.Clear();
        }
        var reward = MissionRewardReader.ReadCard(observation);
        if (reward is null || Images.ContainsKey(reward.Name) || Images.Count >= 64)
        {
            return;
        }

        var box = observation.FindVisual("reward-card").Single().Bounds;

        var crop = Rectangle.Intersect(new(0, 0, frame.Width, frame.Height),
            Rectangle.Round(new RectangleF((float)(box.X + box.Width * .1), (float)(box.Y + box.Height * .12),
                (float)(box.Width * .8), (float)(box.Height * .65))));
        if (crop.Width < 8 || crop.Height < 8)
        {
            return;
        }

        using var thumbnail = new Bitmap(128, 104);
        using (var g = Graphics.FromImage(thumbnail))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(frame, new Rectangle(0, 0, 128, 104), crop, GraphicsUnit.Pixel);
        }
        using var stream = new MemoryStream();
        thumbnail.Save(stream, ImageFormat.Png);
        Images[reward.Name] = stream.ToArray();
    }
}

internal static class RewardReceiptArtwork
{
    internal const int PageSize = 10;
    internal static byte[] Render(IReadOnlyList<MissionReward> items, IReadOnlyDictionary<string, byte[]> images,
        IReadOnlyList<MissionReward>? sessionTotals = null, IReadOnlyList<MissionReward>? lifetimeTotals = null)
    {
        if (items.Count is < 1 or > PageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(items));
        }

        const int width = 800, rowHeight = 68, top = 76;
        using var bitmap = new Bitmap(width, top + items.Count * rowHeight + 20);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(19, 23, 29));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var white = new SolidBrush(Color.FromArgb(242, 245, 249));
        using var muted = new SolidBrush(Color.FromArgb(160, 173, 190));
        using var title = new Font("Segoe UI", 32, FontStyle.Bold, GraphicsUnit.Pixel);
        using var nameFont = new Font("Segoe UI", 26, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fallback = new Font("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var line = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
        using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString("Mission Rewards", title, white, 28, 22);
        for (int i = 0; i < items.Count; i++)
        {
            int y = top + i * rowHeight;
            var thumb = new Rectangle(32, y + 9, 48, 48);
            var bytes = RewardAssets.Read(items[i].Name);
            if (bytes is null)
            {
                images.TryGetValue(items[i].Name, out bytes);
            }

            if (bytes is not null)
            {
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                float scale = Math.Min((float)thumb.Width / image.Width, (float)thumb.Height / image.Height);
                var iconSize = new SizeF(image.Width * scale, image.Height * scale);
                g.DrawImage(image, new RectangleF(thumb.X + (thumb.Width - iconSize.Width) / 2,
                    thumb.Y + (thumb.Height - iconSize.Height) / 2, iconSize.Width, iconSize.Height));
            }
            else
            {

                using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var words = items[i].Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string initials = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
                g.DrawString(initials, fallback, muted, thumb, center);
            }
            g.DrawString(items[i].Name, nameFont, white, new RectangleF(98, y, 418, rowHeight), line);
            var session = sessionTotals is null ? items[i] : sessionTotals.FirstOrDefault(r =>
                string.Equals(r.Name, items[i].Name, StringComparison.OrdinalIgnoreCase));
            var lifetime = lifetimeTotals?.FirstOrDefault(r =>
                string.Equals(r.Name, items[i].Name, StringComparison.OrdinalIgnoreCase));
            string quantity = session?.Quantity is { } count ? $"× {count:N0}" : "Unavailable";
            float size = 28;
            while (size > 12)
            {
                using var measure = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                if (g.MeasureString(quantity, measure).Width <= 218)
                {
                    break;
                }

                size -= 1;
            }
            using var quantityFont = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(quantity, quantityFont, session?.Quantity.HasValue == true ? white : muted,
                new RectangleF(540, y, 228, lifetimeTotals is null ? rowHeight : 40), right);
            if (lifetimeTotals is not null)
            {
                using var totalFont = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel);
                string label = lifetime?.Quantity is { } earned ? $"Lifetime × {earned:N0}" : "Lifetime unavailable";
                g.DrawString(label, totalFont, muted, new RectangleF(510, y + 38, 258, 24), right);
            }
        }
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
