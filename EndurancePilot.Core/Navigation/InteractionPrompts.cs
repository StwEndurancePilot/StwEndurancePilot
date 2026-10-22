namespace EndurancePilot.Core;

public static class InteractionPrompts
{
    public static Box? StormShield(Observation frame) => Find(frame, "ACCESS STORM SHIELD MENU");
    public static Box? QuestLog(Observation frame) => Find(frame, "(?:VIEW )?QUEST LOG")
        ?? NavigationPlanner.ReliableVisual(frame, "quest-log-prompt");

    public static Box? QuestMenuAnchor(Observation frame)
    {
        if (QuestLog(frame) is { } prompt)
        {
            return prompt;
        }

        var board = NavigationPlanner.ReliableVisual(frame, "hub-quest-board");
        var door = NavigationPlanner.ReliableVisual(frame, "hub-xp-door");

        if (board is not { } b || door is not { } d)
        {
            return null;
        }

        double scale = b.Width / 420;
        return d.CenterX > b.Right && d.X - b.Right < b.Width
            && Math.Abs(d.CenterY - b.CenterY) < b.Height
            && d.Width / 185 / scale is > .8 and < 1.2 ? b : null;
    }

    private static Box? Find(Observation frame, string label)
    {
        var keys = frame.Find("^E$").ToArray();
        var prompts = frame.Find("^(?:E )?" + label + "$").Where(region => region.Normalized.StartsWith("E ")
            || keys.Any(key => key.Bounds.CenterX < region.Bounds.X
                && region.Bounds.X - key.Bounds.Right < region.Bounds.Height * 5
                && Math.Abs(key.Bounds.CenterY - region.Bounds.CenterY) < region.Bounds.Height));
        return AutomationController.UniqueTarget(prompts.ToArray());
    }

    public static bool IsAllowedKeyboardAction(ClickAction action) => QuestMenuEntry.IsFallback(action) || (action.Key, action.Step, action.SourceScreen) switch
    {
        (NavigationKey.C, NavigationStep.ReturnToHomebase, ScreenKind.Victory or ScreenKind.Defeat) => true,
        (NavigationKey.Escape, NavigationStep.ContinueResults, ScreenKind.Victory or ScreenKind.Defeat) => true,
        (NavigationKey.C or NavigationKey.Space, NavigationStep.CollectMissionRewards, ScreenKind.Rewards) => true,
        (NavigationKey.E, NavigationStep.OpenStormShieldConsole, ScreenKind.Homebase) => true,
        (NavigationKey.Tab, NavigationStep.OpenQuestMenu, ScreenKind.SaveTheWorldHub) => true,
        (NavigationKey.Tab, NavigationStep.OpenBackpack, ScreenKind.Homebase) => true,
        (NavigationKey.Tab, NavigationStep.OpenTargetExitBackpack, ScreenKind.ActiveEndurance) => true,
        (NavigationKey.Escape, NavigationStep.BackToStormShield, ScreenKind.EnduranceStatus) => true,
        _ => false
    };
}
