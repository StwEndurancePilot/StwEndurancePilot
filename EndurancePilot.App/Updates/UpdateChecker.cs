using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndurancePilot.App;

internal enum UpdateStatus
{
    NotChecked, Current, Available, NoRelease, Unavailable
}
internal sealed record UpdateCheckResult(UpdateStatus Status, string Message, Version? Version = null, Uri? Link = null, bool DirectDownload = false, bool Installer = false);

internal sealed class UpdateChecker : IDisposable
{
    internal const string Repository = "https://github.com/StwEndurancePilot/StwEndurancePilot";
    internal const string Endpoint = "https://api.github.com/repos/StwEndurancePilot/StwEndurancePilot/releases/latest";
    internal static Version InstalledVersion { get; } = ReadInstalledVersion();
    private readonly HttpClient client;
    internal UpdateChecker(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 1024 * 1024
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EndurancePilot/" + InstalledVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
    }
    private static Version ReadInstalledVersion()
    {
        var assembly = typeof(UpdateChecker).Assembly;
        return ParseInstalledVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version);
    }

    internal static Version ParseInstalledVersion(string? informational, Version? assemblyVersion)
    {
        // Local build labels are not release tags. Compare their numeric base
        // version without relaxing the strict parser for remote release tags.
        var version = ParseVersion(informational?.Split('+', '-')[0]);
        return version ?? (assemblyVersion is null ? new Version(0, 0, 0)
            : new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(0, assemblyVersion.Build)));
    }
    internal static Version? ParseVersion(string? tag)
    {
        if (tag is null || tag.Length > 40 || !Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        return Version.TryParse(tag.TrimStart('v'), out var version) ? version : null;
    }
    internal async Task<UpdateCheckResult> CheckAsync(Version? installed = null, CancellationToken cancellation = default)
    {
        try
        {
            using var response = await client.GetAsync(Endpoint, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(UpdateStatus.NoRelease, "No published release available.");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new(UpdateStatus.Unavailable, "GitHub limited update checks. Try again later.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new(UpdateStatus.Unavailable, "Couldn’t check for updates. Try again later.");
            }

            string json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return ParseRelease(json, installed ?? InstalledVersion);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new(UpdateStatus.Unavailable, "Update check cancelled.");
        }
        catch (OperationCanceledException)
        {
            return new(UpdateStatus.Unavailable, "Update check timed out. Try again later.");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException)
        {
            return new(UpdateStatus.Unavailable, "Couldn’t check for updates. Check your connection and try again.");
        }
    }
    internal static UpdateCheckResult ParseRelease(string json, Version installed)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False)
        {
            return new(UpdateStatus.NoRelease, "No stable published release available.");
        }

        string? tag = String(root, "tag_name");
        var version = ParseVersion(tag);
        if (version is null)
        {
            return new(UpdateStatus.Unavailable, "The release tag has an unsupported version format.");
        }

        if (version <= installed)
        {
            return new(UpdateStatus.Current, "You’re up to date.", version);
        }

        var page = new Uri(Repository + "/releases/tag/" + tag);

        foreach (bool installer in new[] { true, false })
        {
            string expectedName = $"endurance-pilot-{version}{(installer ? "-setup" : "")}.exe";
            string expectedUrl = Repository + "/releases/download/" + tag + "/" + expectedName;
            var matches = new List<JsonElement>();
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.ValueKind == JsonValueKind.Object && String(asset, "name") == expectedName)
                    {
                        matches.Add(asset);
                    }
                }
            }

            if (matches.Count == 0)
            {
                continue;
            }

            if (matches.Count == 1 && String(matches[0], "state") == "uploaded"
                && matches[0].TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number
                && size.TryGetInt64(out var bytes) && bytes > 0 && String(matches[0], "browser_download_url") == expectedUrl)
            {
                return new(UpdateStatus.Available, $"Version {version} is available.", version, new Uri(expectedUrl), true, installer);
            }

            break;
        }
        return new(UpdateStatus.Available, $"Version {version} is available.", version, page);
    }
    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    public void Dispose() => client.Dispose();
}
