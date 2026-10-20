using System.Diagnostics;
using System.Windows;

namespace EndurancePilot.App;

public partial class PilotWindow
{
    private readonly UpdateChecker updateChecker = new();
    private readonly CancellationTokenSource updateLifetime = new();
    private UpdateCheckResult updateResult = new(UpdateStatus.NotChecked, "Updates are checked when Pilot opens.");
    private bool checkingUpdates;

    private async Task PollUpdatesAsync()
    {

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            await CheckUpdatesAsync();
            while (await timer.WaitForNextTickAsync(updateLifetime.Token))
            {
                await CheckUpdatesAsync();
            }
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { }
    }

    private async Task CheckUpdatesAsync()
    {
        if (checkingUpdates || !loaded)
        {
            return;
        }

        checkingUpdates = true;
        RefreshUpdates();
        try
        {
            var result = await updateChecker.CheckAsync(cancellation: updateLifetime.Token);

            updateResult = result.Status == UpdateStatus.Unavailable && updateResult.Status == UpdateStatus.Available
                ? updateResult with
                {
                    Message = result.Message
                } : result;
        }
        finally
        {
            checkingUpdates = false;
            if (loaded)
            {
                RefreshUpdates();
            }
        }
    }
    private void RefreshUpdates()
    {
        UpdateNotice.SetUpdate(updateResult);
        settingsPane?.SetUpdates(updateResult, checkingUpdates);
    }
    private void OpenUpdateClicked(object? sender, EventArgs e) => OpenUpdate();
    private void OpenUpdate()
    {
        if (updateResult.Status != UpdateStatus.Available || updateResult.Link is not { } link)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            updateResult = updateResult with
            {
                Message = "Couldn’t open the browser. Visit the project’s GitHub releases."
            };
            RefreshUpdates();
        }
    }
}
