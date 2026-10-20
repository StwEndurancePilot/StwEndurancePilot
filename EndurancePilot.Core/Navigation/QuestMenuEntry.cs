namespace EndurancePilot.Core;

public sealed class QuestMenuEntry
{
    public const int MaximumAttempts = 10;
    private int attempts;
    private DateTimeOffset nextTap;
    private Box? previousHealth;
    private DateTimeOffset healthSeenAt;
    public bool Active
    {
        get; private set;
    }
    public bool AttemptsExhausted => attempts >= MaximumAttempts;
    public void Invalidate() => previousHealth = null;
    public void Reset()
    {
        Active = false;
        attempts = 0;
        previousHealth = null;
    }
    public void Joined(DateTimeOffset now)
    {
        Active = true;
        attempts = 0;
        nextTap = now;
        previousHealth = null;
    }
    public void Tapped(DateTimeOffset now)
    {
        attempts++;
        nextTap = now.AddSeconds(2);
        previousHealth = null;
    }
    public static bool CanTap(ScreenKind screen) => screen is ScreenKind.Unknown or ScreenKind.SaveTheWorldHub;
    public static bool IsMenuDestination(ScreenKind screen) => screen is ScreenKind.SaveTheWorldLobby
        or ScreenKind.WorldMap or ScreenKind.MissionMap or ScreenKind.Backpack;
    public static bool IsEntryAction(NavigationStep? step) => step is NavigationStep.EnterSaveTheWorld
        or NavigationStep.SelectSaveTheWorld or NavigationStep.ConfirmSaveTheWorld or NavigationStep.OpenQuestMenu;
    public static bool IsEntryScreen(ScreenKind screen) => CanTap(screen) || screen is ScreenKind.Loading or ScreenKind.ModeSelection
        or ScreenKind.Matchmaking or ScreenKind.HomebaseReady or ScreenKind.HomebasePreparing;
    public Box? ReadyTarget(Observation frame, ScreenEvidence screen)
    {
        var health = NavigationPlanner.ReliableVisual(frame, "player-health-bar");
        if (!Active || !screen.AllowsInput || !CanTap(screen.Kind) || health is null)
        {
            previousHealth = null;
            return null;
        }
        bool stable = previousHealth is { } prior && prior.IntersectionOverUnion(health.Value) > .6
            && frame.CapturedAt - healthSeenAt >= TimeSpan.FromMilliseconds(100)
            && frame.CapturedAt - healthSeenAt <= NavigationTiming.HealthProbeMaximumGap;
        if (previousHealth is not { } old || old.IntersectionOverUnion(health.Value) <= .6
            || frame.CapturedAt - healthSeenAt > NavigationTiming.HealthProbeMaximumGap)
        {
            healthSeenAt = frame.CapturedAt;
        }

        previousHealth = health;

        if (stable && screen.Kind == ScreenKind.SaveTheWorldHub)
        {
            attempts = 0;
        }

        return stable && frame.CapturedAt >= nextTap ? health : null;
    }
    public static bool IsFallback(ClickAction action) => action.Step == NavigationStep.RetryQuestMenu
        && action.Key == NavigationKey.Tab && CanTap(action.SourceScreen);
}
