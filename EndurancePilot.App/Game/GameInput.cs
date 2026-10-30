using System.ComponentModel;
using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class TargetChangedException() : InvalidOperationException("Detected button changed during recognition. No click or keypress sent.");

internal static class GameInput
{
    // Explicit live diagnostic only. The runner never calls this to regain focus.
    internal static async Task<bool> FocusForAutomationAsync(GameWindow game, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (GameWindow.Find() != game) return false;
        if (game.IsForeground) return true;
        game.Activate();
        await Task.Delay(100, cancellation);
        if (GameWindow.Find() != game) return false;
        if (game.IsForeground) return true;

        // A background request lacks the last-input permission that clicking Run gives
        // Pilot. A balanced ALT tap lets Windows release its foreground lock. Do not
        // combine it with a held key/button or attach input queues to another process.
        for (int key = 1; key < 255; key++)
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
        var down = new NativeInput { Type = 1, Data = new() { Keyboard = new() { VirtualKey = 0x12 } } };
        var up = new NativeInput { Type = 1, Data = new() { Keyboard = new() { VirtualKey = 0x12, Flags = 2 } } };
        uint inserted = SendInput(2, [down, up], Marshal.SizeOf<NativeInput>());
        if (inserted != 2)
        {
            if (inserted == 1) SendInput(1, [up], Marshal.SizeOf<NativeInput>());
            return false;
        }
        await Task.Delay(100, cancellation);
        if (GameWindow.Find() != game) return false;
        game.Activate();
        await Task.Delay(250, cancellation);
        return GameWindow.Find() == game && game.IsForeground;
    }

    public static async Task<bool> DispatchAsync(GameWindow game, Bitmap observedImage, ClickAction action,
        OcrService ocr, Func<bool> sessionIsCurrent, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!sessionIsCurrent()) return false;
        if (action.Step == NavigationStep.SearchSaveTheWorld && !DiscoveryNavigation.IsSearchAction(action))
        {
            throw new InvalidOperationException("Search typing is not allowed on this screen.");
        }

        var now = DateTimeOffset.UtcNow;
        if (now < action.ObservedAt || now - action.ObservedAt > NavigationTiming.MaximumObservationAge(action.Step))
        {
            throw new GameObservationChangedException("Detection expired before input. Reacquiring the screen.");
        }

        if (observedImage.Width != action.FrameWidth || observedImage.Height != action.FrameHeight
            || !AutomationController.IsValidTarget(action.Target, action.FrameWidth, action.FrameHeight))
        {
            throw new InvalidOperationException("Detection does not belong to this image.");
        }

