namespace EndurancePilot.Core;

public static class StormShieldNavigation
{
    private const string Shield = @"STORM\s*S\s*(?:[HN]?I|III|IU)ELD";
    private const string Objective = @"ACTIVATE\s*" + Shield + @"\s*TO\s*DEFEND";

    public static Box? InactiveObjective(Observation frame)
    {
        var headers = frame.Find("^" + Shield + "$").ToArray();
        var objectives = frame.Regions.Where(IsActivationLine).Where(line => headers.Any(header =>
            line.Bounds.CenterY > header.Bounds.CenterY
            && line.Bounds.Y - header.Bounds.Bottom < Math.Max(line.Bounds.Height, header.Bounds.Height) * 6
            && Math.Abs(line.Bounds.X - header.Bounds.X) < Math.Max(line.Bounds.Height, header.Bounds.Height) * 6)).ToList();

        objectives.AddRange(frame.Find("^" + Shield + @"\s+(?:O\s+)?" + Objective + "$"));

        foreach (var start in frame.Find(@"^(?:O\s+)?ACTIVATE\s*" + Shield + "$"))
        {
            foreach (var end in frame.Find(@"^TO\s*DEFEND$"))
            {
                double h = Math.Max(start.Bounds.Height, end.Bounds.Height);
                if (end.Bounds.Y < start.Bounds.Bottom || end.Bounds.Y - start.Bounds.Bottom > h * 2
                    || Math.Abs(end.Bounds.X - start.Bounds.X) > h * 3)
                {
                    continue;
                }

                if (!headers.Any(header => start.Bounds.CenterY > header.Bounds.CenterY
                    && start.Bounds.Y - header.Bounds.Bottom < h * 6 && Math.Abs(start.Bounds.X - header.Bounds.X) < h * 6))
                {
                    continue;
                }

                objectives.Add(start);
            }
        }

        return AutomationController.UniqueTarget(objectives.ToArray());
    }

    private static bool IsActivationLine(TextRegion region)
    {
        var match = System.Text.RegularExpressions.Regex.Match(region.Normalized,
            @"^(?:O\s+)?ACTIVATE\s*(?<shield>.+?)\s*TO\s*DEFEND$");
        if (!match.Success)
        {
            return false;
        }

        string read = match.Groups["shield"].Value.Replace(" ", "");
        const string expected = "STORMSHIELD";

        if (!read.StartsWith("STO") || Math.Abs(read.Length - expected.Length) > 2)
        {
            return false;
        }

        var previous = Enumerable.Range(0, expected.Length + 1).ToArray();
        for (int i = 1; i <= read.Length; i++)
        {
            var current = new int[expected.Length + 1];
            current[0] = i;
            for (int j = 1; j <= expected.Length; j++)
            {
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (read[i - 1] == expected[j - 1] ? 0 : 1));
            }

            previous = current;
        }
        return previous[expected.Length] <= 2;
    }

    public static bool IsConsole(Observation frame) => EnduranceTile(frame) is not null
        && new[] { @"^DAIL[YV]\s*WARGAMES$", @"^CUSTOM\s*WARGAMES$", @"^STORAGE$" }.Count(frame.Has) >= 2;

    public static Box? BackpackTab(Observation frame)
    {

        var tabs = frame.Find(@"^STORM SHIELD$").Where(tab =>
            new[] { "LOADOUT", "OBJECTIVES", "QUESTS" }.All(label => frame.Find("^" + label + "$").Any(peer =>
                peer.Bounds.Right < tab.Bounds.X
                && Math.Abs(peer.Bounds.CenterY - tab.Bounds.CenterY) < Math.Max(peer.Bounds.Height, tab.Bounds.Height)
                && tab.Bounds.X - peer.Bounds.Right < tab.Bounds.Height * 30)));
        return AutomationController.UniqueTarget(tabs.ToArray());
    }

    public static Box? EnduranceTile(Observation frame)
    {
        var full = frame.Find("^" + Shield + @"\s*ENDURANCE$").ToArray();
        if (full.Length > 0)
        {
            return AutomationController.UniqueTarget(full);
        }

        var shield = frame.Find("^" + Shield + "$|^STORM S[HN]ELD$").ToArray();
        return AutomationController.UniqueTarget(frame.Find(@"^ENDURANCE$").Where(endurance => shield.Any(top =>
            top.Bounds.Bottom <= endurance.Bounds.Y && endurance.Bounds.Y - top.Bounds.Bottom < endurance.Bounds.Height * 2
            && Math.Abs(top.Bounds.CenterX - endurance.Bounds.CenterX) < endurance.Bounds.Height * 2
            && top.Bounds.Height / endurance.Bounds.Height is > .6 and < 1.7)).ToArray());
    }
}
