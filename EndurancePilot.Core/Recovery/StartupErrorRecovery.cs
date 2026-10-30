using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

public sealed class StartupErrorRecovery
{
    private DateTimeOffset first, last;
    private int processId, hits;
    private readonly Queue<DateTimeOffset> restarts = new();
    public bool Exhausted { get; private set; }

    public void InvalidateObservation() => hits = 0;

    public static bool Matches(Observation frame)
    {
        // Require the complete, specific startup error and its dialog controls.
        // Wrapped OCR lines are allowed; unrelated ERROR dialogs must never restart the game.
        return frame.Has(@"^ERROR$") && frame.Has(@"^CONFIRM$")
            && frame.Regions.Where(r => !r.Pass.EndsWith("-word")).GroupBy(r => r.Pass).Any(pass =>
            {
                string text = Regex.Replace(string.Join(" ", pass.OrderBy(r => r.Bounds.Y).ThenBy(r => r.Bounds.X).Select(r => r.Normalized)), @"\s+", " ");
                return Regex.IsMatch(text, @"FORTNITE WAS NOT STARTED CORRECTLY AND NEEDS TO BE CLOSED")
                    && Regex.IsMatch(text, @"EPIC GAMES\s+LAUNCHER");
            });
    }

    public bool Observe(Observation frame, int gameProcessId)
    {
        Exhausted = false;
        if (!Matches(frame) || gameProcessId <= 0) { hits = 0; return false; }
        if (frame.CapturedAt <= last) return false;
        if (processId != gameProcessId || hits == 0 || frame.CapturedAt - last > TimeSpan.FromSeconds(10))
        {
            first = frame.CapturedAt; hits = 0; processId = gameProcessId;
        }
        last = frame.CapturedAt;
        if (++hits < 2 || last - first < TimeSpan.FromSeconds(1)) return false;
        while (restarts.TryPeek(out var time) && last - time > TimeSpan.FromMinutes(30)) restarts.Dequeue();
        Exhausted = restarts.Count >= 3;
        if (Exhausted) return false;
        restarts.Enqueue(last);
        hits = 0;
        return true;
    }
}
