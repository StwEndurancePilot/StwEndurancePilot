using System.Diagnostics;
using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class WalkInput
{
    public static async Task Replay(GameWindow game, WalkRoute route, CancellationToken cancellation)
    {
        route.Validate();
        ValidateWindow(game);
        foreach (int key in new[] { 0x41, 0x57, 0x53, 0x44 })
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                throw new InvalidOperationException("Release movement keys before starting the route.");
            }
        }

        ushort held = 0;
        try
        {
            foreach (var step in route.Steps)
            {
                cancellation.ThrowIfCancellationRequested();
                ValidateWindow(game);
                held = step.Key switch
                {
                    WalkKey.A => 0x41,
                    WalkKey.W => 0x57,
                    WalkKey.S => 0x53,
                    WalkKey.D => 0x44,
                    _ => throw new InvalidOperationException()
                };
                Send(held, up: false);
                var elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < step.Milliseconds)
                {
                    await Task.Delay((int)Math.Clamp(step.Milliseconds - elapsed.ElapsedMilliseconds, 1, 20), cancellation);
                    ValidateWindow(game);
                }
                Send(held, up: true);
                held = 0;
            }
        }
        finally
        {

            if (held != 0)
            {
                Send(held, up: true);
            }
        }
    }

    private static void ValidateWindow(GameWindow game)
    {
        if (GameWindow.Find() != game || !game.IsForeground)
        {
            throw new InvalidOperationException("Walking stopped because Fortnite focus or window layout changed.");
        }
    }

    private static void Send(ushort key, bool up)
    {
        var input = new Input { Type = 1, Data = new InputUnion { Keyboard = new Keyboard { VirtualKey = key, Flags = up ? 2u : 0u } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            throw new InvalidOperationException("Windows did not accept the movement key event.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type; public InputUnion Data;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Keyboard
    {
        public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Mouse
    {
        public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo;
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
