namespace EndurancePilot.Core;

public sealed record ClickAction(Guid Id, string Description, ScreenKind SourceScreen, Box Target,
    int FrameWidth, int FrameHeight, DateTimeOffset ObservedAt, bool PauseAfterDismissal = false,
    NavigationStep? Step = null, string? Context = null, NavigationKey? Key = null);

public enum ControllerStatus
{
    Stopped, Observing, AwaitingTransition, PausedForAttention
}

public sealed class AutomationController
{
    public ControllerStatus Status { get; private set; } = ControllerStatus.Stopped;
    public string Detail { get; private set; } = "Stopped";
    public ClickAction? Pending
    {
        get; private set;
    }
    public int ConfirmedDismissals
    {
        get; private set;
    }
    public bool ErrorRecoveryOnly { get; set; } = true;
    public bool ClaimRewards
    {
        get; set;
    }
    public bool WaitForRewardRead
    {
        get; set;
    }
    public bool FinishRewardsOnly
    {
        get; set;
    }
    public int ConfirmedRewardClaims
    {
        get; private set;
    }
    public int ConfirmedAbandons
    {
        get; private set;
    }
    public int ConfirmedWakes
    {
        get; private set;
    }
    public int ConfirmedTargetExits
    {
        get; private set;
    }
    public bool TargetExitRequested
    {
        get; private set;
    }
    public bool AwaitingTargetExit => TargetExitRequested && (TargetWaveExit.IsExitStep(Pending?.Step) || TargetWaveExit.IsExitStep(suspendedPending?.Step));
    public bool AwaitingErrorDismissal => Pending?.SourceScreen is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention
        || suspendedPending?.SourceScreen is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention;
    private ClickAction? suspendedPending;
    private int suspendedAttempts;
    public RunnerProfile Profile { get; set; } = new();
    public bool RunInProgress
    {
        get; private set;
    }
    public bool HomebaseClickConfirmed
    {
        get; private set;
    }
    private readonly QuestMenuEntry questEntry = new();
    public bool AwaitingQuestMenu => questEntry.Active;
    public int QuestMenuTimeouts
    {
        get; private set;
    }
    private DateTimeOffset? homebaseHeadingMissingSince;
    private DateTimeOffset dispatchedAt;
    private DateTimeOffset lastObservation;
    private int lastWidth, lastHeight;
    private DateTimeOffset nextActionAt;
    private ScreenKind previousScreen;
    private Box? previousTarget;
    private NavigationStep? previousStep;
    private string? previousContext;
    private DateTimeOffset stableSince;
    private int stableFrames;
    private int attempts;
    private int navigationActions;
    private int rejectedTargets;
    private ScreenKind exitCandidate;
    private int exitFrames;
    private DateTimeOffset exitSince;
    private ClickAction? proposed;
    private NavigationPlan? transitionPlan;
    private int transitionPlanFrames;
    private DateTimeOffset transitionPlanSince;
    private readonly Queue<DateTimeOffset> recentErrors = new();

    public void Start(bool runInProgress = false)
    {
        questEntry.Reset();
        TargetExitRequested = false;
        suspendedPending = null;
        Pending = null;
        proposed = null;
        attempts = 0;
        stableFrames = 0;
        previousTarget = null;
        recentErrors.Clear();
        lastObservation = default;
        lastWidth = lastHeight = 0;
        nextActionAt = default;
        RunInProgress = runInProgress;
        HomebaseClickConfirmed = false;
        homebaseHeadingMissingSince = null;
        navigationActions = 0;
        rejectedTargets = 0;
        exitFrames = 0;
        Status = ControllerStatus.Observing;
        Detail = "Waiting for confirmed screen evidence";
    }

    public void Stop(string reason = "Stopped by user")
    {
        questEntry.Reset();
        TargetExitRequested = false;
        suspendedPending = null;
        Pending = null;
        proposed = null;
        Status = ControllerStatus.Stopped;
        Detail = reason;
    }

