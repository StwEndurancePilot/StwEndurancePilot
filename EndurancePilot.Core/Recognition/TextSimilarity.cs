namespace EndurancePilot.Core;

public sealed record TextMatch(string Expected, string Read, double Score, Box Bounds);

public static class TextSimilarity
{

    public static double Score(string read, string expected)
    {
        string a = Compact(read), b = Compact(expected);
        if (a == b)
        {
            return a.Length == 0 ? 0 : 1;
        }

        if (a.Length == 0 || b.Length == 0 || a.Length > 96 || b.Length > 96)
        {
            return 0;
        }

        if (new string(a.Where(char.IsDigit).ToArray()) != new string(b.Where(char.IsDigit).ToArray()))
        {
            return 0;
        }

        if (Math.Abs(a.Length - b.Length) > Math.Max(a.Length, b.Length) * .3)
        {
            return 0;
        }

        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }
        return 1 - previous[b.Length] / (double)Math.Max(a.Length, b.Length);
    }

    public static string Compact(string text) => new(TextRegion.Normalize(text).Where(char.IsLetterOrDigit).ToArray());

    public static IReadOnlyList<TextMatch> Find(Observation frame, string expected, double minimum = .78, bool bullet = false)
    {
        var matches = new List<TextMatch>();
        foreach (var region in frame.Regions)
        {
            var read = region.Normalized;
            if (bullet && read.StartsWith("O "))
            {
                read = read[2..];
            }

            double score = Score(read, expected);

            if (score < minimum || Compact(expected).Length < 5 && score < 1)
            {
                continue;
            }

            matches.Add(new(expected, region.Text, score, region.Bounds));
        }
        return matches.OrderByDescending(m => m.Score).ToArray();
    }
}
