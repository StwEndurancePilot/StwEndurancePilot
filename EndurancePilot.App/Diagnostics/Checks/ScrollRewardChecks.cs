using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class ScrollRewardChecks
{
    internal static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        var failures = new List<string>();
        var ocr = new OcrService();
        foreach (var (file, name, quantity) in new[] { ("people-xp-reward.png", "People XP", 31L), ("reperk-grey-reward.png", "RE-PERK!", 100L) })
        {
            using var original = new Bitmap(Path.Combine(captures, file));
            foreach (var (width, height) in new[] { (original.Width, original.Height), (1280, 720), (1920, 1080), (2560, 1392), (3440, 1440) })
            {
                using var image = new Bitmap(width, height);
                double scale = Math.Min((double)width / original.Width, (double)height / original.Height);
                using (var g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    g.DrawImage(original, (float)(width - original.Width * scale) / 2, (float)(height - original.Height * scale) / 2,
                        (float)(original.Width * scale), (float)(original.Height * scale));
                }
                var trace = new List<object>();
                var card = RewardCardDetector.Find(image, trace);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var frame = await ocr.ReadAsync(image);
                var elapsed = clock.Elapsed.TotalMilliseconds;
                var screen = new ScreenRecognizer().Recognize(frame);
                var reward = MissionRewardReader.ReadCard(frame);
                bool passed = screen.Kind == ScreenKind.Rewards && reward?.Name == name && reward.Quantity == quantity && !screen.AllowsInput;
                if (passed)
                {
                    var tracker = new MissionRewardTracker();
                    tracker.Observe(frame, screen, null);
                    var hub = new ScreenEvidence(ScreenKind.SaveTheWorldHub, [], []);
                    tracker.Observe(frame with
                    {
                        CapturedAt = frame.CapturedAt.AddSeconds(1),
                        Regions = [],
                        Visuals = []
                    }, hub, null);
                    tracker.Observe(frame with
                    {
                        CapturedAt = frame.CapturedAt.AddSeconds(2),
                        Regions = [],
                        Visuals = []
                    }, hub, null);
                    var ledger = new RewardEarnings(Path.Combine(output, $"sample-{Guid.NewGuid():N}.json"));
                    passed = ledger.Record(tracker.ReceiptId, tracker.Items, tracker.IsComplete)
                        && ledger.Session.Single().Name == name && ledger.Session.Single().Quantity == quantity
                        && ledger.Lifetime.Single().Quantity == quantity;
                    File.WriteAllBytes(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(file)}-{width}-receipt.png"),
                        RewardReceiptArtwork.Render(tracker.Items, new Dictionary<string, byte[]>(), ledger.Session, ledger.Lifetime));
                }
                reports.Add(new
                {
                    file,
                    width,
                    height,
                    Passed = passed,
                    Milliseconds = elapsed,
                    card,
                    trace,
                    reward,
                    screen,
                    frame
                });
                if (!passed)
                {
                    failures.Add($"{name} × {quantity} not read at {width}x{height}");
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "scroll-rewards.json"), JsonSerializer.Serialize(reports, Inspection.Json));
        if (failures.Count > 0)
        {
            throw new Exception(string.Join(Environment.NewLine, failures));
        }
    }
}
