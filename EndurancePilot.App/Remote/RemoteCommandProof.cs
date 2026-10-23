using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace EndurancePilot.App;

internal sealed record DiscordProof(string Body, string Signature, string Timestamp)
{
    public override string ToString() => "Discord proof (interaction redacted)";
}

internal static class RemoteCommandProof
{
    internal static void ValidateIdentity(string application, string key)
    {
        if (!Regex.IsMatch(application ?? "", "\\A[0-9]{17,20}\\z") || !Regex.IsMatch(key ?? "", "\\A[a-fA-F0-9]{64}\\z"))
            throw new InvalidDataException("Enter the Discord application ID and public verification key from a trusted publisher.");
    }

    internal static bool Verify(RemoteCommand command, RemoteConfiguration configuration, long now)
    {
        try
        {
            var proof = command.Proof;
            if (proof is null || proof.Body is null || Encoding.UTF8.GetByteCount(proof.Body) > 32768 ||
                !Regex.IsMatch(proof.Signature ?? "", "\\A[a-fA-F0-9]{128}\\z") ||
                !Regex.IsMatch(proof.Timestamp ?? "", "\\A[0-9]{10}\\z") ||
                !Regex.IsMatch(command.Id ?? "", "\\A[0-9]{17,20}\\z")) return false;
            long issued = long.Parse(proof.Timestamp!) * 1000;
            if (issued < configuration.PairedAt / 1000 * 1000 || issued > now + 5000 || now - issued > 90_000 ||
                command.Expires <= now || command.Expires > issued + 90_000) return false;
            byte[] message = Encoding.UTF8.GetBytes(proof.Timestamp + proof.Body);
            if (!Ed25519.Verify(Convert.FromHexString(proof.Signature!), 0, Convert.FromHexString(configuration.DiscordPublicKey), 0, message, 0, message.Length)) return false;
            using var document = JsonDocument.Parse(proof.Body);
            var i = document.RootElement;
            string? user = i.TryGetProperty("member", out var member) ? member.GetProperty("user").GetProperty("id").GetString() : i.GetProperty("user").GetProperty("id").GetString();
            if (i.GetProperty("application_id").GetString() != configuration.ApplicationId || user != configuration.UserId ||
                i.GetProperty("id").GetString() != command.Id || i.GetProperty("type").GetInt32() != 2) return false;
            var data = i.GetProperty("data");
            if (data.GetProperty("name").GetString() != "pilot") return false;
            var options = data.GetProperty("options");
            if (options.GetArrayLength() != 1) return false;
            var option = options[0];
            if (option.GetProperty("name").GetString() != command.Command || option.GetProperty("type").GetInt32() != 1) return false;
            int? account = null;
            if (command.Command == "switch")
            {
                var arguments = option.GetProperty("options");
                if (arguments.GetArrayLength() != 1 || arguments[0].GetProperty("name").GetString() != "account" || arguments[0].GetProperty("type").GetInt32() != 4) return false;
                account = arguments[0].GetProperty("value").GetInt32();
                if (account is < 1 or > 100) return false;
            }
            else if (option.TryGetProperty("options", out var arguments) && arguments.GetArrayLength() != 0) return false;
            return account == command.Account;
        }
        catch { return false; }
    }
}
