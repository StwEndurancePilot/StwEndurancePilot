using System.Security.Cryptography;
using System.Text.Json;

namespace EndurancePilot.App;

internal sealed class RemoteReplayJournal(string path)
{
    // Persist BEFORE any action. A crash/restart or lost acknowledgement must not replay a command.
    internal bool TryAccept(string id, long now)
    {
        var seen = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, long>>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser))!
            : new Dictionary<string, long>();
        if (seen is null) throw new InvalidDataException("Invalid replay journal.");
        seen = seen.Where(x => x.Value > now).ToDictionary(x => x.Key, x => x.Value);
        if (seen.ContainsKey(id) || seen.Count >= 1024) return false;
        seen.Add(id, now + 600_000);
        WriteProtected(path, JsonSerializer.SerializeToUtf8Bytes(seen));
        return true;
    }

    internal static void WriteProtected(string path, byte[] value)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var encrypted = ProtectedData.Protect(value, null, DataProtectionScope.CurrentUser);
                stream.Write(encrypted);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(value); if (File.Exists(temp)) File.Delete(temp); }
    }
}
