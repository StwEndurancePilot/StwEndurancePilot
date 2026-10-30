using System.Diagnostics;
using System.Text;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class FortniteLogMonitor(string? path = null)
{
    private readonly string path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FortniteGame", "Saved", "Logs", "FortniteGame.log");
    private (int Id, DateTimeOffset Started)? owner;
    private DateTime creation;
    private long position;
    private readonly StringBuilder pending = new();
    private bool skipLine;
    private GameLogProgress? progress;
    private DateTimeOffset nextPoll;
    internal GameLogWave? Latest { get; private set; }
    internal GameLogPlaytime? Playtime { get; private set; }
    internal DateTimeOffset? ResultAvailableAt { get; private set; }

    internal void Reset()
    {
        owner = null;
        progress = null;
        Latest = null;
        Playtime = null;
        ResultAvailableAt = null;
        position = 0;
        pending.Clear();
        skipLine = false;
    }

    internal void Poll(DateTimeOffset now)
    {
        if (now < nextPoll) return;
        nextPoll = now.AddMilliseconds(500);
        var processes = Process.GetProcessesByName("FortniteClient-Win64-Shipping");
        try
        {
            if (processes.Length != 1) { Reset(); return; }
            var process = processes[0];
            Poll(process.Id, process.StartTime.ToUniversalTime(), now);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Reset();
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal void Poll(int processId, DateTimeOffset started, DateTimeOffset now)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) { Reset(); return; }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (owner != (processId, started) || creation != file.CreationTimeUtc || stream.Length < position)
            {
                Reset();
                owner = (processId, started);
                creation = file.CreationTimeUtc;
                progress = new(started);
                // Bootstrap from a bounded recent tail, never archives from an old game.
                position = Math.Max(0, stream.Length - 2 * 1024 * 1024);
                skipLine = position > 0;
            }
            stream.Position = position;
            byte[] buffer = new byte[16384];
            long remaining = Math.Min(stream.Length - position, 2 * 1024 * 1024);
            while (remaining > 0)
            {
                int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0) break;
                position += count;
                remaining -= count;
                // Relevant records are ASCII. Only retain a bounded partial line;
                // never forward raw game logs or unrelated fields to Pilot's log.
                for (int i = 0; i < count; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\n')
                    {
                        if (!skipLine) progress!.Observe(pending.ToString().TrimEnd('\r'), now);
                        pending.Clear();
                        skipLine = false;
                    }
                    else if (!skipLine)
                    {
                        if (pending.Length < 4096) pending.Append(c);
                        else { pending.Clear(); skipLine = true; }
                    }
                }
            }
            Latest = progress?.Latest;
            Playtime = progress?.Playtime;
            ResultAvailableAt = progress?.ResultAvailableAt;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Reset();
        }
    }
}
