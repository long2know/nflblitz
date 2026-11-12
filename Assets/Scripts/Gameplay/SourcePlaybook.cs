using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public readonly struct SourcePlanar
    {
        public float X { get; }
        public float Z { get; }

        public SourcePlanar(float x, float z)
        {
            SourceP1Guard.Coordinate(x, nameof(x));
            SourceP1Guard.Coordinate(z, nameof(z));
            X = x;
            Z = z;
        }

        public float DistanceTo(SourcePlanar other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    public enum SourcePlaySide { Offense, Defense, Kickoff, KickReturn }
    public enum SourceActorMode
    {
        Lineup = 0, Dead = 1, LinePreSnap = 2, Line = 3,
        QuarterbackPreSnap = 4, Quarterback = 5, ReceiverPreSnap = 6,
        Receiver = 7, DefensiveBackPreSnap = 8, DefensiveBack = 9, Kicker = 10
    }
    public enum SourceActorJob { None, Rush, Cover, Block, Catch, Breakup, Zone }

    [Flags]
    public enum SourceFormationControl
    {
        None = 0, Right = 1, Left = 2, Center = 4, PositionMask = 7,
        Captain = 8, SecondPlayer = 16, CoverWithoutAligning = 64, WeakSide = 128, Motion = 256
    }

    [Flags]
    public enum SourcePlayFlags
    {
        None = 0, Blitz = 1, Punt = 2, FieldGoal = 4, OnsideKick = 8,
        FakeFieldGoal = 16, FakePunt = 32, Custom = 64,
        PrimaryReceiver1 = 128, PrimaryReceiver2 = 256, PrimaryReceiver3 = 512,
        RedZone = 1024, LineDive = 2048, FakeZone = 4096, CheapShot = 8192,
        SmartIntercept = 16384, TryIntercept = 32768
    }

    public enum SourceRouteOpcode
    {
        // Nonpositive controls belong to explicitly converted/custom programs, not the C catalog.
        Branch = -3, Restart = -2, NoOperation = -1, End = 0,
        WaitSnap = 1, Face = 2, GotoLosPlus = 3, RunYards = 4, Improvise = 5,
        CoverTarget = 6, RushQuarterback = 7, Block = 8, Juke = 9, FakeOut = 10,
        TurboOn = 11, TurboOff = 12, WaveArm = 13, Spin = 14, MoveDefender = 15,
        Wait = 16, Zone = 17, SetFlag = 18, CoverWeak = 19, DelayBlock = 20
    }

    public sealed class SourceRouteProgram
    {
        private readonly int[] words;
        private readonly HashSet<int> boundaries;
        public string Identity { get; }
        public string SourceBlob { get; }
        public int SourceLine { get; }
        public int WordCount { get { return words.Length; } }
        public IReadOnlyList<int> Words { get; }

        public SourceRouteProgram(string identity, string sourceBlob, int sourceLine, IEnumerable<int> words)
        {
            Identity = SourceP1Guard.Identity(identity, nameof(identity));
            SourceBlob = SourceP1Guard.Identity(sourceBlob, nameof(sourceBlob));
            if (sourceLine < 1) throw new ArgumentOutOfRangeException(nameof(sourceLine));
            if (words == null) throw new ArgumentNullException(nameof(words));
            SourceLine = sourceLine;
            this.words = new List<int>(words).ToArray();
            if (this.words.Length == 0 || this.words.Length > 16384)
                throw new ArgumentException("A route requires 1..16384 explicit words.", nameof(words));
            Words = Array.AsReadOnly(this.words);
            boundaries = new HashSet<int>();
            for (int at = 0; at < this.words.Length;)
            {
                boundaries.Add(at);
                int width = InstructionWidth(this.words[at]);
                if (width == 0) throw Error(at, "unknown opcode " + this.words[at]);
                if (at + width > this.words.Length) throw Error(at, "truncated parameters for opcode " + this.words[at]);
                at += width;
            }
            foreach (int at in boundaries)
                if (this.words[at] == (int)SourceRouteOpcode.Branch &&
                    !boundaries.Contains(this.words[at + 1]))
                    throw Error(at, "branch target is not an instruction boundary");
        }

        public int WordAt(int at)
        {
            if (at < 0 || at >= words.Length) throw Error(at, "program exhausted without explicit terminal behavior");
            return words[at];
        }

        public bool IsInstruction(int at) { return boundaries.Contains(at); }

        internal InvalidOperationException Error(int at, string reason)
        {
            return new InvalidOperationException("Source route " + Identity + " [" + SourceBlob + ":" +
                SourceLine.ToString(CultureInfo.InvariantCulture) + "] word " +
                at.ToString(CultureInfo.InvariantCulture) + ": " + reason + ".");
        }

        public static int InstructionWidth(int opcode)
        {
            switch ((SourceRouteOpcode)opcode)
            {
                case SourceRouteOpcode.Face:
                case SourceRouteOpcode.GotoLosPlus:
                case SourceRouteOpcode.RunYards:
                case SourceRouteOpcode.Wait:
                case SourceRouteOpcode.SetFlag:
                case SourceRouteOpcode.DelayBlock:
                case SourceRouteOpcode.Branch: return 2;
                case SourceRouteOpcode.MoveDefender: return 3;
                case SourceRouteOpcode.FakeOut:
                case SourceRouteOpcode.Zone: return 4;
                case SourceRouteOpcode.WaitSnap:
                case SourceRouteOpcode.Improvise:
                case SourceRouteOpcode.CoverTarget:
                case SourceRouteOpcode.RushQuarterback:
                case SourceRouteOpcode.Block:
                case SourceRouteOpcode.Juke:
                case SourceRouteOpcode.TurboOn:
                case SourceRouteOpcode.TurboOff:
                case SourceRouteOpcode.WaveArm:
                case SourceRouteOpcode.Spin:
                case SourceRouteOpcode.CoverWeak:
                case SourceRouteOpcode.End:
                case SourceRouteOpcode.NoOperation:
                case SourceRouteOpcode.Restart: return 1;
                default: return 0;
            }
        }
    }

    public readonly struct SourceFormationSlot
    {
        public SourcePlanar Offset { get; }
        public string Script { get; }
        public SourceActorMode Mode { get; }
        public SourceFormationControl Control { get; }

        public SourceFormationSlot(SourcePlanar offset, string script, SourceActorMode mode,
            SourceFormationControl control)
        {
            if (mode < SourceActorMode.Lineup || mode > SourceActorMode.Kicker)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (((int)control & ~0x1df) != 0) throw new ArgumentOutOfRangeException(nameof(control));
            Offset = offset;
            Script = SourceP1Guard.Identity(script, nameof(script));
            Mode = mode;
            Control = control;
        }
    }

    public sealed class SourceFormation
    {
        public string Identity { get; }
        public string SourceBlob { get; }
        public int SourceLine { get; }
        public IReadOnlyList<SourceFormationSlot> Slots { get; }

        public SourceFormation(string identity, string sourceBlob, int sourceLine,
            IEnumerable<SourceFormationSlot> slots)
        {
            Identity = SourceP1Guard.Identity(identity, nameof(identity));
            SourceBlob = SourceP1Guard.Identity(sourceBlob, nameof(sourceBlob));
            if (sourceLine < 1) throw new ArgumentOutOfRangeException(nameof(sourceLine));
            SourceLine = sourceLine;
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            SourceFormationSlot[] copy = new List<SourceFormationSlot>(slots).ToArray();
            if (copy.Length != 7) throw new ArgumentException("A source formation has seven slots.", nameof(slots));
            for (int i = 0; i < copy.Length; i++)
                SourceP1Guard.Identity(copy[i].Script, nameof(slots));
            Slots = Array.AsReadOnly(copy);
        }
    }

    public sealed class SourcePlayDefinition
    {
        public string Identity { get; }
        public string DisplayName { get; }
        public string SourceBlob { get; }
        public int SourceLine { get; }
        public SourcePlaySide Side { get; }
        public SourcePlayFlags Flags { get; }
        public SourceFormation Formation { get; }
        public IReadOnlyList<SourceRouteProgram> Routes { get; }

        public SourcePlayDefinition(string identity, string displayName, string sourceBlob, int sourceLine,
            SourcePlaySide side, SourcePlayFlags flags, SourceFormation formation,
            IEnumerable<SourceRouteProgram> routes)
        {
            Identity = SourceP1Guard.Identity(identity, nameof(identity));
            DisplayName = SourceP1Guard.Identity(displayName, nameof(displayName));
            SourceBlob = SourceP1Guard.Identity(sourceBlob, nameof(sourceBlob));
            if (sourceLine < 1) throw new ArgumentOutOfRangeException(nameof(sourceLine));
            if (side < SourcePlaySide.Offense || side > SourcePlaySide.KickReturn)
                throw new ArgumentOutOfRangeException(nameof(side));
            if (((int)flags & ~0xffff) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
            SourceLine = sourceLine;
            Side = side;
            Flags = flags;
            Formation = formation ?? throw new ArgumentNullException(nameof(formation));
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            SourceRouteProgram[] copy = new List<SourceRouteProgram>(routes).ToArray();
            if (copy.Length != 7) throw new ArgumentException("A source play has seven routes.", nameof(routes));
            foreach (SourceRouteProgram route in copy)
                if (route == null) throw new ArgumentException("Null route in " + Identity + ".", nameof(routes));
            Routes = Array.AsReadOnly(copy);
        }
    }

    public sealed class SourceWeightedPlayPool
    {
        public SourceCpuPlayFamily Family { get; }
        public string Identity { get; }
        public string SourceBlob { get; }
        public int SourceLine { get; }
        public IReadOnlyList<SourcePlayDefinition> Entries { get; }

        public SourceWeightedPlayPool(SourceCpuPlayFamily family, string identity, string sourceBlob,
            int sourceLine, IEnumerable<SourcePlayDefinition> entries)
        {
            if (!Enum.IsDefined(typeof(SourceCpuPlayFamily), family))
                throw new ArgumentOutOfRangeException(nameof(family));
            Identity = SourceP1Guard.Identity(identity, nameof(identity));
            SourceBlob = SourceP1Guard.Identity(sourceBlob, nameof(sourceBlob));
            if (sourceLine < 1) throw new ArgumentOutOfRangeException(nameof(sourceLine));
            SourceLine = sourceLine;
            Family = family;
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            SourcePlayDefinition[] copy = new List<SourcePlayDefinition>(entries).ToArray();
            int expected = SourceMatchFlow.CpuPlayPoolSize(family);
            if (copy.Length != (expected == 0 ? 1 : expected))
                throw new ArgumentException("Wrong original weighted count for " + family + ".", nameof(entries));
            bool offense = family == SourceCpuPlayFamily.Punt || family == SourceCpuPlayFamily.FakePunt ||
                family == SourceCpuPlayFamily.FieldGoal || family == SourceCpuPlayFamily.ShortOffense ||
                family == SourceCpuPlayFamily.LongOffense;
            foreach (SourcePlayDefinition entry in copy)
                if (entry == null || entry.Side != (offense ? SourcePlaySide.Offense : SourcePlaySide.Defense))
                    throw new ArgumentException("Weighted pool side or play identity mismatch.", nameof(entries));
            Entries = Array.AsReadOnly(copy);
        }
    }

    public readonly struct SourceLineupInput
    {
        public int OffenseSide { get; }
        public int LineOfScrimmage { get; }
        public float HashZ { get; }
        public bool OffenseFlipped { get; }

        public SourceLineupInput(int offenseSide, int lineOfScrimmage, float hashZ, bool offenseFlipped = false)
        {
            SourceP1Guard.Side(offenseSide);
            if (lineOfScrimmage < 0 || lineOfScrimmage > 100)
                throw new ArgumentOutOfRangeException(nameof(lineOfScrimmage));
            SourceP1Guard.Coordinate(hashZ, nameof(hashZ));
            OffenseSide = offenseSide;
            LineOfScrimmage = lineOfScrimmage;
            HashZ = hashZ;
            OffenseFlipped = offenseFlipped;
        }
    }

    public readonly struct SourceFormationActor
    {
        public int ActorId { get; }
        public SourcePlanar Position { get; }
        public SourceFormationSlot Slot { get; }
        public int Facing { get; }
        public int CoverageTarget { get; }
        public bool ReverseRouteAtSnap { get; }

        internal SourceFormationActor(int actorId, SourcePlanar position, SourceFormationSlot slot,
            int target, bool reverse)
        {
            ActorId = actorId;
            Position = position;
            Slot = slot;
            Facing = actorId < 7 ? 256 : 768;
            CoverageTarget = target;
            ReverseRouteAtSnap = reverse;
        }
    }

    public sealed class SourceFormationLayout
    {
        public IReadOnlyList<SourceFormationActor> Actors { get; }
        internal SourceFormationLayout(SourceFormationActor[] actors) { Actors = Array.AsReadOnly(actors); }
    }

    public sealed class SourceDefaultAudibleSpecialPlays
    {
        public SourcePlayDefinition DefenseBefore85 { get; }
        public SourcePlayDefinition FieldGoalOffense { get; }
        public SourcePlayDefinition FieldGoalDefense { get; }
        public SourcePlayDefinition FakeFieldGoalOffense { get; }
        public SourcePlayDefinition PuntOffense { get; }
        public SourcePlayDefinition FakePuntOffense { get; }
        public SourcePlayDefinition PuntReturnDefense { get; }
        public SourcePlayDefinition ExtraPointOffense { get; }

        public SourceDefaultAudibleSpecialPlays(SourcePlayDefinition defenseBefore85,
            SourcePlayDefinition fieldGoalOffense, SourcePlayDefinition fieldGoalDefense,
            SourcePlayDefinition fakeFieldGoalOffense, SourcePlayDefinition puntOffense,
            SourcePlayDefinition fakePuntOffense, SourcePlayDefinition puntReturnDefense,
            SourcePlayDefinition extraPointOffense)
        {
            DefenseBefore85 = defenseBefore85 ?? throw new ArgumentNullException(nameof(defenseBefore85));
            FieldGoalOffense = fieldGoalOffense ?? throw new ArgumentNullException(nameof(fieldGoalOffense));
            FieldGoalDefense = fieldGoalDefense ?? throw new ArgumentNullException(nameof(fieldGoalDefense));
            FakeFieldGoalOffense = fakeFieldGoalOffense ?? throw new ArgumentNullException(nameof(fakeFieldGoalOffense));
            PuntOffense = puntOffense ?? throw new ArgumentNullException(nameof(puntOffense));
            FakePuntOffense = fakePuntOffense ?? throw new ArgumentNullException(nameof(fakePuntOffense));
            PuntReturnDefense = puntReturnDefense ?? throw new ArgumentNullException(nameof(puntReturnDefense));
            ExtraPointOffense = extraPointOffense ?? throw new ArgumentNullException(nameof(extraPointOffense));
        }
    }

    public sealed class SourceDefaultAudibleData
    {
        public const string StableProfileId = "original-teams-contact282-c2-m1-default-audibles";
        public const string RequiredFeatureContract = SourceAudibleView.Contract;
        public const string RequiredHudSha256 = "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435";
        public const string RequiredPlaybookSha256 = "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3";
        public const string RequiredPlaybookRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string RequiredPlaybookProfile = "OriginalRelease57Hz-USE_OLD_QB_FALSE";

        public string ResourceProfileId { get; }
        public string FeatureContract { get; }
        public string HudSha256 { get; }
        public string PlaybookSha256 { get; }
        public SourcePlaybook Playbook { get; }
        public ReadOnlyCollection<int> Digital8 { get; }
        public ReadOnlyCollection<ReadOnlyCollection<SourcePlayDefinition>> OffensePages { get; }
        public ReadOnlyCollection<ReadOnlyCollection<SourcePlayDefinition>> DefensePages { get; }
        public ReadOnlyCollection<ReadOnlyCollection<SourcePlayDefinition>> TeamSpecials { get; }
        public SourceDefaultAudibleSpecialPlays SpecialPlays { get; }

        // The producer authenticates the existing HUD/catalog bytes before constructing
        // this detached graph. A declared content pin is not a replacement for that read.
        public SourceDefaultAudibleData(string resourceProfileId, string featureContract,
            string hudSha256, string playbookSha256, SourcePlaybook playbook,
            IReadOnlyList<int> digital8, IReadOnlyList<IReadOnlyList<SourcePlayDefinition>> offensePages,
            IReadOnlyList<IReadOnlyList<SourcePlayDefinition>> defensePages,
            IReadOnlyList<IReadOnlyList<SourcePlayDefinition>> teamSpecials,
            SourceDefaultAudibleSpecialPlays specialPlays)
        {
            if (resourceProfileId != StableProfileId)
                throw new ArgumentException("Default audibles require their distinct stable resource profile.", nameof(resourceProfileId));
            if (featureContract != RequiredFeatureContract)
                throw new ArgumentException("An explicit source-default-audibles/v1 feature contract is required.", nameof(featureContract));
            if (hudSha256 != RequiredHudSha256)
                throw new ArgumentException("Default audibles require the authenticated complete existing HUD.", nameof(hudSha256));
            if (playbookSha256 != RequiredPlaybookSha256)
                throw new ArgumentException("Default audibles require the authenticated existing playbook.", nameof(playbookSha256));
            Playbook = playbook ?? throw new ArgumentNullException(nameof(playbook));
            if (playbook.Revision != RequiredPlaybookRevision || playbook.Profile != RequiredPlaybookProfile)
                throw new ArgumentException("Default audibles require the original normal57 catalog identity.", nameof(playbook));
            if (digital8 == null) throw new ArgumentNullException(nameof(digital8));
            if (digital8.Count != 49)
                throw new ArgumentException("Supply the actual 49-to-8 digital table.", nameof(digital8));
            var digitalCopy = new int[49];
            for (int i = 0; i < digitalCopy.Length; i++)
            {
                if (digital8[i] < 0 || digital8[i] > 15)
                    throw new ArgumentOutOfRangeException(nameof(digital8));
                digitalCopy[i] = digital8[i];
            }
            if (digitalCopy[24] != 0)
                throw new ArgumentException("The source 49-way center must decode to neutral.", nameof(digital8));
            Digital8 = Array.AsReadOnly(digitalCopy);
            // Source offense page4 is CUSTOM, not an ordinary MakeChoice play.
            OffensePages = CopyRows(offensePages, 4, 9, SourcePlaySide.Offense, nameof(offensePages));
            DefensePages = CopyRows(defensePages, 2, 9, SourcePlaySide.Defense, nameof(defensePages));
            TeamSpecials = CopyRows(teamSpecials, SourceMatchLimits.TeamCount, 3,
                SourcePlaySide.Offense, nameof(teamSpecials));
            for (int i = 0; i < 3; i++)
                if (!ReferenceEquals(OffensePages[0][i], TeamSpecials[0][i]))
                    throw new ArgumentException("Page0 must retain PageSlot's source-team0 special references.", nameof(offensePages));
            SpecialPlays = specialPlays ?? throw new ArgumentNullException(nameof(specialPlays));
            RequirePlay(specialPlays.DefenseBefore85, SourcePlaySide.Defense, nameof(specialPlays));
            RequirePlay(specialPlays.FieldGoalOffense, SourcePlaySide.Offense, nameof(specialPlays));
            RequirePlay(specialPlays.FieldGoalDefense, SourcePlaySide.Defense, nameof(specialPlays));
            RequirePlay(specialPlays.FakeFieldGoalOffense, SourcePlaySide.Offense, nameof(specialPlays));
            RequirePlay(specialPlays.PuntOffense, SourcePlaySide.Offense, nameof(specialPlays));
            RequirePlay(specialPlays.FakePuntOffense, SourcePlaySide.Offense, nameof(specialPlays));
            RequirePlay(specialPlays.PuntReturnDefense, SourcePlaySide.Defense, nameof(specialPlays));
            RequirePlay(specialPlays.ExtraPointOffense, SourcePlaySide.Offense, nameof(specialPlays));
            ResourceProfileId = resourceProfileId; FeatureContract = featureContract;
            HudSha256 = hudSha256; PlaybookSha256 = playbookSha256;
        }

        public SourceDigitalDirections DigitalCurrentAt(int quantized49)
        {
            if (quantized49 < 0 || quantized49 >= 49) throw new ArgumentOutOfRangeException(nameof(quantized49));
            return (SourceDigitalDirections)Digital8[quantized49];
        }

        public SourcePlayDefinition PagePlay(SourcePlaybookKind kind, int pageIndex, int slotIndex, int sourceTeam)
        {
            if (kind != SourcePlaybookKind.Offense && kind != SourcePlaybookKind.Defense)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (sourceTeam < 0 || sourceTeam >= SourceMatchLimits.TeamCount)
                throw new ArgumentOutOfRangeException(nameof(sourceTeam));
            var pages = kind == SourcePlaybookKind.Offense ? OffensePages : DefensePages;
            if (pageIndex < 0 || pageIndex >= pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Custom/missing audible pages have no ordinary-play fallback.");
            if (slotIndex < 0 || slotIndex >= 9) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return kind == SourcePlaybookKind.Offense && pageIndex == 0 && slotIndex < 3
                ? TeamSpecials[sourceTeam][slotIndex] : pages[pageIndex][slotIndex];
        }

        internal SourcePlayDefinition Resolve(short signedId, SourceAudibleRole role, int sourceTeam,
            SourceRulesState rules, int quarter, uint packedTime, bool superFieldGoal, bool noPunt, ref bool flip)
        {
            int id = signedId;
            bool offense = role == SourceAudibleRole.Offense;
            if (offense) { flip = id < 0; if (id < 0) id = -id; }
            id--;
            int page = id / 9, slot = id % 9;
            return CurrentPagePlay(offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense,
                page, slot, sourceTeam, rules, quarter, packedTime, superFieldGoal, noPunt);
        }

        internal SourcePlayDefinition CurrentPagePlay(SourcePlaybookKind kind, int page, int slot, int sourceTeam,
            SourceRulesState rules, int quarter, uint packedTime, bool superFieldGoal, bool noPunt)
        {
            bool offense = kind == SourcePlaybookKind.Offense;
            SourcePlayDefinition play = PagePlay(offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense,
                page, slot, sourceTeam);
            return SourcePlaybook.CurrentPagePlay(play, SpecialPlays, offense, page, slot,
                rules, quarter, packedTime, superFieldGoal, noPunt);
        }

        private ReadOnlyCollection<ReadOnlyCollection<SourcePlayDefinition>> CopyRows(
            IReadOnlyList<IReadOnlyList<SourcePlayDefinition>> rows, int rowCount, int columnCount,
            SourcePlaySide side, string name)
        {
            if (rows == null) throw new ArgumentNullException(name);
            if (rows.Count != rowCount) throw new ArgumentException("Incomplete original audible table.", name);
            var copy = new ReadOnlyCollection<SourcePlayDefinition>[rowCount];
            for (int row = 0; row < rowCount; row++)
            {
                if (rows[row] == null || rows[row].Count != columnCount)
                    throw new ArgumentException("Incomplete original audible table row.", name);
                var entries = new SourcePlayDefinition[columnCount];
                for (int column = 0; column < columnCount; column++)
                {
                    SourcePlayDefinition play = rows[row][column];
                    RequirePlay(play, side, name);
                    entries[column] = play;
                }
                copy[row] = Array.AsReadOnly(entries);
            }
            return Array.AsReadOnly(copy);
        }

        private void RequirePlay(SourcePlayDefinition play, SourcePlaySide side, string name)
        {
            SourcePlayDefinition actual;
            if (play == null || play.Side != side || (play.Flags & SourcePlayFlags.Custom) != 0
                || !Playbook.Plays.TryGetValue(play.Identity, out actual) || !ReferenceEquals(play, actual))
                throw new ArgumentException("An actual non-custom play reference from the same catalog and side is required.", name);
            foreach (SourceRouteProgram route in play.Routes)
            {
                SourceRouteProgram actualRoute;
                if (!Playbook.Routes.TryGetValue(route.Identity, out actualRoute) || !ReferenceEquals(route, actualRoute))
                    throw new ArgumentException("An audible play route is outside the same catalog.", name);
            }
        }
    }

    public sealed class SourcePlaybook
    {
        private readonly Dictionary<string, SourcePlayDefinition> plays;
        private readonly Dictionary<string, SourceRouteProgram> routes;
        private readonly Dictionary<SourceCpuPlayFamily, SourceWeightedPlayPool> pools;
        private readonly Dictionary<string, float[]> handicaps;
        public string Revision { get; }
        public string Profile { get; }
        public SourceDirectionMath Directions { get; }
        public IReadOnlyDictionary<string, SourcePlayDefinition> Plays { get; }
        public IReadOnlyDictionary<string, SourceRouteProgram> Routes { get; }
        public IReadOnlyDictionary<SourceCpuPlayFamily, SourceWeightedPlayPool> Pools { get; }

        public SourcePlaybook(string revision, string profile, IEnumerable<SourcePlayDefinition> plays,
            IEnumerable<SourceRouteProgram> routes, IEnumerable<SourceWeightedPlayPool> pools,
            SourceDirectionMath directions = null, IDictionary<string, float[]> handicapTables = null)
        {
            Revision = SourceP1Guard.Identity(revision, nameof(revision));
            Profile = SourceP1Guard.Identity(profile, nameof(profile));
            if (plays == null || routes == null || pools == null) throw new ArgumentNullException(nameof(plays));
            this.plays = new Dictionary<string, SourcePlayDefinition>(StringComparer.Ordinal);
            this.routes = new Dictionary<string, SourceRouteProgram>(StringComparer.Ordinal);
            this.pools = new Dictionary<SourceCpuPlayFamily, SourceWeightedPlayPool>();
            handicaps = new Dictionary<string, float[]>(StringComparer.Ordinal);
            Directions = directions;
            if (handicapTables != null)
                foreach (KeyValuePair<string, float[]> entry in handicapTables)
                {
                    SourceP1Guard.Identity(entry.Key, nameof(handicapTables));
                    if (entry.Value == null || entry.Value.Length != 12)
                        throw new ArgumentException("A source handicap table has twelve levels.", nameof(handicapTables));
                    var copy = (float[])entry.Value.Clone();
                    foreach (float value in copy) SourceP1Guard.Coordinate(value, nameof(handicapTables));
                    handicaps.Add(entry.Key, copy);
                }
            foreach (SourcePlayDefinition play in plays) this.plays.Add(play.Identity, play);
            foreach (SourceRouteProgram route in routes) this.routes.Add(route.Identity, route);
            foreach (SourceWeightedPlayPool pool in pools)
            {
                foreach (SourcePlayDefinition play in pool.Entries)
                    if (!this.plays.ContainsKey(play.Identity) || !ReferenceEquals(this.plays[play.Identity], play))
                        throw new ArgumentException("Weighted pool refers to a play outside this catalog.", nameof(pools));
                this.pools.Add(pool.Family, pool);
            }
            Plays = new ReadOnlyDictionary<string, SourcePlayDefinition>(this.plays);
            Routes = new ReadOnlyDictionary<string, SourceRouteProgram>(this.routes);
            Pools = new ReadOnlyDictionary<SourceCpuPlayFamily, SourceWeightedPlayPool>(this.pools);
        }

        public SourcePlayDefinition GetPlay(string identity) { return plays[identity]; }
        public SourceRouteProgram GetRoute(string identity) { return routes[identity]; }
        public SourceWeightedPlayPool GetPool(SourceCpuPlayFamily family) { return pools[family]; }
        internal static SourcePlayDefinition CurrentPagePlay(SourcePlayDefinition play,
            SourceDefaultAudibleSpecialPlays specials, bool offense, int page, int slot,
            SourceRulesState rules, int quarter, uint packedTime, bool superFieldGoal, bool noPunt)
        {
            if (page != 0) return play;
            if (!offense && slot == 1 && rules.LineOfScrimmage < 85) play = specials.DefenseBefore85;
            bool fieldGoal = (rules.LineOfScrimmage >= 50 || superFieldGoal) && rules.PlayType != SourcePlayType.ExtraPoint
                && (rules.Down == 4 || (quarter == 1 || quarter >= 3) && packedTime < 0x1000u);
            if (fieldGoal)
            {
                if (slot == 6) play = offense ? specials.FieldGoalOffense : specials.FieldGoalDefense;
                else if (offense && slot == 7) play = specials.FakeFieldGoalOffense;
            }
            if (rules.LineOfScrimmage < 50 && !noPunt && !superFieldGoal && rules.Down == 4)
            {
                if (offense && slot == 6) play = specials.PuntOffense;
                else if (slot == 7) play = offense ? specials.FakePuntOffense : specials.PuntReturnDefense;
            }
            if (offense && rules.PlayType == SourcePlayType.ExtraPoint && slot == 0) play = specials.ExtraPointOffense;
            return play;
        }

        public float HandicapValue(string table, int level)
        {
            if (level < 0 || level > 11) throw new ArgumentOutOfRangeException(nameof(level));
            return handicaps[table][level];
        }

        public SourcePlayDefinition ResolveCpuPlay(SourceCpuPlayDecision decision)
        {
            if (!decision.IsResolved) throw new ArgumentException("Resolve SourceMatchFlow's RNG requests first.", nameof(decision));
            SourceWeightedPlayPool pool = GetPool(decision.Family);
            int index = decision.PoolIndex;
            if (index == -1 && SourceMatchFlow.CpuPlayPoolSize(decision.Family) == 0) index = 0;
            if (index < 0 || index >= pool.Entries.Count) throw new ArgumentOutOfRangeException(nameof(decision));
            return pool.Entries[index];
        }

        // Explicit functional-data records only; no C evaluation, executable payloads, art or source-array defaults.
        public static SourcePlaybook LoadConverted(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var forms = new Dictionary<string, SourceFormation>(StringComparer.Ordinal);
            var routeMap = new Dictionary<string, SourceRouteProgram>(StringComparer.Ordinal);
            var playMap = new Dictionary<string, SourcePlayDefinition>(StringComparer.Ordinal);
            var poolList = new List<SourceWeightedPlayPool>();
            var handicapTables = new Dictionary<string, float[]>(StringComparer.Ordinal);
            SourceDirectionMath directions = null;
            string revision = null, profile = null, line;
            int number = 0;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.Length > 131072) throw new FormatException("Converted playbook line is too long.");
                string[] f = line.Split('\t');
                try
                {
                    switch (f[0])
                    {
                        case "source-playbook/v1":
                            RequireColumns(f, 3);
                            if (revision != null) throw new FormatException("Duplicate header.");
                            revision = f[1]; profile = f[2];
                            break;
                        case "formation":
                            RequireColumns(f, 5);
                            var slots = new List<SourceFormationSlot>();
                            foreach (string record in f[4].Split(';'))
                            {
                                string[] s = record.Split(',');
                                RequireColumns(s, 5);
                                slots.Add(new SourceFormationSlot(new SourcePlanar(Float(s[0]), Float(s[1])),
                                    s[2], (SourceActorMode)Int(s[3]), (SourceFormationControl)Int(s[4])));
                            }
                            forms.Add(f[1], new SourceFormation(f[1], f[2], Int(f[3]), slots));
                            break;
                        case "trig":
                            RequireColumns(f, 5);
                            if (directions != null) throw new FormatException("Duplicate direction table.");
                            var sines = new List<float>();
                            foreach (string value in f[3].Split(',')) sines.Add(Float(value));
                            var stickAngles = new List<int>();
                            foreach (string value in f[4].Split(',')) stickAngles.Add(Int(value));
                            directions = new SourceDirectionMath(sines, stickAngles, f[1], f[2]);
                            break;
                        case "handicap":
                            RequireColumns(f, 5);
                            SourceP1Guard.Identity(f[2], "handicap blob");
                            if (Int(f[3]) < 1) throw new FormatException("Invalid handicap source line.");
                            var levels = new List<float>();
                            foreach (string value in f[4].Split(',')) levels.Add(Float(value));
                            if (levels.Count != 12) throw new FormatException("Expected twelve handicap values.");
                            handicapTables.Add(f[1], levels.ToArray());
                            break;
                        case "route":
                            RequireColumns(f, 5);
                            var words = new List<int>();
                            foreach (string word in f[4].Split(',')) words.Add(Int(word));
                            routeMap.Add(f[1], new SourceRouteProgram(f[1], f[2], Int(f[3]), words));
                            break;
                        case "play":
                            RequireColumns(f, 9);
                            var playRoutes = new List<SourceRouteProgram>();
                            foreach (string route in f[8].Split(',')) playRoutes.Add(routeMap[route]);
                            playMap.Add(f[1], new SourcePlayDefinition(f[1], f[2], f[3], Int(f[4]),
                                ParseEnum<SourcePlaySide>(f[5]), (SourcePlayFlags)Int(f[6]), forms[f[7]], playRoutes));
                            break;
                        case "pool":
                            RequireColumns(f, 6);
                            var entries = new List<SourcePlayDefinition>();
                            foreach (string play in f[5].Split(',')) entries.Add(playMap[play]);
                            poolList.Add(new SourceWeightedPlayPool(ParseEnum<SourceCpuPlayFamily>(f[1]),
                                f[2], f[3], Int(f[4]), entries));
                            break;
                        default: throw new FormatException("Unknown converted record '" + f[0] + "'.");
                    }
                }
                catch (Exception e) when (e is ArgumentException || e is FormatException ||
                    e is KeyNotFoundException || e is OverflowException || e is InvalidOperationException)
                {
                    throw new FormatException("Converted playbook line " + number.ToString(CultureInfo.InvariantCulture) +
                        " (" + f[0] + "): " + e.Message, e);
                }
            }
            if (revision == null) throw new FormatException("Missing source-playbook/v1 provenance header.");
            return new SourcePlaybook(revision, profile, playMap.Values, routeMap.Values, poolList,
                directions, handicapTables);
        }

        public static SourceFormationLayout LineUp(SourcePlayDefinition offense, SourcePlayDefinition defense,
            SourceLineupInput input)
        {
            if (offense == null || defense == null) throw new ArgumentNullException(nameof(offense));
            if (offense.Side != SourcePlaySide.Offense && offense.Side != SourcePlaySide.Kickoff)
                throw new ArgumentException("Expected offense or kicking formation.", nameof(offense));
            if (defense.Side != SourcePlaySide.Defense && defense.Side != SourcePlaySide.KickReturn)
                throw new ArgumentException("Expected defense or returning formation.", nameof(defense));
            var positions = new SourcePlanar[14];
            var targets = new int[14];
            for (int i = 0; i < targets.Length; i++) targets[i] = -1;
            float lineX = WorldX(input.LineOfScrimmage, input.OffenseSide);
            float forward = input.OffenseSide == 0 ? 1f : -1f;
            int off = input.OffenseSide * 7, def = (1 - input.OffenseSide) * 7;
            for (int i = 0; i < 7; i++)
            {
                SourceFormationSlot slot = offense.Formation.Slots[i];
                positions[off + i] = new SourcePlanar(ClampX(lineX + slot.Offset.X * forward),
                    input.HashZ + slot.Offset.Z * forward * (input.OffenseFlipped ? -1f : 1f));
            }
            int positive = 0;
            for (int i = 4; i < 7; i++) if (positions[off + i].Z >= 0f) positive++;
            // strong_left compares to world zero, NOT the hash or the QB.
            bool strongLeft = input.OffenseSide == 1 ? positive > 3 - positive : positive <= 3 - positive;
            for (int i = 0; i < 7; i++)
            {
                SourceFormationSlot slot = defense.Formation.Slots[i];
                float z = input.HashZ + slot.Offset.Z * forward;
                var match = slot.Control & SourceFormationControl.PositionMask;
                if (match != SourceFormationControl.None)
                {
                    int found = -1;
                    for (int j = 4; j < 7; j++)
                        if ((offense.Formation.Slots[j].Control & SourceFormationControl.PositionMask) == match)
                        { found = off + j; break; }
                    if (found < 0) throw new InvalidOperationException("No LU receiver match in " + offense.Identity + " for " + match + ".");
                    targets[def + i] = found;
                    targets[found] = def + i;
                    if ((slot.Control & SourceFormationControl.CoverWithoutAligning) == 0) z = positions[found].Z;
                }
                if ((slot.Control & SourceFormationControl.WeakSide) != 0 && strongLeft)
                    z = input.HashZ - slot.Offset.Z * forward;
                positions[def + i] = new SourcePlanar(ClampX(lineX - slot.Offset.X * forward), z);
            }
            var actors = new SourceFormationActor[14];
            for (int id = 0; id < actors.Length; id++)
            {
                bool isOffense = id / 7 == input.OffenseSide;
                SourceFormationSlot slot = (isOffense ? offense : defense).Formation.Slots[id % 7];
                bool reverse = isOffense && id % 7 >= 4 && (input.OffenseSide == 0
                    ? positions[id].Z < input.HashZ - 2f : positions[id].Z > input.HashZ + 2f);
                actors[id] = new SourceFormationActor(id, positions[id], slot, targets[id], reverse);
            }
            return new SourceFormationLayout(actors);
        }

        internal static SourceFormationLayout LineUpSide(SourcePlayDefinition offense, SourcePlayDefinition defense,
            SourceLineupInput input, IReadOnlyList<SourcePlanar> retained, int side)
        {
            SourceP1Guard.Side(side);
            if (retained == null || retained.Count != 14) throw new ArgumentException("Retain all actual actor positions.", nameof(retained));
            int off = input.OffenseSide * 7, def = (1 - input.OffenseSide) * 7;
            float line = WorldX(input.LineOfScrimmage, input.OffenseSide);
            float forward = input.OffenseSide == 0 ? 1f : -1f;
            var positions = new SourcePlanar[14];
            var targets = new int[14];
            for (int i = 0; i < 14; i++) { positions[i] = retained[i]; targets[i] = -1; }
            if (side == input.OffenseSide)
                for (int i = 0; i < 7; i++)
                {
                    SourceFormationSlot slot = offense.Formation.Slots[i];
                    positions[off + i] = new SourcePlanar(ClampX(line + slot.Offset.X * forward),
                        input.HashZ + slot.Offset.Z * forward * (input.OffenseFlipped ? -1f : 1f));
                }
            int positive = 0;
            for (int i = 4; i < 7; i++) if (positions[off + i].Z >= 0f) positive++;
            bool strongLeft = input.OffenseSide == 1 ? positive > 3 - positive : positive <= 3 - positive;
            for (int i = 0; i < 7; i++)
            {
                SourceFormationSlot slot = defense.Formation.Slots[i];
                SourceFormationControl match = slot.Control & SourceFormationControl.PositionMask;
                if (side == input.OffenseSide && match == 0 && (slot.Control & SourceFormationControl.WeakSide) == 0) continue;
                float z = input.HashZ + slot.Offset.Z * forward;
                if (match != 0)
                {
                    int receiver = -1;
                    for (int j = 4; j < 7; j++)
                        if ((offense.Formation.Slots[j].Control & SourceFormationControl.PositionMask) == match)
                        { receiver = off + j; break; }
                    if (receiver < 0) throw new InvalidOperationException("Source lineup has no matching LU receiver.");
                    targets[def + i] = receiver;
                    targets[receiver] = def + i;
                    if ((slot.Control & SourceFormationControl.CoverWithoutAligning) == 0) z = positions[receiver].Z;
                }
                if ((slot.Control & SourceFormationControl.WeakSide) != 0 && strongLeft)
                    z = input.HashZ - slot.Offset.Z * forward;
                positions[def + i] = new SourcePlanar(ClampX(line - slot.Offset.X * forward), z);
            }
            var actors = new SourceFormationActor[14];
            for (int i = 0; i < 14; i++)
                actors[i] = new SourceFormationActor(i, positions[i],
                    (i / 7 == input.OffenseSide ? offense : defense).Formation.Slots[i % 7], targets[i], false);
            return new SourceFormationLayout(actors);
        }

        // WORLDX/FIELDX use (390f/300f)*6f, one binary32 ULP below literal YD2UN=7.8f.
        public static float WorldX(int yards, int offenseSide)
        {
            SourceP1Guard.Side(offenseSide);
            if (yards < -100000 || yards > 100000) throw new ArgumentOutOfRangeException(nameof(yards));
            float scale = (390f / 300f) * 6f;
            return scale * (offenseSide == 0 ? yards : 100 - yards) - 390f;
        }

        public static int FieldX(float sourceX, int offenseSide)
        {
            SourceP1Guard.Side(offenseSide);
            SourceP1Guard.Coordinate(sourceX, nameof(sourceX));
            float scale = (390f / 300f) * 6f;
            return (int)((390f + (offenseSide == 0 ? sourceX : -sourceX)) / scale);
        }

        private static float ClampX(float x) { return Math.Max(-460.2f, Math.Min(460.2f, x)); }
        private static int Int(string value) { return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture); }
        private static float Float(string value) { return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture); }
        private static T ParseEnum<T>(string value) where T : struct
        {
            T parsed;
            if (!Enum.TryParse(value, false, out parsed) || !Enum.IsDefined(typeof(T), parsed))
                throw new FormatException("Unknown " + typeof(T).Name + " '" + value + "'.");
            return parsed;
        }
        private static void RequireColumns(string[] fields, int count)
        {
            if (fields.Length != count) throw new FormatException("Expected " + count + " columns.");
        }
    }

    internal static class SourceP1Guard
    {
        internal static void Coordinate(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1000000f)
                throw new ArgumentOutOfRangeException(name, "Expected finite bounded source units.");
        }
        internal static string Identity(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
                value.IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0)
                throw new ArgumentException("Expected explicit source identity.", name);
            return value;
        }
        internal static void Side(int side)
        {
            if (side != 0 && side != 1) throw new ArgumentOutOfRangeException(nameof(side));
        }
        internal static void Actor(int actor)
        {
            if (actor < 0 || actor >= 14) throw new ArgumentOutOfRangeException(nameof(actor));
        }
    }
}
