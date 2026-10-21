using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.SourcePlayer;

public sealed class PreviewOptions
{
    public bool FireEnabled { get; }
    public SourceKickMeterMode KickMeters { get; }
    public SourceGameplayCompatibility Compatibility { get; }
    public string Profile { get; }

    private PreviewOptions(string profile, bool fire, SourceKickMeterMode meters, SourceGameplayCompatibility compatibility)
    { Profile = profile; FireEnabled = fire; KickMeters = meters; Compatibility = compatibility; }

    public static PreviewOptions FromUrl(string url)
        => FromUrl(url, SourceTeamResourceProfile.C2M1StableId);

    public static PreviewOptions FromUrl(string url, string builtStableId)
    {
        DataCatalog.Require(builtStableId == SourceTeamResourceProfile.C2M1StableId ||
            builtStableId == SourceTeamResourceProfile.PreSnapSwitchStableId,
            "An explicit authenticated historical M1 or pre-snap-switch built profile is required.");
        var profile = SourceTeamResourceProfile.SelectRuntimeProfile(url, builtStableId);
        bool fire = SourceFireGameplayOptions.PortableRequested(url);
        SourceKickMeterMode meters = SourceKickMeterOptions.FromUrl(url);
        SourceGameplayCompatibility parsed = SourceGameplayCompatibilityOptions.FromUrl(url);
        bool explicitCompatibility = false;
        int query = url?.IndexOf('?') ?? -1, fragment = url?.IndexOf('#') ?? -1;
        if (query >= 0 && (fragment < 0 || query < fragment))
            foreach (string part in url.Substring(query + 1, (fragment < 0 ? url.Length : fragment) - query - 1).Split('&'))
                explicitCompatibility |= Uri.UnescapeDataString(part.Split('=')[0]) == "source-compatibility";
        DataCatalog.Require(profile.UsesM1Services, "This private browser asset generation requires its selected M1 profile; no C1/old-nine alias.");
        return new PreviewOptions(profile.StableId, fire, meters,
            explicitCompatibility ? parsed : SourceGameplayCompatibility.Portable);
    }
}
