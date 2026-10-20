using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class WaveNotificationChecks
{
    internal static IReadOnlyList<StartupCheck> Run()
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new("Wave notifications: " + name, passed, passed ? "Passed (offline)" : "Failed"));
        var settings = new WebhookSettings();
        Check("Default frequency is five and event is off", settings.WaveNotificationFrequency == 5 && !settings.Events.HasFlag(WebhookEvent.WaveReached));
        var legacy = JsonSerializer.Deserialize<WebhookSettings>("{\"Enabled\":true,\"Events\":\"All\"}", Inspection.Json)!;
        Check("Upgrades preserve old events without enabling waves", legacy.Events == WebhookEvent.All && !legacy.Events.HasFlag(WebhookEvent.WaveReached) && legacy.WaveNotificationFrequency == 5);
        settings = settings with
        {
            Enabled = true,
            Events = settings.Events | WebhookEvent.WaveReached,
            WaveNotificationFrequency = 7
        };
        Check("Enabled frequency survives serialization", JsonSerializer.Deserialize<WebhookSettings>(JsonSerializer.Serialize(settings, Inspection.Json), Inspection.Json) == settings);
        foreach (var invalid in new[] { 0, 31, -1 })
        {
            bool rejected = false;
            try
            {
                new RunnerSettings(Webhook: settings with
                {
                    WaveNotificationFrequency = invalid
                }).Validate();
            }
            catch (InvalidDataException) { rejected = true; }
            Check("Reject invalid frequency " + invalid, rejected);
        }
        var messages = new List<(string Title, int? Wave)>();
        var monitor = new RunNotifications((kind, title, _, wave) => { if (kind == WebhookEvent.WaveReached) { messages.Add((title, wave)); } });
        var epoch = DateTimeOffset.UtcNow;
        var run = new RunRecord(Guid.NewGuid(), epoch, null, 1, RunOutcome.InProgress);
        int second = 0;
        void Observe(int wave, RunOutcome outcome = RunOutcome.InProgress) => monitor.Observe(run with
        {
            HighestWave = wave,
            Outcome = outcome
        }, 30, false, ScreenKind.ActiveEndurance, epoch.AddSeconds(++second), 0);
        monitor.ConfigureWaves(new());
        Observe(5);
        Check("Disabled event emits nothing", messages.Count == 0);
        monitor.ConfigureWaves(settings with
        {
            WaveNotificationFrequency = 5
        });
        Observe(5);
        Check("Enabling does not replay the current wave", messages.Count == 0);
        Observe(6);
        Observe(10);
        Observe(10);
        Observe(9);
        Observe(10);
        Check("Only new confirmed multiples emit once", messages.SequenceEqual(new[] { ("Wave 10 reached", (int?)10) }));
        Observe(16);
        Check("Skipped milestones do not generate invented wave messages", messages.Count == 1);
        monitor.ConfigureWaves(settings);
        Observe(21);
        Observe(21);
        Check("Custom frequency applies without duplicate polling", messages.Count == 2 && messages.Last() == ("Wave 21 reached", 21));
        monitor.ConfigureWaves(settings with
        {
            Enabled = false
        });
        Observe(28);
        Check("Master switch suppresses milestones", messages.Count == 2);
        monitor.ConfigureWaves(settings);
        Observe(28);
        Check("Unmuting does not backfill old milestones", messages.Count == 2);
        run = run with
        {
            Id = Guid.NewGuid()
        };
        Observe(7);
        Check("New endurance can report the same milestone", messages.Count == 3 && messages.Last().Wave == 7);
        Observe(14, RunOutcome.Completed30);
        Check("Ended run does not emit wave progress", messages.Count == 3);
        monitor.Observe(run with
        {
            HighestWave = 14
        }, 30, false, ScreenKind.ActiveEndurance, epoch, 0);
        Check("Out-of-order observation cannot notify", messages.Count == 3);
        monitor.Reset();
        messages.Clear();
        monitor.ConfigureWaves(settings with
        {
            WaveNotificationFrequency = 5
        });
        for (int wave = 1; wave <= 30; wave++)
        {
            Observe(wave);
        }

        Check("Five-wave schedule reports 5 through 30", messages.Select(m => m.Wave).SequenceEqual(new int?[] { 5, 10, 15, 20, 25, 30 }));
        monitor.Reset();
        messages.Clear();
        monitor.ConfigureWaves(settings with
        {
            WaveNotificationFrequency = 1
        });
        Observe(1);
        Observe(2);
        Observe(3);
        Check("Frequency one reports each confirmed wave", messages.Select(m => m.Wave).SequenceEqual(new int?[] { 1, 2, 3 }));
        return checks;
    }
}
