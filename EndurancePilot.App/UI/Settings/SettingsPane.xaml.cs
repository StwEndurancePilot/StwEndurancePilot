using System.Windows;
using Wpf.Ui.Controls;

namespace EndurancePilot.App;

public partial class SettingsPane : System.Windows.Controls.UserControl
{
    internal void ConfigureTransitionFocus(bool enabled, Func<bool, string?> save)
    {
        bool saved = enabled, changing = false;
        TransitionFocusToggle.IsChecked = enabled;
        void Changed(object sender, RoutedEventArgs e)
        {
            if (changing) return;
            bool next = TransitionFocusToggle.IsChecked == true;
            string? error = save(next);
            if (error is null) saved = next;
            else { changing = true; TransitionFocusToggle.IsChecked = saved; changing = false; }
            SaveStatus.Text = error ?? "Settings saved.";
        }
        TransitionFocusToggle.Checked += Changed;
        TransitionFocusToggle.Unchecked += Changed;
    }
    internal void ConfigureLifecycle(RunnerSettings settings, Func<bool, bool, string?> save)
    {
        bool close = settings.CloseToTray, startup = settings.StartWithWindows, changing = false;
        CloseToTrayToggle.IsChecked = close; StartupToggle.IsChecked = startup;
        void Changed(object sender, RoutedEventArgs e)
        {
            if (changing) return;
            string? error = save(CloseToTrayToggle.IsChecked == true, StartupToggle.IsChecked == true);
            if (error is null) { close = CloseToTrayToggle.IsChecked == true; startup = StartupToggle.IsChecked == true; }
            else { changing = true; CloseToTrayToggle.IsChecked = close; StartupToggle.IsChecked = startup; changing = false; }
            SaveStatus.Text = error ?? "Settings saved.";
        }
        CloseToTrayToggle.Checked += Changed; CloseToTrayToggle.Unchecked += Changed;
        StartupToggle.Checked += Changed; StartupToggle.Unchecked += Changed;
    }
    public event EventHandler? BackRequested;
    internal void ConfigurePennyImport(bool enabled, Func<bool, string?> save)
    {
        bool saved = enabled, changing = false;
        PennyImportToggle.IsChecked = enabled;
        void Changed(object sender, RoutedEventArgs e)
        {
            if (changing) return;
            bool next = PennyImportToggle.IsChecked == true;
            string? error = save(next);
            if (error is null) saved = next;
            else { changing = true; PennyImportToggle.IsChecked = saved; changing = false; }
            SaveStatus.Text = error ?? "Settings saved.";
        }
        PennyImportToggle.Checked += Changed; PennyImportToggle.Unchecked += Changed;
    }
    public event EventHandler? DiscordRequested;
    private void DiscordClicked(object sender, RoutedEventArgs e) => DiscordRequested?.Invoke(this, EventArgs.Empty);
    private readonly Func<bool, string?> saveLaunch;
    private readonly Func<bool, string?> saveManualTheme;
    private readonly Func<bool, string?> saveIndefinite;
    private readonly Func<int, string?> saveTargetWave;
    private readonly Func<bool, string?> saveEndAtTarget;
    private readonly Func<bool, string?> saveAssistance;
    private readonly Func<string, string?> saveHotkey;
    private bool savedEndAtTarget;
    private bool? running;
    private bool updating;
    private bool savedLaunch;
    private bool savedManualTheme;
    private bool savedIndefinite;
    private bool savedAssistance;
    private string savedHotkey;

    internal SettingsPane(RunnerSettings settings, Func<bool, string?> saveLaunch, Func<bool, string?> saveManualTheme, Func<bool, string?> saveIndefinite, Func<int, string?> saveTargetWave,
        Func<WebhookSettings, string?> saveWebhook, Func<WebhookEvent?, Task<string>> testWebhook,
        Func<bool, string?> saveEndAtTarget, Func<bool, string?> saveAssistance,
        Func<string, string?> saveHotkey)
    {
        InitializeComponent();
        SetUpdates(new(UpdateStatus.NotChecked, "Updates are checked when Pilot opens."));
        InitializeWebhook(settings.Webhook ?? new(), saveWebhook, testWebhook);
        this.saveLaunch = saveLaunch;
        this.saveManualTheme = saveManualTheme;
        this.saveIndefinite = saveIndefinite;
        this.saveTargetWave = saveTargetWave;
        this.saveEndAtTarget = saveEndAtTarget;
        this.saveAssistance = saveAssistance;
        this.saveHotkey = saveHotkey;
        HotkeyChoice.ItemsSource = RunnerHotkey.Choices;
        HotkeyChoice.SelectedItem = savedHotkey = settings.StartStopHotkey;
        HotkeyChoice.SelectionChanged += (_, _) => HotkeyChanged();
        AssistanceToggle.IsChecked = savedAssistance = settings.WaitForAssistance;
        AssistanceToggle.Checked += AssistanceChanged;
        AssistanceToggle.Unchecked += AssistanceChanged;
        EndAtTargetToggle.IsChecked = savedEndAtTarget = settings.EndRunWhenTargetReached;
        EndAtTargetToggle.Checked += EndAtTargetChanged;
        EndAtTargetToggle.Unchecked += EndAtTargetChanged;
        TargetWaveInput.Value = settings.TargetWave;
        UpdateTargetHint(settings.TargetWave);
        TargetWaveInput.ValueChanged += (_, _) => TargetWaveChanged();
        TargetWaveInput.LostKeyboardFocus += (_, _) =>
        {
            if (TargetWaveInput.Value is null)
            {
                TargetWaveInput.Value = savedTargetWave;
            }
        };
        savedTargetWave = settings.TargetWave;
        IndefiniteToggle.IsChecked = savedIndefinite = settings.RunIndefinitely;
        IndefiniteToggle.Checked += IndefiniteChanged;
        IndefiniteToggle.Unchecked += IndefiniteChanged;
        LaunchToggle.IsChecked = savedLaunch = settings.LaunchIfMissing;
        ManualThemeToggle.IsChecked = savedManualTheme = settings.ManualThemeEnabled;
        ManualThemeToggle.Checked += ManualThemeChanged;
        ManualThemeToggle.Unchecked += ManualThemeChanged;
        LaunchToggle.Checked += LaunchChanged;
        LaunchToggle.Unchecked += LaunchChanged;
    }

