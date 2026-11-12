using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static class LiveCallerChecks
{
    private static int assertions;
    private static readonly List<object> cases = new List<object>();
    private static readonly List<object> trace = new List<object>();
    private static readonly MethodInfo FrameMethod = typeof(GameSimulation).GetMethod(
        "BuildSourceAiFrame", BindingFlags.Instance | BindingFlags.NonPublic);
    private static FullKernelInputs inputs;
    private static NativeReference native;
    private static int nativeWakes;
    private static int assignmentFramesWithUnverifiedTies;
    private static readonly SortedSet<int> visited = new SortedSet<int>();
    private static readonly HashSet<string> edges = new HashSet<string>();

    private sealed class Observation
    {
        internal readonly SourceAiFrame Frame;
        internal readonly SourceRouteState[] Routes;
        internal readonly int[] TargetFacing;
        internal readonly int[] Buttons;
        internal readonly int[] NewButtons;
        internal readonly long Tick, Snap;
        internal readonly int Quarter, Handicap0, Handicap1;
        internal readonly string Offense, Defense;
        internal readonly SourceQuarterbackPlan Plan;
        internal Observation(GameSimulation game)
        {
            SimulationState m = game.Model;
            Frame = ObserveFrame(game); Routes = m.SourceDrones.ToArray();
            TargetFacing = m.Players.Select(p => p.TargetFacing).ToArray();
            Buttons = m.Players.Select(p => (int)p.SourceButtons).ToArray();
            NewButtons = m.Players.Select(p => (int)p.SourceNewButtons).ToArray();
            Tick = game.SourceTick; Snap = m.SourceSnapTick; Quarter = m.Quarter - 1;
            Handicap0 = m.SourceEffectiveTeamHandicap[0]; Handicap1 = m.SourceEffectiveTeamHandicap[1];
            Offense = m.SourceOffensePlay.Identity; Defense = m.SourceDefensePlay.Identity;
            Plan = m.SourceQuarterbackPlan;
        }
    }

    private static int[] StateInts(SourceRouteState s)
    {
        return new[] { s.WordIndex, (int)s.Flags, s.Stick, s.OldStick, s.StickTicks,
            (int)s.Buttons, (int)s.OldButtons, s.ButtonTicks, (int)s.Job, s.Target,
            s.PassReactTicks, s.WaitTicks, s.BlockTicks, s.FakeTicks, s.CatchTicks,
            s.EvadeTicks, s.ThinkTicks, s.BlitzTicks, s.MoveTicks, s.OrderTicks,
            s.OrderStick, s.FakeZoneTicks };
    }

    private static float[] StateFloats(SourceRouteState s)
    {
        return new[] { s.Mark.X, s.Mark.Z, s.Ghost.X, s.Ghost.Z, s.GhostVelocity.X,
            s.GhostVelocity.Z, s.ZoneLowX, s.ZoneHighX, s.ZoneLowZ, s.ZoneHighZ,
            s.ZonePoint.X, s.ZonePoint.Z, s.ZoneVelocity.X, s.ZoneVelocity.Z };
    }

    private sealed class NativeReference : IDisposable
    {
        private readonly Process process;
        private readonly List<string> transcript = new List<string>();
        private readonly string output;
        internal NativeReference(string executable, string output)
        {
            this.output = output;
            process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
        }
        internal void Send(params object[] values)
        {
            string line = string.Join(" ", values.Select(v => v is float
                ? ((float)v).ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToString(v, CultureInfo.InvariantCulture)));
            transcript.Add("> " + line);
            process.StandardInput.WriteLine(line); process.StandardInput.Flush();
        }
        internal string[] Ask(params object[] values)
        {
            Send(values);
            string line = process.StandardOutput.ReadLine();
            if (line == null)
                throw new InvalidOperationException("Original route C exited: " + process.StandardError.ReadToEnd());
            transcript.Add(line);
            return line.Split(' ');
        }
        private void Pose(Observation before, Observation after, int current, int id)
        {
            SourceAiActor a = id < current ? after.Frame.Actors[id] : before.Frame.Actors[id];
            Send("pose", id, a.Position.X, a.Position.Z, a.Velocity.X, a.Velocity.Z, a.Height,
                (int)a.Mode, id < current ? -999 : (int)a.Job, a.Target, (int)a.Flags,
                a.Station, a.RawFacing, id < current ? after.TargetFacing[id] : before.TargetFacing[id],
                a.Stick, a.AttackTarget);
        }
        private void LoadState(int id, SourceRouteState s)
        {
            var args = new List<object> { "state", id, s.Program.Identity };
            args.AddRange(StateInts(s).Select(v => (object)v));
            args.AddRange(StateFloats(s).Select(v => (object)v));
            args.Add(s.PendingCatch ?? "-");
            Send(args.ToArray());
        }
        internal void CheckFormation(Fixture fixture)
        {
            Observation observed = new Observation(fixture.Game);
            SourceAiFrame f = observed.Frame;
            Send("reset", f.Rules.OffenseSide, f.Rules.LineOfScrimmage, f.Rules.FirstDown, f.Rules.Down,
                observed.Quarter, (int)f.Rules.PlayType, (int)f.GameFlags, 1, f.HumanTeamMask,
                observed.Handicap0, observed.Handicap1, f.OffenseFire ? 1 : 0, f.FireReceiver, 0,
                observed.Offense, observed.Defense, observed.Plan.DropbackUnits, observed.Plan.GainSought,
                observed.Plan.RollDirection, observed.Plan.MotionDirection, observed.Plan.HurdlePass ? 1 : 0);
            for (int id = 0; id < 14; id++) Pose(observed, observed, 0, id);
            string[] row = Ask("lineup", 0, 0);
            for (int id = 0; id < 14; id++)
            {
                int at = 1 + id * 6;
                Check(int.Parse(row[at + 2], CultureInfo.InvariantCulture)
                    == (int)fixture.Game.Model.SourceFormationSlots[id].Mode,
                    fixture.Name + ": original selected formation mode actor" + id);
                Check(row[at + 5] == fixture.Game.Model.SourceFormationSlots[id].Script,
                    fixture.Name + ": original selected formation animation actor" + id);
                Check(int.Parse(row[at + 4], CultureInfo.InvariantCulture) == observed.Routes[id].Target,
                    fixture.Name + ": original lineup coverage/backlink target actor" + id);
            }
            for (int id = 7; id < 14; id++)
            {
                SourceRouteState state = observed.Routes[id];
                if (state.Program.WordAt(0) != (int)SourceRouteOpcode.Zone
                    || fixture.Game.Model.SourceFormationSlots[id].Mode != SourceActorMode.DefensiveBackPreSnap) continue;
                Send("clear-tape");
                SourceRandomDraw[] initial = fixture.Random.Trace.Where(d => d.Site.SourceTick == observed.Tick
                    && d.Site.ActorId == id && d.Site.Process == "init_drones_dbps").ToArray();
                var tape = new List<object> { "tape", initial.Count(d => d.Raw.HasValue) };
                tape.AddRange(initial.Where(d => d.Raw.HasValue).Select(d => (object)d.Raw.Value));
                Send(tape.ToArray());
                row = Ask("initial-dbps", id, state.Program.Identity);
                float[] actual = { state.ZoneLowX, state.ZoneHighX, state.ZoneLowZ, state.ZoneHighZ };
                for (int i = 0; i < actual.Length; i++)
                {
                    float expected = float.Parse(row[30 + i], CultureInfo.InvariantCulture);
                    Check(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual[i]),
                        fixture.Name + ": original scheduled DBPS zone bound actor" + id + "/" + i);
                }
                visited.Add((int)SourceRouteOpcode.Zone);
            }
        }
        internal void Replay(Observation before, Observation after, Fixture fixture)
        {
            SourceAiFrame f = before.Frame;
            Send("reset", f.Rules.OffenseSide, f.Rules.LineOfScrimmage, f.Rules.FirstDown, f.Rules.Down,
                before.Quarter, (int)f.Rules.PlayType, (int)f.GameFlags, 3, f.HumanTeamMask,
                before.Handicap0, before.Handicap1, f.OffenseFire ? 1 : 0, f.FireReceiver, 0,
                before.Offense, before.Defense, before.Plan.DropbackUnits, before.Plan.GainSought,
                before.Plan.RollDirection, before.Plan.MotionDirection, before.Plan.HurdlePass ? 1 : 0);
            for (int id = 0; id < 14; id++) { Pose(before, after, 0, id); LoadState(id, before.Routes[id]); }
            SourceAiBall ball = f.Ball;
            Send("ball", ball.Position.X, ball.Position.Z, ball.Velocity.X, ball.Velocity.Z,
                ball.Target.X, ball.Target.Z, ball.Height, ball.VerticalVelocity, ball.Carrier,
                ball.IntendedReceiver, fixture.Game.Model.ThrowerId, (int)ball.Type,
                ball.FlightTicks, ball.TotalTicks, ball.Bounced ? 1 : 0);
            Send("clock", after.Tick, before.Snap, after.Frame.CameraHeading, 3, (int)f.GameFlags);
            var cache = new List<object> { "cache" };
            for (int i = 0; i < 14; i++)
                for (int j = 0; j < 14; j++) { cache.Add(f.Distance(i, j)); cache.Add(f.Angle(i, j)); }
            Send(cache.ToArray());
            string[] assignment = Ask("assignments");
            SourceBlockAssignments actualBlocks = fixture.Game.Model.SourceBlocking;
            if (actualBlocks.HasUnverifiedEqualTimeOrder) assignmentFramesWithUnverifiedTies++;
            int at = 1, rushers = int.Parse(assignment[at++], CultureInfo.InvariantCulture);
            Check(rushers == actualBlocks.Rushers.Count, fixture.Name + ": actual scheduled blocker rusher count");
            for (int i = 0; i < rushers; i++)
            {
                Check(int.Parse(assignment[at++], CultureInfo.InvariantCulture) == actualBlocks.Rushers[i],
                    fixture.Name + ": original rusher actor");
                Check(int.Parse(assignment[at++], CultureInfo.InvariantCulture) == actualBlocks.RushTimes[i],
                    fixture.Name + ": original integer rush time");
                int urgency = int.Parse(assignment[at++], CultureInfo.InvariantCulture);
                if (!actualBlocks.HasUnverifiedEqualTimeOrder)
                    Check(urgency == actualBlocks.UrgencyIndices[i], fixture.Name + ": source-defined distinct urgency order");
            }
            int blockers = int.Parse(assignment[at++], CultureInfo.InvariantCulture);
            Check(blockers == actualBlocks.Blockers.Count, fixture.Name + ": actual scheduled blocker count");
            for (int i = 0; i < blockers; i++)
            {
                int id = int.Parse(assignment[at++], CultureInfo.InvariantCulture);
                int target = int.Parse(assignment[at++], CultureInfo.InvariantCulture);
                Check(id == actualBlocks.Blockers[i], fixture.Name + ": original blocker actor");
                if (!actualBlocks.HasUnverifiedEqualTimeOrder)
                    Check(target == actualBlocks.TargetsByActor[id], fixture.Name + ": actual native blocker assignment consumer");
            }
            Ask("update-receivers");
            for (int id = 0; id < 14; id++)
            {
                for (int actor = 0; actor < 14; actor++) Pose(before, after, id, actor);
                if (after.Routes[id].LastSourceTick != after.Tick) continue;
                Send("buttons", id, before.Buttons[id], before.NewButtons[id]);
                Send("clear-tape");
                SourceRandomDraw[] draws = fixture.Random.Trace.Where(d => d.Site.SourceTick == after.Tick
                    && d.Site.ActorId == id && d.Site.Process == "player_proc").ToArray();
                var tape = new List<object> { "tape", draws.Count(d => d.Raw.HasValue) };
                tape.AddRange(draws.Where(d => d.Raw.HasValue).Select(d => (object)d.Raw.Value));
                Send(tape.ToArray());
                string[] row = Ask("step", id, 0, 0);
                int drawAt = 2 + 22 + 14 + 2 + 5;
                int count = int.Parse(row[drawAt++], CultureInfo.InvariantCulture);
                Check(count <= draws.Length, fixture.Name + ": original route draws precede animation suffix");
                for (int i = 0; i < count; i++)
                {
                    int bound = int.Parse(row[drawAt++], CultureInfo.InvariantCulture);
                    int value = int.Parse(row[drawAt++], CultureInfo.InvariantCulture);
                    int raw = int.Parse(row[drawAt++], CultureInfo.InvariantCulture);
                    Check((int)draws[i].Site.Kind == (bound == 0 ? 0 : 1)
                        && draws[i].Site.BoundOrModulus == bound && draws[i].Value == value
                        && (draws[i].Raw ?? -1) == raw,
                        fixture.Name + ": original route RNG kind/bound/raw/value in actual process order");
                }
                SourceAiActor entry = before.Frame.Actors[id], finish = after.Frame.Actors[id];
                bool observedBoundaryCorrection = finish.Mode == SourceActorMode.Receiver
                    && (finish.Flags & SourceAiActorFlags.LowerUninterruptible) == 0
                    && (Math.Abs(finish.Position.X) > 463f || Math.Abs(finish.Position.Z) > 178.85f
                        || Math.Abs(entry.Position.X) > 463f && Math.Abs(finish.Position.X) == 463f
                        || Math.Abs(entry.Position.Z) > 178.85f && Math.Abs(finish.Position.Z) == 178.85f);
                if (observedBoundaryCorrection)
                {
                    Send("abort-route", id);
                    edges.Add("native-player-boundary/abort-route");
                    trace.Add(new { stage = fixture.Name + "/observed-player-physics-native-abort-boundary",
                        actor = id, after.Tick, before = entry.Position, after = finish.Position });
                }
            }
            for (int id = 0; id < 14; id++)
            {
                string[] row = Ask("dump", id);
                SourceRouteState actual = after.Routes[id];
                Check(row[1] == actual.Program.Identity, fixture.Name + ": native route identity actor" + id);
                int index = 2;
                int[] ints = StateInts(actual);
                // Native JOB lives on the player; a human has no new drone-output shadow this wake.
                if (actual.LastSourceTick != after.Tick) ints[8] = (int)before.Frame.Actors[id].Job;
                for (int i = 0; i < ints.Length; i++)
                    Check(int.Parse(row[index++], CultureInfo.InvariantCulture) == ints[i],
                        fixture.Name + ": native actor" + id + " state-int" + i + " actual=" + ints[i]
                            + " native=" + row[index - 1]);
                foreach (float value in StateFloats(actual))
                {
                    float expected = float.Parse(row[index++], CultureInfo.InvariantCulture);
                    Check(BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(expected),
                        fixture.Name + ": native actor" + id + " state-float" + (index - 25));
                }
            }
            nativeWakes++;
        }
        public void Dispose()
        {
            Send("quit"); process.StandardInput.Close();
            if (!process.WaitForExit(10000)) { process.Kill(); throw new InvalidOperationException("Original route oracle did not exit."); }
            string errors = process.StandardError.ReadToEnd();
            File.WriteAllLines(output, transcript);
            Check(process.ExitCode == 0 && errors.Length == 0, "original C exits cleanly without a masked native failure");
            process.Dispose();
        }
    }

    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("P1_LIVE_ROUTE: " + name);
    }

    private static SourceAiFrame ObserveFrame(GameSimulation game)
    {
        return (SourceAiFrame)FrameMethod.Invoke(game, new object[] { true });
    }

    private static object Route(SourceRouteState s)
    {
        return new {
            program = s.Program.Identity, s.WordIndex, opcode = s.Program.WordAt(s.WordIndex),
            flags = (int)s.Flags, s.Stick, s.OldStick, buttons = (int)s.Buttons,
            oldButtons = (int)s.OldButtons, s.StickTicks, s.ButtonTicks, s.Target,
            job = (int)s.Job, s.Mark, s.Ghost, s.GhostVelocity, s.FakeTicks,
            s.WaitTicks, s.BlockTicks, s.PassReactTicks, s.CatchTicks, s.PendingCatch,
            s.EvadeTicks, s.ThinkTicks, s.BlitzTicks, s.OrderTicks, s.OrderStick,
            s.MoveTicks, s.ZoneLowX, s.ZoneHighX, s.ZoneLowZ, s.ZoneHighZ,
            s.ZonePoint, s.ZoneVelocity, s.FakeZoneTicks, s.LastSourceTick
        };
    }

    private sealed class Fixture
    {
        internal readonly GameSimulation Game;
        internal readonly SourceMatchConfiguration Configuration;
        internal readonly SourceInjectedRandom Random;
        internal readonly int Focus;
        internal readonly string Name;

        internal Fixture(string name, string offense, string defense, int focus)
        {
            Name = name; Focus = focus;
            Configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation,
                new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new[] { new SourceStationConfiguration(0, TeamId.Home),
                    new SourceStationConfiguration(2, TeamId.Away) },
                TeamId.Home, SourceCaptainSlot.First);
            SourceInjectedRandom random = null;
            Func<int, ISourceRandom> randomFactory = seed => random = (SourceInjectedRandom)SourceRandom.CreatePortableLibrary(seed,
                "Nov11 finite public scheduled-route callers; unchanged seed1; target libc unverified");
            Game = SourceC2Composition.CreateM1Simulation(inputs.Animations, inputs.ReferencePivots,
                inputs.Playbook, Configuration, inputs.Rosters,
                new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                inputs.MovementRuntime, new SourceMovementSettings(0, 0, false, 0, 0, false),
                inputs.SoundFunctions, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(inputs.C2Data),
                randomFactory, compatibility: SourceGameplayCompatibility.StrictSource);
            Random = random;
            Game.SourceCameraHeadingProvider = (snapshot, tick) =>
                FullKernelInputs.SetControlledMovementCamera(Game, snapshot, tick);
            SimulationState m = Game.Model;
            m.Phase = GamePhase.PlaySelect; m.SourceMatchPhase = SourceMatchPhase.PlaySelection;
            m.Possession = TeamId.Home; m.SourceLastPossession = 0;
            m.SourceRules = new SourceRulesState(0, 0, 1, 35, 65);
            m.SourceBeginPlayInitialized = false; m.SourceFlags = SourceGameFlags.None;
            for (int i = 0; i < 30; i++) Wake("menu-introduction");
            Wake("public-offense-choice", new SourceCommand(SourceAction.SelectPlay,
                new SourcePlaySelection(offense, SourcePlaybookKind.Offense)));
            Wake("public-defense-choice", new SourceCommand(SourceAction.SelectPlay,
                new SourcePlaySelection(defense, SourcePlaybookKind.Defense)));
            for (int i = 0; i < 12 && m.Phase == GamePhase.PlaySelect; i++) Wake("selection-continuation");
            Check(m.Phase == GamePhase.PreSnap && m.SourceOffensePlay.Identity == offense
                && m.SourceDefensePlay.Identity == defense, name + ": real public choices produce the actual selected formation");
            for (int i = 0; i < 14; i++)
                Check(m.SourceDrones[i].Program.Identity == (i < 7 ? m.SourceOffensePlay : m.SourceDefensePlay).Routes[i % 7].Identity,
                    name + ": real lineup admits original route pointer for actor" + i);
            Check(m.Players[focus].SourceControllerStation == -1, name + ": focus is an actual scheduled CPU route actor");
            trace.Add(new { stage = name + "/actual-formation-produced", Game.SourceTick,
                offenseFormation = m.SourceOffensePlay.Formation.Identity,
                defenseFormation = m.SourceDefensePlay.Formation.Identity,
                routes = m.SourceDrones.Select(Route).ToArray(),
                controllers = m.SourceStationPlayers.ToArray() });
            if (native != null) native.CheckFormation(this);
        }

        internal void Wake(string stage, SourceCommand command = default(SourceCommand))
        {
            Observation previous = Game.Model.Phase == GamePhase.LiveBall
                ? new Observation(Game) : null;
            long before = Game.SourceTick;
            Game.Step(new PlayerCommand(source: command));
            if (Game.SourceTick == before) Game.Step(new PlayerCommand(source: command));
            Check(Game.SourceTick == before + 1, Name + ": one actual executing source wake");
            if (previous != null)
            {
                Observation after = new Observation(Game);
                if (native != null) native.Replay(previous, after, this);
                SourceRouteState a = previous.Routes[Focus], b = after.Routes[Focus];
                SourceAiActor actor = previous.Frame.Actors[Focus];
                if (b.WordIndex > a.WordIndex && a.Program == b.Program
                    && a.Program.WordAt(a.WordIndex) == 10)
                    edges.Add((b.Flags & SourceDroneFlags.GhostActive) != 0 ? "fake/ghost-produced" : "fake/not-sold");
                if (a.Target >= 0 && after.Routes[a.Target].FakeTicks > previous.Routes[a.Target].FakeTicks)
                    edges.Add("fake/defender-timer-produced");
                if (actor.Mode == SourceActorMode.Receiver && previous.Frame.Ball.Carrier != Focus
                    && previous.Frame.Ball.Carrier >= 0
                    && previous.Frame.Actors[previous.Frame.Ball.Carrier].Mode != SourceActorMode.Receiver
                    && a.ThinkTicks <= 0 && a.Program == b.Program && b.WordIndex >= a.WordIndex)
                {
                    for (int at = a.WordIndex; at <= b.WordIndex; at += SourceRouteProgram.InstructionWidth(a.Program.WordAt(at)))
                    {
                        int current = a.Program.WordAt(at);
                        visited.Add(current);
                        if (at != a.WordIndex && (current == 9 || current == 14 || current == 13))
                            edges.Add(current + "/" + (at == b.WordIndex
                                && (b.Flags & SourceDroneFlags.WaitingAnimation) != 0 ? "animation-start" : "skip"));
                        if (at != a.WordIndex && current == 4 && at == b.WordIndex
                            && (b.Flags & SourceDroneFlags.RunningYards) != 0) edges.Add("run/start");
                        if (at != a.WordIndex && current == 5
                            && (b.Flags & SourceDroneFlags.Improvising) != 0) edges.Add("improvise/initialize");
                    }
                    int op = a.Program.WordAt(a.WordIndex);
                    if (op == 3) edges.Add(b.WordIndex == a.WordIndex ? "goto/before" : "goto/reached");
                    if (op == 4)
                        edges.Add((a.Flags & SourceDroneFlags.RunningYards) == 0 ? "run/start"
                            : b.WordIndex == a.WordIndex ? "run/below" : "run/reached");
                    if (op == 9 || op == 14 || op == 13)
                        edges.Add(op + "/" + ((a.Flags & SourceDroneFlags.WaitingAnimation) != 0
                            ? b.WordIndex == a.WordIndex ? "animation-wait" : "animation-complete"
                            : (b.Flags & SourceDroneFlags.WaitingAnimation) != 0 ? "animation-start" : "skip"));
                    if (op == 5) edges.Add((a.Flags & SourceDroneFlags.Improvising) == 0
                        ? "improvise/initialize" : "improvise/continue");
                }
            }
            if (Game.Model.SourceFormationSlots == null) return;
            trace.Add(new { stage = Name + "/" + stage, Game.SourceTick,
                phase = Game.Model.Phase.ToString(), route = Route(Game.Model.SourceDrones[Focus]),
                position = SourcePlayerDriver.WorldPosition(Game.Model.Players[Focus]),
                script = Game.Model.Players[Focus].AnimationState.Primary.ScriptSymbol,
                upperScript = Game.Model.Players[Focus].AnimationState.Secondary.ScriptSymbol,
                modes = Game.Model.Players[Focus].AnimationState.Primary.Mode,
                playerWake = Game.Model.SourceProcesses.Require(2 + Focus).LastWakeTick,
                events = Game.SourceEvents.Select(e => new { e.Type, e.SourceTick, e.ActorPlayerId, e.TargetPlayerId }).ToArray(),
                targetRoute = Game.Model.SourceDrones[Focus].Target >= 0
                    ? Route(Game.Model.SourceDrones[Game.Model.SourceDrones[Focus].Target]) : null,
                rng = Random.Trace.ToArray() });
        }

        internal void DeclareLiveBoundary()
        {
            SimulationState m = Game.Model;
            m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
            m.CarrierId = m.SourceLastCarrierId = 3; m.ThrowerId = 3; m.TargetPlayerId = -1;
            m.BallMode = BallMode.Held; m.BallKind = SourceBallKind.Pass;
            m.SourceFlags = SourceGameFlags.OverLineSpeedsSet;
            m.SourceSnapTick = Game.SourceTick; m.ClockActive = true;
            foreach (ActorState actor in m.Players)
            {
                if (actor.SourceMode == SourceActorMode.LinePreSnap) actor.SourceMode = SourceActorMode.Line;
                if (actor.SourceMode == SourceActorMode.QuarterbackPreSnap) actor.SourceMode = SourceActorMode.Quarterback;
                if (actor.SourceMode == SourceActorMode.ReceiverPreSnap) actor.SourceMode = SourceActorMode.Receiver;
                if (actor.SourceMode == SourceActorMode.DefensiveBackPreSnap) actor.SourceMode = SourceActorMode.DefensiveBack;
                actor.SourceRawFlags |= (int)SourceMovementFlags.NoCollision;
            }
            SourcePlayerDriver.SetSourcePosition(m.Players[3], new SourceVector3(-240f, 0f, -20f));
            for (int i = 7; i < 14; i++)
                SourcePlayerDriver.SetSourcePosition(m.Players[i], new SourceVector3(
                    -105f + (i - 7) * 29f, 0f, -115f + (i - 7) * 31f));
            SourceBallDriver.SetPosition(m, new SourceVector3(-240f, 10f, -20f));
        }
    }

    private static void Probe()
    {
        var fixture = new Fixture("P1/public-formation/receiver-first-wakes",
            "play_upper_cut", "play_safe_cover", 4);
        fixture.DeclareLiveBoundary();
        for (int i = 0; i < 4; i++)
        {
            fixture.Wake("ordinary-scheduled-receiver-" + i);
            Check(fixture.Game.Model.SourceDrones[4].LastSourceTick == fixture.Game.SourceTick,
                "actual player_proc invokes source route on this ordinary public wake");
        }
        SourceAiFrame observed = ObserveFrame(fixture.Game);
        cases.Add(new { name = fixture.Name, declaredLiveBoundary = true,
            fixture.Game.SourceTick, finalRoute = Route(fixture.Game.Model.SourceDrones[4]),
            control = observed.Actors[4].Station, trace = trace.ToArray() });
        trace.Clear();
    }

    private static void ReceiverEdges(string name, string play, int focus, bool near, bool stopAtBlock = false,
        bool liveCover = false)
    {
        visited.Clear(); edges.Clear();
        var fixture = new Fixture(name, play, "play_safe_cover", focus);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        ActorState actor = m.Players[focus];
        int defender = m.SourceDrones[focus].Target;
        foreach (ActorState other in m.Players)
            if (other.Id != focus && other.Id != 3 && (!liveCover || other.Id != defender))
                other.SourceMode = SourceActorMode.Dead;
        SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(-150f, 0f, 0f));
        Check(defender >= 7, name + ": actual formation produced a real opposite-side target");
        SourcePlayerDriver.SetSourcePosition(m.Players[defender], new SourceVector3(near ? -130f : 20f, 0f, 0f));
        actor.SourceMode = SourceActorMode.ReceiverPreSnap;
        fixture.Wake("declared-layout-real-cache-primer");
        actor.SourceMode = SourceActorMode.Receiver;
        bool done = false;
        bool arrived = false;
        bool ghostProduced = false, fakeConsumed = false, actualWaveAnimation = false;
        var geometryPlaced = new HashSet<string>();
        int sameWord = 0, lastWord = -1;
        for (int tick = 0; tick < 160 && !done; tick++)
        {
            SourceRouteState state = m.SourceDrones[focus];
            int op = state.Program.WordAt(state.WordIndex);
            sameWord = lastWord == state.WordIndex ? sameWord + 1 : 0;
            lastWord = state.WordIndex;
            if (op == 3 && sameWord == 1)
            {
                int yard = m.SourceRules.LineOfScrimmage + state.Program.WordAt(state.WordIndex + 1) + 1;
                SourceVector3 position = SourcePlayerDriver.WorldPosition(actor);
                SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(SourceMatchRules.WorldPosition(yard, 0), 0f, position.Z));
                trace.Add(new { stage = name + "/declared-LOS-threshold-layout", fixture.Game.SourceTick, yard });
            }
            if (op == 4 && (state.Flags & SourceDroneFlags.RunningYards) != 0 && sameWord >= 2
                && geometryPlaced.Add(state.Program.Identity + ":" + state.WordIndex))
            {
                int yards = state.Program.WordAt(state.WordIndex + 1);
                SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(state.Mark.X + 7.8f * yards, 0f, state.Mark.Z));
                trace.Add(new { stage = name + "/declared-run-distance-layout", fixture.Game.SourceTick, yards });
            }
            if (op == 5 && (state.Flags & SourceDroneFlags.Improvising) != 0 && !arrived)
            {
                SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(state.Mark.X, 0f, state.Mark.Z));
                arrived = true;
                trace.Add(new { stage = name + "/declared-improvisation-arrival-layout", fixture.Game.SourceTick });
            }
            if (near)
            {
                SourceVector3 position = SourcePlayerDriver.WorldPosition(actor);
                SourcePlayerDriver.SetSourcePosition(m.Players[defender], new SourceVector3(position.X + 20f, 0f, position.Z));
            }
            fixture.Wake("actual-route-wake-" + tick);
            ghostProduced |= (m.SourceDrones[focus].Flags & SourceDroneFlags.GhostActive) != 0;
            fakeConsumed |= m.SourceDrones[defender].FakeTicks > 0;
            actualWaveAnimation |= actor.AnimationState.Secondary.ScriptSymbol == "m_wavqb_anim"
                || actor.AnimationState.Secondary.ScriptSymbol == "m_wavqbf_anim";
            Check(m.SourceDrones[focus].LastSourceTick == fixture.Game.SourceTick,
                name + ": ordinary player process really calls this route");
            done = arrived && (m.SourceDrones[focus].Flags & SourceDroneFlags.Improvising) != 0
                || stopAtBlock && m.SourceDrones[focus].Program.WordAt(m.SourceDrones[focus].WordIndex) == 8
                    && m.SourceDrones[focus].Job == SourceActorJob.Block;
        }
        Check(done, name + ": finite original route reaches its genuine terminal continuation");
        if (liveCover)
        {
            Check(fixture.Random.Trace.Any(d => d.Site.SourceIdentity == "sell_fake:bite"),
                name + ": actual covering defender reaches original probabilistic fake decision");
            if (play == "play_middle_pick")
                Check(ghostProduced && fakeConsumed,
                    name + ": actual source-authored probability256 produces a ghost and a consumed defender timer");
            else
                Check(!ghostProduced && !fakeConsumed,
                    name + ": the unchanged actual66/77 draws exceed original50 probability; do not retune seed to force a fake");
        }
        if (play == "play_upper_cut" && !near)
            Check(actualWaveAnimation, name + ": route wave flag reaches the real upper-body animation consumer");
        if (stopAtBlock)
        {
            int word = m.SourceDrones[focus].WordIndex;
            fixture.Wake("actual-terminal-block-retained");
            Check(m.SourceDrones[focus].WordIndex == word && actor.SourceBackpedal
                && m.SourceDrones[focus].Job == SourceActorJob.Block,
                name + ": actual terminal BLOCK retains its route pointer and reaches the backpedal consumer");
        }
        cases.Add(new { name, declaredLiveAndGeometryBoundaries = true, fixture.Game.SourceTick,
            actualRoute = m.SourceDrones[focus].Program.Identity, opcodes = visited.ToArray(),
            branchEdges = edges.OrderBy(s => s).ToArray(), ghostProduced, fakeConsumed, actualWaveAnimation,
            trace = trace.ToArray() });
        trace.Clear();
    }

    private static void OrderedDefender()
    {
        visited.Clear(); edges.Clear();
        const string name = "P1/receiver/ordered-defender-consumer";
        var fixture = new Fixture(name, "play_gen_1", "play_safe_cover", 4);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        int target = m.SourceDrones[4].Target;
        Check(target >= 7 && m.Players[target].SourceControllerStation == -1,
            name + ": actual formation assigned a real CPU defender");
        m.Players[4].SourceMode = SourceActorMode.ReceiverPreSnap;
        fixture.Wake("real-cache-and-presnap-route-primer");
        m.Players[4].SourceMode = SourceActorMode.Receiver;
        fixture.Wake("actual-MOVE-DEFENDER-and-later-defender-wake");
        int order = m.SourceDrones[target].OrderTicks;
        Check(visited.Contains(1) && visited.Contains(15), name + ": real selected route executes WAIT_SNAP and MOVE_DEFENDER");
        Check(order > 0 && order < 90 && (m.SourceDrones[target].Flags & SourceDroneFlags.Ordered) != 0
            && m.SourceDrones[target].LastSourceTick == fixture.Game.SourceTick,
            name + ": defender consumes the new order later in this actual source wake");
        fixture.Wake("actual-ordered-defender-next-wake");
        Check(m.SourceDrones[target].OrderTicks < order, name + ": actual ordered defender continuation decrements the source timer");
        cases.Add(new { name, declaredLiveBoundary = true, target, firstConsumedOrderTicks = order,
            nextOrderTicks = m.SourceDrones[target].OrderTicks, fixture.Game.SourceTick,
            opcodes = visited.ToArray(), trace = trace.ToArray() });
        trace.Clear();
    }

    private static void DelayBlock()
    {
        visited.Clear(); edges.Clear();
        const string name = "P1/receiver/delay-block-to-live-blocker";
        var fixture = new Fixture(name, "play_hail_mary", "play_safe_cover", 4);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        foreach (ActorState other in m.Players)
            if (other.Id != 3 && other.Id != 4) other.SourceMode = SourceActorMode.Dead;
        fixture.Wake("actual-delay-block-opcode");
        int initial = m.SourceDrones[4].BlockTicks;
        Check(initial == 60 && (m.SourceDrones[4].Flags & SourceDroneFlags.DelayBlock) != 0,
            name + ": selected genuine route produces original sixty-tick delayed blocking");
        // A declared live carrier-mode boundary; route pointers, flags and timers remain produced.
        m.Players[3].SourceMode = SourceActorMode.Receiver;
        m.Players[3].Role = ActorRole.Receiver;
        fixture.Wake("declared-carrier-mode-real-countdown");
        Check(m.SourceDrones[4].BlockTicks == initial - 1, name + ": actual receiver carrier starts the source countdown");
        for (int tick = 0; tick < initial && (m.SourceDrones[4].Flags & SourceDroneFlags.DelayBlock) != 0; tick++)
            fixture.Wake("actual-delay-countdown-" + tick);
        Check((m.SourceDrones[4].Flags & SourceDroneFlags.DelayBlock) == 0
            && m.SourceDrones[4].BlockTicks == 0 && m.SourceDrones[4].Job == SourceActorJob.Block,
            name + ": natural route timer expiry switches the actual actor to blocking");
        fixture.Wake("actual-next-preplayer-blocker-assignment");
        Check(m.SourceBlocking.Blockers.Contains(4)
            && m.Players[4].SourceTarget == m.SourceBlocking.TargetsByActor[4],
            name + ": next real pre-player assignment is consumed by the new blocker");
        cases.Add(new { name, declaredLiveAndCarrierModeBoundaries = true, initial,
            fixture.Game.SourceTick, route = Route(m.SourceDrones[4]), trace = trace.ToArray() });
        trace.Clear();
    }

    private static void DefenseConsumers(string name, string defense, int focus)
    {
        visited.Clear(); edges.Clear();
        var fixture = new Fixture(name, "play_gen_1", defense, focus);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        var observed = new HashSet<SourceActorJob>();
        bool blockConsumed = false, zoneContinuation = false;
        bool expirySeen = false;
        int watched = -1;
        if ((m.SourceDefensePlay.Flags & SourcePlayFlags.Blitz) != 0)
            watched = Enumerable.Range(7, 7).Where(id => m.Players[id].SourceControllerStation < 0
                && m.SourceDrones[id].BlitzTicks > 0).OrderBy(id => m.SourceDrones[id].BlitzTicks).First();
        int wakes = watched < 0 ? 8 : m.SourceDrones[watched].BlitzTicks + 1;
        for (int tick = 0; tick < wakes; tick++)
        {
            fixture.Wake("actual-man-zone-rush-blocker-wake-" + tick);
            Check(!m.SourceBlocking.HasUnverifiedEqualTimeOrder,
                name + ": declared asymmetric layout keeps this assignment independent of target-libc qsort ties");
            for (int id = 7; id < 14; id++)
            {
                if (m.Players[id].SourceControllerStation >= 0 || m.SourceDrones[id].LastSourceTick != fixture.Game.SourceTick) continue;
                observed.Add(m.SourceDrones[id].Job);
                int op = m.SourceDrones[id].Program.WordAt(0);
                if (op == 6 || op == 7 || op == 17) visited.Add(op);
                if (m.SourceDrones[id].Job == SourceActorJob.Zone && m.SourceDrones[id].MoveTicks > 0)
                    zoneContinuation = true;
            }
            blockConsumed |= m.SourceBlocking.Blockers.Any(id => m.Players[id].SourceTarget
                == m.SourceBlocking.TargetsByActor[id] && m.SourceDrones[id].LastSourceTick == fixture.Game.SourceTick);
            if (watched >= 0 && m.SourceDrones[watched].BlitzTicks == 0
                && (m.SourceDrones[watched].Flags & SourceDroneFlags.Blitz) != 0) expirySeen = true;
        }
        Check(blockConsumed && m.SourceBlocking.Rushers.Count > 0, name + ": actual line blockers consume scheduled original assignments");
        if (defense != "play_suicide_blitz")
            Check(observed.Contains(SourceActorJob.Zone) && zoneContinuation,
                name + ": actual zone process executes both target selection and continuing zone movement");
        if (defense == "play_mix_it_up")
            Check(observed.Contains(SourceActorJob.Cover), name + ": actual man target remains a scheduled coverage consumer");
        if ((m.SourceDefensePlay.Flags & SourcePlayFlags.Blitz) != 0)
            Check(expirySeen, name + ": actual prelineup delay expires through real live route wakes and sets BLITZ");
        cases.Add(new { name, declaredLiveLayout = true, fixture.Game.SourceTick,
            jobs = observed.Select(v => v.ToString()).OrderBy(v => v).ToArray(),
            opcodes = visited.ToArray(), zoneContinuation, blockConsumed, watched, expirySeen,
            trace = trace.ToArray() });
        trace.Clear();
    }

    private static void FakeZoneExpiry()
    {
        visited.Clear(); edges.Clear();
        const string name = "P1/defense/fake-zone-to-man-native-timer";
        var fixture = new Fixture(name, "play_gen_1", "play_fake_zone", 10);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        int defender = Enumerable.Range(7, 7).Where(id => m.Players[id].SourceControllerStation < 0
            && m.SourceDrones[id].Job == SourceActorJob.Zone && m.SourceDrones[id].FakeZoneTicks > 0)
            .OrderBy(id => m.SourceDrones[id].FakeZoneTicks).First();
        int initial = m.SourceDrones[defender].FakeZoneTicks;
        for (int tick = 0; tick <= initial && m.SourceDrones[defender].Job == SourceActorJob.Zone; tick++)
            fixture.Wake("actual-fake-zone-countdown-" + tick);
        Check(m.SourceDrones[defender].FakeZoneTicks == 0 && m.SourceDrones[defender].Job == SourceActorJob.Cover
            && m.SourceDrones[defender].LastSourceTick == fixture.Game.SourceTick,
            name + ": real fake-zone timer transitions the actual scheduled actor from zone to cover");
        cases.Add(new { name, declaredLiveLayout = true, defender, initial, fixture.Game.SourceTick,
            finalRoute = Route(m.SourceDrones[defender]), trace = trace.ToArray() });
        trace.Clear();
    }

    private static void TerminalAndInactiveWords()
    {
        const string name = "P1/catalog/active-word-reachability";
        var opcodes = new HashSet<int>();
        var waitRoutes = new List<string>();
        foreach (SourceRouteProgram route in inputs.Playbook.Routes.Values)
        {
            for (int at = 0; at < route.WordCount; at += SourceRouteProgram.InstructionWidth(route.WordAt(at)))
            {
                int op = route.WordAt(at);
                opcodes.Add(op);
                if (op == 16)
                {
                    Check(route.Identity == "pr_QB_sweep" && at == 1 && route.WordAt(0) == 8,
                        name + ": the only catalog WAIT lies after native terminal BLOCK");
                    waitRoutes.Add(route.Identity);
                }
            }
        }
        Check(opcodes.SetEquals(Enumerable.Range(1, 17).Concat(new[] { 20 })),
            name + ": exact active catalog opcode classes, without invented End/SetFlag/CoverWeak/custom controls");
        Check(waitRoutes.Count == 1, name + ": one original dormant WAIT payload, not a coverage claim");
        var fixture = new Fixture(name, "play_gen_1", "play_safe_cover", 4);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        m.Players[3].SourceMode = SourceActorMode.Receiver;
        m.CarrierId = m.SourceLastCarrierId = 5; m.Players[5].SourceMode = SourceActorMode.Quarterback;
        SourcePlayerDriver.SetSourcePosition(m.Players[5], new SourceVector3(-240f, 0f, -20f));
        m.SourceStationPlayers[0] = 5; m.Players[5].SourceControllerStation = 0;
        m.Players[3].SourceControllerStation = -1;
        for (int tick = 0; tick < 3; tick++)
        {
            fixture.Wake("declared-handoff-real-original-BLOCK-" + tick);
            Check(m.SourceDrones[3].Program.Identity == "pr_QB_sweep" && m.SourceDrones[3].WordIndex == 0
                && m.SourceDrones[3].Job == SourceActorJob.Block,
                name + ": ordinary source caller retains terminal BLOCK and does not invent an execution of WAIT16");
        }
        cases.Add(new { name, catalogOpcodes = opcodes.OrderBy(v => v).ToArray(),
            nonexecutingWaitBehindTerminalBlock = waitRoutes.ToArray(),
            declaredControllerHandoff = true, fixture.Game.SourceTick, trace = trace.ToArray() });
        trace.Clear();
    }

    private static void EarlyJukeSkip()
    {
        visited.Clear(); edges.Clear();
        const string name = "P1/receiver/first-juke-far-skip";
        var fixture = new Fixture(name, "play_hail_mary", "play_safe_cover", 6);
        fixture.DeclareLiveBoundary();
        SimulationState m = fixture.Game.Model;
        foreach (ActorState other in m.Players)
            if (other.Id != 3 && other.Id != 6) other.SourceMode = SourceActorMode.Dead;
        SourcePlayerDriver.SetSourcePosition(m.Players[6], new SourceVector3(-150f, 0f, 0f));
        for (int id = 7; id < 14; id++)
            SourcePlayerDriver.SetSourcePosition(m.Players[id], new SourceVector3(130f + (id - 7) * 13f, 0f, 60f));
        m.Players[6].SourceMode = SourceActorMode.ReceiverPreSnap;
        fixture.Wake("real-far-cache-primer");
        m.Players[6].SourceMode = SourceActorMode.Receiver;
        fixture.Wake("actual-waitsnap-juke-skip");
        Check(visited.Contains(1) && visited.Contains(9) && edges.Contains("9/skip")
            && (m.SourceDrones[6].Flags & SourceDroneFlags.WaitingAnimation) == 0,
            name + ": original nearest-defender distance skips Juke without a fake animation");
        cases.Add(new { name, declaredFarLayout = true, fixture.Game.SourceTick,
            opcodes = visited.ToArray(), branchEdges = edges.OrderBy(v => v).ToArray(), trace = trace.ToArray() });
        trace.Clear();
    }

    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[3]);
        try
        {
            inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
            if (args.Length > 5 && args[5] != "-") native = new NativeReference(args[5], output + ".original-C.txt");
            Check(inputs.Playbook.Plays.Count == 88
                && inputs.Playbook.Plays.Values.Select(p => p.Formation.Identity).Distinct().Count() == 29
                && inputs.Playbook.Routes.Count == 140, "retain exact original 88/29/140 catalog, no rewritten route");
            bool all = args.Length > 4 && args[4] == "finite";
            if (all || args.Length > 4 && args[4] == "receiver-edges")
            {
                ReceiverEdges("P1/receiver/hail-mary/far", "play_hail_mary", 4, false);
                ReceiverEdges("P1/receiver/hail-mary/near", "play_hail_mary", 4, true);
                ReceiverEdges("P1/receiver/upper-cut/far", "play_upper_cut", 4, false);
                ReceiverEdges("P1/receiver/upper-cut/near", "play_upper_cut", 4, true);
            }
            if (all || args.Length > 4 && args[4] == "assignment-edges")
            {
                OrderedDefender();
                DelayBlock();
                ReceiverEdges("P1/receiver/terminal-block", "play_one_man_back", 5, false, true);
                DefenseConsumers("P1/defense/man-zone-blitz", "play_mix_it_up", 10);
                DefenseConsumers("P1/defense/zone-widths", "play_near_zone", 9);
                DefenseConsumers("P1/defense/all-blitz-rushers", "play_suicide_blitz", 10);
                FakeZoneExpiry();
                TerminalAndInactiveWords();
                EarlyJukeSkip();
                ReceiverEdges("P1/receiver/real-cover-fake-not-sold", "play_upper_cut", 4,
                    false, liveCover: true);
                ReceiverEdges("P1/receiver/source256-fake-to-real-cover-ghost-consumer", "play_middle_pick", 5,
                    false, liveCover: true);
            }
            if (args.Length <= 4 || args[4] == "probe") Probe();
            if (all)
                Check(cases.Count == 15 && nativeWakes == 511 && assignmentFramesWithUnverifiedTies == 0,
                    "the fifteen finite named cases execute their exact511 real source wakes, without qsort-tie qualification");
            if (native != null) { native.Dispose(); native = null; }
            File.WriteAllText(output, JsonSerializer.Serialize(new { status = "passed", assertions, cases,
                qualified = false, nativeWakes, assignmentFramesWithUnverifiedTies,
                original_C_compared = nativeWakes > 0, scope = "finite public scheduled-route caller evidence" }));
            Console.WriteLine("P1_LIVE_CALLER_PROBE_PASS assertions=" + assertions);
            return 0;
        }
        catch (Exception error)
        {
            if (native != null) { native.Dispose(); native = null; }
            File.WriteAllText(output, JsonSerializer.Serialize(new { status = "failed", assertions,
                nativeWakes, error = error.ToString(), cases, trace }));
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
