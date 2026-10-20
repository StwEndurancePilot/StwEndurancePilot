namespace EndurancePilot.Core;

public enum HomebaseZone
{
    Unspecified, Stonewood, Plankerton, CannyValley, TwinePeaks
}
public enum NavigationStep
{
    EnterSaveTheWorld, OpenMap, ChangeZone, EnterZone, SelectHomebase, OpenHomebase,
    SelectOwnHomebase, LaunchHomebase, LaunchLoadedHomebase, StartEndurance, ClaimReward, SelectEndurance, DisableAssistance,
    SelectSaveTheWorld, OpenQuestMenu, OpenStormShieldConsole, ContinueResults, CollectMissionRewards, OpenBackpack, SelectStormShieldTab, AbandonRejoin, KeepPlaying, ReturnToHomebase,
    OpenTargetExitBackpack, BackToStormShield, LeaveTargetMission, ConfirmTargetLeave,
    OpenDiscover, FilterByEpic, SearchSaveTheWorld, ConfirmSaveTheWorld, RetryQuestMenu, SkipFeedback, ContinueUnlock
}
public enum NavigationKey
{
    E, Tab, Escape, C
}
public sealed record RunnerProfile(HomebaseZone Zone = HomebaseZone.Unspecified, bool AfkBaseConfirmed = false, int TargetWave = 30,
    bool EndRunWhenTargetReached = true);
public sealed record NavigationPlan(NavigationStep Step, string Description, Box Target, string? Context = null, NavigationKey? Key = null);
public sealed record NavigationDecision(NavigationPlan? Plan, string Detail, bool RequiresAttention = false);

public static class NavigationPlanner
{