        ValidateWindow(game, action);
        if ((GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
        {
            throw new UserInteractionException("Mouse is in use. No input sent.");
        }

        using var currentImage = game.Capture();

        if (QuestMenuEntry.IsFallback(action))
        {
            var bars = HealthBarDetector.Find(currentImage);
            if (bars.Count != 1 || bars[0].Bounds.IntersectionOverUnion(action.Target) <= .6)
            {
                throw new TargetChangedException();
            }
        }
        if (action.Step == NavigationStep.ContinueUnlock)
        {
            var current = await ocr.ReadAsync(currentImage, cancellation, enhance: false);
            if (!UnlockTargetStillPresent(current, action))
            {
                throw new TargetChangedException();
            }
        }
        else if (!QuestMenuEntry.IsFallback(action) && !MenuGeometry.UsesFixedTarget(action.Step)
            && action.Step is not (NavigationStep.CollectMissionRewards or NavigationStep.ReturnToHomebase or NavigationStep.ContinueResults)
            && !TargetStillMatches(observedImage, currentImage, action.Target))
        {
            throw new TargetChangedException();
        }

        var point = new NativePoint
        {
            X = game.ClientBounds.Left + (int)Math.Round(action.Target.CenterX),
            Y = game.ClientBounds.Top + (int)Math.Round(action.Target.CenterY)
        };
        ValidateWindow(game, action);
        if (GetAncestor(WindowFromPoint(point), 2) != game.Handle)
        {
            throw new UserInteractionException("Another window covers the target. No input sent.");
        }

        if (action.Key is { } key)
        {
            if (!InteractionPrompts.IsAllowedKeyboardAction(action))
            {
                throw new InvalidOperationException("Keyboard action is not allowed for this screen.");
            }

            ushort virtualKey = key switch
            {
                NavigationKey.C => 0x43,
                NavigationKey.Space => 0x20,
                NavigationKey.E => 0x45,
                NavigationKey.Tab => 0x09,
                NavigationKey.Escape => 0x1B,
                _ => throw new InvalidOperationException("Unsupported key")
            };
            foreach (int heldKey in new[] { (int)virtualKey, 0x10, 0x11, 0x12, 0x41, 0x57, 0x53, 0x44 })
            {
                if ((GetAsyncKeyState(heldKey) & 0x8000) != 0)
                {
                    throw new UserInteractionException("Keyboard is in use. No input sent.");
                }
            }

            ValidateWindow(game, action);
            cancellation.ThrowIfCancellationRequested();
            if (!sessionIsCurrent())
            {
                return false;
            }

            NativeInput down = new()
            {
                Type = 1,
                Data = new()
                {
                    Keyboard = new()
                    {
                        VirtualKey = virtualKey
                    }
                }
            };
            NativeInput up = new()
            {
                Type = 1,
                Data = new()
                {
                    Keyboard = new()
                    {
                        VirtualKey = virtualKey,
                        Flags = 2
                    }
                }
            };
            return await KeyPress.SendAsync(
                () => SendRequired([down], "key down for " + action.Step),
                () =>
                {
                    if (SendInput(1, [up], Marshal.SizeOf<NativeInput>()) != 1 && SendInput(1, [up], Marshal.SizeOf<NativeInput>()) != 1)
                    {
                        throw InputError("key release for " + action.Step, Marshal.GetLastWin32Error(), 0, 1);
                    }
                },
                () => sessionIsCurrent() && game.IsForeground, cancellation);
        }

        int desktopX = GetSystemMetrics(76), desktopY = GetSystemMetrics(77);
        int desktopWidth = GetSystemMetrics(78), desktopHeight = GetSystemMetrics(79);
        if (desktopWidth <= 1 || desktopHeight <= 1)
        {
            throw new InvalidOperationException("Desktop geometry unavailable.");
        }

        NativeInput move = new()
        {
            Data = new()
            {
                Mouse = new()
                {
                    X = (int)Math.Round((point.X - desktopX) * 65535d / (desktopWidth - 1)),
                    Y = (int)Math.Round((point.Y - desktopY) * 65535d / (desktopHeight - 1)),
                    Flags = 0x0001 | 0x8000 | 0x4000
                }
            }
        };
        cancellation.ThrowIfCancellationRequested();
        if (!sessionIsCurrent()) return false;
        ValidateWindow(game, action);
        SendRequired([move], "move pointer for " + action.Step);

        await Task.Delay(NavigationTiming.HoverSettleMilliseconds(action.Step), cancellation);
        if (!sessionIsCurrent())
        {
            return false;
        }

        ValidateWindow(game, action);
        if (!GetCursorPos(out var settled) || Math.Abs(settled.X - point.X) > 2 || Math.Abs(settled.Y - point.Y) > 2)
        {
            throw new UserInteractionException("Pointer moved during settling. Reacquiring the target; no click sent.");
        }

        if (GetAncestor(WindowFromPoint(point), 2) != game.Handle
            || (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
        {
            throw new UserInteractionException("Target became covered or mouse is in use. No click sent.");
        }

        if (!sessionIsCurrent())
        {
            return false;
        }

        ValidateWindow(game, action);
        if (!GetCursorPos(out var finalPointer) || Math.Abs(finalPointer.X - point.X) > 2 || Math.Abs(finalPointer.Y - point.Y) > 2
            || GetAncestor(WindowFromPoint(point), 2) != game.Handle
            || (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
        {
            throw new UserInteractionException("Pointer or target changed during verification. Reacquiring the target; no click sent.");
        }

        NativeInput[] inputs =
        [
            new() { Data = new() { Mouse = new() { Flags = 0x0002 } } },
            new() { Data = new() { Mouse = new() { Flags = 0x0004 } } }
        ];
        uint inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        int clickError = inserted != inputs.Length ? Marshal.GetLastWin32Error() : 0;
        if (inserted == 1)
        {
            SendInput(1, [new() { Data = new() { Mouse = new() { Flags = 0x0004 } } }], Marshal.SizeOf<NativeInput>());
        }

        if (inserted != inputs.Length)
        {
            throw InputError("click for " + action.Step, clickError, inserted, (uint)inputs.Length);
        }

        if (action.Step == NavigationStep.SearchSaveTheWorld)
        {
            if (!DiscoveryNavigation.IsSearchAction(action))
            {
                throw new InvalidOperationException("Search typing is not allowed on this screen.");
            }

            await Task.Delay(100, cancellation);
            if (!sessionIsCurrent())
            {
                return false;
            }

            ValidateWindow(game, action);
            foreach (int held in new[] { 0x10, 0x11, 0x12, 0x41, 0x0D })
            {
                if ((GetAsyncKeyState(held) & 0x8000) != 0)
                {
                    throw new UserInteractionException("Keyboard is in use. Search typing cancelled.");
                }
            }

            SendKeyboard([Key(0x11), Key(0x41), Key(0x41, 2), Key(0x11, 2)]);
            foreach (char character in DiscoveryNavigation.Query)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!sessionIsCurrent())
                {
                    return false;
                }

                ValidateWindow(game, action);
                SendKeyboard([Unicode(character, 4), Unicode(character, 6)]);
                await Task.Delay(8, cancellation);
            }
            if (!sessionIsCurrent())
            {
                return false;
            }

            ValidateWindow(game, action);
            SendKeyboard([Key(0x0D), Key(0x0D, 2)]);
        }
        return true;
    }

    private static NativeInput Key(ushort key, uint flags = 0) => new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = flags } } };
    private static NativeInput Unicode(char value, uint flags) => new() { Type = 1, Data = new() { Keyboard = new() { Scan = value, Flags = flags } } };
    private static void SendKeyboard(NativeInput[] inputs)
    {
        uint inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (inserted == inputs.Length)
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        var releases = inputs.Where(i => (i.Data.Keyboard.Flags & 2) != 0).ToArray();
        SendInput((uint)releases.Length, releases, Marshal.SizeOf<NativeInput>());
        throw InputError("search keyboard input", error, inserted, (uint)inputs.Length);
    }

