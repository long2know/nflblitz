using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static class SourceInputIntroChecks
{
    private static int assertions;
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message)
    { Check(EqualityComparer<T>.Default.Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private sealed class Inputs
    {
        private readonly string root;
        private readonly Dictionary<string, string> pins = new Dictionary<string, string>();
        private readonly Dictionary<string, int> sizes = new Dictionary<string, int>();
        private readonly Dictionary<string, string> entries = new Dictionary<string, string>();
        internal readonly bool DefaultAudibles;
        internal readonly SourceAnimationLibrary Animations;
        internal readonly SourceVector3[] Pivots;
        internal readonly SourcePlaybook Plays;
        internal readonly SourceTeamRosterLibrary Rosters;
        internal readonly SourceMovementRuntimeData Movement;
        internal readonly SourceMovementSettings MovementSettings;
        internal readonly SourceAudioFunctionData Sound;
        internal readonly SourceC2CallbackData Callbacks;
        internal readonly SourcePossessionSoundData Speech;
        internal readonly SourceInterfaceData Hud;
        internal readonly SourceCinematicLibrary Impact;

        internal Inputs(string root, byte[] descriptor, bool defaultAudibles)
        {
            this.root = root; DefaultAudibles = defaultAudibles;
            Equal(defaultAudibles ? "885c9e04cee21a2bb15d40b243744d3b1bfa6f4846ddf2ea9837c7fd3bec5548"
                : "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6",
                Hash(descriptor), "actual separate descriptor identity, not a profile alias");
            using (JsonDocument doc = JsonDocument.Parse(descriptor))
            {
                JsonElement manifest = doc.RootElement;
                Equal(defaultAudibles ? SourceTeamResourceProfile.DefaultAudiblesStableId
                    : SourceTeamResourceProfile.PreSnapSwitchStableId, manifest.GetProperty("profile").GetString(),
                    "actual explicit profile");
                if (defaultAudibles)
                {
                    Equal(SourceInterfacePackagePlan.DefaultAudiblesFeatureContract,
                        manifest.GetProperty("featureContract").GetString(), "actual successor feature");
                    Equal(SourceInterfacePackagePlan.DefaultAudiblesFactory,
                        manifest.GetProperty("factory").GetString(), "actual successor factory");
                }
                foreach (JsonElement pin in manifest.GetProperty("files").EnumerateArray())
                {
                    string path = pin.GetProperty("path").GetString();
                    AuthorizedAssetRoot.ValidateRelativePath(path);
                    pins.Add(path, pin.GetProperty("sha256").GetString());
                    sizes.Add(path, pin.GetProperty("bytes").GetInt32());
                }
                foreach (JsonProperty entry in manifest.GetProperty("entrypoints").EnumerateObject())
                    entries.Add(entry.Name, entry.Value.GetString());
                Equal(563, pins.Count, "retained original563 payload inventory");
            }
            Animations = SourceAnimationLibrary.Read(Read(entries["animations"]), path => Read("animation/" + path));
            SourceTeamResourceProfile.Contact282C2M1PreSnapSwitch.RequireLibrary(Animations);
            Blitz.AssetImport.SourcePlayer player = GeneratedPlayerReader.Read(Encoding.UTF8.GetString(Read(entries["model"])),
                "models/plyr266.h", Encoding.UTF8.GetString(Read(entries["skeleton"])), "models/skeleton.h",
                new PlayerSymbolSelection("plyr266_limbs", "fbplyr_skel", "model266_limbs", "plyr266_textures"));
            var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
                SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
            Pivots = player.Nodes.Select(node => {
                AssetVector3 pivot = conversion.Convert(node.BindPivot);
                return new SourceVector3(pivot.X, pivot.Y, pivot.Z);
            }).ToArray();
            Rosters = SourceTeamRosterLibrary.Read(Read(entries["rosters"]), pins[entries["rosters"]]);
            byte[] plays = Read(entries["playbook"]);
            if (defaultAudibles) Plays = SourceInterfaceData.ReadDefaultAudiblePlaybook(plays);
            else using (var reader = new StringReader(Encoding.UTF8.GetString(plays))) Plays = SourcePlaybook.LoadConverted(reader);
            SourceInterfaceMovementData movement = SourceInterfaceMovementData.Read(Read(entries["movement"]), pins[entries["movement"]]);
            Movement = new SourceMovementRuntimeData(new SourceMovementData(movement.Constants, movement.Tables),
                movement.Friction, movement.Defense.Select(row => new SourceMovementDefenseRow(
                    row.Line, row.Back, row.Base, row.Range, row.Plow)).ToArray());
            SourceInterfaceMovementBootstrap bootstrap = SourceInterfaceMovementBootstrap.Read(Read(entries["bootstrap"]), pins[entries["bootstrap"]]);
            MovementSettings = new SourceMovementSettings(bootstrap.FieldIndex, bootstrap.InfiniteTurboTeamMask,
                bootstrap.SpeedPowerup, bootstrap.FastTurboTeamMask, bootstrap.RunOutOfBoundsTeamMask, bootstrap.ViolencePowerup);
            Sound = SourceAudioFunctionData.Read(Read(entries["sound"]), GameSimulation.SourceSoundVmDataSha256);
            Callbacks = SourceInterfacePackagePlan.ReadPreSnapSwitchCallbacks(Read(entries["callbacks"]), Read(entries["hit"]));
            Speech = SourcePossessionSoundData.Read(Read(entries["possessionSound"]));
            Hud = SourceInterfaceData.Read(Read(entries["hud"]), SourceInterfacePackagePlan.HudSha256);
            if (defaultAudibles) Hud.BindDefaultAudiblePresentation(Rosters, bootstrap);
            Impact = SourceCinematicLibrary.Read(Read(entries["impact"]), SourceTeamResourceProfile.PreSnapSwitchImpactSha256);
        }
        private byte[] Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
            Check(bytes.Length == sizes[path] && Hash(bytes) == pins[path], "unchanged actual input " + path);
            return bytes;
        }
        internal GameSimulation Create(SourceMatchConfiguration configuration)
        {
            Func<int, ISourceRandom> random = seed => new SourceInjectedRandom(site => 17,
                new SourceRandomProvenance("source-input-intro/bounded-fixture", "explicit raw17 test service; not target PRNG",
                    "constant17", SourceRandomEvidence.RecordedOracle));
            var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
            SourceC2Bootstrap bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(Callbacks);
            GameSimulation game = DefaultAudibles
                ? SourceC2Composition.CreateDefaultAudiblesSimulation(Animations, Pivots, Plays, configuration, Rosters,
                    presentation, Movement, MovementSettings, Sound, bootstrap, randomFactory: random,
                    compatibility: SourceGameplayCompatibility.Portable, defaultAudibles: Hud.CreateDefaultAudibleData(Plays),
                    impactLibrary: Impact)
                : SourceC2Composition.CreatePreSnapSwitchSimulation(Animations, Pivots, Plays, configuration, Rosters,
                    presentation, Movement, MovementSettings, Sound, bootstrap, randomFactory: random,
                    compatibility: SourceGameplayCompatibility.Portable);
            game.BindSourcePossessionSoundData(Speech);
            game.SourceCameraHeadingProvider = (state, tick) => FullKernelInputs.SetControlledMovementCamera(game, state, tick);
            return game;
        }
    }

    private static SourceMatchConfiguration Configuration(bool bothTeams = true)
    {
        var records = Enumerable.Range(0, 7).ToArray();
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.FourStation,
            new SourceTeamSelection(0, records), new SourceTeamSelection(1, records),
            bothTeams ? new[] { new SourceStationConfiguration(2, TeamId.Away), new SourceStationConfiguration(0, TeamId.Home) }
                : new[] { new SourceStationConfiguration(0, TeamId.Home) },
            TeamId.Home, SourceCaptainSlot.First);
    }

    private sealed class Rig
    {
        internal readonly Inputs Data;
        internal readonly GameSimulation Game;
        internal readonly FixedStepBridge Bridge;
        internal readonly SourceCabinetInputBuffer Buffer;
        internal readonly SourceCabinetQuantizer Quantizer;
        internal readonly SourceMatchConfiguration Configuration;
        internal readonly Services Services;
        internal readonly List<SourceProcessSoundCall> Sounds = new List<SourceProcessSoundCall>();
        internal int Skips;
        internal Rig(Inputs data, bool introduction = false, bool bothTeams = true)
        {
            Data = data; Configuration = SourceInputIntroChecks.Configuration(bothTeams);
            Game = data.Create(Configuration); Bridge = new FixedStepBridge(Game);
            Buffer = new SourceCabinetInputBuffer(Configuration, preserveSourceDigitalChanges: true);
            Quantizer = new SourceCabinetQuantizer(data.Hud.Input);
            var selector = new SourceCabinetPlaySelection(data.Hud);
            Game.SourceStationInputTransform = (state, tick, frame) => {
                if (frame.PhysicalSwitches != null)
                {
                    Check(Game.ConsumedSourceSwitches != null && Game.ConsumedSourceSwitches.SourceTick == tick,
                        "physical scan is committed before the actual transform/producer callback");
                    Check(ReferenceEquals(frame.PhysicalSwitches, Game.ConsumedSourceSwitches.PhysicalSwitches),
                        "selection-sound producer clone retains the SAME consumed physical sidecar");
                }
                return selector.ApplyAtSourceTick(state, tick, frame);
            };
            Game.SourceProcessSoundRaised += Sounds.Add;
            Services = new Services(this);
            Bridge.Restarted += () => { selector.Clear(); Services.Reset(); };
            if (introduction) Services.Bind(true);
            Capture();
        }
        internal SourcePhysicalSwitchFrame Capture(int p0 = 0, int p1 = 0, int p2 = 0, int p3 = 0,
            int physicalDirectionStation = -1, int screenRight = 0, int screenDown = 0,
            SourceCommand command = default(SourceCommand))
        {
            int[] buttons = { p0, p1, p2, p3 };
            var physical = new SourceStationInput[4];
            var sticks = new SourceCabinetStick[4];
            for (int id = 0; id < 4; id++)
            {
                sticks[id] = Quantizer.SevenLevel(id == physicalDirectionStation ? screenRight : 0,
                    id == physicalDirectionStation ? screenDown : 0);
                physical[id] = new SourceStationInput(id, sticks[id].StickX, sticks[id].StickZ,
                    (SourceButtons)buttons[id], digitalCurrent: (SourceDigitalDirections)sticks[id].DigitalDirections);
            }
            var samples = new SourceCabinetSample[Configuration.Stations.Count];
            for (int i = 0; i < samples.Length; i++)
            {
                int id = Configuration.Stations[i].StationId;
                samples[i] = new SourceCabinetSample(id, sticks[id], (SourceButtons)buttons[id],
                    id == 0 ? command : default(SourceCommand));
            }
            var frame = new SourcePhysicalSwitchFrame(physical);
            Buffer.Capture(samples, frame);
            return frame;
        }
        internal void Host()
        {
            long tick = Game.SourceTick;
            SourceConsumedPhysicalSwitchScan old = Game.ConsumedSourceSwitches;
            Bridge.AdvanceSource(FixedStepBridge.StepSeconds, Buffer.NextHostFrame);
            if (Game.SourceTick == tick)
            {
                Skips++;
                Check(ReferenceEquals(old, Game.ConsumedSourceSwitches), "host57:60 skip cannot publish or advance the source scan");
            }
        }
        internal void Step()
        {
            long tick = Game.SourceTick;
            for (int i = 0; i < 3 && Game.SourceTick == tick; i++) Host();
            Equal(tick + 1, Game.SourceTick, "one actual57Hz source wake through the existing bridge");
            Check(Game.ConsumedSourceSwitches != null && Game.ConsumedSourceSwitches.SourceTick == Game.SourceTick
                && Game.ConsumedSourceSwitches.InputEpoch == Game.SourceInputEpoch, "current typed source tick/epoch");
        }
        internal void Steps(int count) { for (int i = 0; i < count; i++) Step(); }
        internal void EnterSelection(SourcePlayType type = SourcePlayType.Normal, TeamId offense = TeamId.Home)
        {
            // Labelled result/clock fixture, not a natural browser play or score.
            Game.Model.Phase = GamePhase.DeadBall;
            Game.Model.SourceMatchPhase = SourceMatchPhase.PlayResult;
            Game.Model.Possession = offense;
            Game.Model.SourceRules = new SourceRulesState((int)offense, (int)offense, 1, 30, 60, type);
            Game.Model.SourceClock = SourceClock.Create();
            Game.Model.SourceBeginPlayInitialized = false;
            Capture(command: new SourceCommand(SourceAction.Continue));
            for (int i = 0; i < 4 && Game.State.Phase != GamePhase.PlaySelect; i++) Step();
            Equal(GamePhase.PlaySelect, Game.State.Phase, "actual ContinueSource enters real play_select caller");
        }
    }

    // Recording primitive/effect boundaries only. The Game, FIFO, dispatcher,
    // source waits, timer, source clock and selector are real production bodies.
    private sealed class Services : ISourceInstructionAdmissionState, ISourceInstructionInputs,
        ISourceInstructionEffects, ISourcePlaySelectionIntroductionInputs, ISourcePlaySelectionIntroductionEffects,
        ISourceKickoffInstructionChildInputs, ISourceKickoffInstructionChildEffects, ISourceKickoffInstructionPlayerButtons,
        ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects, ISourceKickoffPlayerObject,
        ISourceKickoffInstructionChildReturnBinding, ISourceKickoffInstructionFlashReturnBinding
    {
        private sealed class Plate : ISourceInstructionPlate { }
        private sealed class Text : ISourceInstructionText { internal readonly int Line; internal Text(int line) { Line = line; } }
        private sealed class Flasher : ISourceInstructionFlasher { }
        private readonly Rig rig;
        internal readonly List<string> Trace = new List<string>();
        internal readonly List<SourcePlaySelectionIntroductionPrint> Prints = new List<SourcePlaySelectionIntroductionPrint>();
        internal readonly List<SourceProcessSoundCall> IntroSounds = new List<SourceProcessSoundCall>();
        internal readonly int[] Games = new int[4];
        internal int Gate = 2, Flag, GamesReads, SwitchReads;
        internal bool ThrowIntroductionSound;
        private readonly Dictionary<long, int> pageOrdinals = new Dictionary<long, int>();
        internal SourceInstructionInvocation Invocation;
        internal Services(Rig rig) { this.rig = rig; }
        internal void Reset() { pageOrdinals.Clear(); }
        internal void Bind(bool introduction)
        {
            Invocation = new SourceInstructionInvocation(unchecked((int)0xf0f0f0f0u),
                new SourceInstructionProcessIds(0x0f9b, 0x0502, 0x1400, 0x0f10), this, this);
            rig.Game.BindSourceKickoffInstructionOwnedHelpers(Invocation, this, 0x0f9a, 4, 1, 2, this, this,
                new SourceKickoffInstructionChildOperands(false, 0, 0, 0, 1), this,
                new SourceKickoffInstructionOwnedHelpers(0x10, 0x20, 0x40, SourceFireIds.FlashWhite, this, this, this), this);
            if (introduction) rig.Game.BindSourcePlaySelectionIntroduction(this, this);
        }
        public int ReadIdiotBoxesShown() { return Flag; }
        public void WriteIdiotBoxesShown(int value) { Flag = value; if (value == 0) Gate = 2; }
        public int ReadPlayerStatus() { return rig.Configuration.Stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId); }
        public int ReadGamesPlayed(int slot) { GamesReads++; return Games[slot]; }
        public int ReadShowPlyselIdiotBox() { return Gate; }
        public void WriteShowPlyselIdiotBox(int value) { Trace.Add("gate:" + value); Gate = value; }
        public int ReadTsec() { return 57; }
        public int ReadIsLowResolution() { return 0; }
        public int ReadFourPlayerVersion() { return 1; }
        public int ReadOffensiveSide() { return (int)rig.Game.Model.Possession; }
        public SourceInstructionRaster ReadInstructionRaster() { return new SourceInstructionRaster(512, 384, 256, 192); }
        public int ReadPlayerSwitches()
        {
            SwitchReads++;
            SourceConsumedPhysicalSwitchScan scan = rig.Game.ConsumedSourceSwitches;
            if (scan == null || scan.SourceTick != rig.Game.SourceTick || scan.InputEpoch != rig.Game.SourceInputEpoch)
                throw new InvalidOperationException("No actual current consumed physical switches; no host-latest or unjoined zero fallback.");
            uint result = 0;
            foreach (SourceStationInput station in scan.PhysicalSwitches.Stations)
                result |= ((uint)station.Held << 4 | (uint)station.DigitalCurrent.Value) << (station.StationId * 8);
            return unchecked((int)result);
        }
        public uint ReadCurrentSwitchWord() { return unchecked((uint)ReadPlayerSwitches()); }
        public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition position)
        { return new Plate(); }
        public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint position)
        {
            Equal("playresult", image, "source intro image operand");
            Equal("SCR_PLATE_TID", texture, "source intro texture operand");
            Trace.Add("plate"); return new Plate();
        }
        public void SetPlateWidthScale(ISourceInstructionPlate plate, float scale) { Trace.Add("width:" + scale); }
        public void SetPlateHeightScale(ISourceInstructionPlate plate, float scale) { Trace.Add("height:" + scale); }
        public void SetPlateId(ISourceInstructionPlate plate, string id) { Equal("OID_MESSAGE", id, "source plate class"); }
        public void GeneratePlateVertices(ISourceInstructionPlate plate) { Check(plate != null, "actual source plate handle"); }
        public void SetTextFont(SourceInstructionFont font) { Trace.Add("font:" + font); }
        public void SetStringId(int id) { Trace.Add("string:" + id); }
        public void SetTextPosition(SourceInstructionPosition position) { }
        public void SetTextPosition(SourceInstructionPoint position) { Trace.Add("position:" + position.X + ":" + position.Y); }
        public void SetTextColor(SourceInstructionColor color) { }
        public void SetTextColor(SourcePlaySelectionIntroductionColor color) { Trace.Add("color:" + color); }
        public void SetTextZIncrement(float value) { Equal(0f, value, "original zero text z increment"); }
        public ISourceInstructionText Print(SourceInstructionTextRequest request) { return new Text(request.Line); }
        public ISourceInstructionText Print(SourcePlaySelectionIntroductionPrint request)
        { Prints.Add(request); Trace.Add("print:" + request.NativePrintLine); return new Text(request.NativePrintLine); }
        public void StoreMessage(int slot, ISourceInstructionText text) { Check(text != null, "source stored print return"); }
        public ISourceInstructionFlasher CreateTextFlasher(string name, int processId, ISourceInstructionText first,
            ISourceInstructionText second, int third, int fourth)
        {
            if (name == "pflash")
            {
                Equal(0, processId, "intro original pflash PID0");
                Check(first is Text && second is Text && third == 0 && fourth == 0, "source pmsg0/pmsg1 actual returned handles");
                Trace.Add("flasher");
            }
            return new Flasher();
        }
        public void KillTextFlasher(ISourceInstructionFlasher flasher, int signal)
        { Check(flasher is Flasher && signal == 0, "source flasher identity/signal"); Trace.Add("kill"); }
        public void DeleteStrings(int id, int mask) { }
        public void DeleteStrings(int id, uint mask) { Trace.Add("strings:" + id); Equal(uint.MaxValue, mask, "source string deletion mask"); }
        public void DeletePlateClass(string id, uint mask) { Trace.Add("plates:" + id); Equal(uint.MaxValue, mask, "source class deletion mask"); }
        public void DeletePlate(ISourceInstructionPlate plate) { }
        public void CreatePlateFlasher(string name, int pid, ISourceInstructionPlate plate, int count, int delay, int fourth) { }
        public void PlaySound(SourceInstructionSound sound)
        {
            long caller = rig.Game.Model.SourceProcesses.Caller;
            int ordinal; pageOrdinals.TryGetValue(caller, out ordinal); pageOrdinals[caller] = ordinal + 1;
            SourceProcessSoundCall call = rig.Game.TakeExecutingSourceInstructionSound(sound,
                new SourceInstructionSoundContext(rig.Game.SourceTick, caller, ordinal));
            Check(ReferenceEquals(call, rig.Sounds.Last()), "old page sound getter still returns exact previously emitted object");
        }
        public void PlaySound(SourcePlaySelectionIntroductionSound sound)
        {
            SourceProcessSoundCall call = rig.Game.TakeExecutingSourcePlaySelectionIntroductionSound(sound);
            Check(ReferenceEquals(call, rig.Sounds.Last()), "intro sound is stamped BEFORE effects and returns SAME event/queue object");
            Reject<InvalidOperationException>(() => rig.Game.TakeExecutingSourcePlaySelectionIntroductionSound(sound),
                "intro sound callback cannot observe/stamp a second output");
            Equal(1L, call.ProcessSerial, "original game_proc serial, not invented intro PID");
            Equal("game_proc", call.Process, "original inline source producer");
            Equal(rig.Game.SourceTick, call.SourceTick, "intro sound actual source wake");
            Equal(0, call.RandomCalls, "intro has no reselection draw");
            IntroSounds.Add(call); Trace.Add("sound");
            if (ThrowIntroductionSound) throw new InvalidOperationException("declared introductory sound effect failure");
        }
        public void PlaySound(SourceKickoffInstructionChildSound sound)
        { rig.Game.TakeExecutingSourceKickoffInstructionSound(sound); }
        public int ReadPlayerControl(int slot) { return rig.Game.Model.SourceStationPlayers[slot]; }
        public int ReadBallCarrier() { return rig.Game.Model.CarrierId; }
        public int ReadPlayerTeam(int player) { return (int)rig.Game.Model.Players[player].Team; }
        public int ReadCurrentButtons(int player) { return (int)rig.Game.Model.Players[player].SourceButtons; }
        public void WriteCurrentButtons(int player, int value) { rig.Game.Model.Players[player].SourceButtons = (SourceDroneButtons)value; }
        public int ReadOldButtons(int player) { return (int)rig.Game.Model.Players[player].SourceOldButtons; }
        public void WriteOldButtons(int player, int value) { rig.Game.Model.Players[player].SourceOldButtons = (SourceDroneButtons)value; }
        public void SetPlateZAngle(ISourceInstructionPlate plate, float value) { }
        public void SetPlateHScale(ISourceInstructionPlate plate, float value) { }
        public void SetPlateObjectId(ISourceInstructionPlate plate, string id, int slot) { }
        public void SetTextFont(SourceKickoffInstructionChildFont font) { }
        public void SetTextColor(SourceKickoffInstructionChildColor color) { }
        public void Print(SourceKickoffInstructionChildTextRequest request) { }
        public void DeletePlateClass(string symbol, int slot, uint mask) { }
        public ISourceKickoffPlayerObject ReadPlayerBlock(int player) { return this; }
        public uint ReadFlags() { return 0; }
        public void WriteFlags(uint flags) { }
        public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(SourceKickoffInstructionChildInvocation invocation,
            SourceKickoffInstructionChildReturnReason reason) { return SourceKickoffInstructionChildReturnAction.ExternallyRetire; }
        public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(SourceKickoffInstructionFlashInvocation invocation)
        { return SourceKickoffInstructionFlashReturnAction.ExternallyRetire; }
    }

    private static void ConsumedFifoAndSuspension(Inputs data)
    {
        var rig = new Rig(data);
        rig.Services.Bind(false);
        rig.Game.InvokeSourceInstructions(rig.Services.Invocation);
        rig.Step();
        SourcePhysicalSwitchFrame a = rig.Capture(p0: 1);
        SourcePhysicalSwitchFrame unjoined = rig.Capture(p0: 1, p3: 4, physicalDirectionStation: 3, screenRight: 3);
        SourcePhysicalSwitchFrame b = rig.Capture(p2: 2);
        SourcePhysicalSwitchFrame neutral = rig.Capture();
        foreach (SourcePhysicalSwitchFrame expected in new[] { a, unjoined, b, neutral })
        {
            rig.Step();
            SourceConsumedPhysicalSwitchScan scan = rig.Game.ConsumedSourceSwitches;
            Check(ReferenceEquals(expected, scan.PhysicalSwitches), "source-consumed physical FIFO order, not latest host capture");
            for (int station = 0; station < 4; station++)
                Equal(expected.Station(station).Held, scan.PhysicalSwitches.Station(station).Held, "physical ID preserved including unjoined");
            if (ReferenceEquals(expected, unjoined))
                Equal(expected.Station(3).DigitalCurrent.Value, rig.Game.Model.SourceDigitalScans[3].Current,
                    "unjoined decoded digital current scans once on the same actual source sample");
        }
        Check(rig.Game.Model.SourceProcesses.Require(1).Suspended,
            "real instructions_proc has suspended actual game_proc while physical FIFO continues");
        rig.Capture(p2: 2); rig.Steps(3);
        rig.Capture(); rig.Step();
        Equal(SourceButtons.B, rig.Game.ConsumedSourceSwitches.OpenedButtons(2), "opened uses two preceding source-held scans");
        rig.Step();
        Equal(SourceButtons.None, rig.Game.ConsumedSourceSwitches.OpenedButtons(2), "release is not replayed");
        rig.Capture(p1: 1); rig.Step();
        SourceConsumedPhysicalSwitchScan held = rig.Game.ConsumedSourceSwitches;
        Equal(SourceButtons.A, held.ClosedButtons(1), "unjoined closed scan is source-debounced");
        rig.Services.ReadPlayerSwitches(); rig.Services.ReadPlayerSwitches();
        Check(ReferenceEquals(held, rig.Game.ConsumedSourceSwitches), "tutorial reads neither consume nor rescan the digital FIFO");
        rig.Step();
        Equal(SourceButtons.None, rig.Game.ConsumedSourceSwitches.ClosedButtons(1), "held button is not a new closed edge");
        rig.Capture(p1: 2); rig.Capture();
        long epoch = rig.Game.SourceInputEpoch, tick = rig.Game.SourceTick;
        rig.Buffer.Clear(); rig.Bridge.SetActive(false);
        Check(rig.Game.ConsumedSourceSwitches == null && rig.Game.SourceInputEpoch > epoch, "pause invalidates the actual consumed epoch and pending input");
        Reject<InvalidOperationException>(() => rig.Services.ReadPlayerSwitches(), "a pre-pause physical scan cannot qualify the new input epoch");
        rig.Host(); Equal(tick, rig.Game.SourceTick, "paused source does not dispatch");
        rig.Capture(p3: 4);
        Check(rig.Buffer.WaitingForNeutral, "unjoined physical held button also enforces neutral latch");
        rig.Capture();
        Check(!rig.Buffer.WaitingForNeutral, "allfour physical neutral releases latch");
        rig.Bridge.SetActive(true); rig.Step();
        Equal(SourceButtons.None, rig.Game.ConsumedSourceSwitches.PhysicalSwitches.Station(1).Held, "pause-discarded queued edge cannot reappear");
        long priorEpoch = rig.Game.SourceInputEpoch;
        rig.Buffer.Clear(); rig.Bridge.Restart();
        Check(rig.Game.SourceInputEpoch > priorEpoch && rig.Game.ConsumedSourceSwitches == null && rig.Game.SourceTick == 0,
            "same Game restart clears consumed view and retains monotonic input epoch");
        rig.Capture(); rig.Step();
        Equal(SourceButtons.None, rig.Game.ConsumedSourceSwitches.ClosedButtons(1), "new epoch does not replay prior switch history");
        Check(rig.Skips > 0, "actual host57:60 skip path exercised");
    }

    private static void IntroductionHeldAndPages(Inputs data)
    {
        var rig = new Rig(data, introduction: true);
        rig.Services.Flag = 1; // Labelled admitted-kickoff-return fixture; not a natural catch claim.
        rig.EnterSelection();
        long entry = rig.Game.SourcePlaySelectionIntroduction.EntrySourceTick;
        long drawsAtEntry = rig.Game.Model.SourceRandomDraws;
        Equal(12, rig.Services.Gate, "intro+10 precedes timer's first child wake");
        Equal(1L, rig.Game.SourcePlaySelectionIntroduction.Process.Serial, "intro is actual inline game_proc continuation");
        Equal(10, rig.Services.Prints.Count, "eight shared prints plus actual branch's two labels");
        Equal("1075,1081,1083,1086,1088,1090,1099,1104,1162,1180",
            string.Join(",", rig.Services.Prints.Select(print => print.NativePrintLine)), "actual source print call order and sites");
        Check(rig.Services.Prints.Take(8).All(print => print.ColorOperand.HasValue)
            && rig.Services.Prints.Skip(8).All(print => !print.ColorOperand.HasValue), "jc color operands and j selected-color labels retained");
        rig.Capture(p0: 1);
        rig.Steps(39);
        Equal(SourcePlaySelectionIntroductionStage.WaitingActiveTeam, rig.Game.SourcePlaySelectionIntroduction.Stage, "held own ABC cannot skip40-source-wake minimum");
        Equal(39, rig.Game.SourcePlaySelectionIntroduction.WaitTicks, "source active-team elapsed ticks");
        Equal(9, rig.Game.State is ISourceMatchState ? ((ISourceMatchState)rig.Game.State).Match.SelectionTimer.Number : -1,
            "existing selection timer is frozen by actual gate12");
        rig.Step();
        Equal(entry + 40, rig.Game.SourceTick, "held acceptance resumes exact original40th source wake");
        Equal(SourcePlaySelectionIntroductionStage.Returned, rig.Game.SourcePlaySelectionIntroduction.Stage, "source introductory helper really returns");
        Equal(1, rig.Services.Gate, "source suffix subtracts11 once after real return");
        Equal(1, rig.Services.IntroSounds.Count, "exactly one original intro ricochet");
        Equal(drawsAtEntry, rig.Game.Model.SourceRandomDraws, "the actual blocked intro/timer/scan path consumes no extra RNG");
        SourceProcessSoundCall intro = rig.Services.IntroSounds[0];
        Check(intro.Kind == SourceProcessSoundKind.PlaySelectionIntroduction && intro.PlaylistId == 148
            && intro.Volume == 115 && intro.Pan == 127 && intro.PriorityWord == 1, "original intro sound tuple distinct from page volume/priority");
        string suffix = string.Join("|", rig.Services.Trace.Skip(rig.Services.Trace.LastIndexOf("sound")));
        Equal("sound|kill|strings:52|plates:OID_MESSAGE|gate:1", suffix, "original sound/kill/strings/plates/gate suffix order");
        Reject<InvalidOperationException>(() => rig.Game.TakeExecutingSourcePlaySelectionIntroductionSound(SourcePlaySelectionIntroductionSound.Ricochet),
            "intro output cannot be observed/stamped outside its live callback");
        rig.Steps(21);
        Equal(1, rig.Services.Gate, "later menu wakes do not synthesize another suffix");
        Equal(GamePhase.PlaySelect, rig.Game.State.Phase, "intro held edge is not replayed as a selector choice");
        rig.Capture(); rig.Step(); rig.Capture(p0: 2, p2: 2); rig.Step();
        Check(rig.Game.SourceInstructionProgram != null, "actual eligible NORMAL selector return still admits the four-page program");
        rig.Capture(p3: 4);
        for (int i = 0; i < 100 && rig.Game.SourceInstructionProgram.Stage != SourceInstructionStage.Fallthrough; i++) rig.Step();
        Equal(SourceInstructionStage.Fallthrough, rig.Game.SourceInstructionProgram.Stage, "all four real pages and final10-wake continuation complete");
        Equal(2, rig.Services.Flag, "original four-page admission final flag");
        Equal(1, rig.Services.IntroSounds.Count, "page completion never replays introductory suffix");
        SourceProcessSoundCall[] calls = rig.Game.TakeSourceProcessSoundCalls().ToArray();
        Check(calls.Any(call => ReferenceEquals(call, intro)), "canonical common sound queue retains SAME introductory object");
        Equal(calls.Length, calls.Select(call => call.OutputSequence).Distinct().Count(), "no duplicate or re-stamped common source output");
        Equal(4, calls.Count(call => call.Kind == SourceProcessSoundKind.InstructionPageAdvance), "four actual page ricochets preserved");
    }

    private static void IntroductionGateTimeoutPatAndReset(Inputs data)
    {
        var wrong = new Rig(data, introduction: true);
        wrong.EnterSelection(); wrong.Capture(p1: 1, p2: 4); wrong.Steps(8);
        long pausedTick = wrong.Game.SourceTick;
        wrong.Buffer.Clear(); wrong.Bridge.SetActive(false); wrong.Host(); wrong.Host();
        Equal(pausedTick, wrong.Game.SourceTick, "pause freezes actual inline introductory continuation");
        Equal(8, wrong.Game.SourcePlaySelectionIntroduction.WaitTicks, "no host-time intro wait progress");
        wrong.Capture(); wrong.Bridge.SetActive(true); wrong.Capture(p1: 1, p2: 4); wrong.Steps(33);
        Equal(SourcePlaySelectionIntroductionStage.WaitingActiveTeam, wrong.Game.SourcePlaySelectionIntroduction.Stage,
            "unjoined own-team and joined opponent current ABC cannot accept active-team wait");
        wrong.Capture(p0: 2); wrong.Step();
        Equal(42, wrong.Game.SourcePlaySelectionIntroduction.WaitTicks, "own team current held accepts after minimum without all-buttons requirement");

        var timeout = new Rig(data, introduction: true);
        timeout.EnterSelection(); long entry = timeout.Game.SourceTick;
        timeout.Steps(626);
        Check(timeout.Game.SourcePlaySelectionIntroduction.Active, "active-team total627 timeout has not returned at626");
        timeout.Step();
        Equal(entry + 627, timeout.Game.SourceTick, "active-team max includes40, not40+627");
        Equal(SourcePlaySelectionIntroductionStage.Returned, timeout.Game.SourcePlaySelectionIntroduction.Stage, "real total timeout suffix returns");
        Equal(1, timeout.Services.IntroSounds.Count, "timeout also emits one source ricochet, no tick");

        var experienced = new Rig(data, introduction: true);
        experienced.Services.Games[0] = experienced.Services.Games[2] = 3;
        experienced.EnterSelection();
        Equal(SourcePlaySelectionIntroductionStage.Skipped, experienced.Game.SourcePlaySelectionIntroduction.Stage, "games_played3 excludes experienced joined slots");
        Check(experienced.Services.Gate == 0 && experienced.Services.Prints.Count == 0 && experienced.Services.IntroSounds.Count == 0,
            "unjoined inexperienced slots cannot fabricate an intro");

        var zero = new Rig(data, introduction: true);
        zero.Services.Gate = 0; zero.EnterSelection();
        Equal(0, zero.Services.GamesReads, "zero gate short-circuits experience reads");

        var cpu = new Rig(data, introduction: true, bothTeams: false);
        cpu.EnterSelection(offense: TeamId.Away);
        Check(cpu.Game.SourcePlaySelectionIntroduction.Stage == SourcePlaySelectionIntroductionStage.Returned
            && cpu.Game.SourcePlaySelectionIntroduction.WaitTicks == 0 && cpu.Services.Gate == 1,
            "FourStation CPU offense has no manufactured timed block");
        Equal(1124, cpu.Services.Prints[8].NativePrintLine, "right-side offense uses actual alternate print sites");
        Equal(1143, cpu.Services.Prints[9].NativePrintLine, "left-side defense uses actual alternate print sites");

        var pat = new Rig(data, introduction: true);
        pat.Services.Flag = 1;
        pat.EnterSelection(SourcePlayType.ExtraPoint); pat.Capture(p0: 1); pat.Steps(40);
        Equal(SourcePlaySelectionIntroductionStage.Returned, pat.Game.SourcePlaySelectionIntroduction.Stage, "PAT also executes real pre-selection introduction");
        pat.Steps(21); pat.Capture(); pat.Step();
        pat.Capture(command: new SourceCommand(SourceAction.SelectPat, patChoice: SourcePatOption.TwoPointPlay)); pat.Step();
        pat.Capture(p2: 2); pat.Step();
        Check(pat.Game.SourceInstructionProgram == null, "actual PAT selection return does not admit NORMAL four-page instruction arm");
        Equal(1, pat.Services.IntroSounds.Count, "PAT introductory suffix remains once-only");

        var reset = new Rig(data, introduction: true);
        reset.EnterSelection(); reset.Steps(4);
        reset.Buffer.Clear(); reset.Bridge.Restart(); reset.Capture();
        Check(reset.Game.SourcePlaySelectionIntroduction == null && reset.Services.Gate == 2,
            "real Game restart clears inline continuation and restores coherent new-match gate");
        reset.EnterSelection(); reset.Capture(p0: 1); reset.Steps(40);
        Equal(1, reset.Services.IntroSounds.Count, "abandoned pre-reset intro never emits or replays suffix");
    }

    private static void LegacyAndPhysicalGuards(Inputs data)
    {
        var neutral = new SourceStationInput[4];
        for (int i = 0; i < 4; i++) neutral[i] = new SourceStationInput(i, digitalCurrent: SourceDigitalDirections.None);
        Reject<ArgumentException>(() => new SourcePhysicalSwitchFrame(neutral.Take(3).ToArray()), "physical frame cannot infer unjoined fourth station");
        SourceStationInput[] duplicate = (SourceStationInput[])neutral.Clone(); duplicate[3] = duplicate[2];
        Reject<ArgumentException>(() => new SourcePhysicalSwitchFrame(duplicate), "physical frame cannot remap duplicate station IDs");
        SourceStationInput[] command = (SourceStationInput[])neutral.Clone();
        command[0] = new SourceStationInput(0, command: new SourceCommand(SourceAction.Continue), digitalCurrent: SourceDigitalDirections.None);
        Reject<ArgumentException>(() => new SourcePhysicalSwitchFrame(command), "physical sidecar is current switches, not a second command/edge stream");
        Check(new SourceInputFrame(new[] { new SourceStationInput(0) }).PhysicalSwitches == null, "old exact constructor leaves binding unavailable, never fake neutral");
        SourceMatchConfiguration configuration = Configuration();
        var buffer = new SourceCabinetInputBuffer(configuration);
        var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
        SourceCabinetStick stick = quantizer.SevenLevel(0, 0);
        buffer.Capture(new[] { new SourceCabinetSample(2, stick, SourceButtons.None),
            new SourceCabinetSample(0, stick, SourceButtons.A | SourceButtons.B | SourceButtons.C) });
        buffer.Capture(new[] { new SourceCabinetSample(2, stick, SourceButtons.None),
            new SourceCabinetSample(0, stick, SourceButtons.None) });
        SourceInputFrame pressed = buffer.NextHostFrame(), released = buffer.NextHostFrame();
        Equal(SourceButtons.A | SourceButtons.B | SourceButtons.C, pressed.Stations[1].Pressed, "old Capture overload retains full raw simultaneous ABC press");
        Equal(SourceButtons.A | SourceButtons.B | SourceButtons.C, released.Stations[1].Released, "old Capture overload retains full raw simultaneous ABC release");
        Check(pressed.PhysicalSwitches == null && released.PhysicalSwitches == null, "old buffer overload never fabricates absent physical stations");
        buffer.Clear();
        buffer.Capture(new[] { new SourceCabinetSample(2, stick, SourceButtons.None),
            new SourceCabinetSample(0, stick, SourceButtons.None) });
        Check(!buffer.WaitingForNeutral && buffer.NextHostFrame().PhysicalSwitches == null, "old configured-only neutral policy remains unbound");
        var rig = new Rig(data);
        rig.Game.Step(new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(0), new SourceStationInput(2) })));
        rig.Game.Step(new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(0), new SourceStationInput(2) })));
        Check(rig.Game.SourceTick > 0 && rig.Game.ConsumedSourceSwitches == null, "old base/unbound route remains valid without physical binding");
        Reject<InvalidOperationException>(() => rig.Services.ReadPlayerSwitches(), "a typed instruction provider must reject unavailable old input path");
        Reject<InvalidOperationException>(() => rig.Game.BindSourcePlaySelectionIntroduction(rig.Services, rig.Services),
            "unbound/late introduction cannot be promoted by a renderer");
    }

    private static void IntroductionSoundFailureAndRestart(Inputs data)
    {
        var rig = new Rig(data, introduction: true);
        rig.Services.ThrowIntroductionSound = true;
        rig.EnterSelection(); rig.Capture(p0: 1); rig.Steps(39);
        Reject<InvalidOperationException>(rig.Step, "declared actual introductory effect failure is not completion");
        Check(rig.Game.SourcePlaySelectionIntroduction.Stage == SourcePlaySelectionIntroductionStage.Faulted
            && rig.Services.Gate == 12 && rig.Game.Model.SourceAnimationFailure != null,
            "fault cannot publish successful cleanup/decrement or resume selector");
        SourceProcessSoundCall failed = rig.Services.IntroSounds.Single();
        Check(rig.Game.TakeSourceProcessSoundCalls().Any(call => ReferenceEquals(call, failed)),
            "failed effect retains the original already-stamped canonical call");
        long tick = rig.Game.SourceTick;
        Reject<InvalidOperationException>(rig.Step, "faulted source Game requires actual restart, never retry");
        Equal(tick, rig.Game.SourceTick, "failed retry cannot advance source or stamp another sound");
        Equal(1, rig.Services.IntroSounds.Count, "failed typed sound attempt has one genuine output");
        rig.Services.ThrowIntroductionSound = false;
        rig.Buffer.Clear(); rig.Bridge.Restart(); rig.Capture();
        rig.EnterSelection(); rig.Capture(p0: 1); rig.Steps(40);
        Equal(SourcePlaySelectionIntroductionStage.Returned, rig.Game.SourcePlaySelectionIntroduction.Stage, "same Game real restart admits fresh source continuation");
        SourceProcessSoundCall next = rig.Services.IntroSounds.Last();
        Check(next.OutputSequence > failed.OutputSequence, "new intro reuses preserved common source sound ordinal, never rewinds/stamps twice");
        Equal(1, rig.Services.Gate, "only new successful intro publishes its source suffix");
    }

    private static void RejectUnauthenticatedPhysicalDecode(Inputs data)
    {
        var rig = new Rig(data);
        var physical = new SourceStationInput[4];
        for (int id = 0; id < 4; id++)
            physical[id] = new SourceStationInput(id, digitalCurrent: SourceDigitalDirections.None);
        int ordinal = (3 - 0) * 7 + 3 - 3;
        var actual = (SourceDigitalDirections)data.Hud.Input.Digital8[ordinal];
        physical[3] = new SourceStationInput(3, stickX: 3, digitalCurrent: actual ^ SourceDigitalDirections.Up);
        var frame = new SourceInputFrame(new[] {
            new SourceStationInput(0, digitalCurrent: SourceDigitalDirections.None),
            new SourceStationInput(2, digitalCurrent: SourceDigitalDirections.None) }, new SourcePhysicalSwitchFrame(physical));
        rig.Game.Step(new PlayerCommand(stationInputs: frame));
        Reject<InvalidOperationException>(() => rig.Game.Step(new PlayerCommand(stationInputs: frame)),
            "actual authenticated49-to8 scanner must reject a mismatched unjoined physical decode");
        Check(rig.Game.Model.SourceAnimationFailure != null && rig.Sounds.Count == 0,
            "invalid physical decode fails before source instruction/sound effects");
    }

    private static void Main(string[] args)
    {
        Check(args.Length == 2, "Pass approved563 root and separately authenticated metadata-only successor descriptor.");
        byte[] baseManifest = File.ReadAllBytes(Path.Combine(args[0], "manifest.json"));
        var successor = new Inputs(args[0], File.ReadAllBytes(args[1]), true);
        var original = new Inputs(args[0], baseManifest, false);
        ConsumedFifoAndSuspension(successor);
        IntroductionHeldAndPages(successor);
        IntroductionGateTimeoutPatAndReset(successor);
        LegacyAndPhysicalGuards(original);
        RejectUnauthenticatedPhysicalDecode(successor);
        IntroductionSoundFailureAndRestart(successor);
        Equal("150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6",
            Hash(File.ReadAllBytes(Path.Combine(args[0], "manifest.json"))), "approved original descriptor is unchanged");
        Console.WriteLine("PASS source-consumed FIFO/intro: " + assertions + " assertions; bounded actual Core/clock fixtures, not browser/native evidence.");
    }
}