    public const string MissionLaunchLabel = @"^LAUNC(?:H|N|II)$";
    public static NavigationDecision Decide(Observation frame, ScreenEvidence screen, RunnerProfile profile, bool homebaseClickConfirmed = false)
    {
        if (!screen.AllowsInput)
        {
            return new(null, string.Join("; ", screen.Reasons));
        }

        NavigationDecision Text(NavigationStep step, string description, IEnumerable<TextRegion> regions, string? context = null, NavigationKey? key = null)
        {
            var target = AutomationController.UniqueTarget(regions.ToArray());
            return target is { } box ? new(new(step, description, box, context, key), description)
                : new(null, "Waiting for one unambiguous target: " + description);
        }
        NavigationDecision Visual(NavigationStep step, string description, string id, string? context = null)
        {
            var target = ReliableVisual(frame, id);
            return target is { } box ? new(new(step, description, box, context), description)
                : new(null, "Waiting for a distinctive visual target: " + description);
        }
        NavigationDecision Control(NavigationStep step, string description, string label, string visualId)
        {
            var text = Text(step, description, frame.Find(label));
            return text.Plan is not null ? text : Visual(step, description, visualId);
        }
        if (screen.Kind == ScreenKind.RejoinDialog)
        {
            return Text(NavigationStep.AbandonRejoin, "Abandon the existing game session", screen.Targets);
        }

        if (screen.Kind == ScreenKind.SleepMode)
        {
            return Text(NavigationStep.KeepPlaying, "Exit sleep mode with Keep Playing", screen.Targets);
        }

        if (screen.Kind == ScreenKind.FeedbackSurvey)
        {
            return Text(NavigationStep.SkipFeedback, "Skip optional feedback", screen.Targets);
        }

        if (screen.Kind == ScreenKind.NewUnlock)
        {
            return Text(NavigationStep.ContinueUnlock, "Continue past the new unlock", screen.Targets);
        }

        if (screen.Kind == ScreenKind.RewardClaim)
        {
            var claim = RewardClaimTarget(frame);
            return claim is { } box ? new(new(NavigationStep.ClaimReward, "Claim Save the World Season XP", box), "Claim earned reward")
                : new(null, "Waiting for one unambiguous Claim control");
        }
        if (screen.Kind is ScreenKind.Victory or ScreenKind.Defeat)
        {
            var home = frame.Find(@"^(?:C )?MATCHMAKE INTO HOMEBASE$").ToArray();
            if (home.Length > 0)
            {
                return Text(NavigationStep.ReturnToHomebase, "Press C to matchmake into Homebase", home, key: NavigationKey.C);
            }

            return Text(NavigationStep.ContinueResults, "Continue from the mission result", frame.Find(@"^CONTINUE$"));
        }
        if (screen.Kind == ScreenKind.Rewards)
        {
            foreach (var label in new[] { "OPEN", "COLLECT ALL", "CLAIM", "COLLECT", "CONTINUE" })
            {
                if (MissionRewardControls(frame).Any(region => region.Normalized == label))
                {
                    var decision = Text(NavigationStep.CollectMissionRewards,
                        label == "OPEN" ? "Open mission rewards" : "Collect earned mission rewards",
                        MissionRewardControls(frame).Where(region => region.Normalized == label));
                    return decision.Plan is { } plan
                        ? decision with
                        {
                            Plan = plan with
                            {
                                Context = MissionRewardReader.PageContext(frame, screen)
                            }
                        }
                        : decision;
                }
            }
            return new(null, "Waiting for an unambiguous reward collection control");
        }
        if (profile.Zone == HomebaseZone.Unspecified)
        {
            return new(null, "Choose the intended homebase zone before navigation", true);
        }

        if (screen.UsesFuzzyMatching)
        {
            var target = AutomationController.UniqueTarget(screen.Targets);
            if (target is null)
            {
                return new(null, "Waiting for one unambiguous matched control");
            }

            var plan = screen.Kind switch
            {
                ScreenKind.Homebase => new NavigationPlan(NavigationStep.OpenBackpack, "Open backpack for Storm Shield controls", target.Value, Key: NavigationKey.Tab),
                ScreenKind.Backpack => new NavigationPlan(NavigationStep.SelectStormShieldTab, "Open the Storm Shield tab", target.Value),
                ScreenKind.StormShieldConsole => new NavigationPlan(NavigationStep.SelectEndurance, "Select Storm Shield Endurance", target.Value),
                ScreenKind.HomebaseReady => new NavigationPlan(NavigationStep.LaunchLoadedHomebase, "Launch the loaded homebase", target.Value),
                _ => null
            };
            return new(plan, plan?.Description ?? "Waiting for a supported matched control");
        }
        switch (screen.Kind)
        {
            case ScreenKind.FortniteLobby:
            case ScreenKind.DiscoverBrowser:
            case ScreenKind.DiscoverResults:
            case ScreenKind.SaveTheWorldDetails:
                return DiscoveryNavigation.Decide(frame, screen);
            case ScreenKind.ModeDiscovery:
                var tile = Text(NavigationStep.SelectSaveTheWorld, "Select the Save the World tile", DiscoveryTileLabels(frame));
                if (tile.Plan is not null)
                {
                    return tile;
                }

                var visualTile = Visual(NavigationStep.SelectSaveTheWorld, "Select the Save the World tile", "stw-discovery-tile");
                return visualTile.Plan is not null ? visualTile : DiscoveryNavigation.Decide(frame, screen);
            case ScreenKind.SaveTheWorldHub:
                return InteractionPrompts.QuestMenuAnchor(frame) is { } questPrompt
                    ? new(new(NavigationStep.OpenQuestMenu, "Open the quest menu", questPrompt, Key: NavigationKey.Tab), "Open the quest menu")
                    : new(null, "Waiting for the quest-log prompt");
            case ScreenKind.Homebase:
                return StormShieldNavigation.InactiveObjective(frame) is { } objective
                    ? new(new(NavigationStep.OpenBackpack, "Open backpack for Storm Shield controls", objective, Key: NavigationKey.Tab), "Open backpack")
                    : new(null, "Waiting for the inactive Storm Shield objective");
            case ScreenKind.Backpack:
                return StormShieldNavigation.BackpackTab(frame) is { } shieldTab
                    ? new(new(NavigationStep.SelectStormShieldTab, "Open the Storm Shield tab", shieldTab), "Open Storm Shield tab")
                    : new(null, "Waiting for the Storm Shield backpack tab");
            case ScreenKind.ModeSelection:

                var entry = Text(NavigationStep.EnterSaveTheWorld, "Enter Save the World", SaveTheWorldPlayControls(frame));
                return entry.Plan is not null ? entry : DiscoveryNavigation.Decide(frame, screen);
            case ScreenKind.SaveTheWorldLobby:
                return Text(NavigationStep.OpenMap, "Open the map", frame.Find(@"^MAP$"));
            case ScreenKind.WorldMap:
                var zone = SelectedZone(frame);
                if (zone is null)
                {
                    return new(null, "Waiting to identify the selected world zone");
                }

                if (zone != profile.Zone)
                {
                    var next = NextZoneToward(zone.Value, profile.Zone);
                    if (next is null)
                    {
                        return new(null, "No verified route between these world zones", true);
                    }

                    bool forward = Array.IndexOf(ZoneOrder, next.Value) > Array.IndexOf(ZoneOrder, zone.Value);
                    return Visual(NavigationStep.ChangeZone, "Select " + ZoneName(next.Value),
                        forward ? "next-zone" : "previous-zone", next.ToString());
                }
                if (frame.Has(@"\bREQUIRES COMPLETING QUEST\b"))
                {
                    return new(null, "The selected zone is locked: " + string.Join("; ", frame.Find(@"\bREQUIRES COMPLETING QUEST\b").Select(r => r.Text).Distinct()), true);
                }

                return Control(NavigationStep.EnterZone, "Open " + ZoneName(profile.Zone), @"^SELECT$", "select-control");
            case ScreenKind.MissionMap:
                if (!HasSelectedHomebase(frame))
                {
                    if (homebaseClickConfirmed)
                    {
                        return new(null, "Waiting to re-read the confirmed homebase heading");
                    }

                    return Visual(NavigationStep.SelectHomebase, "Select the homebase icon", "homebase-icon");
                }
                if (!frame.Has("^" + ZoneName(profile.Zone).ToUpperInvariant() + "$")
                    && !HomebaseHeadings(frame).Any(r => r.Normalized.StartsWith(ZoneName(profile.Zone).ToUpperInvariant() + " STORM ", StringComparison.Ordinal)))
                {
                    return new(null, "The selected homebase does not match the configured zone", true);
                }

                if (ReadShieldLevel(frame) is int level && level < 10)
                {
                    return new(null, $"Selected homebase shows shield level {level}/10. Endurance availability must be resolved.", true);
                }

                if (!homebaseClickConfirmed)
                {
                    return Visual(NavigationStep.SelectHomebase, "Click the homebase icon", "homebase-icon");
                }

                return Control(NavigationStep.OpenHomebase, "Open the selected homebase", @"^SELECT$", "select-control");
            case ScreenKind.HomebaseChoice:
                if (ReliableVisual(frame, "own-homebase-selected") is null)
                {
                    return Text(NavigationStep.SelectOwnHomebase, "Select My Storm Shield", HomebaseChoiceControls.OwnLabels(frame));
                }

                return Control(NavigationStep.LaunchHomebase, "Launch My Storm Shield", MissionLaunchLabel, "launch-control");
            case ScreenKind.HomebaseReady:
                return Text(NavigationStep.LaunchLoadedHomebase, "Launch the loaded homebase", frame.Find(MissionLaunchLabel));
            case ScreenKind.StormShieldConsole:
                return StormShieldNavigation.EnduranceTile(frame) is { } enduranceTile
                    ? new(new(NavigationStep.SelectEndurance, "Select Storm Shield Endurance", enduranceTile), "Select endurance")
                    : new(null, "Waiting for an unambiguous endurance tile");
            case ScreenKind.EnduranceSetup:
                if (!profile.AfkBaseConfirmed)
                {
                    return new(null, "In-run behavior and base readiness have not been confirmed", true);
                }

                var assistanceOn = ReliableVisual(frame, "assistance-checked");
                var assistanceOff = ReliableVisual(frame, "assistance-unchecked");
                if (assistanceOn is not null && assistanceOff is null)
                {
                    return Visual(NavigationStep.DisableAssistance, "Turn off Wait for Assistance", "assistance-checked");
                }

                if (assistanceOff is null || assistanceOn is not null)
                {
                    return new(null, "Waiting to verify Wait for Assistance is unchecked");
                }

                return Text(NavigationStep.StartEndurance, "Start endurance", frame.Find(@"^START ENDURANCE$"));
            default:
                return new(null, "Observing " + screen.Kind);
        }
    }

