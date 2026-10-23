using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

public sealed partial class MainForm : Form
{
    private readonly Label status = new() { AutoSize = false, Height = 68, Dock = DockStyle.Top, Padding = new Padding(18), Text = "Ready · detection only" };
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(16, 21, 30) };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false };
    private OcrService? ocr;
    private BackgroundGameCapture? backgroundCapture;
    private readonly ScreenRecognizer recognizer = new();
    private readonly VisualRecognizer visualRecognizer = new();
    private readonly RunnerSession runner = new();
    private RunTracker tracker => runner.Tracker;
    private AutomationController controller => runner.Controller;
    private readonly LaunchSupervisor launcher = new();
    private bool launchSelectedAccountOnStart;
    private readonly CrashRecovery crashRecovery = new();
    private readonly ProcessExitObserver processExits = new();
    private bool pausedByException;
    private readonly ComboBox zone = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private bool manualThemeEnabled;
    private bool manualDarkTheme = true;
    private int targetWave = 30;
    private bool endRunWhenTargetReached = true;
    private bool waitForAssistance;
    private bool autoResumeFortnite;
    private int autoResumeIdleSeconds;
    private readonly NumericUpDown runLimit = new() { Minimum = 1, Maximum = 100, Value = 1, Width = 60 };
    private readonly CheckBox indefinite = new() { Text = "Indefinite", AutoSize = true };
    private SessionProgress runSession => runner.Progress;
    private bool runnerMode => runner.IsRunning && runner.Mode == RunnerMode.Runner;
    private readonly SettingsStore settingsStore;
    private readonly CheckBox launchIfMissing = new() { Text = "Launch / relaunch Fortnite", AutoSize = true, Checked = true };
    private readonly Label progress = new() { Dock = DockStyle.Top, Height = 68, Padding = new Padding(18, 8, 18, 8), Text = "No session started" };
    private readonly Label nextAction = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(18, 5, 18, 5), Text = "Ready · choose your base, then Start runner" };
    private readonly List<Button> startButtons = [];
    private readonly ListBox activity = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, BackColor = Color.FromArgb(28, 35, 47), ForeColor = Color.White };
    private DateTimeOffset? stoppedAt;
    private readonly EventLog log = new();
    private readonly CancellationTokenSource closing = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    private bool busy;
    private bool stopHotkeyRegistered;
    private string startStopHotkey = RunnerHotkey.Default;
    private Button? stopButton;
    private bool closed;
    private int session;
    private ScreenKind lastScreen;
    private string lastControllerDetail = "";
    private DateTimeOffset nextDiagnostics;
    private DateTimeOffset lastInputAt;
    private Guid? clearedPointerFor;
    private const int StopHotkeyId = 0x4550;

    public MainForm() : this(new SettingsStore()) { }

    internal MainForm(SettingsStore store)
    {
        settingsStore = store;
        Text = "Endurance Pilot " + typeof(MainForm).Assembly.GetName().Version?.ToString(3) + " · Early Access";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Size = new Size(1200, 800);
        MinimumSize = new Size(1040, 720);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(22, 28, 39);
        ForeColor = Color.FromArgb(230, 236, 243);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        AddButton(buttons, "Inspect image…", async () =>
        {
            Stop("Offline image inspection · input disabled");
            using var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                using var image = new Bitmap(dialog.FileName);
                await Analyze(image);
            }
        });
        startButtons.Add(AddButton(buttons, "Observe only", () => { Start(recover: false); return Task.CompletedTask; }));
        startButtons.Add(AddButton(buttons, "Errors & rewards only", () => { Start(recover: true); return Task.CompletedTask; }));
        stopButton = AddButton(buttons, "Stop · F10", () => { Stop("Stopped by user"); return Task.CompletedTask; });
        AddButton(buttons, "Open logs", () =>
        {
            Directory.CreateDirectory(log.DirectoryPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(log.DirectoryPath) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        var settings = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 4, 12, 4) };
        zone.Items.AddRange(Enum.GetValues<HomebaseZone>().Where(z => z != HomebaseZone.Unspecified).Cast<object>().ToArray());
        var saved = settingsStore.Load(out var settingsWarning);
        SettingsWarning = settingsWarning;
        startStopHotkey = saved.StartStopHotkey;
        stopButton.Text = "Stop · " + startStopHotkey;
        webhook = saved.Webhook ?? new();
        notifier.Configure(webhook);
        notifications = new RunNotifications(Notify, NotifyRewards);
        notifications.ConfigureWaves(webhook);
        zone.SelectedItem = saved.Zone;
        runLimit.Value = saved.AttemptLimit;
        indefinite.Checked = saved.RunIndefinitely;
        runLimit.Enabled = !indefinite.Checked;
        indefinite.CheckedChanged += (_, _) => runLimit.Enabled = !runner.IsRunning && !indefinite.Checked;
        manualThemeEnabled = saved.ManualThemeEnabled;
        manualDarkTheme = saved.ManualDarkTheme;
        targetWave = saved.TargetWave;
        endRunWhenTargetReached = saved.EndRunWhenTargetReached;
        waitForAssistance = saved.WaitForAssistance;
        autoResumeFortnite = saved.AutoResumeFortnite;
        autoResumeIdleSeconds = saved.AutoResumeIdleSeconds;
        launchIfMissing.Checked = saved.LaunchIfMissing;
        settings.Controls.Add(zone);
        settings.Controls.Add(new Label { Text = "Attempt limit", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
        settings.Controls.Add(runLimit);
        settings.Controls.Add(indefinite);
        settings.Controls.Add(launchIfMissing);
        startButtons.Add(AddButton(settings, "Start runner", () => { Start(recover: true, navigate: true); return Task.CompletedTask; }));
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(preview);
        details.BackColor = Color.FromArgb(28, 35, 47);
        details.ForeColor = ForeColor;
        details.BorderStyle = BorderStyle.None;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var activityTab = new TabPage("Activity");
        activityTab.Controls.Add(activity);
        tabs.TabPages.Add(activityTab);
        var evidenceTab = new TabPage("Recognition details");
        evidenceTab.Controls.Add(details);
        tabs.TabPages.Add(evidenceTab);
        split.Panel2.Controls.Add(tabs);
        Controls.Add(split);
        Controls.Add(nextAction);
        Controls.Add(progress);
        Controls.Add(status);
        Controls.Add(settings);
        Controls.Add(buttons);
        Shown += (_, _) =>
        {
            split.SplitterDistance = Math.Max(200, split.Height * 2 / 3);

        };
        if (settingsWarning is not null)
        {
            AddActivity(settingsWarning);
        }

        timer.Tick += async (_, _) =>
        {
            if (busy)
            {
                return;
            }

            var cycleClock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (runnerMode)
                {
                    var processes = EpicLauncher.Inspect();
                    bool watchCrash = launchIfMissing.Checked || (webhook.Enabled && webhook.Events.HasFlag(WebhookEvent.Crash));
                    if (watchCrash && (controller.Status != ControllerStatus.PausedForAttention || pausedByException))
                    {
                        int checkingSession = session;
                        int previousExits = crashRecovery.ConfirmedExits;
                        IReadOnlyList<CrashDialogEvidence> dialogs;
                        busy = true;
                        try
                        {
                            dialogs = await Task.Run(NativeCrashDialogs.Find, closing.Token);
                        }
                        finally { busy = false; }
                        if (closed || checkingSession != session)
                        {
                            return;
                        }

                        processes = EpicLauncher.Inspect();

                        if (processes.GamePresent && dialogs.Any(CrashRecovery.IsKnownCrash))
                        {
                            notifications.Crash();
                        }

                        var dismiss = crashRecovery.Observe(new(processes.CapturedAt, processes.GamePresent, processes.BootstrapperPresent, dialogs));
                        if (dismiss is not null)
                        {
                            try
                            {
                                NativeCrashDialogs.Dismiss(dismiss, () => !closed && checkingSession == session && runnerMode);
                                crashRecovery.Dispatched(dismiss, DateTimeOffset.UtcNow);
                                notifications.Crash();
                                controller.Stop("Waiting for crashed Fortnite to exit");
                                log.Write("crash-dialog-dismissed", new
                                {
                                    dismiss.Dialog.Title,
                                    dismiss.Dialog.ProcessId
                                });
                            }
                            catch (InvalidOperationException error)
                            {
                                crashRecovery.Rejected(dismiss, DateTimeOffset.UtcNow, error.Message);
                                log.Write("crash-dismissal-rejected", new
                                {
                                    error.Message
                                });
                            }
                            AddActivity(crashRecovery.Detail);
                        }
                        if (crashRecovery.Status == CrashRecoveryStatus.NeedsAttention)
                        {
                            controller.Pause(crashRecovery.Detail);
                            launcher.Pause(crashRecovery.Detail);
                            pausedByException = false;
                            status.Text = nextAction.Text = crashRecovery.Detail;
                            log.Write("crash-recovery-paused", new
                            {
                                crashRecovery.Detail
                            });
                            AddActivity(crashRecovery.Detail);
                            return;
                        }
                        if (crashRecovery.Status is CrashRecoveryStatus.Confirming or CrashRecoveryStatus.AwaitingExit)
                        {
                            status.Text = nextAction.Text = crashRecovery.Detail;
                            return;
                        }
                        if (crashRecovery.ConfirmedExits > previousExits)
                        {
                            notifications.Crash();
                            var crashOutcome = runner.RecoverAfterCrash(processes.CapturedAt);
                            if (crashOutcome is not null)
                            {
                                RecordOutcome(crashOutcome);
                            }

                            if (runSession.Phase == SessionPhase.Complete)
                            {
                                StopAfterSession();
                                return;
                            }
                            launcher.RecoverAfterCrash();
                            pausedByException = false;
                            processExits.Reset();
                            log.Write("crash-exit-confirmed", new
                            {
                                crashRecovery.ConfirmedExits
                            });
                            AddActivity(crashRecovery.Detail);
                        }
                    }
                    if (processExits.Observe(processes))
                    {
                        notifications.Crash();
                        bool resume = controller.Status != ControllerStatus.PausedForAttention || pausedByException;
                        var interrupted = runner.RecoverAfterProcessExit(processes.CapturedAt, resumeNavigation: resume);
                        if (interrupted is not null)
                        {
                            RecordOutcome(interrupted);
                        }

                        if (runSession.Phase == SessionPhase.Complete)
                        {
                            StopAfterSession();
                            return;
                        }
                        if (resume)
                        {
                            launcher.RecoverAfterCrash();
                            pausedByException = false;
                        }
                        log.Write("game-exit-confirmed", new
                        {
                            ResumeNavigation = resume
                        });
                        AddActivity(resume ? "Game exit confirmed · reacquiring the route after relaunch" : "Game exit confirmed · attention pause retained");
                    }
                    if (controller.Status == ControllerStatus.PausedForAttention)
                    {
                        launcher.Pause(controller.Detail);
                        status.Text = controller.Detail;
                        return;
                    }
                    bool launchOnThisObservation = launchSelectedAccountOnStart;
                    launchSelectedAccountOnStart = false;
                    var request = launcher.Observe(processes, launchOnThisObservation);
                    if (request is not null && (launchIfMissing.Checked || launchOnThisObservation))
                    {
                        launcher.Dispatched(request, DateTimeOffset.UtcNow);
                        busy = true;
                        try { await EpicLauncher.LaunchAsync(closing.Token); }
                        finally { busy = false; }
                        notifications.Launched();
                        log.Write("game-launch", request);
                        AddActivity(EpicLauncher.Accounts.Selected is { } account
                            ? $"Launching Fortnite as {account.DisplayName}"
                            : "Launch requested through Epic");
                    }
                    if (launcher.Status == LaunchStatus.NeedsAttention)
                    {
                        controller.Pause(launcher.Detail);
                        status.Text = launcher.Detail;
                        return;
                    }
                }
                var game = GameWindow.Find();
                if (game is null)
                {
                    backgroundCapture?.Dispose();
                    backgroundCapture = null;
                    notifications.LoseObservation();
                    controller.InvalidateObservation();
                    timer.Interval = 250;
                    status.Text = game is null && runnerMode
                        ? launchIfMissing.Checked || launcher.Status == LaunchStatus.Starting
                            ? launcher.Detail : "Waiting for Fortnite · automatic relaunch is off"
                        : "Observation paused · select Fortnite to resume";
                    RefreshProgress();
                    return;
                }
                if (!game.IsForeground)
                {
                    if (!interactionPause.Active)
                    {
                        WaitForUserInput("Waiting for Fortnite focus.");
                    }

                    bool resumeUnattended = autoResumeFortnite && runner.IsRunning && runner.Mode != RunnerMode.Observe
                        && controller.Status is ControllerStatus.Observing or ControllerStatus.AwaitingTransition;
                    bool idle = InputReady(game, resumeUnattended);
                    controller.InvalidateObservation();
                    if (idle && resumeUnattended && game.Activate())
                    {
                        interactionPause.Begin();
                        InputPauseChanged?.Invoke();
                        timer.Interval = 250;
                        status.Text = "Focusing Fortnite · waiting for fresh screen evidence";
                        return;
                    }
                    timer.Interval = 250;
                    Bitmap? backgroundFrame = null;
                    int captureSession = session;
                    busy = true;
                    try
                    {
                        if (backgroundCapture?.Matches(game) != true)
                        {
                            backgroundCapture?.Dispose();
                            backgroundCapture = null;
                            backgroundCapture = new BackgroundGameCapture(game);
                        }
                        backgroundFrame = await backgroundCapture.CaptureAsync(closing.Token);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        backgroundCapture?.Dispose();
                        backgroundCapture = null;
                        status.Text = "Background capture unavailable · focus Fortnite to resume reading waves";
                        log.Write("background-capture-unavailable", new
                        {
                            Error = error.GetType().Name
                        });
                    }
                    finally { busy = false; }
                    using (backgroundFrame)
                    {
                        if (closed || captureSession != session)
                        {
                            return;
                        }

                        if (backgroundFrame is not null)
                        {
                            await Analyze(backgroundFrame, game, cycleClock.Elapsed.TotalMilliseconds, observationOnly: true);
                        }
                        else
                        {
                            notifications.LoseObservation();
                            status.Text = "Waiting for a fresh Fortnite frame · keep the game unminimized";
                            RefreshProgress();
                        }
                    }
                    return;
                }
                backgroundCapture?.Dispose();
                backgroundCapture = null;
                if (!InputReady(game))
                {
                    return;
                }

                using var frame = game.Capture();
                await Analyze(frame, game, cycleClock.Elapsed.TotalMilliseconds);
            }
            catch (UserInteractionException error)
            {
                WaitForUserInput(error.Message);
                log.Write("input-paused", new
                {
                    error.Message
                });
            }
            catch (GameObservationChangedException error)
            {
                controller.InvalidateObservation();
                timer.Interval = 250;
                status.Text = error.Message;
                log.Write("observation-invalidated", new
                {
                    error.Message
                });
            }
            catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!closed)
                {
                    PauseWithError(error);
                }
            }
            finally
            {
                if (notificationSession)
                {
                    notifications.Attention(controller.Status == ControllerStatus.PausedForAttention, tracker.Current?.HighestWave);
                }
            }
        };
        FormClosed += (_, _) =>
        {
            closed = true;
            session++;
            runner.Stop("App closed");
            launcher.Stop();
            crashRecovery.Stop();
            closing.Cancel();
            if (stopHotkeyRegistered)
            {
                UnregisterHotKey(Handle, StopHotkeyId);
            }

            timer.Dispose();
            preview.Image?.Dispose();
        };
    }

    private void Start(bool recover, bool navigate = false)
    {
        if (recover && !stopHotkeyRegistered)
        {
            status.Text = $"Recovery requires the global {startStopHotkey} shortcut. Choose another shortcut in Settings if it is unavailable.";
            return;
        }
        var game = GameWindow.Find();
        if (game is null && !navigate)
        {
            status.Text = "Fortnite window unavailable. Open and restore Fortnite first.";
            return;
        }
        var checks = StartupChecks.Run();
        foreach (var check in checks)
        {
            AddActivity($"{check.Name}: {check.Detail}");
        }

        if (checks.Any(c => !c.Passed))
        {
            status.Text = "Startup check failed · see Activity for the fix";
            return;
        }
        ocr ??= new OcrService();
        var chosen = ReadSettings();
        chosen.Validate();
        settingsStore.Save(chosen);
        session++;
        launcher.Stop();
        launchSelectedAccountOnStart = false;
        nextDiagnostics = default;
        lastInputAt = default;
        clearedPointerFor = null;
        crashRecovery.Stop();
        pausedByException = false;
        interactionPause.Clear();
        processExits.Reset();
        runner.Start(navigate ? RunnerMode.Runner : recover ? RunnerMode.Recovery : RunnerMode.Observe,
            new(chosen.Zone, true, chosen.TargetWave, chosen.EndRunWhenTargetReached, chosen.WaitForAssistance), chosen.RunIndefinitely ? null : chosen.AttemptLimit, DateTimeOffset.UtcNow);
        stoppedAt = null;
        Earnings.StartSession();
        notificationSession = navigate;
        notifications.Reset(controller.ConfirmedDismissals + controller.ConfirmedAbandons);
        Notify(WebhookEvent.RunnerStarted, "Runner started", "Preparing to run endurance.");
        log.Write("monitor-start", new
        {
            RecoverErrors = recover,
            ClaimRewards = recover,
            Navigate = navigate,
            controller.Profile,
            RunLimit = runSession.AttemptLimit,
            Bounds = game?.ClientBounds
        });
        launchSelectedAccountOnStart = navigate && EpicLauncher.Accounts.Selected is not null;
        if (navigate && (chosen.LaunchIfMissing || launchSelectedAccountOnStart))
        {
            launcher.Start();
            if (chosen.LaunchIfMissing) crashRecovery.Start();
        }
        SetControlsRunning(true);
        AddActivity($"{runner.Mode} started · {chosen.Zone} · {(chosen.RunIndefinitely ? "indefinite" : $"limit {chosen.AttemptLimit}")}");
        RefreshProgress();
        if (game is not null && !game.Activate())
        {
            status.Text = "Select Fortnite to allow capture.";
        }

        timer.Interval = NavigationTiming.MenuPollMilliseconds;
        timer.Start();
    }

    private void Stop(string reason)
    {
        interactionPause.Clear();
        Notify(WebhookEvent.RunnerStopped, "Runner stopped", reason + " Stopping the runner does not itself leave an active Fortnite mission.", tracker.Current?.HighestWave);
        notificationSession = false;
        session++;
        timer.Stop();
        runner.Stop(reason);
        launcher.Stop();
        launchSelectedAccountOnStart = false;
        crashRecovery.Stop();
        stoppedAt ??= DateTimeOffset.UtcNow;
        SetControlsRunning(false);
        AddActivity(reason);
        RefreshProgress();
        status.Text = reason;
    }

    private Button AddButton(FlowLayoutPanel panel, string text, Func<Task> action)
    {
        var button = new Button { Text = text, UseMnemonic = false, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, Margin = new Padding(4), Padding = new Padding(10, 2, 10, 2) };
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!closed)
                {
                    PauseWithError(error);
                }
            }
        };
        panel.Controls.Add(button);
        return button;
    }

    private async Task Analyze(Bitmap image, GameWindow? game = null, double preRecognitionMilliseconds = 0, bool observationOnly = false)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        int capturedSession = session;
        try
        {
            status.Text = "Reading text at overview and native resolutions…";
            ocr ??= new OcrService();
            var recognitionClock = System.Diagnostics.Stopwatch.StartNew();
            var observation = await ocr.ReadAsync(image, closing.Token, requireExitControls: controller.TargetExitRequested);
            double ocrMilliseconds = recognitionClock.Elapsed.TotalMilliseconds;
            if (closed || capturedSession != session)
            {
                return;
            }

            var evidence = recognizer.Recognize(observation);
            bool requireHomebaseIcon = !controller.HomebaseClickConfirmed && controller.Pending?.Step != NavigationStep.SelectHomebase;
            observation = await Task.Run(() => visualRecognizer.Enrich(image, observation, evidence.Kind, requireHomebaseIcon), closing.Token);
            if (closed || capturedSession != session)
            {
                return;
            }

            evidence = recognizer.Recognize(observation);
            observationOnly = game is not null && (observationOnly || !game.IsForeground);
            double recognitionMilliseconds = recognitionClock.Elapsed.TotalMilliseconds;
            bool refreshDiagnostics = game is null || evidence.Kind != lastScreen || DateTimeOffset.UtcNow >= nextDiagnostics;
            if (refreshDiagnostics)
            {
                nextDiagnostics = DateTimeOffset.UtcNow.AddMilliseconds(evidence.Kind == ScreenKind.Rewards ? 2000 : 350);
            }

            bool rewardTransition = Rewards.Visible && !Rewards.IsComplete && DateTimeOffset.UtcNow - Rewards.LastSeenAt < TimeSpan.FromSeconds(3)
                            && evidence.Kind is ScreenKind.Unknown or ScreenKind.Loading;
            bool recentRewards = !runner.Rewards.IsComplete && runner.Rewards.Visible
                && observation.CapturedAt - runner.Rewards.LastSeenAt < TimeSpan.FromSeconds(20);
            if (game is not null)
            {
                timer.Interval = evidence.Kind is ScreenKind.Rewards or ScreenKind.RewardClaim || rewardTransition || recentRewards ? 50
                : observationOnly ? NavigationTiming.GameplayPollMilliseconds : controller.AwaitingQuestMenu ? 250 : NavigationTiming.PollMilliseconds(evidence.Kind, controller.Pending?.Step);
            }

            if (game is not null)
            {
                if (!observationOnly && controller.Pending is { } triangle
                    && (evidence.Kind == ScreenKind.MissionMap && triangle.Step == NavigationStep.SelectHomebase
                        || evidence.Kind == ScreenKind.WorldMap && triangle.Step == NavigationStep.ChangeZone
                        || evidence.Kind == ScreenKind.EnduranceSetup && triangle.Step == NavigationStep.DisableAssistance)
                    && clearedPointerFor != triangle.Id)
                {

                    var destination = NavigationPlanner.PostClickPointerDestination(observation, triangle);
                    if (GameInput.ClearClickedControl(game, triangle, destination, () => !closed && capturedSession == session))
                    {
                        clearedPointerFor = triangle.Id;
                        log.Write("pointer-cleared", new
                        {
                            triangle.Id,
                            Moved = true,
                            Destination = destination
                        });
                        return;
                    }
                }
                if (refreshDiagnostics)
                {
                    log.SaveObservation(observation, evidence);
                }

                if (controller.AwaitingQuestMenu || evidence.Kind is ScreenKind.Unknown or ScreenKind.SaveTheWorldHub)
                {
                    log.Write("quest-entry-observation", new
                    {
                        Screen = evidence.Kind,
                        HealthBars = observation.Visuals?.Count(v => v.Id == "player-health-bar") ?? 0,
                        RecognitionMilliseconds = recognitionMilliseconds,
                        ObservationAgeMilliseconds = (DateTimeOffset.UtcNow - observation.CapturedAt).TotalMilliseconds,
                        controller.AwaitingQuestMenu,
                        controller.RunInProgress,
                        controller.Status,
                        ObservationOnly = observationOnly,
                        PendingStep = controller.Pending?.Step
                    });
                }

                notificationFrame = observationOnly ? null : image;
                var previousRun = tracker.Current;
                var entryTimeouts = controller.QuestMenuTimeouts;
                var update = observationOnly ? runner.ObserveBackground(observation, evidence) : runner.Observe(observation, evidence);
                rewardThumbnails.Observe(runner.Rewards.ReceiptId, observation, image);
                ReportRewards();
                if (notificationSession && controller.QuestMenuTimeouts > entryTimeouts)
                {
                    notifications.QuestMenuTimedOut(tracker.Current?.HighestWave);
                }

                if (notificationSession)
                {
                    notifications.Observe(tracker.Current, controller.Profile.TargetWave, !observationOnly && controller.TargetExitRequested,
                    evidence.Kind, observation.CapturedAt, controller.ConfirmedDismissals + controller.ConfirmedAbandons, controller.AwaitingQuestMenu, background: observationOnly);
                }

                if (notificationSession && evidence.Kind == ScreenKind.NeedsAttention && controller.Status == ControllerStatus.PausedForAttention)
                {
                    notifications.Error(tracker.Current?.HighestWave);
                }
                if (notificationSession && !observationOnly)
                {
                    notifications.Attention(controller.Status == ControllerStatus.PausedForAttention,
                        tracker.Current?.HighestWave, controller.Detail);
                }

                if (tracker.Current != previousRun && tracker.Current is not null)
                {
                    log.Write("run-progress", tracker.Current);
                    if (tracker.Current.Outcome == RunOutcome.InProgress)
                    {
                        AddActivity("Confirmed wave " + tracker.Current.HighestWave);
                    }
                }
                if (update.Outcome is not null)
                {
                    RecordOutcome(update.Outcome);
                }

                if (update.Complete)
                {
                    StopAfterSession();
                    return;
                }
                if (evidence.Kind != lastScreen)
                {
                    log.Write("screen", new
                    {
                        evidence.Kind,
                        evidence.Reasons,
                        observation.Width,
                        observation.Height
                    });
                    lastScreen = evidence.Kind;
                    AddActivity("Screen: " + evidence.Kind);
                }
                var action = update.Action;
                if (!observationOnly && action is not null)
                {
                    log.Write("navigation-timing", new
                    {
                        action.Id,
                        action.Step,
                        OcrMilliseconds = ocrMilliseconds,
                        VisualAndClassificationMilliseconds = recognitionMilliseconds - ocrMilliseconds,
                        TotalRecognitionMilliseconds = recognitionMilliseconds,
                        PollMilliseconds = timer.Interval,
                        ProcessRecoveryAndCaptureMilliseconds = preRecognitionMilliseconds
                    });
                    log.Write("input-planned", action);
                    try
                    {
                        var inputClock = System.Diagnostics.Stopwatch.StartNew();
                        if (!await GameInput.DispatchAsync(game, image, action, ocr!,
                            () => !closed && capturedSession == session, closing.Token))
                        {
                            return;
                        }

                        var sentAt = DateTimeOffset.UtcNow;
                        controller.Dispatched(action, sentAt);
                        runner.Rewards.Dispatched(action);
                        if (notificationSession && action.SourceScreen is ScreenKind.ErrorDialog or ScreenKind.NeedsAttention)
                        {
                            notifications.Error(tracker.Current?.HighestWave);
                        }

                        log.Write("input-dispatched", new
                        {
                            action.Id,
                            action.Key,
                            action.Step,
                            InputMilliseconds = inputClock.Elapsed.TotalMilliseconds,
                            ObservationAgeMilliseconds = (sentAt - action.ObservedAt).TotalMilliseconds,
                            MillisecondsSincePreviousInput = lastInputAt == default ? (double?)null : (sentAt - lastInputAt).TotalMilliseconds
                        });
                        lastInputAt = sentAt;
                        AddActivity(action.Description);
                        if (action.Step is NavigationStep.SelectHomebase or NavigationStep.ChangeZone or NavigationStep.DisableAssistance)
                        {

                            var destination = NavigationPlanner.PostClickPointerDestination(observation, action);
                            bool moved = GameInput.ClearClickedControl(game, action, destination,
                                () => !closed && capturedSession == session);
                            if (moved)
                            {
                                clearedPointerFor = action.Id;
                            }

                            log.Write("pointer-cleared", new
                            {
                                action.Id,
                                Moved = moved,
                                Destination = destination
                            });
                        }
                    }
                    catch (TargetChangedException)
                    {
                        controller.TargetChanged(action, DateTimeOffset.UtcNow);
                        log.Write("input-rejected", new
                        {
                            action.Id,
                            Reason = "Target changed; no click or keypress sent"
                        });
                        AddActivity(controller.Detail);
                    }
                }
                if (controller.Detail != lastControllerDetail)
                {
                    log.Write("controller", new
                    {
                        controller.Status,
                        controller.Detail,
                        controller.ConfirmedDismissals,
                        controller.ConfirmedRewardClaims,
                        controller.ConfirmedAbandons,
                        controller.ConfirmedWakes
                    });
                    lastControllerDetail = controller.Detail;
                    nextAction.Text = controller.Detail;
                }
            }
            if (refreshDiagnostics)
            {
                var previous = preview.Image;
                preview.Image = OcrService.Annotate(image, observation);
                previous?.Dispose();
            }
            string mode = game is null ? "Offline image · no input" : observationOnly ? "Background wave monitoring · focus Fortnite for actions · " + tracker.Detail
                : controller.Status == ControllerStatus.Stopped ? tracker.Detail : controller.Detail + " · " + tracker.Detail;
            string screenLabel = evidence.MatchScore is { } score
                ? $"{(evidence.AllowsInput ? evidence.Kind : "Probably " + evidence.LikelyKind)} · {score:P0} text match" : evidence.Kind.ToString();
            status.Text = $"{screenLabel} · {image.Width} × {image.Height} · {observation.Regions.Count} text regions\n{mode}";
            if (refreshDiagnostics)
            {
                details.Text = string.Join(Environment.NewLine, evidence.Reasons) + Environment.NewLine
                + string.Join(Environment.NewLine, evidence.TextMatches.Select(m => $"{m.Expected}: {m.Score:P0} · read '{m.Read}'"))
                + Environment.NewLine + observation.Text;
            }

            RefreshProgress();
        }
        finally { notificationFrame = null; busy = false; }
    }

    protected override void WndProc(ref Message message)
    {
        if (!shuttingDown && message.Msg == 0x0312 && message.WParam == StopHotkeyId)
        {

            if (RunnerToggleRequested is { } toggle)
            {
                toggle();
            }
            else
            {
                ToggleRunner();
            }
        }
        base.WndProc(ref message);
    }

    private void RecordOutcome(RunRecord outcome)
    {
        if (outcome.Outcome is RunOutcome.Completed30 or RunOutcome.ReachedTarget)
        {
            Earnings.RecordCompletedRun(outcome.Id);
        }
        if (notificationSession)
        {
            notifications.Outcome(outcome, controller.Profile.TargetWave);
        }

        log.Write("run-outcome", outcome);
        AddActivity($"Run ended: {outcome.Outcome} · highest confirmed wave {outcome.HighestWave}");
    }

    private void SetControlsRunning(bool running)
    {
        foreach (var button in startButtons)
        {
            button.Enabled = !running;
        }

        zone.Enabled = indefinite.Enabled = launchIfMissing.Enabled = !running;
        runLimit.Enabled = !running && !indefinite.Checked;
    }

    private void RefreshProgress()
    {
        var elapsed = runner.StartedAt is { } started ? (stoppedAt ?? DateTimeOffset.UtcNow) - started : TimeSpan.Zero;
        string wave = tracker.Current is { } current ? $"Wave {current.HighestWave}/30 · {current.Outcome}" : "Waiting for a wave";
        progress.Text = $"{wave}     Elapsed {elapsed:hh\\:mm\\:ss}     Attempts {runSession.AttemptsEnded}/{runSession.AttemptLimit?.ToString() ?? "Indefinite"}\n"
            + $"Completed {runSession.CompletedRuns}   Failed {runSession.FailedRuns}   Interrupted {runSession.InterruptedRuns}   Unverified {runSession.UnverifiedRuns}"
            + (runSession.Phase == SessionPhase.FinishingRewards ? "   · Finishing rewards" : "");
    }

    private void AddActivity(string message)
    {
        activity.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (activity.Items.Count > 250)
        {
            activity.Items.RemoveAt(0);
        }

        activity.TopIndex = activity.Items.Count - 1;
    }

    private void PauseWithError(Exception error)
    {
        if (!pausedByException)
        {
            Notify(WebhookEvent.Error, "Runner error", "An internal error paused the runner. Check Endurance Pilot for details.", tracker.Current?.HighestWave);
        }

        pausedByException = true;
        controller.Pause(error.Message);
        launcher.Pause(error.Message);
        status.Text = error.Message;
        nextAction.Text = "Paused · correct the issue, then Stop and Start a new session";
        AddActivity("Paused: " + error.Message);
        log.Write("error", new
        {
            error.Message,
            Type = error.GetType().Name
        });
    }

    private void StopAfterSession() => Stop($"Session attempt limit reached ({runSession.AttemptsEnded}/{runSession.AttemptLimit}). Last outcome: {runSession.LastOutcome?.Outcome}");

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