    private static Win32Exception InputError(string operation, int error, uint inserted, uint expected) =>
        new(error, $"SendInput ({operation}) accepted {inserted}/{expected} events; native error {error}: {new Win32Exception(error).Message}. INPUT size {Marshal.SizeOf<NativeInput>()}, process {(Environment.Is64BitProcess ? "x64" : "x86")}.");

    private static void SendRequired(NativeInput[] inputs, string operation)
    {
        uint inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (inserted != inputs.Length)
            throw InputError(operation, Marshal.GetLastWin32Error(), inserted, (uint)inputs.Length);
    }

    private static void ValidateWindow(GameWindow game, ClickAction action)
    {
        var current = GameWindow.Find();
        if (current != game || !game.IsForeground || game.ClientBounds.Width != action.FrameWidth
            || game.ClientBounds.Height != action.FrameHeight)
        {
            throw new GameObservationChangedException("Game focus or window layout changed. No click or keypress sent.");
        }
    }

    internal static bool ClearClickedControl(GameWindow game, ClickAction action, Box? destination, Func<bool> sessionIsCurrent)
    {

        if (action.Step is not (NavigationStep.SelectHomebase or NavigationStep.ChangeZone or NavigationStep.DisableAssistance or NavigationStep.EnableAssistance) || destination is not { } target
                    || !sessionIsCurrent() || !AutomationController.IsValidTarget(target, action.FrameWidth, action.FrameHeight))
        {
            return false;
        }

        if (NavigationPlanner.PointerRestingPoint(target, action.FrameWidth, action.FrameHeight) is not { } resting)
        {
            return false;
        }

        ValidateWindow(game, action);
        int oldX = game.ClientBounds.Left + (int)Math.Round(action.Target.CenterX);
        int oldY = game.ClientBounds.Top + (int)Math.Round(action.Target.CenterY);
        if (!GetCursorPos(out var cursor) || Math.Abs(cursor.X - oldX) > 2 || Math.Abs(cursor.Y - oldY) > 2
            || (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
        {
            return false;
        }

        var point = new NativePoint
        {
            X = game.ClientBounds.Left + (int)Math.Round(resting.CenterX),
            Y = game.ClientBounds.Top + (int)Math.Round(resting.CenterY)
        };
        if (GetAncestor(WindowFromPoint(point), 2) != game.Handle)
        {
            return false;
        }

        int desktopX = GetSystemMetrics(76), desktopY = GetSystemMetrics(77);
        int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        if (width <= 1 || height <= 1 || !sessionIsCurrent())
        {
            return false;
        }

        NativeInput move = new()
        {
            Data = new()
            {
                Mouse = new()
                {
                    X = (int)Math.Round((point.X - desktopX) * 65535d / (width - 1)),
                    Y = (int)Math.Round((point.Y - desktopY) * 65535d / (height - 1)),
                    Flags = 0x0001 | 0x8000 | 0x4000
                }
            }
        };
        SendRequired([move], "clear pointer after " + action.Step);
        return true;
    }

    internal static bool TargetStillMatches(Bitmap before, Bitmap after, Box target)
    {
        if (before.Size != after.Size || !AutomationController.IsValidTarget(target, before.Width, before.Height))
        {
            return false;
        }

        var bounds = Rectangle.Intersect(new Rectangle(0, 0, before.Width, before.Height),
            Rectangle.FromLTRB((int)target.X - 3, (int)target.Y - 3, (int)Math.Ceiling(target.Right) + 3, (int)Math.Ceiling(target.Bottom) + 3));
        double difference = 0;
        int changed = 0, samples = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y += 2)
        {
            for (int x = bounds.Left; x < bounds.Right; x += 2)
            {
                var a = before.GetPixel(x, y);
                var b = after.GetPixel(x, y);
                double delta = (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3d;
                difference += delta;
                if (delta > 40)
                {
                    changed++;
                }

                samples++;
            }
        }

        return samples > 0 && difference / samples <= 14 && changed / (double)samples <= .15;
    }

    internal static bool UnlockTargetStillPresent(Observation current, ClickAction action)
    {
        if (action.Step != NavigationStep.ContinueUnlock || action.SourceScreen != ScreenKind.NewUnlock
            || current.Width != action.FrameWidth || current.Height != action.FrameHeight)
        {
            return false;
        }

        var evidence = new ScreenRecognizer().Recognize(current);
        var plan = NavigationPlanner.Decide(current, evidence, new()).Plan;
        return plan?.Step == NavigationStep.ContinueUnlock
            && plan.Target.IntersectionOverUnion(action.Target) > .5;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type; public NativeUnion Data;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct NativeUnion
    {
        [FieldOffset(0)] public NativeMouse Mouse;
        [FieldOffset(0)] public NativeKeyboard Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboard
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouse
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
}
