namespace EndurancePilot.Core;

public static class HomebaseChoiceControls
{
    public static IReadOnlyList<TextRegion> OwnLabels(Observation frame)
    {
        var labels = new List<TextRegion>();

        foreach (var my in frame.Find(@"^MY$"))
        {
            foreach (var storm in frame.Find(@"^STORM$").Where(r => Follows(my.Bounds, r.Bounds)))
            {
                foreach (var shield in frame.Find(@"^SHIELD$").Where(r => Follows(storm.Bounds, r.Bounds)))
                {
                    double top = Math.Min(my.Bounds.Y, Math.Min(storm.Bounds.Y, shield.Bounds.Y));
                    double bottom = Math.Max(my.Bounds.Bottom, Math.Max(storm.Bounds.Bottom, shield.Bounds.Bottom));
                    var bounds = new Box(my.Bounds.X, top, shield.Bounds.Right - my.Bounds.X, bottom - top);
                    if (!labels.Any(r => r.Bounds.IntersectionOverUnion(bounds) > .35))
                    {
                        labels.Add(new("MY STORM SHIELD", bounds, "joined-own-homebase"));
                    }
                }
            }
        }

        foreach (var line in frame.Find(@"^MY STORM SHIELD$"))
        {
            if (!labels.Any(r => r.Bounds.IntersectionOverUnion(line.Bounds) > .35))
            {
                labels.Add(line);
            }
        }

        return labels;
    }

    private static bool Follows(Box left, Box right)
    {
        double h = Math.Max(left.Height, right.Height);
        return h > 0 && right.X >= left.Right - h * .15 && right.X - left.Right < h * 1.5
            && Math.Abs(left.CenterY - right.CenterY) < h * .4
            && Math.Min(left.Height, right.Height) >= h * .5;
    }
}
