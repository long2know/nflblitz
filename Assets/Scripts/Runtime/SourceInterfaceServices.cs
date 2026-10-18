using System;
using System.IO;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using UnityEngine;

namespace Blitz.Runtime
{
    public static class SourceInterfaceMovementComposition
    {
        public static SourceMovementRuntimeData RuntimeData(SourceMovementData movement, SourceInterfaceMovementData actual)
        {
            if (movement == null || actual == null)
                throw new ArgumentNullException(movement == null ? nameof(movement) : nameof(actual));
            var rows = new SourceMovementDefenseRow[actual.Defense.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                SourceInterfaceDefenseRow row = actual.Defense[i];
                rows[i] = new SourceMovementDefenseRow(row.Line, row.Back, row.Base, row.Range, row.Plow);
            }
            return new SourceMovementRuntimeData(movement, actual.Friction, rows);
        }

        public static SourceMovementSettings Settings(SourceInterfaceMovementBootstrap actual)
        {
            if (actual == null) throw new ArgumentNullException(nameof(actual));
            return new SourceMovementSettings(actual.FieldIndex, actual.InfiniteTurboTeamMask,
                actual.SpeedPowerup, actual.FastTurboTeamMask, actual.RunOutOfBoundsTeamMask, actual.ViolencePowerup);
        }
    }

    [Serializable]
    public sealed class SourceInterfaceStation
    {
        [SerializeField] private int stationId;
        [SerializeField] private TeamId team;
        [SerializeField] private int initialPlayerId = -1;
        [SerializeField] private int profileTeam = -1;
        [SerializeField] private int profileRecord = -1;
        public SourceInterfaceStation(SourceStationConfiguration station)
        {
            stationId = station.StationId; team = station.Team; initialPlayerId = station.InitialPlayerId;
            profileTeam = station.Profile.TeamIndex; profileRecord = station.Profile.RosterIndex;
        }
        public SourceStationConfiguration Configuration()
        {
            if ((profileTeam < 0) != (profileRecord < 0))
                throw new SourceInterfaceException("An original station profile must retain both source team/static-record identities.");
            SourceRosterIdentity profile = profileTeam < 0 ? default(SourceRosterIdentity) : new SourceRosterIdentity(profileTeam, profileRecord);
            return new SourceStationConfiguration(stationId, team, initialPlayerId, profile);
        }
    }

    [Serializable]
    public sealed class SourceInterfacePlay
    {
        [SerializeField] private string symbol;
        [SerializeField] private SourcePlaybookKind kind;
        [SerializeField] private int slot = -1;
        [SerializeField] private int page = -1;
        [SerializeField] private bool hasPool;
        [SerializeField] private SourcePlayPool pool;
        [SerializeField] private int poolIndex = -1;
        public SourceInterfacePlay(SourcePlaySelection play)
        {
            symbol = play.PlaySymbol; kind = play.Playbook; slot = play.SlotIndex; page = play.PageIndex;
            hasPool = play.Pool.HasValue; pool = play.Pool.GetValueOrDefault(); poolIndex = play.PoolIndex;
        }
        public SourcePlaySelection Selection()
        {
            return string.IsNullOrEmpty(symbol) ? default(SourcePlaySelection) :
                new SourcePlaySelection(symbol, kind, slot, page, hasPool ? (SourcePlayPool?)pool : null, poolIndex);
        }
    }

