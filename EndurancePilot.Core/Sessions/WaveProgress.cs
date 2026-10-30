namespace EndurancePilot.Core;

public sealed record WaveProgressSnapshot(int Wave, bool Starting, TimeSpan Remaining, DateTimeOffset ObservedAt);

// Presentation evidence is separate from confirmed run outcomes and input decisions.
// A covered Endurance label can still supply a clock for an already tracked run.
public sealed class WaveProgress
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(5);
    private WaveHudReading? candidate;
    private DateTimeOffset candidateAt, lastFrame;
    private WaveProgressSnapshot? confirmed;

    public void Reset()
    {
        candidate = null;
        confirmed = null;
        candidateAt = lastFrame = default;
    }

    public void Observe(Observation frame, ScreenEvidence evidence, RunRecord? run)
    {
        if (frame.CapturedAt <= lastFrame) return;
        lastFrame = frame.CapturedAt;
        var reading = WaveHud.Read(frame);
        bool knownScreen = evidence.AllowsInput && evidence.Kind is ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown;
        bool coveredLabel = evidence.Kind == ScreenKind.Unknown && run is { Outcome: RunOutcome.InProgress }
            && reading is { Starting: false } && reading.Wave >= run.HighestWave && reading.Wave <= run.HighestWave + 1;
        if (reading is null || (!knownScreen && !coveredLabel))
        {
            candidate = null;
            if (evidence.Kind is not (ScreenKind.Unknown or ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown)) confirmed = null;
            return;
        }
        var gap = frame.CapturedAt - candidateAt;
        if (candidate is not { } previous || previous.Wave != reading.Wave || previous.Starting != reading.Starting
            || gap > MaximumAge || reading.Remaining > previous.Remaining + TimeSpan.FromSeconds(1)
            || previous.Remaining - reading.Remaining > gap + TimeSpan.FromSeconds(2))
        {
            candidate = reading;
            candidateAt = frame.CapturedAt;
            if (confirmed?.Wave != reading.Wave || confirmed.Starting != reading.Starting) confirmed = null;
            return;
        }
        if (gap < TimeSpan.FromMilliseconds(250)) return;
        confirmed = new(reading.Wave, reading.Starting, reading.Remaining, frame.CapturedAt);
        candidate = reading;
        candidateAt = frame.CapturedAt;
    }

    public WaveProgressSnapshot? Snapshot(DateTimeOffset now)
    {
        if (confirmed is not { } current || now < current.ObservedAt || now - current.ObservedAt > MaximumAge) return null;
        // Smooth the clock between captured frames. Zero never changes the phase;
        // only a new game observation can establish the next active wave.
        double seconds = Math.Max(0, Math.Ceiling((current.Remaining - (now - current.ObservedAt)).TotalSeconds));
        return current with { Remaining = TimeSpan.FromSeconds(seconds) };
    }
}
