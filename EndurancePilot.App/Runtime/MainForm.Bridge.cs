using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed record PilotSnapshot(bool Running, bool Attention, string Headline, string Detail, int? Wave,
    string WaveCaption, TimeSpan Elapsed, int Completed, int AttemptsEnded, int? AttemptLimit, string Activity);

public sealed partial class MainForm
{
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
        stopHotkeyRegistered = RegisterHotKey(Handle, StopHotkeyId, 0x4000, 0x79);
        status.Text = stopHotkeyRegistered ? "Keep Fortnite active and focused while running."
            : "The start/stop shortcut is unavailable. Close other apps using F10 and reopen Endurance Pilot.";
        FormClosing += (_, e) => { if (!shuttingDown && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        AddActivity("Early access: full unattended wave-30 repeat is not yet verified. Tiny 25% HUD text is still a known gap.");
    }

    internal RunnerSettings ReadSettings() => new((HomebaseZone)zone.SelectedItem!, (int)runLimit.Value,
        launchIfMissing.Checked, indefinite.Checked, manualThemeEnabled, manualDarkTheme, targetWave, webhook,
        endRunWhenTargetReached, waitForAssistance, autoResumeFortnite, autoResumeIdleSeconds);
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
            Webhook = value.Webhook ?? webhook
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
                ? $"Fortnite resumes after {autoResumeIdleSeconds} seconds without keyboard or mouse input, or click Resume."
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
        string caption = current is null ? "" : current.Outcome switch
        {
            RunOutcome.InProgress => "Highest wave confirmed in this run.",
            RunOutcome.Completed30 => "Wave 30 and victory confirmed.",
            RunOutcome.ReachedTarget => "Target reached; mission exit confirmed.",
            RunOutcome.Failed => "Last run ended in defeat.",
            RunOutcome.Interrupted => "Last run was interrupted.",
            _ => "Victory detected. Wave 30 was not confirmed."
        };
        var elapsed = runner.StartedAt is { } started ? (stoppedAt ?? DateTimeOffset.UtcNow) - started : TimeSpan.Zero;
        return new(runner.IsRunning, attention, heading, detail, current?.HighestWave, caption, elapsed, runSession.CompletedRuns,
            runSession.AttemptsEnded, runner.StartedAt is null ? indefinite.Checked ? null : (int)runLimit.Value : runSession.AttemptLimit,
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
