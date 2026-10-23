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
