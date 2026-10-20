namespace EndurancePilot.Core;

public static class RejoinDialog
{
    public static ScreenEvidence? Recognize(Observation frame)
    {
        var headers = TextSimilarity.Find(frame, "Rejoin", .83);
        var messages = TextSimilarity.Find(frame, "An existing game session was found", .85);
        var pairs = (from header in headers
                     from message in messages
                     where message.Bounds.Y > header.Bounds.Bottom
                         && message.Bounds.Y - header.Bounds.Bottom < header.Bounds.Height * 8
                         && Math.Abs(message.Bounds.X - header.Bounds.X) < header.Bounds.Height * 5
                     select (header, message)).ToArray();
        if (pairs.Length == 0)
        {
            return null;
        }

        var rejoin = TextSimilarity.Find(frame, "Rejoin Game", .9);
        var abandon = TextSimilarity.Find(frame, "Abandon Game", .9).Where(button => pairs.Any(pair =>
            button.Bounds.Y > pair.message.Bounds.Bottom && rejoin.Any(other =>
                other.Bounds.Right < button.Bounds.X
                && Math.Abs(other.Bounds.CenterY - button.Bounds.CenterY) < Math.Max(other.Bounds.Height, button.Bounds.Height)
                && button.Bounds.X - other.Bounds.Right < button.Bounds.Height * 12))).ToArray();
        return new(ScreenKind.RejoinDialog,
            ["Existing-session Rejoin dialog; abandon the disconnected session"],
            abandon.Select(m => new TextRegion(m.Read, m.Bounds, "rejoin-matched")).ToArray());
    }
}
