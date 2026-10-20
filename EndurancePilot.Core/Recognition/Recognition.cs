using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double IntersectionOverUnion(Box other)
    {
        var intersection = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X))
            * Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
        var union = Width * Height + other.Width * other.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}

public sealed record TextRegion(string Text, Box Bounds, string Pass = "native")
{
    public string Normalized => Normalize(Text);
    public static string Normalize(string text) => Regex.Replace(text.ToUpperInvariant(), @"[^A-Z0-9:]+", " ").Trim();
}

public sealed record VisualRegion(string Id, Box Bounds, double Score, double Margin);

public sealed record Observation(DateTimeOffset CapturedAt, int Width, int Height, IReadOnlyList<TextRegion> Regions,
    IReadOnlyList<VisualRegion>? Visuals = null)
{
    public IEnumerable<TextRegion> Find(string pattern) => Regions.Where(r => Regex.IsMatch(r.Normalized, pattern));
    public bool Has(string pattern) => Find(pattern).Any();
    public string Text => string.Join(Environment.NewLine, Regions.Select(r => r.Text));
    public IEnumerable<VisualRegion> FindVisual(string id) => (Visuals ?? []).Where(v => v.Id == id);
}

public enum ScreenKind
{
    Unknown, ModeSelection, SaveTheWorldLobby, WorldMap, MissionMap, HomebaseChoice, HomebaseReady, Matchmaking,
    Loading, Homebase, EnduranceSetup, ActiveEndurance, Victory, Defeat, Rewards, ErrorDialog, NeedsAttention,
    RewardClaim, ModeDiscovery, EnduranceCountdown, StormShieldConsole, SaveTheWorldHub, Backpack, EnduranceStatus,
    HomebasePreparing, RejoinDialog, SleepMode, LeaveConfirmation,
    FortniteLobby, DiscoverBrowser, DiscoverResults, SaveTheWorldDetails, FeedbackSurvey, NewUnlock
}

public sealed record ScreenEvidence(ScreenKind Kind, IReadOnlyList<string> Reasons, IReadOnlyList<TextRegion> Targets)
{
    public double? MatchScore
    {
        get; init;
    }
    public ScreenKind? LikelyKind
    {
        get; init;
    }
    public bool UsesFuzzyMatching
    {
        get; init;
    }
    public bool AllowsInput { get; init; } = true;
    public IReadOnlyList<TextMatch> TextMatches { get; init; } = [];
    public static ScreenEvidence Unknown(string reason) => new(ScreenKind.Unknown, [reason], []);
}

public sealed class ScreenRecognizer
{
    public ScreenEvidence Recognize(Observation frame)
    {
        var exact = RecognizeRules(frame);

        return exact.Kind is ScreenKind.Unknown or ScreenKind.Backpack or ScreenKind.SaveTheWorldLobby or ScreenKind.HomebasePreparing
                    ? FuzzyScreens.Recognize(frame, exact) : exact;
    }

