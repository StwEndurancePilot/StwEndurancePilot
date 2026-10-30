using EndurancePilot.Core;

internal static class WavesSurvivedContinueScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var session = new RunnerSession();
        var recognizer = new ScreenRecognizer();
        double seconds = 0;
        Observation Frame(params (string Text, Box Bounds)[] regions) => new(epoch, 1920, 1080,
            regions.Select(r => new TextRegion(r.Text, r.Bounds)).ToArray());
        SessionUpdate Feed(Observation sample, double step = 2)
        {
            seconds += step;
            var frame = sample with { CapturedAt = epoch.AddSeconds(seconds) };
            return session.Observe(frame, recognizer.Recognize(frame));
        }
        ClickAction? Next(Observation sample)
        {
            ClickAction? action = null;
            for (int i = 0; i < 20 && action is null; i++) action = Feed(sample).Action;
            return action;
        }
        // Real OCR layout captured from the user's "WAVES SURVIVED: 30" result screen.
        var result = Frame(("WAVES SURVIVED:30", new(740, 29, 446, 48)), ("Storm Shield Twine Peaks | 140", new(631, 84, 472, 32)),
            ("CHAT", new(34, 1036, 102, 24)), ("Emote", new(1629, 1038, 70, 18)), ("Continue", new(1782, 1038, 102, 18)));
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), null, epoch);
        var first = Next(result);
        check("First Continue attempt on WAVES SURVIVED uses Escape",
            first is { Step: NavigationStep.ContinueResults, Key: NavigationKey.Escape });
        session.Controller.Dispatched(first!, epoch.AddSeconds(seconds));
        var second = Next(result);
        check("Second attempt clicks the Continue button when Escape did not advance",
            second is { Step: NavigationStep.ContinueResults, Key: null } && second.Target.X == 1782 && second.Target.Y == 1038);
        check("Runner did not pause before the click fallback", session.Controller.Status != ControllerStatus.PausedForAttention);
        // Background mode: with a target of 20, wave 21 seen in background must request the exit,
        // which is what makes Pilot take Fortnite back to leave the mission.
        var bg = new RunnerSession();
        bg.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true, 20), null, epoch);
        Observation Wave(int n) => new(epoch, 1920, 1080, [new TextRegion("Endurance: Wave " + n, new(100, 100, 350, 30))]);
        double t = 0;
        foreach (var n in new[] { 20, 20 })
        {
            var f = Wave(n) with { CapturedAt = epoch.AddSeconds(t += 2) };
            bg.ObserveBackground(f, recognizer.Recognize(f));
        }
        check("Wave 20 in background keeps the PC with the user", !bg.Controller.TargetExitRequested);
        foreach (var n in new[] { 21, 21 })
        {
            var f = Wave(n) with { CapturedAt = epoch.AddSeconds(t += 2) };
            bg.ObserveBackground(f, recognizer.Recognize(f));
        }
        check("Wave 21 in background requests leaving for target 20", bg.Controller.TargetExitRequested);
    }
}
