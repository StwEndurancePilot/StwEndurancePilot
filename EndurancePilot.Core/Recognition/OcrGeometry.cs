namespace EndurancePilot.Core;

public static class OcrGeometry
{

    public static Box Map(Box bounds, int imageWidth, int imageHeight, double angleDegrees, Box crop)
    {
        double radians = angleDegrees * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        var corners = new[] { (bounds.X, bounds.Y), (bounds.Right, bounds.Y),
            (bounds.Right, bounds.Bottom), (bounds.X, bounds.Bottom) };
        var mapped = corners.Select(p =>
        {
            double x = p.Item1 - imageWidth / 2d, y = p.Item2 - imageHeight / 2d;
            return (X: crop.X + (x * cos - y * sin + imageWidth / 2d) * crop.Width / imageWidth,
                Y: crop.Y + (x * sin + y * cos + imageHeight / 2d) * crop.Height / imageHeight);
        }).ToArray();
        double left = mapped.Min(p => p.X), top = mapped.Min(p => p.Y);
        return new(left, top, mapped.Max(p => p.X) - left, mapped.Max(p => p.Y) - top);
    }
}
