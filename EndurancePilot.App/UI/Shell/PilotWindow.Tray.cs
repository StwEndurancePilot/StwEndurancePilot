using System.ComponentModel;
using System.Windows;

namespace EndurancePilot.App;

public partial class PilotWindow
{
    private System.Windows.Forms.NotifyIcon? tray;
    private bool exitRequested;
    private bool trayHintShown;
    private EventWaitHandle? showSignal;
    private RegisteredWaitHandle? showWait;

    private void InitializeTray()
    {
        tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Endurance Pilot", Visible = true,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Pilot", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)));
        menu.Items.Add("Quit Pilot", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (ConfirmExit()) { exitRequested = true; Close(); } })));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\EndurancePilot.Show");
        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)), null, Timeout.Infinite, false);
        System.Windows.Application.Current.SessionEnding += SessionEnding;
        Closed += (_, _) =>
        {
            System.Windows.Application.Current.SessionEnding -= SessionEnding;
            showWait?.Unregister(null); showSignal?.Dispose();
            tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); menu.Dispose();
        };
    }
    private void SessionEnding(object sender, SessionEndingCancelEventArgs e) => exitRequested = true;
    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    private bool ConfirmExit() => System.Windows.MessageBox.Show(this,
        "Quitting stops the runner and Discord commands. Your pairing and settings stay saved.\n\nQuit Endurance Pilot?",
        "Quit Pilot?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private bool KeepInTray(CancelEventArgs e)
    {
        if (exitRequested) return false;
        if (host.ReadSettings().CloseToTray)
        {
            e.Cancel = true; Hide(); activityWindow?.Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray?.ShowBalloonTip(2500, "Pilot is still running", "Open Pilot or quit from its tray icon.", System.Windows.Forms.ToolTipIcon.Info);
            }
            return true;
        }
        if (!ConfirmExit()) { e.Cancel = true; return true; }
        return false;
    }
}
