using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.SourcePlayer;

public sealed class PreviewOptions
{
    public bool FireEnabled { get; }
    public SourceKickMeterMode KickMeters { get; }
    public SourceGameplayCompatibility Compatibility { get; }
    public string Profile { get; }
    public bool GameflowEnabled { get; }
    public bool InstructionsEnabled { get; }
    public bool WorldEnabled { get; }
    public bool RecordsEnabled { get; }
    public bool PreGameSoundEnabled => Profile == SourceTeamResourceProfile.CompleteOriginalPrematchStableId;

    private PreviewOptions(string profile, bool fire, SourceKickMeterMode meters,
        SourceGameplayCompatibility compatibility, bool gameflow, bool instructions, bool world, bool records)
    {
        Profile = profile; FireEnabled = fire; KickMeters = meters; Compatibility = compatibility;
        GameflowEnabled = gameflow; InstructionsEnabled = instructions;
        WorldEnabled = world;
        RecordsEnabled = records;
    }

    public static PreviewOptions FromUrl(string url)
        => FromUrl(url, SourceTeamResourceProfile.C2M1StableId);

    public static PreviewOptions FromUrl(string url, string builtStableId)
    {
        DataCatalog.Require(builtStableId == SourceTeamResourceProfile.C2M1StableId ||
            builtStableId == SourceTeamResourceProfile.PreSnapSwitchStableId ||
            builtStableId == SourceTeamResourceProfile.DefaultAudiblesStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteOriginalStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteOriginalSourceBanksStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteOriginalRecordsStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteOriginalFinalStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteOriginalPrematchStableId,
            "An explicit authenticated historical M1 or pre-snap-switch built profile is required.");
        var profile = SourceTeamResourceProfile.SelectRuntimeProfile(url, builtStableId);
        bool fire = SourceFireGameplayOptions.PortableRequested(url);
        SourceKickMeterMode meters = SourceKickMeterOptions.FromUrl(url);
        SourceGameplayCompatibility parsed = SourceGameplayCompatibilityOptions.FromUrl(url);
        bool explicitCompatibility = false;
        bool gameflow = profile.IsCompleteOriginal303, seenGameflow = false;
        bool instructions = profile.IsCompleteOriginal303, seenInstructions = false;
        bool world = profile.IsCompleteOriginal303, seenWorld = false;
        bool records = profile.IsCompleteOriginalRecords303, seenRecords = false;
        int query = url?.IndexOf('?') ?? -1, fragment = url?.IndexOf('#') ?? -1;
        if (query >= 0 && (fragment < 0 || query < fragment))
            foreach (string part in url.Substring(query + 1, (fragment < 0 ? url.Length : fragment) - query - 1).Split('&'))
            {
                string[] pair = part.Split('=');
                string key = Uri.UnescapeDataString(pair[0]);
                explicitCompatibility |= key == "source-compatibility";
                if (key == "source-records")
                {
                    DataCatalog.Require(!seenRecords && pair.Length == 2,
                        "Specify source-records exactly once as portable or disabled.");
                    seenRecords = true;
                    string value = Uri.UnescapeDataString(pair[1]);
                    DataCatalog.Require(value == "portable" || value == "disabled",
                        "source-records must be exactly portable or disabled.");
                    records = value == "portable";
                }
                if (key == "source-world")
                {
                    DataCatalog.Require(!seenWorld && pair.Length == 2,
                        "Specify source-world exactly once with the value portable or disabled.");
                    seenWorld = true;
                    string value = Uri.UnescapeDataString(pair[1]);
                    DataCatalog.Require(value == "portable" || value == "disabled",
                        "source-world must be exactly portable or disabled.");
                    world = value == "portable";
                }
                if (key == "source-instructions")
                {
                    DataCatalog.Require(!seenInstructions && pair.Length == 2,
                        "Specify source-instructions exactly once with the value portable or disabled.");
                    seenInstructions = true;
                    string value = Uri.UnescapeDataString(pair[1]);
                    DataCatalog.Require(value == "portable" || value == "disabled",
                        "source-instructions must be exactly portable or disabled.");
                    instructions = value == "portable";
                }
                if (key != "source-gameflow") continue;
                DataCatalog.Require(!seenGameflow && pair.Length == 2,
                    "Specify source-gameflow exactly once with the value portable.");
                seenGameflow = true;
                DataCatalog.Require(Uri.UnescapeDataString(pair[1]) == "portable",
                    "source-gameflow must be exactly portable; omission leaves the cabinet flow unbound.");
                gameflow = true;
            }
        DataCatalog.Require(profile.UsesM1Services, "This private browser asset generation requires its selected M1 profile; no C1/old-nine alias.");
        DataCatalog.Require(!profile.UsesDefaultAudibles || !explicitCompatibility ||
            parsed == SourceGameplayCompatibility.Portable,
            "Default audibles require explicit Portable policy; StrictSource is not a successor fallback.");
        SourceGameplayCompatibility compatibility = explicitCompatibility ? parsed : SourceGameplayCompatibility.Portable;
        DataCatalog.Require(!gameflow || compatibility == SourceGameplayCompatibility.Portable,
            "source-gameflow=portable requires Portable gameplay; StrictSource is never promoted.");
        DataCatalog.Require(!instructions || gameflow && compatibility == SourceGameplayCompatibility.Portable &&
            profile.UsesPreSnapSwitchServices,
            "source-instructions=portable requires source-gameflow=portable and a genuine FourStation pre-snap-switch " +
            "or explicitly admitted default-audibles composition with actual consumed physical switches; no input-scan fallback.");
        DataCatalog.Require(!world || compatibility == SourceGameplayCompatibility.Portable,
            "source-world=portable requires Portable gameplay; StrictSource is never promoted.");
        DataCatalog.Require(!profile.IsCompleteOriginal303 || gameflow && instructions && world,
            "The explicit complete-original feature contract bundles actual gameflow/instructions/world/audio; use an old profile for old flights.");
        DataCatalog.Require(!records || gameflow && compatibility == SourceGameplayCompatibility.Portable &&
            profile.IsCompleteOriginalRecords303,
            "Portable record UI requires its explicit authenticated original record/CAP artwork successor and gameflow; old packages are not aliases.");
        DataCatalog.Require(!profile.IsCompleteOriginalPrematch303 || records,
            "The original source pregame profile requires its genuine options/name/PIN/CAP record driver; use the frozen final profile for a record-disabled AV probe.");
        return new PreviewOptions(profile.StableId, fire, meters, compatibility, gameflow, instructions, world, records);
    }
}
