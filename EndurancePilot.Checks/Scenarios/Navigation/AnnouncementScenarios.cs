using EndurancePilot.Core;

internal static class AnnouncementScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        Observation Frame(double second, params string[] labels) => new(epoch.AddSeconds(second), 1920, 1080,
            labels.Select((label, i) => new TextRegion(label, new(100, 100 + i * 100, 300, 40))).ToArray());
        var unlock = Frame(1, "NEW!", "Increased Radius II", "Deploys up to 4 Proximity Mines", "CONTINUE");
        var survey = Frame(1, "WE WANT TO HEAR FROM YOU!", "Select all that apply", "SKIP", "SUBMIT");
        check("Unlock announcement recognised", recognizer.Recognize(unlock).Kind == ScreenKind.NewUnlock);
        check("Survey recognised", recognizer.Recognize(survey).Kind == ScreenKind.FeedbackSurvey);
        check("Survey accepts captured heading OCR", recognizer.Recognize(Frame(1,
            "WE WANT TO HEAR FROM voug", "Providing feedback is optional", "SKIP", "SUBMIT")).Kind == ScreenKind.FeedbackSurvey);
        check("Bare new/continue cannot dismiss", recognizer.Recognize(Frame(1, "NEW!", "CONTINUE")).Kind == ScreenKind.Unknown);
        check("Bare skip/submit cannot dismiss", recognizer.Recognize(Frame(1, "SKIP", "SUBMIT")).Kind == ScreenKind.Unknown);
        check("Survey only selects Skip", NavigationPlanner.Decide(survey, recognizer.Recognize(survey), new()).Plan is { Step: NavigationStep.SkipFeedback });
        check("Duplicate Continue blocks input", NavigationPlanner.Decide(unlock with
        {
            Regions = unlock.Regions.Concat([new TextRegion("CONTINUE", new(900, 900, 200, 40))]).ToArray()
        },
            recognizer.Recognize(unlock with
            {
                Regions = unlock.Regions.Concat([new TextRegion("CONTINUE", new(900, 900, 200, 40))]).ToArray()
            }), new()).Plan is null);
        var controller = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
        controller.Start();
        ClickAction? Observe(Observation f) => controller.Observe(f, recognizer.Recognize(f));
        Observe(unlock);
        var first = Observe(unlock with
        {
            CapturedAt = epoch.AddSeconds(1.6)
        });
        check("Stable unlock produces Continue", first?.Step == NavigationStep.ContinueUnlock);
        controller.Dispatched(first!, epoch.AddSeconds(1.6));
        check("Unlock click waits for settle", Observe(unlock with { CapturedAt = epoch.AddSeconds(2) }) is null);
        Observe(unlock with
        {
            CapturedAt = epoch.AddSeconds(4)
        });
        var second = Observe(unlock with
        {
            CapturedAt = epoch.AddSeconds(4.6)
        });
        check("Identical stacked unlock is reacquired", second?.Step == NavigationStep.ContinueUnlock);
        controller.Dispatched(second!, epoch.AddSeconds(4.6));
        Observe(survey with
        {
            CapturedAt = epoch.AddSeconds(7)
        });
        var skip = Observe(survey with
        {
            CapturedAt = epoch.AddSeconds(7.6)
        });
        check("Survey following unlock can be skipped", skip?.Step == NavigationStep.SkipFeedback);
        controller.Dispatched(skip!, epoch.AddSeconds(7.6));
        var menu = Frame(10, "MAP", "COMMAND", "ARMORY");
        Observe(menu);
        check("Navigation resumes after overlays", Observe(menu with
        {
            CapturedAt = epoch.AddSeconds(10.6)
        })?.Step == NavigationStep.OpenMap);
        var stuck = new AutomationController { ErrorRecoveryOnly = false };
        stuck.Start();
        int clicks = 0;
        for (int i = 0; i < 200; i++)
        {
            var frame = unlock with
            {
                CapturedAt = epoch.AddSeconds(i)
            };
            var next = stuck.Observe(frame, recognizer.Recognize(frame));
            if (next is not null)
            {
                stuck.Dispatched(next, frame.CapturedAt);
                clicks++;
            }
        }
        check("Stuck unlock control has a bounded retry budget", clicks == 40 && stuck.Status == ControllerStatus.PausedForAttention);
        check("Announcements do not count as rewards", controller.ConfirmedRewardClaims == 0);

        var launch = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
        launch.Start();
        var ready = Frame(0, "HERO LOADOUT", "STORM SHIELD", "LAUNCH");
        launch.Observe(ready, recognizer.Recognize(ready));
        var action = launch.Observe(ready with
        {
            CapturedAt = epoch.AddSeconds(.2)
        }, recognizer.Recognize(ready));
        check("Homebase launch is proposed", action?.Step == NavigationStep.LaunchLoadedHomebase);
        launch.Dispatched(action!, epoch.AddSeconds(.2));
        launch.Observe(ready with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(ready));
        check("Launch wait retains entry fallback", launch.AwaitingQuestMenu);
        var hud = Frame(3) with
        {
            Visuals = [new("player-health-bar", new(80, 900, 280, 24), 1, 1)]
        };
        launch.Observe(hud, recognizer.Recognize(hud));
        check("Homebase launch arms Tab even if objective OCR is missing",
            launch.Observe(hud with
            {
                CapturedAt = epoch.AddSeconds(3.2)
            }, recognizer.Recognize(hud))?.Step == NavigationStep.RetryQuestMenu);
    }
}
