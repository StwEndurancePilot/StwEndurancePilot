namespace EndurancePilot.App;

public sealed partial class MainForm
{
    internal async Task<RemoteResult> RemoteReadAsync(string command, CancellationToken token)
    {
        var snapshot = Snapshot();
        if (command == "progress") return new($"Runner: {(snapshot.Running ? "running" : "stopped")}\nNeeds attention: {snapshot.Attention}\nWave: {snapshot.Wave?.ToString() ?? "—"}\nCompleted this session: {snapshot.Completed}");
        if (command == "statistics")
        {
            var text = $"Completed this session: {snapshot.Completed}\nLifetime completed runs: {Earnings.LifetimeCompletedRuns}\n" +
                string.Join("\n", Earnings.Lifetime.Select(r => $"{r.Name}: {r.QuantityLabel}"));
            return new(text.Length > 1800 ? text[..1800] : text);
        }
        if (command == "screenshot")
        {
            var game = GameWindow.Find();
            if (game is null) return new("No visible Fortnite window was found.");
            // Windows Graphics Capture targets the game HWND; never fall back to desktop capture.
            using var capture = new BackgroundGameCapture(game);
            using var frame = await capture.CaptureAsync(token);
            var bytes = frame is null ? null : NotificationScreenshot.Encode(frame);
            return bytes is null ? new("Fortnite capture is unavailable.") : new("Current Fortnite window. Player names or chat may be visible.", Convert.ToBase64String(bytes));
        }
        throw new InvalidOperationException();
    }
}
