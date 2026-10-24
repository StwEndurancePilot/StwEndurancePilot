using EndurancePilot.Core;

internal static class SleepScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        TextRegion Label(string text, double x, double y, double w, double h = 22) => new(text, new(x, y, w, h));
        var sleep = new Observation(epoch, 1650, 928, [Label("FORTNITE IS IN SLEEP MODE",490,345,675,30),
            Label("KEEP PLAYING",740,420,170),Label("CLOSE FORTNITE",729,488,190)]);
        var recognizer = new ScreenRecognizer();
        var countdown = new Observation(epoch, 1280, 696, [Label("ARE YOU STILL THERE?", 434, 261, 412, 25),
            Label("Fortnite will enter sleep mode soon.", 472, 305, 337, 18),
            Label("PRESS ANY KEY TO KEEP PLAYING", 490, 354, 301, 17)]);
        var countdownEvidence = recognizer.Recognize(countdown);
        check("Gameplay checks for sleep at least twice a second", NavigationTiming.GameplayPollMilliseconds <= 500);
        check("Sleep dismissal uses fast confirmation", NavigationTiming.Stability(NavigationStep.KeepPlaying, countdownEvidence) <= TimeSpan.FromMilliseconds(100));
        check("Sleep countdown selects the keep-playing button without a close button", countdownEvidence.Kind == ScreenKind.SleepMode
            && NavigationPlanner.Decide(countdown, countdownEvidence, new()).Plan is { Step: NavigationStep.KeepPlaying, Target.Y: 354 });
        var countdownController = new AutomationController();
        countdownController.Start(runInProgress: true);
        check("Countdown requires stable evidence before input", countdownController.Observe(countdown, countdownEvidence) is null);
        check("Countdown wakes during an active run", countdownController.Observe(countdown with { CapturedAt = epoch.AddSeconds(.6) }, countdownEvidence)?.Step == NavigationStep.KeepPlaying);
        var noButton = countdown with { Regions = countdown.Regions.Take(2).ToArray() };
        check("Countdown without a button sends no input", NavigationPlanner.Decide(noButton, recognizer.Recognize(noButton), new()).Plan is null);
        var noDetail = countdown with { Regions = [countdown.Regions[0], countdown.Regions[2]] };
        check("Generic inactivity prompt does not authorize waking", recognizer.Recognize(noDetail).Kind != ScreenKind.SleepMode);
        var duplicateButton = countdown with { Regions = [.. countdown.Regions, Label("PRESS ANY KEY TO KEEP PLAYING", 490, 385, 301, 17)] };
        check("Ambiguous countdown targets send no input", NavigationPlanner.Decide(duplicateButton, recognizer.Recognize(duplicateButton), new()).Plan is null);
        var evidence = recognizer.Recognize(sleep);
        check("Sleep overlay selects only Keep Playing", evidence.Kind == ScreenKind.SleepMode
            && NavigationPlanner.Decide(sleep, evidence, new()).Plan is { Step: NavigationStep.KeepPlaying, Target.Y: 420 });
        var noisy = sleep with
        {
            Regions = sleep.Regions.Select(r => r with { Text = r.Text.Replace("FORTNITE IS", "FORTNlTE IS").Replace("PLAYING", "PLAYlNG") }).ToArray()
        };
        check("Sleep labels tolerate a corroborated character error", NavigationPlanner.Decide(noisy, recognizer.Recognize(noisy), new()).Plan?.Step == NavigationStep.KeepPlaying);
        var missing = sleep with
        {
            Regions = [sleep.Regions[0], sleep.Regions[2]]
        };
        check("Missing Keep Playing never chooses Close Fortnite", recognizer.Recognize(missing).Kind == ScreenKind.SleepMode && NavigationPlanner.Decide(missing, recognizer.Recognize(missing), new()).Plan is null);
        var noHeading = sleep with
        {
            Regions = sleep.Regions.Skip(1).ToArray()
        };
        check("Button labels alone cannot authorize waking", NavigationPlanner.Decide(noHeading, recognizer.Recognize(noHeading), new()).Plan is null);
        var ambiguous = sleep with
        {
            Regions = [.. sleep.Regions, Label("KEEP PLAYING", 740, 450, 170)]
        };
        check("Competing Keep Playing targets block clicks", NavigationPlanner.Decide(ambiguous, recognizer.Recognize(ambiguous), new()).Plan is null);
        var wave = new Observation(epoch, 1650, 928, [Label("Endurance: Wave 8", 50, 50, 300)]);
        var background = sleep with
        {
            Regions = [.. sleep.Regions, .. wave.Regions]
        };
        check("Sleep takes priority over visible gameplay text", recognizer.Recognize(background).Kind == ScreenKind.SleepMode);
        var controller = new AutomationController();
        controller.Start(runInProgress: true);
        check("One sleep frame cannot click", controller.Observe(sleep, evidence) is null);
        var action = controller.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(.6)
        }, evidence);
        check("Recovery can wake an active run", action?.Step == NavigationStep.KeepPlaying);
        controller.Dispatched(action!, epoch.AddSeconds(.6));
        controller.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, evidence);
        controller.Observe(new(epoch.AddSeconds(2), 1650, 928, []), ScreenEvidence.Unknown("transition"));
        check("Unchanged and blank screens cannot confirm wake", controller.ConfirmedWakes == 0 && controller.Pending == action);
        controller.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, recognizer.Recognize(wave));
        controller.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(3.6)
        }, recognizer.Recognize(wave));
        check("Stable gameplay confirms wake while retaining run guard", controller.ConfirmedWakes == 1 && controller.RunInProgress && controller.Pending is null);
        controller.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(5)
        }, evidence);
        check("A later sleep episode is handled again", controller.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(5.6)
        }, evidence)?.Step == NavigationStep.KeepPlaying);
        controller.Stop();
        check("Stopped controller does not wake", controller.Observe(sleep with { CapturedAt = epoch.AddSeconds(6) }, evidence) is null);
        controller.Start();
        controller.Pause("User attention required");
        check("Sleep cannot clear an existing attention pause", controller.Observe(sleep with { CapturedAt = epoch.AddSeconds(7) }, evidence) is null);
        check("Sleep polls quickly", NavigationTiming.PollMilliseconds(ScreenKind.SleepMode, null) == 50);

        var session = new RunnerSession();
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        session.Observe(wave, recognizer.Recognize(wave));
        session.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(wave));
        var run = session.Tracker.Current!.Id;
        session.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, evidence);
        action = session.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(2.6)
        }, evidence).Action;
        session.Controller.Dispatched(action!, epoch.AddSeconds(2.6));
        session.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, recognizer.Recognize(wave));
        session.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(3.6)
        }, recognizer.Recognize(wave));
        check("Sleep and wake preserve run identity, wave and attempt count", session.IsRunning && session.Tracker.Current is { Outcome: RunOutcome.InProgress, HighestWave: 8 }
            && session.Tracker.Current.Id == run && session.Progress.AttemptsEnded == 0);
        var passive = new RunnerSession();
        passive.Start(RunnerMode.Observe, new(), 1, epoch);
        passive.Observe(sleep, evidence);
        check("Observe mode stays passive on sleep overlay", passive.Observe(sleep with { CapturedAt = epoch.AddSeconds(1) }, evidence).Action is null);

        var lobby = new Observation(epoch, 1650, 928, [Label("MAP", 20, 20, 100), Label("COMMAND", 200, 20, 150), Label("ARMORY", 400, 20, 150)]);
        var menu = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
        menu.Start();
        menu.Observe(lobby, recognizer.Recognize(lobby));
        var prior = menu.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(.6)
        }, recognizer.Recognize(lobby));
        menu.Dispatched(prior!, epoch.AddSeconds(.6));
        menu.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, evidence);
        action = menu.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(1.6)
        }, evidence);
        check("Sleep suspends a pending menu transition", action?.Step == NavigationStep.KeepPlaying);
        menu.Dispatched(action!, epoch.AddSeconds(1.6));
        menu.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, recognizer.Recognize(lobby));
        menu.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(2.6)
        }, recognizer.Recognize(lobby));
        check("Waking restores the original pending action", menu.Pending == prior && menu.Status == ControllerStatus.AwaitingTransition);

        var error = new Observation(epoch, 1650, 928, [Label("THERE WAS AN ERROR", 300, 100, 600, 40), Label("CONNECTION LOST", 300, 180, 600), Label("CONTINUE", 500, 250, 200)]);
        var cleanup = new RunnerSession();
        cleanup.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        cleanup.Observe(wave, recognizer.Recognize(wave));
        cleanup.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(wave));
        cleanup.Observe(error with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, recognizer.Recognize(error));
        prior = cleanup.Observe(error with
        {
            CapturedAt = epoch.AddSeconds(2.6)
        }, recognizer.Recognize(error)).Action;
        cleanup.Controller.Dispatched(prior!, epoch.AddSeconds(2.6));
        cleanup.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, evidence);
        action = cleanup.Observe(sleep with
        {
            CapturedAt = epoch.AddSeconds(3.6)
        }, evidence).Action;
        cleanup.Controller.Dispatched(action!, epoch.AddSeconds(3.6));
        check("Sleep retains pending error recovery bookkeeping", cleanup.Controller.AwaitingErrorDismissal && cleanup.Progress.AttemptsEnded == 0);
        cleanup.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, recognizer.Recognize(lobby));
        cleanup.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(4.6)
        }, recognizer.Recognize(lobby));
        cleanup.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(5)
        }, recognizer.Recognize(lobby));
        cleanup.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(5.6)
        }, recognizer.Recognize(lobby));
        check("Error dismissal interrupted by sleep still counts exactly once", !cleanup.IsRunning && cleanup.Progress.InterruptedRuns == 1
            && cleanup.Controller.ConfirmedDismissals == 1 && cleanup.Controller.ConfirmedWakes == 1);
    }
}
