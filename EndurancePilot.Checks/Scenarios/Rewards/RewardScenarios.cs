using EndurancePilot.Core;

internal static class RewardScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var map = new Observation(epoch, 1920, 1080, [
            new("MAP",new(400,50,100,30)),new("COMMAND",new(520,50,160,30)),new("ARMORY",new(700,50,150,30)),
            new("REWARDS",new(40,600,150,30)),new("ZOOM",new(800,950,120,30)),
            new("20% Bonus XP + Mission Rewards",new(40,700,450,35)),
            new("Eliminate and Collect mission",new(1450,400,400,30)),
            new("Collect",new(1630,400,90,30),"overview-word")],
            [new("homebase-icon", new(1000, 700, 60, 60), .95, .2)]);
        check("Map reward descriptions and pinned Collect do not identify rewards", recognizer.Recognize(map).Kind == ScreenKind.MissionMap);
        check("Map with reward description selects homebase", NavigationPlanner.Decide(map, recognizer.Recognize(map),
            new(HomebaseZone.TwinePeaks, true)).Plan?.Step == NavigationStep.SelectHomebase);
        var falseControl = map with
        {
            Regions = map.Regions.Append(new TextRegion("MISSION REWARDS", new(400, 200, 300, 40))).ToArray()
        };
        check("Reward heading cannot turn a quest word into a button", recognizer.Recognize(falseControl).Kind == ScreenKind.MissionMap);
        foreach (var control in new[] { "OPEN", "COLLECT ALL", "COLLECT", "CONTINUE", "CLAIM" })
        {
            var reward = new Observation(epoch, 1920, 1080, [
                new("MISSION REWARDS",new(700,100,400,45)),new(control,new(800,900,250,40))]);
            check("Standalone reward control remains supported: " + control, recognizer.Recognize(reward).Kind == ScreenKind.Rewards
                && NavigationPlanner.Decide(reward, recognizer.Recognize(reward), new()).Plan?.Step == NavigationStep.CollectMissionRewards);
        }
        var received = new Observation(epoch, 1920, 1080, [new("VOU RECEIVED", new(800, 200, 320, 50)), new("CONTINUE", new(900, 1020, 160, 30))]);
        Observation Reveal(double seconds, string item) => new(epoch.AddSeconds(seconds), 1920, 1080,
            [new("MISSION REWARDS",new(700,100,400,45)),new(item,new(150,400,400,40)),
             new("COLLECT ALL",new(800,900,250,40))]);
        var revealSession = new RunnerSession();
        revealSession.Start(RunnerMode.Recovery, new(), null, epoch);
        for (int i = 0; i < 8; i++)
        {
            var revealFrame = Reveal(i * .5, i < 4 ? "Schematic XP" : "Gold");
            check("Unreadable carousel stays open " + i, revealSession.Observe(revealFrame, recognizer.Recognize(revealFrame)).Action is null);
        }
        check("Auto-scrolling fallback preserves earlier unreadable rewards",
            revealSession.Rewards.Items.Select(r => r.Name).Order().SequenceEqual(new[] { "Gold", "Schematic XP" }));
        for (int i = 0; i < 4; i++)
        {
            var revealFrame = Reveal(4 + i * .5, "Schematic XP 100");
            revealSession.Observe(revealFrame, recognizer.Recognize(revealFrame));
        }
        for (int i = 0; i < 10; i++)
        {
            var revealFrame = Reveal(6 + i * .5, "Gold 50");
            check("Readable carousel waits for its final stable card " + i,
                revealSession.Observe(revealFrame, recognizer.Recognize(revealFrame)).Action is null);
        }
        ClickAction? collect = null;
        for (int i = 0; i < 6; i++)
        {
            var revealFrame = Reveal(11 + i * .5, "Gold 50");
            collect ??= revealSession.Observe(revealFrame, recognizer.Recognize(revealFrame)).Action;
        }
        check("Completed stable reveal can collect", collect?.Step == NavigationStep.CollectMissionRewards);
        check("Corrected quantities are retained without duplicate polling",
            revealSession.Rewards.Items.Single(r => r.Name == "Gold").Quantity == 50
            && revealSession.Rewards.Items.Single(r => r.Name == "Schematic XP").Quantity == 100);
        check("Captured VOU RECEIVED font error still permits reward Continue", recognizer.Recognize(received).Kind == ScreenKind.Rewards
            && NavigationPlanner.Decide(received, recognizer.Recognize(received), new()).Plan?.Step == NavigationStep.CollectMissionRewards);
        check("VOU RECEIVED heading without an action cannot trigger reward input", recognizer.Recognize(received with
        {
            Regions = [received.Regions[0]]
        }).Kind == ScreenKind.Unknown);
        var result = new Observation(epoch, 1920, 1080, [new("DEFEAT",new(880,30,160,50)),
            new("TEAM SCORE",new(800,150,220,40)),new("C MATCHMAKE INTO HOMEBASE",new(750,900,420,55))]);
        var resultEvidence = recognizer.Recognize(result);
        var scoreAnimation = result with
        {
            Regions = result.Regions.Select(r => r.Text.StartsWith("C ") ? r with { Text = "CONTINUE" } : r).ToArray()
        };
        var scoreController = new AutomationController { ClaimRewards = true };
        scoreController.Start();
        scoreController.Observe(scoreAnimation, recognizer.Recognize(scoreAnimation));
        var scoreContinue = scoreController.Observe(scoreAnimation with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(scoreAnimation))!;
        scoreController.Dispatched(scoreContinue, epoch.AddSeconds(1));
        scoreController.Observe(scoreAnimation with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, recognizer.Recognize(scoreAnimation));
        check("Unchanged result animation does not confirm Continue", scoreController.Pending == scoreContinue);
        scoreController.Observe(result with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, resultEvidence);
        check("One Matchmake destination frame does not confirm Continue", scoreController.Pending == scoreContinue);
        scoreController.Observe(result with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, resultEvidence);
        check("Stable Matchmake control confirms score-animation Continue without a timeout", scoreController.Pending is null);
        check("Observed result offers return-homebase without Continue", NavigationPlanner.Decide(result, resultEvidence, new()).Plan?.Step == NavigationStep.ReturnToHomebase);
        var duplicates = result with
        {
            Regions = result.Regions.Append(new TextRegion("MATCHMAKE INTO HOMEBASE", new(100, 700, 400, 50))).ToArray()
        };
        check("Ambiguous Matchmake controls cannot confirm result Continue", !NavigationPlanner.IsSatisfied(scoreContinue, duplicates, recognizer.Recognize(duplicates)));
        check("Separate return-homebase buttons remain ambiguous", NavigationPlanner.Decide(duplicates, recognizer.Recognize(duplicates), new()).Plan is null);
        var cancelled = result with
        {
            Regions = result.Regions.Select(r => r.Text.StartsWith("C ") ? r with { Text = "CANCEL MATCHMAKING" } : r).ToArray()
        };
        check("Result cleanup never selects Cancel Matchmaking", NavigationPlanner.Decide(cancelled, recognizer.Recognize(cancelled), new()).Plan is null);
        var cleanup = new AutomationController { ClaimRewards = true };
        cleanup.Start();
        cleanup.Observe(result, resultEvidence);
        var returning = cleanup.Observe(result with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, resultEvidence)!;
        check("Matchmake uses C on the detected defeat result", returning.Key == NavigationKey.C && InteractionPrompts.IsAllowedKeyboardAction(returning));
        check("Matchmake C is allowed on victory", InteractionPrompts.IsAllowedKeyboardAction(returning with
        {
            SourceScreen = ScreenKind.Victory
        }));
        check("Matchmake C is rejected during gameplay", !InteractionPrompts.IsAllowedKeyboardAction(returning with
        {
            SourceScreen = ScreenKind.ActiveEndurance
        }));
        cleanup.Dispatched(returning, epoch.AddSeconds(1));
        cleanup.Observe(cancelled with
        {
            CapturedAt = epoch.AddSeconds(20)
        }, recognizer.Recognize(cancelled));
        check("Homebase matchmaking waits instead of retrying after 12 seconds", cleanup.Pending == returning && cleanup.Status == ControllerStatus.AwaitingTransition);
        var mapped = OcrGeometry.Map(new(20, 30, 10, 20), 100, 100, 90, new(1000, 2000, 200, 200));
        check("OCR rotation is restored before crop scaling and translation", Math.Abs(mapped.X - 1100) < .001
            && Math.Abs(mapped.Y - 2040) < .001 && Math.Abs(mapped.Width - 40) < .001 && Math.Abs(mapped.Height - 20) < .001);
        var regions = new TextRegion[] {
            new("ACCOUNT RESOURCE", new(700,200,250,25)), new("28,000 SEASON XP", new(700,240,300,40)),
            new("Thanks for playing Save the", new(100,250,400,30)),
            new("CLAIM", new(700,650,100,25)), new("CLAIM", new(701,662,100,28)),
            new("EQUIP", new(500,650,100,25)), new("GO TO LOCKER", new(100,650,200,25)) };
        Observation Reward(int time) => new(epoch.AddSeconds(time), 1280, 720, regions);
        var frame = Reward(0);
        var evidence = recognizer.Recognize(frame);
        check("Season XP claim is recognised independently of mission completion", evidence.Kind == ScreenKind.RewardClaim);
        var plan = NavigationPlanner.Decide(frame, evidence, new()).Plan;
        check("Reward can be claimed before a zone is configured", plan?.Step == NavigationStep.ClaimReward);
        check("Claim target uses the overlap of shifted OCR readings", plan?.Target == new Box(701, 662, 99, 13));
        var duplicate = frame with
        {
            Regions = regions.Append(new TextRegion("CLAIM", new(900, 650, 100, 25))).ToArray()
        };
        check("Separate Claim controls block input", NavigationPlanner.Decide(duplicate, recognizer.Recognize(duplicate), new()).Plan is null);
        var bare = frame with
        {
            Regions = [regions[3]]
        };
        check("Claim text alone cannot classify a reward", recognizer.Recognize(bare).Kind == ScreenKind.Unknown);
        var controller = new AutomationController();
        controller.Start();
        controller.Observe(frame, evidence);
        check("Error-only mode does not claim rewards", controller.Observe(Reward(1), evidence) is null);
        controller.ClaimRewards = true;
        controller.Start();
        check("Reward needs more than one observation", controller.Observe(frame, evidence) is null);
        var action = controller.Observe(Reward(1), evidence)!;
        check("Enabled reward handler proposes Claim", action?.Step == NavigationStep.ClaimReward);
        controller.Dispatched(action!, epoch.AddSeconds(1));
        controller.Observe(Reward(2), evidence);
        check("Unchanged reward page cannot confirm a claim", controller.ConfirmedRewardClaims == 0 && controller.Pending is not null);
        var blank = frame with
        {
            CapturedAt = epoch.AddSeconds(3),
            Regions = []
        };
        controller.Observe(blank, recognizer.Recognize(blank));
        check("Blank transition cannot confirm a claim", controller.Pending is not null);
        var menu = frame with
        {
            CapturedAt = epoch.AddSeconds(4),
            Regions = [
            new("CURRENT LOBBY", new(100,500,200,30)), new("SAVE THE WORLD", new(500,70,300,30)),
            new("PLAY", new(100,70,70,30)), new("SHOP", new(200,70,70,30))]
        };
        controller.Observe(menu, recognizer.Recognize(menu));
        check("One destination frame is insufficient", controller.Pending is not null);
        controller.Observe(menu with
        {
            CapturedAt = epoch.AddSeconds(5)
        }, recognizer.Recognize(menu));
        check("Stable discovery menu confirms exactly one claimed reward", controller.ConfirmedRewardClaims == 1 && controller.Pending is null);
        var tracker = new RunTracker();
        check("Claiming Season XP does not complete an endurance", tracker.Observe(frame, evidence) is null);
    }
}

