namespace EndurancePilot.Core;

public enum RunOutcome
{
    InProgress, Completed30, Failed, Interrupted, UnverifiedVictory, ReachedTarget
}
public sealed record RunRecord(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int HighestWave, RunOutcome Outcome);

public sealed class RunTracker
{
    public RunRecord? Current
    {
        get; private set;
    }
    public int ConfirmedWave => Current?.HighestWave ?? 0;
    public DateTimeOffset LastConfirmedWaveAt
    {
        get; private set;
    }
    public string Detail { get; private set; } = "Waiting for an endurance run";
    private int candidate;
    private int hits;
    private DateTimeOffset candidateSince;
    private DateTimeOffset lastFrame;
    private ScreenKind resultCandidate;
    private DateTimeOffset resultSince;
    private int resultHits;
    private DateTimeOffset lastLogWave;

    public bool ObserveLogWave(GameLogWave wave, DateTimeOffset now)
    {
        if (wave.Wave is < 1 or > 30 || wave.StartedAt <= lastLogWave || wave.StartedAt > now
            || now - wave.StartedAt > TimeSpan.FromMinutes(20)
            || Current?.EndedAt >= wave.StartedAt) return false;
        lastLogWave = wave.StartedAt;
        if (Current is null || Current.Outcome != RunOutcome.InProgress)
            Current = new(Guid.NewGuid(), wave.StartedAt, null, wave.Wave, RunOutcome.InProgress);
        else if (wave.Wave > Current.HighestWave)
            Current = Current with { HighestWave = wave.Wave };
        else return false;
        if (wave.StartedAt > LastConfirmedWaveAt) LastConfirmedWaveAt = wave.StartedAt;
        Detail = $"Wave {Current.HighestWave} reported by Fortnite; awaiting a confirmed result";
        return true;
    }

    public RunRecord? Observe(Observation frame, ScreenEvidence screen)
    {
        if (frame.CapturedAt <= lastFrame)
        {
            return null;
        }

        if (frame.CapturedAt - lastFrame > TimeSpan.FromSeconds(10))
        {
            hits = 0;
            resultHits = 0;
        }
        lastFrame = frame.CapturedAt;
        if (screen.Kind == ScreenKind.EnduranceCountdown)
        {
            hits = 0;
            resultHits = 0;
            var upcoming = WaveReader.ReadCountdown(frame).Select(r => r.Wave).Distinct().ToArray();
            Detail = upcoming.Length == 1 ? $"Waiting for wave {upcoming[0]} to start · last confirmed wave {ConfirmedWave}" : "Between waves · countdown readings disagree";
            return null;
        }
        if (screen.Kind == ScreenKind.ActiveEndurance)
        {
            resultHits = 0;
            var waves = WaveReader.Read(frame).Select(r => r.Wave).Distinct().ToArray();
            if (waves.Length != 1)
            {
                hits = 0;
                Detail = "Wave indicators disagree; retaining last confirmed wave";
                return null;
            }
            int wave = waves[0];
            if (wave != candidate || hits == 0)
            {
                candidate = wave;
                hits = 1;
                candidateSince = frame.CapturedAt;
            }
            else
            {
                hits++;
            }

            var jump = Current is { Outcome: RunOutcome.InProgress } && wave > Current.HighestWave + 1;
            int requiredHits = jump ? 4 : 2;
            var requiredTime = TimeSpan.FromSeconds(jump ? 6 : 1);
            if (hits < requiredHits || frame.CapturedAt - candidateSince < requiredTime)
            {
                return null;
            }

            if (Current is null || Current.Outcome != RunOutcome.InProgress)
            {
                Current = new(Guid.NewGuid(), frame.CapturedAt, null, wave, RunOutcome.InProgress);
            }
            else if (wave > Current.HighestWave)
            {
                Current = Current with
                {
                    HighestWave = wave
                };
            }
            else if (wave < Current.HighestWave)
            {
                Detail = "Lower wave reading rejected; run identity requires a confirmed restart";
                return null;
            }
            Detail = $"Wave {Current.HighestWave} observed; its completion is not yet proven";
            LastConfirmedWaveAt = frame.CapturedAt;
        }
        else
        {
            hits = 0;
            if (Current is not { Outcome: RunOutcome.InProgress })
            {
                return null;
            }

            bool returnedToMenu = screen.Kind is ScreenKind.ModeDiscovery or ScreenKind.ModeSelection
                            or ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.WorldMap or ScreenKind.MissionMap
                            || DiscoveryNavigation.IsOutOfMission(screen.Kind);
            if (!returnedToMenu && screen.Kind is not (ScreenKind.Victory or ScreenKind.Defeat or ScreenKind.ErrorDialog or ScreenKind.NeedsAttention))
            {
                resultHits = 0;
                return null;
            }
            if (screen.Kind != resultCandidate || resultHits == 0)
            {
                resultCandidate = screen.Kind;
                resultHits = 1;
                resultSince = frame.CapturedAt;
                return null;
            }
            resultHits++;
            if (resultHits < 2 || frame.CapturedAt - resultSince < TimeSpan.FromSeconds(1))
            {
                return null;
            }

            var outcome = screen.Kind switch
            {
                ScreenKind.Victory when Current.HighestWave == 30 => RunOutcome.Completed30,
                ScreenKind.Victory => RunOutcome.UnverifiedVictory,
                ScreenKind.Defeat => RunOutcome.Failed,
                _ => RunOutcome.Interrupted
            };
            Current = Current with
            {
                Outcome = outcome,
                EndedAt = frame.CapturedAt
            };
            Detail = returnedToMenu ? "Returned to menus without a confirmed result; run interrupted"
                : outcome == RunOutcome.Completed30 ? "Wave 30 and subsequent victory confirmed" : $"Run ended: {outcome}";
            return Current;
        }
        return null;
    }

    public RunRecord? CompleteTarget(DateTimeOffset now, int targetWave)
    {
        if (targetWave is < 1 or >= 30 || Current is not { Outcome: RunOutcome.InProgress } || Current.HighestWave <= targetWave)
        {
            return null;
        }

        Current = Current with
        {
            Outcome = RunOutcome.ReachedTarget,
            EndedAt = now
        };
        Detail = $"Wave {targetWave} finished; wave {Current.HighestWave} started and mission exit confirmed";
        return Current;
    }

    public RunRecord? Interrupt(DateTimeOffset now, string reason)
    {
        if (Current is not { Outcome: RunOutcome.InProgress })
        {
            return null;
        }

        Current = Current with
        {
            Outcome = RunOutcome.Interrupted,
            EndedAt = now
        };
        Detail = reason;
        return Current;
    }
}
