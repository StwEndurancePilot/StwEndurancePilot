using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EndurancePilot.App;

internal sealed record EpicSignIn(Uri VerificationUri, DateTimeOffset ExpiresAt, string DeviceCode, int IntervalSeconds)
{
    public override string ToString() => "Epic sign-in (approval details redacted)";
}

internal sealed class EpicAuthentication : IDisposable
{
    // Public Epic client credentials used by Penny Launcher for its device login and official game launch.
    private const string SwitchClient = "98f7e42c2e3a4f86a74eb43fbb41ed39:0a2449a2-001a-451e-afec-3e812901c4d7";
    private const string AndroidClient = "3f69e56c7649492c8cc29f1af08a8a12:b51ee9cb12234f50a69efa67ef53812e";
    private const string LauncherClient = "34a02cf8f4414e29b15921876da36f9a:daafbccc737745039dffe53d94fc76cf";
    private const string Oauth = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/";
    private const string Oauth03 = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/";
    private const string Account = "https://account-public-service-prod.ol.epicgames.com/account/api/public/account/";
    private readonly HttpClient client;

    internal EpicAuthentication(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        client.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<EpicSignIn> BeginSignInAsync(CancellationToken token)
    {
        using var bootstrap = await PostAsync(Oauth + "token", SwitchClient,
            new() { ["grant_type"] = "client_credentials" }, token);
        string accessToken = Required(bootstrap.RootElement, "access_token");
        using var auth = await PostAsync(Oauth03 + "deviceAuthorization", null, new(), token, accessToken);
        var root = auth.RootElement;
        var verification = new Uri(Required(root, "verification_uri_complete"));
        if (verification.Scheme != Uri.UriSchemeHttps ||
            (verification.Host != "epicgames.com" && !verification.Host.EndsWith(".epicgames.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Epic returned an unexpected sign in address.");
        int expires = root.GetProperty("expires_in").GetInt32();
        int interval = root.GetProperty("interval").GetInt32();
        return new(verification, DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(expires, 1, 900)),
            Required(root, "device_code"), Math.Max(5, interval));
    }

    public async Task<EpicAccount> FinishSignInAsync(EpicSignIn signIn, CancellationToken token)
    {
        int interval = signIn.IntervalSeconds;
        while (DateTimeOffset.UtcNow < signIn.ExpiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token);
            using var response = await SendFormAsync(Oauth03 + "token", SwitchClient,
                new() { ["grant_type"] = "device_code", ["device_code"] = signIn.DeviceCode }, token);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (response.IsSuccessStatusCode)
            {
                string switchToken = Required(json.RootElement, "access_token");
                string code = await ExchangeAsync(Oauth03, switchToken, token);
                return await AddFromCodeAsync(code, token);
            }
            string error = json.RootElement.TryGetProperty("errorCode", out var codeElement) ? codeElement.GetString() ?? "" : "";
            if (error.EndsWith("authorization_pending", StringComparison.Ordinal) || error == "errors.com.epicgames.not_found") continue;
            if (error.EndsWith("slow_down", StringComparison.Ordinal)) { interval += 5; continue; }
            if (error.Contains("expired", StringComparison.OrdinalIgnoreCase)) break;
            throw new InvalidOperationException("Epic sign in was declined or could not be completed.");
        }
        throw new TimeoutException("Epic sign in expired. Start again to get a new sign in link.");
    }

    public async Task<EpicAccount> AddFromCodeAsync(string code, CancellationToken token)
    {
        using var access = await PostAsync(Oauth + "token", AndroidClient,
            new() { ["grant_type"] = "exchange_code", ["exchange_code"] = code.Trim() }, token);
        return await CreateDeviceAccountAsync(access.RootElement, token);
    }

    public async Task<EpicAccount> AddFromAuthorizationCodeAsync(string code, CancellationToken token)
    {
        string pasted = code.Trim();
        if (pasted.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(pasted);
                pasted = Required(json.RootElement, "authorizationCode");
            }
            catch (JsonException) { throw new InvalidDataException("Paste the authorizationCode shown by Epic."); }
        }
        using var access = await PostAsync(Oauth + "token", AndroidClient,
            new() { ["grant_type"] = "authorization_code", ["code"] = pasted }, token);
        return await CreateDeviceAccountAsync(access.RootElement, token);
    }

    public async Task<EpicAccount> AddFromDeviceAuthAsync(string accountId, string deviceId, string secret, CancellationToken token)
    {
        using var access = await DeviceTokenAsync(new(accountId.Trim(), "", deviceId.Trim(), secret.Trim()), token);
        return new(Required(access.RootElement, "account_id"), Required(access.RootElement, "displayName"), deviceId.Trim(), secret.Trim());
    }

    private async Task<EpicAccount> CreateDeviceAccountAsync(JsonElement access, CancellationToken token)
    {
        string accountId = Required(access, "account_id");
        string displayName = Required(access, "displayName");
        string accessToken = Required(access, "access_token");
        using var device = await PostJsonAsync(Account + Uri.EscapeDataString(accountId) + "/deviceAuth", accessToken, token);
        return new(accountId, displayName, Required(device.RootElement, "deviceId"), Required(device.RootElement, "secret"));
    }

    public async Task<string> LaunchExchangeCodeAsync(EpicAccount account, CancellationToken token)
    {
        using var access = await DeviceTokenAsync(account, token);
        string accountCode = await ExchangeAsync(Oauth, Required(access.RootElement, "access_token"), token);
        using var launcher = await PostAsync(Oauth + "token", LauncherClient,
            new() { ["grant_type"] = "exchange_code", ["exchange_code"] = accountCode }, token);
        return await ExchangeAsync(Oauth, Required(launcher.RootElement, "access_token"), token);
    }

    private Task<JsonDocument> DeviceTokenAsync(EpicAccount account, CancellationToken token) => PostAsync(Oauth + "token", AndroidClient,
        new() { ["grant_type"] = "device_auth", ["account_id"] = account.AccountId,
            ["device_id"] = account.DeviceId, ["secret"] = account.Secret }, token);

    private async Task<string> ExchangeAsync(string baseUrl, string accessToken, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "exchange");
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
        using var response = await client.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Epic could not create a launch code. Sign in again if this persists.");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        return Required(json.RootElement, "code");
    }

    private async Task<JsonDocument> PostAsync(string uri, string? basic, Dictionary<string, string> form, CancellationToken token, string? bearer = null)
    {
        using var response = await SendFormAsync(uri, basic, form, token, bearer);
        EnsureSuccess(response, uri);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }

    private async Task<JsonDocument> PostJsonAsync(string uri, string bearer, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", bearer);
        using var response = await client.SendAsync(request, token);
        EnsureSuccess(response, uri);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string uri)
    {
        if (response.IsSuccessStatusCode) return;
        string step = uri.EndsWith("deviceAuthorization", StringComparison.Ordinal) ? "device sign in" :
            uri.EndsWith("deviceAuth", StringComparison.Ordinal) ? "saving account credentials" : "token exchange";
        int status = (int)response.StatusCode;
        throw new InvalidOperationException(status is 400 or 401 or 403
            ? $"Epic rejected the {step} request (HTTP {status}). Sign in again."
            : $"Epic {step} failed (HTTP {status}). Try again shortly.");
    }

    private async Task<HttpResponseMessage> SendFormAsync(string uri, string? basic, Dictionary<string, string> form, CancellationToken token, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(form) };
        if (basic is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(basic)));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("bearer", bearer);
        return await client.SendAsync(request, token);
    }

    private static string Required(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidDataException("Epic returned incomplete authentication data.");

    public void Dispose() => client.Dispose();
}
