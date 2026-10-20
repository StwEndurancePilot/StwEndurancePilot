using EndurancePilot.Core;

internal static class RunnerSessionScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var session = new RunnerSession();
        var recognizer = new ScreenRecognizer();
        double seconds = 0;
        Observation Frame(params string[] text) => new(epoch, 1920, 1080,
            text.Select((t, i) => new TextRegion(t, new(100, 100 + i * 60, 350, 30))).ToArray());
        SessionUpdate Feed(Observation sample)
        {
            seconds += 2;
            var frame = sample with
            {
                CapturedAt = epoch.AddSeconds(seconds)
            };
            return session.Observe(frame, recognizer.Recognize(frame));
        }
        void Dispatch(Observation sample, NavigationStep expected)
        {
            ClickAction? action = null;
            for (int i = 0; i < 10 && action is null; i++)
            {
                action = Feed(sample).Action;
            }

            check("Integrated flow dispatches " + expected, action?.Step == expected);
            session.Controller.Dispatched(action!, epoch.AddSeconds(seconds));
        }
        var homebase = Frame("Storm Shield", "Activate Storm Shield to Defend");
        var backpack = new Observation(epoch, 1920, 1080, [
            new("Loadout",new(100,20,100,25)),new("Objectives",new(240,20,120,25)),
            new("Quests",new(400,20,100,25)),new("Storm Shield",new(550,20,180,25))]);
        var console = Frame("Storm Shield Endurance", "Daily Wargames", "Storage");
        var setup = Frame("Storm Shield", "Start Endurance", "Wait for Assistance") with
        {
            Visuals = [new("assistance-unchecked", new(50, 220, 25, 25), .99, .3)]
        };
        var countdown = Frame("Endurance Defense: Wave 1 starts in:", "0:20");
        var victory = Frame("VICTORY", "TEAM SCORE", "CONTINUE");
        var rewards = Frame("MISSION REWARDS", "Gold 50", "CLAIM");
        var lobby = Frame("MAP", "COMMAND", "ARMORY");
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 2, epoch);
        Dispatch(homebase, NavigationStep.OpenBackpack);
        Dispatch(backpack, NavigationStep.SelectStormShieldTab);
        Dispatch(console, NavigationStep.SelectEndurance);
        Dispatch(setup, NavigationStep.StartEndurance);
        Feed(countdown);
        Feed(countdown);
        check("Start transition reaches countdown without inventing a wave", session.Controller.Pending is null && session.Tracker.Current is null);
        Feed(Frame("Endurance: Wave 29"));
        Feed(Frame("Endurance: Wave 29"));
        Feed(Frame("Endurance: Wave 30"));
        Feed(Frame("Endurance: Wave 30"));
        check("Integrated wave thirty remains in progress", session.Progress.AttemptsEnded == 0);
        Dispatch(victory, NavigationStep.ContinueResults);
        check("Confirmed victory records one successful attempt", session.Progress.AttemptsEnded == 1 && session.Progress.CompletedRuns == 1);
        Dispatch(rewards, NavigationStep.CollectMissionRewards);
        Feed(lobby);
        Feed(lobby);
        Dispatch(lobby, NavigationStep.OpenMap);
        check("A remaining attempt permits navigation after reward cleanup", session.IsRunning && session.Progress.Phase == SessionPhase.Running);

        var map = Frame("MAP", "COMMAND", "ARMORY", "Twine Peaks", "Recommended Power");
        Feed(map);
        Feed(map);
        Feed(Frame("Endurance: Wave 29"));
        Feed(Frame("Endurance: Wave 29"));
        Feed(Frame("Endurance: Wave 30"));
        Feed(Frame("Endurance: Wave 30"));
        Dispatch(victory, NavigationStep.ContinueResults);
        check("Final victory enters reward cleanup instead of stopping early", session.Progress.CompletedRuns == 2 && session.Progress.Phase == SessionPhase.FinishingRewards && session.IsRunning);
        Dispatch(rewards, NavigationStep.CollectMissionRewards);
        bool completed = false;
        for (int i = 0; i < 5; i++)
        {
            var update = Feed(lobby);
            check("Final reward return cannot propose another launch", update.Action is null);
            completed |= update.Complete;
        }
        check("Final stable lobby completes the integrated session", completed && !session.IsRunning && session.Progress.Phase == SessionPhase.Complete);
        check("Completed coordinator ignores further frames", Feed(homebase).Action is null && session.Progress.AttemptsEnded == 2);

        session.Start(RunnerMode.Observe, new(), 1, epoch.AddSeconds(seconds));
        Feed(Frame("Endurance: Wave 4"));
        Feed(Frame("Endurance: Wave 4"));
        check("Observe mode tracks waves without navigation", Feed(homebase).Action is null && session.Tracker.ConfirmedWave == 4);
        session.Stop("User stop");
        check("Stopped session cannot emit input or end a run", Feed(victory) is { Action: null, Outcome: null, Complete: false });
        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 1, epoch.AddSeconds(seconds));
        var interrupted = session.Interrupt(epoch.AddSeconds(++seconds), "Process exited");
        check("Process exit closes the final attempt and stops navigation", interrupted?.Outcome == RunOutcome.Interrupted
            && session.Progress.InterruptedRuns == 1 && !session.IsRunning && Feed(homebase).Action is null);

        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), 2, epoch.AddSeconds(seconds));
        Dispatch(homebase, NavigationStep.OpenBackpack);
        var recovered = session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Crash before a tracked run clears stale navigation without spending a run attempt", recovered is null
            && session.Controller.Pending is null && session.Controller.Status == ControllerStatus.Observing
            && session.IsRunning && session.Progress.AttemptsEnded == 0);
        Feed(Frame("Endurance: Wave 4"));
        Feed(Frame("Endurance: Wave 4"));
        recovered = session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Mid-run crash records an interruption and keeps remaining attempts", recovered?.Outcome == RunOutcome.Interrupted
            && session.Progress.InterruptedRuns == 1 && session.Progress.CompletedRuns == 0
            && session.IsRunning && !session.Controller.RunInProgress);
        session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Repeated crash-exit observations cannot double-count a run", session.Progress.AttemptsEnded == 1);
        Feed(Frame("Endurance: Wave 1"));
        Feed(Frame("Endurance: Wave 1"));
        check("Wave one after crash belongs to a new run", session.Tracker.Current is { Outcome: RunOutcome.InProgress, HighestWave: 1 });
        recovered = session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Final allowed attempt crashing stops recovery navigation", recovered?.Outcome == RunOutcome.Interrupted
            && session.Progress.AttemptsEnded == 2 && session.Progress.Phase == SessionPhase.Complete && !session.IsRunning);
        session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Crash recovery cannot reactivate a stopped session", !session.IsRunning && session.Controller.Status == ControllerStatus.Stopped);

        session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), null, epoch.AddSeconds(seconds));
        for (int run = 0; run < 3; run++)
        {
            Feed(Frame("Endurance: Wave 29"));
            Feed(Frame("Endurance: Wave 29"));
            Feed(Frame("Endurance: Wave 30"));
            Feed(Frame("Endurance: Wave 30"));
            Dispatch(victory, NavigationStep.ContinueResults);
            Dispatch(rewards, NavigationStep.CollectMissionRewards);
            Feed(lobby);
            Feed(lobby);
            Dispatch(lobby, NavigationStep.OpenMap);
            Feed(map);
            Feed(map);
        }
        check("Indefinite coordinator repeats victory, rewards and navigation", session.IsRunning
            && session.Progress.CompletedRuns == 3 && session.Progress.AttemptLimit is null
            && !session.Controller.FinishRewardsOnly);
        Feed(Frame("Endurance: Wave 4"));
        Feed(Frame("Endurance: Wave 4"));
        recovered = session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Indefinite coordinator can recover an interrupted attempt", recovered?.Outcome == RunOutcome.Interrupted
            && session.IsRunning && session.Progress.InterruptedRuns == 1 && session.Progress.Phase == SessionPhase.Running);
        session.Stop("User stop");
        check("Manual Stop also stops indefinite sessions", !session.IsRunning && Feed(homebase).Action is null);
        session.RecoverAfterCrash(epoch.AddSeconds(++seconds));
        check("Crash recovery cannot restart a manually stopped indefinite session", !session.IsRunning);
    }
}