    public void RecordRunOutcome(RunRecord outcome)
    {
        if (outcome.Outcome == RunOutcome.InProgress || outcome.EndedAt is null)
        {
            return;
        }

        RunInProgress = false;
        TargetExitRequested = false;
        navigationActions = 0;
        exitFrames = 0;

        if (Pending?.Step is NavigationStep.OpenTargetExitBackpack or NavigationStep.BackToStormShield
                    or NavigationStep.SelectStormShieldTab or NavigationStep.StartEndurance)
        {
            Pending = null;
            proposed = null;
            suspendedPending = null;
            attempts = stableFrames = transitionPlanFrames = 0;
            nextActionAt = default;
            Status = ControllerStatus.Observing;
        }
    }

    public void ObserveRunInProgress()
    {
        RunInProgress = true;
        navigationActions = 0;
    }

    public void RequestTargetExit(string reason = "Target wave finished · preparing to leave Storm Shield")
    {
        if (TargetExitRequested || ErrorRecoveryOnly || Status is ControllerStatus.Stopped or ControllerStatus.PausedForAttention)
        {
            return;
        }

        TargetExitRequested = true;
        Pending = null;
        proposed = null;
        suspendedPending = null;
        attempts = 0;
        stableFrames = 0;
        transitionPlanFrames = 0;
        nextActionAt = default;
        Status = ControllerStatus.Observing;
        Detail = reason;
    }

    private NavigationDecision DecideNavigation(Observation frame, ScreenEvidence evidence)
    {
        if (WaitForRewardRead && evidence.Kind == ScreenKind.Rewards
            && !NavigationPlanner.MissionRewardControls(frame).Any(r => r.Normalized == "OPEN"))
        {
            return new(null, "Reading rewards; leaving the reveal open until quantities are stable.");
        }

        if (evidence.Kind == ScreenKind.Rewards && attempts == 1
            && NavigationPlanner.MissionRewardControls(frame).Any(r => r.Normalized == "COLLECT ALL"))
        {
            var all = NavigationPlanner.MissionRewardControls(frame).Where(r => r.Normalized == "COLLECT ALL").ToArray();
            if (UniqueTarget(all) is { } button)
            {
                return new(new(NavigationStep.CollectMissionRewards, "Collect remaining rewards after C did not advance",
                    button, MissionRewardReader.PageContext(frame, evidence), NavigationKey.Space), "Collect remaining rewards");
            }
        }

        return TargetExitRequested
        && evidence.Kind is not (ScreenKind.RejoinDialog or ScreenKind.SleepMode or ScreenKind.Victory or ScreenKind.Defeat or ScreenKind.Rewards or ScreenKind.RewardClaim or ScreenKind.FeedbackSurvey or ScreenKind.NewUnlock)
        ? TargetWaveExit.Decide(frame, evidence) : NavigationPlanner.Decide(frame, evidence, Profile, HomebaseClickConfirmed);
    }

    public void Pause(string reason)
    {
        questEntry.Reset();
        suspendedPending = null;
        Pending = null;
        proposed = null;
        Status = ControllerStatus.PausedForAttention;
        Detail = reason;
    }

    public void Dispatched(ClickAction action, DateTimeOffset now)
    {
        if (Status != ControllerStatus.Observing || action != proposed || action.ObservedAt != lastObservation
            || now < action.ObservedAt || now - action.ObservedAt > NavigationTiming.MaximumObservationAge(action.Step))
        {
            throw new InvalidOperationException("Action is no longer current.");
        }

        proposed = null;
        rejectedTargets = 0;
        if (action.Step is NavigationStep.EnterSaveTheWorld or NavigationStep.LaunchLoadedHomebase)
        {
            questEntry.Joined(now);
        }

        if (QuestMenuEntry.IsFallback(action))
        {
            questEntry.Tapped(now);
            Pending = null;
            attempts = 0;
            stableFrames = 0;
            transitionPlanFrames = 0;
            Status = ControllerStatus.Observing;
            Detail = "Tab sent; checking for the quest menu";
            return;
        }
        Pending = action;
        transitionPlan = null;
        transitionPlanFrames = 0;
        dispatchedAt = now;
        attempts++;
        if (action.Step is not null && action.Step is not (NavigationStep.ClaimReward or NavigationStep.KeepPlaying))
        {
            navigationActions++;
        }

        Status = ControllerStatus.AwaitingTransition;
        Detail = "Checking the result: " + action.Description;
        stableFrames = 0;
    }

