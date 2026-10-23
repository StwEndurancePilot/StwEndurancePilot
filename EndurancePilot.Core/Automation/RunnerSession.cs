namespace EndurancePilot.Core;

public enum RunnerMode
{
    Observe, Recovery, Runner
}
public sealed record SessionUpdate(ClickAction? Action, RunRecord? Outcome, bool Complete);

public sealed class RunnerSession
{
    public RunTracker Tracker { get; } = new();
    public MissionRewardTracker Rewards { get; } = new();
    public AutomationController Controller { get; } = new();
    public SessionProgress Progress { get; private set; } = new(1);
    public RunnerMode Mode
    {
        get; private set;
    }
    public bool IsRunning
    {
        get; private set;
    }
    public DateTimeOffset? StartedAt
    {
        get; private set;
    }

    public void Start(RunnerMode mode, RunnerProfile profile, int? attemptLimit, DateTimeOffset now)
    {
        var progress = new SessionProgress(attemptLimit);
        if (profile.TargetWave is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(profile.TargetWave));
        }

        if (mode == RunnerMode.Runner && (profile.Zone == HomebaseZone.Unspecified || !Enum.IsDefined(profile.Zone) || !profile.AfkBaseConfirmed))
        {
            throw new InvalidOperationException("Choose your homebase and confirm that the base is ready to run AFK.");
        }

