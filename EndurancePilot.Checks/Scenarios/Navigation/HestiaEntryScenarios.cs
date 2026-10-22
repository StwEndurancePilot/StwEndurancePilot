using EndurancePilot.Core;

internal static class HestiaEntryScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var pause = new InteractionPause();
        pause.Begin();
        check("Background inactivity cannot resume input", !pause.Ready(epoch, false, false, 0, 0)
            && !pause.Ready(epoch.AddSeconds(10), false, false, 0, 0) && pause.Active);
        check("Returning focus starts a fresh idle wait", !pause.Ready(epoch.AddSeconds(11), true, false, 0, 0));
        check("Held input blocks automatic resume", !pause.Ready(epoch.AddSeconds(14), true, true, 0, 0));
        check("Releasing input starts a fresh wait", !pause.Ready(epoch.AddSeconds(15), true, false, 0, 0));
        check("Pointer movement restarts idle wait", !pause.Ready(epoch.AddSeconds(17), true, false, 1, 0));
        pause.Ready(epoch.AddSeconds(18), true, false, 1, 0);
        check("Less than two idle seconds stays paused", !pause.Ready(epoch.AddSeconds(19), true, false, 1, 0));
        check("Two idle seconds resumes automatically", pause.Ready(epoch.AddSeconds(20), true, false, 1, 0) && !pause.Active);
        pause.Begin();
        pause.Ready(epoch.AddSeconds(21), true, false, 1, 0);
        pause.Begin();
        check("Explicit resume reacquires idle evidence", !pause.Ready(epoch.AddSeconds(25), true, false, 1, 0));
        pause.Clear();
        check("Stop clears temporary pause", !pause.Active);
        pause.Begin();
        check("Confirmed background hub begins idle wait", !pause.Ready(epoch, false, false, 0, 0, resumeHub: true));
        check("Background hub waits ten seconds", !pause.Ready(epoch.AddSeconds(9), false, false, 0, 0, resumeHub: true));
        check("Confirmed background hub can resume after inactivity", pause.Ready(epoch.AddSeconds(10), false, false, 0, 0, resumeHub: true));
        pause.Begin();
        check("OS-confirmed idle resumes without a second countdown", pause.Ready(epoch.AddSeconds(10), false, false, 0, 0,
            resumeHub: true, backgroundIdleSeconds: 0));
        pause.Begin();
        pause.Ready(epoch, false, false, 0, 0, resumeHub: true);
        check("User activity postpones hub focus recovery", !pause.Ready(epoch.AddSeconds(10), false, true, 0, 0, resumeHub: true));
        pause.Ready(epoch.AddSeconds(11), false, false, 0, 0, resumeHub: true);
        check("Losing hub evidence blocks automatic focus recovery", !pause.Ready(epoch.AddSeconds(22), false, false, 0, 0));
        pause.Clear();
        var recognizer = new ScreenRecognizer();
        Observation Frame(double t, params string[] labels) => new(epoch.AddSeconds(t), 1920, 1080,
            labels.Select((label, i) => new TextRegion(label, new(100 + i * 250, 40, 180, 25))).ToArray());
        var mode = Frame(0) with
        {
            Regions = [new("SAVE THE WORLD", new(100, 700, 450, 30)), new("PLAY", new(200, 900, 200, 30))]
        };
        var hub = Frame(900) with
        {
            Visuals = [new("player-health-bar", new(80, 900, 280, 24), 1, 1)]
        };
        var oldMenu = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
        oldMenu.Start();
        var prompt = Frame(0, "E VIEW QUEST LOG");
        oldMenu.Observe(prompt, recognizer.Recognize(prompt));
        var open = oldMenu.Observe(prompt with
        {
            CapturedAt = epoch.AddSeconds(.2)
        }, recognizer.Recognize(prompt))!;
        check("Quest-prompt route opens Tab", open?.Step == NavigationStep.OpenQuestMenu);
        oldMenu.Dispatched(open!, epoch.AddSeconds(.2));
        oldMenu.Observe(hub, recognizer.Recognize(hub));
        check("Verified HUD supersedes stale quest-menu action",
            oldMenu.Observe(hub with
            {
                CapturedAt = epoch.AddSeconds(908)
            }, recognizer.Recognize(hub))?.Step == NavigationStep.RetryQuestMenu);
        foreach (var step in new[]{NavigationStep.EnterSaveTheWorld,NavigationStep.OpenQuestMenu,
            NavigationStep.ConfirmSaveTheWorld,NavigationStep.SelectSaveTheWorld})
        {
            foreach (var destination in new[] { ScreenKind.SaveTheWorldLobby, ScreenKind.Backpack, ScreenKind.WorldMap, ScreenKind.MissionMap })
            {
                var action = new ClickAction(Guid.NewGuid(), "Entry", ScreenKind.SaveTheWorldHub, new(100, 100, 100, 30), 1920, 1080, epoch, Step: step);
                check($"{step} accepts actual next menu {destination}", NavigationPlanner.IsSatisfied(action, Frame(1), new(destination, [], [])));
            }
        }

        check("Entry menu freshness still expires", NavigationTiming.MaximumObservationAge(NavigationStep.OpenMap) == TimeSpan.FromSeconds(30));
        check("Ambiguous menu cannot use extended confirmation", NavigationTiming.ConfirmationGap(
            new ScreenEvidence(ScreenKind.Backpack, [], []) with
            {
                AllowsInput = false
            }) == TimeSpan.FromSeconds(6));
        foreach (int scanSeconds in new[] { 7, 10, 15 })
        {
            var repeat = new AutomationController { ErrorRecoveryOnly = false, ClaimRewards = true, Profile = new(HomebaseZone.TwinePeaks, true) };
            repeat.Start();
            var rewards = Frame(0, "MISSION REWARDS", "CONTINUE");
            var rewardScreen = recognizer.Recognize(rewards);
            repeat.Observe(rewards, rewardScreen);
            var collect = repeat.Observe(rewards with
            {
                CapturedAt = epoch.AddSeconds(1)
            }, rewardScreen);
            check($"Reward claim dispatched before slow Homebase ({scanSeconds}s)", collect?.Step == NavigationStep.CollectMissionRewards);
            repeat.Dispatched(collect!, epoch.AddSeconds(1));
            var landing = prompt with
            {
                Visuals = hub.Visuals
            };
            repeat.Observe(landing with
            {
                CapturedAt = epoch.AddSeconds(1 + scanSeconds)
            }, recognizer.Recognize(landing));
            repeat.Observe(landing with
            {
                CapturedAt = epoch.AddSeconds(1 + scanSeconds * 2)
            }, recognizer.Recognize(landing));
            check($"Slow Homebase confirms reward collection ({scanSeconds}s)", repeat.Pending is null && repeat.Status == ControllerStatus.Observing);
            ClickAction? next = null;
            for (int i = 3; i <= 6 && next is null; i++)
            {
                next = repeat.Observe(landing with
                {
                    CapturedAt = epoch.AddSeconds(1 + scanSeconds * i)
                }, recognizer.Recognize(landing));
            }

            check($"Repeat run opens Homebase menu after rewards ({scanSeconds}s)", next?.Step is NavigationStep.RetryQuestMenu or NavigationStep.OpenQuestMenu);
            var progress = new SessionProgress(1);
            progress.Record(new(Guid.NewGuid(), epoch, epoch.AddSeconds(1), 2, RunOutcome.ReachedTarget));
            var landingScreen = recognizer.Recognize(landing);
            progress.Observe(landing with
            {
                CapturedAt = epoch.AddSeconds(20)
            }, landingScreen, false);
            progress.Observe(landing with
            {
                CapturedAt = epoch.AddSeconds(20 + scanSeconds)
            }, landingScreen, false);
            check($"Finite run still stops after slow Homebase ({scanSeconds}s)", progress.Phase == SessionPhase.Complete);
            var receipt = new MissionRewardTracker();
            receipt.Observe(rewards, rewardScreen, null);
            receipt.Observe(landing with
            {
                CapturedAt = epoch.AddSeconds(20)
            }, landingScreen, null);
            receipt.Observe(landing with
            {
                CapturedAt = epoch.AddSeconds(20 + scanSeconds)
            }, landingScreen, null);
            check($"Slow Homebase finalizes reward notification ({scanSeconds}s)", receipt.IsComplete);
        }
        check("Unverified Homebase cannot use extended confirmation", NavigationTiming.ConfirmationGap(
            new ScreenEvidence(ScreenKind.SaveTheWorldHub, [], []) with
            {
                AllowsInput = false
            }) == TimeSpan.FromSeconds(6));
        foreach (bool backpack in new[] { false, true })
        {
            foreach (bool missedHud in new[] { false, true })
            {
                var c = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
                c.Start();
                var modeEvidence = new ScreenEvidence(ScreenKind.ModeSelection, [], []);
                c.Observe(mode, modeEvidence);
                var join = c.Observe(mode with
                {
                    CapturedAt = epoch.AddSeconds(.2)
                }, modeEvidence)!;
                c.Dispatched(join, epoch.AddSeconds(.2));
                if (!missedHud)
                {
                    c.Observe(hub, recognizer.Recognize(hub));
                    var tab = c.Observe(hub with
                    {
                        CapturedAt = epoch.AddSeconds(908)
                    }, recognizer.Recognize(hub));
                    check($"Hestia after slow load sends Tab {backpack}", tab?.Step == NavigationStep.RetryQuestMenu);
                    c.Dispatched(tab!, epoch.AddSeconds(914));
                    check("Blank frame after Tab cannot toggle it closed", c.Observe(Frame(920), recognizer.Recognize(Frame(920))) is null);
                }
                var menu = backpack ? Frame(930, "LOADOUT", "OBJECTIVES", "QUESTS", "STORM SHIELD")
                    : Frame(930, "MAP", "COMMAND", "ARMORY");
                c.Observe(menu, recognizer.Recognize(menu));
                ClickAction? next = null;
                foreach (int t in new[] { 938, 946, 954 })
                {
                    next = c.Observe(menu with
                    {
                        CapturedAt = epoch.AddSeconds(t)
                    }, recognizer.Recognize(menu));
                    if (next is not null)
                    {
                        break;
                    }
                }
                check($"Actual menu wins after join (backpack={backpack}, missed HUD={missedHud})",
                    next?.Step == (backpack ? NavigationStep.SelectStormShieldTab : NavigationStep.OpenMap));
                c.Dispatched(next!, next!.ObservedAt.AddSeconds(6));
                check("Slow menu OCR can dispatch with live target recheck", c.Status == ControllerStatus.AwaitingTransition);
            }
        }
    }
}
