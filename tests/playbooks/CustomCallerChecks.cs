using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;
using Blitz.Storage;

internal static class CustomCallerChecks
{
    private static int assertions;
    private static FullKernelInputs inputs;
    private static SourceInterfaceData hud;
    private static SourceInterfaceMovementBootstrap bootstrap;
    private static SourceCinematicLibrary impact;
    private static SourcePlaybook book;
    private static readonly List<object> cases = new List<object>();
    private static readonly List<object> trace = new List<object>();
    private static string nativePath, outputPath;
    private static NativeC executingNative;
    private static Fixture activeFixture;
    private static int nativeRouteWakes, nativeRouteDraws;
    private static int capturedEntries;
    private static readonly List<object> nativeTrace = new List<object>();
    private static readonly SortedSet<int> executed = new SortedSet<int>();

    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException("CUSTOM_CALLER: " + message); }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void ReadHud(string root)
    {
        byte[] manifest = File.ReadAllBytes(Path.Combine(root, "manifest.json"));
        Check(Hash(manifest) == "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6",
            "explicit unchanged pre-snap-switch563 input descriptor, not a relabeled AV306 profile");
        using (JsonDocument doc = JsonDocument.Parse(manifest))
        {
            JsonElement descriptor = doc.RootElement;
            Func<string, byte[]> read = entry => {
                string path = descriptor.GetProperty("entrypoints").GetProperty(entry).GetString();
                JsonElement pin = descriptor.GetProperty("files").EnumerateArray().Single(
                    item => item.GetProperty("path").GetString() == path);
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
                Check(bytes.Length == pin.GetProperty("bytes").GetInt32()
                    && Hash(bytes) == pin.GetProperty("sha256").GetString(), "authenticated explicit " + entry);
                return bytes;
            };
            hud = SourceInterfaceData.Read(read("hud"), SourceInterfacePackagePlan.HudSha256);
            byte[] boot = read("bootstrap");
            bootstrap = SourceInterfaceMovementBootstrap.Read(boot, Hash(boot));
            impact = SourceCinematicLibrary.Read(read("impact"), SourceTeamResourceProfile.PreSnapSwitchImpactSha256);
            book = SourceInterfaceData.ReadDefaultAudiblePlaybook(read("playbook"));
            hud.BindDefaultAudiblePresentation(inputs.Rosters, bootstrap);
        }
    }

    private static SourceCustomPlay Grid(int formation, string name)
    {
        SourceCustomPlay play = SourceCustomPlayCodec.CreateFromFormation(formation, name, "CAP");
        for (int receiver = 0; receiver < 3; receiver++)
        {
            SourceCustomPoint root = play.Point(receiver, 0);
            play = SourceCustomPlayCodec.ReplaceRoute(play, receiver, new[] {
                root, new SourceCustomPoint(root.X, 10, SourceCustomPointFlags.Turbo),
                new SourceCustomPoint(root.X + 2, 12, SourceCustomPointFlags.Spin),
                new SourceCustomPoint(root.X + 2, 14, SourceCustomPointFlags.Juke),
                new SourceCustomPoint(root.X + 4, 16, SourceCustomPointFlags.Last)
            });
        }
        return play;
    }

    private sealed class Fixture
    {
        internal readonly string Name;
        internal readonly GameSimulation Game;
        internal readonly SourceMatchConfiguration Configuration;
        internal readonly SourceInjectedRandom Random;
        internal readonly SourceCabinetPlaySelection Menu;
        internal readonly TeamId Offense;
        internal readonly SourceSavedRecordSession Records;
        internal readonly NativeC Native;
        internal readonly SourceButtons[] Buttons = new SourceButtons[4];
        internal readonly int[] X = new int[4], Z = new int[4];
        private readonly SourceCabinetInputBuffer buffer;
        private readonly SourceCabinetQuantizer quantizer;
        internal SourceMatchView Match { get { return ((ISourceMatchState)Game.State).Match; } }
        internal int Captain { get { return SourceCabinetStations.Captain(Match, Offense); } }
        internal int WakeCount;
        internal Observation Entry;
        private readonly StreamWriter stateLog;
        private int randomAt;

        internal Fixture(TeamId offense, bool cooperative)
        {
            Offense = offense;
            Name = "P1/custom/" + offense + (cooperative ? "/cooperative" : "/one-human");
            int station = (int)offense * 2;
            var stations = new List<SourceStationConfiguration> { new SourceStationConfiguration(station, offense) };
            if (cooperative) stations.Add(new SourceStationConfiguration(station + 1, offense));
            Configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, new SourceTeamSelection(0, Enumerable.Range(0, 7).ToArray()),
                new SourceTeamSelection(1, Enumerable.Range(0, 7).ToArray()), stations,
                (TeamId)(1 - (int)offense), cooperative ? SourceCaptainSlot.Second : SourceCaptainSlot.First);
            var selections = new List<SourceStationRecordSelection>();
            foreach (SourceStationConfiguration participant in stations)
            {
                var grids = new SourceCustomPlay[9];
                grids[0] = Grid(participant.StationId % 2, "S" + participant.StationId);
                grids[1] = Grid(1 - participant.StationId % 2, "ALT" + participant.StationId);
                var record = new SourcePlayerRecord(participant.StationId,
                    new SourceRecordIdentity("CAP" + (char)('A' + participant.StationId), "0000"),
                    new SourceRecordCareer(gamesPlayed: 12, gamesWon: 10), playsAllowed: 9, customPlays: grids,
                    audibles: new SourceRecordAudibles(new short[] { -38, 37, 38 }, new short[3]));
                selections.Add(new SourceStationRecordSelection(participant.StationId,
                    SourceRecordSelectionState.KnownSaved, record: record));
            }
            Records = new SourceSavedRecordSession(Configuration, selections);
            SourceInjectedRandom random = null;
            Game = SourceC2Composition.CreateDefaultAudiblesSimulation(inputs.Animations, inputs.ReferencePivots,
                book, Configuration, inputs.Rosters,
                new SourcePresentationSettings(offense, new[] { -1, -1, -1, -1 }, 0),
                inputs.MovementRuntime, new SourceMovementSettings(0, 0, false, 0, 0, false),
                inputs.SoundFunctions, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(inputs.C2Data),
                seed => random = (SourceInjectedRandom)SourceRandom.CreatePortableLibrary(seed,
                    "finite nonempty CAP callers; unchanged seed1; target libc unverified"),
                compatibility: SourceGameplayCompatibility.Portable,
                defaultAudibles: hud.CreateDefaultAudibleData(book), impactLibrary: impact);
            Random = random;
            Game.BindSourceCustomPlaySession(new SourceCustomPlaySession(Records), hud);
            Menu = new SourceCabinetPlaySelection(hud, customPlaySelection: Game);
            Game.SourceStationInputTransform = Menu.ApplyAtSourceTick;
            buffer = new SourceCabinetInputBuffer(Configuration, preserveSourceDigitalChanges: true);
            quantizer = new SourceCabinetQuantizer(hud.Input);
            Game.SourceCameraHeadingProvider = (state, tick) =>
                FullKernelInputs.SetControlledMovementCamera(Game, state, tick);
            if (nativePath != null) Native = new NativeC(this);
            activeFixture = this;
            stateLog = new StreamWriter(outputPath + "." + Name.Replace('/', '-') + ".state.jsonl", false);
        }

        internal void Wake(SourceCommand command = default(SourceCommand))
        {
            Entry = null;
            long before = Game.SourceTick;
            for (int host = 0; host < 3 && Game.SourceTick == before; host++)
            {
                buffer.Capture(Configuration.Stations.Select(s => new SourceCabinetSample(s.StationId,
                    quantizer.SevenLevel(X[s.StationId], Z[s.StationId]), Buttons[s.StationId])).ToArray());
                Game.Step(new PlayerCommand(source: command, stationInputs: buffer.NextHostFrame()));
            }
            Check(Game.SourceTick == before + 1, Name + ": one actual source wake");
            if (Entry != null) Native.Receiver(Entry, new Observation(Game), this);
            WakeCount++;
            SimulationState m = Game.Model;
            stateLog.WriteLine(JsonSerializer.Serialize(new {
                Game.SourceTick, m.SourceSnapTick, phase = m.Phase.ToString(), m.SourceFlags,
                m.CarrierId, m.ThrowerId, m.TargetPlayerId, m.SourceRules, m.SourceOffenseFlipped,
                offense = m.SourceOffensePlay.Identity, defense = m.SourceDefensePlay.Identity,
                m.SourceBallPosition, m.SourceBallTarget, m.SourceBallVelocity, m.SourceBallVerticalVelocity,
                m.PassTicksElapsed, m.PassTicksTotal, m.SourceClock.PackedTime,
                controls = m.SourceStationPlayers.ToArray(),
                input = Configuration.Stations.Select(s => new { s.StationId,
                    x = X[s.StationId], z = Z[s.StationId], held = (int)Buttons[s.StationId] }).ToArray(),
                actors = m.Players.Select(p => new { p.Id, p.SourceMode, p.SourceControllerStation,
                    position = SourcePlayerDriver.WorldPosition(p), p.SourceVelocity,
                    p.Facing, p.TargetFacing, p.SourceButtons, p.SourceNewButtons, p.SourceJob, p.SourceTarget,
                    script = p.AnimationState.Primary.ScriptSymbol, frame = p.AnimationState.Primary.FrameOrdinal,
                    mode = p.AnimationState.Primary.Mode }).ToArray(),
                routes = m.SourceDrones.Select(s => new { program = s.Program.Identity,
                    ints = StateInts(s), floats = StateFloats(s), s.PendingCatch, s.LastSourceTick }).ToArray(),
                events = Game.SourceEvents.Select(e => new { e.Type, e.SourceTick, e.ActorPlayerId, e.TargetPlayerId }).ToArray(),
                rng = Random.Trace.Skip(randomAt).ToArray()
            }));
            randomAt = Random.Trace.Count;
        }

        internal void Wakes(int count)
        { for (int i = 0; i < count; i++) Wake(); }

        internal void Close() { if (stateLog != null) stateLog.Dispose(); }

        internal void Press(SourceButtons button)
        {
            Buttons[Captain] = SourceButtons.None; Wake();
            Buttons[Captain] = button; Wake();
            Buttons[Captain] = SourceButtons.None; Wake();
        }

        internal void Note(string stage)
        {
            SimulationState m = Game.Model;
            trace.Add(new { stage = Name + "/" + stage, Game.SourceTick,
                phase = m.Phase.ToString(), match = Match.Phase.ToString(), possession = m.Possession.ToString(),
                kind = m.SourceRules.PlayType.ToString(), play = m.SourceOffensePlay.Identity,
                m.SourceOffenseFlipped, controls = m.SourceStationPlayers.ToArray(),
                routes = m.SourceDrones.Select(s => new { program = s.Program.Identity, s.WordIndex,
                    flags = (int)s.Flags, s.Target, s.Job, s.LastSourceTick }).ToArray(),
                events = Game.SourceEvents.Select(e => new { e.Type, e.SourceTick, e.ActorPlayerId, e.TargetPlayerId }).ToArray(),
                rng = Random.Trace.ToArray() });
        }

        internal void SelectFromRealKickoff()
        {
            for (int wake = 0; wake < 2200 && Match.Phase != SourceMatchPhase.PlaySelection; wake++)
            {
                SourceCommand command = Match.Phase == SourceMatchPhase.PlayResult
                    ? new SourceCommand(SourceAction.Continue) : default(SourceCommand);
                Wake(command);
                if (wake % 200 == 0) Note("real-opening-kickoff-" + wake);
            }
            Note("normal-menu");
            Check(Match.Phase == SourceMatchPhase.PlaySelection && Match.Possession.Offense == Offense,
                Name + ": real opening kickoff/result reaches the participating offense's normal menu; actual=" +
                Match.Phase + "/" + Match.Possession.Offense + "/" + Game.Model.Phase);
            Wakes(30);
            for (int page = 0; page < 4; page++) Press(SourceButtons.C);
            Check(Menu.Page(Offense) == 4 && Menu.Cursor(Offense) == 0,
                Name + ": actual captain's four raw C presses reach nonempty custom slot0");
            Press(SourceButtons.B);
            for (int wake = 0; wake < 200 && Match.Phase != SourceMatchPhase.PreSnap; wake++) Wake();
            Note("selected");
            Check(Match.Phase == SourceMatchPhase.PreSnap &&
                Game.Model.SourceOffensePlay.Identity == "custom_p" + Captain + "_s0",
                Name + ": real saved origin reaches actual normal formation");
        }
    }

    private sealed class Observation
    {
        private static readonly MethodInfo Frame = typeof(GameSimulation).GetMethod(
            "BuildSourceAiFrame", BindingFlags.Instance | BindingFlags.NonPublic);
        internal readonly SourceAiFrame Ai;
        internal readonly SourceRouteState[] Routes;
        internal readonly int[] TargetFacing, Buttons, NewButtons;
        internal readonly long Tick, Snap;
        internal readonly int Quarter, Handicap0, Handicap1;
        internal readonly string Offense, Defense;
        internal readonly SourceQuarterbackPlan Plan;
        internal readonly int RandomAt;
        internal Observation(GameSimulation game, SourceAiFrame supplied = null)
        {
            SimulationState m = game.Model;
            Ai = supplied ?? (SourceAiFrame)Frame.Invoke(game, new object[] { true });
            Routes = m.SourceDrones.ToArray();
            TargetFacing = m.Players.Select(p => p.TargetFacing).ToArray();
            Buttons = m.Players.Select(p => (int)p.SourceButtons).ToArray();
            NewButtons = m.Players.Select(p => (int)p.SourceNewButtons).ToArray();
            Tick = game.SourceTick; Snap = m.SourceSnapTick; Quarter = m.Quarter - 1;
            Handicap0 = m.SourceEffectiveTeamHandicap[0]; Handicap1 = m.SourceEffectiveTeamHandicap[1];
            Offense = m.SourceOffensePlay.Identity; Defense = m.SourceDefensePlay.Identity;
            Plan = m.SourceQuarterbackPlan;
            RandomAt = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        }
    }

    internal static void RecordEntry(GameSimulation game, ActorState actor, SourceAiFrame frame)
    {
        Fixture fixture = activeFixture;
        if (fixture == null || !ReferenceEquals(fixture.Game, game)
            || actor.Id != (int)fixture.Offense * 7 + 6 || game.Model.Phase != GamePhase.LiveBall
            || (game.Model.SourceOffensePlay.Flags & SourcePlayFlags.Custom) == 0) return;
        Check(fixture.Entry == null, fixture.Name + ": one actual focused production caller entry per source wake");
        fixture.Entry = new Observation(game, frame);
        capturedEntries++;
    }

    private static int[] StateInts(SourceRouteState s)
    {
        return new[] { s.WordIndex, (int)s.Flags, s.Stick, s.OldStick, s.StickTicks,
            (int)s.Buttons, (int)s.OldButtons, s.ButtonTicks, (int)s.Job, s.Target,
            s.PassReactTicks, s.WaitTicks, s.BlockTicks, s.FakeTicks, s.CatchTicks,
            s.EvadeTicks, s.ThinkTicks, s.BlitzTicks, s.MoveTicks, s.OrderTicks, s.OrderStick, s.FakeZoneTicks };
    }

    private static float[] StateFloats(SourceRouteState s)
    {
        return new[] { s.Mark.X, s.Mark.Z, s.Ghost.X, s.Ghost.Z, s.GhostVelocity.X,
            s.GhostVelocity.Z, s.ZoneLowX, s.ZoneHighX, s.ZoneLowZ, s.ZoneHighZ,
            s.ZonePoint.X, s.ZonePoint.Z, s.ZoneVelocity.X, s.ZoneVelocity.Z };
    }

    private sealed class NativeC : IDisposable
    {
        private readonly Process process;
        private readonly string output;
        private readonly List<string> transcript = new List<string>();
        private bool disposed;
        internal NativeC(Fixture fixture)
        {
            output = outputPath + "." + fixture.Name.Replace('/', '-') + ".original-C.txt";
            process = Process.Start(new ProcessStartInfo(nativePath) {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true });
            executingNative = this;
            foreach (SourceStationRecordSelection station in fixture.Records.Stations)
                for (int slot = 0; slot < 2; slot++)
                {
                    string hex = BitConverter.ToString(SourceCustomPlayCodec.EncodeCompact(
                        station.Record.CustomPlays[slot])).Replace("-", "");
                    Check(Ask("cap-input", station.StationId, slot, hex)[0] == "CAP-INPUT",
                        fixture.Name + ": original C receives the actual source compact CAP input");
                }
        }

        private void Send(params object[] values)
        {
            string line = string.Join(" ", values.Select(v => v is float
                ? ((float)v).ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToString(v, CultureInfo.InvariantCulture)));
            transcript.Add("> " + line);
            process.StandardInput.WriteLine(line); process.StandardInput.Flush();
        }

        private string[] Ask(params object[] values)
        {
            Send(values);
            string line = process.StandardOutput.ReadLine();
            if (line == null)
                throw new InvalidOperationException("Original CAP C exited: " + process.StandardError.ReadToEnd());
            transcript.Add(line);
            return line.Split(' ');
        }

        private static int Int(string value) { return int.Parse(value, CultureInfo.InvariantCulture); }
        private static float Float(string value) { return float.Parse(value, CultureInfo.InvariantCulture); }
        private static void FloatEqual(float actual, string expected, string message)
        { Check(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(Float(expected)), message); }

        internal void Selected(Fixture fixture, int slot)
        {
            int status = fixture.Configuration.Stations.Aggregate(0, (mask, s) => mask | 1 << s.StationId);
            Compare(Ask("cap-choice", (int)fixture.Offense, (int)fixture.Game.Model.SourceCaptain,
                status, fixture.Captain, slot), fixture);
        }

        internal void Audible(Fixture fixture, int choice)
        {
            Compare(Ask("cap-audible", fixture.Captain, choice, -38, 37, 38), fixture);
        }

        private void Compare(string[] row, Fixture fixture)
        {
            SimulationState m = fixture.Game.Model;
            SourcePlayDefinition play = m.SourceOffensePlay;
            Check(row[0] == "CAP" && row[1] == play.Identity, fixture.Name + ": original choice/audible identity");
            Check(Int(row[2]) == (int)play.Flags && (Int(row[3]) != 0) == m.SourceOffenseFlipped,
                fixture.Name + ": original custom flags and signed audible flip");
            int at = 4;
            for (int i = 0; i < 7; i++)
            {
                SourceFormationSlot slot = play.Formation.Slots[i];
                FloatEqual(slot.Offset.X, row[at++], fixture.Name + ": native CAP local x" + i);
                FloatEqual(slot.Offset.Z, row[at++], fixture.Name + ": native CAP local z" + i);
                Check((int)slot.Mode == Int(row[at++]), fixture.Name + ": native CAP mode" + i);
                int control = Int(row[at++]);
                Check((int)slot.Control == control, fixture.Name + ": native CAP control" + i
                    + " actual=" + (int)slot.Control + " native=" + control);
                Check(slot.Script == row[at++], fixture.Name + ": native CAP script" + i);
                Check(m.SourceFormationSlots[(int)fixture.Offense * 7 + i].Control == slot.Control,
                    fixture.Name + ": actual public lineup consumes the compiled CAP controls");
            }
            for (int r = 0; r < 3; r++)
            {
                int count = Int(row[at++]);
                Check(play.Routes[r + 4].WordCount == count, fixture.Name + ": native CAP route word count" + r);
                for (int word = 0; word < count; word++)
                    Check(play.Routes[r + 4].WordAt(word) == Int(row[at++]),
                        fixture.Name + ": original CAP route instruction/operand" + r + "/" + word);
                Check(ReferenceEquals(m.SourceDrones[(int)fixture.Offense * 7 + r + 4].Program, play.Routes[r + 4]),
                    fixture.Name + ": actual scheduled actor admits this source-created custom route");
            }
            for (int station = 0; station < 4; station++)
                Check(m.SourceStationPlayers[station] == Int(row[at++]),
                    fixture.Name + ": original active AUDIBLE_RESTART controller assignment station" + station);
            Check(at == row.Length, fixture.Name + ": consume every native CAP/caller observation");
        }

        private void Pose(Observation o, int id)
        {
            SourceAiActor a = o.Ai.Actors[id];
            Send("pose", id, a.Position.X, a.Position.Z, a.Velocity.X, a.Velocity.Z, a.Height,
                (int)a.Mode, (int)a.Job, a.Target, (int)a.Flags, a.Station, a.RawFacing,
                o.TargetFacing[id], a.Stick, a.AttackTarget);
        }

        private void Load(int id, SourceRouteState s)
        {
            var args = new List<object> { "state", id, s.Program.Identity };
            args.AddRange(StateInts(s).Select(v => (object)v));
            args.AddRange(StateFloats(s).Select(v => (object)v));
            args.Add(s.PendingCatch ?? "-");
            Send(args.ToArray());
        }

        internal void Receiver(Observation before, Observation after, Fixture fixture)
        {
            int id = (int)fixture.Offense * 7 + 6;
            SourceRouteState actual = after.Routes[id], initial = before.Routes[id];
            Check(actual.LastSourceTick == after.Tick, fixture.Name + ": captured caller produces actual same-wake state");
            Check(before.Ai.Actors[id].Station == -1, fixture.Name + ": focused actual caller is CPU, not an overridden human");
            SourceAiFrame f = before.Ai;
            if (f.Ball.Carrier < 0)
            {
                nativeTrace.Add(new { stage = fixture.Name + "/original-negative-index-unqualified",
                    before.Tick, f.Ball, initial.WordIndex,
                    original = "drone.c:_drone_wpn player_blocks[game_info.ball_carrier] at1678",
                    nativeExecuted = false, actualState = StateInts(actual) });
                return;
            }
            Send("reset", f.Rules.OffenseSide, f.Rules.LineOfScrimmage, f.Rules.FirstDown, f.Rules.Down,
                before.Quarter, (int)f.Rules.PlayType, (int)f.GameFlags, 3, f.HumanTeamMask,
                before.Handicap0, before.Handicap1, f.OffenseFire ? 1 : 0, f.FireReceiver, 0,
                before.Offense, before.Defense, before.Plan.DropbackUnits, before.Plan.GainSought,
                before.Plan.RollDirection, before.Plan.MotionDirection, before.Plan.HurdlePass ? 1 : 0);
            for (int actor = 0; actor < 14; actor++)
            {
                Pose(before, actor); Load(actor, before.Routes[actor]);
            }
            SourceAiBall ball = f.Ball;
            nativeTrace.Add(new { stage = fixture.Name + "/captured-focused-entry", after.Tick,
                incomingBall = ball, initial.WordIndex, initial.PassReactTicks, before.RandomAt,
                actualWord = actual.WordIndex, actualReact = actual.PassReactTicks });
            Send("ball", ball.Position.X, ball.Position.Z, ball.Velocity.X, ball.Velocity.Z,
                ball.Target.X, ball.Target.Z, ball.Height, ball.VerticalVelocity, ball.Carrier,
                ball.IntendedReceiver, fixture.Game.Model.ThrowerId, (int)ball.Type,
                ball.FlightTicks, ball.TotalTicks, ball.Bounced ? 1 : 0);
            Send("clock", after.Tick, before.Snap, after.Ai.CameraHeading, 3, (int)f.GameFlags);
            var cache = new List<object> { "cache" };
            for (int a = 0; a < 14; a++)
                for (int b = 0; b < 14; b++) { cache.Add(f.Distance(a, b)); cache.Add(f.Angle(a, b)); }
            Send(cache.ToArray());
            Ask("update-receivers");
            Send("buttons", id, before.Buttons[id], before.NewButtons[id]);
            Send("clear-tape");
            SourceRandomDraw[] draws = fixture.Random.Trace.Skip(before.RandomAt).Where(d => d.Site.SourceTick == after.Tick
                && d.Site.ActorId == id && d.Site.Process == "player_proc").ToArray();
            var tape = new List<object> { "tape", draws.Count(d => d.Raw.HasValue) };
            tape.AddRange(draws.Where(d => d.Raw.HasValue).Select(d => (object)d.Raw.Value));
            Send(tape.ToArray());
            string[] row = Ask("step", id, 0, 0);
            Check(row[1] == actual.Program.Identity, fixture.Name + ": native focused route identity");
            int at = 2;
            foreach (int value in StateInts(actual))
                Check(Int(row[at++]) == value, fixture.Name + ": native focused state-int" + (at - 3)
                    + " actual=" + value + " native=" + row[at - 1]);
            foreach (float value in StateFloats(actual))
                FloatEqual(value, row[at++], fixture.Name + ": native focused state-float" + (at - 25));
            Check(row[39] == (actual.PendingCatch ?? "-"), fixture.Name + ": native focused pending script");
            at = 45;
            int count = Int(row[at++]);
            Check(count <= draws.Length, fixture.Name + ": authentic route RNG precedes actual animation suffix");
            for (int i = 0; i < count; i++)
            {
                int bound = Int(row[at++]), value = Int(row[at++]), raw = Int(row[at++]);
                Check((int)draws[i].Site.Kind == (bound == 0 ? 0 : 1)
                    && draws[i].Site.BoundOrModulus == bound && draws[i].Value == value
                    && (draws[i].Raw ?? -1) == raw, fixture.Name + ": actual ordered native route RNG");
            }
            nativeRouteDraws += count;
            nativeRouteWakes++;
            if (initial.Program == actual.Program && actual.WordIndex >= initial.WordIndex)
                for (int word = initial.WordIndex; word <= actual.WordIndex;
                    word += SourceRouteProgram.InstructionWidth(initial.Program.WordAt(word)))
                    executed.Add(initial.Program.WordAt(word));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Send("quit"); process.StandardInput.Close();
            if (!process.WaitForExit(10000)) { process.Kill(); throw new InvalidOperationException("Original CAP C did not exit."); }
            string error = process.StandardError.ReadToEnd();
            File.WriteAllLines(output, transcript);
            Check(process.ExitCode == 0 && error.Length == 0, "Original CAP C terminates without a masked error");
            process.Dispose();
            executingNative = null;
        }
    }

    private static void Probe(TeamId offense, bool cooperative)
    {
        var fixture = new Fixture(offense, cooperative);
        fixture.SelectFromRealKickoff();
        if (fixture.Native != null) fixture.Native.Selected(fixture, 0);
        fixture.Wakes(12);
        fixture.Press(SourceButtons.C); fixture.Wake(); fixture.Press(SourceButtons.C);
        Check(fixture.Match.Audibles.Offense.CallerStationId == fixture.Captain &&
            (offense == TeamId.Home ? fixture.Match.Audibles.HomeTeam : fixture.Match.Audibles.AwayTeam).Action == 1,
            fixture.Name + ": real captain double-C opens the actual audible");
        for (int choice = 1; choice <= 4; choice++)
        {
            fixture.Z[fixture.Captain] = 3; fixture.Wake();
            fixture.Z[fixture.Captain] = 0; fixture.Wake(); fixture.Wakes(6);
            fixture.Note("audible-" + choice);
            Check(fixture.Match.Audibles.Offense.Choice == choice % 4,
                fixture.Name + ": ordinary digital Down chooses actual audible " + choice);
            if (fixture.Native != null) fixture.Native.Audible(fixture, choice % 4);
        }
        if (cooperative)
        {
            fixture.Z[fixture.Captain] = 3; fixture.Wake();
            fixture.Z[fixture.Captain] = 0; fixture.Wake(); fixture.Wakes(6);
            Check(fixture.Match.Audibles.Offense.Choice == 1 && fixture.Game.Model.SourceOffenseFlipped,
                fixture.Name + ": actual signed nonempty alternate, not a flip setter, is retained into snap");
            if (fixture.Native != null) fixture.Native.Audible(fixture, 1);
        }
        fixture.Press(SourceButtons.C); fixture.Wakes(32);
        fixture.Press(SourceButtons.A);
        for (int wake = 0; wake < 100 && fixture.Game.State.Phase != GamePhase.LiveBall; wake++) fixture.Wake();
        fixture.Note("live");
        Check(fixture.Game.State.Phase == GamePhase.LiveBall, fixture.Name + ": real A/center VM reaches live saved route");
        SourceVector3 before = SourcePlayerDriver.WorldPosition(fixture.Game.Model.Players[(int)offense * 7 + 6]);
        fixture.Wakes(192); fixture.Note("live-routes");
        ActorState focus = fixture.Game.Model.Players[(int)offense * 7 + 6];
        Check(focus.SourceControllerStation == -1 && SourcePlayerDriver.WorldPosition(focus).X != before.X,
            fixture.Name + ": actual CPU receiver consumes source-created route and moves after real snap");
        if (fixture.Native != null) fixture.Native.Dispose();
        fixture.Close();
        cases.Add(new { fixture.Name, cooperative, offense = offense.ToString(), fixture.WakeCount,
            fixture.Game.SourceTick, captain = fixture.Captain, flip = fixture.Game.Model.SourceOffenseFlipped,
            route = fixture.Game.Model.SourceDrones[focus.Id].Program.Identity,
            word = fixture.Game.Model.SourceDrones[focus.Id].WordIndex });
    }

    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[4]);
        outputPath = output;
        nativePath = args.Length > 5 ? args[5] : null;
        try
        {
            inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2PreSnapSwitchInputsPath: args[2]);
            ReadHud(args[3]);
            foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
                foreach (bool cooperative in new[] { false, true }) Probe(side, cooperative);
            if (args.Length > 6 && args[6] == "captured")
                Check(capturedEntries > 0 && nativeRouteWakes > 0, "capture variant must execute actual native receiver callers");
            File.WriteAllText(output, JsonSerializer.Serialize(new { status = "passed", assertions, cases, trace,
                capturedEntries, nativeRouteWakes, nativeRouteDraws, nativeTrace,
                executedReceiverOpcodes = executed.ToArray(),
                original_C_compared = nativePath != null,
                boundary = "actual source opening kickoff, raw normal menu, saved audible restart and live snap; original CAP/choice/controller bodies and focused receiver entry-state comparison; no private state setters or target ABI/PRNG claim" }));
            return 0;
        }
        catch (Exception error)
        {
            if (executingNative != null)
                try { executingNative.Dispose(); }
                catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            if (activeFixture != null) activeFixture.Close();
            File.WriteAllText(output, JsonSerializer.Serialize(new { status = "failed", assertions, error = error.ToString(), cases, trace }));
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