    public sealed class SourceInterfaceServices : ScriptableObject
    {
        public const string ResourceKey = SourceInterfacePackagePlan.ContactServicesResourceKey;
        public const string C2ResourceKey = SourceInterfacePackagePlan.C2ServicesResourceKey;
        public const string C2M1ResourceKey = SourceInterfacePackagePlan.C2M1ServicesResourceKey;
        public const string HistoricalSoundVmResourceKey = SourceInterfacePackagePlan.SoundVmServicesResourceKey;
        public const string SoundVmFunctionsResourceKey = "SourceData/sound-functions-safn1-4a557304";
        public const string ExpandedAnimationSha256 = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e";
        public const string PlaybookSha256 = "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3";
        [SerializeField] private SourceAnimationLibraryAsset originalAnimations;
        [SerializeField] private string resourceProfile = null;
        [SerializeField] private SourceHudAsset originalHud;
        [SerializeField] private TextAsset originalPlaybook;
        [SerializeField] private int homeTeamIndex;
        [SerializeField] private int awayTeamIndex = 1;
        [SerializeField] private int[] homeRosterIndices;
        [SerializeField] private int[] awayRosterIndices;
        [SerializeField] private SourceStationLayout stationLayout = SourceStationLayout.TwoStation;
        [SerializeField] private SourceInterfaceStation[] stations;
        [SerializeField] private TeamId firstPossession;
        [SerializeField] private SourceCaptainSlot captain;
        [SerializeField] private int timeAdjustment = 2;
        [SerializeField] private int difficulty = 5;
        [SerializeField] private bool continueAfterCabinetGate = true;
        [SerializeField] private SourceInterfacePlay homePlay;
        [SerializeField] private SourceInterfacePlay awayPlay;
        [SerializeField] private TeamId designatedHomeSide;
        [SerializeField] private int[] stationHeads;
        [SerializeField] private int pupBigBall;
        [SerializeField] private bool nativeGamepadsEnabled;
        [SerializeField] private TextAsset originalMovementBootstrap = null;
        [SerializeField] private string movementBootstrapSha256 = null;
        [SerializeField] private TextAsset originalSoundVmFunctions = null;
        [SerializeField] private string soundVmFunctionsSha256 = null;
        [SerializeField] private TextAsset originalC2Callbacks = null;
        [SerializeField] private TextAsset originalC2HitExtension = null;
        private ISourceAnimationLibrary animations;
        private SourcePlaybook playbook;
        private SourceInterfaceMovementData movementRuntime;
        private SourceMovementData movement;
        private SourceMovementRuntimeData movementData;
        private SourceInterfaceMovementBootstrap movementBootstrap;
        private SourceMovementSettings movementSettings;
        private SourceAudioFunctionData soundVmData;
        private SourceC2CallbackData c2Data;