    internal void SetRunning(bool value)
    {
        if (running == value)
        {
            return;
        }

        running = value;
        RunOptions.IsEnabled = !value;
        SaveStatus.Text = value ? "Stop to change run options. Appearance and Discord stay available." : "Changes save automatically, except the webhook URL.";
        SaveStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "PilotSecondary");
    }

    private int savedTargetWave;
    private void HotkeyChanged()
    {
        if (updating || HotkeyChoice.SelectedItem is not string choice || choice == savedHotkey)
        {
            return;
        }

        string? error = saveHotkey(choice);
        if (error is null)
        {
            savedHotkey = choice;
        }
        else
        {
            updating = true;
            HotkeyChoice.SelectedItem = savedHotkey;
            updating = false;
        }
        ShowSaveResult(error);
    }
    private void TargetWaveChanged()
    {
        if (updating || running == true)
        {
            return;
        }

        if (TargetWaveInput.Value is not { } value || !double.IsFinite(value) || value != Math.Truncate(value) || value is < 1 or > 30)
        {
            ShowSaveResult("Choose a whole target wave from 1 to 30.");
            return;
        }
        string? error = saveTargetWave((int)value);
        if (error is null)
        {
            savedTargetWave = (int)value;
            UpdateTargetHint(savedTargetWave);
        }
        else
        {
            updating = true;
            TargetWaveInput.Value = savedTargetWave;
            updating = false;
        }
        ShowSaveResult(error);
    }
    private void EndAtTargetChanged(object sender, RoutedEventArgs e)
    {
        if (updating || running == true)
        {
            return;
        }

        string? error = saveEndAtTarget(EndAtTargetToggle.IsChecked == true);
        if (error is null)
        {
            savedEndAtTarget = EndAtTargetToggle.IsChecked == true;
        }
        else
        {
            updating = true;
            EndAtTargetToggle.IsChecked = savedEndAtTarget;
            updating = false;
        }
        UpdateTargetHint(savedTargetWave);
        ShowSaveResult(error);
    }
    private void AssistanceChanged(object sender, RoutedEventArgs e)
    {
        if (updating || running == true)
        {
            return;
        }

        string? error = saveAssistance(AssistanceToggle.IsChecked == true);
        if (error is null)
        {
            savedAssistance = AssistanceToggle.IsChecked == true;
        }
        else
        {
            updating = true;
            AssistanceToggle.IsChecked = savedAssistance;
            updating = false;
        }
        ShowSaveResult(error);
    }
    private void UpdateTargetHint(int value) => TargetWaveHint.Text = !savedEndAtTarget
        ? $"Wave {value} remains your target. The mission continues until its final result."
        : value == 30
        ? "Finish all 30 waves and wait for the final result."
        : $"Leave when wave {value + 1} is confirmed active, after wave {value} finishes.";
    private void IndefiniteChanged(object sender, RoutedEventArgs e)
    {
        if (updating || running == true)
        {
            return;
        }

        string? error = saveIndefinite(IndefiniteToggle.IsChecked == true);
        if (error is null)
        {
            savedIndefinite = IndefiniteToggle.IsChecked == true;
        }
        else
        {
            updating = true;
            IndefiniteToggle.IsChecked = savedIndefinite;
            updating = false;
        }
        ShowSaveResult(error);
    }
    private void LaunchChanged(object sender, RoutedEventArgs e)
    {
        if (updating || running == true)
        {
            return;
        }

        string? error = saveLaunch(LaunchToggle.IsChecked == true);
        if (error is null)
        {
            savedLaunch = LaunchToggle.IsChecked == true;
        }
        else
        {
            updating = true;
            LaunchToggle.IsChecked = savedLaunch;
            updating = false;
        }
        ShowSaveResult(error);
    }
    private void ManualThemeChanged(object sender, RoutedEventArgs e)
    {
        if (updating)
        {
            return;
        }

        string? error = saveManualTheme(ManualThemeToggle.IsChecked == true);
        if (error is null)
        {
            savedManualTheme = ManualThemeToggle.IsChecked == true;
        }
        else
        {
            updating = true;
            ManualThemeToggle.IsChecked = savedManualTheme;
            updating = false;
        }
        ShowSaveResult(error);
    }
    private void ShowSaveResult(string? error)
    {
        SaveStatus.Text = error ?? "Settings saved.";
        SaveStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, error is null ? "PilotSecondary" : "PilotWarning");
    }

    internal void FocusBack() => BackButton.Focus();
    private void BackClicked(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
