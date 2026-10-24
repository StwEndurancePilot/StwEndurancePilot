using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed record PilotSnapshot(bool Running, bool Attention, string Headline, string Detail, int? Wave,
    int Completed, string Activity);

public sealed partial class MainForm
{
    internal string? SettingsWarning { get; }
    internal MissionRewardTracker Rewards => runner.Rewards;
    internal RewardEarnings Earnings { get; } = new();
    private string lastRewardReceipt = "";
    private void ReportRewards()
    {
        Earnings.Record(Rewards.ReceiptId, Rewards.Items, Rewards.IsComplete);
        var receipt = string.Join(" · ", Rewards.Items.Select(r => $"{r.Name} {r.QuantityLabel}"));
        if (receipt != lastRewardReceipt)
        {
            lastRewardReceipt = receipt;
            if (receipt.Length > 0)
            {
                AddActivity("Mission rewards: " + receipt);
                log.Write("mission-rewards", new
                {
                    Rewards.Items,
                    Rewards.Status
                });
            }
        }
        if (notificationSession)
        {
            notifications.Rewards(Rewards);
        }
    }
    private bool shuttingDown;
    internal event Action? RunnerToggleRequested;
    internal void InitializeBridge()
    {
        _ = Handle;
        var (modifiers, key) = RunnerHotkey.Resolve(startStopHotkey);
        stopHotkeyRegistered = RegisterHotKey(Handle, StopHotkeyId, modifiers, key);
        status.Text = stopHotkeyRegistered ? "Keep Fortnite active and focused while running."
            : $"The {startStopHotkey} shortcut is unavailable. Choose another shortcut in Settings.";
        FormClosing += (_, e) => { if (!shuttingDown && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        AddActivity("Early access: full unattended wave-30 repeat is not yet verified. Tiny 25% HUD text is still a known gap.");
    }

    internal RunnerSettings ReadSettings() => new((HomebaseZone)zone.SelectedItem!, (int)runLimit.Value,
        launchIfMissing.Checked, indefinite.Checked, manualThemeEnabled, manualDarkTheme, targetWave, webhook,
        endRunWhenTargetReached, waitForAssistance, autoResumeFortnite, autoResumeIdleSeconds, startStopHotkey, closeToTray, startWithWindows, autoImportPenny);
    internal void SetLifecycle(bool closeToTray, bool startup)
    {
        bool previousStartup = startWithWindows;
        if (startup != previousStartup) WindowsStartup.SetEnabled(startup);
        try { settingsStore.Save(ReadSettings() with { CloseToTray = closeToTray, StartWithWindows = startup }); }
        catch { if (startup != previousStartup) WindowsStartup.SetEnabled(previousStartup); throw; }
        this.closeToTray = closeToTray; startWithWindows = startup;
    }
    internal void SetHotkey(string choice)
    {
        var next = ReadSettings() with { StartStopHotkey = choice };
        next.Validate();
        if (choice == startStopHotkey)
        {
            return;
        }

        string previous = startStopHotkey;
        bool hadPrevious = stopHotkeyRegistered;
        if (hadPrevious && !UnregisterHotKey(Handle, StopHotkeyId))
        {
            throw new InvalidOperationException($"Could not release {previous}; it remains selected.");
        }
        stopHotkeyRegistered = false;
        var (modifiers, key) = RunnerHotkey.Resolve(choice);
        if (!RegisterHotKey(Handle, StopHotkeyId, modifiers, key))
        {
            bool restored = RestoreHotkey(previous, hadPrevious);
            throw new InvalidOperationException(restored
                ? $"{choice} is unavailable. {previous} remains active."
                : $"{choice} is unavailable, and {previous} could not be restored. Choose another shortcut.");
        }

        stopHotkeyRegistered = true;
        try
        {
            settingsStore.Save(next);
        }
        catch
        {
            UnregisterHotKey(Handle, StopHotkeyId);
            _ = RestoreHotkey(previous, hadPrevious);
            throw;
        }

        startStopHotkey = choice;
        if (stopButton is not null) stopButton.Text = "Stop · " + choice;
        status.Text = "Start/stop shortcut: " + choice;
    }
    internal void SetAutoImportPenny(bool enabled)
    {
        settingsStore.Save(ReadSettings() with { AutoImportPenny = enabled });
        autoImportPenny = enabled;
    }
    private bool RestoreHotkey(string choice, bool wasRegistered)
    {
        if (!wasRegistered)
        {
            stopHotkeyRegistered = false;
            return false;
        }

        var (modifiers, key) = RunnerHotkey.Resolve(choice);
        stopHotkeyRegistered = RegisterHotKey(Handle, StopHotkeyId, modifiers, key);
        if (!stopHotkeyRegistered)
        {
            status.Text = $"Could not restore {choice}; choose another shortcut in Settings.";
        }
        return stopHotkeyRegistered;
    }
    internal void SetAutoResume(bool enabled, int idleSeconds)
    {
        var next = ReadSettings() with { AutoResumeFortnite = enabled, AutoResumeIdleSeconds = idleSeconds };
        next.Validate();
        settingsStore.Save(next);
        autoResumeFortnite = enabled;
        autoResumeIdleSeconds = idleSeconds;
    }
    internal void ApplySettings(RunnerSettings value)
    {
        if (runner.IsRunning)
        {
            throw new InvalidOperationException("Stop the runner before changing run options.");
        }

        value = value with
        {
            Webhook = value.Webhook ?? webhook,
            StartStopHotkey = startStopHotkey
        };
        value.Validate();
        settingsStore.Save(value);
        zone.SelectedItem = value.Zone;
        runLimit.Value = value.AttemptLimit;
        launchIfMissing.Checked = value.LaunchIfMissing;
        indefinite.Checked = value.RunIndefinitely;
        manualThemeEnabled = value.ManualThemeEnabled;
        manualDarkTheme = value.ManualDarkTheme;
        targetWave = value.TargetWave;
        endRunWhenTargetReached = value.EndRunWhenTargetReached;
        waitForAssistance = value.WaitForAssistance;
        closeToTray = value.CloseToTray;
        startWithWindows = value.StartWithWindows; autoImportPenny = value.AutoImportPenny;
        autoResumeFortnite = value.AutoResumeFortnite;
        autoResumeIdleSeconds = value.AutoResumeIdleSeconds;
        ConfigureWebhook(value.Webhook!);
    }
    internal void ApplyAppearance(bool manual, bool dark)
    {

        settingsStore.Save(ReadSettings() with
        {
            ManualThemeEnabled = manual,
            ManualDarkTheme = dark
        });
        manualThemeEnabled = manual;
        manualDarkTheme = dark;
    }
    internal void ToggleRunner()
    {
        try
        {
            if (runner.IsRunning)
            {
                Stop("Stopped by user. Fortnite remains open.");
            }
            else
            {
                Start(true, true);
            }
        }
        catch (Exception error) { PauseWithError(error); }
    }
    internal PilotSnapshot Snapshot()
    {
        bool attention = controller.Status == ControllerStatus.PausedForAttention || !stopHotkeyRegistered;
        string heading = attention ? "Needs attention" : runner.IsRunning ? runner.Mode == RunnerMode.Observe ? "Watching Fortnite" : "Runner active" : runner.StartedAt is null ? "" : "Session stopped";
        string detail = attention || !runner.IsRunning ? status.Text : runner.Mode == RunnerMode.Observe ? "Observing only. No game input is sent." : controller.Detail;
        if (InputPaused && !attention)
        {
            heading = "Paused for your input";
            detail = autoResumeFortnite
                ? $"Focusing Fortnite after {autoResumeIdleSeconds} seconds without keyboard or mouse input, or click Resume."
                : "Focus Fortnite and leave input idle for 2 seconds, or click Resume.";
        }
        if (runner.IsRunning && status.Text.StartsWith("Observation paused"))
        {
            detail = "Waiting for Fortnite. Bring the game to the foreground to continue.";
        }

        if (runner.IsRunning && status.Text.StartsWith("Waiting for Fortnite"))
        {
            detail = status.Text;
        }

        if (runner.IsRunning && launcher.Status == LaunchStatus.NeedsAttention)
        {
            detail = launcher.Detail;
        }

        var current = tracker.Current;
        int? activeWave = runner.IsRunning && current is { Outcome: RunOutcome.InProgress, EndedAt: null }
            ? current.HighestWave : null;
        return new(runner.IsRunning, attention, heading, detail, activeWave, runSession.CompletedRuns,
            string.Join(Environment.NewLine, activity.Items.Cast<string>().Reverse().Take(80)));
    }
    internal void ShowTechnicalDetails()
    {
        Text = "Endurance Pilot · Diagnostics";
        Show();
        Activate();
    }
    internal void ShutdownBridge()
    {
        shuttingDown = true;
        Stop("App closed");
        notifier.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Close();
        Dispose();
    }
}
