using EndurancePilot.Core;

internal static class TargetWaveScenarios
{
    public static void RunCaptures(string directory, Action<string, bool> check)
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var files = Directory.GetFiles(directory, "backpack-console-active-2048x1152*.json");
        check("Target exit capture fixtures exist", files.Length > 0);
        foreach (string path in files)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var frame = System.Text.Json.JsonSerializer.Deserialize<Observation>(doc.RootElement.GetProperty("Observation").GetRawText(), options)!;
            var evidence = new ScreenRecognizer().Recognize(frame);
            var plan = TargetWaveExit.Decide(frame, evidence).Plan;
            double scale = Math.Min(frame.Width / 2048d, frame.Height / 1152d);
            double x = (frame.Width - 2048 * scale) / 2, y = (frame.Height - 1152 * scale) / 2;
            check("Captured exit button: " + Path.GetFileName(path), evidence.Kind == ScreenKind.StormShieldConsole
                && plan is { Step: NavigationStep.LeaveTargetMission }
                && plan.Target.CenterX >= x + 1320 * scale && plan.Target.CenterX <= x + 1560 * scale
                && plan.Target.CenterY >= y + 780 * scale && plan.Target.CenterY <= y + 835 * scale);
        }
    }
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        Observation Frame(params string[] lines) => new(epoch, 1920, 1080, lines.Select((s, i) => new TextRegion(s, new(100, 100 + i * 60, 350, 30))).ToArray());
        var backpack = new Observation(epoch, 1920, 1080, [new("Loadout",new(100,20,100,25)),new("Objectives",new(240,20,120,25)),
            new("Quests",new(400,20,100,25)),new("Storm Shield",new(550,20,180,25))]);
        var console = Frame("Storm Shield Endurance", "Daily Wargames", "Storage", "LEAVE WITH PARTY");
        var confirm = new Observation(epoch, 1920, 1080, [new("Are you sure you want to leave the mission?",new(400,300,600,40)),
            new("LEAVE",new(600,600,150,40)),new("CANCEL",new(900,600,150,40))]);
        var lobby = Frame("MAP", "COMMAND", "ARMORY");
        var liveConfirmation = confirm with
        {
            Regions = [
            new("LEAVE STORM SNIELD?",new(699,341,560,43)),
            new("Leave the Storm Shield and return to Homebase.",new(699,471,710,30)),
            new("QUIT",new(930,990,100,40)),new("CANCEL",new(1530,990,180,40))]
        };
        check("Live Quit dialog is recognized despite title OCR typo", recognizer.Recognize(liveConfirmation).Kind == ScreenKind.LeaveConfirmation);
        check("Live Quit dialog selects affirmative control", TargetWaveExit.Decide(liveConfirmation, recognizer.Recognize(liveConfirmation)).Plan?.Step == NavigationStep.ConfirmTargetLeave);
        var rewards = Frame("MISSION REWARDS", "Gold 50", "CLAIM");
        var loading = Frame("LOADING");
        var wave26 = Frame("Endurance: Wave 26");
        var wave27 = Frame("Endurance: Wave 27");
        var repeatedWave = Frame("Endurance: Wave 27", "Endurance: Wave 27");
        var repeatedPlan = TargetWaveExit.Decide(repeatedWave, recognizer.Recognize(repeatedWave)).Plan;
        check("Agreeing separate HUD wave labels allow keyboard exit", repeatedPlan?.Key == NavigationKey.Tab);
        check("Wave anchor remains stable across OCR ordering",
            TargetWaveExit.Decide(repeatedWave with
            {
                Regions = repeatedWave.Regions.Reverse().ToArray()
            }, recognizer.Recognize(repeatedWave)).Plan?.Target == repeatedPlan?.Target);
        var conflictingWave = Frame("Endurance: Wave 27", "Endurance: Wave 28");
        check("Conflicting HUD labels cannot anchor exit", TargetWaveExit.Decide(conflictingWave, recognizer.Recognize(conflictingWave)).Plan is null);
        var countdown = Frame("Endurance Defense: Wave 27 starts in:", "0:20");
        var session = new RunnerSession();
        double seconds = 0;
        SessionUpdate Feed(Observation sample)
        {
            var frame = sample with
            {
                CapturedAt = epoch.AddSeconds(seconds += 2)
            };
            return session.Observe(frame, recognizer.Recognize(frame));
        }
        ClickAction Dispatch(Observation sample, NavigationStep expected)
        {
            ClickAction? action = null;
            for (int i = 0; i < 12 && action is null; i++)
            {
                action = Feed(sample).Action;
            }

            check("Target flow dispatches " + expected, action?.Step == expected);
            session.Controller.Dispatched(action!, epoch.AddSeconds(seconds));
            return action!;
        }
        void Start(int target = 26, int? limit = null, RunnerMode mode = RunnerMode.Runner)
        {
            session = new RunnerSession();
            session.Start(mode, new(HomebaseZone.TwinePeaks, true, target), limit, epoch.AddSeconds(seconds));
        }
        foreach (bool background in new[] { false, true })
        {
            session = new RunnerSession();
            session.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true, 26, EndRunWhenTargetReached: false), 1, epoch.AddSeconds(seconds));
            for (int i = 0; i < 4; i++)
            {
                var sample = (i < 2 ? wave26 : wave27) with
                {
                    CapturedAt = epoch.AddSeconds(seconds += 2)
                };
                var update = background ? session.ObserveBackground(sample, recognizer.Recognize(sample))
                    : session.Observe(sample, recognizer.Recognize(sample));
                check($"Disabled target exit sends no leave action (background={background}, frame={i})", update.Action is null);
            }
            check($"Disabled target exit continues past target (background={background})",
                !session.Controller.TargetExitRequested && session.Tracker.ConfirmedWave == 27
                && session.Progress.AttemptsEnded == 0 && session.IsRunning);
        }
        Start(limit: 1);
        Feed(wave26);
        Feed(wave26);
        Dispatch(wave27, NavigationStep.OpenTargetExitBackpack);
        var defeat = Frame("DEFEAT", "TEAM SCORE!", "MATCHMAKE INTO HOMEBASE", "MISSION STATS");
        Feed(defeat);
        check("One result frame preserves the pending target navigation", session.Controller.Pending?.Step == NavigationStep.OpenTargetExitBackpack);
        Feed(defeat);
        check("Confirmed defeat clears obsolete target navigation", session.Controller.Pending is null
            && session.Progress.Phase == SessionPhase.FinishingRewards);
        var home = Feed(defeat).Action;
        check("Final attempt still presses C to Matchmake into Homebase after defeat", home is { Step: NavigationStep.ReturnToHomebase, Key: NavigationKey.C } && session.IsRunning);
        Start();
        Feed(wave26);
        Feed(wave26);
        check("Target wave beginning cannot trigger leave", !session.Controller.TargetExitRequested && Feed(wave26).Action is null);
        Feed(countdown);
        Feed(countdown);
        check("Next-wave countdown cannot trigger leave", !session.Controller.TargetExitRequested && session.Tracker.ConfirmedWave == 26);
        Feed(wave27);
        check("One next-wave reading cannot trigger leave", !session.Controller.TargetExitRequested);
        Feed(Frame("Endurance: Wave 27", "Wave 28"));
        Feed(Frame("Endurance: Wave 27", "Wave 28"));
        check("Disagreeing next-wave reads cannot trigger leave", !session.Controller.TargetExitRequested);
        var open = Dispatch(wave27, NavigationStep.OpenTargetExitBackpack);
        check("Exit opens backpack with a guarded Tab action", open.Key == NavigationKey.Tab && InteractionPrompts.IsAllowedKeyboardAction(open));
        check("Reaching threshold alone is not a completed attempt", session.Progress.CompletedRuns == 0 && session.Progress.AttemptsEnded == 0);
        Dispatch(backpack, NavigationStep.SelectStormShieldTab);
        Dispatch(console, NavigationStep.LeaveTargetMission);
        check("Leave confirmation overrides background gameplay", recognizer.Recognize(confirm with
        {
            Regions = confirm.Regions.Concat(wave27.Regions).ToArray()
        }).Kind == ScreenKind.LeaveConfirmation);
        Dispatch(confirm, NavigationStep.ConfirmTargetLeave);
        Feed(loading);
        Feed(loading);
        check("Loading is not proof of target exit", session.Progress.AttemptsEnded == 0 && session.Controller.AwaitingTargetExit);
        Feed(rewards);
        Feed(rewards);
        check("Confirmed target exit records a distinct success", session.Tracker.Current?.Outcome == RunOutcome.ReachedTarget && session.Progress.CompletedRuns == 1 && session.Progress.AttemptsEnded == 1);
        Dispatch(rewards, NavigationStep.CollectMissionRewards);
        Feed(lobby);
        Feed(lobby);
        Dispatch(lobby, NavigationStep.OpenMap);
        check("Indefinite target runs continue after rewards", session.IsRunning && !session.Controller.TargetExitRequested);

        Start(limit: 1);
        Dispatch(wave27, NavigationStep.OpenTargetExitBackpack);
        Dispatch(backpack, NavigationStep.SelectStormShieldTab);
        Dispatch(console, NavigationStep.LeaveTargetMission);
        Feed(lobby);
        Feed(lobby);
        check("Direct menu return verifies target exit", session.Progress.CompletedRuns == 1);
        for (int i = 0; i < 4; i++)
        {
            check("Final target exit cannot relaunch", Feed(lobby).Action is null);
        }

        check("Finite target session stops after stable menu", !session.IsRunning && session.Progress.AttemptsEnded == 1);

        Start();
        Dispatch(wave27, NavigationStep.OpenTargetExitBackpack);
        Dispatch(backpack, NavigationStep.SelectStormShieldTab);
        Dispatch(console, NavigationStep.LeaveTargetMission);
        var sleep = Frame("FORTNITE IS IN SLEEP MODE", "KEEP PLAYING", "CLOSE FORTNITE");
        Dispatch(sleep, NavigationStep.KeepPlaying);
        Feed(console);
        Feed(console);
        check("Sleep recovery retains the unconfirmed exit", session.Controller.AwaitingTargetExit && session.Progress.AttemptsEnded == 0);
        Feed(lobby);
        Feed(lobby);
        check("Target exit still completes after waking", session.Progress.CompletedRuns == 1);
        Feed(lobby);
        Feed(lobby);
        check("Repeated menus cannot double-count a target exit", session.Progress.AttemptsEnded == 1);
        Feed(Frame("Endurance: Wave 1"));
        Feed(Frame("Endurance: Wave 1"));
        check("Next run resets target-exit intent", session.Tracker.ConfirmedWave == 1 && !session.Controller.TargetExitRequested);

        Start();
        Dispatch(wave27, NavigationStep.OpenTargetExitBackpack);
        var crash = session.RecoverAfterCrash(epoch.AddSeconds(seconds += 2));
        check("Crash during target exit stays an interruption", crash?.Outcome == RunOutcome.Interrupted
            && session.Progress.CompletedRuns == 0 && !session.Controller.TargetExitRequested && session.IsRunning);

        Start();
        Dispatch(wave27, NavigationStep.OpenTargetExitBackpack);
        var panel = Frame("ENDURANCE", "POWER LEVEL", "Drop your Storm Shield and survive at least 4 waves");
        var back = Dispatch(panel, NavigationStep.BackToStormShield);
        check("Already-open endurance panel returns via guarded Escape", back.Key == NavigationKey.Escape && InteractionPrompts.IsAllowedKeyboardAction(back));
        Dispatch(console, NavigationStep.LeaveTargetMission);
        session.Controller.InvalidateObservation();
        Feed(Frame("UNKNOWN"));
        Feed(Frame("UNKNOWN"));
        check("Focus/unknown frames cannot complete the exit", session.Controller.AwaitingTargetExit && session.Progress.CompletedRuns == 0);

        Start();
        Feed(Frame("Endurance: Wave 1"));
        Feed(Frame("Endurance: Wave 1"));
        Feed(wave27);
        Feed(wave27);
        check("Large OCR jump needs longer confirmation before exit", !session.Controller.TargetExitRequested);
        Feed(wave27);
        Feed(wave27);
        check("Sustained large jump can reacquire target", session.Controller.TargetExitRequested);
        session.Stop("F10");
        check("Stop cancels target exit and all pending actions", !session.Controller.TargetExitRequested && Feed(console).Action is null);

        Start(target: 30);
        Feed(Frame("Endurance: Wave 30"));
        Feed(Frame("Endurance: Wave 30"));
        check("Default target 30 never tries to leave on wave 30", !session.Controller.TargetExitRequested && Feed(console).Action is null);
        Feed(Frame("VICTORY", "TEAM SCORE", "CONTINUE"));
        Feed(Frame("VICTORY", "TEAM SCORE", "CONTINUE"));
        check("Default target preserves wave-30 victory accounting", session.Progress.CompletedRuns == 1 && session.Tracker.Current?.Outcome == RunOutcome.Completed30);
        foreach (var mode in new[] { RunnerMode.Observe, RunnerMode.Recovery })
        {
            Start(mode: mode);
            Feed(wave27);
            Feed(wave27);
            check(mode + " does not request target exit", !session.Controller.TargetExitRequested && Feed(console).Action is null);
        }
        Start();
        check("Unrequested leave dialog stays inert", Feed(confirm).Action is null && Feed(confirm).Action is null);
        var restartRequired = Frame("ENDURANCE", "POWER LEVEL",
            "Drop your Storm Shield and survive at least 4 waves against a relentless assault!",
            "Restart Match to Start Endurance");
        var restartScreen = recognizer.Recognize(restartRequired);
        check("Restart Match message is not a Start Endurance button",
            restartScreen.Kind == ScreenKind.EnduranceRestartRequired
            && NavigationPlanner.Decide(restartRequired, restartScreen, new(HomebaseZone.TwinePeaks, true)).Plan?.Step != NavigationStep.StartEndurance);
        check("Truncated restart message still requires a new match",
            recognizer.Recognize(Frame("ENDURANCE", "POWER LEVEL",
                "Drop your Storm Shield and survive at least 4 waves", "Restart Match to Start Em"))
                .Kind == ScreenKind.EnduranceRestartRequired);
        var restartBack = Dispatch(restartRequired, NavigationStep.BackToStormShield);
        check("Restart requirement uses the verified Escape route",
            restartBack.Key == NavigationKey.Escape && InteractionPrompts.IsAllowedKeyboardAction(restartBack)
            && session.Controller.TargetExitRequested);
        Dispatch(console, NavigationStep.LeaveTargetMission);
        Dispatch(confirm, NavigationStep.ConfirmTargetLeave);
        Feed(lobby);
        Feed(lobby);
        check("Restart exit is confirmed without inventing a completed run",
            !session.Controller.TargetExitRequested && session.Progress.AttemptsEnded == 0 && session.IsRunning);
        Dispatch(lobby, NavigationStep.OpenMap);
        check("Restart returns to the Homebase launch route", session.IsRunning);

        Start(target: 30);
        Feed(Frame("Endurance: Wave 10"));
        Feed(Frame("Endurance: Wave 10"));
        Dispatch(restartRequired, NavigationStep.BackToStormShield);
        Dispatch(console, NavigationStep.LeaveTargetMission);
        Dispatch(confirm, NavigationStep.ConfirmTargetLeave);
        Feed(lobby);
        Feed(lobby);
        check("Restart after an unverified active wave records an interruption once",
            session.Tracker.Current?.Outcome == RunOutcome.Interrupted && session.Progress.AttemptsEnded == 1
            && session.Progress.CompletedRuns == 0 && session.IsRunning);

        Start();
        check("Generic Yes/Cancel is not a leave dialog", recognizer.Recognize(confirm with
        {
            Regions = confirm.Regions.Skip(1).ToArray()
        }).Kind != ScreenKind.LeaveConfirmation);
        check("Word fragments cannot become leave controls", TargetWaveExit.LeaveControl(console with { Regions = [new("LEAVE", new(100, 100, 80, 30), "overview-word")] }) is null);
        check("Observed WITNPARTV OCR can identify console exit", TargetWaveExit.LeaveControl(console with { Regions = [new("LEAVE WITNPARTV", new(100, 100, 250, 30))] }) is not null);
        var ambiguous = console with
        {
            Regions = console.Regions.Append(new TextRegion("LEAVE", new(900, 600, 100, 40))).ToArray()
        };
        check("Ambiguous leave controls cannot click", TargetWaveExit.Decide(ambiguous, recognizer.Recognize(ambiguous)).Plan is null);
        foreach (int target in new[] { 0, 31 })
        {
            bool rejected = false;
            try
            {
                Start(target);
            }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check("Invalid target rejected: " + target, rejected);
        }
    }
}
