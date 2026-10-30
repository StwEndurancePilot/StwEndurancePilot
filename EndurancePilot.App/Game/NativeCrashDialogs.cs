using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class NativeCrashDialogs
{

    public static IReadOnlyList<CrashDialogEvidence> Find()
    {
        var results = new List<CrashDialogEvidence>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            string title = Caption(window);
            if (title is not ("Application Hang Detected" or "Application Crash Detected"))
            {
                return true;
            }

            GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!process.ProcessName.Equals("FortniteClient-Win64-Shipping", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var buttons = new List<nint>();
                var messages = new List<string>();
                int children = 0;
                EnumChildWindows(window, (child, _) =>
                {
                    if (++children > 32)
                    {
                        return false;
                    }

                    GetWindowThreadProcessId(child, out uint childPid);
                    if (childPid != pid || !IsWindowVisible(child))
                    {
                        return true;
                    }

                    var className = new StringBuilder(128);
                    GetClassName(child, className, className.Capacity);
                    string name = className.ToString();
                    if (name is not ("Button" or "Edit" or "Static"))
                    {
                        return true;
                    }

                    string text = ControlText(child);
                    if (name == "Button" && text.Replace("&", "").Trim() == "OK" && IsWindowEnabled(child))
                    {
                        buttons.Add(child);
                    }

                    if (name is "Edit" or "Static" && text.StartsWith("The application has ", StringComparison.Ordinal))
                    {
                        messages.Add(text);
                    }

                    return true;
                }, 0);
                if (children > 32 || buttons.Count != 1 || messages.Count != 1)
                {
                    return true;
                }

                var evidence = new CrashDialogEvidence(window.ToInt64(), buttons[0].ToInt64(), (int)pid,
                    process.ProcessName, title, messages[0], true);
                if (CrashRecovery.IsKnownCrash(evidence))
                {
                    results.Add(evidence);
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        return results;
    }

    public static void Dismiss(CrashDismissRequest request, Func<bool> sessionIsCurrent)
    {
        if (!sessionIsCurrent() || DateTimeOffset.UtcNow - request.ObservedAt > TimeSpan.FromSeconds(5))
        {
            throw new InvalidOperationException("Crash recovery stopped or its detection expired.");
        }

        var fresh = Find();
        if (fresh.Count != 1 || fresh[0] != request.Dialog)
        {
            throw new InvalidOperationException("Crash dialog changed before dismissal; no action sent.");
        }

        nint window = (nint)request.Dialog.WindowId, button = (nint)request.Dialog.ButtonId;
        if (GetForegroundWindow() != window || !sessionIsCurrent()
            || DateTimeOffset.UtcNow - request.ObservedAt > TimeSpan.FromSeconds(5))
        {
            throw new InvalidOperationException("Select Fortnite's crash dialog to allow recovery.");
        }

        if (!PostMessage(button, 0x00F5, 0, 0))
        {
            throw new InvalidOperationException("Windows did not accept the crash-dialog button action. No relaunch was sent.");
        }
    }

    private static string Caption(nint window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
    private static string ControlText(nint control)
    {
        var text = new StringBuilder(512);
        return SendMessageTimeoutText(control, 0x000D, (nuint)text.Capacity, text, 2, 100, out _) == 0 ? "" : text.ToString();
    }
    private delegate bool EnumerateWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumerateWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeoutText(nint window, uint message, nuint wParam, StringBuilder lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
