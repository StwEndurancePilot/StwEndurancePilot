using System.Text.Json;

namespace EndurancePilot.App;

// Public trust anchors, embedded at build time. Never fetch a verification key from the Worker.
internal sealed record DiscordPublicSettings(string WorkerOrigin, string ApplicationId, string PublicKey)
{
    internal static DiscordPublicSettings? Load()
    {
        try
        {
            using var source = typeof(DiscordPublicSettings).Assembly.GetManifestResourceStream("EndurancePilot.DiscordPublicSettings");
            return source is null ? null : Parse(JsonSerializer.Deserialize<DiscordPublicSettings>(source));
        }
        catch (JsonException) { return null; }
    }

    internal static DiscordPublicSettings? Parse(DiscordPublicSettings? settings)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.WorkerOrigin) ||
            string.IsNullOrWhiteSpace(settings.ApplicationId) || string.IsNullOrWhiteSpace(settings.PublicKey)) return null;
        try
        {
            DiscordRemote.Validate(settings.WorkerOrigin, settings.ApplicationId);
            RemoteCommandProof.ValidateIdentity(settings.ApplicationId, settings.PublicKey);
            return settings;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return null; }
    }
}
