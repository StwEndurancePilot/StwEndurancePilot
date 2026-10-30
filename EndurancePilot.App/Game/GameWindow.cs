using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EndurancePilot.App;

internal sealed class GameObservationChangedException(string message) : InvalidOperationException(message);

public sealed record GameWindow(nint Handle, int ProcessId, Rectangle ClientBounds)
{
    private const string ProcessName = "FortniteClient-Win64-Shipping";
    public static GameWindow? Find()
    {
        var candidates = new List<GameWindow>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                nint handle = process.MainWindowHandle;
                if (handle == 0 || !IsWindowVisible(handle) || IsIconic(handle))
                {
                    continue;
                }

                if (!GetClientRect(handle, out var rect))
                {
                    continue;
                }

                var point = new NativePoint();
                if (!ClientToScreen(handle, ref point))
                {
                    continue;
                }

                if (rect.Right <= 0 || rect.Bottom <= 0)
                {
                    continue;
                }

                candidates.Add(new(handle, process.Id, new(point.X, point.Y, rect.Right, rect.Bottom)));
            }
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    public bool IsForeground => GetForegroundWindow() == Handle;
    public bool Activate() => SetForegroundWindow(Handle);

    internal static async Task<(nint Game, nint Previous)?> FocusForTransitionAsync(Func<bool> isCurrent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!isCurrent()) return null;
        nint handle;
        int processId;
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            if (processes.Length != 1) return null;
            handle = processes[0].MainWindowHandle;
            processId = processes[0].Id;
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (handle == 0) return null;
        nint previous = GetForegroundWindow();
        bool focused = false;
        try
        {
            if (IsIconic(handle)) ShowWindowAsync(handle, 9);
            await Task.Delay(150, token);
            if (!isCurrent() || Find() is not { } game || game.Handle != handle || game.ProcessId != processId) return null;
            // No Alt tap, input queue attachment, or background keystrokes.
            if (!game.IsForeground && !SetForegroundWindow(handle)) return null;
            await Task.Delay(100, token);
            focused = isCurrent() && game.IsForeground && Find() == game;
            return focused ? (handle, previous) : null;
        }
        finally { if (!focused && isCurrent()) ReturnTransitionFocus(handle, previous); }
    }

    internal static bool ReturnTransitionFocus(nint game, nint previous)
    {
        if (game == 0 || previous == game || GetForegroundWindow() != game) return true;
        return previous != 0 && previous != game && IsWindow(previous)
            && IsWindowVisible(previous) && SetForegroundWindow(previous);
    }
    public Bitmap Capture()
    {
        if (!IsForeground)
        {
            throw new GameObservationChangedException("Fortnite is not in the foreground. Waiting for fresh evidence.");
        }

        var fresh = Find();
        if (fresh is null || fresh.Handle != Handle || fresh.ClientBounds != ClientBounds)
        {
            throw new GameObservationChangedException("Game window changed. Reacquiring its layout.");
        }

        var image = new Bitmap(ClientBounds.Width, ClientBounds.Height);
        try
        {
            using var graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(ClientBounds.Location, Point.Empty, ClientBounds.Size, CopyPixelOperation.SourceCopy);
            if (!IsForeground || Find() != this)
            {
                throw new GameObservationChangedException("Focus or layout changed during capture. Reacquiring the screen.");
            }

            return image;
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            image.Dispose();
            throw new System.ComponentModel.Win32Exception(error.NativeErrorCode,
                $"Fortnite screen capture (CopyFromScreen) failed; native error {error.NativeErrorCode}: {error.Message}");
        }
        catch { image.Dispose(); throw; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
}
