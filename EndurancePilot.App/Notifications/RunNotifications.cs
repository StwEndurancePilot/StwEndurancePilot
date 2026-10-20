using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class RunNotifications(Action<WebhookEvent, string, string, int?> emit,
    Action<IReadOnlyList<MissionReward>, int, int>? emitRewards = null)
{
    private Guid? startedRun;
    private Guid? targetRun;
    private readonly HashSet<Guid> ended = [];
    private readonly HashSet<Guid> rewardReceipts = [];
    private bool attention;
    private bool crash;
    private ScreenKind lastScreen;
    private DateTimeOffset screenSince;
    private DateTimeOffset lastFrame;
    private DateTimeOffset? clearSince;
    private bool errorSent;
    private bool stuckSent;
    private int recoveries;
    private bool waveNotificationsEnabled;
    private int waveNotificationFrequency = 5;
    private Guid? waveRun;
    private int lastWave;

    internal void ConfigureWaves(WebhookSettings settings)
    {
        settings.Validate();
        waveNotificationsEnabled = settings.Enabled && settings.Events.HasFlag(WebhookEvent.WaveReached);
        waveNotificationFrequency = settings.WaveNotificationFrequency;
    }

    internal void Reset(int recoveryCount = 0)
    {
        startedRun = targetRun = null;
        ended.Clear();
        rewardReceipts.Clear();
        attention = crash = errorSent = stuckSent = false;
        waveRun = null;
        lastWave = 0;
        screenSince = lastFrame = default;
        clearSince = null;
        recoveries = recoveryCount;
        lastScreen = ScreenKind.Unknown;
    }
    internal void LoseObservation()
    {
        screenSince = lastFrame = default;
        clearSince = null;
    }
    internal void Rewards(MissionRewardTracker rewards)
    {
        if (!rewards.IsComplete || !rewardReceipts.Add(rewards.ReceiptId))
        {
            return;
        }

        if (emitRewards is not null && rewards.Items.Count > 0)
        {
            var pages = rewards.Items.Chunk(RewardReceiptArtwork.PageSize).ToArray();
            for (int i = 0; i < pages.Length; i++)
            {
                emitRewards(pages[i], i + 1, pages.Length);
            }

            return;
        }
        var lines = rewards.Items.Select(r => $"{r.Icon} {r.Name} — {r.QuantityLabel}").ToArray();
        var chunks = new List<string>();
        string text = "";
        foreach (var line in lines)
        {
            if (text.Length + line.Length > 3400)
            {
                chunks.Add(text);
                text = "";
            }
            text += line + "\n";
        }
        if (text.Length > 0)
        {
            chunks.Add(text);
        }

        if (chunks.Count == 0)
        {
            chunks.Add("Reward details could not be read.");
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            emit(WebhookEvent.EnduranceEnded, chunks.Count == 1 ? "Mission rewards received" : $"Mission rewards received ({i + 1}/{chunks.Count})",
                chunks[i] + "\nRead from the game screen. Unreadable or missed cards may be missing.", null);
        }
    }
    internal void Error(int? wave)
    {
        if (errorSent)
        {
            return;
        }

        errorSent = true;
        emit(WebhookEvent.Error, "Fortnite error detected", "Recovery will be attempted if supported. Otherwise, check the game.", wave);
    }
    internal void Crash()
    {
        if (crash)
        {
            return;
        }

        crash = true;
        emit(WebhookEvent.Crash, "Fortnite crashed or closed", "The game exited unexpectedly.", null);
    }
    internal void QuestMenuTimedOut(int? wave)
    {
        if (attention)
        {
            return;
        }

        attention = true;
        emit(WebhookEvent.Error, "Save the World menu did not open",
            $"The gameplay HUD remains visible after {QuestMenuEntry.MaximumAttempts} verified Tab attempts. The runner has paused; check Fortnite's menu key binding and restart the runner.", wave);
    }
    internal void Launched()
    {
        emit(WebhookEvent.Launch, crash ? "Restarting Fortnite" : "Launching Fortnite", "Waiting for Epic to open the game.", null);
        crash = false;
    }
    internal void Attention(bool paused, int? wave)
    {
        if (paused && !attention)
        {
            emit(WebhookEvent.Stuck, "Runner needs attention", "Unable to continue. Check Fortnite.", wave);
        }

        attention = paused;
    }
    internal void Observe(RunRecord? run, int target, bool targetExit, ScreenKind screen, DateTimeOffset now, int recoveryCount, bool entryWait = false, bool background = false)
    {
        if (run is { Outcome: RunOutcome.InProgress } && run.Id != startedRun)
        {
            startedRun = run.Id;
            emit(WebhookEvent.EnduranceStarted, run.HighestWave == 1 ? "Endurance started" : "Endurance detected",
                run.HighestWave == 1 ? "Wave 1 is confirmed active." : "Monitoring an endurance already in progress.", run.HighestWave);
        }
        if (run is not null && target < 30 && targetExit && run.HighestWave > target && targetRun != run.Id)
        {
            targetRun = run.Id;
            emit(WebhookEvent.TargetReached, "Target wave completed", $"Wave {target} completed. Leaving the mission.", run.HighestWave);
        }
        if (recoveryCount > recoveries)
        {
            emit(WebhookEvent.Recovery, "Recovery confirmed", "An error was dismissed or a disconnected session abandoned.", run?.HighestWave);
        }

        recoveries = recoveryCount;

        if (now <= lastFrame)
        {
            return;
        }

        if (run is { Outcome: RunOutcome.InProgress, HighestWave: >= 1 and <= 30 })
        {
            if (waveRun != run.Id)
            {
                waveRun = run.Id;
                lastWave = 0;
            }
            if (run.HighestWave > lastWave)
            {
                lastWave = run.HighestWave;
                if (waveNotificationsEnabled && lastWave % waveNotificationFrequency == 0)
                {
                    emit(WebhookEvent.WaveReached, $"Wave {lastWave} reached", "", lastWave);
                }
            }
        }
        if (screen != lastScreen || lastFrame == default || now - lastFrame > TimeSpan.FromSeconds(10))
        {
            screenSince = now;
            stuckSent = false;
        }
        lastFrame = now;
        lastScreen = screen;
        if (screen is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention)
        {
            clearSince = null;
            if (!errorSent && now - screenSince >= TimeSpan.FromSeconds(0.5))
            {
                Error(run?.HighestWave);
            }
        }
        else
        {
            clearSince ??= now;
            if (now - clearSince >= TimeSpan.FromSeconds(5))
            {
                errorSent = false;
            }
        }

        bool monitoringEndurance = run is { Outcome: RunOutcome.InProgress } && !targetExit;
        var threshold = screen is ScreenKind.Loading or ScreenKind.Matchmaking or ScreenKind.HomebasePreparing ? 300 : 90;
        if (background)
        {
            screenSince = now;
            stuckSent = false;
        }
        if (!background && !stuckSent && !attention && !entryWait && !monitoringEndurance && screen is not (ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown or ScreenKind.EnduranceStatus)
            && now - screenSince >= TimeSpan.FromSeconds(threshold))
        {
            stuckSent = true;
            emit(WebhookEvent.Stuck, "Possible stuck screen", $"No screen transition was detected for {threshold} seconds while Fortnite was focused. Check the game.", run?.HighestWave);
        }
    }
    internal void Outcome(RunRecord run, int target)
    {
        if (!ended.Add(run.Id))
        {
            return;
        }

        if (ended.Count > 128)
        {
            ended.Clear();
            ended.Add(run.Id);
        }
        if (run.Outcome == RunOutcome.Completed30)
        {
            if (targetRun != run.Id)
            {
                targetRun = run.Id;
                emit(WebhookEvent.TargetReached, "Target wave completed", "All 30 waves completed. Victory confirmed.", 30);
            }
            emit(WebhookEvent.Completed30, "All 30 waves completed", "All 30 waves completed. Victory confirmed.", 30);
        }
        var detail = run.Outcome switch
        {
            RunOutcome.Completed30 => "Victory after all 30 waves.",
            RunOutcome.ReachedTarget => $"Target wave {target} finished and mission exit confirmed.",
            RunOutcome.Failed => "The mission ended in defeat.",
            RunOutcome.UnverifiedVictory => "Victory detected, but completion of all 30 waves could not be verified.",
            _ => "Run interrupted before a confirmed finish."
        };
        emit(WebhookEvent.EnduranceEnded, "Endurance ended", detail, run.HighestWave);
    }
}