        public SourceTeamResourceProfile Profile
        { get { return resourceProfile == null ? SourceTeamResourceProfile.Movement157 : SourceTeamResourceProfile.FromStableId(resourceProfile); } }
        public static string ResourceKeyFor(SourceTeamResourceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.IsContact281C2M1) return C2M1ResourceKey;
            if (profile.IsContact281C2) return C2ResourceKey;
            if (profile.IsContact272) return ResourceKey;
            throw new SourceInterfaceException("Select an explicit C1/C2 services generation; historical profiles are not aliases.");
        }
        public SourceC2CallbackData C2Data
        {
            get
            {
                if (!Profile.UsesC2Data) throw new SourceInterfaceException("Historical services have no implicit C2 bank-entry bootstrap.");
                if (c2Data == null)
                {
                    string callbackKey = Profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackResourceKey :
                        SourceInterfacePackagePlan.C2CallbackResourceKey;
                    TextAsset callbacks = Resources.Load<TextAsset>(callbackKey);
                    TextAsset hit = Resources.Load<TextAsset>(SourceInterfacePackagePlan.C2HitResourceKey);
                    if (callbacks == null || hit == null || !ReferenceEquals(callbacks, originalC2Callbacks) ||
                        !ReferenceEquals(hit, originalC2HitExtension))
                        throw new SourceInterfaceException("C2 services require the SAME canonical JSON/ONE7538 extension assets; copied/old resources are not aliases.");
                    c2Data = Profile.IsContact281C2M1 ? SourceInterfacePackagePlan.ReadC2M1Callbacks(callbacks.bytes, hit.bytes) :
                        SourceInterfacePackagePlan.ReadC2Callbacks(callbacks.bytes, hit.bytes);
                }
                return c2Data;
            }
        }
        public SourceHudAsset Hud { get { Validate(); return originalHud; } }
        public bool NativeGamepadsEnabled { get { return nativeGamepadsEnabled; } }
        public ISourceAnimationLibrary Animations { get { Validate(); return animations; } }
        public SourcePlaybook Playbook { get { Validate(); return playbook; } }
        public SourceMovementData Movement { get { LoadMovement(); return movement; } }
        public SourceInterfaceMovementData MovementRuntime { get { LoadMovement(); return movementRuntime; } }
        public SourceInterfaceMovementBootstrap MovementBootstrap
        {
            get
            {
                if (movementBootstrap == null)
                {
                    if (originalMovementBootstrap == null)
                        throw new SourceInterfaceException("M1 requires an explicit SHA-pinned movement settings/source-DIP bootstrap; no defaults are inferred.");
                    movementBootstrap = SourceInterfaceMovementBootstrap.Read(originalMovementBootstrap.bytes, movementBootstrapSha256);
                }
                return movementBootstrap;
            }
        }
        public SourceMovementRuntimeData MovementData
        {
            get
            {
                if (movementData == null)
                    movementData = SourceInterfaceMovementComposition.RuntimeData(Movement, MovementRuntime);
                return movementData;
            }
        }
        public SourceMovementSettings MovementSettings
        {
            get
            {
                if (movementSettings == null)
                    movementSettings = SourceInterfaceMovementComposition.Settings(MovementBootstrap);
                return movementSettings;
            }
        }
        public SourceAudioFunctionData SoundVmData
        {
            get
            {
                if (soundVmData == null)
                {
                    TextAsset canonical = Resources.Load<TextAsset>(SoundVmFunctionsResourceKey);
                    if (originalSoundVmFunctions == null || canonical == null ||
                        !ReferenceEquals(originalSoundVmFunctions, canonical) ||
                        soundVmFunctionsSha256 != GameSimulation.SourceSoundVmDataSha256 ||
                        originalSoundVmFunctions.bytes.Length != 1983)
                        throw new SourceInterfaceException("Source sound VM requires the canonical1983-byte SHA-pinned SAFN resource at Resources/" +
                            SoundVmFunctionsResourceKey + ". Old M1 services, muted selection or post-host RNG are not substitutes.");
                    soundVmData = SourceAudioFunctionData.Read(originalSoundVmFunctions.bytes, GameSimulation.SourceSoundVmDataSha256);
                }
                return soundVmData;
            }
        }
        public SourcePresentationSettings PresentationSettings
        { get { return new SourcePresentationSettings(designatedHomeSide, stationHeads, pupBigBall); } }

        public void Initialize(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads = false)
        {
            throw new SourceInterfaceException("This sound-VM resource needs explicit SHA-pinned movement/source-DIP and SAFN data. Use the ten-argument Initialize; old B1/M1 resources are not silently upgraded.");
        }

        public void Initialize(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads,
            TextAsset movementBootstrapText, string trustedMovementBootstrapSha256)
        {
            throw new SourceInterfaceException("This sound-VM resource requires explicit canonical SAFN bytes/pin. Use the ten-argument Initialize; old M1 resources are not silently upgraded.");
        }

        public void Initialize(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads,
            TextAsset movementBootstrapText, string trustedMovementBootstrapSha256,
            TextAsset soundVmFunctions, string trustedSoundVmSHA)
        {
            InitializeProfile(SourceTeamResourceProfile.Movement157, animationAsset, hud, playbookText, configuration,
                actualPresentation, enableNativeGamepads, movementBootstrapText, trustedMovementBootstrapSha256,
                soundVmFunctions, trustedSoundVmSHA);
        }

        public void InitializeContact272(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads,
            TextAsset movementBootstrapText, string trustedMovementBootstrapSha256,
            TextAsset soundVmFunctions, string trustedSoundVmSHA)
        {
            InitializeProfile(SourceTeamResourceProfile.Contact272, animationAsset, hud, playbookText, configuration,
                actualPresentation, enableNativeGamepads, movementBootstrapText, trustedMovementBootstrapSha256,
                soundVmFunctions, trustedSoundVmSHA);
        }

