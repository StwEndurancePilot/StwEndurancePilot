using System.Net;
using System.Net.Http;
using System.Text;

namespace EndurancePilot.App;

internal static class EpicAuthenticationChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        public bool TokenFormCorrect { get; private set; }
        public bool DeviceJsonCorrect { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string body = await request.Content!.ReadAsStringAsync(token);
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                TokenFormCorrect = request.Content.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded"
                    && body.Contains("grant_type=exchange_code", StringComparison.Ordinal);
                return Json("{\"account_id\":\"account-id\",\"displayName\":\"Player\",\"access_token\":\"access-token\"}");
            }
            DeviceJsonCorrect = request.RequestUri.AbsolutePath.EndsWith("/account-id/deviceAuth", StringComparison.Ordinal)
                && request.Content.Headers.ContentType?.MediaType == "application/json"
                && body == "{}" && request.Headers.Authorization?.Scheme == "bearer";
            return Json("{\"deviceId\":\"device-id\",\"secret\":\"device-secret\"}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    public static async Task<IReadOnlyList<StartupCheck>> RunAsync()
    {
        var handler = new Handler();
        using var auth = new EpicAuthentication(handler);
        try
        {
            var account = await auth.AddFromCodeAsync("one-time-code", CancellationToken.None);
            bool passed = handler.TokenFormCorrect && handler.DeviceJsonCorrect
                && account is { AccountId: "account-id", DeviceId: "device-id", Secret: "device-secret" };
            return [new StartupCheck("Epic account creation uses JSON for device credentials", passed, passed ? "Passed" : "Failed")];
        }
        catch (Exception error)
        {
            return [new StartupCheck("Epic account creation uses JSON for device credentials", false, error.Message)];
        }
    }
}
