using EndurancePilot.Core;

internal static class ControlPauseScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var session = new RunnerSession();
        var recognizer = new ScreenRecognizer();
        double seconds = 0;
        Observation Frame(params string[] text) => new(epoch.AddSeconds(seconds += 2), 1920, 1080,
            text.Select((t, i) => new TextRegion(t, new(100, 100 + i * 60, 350, 30))).ToArray());
        SessionUpdate Feed(bool background, params string[] text)
        {
            var frame = Frame(text);
            return background ? session.ObserveBackground(frame, recognizer.Recognize(frame))
                : session.Observe(frame, recognizer.Recognize(frame));
        }
        ClickAction Propose()
        {
            ClickAction? action = null;
            for (int i = 0; i < 5 && action is null; i++) action = Feed(false, "MAP", "COMMAND", "ARMORY").Action;
            check("Fresh stable lobby proposes map navigation", action?.Step == NavigationStep.OpenMap);
            return action!;
        }
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 2, epoch);
        var proposed = Propose();
        int generation = session.InputGeneration;
        var progress = session.Progress;
        session.PauseControls();
        check("Manual pause preserves the running session and its progress", session.IsRunning && session.ControlsPaused
            && ReferenceEquals(progress, session.Progress) && session.StartedAt == epoch);
        check("Pause invalidates an in-flight dispatch", !session.CanControl(generation)
            && !session.TryDispatched(proposed, epoch.AddSeconds(seconds), generation));
        seconds += 3600;
        check("An hour of foreground menu observations cannot clear manual pause",
            Feed(false, "MAP", "COMMAND", "ARMORY").Action is null && Feed(false, "MAP", "COMMAND", "ARMORY").Action is null
            && session.ControlsPaused);
        Feed(true, "Endurance: Wave 4");
        Feed(true, "Endurance: Wave 4");
        var run = session.Tracker.Current;
        check("Background frames still track waves during manual pause", run?.HighestWave == 4 && session.ControlsPaused);
        Feed(false, "Endurance: Wave 5");
        Feed(false, "Endurance: Wave 5");
        check("Foreground return monitors without resuming manually paused controls",
            session.ControlsPaused && session.Tracker.Current?.HighestWave == 5 && session.Tracker.Current.Id == run?.Id);
        session.ResumeControls();
        check("Resume preserves the run, counters and start time", !session.ControlsPaused && session.IsRunning
            && session.Tracker.Current?.Id == run?.Id && ReferenceEquals(progress, session.Progress) && session.StartedAt == epoch);
        check("A dispatch from before Pause cannot revive after Resume", !session.TryDispatched(proposed, epoch.AddSeconds(seconds), generation));

        session.Stop("Test end");
        // A fresh coordinator avoids intentionally carrying an active game run into this menu test.
        session = new RunnerSession();
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 2, epoch);
        proposed = Propose();
        generation = session.InputGeneration;
        check("Current dispatch is acknowledged", session.TryDispatched(proposed, epoch.AddSeconds(seconds), generation));
        session.PauseControls();
        check("Pause forgets pending transition and homebase confirmation", session.Controller.Pending is null && !session.Controller.HomebaseClickConfirmed);
        seconds += 3600;
        session.ResumeControls();
        check("Resume requires fresh evidence instead of an expired transition retry", Feed(false, "MAP", "COMMAND", "ARMORY").Action is null
            && session.Controller.Status == ControllerStatus.Observing);
        proposed = Propose();
        generation = session.InputGeneration;
        Feed(true, "MAP", "COMMAND", "ARMORY");
        check("Background observation invalidates a proposed input acknowledgement", !session.TryDispatched(proposed, epoch.AddSeconds(seconds), generation));
        check("Foreground return needs new stable evidence", Feed(false, "MAP", "COMMAND", "ARMORY").Action is null);
        proposed = Propose();
        generation = session.InputGeneration;
        session.Stop("User stop");
        check("Stop ends the session and clears manual pause", !session.IsRunning && !session.ControlsPaused
            && !session.TryDispatched(proposed, epoch.AddSeconds(seconds), generation));
        session.ResumeControls();
        check("Resume cannot restart a stopped session", !session.IsRunning);
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        check("New Start rejects old dispatches", !session.ControlsPaused && !session.TryDispatched(proposed, epoch, generation));
        session.Controller.Pause("Resolve a game error");
        session.PauseControls();
        session.ResumeControls();
        check("Resume does not clear an attention error", session.Controller.Status == ControllerStatus.PausedForAttention
            && !session.CanControl(session.InputGeneration));

        session = new RunnerSession();
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true, 4), 1, epoch);
        session.PauseControls();
        Feed(true, "Endurance: Wave 4");
        Feed(true, "Endurance: Wave 4");
        Feed(true, "Endurance: Wave 5");
        var update = Feed(true, "Endurance: Wave 5");
        check("Target crossing while paused queues intent without input", update.Action is null
            && session.Controller.TargetExitRequested && session.ControlsPaused);
        session.ResumeControls();
        check("Resume retains the target-exit intent", session.Controller.TargetExitRequested);
        session.PauseControls();
        Feed(true, "VICTORY", "TEAM SCORE", "CONTINUE");
        update = Feed(true, "VICTORY", "TEAM SCORE", "CONTINUE");
        check("Paused result observation records the attempt without clicking", update.Action is null
            && session.Progress.AttemptsEnded == 1 && session.ControlsPaused && session.Progress.Phase == SessionPhase.FinishingRewards);
        Feed(true, "MISSION REWARDS", "Gold 50", "CLAIM");
        Feed(true, "MISSION REWARDS", "Gold 50", "CLAIM");
        check("Reward monitoring continues while paused", session.Rewards.Visible && session.Controller.Pending is null);
        Feed(true, "MAP", "COMMAND", "ARMORY");
        update = Feed(true, "MAP", "COMMAND", "ARMORY");
        check("Manual reward cleanup can finish the attempt limit without background input", update.Complete
            && update.Action is null && !session.IsRunning && session.Progress.AttemptsEnded == 1);
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        session.PauseControls();
        session.Stop("Stop while paused");
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        check("Stop followed by Start clears a persistent manual pause", session.IsRunning && !session.ControlsPaused && session.Progress.AttemptsEnded == 0);

        var crash = new CrashRecovery();
        crash.Start();
        var dialog = new CrashDialogEvidence(1, 2, 123, "FortniteClient-Win64-Shipping", "Application Crash Detected",
            "The application has crashed and will now close. We apologize for the inconvenience.", true);
        CrashSnapshot CrashFrame() => new(epoch.AddSeconds(seconds += 2), true, false, [dialog]);
        crash.Observe(CrashFrame());
        var dismiss = crash.Observe(CrashFrame());
        check("Stable crash evidence can request dismissal", dismiss is not null);
        crash.Dispatched(dismiss!, epoch.AddSeconds(seconds));
        crash.InvalidateObservation();
        seconds += 3600;
        check("Suspending crash recovery discards old exit deadlines", crash.Observe(CrashFrame()) is null
            && crash.Status == CrashRecoveryStatus.Confirming);
        crash.InvalidateObservation();
        check("Recovery needs two new observations after interruption", crash.Observe(CrashFrame()) is null
            && crash.Observe(CrashFrame()) is not null);
        var startup = new StartupErrorRecovery();
        Observation StartupFrame() => Frame("ERROR", "CONFIRM", "FORTNITE WAS NOT STARTED CORRECTLY AND NEEDS TO BE CLOSED", "EPIC GAMES LAUNCHER");
        startup.Observe(StartupFrame(), 123);
        startup.InvalidateObservation();
        check("Startup recovery cannot combine evidence from before and after Pause", !startup.Observe(StartupFrame(), 123)
            && startup.Observe(StartupFrame(), 123));
    }
}