        public void InitializeContact281C2(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads,
            TextAsset movementBootstrapText, string trustedMovementBootstrapSha256,
            TextAsset soundVmFunctions, string trustedSoundVmSHA, TextAsset callbacks, TextAsset hitExtension)
        {
            InitializeProfile(SourceTeamResourceProfile.Contact281C2, animationAsset, hud, playbookText, configuration,
                actualPresentation, enableNativeGamepads, movementBootstrapText, trustedMovementBootstrapSha256,
                soundVmFunctions, trustedSoundVmSHA, callbacks, hitExtension);
        }

        public void InitializeContact281C2M1(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads,
            TextAsset movementBootstrapText, string trustedMovementBootstrapSha256,
            TextAsset soundVmFunctions, string trustedSoundVmSHA, TextAsset callbacks, TextAsset hitExtension)
        {
            InitializeProfile(SourceTeamResourceProfile.Contact281C2M1, animationAsset, hud, playbookText, configuration,
                actualPresentation, enableNativeGamepads, movementBootstrapText, trustedMovementBootstrapSha256,
                soundVmFunctions, trustedSoundVmSHA, callbacks, hitExtension);
        }

        private void InitializeProfile(SourceTeamResourceProfile profile, SourceAnimationLibraryAsset animationAsset,
            SourceHudAsset hud, TextAsset playbookText, SourceMatchConfiguration configuration,
            SourcePresentationSettings actualPresentation, bool enableNativeGamepads, TextAsset movementBootstrapText,
            string trustedMovementBootstrapSha256, TextAsset soundVmFunctions, string trustedSoundVmSHA,
            TextAsset callbacks = null, TextAsset hitExtension = null)
        {
            if (configuration == null || actualPresentation == null)
                throw new ArgumentNullException(configuration == null ? nameof(configuration) : nameof(actualPresentation));
            if (animations != null || configuration.Profile != SourceSimulationProfile.OriginalRelease57Hz)
                throw new SourceInterfaceException("Original match service resources require one immutable explicit 57-Hz configuration.");
            if (movementBootstrapText == null)
                throw new SourceInterfaceException("An explicit pinned M1 movement/source-DIP bootstrap is required.");
            SourceInterfaceMovementBootstrap actualBootstrap =
                SourceInterfaceMovementBootstrap.Read(movementBootstrapText.bytes, trustedMovementBootstrapSha256);
            if (soundVmFunctions == null || trustedSoundVmSHA != GameSimulation.SourceSoundVmDataSha256)
                throw new SourceInterfaceException("The original sound VM needs explicit SAFN bytes and the frozen Core content pin.");
            resourceProfile = profile.StableId;
            if (profile.UsesC2Data)
            {
                if (callbacks == null || hitExtension == null)
                    throw new SourceInterfaceException("C2 initialization requires actual canonical callback JSON and ONE7538 extension.");
                if (profile.IsContact281C2M1)
                    SourceInterfacePackagePlan.ReadC2M1Callbacks(callbacks.bytes, hitExtension.bytes);
                else SourceInterfacePackagePlan.ReadC2Callbacks(callbacks.bytes, hitExtension.bytes);
                originalC2Callbacks = callbacks; originalC2HitExtension = hitExtension;
            }
            originalSoundVmFunctions = soundVmFunctions; soundVmFunctionsSha256 = trustedSoundVmSHA;
            originalMovementBootstrap = movementBootstrapText; movementBootstrapSha256 = trustedMovementBootstrapSha256;
            movementBootstrap = actualBootstrap;
            originalAnimations = animationAsset; originalHud = hud; originalPlaybook = playbookText;
            homeTeamIndex = configuration.HomeTeam.TeamIndex; awayTeamIndex = configuration.AwayTeam.TeamIndex;
            homeRosterIndices = new int[7]; awayRosterIndices = new int[7];
            for (int i = 0; i < 7; i++)
            { homeRosterIndices[i] = configuration.HomeTeam.RosterIndices[i]; awayRosterIndices[i] = configuration.AwayTeam.RosterIndices[i]; }
            stations = new SourceInterfaceStation[configuration.Stations.Count];
            for (int i = 0; i < stations.Length; i++)
            {
                stations[i] = new SourceInterfaceStation(configuration.Stations[i]);
            }
            stationLayout = configuration.StationLayout; firstPossession = configuration.FirstPossession;
            captain = configuration.Captain; timeAdjustment = configuration.TimeAdjustment; difficulty = configuration.Difficulty;
            continueAfterCabinetGate = configuration.ContinueAfterCabinetGate;
            homePlay = new SourceInterfacePlay(configuration.HomePlay); awayPlay = new SourceInterfacePlay(configuration.AwayPlay);
            designatedHomeSide = actualPresentation.DesignatedHomeSide; pupBigBall = actualPresentation.PupBigBall;
            stationHeads = new int[4];
            for (int i = 0; i < 4; i++) stationHeads[i] = actualPresentation.StationHeads[i];
            nativeGamepadsEnabled = enableNativeGamepads; Validate();
        }

