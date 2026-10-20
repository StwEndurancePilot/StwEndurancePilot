using EndurancePilot.Core;

internal static class RunRestartScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        Observation Frame(double second, params string[] labels) => new(epoch.AddSeconds(second), 1920, 1080,
            labels.Select((label, i) => new TextRegion(label, new(100, 50 + i * 50, 300, 30))).ToArray());
        var tracker = new RunTracker();
        RunRecord? Feed(double second, params string[] labels)
        {
            var frame = Frame(second, labels);
            return tracker.Observe(frame, recognizer.Recognize(frame));
        }
        Feed(1, "Endurance: Wave 30");
        Feed(3, "Endurance: Wave 30");
        var originalId = tracker.Current!.Id;
        Feed(4, "MAP", "COMMAND", "ARMORY");
        Feed(4, "MAP", "COMMAND", "ARMORY");
        Feed(4.5, "MAP", "COMMAND", "ARMORY");
        check("Duplicate or closely spaced menu reads cannot end a run", tracker.Current.Outcome == RunOutcome.InProgress);
        Feed(5, "Endurance: Wave 30");
        Feed(6, "MAP", "COMMAND", "ARMORY");
        check("A transient menu does not carry confirmation across gameplay", tracker.Current.Outcome == RunOutcome.InProgress);
        Feed(7, "LOADING");
        Feed(8, "MAP", "COMMAND", "ARMORY");
        check("Loading breaks consecutive menu evidence", tracker.Current.Outcome == RunOutcome.InProgress);
        var ended = Feed(10, "MAP", "COMMAND", "ARMORY");
        check("Stable menu return interrupts rather than completes wave 30", ended is { Outcome: RunOutcome.Interrupted, HighestWave: 30 }
            && ended.Id == originalId && ended.EndedAt == epoch.AddSeconds(10));
        check("Menu interruption is emitted once", Feed(12, "MAP", "COMMAND", "ARMORY") is null);
        Feed(14, "Endurance: Wave 1");
        check("Restart still requires two active observations", tracker.Current.Id == originalId);
        Feed(16, "Endurance: Wave 1");
        check("A restarted run gets a new identity and starts at wave one", tracker.Current is { HighestWave: 1, Outcome: RunOutcome.InProgress }
            && tracker.Current.Id != originalId);

        foreach (var kind in new[] { ScreenKind.Backpack, ScreenKind.StormShieldConsole, ScreenKind.EnduranceStatus,
            ScreenKind.EnduranceSetup, ScreenKind.Homebase, ScreenKind.Unknown, ScreenKind.Loading })
        {
            var active = new RunTracker();
            foreach (int second in new[] { 1, 3 })
            {
                var frame = Frame(second, "Endurance: Wave 10");
                active.Observe(frame, recognizer.Recognize(frame));
            }
            var before = active.Current;
            foreach (int second in new[] { 5, 7 })
            {
                active.Observe(Frame(second), new(kind, [], []));
            }

            check($"Opening {kind} does not discard an active run", active.Current == before);
        }

        foreach (var menu in new[] { ScreenKind.ModeDiscovery, ScreenKind.ModeSelection, ScreenKind.SaveTheWorldHub,
            ScreenKind.SaveTheWorldLobby, ScreenKind.WorldMap, ScreenKind.MissionMap })
        {
            var active = new RunTracker();
            foreach (int second in new[] { 1, 3 })
            {
                var frame = Frame(second, "Endurance: Wave 10");
                active.Observe(frame, recognizer.Recognize(frame));
            }
            active.Observe(Frame(5), new(menu, [], []));
            active.Observe(Frame(20), new(menu, [], []));
            check($"A long observation gap cannot confirm return to {menu}", active.Current!.Outcome == RunOutcome.InProgress);
            check($"Fresh consecutive {menu} observations end the previous attempt",
                active.Observe(Frame(22), new(menu, [], []))?.Outcome == RunOutcome.Interrupted);
        }

        var controller = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
        controller.Start(runInProgress: true);
        controller.RecordRunOutcome(tracker.Current!);
        check("A nonterminal record cannot clear the active-run guard", controller.RunInProgress);
        controller.RecordRunOutcome(ended!);
        var homebase = Frame(30, "Storm Shield", "Activate Storm Shield to Defend");
        controller.Observe(homebase, recognizer.Recognize(homebase));
        var next = controller.Observe(homebase with
        {
            CapturedAt = epoch.AddSeconds(32)
        }, recognizer.Recognize(homebase));
        check("Confirmed interruption releases navigation for the next attempt", !controller.RunInProgress && next?.Step == NavigationStep.OpenBackpack);
        var session = new SessionProgress(1);
        session.Record(ended!);
        session.Record(ended!);
        check("Menu interruption consumes one attempt and obeys the attempt limit", session.AttemptsEnded == 1 && session.Phase == SessionPhase.Complete);

        foreach (var exitKind in new[] { ScreenKind.Victory, ScreenKind.Defeat, ScreenKind.RewardClaim, ScreenKind.Rewards,
            ScreenKind.ModeDiscovery, ScreenKind.ModeSelection, ScreenKind.SaveTheWorldHub,
            ScreenKind.SaveTheWorldLobby, ScreenKind.WorldMap, ScreenKind.MissionMap })
        {
            var guarded = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
            guarded.Start(runInProgress: true);
            guarded.Observe(Frame(1), new(exitKind, [], []));
            foreach (int second in new[] { 2, 4 })
            {
                var baseFrame = homebase with
                {
                    CapturedAt = epoch.AddSeconds(second)
                };
                check($"One {exitKind} read cannot unlock a restart", guarded.Observe(baseFrame, recognizer.Recognize(baseFrame)) is null && guarded.RunInProgress);
            }
            guarded.Observe(Frame(5), new(exitKind, [], []));
            guarded.Observe(Frame(5.5), new(exitKind, [], []));
            check($"Exit confirmation for {exitKind} requires elapsed time", guarded.RunInProgress);
            guarded.Observe(Frame(6), new(exitKind, [], []));
            check($"Stable {exitKind} releases the controller's run guard", !guarded.RunInProgress);
        }
        controller.Start();
        var countdown = Frame(40, "Endurance Defense: Wave 1 starts in:", "0:18");
        controller.Observe(countdown, recognizer.Recognize(countdown));
        controller.Observe(Frame(41, "MAP", "COMMAND", "ARMORY"), new(ScreenKind.SaveTheWorldLobby, [], []));
        controller.Observe(Frame(42), new(ScreenKind.WorldMap, [], []));
        check("Different exit screens cannot pool confirmation for a countdown-only run", controller.RunInProgress);
        controller.Observe(Frame(55), new(ScreenKind.WorldMap, [], []));
        check("A long observation gap resets controller exit confirmation", controller.RunInProgress);
        controller.Observe(Frame(57), new(ScreenKind.WorldMap, [], []));
        check("Countdown-only runs can recover after a verified menu return", !controller.RunInProgress);
    }
}
