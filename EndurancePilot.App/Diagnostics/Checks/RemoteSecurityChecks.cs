using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace EndurancePilot.App;

internal static class RemoteSecurityChecks
{
    private const string User = "111111111111111111", App = "333333333333333333";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<IReadOnlyList<StartupCheck>> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed" : "Failed"));
        var seed = RandomNumberGenerator.GetBytes(32);
        var publicKey = new byte[32];
        Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var config = new RemoteConfiguration("https://pilot.example", User, "", false, false, App, Convert.ToHexStringLower(publicKey), now - 1000, now + 60_000);
        RemoteCommand Command(string user = User, string application = App, string action = "progress", int? account = null, long? issued = null)
        {
            string timestamp = ((issued ?? now) / 1000).ToString();
            string id = "444444444444444444";
            object option = action == "switch"
                ? new { name = action, type = 1, options = new[] { new { name = "account", type = 4, value = account } } }
                : new { name = action, type = 1 };
            string body = JsonSerializer.Serialize(new { id, type = 2, application_id = application, token = "test-only-reply-token", user = new { id = user }, data = new { name = "pilot", options = new[] { option } } });
            var data = Encoding.UTF8.GetBytes(timestamp + body);
            var signature = new byte[64];
            Ed25519.Sign(seed, 0, data, 0, data.Length, signature, 0);
            return new(id, action, account, (issued ?? now) + 45_000, new(body, Convert.ToHexStringLower(signature), timestamp));
        }
        var command = Command();
        Check("Desktop verifies an authentic Discord command", RemoteCommandProof.Verify(command, config, now));
        Check("Desktop rejects unsigned Worker commands", !RemoteCommandProof.Verify(command with { Proof = null }, config, now));
        Check("Desktop rejects another Discord user", !RemoteCommandProof.Verify(Command(user: "222222222222222222"), config, now));
        Check("Desktop rejects another Discord application", !RemoteCommandProof.Verify(Command(application: "555555555555555555"), config, now));
        Check("Desktop rejects a substituted Discord public key", !RemoteCommandProof.Verify(command, config with { DiscordPublicKey = new string('0', 64) }, now));
        Check("Desktop rejects tampered signed bytes", !RemoteCommandProof.Verify(command with { Proof = command.Proof! with { Body = command.Proof.Body + " " } }, config, now));
        Check("Desktop rejects a substituted action", !RemoteCommandProof.Verify(command with { Command = "start" }, config, now));
        Check("Desktop rejects substituted command ID", !RemoteCommandProof.Verify(command with { Id = "666666666666666666" }, config, now));
        Check("Desktop rejects substituted account number", !RemoteCommandProof.Verify(Command(action: "switch", account: 1) with { Account = 2 }, config, now));
        Check("Desktop accepts a signed account number", RemoteCommandProof.Verify(Command(action: "switch", account: 1), config, now));
        Check("Desktop rejects expired Discord proof", !RemoteCommandProof.Verify(Command(issued: now - 91_000), config, now));
        Check("Desktop rejects proof from before this pairing", !RemoteCommandProof.Verify(Command(issued: now - 20_000), config, now));
        Check("Desktop rejects future Discord proof", !RemoteCommandProof.Verify(Command(issued: now + 10_000), config, now));
        Check("Desktop rejects extended command lifetime", !RemoteCommandProof.Verify(command with { Expires = now + 180_000 }, config, now));
        Check("Desktop rejects malformed Discord proof", !RemoteCommandProof.Verify(command with { Proof = new("{}", "bad", "0") }, config, now));

        string journalPath = Path.Combine(directory, "replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check("Desktop records a command before execution", new RemoteReplayJournal(journalPath).TryAccept(command.Id, now));
            Check("Desktop rejects replay after process journal reload", !new RemoteReplayJournal(journalPath).TryAccept(command.Id, now + 1));
            Check("Replay journal is encrypted at rest", !Encoding.UTF8.GetString(File.ReadAllBytes(journalPath)).Contains(command.Id));
            File.WriteAllText(journalPath, "invalid");
            bool rejected = false;
            try { new RemoteReplayJournal(journalPath).TryAccept("another", now); } catch { rejected = true; }
            Check("Corrupt replay journal fails closed", rejected);
        }
        finally { File.Delete(journalPath); }