    private ScreenEvidence RecognizeRules(Observation frame)
    {

        if (SleepDialog.Recognize(frame) is { } sleep)
        {
            return sleep;
        }

        if (RejoinDialog.Recognize(frame) is { } rejoin)
        {
            return rejoin;
        }

        bool Has(string pattern) => frame.Has(pattern);
        ScreenEvidence Result(ScreenKind kind, string reason, string targets = "(?!)") =>
            new(kind, [reason], frame.Find(targets).ToArray());

        if (Has(@"\b(ERRORS IN YOUR INSTALLATION|VERIFY OR REINSTALL|INSTALLATION ERROR)\b"))
        {
            return Result(ScreenKind.NeedsAttention, "Fortnite requires installation verification; dismiss once and pause", @"^CONTINUE$");
        }

        if (ErrorDialogs.Recognize(frame) is { } error)
        {
            return error;
        }

        if (frame.Has(MissionRewardReader.RevealHeading) && frame.FindVisual("reward-card").Count() == 1)
        {
            return new(ScreenKind.Rewards, ["Received reward with selected card"], NavigationPlanner.MissionRewardControls(frame).ToArray())
            {
                AllowsInput = NavigationPlanner.MissionRewardControls(frame).Any()
            };
        }

        if (Has(@"\bWE WANT TO HEAR FROM\b") && Has(@"^SKIP$")
            && Has(@"\b(PROVIDING FEEDBACK IS OPTIONAL|SELECT ALL THAT APPLY|SUBMIT)\b"))
        {
            return Result(ScreenKind.FeedbackSurvey, "Optional feedback survey with Skip control", @"^SKIP$");
        }

        if (Has(@"^NEW$") && Has(@"^CONTINUE$")
            && frame.Find(@"^NEW$").Any(heading => frame.Find(@"^CONTINUE$").Any(button =>
                button.Bounds.Y > heading.Bounds.Bottom
                && frame.Regions.Any(body => !body.Pass.EndsWith("-word")
                    && body.Normalized.Length > 12 && body.Bounds.Y > heading.Bounds.Bottom
                    && body.Bounds.Bottom < button.Bounds.Y))))
        {
            return Result(ScreenKind.NewUnlock, "New unlock announcement with description and Continue", @"^CONTINUE$");
        }

        var hasErrorHeading = Has(@"\b(THERE WAS A PROBLEM|THERE WAS AN ERROR|SUPPOSED TO HAPPEN|CONNECTION LOST|NETWORK CONNECTION LOST)\b");
        if (hasErrorHeading && Has(@"\b(KICKED FOR IDLE|CONNECTION TO THE HOST WAS LOST|CONNECTION LOST|NETWORK CONNECTION LOST|CONNECTION TIMED OUT|FAILED TO CONNECT)\b") && Has(@"^(CONTINUE|RETRY|OK)$"))
        {
            return Result(ScreenKind.ErrorDialog, "Connection failure and recovery control", @"^(CONTINUE|RETRY|OK)$");
        }

        if (hasErrorHeading)
        {
            return Result(ScreenKind.NeedsAttention, "Unclassified game error; dismiss only a Continue/OK control and pause", @"^(CONTINUE|OK)$");
        }

        if (TargetWaveExit.RecognizeConfirmation(frame) is { } leave)
        {
            return leave;
        }

        if (Has(@"^ACCOUNT RESOURCE$") && Has(@"\bSEASON XP$")
            && Has(@"\bTHANKS FOR PLAYING SAVE THE\b") && Has(@"^CLAIM$"))
        {
            return Result(ScreenKind.RewardClaim, "Save the World Season XP reward and Claim control", @"^CLAIM$");
        }

        if (Has(@"^(MISSION REWARDS|[YV]OU RECEIVED|QUEST COMPLETE)$")
                    && NavigationPlanner.MissionRewardControls(frame).Any())
        {
            return new(ScreenKind.Rewards, ["Reward heading and standalone reward action"],
                NavigationPlanner.MissionRewardControls(frame).ToArray());
        }

        if (Has(@"^VICTORY$") && Has(@"\b(TEAM SCORE|CONTINUE|MISSION STATS|REWARDS)\b"))
        {
            return Result(ScreenKind.Victory, "Victory heading corroborated by result controls", @"\bCONTINUE\b");
        }

        if (Has(@"^DEFEAT$") && Has(@"\b(TEAM SCORE|CONTINUE|MISSION STATS|REWARDS)\b"))
        {
            return Result(ScreenKind.Defeat, "Defeat heading corroborated by result controls", @"\bCONTINUE\b");
        }

        if (Has(@"\bSTART ENDURANCE\b") && Has(@"\bSTORM SHIELD\b"))
        {
            return Result(ScreenKind.EnduranceSetup, "Start Endurance control", @"\bSTART ENDURANCE\b");
        }

        if (StormShieldNavigation.IsConsole(frame))
        {
            return Result(ScreenKind.StormShieldConsole, "Storm Shield console with endurance, wargames and storage choices", @"^STORM S[HN]IELD ENDURANCE$");
        }

        if (Has(@"^ENDURANCE$") && Has(@"^POWER LEVEL$") && Has(@"\bDROP YOUR STORM SHIELD AND SURVIVE\b"))
        {
            return Result(ScreenKind.EnduranceStatus, "Endurance information panel without a verified start control");
        }

        if (StormShieldNavigation.BackpackTab(frame) is not null)
        {
            return Result(ScreenKind.Backpack, "Aligned Loadout, Objectives, Quests and Storm Shield tabs");
        }

        if (WaveReader.ReadCountdown(frame).Count > 0 && Has(@"^\d{1,2}:\d{2}$"))
        {
            return Result(ScreenKind.EnduranceCountdown, "Endurance defense countdown; upcoming wave is not yet active");
        }

        if (WaveReader.Read(frame).Count > 0 && Has(@"\bENDURANCE\b") && !Has(@"\bENDURANCE MAX WAVE\b"))
        {
            return Result(ScreenKind.ActiveEndurance, "Endurance label and wave indicator");
        }

        if (StormShieldNavigation.InactiveObjective(frame) is not null)
        {
            return Result(ScreenKind.Homebase, "Storm Shield objective awaiting activation");
        }

        if (InteractionPrompts.QuestMenuAnchor(frame) is not null && WaveReader.Read(frame).Count == 0)
        {
            return Result(ScreenKind.SaveTheWorldHub, "Quest-log prompt or corroborated hub landmarks in the 3D lobby");
        }

        if (Has(@"\bCOMMUNIT[YV] LOOKOUT\b") && Has(@"\bMY STORM SHIELD\b") && Has(@"\b(STORM SHIELD ASSIST|SELECT A GAMEPLAY OPTION)\b"))
        {
            return Result(ScreenKind.HomebaseChoice, "Own Storm Shield choice, dialog heading and assist option", @"^(MY STORM SHIELD|LAUNCH)$");
        }

        var hasLoadout = Has(@"\b[HN]ERO\s*LOADOUT\b")
                    || Has(@"^[HMN]ERO ABILITIES$") && Has(@"^EDIT$")
                        && Has(@"^MISSION DETAILS$") && Has(@"^ABANDON$");
        var hasHomebase = Has(@"\bSTORM S(?:[HN]I|III|IU)ELD\b");
        if (hasLoadout && hasHomebase && Has(NavigationPlanner.MissionLaunchLabel))
        {
            return Result(ScreenKind.HomebaseReady, "Homebase, hero loadout and Launch control", NavigationPlanner.MissionLaunchLabel);
        }

        if (hasLoadout && (hasHomebase || Has(@"^EDIT$")
            && (Has(@"^[HMN]ERO ABILITIES$") || Has(@"^MISSION DETAILS$") && Has(@"^ABANDON$"))))
        {
            return Result(ScreenKind.HomebasePreparing, "Mission hero loadout; waiting for homebase Launch");
        }

        if (Has(@"\bSTARTING STORM SHIELD\b") && Has(@"^CANCEL$"))
        {
            return Result(ScreenKind.Matchmaking, "Starting Storm Shield with cancellation control");
        }

        var hasStwNavigation = Has(@"^(MAP|QUESTS)$") && Has(@"^COMMAND\b") && Has(@"^ARMORY\b");
        if (hasStwNavigation && Has(@"\bRECOMMENDED POWER\b") && NavigationPlanner.SelectedZone(frame) is not null)
        {
            return Result(ScreenKind.WorldMap, "STW navigation, zone heading and power description", @"^SELECT$");
        }

        if (hasStwNavigation && (Has(@"\b(RECOMMENDED|STORM SHIELD)\b") && Has(@"^SELECT$")
            || NavigationPlanner.HasSelectedHomebase(frame) && Has(@"\bENDURANCE MAX WAVE\b")
            || NavigationPlanner.HasSelectedHomebase(frame) && Has(@"^REWARDS$") && Has(@"^ZOOM$")
            || Has(@"^REWARDS$") && Has(@"^ZOOM$")))
        {
            return Result(ScreenKind.MissionMap, "STW navigation and mission details", @"^SELECT$");
        }

        if (hasStwNavigation)
        {
            return Result(ScreenKind.SaveTheWorldLobby, "Three distinct Save the World navigation labels", @"^MAP$");
        }

        if (DiscoveryNavigation.Recognize(frame) is { } discovery)
        {
            return discovery;
        }

        if (Has(@"^CURRENT LOBBY$") && Has(@"^PLA[YV]$") && Has(@"^SHOP$"))
        {
            return Result(ScreenKind.ModeDiscovery, "Mode discovery with current lobby and Save the World category");
        }

        if (NavigationPlanner.SaveTheWorldTitles(frame).Any()
            && (Has(@"^COMPETE?$") && Has(@"^CAREER$") || DiscoveryNavigation.HasLobbyNavigation(frame)))
        {
            return Result(ScreenKind.ModeSelection, "Main mode navigation with Save the World selected", @"^PLAY$");
        }

        if (DiscoveryNavigation.HasLobbyNavigation(frame))
        {
            return Result(ScreenKind.FortniteLobby, "Fortnite lobby navigation; Save the World not selected");
        }

        if (Has(@"^CONNECTING$") || Has(@"^LOADING$"))
        {
            return Result(ScreenKind.Loading, "Explicit loading text");
        }

        return ScreenEvidence.Unknown("No corroborated screen rule matched");
    }
}

