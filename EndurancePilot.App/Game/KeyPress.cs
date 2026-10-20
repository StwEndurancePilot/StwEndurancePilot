namespace EndurancePilot.App;

internal static class KeyPress
{
    internal static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(80);

    internal static async Task<bool> SendAsync(Action down, Action up, Func<bool> isCurrent, CancellationToken token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        token.ThrowIfCancellationRequested();
        if (!isCurrent())
        {
            return false;
        }

        down();
        try
        {
            await (delay ?? Task.Delay)(Hold, token);
            return isCurrent();
        }
        finally { up(); }
    }
}
