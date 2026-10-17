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
        public const string ResourceKey = "BlitzSourceInterface/original-movement157-match-services";
        public const string ExpandedAnimationSha256 = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e";
        public const string PlaybookSha256 = "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3";
        [SerializeField] private SourceAnimationLibraryAsset originalAnimations;
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
        private ISourceAnimationLibrary animations;
        private SourcePlaybook playbook;
        private SourceInterfaceMovementData movementRuntime;
        private SourceMovementData movement;
        private SourceMovementRuntimeData movementData;
        private SourceInterfaceMovementBootstrap movementBootstrap;
        private SourceMovementSettings movementSettings;

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
        public SourcePresentationSettings PresentationSettings
        { get { return new SourcePresentationSettings(designatedHomeSide, stationHeads, pupBigBall); } }

        public void Initialize(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads = false)
        {
            throw new SourceInterfaceException("This M1 resource needs explicit SHA-pinned movement settings/source-DIP data. Use the eight-argument Initialize; a B1/117 resource is not an M1 fallback.");
        }

        public void Initialize(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads,
            TextAsset movementBootstrapText, string trustedMovementBootstrapSha256)
        {
            if (configuration == null || actualPresentation == null)
                throw new ArgumentNullException(configuration == null ? nameof(configuration) : nameof(actualPresentation));
            if (animations != null || configuration.Profile != SourceSimulationProfile.OriginalRelease57Hz)
                throw new SourceInterfaceException("Original match service resources require one immutable explicit 57-Hz configuration.");
            if (movementBootstrapText == null)
                throw new SourceInterfaceException("An explicit pinned M1 movement/source-DIP bootstrap is required.");
            SourceInterfaceMovementBootstrap actualBootstrap =
                SourceInterfaceMovementBootstrap.Read(movementBootstrapText.bytes, trustedMovementBootstrapSha256);
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

        public GameSimulation CreateSimulation(SourcePlayerRig prefab)
        {
            Validate();
            if (prefab == null) throw new SourceInterfaceException("Source match geometry needs the actual imported player reference rig.");
            if (prefab.JointCount != SourceTiming.JointCount)
                throw new SourceInterfaceException("Source catch geometry needs all fifteen actual imported reference pivots.");
            var pivots = new SourceVector3[SourceTiming.JointCount];
            for (int i = 0; i < pivots.Length; i++) pivots[i] = prefab.GetUnityReferencePivot(i);
            ISourceActorGeometry geometry = new SourceCatchGeometry(animations, pivots);
            SourceTeamPresentationAsset teams = SourcePlayerProvider.ResolveRequiredTeamPresentation(prefab);
            Debug.LogWarning("Original source services use explicit ExternalUnverified legacy-LCG preview RNG when no oracle factory is supplied. Historical libc/scheduler/audio draw parity is NOT established.");
            return new GameSimulation(animations, geometry, playbook, Configuration(), null, 1,
                rosters: teams.Rosters, presentation: PresentationSettings,
                movementData: MovementData, movementSettings: MovementSettings);
        }

        private void Validate()
        {
            if (animations != null) return;
            SourceAnimationLibraryAsset canonical = Resources.Load<SourceAnimationLibraryAsset>(
                SourcePlayerProvider.MovementAnimationResourceKey);
            if (canonical == null || !ReferenceEquals(originalAnimations, canonical))
                throw new SourceInterfaceException("M1 services must reference the SAME canonical157 animation asset at Resources/" +
                    SourcePlayerProvider.MovementAnimationResourceKey + "; matching hashes on another asset are not aliases.");
            if (originalAnimations == null || originalAnimations.InventorySha256 != ExpandedAnimationSha256 ||
                originalHud == null || originalPlaybook == null ||
                AuthorizedAssetRoot.Sha256(originalPlaybook.bytes) != PlaybookSha256)
                throw new SourceInterfaceException("Source mode requires Resources/" + ResourceKey +
                    " with the NEW pinned157 library, actual converted playbook and original HUD. Immutable56/117/156 libraries, ignored raw files and runtime network loading are not substitutes.");
            SourceInterfaceMovementBootstrap bootstrap = MovementBootstrap;
            if (bootstrap.ContentSha256 != movementBootstrapSha256)
                throw new SourceInterfaceException("The explicit M1 bootstrap pin changed after configuration.");
            ISourceAnimationLibrary actual = originalAnimations.Load();
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
