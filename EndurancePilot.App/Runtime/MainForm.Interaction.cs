using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class UserInteractionException(string message) : InvalidOperationException(message);

public sealed partial class MainForm
{
    private readonly InteractionPause interactionPause = new();
    internal event Action? InputPauseChanged;
    internal bool InputPaused => runner.IsRunning && interactionPause.Active;
    internal (bool Visible, string Label) ResumePrompt()
    {
        if (!InputPaused || GameWindow.Find() is not { } game || game.IsForeground)
        {
            return (false, "Resume in Fortnite");
        }

        if (!autoResumeFortnite || runner.Mode == RunnerMode.Observe
            || controller.Status is not (ControllerStatus.Observing or ControllerStatus.AwaitingTransition))
        {
            return (true, "Resume in Fortnite");
        }

        var lastInput = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref lastInput))
        {
            return (true, "Resume in Fortnite");
        }

        uint idleMilliseconds = unchecked((uint)Environment.TickCount - lastInput.Tick);
        int remaining = Math.Max(0, (int)Math.Ceiling((autoResumeIdleSeconds * 1000.0 - idleMilliseconds) / 1000));
        return (true, remaining == 0 ? "Focusing Fortnite…" : $"Resume in Fortnite · {remaining}s");
    }
    private void WaitForUserInput(string reason)
    {
        if (!runner.IsRunning || controller.Status == ControllerStatus.PausedForAttention)
        {
            return;
        }

        interactionPause.Begin();
        InputPauseChanged?.Invoke();
        controller.InvalidateObservation();
        timer.Interval = 250;
        status.Text = autoResumeFortnite
            ? $"Paused for your input · focusing Fortnite after {autoResumeIdleSeconds} idle seconds."
            : "Paused for your input · focus Fortnite and leave input idle for 2 seconds to resume.";
        AddActivity(reason);
    }
    internal void ResumeInput()
    {
        if (!InputPaused)
        {
            return;
        }

        GameWindow.Find()?.Activate();
        interactionPause.Begin();
        InputPauseChanged?.Invoke();
        timer.Interval = 250;
    }
    private bool InputReady(GameWindow game, bool resumeUnattended = false)
    {
        if (!interactionPause.Active)
        {
            return true;
        }

        bool gotPointer = GetCursorPos(out var point);
        bool held = false;
        for (int key = 1; key < 255; key++)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                held = true;
                break;
            }
        }

        var lastInput = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (resumeUnattended && (!GetLastInputInfo(ref lastInput)
            || unchecked((uint)Environment.TickCount - lastInput.Tick) < (uint)autoResumeIdleSeconds * 1000u))
        {
            held = true;
        }

        if (!interactionPause.Ready(DateTimeOffset.UtcNow, game.IsForeground, held || !gotPointer, point.X, point.Y,
            resumeUnattended, resumeUnattended ? 0 : 10))
        {
            timer.Interval = 250;
            return false;
        }
        InputPauseChanged?.Invoke();
        controller.InvalidateObservation();
        status.Text = "Input idle · reacquiring Fortnite before resuming.";
        AddActivity("Resumed after input interruption.");
        return true;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct InputPoint
    {
        public int X; public int Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size; public uint Tick;
    }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out InputPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
