namespace EndurancePilot.App;

internal static class DiscordCommunityChecks
{
    internal static IReadOnlyList<StartupCheck> Run(string output)
    {
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, "community-" + Guid.NewGuid().ToString("N"), "state.json");
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed" : "Failed"));
        try
        {
            string? opened = null;
            var community = new DiscordCommunity(path, url => opened = url);
            Check("First launch requires the community prompt", !community.InviteOpened);
            var dialog = new DiscordCommunityDialog(community);
            bool closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Close();
            Check("First-run dialog cannot be dismissed", !closed && !community.InviteOpened);
            community.Join();
            dialog.Close();
            Check("Opening the invite allows the dialog to close", closed);
            Check("Join opens the STW community invite", opened == DiscordCommunity.Invite);
            Check("Invite acknowledgement survives restart", new DiscordCommunity(path).InviteOpened);
            File.Delete(path);
            var failed = new DiscordCommunity(path, _ => throw new InvalidOperationException("Browser unavailable"));
            try { failed.Join(); } catch (InvalidOperationException) { }
            Check("Browser failure does not dismiss future prompts", !failed.InviteOpened && !File.Exists(path));
            Directory.CreateDirectory(path);
            var unsaved = new DiscordCommunity(path, _ => { });
            try { unsaved.Join(); } catch (InvalidOperationException) { }
            Check("Save failure keeps the prompt pending", !unsaved.InviteOpened);
            Directory.Delete(path);
            File.WriteAllText(path, "broken json");
            Check("Unreadable acknowledgement shows the prompt", !new DiscordCommunity(path).InviteOpened);
            new DiscordCommunity(path, _ => { }).Join();
            Check("Joining repairs an unreadable acknowledgement", new DiscordCommunity(path).InviteOpened);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path);
            Directory.Delete(Path.GetDirectoryName(path)!);
        }
        return checks;
    }
}
