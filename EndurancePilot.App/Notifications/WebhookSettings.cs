using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EndurancePilot.App;

[Flags]
internal enum WebhookEvent
{
    RunnerStarted = 1, RunnerStopped = 2, EnduranceStarted = 4, EnduranceEnded = 8,
    TargetReached = 16, Completed30 = 32, Crash = 64, Error = 128, Stuck = 256,
    Launch = 512, Recovery = 1024,

    All = 2047, WaveReached = 2048
}

internal sealed record WebhookSettings(bool Enabled = false, string ProtectedUrl = "", WebhookEvent Events = WebhookEvent.All,
    int WaveNotificationFrequency = 5)
{
    public void Validate()
    {
        if (WaveNotificationFrequency is < 1 or > 30)
        {
            throw new InvalidDataException("Choose a wave notification frequency from 1 to 30.");
        }
    }
    public string ReadUrl() => string.IsNullOrEmpty(ProtectedUrl) ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(ProtectedUrl), null, DataProtectionScope.CurrentUser));

    public WebhookSettings WithUrl(string url)
    {
        url = url.Trim();
        if (url.Length > 0)
        {
            _ = Endpoint(url);
        }

        return this with
        {
            ProtectedUrl = url.Length == 0 ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(url), null, DataProtectionScope.CurrentUser))
        };
    }

    public static Uri Endpoint(string url)
    {
        const string message = "Paste a Discord webhook URL (https://discord.com/api/webhooks/…).";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0
            || uri.Host is not ("discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com")
            || !Regex.IsMatch(uri.AbsolutePath, @"^/api/(?:v\d+/)?webhooks/[0-9]+/[A-Za-z0-9_-]+/?$"))
        {
            throw new InvalidDataException(message);
        }

        string? thread = null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "thread_id" && Regex.IsMatch(pair[1], @"^\d+$"))
            {
                thread = pair[1];
            }
            else if (pair.Length != 2 || pair[0] != "wait" || pair[1] is not ("true" or "false"))
            {
                throw new InvalidDataException(message);
            }
        }
        return new UriBuilder(uri) { Query = "wait=true" + (thread is null ? "" : "&thread_id=" + thread) }.Uri;
    }
}
