using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace EndurancePilot.App;

internal static class PennyAccountImport
{
    internal static string SourcePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "penny-launcher-data", "accounts.json");
    internal static string Import(EpicAccounts accounts, string? source = null, string? localState = null)
    {
        source ??= SourcePath;
        if (!File.Exists(source)) return "Penny Launcher accounts.json was not found in Roaming AppData.";
        try
        {
            using var stream = File.OpenRead(source);
            if (stream.Length > 2_000_000) throw new InvalidDataException();
            using var data = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
            if (data.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
            var candidates = new List<EpicAccount>();
            int invalid = 0;
            byte[]? key = null;
            try
            {
            foreach (var item in data.RootElement.EnumerateArray())
            {
                string Read(string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                string id = Read("accountId"), device = Read("deviceId"), secret = Read("secret"), name = Read("displayName").Trim();
                try
                {
                    string Decode(string value)
                    {
                        if (!value.StartsWith("enc:v1:", StringComparison.Ordinal)) return value;
                        key ??= ReadKey(localState ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Penny", "Local State"));
                        byte[] payload = Convert.FromBase64String(value[7..]);
                        if (payload.Length < 31 || Encoding.ASCII.GetString(payload, 0, 3) != "v10") throw new CryptographicException();
                        byte[] clear = new byte[payload.Length - 31];
                        try
                        {
                            using var aes = new AesGcm(key, 16);
                            aes.Decrypt(payload.AsSpan(3, 12), payload.AsSpan(15, clear.Length), payload.AsSpan(payload.Length - 16), clear);
                            return Encoding.UTF8.GetString(clear);
                        }
                        finally { CryptographicOperations.ZeroMemory(clear); }
                    }
                    device = Decode(device); secret = Decode(secret);
                }
                catch (Exception error) when (error is CryptographicException or FormatException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
                { invalid++; continue; }
                bool Hex(string value) => Regex.IsMatch(value, @"\A[a-fA-F0-9]{32}\z");
                if (!Hex(id) || !Hex(device) || !Regex.IsMatch(secret, @"\A[a-zA-Z0-9]{32}\z") || name.Length is < 1 or > 100 || name.Any(char.IsControl)) { invalid++; continue; }
                candidates.Add(new(id, name, device, secret));
            }
            }
            finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
            var result = accounts.Import(candidates);
            return $"Imported {result.Imported} account(s). Skipped {result.Skipped} already saved and {invalid} invalid entry/entries.";
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // Never surface JSON snippets, account identifiers or credentials in errors.
            throw new InvalidOperationException("Penny accounts could not be imported. Check that the file is readable and contains valid account data.");
        }
    }

    private static byte[] ReadKey(string path)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(path));
        byte[] envelope = Convert.FromBase64String(data.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!);
        if (envelope.Length < 6 || Encoding.ASCII.GetString(envelope, 0, 5) != "DPAPI") throw new CryptographicException();
        return ProtectedData.Unprotect(envelope[5..], null, DataProtectionScope.CurrentUser);
    }

    internal static void ImportOnStartup(EpicAccounts accounts, bool enabled)
    {
        if (!enabled) return;
        try
        {
            Import(accounts);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        { /* Leave it retryable; manual import displays a safe error in Manage Accounts. */ }
    }
}
