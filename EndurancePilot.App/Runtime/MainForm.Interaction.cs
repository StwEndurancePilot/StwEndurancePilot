using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class UserInteractionException(string message) : InvalidOperationException(message);

public sealed partial class MainForm
{
    private readonly InteractionPause interactionPause = new();
    private CancellationTokenSource inputCancellation = new();
    internal event Action? InputPauseChanged;
    internal bool InputPaused => runner.IsRunning && runner.ControlsPaused;

    private bool ControlsCurrent(int capturedSession, int generation, GameWindow game) =>
        !closed && capturedSession == session && runner.CanControl(generation)
        && game.IsForeground && GameWindow.Find() == game;

    private void InvalidateRecoveryObservation()
    {
        crashRecovery.InvalidateObservation();
        startupErrorRecovery.InvalidateObservation();
    }

    private void CancelInput()
    {
        inputCancellation.Cancel();
        inputCancellation.Dispose();
        inputCancellation = new();
        startupErrorRestart?.Cancel();
        InvalidateRecoveryObservation();
        launchSelectedAccountOnStart = false;
        clearedPointerFor = null;
    }

    internal void ToggleInputPause()
    {
        if (!runner.IsRunning || runner.Mode == RunnerMode.Observe) return;
        CancelInput();
        if (runner.ControlsPaused)
        {
            runner.ResumeControls();
            interactionPause.Begin();
            status.Text = "Resumed · return to Fortnite to continue after fresh screen validation.";
        }
        else
        {
            runner.PauseControls();
            interactionPause.Clear();
            status.Text = "Paused · monitoring continues. Resume to allow game controls; Stop ends the session.";
        }
        AddActivity(status.Text);
        timer.Interval = 250;
        InputPauseChanged?.Invoke();
    }

    private void WaitForUserInput(string reason)
    {
        if (!runner.IsRunning || runner.ControlsPaused || controller.Status == ControllerStatus.PausedForAttention) return;
        runner.ReacquireInput();
        InvalidateRecoveryObservation();
        interactionPause.Begin();
        InputPauseChanged?.Invoke();
        timer.Interval = 250;
        status.Text = "Waiting for Fortnite and released input · monitoring continues. Use Pause to take control.";
        AddActivity(reason);
    }

    private bool InputReady(GameWindow game)
    {
        if (runner.ControlsPaused) return false;
        if (!interactionPause.Active) return true;
        bool gotPointer = GetCursorPos(out var point);
        bool held = false;
        for (int key = 1; key < 255; key++)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0) { held = true; break; }
        }
        if (!interactionPause.Ready(DateTimeOffset.UtcNow, game.IsForeground, held || !gotPointer, point.X, point.Y))
        {
            timer.Interval = 250;
            return false;
        }
        InputPauseChanged?.Invoke();
        runner.ReacquireInput();
        status.Text = "Input idle · reacquiring Fortnite before resuming.";
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out InputPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
