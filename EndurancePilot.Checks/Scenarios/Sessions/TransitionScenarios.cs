using EndurancePilot.Core;

internal static class TransitionScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var sleep = new GameLogPlaytime(true, epoch.AddSeconds(1));
        TransitionRequest? Request(TransitionFocus focus, bool enabled = true, bool running = true, bool paused = false,
            bool attention = false, bool foreground = false, bool repeat = true, GameLogPlaytime? playtime = null,
            DateTimeOffset? result = null, DateTimeOffset? now = null, DateTimeOffset? started = null) =>
            focus.Request(enabled, running, paused, attention, foreground, repeat, started ?? epoch,
                playtime, result, now ?? epoch.AddSeconds(2));
        foreach (var block in new[] { "disabled", "stopped", "paused", "attention", "foreground" })
            check("Automatic focus blocked: " + block, Request(new(), enabled: block != "disabled", running: block != "stopped",
                paused: block == "paused", attention: block == "attention", foreground: block == "foreground", playtime: sleep) is null);
        var focus = new TransitionFocus();
        check("A current sleep episode permits one wake request", Request(focus, playtime: sleep)?.Reason == TransitionReason.Wake);
        check("Wake scope permits only Keep Playing", focus.AllowsAction(NavigationStep.KeepPlaying)
            && !focus.AllowsAction(NavigationStep.StartEndurance) && !focus.AllowsAction(NavigationStep.OpenMap));
        check("Unknown or sleeping screens cannot finish a wake", !focus.Finished(ScreenKind.Unknown) && !focus.Finished(ScreenKind.SleepMode));
        check("Fresh awake evidence ends the wake scope", focus.Finished(ScreenKind.ActiveEndurance));
        check("An active takeover cannot request another focus", Request(focus, playtime: sleep) is null);
        focus.Cancel();
        check("Switching away does not trigger repeated focus theft", Request(focus, playtime: sleep) is null);
        check("A new sleep episode can be handled", Request(focus, playtime: new(true, epoch.AddSeconds(3)), now: epoch.AddSeconds(4))?.Reason == TransitionReason.Wake);
        check("A takeover has a bounded lifetime", focus.Expired(epoch.AddMinutes(6)));
        focus.Reset();
        check("An explicitly restarted session can retry", Request(focus, playtime: sleep) is not null);
        check("An awake log state does not request focus", Request(new(), playtime: new(false, epoch.AddSeconds(1))) is null);
        check("Held input postpones automatic focus", new TransitionFocus().Request(true, true, false, false, false, true,
            epoch, sleep, null, epoch.AddSeconds(2), inputHeld: true) is null);
        check("A current-process sleep state persists until an awake transition", Request(new(), playtime: sleep, now: epoch.AddHours(1)) is not null);
        check("Future sleep state cannot trigger focus", Request(new(), playtime: new(true, epoch.AddHours(1))) is null);
        check("Attempt limits block automatic next-run focus", Request(new(), repeat: false, result: epoch.AddSeconds(1)) is null);
        check("Old results cannot start an unexpected repeat", Request(new(), result: epoch.AddSeconds(-1)) is null
            && Request(new(), result: epoch.AddSeconds(1), now: epoch.AddMinutes(3)) is null);
        focus = new();
        check("A fresh result hint can request next-run handling", Request(focus, result: epoch.AddSeconds(1))?.Reason == TransitionReason.NextRun);
        check("Current gameplay is not proof of starting the next run", !focus.Finished(ScreenKind.ActiveEndurance));
        focus.Dispatched(NavigationStep.StartEndurance);
        check("Verified start followed by countdown finishes next-run handling", focus.Finished(ScreenKind.EnduranceCountdown));
        focus.Cancel();
        check("The same result does not launch repeatedly", Request(focus, result: epoch.AddSeconds(1)) is null);
        focus = new();
        Request(focus, result: epoch.AddSeconds(1), playtime: sleep);
        focus.Cancel();
        check("Cancelling result handling also cancels its sleep episode", Request(focus, playtime: sleep) is null);
        focus = new();
        Request(focus, result: epoch.AddSeconds(1));
        focus.ObserveLogWave(new(1, epoch));
        check("An old wave-one record cannot finish a new-run handoff", !focus.Finished(ScreenKind.ActiveEndurance));
        focus.ObserveLogWave(new(1, epoch.AddSeconds(3)));
        check("A later wave-one log plus gameplay can finish the handoff", focus.Finished(ScreenKind.ActiveEndurance));

        var parser = new GameLogProgress(epoch);
        string Line(int second, string message) => $"[{epoch.AddSeconds(second):yyyy.MM.dd-HH.mm.ss:fff}][123]{message}";
        var now = epoch.AddMinutes(1);
        parser.Observe(Line(1, "LogFortPlaytimeManager: [394e] Changing Playtime State from Idle to Sleep"), now);
        check("Exact log transition detects sleep without an image", parser.Playtime is { Sleeping: true });
        parser.Observe(Line(2, "LogFortPlaytimeManager: [394e] Changing Playtime State from Sleep to Unblocked"), now);
        check("Wake log transition clears sleep status", parser.Playtime is { Sleeping: false });
        var episodeParser = new GameLogProgress(epoch);
        episodeParser.Observe(Line(1, "LogFortPlaytimeManager: [394e] Changing Playtime State from Unblocked to Idle"), now);
        focus = new();
        check("The inactivity warning can be handled before sleep", Request(focus, playtime: episodeParser.Playtime)?.Reason == TransitionReason.Wake);
        focus.Cancel();
        episodeParser.Observe(Line(31, "LogFortPlaytimeManager: [394e] Changing Playtime State from Idle to Sleep"), now);
        check("Warning and full sleep are one focus episode", Request(focus, playtime: episodeParser.Playtime, now: epoch.AddSeconds(32)) is null);
        const string resultMessage = "LogFortMovie: UFortMovieWidget::SetMediaSource(FMS_OutpostVictory)  SourceOpenSuccess 1";
        parser.Observe(Line(3, resultMessage), now);
        check("A result hint without a current wave is ignored", parser.ResultAvailableAt is null);
        parser.Observe(Line(4, "LogHealthSnapshot: ======= Snapshot: Start of Wave 30 (FortGameStateOutpost, Difficulty 66.90) ======="), now);
        parser.Observe(Line(5, resultMessage.Replace("Success 1", "Success 0")), now);
        check("Failed media loading does not establish a result hint", parser.ResultAvailableAt is null);
        parser.Observe(Line(6, resultMessage), now);
        check("Current post-run media queues screen inspection", parser.ResultAvailableAt == epoch.AddSeconds(6) && parser.Latest?.Wave == 30);
        parser.Observe(Line(7, resultMessage), now);
        check("Duplicate result hints keep one episode identity", parser.ResultAvailableAt == epoch.AddSeconds(6));
        parser.Observe(Line(8, "LogLoad: LoadMap: /Game/Maps/Frontend"), now);
        check("Travel clears the result handoff", parser.ResultAvailableAt is null);

        var runner = new RunnerSession();
        runner.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), null, epoch);
        runner.PauseControls();
        check("Log waves update paused run tracking without an action", runner.ObserveLogWave(new(30, epoch.AddSeconds(1)), epoch.AddSeconds(2))
            && runner.Tracker.ConfirmedWave == 30 && runner.Progress.AttemptsEnded == 0 && runner.ControlsPaused);
        var recognizer = new ScreenRecognizer();
        Observation Victory(int seconds) => new(epoch.AddSeconds(seconds), 1920, 1080,
            [new("VICTORY", new(100, 100, 350, 30)), new("TEAM SCORE", new(100, 200, 350, 30)), new("CONTINUE", new(100, 300, 350, 30))]);
        var first = Victory(3);
        runner.ObserveBackground(first, recognizer.Recognize(first));
        check("A single result frame cannot complete a log-tracked run", runner.Progress.CompletedRuns == 0);
        var second = Victory(5);
        runner.ObserveBackground(second, recognizer.Recognize(second));
        check("Log wave thirty plus stable victory completes exactly once", runner.Progress.CompletedRuns == 1 && runner.IsRunning);
        runner.ObserveBackground(Victory(7), recognizer.Recognize(second));
        check("Repeated result observations cannot double-count", runner.Progress.CompletedRuns == 1);
        check("Old wave events cannot resurrect a completed run", !runner.ObserveLogWave(new(30, epoch.AddSeconds(1)), epoch.AddSeconds(8)));
        check("The next run begins from a newer log event", runner.ObserveLogWave(new(1, epoch.AddSeconds(9)), epoch.AddSeconds(10))
            && runner.Tracker.Current is { Outcome: RunOutcome.InProgress, HighestWave: 1 });
        runner.Stop("Stop");
        check("Stopped sessions ignore log progress", !runner.ObserveLogWave(new(2, epoch.AddSeconds(11)), epoch.AddSeconds(12)));
        runner = new();
        runner.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch.AddSeconds(20));
        check("A result from before Start cannot spend a new session attempt", !runner.ObserveLogWave(new(30, epoch.AddSeconds(1)),
            epoch.AddSeconds(21), resultAt: epoch.AddSeconds(10)) && runner.Tracker.Current is null);
    }
}
