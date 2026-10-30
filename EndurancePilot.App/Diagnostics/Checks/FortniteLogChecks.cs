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
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        return checks;
    }
}
