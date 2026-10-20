using EndurancePilot.Core;

internal static class ProcessExitScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var exits = new ProcessExitObserver();
        GameProcessSnapshot Frame(double time, bool game = false, bool boot = false) => new(epoch.AddSeconds(time), game, boot, game);
        check("Starting from a closed game is not an interrupted run", !exits.Observe(Frame(0)) && !exits.Observe(Frame(5)));
        exits.Observe(Frame(10, game: true));
        check("One absent process observation cannot interrupt a run", !exits.Observe(Frame(11)));
        check("Repeated timestamp cannot confirm process exit", !exits.Observe(Frame(11)));
        check("Short process absence cannot interrupt a run", !exits.Observe(Frame(12)));
        exits.Observe(Frame(13, game: true));
        check("A returning game clears the absence candidate", !exits.Observe(Frame(14)));
        check("Confirmed five-second process absence records exit", exits.Observe(Frame(19)));
        check("Continued process absence cannot repeat exit", !exits.Observe(Frame(25)));
        exits.Observe(Frame(30, game: true));
        exits.Observe(Frame(31));
        check("A startup process blocks exit confirmation", !exits.Observe(Frame(36, boot: true)));
        check("Bootstrapper disappearance requires fresh absence confirmation", !exits.Observe(Frame(37)));
        check("A second genuine exit is recognized", exits.Observe(Frame(42)));
        exits.Observe(Frame(50, game: true));
        exits.Observe(Frame(51));
        check("Long observation gaps cannot confirm process exit", !exits.Observe(Frame(100)));
        check("Fresh observations after a gap can confirm exit", exits.Observe(Frame(105)));
        exits.Reset();
        check("A new runner session does not inherit game-exit evidence", !exits.Observe(Frame(106)) && !exits.Observe(Frame(111)));

        var session = new RunnerSession();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        var recognizer = new ScreenRecognizer();
        Observation Lobby(double time) => new(epoch.AddSeconds(time), 1920, 1080,
            [new("MAP", new(100, 50, 80, 30)), new("COMMAND", new(300, 50, 160, 30)), new("ARMORY", new(500, 50, 160, 30))]);
        session.Start(RunnerMode.Runner, profile, 2, epoch);
        var frame = Lobby(0);
        session.Observe(frame, recognizer.Recognize(frame));
        frame = Lobby(1);
        var action = session.Observe(frame, recognizer.Recognize(frame)).Action!;
        session.Controller.Dispatched(action, frame.CapturedAt);
        check("Menu click is pending before abrupt game exit", session.Controller.Pending?.Step == NavigationStep.OpenMap);
        session.RecoverAfterProcessExit(epoch.AddSeconds(7));
        check("Exit without a dialog clears the obsolete menu transition", session.Controller.Pending is null && session.Controller.Status == ControllerStatus.Observing);
        frame = Lobby(30);
        check("Relaunch does not reuse the old click or its timeout", session.Observe(frame, recognizer.Recognize(frame)).Action is null && session.Controller.Status == ControllerStatus.Observing);
        frame = Lobby(31);
        check("Relaunched game navigation resumes from fresh evidence", session.Observe(frame, recognizer.Recognize(frame)).Action?.Step == NavigationStep.OpenMap);
        session.Controller.Pause("Inspect account access");
        session.RecoverAfterProcessExit(epoch.AddSeconds(40), resumeNavigation: false);
        check("Game exit cannot override an unrelated attention pause", session.Controller.Status == ControllerStatus.PausedForAttention
            && session.Controller.Detail == "Inspect account access");
        session.Stop("User stop");
        session.RecoverAfterProcessExit(epoch.AddSeconds(45));
        check("Process exit never restarts a user-stopped runner", !session.IsRunning && session.Controller.Status == ControllerStatus.Stopped);
    }
}
