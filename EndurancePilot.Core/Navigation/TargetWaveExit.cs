namespace EndurancePilot.Core;

public static class TargetWaveExit
{
    private static Box? ActiveWaveAnchor(Observation frame)
    {
        var readings = WaveReader.Read(frame);

        return readings.Count > 0 && readings.Select(r => r.Wave).Distinct().Count() == 1
                    ? readings.OrderBy(r => r.Bounds.Y).ThenBy(r => r.Bounds.X).First().Bounds
                    : null;
    }
    public static Box? LeaveControl(Observation frame) => AutomationController.UniqueTarget(frame.Regions
        .Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal) && (r.Normalized is "LEAVE" or "LEAVE PARTY"
            || r.Normalized.StartsWith("LEAVE ") && TextSimilarity.Score(r.Text, "LEAVE WITH PARTY") >= .8)).ToArray());
    public static bool IsExitStep(NavigationStep? step) => step is NavigationStep.LeaveTargetMission or NavigationStep.ConfirmTargetLeave;
    public static bool IsExited(ScreenKind kind) => kind is ScreenKind.Victory or ScreenKind.Defeat
        or ScreenKind.Rewards or ScreenKind.RewardClaim or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby
        or ScreenKind.ModeSelection or ScreenKind.ModeDiscovery or ScreenKind.WorldMap or ScreenKind.MissionMap || DiscoveryNavigation.IsOutOfMission(kind);

    public static ScreenEvidence? RecognizeConfirmation(Observation frame)
    {
        var lines = frame.Regions.Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal)).ToArray();
        bool context = frame.Has(@"\bARE YOU SURE\b.*\b(LEAVE|EXIT)\b")
            || frame.Has(@"\bLEAVE THE STORM SHIELD AND RETURN TO HOMEBASE\b")
            || frame.Has(@"^LEAVE (MISSION|STORM SHIELD|WITH PARTY)$") && frame.Has(@"\b(PROGRESS|REWARDS|RETURN TO|LOBBY|ABANDON)\b");
        if (!context)
        {
            return null;
        }

        var cancel = lines.Where(r => r.Normalized is "CANCEL" or "NO").ToArray();
        var controls = lines.Where(r => r.Normalized is "LEAVE" or "LEAVE MISSION" or "LEAVE WITH PARTY" or "YES" or "CONFIRM" or "QUIT")
            .Where(r => cancel.Any(c => Math.Abs(c.Bounds.CenterY - r.Bounds.CenterY) < Math.Max(c.Bounds.Height, r.Bounds.Height) * 2
                && Math.Abs(c.Bounds.CenterX - r.Bounds.CenterX) > Math.Max(c.Bounds.Width, r.Bounds.Width) * .5)).ToArray();
        return controls.Length == 0 ? null : new(ScreenKind.LeaveConfirmation, ["Leave confirmation with aligned affirmative and cancel controls"], controls);
    }

    public static NavigationDecision Decide(Observation frame, ScreenEvidence screen)
    {
        if (!screen.AllowsInput)
        {
            return new(null, "Waiting for verified exit controls");
        }

        NavigationDecision Plan(NavigationStep step, string description, Box? target, NavigationKey? key = null) => target is { } box
            ? new(new(step, description, box, Key: key), description) : new(null, "Waiting for one unambiguous target: " + description);
        switch (screen.Kind)
        {
            case ScreenKind.ActiveEndurance:
                return Plan(NavigationStep.OpenTargetExitBackpack, "Target wave complete · open backpack to leave",
                    ActiveWaveAnchor(frame), NavigationKey.Tab);
            case ScreenKind.Backpack:
                return Plan(NavigationStep.SelectStormShieldTab, "Open Storm Shield to leave the completed target", StormShieldNavigation.BackpackTab(frame));
            case ScreenKind.StormShieldConsole:
                return Plan(NavigationStep.LeaveTargetMission, "Leave Storm Shield after the target wave",
                    LeaveControl(frame));
            case ScreenKind.EnduranceStatus:
            case ScreenKind.EnduranceRestartRequired:
                return Plan(NavigationStep.BackToStormShield, "Back to Storm Shield exit controls",
                    AutomationController.UniqueTarget(frame.Find(@"^ENDURANCE$")
                        .Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal)).ToArray()), NavigationKey.Escape);
            case ScreenKind.LeaveConfirmation:
                return Plan(NavigationStep.ConfirmTargetLeave, "Confirm leaving after the target wave", AutomationController.UniqueTarget(screen.Targets));
            default:
                return new(null, "Target wave complete · waiting for verified Storm Shield exit controls");
        }
    }
}
