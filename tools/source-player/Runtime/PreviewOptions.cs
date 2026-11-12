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
    public SourceStationLayout? CabinetLayout { get; }
    public int? CabinetStationsMask { get; }
    public int GcSkillIndex { get; }
    public bool GcSkillSpecified { get; }
    public int UserBaseAdjustment { get; }
    public bool PreGameSoundEnabled => Profile == SourceTeamResourceProfile.CompleteOriginalPrematchStableId ||
        Profile == SourceTeamResourceProfile.CompleteOriginalSelectedWorldStableId ||
        Profile == SourceTeamResourceProfile.CompleteAv306StableId ||
        Profile == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsStableId;

    private PreviewOptions(string profile, bool fire, SourceKickMeterMode meters,
        SourceGameplayCompatibility compatibility, bool gameflow, bool instructions, bool world, bool records,
        SourceStationLayout? cabinetLayout, int? cabinetStationsMask, int gcSkillIndex, bool gcSkillSpecified)
    {
        Profile = profile; FireEnabled = fire; KickMeters = meters; Compatibility = compatibility;
        GameflowEnabled = gameflow; InstructionsEnabled = instructions;
        WorldEnabled = world;
        RecordsEnabled = records;
        CabinetLayout = cabinetLayout; CabinetStationsMask = cabinetStationsMask;
        GcSkillIndex = gcSkillIndex;
        GcSkillSpecified = gcSkillSpecified;
        UserBaseAdjustment = gcSkillIndex == 0 ? -3 : gcSkillIndex == 1 ? 0 : 3;
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
            builtStableId == SourceTeamResourceProfile.CompleteOriginalPrematchStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteOriginalSelectedWorldStableId ||
            builtStableId == SourceTeamResourceProfile.CompleteAv306StableId ||
            builtStableId == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsStableId,
            "An explicit authenticated historical M1 or pre-snap-switch built profile is required.");
        var profile = SourceTeamResourceProfile.SelectRuntimeProfile(url, builtStableId);
        bool fire = SourceFireGameplayOptions.PortableRequested(url);
        SourceKickMeterMode meters = SourceKickMeterOptions.FromUrl(url);
        SourceGameplayCompatibility parsed = SourceGameplayCompatibilityOptions.FromUrl(url);
        bool explicitCompatibility = false;
        bool gameflow = profile.UsesCompleteSourcePrograms, seenGameflow = false;
        bool instructions = profile.UsesCompleteSourcePrograms, seenInstructions = false;
        bool world = profile.UsesCompleteSourcePrograms, seenWorld = false;
        bool records = profile.UsesRecordPresentation, seenRecords = false;
        SourceStationLayout? cabinetLayout = null;
        int? cabinetStations = null;
        bool seenCabinet = false, seenStations = false;
        int gcSkillIndex = 1;
        bool seenGcSkill = false;
        int query = url?.IndexOf('?') ?? -1, fragment = url?.IndexOf('#') ?? -1;
        if (query >= 0 && (fragment < 0 || query < fragment))
            foreach (string part in url.Substring(query + 1, (fragment < 0 ? url.Length : fragment) - query - 1).Split('&'))
            {
                string[] pair = part.Split('=');
                string key = Uri.UnescapeDataString(pair[0]);
                explicitCompatibility |= key == "source-compatibility";
                if (key == "source-gc-skill")
                {
                    DataCatalog.Require(!seenGcSkill && pair.Length == 2,
                        "Specify source-gc-skill exactly once as the original GC_SKILL index 0,1,2.");
                    seenGcSkill = true;
                    string value = Uri.UnescapeDataString(pair[1]);
                    DataCatalog.Require(value.Length == 1 && value[0] is >= '0' and <= '2',
                        "source-gc-skill must be exactly the original GC_SKILL index 0,1,2; operator Difficulty is separate.");
                    gcSkillIndex = value[0] - '0';
                }
                if (key == "source-cabinet")
                {
                    DataCatalog.Require(!seenCabinet && pair.Length == 2,
                        "Specify source-cabinet exactly once as two-station or four-station.");
                    seenCabinet = true;
                    string value = Uri.UnescapeDataString(pair[1]);
                    DataCatalog.Require(value == "two-station" || value == "four-station",
                        "source-cabinet must be exactly two-station or four-station.");
                    cabinetLayout = value == "two-station" ? SourceStationLayout.TwoStation : SourceStationLayout.FourStation;
                }
                if (key == "source-stations")
                {
                    DataCatalog.Require(!seenStations && pair.Length == 2,
                        "Specify source-stations exactly once as distinct original physical IDs.");
                    seenStations = true;
                    int mask = 0;
                    string[] ids = Uri.UnescapeDataString(pair[1]).Split(',');
                    DataCatalog.Require(ids.Length is >= 1 and <= 4, "One to four original physical station IDs are required.");
                    foreach (string id in ids)
                    {
                        DataCatalog.Require(id.Length == 1 && id[0] is >= '0' and <= '3',
                            "source-stations accepts only original physical IDs 0,1,2,3.");
                        int bit = 1 << (id[0] - '0');
                        DataCatalog.Require((mask & bit) == 0, "source-stations cannot repeat a physical station.");
                        mask |= bit;
                    }
                    cabinetStations = mask;
                }
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
        if (profile.IsCompleteAv306TwoStationRecords)
        {
            DataCatalog.Require((!cabinetLayout.HasValue || cabinetLayout == SourceStationLayout.TwoStation) &&
                (!cabinetStations.HasValue || cabinetStations == 3),
                "The saved-record TwoStation profile requires its actual physical 0/Home and 1/Away; it cannot alias a FourStation cabinet.");
            cabinetLayout = SourceStationLayout.TwoStation;
            cabinetStations = 3;
        }
        DataCatalog.Require(!seenStations || cabinetLayout.HasValue,
            "source-stations requires an explicit source-cabinet layout; two-station P2 and four-station P2 are different sides.");
        DataCatalog.Require(!cabinetLayout.HasValue || profile.UsesPreSnapSwitchServices &&
            compatibility == SourceGameplayCompatibility.Portable,
            "Cabinet layout choices require explicit Portable original pre-snap-switch services; old flights are unchanged.");
        if (cabinetLayout == SourceStationLayout.TwoStation)
        {
            cabinetStations ??= 3;
            DataCatalog.Require(cabinetStations == 3,
                "Original TwoStation requires both physical 0/Home and 1/Away; missing plyr_control entries are undefined.");
        }
        DataCatalog.Require(!gameflow || compatibility == SourceGameplayCompatibility.Portable,
            "source-gameflow=portable requires Portable gameplay; StrictSource is never promoted.");
        DataCatalog.Require(!instructions || gameflow && compatibility == SourceGameplayCompatibility.Portable &&
            profile.UsesPreSnapSwitchServices,
            "source-instructions=portable requires source-gameflow=portable and a genuine FourStation pre-snap-switch " +
            "or explicitly admitted default-audibles composition with actual consumed physical switches; no input-scan fallback.");
        DataCatalog.Require(!world || compatibility == SourceGameplayCompatibility.Portable,
            "source-world=portable requires Portable gameplay; StrictSource is never promoted.");
        DataCatalog.Require(!profile.UsesCompleteSourcePrograms || gameflow && instructions && world,
            "The explicit complete-original feature contract bundles actual gameflow/instructions/world/audio; use an old profile for old flights.");
        DataCatalog.Require(!records || gameflow && compatibility == SourceGameplayCompatibility.Portable &&
            profile.UsesRecordPresentation,
            "Portable record UI requires its explicit authenticated original record/CAP artwork successor and gameflow; old packages are not aliases.");
        DataCatalog.Require(!profile.UsesPreGamePresentation || records,
            "The original source pregame profile requires its genuine options/name/PIN/CAP record driver; use the frozen final profile for a record-disabled AV probe.");
        return new PreviewOptions(profile.StableId, fire, meters, compatibility, gameflow, instructions, world, records,
            cabinetLayout, cabinetStations, gcSkillIndex, seenGcSkill);
    }
}
