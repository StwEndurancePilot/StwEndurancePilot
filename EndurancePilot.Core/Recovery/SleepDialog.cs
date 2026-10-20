namespace EndurancePilot.Core;

internal static class SleepDialog
{
    public static ScreenEvidence? Recognize(Observation frame)
    {
        var headings = TextSimilarity.Find(frame, "Fortnite is in sleep mode", .85);
        var close = TextSimilarity.Find(frame, "Close Fortnite", .9);
        var pairs = (from heading in headings
                     from button in close
                     where button.Bounds.Y > heading.Bounds.Bottom && button.Bounds.Y - heading.Bounds.Bottom < heading.Bounds.Height * 10
                         && button.Bounds.CenterX > heading.Bounds.X && button.Bounds.CenterX < heading.Bounds.Right
                     select (heading, button)).ToArray();
        if (pairs.Length == 0)
        {
            return null;
        }

        var keep = TextSimilarity.Find(frame, "Keep Playing", .9).Where(button => pairs.Any(pair =>
            button.Bounds.Y > pair.heading.Bounds.Bottom && button.Bounds.Bottom < pair.button.Bounds.Y
            && Math.Abs(button.Bounds.CenterX - pair.button.Bounds.CenterX) < Math.Max(button.Bounds.Height, pair.button.Bounds.Height) * 3)).ToArray();
        return new(ScreenKind.SleepMode, ["Fortnite sleep overlay; choose Keep Playing"],
            keep.Select(m => new TextRegion(m.Read, m.Bounds, "sleep-matched")).ToArray());
    }
}
