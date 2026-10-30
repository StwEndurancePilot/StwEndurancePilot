using EndurancePilot.Core;

internal static class GameLogScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        string Line(int seconds, string message) => $"[{epoch.AddSeconds(seconds):yyyy.MM.dd-HH.mm.ss:fff}][123]{message}";
        string Wave(int seconds, int wave, string state = "FortGameStateOutpost") => Line(seconds,
            $"LogHealthSnapshot: ======= Snapshot: Start of Wave {wave} ({state}, Difficulty 63.70) =======");
        var progress = new GameLogProgress(epoch);
        var now = epoch.AddHours(2);
        progress.Observe(Wave(-1, 30), now);
        check("A prior process's wave cannot seed progress", progress.Latest is null);
        foreach (int wave in new[] { 0, 31, 99 }) progress.Observe(Wave(1, wave), now);
        progress.Observe(Wave(1, 16, "FortGameStateAthena"), now);
        progress.Observe(Line(1, "LogSubtitlesAndClosedCaptions: Wave 16 incoming!"), now);
        check("Only bounded Storm Shield snapshot events report a wave", progress.Latest is null);
        progress.Observe(Wave(2, 16), now);
        check("Exact game-log wave starts are read without an image", progress.Latest is { Wave: 16 } && progress.Latest.StartedAt == epoch.AddSeconds(2));
        progress.Observe(Wave(3, 16), now);
        check("Duplicate wave reports do not restart the elapsed clock", progress.Latest?.StartedAt == epoch.AddSeconds(2));
        progress.Observe(Wave(4, 17), now);
        check("A later game-log wave advances minimized status", progress.Latest?.Wave == 17);
        progress.Observe(Wave(3, 18), now);
        progress.Observe(Wave(5, 15), now);
        check("Older or regressing events do not replace progress", progress.Latest?.Wave == 17);
        progress.Observe(Wave(9000, 30), now);
        check("Future log events are rejected", progress.Latest?.Wave == 17);
        progress.Observe(Line(6, "LogLoad: LoadMap: /Game/Maps/Frontend"), now);
        check("Map travel clears the prior mission's wave", progress.Latest is null);
        progress.Observe(Wave(5, 30), now);
        check("Old wave evidence cannot cross a map transition", progress.Latest is null);
        progress.Observe(Wave(7, 1), now);
        check("A new run can report wave one", progress.Latest?.Wave == 1);
        progress.Observe(Wave(8, 30), now);
        check("Reported wave thirty is a wave, not a fabricated victory", progress.Latest?.Wave == 30);
        progress.Observe(Line(9, "LogWorld: BeginTearingDown for /Game/Outpost"), now);
        check("World teardown clears log progress", progress.Latest is null);
        progress.Observe(Wave(10, 16) + new string('x', 4096), now);
        progress.Observe("[broken][123]LogHealthSnapshot: Start of Wave 16", now);
        check("Malformed and oversized records stay inert", progress.Latest is null);
    }
}
