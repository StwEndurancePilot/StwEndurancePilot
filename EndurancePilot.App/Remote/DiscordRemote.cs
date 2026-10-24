using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndurancePilot.App;

internal sealed record RemoteConfiguration(string Origin, string UserId, string KeyName, bool AllowControl, bool AllowScreenshots,
    string ApplicationId, string DiscordPublicKey, long PairedAt, long KeyExpires);
internal sealed record RemoteCommand(string Id, string Command, int? Account, long Expires, DiscordProof? Proof = null, string? AccountSet = null);
internal sealed record RemoteAccountChoice(int Number, string Name, bool Selected);
internal sealed record RemoteResult(string Content, string? Image = null, RemoteAccountChoice[]? Accounts = null, string? AccountSet = null)
{
    public override string ToString() => "Remote result (content redacted)";
}

internal sealed class DiscordRemote : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "discord-remote.bin");
    private readonly HttpClient http;
    private readonly RemoteReplayJournal replay;
    private readonly SemaphoreSlim configurationGate = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private readonly FileStream? instance;
    private readonly bool ownsInstance;
    private readonly Func<RemoteCommand, RemoteConfiguration, CancellationToken, Task<RemoteResult>> execute;
    private CancellationTokenSource? lifetime;
    private Task? loop;
    internal RemoteConfiguration? Configuration { get; private set; }
    internal string Status { get; private set; } = "Disabled";
    internal string LastCommandStatus { get; private set; } = "No command received this session.";
    internal event Action? ConnectionStopped;

    internal DiscordRemote(Func<RemoteCommand, RemoteConfiguration, CancellationToken, Task<RemoteResult>> execute,
        string? configurationPath = null, HttpMessageHandler? handler = null)
    {
        this.execute = execute;
        if (configurationPath is not null) path = configurationPath;
        replay = new(path + ".replay");
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 200_000 };
        // One process per configuration, including across Windows sessions for the same user.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            instance = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ownsInstance = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ownsInstance = false; }
        // Read the saved identity even if another process briefly owns the connection
        // during an update. It must not look like the user has never paired this PC.
        try
        {
            if (File.Exists(path))
            {
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                Configuration = JsonSerializer.Deserialize<RemoteConfiguration>(bytes, Json);
                if (Configuration is { } configuration)
                {
                    Validate(configuration.Origin, configuration.UserId);
                    RemoteCommandProof.ValidateIdentity(configuration.ApplicationId, configuration.DiscordPublicKey);
                    using var key = RemoteDeviceKey.Open(configuration.KeyName);
                    // Legacy KeyExpires is retained for file compatibility only.
                    // A saved pairing lasts until explicit revocation, including across upgrades.
                }
            }
        }
        catch { Configuration = null; Status = "Saved connection could not be read. Pair again."; }
        if (!ownsInstance) Status = "Remote storage is unavailable or open in another Pilot instance. Close the other instance and reopen Pilot.";
    }

    internal static Uri Validate(string origin, string user)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !Regex.IsMatch(user, "\\A[0-9]{17,20}\\z")) throw new InvalidDataException("Enter an HTTPS Worker origin and your Discord user ID.");
        return uri;
    }

    internal void Start()
    {
        if (!ownsInstance || shutdown.IsCancellationRequested || Configuration is null || lifetime is not null) return;
        Status = "Reconnecting to Discord…";
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        loop = RunAsync(Configuration, lifetime.Token);
    }

    internal async Task PairAsync(string origin, string user, string code, bool control, bool screenshots,
        string application, string publicKey, CancellationToken cancellation = default)
    {
        if (!ownsInstance || !await configurationGate.WaitAsync(0, cancellation)) throw new InvalidOperationException("Another connection operation is active.");
        string? keyName = null;
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
            var token = operation.Token;
            token.ThrowIfCancellationRequested();
            if (Configuration is not null) throw new InvalidOperationException("Disconnect the current PC pairing first.");
            var uri = Validate(origin.Trim(), user.Trim());
            RemoteCommandProof.ValidateIdentity(application.Trim(), publicKey.Trim());
            if (!Regex.IsMatch(code.Trim(), "\\A[a-f0-9]{32}\\z")) throw new InvalidDataException("Enter the pairing code from /pair.");
            keyName = RemoteDeviceKey.Create();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var config = new RemoteConfiguration(uri.GetLeftPart(UriPartial.Authority), user.Trim(), keyName, control, screenshots,
                application.Trim(), publicKey.Trim().ToLowerInvariant(), now, long.MaxValue);
            using var response = await SendAsync(config, "pair", new { code = code.Trim(), publicKey = RemoteDeviceKey.PublicKey(keyName) }, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Pairing failed. Request a fresh code using /unpair, then /pair.");
            using var result = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
            if (!result.RootElement.GetProperty("paired").GetBoolean()) throw new InvalidDataException();
            token.ThrowIfCancellationRequested();
            // Atomic save; a truncated configuration never becomes an enabled connection.
            RemoteReplayJournal.WriteProtected(path, JsonSerializer.SerializeToUtf8Bytes(config, Json));
            Configuration = config;
            keyName = null; // Configuration now owns the key.
            Start();
        }
        finally
        {
            try { if (keyName is not null) RemoteDeviceKey.Delete(keyName); }
            finally { configurationGate.Release(); }
        }
    }

    internal async Task DisconnectAsync()
    {
        if (!ownsInstance || !await configurationGate.WaitAsync(0)) throw new InvalidOperationException("Another connection operation is active.");
        try
        {
        var config = Configuration;
        ConnectionStopped?.Invoke();
        lifetime?.Cancel();
        if (loop is not null) await loop;
        lifetime?.Dispose(); lifetime = null; loop = null;
        Configuration = null;
        Status = "Disabled";
        try
        {
            if (config is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var response = await SendAsync(config, "revoke", new { }, timeout.Token);
                if (!response.IsSuccessStatusCode) Status = "Disabled locally. Use /unpair to clear the cloud pairing.";
            }
        }
        catch { Status = "Disabled locally. Use /unpair to clear the cloud pairing."; }
        finally
        {
            // Deleting the key makes a leftover configuration unusable, even if file deletion fails.
            try { if (config is not null) RemoteDeviceKey.Delete(config.KeyName); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        }
        finally { configurationGate.Release(); }
    }

    private Task<HttpResponseMessage> SendAsync(RemoteConfiguration config, string route, object body, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{config.Origin}/device/{config.UserId}/{route}");
        string raw = JsonSerializer.Serialize(body, Json);
        request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        if (route != "pair")
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            string nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            request.Headers.Add("X-Pilot-Timestamp", timestamp);
            request.Headers.Add("X-Pilot-Nonce", nonce);
            request.Headers.Add("X-Pilot-Signature", RemoteDeviceKey.Sign(config.KeyName, config.Origin, config.UserId, route, raw, timestamp, nonce));
        }
        return SendAndDisposeAsync(request, token);
    }

    private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request, CancellationToken token)
    {
        using (request) return await http.SendAsync(request, token);
    }

    private async Task RunAsync(RemoteConfiguration config, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var response = await SendAsync(config, "poll", new { }, token);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // A 401 can also mean clock skew after sleep or a transient authentication
                    // failure. Keep the saved key and retry; never execute an untrusted command.
                    Status = "Connection not authorized; retrying. If you used /unpair, disconnect here and pair again.";
                    ConnectionStopped?.Invoke();
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                // A Worker response should be tiny; do not deserialize unbounded remote data.
                var bytes = await response.Content.ReadAsByteArrayAsync(token);
                if (bytes.Length > 200_000) throw new InvalidDataException();
                using var document = JsonDocument.Parse(bytes);
                Status = "Connected";
                if (document.RootElement.GetProperty("command") is { ValueKind: JsonValueKind.Object } value)
                {
                    var command = value.Deserialize<RemoteCommand>(Json)!;
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (!RemoteCommandProof.Verify(command, config, now))
                    {
                        LastCommandStatus = "Command rejected: Discord proof did not match this pairing or its time window.";
                        throw new InvalidDataException("Command proof rejected.");
                    }
                    if (replay.TryAccept(command.Id, now))
                    {
                        LastCommandStatus = "Command verified; waiting for the desktop action to finish.";
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromMilliseconds(command.Expires - now));
                        RemoteResult result;
                        try
                        {
                            result = RemotePermissions.Allows(command.Command, config)
                                ? await execute(command, config, deadline.Token)
                                : new("This command is not permitted by this PC. Change permissions by pairing again locally.");
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                        catch { result = new("Action could not be completed. Check Pilot on your PC."); }
                        token.ThrowIfCancellationRequested();
                        LastCommandStatus = "Desktop action finished; sending reply.";
                        try
                        {
                            using var sent = await SendAsync(config, "result", new { id = command.Id, content = result.Content, image = result.Image, accounts = result.Accounts, accountSet = result.AccountSet }, token);
                            LastCommandStatus = await DescribeReplyAsync(sent, token);
                            if (!sent.IsSuccessStatusCode) Status = "Reply was not delivered. Check Discord before repeating an action.";
                        }
                        catch
                        {
                            LastCommandStatus = "Desktop action finished, but reply delivery timed out or the connection failed. Check Pilot before repeating it.";
                            throw;
                        }
                    }
                    else LastCommandStatus = "Duplicate command rejected; the action was not repeated.";
                }
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch
            {
                Status = "Connection unavailable; retrying";
                ConnectionStopped?.Invoke(); // Cancel pending Epic approval on loss of trust/connectivity.
                try { await Task.Delay(TimeSpan.FromSeconds(10), token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    internal static async Task<string> DescribeReplyAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return "Reply accepted by the Worker and delivered to Discord.";
        string upstream = "";
        try
        {
            using var data = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
            if (data.RootElement.TryGetProperty("discordStatus", out var status) && status.TryGetInt32(out int code) && code is >= 100 and <= 599)
                upstream = $" Discord HTTP {code}.";
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { }
        return $"Desktop action finished; reply failed (Worker HTTP {(int)response.StatusCode}).{upstream} Check Pilot before repeating it.";
    }

    public void Dispose()
    {
        if (shutdown.IsCancellationRequested) return;
        shutdown.Cancel(); ConnectionStopped?.Invoke(); lifetime?.Cancel(); http.Dispose();
        instance?.Dispose();
    }
}
