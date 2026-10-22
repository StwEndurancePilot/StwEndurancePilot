namespace EndurancePilot.Core;

public static class NavigationTiming
{
    public const int MenuPollMilliseconds = 50;
    public const int GameplayPollMilliseconds = 2500;

    public static readonly TimeSpan HealthProbeMaximumGap = TimeSpan.FromSeconds(30);
    public static TimeSpan MaximumObservationAge(NavigationStep? step) => step is NavigationStep.RetryQuestMenu
        or NavigationStep.OpenQuestMenu or NavigationStep.OpenMap or NavigationStep.SelectStormShieldTab
        ? HealthProbeMaximumGap : TimeSpan.FromSeconds(5);
    public static TimeSpan ConfirmationGap(ScreenEvidence screen) =>
        screen.AllowsInput
            && screen.Kind is ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.Backpack
        ? HealthProbeMaximumGap : TimeSpan.FromSeconds(6);

    public static bool IsMenuStep(NavigationStep? step) => step is not null
        && step is not (NavigationStep.StartEndurance or NavigationStep.ClaimReward
            or NavigationStep.ContinueResults or NavigationStep.CollectMissionRewards or NavigationStep.ReturnToHomebase);

    public static bool IsFastStep(NavigationStep? step) => step is NavigationStep.EnterSaveTheWorld or NavigationStep.OpenMap
        or NavigationStep.ChangeZone or NavigationStep.EnterZone or NavigationStep.SelectHomebase or NavigationStep.OpenHomebase
        or NavigationStep.SelectOwnHomebase or NavigationStep.LaunchHomebase or NavigationStep.LaunchLoadedHomebase
        or NavigationStep.SelectEndurance or NavigationStep.DisableAssistance or NavigationStep.EnableAssistance or NavigationStep.SelectSaveTheWorld
        or NavigationStep.OpenQuestMenu or NavigationStep.OpenStormShieldConsole or NavigationStep.OpenBackpack or NavigationStep.SelectStormShieldTab
        or NavigationStep.OpenDiscover or NavigationStep.FilterByEpic or NavigationStep.SearchSaveTheWorld or NavigationStep.ConfirmSaveTheWorld;

    public static TimeSpan Stability(NavigationStep? step, ScreenEvidence evidence) => IsFastStep(step) && !evidence.UsesFuzzyMatching
        ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(500);

    public static TimeSpan TransitionCooldown(NavigationStep? step) => IsFastStep(step) ? TimeSpan.Zero
        : IsMenuStep(step) ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(5);

    public static int HoverSettleMilliseconds(NavigationStep? step) => IsFastStep(step) ? 60 : 180;

    public static int PollMilliseconds(ScreenKind screen, NavigationStep? pending) => screen is ScreenKind.Rewards or ScreenKind.RewardClaim ? 50
        : screen == ScreenKind.Loading && pending is null ? 250 : IsMenuStep(pending)
        || screen is ScreenKind.ModeDiscovery or ScreenKind.ModeSelection or ScreenKind.SaveTheWorldHub
            or ScreenKind.FortniteLobby or ScreenKind.DiscoverBrowser or ScreenKind.DiscoverResults or ScreenKind.SaveTheWorldDetails
            or ScreenKind.SaveTheWorldLobby or ScreenKind.WorldMap or ScreenKind.MissionMap
            or ScreenKind.HomebaseChoice or ScreenKind.HomebasePreparing or ScreenKind.HomebaseReady
            or ScreenKind.Matchmaking or ScreenKind.Homebase or ScreenKind.Backpack
            or ScreenKind.StormShieldConsole or ScreenKind.EnduranceSetup or ScreenKind.RejoinDialog
            or ScreenKind.ErrorDialog or ScreenKind.NeedsAttention or ScreenKind.SleepMode or ScreenKind.LeaveConfirmation
            or ScreenKind.FeedbackSurvey or ScreenKind.NewUnlock
        ? MenuPollMilliseconds : GameplayPollMilliseconds;
}
