using EndurancePilot.Core;

namespace EndurancePilot.App;

public sealed partial class MainForm
{
    internal async Task<RemoteResult> RemoteReadAsync(string command, CancellationToken token)
    {
        var snapshot = Snapshot();
        if (command == "progress")
        {
            string wave = "Not visible";
            var game = GameWindow.Find();
            if (game is not null)
            {
                // Read-only, fresh HWND captures. Never focus the game or advance automation
                // just to answer a status request; do not reuse a previous run's highest wave.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    using var capture = new BackgroundGameCapture(game);
                    var reader = new OcrService();
                    string? first = null;
                    for (int i = 0; i < 2; i++)
                    {
                        using var frame = await capture.CaptureAsync(timeout.Token);
                        if (frame is null) break;
                        var observation = await reader.ReadAsync(frame, timeout.Token);
                        string? reading = RemoteWave(observation, recognizer.Recognize(observation).Kind);
                        if (i == 1 && reading is not null && reading == first) wave = reading;
                        first = reading;
                        if (i == 0) await Task.Delay(500, timeout.Token);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                catch (Exception) when (!token.IsCancellationRequested) { }
            }
            token.ThrowIfCancellationRequested();
            snapshot = Snapshot();
            var now = DateTimeOffset.UtcNow;
            if (game is null) wave = "Fortnite is closed, minimized or unavailable";
            else if (wave == "Not visible" && tracker.Current is { Outcome: RunOutcome.InProgress } current
                && now - tracker.LastConfirmedWaveAt < TimeSpan.FromMinutes(3))
                wave = $"Last confirmed: {current.HighestWave} / 30 ({Math.Max(0, (int)(now - tracker.LastConfirmedWaveAt).TotalSeconds)}s ago)";
            var elapsed = runner.StartedAt is { } started ? (stoppedAt ?? now) - started : TimeSpan.Zero;
            string account = EpicLauncher.ProgressAccount;
            string text = $"Account: {account}\nStatus: {(snapshot.Attention ? "Error · runner paused" : snapshot.Paused ? "Paused · monitoring only" : snapshot.Background ? "Monitoring in background" : snapshot.Running ? "Running" : "Stopped")}\nWave: {wave}\nSession time: {(int)Math.Max(0, elapsed.TotalHours)}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s\nCompleted runs: {snapshot.Completed}";
            if (snapshot.Attention) text += "\nError: Pilot needs a local check. Open the app for the error details.";
            return new(text);
        }
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

    internal static string? RemoteWave(Observation frame, ScreenKind kind)
    {
        bool countdown = kind == ScreenKind.EnduranceCountdown;
        if (!countdown && kind != ScreenKind.ActiveEndurance) return null;
        var values = (countdown ? WaveReader.ReadCountdown(frame) : WaveReader.Read(frame)).Select(x => x.Wave).Distinct().ToArray();
        return values.Length == 1 ? countdown ? $"Wave {values[0]} starts next" : $"{values[0]} / 30" : null;
    }
}
