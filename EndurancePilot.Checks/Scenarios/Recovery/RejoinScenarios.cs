using EndurancePilot.Core;

internal static class RejoinScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        TextRegion Label(string text, double x, double y, double w = 350, double h = 25) => new(text, new(x, y, w, h));
        var dialog = new Observation(epoch, 1047, 664, [Label("Rejoin",50,54,100,40),
            Label("An existing game session was found.",63,151,430),
            Label("Abandoning the game may forfeit quest progress.",63,213,600),
            Label("AUTOMATIC REJOIN IN:",375,299,270),Label("0:25",487,333,47),
            Label("REJOIN GAME",562,552,156),Label("ABANDON GAME",757,552,194)]);
        var recognizer = new ScreenRecognizer();
        var evidence = recognizer.Recognize(dialog);
        var plan = NavigationPlanner.Decide(dialog, evidence, new()).Plan;
        check("Rejoin dialog selects Abandon even without a configured zone", evidence.Kind == ScreenKind.RejoinDialog
            && plan is { Step: NavigationStep.AbandonRejoin, Target.X: 757 });
        var noTimer = dialog with
        {
            Regions = dialog.Regions.Where(r => !r.Text.Contains("0:25") && !r.Text.Contains("AUTOMATIC")).ToArray()
        };
        check("Rejoin detection does not depend on countdown value", recognizer.Recognize(noTimer).Kind == ScreenKind.RejoinDialog);
        var noisy = dialog with
        {
            Regions = dialog.Regions.Select(r => r with { Text = r.Text.Replace("existing", "exsting").Replace("ABANDON", "ABAND0N") }).ToArray()
        };
        check("Corroborated imperfect dialog text is recognized", recognizer.Recognize(noisy).Kind == ScreenKind.RejoinDialog);
        check("Uncertain abandon digit substitution does not click", NavigationPlanner.Decide(noisy, recognizer.Recognize(noisy), new()).Plan is null);
        var typo = dialog with
        {
            Regions = dialog.Regions.Select(r => r with { Text = r.Text.Replace("ABANDON", "ABANDN") }).ToArray()
        };
        check("One omitted abandon letter can be corroborated", NavigationPlanner.Decide(typo, recognizer.Recognize(typo), new()).Plan?.Step == NavigationStep.AbandonRejoin);
        foreach (var omitted in new[] { 0, 1, 5, 6 })
        {
            var missing = dialog with
            {
                Regions = dialog.Regions.Where((_, i) => i != omitted).ToArray()
            };
            check("Missing dialog clue/control prevents abandoning " + omitted, NavigationPlanner.Decide(missing, recognizer.Recognize(missing), new()).Plan is null);
        }
        var duplicate = dialog with
        {
            Width = 1400,
            Regions = [.. dialog.Regions, Label("ABANDON GAME", 1000, 552, 194)]
        };
        check("Ambiguous abandon buttons prevent input", NavigationPlanner.Decide(duplicate, recognizer.Recognize(duplicate), new()).Plan is null);
        var background = dialog with
        {
            Regions = [.. dialog.Regions, Label("MAP", 20, 600), Label("COMMAND", 100, 600), Label("ARMORY", 200, 600)]
        };
        check("Rejoin modal takes priority over background lobby", recognizer.Recognize(background).Kind == ScreenKind.RejoinDialog);
        var controller = new AutomationController();
        controller.Start(runInProgress: true);
        check("Rejoin requires more than one observation", controller.Observe(dialog, evidence) is null);
        var next = dialog with
        {
            CapturedAt = epoch.AddMilliseconds(600)
        };
        var action = controller.Observe(next, evidence);
        check("Recovery mode can abandon a disconnected active run", action?.Step == NavigationStep.AbandonRejoin);
        controller.Dispatched(action!, next.CapturedAt);
        controller.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, evidence);
        check("Unchanged rejoin dialog does not confirm abandonment", controller.ConfirmedAbandons == 0 && controller.Pending == action);
        var blank = new Observation(epoch.AddSeconds(2), 1047, 664, []);
        controller.Observe(blank, ScreenEvidence.Unknown("transition"));
        check("Blank transition does not confirm abandonment", controller.ConfirmedAbandons == 0);
        var lobby = new Observation(epoch.AddSeconds(3), 1047, 664, [Label("MAP", 10, 10), Label("COMMAND", 10, 50), Label("ARMORY", 10, 90)]);
        controller.Observe(lobby, recognizer.Recognize(lobby));
        check("One lobby frame cannot confirm abandonment", controller.ConfirmedAbandons == 0);
        controller.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(3.6)
        }, recognizer.Recognize(lobby));
        check("Stable return to lobby confirms abandonment and clears active guard", controller.ConfirmedAbandons == 1 && !controller.RunInProgress && controller.Pending is null);
        check("Rejoin polls quickly while countdown runs", NavigationTiming.PollMilliseconds(ScreenKind.RejoinDialog, null) == 50);

        var session = new RunnerSession();
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        var wave = new Observation(epoch, 1047, 664, [Label("Endurance: Wave 8", 100, 100)]);
        session.Observe(wave, recognizer.Recognize(wave));
        session.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(wave));
        session.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, evidence);
        var update = session.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(2.6)
        }, evidence);
        check("Final attempt can abandon before recording interruption", update.Action?.Step == NavigationStep.AbandonRejoin && session.Progress.AttemptsEnded == 0);
        session.Controller.Dispatched(update.Action!, epoch.AddSeconds(2.6));
        session.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, recognizer.Recognize(lobby));
        update = session.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, recognizer.Recognize(lobby));
        check("Confirmed abandon counts one interrupted attempt and stops at limit", update.Complete && !session.IsRunning
            && session.Progress.InterruptedRuns == 1 && session.Progress.CompletedRuns == 0 && session.Controller.ConfirmedAbandons == 1);

        var continuing = new RunnerSession();
        continuing.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 2, epoch);
        continuing.Observe(wave, recognizer.Recognize(wave));
        continuing.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(wave));
        continuing.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, evidence);
        update = continuing.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(2.6)
        }, evidence);
        continuing.Controller.Dispatched(update.Action!, epoch.AddSeconds(2.6));
        continuing.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, recognizer.Recognize(lobby));
        continuing.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(3.6)
        }, recognizer.Recognize(lobby));
        continuing.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, recognizer.Recognize(lobby));
        update = continuing.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(4.6)
        }, recognizer.Recognize(lobby));
        check("Remaining attempt resumes normal navigation after abandon", continuing.IsRunning
            && continuing.Progress.AttemptsEnded == 1 && update.Action?.Step == NavigationStep.OpenMap);
        var passive = new RunnerSession();
        passive.Start(RunnerMode.Observe, new(), 1, epoch);
        passive.Observe(dialog, evidence);
        check("Observation mode never abandons", passive.Observe(next, evidence).Action is null);

        var pending = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
        pending.Start();
        pending.Observe(lobby with
        {
            CapturedAt = epoch
        }, recognizer.Recognize(lobby));
        var map = pending.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(.6)
        }, recognizer.Recognize(lobby));
        pending.Dispatched(map!, epoch.AddSeconds(.6));
        pending.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, evidence);
        check("Rejoin supersedes a pending menu action", pending.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(1.6)
        }, evidence)?.Step == NavigationStep.AbandonRejoin);
        controller.Start();
        controller.Observe(dialog, evidence);
        action = controller.Observe(next, evidence);
        controller.Dispatched(action!, next.CapturedAt);
        controller.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(13)
        }, evidence);
        controller.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(16)
        }, evidence);
        action = controller.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(16.6)
        }, evidence);
        controller.Dispatched(action!, epoch.AddSeconds(16.6));
        controller.Observe(dialog with
        {
            CapturedAt = epoch.AddSeconds(29)
        }, evidence);
        check("Unchanged dialog stops after two dispatched attempts", controller.Status == ControllerStatus.PausedForAttention && controller.ConfirmedAbandons == 1);
    }
}
