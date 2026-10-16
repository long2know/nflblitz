using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public sealed class SourceRosterException : Exception
    {
        public SourceRosterException(string message) : base(message) { }
    }

    public enum SourceBodySize { Normal = 0, Fat = 1 }
    public enum SourceSkinSelector { Black = 0, White = 1 }
    public enum SourceUniformVariant { Home = 0, Away = 1 }
    public enum SourceRosterPosition
    {
        Quarterback = 0, WideReceiver = 1, RunningBack = 2, TightEnd = 3, OffensiveLine = 4,
        Kicker = 5, Linebacker = 6, Cornerback = 7, DefensiveLine = 8, Safety = 9
    }
    [Flags]
    public enum SourceHeldBallModel { None = 0, Left = 4, Right = 8, Both = 12 }

    public readonly struct SourceRosterRatings
    {
        public int Speed { get; }
        public int Power { get; }
        public int Quarterback { get; }
        public int Receiver { get; }
        public int Offense { get; }
        public int Defense { get; }
        internal SourceRosterRatings(int[] values)
        {
            Speed = values[0]; Power = values[1]; Quarterback = values[2];
            Receiver = values[3]; Offense = values[4]; Defense = values[5];
        }
    }

    public readonly struct SourceTeamRatings
    {
        public int Passing { get; }
        public int Rushing { get; }
        public int Offense { get; }
        public int Defense { get; }
        public int SpecialTeams { get; }
        internal SourceTeamRatings(int[] values)
        {
            Passing = values[0]; Rushing = values[1]; Offense = values[2];
            Defense = values[3]; SpecialTeams = values[4];
        }
    }

    public readonly struct SourceRosterEnumValue
    {
        public string Raw { get; }
        public string Symbol { get; }
        public int Value { get; }
        internal SourceRosterEnumValue(string raw, string symbol, int value)
        { Raw = raw; Symbol = symbol; Value = value; }
    }

    public sealed class SourceRosterPlayer
    {
        public SourceRosterProfile Profile { get; }
        public int SourceStaticId { get; }
        public int SourceLine { get; }
        public ReadOnlyCollection<string> RawFields { get; }
        public SourceRosterRatings Ratings { get; }
        public string ScaleExpression { get; }
        public SourceRosterEnumValue SizeSelector { get; }
        public SourceRosterEnumValue SkinSelector { get; }
        public SourceRosterEnumValue PositionSelector { get; }
        public SourceBodySize BodySize { get { return (SourceBodySize)SizeSelector.Value; } }
        public SourceSkinSelector Skin { get { return (SourceSkinSelector)SkinSelector.Value; } }
        public SourceRosterPosition Position { get { return (SourceRosterPosition)PositionSelector.Value; } }
        public string NumberExpression { get; }
        public int JerseyNumber { get; }
        public ReadOnlyCollection<int> JerseyDigitIndices { get; }
        public string MugshotSymbol { get; }
        public string SelectionNameSymbol { get; }
        public string HudNameSymbol { get; }
        public string AnnouncerExpression { get; }
        public int AnnouncerIndex { get; }
        public int? HeightInches { get { return null; } }
        public int? WeightPounds { get { return null; } }
        public int? StaticFlags { get { return null; } }
        public int? StaticHeadSelector { get { return null; } }

        internal SourceRosterPlayer(SourceRosterProfile profile, int staticId, int line, string[] raw,
            int[] ratings, string scale, SourceRosterEnumValue size, SourceRosterEnumValue skin,
            SourceRosterEnumValue position, string number, int jerseyNumber, int[] digits,
            string mugshot, string selectionName, string hudName, string announcer, int announcerIndex)
        {
            Profile = profile; SourceStaticId = staticId; SourceLine = line;
            RawFields = RosterGuard.Copy(raw); Ratings = new SourceRosterRatings(ratings);
            ScaleExpression = scale; SizeSelector = size; SkinSelector = skin; PositionSelector = position;
            NumberExpression = number; JerseyNumber = jerseyNumber; JerseyDigitIndices = RosterGuard.Copy(digits);
            MugshotSymbol = mugshot; SelectionNameSymbol = selectionName; HudNameSymbol = hudName;
            AnnouncerExpression = announcer; AnnouncerIndex = announcerIndex;
        }
    }

    public sealed class SourceRosterTeam
    {
        public SourceTeamRoster Roster { get; }
        public string Symbol { get; }
        public ReadOnlyCollection<string> Aliases { get; }
        public string Name { get; }
        public string Home { get; }
        public string Abbreviation { get; }
        public string UniformPrefix { get; }
        public SourceTeamRatings Ratings { get; }
        public int DroneBase { get; }
        public ReadOnlyCollection<string> ImageSymbols { get; }
        public ReadOnlyCollection<SourceRosterPlayer> Players { get; }
        public string CaptainSourcePlayerId { get { return null; } }
        public ReadOnlyCollection<uint> RgbPalette { get { return null; } }

        internal SourceRosterTeam(SourceTeamRoster roster, string symbol, string[] aliases, string name,
            string home, string abbreviation, string prefix, int[] ratings, int droneBase,
            string[] images, SourceRosterPlayer[] players)
        {
            Roster = roster; Symbol = symbol; Aliases = RosterGuard.Copy(aliases); Name = name; Home = home;
            Abbreviation = abbreviation; UniformPrefix = prefix; Ratings = new SourceTeamRatings(ratings);
            DroneBase = droneBase; ImageSymbols = RosterGuard.Copy(images); Players = RosterGuard.Copy(players);
        }
    }

    public sealed class SourceRosterInputPin
    {
        public string Path { get; }
        public int ByteLength { get; }
        public string GitBlobSha1 { get; }
        public string Sha256 { get; }
        internal SourceRosterInputPin(string path, int bytes, string blob, string hash)
        { Path = path; ByteLength = bytes; GitBlobSha1 = blob; Sha256 = hash; }
    }

    public sealed class SourceRosterProvenance
    {
        public string Repository { get; }
        public string Commit { get; }
        public string Tree { get; }
        public string ManifestSha256 { get; }
        public ReadOnlyCollection<SourceRosterInputPin> Inputs { get; }
        internal SourceRosterProvenance(string repository, string commit, string tree, string manifest,
            SourceRosterInputPin[] inputs)
        {
            Repository = repository; Commit = commit; Tree = tree; ManifestSha256 = manifest;
            Inputs = RosterGuard.Copy(inputs);
        }
    }

    public sealed class SourceRosterExportProfile
    {
        public string Platform { get; }
        public bool Release { get; }
        public ReadOnlyCollection<string> Defines { get; }
        public ReadOnlyCollection<string> DisabledFlags { get; }
        public string ScaleMacro { get; }
        public string FloatArithmetic { get; }
        public ReadOnlyCollection<string> PlayerFieldOrder { get; }
        internal SourceRosterExportProfile(string platform, bool release, string[] defines,
            string[] disabled, string scale, string arithmetic, string[] fields)
        {
            Platform = platform; Release = release; Defines = RosterGuard.Copy(defines);
            DisabledFlags = RosterGuard.Copy(disabled); ScaleMacro = scale; FloatArithmetic = arithmetic;
            PlayerFieldOrder = RosterGuard.Copy(fields);
        }
    }

    public sealed class SourceRosterAsset
    {
        public string Id { get; }
        public string Kind { get; }
        public string RuntimeFilename { get; }
        public string Symbol { get; }
        public string RequestedSourcePath { get; }
        public string SourcePath { get; }
        public string GitBlobSha1 { get; }
        public string Status { get; }
        public bool AvailableInPinnedTree { get { return SourcePath != null; } }
        internal SourceRosterAsset(string id, string kind, string filename, string symbol, string requested,
            string path, string blob, string status)
        {
            Id = id; Kind = kind; RuntimeFilename = filename; Symbol = symbol;
            RequestedSourcePath = requested; SourcePath = path; GitBlobSha1 = blob; Status = status;
        }
    }

    public sealed class SourceRosterUniform
    {
        public int TeamId { get; }
        public SourceUniformVariant Variant { get; }
        public SourceSkinSelector Skin { get; }
        public int DecalOffset { get; }
        public ReadOnlyCollection<SourceRosterAsset> Slots { get; }
        internal SourceRosterUniform(int teamId, int variant, int skin, int offset, SourceRosterAsset[] slots)
        {
            TeamId = teamId; Variant = (SourceUniformVariant)variant; Skin = (SourceSkinSelector)skin;
            DecalOffset = offset; Slots = RosterGuard.Copy(slots);
        }
    }

    public sealed class SourceRosterModel
    {
        public int Index { get; }
        public string LimbArray { get; }
        public string Header { get; }
        public string MaterialArray { get; }
        public ReadOnlyCollection<string> MaterialSlots { get; }
        public ReadOnlyCollection<string> LimbSymbols { get; }
        public ReadOnlyCollection<SourceRosterAsset> Limbs { get; }
        internal SourceRosterModel(int index, string array, string header, string materials, string[] slots,
            string[] symbols, SourceRosterAsset[] limbs)
        {
            Index = index; LimbArray = array; Header = header; MaterialArray = materials;
            MaterialSlots = RosterGuard.Copy(slots); LimbSymbols = RosterGuard.Copy(symbols); Limbs = RosterGuard.Copy(limbs);
        }
    }

    public sealed class SourceRosterJersey
    {
        public int Index { get; }
        public string LimbSymbol { get; }
        public SourceRosterAsset Asset { get; }
        internal SourceRosterJersey(int index, string symbol, SourceRosterAsset asset)
        { Index = index; LimbSymbol = symbol; Asset = asset; }
    }

    public sealed class SourceRosterActorModels
    {
        public SourceRosterAsset Skeleton { get; }
        public string SkeletonSymbol { get; }
        public int HeadJointIndex { get; }
        public int CustomHeadFirstTextureSlot { get; }
        public int NumberTextureSlot { get; }
        public int NumberMatrixIndex { get; }
        public ReadOnlyCollection<SourceRosterModel> Models { get; }
        public ReadOnlyCollection<SourceRosterJersey> Jerseys { get; }
        public ReadOnlyCollection<SourceRosterAsset> GeneratedHeaderAssets { get; }
        internal SourceRosterActorModels(SourceRosterAsset skeleton, string symbol, int joint, int headSlot,
            int numberSlot, int matrix, SourceRosterModel[] models, SourceRosterJersey[] jerseys,
            SourceRosterAsset[] generated)
        {
            Skeleton = skeleton; SkeletonSymbol = symbol; HeadJointIndex = joint;
            CustomHeadFirstTextureSlot = headSlot; NumberTextureSlot = numberSlot; NumberMatrixIndex = matrix;
            Models = RosterGuard.Copy(models); Jerseys = RosterGuard.Copy(jerseys); GeneratedHeaderAssets = RosterGuard.Copy(generated);
        }

        public SourceRosterModel SelectModel(SourceBodySize body, bool highCount = false,
            SourceHeldBallModel heldBall = SourceHeldBallModel.None, bool jumboBall = false)
        {
            RosterGuard.Range((int)body, 0, 1, "body");
            RosterGuard.Require(((int)heldBall & ~12) == 0, "Unknown original held-ball model flags.");
            int index = (int)body | (highCount ? 2 : 0) | (int)heldBall | (jumboBall ? 16 : 0);
            return Models[index];
        }

        public SourceRosterJersey SelectJersey(SourceRosterPlayer player, bool highCount = false)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            int index = (int)player.BodySize | (highCount ? 2 : 0) |
                ((player.Profile.PackedJerseyNumber >> 4) != 0 ? 4 : 0);
            return Jerseys[index];
        }
    }

    public sealed class SourceRosterHead
    {
        public int Index { get; }
        public string LimbSymbol { get; }
        public SourceRosterAsset Limb { get; }
        public SourceRosterAsset Texture { get; }
        internal SourceRosterHead(int index, string symbol, SourceRosterAsset limb, SourceRosterAsset texture)
        { Index = index; LimbSymbol = symbol; Limb = limb; Texture = texture; }
    }

    public readonly struct SourceRosterHeadCode
    {
        public string Name { get; }
        public string Code { get; }
        public int HeadIndex { get; }
        internal SourceRosterHeadCode(string name, string code, int index)
        { Name = name; Code = code; HeadIndex = index; }
    }

    public sealed class SourceRosterSecrets
    {
        public ReadOnlyCollection<SourceRosterHead> Heads { get; }
        public ReadOnlyCollection<SourceRosterHeadCode> SelectionCodes { get; }
        public string Qualification { get; }
        internal SourceRosterSecrets(SourceRosterHead[] heads, SourceRosterHeadCode[] codes, string qualification)
        { Heads = RosterGuard.Copy(heads); SelectionCodes = RosterGuard.Copy(codes); Qualification = qualification; }

        public int FindHeadIndex(string sixCharacterName, string fourCharacterGameCode)
        {
            if (sixCharacterName == null || fourCharacterGameCode == null ||
                sixCharacterName.Length != 6 || fourCharacterGameCode.Length != 4) return -1;
            foreach (SourceRosterHeadCode code in SelectionCodes)
                if (code.Name == sixCharacterName && code.Code == fourCharacterGameCode) return code.HeadIndex;
            return -1;
        }
    }

    public readonly struct SourceRosterConstant
    {
        public string Symbol { get; }
        public string Raw { get; }
        public double Value { get; }
        internal SourceRosterConstant(string symbol, string raw, double value)
        { Symbol = symbol; Raw = raw; Value = value; }
    }

    public readonly struct SourceRosterDifficulty
    {
        public int Panic { get; }
        public int DroneMinimum { get; }
        public int HumanMaximum { get; }
        internal SourceRosterDifficulty(int[] row) { Panic = row[0]; DroneMinimum = row[1]; HumanMaximum = row[2]; }
    }

    public sealed class SourceRosterTuning
    {
        public ReadOnlyDictionary<string, SourceRosterConstant> Constants { get; }
        public ReadOnlyCollection<int> Direction49Table { get; }
        public ReadOnlyCollection<double> FieldFrictionMultipliers { get; }
        public ReadOnlyCollection<double> BaseSpeeds { get; }
        public ReadOnlyCollection<double> BaseTurboSpeeds { get; }
        public ReadOnlyCollection<SourceRosterDifficulty> DifficultySettings { get; }
        public ReadOnlyCollection<ReadOnlyCollection<int>> TeamLadder { get; }
        public ReadOnlyCollection<int> BeatAllTeams { get; }
        internal SourceRosterTuning(Dictionary<string, SourceRosterConstant> constants, int[] directions,
            double[] friction, double[] speeds, double[] turbo, SourceRosterDifficulty[] difficulty,
            ReadOnlyCollection<int>[] ladder, int[] beat)
        {
            Constants = new ReadOnlyDictionary<string, SourceRosterConstant>(
                new Dictionary<string, SourceRosterConstant>(constants, StringComparer.Ordinal));
            Direction49Table = RosterGuard.Copy(directions); FieldFrictionMultipliers = RosterGuard.Copy(friction);
            BaseSpeeds = RosterGuard.Copy(speeds); BaseTurboSpeeds = RosterGuard.Copy(turbo);
            DifficultySettings = RosterGuard.Copy(difficulty); TeamLadder = RosterGuard.Copy(ladder); BeatAllTeams = RosterGuard.Copy(beat);
        }
    }

    public sealed class SourceRosterLineups
    {
        public ReadOnlyDictionary<string, ReadOnlyCollection<int>> Indices { get; }
        public ReadOnlyCollection<int> ReturnersByTeam { get; }
        public ReadOnlyDictionary<string, string> OffenseSelectors { get; }
        public ReadOnlyDictionary<string, string> DefenseSelectors { get; }
        public string SourceIssue { get; }
        internal SourceRosterLineups(Dictionary<string, ReadOnlyCollection<int>> indices, int[] returners,
            Dictionary<string, string> offense, Dictionary<string, string> defense, string issue)
        {
            Indices = new ReadOnlyDictionary<string, ReadOnlyCollection<int>>(
                new Dictionary<string, ReadOnlyCollection<int>>(indices, StringComparer.Ordinal));
            ReturnersByTeam = RosterGuard.Copy(returners);
            OffenseSelectors = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(offense, StringComparer.Ordinal));
            DefenseSelectors = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(defense, StringComparer.Ordinal));
            SourceIssue = issue;
        }

        public SourceTeamSelection Select(int sourceTeamId, string offensePlaySymbol, bool offense)
        {
            RosterGuard.Range(sourceTeamId, 0, 30, nameof(sourceTeamId));
            RosterGuard.Identifier(offensePlaySymbol);
            string table;
            var selectors = offense ? OffenseSelectors : DefenseSelectors;
            if (!selectors.TryGetValue(offensePlaySymbol, out table))
                table = offense ? "std_offense" : "std_defense";
            if (table == "returners")
            {
                RosterGuard.Require(sourceTeamId < ReturnersByTeam.Count,
                    "Original returners[30] has no entry for source team 30. No default returner is fabricated.");
                table = "kick_return" + (ReturnersByTeam[sourceTeamId] + 1).ToString(CultureInfo.InvariantCulture);
            }
            return new SourceTeamSelection(sourceTeamId, Indices[table]);
        }
    }

    public readonly struct SourceRosterReference
    {
        public string Path { get; }
        public string Symbol { get; }
        public string Detail { get; }
        internal SourceRosterReference(string path, string symbol, string detail)
        { Path = path; Symbol = symbol; Detail = detail; }
    }

    public readonly struct SourceRosterIssue
    {
        public string Id { get; }
        public string Source { get; }
        public string Detail { get; }
        internal SourceRosterIssue(string id, string source, string detail)
        { Id = id; Source = source; Detail = detail; }
    }

    public readonly struct SourceRosterUniformPair
    {
        public SourceUniformVariant Side0 { get; }
        public SourceUniformVariant Side1 { get; }
        internal SourceRosterUniformPair(SourceUniformVariant side0, SourceUniformVariant side1)
        { Side0 = side0; Side1 = side1; }
    }

    public sealed class SourceTeamRosterLibrary : ISourceTeamRoster
    {
        public const string PinnedSourceRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string PinnedSourceTree = "a7e0352125d46075b7d98e87253ab89b9160a4c7";
        public const string PinnedInputManifestSha256 = "608e2e6352470993426b93130a779d014d7bc32b1794c7cb9e2eaf6ba14469a1";
        private const string PinnedInputCatalogSha256 = "5c7c893121135e73ca54570a2f7022f3b593524c024f7beefc0f12b9bd622b0c";
        public string SourceRevision { get { return PinnedSourceRevision; } }
        public string ContentSha256 { get; }
        public SourceRosterProvenance Provenance { get; }
        public SourceRosterExportProfile Profile { get; }
        public IReadOnlyList<SourceTeamRoster> Teams { get; }
        public ReadOnlyCollection<SourceRosterTeam> TeamData { get; }
        public ReadOnlyDictionary<string, SourceRosterPlayer> PlayersBySourceId { get; }
        public ReadOnlyDictionary<string, SourceRosterAsset> Assets { get; }
        public ReadOnlyCollection<SourceRosterAsset> MissingAssets { get; }
        public ReadOnlyCollection<SourceRosterUniform> Uniforms { get; }
        public SourceRosterActorModels ActorModels { get; }
        public SourceRosterLineups Lineups { get; }
        public SourceRosterSecrets Secrets { get; }
        public SourceRosterTuning Tuning { get; }
        public ReadOnlyCollection<ReadOnlyCollection<int>> AwayJersey { get; }
        public ReadOnlyCollection<ReadOnlyCollection<int>> InactiveBlitz99AwayJersey { get; }
        public string InactiveUniformQualifier { get; }
        public ReadOnlyCollection<SourceRosterReference> References { get; }
        public ReadOnlyCollection<SourceRosterIssue> SourceIssues { get; }

        private SourceTeamRosterLibrary(string hash, SourceRosterProvenance provenance,
            SourceRosterExportProfile profile, SourceRosterTeam[] teams, Dictionary<string, SourceRosterAsset> assets,
            SourceRosterAsset[] missing, SourceRosterUniform[] uniforms, SourceRosterActorModels models,
            SourceRosterLineups lineups, SourceRosterSecrets secrets, SourceRosterTuning tuning,
            ReadOnlyCollection<int>[] away, ReadOnlyCollection<int>[] legacy, string qualifier,
            SourceRosterReference[] references, SourceRosterIssue[] issues)
        {
            ContentSha256 = hash; Provenance = provenance; Profile = profile;
            TeamData = RosterGuard.Copy(teams);
            var rosters = new SourceTeamRoster[teams.Length];
            var players = new Dictionary<string, SourceRosterPlayer>(StringComparer.Ordinal);
            for (int i = 0; i < teams.Length; i++)
            {
                rosters[i] = teams[i].Roster;
                foreach (SourceRosterPlayer player in teams[i].Players) players.Add(player.Profile.SourcePlayerId, player);
            }
            Teams = RosterGuard.Copy(rosters);
            PlayersBySourceId = new ReadOnlyDictionary<string, SourceRosterPlayer>(players);
            Assets = new ReadOnlyDictionary<string, SourceRosterAsset>(
                new Dictionary<string, SourceRosterAsset>(assets, StringComparer.Ordinal));
            MissingAssets = RosterGuard.Copy(missing); Uniforms = RosterGuard.Copy(uniforms);
            ActorModels = models; Lineups = lineups; Secrets = secrets; Tuning = tuning;
            AwayJersey = RosterGuard.Copy(away); InactiveBlitz99AwayJersey = RosterGuard.Copy(legacy);
            InactiveUniformQualifier = qualifier; References = RosterGuard.Copy(references); SourceIssues = RosterGuard.Copy(issues);
        }

        public SourceTeamRoster GetTeam(int sourceTeamId) { return GetTeamData(sourceTeamId).Roster; }
        public SourceRosterTeam GetTeamData(int sourceTeamId)
        {
            RosterGuard.Range(sourceTeamId, 0, TeamData.Count - 1, nameof(sourceTeamId));
            return TeamData[sourceTeamId];
        }
        public SourceRosterProfile GetPlayer(string sourcePlayerId) { return GetPlayerData(sourcePlayerId).Profile; }
        public SourceRosterPlayer GetPlayerData(string sourcePlayerId)
        {
            SourceRosterPlayer player;
            RosterGuard.Require(sourcePlayerId != null && PlayersBySourceId.TryGetValue(sourcePlayerId, out player),
                "Missing stable original source-player ID: " + sourcePlayerId);
            return PlayersBySourceId[sourcePlayerId];
        }
        public SourceRosterPlayer GetPlayerData(SourceRosterIdentity identity)
        {
            RosterGuard.Require(identity.IsSpecified, "An original team/static-index identity is required.");
            return GetTeamData(identity.TeamIndex).Players[identity.RosterIndex];
        }
        public SourceRosterUniform GetUniform(int sourceTeamId, SourceUniformVariant variant, SourceSkinSelector skin)
        {
            RosterGuard.Range(sourceTeamId, 0, 30, nameof(sourceTeamId));
            RosterGuard.Range((int)variant, 0, 1, nameof(variant));
            RosterGuard.Range((int)skin, 0, 1, nameof(skin));
            return Uniforms[sourceTeamId * 4 + (int)variant * 2 + (int)skin];
        }
        public SourceRosterUniformPair SelectMatchUniforms(int side0TeamId, int side1TeamId, int designatedHomeSide)
        {
            RosterGuard.Range(side0TeamId, 0, 30, nameof(side0TeamId));
            RosterGuard.Range(side1TeamId, 0, 30, nameof(side1TeamId));
            RosterGuard.Range(designatedHomeSide, 0, 1, nameof(designatedHomeSide));
            bool clash = AwayJersey[Math.Min(side0TeamId, side1TeamId)][Math.Max(side0TeamId, side1TeamId)] != 0;
            return new SourceRosterUniformPair(clash && designatedHomeSide != 0 ? SourceUniformVariant.Away : SourceUniformVariant.Home,
                clash && designatedHomeSide != 1 ? SourceUniformVariant.Away : SourceUniformVariant.Home);
        }

        // Bytes and their expected pin are supplied by the bundling owner, never fetched by WebGL.
        public static SourceTeamRosterLibrary Read(byte[] jsonBytes, string expectedSha256)
        {
            RosterGuard.Hex(expectedSha256, 64, "expected roster content SHA256");
            RosterGuard.Require(jsonBytes != null && jsonBytes.Length >= 2 && jsonBytes.Length <= 4 * 1024 * 1024,
                "Roster JSON must be 2 bytes..4 MiB.");
            byte[] detached = (byte[])jsonBytes.Clone();
            string hash = RosterGuard.Sha256(detached);
            RosterGuard.Require(hash == expectedSha256, "Roster JSON does not match its explicit content pin.");
            var root = RosterJson.Object(RosterJson.Parse(detached), "roster");
            RosterGuard.Require(root.Text("schema") == "original-team-rosters/v1", "Unknown roster schema/version.");
            SourceRosterProvenance provenance = ReadProvenance(root.Object("provenance"));
            SourceRosterExportProfile profile = ReadProfile(root.Object("profile"));
            Dictionary<string, SourceRosterAsset> assets = ReadAssets(root.Array("assets"));
            SourceRosterTeam[] teams = ReadTeams(root.Array("teams"));
            SourceRosterAsset[] missing = AssetReferences(root.Strings("missing_asset_ids", 0, 2048), assets);
            var missingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SourceRosterAsset asset in missing)
                RosterGuard.Require(!asset.AvailableInPinnedTree && missingIds.Add(asset.Id), "Invalid/duplicate missing-asset entry.");
            foreach (SourceRosterAsset asset in assets.Values)
                RosterGuard.Require(asset.AvailableInPinnedTree != missingIds.Contains(asset.Id), "Incomplete explicit missing-asset list.");
            SourceRosterUniform[] uniforms = ReadUniforms(root.Array("uniforms"), assets, teams);
            SourceRosterActorModels models = ReadModels(root.Object("actor_model_schema"), assets);
            SourceRosterLineups lineups = ReadLineups(root.Object("lineups"));
            var selection = root.Object("selection");
            ReadOnlyCollection<int>[] away = Matrix(selection.Array("away_jersey"), 31, 31, 0, 1);
            ReadOnlyCollection<int>[] legacy = Matrix(selection.Array("legacy_away_jersey"), 31, 31, 0, 1);
            string qualifier = selection.Text("legacy_qualifier");
            SourceRosterSecrets secrets = ReadSecrets(selection.Object("secret_heads"), assets);
            selection.Done();
            SourceRosterTuning tuning = ReadTuning(root.Object("tuning"));
            SourceRosterReference[] references = ReadReferences(root.Array("references"));
            SourceRosterIssue[] issues = ReadIssues(root.Array("source_issues"));
            root.Done();
            return new SourceTeamRosterLibrary(hash, provenance, profile, teams, assets, missing, uniforms, models,
                lineups, secrets, tuning, away, legacy, qualifier, references, issues);
        }

        private static SourceRosterProvenance ReadProvenance(RosterObject obj)
        {
            string repository = obj.Text("repository"), commit = obj.Text("commit"), tree = obj.Text("tree");
            string manifest = obj.Text("manifest_sha256");
            RosterGuard.Require(repository == "historicalsource/nflblitz2000" && commit == PinnedSourceRevision &&
                tree == PinnedSourceTree && manifest == PinnedInputManifestSha256 && obj.Boolean("content_pin_verified"),
                "Roster provenance must identify the known, content-verified original source pin.");
            object[] raw = obj.Array("inputs");
            RosterGuard.Require(raw.Length == 22, "Expected the complete pinned roster input catalog.");
            var pins = new SourceRosterInputPin[raw.Length];
            var catalog = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < raw.Length; i++)
            {
                var pin = RosterJson.Object(raw[i], "source input");
                string path = pin.Path("path"), blob = pin.Text("git_blob_sha1"), sha = pin.Text("sha256");
                int length = pin.Integer("bytes", 1, 2 * 1024 * 1024);
                RosterGuard.Hex(blob, 40, path); RosterGuard.Hex(sha, 64, path);
                RosterGuard.Require(seen.Add(path), "Duplicate roster source input.");
                pins[i] = new SourceRosterInputPin(path, length, blob, sha);
                catalog.Append(path).Append('\n').Append(length.ToString(CultureInfo.InvariantCulture))
                    .Append('\n').Append(blob).Append('\n').Append(sha).Append('\n');
                pin.Done();
            }
            RosterGuard.Require(RosterGuard.Sha256(Encoding.UTF8.GetBytes(catalog.ToString())) == PinnedInputCatalogSha256,
                "Roster source input metadata does not match the pinned manifest catalog.");
            obj.Done();
            return new SourceRosterProvenance(repository, commit, tree, manifest, pins);
        }

        private static SourceRosterExportProfile ReadProfile(RosterObject obj)
        {
            string platform = obj.Text("platform");
            bool release = obj.Boolean("release");
            string[] defines = obj.Strings("defines", 1, 1), disabled = obj.Strings("disabled_flags", 8, 8);
            RosterGuard.Require(platform == "SEATTLE" && release && defines[0] == "SEATTLE" &&
                string.Join(",", disabled) == "DEBUG,JASON,NODRONES,NOKICKOFF,NOCLOCK,SHORT_QUARTERS,NO_FIRE,NOAUTOHIKE",
                "Unsupported original roster platform/release flags.");
            obj.ExactInteger("teams", 31); obj.ExactInteger("static_records_per_team", 16);
            obj.ExactInteger("active_per_team", 7); obj.ExactInteger("maximum_humans", 4);
            string scale = obj.Text("scale_macro"), arithmetic = obj.Text("float_arithmetic");
            string[] fields = obj.Strings("player_field_order", 17, 17);
            RosterGuard.Require(string.Join(",", fields) == "at_spd,at_pwr,at_qb,at_wr,at_off,at_def,scale,size,color,number,position,pii_mugshot,pii_selname,pii_name,ancr,lastname,frstname" &&
                scale == "(1.05f * x)" && arithmetic == "IEEE-754 binary32", "Original field layout/scale semantics mismatch.");
            obj.Done();
            return new SourceRosterExportProfile(platform, release, defines, disabled, scale, arithmetic, fields);
        }

        private static Dictionary<string, SourceRosterAsset> ReadAssets(object[] raw)
        {
            RosterGuard.Require(raw.Length > 0 && raw.Length <= 2048, "Invalid roster asset count.");
            var result = new Dictionary<string, SourceRosterAsset>(StringComparer.Ordinal);
            foreach (object value in raw)
            {
                var obj = RosterJson.Object(value, "roster asset");
                string id = obj.Text("id"), kind = obj.Text("kind"), filename = obj.NullableText("runtime_filename");
                string symbol = obj.NullableText("symbol"), requested = obj.NullablePath("requested_source_path");
                string path = obj.NullablePath("source_path"), blob = obj.NullableText("git_blob_sha1"), status = obj.Text("status");
                RosterGuard.Require(!result.ContainsKey(id), "Duplicate original asset ID.");
                if (symbol != null) RosterGuard.Identifier(symbol);
                RosterGuard.Require((path == null && blob == null && status == "missing-from-pinned-tree") ||
                    (path != null && blob != null && status == "available-in-pinned-tree"), "Invalid asset availability/content identity.");
                if (blob != null) RosterGuard.Hex(blob, 40, "asset blob");
                result.Add(id, new SourceRosterAsset(id, kind, filename, symbol, requested, path, blob, status));
                obj.Done();
            }
            return result;
        }

        private static SourceRosterTeam[] ReadTeams(object[] raw)
        {
            RosterGuard.Require(raw.Length == 31, "Expected all 31 original teams.");
            var result = new SourceRosterTeam[31];
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            var playerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.Length; i++)
            {
                var obj = RosterJson.Object(raw[i], "source team");
                obj.ExactInteger("id", i);
                string symbol = obj.Text("symbol"), prefix = obj.Text("prefix"), rosterSymbol = obj.Text("roster_symbol");
                string[] aliases = obj.Strings("aliases", 0, 8);
                RosterGuard.Identifier(symbol); RosterGuard.Identifier(rosterSymbol);
                RosterGuard.Require(symbols.Add(symbol) && prefix.Length == 3 && prefix == prefix.ToUpperInvariant() &&
                    rosterSymbol == "spd_" + prefix.ToLowerInvariant(), "Invalid original team symbol/roster prefix.");
                bool ordered = symbol == "TEAM_" + prefix;
                foreach (string alias in aliases)
                {
                    RosterGuard.Identifier(alias);
                    RosterGuard.Require(symbols.Add(alias), "Duplicate original team alias.");
                    if (alias == "TEAM_" + prefix) ordered = true;
                }
                RosterGuard.Require(ordered, "Original team alias/table order mismatch.");
                string name = obj.Text("name"), home = obj.Text("home"), abbreviation = obj.Text("abbrev", 3);
                int[] attributes = obj.Integers("attributes", 5, 0, 5);
                int droneBase = obj.Integer("drone_base", 0, 100);
                string[] images = obj.NullableStrings("image_symbols", 6);
                foreach (string image in images) if (image != null) RosterGuard.Identifier(image);
                obj.Null("captain_player_id"); obj.Null("rgb_palette");
                object[] records = obj.Array("players");
                RosterGuard.Require(records.Length == 16, "An original team requires sixteen static records.");
                var players = new SourceRosterPlayer[16];
                var profiles = new SourceRosterProfile[16];
                for (int index = 0; index < 16; index++)
                {
                    players[index] = ReadPlayer(RosterJson.Object(records[index], "source static player"), i, index, rosterSymbol);
                    profiles[index] = players[index].Profile;
                    RosterGuard.Require(playerIds.Add(profiles[index].SourcePlayerId), "Duplicate original static player key.");
                }
                var roster = new SourceTeamRoster(i, rosterSymbol, profiles);
                result[i] = new SourceRosterTeam(roster, symbol, aliases, name, home, abbreviation, prefix,
                    attributes, droneBase, images, players);
                obj.Done();
            }
            return result;
        }

        private static SourceRosterPlayer ReadPlayer(RosterObject obj, int team, int index, string roster)
        {
            string id = obj.Text("id");
            obj.ExactInteger("team_id", team); obj.ExactInteger("index", index);
            int staticId = obj.Integer("source_static_id", 0, 495);
            RosterGuard.Require(staticId == team * 16 + index && id == roster + ":" + index.ToString("00", CultureInfo.InvariantCulture),
                "Player key/static identity must retain original team and record order.");
            int line = obj.Integer("source_line", 1, 1000000);
            string[] raw = obj.Strings("raw_fields", 17, 17, true);
            int[] ratings = obj.Integers("statistics", 6, 0, 100);
            string scaleExpression = obj.Text("scale_raw"), numberExpression = obj.Text("number_raw");
            double scale = obj.Number("scale", 0.1, 4);
            RosterGuard.Require((double)(float)scale == scale && scaleExpression == "SC(1.0f)" && scale == (double)1.05f,
                "Model scale must retain this pinned source's active SC(1.0f) binary32 result, not unity scale.");
            SourceRosterEnumValue size = ReadEnum(obj.Object("size"), new[] { "PS_NORM", "PS_FAT" });
            SourceRosterEnumValue skin = ReadEnum(obj.Object("skin"), new[] { "PC_BLACK", "PC_WHITE" });
            SourceRosterEnumValue position = ReadEnum(obj.Object("position"),
                new[] { "PP_QB", "PP_WR", "PP_RB", "PP_TE", "PP_OL", "PP_K", "PP_LB", "PP_CB", "PP_DL", "PP_S" });
            int packed = obj.Integer("number_packed_bcd", 0, 0x99), jersey = obj.Integer("jersey_number", 0, 99);
            int high = packed >> 4, low = packed & 15;
            int[] digits = obj.Integers("jersey_digit_indices", 2, 0, 9);
            RosterGuard.Require(high <= 9 && low <= 9 && jersey == high * 10 + low &&
                digits[0] == (high == 0 ? 9 : high - 1) && digits[1] == (low == 0 ? 9 : low - 1),
                "Packed BCD jersey number/digit-atlas selection mismatch.");
            string mugshot = obj.NullableText("mugshot"), selected = obj.NullableText("selection_name"), hud = obj.NullableText("hud_name");
            foreach (string image in new[] { mugshot, selected, hud }) if (image != null) RosterGuard.Identifier(image);
            string announcer = obj.Text("announcer_raw");
            int announcerIndex = obj.Integer("announcer", -1, 1000);
            string first = obj.Text("firstname", 15, true), last = obj.Text("lastname", 15, true);
            obj.Null("height_inches"); obj.Null("weight_pounds"); obj.Null("static_flags"); obj.Null("static_head_selector");
            RosterGuard.Require(raw[6] == scaleExpression && raw[7] == size.Raw && raw[8] == skin.Raw &&
                raw[9] == numberExpression && raw[10] == position.Raw && raw[14] == announcer,
                "Raw original player fields disagree with typed selectors.");
            for (int i = 0; i < ratings.Length; i++)
            {
                int original;
                RosterGuard.Require(int.TryParse(raw[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out original) &&
                    original == ratings[i], "Raw original player rating disagrees with its typed value.");
            }
            int originalNumber;
            bool numberMatches = numberExpression == "NUNK" ? packed == 0 :
                numberExpression.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(numberExpression.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out originalNumber) &&
                originalNumber == packed;
            RosterGuard.Require(numberMatches, "Raw packed BCD jersey value disagrees with its typed value.");
            obj.Done();
            return new SourceRosterPlayer(new SourceRosterProfile(new SourceRosterIdentity(team, index), id, packed, (float)scale, first, last),
                staticId, line, raw, ratings, scaleExpression, size, skin, position, numberExpression, jersey, digits,
                mugshot, selected, hud, announcer, announcerIndex);
        }

        private static SourceRosterEnumValue ReadEnum(RosterObject obj, string[] symbols)
        {
            string raw = obj.Text("raw"), symbol = obj.Text("symbol");
            int value = obj.Integer("value", 0, symbols.Length - 1);
            RosterGuard.Require(symbol == symbols[value], "Unknown original enum symbol/numeric mapping.");
            string alias = symbols[0] == "PS_NORM" ? "PSUNK" : symbols[0] == "PC_BLACK" ? "PCUNK" : "PPUNK";
            int aliasValue = symbols[0] == "PS_NORM" ? 0 : symbols[0] == "PC_BLACK" ? 1 : 5;
            RosterGuard.Require(raw == symbol || raw == alias && value == aliasValue, "Unknown original enum alias.");
            obj.Done();
            return new SourceRosterEnumValue(raw, symbol, value);
        }

        private static SourceRosterUniform[] ReadUniforms(object[] raw, Dictionary<string, SourceRosterAsset> assets,
            SourceRosterTeam[] teams)
        {
            RosterGuard.Require(raw.Length == 124, "Expected all team/home-away/skin uniform bindings.");
            var result = new SourceRosterUniform[124];
            foreach (object value in raw)
            {
                var obj = RosterJson.Object(value, "source uniform");
                int team = obj.Integer("team_id", 0, 30), variant = obj.Integer("home_away", 0, 1), skin = obj.Integer("skin", 0, 1);
                int offset = obj.Integer("decal_offset", 0, 1), key = team * 4 + variant * 2 + skin;
                RosterGuard.Require(result[key] == null && offset == (skin == 1 ? 0 : 1), "Duplicate uniform or wrong source skin/decal selector.");
                string[] ids = obj.Strings("slot_asset_ids", 7, 7);
                string atlas = "runtime/" + teams[team].UniformPrefix + (variant == 0 ? "H" : "A") + "LOD.WMS";
                RosterGuard.Require(ids[0] == (skin == 1 ? "runtime/ARMHND.WMS" : "runtime/ARMHND2.WMS") &&
                    ids[1] == (skin == 1 ? "runtime/HEDSHU.WMS" : "runtime/HEDSHU2.WMS") &&
                    ids[2] == atlas && ids[3] == atlas && ids[4] == atlas && ids[5] == atlas &&
                    ids[6] == "runtime/HEDSHU.WMS", "Uniform slots do not match original load_uniform selectors.");
                result[key] = new SourceRosterUniform(team, variant, skin, offset, AssetReferences(ids, assets));
                obj.Done();
            }
            return result;
        }

        private static SourceRosterActorModels ReadModels(RosterObject obj, Dictionary<string, SourceRosterAsset> assets)
        {
            SourceRosterAsset skeleton = AssetReference(obj.Text("skeleton_asset_id"), assets);
            string symbol = obj.Text("skeleton_symbol");
            int joint = obj.Integer("head_joint_index", 8, 8), headSlot = obj.Integer("custom_head_first_texture_slot", 7, 7);
            int numberSlot = obj.Integer("number_texture_slot", 5, 5), matrix = obj.Integer("number_matrix_index", 1, 1);
            RosterGuard.Require(skeleton.SourcePath == "models/skeleton.h" && symbol == "fbplyr_skel", "Unknown original player skeleton.");
            object[] rawModels = obj.Array("models"), rawJerseys = obj.Array("jerseys");
            RosterGuard.Require(rawModels.Length == 32 && rawJerseys.Length == 8, "Incomplete body/ball/number selector tables.");
            var models = new SourceRosterModel[32];
            for (int i = 0; i < models.Length; i++)
            {
                var model = RosterJson.Object(rawModels[i], "source body model");
                model.ExactInteger("index", i);
                string array = model.Text("limb_array"), header = model.Path("header"), materialArray = model.Text("material_array");
                RosterGuard.Identifier(array); RosterGuard.Identifier(materialArray);
                string[] slots = model.Strings("material_slots", 5, 5), symbols = model.Strings("limb_symbols", 15, 15);
                SourceRosterAsset[] limbs = AssetReferences(model.Strings("limb_asset_ids", 15, 15), assets);
                RosterGuard.Require(string.Join(",", slots) == "armhnd.wms,hedshu.wms,jersf.wms,leghel.wms,slvmsk.wms",
                    "Unknown original body material order.");
                for (int j = 0; j < limbs.Length; j++)
                    RosterGuard.Require(limbs[j].Symbol == symbols[j], "Original skeleton limb symbol/reference mismatch.");
                models[i] = new SourceRosterModel(i, array, header, materialArray, slots, symbols, limbs);
                model.Done();
            }
            var jerseys = new SourceRosterJersey[8];
            for (int i = 0; i < jerseys.Length; i++)
            {
                var jersey = RosterJson.Object(rawJerseys[i], "source jersey overlay");
                jersey.ExactInteger("index", i);
                string limb = jersey.Text("limb_symbol");
                SourceRosterAsset asset = AssetReference(jersey.Text("asset_id"), assets);
                RosterGuard.Require(asset.Symbol == limb, "Jersey limb symbol/reference mismatch.");
                jerseys[i] = new SourceRosterJersey(i, limb, asset);
                jersey.Done();
            }
            SourceRosterAsset[] generated = AssetReferences(obj.Strings("generated_header_asset_ids", 3, 3), assets);
            obj.Done();
            return new SourceRosterActorModels(skeleton, symbol, joint, headSlot, numberSlot, matrix, models, jerseys, generated);
        }

        private static SourceRosterLineups ReadLineups(RosterObject obj)
        {
            var indices = new Dictionary<string, ReadOnlyCollection<int>>(StringComparer.Ordinal);
            foreach (string name in new[] { "std_offense", "std_defense", "kickoff", "kick_return1", "kick_return2", "kick_return3", "punt", "fgoal" })
            {
                int[] row = obj.Integers(name, 7, 0, 15);
                RosterGuard.Require(new HashSet<int>(row).Count == 7, "Duplicate record in original seven-actor lineup.");
                indices.Add(name, RosterGuard.Copy(row));
            }
            int[] returners = obj.Integers("returners", 30, 0, 2);
            string issue = obj.Text("source_issue");
            Dictionary<string, string> offense = ReadPlaySelectors(obj.Array("offense_selector"), indices);
            Dictionary<string, string> defense = ReadPlaySelectors(obj.Array("defense_selector"), indices);
            obj.Done();
            return new SourceRosterLineups(indices, returners, offense, defense, issue);
        }

        private static Dictionary<string, string> ReadPlaySelectors(object[] raw, Dictionary<string, ReadOnlyCollection<int>> indices)
        {
            RosterGuard.Require(raw.Length > 0 && raw.Length <= 32, "Invalid source lineup selector count.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (object value in raw)
            {
                var obj = RosterJson.Object(value, "lineup selector");
                string play = obj.Text("play"), table = obj.Text("lineup");
                RosterGuard.Identifier(play);
                RosterGuard.Require(!result.ContainsKey(play) && (indices.ContainsKey(table) || table == "returners"),
                    "Duplicate lineup selector or missing lineup reference.");
                result.Add(play, table); obj.Done();
            }
            return result;
        }

        private static SourceRosterSecrets ReadSecrets(RosterObject obj, Dictionary<string, SourceRosterAsset> assets)
        {
            object[] rawHeads = obj.Array("heads"), rawCodes = obj.Array("selection_codes");
            RosterGuard.Require(rawHeads.Length == 20 && rawCodes.Length == 21, "Incomplete original head catalog/aliases.");
            var heads = new SourceRosterHead[20];
            for (int i = 0; i < heads.Length; i++)
            {
                var head = RosterJson.Object(rawHeads[i], "source head");
                head.ExactInteger("index", i);
                string symbol = head.Text("limb_symbol");
                SourceRosterAsset limb = AssetReference(head.Text("limb_asset_id"), assets), texture = AssetReference(head.Text("texture_asset_id"), assets);
                RosterGuard.Require(limb.Symbol == symbol && texture.Kind == "texture", "Invalid original custom-head binding.");
                heads[i] = new SourceRosterHead(i, symbol, limb, texture); head.Done();
            }
            var codes = new SourceRosterHeadCode[21];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < codes.Length; i++)
            {
                var code = RosterJson.Object(rawCodes[i], "historical game head selector");
                string name = code.Text("name", 6), value = code.Text("code", 4);
                int head = code.Integer("head_index", 0, 19);
                RosterGuard.Require(name.Length == 6 && value.Length == 4 && seen.Add(name + value), "Invalid/duplicate historical head selection.");
                foreach (char digit in value) RosterGuard.Require(digit >= '0' && digit <= '9', "Invalid historical head game-code character.");
                codes[i] = new SourceRosterHeadCode(name, value, head); code.Done();
            }
            RosterGuard.Require(obj.Array("separate_static_rosters").Length == 0, "Additional secret stat rosters need a schema extension.");
            string qualification = obj.Text("qualification");
            obj.Done();
            return new SourceRosterSecrets(heads, codes, qualification);
        }

        private static SourceRosterTuning ReadTuning(RosterObject obj)
        {
            var constants = new Dictionary<string, SourceRosterConstant>(StringComparer.Ordinal);
            foreach (object value in obj.Array("constants"))
            {
                var constant = RosterJson.Object(value, "original tuning constant");
                string symbol = constant.Text("symbol"), raw = constant.Text("raw");
                double number = constant.Number("value", -2147483648, 2147483647);
                RosterGuard.Identifier(symbol);
                RosterGuard.Require(!constants.ContainsKey(symbol), "Duplicate original tuning constant.");
                constants.Add(symbol, new SourceRosterConstant(symbol, raw, number)); constant.Done();
            }
            RosterGuard.Require(constants.ContainsKey("PPOS_QBACK") && constants["PPOS_QBACK"].Value == 3 &&
                constants.ContainsKey("MOD_266") && constants["MOD_266"].Value == 0 &&
                constants.ContainsKey("MOD_FAT") && constants["MOD_FAT"].Value == 1 &&
                constants.ContainsKey("MOD_498") && constants["MOD_498"].Value == 2 &&
                constants.ContainsKey("MOD_LBALL") && constants["MOD_LBALL"].Value == 4 &&
                constants.ContainsKey("MOD_RBALL") && constants["MOD_RBALL"].Value == 8 &&
                constants.ContainsKey("MOD_JUMBO") && constants["MOD_JUMBO"].Value == 16 &&
                constants.ContainsKey("MOD_2DIGITS") && constants["MOD_2DIGITS"].Value == 4,
                "Unknown original model/actor role enum mapping.");
            int[] directions = obj.Integers("dir49_table", 49, -1, 1023);
            double[] friction = obj.Numbers("field_friction_mult_factor", 7, 0, 4);
            double[] speeds = obj.Numbers("base_speeds", 11, 0, 4), turbo = obj.Numbers("base_turbo_speeds", 11, 0, 4);
            ReadOnlyCollection<int>[] difficulty = Matrix(obj.Array("diff_settings"), 9, 3, 0, 100);
            var settings = new SourceRosterDifficulty[9];
            for (int i = 0; i < settings.Length; i++)
                settings[i] = new SourceRosterDifficulty(new[] { difficulty[i][0], difficulty[i][1], difficulty[i][2] });
            ReadOnlyCollection<int>[] ladder = Matrix(obj.Array("team_ladder"), 9, 5, -1, 30);
            int[] beat = obj.Integers("beat_all_teams", 4, 0, 30);
            obj.Done();
            return new SourceRosterTuning(constants, directions, friction, speeds, turbo, settings, ladder, beat);
        }

        private static SourceRosterReference[] ReadReferences(object[] raw)
        {
            RosterGuard.Require(raw.Length > 0 && raw.Length <= 64, "Invalid source selector reference count.");
            var result = new SourceRosterReference[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                var obj = RosterJson.Object(raw[i], "source selector reference");
                result[i] = new SourceRosterReference(obj.Path("path"), obj.Text("symbol"), obj.Text("detail"));
                obj.Done();
            }
            return result;
        }

        private static SourceRosterIssue[] ReadIssues(object[] raw)
        {
            RosterGuard.Require(raw.Length > 0 && raw.Length <= 64, "Invalid source issue count.");
            var result = new SourceRosterIssue[raw.Length];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < raw.Length; i++)
            {
                var obj = RosterJson.Object(raw[i], "source issue");
                string id = obj.Text("id");
                RosterGuard.Require(seen.Add(id), "Duplicate source issue identity.");
                result[i] = new SourceRosterIssue(id, obj.Text("source"), obj.Text("detail")); obj.Done();
            }
            return result;
        }

        private static ReadOnlyCollection<int>[] Matrix(object[] raw, int rows, int columns, int min, int max)
        {
            RosterGuard.Require(raw.Length == rows, "Original table row count mismatch.");
            var result = new ReadOnlyCollection<int>[rows];
            for (int i = 0; i < rows; i++) result[i] = RosterGuard.Copy(RosterJson.Integers(raw[i], columns, min, max));
            return result;
        }
        private static SourceRosterAsset[] AssetReferences(string[] ids, Dictionary<string, SourceRosterAsset> assets)
        {
            var result = new SourceRosterAsset[ids.Length];
            for (int i = 0; i < ids.Length; i++) result[i] = AssetReference(ids[i], assets);
            return result;
        }
        private static SourceRosterAsset AssetReference(string id, Dictionary<string, SourceRosterAsset> assets)
        {
            SourceRosterAsset asset;
            RosterGuard.Require(assets.TryGetValue(id, out asset), "Missing original asset reference: " + id);
            return asset;
        }
    }

    internal static class RosterGuard
    {
        internal static void Require(bool condition, string message)
        { if (!condition) throw new SourceRosterException(message); }
        internal static void Range(int value, int minimum, int maximum, string label)
        { Require(value >= minimum && value <= maximum, label + ": out of range."); }
        internal static ReadOnlyCollection<T> Copy<T>(T[] array) { return Array.AsReadOnly((T[])array.Clone()); }
        internal static void Hex(string value, int length, string label)
        {
            Require(value != null && value.Length == length, label + ": expected lowercase hex pin.");
            foreach (char c in value) Require(c >= '0' && c <= '9' || c >= 'a' && c <= 'f', label + ": invalid lowercase hex pin.");
        }
        internal static void Identifier(string value)
        {
            Require(value != null && value.Length > 0 && value.Length <= 128, "Expected original C symbol.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                Require(c == '_' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || i > 0 && c >= '0' && c <= '9',
                    "Invalid original C symbol: " + value);
            }
        }
        internal static void Path(string value)
        {
            Require(value != null && value.Length > 0 && value.Length <= 256 && value[0] != '/' &&
                value.IndexOf('\\') < 0 && value.IndexOf(':') < 0, "Expected portable source-relative path.");
            foreach (string part in value.Split('/'))
                Require(part.Length > 0 && part != "." && part != "..", "Invalid source-relative path.");
        }
        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var text = new StringBuilder(64);
                foreach (byte value in hash) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }
    }

    internal sealed class RosterObject
    {
        private readonly Dictionary<string, object> values;
        private readonly string label;
        internal RosterObject(Dictionary<string, object> values, string label) { this.values = values; this.label = label; }
        private object Take(string key)
        {
            object value;
            RosterGuard.Require(values.TryGetValue(key, out value), label + ": missing required member " + key + ".");
            values.Remove(key); return value;
        }
        internal string Text(string key, int maximum = 4096, bool allowEmpty = false)
        { return RosterJson.Text(Take(key), maximum, allowEmpty); }
        internal string NullableText(string key)
        { object value = Take(key); return value == null ? null : RosterJson.Text(value, 4096, false); }
        internal string Path(string key) { string value = Text(key, 256); RosterGuard.Path(value); return value; }
        internal string NullablePath(string key)
        { string value = NullableText(key); if (value != null) RosterGuard.Path(value); return value; }
        internal bool Boolean(string key)
        { object value = Take(key); RosterGuard.Require(value is bool, label + ": expected boolean " + key); return (bool)value; }
        internal void Null(string key) { RosterGuard.Require(Take(key) == null, label + ": absent original field must remain null: " + key); }
        internal int Integer(string key, int minimum, int maximum) { return RosterJson.Integer(Take(key), minimum, maximum); }
        internal void ExactInteger(string key, int expected)
        { RosterGuard.Require(Integer(key, expected, expected) == expected, label + ": unexpected " + key); }
        internal double Number(string key, double minimum, double maximum) { return RosterJson.Number(Take(key), minimum, maximum); }
        internal RosterObject Object(string key) { return RosterJson.Object(Take(key), label + "." + key); }
        internal object[] Array(string key) { return RosterJson.Array(Take(key)); }
        internal string[] Strings(string key, int minimum, int maximum, bool allowEmpty = false)
        {
            object[] raw = Array(key);
            RosterGuard.Require(raw.Length >= minimum && raw.Length <= maximum, label + ": string array count mismatch: " + key);
            var result = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++) result[i] = RosterJson.Text(raw[i], 4096, allowEmpty);
            return result;
        }
        internal string[] NullableStrings(string key, int count)
        {
            object[] raw = Array(key);
            RosterGuard.Require(raw.Length == count, label + ": nullable string array count mismatch.");
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = raw[i] == null ? null : RosterJson.Text(raw[i], 4096, false);
            return result;
        }
        internal int[] Integers(string key, int count, int minimum, int maximum)
        { return RosterJson.Integers(Take(key), count, minimum, maximum); }
        internal double[] Numbers(string key, int count, double minimum, double maximum)
        {
            object[] raw = Array(key);
            RosterGuard.Require(raw.Length == count, label + ": numeric array count mismatch.");
            var result = new double[count];
            for (int i = 0; i < count; i++) result[i] = RosterJson.Number(raw[i], minimum, maximum);
            return result;
        }
        internal void Done() { RosterGuard.Require(values.Count == 0, label + ": unknown JSON member(s): " + string.Join(",", values.Keys)); }
    }

    internal static class RosterJson
    {
        internal static RosterObject Object(object value, string label)
        {
            var obj = value as Dictionary<string, object>;
            RosterGuard.Require(obj != null, label + ": expected JSON object.");
            return new RosterObject(obj, label);
        }
        internal static object[] Array(object value)
        { var array = value as object[]; RosterGuard.Require(array != null, "Expected JSON array."); return array; }
        internal static string Text(object value, int maximum, bool allowEmpty)
        {
            string text = value as string;
            RosterGuard.Require(text != null && text.Length <= maximum && (allowEmpty || text.Length > 0), "Invalid bounded JSON string.");
            foreach (char c in text) RosterGuard.Require(c >= 32, "Control character in roster JSON field.");
            return text;
        }
        internal static double Number(object value, double minimum, double maximum)
        {
            RosterGuard.Require(value is double, "Expected finite JSON number.");
            double number = (double)value;
            RosterGuard.Require(!double.IsNaN(number) && !double.IsInfinity(number) && number >= minimum && number <= maximum,
                "JSON number is nonfinite or out of range.");
            return number;
        }
        internal static int Integer(object value, int minimum, int maximum)
        {
            double number = Number(value, minimum, maximum);
            RosterGuard.Require(number == Math.Truncate(number), "Expected integer JSON number.");
            return (int)number;
        }
        internal static int[] Integers(object value, int count, int minimum, int maximum)
        {
            object[] array = Array(value);
            RosterGuard.Require(array.Length == count, "Integer array count mismatch.");
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = Integer(array[i], minimum, maximum);
            return result;
        }
        internal static object Parse(byte[] bytes)
        {
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException e) { throw new SourceRosterException("Invalid roster UTF-8: " + e.Message); }
            return new Parser(text).Read();
        }

        private sealed class Parser
        {
            private readonly string text;
            private int position;
            private int values;
            internal Parser(string text) { this.text = text; }
            private void Space() { while (position < text.Length && " \t\r\n".IndexOf(text[position]) >= 0) position++; }
            private char Peek { get { return position < text.Length ? text[position] : '\0'; } }
            private void Expect(char expected)
            { RosterGuard.Require(Peek == expected, "Invalid roster JSON at character " + position + "."); position++; }
            internal object Read()
            { object value = Value(0); Space(); RosterGuard.Require(position == text.Length, "Trailing roster JSON content."); return value; }
            private object Value(int depth)
            {
                RosterGuard.Require(depth <= 24 && ++values <= 500000, "Roster JSON nesting/item limit exceeded.");
                Space();
                if (Peek == '{')
                {
                    position++; Space();
                    var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (Peek == '}') { position++; return obj; }
                    while (true)
                    {
                        string key = String(); Space(); Expect(':');
                        RosterGuard.Require(!obj.ContainsKey(key), "Duplicate roster JSON member: " + key);
                        obj.Add(key, Value(depth + 1)); Space();
                        if (Peek == '}') { position++; return obj; }
                        Expect(','); Space();
                    }
                }
                if (Peek == '[')
                {
                    position++; Space();
                    var array = new List<object>();
                    if (Peek == ']') { position++; return array.ToArray(); }
                    while (true)
                    {
                        RosterGuard.Require(array.Count < 50000, "Roster JSON array limit exceeded.");
                        array.Add(Value(depth + 1)); Space();
                        if (Peek == ']') { position++; return array.ToArray(); }
                        Expect(',');
                    }
                }
                if (Peek == '"') return String();
                if (Peek == 't') { Literal("true"); return true; }
                if (Peek == 'f') { Literal("false"); return false; }
                if (Peek == 'n') { Literal("null"); return null; }
                return Numeric();
            }
            private void Literal(string value)
            {
                RosterGuard.Require(position + value.Length <= text.Length &&
                    string.CompareOrdinal(text, position, value, 0, value.Length) == 0, "Invalid roster JSON literal.");
                position += value.Length;
            }
            private string String()
            {
                Expect('"');
                var result = new StringBuilder();
                while (Peek != '"')
                {
                    RosterGuard.Require(position < text.Length && Peek >= 32 && result.Length < 4096, "Invalid or oversized roster JSON string.");
                    char c = text[position++];
                    if (c == '\\')
                    {
                        RosterGuard.Require(position < text.Length, "Incomplete JSON escape.");
                        char escape = text[position++];
                        switch (escape)
                        {
                            case '"': c = '"'; break;
                            case '\\': c = '\\'; break;
                            case '/': c = '/'; break;
                            case 'b': c = '\b'; break;
                            case 'f': c = '\f'; break;
                            case 'n': c = '\n'; break;
                            case 'r': c = '\r'; break;
                            case 't': c = '\t'; break;
                            case 'u': c = Unicode(); break;
                            default: throw new SourceRosterException("Unsupported JSON string escape.");
                        }
                    }
                    result.Append(c);
                }
                position++;
                string value = result.ToString();
                for (int i = 0; i < value.Length; i++)
                {
                    if (char.IsHighSurrogate(value[i]))
                    {
                        RosterGuard.Require(i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]), "Unpaired JSON Unicode surrogate.");
                        i++;
                    }
                    else RosterGuard.Require(!char.IsLowSurrogate(value[i]), "Unpaired JSON Unicode surrogate.");
                }
                return value;
            }
            private char Unicode()
            {
                RosterGuard.Require(position + 4 <= text.Length, "Incomplete JSON Unicode escape.");
                int result = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = text[position++];
                    int value = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    RosterGuard.Require(value >= 0, "Invalid JSON Unicode escape.");
                    result = result * 16 + value;
                }
                return (char)result;
            }
            private double Numeric()
            {
                int start = position;
                if (Peek == '-') position++;
                if (Peek == '0') position++;
                else
                {
                    RosterGuard.Require(Peek >= '1' && Peek <= '9', "Expected JSON number.");
                    while (Peek >= '0' && Peek <= '9') position++;
                }
                if (Peek == '.')
                {
                    position++;
                    RosterGuard.Require(Peek >= '0' && Peek <= '9', "Incomplete JSON fraction.");
                    while (Peek >= '0' && Peek <= '9') position++;
                }
                if (Peek == 'e' || Peek == 'E')
                {
                    position++;
                    if (Peek == '+' || Peek == '-') position++;
                    RosterGuard.Require(Peek >= '0' && Peek <= '9', "Incomplete JSON exponent.");
                    while (Peek >= '0' && Peek <= '9') position++;
                }
                RosterGuard.Require(position - start <= 32, "JSON numeric token limit exceeded.");
                double result;
                RosterGuard.Require(double.TryParse(text.Substring(start, position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result) && !double.IsNaN(result) && !double.IsInfinity(result),
                    "Nonfinite/invalid JSON number.");
                return result;
            }
        }
    }
}
