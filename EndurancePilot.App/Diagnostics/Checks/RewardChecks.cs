using System.Drawing.Drawing2D;
using System.Text.Json;
using EndurancePilot.Core;

internal static class RewardChecks
{
    public static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        using var original = new Bitmap(Path.Combine(captures, "season-xp-claim-native.png"));
        var ocr = new EndurancePilot.App.OcrService();
        var recognizer = new ScreenRecognizer();
        var reports = new List<object>();
        var failures = new List<string>();
        foreach (var (width, height) in new[] { (2560, 1392), (1280, 720), (1920, 1080), (3440, 1440) })
        {
            using var image = new Bitmap(width, height);
            double scale = Math.Min((double)width / original.Width, (double)height / original.Height);
            float offsetX = (float)(width - original.Width * scale) / 2, offsetY = (float)(height - original.Height * scale) / 2;
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original, offsetX, offsetY, (float)(original.Width * scale), (float)(original.Height * scale));
            }
            var frame = await ocr.ReadAsync(image);
            var evidence = recognizer.Recognize(frame);
            var decision = NavigationPlanner.Decide(frame, evidence, new());
            var expected = new Box(offsetX + 1440 * scale, offsetY + 1255 * scale, 120 * scale, 50 * scale);
            bool passed = decision.Plan is { Step: NavigationStep.ClaimReward } plan
                && plan.Target.CenterX >= expected.X && plan.Target.CenterX <= expected.Right
                && plan.Target.CenterY >= expected.Y && plan.Target.CenterY <= expected.Bottom;
            var rewards = MissionRewardReader.Read(frame, evidence);
            bool quantityPassed = rewards.Any(r => r.Name == "Season XP" && r.Quantity == 28000);
            if (!quantityPassed)
            {
                failures.Add($"Season XP amount was not read exactly at {width}x{height}");
            }

            if (!passed)
            {
                failures.Add($"Claim recognition failed at {width}x{height}");
            }

            reports.Add(new
            {
                width,
                height,
                Synthetic = width != original.Width,
                Passed = passed && quantityPassed,
                evidence.Kind,
                decision.Plan,
                Rewards = rewards
            });
            await File.WriteAllTextAsync(Path.Combine(output, $"claim-{width}x{height}.json"), JsonSerializer.Serialize(new
            {
                Observation = frame,
                Screen = evidence
            }, EndurancePilot.App.Inspection.Json));
            using var annotated = EndurancePilot.App.OcrService.Annotate(image, frame);
            annotated.Save(Path.Combine(output, $"claim-{width}x{height}.png"));
        }
        using var resultCapture = new Bitmap(Path.Combine(captures, "defeat-matchmake-homebase.png"));
        foreach (var (width, height) in new[] { (resultCapture.Width, resultCapture.Height), (1280, 720), (1920, 1080), (3440, 1440) })
        {
            using var image = new Bitmap(width, height);
            double scale = Math.Min((double)width / resultCapture.Width, (double)height / resultCapture.Height);
            float x = (float)(width - resultCapture.Width * scale) / 2, y = (float)(height - resultCapture.Height * scale) / 2;
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(resultCapture, x, y, (float)(resultCapture.Width * scale), (float)(resultCapture.Height * scale));
            }
            var frame = await ocr.ReadAsync(image);
            var evidence = recognizer.Recognize(frame);
            var controller = new AutomationController { ClaimRewards = true };
            controller.Start();
            controller.Observe(frame, evidence);
            var action = controller.Observe(frame with
            {
                CapturedAt = frame.CapturedAt.AddSeconds(1)
            }, evidence);
            bool passed = evidence.Kind == ScreenKind.Defeat && action is { Step: NavigationStep.ReturnToHomebase, Key: NavigationKey.C }
                && action.Target.CenterX >= x + 414 * scale && action.Target.CenterX <= x + 950 * scale
                && action.Target.CenterY >= y + 1155 * scale && action.Target.CenterY <= y + 1218 * scale;
            if (!passed)
            {
                failures.Add($"Matchmake into Homebase was not targeted at {width}x{height}");
            }

            reports.Add(new
            {
                Capture = "defeat-matchmake-homebase",
                width,
                height,
                Passed = passed,
                evidence.Kind,
                Action = action
            });
            await File.WriteAllTextAsync(Path.Combine(output, $"matchmake-{width}x{height}.json"),
                JsonSerializer.Serialize(new
                {
                    Observation = frame,
                    Screen = evidence,
                    Action = action
                }, EndurancePilot.App.Inspection.Json));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "reward-checks.json"), JsonSerializer.Serialize(reports, EndurancePilot.App.Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception(string.Join(Environment.NewLine, failures));
        }
    }
}
