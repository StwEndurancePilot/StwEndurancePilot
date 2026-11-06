using System.Diagnostics;
using System.Text.Json;

namespace EndurancePilot.App;

internal sealed class DiscordCommunity
{
    internal const string Invite = "https://discord.gg/j3U6mp7p6";
    private readonly string path;
    private readonly Action<string> open;
    internal bool InviteOpened { get; private set; }

    internal DiscordCommunity(string? path = null, Action<string>? open = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "discord-community.json");
        this.open = open ?? (url => { using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); });
        try { InviteOpened = JsonSerializer.Deserialize<State>(File.ReadAllText(this.path))?.InviteOpened == true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    internal void Join()
    {
        try { open(Invite); }
        catch (Exception error) { throw new InvalidOperationException("Discord could not be opened. Check your default browser and try again.", error); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new State(true)));
            File.Move(temporary, path, overwrite: true);
            InviteOpened = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Discord opened, but your preference could not be saved. Try again to keep this prompt dismissed.", error);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record State(bool InviteOpened);
}
