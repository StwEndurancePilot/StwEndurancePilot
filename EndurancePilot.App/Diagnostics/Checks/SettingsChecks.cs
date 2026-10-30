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
            Check("Closing keeps Pilot in tray by default without enabling Windows startup", store.Load(out warning) is { CloseToTray: true, StartWithWindows: false });
            Check("Default target is wave 30", store.Load(out warning).TargetWave == 30);
            Check("Penny startup import defaults on", store.Load(out warning).AutoImportPenny);
            Check("End at target defaults on", store.Load(out warning).EndRunWhenTargetReached);
            Check("Wait for assistance defaults off", !store.Load(out warning).WaitForAssistance);
            Check("New settings disable legacy automatic refocus", store.Load(out warning) is { AutoResumeFortnite: false, AutoResumeIdleSeconds: 10 });
            Check("Existing shortcut defaults to F10", store.Load(out warning).StartStopHotkey == "F10"
                && RunnerHotkey.Resolve("F10") is (0x4000u, 0x79u));
            Check("Modified shortcut maps to Ctrl+Alt+F9", RunnerHotkey.Resolve("Ctrl+Alt+F9") is (0x4003u, 0x78u));
            var saved = new RunnerSettings(EndurancePilot.Core.HomebaseZone.CannyValley, 3, false, false, CloseToTray: false, StartWithWindows: true, AutoImportPenny: false);
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
            saved = saved with { AutoResumeFortnite = false, AutoResumeIdleSeconds = 23 };
            store.Save(saved);
            Check("Legacy focus settings survive restart for compatibility", new SettingsStore(path).Load(out warning) == saved && warning is null);
            saved = saved with { StartStopHotkey = "Ctrl+Alt+F8" };
            store.Save(saved);
            Check("Custom start/stop shortcut survives restart", new SettingsStore(path).Load(out warning) == saved && warning is null);
            var priorContext = SynchronizationContext.Current;
            var host = new MainForm(store);
            try
            {
                Check("New app host restores all saved runner preferences", host.ReadSettings() == saved with { Webhook = new WebhookSettings() });
                host.ApplySettings(host.ReadSettings() with { AutoResumeFortnite = true, AutoResumeIdleSeconds = 17 });
                Check("Legacy focus values remain round-trippable without a focus control", host.ReadSettings() is { AutoResumeFortnite: true, AutoResumeIdleSeconds: 17 }
                    && store.Load(out warning).AutoResumeIdleSeconds == 17);
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
            Check("Older settings without focus values disable legacy automatic refocus", legacy is { AutoResumeFortnite: false, AutoResumeIdleSeconds: 10 });
            Check("Existing settings retain F10 as the shortcut", legacy.StartStopHotkey == "F10");
            File.WriteAllText(path, "{\"Zone\":\"TwinePeaks\",\"AttemptLimit\":2,\"AfkBaseReady\":false,\"LaunchIfMissing\":true}");
            Check("Removed AFK preference does not invalidate existing settings", store.Load(out warning) == legacy && warning is null);
            File.WriteAllText(path, "broken json");
            Check("Unreadable settings recover the previous valid save", store.Load(out warning) != new RunnerSettings() && warning is not null);
            var recovered = store.Load(out _);
            store.Save(recovered);
            Check("Recovery restores settings without destroying the valid backup", store.Load(out warning) == recovered && warning is null
                && File.Exists(path + ".bak") && Directory.GetFiles(output, Path.GetFileName(path) + ".unreadable-*").Length == 1);
            var future = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            future["FutureOption"] = "keep across versions";
            future["Webhook"] ??= new System.Text.Json.Nodes.JsonObject();
            future["Webhook"]!["FutureNotification"] = true;
            File.WriteAllText(path, future.ToJsonString());
            var reopened = new SettingsStore(path);
            var migrated = reopened.Load(out warning) with { TargetWave = 17 };
            reopened.Save(migrated);
            var persisted = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            Check("Updating settings preserves unknown fields from another release", persisted["FutureOption"]!.GetValue<string>() == "keep across versions"
                && persisted["Webhook"]!["FutureNotification"]!.GetValue<bool>() && reopened.Load(out warning).TargetWave == 17);
            File.Delete(path);
            Check("Missing primary settings recover from backup", new SettingsStore(path).Load(out warning).TargetWave == recovered.TargetWave && warning is not null);
            File.Delete(path + ".bak");
            File.WriteAllText(path, "broken json");
            Check("Corrupt preferences recover with a visible warning", store.Load(out warning) == new RunnerSettings() && warning is not null);
            bool blockedOverwrite = false;
            try { store.Save(new()); } catch (InvalidDataException) { blockedOverwrite = true; }
            Check("Fallback defaults cannot overwrite unreadable saved preferences", blockedOverwrite && File.ReadAllText(path) == "broken json");
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
            rejected = false;
            try { store.Save(new(StartStopHotkey: "Escape")); }
            catch (InvalidDataException) { rejected = true; }
            Check("Unsupported global shortcut cannot be saved", rejected);
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
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            foreach (var preserved in Directory.GetFiles(output, Path.GetFileName(path) + ".unreadable-*")) File.Delete(preserved);
        }
        return report;
    }
}
