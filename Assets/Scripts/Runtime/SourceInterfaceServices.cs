using System;
using System.IO;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using UnityEngine;

namespace Blitz.Runtime
{
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
        public const string ResourceKey = "BlitzSourceInterface/original-match-services";
        public const string ExpandedAnimationSha256 = "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274";
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
        private ISourceAnimationLibrary animations;
        private SourcePlaybook playbook;
        private SourceInterfaceMovementData movementRuntime;
        private SourceMovementData movement;

        public SourceHudAsset Hud { get { Validate(); return originalHud; } }
        public bool NativeGamepadsEnabled { get { return nativeGamepadsEnabled; } }
        public ISourceAnimationLibrary Animations { get { Validate(); return animations; } }
        public SourcePlaybook Playbook { get { Validate(); return playbook; } }
        public SourceMovementData Movement { get { LoadMovement(); return movement; } }
        public SourceInterfaceMovementData MovementRuntime { get { LoadMovement(); return movementRuntime; } }
        public SourcePresentationSettings PresentationSettings
        { get { return new SourcePresentationSettings(designatedHomeSide, stationHeads, pupBigBall); } }

        public void Initialize(SourceAnimationLibraryAsset animationAsset, SourceHudAsset hud, TextAsset playbookText,
            SourceMatchConfiguration configuration, SourcePresentationSettings actualPresentation, bool enableNativeGamepads = false)
        {
            if (configuration == null || actualPresentation == null)
                throw new ArgumentNullException(configuration == null ? nameof(configuration) : nameof(actualPresentation));
            if (animations != null || configuration.Profile != SourceSimulationProfile.OriginalRelease57Hz)
                throw new SourceInterfaceException("Original match service resources require one immutable explicit 57-Hz configuration.");
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
            SourceTeamPresentationAsset teams = SourcePlayerProvider.ResolveRequiredTeamPresentation();
            Debug.LogWarning("Original source services use explicit ExternalUnverified legacy-LCG preview RNG when no oracle factory is supplied. Historical libc/scheduler/audio draw parity is NOT established.");
            return new GameSimulation(animations, geometry, playbook, Configuration(), null, 1,
                rosters: teams.Rosters, presentation: PresentationSettings);
        }

        private void Validate()
        {
            if (animations != null) return;
            if (originalAnimations == null || originalAnimations.InventorySha256 != ExpandedAnimationSha256 ||
                originalHud == null || originalPlaybook == null ||
                AuthorizedAssetRoot.Sha256(originalPlaybook.bytes) != PlaybookSha256)
                throw new SourceInterfaceException("Source mode requires Resources/" + ResourceKey +
                    " with the NEW pinned117 library, actual converted playbook and original HUD. The immutable56 library, ignored raw files and runtime network loading are not substitutes.");
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
