using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndurancePilot.App;

internal sealed record EpicAccount(string AccountId, string DisplayName, string DeviceId, string Secret)
{
    public override string ToString() => "Epic account (credentials redacted)";
}

internal sealed class EpicAccounts
{
    private sealed record SavedAccount(string AccountId, string DisplayName, string DeviceId, string Secret);
    private sealed record SavedAccounts(string? SelectedAccountId, List<SavedAccount> Accounts);

    private readonly string path;
    private readonly List<EpicAccount> accounts = [];
    public string? SelectedAccountId { get; private set; }
    public IReadOnlyList<EpicAccount> All => accounts;
    public EpicAccount? Selected => accounts.FirstOrDefault(a => a.AccountId == SelectedAccountId);

    public EpicAccounts(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "accounts.json");
        if (!File.Exists(this.path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedAccounts>(File.ReadAllText(this.path));
            if (saved is null) return;
            foreach (var item in saved.Accounts)
            {
                accounts.Add(new(item.AccountId, item.DisplayName, Unprotect(item.DeviceId), Unprotect(item.Secret)));
            }
            SelectedAccountId = accounts.Any(a => a.AccountId == saved.SelectedAccountId)
                ? saved.SelectedAccountId : accounts.FirstOrDefault()?.AccountId;
        }
        catch (Exception error) when (error is IOException or JsonException or CryptographicException or FormatException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("Saved Epic accounts could not be read. The account file was left unchanged.", error);
        }
    }

    public void Add(EpicAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.AccountId) || string.IsNullOrWhiteSpace(account.DeviceId) || string.IsNullOrWhiteSpace(account.Secret))
            throw new InvalidDataException("Epic returned incomplete account credentials.");
        int existing = accounts.FindIndex(a => a.AccountId == account.AccountId);
        if (existing < 0) accounts.Add(account);
        else accounts[existing] = account;
        SelectedAccountId = account.AccountId;
        Save();
    }

    public void Select(string accountId)
    {
        if (!accounts.Any(a => a.AccountId == accountId)) throw new InvalidDataException("Choose a saved account.");
        SelectedAccountId = accountId;
        Save();
    }

    public void Remove(string accountId)
    {
        accounts.RemoveAll(a => a.AccountId == accountId);
        if (SelectedAccountId == accountId) SelectedAccountId = accounts.FirstOrDefault()?.AccountId;
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var saved = new SavedAccounts(SelectedAccountId, accounts.Select(a =>
            new SavedAccount(a.AccountId, a.DisplayName, Protect(a.DeviceId), Protect(a.Secret))).ToList());
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(saved));
        File.Move(temporary, path, true);
    }

    private static string Protect(string value) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string value) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
}
