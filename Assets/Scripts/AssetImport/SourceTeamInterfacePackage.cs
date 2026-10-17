using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.AssetImport
{
    public sealed class SourceTeamResourceProfile
    {
        public const string MovementStableId = "original-teams-movement157";
        public const string MovementAnimationKey = "SourceData/local-plyr266-movement157-f0ae9395";
        public const string MovementAnimationSha256 = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e";
        public static readonly SourceTeamResourceProfile B1Final117 = new SourceTeamResourceProfile(
            "original-teams", "BlitzSourcePlayers/original-teams/animation-library",
            SourceInterfacePackagePlan.AnimationSha256, 117, 10288, false);
        public static readonly SourceTeamResourceProfile Movement157 = new SourceTeamResourceProfile(
            MovementStableId, MovementAnimationKey, MovementAnimationSha256, 157, 13490, true);

        public string StableId { get; }
        public string AnimationResourceKey { get; }
        public string AnimationSha256 { get; }
        public int ClipCount { get; }
        public int FrameCount { get; }
        public bool IsMovement157 { get; }
        public string TeamResourceKey { get { return "BlitzSourcePlayers/" + StableId + "/team-presentation"; } }
        public string ImpactResourceKey { get { return "BlitzSourcePlayers/" + StableId + "/impact-library"; } }
        public string Destination { get { return "Assets/Imported/" + StableId; } }
        public string TeamResourceFolder { get { return Destination + "/Resources/BlitzSourcePlayers/" + StableId; } }
        public string AnimationAssetPath { get { return Destination + "/Resources/" + AnimationResourceKey + ".asset"; } }

        private SourceTeamResourceProfile(string stableId, string animationKey, string hash, int clips, int frames, bool movement)
        {
            StableId = stableId; AnimationResourceKey = animationKey; AnimationSha256 = hash;
            ClipCount = clips; FrameCount = frames; IsMovement157 = movement;
        }

        public static SourceTeamResourceProfile FromStableId(string stableId)
        {
            if (stableId == B1Final117.StableId) return B1Final117;
            if (stableId == Movement157.StableId) return Movement157;
            throw new SourceInterfaceException("Unknown original team resource profile; no117/157 key alias: " + stableId);
        }

        public void RequireInventory(string manifestSha256, int clips, int frames)
        {
            if (manifestSha256 != AnimationSha256 || clips != ClipCount || frames != FrameCount)
                throw new SourceInterfaceException("Original " + StableId + " requires its exact " + ClipCount +
                    "-clip/" + FrameCount + "-frame manifest; old or future inventories are not aliases.");
        }
    }

    public sealed class SourceInterfacePackageConfiguration
    {
        public SourceMatchConfiguration Match { get; }
        public SourcePresentationSettings Presentation { get; }
        public bool NativeGamepadsEnabled { get; }
        internal SourceInterfacePackageConfiguration(SourceMatchConfiguration match,
            SourcePresentationSettings presentation, bool gamepads)
        { Match = match; Presentation = presentation; NativeGamepadsEnabled = gamepads; }
    }

    public sealed class SourceInterfacePackageTexture
    {
        private readonly byte[] bytes;
        public SourceInterfaceTexture Definition { get; }
        internal SourceInterfacePackageTexture(SourceInterfaceTexture definition, byte[] data)
        { Definition = definition; bytes = (byte[])data.Clone(); }
        public byte[] CopyBytes() { return (byte[])bytes.Clone(); }
    }

    // Kept in the runtime assembly to reuse its strict parser, not an Editor-only parser clone.
    public sealed class SourceInterfacePackagePlan
    {
        public const string HudSha256 = "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435";
        public const string PlaybookSha256 = "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3";
        public const string AnimationSha256 = "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274";
        public const string HudResourceKey = "BlitzSourceInterface/original-interface";
        public const string ServicesResourceKey = "BlitzSourceInterface/original-match-services";
        public const string MovementServicesResourceKey = "BlitzSourceInterface/original-movement157-match-services";
        public const string HudShaderPath = "Assets/Scripts/Presentation/SourceHud.shader";
        public const string HudShaderSha256 = "d43a25cb309f2a6ae511aecbbc39c5272c8b784d7e782fc794d2eed33a2a0369";
        private readonly byte[] manifest, playbook, configuration;
        private readonly byte[] movement, bootstrap;
        public SourceInterfaceData Hud { get; }
        public SourcePlaybook Playbook { get; }
        public SourceInterfacePackageConfiguration Configuration { get; }
        public ReadOnlyCollection<SourceInterfacePackageTexture> Textures { get; }
        public string ConfigurationSha256 { get; }
        public SourceTeamResourceProfile Profile { get; }
        public SourceInterfaceMovementData MovementRuntime { get; }
        public SourceInterfaceMovementBootstrap MovementBootstrap { get; }
        public string BootstrapSha256 { get; }
        public string SelectedServicesResourceKey
        { get { return Profile.IsMovement157 ? MovementServicesResourceKey : ServicesResourceKey; } }

        private SourceInterfacePackagePlan(byte[] manifest, byte[] book, byte[] config, string configHash,
            SourceInterfaceData hud, SourcePlaybook playbook, SourceInterfacePackageConfiguration configuration,
            SourceInterfacePackageTexture[] textures, SourceTeamResourceProfile profile = null,
            byte[] movement = null, byte[] bootstrap = null, string bootstrapHash = null)
        {
            this.manifest = (byte[])manifest.Clone(); this.playbook = (byte[])book.Clone();
            this.configuration = (byte[])config.Clone(); ConfigurationSha256 = configHash;
            Hud = hud; Playbook = playbook; Configuration = configuration;
            Textures = Array.AsReadOnly(textures);
            Profile = profile ?? SourceTeamResourceProfile.B1Final117;
            if (Profile.IsMovement157)
            {
                if (movement == null || bootstrap == null)
                    throw new SourceInterfaceException("Movement157 packaging requires actual movement data and explicit bootstrap bytes.");
                this.movement = (byte[])movement.Clone(); this.bootstrap = (byte[])bootstrap.Clone();
                MovementRuntime = SourceInterfaceMovementData.Read(this.movement, SourceInterfaceMovementData.ContentSha256);
                MovementBootstrap = SourceInterfaceMovementBootstrap.Read(this.bootstrap, bootstrapHash);
                BootstrapSha256 = bootstrapHash;
            }
        }

        public byte[] CopyManifest() { return (byte[])manifest.Clone(); }
        public byte[] CopyPlaybook() { return (byte[])playbook.Clone(); }
        public byte[] CopyConfiguration() { return (byte[])configuration.Clone(); }
        public byte[] CopyMovement()
        {
            Require(Profile.IsMovement157, "A B1 package has no implicit movement data.");
            return (byte[])movement.Clone();
        }
        public byte[] CopyBootstrap()
        {
            Require(Profile.IsMovement157, "A B1 package has no inferred movement settings/DIP bootstrap.");
            return (byte[])bootstrap.Clone();
        }

        public static SourceInterfacePackagePlan CreateMovement157(string hudRoot, string manifestPath, string expectedHudHash,
            string playbookRoot, string playbookPath, string expectedPlaybookHash,
            string configurationRoot, string configurationPath, string expectedConfigurationHash,
            string movementRoot, string movementPath, string expectedMovementHash,
            string bootstrapRoot, string bootstrapPath, string expectedBootstrapHash)
        {
            Require(expectedMovementHash == SourceInterfaceMovementData.ContentSha256,
                "Movement157 packaging requires the actual pinned runtime movement data, not test Responses.");
            SourceInterfacePackagePlan shared = Create(hudRoot, manifestPath, expectedHudHash,
                playbookRoot, playbookPath, expectedPlaybookHash, configurationRoot, configurationPath, expectedConfigurationHash);
            byte[] movement = new AuthorizedAssetRoot(movementRoot).ReadBytes(movementPath, 128 * 1024);
            byte[] bootstrap = new AuthorizedAssetRoot(bootstrapRoot).ReadBytes(bootstrapPath, 64 * 1024);
            return new SourceInterfacePackagePlan(shared.CopyManifest(), shared.CopyPlaybook(), shared.CopyConfiguration(),
                expectedConfigurationHash, shared.Hud, shared.Playbook, shared.Configuration, shared.Textures.ToArray(),
                SourceTeamResourceProfile.Movement157, movement, bootstrap, expectedBootstrapHash);
        }

        public static SourceInterfacePackagePlan Create(string hudRoot, string manifestPath, string expectedHudHash,
            string playbookRoot, string playbookPath, string expectedPlaybookHash,
            string configurationRoot, string configurationPath, string expectedConfigurationHash)
        {
            Require(expectedHudHash == HudSha256, "Complete original HUD manifest pin is required; old subset is not an alias.");
            Require(expectedPlaybookHash == PlaybookSha256, "Actual immutable original P1 catalog pin is required.");
            var hudInput = new AuthorizedAssetRoot(hudRoot);
            var bookInput = new AuthorizedAssetRoot(playbookRoot);
            var configInput = new AuthorizedAssetRoot(configurationRoot);
            byte[] manifest = hudInput.ReadBytes(manifestPath, 4 * 1024 * 1024);
            byte[] book = bookInput.ReadBytes(playbookPath, 4 * 1024 * 1024);
            byte[] config = configInput.ReadBytes(configurationPath, 64 * 1024);
            Require(AuthorizedAssetRoot.Sha256(book) == expectedPlaybookHash, "Original P1 catalog content pin mismatch.");
            SourceInterfacePackageConfiguration selected = ReadConfiguration(config, expectedConfigurationHash);
            SourceInterfaceData hud = SourceInterfaceData.Read(manifest, expectedHudHash);
            SourcePlaybook parsedBook;
            using (var reader = new StringReader(AuthorizedAssetRoot.DecodeUtf8(book, playbookPath)))
                parsedBook = SourcePlaybook.LoadConverted(reader);
            ValidatePlay(parsedBook, selected.Match.HomePlay); ValidatePlay(parsedBook, selected.Match.AwayPlay);
            var textures = new List<SourceInterfacePackageTexture>();
            foreach (KeyValuePair<string, SourceInterfaceTexture> pair in hud.Textures)
            {
                SourceInterfaceTexture definition = pair.Value;
                byte[] rgba = hudInput.ReadBytes(definition.Path, PlayerImportLimits.TextureBytes);
                byte[] verified = hud.RequireTextureBytes(pair.Key, rgba);
                textures.Add(new SourceInterfacePackageTexture(definition, verified));
            }
            Require(textures.Count == 3 && hud.Textures.ContainsKey("interface") &&
                hud.Textures.ContainsKey("playsel8") && hud.Textures.ContainsKey("psarrow1"),
                "The complete original interface/playsel8/psarrow1 texture identities are required.");
            textures.Sort((a, b) => string.CompareOrdinal(a.Definition.Id, b.Definition.Id));
            return new SourceInterfacePackagePlan(manifest, book, config, expectedConfigurationHash,
                hud, parsedBook, selected, textures.ToArray());
        }

        public static SourceInterfacePackageConfiguration ReadConfiguration(byte[] bytes, string expectedHash)
        {
            Require(bytes != null && bytes.Length > 1 && bytes.Length <= 64 * 1024 &&
                IsHash(expectedHash) && AuthorizedAssetRoot.Sha256(bytes) == expectedHash,
                "Explicit source configuration content pin mismatch.");
            try
            {
                object parsed = RosterJson.Parse(bytes);
                RosterObject root = RosterJson.Object(parsed, "explicit source configuration");
                Require(root.Text("schema", 64) == "original-source-match-package/v1",
                    "Unknown explicit source package configuration schema.");
                var profile = (SourceSimulationProfile)root.Integer("profile", 1, 1);
                var layout = (SourceStationLayout)root.Integer("station_layout", 2, 4);
                SourceTeamSelection home = Team(root.Object("home_team")), away = Team(root.Object("away_team"));
                object[] rawStations = root.Array("stations");
                var stations = new SourceStationConfiguration[rawStations.Length];
                for (int i = 0; i < stations.Length; i++)
                {
                    RosterObject station = RosterJson.Object(rawStations[i], "explicit source station");
                    int id = station.Integer("id", 0, 3), team = station.Integer("team", 0, 1);
                    int actor = station.Integer("initial_player_id", -1, 13);
                    SourceRosterIdentity identity = default(SourceRosterIdentity);
                    if (IsNull(rawStations[i], "profile")) station.Null("profile");
                    else
                    {
                        RosterObject selected = station.Object("profile");
                        identity = new SourceRosterIdentity(selected.Integer("team", 0, 30), selected.Integer("record", 0, 15));
                        selected.Done();
                    }
                    station.Done();
                    stations[i] = new SourceStationConfiguration(id, (TeamId)team, actor, identity);
                }
                var possession = (TeamId)root.Integer("first_possession", 0, 1);
                var captain = (SourceCaptainSlot)root.Integer("captain", 0, 1);
                int adjustment = root.Integer("time_adjustment", 0, 4), difficulty = root.Integer("difficulty", 0, 10);
                bool continuation = root.Boolean("continue_after_cabinet_gate");
                SourcePlaySelection homePlay = Play(root, parsed, "home_play"), awayPlay = Play(root, parsed, "away_play");
                RosterObject presentation = root.Object("presentation");
                var settings = new SourcePresentationSettings((TeamId)presentation.Integer("designated_home_side", 0, 1),
                    presentation.Integers("station_heads", 4, -1, 19), presentation.Integer("pup_bigball", int.MinValue, int.MaxValue));
                presentation.Done();
                bool gamepads = root.Boolean("native_gamepads_enabled");
                root.Done();
                var match = new SourceMatchConfiguration(profile, layout, home, away, stations, possession,
                    captain, adjustment, difficulty, continuation, homePlay, awayPlay);
                return new SourceInterfacePackageConfiguration(match, settings, gamepads);
            }
            catch (SourceRosterException error)
            { throw new SourceInterfaceException("Invalid explicit source package configuration: " + error.Message); }
            catch (ArgumentException error)
            { throw new SourceInterfaceException("Invalid explicit source package configuration: " + error.Message); }
        }

        public static void RequireOutputIdentity(string assetPath, string resourceKey)
        {
            AuthorizedAssetRoot.ValidateRelativePath(assetPath);
            Require((resourceKey == HudResourceKey || resourceKey == ServicesResourceKey || resourceKey == MovementServicesResourceKey) &&
                assetPath.StartsWith("Assets/Imported/", StringComparison.Ordinal) &&
                assetPath.EndsWith("/Resources/" + resourceKey + ".asset", StringComparison.Ordinal),
                "Generated original resource must remain ignored under Assets/Imported with its exact Resources identity.");
        }

        public static void RequireHudShaderSource(string projectRoot)
        {
            byte[] shader = new AuthorizedAssetRoot(projectRoot).ReadBytes(HudShaderPath, 64 * 1024);
            Require(AuthorizedAssetRoot.Sha256(shader) == HudShaderSha256, "Actual original HUD shader source pin mismatch.");
        }

        private static SourceTeamSelection Team(RosterObject team)
        {
            var result = new SourceTeamSelection(team.Integer("team", 0, 30), team.Integers("records", 7, 0, 15));
            team.Done(); return result;
        }
        private static bool IsNull(object raw, string key)
        {
            object value;
            return ((Dictionary<string, object>)raw).TryGetValue(key, out value) && value == null;
        }
        private static SourcePlaySelection Play(RosterObject root, object raw, string key)
        {
            if (IsNull(raw, key)) { root.Null(key); return default(SourcePlaySelection); }
            object rawPlay;
            ((Dictionary<string, object>)raw).TryGetValue(key, out rawPlay);
            RosterObject play = root.Object(key);
            string symbol = play.Text("symbol", 128);
            var kind = (SourcePlaybookKind)play.Integer("kind", 0, 1);
            int slot = play.Integer("slot", -1, int.MaxValue), page = play.Integer("page", -1, int.MaxValue);
            var playValues = (Dictionary<string, object>)rawPlay;
            SourcePlayPool? pool;
            if (IsNull(playValues, "pool")) { play.Null("pool"); pool = null; }
            else pool = (SourcePlayPool)play.Integer("pool", 0, 12);
            int ordinal = play.Integer("pool_index", -1, int.MaxValue);
            play.Done();
            return new SourcePlaySelection(symbol, kind, slot, page, pool, ordinal);
        }
        private static void ValidatePlay(SourcePlaybook book, SourcePlaySelection selection)
        {
            if (!selection.IsSpecified) return;
            Require(book.Plays.ContainsKey(selection.PlaySymbol), "Configured original play symbol is absent from actual P1 catalog.");
        }
        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new SourceInterfaceException(message); }
    }
}
