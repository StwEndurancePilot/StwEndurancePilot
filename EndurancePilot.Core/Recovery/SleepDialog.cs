namespace EndurancePilot.Core;

internal static class SleepDialog
{
    public static ScreenEvidence? Recognize(Observation frame)
    {
        // The countdown is a different overlay from the fully asleep screen: it
        // has no Close Fortnite button. Corroborate heading and explanation,
        // then target only the labelled keep-playing button below both.
        var warnings = (from heading in TextSimilarity.Find(frame, "Are you still there?", .85)
                        from detail in TextSimilarity.Find(frame, "Fortnite will enter sleep mode soon.", .85)
                        where detail.Bounds.Y > heading.Bounds.Bottom
                            && detail.Bounds.Y - heading.Bounds.Bottom < frame.Height * .12
                            && Math.Abs(detail.Bounds.CenterX - heading.Bounds.CenterX) < frame.Width * .1
                        select (heading, detail)).ToArray();
        if (warnings.Length > 0)
        {
            var buttons = TextSimilarity.Find(frame, "Press any key to keep playing", .85).Where(button => warnings.Any(w =>
                button.Bounds.Y > w.detail.Bounds.Bottom && button.Bounds.Y - w.detail.Bounds.Bottom < frame.Height * .15
                && Math.Abs(button.Bounds.CenterX - w.heading.Bounds.CenterX) < frame.Width * .1));
            return new(ScreenKind.SleepMode, ["Fortnite sleep countdown; choose Keep Playing"],
                buttons.Select(m => new TextRegion(m.Read, m.Bounds, "sleep-countdown-matched")).ToArray());
        }
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