        public SourceMatchConfiguration Configuration()
        {
            if (homeRosterIndices == null || awayRosterIndices == null || stations == null)
                throw new SourceInterfaceException("Original match configuration needs explicit actual team/static-roster/station selections.");
            var configured = new SourceStationConfiguration[stations.Length];
            for (int i = 0; i < configured.Length; i++)
            {
                if (stations[i] == null) throw new SourceInterfaceException("A configured original human station is missing.");
                configured[i] = stations[i].Configuration();
            }
            return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, stationLayout,
                new SourceTeamSelection(homeTeamIndex, homeRosterIndices), new SourceTeamSelection(awayTeamIndex, awayRosterIndices),
                configured, firstPossession, captain, timeAdjustment, difficulty, continueAfterCabinetGate,
                homePlay == null ? default(SourcePlaySelection) : homePlay.Selection(),
                awayPlay == null ? default(SourcePlaySelection) : awayPlay.Selection());
        }

        public GameSimulation CreateSimulation(SourcePlayerRig prefab,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMode kickMeters = SourceKickMeterMode.Disabled)
        {
            if (fireEffects != null && !Profile.IsContact281C2M1)
                throw new SourceInterfaceException("source-fire=portable requires the explicit actual M1 resource profile.");
            if (compatibility != SourceGameplayCompatibility.StrictSource
                && (compatibility != SourceGameplayCompatibility.Portable || !Profile.IsContact281C2M1))
                throw new SourceInterfaceException("Portable gameplay requires the explicit M1 resource profile.");
            if (!Profile.IsContact272 && !Profile.UsesC2Data)
                throw new SourceInterfaceException("This engine requires explicit Contact272 or Contact281C2 services. Historical157 resources are not silently upgraded or routed to old contact physics.");
            Validate();
            if (kickMeters != SourceKickMeterMode.Disabled && kickMeters != SourceKickMeterMode.PortableAuthored)
                throw new ArgumentOutOfRangeException(nameof(kickMeters));
            SourceKickMeterMetrics kickMetrics = kickMeters == SourceKickMeterMode.PortableAuthored
                ? originalHud.Data.CreatePortableAuthoredKickMeterMetrics() : null;
            if (prefab == null) throw new SourceInterfaceException("Source match geometry needs the actual imported player reference rig.");
            if (prefab.JointCount != SourceTiming.JointCount)
                throw new SourceInterfaceException("Source catch geometry needs all fifteen actual imported reference pivots.");
            var pivots = new SourceVector3[SourceTiming.JointCount];
            for (int i = 0; i < pivots.Length; i++) pivots[i] = prefab.GetUnityReferencePivot(i);
            SourceTeamPresentationAsset teams = SourcePlayerProvider.ResolveRequiredTeamPresentation(prefab);
            if (prefab.StableId != Profile.StableId || !ReferenceEquals(prefab, teams.Template))
                throw new SourceInterfaceException("Contact272 services must use the SAME canonical team template/rig, not another profile or a copied asset.");
            Profile.RequireImpact(SourcePlayerProvider.ResolveRequiredCinematicLibrary(prefab));
            Debug.LogWarning("Original source services use explicit ExternalUnverified legacy-LCG preview RNG when no oracle factory is supplied. Historical libc/scheduler/audio draw parity is NOT established.");
            if (Profile.UsesC2Data)
            {
                SourceC2Bootstrap bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data);
                Debug.LogWarning("Explicit C2 original sound-bank entry consumes announcer then taunt draws BEFORE initial AI. Pre-boundary boot RNG and old golden trajectories are not claimed equivalent.");
                if (Profile.IsContact281C2M1)
                    return SourceC2Composition.CreateM1Simulation(animations, pivots, playbook, Configuration(),
                        teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData, bootstrap,
                        compatibility: compatibility, fireEffects: fireEffects, kickMeterMetrics: kickMetrics);
                return SourceC2Composition.CreateSimulation(animations, pivots, playbook, Configuration(),
                    teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData, bootstrap,
                    kickMeterMetrics: kickMetrics);
            }
            return SourceContactComposition.CreateSimulation(animations, pivots, playbook, Configuration(),
                teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData,
                kickMeterMetrics: kickMetrics);
        }

        private void Validate()
        {
            if (animations != null) return;
            SourceTeamResourceProfile profile = Profile;
            SourceAnimationLibraryAsset canonical = Resources.Load<SourceAnimationLibraryAsset>(
                profile.AnimationResourceKey);
            if (canonical == null || !ReferenceEquals(originalAnimations, canonical))
                throw new SourceInterfaceException("Source services must reference the SAME canonical " + profile.ClipCount + " animation asset at Resources/" +
                    profile.AnimationResourceKey + "; matching hashes on another asset are not aliases.");
            if (originalAnimations == null || originalAnimations.InventorySha256 != profile.AnimationSha256 ||
                originalHud == null || originalPlaybook == null ||
                AuthorizedAssetRoot.Sha256(originalPlaybook.bytes) != PlaybookSha256)
                throw new SourceInterfaceException("Source mode requires Resources/" + ResourceKey +
                    " with its explicit " + profile.ClipCount + "-clip library, actual converted playbook and original HUD. Historical libraries, ignored raw files and runtime network loading are not substitutes.");
            SourceInterfaceMovementBootstrap bootstrap = MovementBootstrap;
            if (bootstrap.ContentSha256 != movementBootstrapSha256)
                throw new SourceInterfaceException("The explicit M1 bootstrap pin changed after configuration.");
            if (SoundVmData.PayloadSha256 != GameSimulation.SourceSoundVmDataSha256)
                throw new SourceInterfaceException("The immutable source sound-function data pin changed.");
            if (profile.UsesC2Data && C2Data.AnimationManifestSha256 != profile.AnimationSha256)
                throw new SourceInterfaceException("C2 bootstrap data and actual animation profile differ.");
            ISourceAnimationLibrary actual = originalAnimations.Load();
            profile.RequireLibrary(actual);
            SourcePlaybook book;
            using (var reader = new StringReader(originalPlaybook.text)) book = SourcePlaybook.LoadConverted(reader);
            originalHud.Load(); Configuration();
            animations = actual; playbook = book;
        }

        private void LoadMovement()
        {
            if (movementRuntime != null) return;
            TextAsset movementAsset = Resources.Load<TextAsset>(SourceInterfaceMovementData.ResourceKey);
            if (movementAsset == null)
                throw new SourceInterfaceException("Missing original runtime-safe movement Resources/" + SourceInterfaceMovementData.ResourceKey +
                    ". Package tools/movement runtime schema/pin, never the test-only Responses export.");
            SourceInterfaceMovementData actualMovement = SourceInterfaceMovementData.Read(movementAsset.bytes,
                SourceInterfaceMovementData.ContentSha256);
            movement = new SourceMovementData(actualMovement.Constants, actualMovement.Tables);
            movementRuntime = actualMovement;
        }
    }
}
