namespace EndurancePilot.App;

internal static class KeyPressChecks
{
    public static async Task<IReadOnlyList<StartupCheck>> RunAsync()
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed (no real input)" : "Failed"));
        var events = new List<string>();
        var sent = await KeyPress.SendAsync(() => events.Add("down"), () => events.Add("up"), () => true, CancellationToken.None,
            (duration, _) => { events.Add("hold:" + duration.TotalMilliseconds); return Task.CompletedTask; });
        Check("Keypress spans an 80ms hold before release", sent && events.SequenceEqual(new[] { "down", "hold:80", "up" }));
        events.Clear();
        sent = await KeyPress.SendAsync(() => events.Add("down"), () => events.Add("up"), () => false, CancellationToken.None);
        Check("Stale input session cannot press a key", !sent && events.Count == 0);
        events.Clear();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await KeyPress.SendAsync(() => events.Add("down"), () => events.Add("up"), () => true, cancelled.Token);
        }
        catch (OperationCanceledException) { }
        Check("Pre-cancelled input cannot press a key", events.Count == 0);
        events.Clear();
        bool cancellationObserved = false;
        using var duringHold = new CancellationTokenSource();
        try
        {
            await KeyPress.SendAsync(() => events.Add("down"), () => events.Add("up"), () => true, duringHold.Token,
                (_, token) => { duringHold.Cancel(); return Task.FromCanceled(token); });
        }
        catch (OperationCanceledException) { cancellationObserved = true; }
        Check("Cancellation during hold releases the key", cancellationObserved && events.SequenceEqual(new[] { "down", "up" }));
        events.Clear();
        bool failed = false;
        try
        {
            await KeyPress.SendAsync(() => events.Add("down"), () => events.Add("up"), () => true, CancellationToken.None,
                (_, _) => Task.FromException(new IOException("Simulated timer failure")));
        }
        catch (IOException) { failed = true; }
        Check("Hold failure releases the key", failed && events.SequenceEqual(new[] { "down", "up" }));
        events.Clear();
        bool focused = true;
        sent = await KeyPress.SendAsync(() => events.Add("down"), () => events.Add("up"), () => focused, CancellationToken.None,
            (_, _) => { focused = false; return Task.CompletedTask; });
        Check("Focus lost during hold still releases without acknowledging success", !sent && events.SequenceEqual(new[] { "down", "up" }));
        return checks;
    }
}
