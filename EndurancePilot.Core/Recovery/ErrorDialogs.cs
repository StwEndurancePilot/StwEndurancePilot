using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

internal static class ErrorDialogs
{
    public static ScreenEvidence? Recognize(Observation frame)
    {
        var headings = new[] { "There was an error", "There was a problem", "That wasn't supposed to happen" }
            .SelectMany(label => TextSimilarity.Find(frame, label, .8)).ToArray();
        var candidates = new List<(TextMatch Header, TextRegion Body)>();
        foreach (var header in headings)
        {
            foreach (var body in frame.Regions.Where(r => !r.Pass.EndsWith("-word") && r.Normalized.Split(' ').Length >= 4))
            {
                if (body.Bounds.Y >= header.Bounds.Bottom && body.Bounds.Y - header.Bounds.Bottom < header.Bounds.Height * 6
                    && body.Bounds.X < header.Bounds.Right && body.Bounds.Right > header.Bounds.X)
                {
                    candidates.Add((header, body));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var buttons = TextSimilarity.Find(frame, "Continue", .875).Where(button => candidates.Any(pair =>
            button.Bounds.Y > pair.Body.Bounds.Bottom && button.Bounds.Y - pair.Body.Bounds.Bottom < pair.Header.Bounds.Height * 10
            && button.Bounds.CenterX >= pair.Body.Bounds.X && button.Bounds.CenterX <= pair.Body.Bounds.Right)).ToArray();

        if (buttons.Length == 0 && frame.Has(@"^(OK|RETRY)$"))
        {
            return null;
        }

        bool recoverable = candidates.Any(pair => Regex.IsMatch(pair.Body.Normalized,
            @"\b(KICKED FOR IDLE|CONNECTION TO THE HOST WAS LOST|CONNECTION LOST|NETWORK CONNECTION LOST|CONNECTION TIMED OUT|FAILED TO CONNECT)\b"));
        var best = candidates.OrderByDescending(c => c.Header.Score).First();
        return new(recoverable ? ScreenKind.ErrorDialog : ScreenKind.NeedsAttention,
            [recoverable ? "Disconnect error with corroborated heading, message and Continue control" : "Error dialog; dismiss Continue and pause for attention"],
            buttons.Select(b => new TextRegion(b.Read, b.Bounds, "error-matched")).ToArray())
        {
            MatchScore = best.Header.Score,
            TextMatches = [best.Header, .. buttons]
        };
    }
}
