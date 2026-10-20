using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class UserInteractionException(string message) : InvalidOperationException(message);

public sealed partial class MainForm
{
    private readonly InteractionPause interactionPause = new();
    private DateTimeOffset confirmedHubAt;
    internal bool InputPaused => runner.IsRunning && interactionPause.Active;
    private void WaitForUserInput(string reason)
    {
        if (!runner.IsRunning || controller.Status == ControllerStatus.PausedForAttention)
        {
            return;
        }

        interactionPause.Begin();
        controller.InvalidateObservation();
        timer.Interval = 250;
        status.Text = "Paused for your input · focus Fortnite and leave input idle for 2 seconds to resume.";
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
        timer.Interval = 250;
    }
    private bool InputReady(GameWindow game, bool resumeHub = false)
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
        if (resumeHub && (!GetLastInputInfo(ref lastInput)
            || unchecked((uint)Environment.TickCount - lastInput.Tick) < 10000))
        {
            held = true;
        }

        if (!interactionPause.Ready(DateTimeOffset.UtcNow, game.IsForeground, held || !gotPointer, point.X, point.Y, resumeHub))
        {
            timer.Interval = 250;
            return false;
        }
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
