using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace EndurancePilot.App;

public partial class SettingsPane
{
    private WebhookSettings webhookSettings = new();
    private Func<WebhookSettings, string?> saveWebhook = null!;
    private Func<WebhookEvent?, Task<string>> testWebhook = null!;
    private bool testingWebhook;
    private bool updatingWebhook;
    private bool webhookUrlDirty;

    private void InitializeWebhook(WebhookSettings value, Func<WebhookSettings, string?> save, Func<WebhookEvent?, Task<string>> test)
    {
        webhookSettings = value;
        saveWebhook = save;
        testWebhook = test;
        WaveFrequencyInput.Value = value.WaveNotificationFrequency;
        WaveFrequencyInput.IsEnabled = value.Events.HasFlag(WebhookEvent.WaveReached);
        WaveFrequencyInput.ValueChanged += (_, _) => WaveFrequencyChanged();
        WaveFrequencyInput.LostKeyboardFocus += (_, _) =>
        {
            if (WaveFrequencyInput.Value is not { } frequency || !double.IsFinite(frequency)
                || frequency != Math.Truncate(frequency) || frequency is < 1 or > 30)
            {
                WaveFrequencyInput.Value = webhookSettings.WaveNotificationFrequency;
            }
        };
        try
        {
            WebhookUrlInput.Text = value.ReadUrl();
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException)
        {
            WebhookResult.Text = "Saved URL unreadable. Paste it again.";
        }
        WebhookToggle.IsChecked = value.Enabled;
        TestWebhookButton.IsEnabled = value.Enabled && WebhookUrlInput.Text.Length > 0;
        WebhookToggle.Checked += WebhookEnabledChanged;
        WebhookToggle.Unchecked += WebhookEnabledChanged;
        WebhookUrlInput.TextChanged += (_, _) =>
        {
            webhookUrlDirty = true;
            UpdateTestButtons();
            WebhookResult.Text = "Save URL to apply changes.";
        };
        (WebhookEvent Event, string Label)[] events =
        [
            (WebhookEvent.RunnerStarted,"Runner started"), (WebhookEvent.RunnerStopped,"Runner stopped"),
            (WebhookEvent.EnduranceStarted,"Endurance started"), (WebhookEvent.EnduranceEnded,"Endurance ended / rewards / interrupted"),
            (WebhookEvent.TargetReached,"Target wave completed"), (WebhookEvent.Completed30,"All 30 waves completed"),
            (WebhookEvent.Crash,"Crash / unexpected game exit"), (WebhookEvent.Error,"Game or runner error"),
            (WebhookEvent.Stuck,"Stuck / needs attention"), (WebhookEvent.Launch,"Launching / restarting Fortnite"),
            (WebhookEvent.Recovery,"Error or rejoin recovery"), (WebhookEvent.WaveReached,"Wave reached")
        ];
        foreach (var item in events)
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new System.Windows.Controls.TextBlock { Text = item.Label, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var toggle = new ToggleSwitch { IsChecked = value.Events.HasFlag(item.Event), VerticalAlignment = VerticalAlignment.Center };
            if (item.Event == WebhookEvent.Error)
            {
                label.ToolTip = toggle.ToolTip = "Detected Fortnite error dialogs include a screenshot of the game window.";
            }

            if (item.Event == WebhookEvent.WaveReached)
            {
                label.ToolTip = toggle.ToolTip = "Includes a Fortnite screenshot at each confirmed wave interval.";
            }

            System.Windows.Automation.AutomationProperties.SetName(toggle, item.Label);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(label);
            row.Children.Add(toggle);
            WebhookEventsPanel.Children.Add(row);
            void Changed(object sender, RoutedEventArgs args)
            {
                if (updatingWebhook)
                {
                    return;
                }

                var flags = toggle.IsChecked == true ? webhookSettings.Events | item.Event : webhookSettings.Events & ~item.Event;
                string? error = SaveWebhook(webhookSettings with
                {
                    Events = flags
                });
                if (error is not null)
                {
                    updatingWebhook = true;
                    toggle.IsChecked = webhookSettings.Events.HasFlag(item.Event);
                    updatingWebhook = false;
                }
            }
            toggle.Checked += Changed;
            toggle.Unchecked += Changed;
        }
        UpdateTestButtons();
    }
    internal void SetWebhookStatus(string status) => WebhookDeliveryStatus.Text = status;
    private string? SaveWebhook(WebhookSettings value)
    {
        var error = saveWebhook(value);
        if (error is null)
        {
            webhookSettings = value;
        }

        WaveFrequencyInput.IsEnabled = webhookSettings.Events.HasFlag(WebhookEvent.WaveReached);
        WebhookResult.Text = error ?? "Saved.";
        UpdateTestButtons();
        return error;
    }
    private void WebhookEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (updatingWebhook)
        {
            return;
        }

        if (SaveWebhook(webhookSettings with { Enabled = WebhookToggle.IsChecked == true }) is not null)
        {
            updatingWebhook = true;
            WebhookToggle.IsChecked = webhookSettings.Enabled;
            updatingWebhook = false;
        }
    }
    private void WaveFrequencyChanged()
    {
        if (updatingWebhook)
        {
            return;
        }

        if (WaveFrequencyInput.Value is not { } frequency || !double.IsFinite(frequency)
            || frequency != Math.Truncate(frequency) || frequency is < 1 or > 30)
        {
            WebhookResult.Text = "Choose a whole wave frequency from 1 to 30.";
            return;
        }
        if (SaveWebhook(webhookSettings with { WaveNotificationFrequency = (int)frequency }) is not null)
        {
            updatingWebhook = true;
            WaveFrequencyInput.Value = webhookSettings.WaveNotificationFrequency;
            updatingWebhook = false;
        }
    }
    private void SaveWebhookClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var value = webhookSettings.WithUrl(WebhookUrlInput.Text);
            if (SaveWebhook(value) is null)
            {
                webhookUrlDirty = false;
                UpdateTestButtons();
                updatingWebhook = true;
                WebhookToggle.IsChecked = value.Enabled;
                updatingWebhook = false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            WebhookResult.Text = ex is InvalidDataException ? ex.Message : "Windows could not encrypt the URL. It was not saved.";
        }
    }
    private void UpdateTestButtons()
    {
        bool enabled = !webhookUrlDirty && !testingWebhook && webhookSettings.Enabled && webhookSettings.ProtectedUrl.Length > 0;
        TestWebhookButton.IsEnabled = enabled;
    }
    private async void TestWebhookClicked(object sender, RoutedEventArgs e) => await SendTest(null);
    private async Task SendTest(WebhookEvent? kind)
    {
        if (testingWebhook)
        {
            return;
        }

        testingWebhook = true;
        UpdateTestButtons();
        WebhookResult.Text = "Sending all sample embeds…";
        try
        {
            WebhookResult.Text = await testWebhook(kind);
        }
        catch (Exception) { WebhookResult.Text = "Test failed. Check the URL and connection."; }
        finally { testingWebhook = false; UpdateTestButtons(); }
    }
}
