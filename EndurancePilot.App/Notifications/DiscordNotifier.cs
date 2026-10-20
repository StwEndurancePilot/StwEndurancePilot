using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace EndurancePilot.App;

internal sealed record PilotNotification(WebhookEvent Event, string Title, string Description, string Zone, int Target, int? Wave = null, bool IsTest = false, byte[]? Screenshot = null,
    byte[]? RewardImage = null, bool IsRewardReceipt = false);

internal sealed class DiscordNotifier : IAsyncDisposable
{
    private static readonly JsonSerializerOptions PayloadJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private sealed record Delivery(PilotNotification Message, bool Test, int Revision, CancellationToken Token, TaskCompletionSource<string> Completion);
    private readonly object gate = new();
    private readonly HttpClient client;
    private readonly Channel<Delivery> queue = Channel.CreateBounded<Delivery>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly List<CancellationTokenSource> generations = [];
    private CancellationTokenSource generation = new();
    private WebhookSettings settings = new();
    private Uri? endpoint;
    private int revision;
    private bool blocked;
    private bool disposed;
    private readonly Task worker;
    private volatile string status = "Notifications are off.";
    internal string Status => status;

    internal DiscordNotifier(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EndurancePilot/0.3.17");
        generations.Add(generation);
        worker = Task.Run(ProcessAsync);
    }

    internal void Configure(WebhookSettings value)
    {
        lock (gate)
        {
            generation.Cancel();
            generation = new();
            generations.Add(generation);
            revision++;
            settings = value;
            endpoint = null;
            blocked = false;
            try
            {
                var url = value.ReadUrl();
                if (url.Length > 0)
                {
                    endpoint = WebhookSettings.Endpoint(url);
                }

                status = !value.Enabled ? "Notifications are off." : endpoint is null ? "Notifications are on. Add and save a webhook URL." : "Notifications are on. Ready to send.";
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException or InvalidDataException)
            {
                status = "Saved webhook cannot be opened. Paste and save it again.";
            }
        }
    }

