using System.Text.RegularExpressions;

namespace EndurancePilot.App;

internal static class RemotePairingCode
{
    // The public Discord ID routes the request; the random secret authorizes it.
    // Neither endpoints nor application verification keys come from this input.
    internal static bool TryParse(string value, out string user, out string secret)
    {
        user = secret = "";
        if (value.Length > 128) return false;
        var match = Regex.Match(value.Trim(), @"\AEP1\.([0-9]{17,20})\.([a-fA-F0-9]{32})\z");
        if (!match.Success) return false;
        user = match.Groups[1].Value;
        secret = match.Groups[2].Value.ToLowerInvariant();
        return true;
    }
}
