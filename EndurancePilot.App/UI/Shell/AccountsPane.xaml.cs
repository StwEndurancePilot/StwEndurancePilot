using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace EndurancePilot.App;

public partial class AccountsPane : System.Windows.Controls.UserControl
{
    private const string EpicCodePage = "https://www.epicgames.com/id/api/redirect?clientId=3f69e56c7649492c8cc29f1af08a8a12&responseType=code";
    private readonly CancellationTokenSource closing = new();
    private bool working;

    public event EventHandler? BackRequested;

    public AccountsPane(bool beginSignIn = false)
    {
        InitializeComponent();
        RefreshAccounts();
        if (beginSignIn) Loaded += (_, _) => SignInClicked(this, new RoutedEventArgs());
    }

    public void FocusBack() => BackButton.Focus();
    public void CancelPending() => closing.Cancel();
    private void BackClicked(object sender, RoutedEventArgs e)
    {
        CancelPending();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshAccounts()
    {
        AccountList.ItemsSource = null;
        AccountList.ItemsSource = EpicLauncher.Accounts.All.ToArray();
        AccountList.SelectedItem = EpicLauncher.Accounts.Selected;
        EmptyAccounts.Visibility = EpicLauncher.Accounts.All.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountList.Visibility = EpicLauncher.Accounts.All.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoveButton.Visibility = EpicLauncher.Accounts.All.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        int count = EpicLauncher.Accounts.All.Count;
        AccountCountText.Text = count == 1 ? "1 connected" : $"{count} connected";
        RemoveButton.IsEnabled = AccountList.SelectedItem is not null && !working;
    }

    private void AccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountList.SelectedItem is EpicAccount selected && EpicLauncher.Accounts.SelectedAccountId != selected.AccountId)
        {
            try
            {
                EpicLauncher.Accounts.Select(selected.AccountId);
                AccountSelectionChanged?.Invoke(this, EventArgs.Empty);
                SetStatus($"{selected.DisplayName} selected for launch and relaunch.");
            }
            catch (Exception error) { SetStatus(error.Message, true); }
        }
        RemoveButton.IsEnabled = AccountList.SelectedItem is not null && !working;
    }

    public event EventHandler? AccountSelectionChanged;

    private async void SignInClicked(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        using var auth = new EpicAuthentication();
        var signIn = await auth.BeginSignInAsync(closing.Token);
        Process.Start(new ProcessStartInfo(signIn.VerificationUri.AbsoluteUri) { UseShellExecute = true });
        SetStatus("Approve sign in on Epic's website. Waiting for your account…");
        var added = await auth.FinishSignInAsync(signIn, closing.Token);
        EpicLauncher.Accounts.Add(added);
        RefreshAccounts();
    }, "Account connected. Select Launch Fortnite when you're ready.");

    private void OpenCodePageClicked(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(EpicCodePage) { UseShellExecute = true });

    private void AdvancedClicked(object sender, RoutedEventArgs e)
    {
        bool show = AdvancedPanel.Visibility != Visibility.Visible;
        AdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedToggle.Content = show ? "Hide code and device options  ↑" : "Use a code or device credentials  ↓";
    }

    private async void AuthorizationClicked(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        using var auth = new EpicAuthentication();
        EpicLauncher.Accounts.Add(await auth.AddFromAuthorizationCodeAsync(AuthorizationCode.Text, closing.Token));
        AuthorizationCode.Clear();
        RefreshAccounts();
    }, "Account connected.");

    private async void ExchangeClicked(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        using var auth = new EpicAuthentication();
        EpicLauncher.Accounts.Add(await auth.AddFromCodeAsync(ExchangeCode.Text, closing.Token));
        ExchangeCode.Clear();
        RefreshAccounts();
    }, "Account connected.");

    private async void DeviceClicked(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        using var auth = new EpicAuthentication();
        EpicLauncher.Accounts.Add(await auth.AddFromDeviceAuthAsync(AccountId.Text, DeviceId.Text, DeviceSecret.Password, closing.Token));
        AccountId.Clear(); DeviceId.Clear(); DeviceSecret.Clear();
        RefreshAccounts();
    }, "Account connected.");

    private void RemoveClicked(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not EpicAccount selected) return;
        if (System.Windows.MessageBox.Show(Window.GetWindow(this), $"Remove {selected.DisplayName} from Endurance Pilot?", "Remove Epic account",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            EpicLauncher.Accounts.Remove(selected.AccountId);
            RefreshAccounts();
            AccountSelectionChanged?.Invoke(this, EventArgs.Empty);
            SetStatus($"{selected.DisplayName} removed.");
        }
        catch (Exception error) { SetStatus(error.Message, true); }
    }

    private async Task Work(Func<Task> action, string success)
    {
        if (working) return;
        working = true;
        AccountContent.IsEnabled = false;
        SetStatus("Working…");
        try { await action(); SetStatus(success); }
        catch (OperationCanceledException) { if (!closing.IsCancellationRequested) SetStatus("Cancelled."); }
        catch (Exception error) { SetStatus(error.Message, true); }
        finally
        {
            working = false;
            AccountContent.IsEnabled = true;
            RefreshAccounts();
            AccountSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "PilotWarning" : "PilotSecondary");
    }
}
