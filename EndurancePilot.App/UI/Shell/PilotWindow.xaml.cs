using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using EndurancePilot.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace EndurancePilot.App;

public partial class PilotWindow : FluentWindow
{
    private readonly MainForm host;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Window? activityWindow;
    private SettingsPane? settingsPane;
    private AccountsPane? accountsPane;
    private bool settingsTransition;
    private System.Windows.Controls.TextBox? activityText;
    private ApplicationTheme theme;
    private bool loaded;
    private bool watchingSystemTheme;
    private readonly Windows.UI.ViewManagement.UISettings windowsColours = new();
    private string? settingsError;
    private string? rewardLayout;
    private bool accountPopupWasOpenOnPress;

    public PilotWindow()
    {
        InitializeComponent();
        Height = Math.Min(690, SystemParameters.WorkArea.Height - 32);
        host = new MainForm();
        settingsError = host.SettingsWarning;
        host.RunnerToggleRequested += ToggleFromControls;
        host.InputPauseChanged += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (loaded) RefreshState();
        }));
        host.InitializeBridge();
        PennyAccountImport.ImportOnStartup(EpicLauncher.Accounts, host.ReadSettings().AutoImportPenny);
        InitializeRemote();
        InitializeTray();
        ApplyThemePreference();
        var settings = host.ReadSettings();
        ZoneChoice.ItemsSource = Enum.GetValues<HomebaseZone>().Where(z => z != HomebaseZone.Unspecified)
            .Select(z => new { Zone = z, Label = z switch { HomebaseZone.TwinePeaks => "Twine Peaks", HomebaseZone.CannyValley => "Canny Valley", _ => z.ToString() } }).ToArray();
        ZoneChoice.SelectedValue = settings.Zone;
        AttemptInput.Value = settings.AttemptLimit;
        RefreshAccountPicker();
        for (int i = 0; i < 30; i++)
        {
            WaveSegments.Children.Add(new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, i == 29 ? 0 : 4, 0) });
        }

        Loaded += (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--tray")) Hide();
            loaded = true;
            ApplicationThemeManager.Changed += WindowsThemeChanged;
            windowsColours.ColorValuesChanged += WindowsColoursChanged;
            ApplyThemePreference();
            RefreshState();
            refresh.Start();
            _ = PollUpdatesAsync();
        };
        refresh.Tick += (_, _) => RefreshState();
        Closing += (_, e) =>
        {
            if (KeepInTray(e)) return;
            loaded = false;
            accountsPane?.CancelPending();
            updateLifetime.Cancel();
            remoteLogin?.Cancel();
            remote?.Dispose();
            windowsColours.ColorValuesChanged -= WindowsColoursChanged;
            ApplicationThemeManager.Changed -= WindowsThemeChanged;
            if (watchingSystemTheme)
            {
                SystemThemeWatcher.UnWatch(this);
            }
        };
        Closed += (_, _) => { refresh.Stop(); host.ShutdownBridge(); activityWindow?.Close(); };
        ZoneChoice.SelectionChanged += (_, _) => SaveOptions();
        AttemptInput.ValueChanged += (_, _) => SaveOptions();
    }

    private bool SaveOptions()
    {
        if (!loaded || host.Snapshot().Running)
        {
            return true;
        }

        try
        {
            bool indefinite = host.ReadSettings().RunIndefinitely;
            double count = indefinite ? host.ReadSettings().AttemptLimit : AttemptInput.Value ?? double.NaN;
            if (ZoneChoice.SelectedValue is not HomebaseZone zone || !double.IsFinite(count) || count != Math.Truncate(count))
            {
                throw new InvalidOperationException("Choose a homebase and a whole attempt limit from 1 to 100.");
            }

            host.ApplySettings(host.ReadSettings() with
            {
                Zone = zone,
                AttemptLimit = checked((int)count),
                RunIndefinitely = indefinite
            });
            if (indefinite)
            {
                AttemptInput.Value = count;
            }

            settingsError = null;
            return true;
        }
        catch (Exception error) { settingsError = error.Message; StatusDetail.Text = error.Message; return false; }
    }
    private void PrimaryClicked(object sender, RoutedEventArgs e) => ToggleFromControls();
    private void PauseClicked(object sender, RoutedEventArgs e)
    {
        host.ToggleInputPause();
        RefreshState();
    }
    private void ToggleFromControls()
    {
        if (!host.Snapshot().Running && !SaveOptions())
        {
            return;
        }

        host.ToggleRunner();
        RefreshState();
    }
    private void RefreshState()
    {
        var s = host.Snapshot();
        var earnings = host.Earnings;
        var items = EarningsView.SelectedIndex switch
        {
            1 => host.Rewards.Items,
            2 => earnings.Lifetime,
            _ => earnings.Session
        };
        string rewardTitle = EarningsView.SelectedIndex switch
        {
            1 => "Mission rewards",
            2 => "Lifetime earnings",
            _ => "Session earnings"
        };
        string rewardStatus = EarningsView.SelectedIndex == 1 ? host.Rewards.Status
            : earnings.Warning ?? (items.Count == 0 ? "No recorded rewards yet."
                : EarningsView.SelectedIndex == 2 ? ""
                : "Total quantity per reward across completed receipts this session.");
        long? completedRuns = EarningsView.SelectedIndex switch
        {
            1 => null,
            2 => earnings.LifetimeCompletedRuns,
            _ => s.Completed
        };
        MissionRewards.Update(true, items, rewardStatus, rewardTitle, completedRuns,
            EarningsView.SelectedIndex == 2 ? "Recorded on this device" : "This session");
        bool indefinite = host.ReadSettings().RunIndefinitely;
        AttemptGap.Width = new GridLength(indefinite ? 0 : 14);
        AttemptColumn.Width = new GridLength(indefinite ? 0 : 125);
        AttemptFields.Visibility = indefinite ? Visibility.Collapsed : Visibility.Visible;
        Headline.Text = s.Headline;
        HeadlineRow.Visibility = string.IsNullOrWhiteSpace(s.Headline) ? Visibility.Collapsed : Visibility.Visible;
        StatusDetail.Text = settingsError ?? s.Detail;
        StatusDetail.Visibility = string.IsNullOrWhiteSpace(StatusDetail.Text) ? Visibility.Collapsed : Visibility.Visible;
        StatusDetail.ToolTip = StatusDetail.Text;
        PrimaryButton.ToolTip = StatusDetail.Text;
        int? displayedWave = s.LiveWave?.Wave ?? s.Wave;
        WaveNumber.Text = (displayedWave ?? 0).ToString("00");
        WavePhaseLabel.Text = s.LiveWave is { Starting: true } ? "NEXT WAVE" : s.LiveWave is not null ? "ACTIVE WAVE" : "WAVE PROGRESS";
        WaveTimerLabel.Text = s.LiveWave is { Starting: true } ? "STARTS IN" : "WAVE REMAINING";
        WaveTimerText.Text = s.LiveWave is { } live ? $"{(int)live.Remaining.TotalMinutes}:{live.Remaining.Seconds:D2}" : "--:--";
        ElapsedText.Text = $"{(int)Math.Max(0, s.Elapsed.TotalHours):D2}:{s.Elapsed.Minutes:D2}:{s.Elapsed.Seconds:D2}";
        WaveFreshnessText.Text = s.LiveWave is { Remaining: var remaining } ? remaining == TimeSpan.Zero
            ? "Waiting for the game to confirm the next phase" : "Live game timer"
            : s.WaveAge is { } age ? $"Last confirmed wave {s.Wave} · {Math.Max(0, (int)age.TotalSeconds)}s ago · waiting for HUD"
            : "Waiting for a fresh game frame";
        int targetWave = host.ReadSettings().EndRunWhenTargetReached ? host.ReadSettings().TargetWave : 30;
        WaveTarget.Text = $"/ {targetWave}";
        if (WaveSegments.Children.Count != targetWave)
        {
            WaveSegments.Children.Clear();
            for (int i = 0; i < targetWave; i++)
            {
                WaveSegments.Children.Add(new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, i == targetWave - 1 ? 0 : 4, 0) });
            }
        }
        System.Windows.Automation.AutomationProperties.SetName(WaveNumber, displayedWave is null ? "No wave detected"
            : $"{(s.LiveWave is { Starting: true } ? "Upcoming" : "Observed")} wave {displayedWave}; target {targetWave}");
        System.Windows.Automation.AutomationProperties.SetName(WaveTimerText, $"{WaveTimerLabel.Text}: {WaveTimerText.Text}");
        System.Windows.Automation.AutomationProperties.SetName(ElapsedText, "Session elapsed " + ElapsedText.Text);
        PrimaryText.Text = s.Running ? "Stop runner" : "Start endurance";
        PauseButton.Content = s.Paused ? "Resume" : "Pause";
        PauseButton.Visibility = s.Running && !s.Attention ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, s.Paused ? "Resume game controls" : "Pause game controls");
        string hotkey = host.ReadSettings().StartStopHotkey;
        ShortcutText.Text = s.Running ? $"{hotkey} to stop" : $"{hotkey} to start";
        PrimaryIcon.Symbol = s.Running ? SymbolRegular.Stop24 : SymbolRegular.Play24;
        ApplyPrimaryButtonPalette(s.Running);
        System.Windows.Automation.AutomationProperties.SetName(PrimaryButton, PrimaryText.Text);
        SetupFields.IsEnabled = !s.Running;
        AttemptInput.IsEnabled = !s.Running && !indefinite;
        settingsPane?.SetRunning(s.Running);
        settingsPane?.SetWebhookStatus(host.WebhookStatus);
        for (int i = 0; i < WaveSegments.Children.Count; i++)
        {
            ((Border)WaveSegments.Children[i]).SetResourceReference(Border.BackgroundProperty, i < (s.Wave ?? 0) ? "PilotAccent" : "PilotTrack");
        }

        if (activityText is not null && activityText.Text != s.Activity)
        {
            activityText.Text = s.Activity;
        }

        var layout = $"{EarningsView.SelectedIndex}:{items.Count}:{PauseButton.Visibility}:{StatusDetail.Text}";
        if (rewardLayout != layout && settingsPane is null && accountsPane is null)
        {
            rewardLayout = layout;
            Dispatcher.BeginInvoke(new Action(FitRewards), DispatcherPriority.Loaded);
        }
    }
    private void EarningsViewChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (loaded)
        {
            RefreshState();
        }
    }
    private void FitRewards()
    {
        if (!loaded || settingsPane is not null || accountsPane is not null || WindowState != WindowState.Normal)
        {
            return;
        }

        UpdateLayout();
        var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var work = screen.WorkingArea;
        var area = Rect.Transform(new Rect(work.X, work.Y, work.Width, work.Height), transform);
        double available = Math.Max(200, area.Height - 24);
        MinHeight = Math.Min(640, available);
        Height = Math.Clamp(DashboardContent.DesiredSize.Height + RunControls.ActualHeight + 34 + UpdateNotice.ActualHeight + 16,
            MinHeight, available);
        Top = Math.Clamp(Top, area.Top + 12, Math.Max(area.Top + 12, area.Bottom - Height - 12));
    }
    private void WindowsColoursChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {

        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => { if (loaded) { ApplyPalette(); } }));
    }
    private void WindowsThemeChanged(ApplicationTheme currentTheme, Color accent)
    {
        theme = currentTheme;
        ApplyPalette();
    }
    private void ApplyThemePreference()
    {
        var settings = host.ReadSettings();
        if (loaded && watchingSystemTheme && settings.ManualThemeEnabled)
        {
            SystemThemeWatcher.UnWatch(this);
            watchingSystemTheme = false;
        }
        else if (loaded && !watchingSystemTheme && !settings.ManualThemeEnabled)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.None, false);
            watchingSystemTheme = true;
        }
        if (settings.ManualThemeEnabled)
        {
            ApplicationThemeManager.Apply(settings.ManualDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, false);
        }
        else
        {
            ApplicationThemeManager.ApplySystemTheme(false);
        }

        theme = ApplicationThemeManager.GetAppTheme();
        ApplyPalette();
    }
    private void ThemeClicked(object sender, RoutedEventArgs e)
    {
        if (!host.ReadSettings().ManualThemeEnabled)
        {
            return;
        }

        try
        {
            host.ApplyAppearance(true, theme != ApplicationTheme.Dark);
            ApplyThemePreference();
        }
        catch (Exception error) { settingsError = error.Message; }
    }
    private void ApplyPalette()
    {
        bool dark = theme == ApplicationTheme.Dark;
        void BrushResource(string key, string darkHex, string lightHex) => System.Windows.Application.Current.Resources[key] = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? darkHex : lightHex));
        BrushResource("PilotBackground", "#121618", "#F5F5EF");
        BrushResource("PilotSurface", "#1C2326", "#EAEDE3");
        BrushResource("PilotBorder", "#343F42", "#D2D8CB");
        BrushResource("PilotText", "#F3F6ED", "#172221");
        BrushResource("PilotSecondary", "#B5BFBA", "#4D6057");
        BrushResource("PilotMuted", "#8F9E96", "#67776C");
        BrushResource("PilotTrack", "#36423D", "#CBD4C4");
        BrushResource("PilotStop", "#C43B42", "#B52E36");
        BrushResource("PilotStopHover", "#AF3038", "#A1262E");
        BrushResource("PilotStopPressed", "#962730", "#8B2028");
        BrushResource("PilotOnStop", "#FFFFFF", "#FFFFFF");
        BrushResource("PilotWarning", "#F4BF70", "#8B4B0B");
        var systemAccent = windowsColours.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        var accent = Color.FromRgb(systemAccent.R, systemAccent.G, systemAccent.B);
        static double Linear(byte channel) => channel / 255d <= .04045 ? channel / 255d / 12.92 : Math.Pow((channel / 255d + .055) / 1.055, 2.4);
        double luminance = .2126 * Linear(accent.R) + .7152 * Linear(accent.G) + .0722 * Linear(accent.B);
        var onAccent = luminance > .179 ? Colors.Black : Colors.White;
        var shade = onAccent == Colors.White ? Colors.Black : Colors.White;
        Color Mix(double amount) => Color.FromRgb((byte)(accent.R + (shade.R - accent.R) * amount),
            (byte)(accent.G + (shade.G - accent.G) * amount), (byte)(accent.B + (shade.B - accent.B) * amount));
        System.Windows.Application.Current.Resources["PilotAccent"] = new SolidColorBrush(accent);
        System.Windows.Application.Current.Resources["PilotOnAccent"] = new SolidColorBrush(onAccent);
        System.Windows.Application.Current.Resources["PilotAccentHover"] = new SolidColorBrush(Mix(.1));
        System.Windows.Application.Current.Resources["PilotAccentPressed"] = new SolidColorBrush(Mix(.2));
        ApplicationAccentColorManager.Apply(accent, theme, false, true);
        SetResourceReference(BackgroundProperty, "PilotBackground");
        SetResourceReference(ForegroundProperty, "PilotText");
        ApplyPrimaryButtonPalette(host.Snapshot().Running);
        ThemeIcon.Symbol = dark ? SymbolRegular.WeatherMoon24 : SymbolRegular.WeatherSunny24;
        ThemeIndicator.ToolTip = $"Following Windows: {(dark ? "dark" : "light")} theme";
        System.Windows.Automation.AutomationProperties.SetName(ThemeIndicator, ThemeIndicator.ToolTip.ToString());
        bool manual = host.ReadSettings().ManualThemeEnabled;
        ThemeIndicator.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
        ThemeButton.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        ManualThemeIcon.Symbol = dark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24;
        ThemeButton.ToolTip = dark ? "Switch to light theme" : "Switch to dark theme";
        System.Windows.Automation.AutomationProperties.SetName(ThemeButton, ThemeButton.ToolTip.ToString());
    }
    private void ApplyPrimaryButtonPalette(bool running)
    {
        PrimaryButton.Background = (Brush)FindResource(running ? "PilotStop" : "PilotAccent");
        var foreground = (Brush)FindResource(running ? "PilotOnStop" : "PilotOnAccent");
        PrimaryButton.Foreground = PrimaryText.Foreground = PrimaryIcon.Foreground = foreground;
        if (running)
        {
            PrimaryButton.MouseOverBackground = (Brush)FindResource("PilotStopHover");
            PrimaryButton.PressedBackground = (Brush)FindResource("PilotStopPressed");
            PrimaryButton.PressedForeground = foreground;
        }
        else
        {
            PrimaryButton.MouseOverBackground = (Brush)FindResource("PilotAccentHover");
            PrimaryButton.PressedBackground = (Brush)FindResource("PilotAccentPressed");
            PrimaryButton.PressedForeground = foreground;
        }
    }
    private void AccountPickerPressed(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        accountPopupWasOpenOnPress = AccountPopup.IsOpen;

    private void AccountPickerClicked(object sender, RoutedEventArgs e)
    {
        bool wasOpen = accountPopupWasOpenOnPress || AccountPopup.IsOpen;
        accountPopupWasOpenOnPress = false;
        RefreshAccountPicker();
        AccountPopup.PlacementTarget = AccountPickerButton;
        AccountPopupPanel.Width = AccountPickerButton.ActualWidth;
        AccountPopup.IsOpen = !wasOpen;
    }

    private void RefreshAccountPicker()
    {
        var selected = EpicLauncher.Accounts.Selected;
        AccountName.Text = selected?.DisplayName ?? "Add an account";
        AccountPickerButton.ToolTip = selected is null ? "Add an Epic account" : $"Switch Epic account · {selected.DisplayName}";
        AccountMenuItems.Children.Clear();
        foreach (var account in EpicLauncher.Accounts.All)
        {
            var captured = account;
            AddAccountMenuAction(SymbolRegular.Person24, account.DisplayName, () =>
            {
                try
                {
                    EpicLauncher.Accounts.Select(captured.AccountId);
                    RefreshAccountPicker();
                }
                catch (Exception error) { System.Windows.MessageBox.Show(this, error.Message, "Epic accounts"); }
                AccountPopup.IsOpen = false;
            }, selected?.AccountId == account.AccountId);
        }
        if (EpicLauncher.Accounts.All.Count > 0)
        {
            AccountMenuItems.Children.Add(new Border { Height = 1, Margin = new Thickness(7, 6, 7, 6), Background = (Brush)FindResource("PilotBorder") });
            AddAccountMenuAction(SymbolRegular.Settings24, "Manage accounts", () => OpenAccounts(false));
        }
        AccountMenuItems.Children.Add(new Border { Height = 1, Margin = new Thickness(7, 6, 7, 6), Background = (Brush)FindResource("PilotBorder") });
        AddAccountMenuAction(SymbolRegular.Add24, "Add account", () => OpenAccounts(true), highlight: true);
        AddAccountMenuAction(SymbolRegular.Settings24, "Discord remote control", OpenRemote);
    }

    private void AddAccountMenuAction(SymbolRegular icon, string label, Action action, bool selected = false, bool highlight = false)
    {
        var row = new System.Windows.Controls.Grid();
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        var symbol = new SymbolIcon { Symbol = icon, FontSize = 17, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource(highlight ? "PilotAccent" : "PilotSecondary") };
        row.Children.Add(symbol);
        var text = new System.Windows.Controls.TextBlock { Text = label, FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource(highlight ? "PilotAccent" : "PilotText") };
        System.Windows.Controls.Grid.SetColumn(text, 1);
        row.Children.Add(text);
        if (selected)
        {
            var mark = new System.Windows.Controls.TextBlock { Text = "ACTIVE", FontSize = 9, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("PilotAccent") };
            System.Windows.Controls.Grid.SetColumn(mark, 2);
            row.Children.Add(mark);
        }
        var button = new Wpf.Ui.Controls.Button { Appearance = ControlAppearance.Transparent, Content = row,
            Height = 40, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(9),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = (Brush)FindResource(selected ? "PilotBackground" : "PilotSurface"),
            MouseOverBackground = (Brush)FindResource("PilotBackground"),
            Foreground = (Brush)FindResource("PilotText") };
        System.Windows.Automation.AutomationProperties.SetName(button, selected ? label + ", selected" : label);
        button.Click += (_, _) => action();
        AccountMenuItems.Children.Add(button);
    }

    private void OpenAccounts(bool add)
    {
        AccountPopup.IsOpen = false;
        if (settingsPane is not null || accountsPane is not null || settingsTransition) return;
        accountsPane = new AccountsPane(add);
        accountsPane.BackRequested += (_, _) => ShowMainView();
        accountsPane.AccountSelectionChanged += (_, _) => RefreshAccountPicker();
        SettingsHost.Content = accountsPane;
        SettingsHost.Visibility = Visibility.Visible;
        MainView.IsEnabled = false;
        SlideSettings(true);
    }

    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        if (settingsPane is not null || accountsPane is not null || settingsTransition)
        {
            return;
        }

        settingsPane = new SettingsPane(host.ReadSettings(), launchIfMissing =>
        {
            try
            {
                host.ApplySettings(host.ReadSettings() with
                {
                    LaunchIfMissing = launchIfMissing
                });
                return null;
            }
            catch (Exception error) { return error.Message; }
        }, manual =>
        {
            try
            {
                host.ApplyAppearance(manual, host.ReadSettings().ManualDarkTheme);
                ApplyThemePreference();
                return null;
            }
            catch (Exception error) { return error.Message; }
        }, indefinite =>
        {
            try
            {
                host.ApplySettings(host.ReadSettings() with
                {
                    RunIndefinitely = indefinite
                });
                AttemptInput.Value = host.ReadSettings().AttemptLimit;
                settingsError = null;
                RefreshState();
                return null;
            }
            catch (Exception error) { return error.Message; }
        }, targetWave =>
        {
            try
            {
                host.ApplySettings(host.ReadSettings() with
                {
                    TargetWave = targetWave
                });
                RefreshState();
                return null;
            }
            catch (Exception error) { return error.Message; }
        }, webhook =>
        {
            try
            {
                host.ApplyWebhook(webhook);
                return null;
            }
            catch (InvalidDataException error) { return error.Message; }
            catch (Exception) { return "Could not save Discord settings. No changes were applied."; }
        }, host.TestWebhook, endAtTarget =>
        {
            try
            {
                host.ApplySettings(host.ReadSettings() with
                {
                    EndRunWhenTargetReached = endAtTarget
                });
                RefreshState();
                return null;
            }
            catch (Exception error) { return error.Message; }
        }, waitForAssistance =>
        {
            try
            {
                host.ApplySettings(host.ReadSettings() with
                {
                    WaitForAssistance = waitForAssistance
                });
                return null;
            }
            catch (Exception error) { return error.Message; }
        }, choice =>
        {
            try
            {
                host.SetHotkey(choice);
                RefreshState();
                return null;
            }
            catch (Exception error) { return error.Message; }
        });
        settingsPane.ConfigureLifecycle(host.ReadSettings(), (closeToTray, startup) =>
        {
            try { host.SetLifecycle(closeToTray, startup); return null; }
            catch { return "Could not save background settings. No changes were applied."; }
        });
        settingsPane.CheckUpdatesRequested += async (_, _) => await CheckUpdatesAsync();
        settingsPane.ConfigurePennyImport(host.ReadSettings().AutoImportPenny, enabled =>
        {
            try { host.SetAutoImportPenny(enabled); return null; }
            catch { return "Could not save Penny import preference."; }
        });
        settingsPane.OpenUpdateRequested += (_, _) => OpenUpdate();
        RefreshUpdates();
        settingsPane.SetRunning(host.Snapshot().Running);
        settingsPane.BackRequested += (_, _) => ShowMainView();
        settingsPane.DiscordRequested += (_, _) => OpenRemote();
        SettingsHost.Content = settingsPane;
        SettingsHost.Visibility = Visibility.Visible;
        MainView.IsEnabled = false;
        SlideSettings(true);
    }
    private void ShowMainView()
    {
        if (settingsTransition || (settingsPane is null && accountsPane is null))
        {
            return;
        }

        MainView.Visibility = Visibility.Visible;
        SettingsHost.IsEnabled = false;
        SlideSettings(false);
    }
    private void SlideSettings(bool opening)
    {
        settingsTransition = true;
        double width = Math.Max(ActualWidth, MinWidth);
        var duration = new Duration(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 230 : 0));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var main = new DoubleAnimation(opening ? 0 : -width * .18, opening ? -width * .18 : 0, duration) { EasingFunction = easing };
        var pane = new DoubleAnimation(opening ? width : 0, opening ? 0 : width, duration) { EasingFunction = easing };
        pane.Completed += (_, _) =>
        {
            MainSlide.BeginAnimation(TranslateTransform.XProperty, null);
            SettingsSlide.BeginAnimation(TranslateTransform.XProperty, null);
            MainSlide.X = 0;
            SettingsSlide.X = 0;
            settingsTransition = false;
            if (opening)
            {
                MainView.Visibility = Visibility.Collapsed;
                if (accountsPane is not null) accountsPane.FocusBack();
                else settingsPane?.FocusBack();
            }
            else
            {
                bool leavingAccounts = accountsPane is not null;
                accountsPane?.CancelPending();
                SettingsHost.Visibility = Visibility.Collapsed;
                SettingsHost.Content = null;
                SettingsHost.IsEnabled = true;
                settingsPane = null;
                accountsPane = null;
                MainView.IsEnabled = true;
                RefreshAccountPicker();
                if (leavingAccounts) AccountPickerButton.Focus();
                else SettingsButton.Focus();
            }
        };
        MainSlide.BeginAnimation(TranslateTransform.XProperty, main);
        SettingsSlide.BeginAnimation(TranslateTransform.XProperty, pane);
    }
    private void DetailsClicked(object sender, RoutedEventArgs e)
    {
        if (activityWindow is not null)
        {
            activityWindow.Activate();
            return;
        }
        activityText = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10)
        };
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        var title = new System.Windows.Controls.TextBlock { Text = "Session activity", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        grid.Children.Add(title);
        System.Windows.Controls.Grid.SetRow(activityText, 1);
        grid.Children.Add(activityText);
        var technical = new Wpf.Ui.Controls.Button { Content = "Open technical diagnostics", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        technical.Click += (_, _) => host.ShowTechnicalDetails();
        System.Windows.Controls.Grid.SetRow(technical, 2);
        grid.Children.Add(technical);
        var root = new System.Windows.Controls.Grid();
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        root.Children.Add(new TitleBar { Title = "Endurance Pilot · Activity", ShowMaximize = false });
        System.Windows.Controls.Grid.SetRow(grid, 1);
        root.Children.Add(grid);
        activityWindow = new FluentWindow
        {
            Title = "Endurance Pilot · Activity",
            Width = 600,
            Height = 650,
            MinWidth = 420,
            MinHeight = 400,
            ExtendsContentIntoTitleBar = true,
            Owner = this,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root
        };
        activityWindow.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        activityWindow.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        activityWindow.Closed += (_, _) => { activityWindow = null; activityText = null; };
        RefreshState();
        activityWindow.Show();
    }
}