    public void TargetChanged(ClickAction action, DateTimeOffset now)
    {
        if (Status != ControllerStatus.Observing || proposed != action)
        {
            throw new InvalidOperationException("Rejected action is no longer current.");
        }

        proposed = null;
        questEntry.Invalidate();
        stableFrames = 0;
        previousTarget = null;
        if (++rejectedTargets >= 3)
        {
            Pause("Target changed during three recognition attempts. No click or keypress sent; inspect the screen.");
            return;
        }
        nextActionAt = now.AddSeconds(3);
        Detail = "Target changed; no click or keypress sent. Waiting for fresh stable evidence.";
    }

    public void InvalidateObservation()
    {
        questEntry.Invalidate();

        proposed = null;
        stableFrames = 0;
        previousTarget = null;
        transitionPlan = null;
        transitionPlanFrames = 0;
        exitFrames = 0;
        HomebaseClickConfirmed = false;
        homebaseHeadingMissingSince = null;
    }

    public void ReacquireInput()
    {
        InvalidateObservation();
        questEntry.Reset();
        Pending = suspendedPending = null;
        attempts = suspendedAttempts = 0;
        dispatchedAt = nextActionAt = default;
        if (Status == ControllerStatus.AwaitingTransition)
        {
            Status = ControllerStatus.Observing;
        }
        if (Status == ControllerStatus.Observing)
        {
            Detail = "Waiting for fresh screen evidence before acting";
        }
    }

