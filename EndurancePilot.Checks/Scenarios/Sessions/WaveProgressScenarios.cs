using EndurancePilot.Core;

internal static class WaveProgressScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        Observation Frame(double at, string heading, string timer, double scale = 1) => new(epoch.AddSeconds(at),
            (int)(1600 * scale), (int)(900 * scale),
            [new(heading, new(600 * scale, 50 * scale, 350 * scale, 20 * scale)),
                new(timer, new(750 * scale, 90 * scale, 80 * scale, 30 * scale))]);
        var active = Frame(0, "Endurance: Wave 16", "3:20");
        var next = Frame(2, "Endurance De nse: Wave 17 starts in:", "0:07");
        foreach (double scale in new[] { .8, 1, 1.5, 2 })
        {
            var scaled = Frame(0, "Endurance: Wave 16", "3:20", scale);
            check($"HUD clock scales with its wave label ({scale})", WaveHud.Read(scaled) is { Wave: 16, Starting: false, Remaining.TotalSeconds: 200 });
        }
        check("A partially covered Defense label retains explicit countdown semantics", recognizer.Recognize(next).Kind == ScreenKind.EnduranceCountdown
            && WaveHud.Read(next) is { Wave: 17, Starting: true, Remaining.TotalSeconds: 7 }
            && WaveReader.Read(next).Count == 0);
        check("Incoming announcement is not an active-wave reading", WaveReader.Read(Frame(0, "Wave 17 Incoming!", "3:55")).Count == 0);
        var inline = new Observation(epoch, 1600, 900, [new("[3:55] Endu nce: Wave 17", new(40, 150, 240, 20)),
            new("Wave 17", new(195, 150, 80, 20))]);
        check("Inline objective timer can corroborate an exact nearby wave number", WaveHud.Read(inline) is { Wave: 17, Starting: false, Remaining.TotalSeconds: 235 });
        check("Unrelated clock elsewhere cannot become the wave timer", WaveHud.Read(active with
            { Regions = [active.Regions[0], new("3:20", new(100, 700, 80, 30))] }) is null);
        check("Invalid timer seconds are rejected", WaveHud.Read(Frame(0, "Endurance: Wave 16", "3:67")) is null);
        check("Different nearby timer values remain ambiguous", WaveHud.Read(active with
            { Regions = [.. active.Regions, new("3:28", active.Regions[1].Bounds, "check")] }) is null);
        check("Repeated OCR of the same timer is not a conflict", WaveHud.Read(active with
            { Regions = [.. active.Regions, active.Regions[1] with { Pass = "check" }] })?.Remaining.TotalSeconds == 200);
        check("Conflicting wave digits cannot authorize a timer", WaveHud.Read(active with
            { Regions = [.. active.Regions, new("Endurance: Wave 18", new(600, 50, 350, 20))] }) is null);

        var progress = new WaveProgress();
        void Observe(Observation frame, RunRecord? run = null) => progress.Observe(frame, recognizer.Recognize(frame), run);
        Observe(active);
        check("One HUD sample cannot establish a live clock", progress.Snapshot(epoch) is null);
        Observe(Frame(1, "Endurance: Wave 16", "3:19"));
        check("The live clock counts down between fresh observations", progress.Snapshot(epoch.AddSeconds(3)) is { Wave: 16, Starting: false, Remaining.TotalSeconds: 197 });
        Observe(next);
        check("A phase change requires new confirming evidence", progress.Snapshot(epoch.AddSeconds(2)) is null);
        Observe(Frame(3, "Endurance Defense: Wave 17 starts in:", "0:06"));
        check("The next wave shows its own start countdown", progress.Snapshot(epoch.AddSeconds(4)) is { Wave: 17, Starting: true, Remaining.TotalSeconds: 5 });
        Observe(Frame(7, "Endurance Defense: Wave 17 starts in:", "0:02"));
        check("Countdown reaching zero never invents an active wave", progress.Snapshot(epoch.AddSeconds(9)) is { Wave: 17, Starting: true, Remaining.TotalSeconds: 0 });
        check("No frames hides the clock instead of extrapolating forever", progress.Snapshot(epoch.AddSeconds(13)) is null);
        Observe(Frame(14, "Endurance: Wave 17", "3:55"));
        Observe(Frame(15, "Endurance: Wave 17", "3:54"));
        check("New active HUD evidence replaces the start countdown", progress.Snapshot(epoch.AddSeconds(16)) is { Wave: 17, Starting: false, Remaining.TotalSeconds: 233 });
        Observe(Frame(16, "Endurance: Wave 17", "9:00"));
        check("A sudden OCR timer jump is not accepted", progress.Snapshot(epoch.AddSeconds(16))?.Remaining.TotalSeconds == 233);
        Observe(Frame(8, "Endurance: Wave 30", "9:00"));
        check("Out-of-order HUD samples cannot replace progress", progress.Snapshot(epoch.AddSeconds(16))?.Wave == 17);
        Observe(new(epoch.AddSeconds(17), 1600, 900, [new("VICTORY", new(100, 100, 300, 25)), new("TEAM SCORE", new(100, 200, 300, 25)), new("CONTINUE", new(100, 300, 300, 25))]));
        check("A result screen clears the live wave clock", progress.Snapshot(epoch.AddSeconds(17)) is null);

        var partial = Frame(20, "Wave 16", "3:20");
        progress.Reset();
        Observe(partial);
        Observe(partial with { CapturedAt = epoch.AddSeconds(21) });
        check("A generic wave and timer cannot establish an Endurance session", progress.Snapshot(epoch.AddSeconds(21)) is null);
        var run = new RunRecord(Guid.NewGuid(), epoch, null, 16, RunOutcome.InProgress);
        Observe(partial with { CapturedAt = epoch.AddSeconds(22) }, run);
        Observe(partial with { CapturedAt = epoch.AddSeconds(23) }, run);
        check("Covered Endurance label can supply display progress for a known run", progress.Snapshot(epoch.AddSeconds(23)) is { Wave: 16, Remaining.TotalSeconds: 200 });

        var runner = new RunnerSession();
        runner.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        runner.PauseControls();
        foreach (var frame in new[] { active, Frame(1, "Endurance: Wave 16", "3:19"), next, Frame(3, "Endurance Defense: Wave 17 starts in:", "0:06") })
            check("Paused background HUD updates never propose input", runner.ObserveBackground(frame, recognizer.Recognize(frame)).Action is null);
        check("Upcoming wave does not increase confirmed run progress", runner.Tracker.ConfirmedWave == 16
            && runner.WaveProgress.Snapshot(epoch.AddSeconds(3)) is { Wave: 17, Starting: true } && runner.ControlsPaused);
        runner.Stop("Stop");
        check("Stop clears the live clock", runner.WaveProgress.Snapshot(epoch.AddSeconds(3)) is null);
    }
}