        string keyName = RemoteDeviceKey.Create();
        try
        {
            using var key = RemoteDeviceKey.Open(keyName);
            bool rejected = false;
            try { key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob); } catch (CryptographicException) { rejected = true; }
            Check("Windows refuses private device key export", rejected);
            string nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            string timestamp = now.ToString();
            string raw = "{}";
            string signature = RemoteDeviceKey.Sign(keyName, config.Origin, User, "poll", raw, timestamp, nonce);
            string message = $"pilot-device-v2\nPOST\n{config.Origin}/device/{User}/poll\n{timestamp}\n{nonce}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))}";
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(RemoteDeviceKey.PublicKey(keyName)), out _);
            Check("Device signatures verify with public key only", verifier.VerifyData(Encoding.UTF8.GetBytes(message), Convert.FromHexString(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            // Public-only interoperability fixture, never contains a private key or live credential.
            File.WriteAllText(Path.Combine(directory, "cng-request.json"), JsonSerializer.Serialize(new { publicKey = RemoteDeviceKey.PublicKey(keyName), signature, nonce, timestamp, body = raw, user = User, origin = config.Origin }, Json));
        }
        finally { RemoteDeviceKey.Delete(keyName); }
        bool deleted = false;
        try { using var key = RemoteDeviceKey.Open(keyName); } catch (CryptographicException) { deleted = true; }
        Check("Disconnect key deletion prevents reuse", deleted);
        Check("Sensitive records redact string formatting", !new EpicAccount("id", "name", "private-device", "private-secret").ToString().Contains("private-secret")
            && !command.Proof!.ToString().Contains("test-only-reply-token") && !new RemoteResult("approval-link").ToString().Contains("approval-link"));
        using (var failure = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("{\"discordStatus\":403,\"error\":\"private-reply-token\"}") })
        {
            string message = await DiscordRemote.DescribeReplyAsync(failure, CancellationToken.None);
            Check("Reply diagnostics show status codes without upstream secrets", message.Contains("Worker HTTP 502") && message.Contains("Discord HTTP 403") && !message.Contains("private-reply-token"));
        }
        using (var failure = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("{\"discordStatus\":\"private-reply-token\"}") })
            Check("Reply diagnostics reject untrusted status text", !(await DiscordRemote.DescribeReplyAsync(failure, CancellationToken.None)).Contains("private-reply-token"));

        string configurationPath = Path.Combine(directory, "remote-" + Guid.NewGuid().ToString("N") + ".bin");
        var handler = new Handler(() => Command(issued: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        int executed = 0;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using (var remote = new DiscordRemote((_, _, _) => { executed++; finished.TrySetResult(); return Task.FromResult(new RemoteResult("done")); }, configurationPath, handler))
            {
                await remote.PairAsync(config.Origin, User, new string('a', 32), false, false, App, config.DiscordPublicKey);
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check("Desktop executes an authenticated command through the connection", executed == 1);
                string savedKey = remote.Configuration!.KeyName;
                Check("Installed settings contain no reusable bearer credential", !handler.PairBody.Contains("token", StringComparison.OrdinalIgnoreCase)
                    && !Encoding.UTF8.GetString(File.ReadAllBytes(configurationPath)).Contains(savedKey));
                Check("Every authenticated device request is signed", handler.SignaturesValid && handler.SignedCount > 0);
                using (var duplicate = new DiscordRemote((_, _, _) => throw new Exception("Must never execute"), configurationPath, new Handler(null)))
                    Check("Second Pilot instance cannot connect to the same pairing", duplicate.Configuration is null && duplicate.Status.Contains("another"));
                await remote.DisconnectAsync();
                Check("Local disconnect removes configuration", remote.Configuration is null && !File.Exists(configurationPath));
                bool missing = false;
                try { using var key = RemoteDeviceKey.Open(savedKey); } catch (CryptographicException) { missing = true; }
                Check("Local disconnect destroys its Windows signing key", missing);
                // A second pair with a failed cloud revoke must still remove local access.
                await remote.PairAsync(config.Origin, User, new string('a', 32), false, false, App, config.DiscordPublicKey);
                savedKey = remote.Configuration!.KeyName;
                handler.FailRevoke = true;
                await remote.DisconnectAsync();
                missing = false;
                try { using var key = RemoteDeviceKey.Open(savedKey); } catch (CryptographicException) { missing = true; }
                Check("Failed cloud revoke still deletes local key and configuration", missing && !File.Exists(configurationPath) && remote.Configuration is null);
            }
        }
        catch (Exception error) { checks.Add(new("Desktop connection integration", false, error.GetType().Name)); }
        finally
        {
            foreach (var suffix in new[] { "", ".lock", ".replay" }) if (File.Exists(configurationPath + suffix)) File.Delete(configurationPath + suffix);
            CryptographicOperations.ZeroMemory(seed);
        }
        return checks;
    }

    private sealed class Handler(Func<RemoteCommand>? command) : HttpMessageHandler
    {
        internal string PairBody = "";
        internal bool SignaturesValid = true;
        internal int SignedCount;
        internal bool FailRevoke;
        private string? publicKey;
        private bool delivered;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string raw = await request.Content!.ReadAsStringAsync(cancellationToken);
            string route = request.RequestUri!.Segments[^1];
            if (route == "pair")
            {
                PairBody = raw;
                using var document = JsonDocument.Parse(raw);
                publicKey = document.RootElement.GetProperty("publicKey").GetString();
                return Response(new { paired = true });
            }
            string timestamp = request.Headers.GetValues("X-Pilot-Timestamp").Single();
            string nonce = request.Headers.GetValues("X-Pilot-Nonce").Single();
            string signature = request.Headers.GetValues("X-Pilot-Signature").Single();
            string message = $"pilot-device-v2\nPOST\n{request.RequestUri.AbsoluteUri}\n{timestamp}\n{nonce}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))}";
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey!), out _);
            SignaturesValid &= request.Headers.Authorization is null && key.VerifyData(Encoding.UTF8.GetBytes(message), Convert.FromHexString(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            SignedCount++;
            if (route == "revoke" && FailRevoke) return new(HttpStatusCode.ServiceUnavailable);
            if (route == "poll")
            {
                var next = delivered ? null : command?.Invoke();
                delivered = true;
                return Response(new { command = next });
            }
            return Response(new { accepted = true });
        }
        private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json") };
    }
}