    internal Task<string> Send(PilotNotification message, bool test = false)
    {
        lock (gate)
        {
            if (disposed)
            {
                return Task.FromResult("Notifications have closed.");
            }

            if (!settings.Enabled || endpoint is null || blocked)
            {
                return Task.FromResult(status);
            }

            if (!test && !settings.Events.HasFlag(message.Event))
            {
                return Task.FromResult("Event disabled.");
            }

            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!queue.Writer.TryWrite(new(message with
            {
                IsTest = test
            }, test, revision, generation.Token, done)))
            {
                done.SetResult(status = "Notification queue is full; this message was skipped.");
            }

            return done.Task;
        }
    }

    private async Task ProcessAsync()
    {
        await foreach (var delivery in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            string result;
            try
            {
                result = await DeliverAsync(delivery).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { result = delivery.Token.IsCancellationRequested ? "Pending notification cancelled." : "Discord timed out; delivery could not be confirmed."; }
            catch (Exception) { result = "Could not reach Discord; delivery could not be confirmed."; }

            lock (gate)
            {
                if (delivery.Revision == revision && !delivery.Token.IsCancellationRequested)
                {
                    status = result;
                }
            }
            delivery.Completion.TrySetResult(result);
        }
    }

    private async Task<string> DeliverAsync(Delivery delivery)
    {
        Uri target;
        lock (gate)
        {
            if (delivery.Revision != revision || delivery.Token.IsCancellationRequested || endpoint is null)
            {
                return "Pending notification cancelled.";
            }

            if (blocked)
            {
                return status;
            }

            target = endpoint;
        }
        for (int attempt = 0; attempt < 3; attempt++)
        {
            delivery.Token.ThrowIfCancellationRequested();
            using var content = Content(delivery.Message);
            using var response = await client.PostAsync(target, content, delivery.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                SetStatus(delivery, "Last message sent at " + DateTime.Now.ToString("HH:mm:ss") + ".");
                delivery.Completion.TrySetResult("Message sent to Discord.");
                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0"
                    && response.Headers.TryGetValues("X-RateLimit-Reset-After", out var reset)
                    && double.TryParse(reset.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 3600)), delivery.Token).ConfigureAwait(false);
                }

                return "Last message sent at " + DateTime.Now.ToString("HH:mm:ss") + ".";
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                double seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0;
                if (seconds <= 0 && response.Headers.RetryAfter?.Date is { } date)
                {
                    seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
                }

                if (seconds <= 0)
                {
                    try
                    {
                        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(delivery.Token).ConfigureAwait(false));
                        seconds = body.RootElement.GetProperty("retry_after").GetDouble();
                    }
                    catch (JsonException) { seconds = 5; }
                    catch (KeyNotFoundException) { seconds = 5; }
                }
                if (!double.IsFinite(seconds) || seconds < 0 || seconds > 3600)
                {
                    return "Discord rate limit is too long; message skipped.";
                }

                SetStatus(delivery, "Discord rate limited delivery; waiting to retry.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(seconds, 0.1)), delivery.Token).ConfigureAwait(false);
                continue;
            }
            if ((int)response.StatusCode >= 500 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), delivery.Token).ConfigureAwait(false);
                continue;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                lock (gate)
                {
                    if (delivery.Revision == revision)
                    {
                        blocked = true;
                    }
                }
                return "Discord rejected the webhook (HTTP " + (int)response.StatusCode + "). Check the URL/channel, then save again.";
            }
            return "Discord delivery failed (HTTP " + (int)response.StatusCode + ").";
        }
        return "Discord is still rate limiting; message skipped after three attempts.";
    }

    private void SetStatus(Delivery delivery, string value)
    {
        lock (gate)
        {
            if (delivery.Revision == revision)
            {
                status = value;
            }
        }
    }

    private static bool HasRewardImage(PilotNotification message) => message.IsRewardReceipt
        && message.RewardImage is { Length: > 0 and <= NotificationScreenshot.MaxBytes };
    private static bool HasImage(PilotNotification message) => HasRewardImage(message)
        || message.Event is (WebhookEvent.Error or WebhookEvent.WaveReached) && !message.IsTest
        && message.Screenshot is { Length: > 0 and <= NotificationScreenshot.MaxBytes };
    private static string ImageFilename(PilotNotification message) => HasRewardImage(message) ? "mission-rewards.png"
        : message.Event == WebhookEvent.WaveReached ? "fortnite-wave.jpg" : "fortnite-error.jpg";

    internal static HttpContent Content(PilotNotification message)
    {
        if (!HasImage(message))
        {
            return JsonContent.Create(Payload(message), options: PayloadJson);
        }

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(JsonSerializer.Serialize(Payload(message), PayloadJson), System.Text.Encoding.UTF8, "application/json"), "payload_json");
        var file = new ByteArrayContent(HasRewardImage(message) ? message.RewardImage! : message.Screenshot!);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(HasRewardImage(message) ? "image/png" : "image/jpeg");
        content.Add(file, "files[0]", ImageFilename(message));
        return content;
    }

    internal static object Payload(PilotNotification message) => new
    {
        username = "Endurance Pilot",
        allowed_mentions = new
        {
            parse = Array.Empty<string>()
        },
        attachments = HasImage(message) ? new[]{new {id=0,filename=ImageFilename(message),description=HasRewardImage(message)
            ? message.Description.Replace("**","")[..Math.Min(1024,message.Description.Replace("**","").Length)]
            : message.Event==WebhookEvent.WaveReached ? "Fortnite wave screen" : "Fortnite error screen"}} : [],
        embeds = new[] { new {
            title=HasRewardImage(message) ? null : message.Title,
            description=HasRewardImage(message) ? null : string.IsNullOrWhiteSpace(message.Description) ? null : message.Description,
            color=Color(message.Event),
            image=HasImage(message) ? new {url="attachment://"+ImageFilename(message)} : null,
            footer=new { text=Context(message) }
        } }
    };

    private static string Context(PilotNotification message)
    {
        string zone = message.Zone switch
        {
            "TwinePeaks" => "Twine Peaks",
            "CannyValley" => "Canny Valley",
            _ => message.Zone
        };

        string progress = message.Event == WebhookEvent.RunnerStarted ? $" · Target {message.Target}"
            : !message.IsRewardReceipt && message.Event != WebhookEvent.WaveReached && message.Wave is { } wave ? $" · Wave {wave}" : "";
        return (message.IsTest ? "Sample · " : "") + zone + progress;
    }
    private static int Color(WebhookEvent kind) => kind switch
    {
        WebhookEvent.Crash or WebhookEvent.Error or WebhookEvent.Stuck => 0xD78376,
        WebhookEvent.Completed30 or WebhookEvent.TargetReached or WebhookEvent.Recovery => 0x83B59B,
        _ => 0x8899AA
    };
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queue.Writer.TryComplete();
        }

        if (await Task.WhenAny(worker, Task.Delay(3000)).ConfigureAwait(false) != worker)
        {
            lock (gate)
            {
                generation.Cancel();
            }
        }

        await worker.ConfigureAwait(false);
        client.Dispose();
        foreach (var source in generations)
        {
            source.Dispose();
        }
    }
}

