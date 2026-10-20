using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace EndurancePilot.App;

internal static class UpdateChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request, token);
    }
    internal static async Task<IReadOnlyList<StartupCheck>> RunAsync()
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed" : "Failed"));
        object Asset(string name = "endurance-pilot-0.3.10.exe", string? url = null) => new
        {
            name,
            state = "uploaded",
            size = 100,
            browser_download_url = url ?? UpdateChecker.Repository + "/releases/download/v0.3.10/" + name
        };
        string Release(string tag = "v0.3.10", bool draft = false, bool prerelease = false, object[]? assets = null) => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            html_url = "https://invalid.example/ignored",
            assets = assets ?? [Asset()]
        });
        var installed = new Version(0, 3, 9);
        var result = UpdateChecker.ParseRelease(Release(), installed);
        Check("Updates compare numeric versions across 9 to 10", result.Status == UpdateStatus.Available && result.Version == new Version(0, 3, 10));
        Check("Versioned release EXE uses the fixed GitHub repository", result.DirectDownload && result.Link?.AbsoluteUri == UpdateChecker.Repository + "/releases/download/v0.3.10/endurance-pilot-0.3.10.exe");
        Check("Same release does not prompt an update", UpdateChecker.ParseRelease(Release(), new(0, 3, 10)).Status == UpdateStatus.Current);
        Check("An older release never offers a downgrade", UpdateChecker.ParseRelease(Release(), new(0, 3, 11)).Status == UpdateStatus.Current);
        Check("Draft release cannot enable download", UpdateChecker.ParseRelease(Release(draft: true), installed).Status == UpdateStatus.NoRelease);
        Check("Prerelease cannot enable a stable update", UpdateChecker.ParseRelease(Release(prerelease: true), installed).Status == UpdateStatus.NoRelease);
        Check("Unsupported tag is reported rather than guessed", UpdateChecker.ParseRelease(Release(tag: "nightly"), installed).Status == UpdateStatus.Unavailable);
        Check("Stable version parser rejects suffixes and paths", UpdateChecker.ParseVersion("v1.0.0-beta") is null && UpdateChecker.ParseVersion("../1.0.0") is null && UpdateChecker.ParseVersion("1.2.3") == new Version(1, 2, 3));
        foreach (var assets in new[] { Array.Empty<object>(), new[] { Asset(url: "https://example.com/other.exe") }, new[] { Asset("wrong.exe") }, new[] { Asset(), Asset() } })
        {
            var fallback = UpdateChecker.ParseRelease(Release(assets: assets), installed);
            Check("Missing, external, mismatched or duplicate assets use the official release page", fallback.Status == UpdateStatus.Available && !fallback.DirectDownload
                && fallback.Link?.AbsoluteUri == UpdateChecker.Repository + "/releases/tag/v0.3.10");
        }
        Check("Incomplete release metadata does not report up-to-date", UpdateChecker.ParseRelease("{}", installed).Status == UpdateStatus.NoRelease);
        var setup = Asset("endurance-pilot-0.3.10-setup.exe");
        foreach (var assets in new[] { new[] { setup }, new[] { Asset(), setup }, new[] { setup, Asset() } })
        {
            var update = UpdateChecker.ParseRelease(Release(assets: assets), installed);
            Check("Installer is preferred regardless of asset ordering", update.Installer && update.DirectDownload
                && update.Link?.AbsoluteUri == UpdateChecker.Repository + "/releases/download/v0.3.10/endurance-pilot-0.3.10-setup.exe");
        }
        foreach (var assets in new[]{new[]{setup,setup,Asset()},new[]{Asset("endurance-pilot-0.3.10-setup.exe","https://example.com/setup.exe"),Asset()},
            new object[]{new{name="endurance-pilot-0.3.10-setup.exe",state="uploaded",size="bad"},Asset()}})
        {
            var update = UpdateChecker.ParseRelease(Release(assets: assets), installed);
            Check("Invalid installer uses the release page instead of silently falling back", !update.DirectDownload && !update.Installer
                && update.Link?.AbsoluteUri == UpdateChecker.Repository + "/releases/tag/v0.3.10");
        }
        Check("Legacy portable release remains downloadable", result.DirectDownload && !result.Installer);
        bool requestValid = false;
        using (var checker = new UpdateChecker(new Handler((request, _) =>
        {
            requestValid = request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == UpdateChecker.Endpoint
                && request.Headers.Authorization is null && request.Content is null && request.Headers.UserAgent.ToString().StartsWith("EndurancePilot/", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release()) });
        })))
        {
            Check("Anonymous GitHub request reads a new release", (await checker.CheckAsync(installed)).Status == UpdateStatus.Available && requestValid);
        }
        foreach (var (status, expected) in new[]{(HttpStatusCode.NotFound,UpdateStatus.NoRelease),(HttpStatusCode.Forbidden,UpdateStatus.Unavailable),
            (HttpStatusCode.TooManyRequests,UpdateStatus.Unavailable),(HttpStatusCode.InternalServerError,UpdateStatus.Unavailable),(HttpStatusCode.Redirect,UpdateStatus.Unavailable)})
        {
            using var checker = new UpdateChecker(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
            Check("Release HTTP status handled: " + status, (await checker.CheckAsync(installed)).Status == expected);
        }
        foreach (string content in new[] { "not json", new string('x', 1024 * 1024 + 1) })
        {
            using var checker = new UpdateChecker(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) })));
            Check("Invalid or excessive release response stays nonfatal", (await checker.CheckAsync(installed)).Status == UpdateStatus.Unavailable);
        }
        using (var checker = new UpdateChecker(new Handler((_, _) => throw new HttpRequestException("Offline"))))
        {
            Check("Offline checks do not interrupt the runner", (await checker.CheckAsync(installed)).Status == UpdateStatus.Unavailable);
        }

        using (var checker = new UpdateChecker(new Handler((_, token) => Task.FromCanceled<HttpResponseMessage>(token))))
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Check("Closing cancels update checks cleanly", (await checker.CheckAsync(installed, cancelled.Token)).Status == UpdateStatus.Unavailable);
        }
        using (var checker = new UpdateChecker(new Handler((_, _) => throw new TaskCanceledException())))
        {
            Check("Update timeout is shown as a timeout", (await checker.CheckAsync(installed)).Message.Contains("timed out"));
        }

        return checks;
    }
}
