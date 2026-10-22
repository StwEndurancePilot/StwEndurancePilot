using EndurancePilot.Core;

internal static class MissionRewardScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        Observation Frame(int second, string control, params TextRegion[] items) => new(epoch.AddSeconds(second), 1920, 1080,
            new TextRegion[] { new("MISSION REWARDS", new(700, 100, 400, 45)), new(control, new(800, 900, 250, 40)) }.Concat(items).ToArray());
        var gold = new TextRegion("Gold × 250", new(600, 400, 250, 40));
        var xp = new TextRegion("28,000 Season XP", new(600, 500, 300, 40));
        var frame = Frame(0, "CONTINUE", gold, xp);
        var rewards = MissionRewardReader.Read(frame, recognizer.Recognize(frame));
        check("Reward names, exact quantities and icons are read", rewards.Count == 2
            && rewards.Single(r => r.Name == "Gold").Quantity == 250
            && rewards.Single(r => r.Name == "Season XP").Quantity == 28000
            && rewards.All(r => r.Icon.Length > 0));
        var duplicate = Frame(0, "CONTINUE", gold, gold with
        {
            Pass = "enhanced"
        }, gold with
        {
            Pass = "overview-word"
        });
        check("Overlapping OCR passes never multiply rewards", MissionRewardReader.Read(duplicate, recognizer.Recognize(duplicate)).Single().Quantity == 250);
        var conflicting = Frame(0, "CONTINUE", gold, gold with
        {
            Text = "Gold × 260"
        });
        check("Conflicting quantities are explicitly unreadable", MissionRewardReader.Read(conflicting, recognizer.Recognize(conflicting)).Single().Quantity is null);
        var split = Frame(0, "CONTINUE", new("Legendary PERK-UP!", new(600, 400, 250, 35)), new("× 120", new(670, 445, 100, 30)),
            new("999", new(1300, 445, 100, 30)));
        check("Separate aligned reward quantity excludes another column", MissionRewardReader.Read(split, recognizer.Recognize(split)).Single().Quantity == 120);
        var unreadable = Frame(0, "CONTINUE", new("Gold", new(600, 400, 250, 35)), new("1.2K", new(670, 445, 100, 30)));
        check("Abbreviated quantities are never presented as exact", MissionRewardReader.Read(unreadable, recognizer.Recognize(unreadable)).Single().Quantity is null);
        check("Rewards cannot be read from ordinary menus", MissionRewardReader.Read(frame,
            new(ScreenKind.MissionMap, [], [])).Count == 0);
        var extra = Frame(0, "CONTINUE", new TextRegion("Upgrade Llama × 2", new(600, 400, 250, 40)));
        check("Other explicitly quantified reward names are supported", MissionRewardReader.Read(extra, recognizer.Recognize(extra)).Single().Quantity == 2);

        var opening = Frame(0, "OPEN");
        var controller = new AutomationController { ClaimRewards = true };
        controller.Start();
        controller.Observe(opening, recognizer.Recognize(opening));
        var action = controller.Observe(opening with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(opening))!;
        controller.Dispatched(action, epoch.AddSeconds(1));
        check("Unchanged chest does not confirm opening", !NavigationPlanner.IsSatisfied(action, opening, recognizer.Recognize(opening)));
        var readContext = MissionRewardReader.PageContext(frame, recognizer.Recognize(frame));
        check("Lost item OCR cannot confirm a page transition", !MissionRewardReader.HasPageChanged(readContext,
            Frame(1, "CONTINUE"), recognizer.Recognize(frame)));
        controller.Observe(frame with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, recognizer.Recognize(frame));
        check("Reward reveal needs two destination observations", controller.Pending is not null);
        controller.Observe(frame with
        {
            CapturedAt = epoch.AddSeconds(3)
        }, recognizer.Recognize(frame));
        check("Opened chest can advance on the same reward screen kind", controller.Pending is null);
        var both = opening with
        {
            Regions = opening.Regions.Append(new TextRegion("COLLECT ALL", new(1100, 900, 250, 40))).ToArray()
        };
        check("Open is preferred before collecting rewards", NavigationPlanner.Decide(both, recognizer.Recognize(both), new()).Plan?.Target.X == 800);

        var tracker = new MissionRewardTracker();
        tracker.Observe(frame, recognizer.Recognize(frame), null);
        check("Reward totals require a second fresh reading", tracker.Items.Count == 0);
        tracker.Observe(frame, recognizer.Recognize(frame), null);
        check("Repeated timestamp does not confirm reward amounts", tracker.Items.Count == 0);
        tracker.Observe(frame with
        {
            CapturedAt = epoch.AddSeconds(1)
        }, recognizer.Recognize(frame), null);
        tracker.Observe(frame with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, recognizer.Recognize(frame), null);
        check("Repeated reward frames do not inflate totals", tracker.Items.Single(r => r.Name == "Gold").Quantity == 250);
        tracker.Dispatched(action);
        var sameItems = Frame(3, "CLAIM", gold, xp);
        tracker.Observe(sameItems, recognizer.Recognize(sameItems), null);
        tracker.Observe(sameItems with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, recognizer.Recognize(sameItems), null);
        check("A changed button does not award the same items twice", tracker.Items.Single(r => r.Name == "Gold").Quantity == 250);
        tracker.Dispatched(action);
        var next = Frame(5, "CLAIM", gold with
        {
            Text = "Gold × 100"
        });
        tracker.Observe(next, recognizer.Recognize(next), null);
        tracker.Observe(next with
        {
            CapturedAt = epoch.AddSeconds(6)
        }, recognizer.Recognize(next), null);
        tracker.Observe(next with
        {
            CapturedAt = epoch.AddSeconds(7)
        }, recognizer.Recognize(next), null);
        check("Confirmed successive reward pages accumulate once", tracker.Items.Single(r => r.Name == "Gold").Quantity == 350
            && tracker.Items.Single(r => r.Name == "Season XP").Quantity == 28000);
        tracker.Observe(Frame(8, "CONTINUE"), new(ScreenKind.SaveTheWorldLobby, [], []), null);
        check("Reward receipt remains visible after leaving rewards", tracker.Visible && tracker.Items.Count == 2);
        tracker.Observe(Frame(9, "CONTINUE"), new(ScreenKind.ActiveEndurance, [], []),
            new(Guid.NewGuid(), epoch.AddSeconds(9), null, 1, RunOutcome.InProgress));
        check("A new run clears the previous mission reward totals", !tracker.Visible && tracker.Items.Count == 0);
        Observation Card(int ms, string name, string count, string? verify = null) => new(epoch.AddMilliseconds(ms), 1920, 1080,
            [new("YOU RECEIVED",new(600,100,400,70)),new(name,new(90,350,300,30),"reward-title"),
             new(count,new(900,730,50,30),"reward-count"),new(verify??count,new(900,730,50,30),"reward-count-check")],
            [new("reward-card", new(620, 450, 350, 350), 1, 1)]);
        var carousel = new MissionRewardTracker();
        var casing = new MissionRewardTracker();
        foreach (var sample in new[] { Card(0, "people X", "31"), Card(50, "People X", "31"), Card(100, "People XP", "31") })
        {
            casing.Observe(sample, recognizer.Recognize(sample), null);
        }

        check("Live XP OCR truncation and casing changes keep one canonical reward",
            casing.Items.Count == 1 && casing.Items[0] is { Name: "People XP", Quantity: 31, Icon: "★" });
        var genericCasing = new MissionRewardTracker();
        foreach (var sample in new[] { Card(0, "Reward Material", "31"), Card(50, "reward material", "31") })
        {
            genericCasing.Observe(sample, recognizer.Recognize(sample), null);
        }

        check("Generic reward casing cannot duplicate quantities", genericCasing.Items.Count == 1 && genericCasing.Items[0].Quantity == 31);
        var first = Card(0, "People XP", "31");
        carousel.Observe(first, recognizer.Recognize(first), null);
        check("Carousel receives People XP 31 immediately from one corroborated capture", carousel.Items.Single().Quantity == 31);
        var animated = Card(10, "People XP", "35");
        carousel.Observe(animated, recognizer.Recognize(animated), null);
        check("An animated amount updates its card instead of adding intermediate counts", carousel.Items.Single().Quantity == 35);
        check("Carousel card without readable controls can advance with C after recording",
            recognizer.Recognize(first).AllowsInput
            && NavigationPlanner.Decide(first, recognizer.Recognize(first), new()).Plan
                is { Step: NavigationStep.CollectMissionRewards, Key: NavigationKey.C });
        for (int i = 1; i <= 20; i++)
        {
            var card = Card(i * 50, "Reward Material " + i, i.ToString());
            carousel.Observe(card, recognizer.Recognize(card), null);
            carousel.Observe(card with
            {
                CapturedAt = card.CapturedAt.AddMilliseconds(10)
            }, recognizer.Recognize(card), null);
        }
        check("Twenty rapidly scrolling cards accumulate without clicks or duplicates", carousel.Items.Count == 21
            && carousel.Items.Single(r => r.Name == "Reward Material 20").Quantity == 20);
        var wrong = Card(1100, "Gold", "250", "260");
        check("Disagreeing count passes cannot confirm a scrolling amount", MissionRewardReader.ReadCard(wrong) is null);
        carousel.Observe(Frame(2, "CONTINUE"), ScreenEvidence.Unknown("animation"), null);
        check("A blank carousel animation cannot finalize the receipt", !carousel.IsComplete && carousel.Items.Count == 21);
        var menuFrame = Frame(3, "CONTINUE");
        var menuScreen = new ScreenEvidence(ScreenKind.SaveTheWorldHub, [], []);
        carousel.Observe(menuFrame, menuScreen, null);
        check("One menu frame cannot finalize a receipt", !carousel.IsComplete);
        carousel.Observe(menuFrame with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, menuScreen, null);
        check("Stable menu finalizes the complete scrolling receipt", carousel.IsComplete && carousel.Items.Count == 21);
        var after = Card(5000, "Gold", "250");
        var later = recognizer.Recognize(after);
        var backgroundSession = new RunnerSession();
        backgroundSession.Start(RunnerMode.Runner, new(HomebaseZone.TwinePeaks, true), null, epoch);
        backgroundSession.ObserveBackground(after, later);
        check("Background capture records scrolling rewards without input", backgroundSession.Rewards.Items.Single().Quantity == 250
            && backgroundSession.Controller.Pending is null);
    }
}
