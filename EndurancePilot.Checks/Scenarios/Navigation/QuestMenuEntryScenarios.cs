using EndurancePilot.Core;

internal static class QuestMenuEntryScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        Observation Frame(double seconds, bool health = false) => new(epoch.AddSeconds(seconds), 1920, 1080, [],
            health ? [new("player-health-bar", new(80, 900, 280, 24), 1, 1)] : []);
        ScreenEvidence Evidence(ScreenKind kind) => new(kind, [], []);
        var modeText = new[] { new TextRegion("SAVE THE WORLD", new(100, 700, 450, 30)), new TextRegion("PLAY", new(200, 900, 200, 30)) };
        AutomationController Fresh()
        {
            var c = new AutomationController { ErrorRecoveryOnly = false, Profile = new(HomebaseZone.TwinePeaks, true) };
            c.Start();
            return c;
        }
        AutomationController Joined()
        {
            var c = Fresh();
            c.Observe(Frame(0) with
            {
                Regions = modeText
            }, Evidence(ScreenKind.ModeSelection));
            var join = c.Observe(Frame(.2) with
            {
                Regions = modeText
            }, Evidence(ScreenKind.ModeSelection))!;
            c.Dispatched(join, epoch.AddSeconds(.2));
            return c;
        }
        var idle = Fresh();
        var persistent = Fresh();
        int hubAttempts = 0;
        for (int second = 0; second < 1200; second++)
        {
            var action = persistent.Observe(Frame(second, true), Evidence(ScreenKind.SaveTheWorldHub));
            if (action is not null)
            {
                persistent.Dispatched(action, epoch.AddSeconds(second));
                hubAttempts++;
            }
        }
        check("Confirmed hub keeps retrying for twenty minutes without timeout",
            hubAttempts > 100 && persistent.Status == ControllerStatus.Observing && persistent.QuestMenuTimeouts == 0);
        check("Hub retries remain paced", hubAttempts <= 600);
        check("Hub retry stops as soon as backpack opens",
            persistent.Observe(Frame(1201, true), Evidence(ScreenKind.Backpack))?.Step != NavigationStep.RetryQuestMenu
            && !persistent.AwaitingQuestMenu);
        persistent.Stop();
        check("Explicit stop prevents indefinite hub retries",
            persistent.Observe(Frame(1202, true), Evidence(ScreenKind.SaveTheWorldHub)) is null);
        foreach (double scanSeconds in new[] { 6d, 15d, 29d })
        {
            var slow = Joined();
            slow.Observe(Frame(2, true), Evidence(ScreenKind.Unknown));
            var slowTap = slow.Observe(Frame(2 + scanSeconds, true), Evidence(ScreenKind.SaveTheWorldHub));
            check($"Slow HUD scans ({scanSeconds}s) still propose Tab", slowTap?.Step == NavigationStep.RetryQuestMenu);
            slow.Dispatched(slowTap!, epoch.AddSeconds(2 + scanSeconds + 6));
            check($"Slow OCR dispatch ({scanSeconds}s scans) acknowledges Tab", slow.Status == ControllerStatus.Observing && slow.Pending is null && slow.Detail.Contains("Tab sent"));
        }
        var expired = Joined();
        expired.Observe(Frame(2, true), Evidence(ScreenKind.Unknown));
        check("HUD samples older than 30 seconds need new confirmation", expired.Observe(Frame(33, true), Evidence(ScreenKind.Unknown)) is null);
        var expiredTap = expired.Observe(Frame(33.2, true), Evidence(ScreenKind.Unknown))!;
        bool staleRejected = false;
        try
        {
            expired.Dispatched(expiredTap, epoch.AddSeconds(64));
        }
        catch (InvalidOperationException) { staleRejected = true; }
        check("Health probe cannot acknowledge a dispatch older than 30 seconds", staleRejected && !expired.Detail.Contains("Tab sent"));
        check("Ordinary clicks retain five-second expiry", NavigationTiming.MaximumObservationAge(NavigationStep.EnterSaveTheWorld) == TimeSpan.FromSeconds(5));
        check("Already-loaded hub requires fresh HUD confirmation", idle.Observe(Frame(1, true), Evidence(ScreenKind.Unknown)) is null && idle.AwaitingQuestMenu);
        check("Already-loaded hub opens Tab without an observed join", idle.Observe(Frame(1.2, true), Evidence(ScreenKind.Unknown)) is { Step: NavigationStep.RetryQuestMenu, Key: NavigationKey.Tab });
        var screenshot = Fresh();
        var actual = Frame(1) with
        {
            Width = 2559,
            Height = 1439,
            Visuals = [new("player-health-bar", new(75, 1325, 311, 22), 1, 1)]
        };
        screenshot.Observe(actual, Evidence(ScreenKind.Unknown));
        check("Reported hub screenshot arms Tab at its detected health bar", screenshot.Observe(actual with { CapturedAt = epoch.AddSeconds(1.2) }, Evidence(ScreenKind.Unknown)) is { Step: NavigationStep.RetryQuestMenu, FrameWidth: 2559, Target.X: 75, Target.Y: 1325 });
        foreach (string mode in new[] { "recovery", "rewards", "active", "ambiguous", "duplicate", "missing" })
        {
            var guarded = Fresh();
            guarded.ErrorRecoveryOnly = mode == "recovery";
            guarded.FinishRewardsOnly = mode == "rewards";
            if (mode == "active")
            {
                guarded.Start(runInProgress: true);
            }

            var frame = Frame(1, mode != "missing");
            if (mode == "duplicate")
            {
                frame = frame with
                {
                    Visuals = [new("player-health-bar", new(80, 900, 280, 24), 1, 1), new("player-health-bar", new(80, 600, 280, 24), 1, 1)]
                };
            }

            var screen = Evidence(ScreenKind.Unknown) with
            {
                AllowsInput = mode != "ambiguous"
            };
            guarded.Observe(frame, screen);
            check("HUD auto-entry respects guard: " + mode, guarded.Observe(frame with { CapturedAt = epoch.AddSeconds(1.2) }, screen) is null && !guarded.AwaitingQuestMenu);
        }
        var autoTimeout = Fresh();
        autoTimeout.Observe(Frame(1, true), Evidence(ScreenKind.Unknown));
        int verifiedAttempts = 0;
        for (int second = 2; second <= 122; second++)
        {
            var action = autoTimeout.Observe(Frame(second, true), Evidence(ScreenKind.Unknown));
            if (action is not null)
            {
                autoTimeout.Dispatched(action, epoch.AddSeconds(second));
                verifiedAttempts++;
            }
        }
        check("Automatic entry pauses after ten verified attempts", verifiedAttempts == QuestMenuEntry.MaximumAttempts
            && autoTimeout.Status == ControllerStatus.PausedForAttention && autoTimeout.QuestMenuTimeouts == 1);
        var c = Joined();
        check("Join arms health-guided entry", c.AwaitingQuestMenu);
        check("Unknown screen without health never receives Tab", c.Observe(Frame(2), Evidence(ScreenKind.Unknown)) is null);
        check("One health reading cannot send Tab", c.Observe(Frame(3, true), Evidence(ScreenKind.Unknown)) is null);
        var tap = c.Observe(Frame(3.2, true), Evidence(ScreenKind.Unknown));
        check("Two health readings send Tab at the verified HUD", tap is { Step: NavigationStep.RetryQuestMenu, Key: NavigationKey.Tab, Target.X: 80 });
        check("Health-guided Tab is allowlisted", InteractionPrompts.IsAllowedKeyboardAction(tap!));
        c.Dispatched(tap!, epoch.AddSeconds(3.2));
        check("Disappearing HUD prevents toggling an unreadable menu closed", c.Observe(Frame(6), Evidence(ScreenKind.Unknown)) is null);
        c.Observe(Frame(6.2, true), Evidence(ScreenKind.SaveTheWorldHub));
        var retry = c.Observe(Frame(6.4, true), Evidence(ScreenKind.SaveTheWorldHub));
        check("Unchanged health permits a checked retry", retry?.Step == NavigationStep.RetryQuestMenu);
        c.Dispatched(retry!, epoch.AddSeconds(6.4));
        c.Observe(Frame(9, true), Evidence(ScreenKind.Loading));
        check("Loading cannot receive fallback Tab", c.Observe(Frame(9.2, true), Evidence(ScreenKind.Loading)) is null);
        c.Observe(Frame(10, true), Evidence(ScreenKind.Unknown));
        check("Ambiguous screen blocks Tab", c.Observe(Frame(10.2, true), Evidence(ScreenKind.Unknown) with { AllowsInput = false }) is null);
        c.Observe(Frame(11, true), Evidence(ScreenKind.Unknown));
        c.InvalidateObservation();
        check("Focus change discards health confirmation", c.Observe(Frame(11.2, true), Evidence(ScreenKind.Unknown)) is null && c.AwaitingQuestMenu);
        c.Observe(Frame(12, true) with
        {
            Width = 1280,
            Height = 960
        }, Evidence(ScreenKind.Unknown));
        var resized = c.Observe(Frame(12.2, true) with
        {
            Width = 1280,
            Height = 960
        }, Evidence(ScreenKind.Unknown));
        check("Resolution change uses fresh confirmation", resized is { FrameWidth: 1280, FrameHeight: 960 });
        c.Dispatched(resized!, epoch.AddSeconds(12.2));
        for (int second = 15; second < 120; second++)
        {
            var action = c.Observe(Frame(second, true), Evidence(ScreenKind.Unknown));
            if (action is not null)
            {
                c.Dispatched(action, epoch.AddSeconds(second));
            }
        }
        check("Repeated unsuccessful inputs exhaust the attempt budget", c.Status == ControllerStatus.PausedForAttention);
        c.Observe(Frame(120.2, true), Evidence(ScreenKind.Unknown));
        check("Attempt exhaustion remains paused", c.Status == ControllerStatus.PausedForAttention && c.QuestMenuTimeouts == 1);
        c.Observe(Frame(130, true), Evidence(ScreenKind.Unknown));
        check("Attempt exhaustion reports once", c.QuestMenuTimeouts == 1);
        c.Start();
        check("Restart clears old probe", !c.AwaitingQuestMenu);
        foreach (var destination in new[]{ScreenKind.SaveTheWorldLobby,ScreenKind.WorldMap,ScreenKind.MissionMap,
            ScreenKind.Backpack,ScreenKind.Homebase,ScreenKind.ActiveEndurance,ScreenKind.ErrorDialog,ScreenKind.RejoinDialog,ScreenKind.FortniteLobby})
        {
            var completed = Joined();
            var action = completed.Observe(Frame(3, true), Evidence(destination));
            check("Recognised destination ends Tab: " + destination, !completed.AwaitingQuestMenu && action?.Step != NavigationStep.RetryQuestMenu);
        }
        foreach (var overlay in new[] { ScreenKind.SleepMode, ScreenKind.RewardClaim, ScreenKind.Rewards, ScreenKind.FeedbackSurvey, ScreenKind.NewUnlock })
        {
            var covered = Joined();
            check("Overlay takes priority: " + overlay, covered.Observe(Frame(3, true), Evidence(overlay))?.Step != NavigationStep.RetryQuestMenu);
            check("After overlay health is reacquired: " + overlay, covered.Observe(Frame(4, true), Evidence(ScreenKind.Unknown)) is null);
        }
        var noHealth = Joined();
        noHealth.Observe(Frame(1800), Evidence(ScreenKind.Unknown));
        check("Missing HUD waits without a loading deadline", noHealth.QuestMenuTimeouts == 0
            && noHealth.AwaitingQuestMenu && noHealth.Status == ControllerStatus.Observing);
        foreach (var waiting in new[] { ScreenKind.Loading, ScreenKind.Unknown, ScreenKind.Matchmaking })
        {
            var hdd = Joined();
            foreach (int second in new[] { 5, 120, 300, 900 })
            {
                check($"Long {waiting} wait at {second}s sends no Tab", hdd.Observe(Frame(second), Evidence(waiting)) is null
                    && hdd.AwaitingQuestMenu && hdd.Status != ControllerStatus.PausedForAttention);
            }

            check("First HUD after long load needs confirmation: " + waiting,
                hdd.Observe(Frame(901, true), Evidence(ScreenKind.Unknown)) is null);
            var loaded = hdd.Observe(Frame(901.2, true), Evidence(ScreenKind.Unknown));
            check("Fifteen-minute load still opens menu: " + waiting, loaded?.Step == NavigationStep.RetryQuestMenu);
            hdd.Dispatched(loaded!, epoch.AddSeconds(901.2));
            hdd.InvalidateObservation();
            check("Long focus loss requires fresh HUD", hdd.Observe(Frame(1800, true), Evidence(ScreenKind.Unknown)) is null);
            check("Long focus loss does not expire retries", hdd.Observe(Frame(1800.2, true), Evidence(ScreenKind.Unknown))?.Step == NavigationStep.RetryQuestMenu);
        }
        var paced = Joined();
        paced.Observe(Frame(1, true), Evidence(ScreenKind.Unknown));
        var initial = paced.Observe(Frame(1.2, true), Evidence(ScreenKind.Unknown))!;
        paced.Dispatched(initial, epoch.AddSeconds(1.2));
        paced.Observe(Frame(1.4, true), Evidence(ScreenKind.Unknown));
        check("Fresh HUD cannot bypass input settling", paced.Observe(Frame(1.6, true), Evidence(ScreenKind.Unknown)) is null);
        paced.Observe(Frame(600), Evidence(ScreenKind.Loading));
        check("Loading after a press does not exhaust attempts", paced.QuestMenuTimeouts == 0 && paced.AwaitingQuestMenu);
        var stopped = Joined();
        stopped.Stop();
        check("Stop cancels probing", !stopped.AwaitingQuestMenu && stopped.Observe(Frame(4, true), Evidence(ScreenKind.Unknown)) is null);
        check("Diagnostic Tab cannot target active gameplay", !InteractionPrompts.IsAllowedKeyboardAction(tap! with
        {
            SourceScreen = ScreenKind.ActiveEndurance
        }));
        check("Diagnostic Tab cannot become another key", !InteractionPrompts.IsAllowedKeyboardAction(tap! with
        {
            Key = NavigationKey.E
        }));
        foreach (bool joined in new[] { true, false })
        {
            foreach (bool backpack in new[] { false, true })
            {
                var route = joined ? Joined() : Fresh();
                route.Observe(Frame(3, true), Evidence(ScreenKind.Unknown));
                var probe = route.Observe(Frame(3.2, true), Evidence(ScreenKind.Unknown))!;
                route.Dispatched(probe, epoch.AddSeconds(3.2));
                string[] labels = backpack ? ["Loadout", "Objectives", "Quests", "Storm Shield"] : ["MAP", "COMMAND", "ARMORY"];
                var menu = Frame(4) with
                {
                    Regions = labels.Select((text, i) => new TextRegion(text, new(100 + i * 250, 30, 180, 25))).ToArray()
                };
                var screen = new ScreenRecognizer().Recognize(menu);
                route.Observe(menu, screen);
                var next = route.Observe(menu with
                {
                    CapturedAt = epoch.AddSeconds(4.7)
                }, screen);
                check((joined ? "After join: " : "Already loaded: ") + (backpack ? "Backpack probe follows Storm Shield" : "Hestia menu probe follows Map"),
                    next?.Step == (backpack ? NavigationStep.SelectStormShieldTab : NavigationStep.OpenMap) && !route.AwaitingQuestMenu);
            }
        }
    }
}
