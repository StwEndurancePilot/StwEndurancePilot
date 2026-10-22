namespace EndurancePilot.App;

internal static class SettingsChecks
{
    public static IReadOnlyList<StartupCheck> Run(string output)
    {
        var report = new List<StartupCheck>();
        string path = Path.Combine(output, "settings-test-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new SettingsStore(path);
        void Check(string name, bool passed) => report.Add(new(name, passed, passed ? "Passed" : "Failed"));
        try
        {
            Check("Missing preferences use defaults", store.Load(out var warning) == new RunnerSettings() && warning is null);
            Check("New installations run indefinitely by default", store.Load(out warning).RunIndefinitely);
            Check("Default target is wave 30", store.Load(out warning).TargetWave == 30);
            Check("End at target defaults on", store.Load(out warning).EndRunWhenTargetReached);
            Check("Wait for assistance defaults off", !store.Load(out warning).WaitForAssistance);
            var saved = new RunnerSettings(EndurancePilot.Core.HomebaseZone.CannyValley, 3, false, false);
            store.Save(saved);
            Check("Preferences survive a new store instance", new SettingsStore(path).Load(out warning) == saved && warning is null);
            saved = saved with
            {
                AttemptLimit = 5
            };
            store.Save(saved);
            Check("Preferences replace the old file", store.Load(out warning) == saved && !File.Exists(path + ".tmp"));
            saved = saved with
            {
                RunIndefinitely = true
            };
            store.Save(saved);
            Check("Indefinite preference persists with remembered fixed count", new SettingsStore(path).Load(out warning) == saved && warning is null);
            saved = saved with
            {
                ManualThemeEnabled = true,
                ManualDarkTheme = false
            };
            store.Save(saved);
            Check("Manual light theme survives restart", new SettingsStore(path).Load(out warning) == saved && warning is null);
            saved = saved with
            {
                ManualThemeEnabled = false
            };
            store.Save(saved);
            Check("Automatic theme retains the manual colour choice", new SettingsStore(path).Load(out warning) == saved && warning is null);
            saved = saved with
            {
                TargetWave = 26
            };
            store.Save(saved);
            Check("Target wave survives restart", new SettingsStore(path).Load(out warning) == saved && warning is null);
            saved = saved with
            {
                EndRunWhenTargetReached = false
            };
            store.Save(saved);
            Check("Disabled target exit persists and remembers the target", new SettingsStore(path).Load(out warning) == saved
                && saved.TargetWave == 26 && warning is null);
            saved = saved with { WaitForAssistance = true };
            store.Save(saved);
            Check("Wait for assistance survives restart", new SettingsStore(path).Load(out warning) == saved && warning is null);
            var priorContext = SynchronizationContext.Current;
            var host = new MainForm(store);
            try
            {
                var firstSetup = new WebhookSettings(Enabled: true);
                host.ApplyWebhook(firstSetup);
                Check("First setup can enable before saving a webhook URL",
                    host.ReadSettings().Webhook == firstSetup && store.Load(out warning).Webhook == firstSetup
                    && host.WebhookStatus == "Notifications are on. Add and save a webhook URL.");
                host.ApplyWebhook(firstSetup with
                {
                    Enabled = false
                });
                Check("First setup second toggle turns notifications off normally",
                    host.ReadSettings().Webhook!.Enabled == false && host.WebhookStatus == "Notifications are off.");
                host.ApplyWebhook(firstSetup);
                var firstUrl = firstSetup.WithUrl("https://discord.com/api/webhooks/123456789/offline_test_token");
                host.ApplyWebhook(firstUrl);
                Check("Saving first URL preserves the enabled toggle and readies sender",
                    host.ReadSettings().Webhook == firstUrl && host.WebhookStatus == "Notifications are on. Ready to send.");
                host.ApplyWebhook(firstUrl.WithUrl(""));
                Check("Clearing URL preserves toggle and returns to awaiting setup",
                    host.ReadSettings().Webhook!.Enabled && host.WebhookStatus == "Notifications are on. Add and save a webhook URL.");
                var enabled = new WebhookSettings(Enabled: true).WithUrl("https://discord.com/api/webhooks/123456789/offline_test_token");
                host.ApplySettings(host.ReadSettings() with
                {
                    Webhook = enabled
                });
                Check("General settings synchronize enabled notification status", host.WebhookStatus == "Notifications are on. Ready to send.");
                host.ApplySettings(host.ReadSettings() with
                {
                    TargetWave = 25,
                    Webhook = null
                });
                Check("Run option changes preserve enabled notifications and saved URL",
                    host.ReadSettings().Webhook == enabled && store.Load(out warning).Webhook == enabled
                    && host.WebhookStatus == "Notifications are on. Ready to send.");
                host.ApplySettings(host.ReadSettings() with
                {
                    Webhook = enabled with
                    {
                        Enabled = false
                    }
                });
                Check("General settings synchronize disabled notification status", host.WebhookStatus == "Notifications are off.");
            }
            finally { host.ShutdownBridge(); SynchronizationContext.SetSynchronizationContext(priorContext); }
            File.WriteAllText(path, "{\"Zone\":\"TwinePeaks\",\"AttemptLimit\":2,\"AfkBaseReady\":true,\"LaunchIfMissing\":true}");
            var legacy = store.Load(out warning);
            Check("Missing indefinite preference defaults on and retains the fixed count", legacy.RunIndefinitely && legacy.AttemptLimit == 2 && warning is null);
            Check("Existing settings default to Windows theme", !legacy.ManualThemeEnabled);
            Check("Existing settings default to target wave 30", legacy.TargetWave == 30);
            Check("Existing settings retain automatic target exit", legacy.EndRunWhenTargetReached);
            Check("Existing settings retain solo Endurance", !legacy.WaitForAssistance);
            File.WriteAllText(path, "{\"Zone\":\"TwinePeaks\",\"AttemptLimit\":2,\"AfkBaseReady\":false,\"LaunchIfMissing\":true}");
            Check("Removed AFK preference does not invalidate existing settings", store.Load(out warning) == legacy && warning is null);
            File.WriteAllText(path, "broken json");
            Check("Corrupt preferences recover with a visible warning", store.Load(out warning) == new RunnerSettings() && warning is not null);
            File.WriteAllText(path, "{\"AttemptLimit\":0}");
            Check("Invalid preferences cannot enter the UI", store.Load(out warning) == new RunnerSettings() && warning is not null);
            bool rejected = false;
            try
            {
                store.Save(new(AttemptLimit: 101));
            }
            catch (InvalidDataException) { rejected = true; }
            Check("Out-of-range limits cannot be saved", rejected);
            foreach (int wave in new[] { 0, 31 })
            {
                rejected = false;
                try
                {
                    store.Save(new(TargetWave: wave));
                }
                catch (InvalidDataException) { rejected = true; }
                Check($"Invalid target wave {wave} cannot be saved", rejected);
            }
        }
        catch (Exception error) { report.Add(new("Settings checks", false, error.Message)); }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + ".tmp"))
            {
                File.Delete(path + ".tmp");
            }
        }
        return report;
    }
}