    public ClickAction? Observe(Observation frame, ScreenEvidence evidence)
    {
        if (Status is ControllerStatus.Stopped or ControllerStatus.PausedForAttention)
        {
            return null;
        }

        if (frame.CapturedAt <= lastObservation)
        {
            return null;
        }

        if (lastWidth != 0 && (lastWidth != frame.Width || lastHeight != frame.Height))
        {
            stableFrames = 0;
            transitionPlanFrames = 0;
            exitFrames = 0;
            HomebaseClickConfirmed = false;
            questEntry.Invalidate();
        }
        lastWidth = frame.Width;
        lastHeight = frame.Height;
        if (frame.CapturedAt - lastObservation > NavigationTiming.ConfirmationGap(evidence))
        {
            stableFrames = 0;
        }

        if (frame.CapturedAt - lastObservation > TimeSpan.FromSeconds(10))
        {
            exitFrames = 0;
        }

        lastObservation = frame.CapturedAt;
        proposed = null;

        if (!questEntry.Active && !ErrorRecoveryOnly && !FinishRewardsOnly && !RunInProgress && !TargetExitRequested
                    && (Status == ControllerStatus.Observing && Pending is null
                        || Status == ControllerStatus.AwaitingTransition && QuestMenuEntry.IsEntryAction(Pending?.Step))
                    && frame.CapturedAt >= nextActionAt
                    && evidence.AllowsInput && QuestMenuEntry.CanTap(evidence.Kind)
                    && NavigationPlanner.ReliableVisual(frame, "player-health-bar") is not null)
        {
            questEntry.Joined(frame.CapturedAt);
        }

        if (questEntry.Active)
        {
            bool overlay = evidence.Kind is ScreenKind.SleepMode or ScreenKind.RewardClaim or ScreenKind.Rewards
                or ScreenKind.FeedbackSurvey or ScreenKind.NewUnlock;
            if (overlay)
            {
                questEntry.Invalidate();
            }

            if (!QuestMenuEntry.IsEntryScreen(evidence.Kind) && !overlay)
            {
                questEntry.Reset();
            }
            else if (QuestMenuEntry.IsEntryScreen(evidence.Kind)
                && evidence.Kind is not (ScreenKind.HomebaseReady or ScreenKind.HomebasePreparing))
            {

                Pending = null;
                suspendedPending = null;
                Status = ControllerStatus.Observing;
                if (questEntry.ReadyTarget(frame, evidence) is { } health && !ErrorRecoveryOnly && !RunInProgress && !FinishRewardsOnly)
                {

                    if (questEntry.AttemptsExhausted)
                    {
                        QuestMenuTimeouts++;
                        Pause($"The gameplay HUD remains visible after {QuestMenuEntry.MaximumAttempts} verified Tab attempts. Check the menu key binding and restart the runner.");
                        return null;
                    }
                    if (IsValidTarget(health, frame.Width, frame.Height))
                    {
                        return proposed = new(Guid.NewGuid(), "Open the quest menu with Tab", evidence.Kind,
                            health, frame.Width, frame.Height, frame.CapturedAt,
                            Step: NavigationStep.RetryQuestMenu, Key: NavigationKey.Tab);
                    }
                }
                Detail = "Waiting for the health HUD before Tab; checking for the Map menu or backpack";
                return null;
            }
        }
        if (evidence.Kind is not (ScreenKind.Unknown or ScreenKind.MissionMap))
        {
            HomebaseClickConfirmed = false;
        }

        if (Status == ControllerStatus.Observing && HomebaseClickConfirmed
                    && evidence.Kind == ScreenKind.MissionMap && !NavigationPlanner.HasSelectedHomebase(frame))
        {
            homebaseHeadingMissingSince ??= frame.CapturedAt;
            if (frame.CapturedAt - homebaseHeadingMissingSince.Value >= TimeSpan.FromSeconds(2))
            {
                HomebaseClickConfirmed = false;
                homebaseHeadingMissingSince = null;
                stableFrames = 0;
                Detail = "Homebase heading changed; reacquiring the homebase icon";
            }
        }
        else
        {
            homebaseHeadingMissingSince = null;
        }

        if (evidence.Kind is ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown)
        {
            RunInProgress = true;
            navigationActions = 0;
        }
        bool possibleExit = evidence.Kind is ScreenKind.Victory or ScreenKind.Defeat or ScreenKind.RewardClaim or ScreenKind.Rewards
            or ScreenKind.ModeDiscovery or ScreenKind.ModeSelection or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby
            or ScreenKind.WorldMap or ScreenKind.MissionMap || DiscoveryNavigation.IsOutOfMission(evidence.Kind);

        if (RunInProgress && possibleExit)
        {
            if (exitFrames == 0 || exitCandidate != evidence.Kind)
            {
                exitCandidate = evidence.Kind;
                exitSince = frame.CapturedAt;
                exitFrames = 1;
            }
            else
            {
                exitFrames++;
            }

            if (exitFrames >= 2 && frame.CapturedAt - exitSince >= TimeSpan.FromSeconds(1))
            {
                RunInProgress = false;
                navigationActions = 0;
                exitFrames = 0;
            }
        }
        else
        {
            exitFrames = 0;
        }

        if (Status == ControllerStatus.AwaitingTransition
                    && (Pending?.Step is NavigationStep.SkipFeedback or NavigationStep.ContinueUnlock
                        || evidence.Kind is ScreenKind.FeedbackSurvey or ScreenKind.NewUnlock))
        {
            if (Pending?.Step is NavigationStep.SkipFeedback or NavigationStep.ContinueUnlock
                && frame.CapturedAt - dispatchedAt < TimeSpan.FromSeconds(2))
            {
                return null;
            }

            Pending = null;
            suspendedPending = null;
            attempts = 0;
            stableFrames = 0;
            nextActionAt = default;
            Status = ControllerStatus.Observing;
        }
        if (evidence.Kind == ScreenKind.SleepMode && Status == ControllerStatus.AwaitingTransition
            && Pending is { Step: not NavigationStep.KeepPlaying })
        {
            suspendedPending = Pending;
            suspendedAttempts = attempts;
            Pending = null;
            attempts = 0;
            stableFrames = 0;
            nextActionAt = default;
            Status = ControllerStatus.Observing;
        }
        if (Status == ControllerStatus.AwaitingTransition && Pending?.Step is not null
            && (evidence.Kind is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention
                || evidence.Kind == ScreenKind.RejoinDialog && Pending.Step != NavigationStep.AbandonRejoin))
        {

            suspendedPending = null;
            Pending = null;
            attempts = 0;
            stableFrames = 0;
            nextActionAt = default;
            Status = ControllerStatus.Observing;
        }
        if (Status == ControllerStatus.AwaitingTransition && Pending is { } pending)
        {

            bool transitioned = pending.Step is not null ? NavigationPlanner.IsSatisfied(pending, frame, evidence)
                : evidence.Kind != ScreenKind.Unknown && evidence.Kind != pending.SourceScreen && IsExpectedDestination(pending.SourceScreen, evidence.Kind);
            if (transitioned)
            {

                var next = NavigationTiming.IsFastStep(pending.Step) && !evidence.UsesFuzzyMatching
                                    ? TargetExitRequested ? DecideNavigation(frame, evidence).Plan
                                        : NavigationPlanner.Decide(frame, evidence, Profile, HomebaseClickConfirmed || pending.Step == NavigationStep.SelectHomebase).Plan : null;
                if (next is not null && NavigationTiming.IsFastStep(next.Step))
                {
                    if (transitionPlanFrames > 0 && transitionPlan is { } prior && prior.Step == next.Step
                        && prior.Context == next.Context && prior.Target.IntersectionOverUnion(next.Target) > .5)
                    {
                        transitionPlanFrames++;
                    }
                    else
                    {
                        transitionPlanFrames = 1;
                        transitionPlanSince = frame.CapturedAt;
                    }
                    transitionPlan = next;
                }
                else
                {
                    transitionPlan = null;
                    transitionPlanFrames = 0;
                }
                if (previousScreen == evidence.Kind && stableFrames > 0)
                {
                    stableFrames++;
                }
                else
                {
                    previousScreen = evidence.Kind;
                    stableFrames = 1;
                    stableSince = frame.CapturedAt;
                }
                if (stableFrames >= 2 && frame.CapturedAt - stableSince >= NavigationTiming.Stability(pending.Step, evidence))
                {
                    if (pending.SourceScreen is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention)
                    {
                        ConfirmedDismissals++;
                        RunInProgress = false;
                    }
                    if (pending.Step == NavigationStep.ClaimReward)
                    {
                        ConfirmedRewardClaims++;
                    }

                    if (pending.Step == NavigationStep.AbandonRejoin)
                    {
                        ConfirmedAbandons++;
                        RunInProgress = false;
                    }
                    if (pending.Step == NavigationStep.KeepPlaying)
                    {
                        ConfirmedWakes++;
                    }

                    if (TargetWaveExit.IsExitStep(pending.Step) && TargetWaveExit.IsExited(evidence.Kind))
                    {
                        ConfirmedTargetExits++;
                        TargetExitRequested = false;
                        RunInProgress = false;
                    }
                    if (pending.Step == NavigationStep.SelectHomebase)
                    {
                        HomebaseClickConfirmed = evidence.Kind == ScreenKind.MissionMap;
                    }

                    if (pending.PauseAfterDismissal)
                    {
                        Pause("Error dismissed. Resolve the reported problem before resuming.");
                        return null;
                    }
                    if (pending.SourceScreen == ScreenKind.ErrorDialog)
                    {
                        recentErrors.Enqueue(frame.CapturedAt);
                        while (recentErrors.TryPeek(out var time) && frame.CapturedAt - time > TimeSpan.FromMinutes(10))
                        {
                            recentErrors.Dequeue();
                        }

                        if (recentErrors.Count >= 3)
                        {
                            Pause("Three error episodes within ten minutes. Check the game before resuming.");
                            return null;
                        }
                    }
                    if (pending.Step == NavigationStep.KeepPlaying && suspendedPending is { } interrupted)
                    {
                        Pending = interrupted;
                        suspendedPending = null;
                        attempts = suspendedAttempts;
                        dispatchedAt = frame.CapturedAt;
                        stableFrames = 0;
                        Status = ControllerStatus.AwaitingTransition;
                        Detail = "Sleep overlay closed; checking the interrupted action: " + interrupted.Description;
                        return null;
                    }
                    Pending = null;
                    attempts = 0;
                    stableFrames = 0;
                    nextActionAt = frame.CapturedAt + NavigationTiming.TransitionCooldown(pending.Step);
                    Status = ControllerStatus.Observing;
                    Detail = "Expected screen transition confirmed";
                    if (transitionPlan is not { } ready || transitionPlanFrames < 2
                        || frame.CapturedAt - transitionPlanSince < NavigationTiming.Stability(ready.Step, evidence))
                    {
                        return null;
                    }

                    previousScreen = evidence.Kind;
                    previousTarget = ready.Target;
                    previousStep = ready.Step;
                    previousContext = ready.Context;
                    stableSince = transitionPlanSince;
                    stableFrames = transitionPlanFrames - 1;
                }
            }
            else
            {
                stableFrames = 0;
                transitionPlanFrames = 0;
            }
            if (Pending is not null)
            {

                if (transitioned)
                {
                    return null;
                }

                var timeout = pending.Step is NavigationStep.SelectHomebase or NavigationStep.ReturnToHomebase ? TimeSpan.FromSeconds(2)
                    : pending.Step is NavigationStep.SelectSaveTheWorld or NavigationStep.EnterSaveTheWorld or NavigationStep.LaunchHomebase or NavigationStep.LaunchLoadedHomebase or NavigationStep.StartEndurance or NavigationStep.ReturnToHomebase or NavigationStep.LeaveTargetMission or NavigationStep.ConfirmTargetLeave
                    ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(12);
                if (frame.CapturedAt - dispatchedAt < timeout)
                {
                    return null;
                }

                if (attempts >= 2)
                {
                    Pause("The screen did not change after two verified input attempts.");
                    return null;
                }
                Pending = null;
                stableFrames = 0;
                Status = ControllerStatus.Observing;
                nextActionAt = frame.CapturedAt + (pending.Step == NavigationStep.SelectHomebase
                    ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(3));
                Detail = "No confirmed transition. Reacquiring the target before one retry.";
                return null;
            }
        }
        if (frame.CapturedAt < nextActionAt)
        {
            return null;
        }

        if (RunInProgress && possibleExit)
        {
            stableFrames = 0;
            Detail = "Confirming the run has left gameplay before navigating";
            return null;
        }
        if (FinishRewardsOnly && evidence.Kind is not (ScreenKind.RewardClaim or ScreenKind.Rewards or ScreenKind.Victory or ScreenKind.Defeat or ScreenKind.ErrorDialog or ScreenKind.NeedsAttention or ScreenKind.RejoinDialog or ScreenKind.SleepMode or ScreenKind.FeedbackSurvey or ScreenKind.NewUnlock))
        {
            stableFrames = 0;
            Detail = "Final attempt ended · waiting for reward cleanup and a stable menu";
            return null;
        }
        if (RunInProgress && !TargetExitRequested && evidence.Kind is ScreenKind.Homebase or ScreenKind.Backpack or ScreenKind.StormShieldConsole or ScreenKind.EnduranceSetup or ScreenKind.EnduranceStatus or ScreenKind.EnduranceRestartRequired)
        {
            stableFrames = 0;
            Detail = "Run is active; leaving the console alone until the run ends";
            return null;
        }

        string? label = evidence.Kind switch
        {
            ScreenKind.NeedsAttention => "Dismiss error and pause for attention",
            ScreenKind.ErrorDialog => "Dismiss recoverable error",
            _ => null
        };
        NavigationPlan? plan = null;
        if (label is null && (!ErrorRecoveryOnly || evidence.Kind is ScreenKind.RejoinDialog or ScreenKind.SleepMode || ClaimRewards && evidence.Kind is ScreenKind.RewardClaim or ScreenKind.Rewards or ScreenKind.Victory or ScreenKind.Defeat))
        {
            var decision = DecideNavigation(frame, evidence);
            if (decision.RequiresAttention)
            {
                Pause(decision.Detail);
                return null;
            }
            plan = decision.Plan;
            label = plan?.Description;
            if (plan is null)
            {
                stableFrames = 0;
                Detail = decision.Detail;
                return null;
            }
        }
        if (label is null)
        {
            stableFrames = 0;
            Detail = "Observing " + evidence.Kind;
            return null;
        }
        var target = plan?.Target ?? UniqueTarget(evidence.Targets);
        if (target is null)
        {
            stableFrames = 0;
            Detail = "Target is missing or ambiguous; no input sent";
            return null;
        }
        if (!IsValidTarget(target.Value, frame.Width, frame.Height))
        {
            stableFrames = 0;
            Detail = "Target is outside the captured game image";
            return null;
        }
        if (stableFrames > 0 && previousScreen == evidence.Kind && previousStep == plan?.Step && previousContext == plan?.Context
            && previousTarget is { } old && old.IntersectionOverUnion(target.Value) > .5)
        {
            stableFrames++;
        }
        else
        {
            previousScreen = evidence.Kind;
            previousTarget = target;
            previousStep = plan?.Step;
            previousContext = plan?.Context;
            stableFrames = 1;
            stableSince = frame.CapturedAt;
        }
        var minimumStableTime = evidence.Kind is ScreenKind.Victory or ScreenKind.Defeat ? TimeSpan.FromSeconds(1) : NavigationTiming.Stability(plan?.Step, evidence);
        if (stableFrames < 2 || frame.CapturedAt - stableSince < minimumStableTime)
        {
            Detail = "Confirming " + label;
            return null;
        }
        if (plan is not null && plan.Step is not (NavigationStep.ClaimReward or NavigationStep.KeepPlaying) && navigationActions >= 40)
        {
            Pause("Navigation exceeded 40 actions without reaching endurance. Inspect the current screen before resuming.");
            return null;
        }
        return proposed = new(Guid.NewGuid(), label, evidence.Kind, target.Value, frame.Width, frame.Height,
            frame.CapturedAt, evidence.Kind == ScreenKind.NeedsAttention, plan?.Step, plan?.Context, plan?.Key);
    }

