using System.Net;
using System.Net.Http;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class WebhookChecks
{

    private sealed class FakeHandler(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Calls;
        internal readonly List<string> Bodies = [];
        internal readonly List<Uri> Uris = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            Uris.Add(request.RequestUri!);
            return await respond(Interlocked.Increment(ref Calls), request, token);
        }
    }
    private static HttpResponseMessage Response(HttpStatusCode code, string text = "{}") => new(code) { Content = new StringContent(text) };

    internal static async Task<IReadOnlyList<StartupCheck>> RunAsync()
    {
        var results = new List<StartupCheck>();
        void Check(string name, bool pass) => results.Add(new("Webhook: " + name, pass, pass ? "Passed (offline)" : "Failed"));
        const string url = "https://discord.com/api/webhooks/123456789/offline_test_token";
        var message = new PilotNotification(WebhookEvent.EnduranceStarted, "Endurance started", "Wave 1 confirmed.", "TwinePeaks", 30, 1);
        try
        {
            var config = new WebhookSettings(Enabled: true).WithUrl(url);
            Check("URL encrypted at rest and recoverable", config.ReadUrl() == url && !JsonSerializer.Serialize(config).Contains("offline_test_token"));
            var stored = new RunnerSettings(Webhook: config with
            {
                Events = WebhookEvent.Crash | WebhookEvent.TargetReached
            });
            var restored = JsonSerializer.Deserialize<RunnerSettings>(JsonSerializer.Serialize(stored, Inspection.Json), Inspection.Json)!;
            Check("Event preferences and encrypted URL survive settings round-trip", restored.Webhook!.ReadUrl() == url && restored.Webhook.Events == (WebhookEvent.Crash | WebhookEvent.TargetReached));
            Check("Existing settings default to notifications off", !(new RunnerSettings().Webhook ?? new()).Enabled);
            Check("Endpoint requests delivery confirmation", WebhookSettings.Endpoint(url).Query == "?wait=true");
            Check("Forum thread preserved", WebhookSettings.Endpoint(url + "?thread_id=123&wait=false").Query == "?wait=true&thread_id=123");
            foreach (var bad in new[] { "http://discord.com/api/webhooks/1/token", "https://discord.com.evil.test/api/webhooks/1/token", "https://example.com/api/webhooks/1/token", url + "/messages/123", url + "?redirect=https://example.com", url + "#fragment", "https://user@discord.com/api/webhooks/1/token" })
            {
                bool rejected = false;
                try
                {
                    _ = WebhookSettings.Endpoint(bad);
                }
                catch (InvalidDataException) { rejected = true; }
                Check("Reject invalid destination " + results.Count, rejected);
            }
            var handler = new FakeHandler((_, _, _) => Task.FromResult(Response(HttpStatusCode.OK)));
            await using (var notifier = new DiscordNotifier(handler))
            {
                await notifier.Send(message);
                Check("No request before configuration", handler.Calls == 0);
                notifier.Configure(config with
                {
                    Enabled = false
                });
                await notifier.Send(message);
                Check("Master off prevents delivery", handler.Calls == 0);
                Check("Disabled status matches master toggle", notifier.Status == "Notifications are off.");
                notifier.Configure(config);
                Check("Enabling notifications clears the off status", notifier.Status == "Notifications are on. Ready to send.");
                notifier.Configure(config with
                {
                    ProtectedUrl = ""
                });
                Check("Enabled without URL is not reported as off", notifier.Status == "Notifications are on. Add and save a webhook URL.");
                foreach (var kind in Enum.GetValues<WebhookEvent>().Where(e => e != WebhookEvent.All))
                {
                    notifier.Configure(config with
                    {
                        Events = WebhookEvent.All & ~kind
                    });
                    await notifier.Send(message with
                    {
                        Event = kind
                    });
                }
                Check("All event toggles suppress delivery independently", handler.Calls == 0);
                notifier.Configure(config with
                {
                    Events = 0
                });
                await notifier.Send(message, test: true);
                Check("Explicit test ignores event filters", handler.Calls == 1);
                notifier.Configure(config);
                Check("Successful delivery reported", (await notifier.Send(message)).Contains("sent"));
                Check("Valid delivery uses saved endpoint", handler.Uris.All(u => u.Query == "?wait=true"));
                using var payload = JsonDocument.Parse(handler.Bodies.Last());
                Check("Payload keeps compact context and prevents mentions", payload.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0
                    && !payload.RootElement.GetProperty("embeds")[0].TryGetProperty("fields", out _)
                    && payload.RootElement.GetProperty("embeds")[0].GetProperty("footer").GetProperty("text").GetString()!.Contains("Twine Peaks"));
                notifier.Configure(config with
                {
                    ProtectedUrl = "invalid"
                });
                await notifier.Send(message);
                Check("Unreadable saved URL fails closed", handler.Calls == 2 && notifier.Status.Contains("Paste"));
            }
            handler = new FakeHandler((n, _, _) => Task.FromResult(n == 1 ? Response(HttpStatusCode.TooManyRequests, "{\"retry_after\":0.01}") : Response(HttpStatusCode.OK)));
            await using (var notifier = new DiscordNotifier(handler))
            {
                notifier.Configure(config);
                var result = await notifier.Send(message);
                Check("429 waits and retries", handler.Calls == 2 && result.Contains("sent"));
            }
            handler = new FakeHandler((_, _, _) => Task.FromResult(Response(HttpStatusCode.NotFound)));
            await using (var notifier = new DiscordNotifier(handler))
            {
                notifier.Configure(config);
                await notifier.Send(message);
                await notifier.Send(message);
                Check("Deleted webhook blocked until configuration changes", handler.Calls == 1 && notifier.Status.Contains("404"));
                notifier.Configure(config);
                await notifier.Send(message);
                Check("Saving permits a fresh attempt", handler.Calls == 2);
            }
            handler = new FakeHandler((_, _, _) => throw new HttpRequestException("Token might be present here: " + url));
            await using (var notifier = new DiscordNotifier(handler))
            {
                notifier.Configure(config);
                var result = await notifier.Send(message);
                Check("Transport failures do not expose secrets or retry ambiguous deliveries", handler.Calls == 1 && !result.Contains("token") && !result.Contains("https") && result.Contains("could not be confirmed"));
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = new FakeHandler(async (_, _, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Response(HttpStatusCode.OK); });
            await using (var notifier = new DiscordNotifier(handler))
            {
                notifier.Configure(config);
                var first = notifier.Send(message);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var queued = notifier.Send(message);
                notifier.Configure(config with
                {
                    Enabled = false
                });
                await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(2));
                Check("Disabling cancels in-flight and queued requests", handler.Calls == 1 && notifier.Status.Contains("off"));
            }

            byte[]? screenshot;
            using (var frame = new Bitmap(2400, 1200))
            {
                using (var graphics = Graphics.FromImage(frame))
                {
                    graphics.Clear(Color.Navy);
                    graphics.FillRectangle(Brushes.Red, 800, 400, 800, 400);
                }
                screenshot = NotificationScreenshot.Encode(frame);
            }
            Check("Error screenshot is bounded and survives source-frame disposal", screenshot is { Length: > 0 and <= NotificationScreenshot.MaxBytes });
            using (var stream = new MemoryStream(screenshot!))
            using (var decoded = new Bitmap(stream))
            {
                Check("Error screenshot preserves aspect ratio and visible error pixels", decoded.Width == 1920 && decoded.Height == 960 && decoded.GetPixel(960, 480).R > 200);
            }

            var errorMessage = message with
            {
                Event = WebhookEvent.Error,
                Screenshot = screenshot
            };
            bool multipartValid = true;
            handler = new FakeHandler(async (attempt, request, token) =>
            {
                if (request.Content is not MultipartFormDataContent multipart)
                {
                    multipartValid = false;
                    return Response(HttpStatusCode.OK);
                }
                var parts = multipart.ToArray();
                using var json = JsonDocument.Parse(await parts[0].ReadAsStringAsync(token));
                var root = json.RootElement;
                multipartValid &= parts.Length == 2 && parts[0].Headers.ContentDisposition?.Name?.Trim('"') == "payload_json"
                    && parts[1].Headers.ContentDisposition?.Name?.Trim('"') == "files[0]"
                    && parts[1].Headers.ContentDisposition?.FileName?.Trim('"') == "fortnite-error.jpg"
                    && parts[1].Headers.ContentType?.MediaType == "image/jpeg"
                    && (await parts[1].ReadAsByteArrayAsync(token)).SequenceEqual(screenshot!)
                    && root.GetProperty("embeds")[0].GetProperty("image").GetProperty("url").GetString() == "attachment://fortnite-error.jpg"
                    && root.GetProperty("attachments")[0].GetProperty("filename").GetString() == "fortnite-error.jpg"
                    && root.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0;
                return attempt == 1 ? Response(HttpStatusCode.TooManyRequests, "{\"retry_after\":0.01}") : Response(HttpStatusCode.OK);
            });
            await using (var notifier = new DiscordNotifier(handler))
            {
                notifier.Configure(config);
                Check("Error image uploads inside its embed and survives rate-limit retries", (await notifier.Send(errorMessage)).Contains("sent") && handler.Calls == 2 && multipartValid);
                notifier.Configure(config with
                {
                    Events = WebhookEvent.All & ~WebhookEvent.Error
                });
                await notifier.Send(errorMessage);
                Check("Disabling errors also prevents screenshot delivery", handler.Calls == 2);
            }
            foreach (var plain in new[] { message with { Screenshot = screenshot }, errorMessage with { IsTest = true }, errorMessage with { Screenshot = null }, errorMessage with { Screenshot = new byte[NotificationScreenshot.MaxBytes + 1] } })
            {
                using var content = DiscordNotifier.Content(plain);
                using var json = JsonDocument.Parse(await content.ReadAsStringAsync());
                Check("Unrelated, test, missing or oversized images use a text-only embed", content.Headers.ContentType?.MediaType == "application/json"
                    && !json.RootElement.GetProperty("embeds")[0].TryGetProperty("image", out _));
            }

            var waveHandler = new FakeHandler((_, _, _) => Task.FromResult(Response(HttpStatusCode.OK)));
            await using (var waveNotifier = new DiscordNotifier(waveHandler))
            {
                var waveMessage = new PilotNotification(WebhookEvent.WaveReached, "Wave 10 reached", "", "TwinePeaks", 30, 10);
                waveNotifier.Configure(config);
                await waveNotifier.Send(waveMessage);
                Check("Wave notification is opt-in at transport", waveHandler.Calls == 0);
                waveNotifier.Configure(config with
                {
                    Events = config.Events | WebhookEvent.WaveReached
                });
                await waveNotifier.Send(waveMessage);
                using var wavePayload = JsonDocument.Parse(waveHandler.Bodies.Single());
                var embed = wavePayload.RootElement.GetProperty("embeds")[0];
                Check("Wave embed contains requested title and confirmed wave", embed.GetProperty("title").GetString()!.EndsWith("Wave 10 reached")
                    && embed.GetProperty("footer").GetProperty("text").GetString() == "Twine Peaks");
            }
            bool waveImageValid = true;
            waveHandler = new FakeHandler(async (attempt, request, token) =>
            {
                if (request.Content is not MultipartFormDataContent multipart)
                {
                    waveImageValid = false;
                    return Response(HttpStatusCode.BadRequest);
                }
                var parts = multipart.ToArray();
                using var json = JsonDocument.Parse(await parts[0].ReadAsStringAsync(token));
                var root = json.RootElement;
                waveImageValid &= parts.Length == 2 && parts[1].Headers.ContentDisposition?.FileName?.Trim('"') == "fortnite-wave.jpg"
                    && parts[1].Headers.ContentType?.MediaType == "image/jpeg"
                    && (await parts[1].ReadAsByteArrayAsync(token)).SequenceEqual(screenshot!)
                    && root.GetProperty("embeds")[0].GetProperty("image").GetProperty("url").GetString() == "attachment://fortnite-wave.jpg"
                    && root.GetProperty("attachments")[0].GetProperty("filename").GetString() == "fortnite-wave.jpg";
                return attempt == 1 ? Response(HttpStatusCode.TooManyRequests, "{\"retry_after\":0.01}") : Response(HttpStatusCode.OK);
            });
            await using (var waveNotifier = new DiscordNotifier(waveHandler))
            {
                var waveMessage = new PilotNotification(WebhookEvent.WaveReached, "Wave 10 reached", "", "TwinePeaks", 30, 10, Screenshot: screenshot);
                waveNotifier.Configure(config with
                {
                    Events = config.Events | WebhookEvent.WaveReached
                });
                Check("Wave screenshot embeds and retries identical captured bytes", (await waveNotifier.Send(waveMessage)).Contains("sent") && waveImageValid && waveHandler.Calls == 2);
                waveNotifier.Configure(config);
                await waveNotifier.Send(waveMessage);
                Check("Disabling wave notifications prevents screenshot delivery", waveHandler.Calls == 2);
                waveNotifier.Configure(config with
                {
                    Enabled = false,
                    Events = config.Events | WebhookEvent.WaveReached
                });
                await waveNotifier.Send(waveMessage);
                Check("Master off prevents wave screenshot delivery", waveHandler.Calls == 2);
                using var sampleContent = DiscordNotifier.Content(waveMessage with
                {
                    IsTest = true
                });
                Check("Wave samples never attach game screenshots", sampleContent.Headers.ContentType?.MediaType == "application/json");
            }

            var events = new List<WebhookEvent>();
            var monitor = new RunNotifications((kind, _, _, _) => events.Add(kind));
            var now = DateTimeOffset.UtcNow;
            var run = new RunRecord(Guid.NewGuid(), now, null, 1, RunOutcome.InProgress);
            for (int i = 0; i < 10; i++)
            {
                monitor.Observe(run, 26, false, ScreenKind.ActiveEndurance, now.AddSeconds(i), 0);
            }

            Check("Repeated wave frames announce a run once", events.Count(e => e == WebhookEvent.EnduranceStarted) == 1);
            monitor.Observe(run with
            {
                HighestWave = 26
            }, 26, false, ScreenKind.ActiveEndurance, now.AddSeconds(10), 0);
            Check("Target starting is not target completion", !events.Contains(WebhookEvent.TargetReached));
            monitor.Observe(run with
            {
                HighestWave = 27
            }, 26, true, ScreenKind.ActiveEndurance, now.AddSeconds(11), 0);
            monitor.Observe(run with
            {
                HighestWave = 27
            }, 26, true, ScreenKind.ActiveEndurance, now.AddSeconds(12), 0);
            Check("Next wave confirms target exactly once", events.Count(e => e == WebhookEvent.TargetReached) == 1);
            monitor.Outcome(run with
            {
                HighestWave = 27,
                Outcome = RunOutcome.ReachedTarget
            }, 26);
            monitor.Outcome(run with
            {
                HighestWave = 27,
                Outcome = RunOutcome.ReachedTarget
            }, 26);
            Check("Target exit reports end once without claiming 30", events.Count(e => e == WebhookEvent.EnduranceEnded) == 1 && !events.Contains(WebhookEvent.Completed30));
            monitor.Reset();
            events.Clear();
            monitor.Observe(run with
            {
                HighestWave = 30
            }, 30, false, ScreenKind.ActiveEndurance, now, 0);
            Check("Wave 30 alone never announces success", !events.Contains(WebhookEvent.Completed30) && !events.Contains(WebhookEvent.TargetReached));
            var victory = run with
            {
                HighestWave = 30,
                Outcome = RunOutcome.Completed30
            };
            monitor.Outcome(victory, 30);
            monitor.Outcome(victory, 30);
            Check("Victory announces target and all-30 once", events.Count(e => e == WebhookEvent.TargetReached) == 1 && events.Count(e => e == WebhookEvent.Completed30) == 1);
            monitor.Reset();
            events.Clear();
            monitor.Error(2);
            monitor.Error(2);
            monitor.Attention(true, 2);
            monitor.Attention(true, 2);
            Check("Persistent error and attention each alert once", events.Count(e => e == WebhookEvent.Error) == 1 && events.Count(e => e == WebhookEvent.Stuck) == 1);
            monitor.Crash();
            monitor.Crash();
            monitor.Launched();
            monitor.Crash();
            Check("Crash deduplicates until relaunch", events.Count(e => e == WebhookEvent.Crash) == 2 && events.Count(e => e == WebhookEvent.Launch) == 1);
            monitor.Reset();
            events.Clear();
            for (int i = 0; i <= 120; i++)
            {
                monitor.Observe(null, 30, false, ScreenKind.Unknown, now.AddSeconds(i), 0);
            }

            Check("Unknown focused screen alerts once after 90 seconds", events.Count(e => e == WebhookEvent.Stuck) == 1);
            monitor.Reset();
            events.Clear();
            for (int i = 0; i < 120; i++)
            {
                monitor.Observe(null, 30, false, ScreenKind.Unknown, now.AddSeconds(i), 0, entryWait: true);
            }

            Check("Bounded STW entry does not send an early stuck notification", events.Count == 0);
            monitor.QuestMenuTimedOut(null);
            monitor.QuestMenuTimedOut(null);
            monitor.Attention(true, null);
            monitor.Observe(null, 30, false, ScreenKind.Unknown, now.AddSeconds(120), 0);
            Check("STW entry timeout sends one error without duplicate stuck notifications", events.SequenceEqual(new[] { WebhookEvent.Error }));
            monitor.Reset();
            events.Clear();
            for (int i = 0; i <= 120; i++)
            {
                monitor.LoseObservation();
                monitor.Observe(null, 30, false, ScreenKind.Unknown, now.AddSeconds(i), 0);
            }
            Check("Time away from Fortnite does not count as stuck", !events.Contains(WebhookEvent.Stuck));
            monitor.Reset();
            events.Clear();
            for (int i = 0; i <= 320; i++)
            {
                monitor.Observe(run, 30, false, ScreenKind.ActiveEndurance, now.AddSeconds(i), 0);
            }

            Check("Long active waves do not trigger stuck", !events.Contains(WebhookEvent.Stuck));
            monitor.Reset();
            events.Clear();
            monitor.Observe(run, 30, false, ScreenKind.ActiveEndurance, now, 0);
            for (int i = 1; i <= 320; i++)
            {
                monitor.Observe(run, 30, false, ScreenKind.Unknown, now.AddSeconds(i), 0);
            }

            Check("Unreadable HUD during a confirmed endurance never sends possible stuck", !events.Contains(WebhookEvent.Stuck));
            monitor.Reset();
            events.Clear();
            for (int i = 0; i <= 320; i++)
            {
                monitor.Observe(run, 30, false, ScreenKind.Backpack, now.AddSeconds(i), 0);
            }

            Check("In-mission backpack remains quiet", !events.Contains(WebhookEvent.Stuck));
            monitor.Reset();
            events.Clear();
            for (int i = 0; i <= 120; i++)
            {
                monitor.Observe(run, 26, true, ScreenKind.Unknown, now.AddSeconds(i), 0);
            }

            Check("A genuinely stalled target exit still reports possible stuck", events.Count(e => e == WebhookEvent.Stuck) == 1);
            monitor.Reset();
            events.Clear();
            monitor.Observe(run, 30, false, ScreenKind.ActiveEndurance, now, 0);
            for (int i = 1; i <= 120; i++)
            {
                monitor.Observe(run with
                {
                    Outcome = RunOutcome.Interrupted
                }, 30, false, ScreenKind.SaveTheWorldLobby, now.AddSeconds(i), 0);
            }

            Check("Ended run does not suppress later menu stalls", events.Count(e => e == WebhookEvent.Stuck) == 1);
            monitor.Reset();
            events.Clear();
            monitor.Observe(run, 30, false, ScreenKind.ErrorDialog, now, 0);
            monitor.Observe(run, 30, false, ScreenKind.ErrorDialog, now.AddSeconds(1), 0);
            Check("Real error alerts remain enabled during an endurance", events.Count(e => e == WebhookEvent.Error) == 1);
            monitor.Reset();
            events.Clear();
            for (int i = 0; i <= 301; i++)
            {
                monitor.Observe(null, 30, false, ScreenKind.Loading, now.AddSeconds(i), 0);
            }

            Check("Loading has a longer five-minute stuck threshold", events.Count(e => e == WebhookEvent.Stuck) == 1);
            monitor.Observe(null, 30, false, ScreenKind.SaveTheWorldLobby, now.AddSeconds(302), 1);
            monitor.Observe(null, 30, false, ScreenKind.SaveTheWorldLobby, now.AddSeconds(303), 1);
            Check("Confirmed recovery counters notify once", events.Count(e => e == WebhookEvent.Recovery) == 1);
            monitor.Reset(7);
            events.Clear();
            monitor.Observe(null, 30, false, ScreenKind.SaveTheWorldLobby, now.AddSeconds(304), 7);
            Check("Restarting runner does not replay old recoveries", !events.Contains(WebhookEvent.Recovery));
            var receipt = new MissionRewardTracker();
            var received = new Observation(now, 1920, 1080,
                [new("YOU RECEIVED",new(600,100,400,70)),new("People XP",new(90,350,300,30),"reward-title"),
                 new("31",new(900,730,50,30),"reward-count"),new("31",new(900,730,50,30),"reward-count-check")],
                [new("reward-card", new(620, 450, 350, 350), 1, 1)]);
            var sentRewards = new List<PilotNotification>();
            var rewardNotifications = new RunNotifications((kind, title, description, wave) =>
                sentRewards.Add(new(kind, title, description, "TwinePeaks", 30, wave)));
            receipt.Observe(received, new ScreenRecognizer().Recognize(received), null);
            rewardNotifications.Rewards(receipt);
            Check("Scrolling rewards do not send a partial receipt", sentRewards.Count == 0);
            var menu = new ScreenEvidence(ScreenKind.SaveTheWorldHub, [], []);
            receipt.Observe(received with
            {
                CapturedAt = now.AddSeconds(1)
            }, menu, null);
            receipt.Observe(received with
            {
                CapturedAt = now.AddSeconds(2)
            }, menu, null);
            rewardNotifications.Rewards(receipt);
            rewardNotifications.Rewards(receipt);
            Check("Rewards notify once with name, quantity and icon", sentRewards.Count == 1
                && sentRewards[0].Description.Contains("★ People XP — × 31"));
            var rewardHandler = new FakeHandler((_, _, _) => Task.FromResult(Response(HttpStatusCode.OK)));
            await using (var delivery = new DiscordNotifier(rewardHandler))
            {
                delivery.Configure(config);
                await delivery.Send(sentRewards.Single());
                using var body = JsonDocument.Parse(rewardHandler.Bodies.Single());
                Check("Reward receipt is delivered in the Discord embed", body.RootElement.GetProperty("embeds")[0]
                    .GetProperty("description").GetString()!.Contains("People XP — × 31"));
                delivery.Configure(config with
                {
                    Events = WebhookEvent.Crash
                });
                await delivery.Send(sentRewards.Single());
                Check("Reward receipts honor the end-of-run event setting", rewardHandler.Calls == 1);
            }
            foreach (var kind in Enum.GetValues<WebhookEvent>().Where(e => e != WebhookEvent.All))
            {
                var sample = WebhookSamples.Create(kind, "TwinePeaks", 26);
                using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordNotifier.Payload(sample)));
                var embed = payload.RootElement.GetProperty("embeds")[0];
                Check("Preview " + kind + " has matching embed and test footer", sample.Event == kind && embed.GetProperty("title").GetString()!.Contains(sample.Title) && embed.GetProperty("footer").GetProperty("text").GetString()!.StartsWith("Sample") && embed.GetProperty("color").GetInt32() > 0);
            }
            var suite = WebhookSamples.All("TwinePeaks", 26);
            Check("Test suite includes every event and real reward artwork", suite.Count == 13
                && suite.Select(s => s.Event).Distinct().Count() == 12 && suite.All(s => s.IsTest)
                && suite.Single(s => s.IsRewardReceipt).RewardImage is { Length: > 0 });
            var suiteHandler = new FakeHandler((_, _, _) => Task.FromResult(Response(HttpStatusCode.OK)));
            await using (var suiteNotifier = new DiscordNotifier(suiteHandler))
            {
                suiteNotifier.Configure(config with
                {
                    Events = 0
                });
                var result = await WebhookSamples.SendAll(suiteNotifier, "TwinePeaks", 26);
                Check("Test all sends exactly one suite despite event filters", suiteHandler.Calls == 13 && result.StartsWith("All 13"));
            }
            var failureHandler = new FakeHandler((_, _, _) => Task.FromResult(Response(HttpStatusCode.NotFound)));
            await using (var failureNotifier = new DiscordNotifier(failureHandler))
            {
                failureNotifier.Configure(config);
                var result = await WebhookSamples.SendAll(failureNotifier, "TwinePeaks", 26);
                Check("Test suite stops and reports delivery failure", failureHandler.Calls == 1 && result.StartsWith("Sent 0 of 13"));
            }
        }
        catch (Exception e) { results.Add(new("Webhook checks", false, e.GetType().Name + ": " + e.Message)); }
        return results;
    }
}
