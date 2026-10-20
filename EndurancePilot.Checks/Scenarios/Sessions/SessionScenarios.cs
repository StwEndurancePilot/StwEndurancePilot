using EndurancePilot.Core;

internal static class SessionScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var session = new SessionProgress(2);
        RunRecord Ended(RunOutcome outcome) => new(Guid.NewGuid(), epoch, epoch.AddHours(2), 30, outcome);
        var first = Ended(RunOutcome.Completed30);
        session.Record(first);
        session.Record(first);
        check("Repeated outcome does not consume another attempt", session.AttemptsEnded == 1 && session.Phase == SessionPhase.Running);
        session.Record(Ended(RunOutcome.Failed));
        check("Final attempt collects rewards before stopping", session.Phase == SessionPhase.FinishingRewards);
        var recognizer = new ScreenRecognizer();
        Observation Frame(double second, params string[] text) => new(epoch.AddSeconds(second), 1920, 1080,
            text.Select((s, i) => new TextRegion(s, new(100, 100 + i * 50, 300, 30))).ToArray());
        void Feed(double second, bool pending, params string[] text)
        {
            var frame = Frame(second, text);
            session.Observe(frame, recognizer.Recognize(frame), pending);
        }
        Feed(1, false, "MISSION REWARDS", "CONTINUE");
        Feed(3, false, "MISSION REWARDS", "CONTINUE");
        check("Reward pages cannot finish cleanup", session.Phase == SessionPhase.FinishingRewards);
        Feed(4, false, "CONTINUE");
        check("Unknown transition cannot finish cleanup", session.Phase == SessionPhase.FinishingRewards);
        Feed(5, true, "MAP", "COMMAND", "ARMORY");
        Feed(7, true, "MAP", "COMMAND", "ARMORY");
        check("Pending reward dispatch must finish verification", session.Phase == SessionPhase.FinishingRewards);
        Feed(8, false, "MAP", "COMMAND", "ARMORY");
        check("One menu frame cannot stop the session", session.Phase == SessionPhase.FinishingRewards);
        Feed(10, false, "MAP", "COMMAND", "ARMORY");
        check("Stable menu after final cleanup completes the session", session.Phase == SessionPhase.Complete);
        var interrupted = new SessionProgress(1);
        interrupted.Record(Ended(RunOutcome.Interrupted));
        check("Interrupted final attempt does not wait for nonexistent rewards", interrupted.Phase == SessionPhase.Complete);

        var indefinite = new SessionProgress(null);
        foreach (var outcome in new[] { RunOutcome.Completed30, RunOutcome.Failed, RunOutcome.Interrupted, RunOutcome.UnverifiedVictory })
        {
            for (int i = 0; i < 26; i++)
            {
                var record = Ended(outcome);
                indefinite.Record(record);
                indefinite.Record(record);
            }
        }

        check("Indefinite sessions continue beyond 100 attempts across every outcome", indefinite.AttemptLimit is null
            && indefinite.AttemptsEnded == 104 && indefinite.CompletedRuns == 26 && indefinite.FailedRuns == 26
            && indefinite.InterruptedRuns == 26 && indefinite.UnverifiedRuns == 26 && indefinite.Phase == SessionPhase.Running);
        var indefiniteMenu = Frame(11, "MAP", "COMMAND", "ARMORY");
        indefinite.Observe(indefiniteMenu, recognizer.Recognize(indefiniteMenu), false);
        indefinite.Observe(indefiniteMenu with
        {
            CapturedAt = epoch.AddSeconds(13)
        }, recognizer.Recognize(indefiniteMenu), false);
        check("Returning to a menu does not finish an indefinite session", indefinite.Phase == SessionPhase.Running);

        var controller = new AutomationController { ErrorRecoveryOnly = false, ClaimRewards = true, FinishRewardsOnly = true, Profile = new(HomebaseZone.TwinePeaks, true) };
        controller.Start();
        var lobby = Frame(20, "MAP", "COMMAND", "ARMORY");
        controller.Observe(lobby, recognizer.Recognize(lobby));
        check("Cleanup mode cannot launch the next attempt", controller.Observe(lobby with { CapturedAt = epoch.AddSeconds(22) }, recognizer.Recognize(lobby)) is null);
        var victory = Frame(23, "VICTORY", "TEAM SCORE", "CONTINUE");
        controller.Observe(victory, recognizer.Recognize(victory));
        check("Result action waits long enough to record outcome", controller.Observe(victory with { CapturedAt = epoch.AddSeconds(23.6) }, recognizer.Recognize(victory)) is null);
        var action = controller.Observe(victory with
        {
            CapturedAt = epoch.AddSeconds(24.1)
        }, recognizer.Recognize(victory));
        check("Cleanup may advance a corroborated result screen", action?.Step == NavigationStep.ContinueResults);
        var reward = Frame(25, "MISSION REWARDS", "CLAIM", "EQUIP", "GO TO LOCKER");
        var plan = NavigationPlanner.Decide(reward, recognizer.Recognize(reward), new()).Plan;
        check("Reward cleanup selects Claim independently of zone configuration", plan?.Step == NavigationStep.CollectMissionRewards && plan.Target.Y == 150);
    }
}