        Controller.Stop();
        Rewards.Reset();
        Mode = mode;
        Progress = progress;
        StartedAt = now;
        IsRunning = true;
        Controller.Profile = profile;
        Controller.ErrorRecoveryOnly = mode != RunnerMode.Runner;
        Controller.ClaimRewards = mode != RunnerMode.Observe;
        Controller.FinishRewardsOnly = false;
        if (mode != RunnerMode.Observe)
        {
            Controller.Start(Tracker.Current is { Outcome: RunOutcome.InProgress });
        }
    }

    public SessionUpdate Observe(Observation frame, ScreenEvidence evidence)
    {
        if (!IsRunning)
        {
            return new(null, null, false);
        }

        bool abandoning = Controller.Pending?.Step == NavigationStep.AbandonRejoin;
        bool dismissingError = Mode != RunnerMode.Observe && (evidence.Kind is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention
            || Controller.AwaitingErrorDismissal);
        bool waking = evidence.Kind == ScreenKind.SleepMode || Controller.Pending?.Step == NavigationStep.KeepPlaying;
        var outcome = abandoning || evidence.Kind == ScreenKind.RejoinDialog || dismissingError || waking || Controller.AwaitingTargetExit ? null : Tracker.Observe(frame, evidence);
        if (outcome is not null)
        {
            Record(outcome);
        }

        Rewards.Observe(frame, evidence, Tracker.Current);
        Controller.WaitForRewardRead = !Rewards.CanCollect;
        if (Mode == RunnerMode.Runner && Progress.Phase == SessionPhase.Complete)
        {
            Stop("Attempt limit reached");
            return new(null, outcome, true);
        }
        int abandonedBefore = Controller.ConfirmedAbandons;
        int dismissedBefore = Controller.ConfirmedDismissals;
        int targetExitsBefore = Controller.ConfirmedTargetExits;
        if (Mode == RunnerMode.Runner && Progress.Phase == SessionPhase.Running
            && evidence.Kind == ScreenKind.EnduranceRestartRequired)
        {
            Controller.RequestTargetExit("Endurance requires a new match · leaving Storm Shield");
        }
        if (Mode == RunnerMode.Runner && Controller.Profile.EndRunWhenTargetReached && Controller.Profile.TargetWave < 30 && evidence.Kind == ScreenKind.ActiveEndurance
            && Tracker.Current is { Outcome: RunOutcome.InProgress } run && run.HighestWave > Controller.Profile.TargetWave
            && Tracker.LastConfirmedWaveAt == frame.CapturedAt)
        {
            Controller.RequestTargetExit();
        }

        var action = Controller.Observe(frame, evidence);
        if (Controller.ConfirmedTargetExits > targetExitsBefore)
        {
            outcome = Tracker.CompleteTarget(frame.CapturedAt, Controller.Profile.TargetWave)
                ?? Tracker.Interrupt(frame.CapturedAt, "Left the previous Homebase match to restart Endurance");
            if (outcome is not null)
            {
                Record(outcome);
            }

            action = null;
        }
        else if (Controller.ConfirmedAbandons > abandonedBefore)
        {
            outcome = Tracker.Interrupt(frame.CapturedAt, "Abandoned disconnected session from Rejoin dialog");
            if (outcome is not null)
            {
                Record(outcome);
            }
        }
        else if (Controller.ConfirmedDismissals > dismissedBefore && evidence.Kind != ScreenKind.RejoinDialog)
        {

            outcome = Tracker.Interrupt(frame.CapturedAt, "Error dismissed after interrupted game session");
            if (outcome is not null)
            {
                Record(outcome);
            }
        }
        if (Mode == RunnerMode.Runner)
        {
            Progress.Observe(frame, evidence, Controller.Pending is not null);
            if (Progress.Phase == SessionPhase.Complete)
            {
                Stop("Attempt limit reached after rewards");
                return new(null, outcome, true);
            }
        }
        return new(action, outcome, false);
    }

    public SessionUpdate ObserveBackground(Observation frame, ScreenEvidence evidence)
    {
        if (!IsRunning)
        {
            return new(null, null, false);
        }

        Controller.InvalidateObservation();
        Rewards.Observe(frame, evidence, Tracker.Current);
        if (!evidence.AllowsInput || evidence.Kind is not (ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown or ScreenKind.Victory or ScreenKind.Defeat))
        {
            return new(null, null, false);
        }

        var outcome = Tracker.Observe(frame, evidence);
        if (outcome is not null)
        {
            Record(outcome);
        }

        if (Mode == RunnerMode.Runner && Controller.Profile.EndRunWhenTargetReached && Controller.Profile.TargetWave < 30 && evidence.Kind == ScreenKind.ActiveEndurance
            && Tracker.Current is { Outcome: RunOutcome.InProgress } run && run.HighestWave > Controller.Profile.TargetWave
            && Tracker.LastConfirmedWaveAt == frame.CapturedAt)
        {
            Controller.RequestTargetExit();
        }

        if (Mode == RunnerMode.Runner && Progress.Phase == SessionPhase.Complete)
        {
            Stop("Attempt limit reached");
            return new(null, outcome, true);
        }
        return new(null, outcome, false);
    }

    public RunRecord? Interrupt(DateTimeOffset now, string reason)
    {
        if (!IsRunning)
        {
            return null;
        }

        var outcome = Tracker.Interrupt(now, reason);
        if (outcome is not null)
        {
            Record(outcome);
        }

        if (Mode == RunnerMode.Runner && Progress.Phase == SessionPhase.Complete)
        {
            Stop(reason);
        }

        return outcome;
    }

    public RunRecord? RecoverAfterCrash(DateTimeOffset now) => RecoverAfterProcessExit(now, "Fortnite exited after a native crash dialog");

    public RunRecord? RecoverAfterProcessExit(DateTimeOffset now, string reason = "Fortnite process exited", bool resumeNavigation = true)
    {
        if (!IsRunning || Mode != RunnerMode.Runner)
        {
            return null;
        }

        var outcome = Interrupt(now, reason);

        if (IsRunning && resumeNavigation)
        {
            Controller.Start();
        }

        return outcome;
    }

    private void Record(RunRecord outcome)
    {
        Controller.RecordRunOutcome(outcome);
        if (Mode != RunnerMode.Runner)
        {
            return;
        }

        Progress.Record(outcome);
        Controller.FinishRewardsOnly = Progress.Phase == SessionPhase.FinishingRewards;
    }

    public void Stop(string reason)
    {
        IsRunning = false;
        Controller.Stop(reason);
    }
}