    public static bool HasSelectedHomebase(Observation frame) => HomebaseHeadings(frame).Any();

    public static IEnumerable<TextRegion> HomebaseHeadings(Observation frame)
    {
        foreach (var region in frame.Regions)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(region.Normalized, @"\b([HN]OMEBASE|TWINE PEAKS|CANNY VALLEY|PLANKERTON|STONEWOOD) STORM S[HN]IELD\b"))
            {
                yield return region;
                continue;
            }

            foreach (var prefix in new[] { "HOMEBASE", "TWINE PEAKS", "CANNY VALLEY", "PLANKERTON", "STONEWOOD" })
            {
                string start = prefix + " STORM ";
                if (!region.Normalized.StartsWith(start, StringComparison.Ordinal))
                {
                    continue;
                }

                string shield = region.Normalized[start.Length..];
                if (shield.Length is >= 5 and <= 8 && shield.StartsWith('S') && shield.EndsWith("LD", StringComparison.Ordinal)
                    && shield.All(char.IsLetter) && TextSimilarity.Score(region.Normalized, start + "SHIELD") >= .88)
                {
                    yield return region;
                }

                break;
            }
        }
    }

    public static Box? PostClickPointerDestination(Observation frame, ClickAction action)
    {
        if (action.Step == NavigationStep.DisableAssistance && action.SourceScreen == ScreenKind.EnduranceSetup
            && action.FrameWidth == frame.Width && action.FrameHeight == frame.Height)
        {

            var heading = AutomationController.UniqueTarget(frame.Find(@"^ENDURANCE$")
                            .Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal)).ToArray());
            return heading is { } parking && AutomationController.IsValidTarget(parking, frame.Width, frame.Height)
                && parking.IntersectionOverUnion(action.Target) == 0 ? parking : null;
        }
        if (action.Step == NavigationStep.ChangeZone && action.SourceScreen == ScreenKind.WorldMap
            && action.FrameWidth == frame.Width && action.FrameHeight == frame.Height)
        {
            var map = AutomationController.UniqueTarget(frame.Find(@"^MAP$").ToArray())
                ?? AutomationController.UniqueTarget(frame.Find(@"^SELECT$").ToArray())
                ?? ReliableVisual(frame, "select-control");
            return map is { } parking && AutomationController.IsValidTarget(parking, frame.Width, frame.Height)
                && parking.IntersectionOverUnion(action.Target) == 0 ? parking : null;
        }
        if (action.Step != NavigationStep.SelectHomebase || action.SourceScreen != ScreenKind.MissionMap
            || action.FrameWidth != frame.Width || action.FrameHeight != frame.Height)
        {
            return null;
        }

        var labels = frame.Find(@"^SELECT$").ToArray();
        var target = labels.Length > 0 ? AutomationController.UniqueTarget(labels) : ReliableVisual(frame, "select-control");

        if (labels.Length == 0 && target is null)
        {
            target = AutomationController.UniqueTarget(frame.Find(@"^MAP$").ToArray());
        }

        return target is { } box && AutomationController.IsValidTarget(box, frame.Width, frame.Height)
            && box.IntersectionOverUnion(action.Target) == 0 ? box : null;
    }

    private static readonly HomebaseZone[] ZoneOrder = [HomebaseZone.Stonewood, HomebaseZone.Plankerton, HomebaseZone.CannyValley, HomebaseZone.TwinePeaks];
    public static Box? PointerRestingPoint(Box anchor, int width, int height)
    {

        double gap = Math.Max(12, anchor.Height * 2);
        foreach (var point in new[] {new Box(anchor.Right+gap,anchor.CenterY,1,1),
            new Box(anchor.X-gap,anchor.CenterY,1,1),new Box(anchor.CenterX,anchor.Y-gap,1,1)})
        {
            if (point.X >= 1 && point.Y >= 1 && point.Right < width - 1 && point.Bottom < height - 1)
            {
                return point;
            }
        }

        return null;
    }
    public static HomebaseZone? NextZoneToward(HomebaseZone current, HomebaseZone destination)
    {
        int from = Array.IndexOf(ZoneOrder, current), to = Array.IndexOf(ZoneOrder, destination);
        return from < 0 || to < 0 || from == to ? null : ZoneOrder[from + Math.Sign(to - from)];
    }

    public static IEnumerable<TextRegion> SaveTheWorldTitles(Observation frame)
    {
        foreach (var title in frame.Find(@"^SA\s*VE THE WORLD$"))
        {
            yield return title;
        }

        foreach (var top in frame.Find(@"^SA\s*VE THE$"))
        {
            foreach (var bottom in frame.Find(@"^WORLD$"))
            {
                var a = top.Bounds;
                var b = bottom.Bounds;
                if (b.Y < a.Bottom || b.Y - a.Bottom > a.Height * 1.5
                    || b.Height < a.Height * .65 || b.Height > a.Height * 1.5
                    || Math.Abs(a.CenterX - b.CenterX) > a.Height)
                {
                    continue;
                }

                double x = Math.Min(a.X, b.X), right = Math.Max(a.Right, b.Right);
                yield return new("SAVE THE WORLD", new(x, a.Y, right - x, b.Bottom - a.Y), "joined-title");
            }
        }
    }

    public static IEnumerable<TextRegion> SaveTheWorldPlayControls(Observation frame)
    {
        var titles = SaveTheWorldTitles(frame).ToArray();
        return frame.Find(@"^PLA[YV]$").Where(play => titles.Any(title =>
            play.Bounds.Y > title.Bounds.Bottom
            && play.Bounds.CenterX >= title.Bounds.X && play.Bounds.CenterX <= title.Bounds.Right
            && play.Bounds.Y - title.Bounds.Bottom < title.Bounds.Height * 12));
    }

    private static IEnumerable<TextRegion> DiscoveryTileLabels(Observation frame)
    {
        var lobby = frame.Find(@"^CURRENT LOBBY$").ToArray();
        var save = frame.Find(@"^SAVE$").ToArray();
        var complete = SaveTheWorldTitles(frame).Where(tile => lobby.Any(title => tile.Bounds.Y > title.Bounds.Bottom
            && tile.Bounds.Y - title.Bounds.Bottom < title.Bounds.Height * 8
            && tile.Bounds.CenterX >= title.Bounds.X && tile.Bounds.CenterX <= title.Bounds.X + title.Bounds.Height * 13));
        return complete.Concat(frame.Find(@"^WORLD$").Where(world => lobby.Any(title => world.Bounds.Y > title.Bounds.Bottom
            && world.Bounds.Y - title.Bounds.Bottom < title.Bounds.Height * 8)
            && save.Any(top => top.Bounds.Bottom <= world.Bounds.CenterY && world.Bounds.Y - top.Bounds.Bottom < world.Bounds.Height * 2
                && top.Bounds.CenterX >= world.Bounds.X && top.Bounds.CenterX <= world.Bounds.Right)));
    }

    public static IEnumerable<TextRegion> MissionRewardControls(Observation frame) =>
        frame.Find(@"^(OPEN|COLLECT ALL|COLLECT|CONTINUE|CLAIM)$")
            .Where(region => !region.Pass.EndsWith("-word", StringComparison.Ordinal));

    public static Box? RewardClaimTarget(Observation frame)
    {
        var boxes = frame.Find(@"^CLAIM$").Select(r => r.Bounds).ToArray();
        if (boxes.Length == 0)
        {
            return null;
        }

        double left = boxes.Max(b => b.X), top = boxes.Max(b => b.Y);
        double right = boxes.Min(b => b.Right), bottom = boxes.Min(b => b.Bottom);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }

    public static Box? ReliableVisual(Observation frame, string id)
    {
        var matches = frame.FindVisual(id).Where(v => v.Score >= .82 && v.Margin >= .08).ToArray();
        return matches.Length == 1 ? matches[0].Bounds : null;
    }

    public static HomebaseZone? SelectedZone(Observation frame)
    {
        var matches = frame.Regions.Select(r => (Region: r, Zone: ParseZone(r.Normalized)))
            .Where(x => x.Zone is not null).OrderByDescending(x => x.Region.Bounds.Height).ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var largest = matches[0];
        if (matches.Any(m => m.Zone != largest.Zone && m.Region.Bounds.Height >= largest.Region.Bounds.Height * .8))
        {
            return null;
        }

        return largest.Zone;
    }

    private static int? ReadShieldLevel(Observation frame)
    {
        if (!frame.Has(@"\bSHIELD POWER\b"))
        {
            return null;
        }

        var levels = frame.Regions.Select(r => System.Text.RegularExpressions.Regex.Match(r.Text, @"^\s*(\d{1,2})\s*/\s*10\s*$"))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToArray();
        return levels.Length == 1 ? levels[0] : null;
    }

    private static HomebaseZone? ParseZone(string text) => text switch
    {
        "STONEWOOD" => HomebaseZone.Stonewood,
        "PLANKERTON" => HomebaseZone.Plankerton,
        "CANNY VALLEY" => HomebaseZone.CannyValley,
        "TWINE PEAKS" => HomebaseZone.TwinePeaks,
        _ => null
    };
    public static string ZoneName(HomebaseZone zone) => zone switch
    {
        HomebaseZone.CannyValley => "Canny Valley",
        HomebaseZone.TwinePeaks => "Twine Peaks",
        _ => zone.ToString()
    };

    public static bool IsSatisfied(ClickAction action, Observation frame, ScreenEvidence screen) => action.Step switch
    {
        NavigationStep.OpenDiscover => DiscoveryNavigation.IsBrowser(screen.Kind) || action.SourceScreen == ScreenKind.FortniteLobby && screen.Kind == ScreenKind.ModeDiscovery,
        NavigationStep.FilterByEpic => DiscoveryNavigation.IsBrowser(screen.Kind) && ReliableVisual(frame, "by-epic-selected") is not null,
        NavigationStep.SearchSaveTheWorld => screen.Kind == ScreenKind.DiscoverResults && DiscoveryNavigation.HasQuery(frame),
        NavigationStep.ConfirmSaveTheWorld => screen.Kind is ScreenKind.ModeSelection or ScreenKind.SaveTheWorldHub
            || QuestMenuEntry.IsMenuDestination(screen.Kind),
        NavigationStep.KeepPlaying => screen.Kind is not (ScreenKind.Unknown or ScreenKind.SleepMode),
        NavigationStep.OpenTargetExitBackpack => screen.Kind is ScreenKind.Backpack or ScreenKind.StormShieldConsole or ScreenKind.EnduranceStatus,
        NavigationStep.BackToStormShield => screen.Kind is ScreenKind.StormShieldConsole or ScreenKind.Backpack,
        NavigationStep.LeaveTargetMission => screen.Kind == ScreenKind.LeaveConfirmation || TargetWaveExit.IsExited(screen.Kind),
        NavigationStep.ConfirmTargetLeave => TargetWaveExit.IsExited(screen.Kind),
        NavigationStep.AbandonRejoin => screen.Kind is ScreenKind.ModeDiscovery or ScreenKind.ModeSelection
            or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.WorldMap
            or ScreenKind.MissionMap or ScreenKind.RewardClaim or ScreenKind.Rewards || DiscoveryNavigation.IsOutOfMission(screen.Kind),
        NavigationStep.ClaimReward => screen.Kind is ScreenKind.ModeDiscovery or ScreenKind.ModeSelection
            or ScreenKind.SaveTheWorldLobby or ScreenKind.SaveTheWorldHub or ScreenKind.WorldMap or ScreenKind.MissionMap || DiscoveryNavigation.IsOutOfMission(screen.Kind),
        NavigationStep.ContinueResults => screen.Kind is ScreenKind.Rewards or ScreenKind.RewardClaim or ScreenKind.Loading
            or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.ModeDiscovery or ScreenKind.ModeSelection
            || DiscoveryNavigation.IsOutOfMission(screen.Kind)
            || screen.Kind == action.SourceScreen && screen.Kind is ScreenKind.Victory or ScreenKind.Defeat
                && !frame.Has(@"^CONTINUE$")
                && AutomationController.UniqueTarget(frame.Find(@"^(?:C )?MATCHMAKE INTO HOMEBASE$").ToArray()) is not null,
        NavigationStep.ReturnToHomebase => screen.Kind is ScreenKind.Rewards or ScreenKind.RewardClaim or ScreenKind.Loading
            or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby,
        NavigationStep.CollectMissionRewards => screen.Kind is ScreenKind.RewardClaim or ScreenKind.Loading
            or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.ModeDiscovery or ScreenKind.ModeSelection || DiscoveryNavigation.IsOutOfMission(screen.Kind)
            || screen.Kind == ScreenKind.Rewards && MissionRewardReader.HasPageChanged(action.Context, frame, screen),
        NavigationStep.SelectSaveTheWorld => screen.Kind is ScreenKind.SaveTheWorldDetails or ScreenKind.SaveTheWorldHub
            || QuestMenuEntry.IsMenuDestination(screen.Kind)
            || screen.Kind == ScreenKind.ModeSelection && SaveTheWorldPlayControls(frame).Any(),
        NavigationStep.OpenQuestMenu => QuestMenuEntry.IsMenuDestination(screen.Kind),
        NavigationStep.OpenStormShieldConsole => screen.Kind == ScreenKind.StormShieldConsole,
        NavigationStep.OpenBackpack => screen.Kind is ScreenKind.Backpack or ScreenKind.StormShieldConsole or ScreenKind.EnduranceSetup or ScreenKind.EnduranceStatus,
        NavigationStep.SelectStormShieldTab => screen.Kind is ScreenKind.StormShieldConsole or ScreenKind.EnduranceSetup or ScreenKind.EnduranceStatus,
        NavigationStep.EnterSaveTheWorld => screen.Kind == ScreenKind.SaveTheWorldHub || QuestMenuEntry.IsMenuDestination(screen.Kind),
        NavigationStep.OpenMap => screen.Kind is ScreenKind.WorldMap or ScreenKind.MissionMap,
        NavigationStep.ChangeZone => screen.Kind == ScreenKind.WorldMap && SelectedZone(frame) is { } zone && zone.ToString() == action.Context,
        NavigationStep.EnterZone => screen.Kind == ScreenKind.MissionMap,
        NavigationStep.SelectHomebase => screen.Kind == ScreenKind.MissionMap && HasSelectedHomebase(frame)
            || screen.Kind == ScreenKind.HomebaseChoice || HasReachedHomebase(screen.Kind),
        NavigationStep.OpenHomebase => screen.Kind == ScreenKind.HomebaseChoice || HasReachedHomebase(screen.Kind),
        NavigationStep.SelectOwnHomebase => screen.Kind == ScreenKind.HomebaseChoice && ReliableVisual(frame, "own-homebase-selected") is not null
            || HasReachedHomebase(screen.Kind),
        NavigationStep.LaunchHomebase => screen.Kind is ScreenKind.Matchmaking or ScreenKind.Loading
            || HasReachedHomebase(screen.Kind),
        NavigationStep.LaunchLoadedHomebase => screen.Kind is ScreenKind.Loading or ScreenKind.Homebase,
        NavigationStep.StartEndurance => screen.Kind is ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown,
        NavigationStep.SelectEndurance => screen.Kind == ScreenKind.EnduranceSetup,
        NavigationStep.DisableAssistance => screen.Kind == ScreenKind.EnduranceSetup
            && ReliableVisual(frame, "assistance-unchecked") is not null && ReliableVisual(frame, "assistance-checked") is null,
        _ => false
    };

    private static bool HasReachedHomebase(ScreenKind kind) => kind is ScreenKind.HomebasePreparing
            or ScreenKind.HomebaseReady or ScreenKind.Homebase or ScreenKind.EnduranceCountdown or ScreenKind.ActiveEndurance;
}
