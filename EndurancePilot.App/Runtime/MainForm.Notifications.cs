namespace EndurancePilot.App;

public sealed partial class MainForm
{
    private readonly DiscordNotifier notifier = new();
    private WebhookSettings webhook = new();
    private RunNotifications notifications = null!;
    private bool notificationSession;
    private Bitmap? notificationFrame;
    private readonly RewardThumbnails rewardThumbnails = new();
    internal string WebhookStatus => notifier.Status;

    private void NotifyRewards(IReadOnlyList<EndurancePilot.Core.MissionReward> items, int page, int pages)
    {
        if (!notificationSession || !webhook.Enabled || !webhook.Events.HasFlag(WebhookEvent.EnduranceEnded))
        {
            return;
        }

        byte[]? artwork = null;
        try
        {
            artwork = RewardReceiptArtwork.Render(items, rewardThumbnails.Images, Earnings.Session, Earnings.Lifetime);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
        }
        var title = pages == 1 ? "Mission rewards received" : $"Mission rewards received ({page}/{pages})";
        var description = string.Join("\n", items.Select(r =>
        {
            var session = Earnings.Session.FirstOrDefault(x => string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase));
            var lifetime = Earnings.Lifetime.FirstOrDefault(x => string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase));
            return $"**{r.Name}** · Session {session?.QuantityLabel ?? "unavailable"} · Lifetime {lifetime?.QuantityLabel ?? "unavailable"}";
        }));
        _ = notifier.Send(new(WebhookEvent.EnduranceEnded, title, description, controller.Profile.Zone.ToString(),
            controller.Profile.TargetWave, tracker.Current?.HighestWave, RewardImage: artwork, IsRewardReceipt: true));
    }
    private void Notify(WebhookEvent kind, string title, string description, int? wave = null)
    {
        if (!notificationSession)
        {
            return;
        }

        var screenshot = kind is (WebhookEvent.Error or WebhookEvent.WaveReached) && webhook.Enabled && webhook.Events.HasFlag(kind)
            && notificationFrame is { } frame ? NotificationScreenshot.Encode(frame) : null;
        _ = notifier.Send(new(kind, title, description, controller.Profile.Zone.ToString(), controller.Profile.TargetWave, wave, Screenshot: screenshot));
    }
    internal void ApplyWebhook(WebhookSettings value)
    {

        if (value.Enabled && value.ProtectedUrl.Length > 0)
        {
            _ = WebhookSettings.Endpoint(value.ReadUrl());
        }

        settingsStore.Save(ReadSettings() with
        {
            Webhook = value
        });
        ConfigureWebhook(value);
    }
    private void ConfigureWebhook(WebhookSettings value)
    {

        if (webhook == value)
        {
            return;
        }

        webhook = value;
        notifications.ConfigureWaves(value);
        notifier.Configure(value);
    }
    internal Task<string> TestWebhook(WebhookEvent? kind) => kind is null
        ? WebhookSamples.SendAll(notifier, ReadSettings().Zone.ToString(), targetWave)
        : notifier.Send(WebhookSamples.Create(kind, ReadSettings().Zone.ToString(), targetWave), test: true);
}