    public static bool IsValidTarget(Box target, int width, int height) =>
        double.IsFinite(target.X) && double.IsFinite(target.Y) && double.IsFinite(target.Width) && double.IsFinite(target.Height)
        && target.Width > 0 && target.Height > 0 && target.X >= 0 && target.Y >= 0
        && target.Right <= width && target.Bottom <= height;

    public static Box? UniqueTarget(IReadOnlyList<TextRegion> targets)
    {
        var distinct = new List<Box>();
        foreach (var target in targets.OrderBy(r => r.Bounds.Width * r.Bounds.Height))
        {
            if (!distinct.Any(box => box.IntersectionOverUnion(target.Bounds) > .35))
            {
                distinct.Add(target.Bounds);
            }
        }

        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static bool IsExpectedDestination(ScreenKind source, ScreenKind destination) => source switch
    {
        ScreenKind.ErrorDialog or ScreenKind.NeedsAttention => destination is ScreenKind.ModeSelection or ScreenKind.ModeDiscovery or ScreenKind.RewardClaim
            or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.WorldMap or ScreenKind.MissionMap or ScreenKind.HomebaseReady
            or ScreenKind.HomebaseChoice or ScreenKind.Homebase or ScreenKind.Loading or ScreenKind.Rewards or ScreenKind.RejoinDialog
            || DiscoveryNavigation.IsOutOfMission(destination),
        ScreenKind.SaveTheWorldLobby => destination is ScreenKind.WorldMap or ScreenKind.MissionMap,
        ScreenKind.HomebaseReady => destination is ScreenKind.Loading or ScreenKind.Homebase,
        ScreenKind.EnduranceSetup => destination is ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown,
        _ => false
    };
}
