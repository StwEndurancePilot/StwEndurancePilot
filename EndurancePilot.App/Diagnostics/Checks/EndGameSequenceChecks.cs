using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class EndGameSequenceChecks
{
    public static async Task Run(string directory, string claimFile, string output)
    {
        Directory.CreateDirectory(output);
        var ocr = new OcrService();
        var screens = new ScreenRecognizer();
        var expected = new Dictionary<string, (ScreenKind Screen, NavigationStep? Step, string? Name, long? Count)>
        {
            ["173749"] = (ScreenKind.Defeat, NavigationStep.ContinueResults, null, null),
            ["173753"] = (ScreenKind.Victory, NavigationStep.ReturnToHomebase, null, null),
            ["173757"] = (ScreenKind.Matchmaking, null, null, null),
            ["173827"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, null, null),
            ["173833"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, "Battle Pass XP", 287400),
            ["173837"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, "Schematic XP", 346260),
            ["173839"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, "Gold", 1530),
            ["173843"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, "RE-PERK!", 1111),
            ["173846"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, "Legendary PERK-UP!", 771),
            ["173907"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, null, null),
            ["173912"] = (ScreenKind.Rewards, NavigationStep.CollectMissionRewards, "RE-PERK!", 100),
            ["173916"] = (ScreenKind.SaveTheWorldHub, NavigationStep.OpenQuestMenu, null, null)
        };
        var report = new List<object>();
        var errors = new List<string>();
        foreach (var file in Directory.GetFiles(directory, "*.png").OrderBy(x => x))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[^6..];
            if (!expected.TryGetValue(stamp, out var want))
            {
                continue;
            }

            using var image = new Bitmap(file);
            var frame = await ocr.ReadAsync(image);
            var screen = screens.Recognize(frame);
            frame = new VisualRecognizer().Enrich(image, frame, screen.Kind);
            screen = screens.Recognize(frame);
            var plan = NavigationPlanner.Decide(frame, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
            var reward = MissionRewardReader.Read(frame, screen).SingleOrDefault();
            bool passed = screen.Kind == want.Screen && plan?.Step == want.Step
                && (want.Name is null || reward?.Name == want.Name && reward.Quantity == want.Count)
                && (want.Step != NavigationStep.CollectMissionRewards || plan?.Key == NavigationKey.C);
            if (!passed)
            {
                errors.Add($"{stamp}: expected {want}, got {screen.Kind}, {plan?.Step}, {reward}");
            }
            report.Add(new { stamp, passed, screen.Kind, plan, reward });
        }

        using (var image = new Bitmap(claimFile))
        {
            var frame = await ocr.ReadAsync(image);
            var screen = screens.Recognize(frame);
            var plan = NavigationPlanner.Decide(frame, screen, new()).Plan;
            var reward = MissionRewardReader.Read(frame, screen).SingleOrDefault();
            bool passed = screen.Kind == ScreenKind.RewardClaim && plan?.Step == NavigationStep.ClaimReward
                && reward is { Name: "Season XP", Quantity: 21000 };
            if (!passed)
            {
                errors.Add($"Season XP claim: got {screen.Kind}, {plan?.Step}, {reward}");
            }
            report.Add(new { stamp = "epic_rewards", passed, screen.Kind, plan, reward });
        }

        await File.WriteAllTextAsync(Path.Combine(output, "end-game-sequence.json"), JsonSerializer.Serialize(report, Inspection.Json));
        if (errors.Count > 0)
        {
            throw new Exception(string.Join(Environment.NewLine, errors));
        }
    }
}