public sealed record WaveReading(int Wave, Box Bounds);
public static class WaveReader
{
    public static IReadOnlyList<WaveReading> ReadCountdown(Observation frame) => frame.Regions
        .Select(region => (region, match: Regex.Match(region.Normalized, @"\bDEFENSE:?\s+WAVE\s+(\d{1,2})\s+STARTS\b")))
        .Where(item => item.match.Success && int.TryParse(item.match.Groups[1].Value, out int wave) && wave is >= 1 and <= 30)
        .Select(item => new WaveReading(int.Parse(item.match.Groups[1].Value), item.region.Bounds)).ToArray();

    public static IReadOnlyList<WaveReading> Read(Observation frame)
    {
        var readings = new List<WaveReading>();
        foreach (var region in frame.Regions)
        {
            if (Regex.IsMatch(region.Normalized, @"\b(STARTS|MAX WAVE)\b"))
            {
                continue;
            }

            var match = Regex.Match(region.Normalized, @"^(?:\d{1,2}:\d{2}\s+)?(?:ENDURANCE\s*:?\s*)?WAVE\s+(\d{1,2})(?:\s|$)");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int wave) || wave is < 1 or > 30)
            {
                continue;
            }

            if (readings.Any(r => r.Wave == wave && r.Bounds.IntersectionOverUnion(region.Bounds) > .35))
            {
                continue;
            }

            readings.Add(new(wave, region.Bounds));
        }
        return readings;
    }
}
