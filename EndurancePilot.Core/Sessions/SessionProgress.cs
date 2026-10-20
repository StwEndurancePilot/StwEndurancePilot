namespace EndurancePilot.Core;

public enum SessionPhase
{
    Running, FinishingRewards, Complete
}

public sealed class SessionProgress
{

    public int? AttemptLimit
    {
        get;
    }
    public int AttemptsEnded => outcomes.Count;
    public int CompletedRuns
    {
        get; private set;
    }
    public int FailedRuns
    {
        get; private set;
    }
    public int InterruptedRuns
    {
        get; private set;
    }
    public int UnverifiedRuns
    {
        get; private set;
    }
    public SessionPhase Phase
    {
        get; private set;
    }
    public RunRecord? LastOutcome
    {
        get; private set;
    }
    private readonly HashSet<Guid> outcomes = [];
    private ScreenKind previousMenu;
    private int menuFrames;
    private DateTimeOffset menuSince;
    private DateTimeOffset lastObservation;

    public SessionProgress(int? attemptLimit)
    {
        if (attemptLimit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptLimit));
        }

        AttemptLimit = attemptLimit;
    }

    public void Record(RunRecord record)
    {
        if (record.Outcome == RunOutcome.InProgress || record.EndedAt is null || !outcomes.Add(record.Id))
        {
            return;
        }

        LastOutcome = record;
        switch (record.Outcome)
        {
            case RunOutcome.Completed30:
            case RunOutcome.ReachedTarget:
                CompletedRuns++;
                break;
            case RunOutcome.Failed:
                FailedRuns++;
                break;
            case RunOutcome.Interrupted:
                InterruptedRuns++;
                break;
            case RunOutcome.UnverifiedVictory:
                UnverifiedRuns++;
                break;
        }
        if (AttemptLimit is { } limit && AttemptsEnded >= limit)
        {
            Phase = record.Outcome == RunOutcome.Interrupted ? SessionPhase.Complete : SessionPhase.FinishingRewards;
        }
    }

    public void Observe(Observation frame, ScreenEvidence screen, bool awaitingInputResult)
    {
        if (Phase != SessionPhase.FinishingRewards || frame.CapturedAt <= lastObservation)
        {
            return;
        }

        var menuGap = screen.Kind == ScreenKind.SaveTheWorldHub && screen.AllowsInput
            ? NavigationTiming.HealthProbeMaximumGap : TimeSpan.FromSeconds(10);
        if (frame.CapturedAt - lastObservation > menuGap)
        {
            menuFrames = 0;
        }

        lastObservation = frame.CapturedAt;
        bool menu = screen.Kind is ScreenKind.ModeDiscovery or ScreenKind.ModeSelection or ScreenKind.SaveTheWorldHub
            or ScreenKind.SaveTheWorldLobby or ScreenKind.WorldMap or ScreenKind.MissionMap || DiscoveryNavigation.IsOutOfMission(screen.Kind);
        if (!menu || awaitingInputResult)
        {
            menuFrames = 0;
            return;
        }
        if (menuFrames == 0 || previousMenu != screen.Kind)
        {
            previousMenu = screen.Kind;
            menuSince = frame.CapturedAt;
            menuFrames = 1;
            return;
        }
        menuFrames++;
        if (menuFrames >= 2 && frame.CapturedAt - menuSince >= TimeSpan.FromSeconds(1))
        {
            Phase = SessionPhase.Complete;
        }
    }
}
