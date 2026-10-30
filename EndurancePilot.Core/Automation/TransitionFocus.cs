namespace EndurancePilot.Core;

public enum TransitionReason { Wake, NextRun }
public sealed record TransitionRequest(TransitionReason Reason, DateTimeOffset EvidenceAt);

// One foreground request per observed episode. Losing focus never authorizes
// another attempt; the user can always interrupt by switching applications.
public sealed class TransitionFocus
{
    public TransitionRequest? Active { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    private DateTimeOffset handledSleep, handledResult;
    private bool startSent;
    public void Reset() { Cancel(); handledSleep = handledResult = default; }
    public void Cancel() { Active = null; startSent = false; }
    public TransitionRequest? Request(bool enabled, bool running, bool paused, bool attention, bool foreground,
        bool canRepeat, DateTimeOffset sessionStarted, GameLogPlaytime? playtime, DateTimeOffset? resultAt, DateTimeOffset now, bool inputHeld = false)
    {
        if (!enabled || !running || paused || attention || foreground || inputHeld || Active is not null) return null;
        TransitionRequest? request = null;
        if (canRepeat && resultAt is { } result && result >= sessionStarted && result > handledResult
            && result <= now && now - result <= TimeSpan.FromMinutes(2))
        {
            handledResult = result;
            if (playtime is { NeedsKeepPlaying: true }) handledSleep = playtime.WakeEventAt;
            request = new(TransitionReason.NextRun, result);
        }
        else if (playtime is { NeedsKeepPlaying: true } sleep && sleep.WakeEventAt > handledSleep
            && sleep.WakeEventAt <= now && sleep.ChangedAt <= now)
        {
            handledSleep = sleep.WakeEventAt;
            request = new(TransitionReason.Wake, sleep.WakeEventAt);
        }
        if (request is null) return null;
        Active = request;
        StartedAt = now;
        return request;
    }
    public void Dispatched(NavigationStep? step)
    {
        startSent |= step == NavigationStep.StartEndurance;
    }
    public void ObserveLogWave(GameLogWave? wave)
    {
        if (Active is { Reason: TransitionReason.NextRun } request && wave is { Wave: 1 } && wave.StartedAt > request.EvidenceAt)
            startSent = true;
    }
    public bool Finished(ScreenKind screen) => Active?.Reason switch
    {
        TransitionReason.Wake => screen is not (ScreenKind.SleepMode or ScreenKind.Unknown or ScreenKind.Loading),
        TransitionReason.NextRun => startSent && screen is ScreenKind.EnduranceCountdown or ScreenKind.ActiveEndurance,
        _ => false
    };
    public bool Expired(DateTimeOffset now) => Active is not null && now - StartedAt > TimeSpan.FromMinutes(5);
    public bool AllowsAction(NavigationStep? step) => Active?.Reason != TransitionReason.Wake || step == NavigationStep.KeepPlaying;
}
