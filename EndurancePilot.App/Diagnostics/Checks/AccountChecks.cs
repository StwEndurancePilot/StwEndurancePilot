namespace EndurancePilot.App;

internal static class AccountChecks
{
    public static IReadOnlyList<StartupCheck> Run(string output)
    {
        string path = Path.Combine(output, "accounts-test-" + Guid.NewGuid().ToString("N") + ".json");
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed" : "Failed"));
        try
        {
            var store = new EpicAccounts(path);
            store.Add(new("first-id", "First", "first-device", "first-secret"));
            store.Add(new("second-id", "Second", "second-device", "second-secret"));
            string raw = File.ReadAllText(path);
            Check("Epic device credentials are encrypted at rest", !raw.Contains("first-secret") && !raw.Contains("second-device"));
            store.Select("first-id");
            var restored = new EpicAccounts(path);
            Check("Selected Epic account survives restart", restored.Selected is { AccountId: "first-id", Secret: "first-secret" });
            restored.Remove("first-id");
            Check("Removing the selected account chooses the next account", new EpicAccounts(path).Selected?.AccountId == "second-id");
            string source = path + ".penny";
            try
            {
                string id = new('a', 32), device = new('b', 32), secret = new('c', 32);
                File.WriteAllText(source, System.Text.Json.JsonSerializer.Serialize(new[] {
                    new { accountId = id, displayName = "Imported", deviceId = device, secret },
                    new { accountId = new string('d', 32), displayName = " imported ", deviceId = device, secret },
                    new { accountId = new string('e', 32), displayName = "SECOND", deviceId = device, secret },
                    new { accountId = "invalid", displayName = "Bad", deviceId = device, secret }
                }));
                PennyAccountImport.Import(restored, source);
                Check("Penny import skips duplicate usernames case-insensitively and invalid entries", restored.All.Count == 2);
                Check("Penny import preserves selected account", restored.SelectedAccountId == "second-id");
                PennyAccountImport.Import(restored, source);
                Check("Repeated Penny import is idempotent", restored.All.Count == 2);
                Check("Imported credentials are encrypted at rest", !File.ReadAllText(path).Contains(secret) && !File.ReadAllText(path).Contains(device));
                byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                string state = source + ".state";
                try
                {
                    string Encrypt(string value)
                    {
                        byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12), ciphertext = new byte[32], tag = new byte[16];
                        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
                        aes.Encrypt(nonce, System.Text.Encoding.UTF8.GetBytes(value), ciphertext, tag);
                        return "enc:v1:" + Convert.ToBase64String([.. System.Text.Encoding.ASCII.GetBytes("v10"), .. nonce, .. ciphertext, .. tag]);
                    }
                    byte[] wrapped = System.Security.Cryptography.ProtectedData.Protect(key, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    File.WriteAllText(state, System.Text.Json.JsonSerializer.Serialize(new { os_crypt = new { encrypted_key = Convert.ToBase64String([.. System.Text.Encoding.ASCII.GetBytes("DPAPI"), .. wrapped]) } }));
                    File.WriteAllText(source, System.Text.Json.JsonSerializer.Serialize(new[] { new { accountId = new string('f', 32), displayName = "Encrypted Penny", deviceId = Encrypt(device), secret = Encrypt(secret) } }));
                    PennyAccountImport.Import(restored, source, state);
                    Check("Penny encrypted v10 credentials decrypt and persist under Pilot encryption", new EpicAccounts(path).All.Any(a => a.DisplayName == "Encrypted Penny" && a.DeviceId == device && a.Secret == secret));
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); if (File.Exists(state)) File.Delete(state); }
            }
            finally { if (File.Exists(source)) File.Delete(source); }
        }
        catch (Exception error)
        {
            checks.Add(new("Epic account storage", false, error.Message));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
        return checks;
    }
}
