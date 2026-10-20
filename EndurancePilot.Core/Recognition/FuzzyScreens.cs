namespace EndurancePilot.Core;

internal static class FuzzyScreens
{
    private sealed record Candidate(ScreenKind Kind, TextMatch Target, TextMatch[] Clues)
    {
        public double Score => Clues.Average(c => c.Score);
    }

    public static ScreenEvidence Recognize(Observation frame, ScreenEvidence fallback)
    {
        var candidates = new List<Candidate>();
        var cache = new Dictionary<string, IReadOnlyList<TextMatch>>();
        IReadOnlyList<TextMatch> Find(string label) => cache.TryGetValue(label, out var found) ? found
            : cache[label] = TextSimilarity.Find(frame, label, bullet: label.StartsWith("Activate"));
        bool Below(TextMatch lower, TextMatch upper, double gap = 6) => lower.Bounds.CenterY > upper.Bounds.CenterY
            && lower.Bounds.Y - upper.Bounds.Bottom < Math.Max(lower.Bounds.Height, upper.Bounds.Height) * gap;
        bool Aligned(TextMatch a, TextMatch b, double widths = 6) => Math.Abs(a.Bounds.X - b.Bounds.X) < Math.Max(a.Bounds.Height, b.Bounds.Height) * widths;
        void Add(ScreenKind kind, TextMatch target, params TextMatch[] clues)
        {

            if (clues.Any(a => clues.Any(b => !ReferenceEquals(a, b) && a.Bounds.IntersectionOverUnion(b.Bounds) > .35)))
            {
                return;
            }

            candidates.Add(new(kind, target, clues));
        }

        foreach (var header in Find("Storm Shield"))
        {
            foreach (var objective in Find("Activate Storm Shield to Defend"))
            {
                string read = TextRegion.Normalize(objective.Read);
                if (read.StartsWith("O "))
                {
                    read = read[2..];
                }

                string verb = read.Split(' ')[0];

                if (TextSimilarity.Score(verb, "Activate") < .85 || !Below(objective, header) || !Aligned(objective, header))
                {
                    continue;
                }

                var words = read.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 4 || TextSimilarity.Score(string.Join(" ", words[1..^2]), "Storm Shield") < .8
                    || TextSimilarity.Score(string.Join(" ", words[^2..]), "to Defend") < .8)
                {
                    continue;
                }

                Add(ScreenKind.Homebase, objective, header, objective);
            }
        }

        var tiles = Find("Storm Shield Endurance").ToList();
        foreach (var top in Find("Storm Shield"))
        {
            foreach (var bottom in Find("Endurance"))
            {
                if (bottom.Bounds.Y >= top.Bounds.Bottom && Below(bottom, top, 2)
                    && Math.Abs(bottom.Bounds.CenterX - top.Bounds.CenterX) < bottom.Bounds.Height * 2
                    && top.Bounds.Height / bottom.Bounds.Height is > .6 and < 1.7)
                {
                    tiles.Add(new("Storm Shield Endurance", top.Read + " / " + bottom.Read,
                        Math.Min(top.Score, bottom.Score), bottom.Bounds));
                }
            }
        }

        foreach (var tile in tiles)
        {
            var peers = new[] { "Daily Wargames", "Custom Wargames", "Storage" }
                .Select(label => Find(label).FirstOrDefault(m => m.Bounds.X > tile.Bounds.Right)).Where(m => m is not null)
                .Cast<TextMatch>().OrderByDescending(m => m.Score).Take(2).ToArray();
            if (peers.Length == 2)
            {
                Add(ScreenKind.StormShieldConsole, tile, tile, peers[0], peers[1]);
            }
        }

        foreach (var tab in Find("Storm Shield"))
        {
            var peers = new[] { "Loadout", "Objectives", "Quests" }.Select(label => Find(label).FirstOrDefault(peer =>
                peer.Bounds.Right < tab.Bounds.X && tab.Bounds.X - peer.Bounds.Right < tab.Bounds.Height * 30
                && Math.Abs(peer.Bounds.CenterY - tab.Bounds.CenterY) < Math.Max(peer.Bounds.Height, tab.Bounds.Height))).ToArray();
            if (peers.All(p => p is not null))
            {
                Add(ScreenKind.Backpack, tab, [tab, .. peers.Cast<TextMatch>()]);
            }
        }

        var homebaseTitles = new[]{"Homebase Storm Shield","Twine Peaks Storm Shield","Canny Valley Storm Shield",
            "Plankerton Storm Shield","Stonewood Storm Shield"}.SelectMany(Find).ToArray();
        foreach (var title in homebaseTitles)
        {
            foreach (var loadout in Find("Hero Loadout"))
            {
                foreach (var launch in Find("Launch"))
                {
                    if (Below(loadout, title, 8) && launch.Bounds.Y > loadout.Bounds.Bottom && launch.Bounds.X > loadout.Bounds.Right)
                    {
                        Add(ScreenKind.HomebaseReady, launch, title, loadout, launch);
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            return fallback;
        }

        var ranked = candidates.GroupBy(c => c.Kind).Select(g => g.OrderByDescending(c => c.Score).First()).OrderByDescending(c => c.Score).ToArray();
        var best = ranked[0];
        if (ranked.All(c => c.Kind is ScreenKind.Backpack or ScreenKind.StormShieldConsole)
            && ranked.FirstOrDefault(c => c.Kind == ScreenKind.StormShieldConsole && c.Score >= .90) is { } console)
        {
            best = console;
        }

        double margin = ranked.Length > 1 ? best.Score - ranked[1].Score : 1;

        if (best.Kind == ScreenKind.StormShieldConsole && ranked.All(c => c.Kind is ScreenKind.StormShieldConsole or ScreenKind.Backpack))
        {
            margin = 1;
        }

        double targetMinimum = TextSimilarity.Compact(best.Target.Expected).Length < 10 ? .8 : .85;
        var possibleTargets = candidates.Where(c => c.Kind == best.Kind && c.Score >= best.Score - .06
            && c.Target.Score >= targetMinimum).Select(c => new TextRegion(c.Target.Read, c.Target.Bounds, "fuzzy")).ToArray();
        bool unambiguous = AutomationController.UniqueTarget(possibleTargets) is not null;
        bool actionable = best.Score >= .90 && best.Clues.All(c => c.Score >= .8) && best.Target.Score >= targetMinimum
            && margin >= .08 && unambiguous;
        string detail = $"{(actionable ? "Matched" : "Probably")} {best.Kind}: {best.Score:P0} text similarity"
            + (actionable ? "; corroborated labels and layout" : "; waiting for clearer, unambiguous evidence");
        return new(actionable ? best.Kind : ScreenKind.Unknown, [detail], actionable ? [new(best.Target.Read, best.Target.Bounds, "fuzzy")] : [])
        {
            MatchScore = best.Score,
            LikelyKind = best.Kind,
            UsesFuzzyMatching = true,
            AllowsInput = actionable,
            TextMatches = best.Clues
        };
    }
}
