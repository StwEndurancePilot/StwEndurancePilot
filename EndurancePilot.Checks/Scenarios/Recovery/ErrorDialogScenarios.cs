using EndurancePilot.Core;

internal static class ErrorDialogScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        TextRegion Label(string text, double x, double y, double w, double h = 30) => new(text, new(x, y, w, h));
        var error = new Observation(epoch, 1920, 1080, [Label("WERE WASANERROR",720,410,480,50),
            Label("Kicked for idle. Your connection to the host was lost because you were",470,520,980),
            Label("idle for too long.",840,560,260),Label("CONTINUE",890,655,140)]);
        var recognizer = new ScreenRecognizer();
        var evidence = recognizer.Recognize(error);
        check("Observed misread heading plus idle message recognizes recoverable error", evidence.Kind == ScreenKind.ErrorDialog);
        var controller = new AutomationController();
        controller.Start();
        check("Error Continue requires two stable observations", controller.Observe(error, evidence) is null);
        var action = controller.Observe(error with
        {
            CapturedAt = epoch.AddSeconds(.6)
        }, evidence);
        check("Idle error targets Continue without pausing", action is { Target.X: 890, PauseAfterDismissal: false });
        var missing = error with
        {
            Regions = error.Regions.Take(3).ToArray()
        };
        controller.Start();
        controller.Observe(missing, recognizer.Recognize(missing));
        check("Missing Continue cannot trigger a click", controller.Observe(missing with { CapturedAt = epoch.AddSeconds(1) }, recognizer.Recognize(missing)) is null);
        var duplicate = error with
        {
            Regions = [.. error.Regions, Label("CONTINUE", 1100, 655, 140)]
        };
        controller.Start();
        controller.Observe(duplicate, recognizer.Recognize(duplicate));
        check("Competing Continue buttons block input", controller.Observe(duplicate with { CapturedAt = epoch.AddSeconds(1) }, recognizer.Recognize(duplicate)) is null);
        var titleOnly = error with
        {
            Regions = [error.Regions[0], error.Regions[3]]
        };
        check("Fuzzy error heading needs a corroborating message", recognizer.Recognize(titleOnly).Kind == ScreenKind.Unknown);
        var generic = error with
        {
            Regions = [error.Regions[0], Label("Something unexpected happened while loading the game", 470, 520, 980), error.Regions[3]]
        };
        check("Unclassified error still provides Continue and an attention pause", recognizer.Recognize(generic) is { Kind: ScreenKind.NeedsAttention, Targets.Count: 1 });
        var fuzzyButton = error with
        {
            Regions = [.. error.Regions.Take(3), error.Regions[3] with { Text = "CONTlNUE" }]
        };
        check("One Continue character error can use corroborated dialog", recognizer.Recognize(fuzzyButton).Targets.Count == 1);
        check("Error screens poll promptly", NavigationTiming.PollMilliseconds(ScreenKind.ErrorDialog, null) == 50);
        var okError = error with
        {
            Regions = [error.Regions[0] with{Text="THERE WAS AN ERROR"},
            error.Regions[1] with{Text="NETWORK CONNECTION LOST during the current game"},error.Regions[3] with{Text="OK"}]
        };
        check("Existing OK recovery dialog remains supported", recognizer.Recognize(okError) is { Kind: ScreenKind.ErrorDialog, Targets.Count: 1 });

        var lobby = new Observation(epoch, 1920, 1080, [Label("MAP", 20, 20, 100), Label("COMMAND", 200, 20, 200), Label("ARMORY", 500, 20, 200)]);
        var wave = new Observation(epoch, 1920, 1080, [Label("Endurance: Wave 8", 100, 100, 300)]);
        foreach (int limit in new[] { 1, 2 })
        {
            var session = new RunnerSession();
            session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), limit, epoch);
            session.Observe(wave, recognizer.Recognize(wave));
            session.Observe(wave with
            {
                CapturedAt = epoch.AddSeconds(1)
            }, recognizer.Recognize(wave));
            session.Observe(error with
            {
                CapturedAt = epoch.AddSeconds(2)
            }, evidence);
            var update = session.Observe(error with
            {
                CapturedAt = epoch.AddSeconds(2.6)
            }, evidence);
            check("Attempt limit does not block Continue " + limit, update.Action is { SourceScreen: ScreenKind.ErrorDialog } && session.Progress.AttemptsEnded == 0);
            session.Controller.Dispatched(update.Action!, epoch.AddSeconds(2.6));
            session.Observe(error with
            {
                CapturedAt = epoch.AddSeconds(3)
            }, evidence);
            check("Unchanged error is not counted as dismissed " + limit, session.Controller.ConfirmedDismissals == 0 && session.Progress.AttemptsEnded == 0);
            session.Observe(lobby with
            {
                CapturedAt = epoch.AddSeconds(4)
            }, recognizer.Recognize(lobby));
            update = session.Observe(lobby with
            {
                CapturedAt = epoch.AddSeconds(4.6)
            }, recognizer.Recognize(lobby));
            check("Continue closes one interrupted attempt " + limit, session.Controller.ConfirmedDismissals == 1 && session.Progress.InterruptedRuns == 1 && session.Progress.CompletedRuns == 0);
            check("Error recovery obeys remaining attempts " + limit, session.IsRunning == (limit == 2) && update.Complete == (limit == 1));
        }
        var rejoin = new Observation(epoch, 1920, 1080, [Label("Rejoin",50,54,100,40),Label("An existing game session was found.",63,151,430),
            Label("REJOIN GAME",562,552,156),Label("ABANDON GAME",757,552,194)]);
        var chain = new RunnerSession();
        chain.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch);
        chain.Observe(wave, recognizer.Recognize(wave));
        chain.Observe(wave with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(wave));
        chain.Observe(error with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, evidence);
        action = chain.Observe(error with
        {
            CapturedAt = epoch.AddSeconds(2.6)
        }, evidence).Action;
        chain.Controller.Dispatched(action!, epoch.AddSeconds(2.6));
        chain.Observe(rejoin with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, recognizer.Recognize(rejoin));
        chain.Observe(rejoin with
        {
            CapturedAt = epoch.AddSeconds(3.6)
        }, recognizer.Recognize(rejoin));
        check("Continue can reveal Rejoin without stopping before abandonment", chain.Controller.ConfirmedDismissals == 1 && chain.IsRunning && chain.Progress.AttemptsEnded == 0);
        chain.Observe(rejoin with
        {
            CapturedAt = epoch.AddSeconds(9)
        }, recognizer.Recognize(rejoin));
        action = chain.Observe(rejoin with
        {
            CapturedAt = epoch.AddSeconds(9.6)
        }, recognizer.Recognize(rejoin)).Action;
        check("Error to Rejoin chain chooses Abandon", action?.Step == NavigationStep.AbandonRejoin);
        chain.Controller.Dispatched(action!, epoch.AddSeconds(9.6));
        chain.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(10)
        }, recognizer.Recognize(lobby));
        chain.Observe(lobby with
        {
            CapturedAt = epoch.AddSeconds(10.6)
        }, recognizer.Recognize(lobby));
        check("Error and Rejoin cleanup count one interrupted attempt", !chain.IsRunning && chain.Progress.AttemptsEnded == 1 && chain.Controller.ConfirmedAbandons == 1);
    }
}
