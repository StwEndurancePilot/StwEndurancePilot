using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class RewardReceiptChecks
{
    internal static MissionReward[] Samples => WebhookSamples.RewardItems;

    internal static IReadOnlyList<StartupCheck> Run(string output)
    {
        var checks = new List<StartupCheck>();
        void Check(string label, bool passed) => checks.Add(new("Reward receipt: " + label, passed, passed ? "Passed (offline)" : "Failed"));
        foreach (var name in RewardAssets.Files.Keys)
        {
            var asset = RewardAssets.Read(name);
            bool visible = false;
            if (asset is not null)
            {
                using var input = new MemoryStream(asset);
                using var icon = new Bitmap(input);

                int colored = 0;
                for (int y = 0; y < icon.Height; y += 8)
                {
                    for (int x = 0; x < icon.Width; x += 8)
                    {
                        var pixel = icon.GetPixel(x, y);
                        if (pixel.A > 32 && pixel.R + pixel.G + pixel.B > 60)
                        {
                            colored++;
                        }
                    }
                }

                visible = colored > 20;
            }
            Check($"Packaged {name} artwork contains visible pixels", visible);
        }
        Check("Every test reward has bundled art without a previous run",
            Samples.All(item => RewardAssets.Read(item.Name) is not null));
        foreach (int count in new[] { 1, 3, 10 })
        {
            var bytes = RewardReceiptArtwork.Render(Samples.Take(count).ToArray(), new Dictionary<string, byte[]>());
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            Check($"{count} items render within attachment limit", image.Width == 800 && image.Height == 76 + 68 * count + 20
                && bytes.Length < NotificationScreenshot.MaxBytes);
        }
        var edge = RewardReceiptArtwork.Render([new(new string('W', 60), null, ""), new("Large amount", long.MaxValue, "")], new Dictionary<string, byte[]>());
        File.WriteAllBytes(Path.Combine(output, "reward-receipt-edge-cases.png"), edge);
        Check("Long names and unknown quantities render", edge.Length > 0);
        var now = DateTimeOffset.UtcNow;
        var observation = new Observation(now, 400, 400,
            [new("YOU RECEIVED",new(1,1,100,20)),new("People XP",new(1,30,100,20),"reward-title"),
             new("31",new(250,270,20,20),"reward-count"),new("31",new(250,270,20,20),"reward-count-check")],
            [new("reward-card", new(100, 100, 200, 200), 1, 1)]);
        using var frame = new Bitmap(400, 400);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Red);
            g.FillRectangle(Brushes.Green, 110, 110, 180, 170);
        }
        var thumbnails = new RewardThumbnails();
        var id = Guid.NewGuid();
        thumbnails.Observe(id, observation, frame);
        Check("Verified card captures its small image", thumbnails.Images.Count == 1 && thumbnails.Images.ContainsKey("People XP"));
        using (var stream = new MemoryStream(thumbnails.Images["People XP"]))
        using (var image = new Bitmap(stream))
        {
            Check("Thumbnail excludes surrounding game frame", image.Width == 128 && image.Height == 104 && image.GetPixel(0, 0).G > image.GetPixel(0, 0).R);
        }

        thumbnails.Observe(Guid.NewGuid(), observation with
        {
            Regions = []
        }, frame);
        Check("New receipt discards previous item images", thumbnails.Images.Count == 0);
        var bytes10 = RewardReceiptArtwork.Render(Samples, new Dictionary<string, byte[]>());
        var message = new PilotNotification(WebhookEvent.EnduranceEnded, "Mission rewards received",
            string.Join("\n", Samples.Select(r => $"**{r.Name}**  {r.QuantityLabel}")), "TwinePeaks", 30,
            RewardImage: bytes10, IsRewardReceipt: true);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordNotifier.Payload(message)));
        var root = payload.RootElement;
        Check("Receipt attachment is referenced inside embed", root.GetProperty("embeds")[0].GetProperty("image").GetProperty("url").GetString() == "attachment://mission-rewards.png");
        Check("Accessible description retains all ten names and quantities", Samples.All(r => root.GetProperty("attachments")[0].GetProperty("description").GetString()!.Contains(r.Name + "  " + r.QuantityLabel)));
        using var content = DiscordNotifier.Content(message);
        Check("Receipt uploads image as multipart PNG", content is System.Net.Http.MultipartFormDataContent multipart
            && multipart.Any(p => p.Headers.ContentType?.MediaType == "image/png"));
        using var testPayload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordNotifier.Payload(message with
        {
            IsTest = true,
            Title = "Sample mission rewards"
        })));
        Check("Explicit reward test includes artwork and sample label",
            testPayload.RootElement.GetProperty("embeds")[0].GetProperty("footer").GetProperty("text").GetString()!.Contains("Sample")
            && testPayload.RootElement.GetProperty("embeds")[0].GetProperty("image").GetProperty("url").GetString() == "attachment://mission-rewards.png");
        using var fallback = JsonDocument.Parse(JsonSerializer.Serialize(DiscordNotifier.Payload(message with
        {
            RewardImage = null
        })));
        Check("Missing artwork keeps complete text receipt", fallback.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString() == message.Description);
        var receipt = new MissionRewardTracker();
        var screen = new ScreenRecognizer();
        for (int i = 0; i < 11; i++)
        {
            var card = observation with
            {
                CapturedAt = now.AddMilliseconds(i * 50),
                Regions = observation.Regions.Select(r => r.Pass == "reward-title" ? r with { Text = "Material " + i } : r).ToArray()
            };
            receipt.Observe(card, screen.Recognize(card), null);
        }
        var menu = new ScreenEvidence(ScreenKind.SaveTheWorldHub, [], []);
        receipt.Observe(observation with
        {
            CapturedAt = now.AddSeconds(1)
        }, menu, null);
        receipt.Observe(observation with
        {
            CapturedAt = now.AddSeconds(2)
        }, menu, null);
        var pages = new List<int>();
        var notifications = new RunNotifications((_, _, _, _) => { }, (items, _, _) => pages.Add(items.Count));
        notifications.Rewards(receipt);
        notifications.Rewards(receipt);
        Check("Overflow paginates without dropping or repeating rewards", pages.SequenceEqual(new[] { 10, 1 }));
        return checks;
    }

    internal static async Task Preview(string captures, string output)
    {
        Directory.CreateDirectory(output);
        using var frame = new Bitmap(Path.Combine(captures, "people-xp-reward.png"));
        var observation = await new OcrService().ReadAsync(frame);
        var thumbs = new RewardThumbnails();
        thumbs.Observe(Guid.NewGuid(), observation, frame);
        File.WriteAllBytes(Path.Combine(output, "ten-rewards.png"), RewardReceiptArtwork.Render(Samples, new Dictionary<string, byte[]>(),
            Samples.Select(r => r with { Quantity = r.Quantity * 3 }).ToArray(),
            Samples.Select(r => r with { Quantity = r.Quantity * 12 }).ToArray()));
        File.WriteAllBytes(Path.Combine(output, "one-reward.png"), RewardReceiptArtwork.Render([Samples[0]], thumbs.Images));
        var checks = Run(output);
        File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(checks, Inspection.Json));
        if (checks.Any(c => !c.Passed))
        {
            throw new InvalidOperationException("Reward receipt checks failed.");
        }
    }
}
