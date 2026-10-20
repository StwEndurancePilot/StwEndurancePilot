namespace EndurancePilot.Core;

public static class DiscoveryNavigation
{
    public const string Query = "Save The World";
    public static bool HasLobbyNavigation(Observation frame)
    {

        var top = frame.Regions.Where(r => r.Bounds.CenterY < frame.Height * .2).ToArray();
        foreach (var play in top.Where(r => r.Normalized is "PLAY" or "PLAV"))
        {
            bool SameRow(TextRegion r) => Math.Abs(r.Bounds.CenterY - play.Bounds.CenterY) < Math.Max(r.Bounds.Height, play.Bounds.Height) * 1.5;
            foreach (var shop in top.Where(r => r.Normalized == "SHOP" && r.Bounds.CenterX > play.Bounds.CenterX + play.Bounds.Height * 2 && SameRow(r)))
            {
                foreach (var locker in top.Where(r => r.Normalized == "LOCKER" && r.Bounds.CenterX > shop.Bounds.CenterX + shop.Bounds.Height * 2 && SameRow(r)))
                {
                    if (top.Any(r => r.Normalized is "PASSES" or "QUESTS" or "COMPETE" or "CAREER"
                        && r.Bounds.CenterX > locker.Bounds.CenterX + locker.Bounds.Height * 2 && SameRow(r)))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    public static bool IsOutOfMission(ScreenKind kind) => kind is ScreenKind.FortniteLobby or ScreenKind.DiscoverBrowser
        or ScreenKind.DiscoverResults or ScreenKind.SaveTheWorldDetails;
    public static bool IsBrowser(ScreenKind kind) => kind is ScreenKind.DiscoverBrowser or ScreenKind.DiscoverResults;
    public static Box? ByEpic(Observation frame) => AutomationController.UniqueTarget(frame.Find("^B[YV] EPIC$")
        .Where(r => r.Bounds.CenterY < frame.Height * .35).ToArray());
    public static Box? SearchField(Observation frame)
    {
        if (ByEpic(frame) is not { } epic)
        {
            return null;
        }

        var discover = AutomationController.UniqueTarget(frame.Find("^DISCOVER$").Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal)
            && Math.Abs(r.Bounds.CenterY - epic.CenterY) < epic.Height * 2).ToArray());
        if (discover is null)
        {
            return null;
        }

        return AutomationController.UniqueTarget(frame.Regions.Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal)
            && r.Bounds.Right < discover.Value.X && r.Bounds.Width > epic.Height * 3
            && Math.Abs(r.Bounds.CenterY - epic.CenterY) < epic.Height * 1.2).ToArray());
    }
    public static bool HasQuery(Observation frame) => SearchField(frame) is { } field && frame.Regions.Any(r =>
        r.Bounds.IntersectionOverUnion(field) > .5 && TextSimilarity.Score(
            System.Text.RegularExpressions.Regex.Replace(r.Normalized, "^[QO] ", ""), Query) >= .94);
    public static ScreenEvidence? Recognize(Observation frame)
    {
        if (frame.Has("^ABOUT ISLAND$") && frame.Has("^BY EPIC$") && frame.Has("^CREATED BY EPIC$")
            && NavigationPlanner.SaveTheWorldTitles(frame).Any(r => r.Bounds.Y < frame.Height * .2) && frame.Has("^SELECT$"))
        {
            return new(ScreenKind.SaveTheWorldDetails, ["Save the World details, Epic creator and Select control"], frame.Find("^SELECT$").ToArray());
        }

        if (ByEpic(frame) is not null && frame.Has("^FOLLOWING$") && frame.Has("^CATEGORIES$") && SearchField(frame) is not null)
        {
            return new(frame.Has("^ISLANDS RESULTS$") ? ScreenKind.DiscoverResults : ScreenKind.DiscoverBrowser,
                ["Discover navigation and search field"], []);
        }

        if ((frame.Has("^FOLLOWING$") ? 1 : 0) + (frame.Has("^CATEGORIES$") ? 1 : 0) + (ByEpic(frame) is not null ? 1 : 0) >= 2)
        {
            return ScreenEvidence.Unknown("Discover navigation visible; waiting to read the search field and By Epic control");
        }

        return null;
    }
    public static NavigationDecision Decide(Observation frame, ScreenEvidence screen)
    {
        NavigationDecision Plan(NavigationStep step, string description, Box? box, string? context = null) => box is { } target
            ? new(new(step, description, target, context), description) : new(null, "Waiting for a verified control: " + description);
        if (screen.Kind == ScreenKind.SaveTheWorldDetails)
        {
            return Plan(NavigationStep.ConfirmSaveTheWorld, "Select Epic's Save the World", AutomationController.UniqueTarget(screen.Targets));
        }

        if (IsBrowser(screen.Kind))
        {
            if (NavigationPlanner.ReliableVisual(frame, "by-epic-selected") is null)
            {
                return Plan(NavigationStep.FilterByEpic, "Choose By Epic", ByEpic(frame));
            }

            if (!HasQuery(frame))
            {
                return Plan(NavigationStep.SearchSaveTheWorld, "Search for Save The World", SearchField(frame), Query);
            }

            return Plan(NavigationStep.SelectSaveTheWorld, "Open the official Save the World result", NavigationPlanner.ReliableVisual(frame, "stw-search-tile"));
        }
        if (screen.Kind is ScreenKind.FortniteLobby or ScreenKind.ModeSelection && NavigationPlanner.ReliableVisual(frame, "stw-discovery-tile") is { } tile)
        {
            return Plan(NavigationStep.SelectSaveTheWorld, "Select the visible Save the World tile", tile);
        }

        return Plan(NavigationStep.OpenDiscover, "Open Discover using the bottom chevron", NavigationPlanner.ReliableVisual(frame, "discover-chevron"));
    }
    public static bool IsSearchAction(ClickAction action) => action.Step == NavigationStep.SearchSaveTheWorld
        && IsBrowser(action.SourceScreen) && action.Context == Query && action.Key is null;
}
