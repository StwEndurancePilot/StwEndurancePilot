namespace EndurancePilot.App;

internal static class FortniteLogChecks
{
    internal static IReadOnlyList<StartupCheck> Run(string directory)
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool pass) => checks.Add(new("Game log: " + name, pass, pass ? "Passed (synthetic local log)" : "Failed"));
        string path = Path.Combine(directory, "game-log-test-" + Guid.NewGuid().ToString("N") + ".log");
        var epoch = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var now = epoch.AddHours(1);
        string Wave(int seconds, int wave) => $"[{epoch.AddSeconds(seconds):yyyy.MM.dd-HH.mm.ss:fff}][123]LogHealthSnapshot: ======= Snapshot: Start of Wave {wave} (FortGameStateOutpost, Difficulty 63.70) =======";
        try
        {
            var reader = new FortniteLogMonitor(path);
            reader.Poll(1, epoch, now);
            Check("missing files are harmless", reader.Latest is null);
            string first = Wave(1, 16);
            File.WriteAllText(path, first[..45]);
            reader.Poll(1, epoch, now);
            Check("partial records cannot establish a wave", reader.Latest is null);
            File.AppendAllText(path, first[45..] + "\r\n");
            reader.Poll(1, epoch, now);
            Check("an appended complete record is accepted", reader.Latest?.Wave == 16);
            File.AppendAllText(path, Wave(2, 17) + "\n");
            reader.Poll(1, epoch, now);
            Check("tailing advances wave status", reader.Latest?.Wave == 17);
            reader.Poll(1, epoch, now);
            Check("no new bytes preserves the event timestamp", reader.Latest?.StartedAt == epoch.AddSeconds(2));
            reader.Poll(2, epoch.AddSeconds(3), now);
            Check("new process identity rejects the previous process log", reader.Latest is null);
            File.AppendAllText(path, Wave(4, 1) + "\n");
            reader.Poll(2, epoch.AddSeconds(3), now);
            Check("new process can establish a new run", reader.Latest?.Wave == 1);
            File.WriteAllText(path, Wave(5, 2) + "\n");
            reader.Poll(2, epoch.AddSeconds(3), now);
            Check("truncation resets the file cursor", reader.Latest?.Wave == 2);
            File.AppendAllText(path, new string('x', 5000) + Wave(6, 30) + "\n" + Wave(7, 3) + "\n");
            reader.Poll(2, epoch.AddSeconds(3), now);
            Check("oversized unrelated lines do not poison later records", reader.Latest?.Wave == 3);
            reader.Reset();
            Check("Stop clears the log snapshot", reader.Latest is null);
            File.WriteAllText(path, $"[{epoch.AddSeconds(1):yyyy.MM.dd-HH.mm.ss:fff}][123]LogFortPlaytimeManager: [394e] Changing Playtime State from Idle to Sleep\n"
                + string.Concat(Enumerable.Repeat("Unrelated diagnostic line\n", 140000))
                + $"[{epoch.AddSeconds(8):yyyy.MM.dd-HH.mm.ss:fff}][123]LogFortPlaytimeManager: [394e] Changing Playtime State from Sleep to Unblocked\n"
                + Wave(9, 4) + "\n");
            reader.Poll(3, epoch, now);
            Check("Partial startup replay does not expose an obsolete sleep state", reader.CatchingUp && reader.Playtime is null && reader.Latest is null);
            reader.Poll(3, epoch, now);
            Check("A long log recovers the latest wave and wake state together", !reader.CatchingUp
                && reader.Latest?.Wave == 4 && reader.Playtime is { NeedsKeepPlaying: false });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        return checks;
    }
}
