using EndurancePilot.Core;

internal static class RapidNavigationScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        TextRegion L(string t, int x, int y, int w = 150) => new(t, new(x, y, w, 30));
        Observation Zone(HomebaseZone zone, double ms, int arrowX = 100) => new(epoch.AddMilliseconds(ms), 1920, 1080,
            [L("MAP",400,50),L("COMMAND",600,50),L("ARMORY",800,50),L(NavigationPlanner.ZoneName(zone),200,200,300),
                L("RECOMMENDED POWER",200,400,400),L("SELECT",1600,900)],
            [new("next-zone", new(arrowX, 200, 30, 40), .99, .3)]);
        var controller = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        controller.Start();
        ClickAction? Observe(Observation frame) => controller.Observe(frame, recognizer.Recognize(frame));
        check("Rapid navigation needs an initial observation", Observe(Zone(HomebaseZone.Stonewood, 0)) is null);
        check("Two frames only 50ms apart are still too early", Observe(Zone(HomebaseZone.Stonewood, 50)) is null);
        var action = Observe(Zone(HomebaseZone.Stonewood, 100));
        check("Clear initial menu can act at 100ms", action is { Step: NavigationStep.ChangeZone, Context: "Plankerton" });
        controller.Dispatched(action!, epoch.AddMilliseconds(160));
        var zones = new[] { HomebaseZone.Plankerton, HomebaseZone.CannyValley, HomebaseZone.TwinePeaks };
        for (int i = 0; i < zones.Length; i++)
        {
            int first = 200 + i * 200, second = 300 + i * 200;
            check("One destination frame does not chain " + zones[i], Observe(Zone(zones[i], first)) is null);
            action = Observe(Zone(zones[i], second));
            check("Second fresh destination frame chains the correct action " + zones[i], action is not null
                && action.Step == (i < 2 ? NavigationStep.ChangeZone : NavigationStep.EnterZone)
                && (i == 2 || action.Context == zones[i + 1].ToString()) && action.ObservedAt == epoch.AddMilliseconds(second));
            controller.Dispatched(action!, epoch.AddMilliseconds(second + 60));
        }
        check("Rapid sequence never clicks again on unchanged Twine", Observe(Zone(HomebaseZone.TwinePeaks, 800)) is null);

        void StartTransition()
        {
            controller.Start();
            Observe(Zone(HomebaseZone.Stonewood, 0));
            var click = Observe(Zone(HomebaseZone.Stonewood, 100))!;
            controller.Dispatched(click, epoch.AddMilliseconds(160));
        }
        StartTransition();
        Observe(Zone(HomebaseZone.Plankerton, 200));
        check("Moving next target breaks chained input", Observe(Zone(HomebaseZone.Plankerton, 300, 200)) is null);
        check("Moved target needs fresh observations", Observe(Zone(HomebaseZone.Plankerton, 400, 200)) is null);
        check("Moved target can be reacquired", Observe(Zone(HomebaseZone.Plankerton, 500, 200)) is { Target.X: 200 });
        StartTransition();
        Observe(Zone(HomebaseZone.Plankerton, 200));
        var ambiguous = Zone(HomebaseZone.Plankerton, 300) with
        {
            Visuals = [new("next-zone", new(100, 200, 30, 40), .99, .3), new("next-zone", new(300, 200, 30, 40), .99, .3)]
        };
        check("Two next targets prevent chaining", Observe(ambiguous) is null);
        StartTransition();
        Observe(Zone(HomebaseZone.Plankerton, 200));
        check("Resize invalidates pending destination stability", Observe(Zone(HomebaseZone.Plankerton, 300) with { Width = 2560 }) is null && controller.Pending is not null);
        check("Fresh resized frame may confirm and chain", Observe(Zone(HomebaseZone.Plankerton, 400) with
        {
            Width = 2560
        })?.Step == NavigationStep.ChangeZone);
        controller.Start();
        var fuzzy = recognizer.Recognize(Zone(HomebaseZone.Stonewood, 0)) with
        {
            UsesFuzzyMatching = true
        };
        check("Fuzzy actions retain 500ms evidence time", NavigationTiming.Stability(NavigationStep.ChangeZone, fuzzy) == TimeSpan.FromMilliseconds(500));
        check("Start Endurance retains 500ms evidence time", NavigationTiming.Stability(NavigationStep.StartEndurance, new(ScreenKind.EnduranceSetup, [], [])) == TimeSpan.FromMilliseconds(500));
        check("Error dismissal retains 500ms evidence time", NavigationTiming.Stability(null, new(ScreenKind.ErrorDialog, [], [])) == TimeSpan.FromMilliseconds(500));
        check("Ordinary menus have no added transition cooldown", NavigationTiming.TransitionCooldown(NavigationStep.ChangeZone) == TimeSpan.Zero);

        controller.Start();
        Observe(Zone(HomebaseZone.Stonewood, 0));
        controller.InvalidateObservation();
        check("Focus interruption discards pre-interruption target evidence", Observe(Zone(HomebaseZone.Stonewood, 100)) is null);
        action = Observe(Zone(HomebaseZone.Stonewood, 200));
        check("Focus return reacquires a target without restarting the runner", action?.Step == NavigationStep.ChangeZone);
        controller.Dispatched(action!, epoch.AddMilliseconds(260));
        Observe(Zone(HomebaseZone.Plankerton, 300));
        controller.InvalidateObservation();
        check("Layout interruption preserves the dispatched action", controller.Pending == action && controller.Status == ControllerStatus.AwaitingTransition);
        check("Layout return cannot reuse an earlier destination frame", Observe(Zone(HomebaseZone.Plankerton, 400)) is null);
        check("Two new destination frames can confirm and chain", Observe(Zone(HomebaseZone.Plankerton, 500))?.Context == "CannyValley");
        controller.Pause("User attention required");
        controller.InvalidateObservation();
        check("Observation invalidation cannot resume an attention pause", Observe(Zone(HomebaseZone.Stonewood, 600)) is null && controller.Status == ControllerStatus.PausedForAttention);
        controller.Stop();
        controller.InvalidateObservation();
        check("Observation invalidation cannot restart a stopped runner", Observe(Zone(HomebaseZone.Stonewood, 700)) is null && controller.Status == ControllerStatus.Stopped);
    }
}
