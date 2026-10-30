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
}
