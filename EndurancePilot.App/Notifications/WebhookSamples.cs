namespace EndurancePilot.App;

internal static class WebhookSamples
{
    internal static IReadOnlyList<PilotNotification> All(string zone, int target)
    {
        var samples = Enum.GetValues<WebhookEvent>().Where(e => e != WebhookEvent.All)
            .Select(e => Create(e, zone, target)).ToList();
        var items = RewardItems;
        samples.Add(new(WebhookEvent.EnduranceEnded, "Mission Rewards",
            string.Join("\n", items.Select(r => $"{r.Name} · Session × {r.Quantity * 3:N0} · Lifetime × {r.Quantity * 12:N0}")), zone, target,
            IsTest: true, RewardImage: RewardReceiptArtwork.Render(items, new Dictionary<string, byte[]>(),
                items.Select(r => r with { Quantity = r.Quantity * 3 }).ToArray(),
                items.Select(r => r with { Quantity = r.Quantity * 12 }).ToArray()), IsRewardReceipt: true));
        return samples;
    }

    internal static async Task<string> SendAll(DiscordNotifier notifier, string zone, int target)
    {
        var samples = All(zone, target);
        int sent = 0;
        foreach (var sample in samples)
        {
            var result = await notifier.Send(sample, test: true);
            if (result != "Message sent to Discord.")
            {
                return $"Sent {sent} of {samples.Count} samples. {result}";
            }

            sent++;
        }
        return $"All {sent} sample embeds sent to Discord.";
    }
    internal static EndurancePilot.Core.MissionReward[] RewardItems =>
    [
        new("People XP",3150,"★"),new("Season XP",28000,"★"),new("Gold",420,"●"),
        new("Legendary PERK-UP!",60,"◆"),new("Epic PERK-UP!",110,"◆"),
        new("RE-PERK!",250,"↻"),new("Pure Drop of Rain",24,"♦"),
        new("Lightning in a Bottle",12,"ϟ"),new("Eye of the Storm",8,"◉"),
        new("Mini Reward Llama",2,"◇")
    ];
    internal static PilotNotification Create(WebhookEvent? kind, string zone, int target)
    {
        var item = kind switch
        {
            WebhookEvent.RunnerStarted => ("Runner started", "Preparing to run endurance.", (int?)null),
            WebhookEvent.RunnerStopped => ("Runner stopped", "Stopped by you. Fortnite remains open.", (int?)Math.Min(18, target)),
            WebhookEvent.EnduranceStarted => ("Endurance started", "Wave 1 is confirmed active.", (int?)1),
            WebhookEvent.EnduranceEnded => ("Endurance ended", "Run interrupted before a confirmed finish.", (int?)Math.Min(18, target)),
            WebhookEvent.TargetReached when target < 30 => ("Target reached", $"Wave {target} completed. Leaving the mission.", (int?)(target + 1)),
            WebhookEvent.TargetReached => ("Target reached", "All 30 waves completed. Victory confirmed.", (int?)30),
            WebhookEvent.Completed30 => ("Endurance complete", "All 30 waves completed. Victory confirmed.", (int?)30),
            WebhookEvent.WaveReached => ("Wave 5 reached", "", (int?)5),
            WebhookEvent.Crash => ("Fortnite closed unexpectedly", "Check the game or wait for automatic recovery.", (int?)Math.Min(18, target)),
            WebhookEvent.Error => ("Fortnite needs attention", "Recovery will be attempted if supported. Otherwise, check the game.", (int?)Math.Min(18, target)),
            WebhookEvent.Stuck => ("Runner needs attention", "Unable to continue. Check Fortnite.", (int?)Math.Min(18, target)),
            WebhookEvent.Launch => ("Restarting Fortnite", "Waiting for Epic to open the game.", (int?)null),
            WebhookEvent.Recovery => ("Recovery confirmed", "An error was dismissed or a disconnected session abandoned.", (int?)Math.Min(18, target)),
            _ => ("Webhook connected", "Your Discord connection is ready. Choose the events you want in Settings.", (int?)null)
        };
        return new(kind ?? WebhookEvent.RunnerStarted, item.Item1, item.Item2, zone, target, item.Item3, IsTest: true);
    }
}
