using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.Gameplay;

internal static class Checks
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    private static string Here, OraclePath;
    private static SourcePlaybook Book;
    private static long Assertions, NativeSteps, NativeNumerics, NativeCatalog, ReceiverCases, BlockCases, LineupCases, InitCases;
    private static long RandomCalls, RetainedCommands, RetainedButtons, CrossActorEffects, SwitchTimingTicks;
    private static string Context;
    private static readonly HashSet<int> OpcodesVisited = new HashSet<int>();
    private static readonly HashSet<string> ScriptsVisited = new HashSet<string>();
    private static readonly HashSet<string> RouteSidesVisited = new HashSet<string>();

    private sealed class AssertionFailure : Exception
    {
        internal readonly string Artifact;
        internal AssertionFailure(string artifact) : base(Context + ": " + artifact) { Artifact = artifact; }
    }

    private sealed class Tape
    {
        private uint state;
        internal Tape(uint seed) { state = seed; }
        internal int Raw() { state = unchecked(state * 1664525u + 1013904223u); return (int)(state & 0x7fffffffu); }
        internal int Next(int bound) { return Raw() % bound; }
        internal float Quarter(int bound) { return (Next(bound * 8 + 1) - bound * 4) / 4f; }
    }

    private sealed class Oracle : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder errors = new StringBuilder();
        internal Oracle()
        {
            process = Process.Start(new ProcessStartInfo(OraclePath) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, WorkingDirectory = Here });
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
            process.BeginErrorReadLine();
            Equal("HOST 4 4 2 24", string.Join(" ", Ask("profile")), "native binary32/int32 host profile");
        }
        internal void Send(string value) { process.StandardInput.WriteLine(value); }
        internal string Read()
        {
            process.StandardInput.Flush();
            string line = process.StandardOutput.ReadLine();
            if (line == null) throw new IOException("Original C oracle exited: " + errors);
            return line;
        }
        internal string[] Ask(string value) { Send(value); return Read().Split(' '); }
        public void Dispose()
        {
            if (!process.HasExited)
            {
                Send("quit"); process.StandardInput.Flush();
                if (!process.WaitForExit(10000))
                {
                    process.Kill(); process.WaitForExit();
                    throw new IOException("Original C oracle did not terminate.");
                }
            }
            int exitCode = process.ExitCode;
            File.WriteAllText(Path.Combine(Here, "original-stderr.log"), errors.ToString());
            process.Dispose();
            if (exitCode != 0) throw new IOException("Original C oracle failed with exit code " + exitCode + ": " + errors);
        }
    }

    private sealed class Fixture
    {
        internal readonly SourceAiActor[] Actors = new SourceAiActor[14];
        internal readonly SourceRouteState[] States = new SourceRouteState[14];
        internal readonly int[] ActualButtons = new int[14];
        internal readonly int[] TargetFacing = new int[14];
        private readonly int[] rawTape = new int[4096];
        private int rawCursor;
        private bool nativeTapeSent;
        internal SourceRulesState Rules;
        internal SourceAiBall Ball;
        internal SourcePlayDefinition Offense, Defense;
        internal long Tick = 200, SnapTick = 100;
        internal int Camera = 0, HumanMask, Handicap0 = 5, Handicap1 = 5, FireReceiver = -1;
        internal int Quarter = 0;
        internal bool Fire, PlayOver = false, InPlay = true;
        internal SourceGameFlags Flags = SourceGameFlags.None;
        internal SourceQuarterbackPlan Plan = new SourceQuarterbackPlan(8f * 7.8f, 5, 0, 1, false);
        internal SourceInjectedRandom Rng;
        internal SourceAiParameters Parameters;

        internal Fixture(Oracle oracle, uint seed, int offSide = 0, string offense = "play_gen_1",
            string defense = "play_safe_cover", int los = 35, int humanMask = 0, int handicap0 = 5, int handicap1 = 5)
        {
            Offense = Book.GetPlay(offense); Defense = Book.GetPlay(defense);
            Rules = new SourceRulesState(offSide, offSide, 1, los, Math.Min(100, los + 10));
            HumanMask = humanMask; Handicap0 = handicap0; Handicap1 = handicap1;
            Parameters = new SourceAiParameters(Book, handicap0, handicap1);
            SourceFormationLayout layout = SourcePlaybook.LineUp(Offense, Defense, new SourceLineupInput(offSide, los, 0));
            for (int i = 0; i < 14; i++)
            {
                SourceFormationActor placement = layout.Actors[i];
                SourceActorMode mode = placement.Slot.Mode;
                if (mode == SourceActorMode.LinePreSnap) mode = SourceActorMode.Line;
                if (mode == SourceActorMode.QuarterbackPreSnap) mode = SourceActorMode.Quarterback;
                if (mode == SourceActorMode.ReceiverPreSnap) mode = SourceActorMode.Receiver;
                SourceActorJob job = i % 7 < 3 ? i / 7 == offSide ? SourceActorJob.Block : SourceActorJob.Rush : SourceActorJob.Cover;
                if (i / 7 == offSide && i % 7 >= 3) job = SourceActorJob.None;
                SourceDroneFlags flags = (placement.Slot.Control & SourceFormationControl.Motion) != 0 ? SourceDroneFlags.Motion : SourceDroneFlags.None;
                States[i] = SourceRouteState.Begin((i / 7 == offSide ? Offense : Defense).Routes[i % 7],
                    placement.CoverageTarget, flags).WithQuarterbackPlan(Plan);
                var actorFlags = placement.ReverseRouteAtSnap ? SourceAiActorFlags.ReverseRoute : SourceAiActorFlags.None;
                Actors[i] = new SourceAiActor(i, placement.Position, default(SourcePlanar), mode, job,
                    placement.CoverageTarget, actorFlags, -1, placement.Facing);
                TargetFacing[i] = placement.Facing;
            }
            int qb = offSide * 7 + 3;
            Ball = new SourceAiBall(SourceBallType.Pass, Actors[qb].Position, default(SourcePlanar), Actors[qb].Position, qb);
            // Input data only: the C boundary receives these integers, not a PRNG implementation.
            var tape = new Tape(seed);
            for (int i = 0; i < rawTape.Length; i++) rawTape[i] = tape.Raw();
            Rng = new SourceInjectedRandom(site => {
                if (rawCursor == rawTape.Length) throw new InvalidOperationException("Explicit synthetic raw input tape exhausted.");
                return rawTape[rawCursor++];
            }, new SourceRandomProvenance(Book.Profile, "explicit synthetic raw input tape; NOT target algorithm",
                "fixture seed " + seed, SourceRandomEvidence.ExternalUnverified));
            ResetNative(oracle, seed);
        }

        internal SourceAiFrame Frame()
        {
            return new SourceAiFrame(Actors, Book.Directions, Rules, Ball, Tick, SnapTick, Camera, HumanMask,
                PlayOver, Flags, Fire, FireReceiver, Offense.Flags, Defense.Flags, InPlay);
        }

        internal void ResetNative(Oracle oracle, uint seed)
        {
            if (rawCursor != 0) throw new InvalidOperationException("Cannot reset a partly consumed functional-seam tape.");
            nativeTapeSent = false;
            oracle.Send(string.Join(" ", "reset", Rules.OffenseSide, Rules.LineOfScrimmage, Rules.FirstDown, Rules.Down, Quarter,
                (int)Rules.PlayType, (int)Flags, PlayOver ? 0 : InPlay ? 3 : 1, HumanMask,
                Handicap0, Handicap1, Fire ? 1 : 0, FireReceiver, "0", Offense.Identity, Defense.Identity,
                F(Plan.DropbackUnits), Plan.GainSought, Plan.RollDirection, Plan.MotionDirection, Plan.HurdlePass ? 1 : 0));
            SendActors(oracle);
            for (int i = 0; i < 14; i++)
                oracle.Send("route " + i + " " + States[i].Program.Identity + " " + (int)States[i].Flags + " " + States[i].BlitzTicks);
            SendBallClock(oracle);
        }

        internal void EnsureNativeTape(Oracle oracle)
        {
            if (nativeTapeSent) return;
            for (int i = 0; i < rawTape.Length; i += 256)
                oracle.Send("tape 256 " + string.Join(" ", rawTape.Skip(i).Take(256)));
            nativeTapeSent = true;
        }

        internal void SendActors(Oracle oracle)
        {
            for (int i = 0; i < 14; i++)
            {
                SourceAiActor a = Actors[i];
                oracle.Send(string.Join(" ", "actor", i, F(a.Position.X), F(a.Position.Z), F(a.Velocity.X), F(a.Velocity.Z),
                    F(a.Height), (int)a.Mode, (int)a.Job, a.Target, (int)a.Flags, a.Station, a.Facing, TargetFacing[i], a.Stick, a.AttackTarget));
            }
        }

        internal void SendBallClock(Oracle oracle)
        {
            oracle.Send(string.Join(" ", "ball", F(Ball.Position.X), F(Ball.Position.Z), F(Ball.Velocity.X), F(Ball.Velocity.Z),
                F(Ball.Target.X), F(Ball.Target.Z), F(Ball.Height), F(Ball.VerticalVelocity), Ball.Carrier, Ball.IntendedReceiver,
                Ball.Thrower, (int)Ball.Type, Ball.FlightTicks, Ball.TotalTicks, Ball.Bounced ? 1 : 0));
            oracle.Send(string.Join(" ", "clock", Tick, SnapTick, Camera, PlayOver ? 0 : InPlay ? 3 : 1, (int)Flags));
        }

        internal void ChangeActor(int id, SourcePlanar? position = null, SourcePlanar? velocity = null,
            SourceActorMode? mode = null, SourceActorJob? job = null, SourceAiActorFlags? flags = null,
            int? target = null, int? station = null, int? stick = null, int? facing = null, int? attackTarget = null)
        {
            SourceAiActor a = Actors[id];
            Actors[id] = new SourceAiActor(id, position ?? a.Position, velocity ?? a.Velocity, mode ?? a.Mode,
                job ?? a.Job, target ?? a.Target, flags ?? a.Flags, station ?? a.Station, facing ?? a.Facing,
                stick ?? a.Stick, attackTarget ?? a.AttackTarget, a.Height);
            if (target.HasValue) States[id] = States[id].WithTarget(target.Value);
        }
    }

    private static string F(float value) { return value.ToString("R", CI); }
    private static int I(string value) { return int.Parse(value, CI); }
    private static float Float(string value) { return float.Parse(value, CI); }
    private static void Check(bool condition, string artifact)
    {
        Assertions++;
        if (!condition) throw new AssertionFailure(artifact);
    }
    private static void Equal<T>(T expected, T actual, string artifact)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual), artifact + ": C=" + expected + " C#=" + actual);
    }
    private static void FloatEqual(float expected, float actual, string artifact)
    {
        Check(expected == actual, artifact + ": C=" + F(expected) + " C#=" + F(actual));
    }
    private static void Reject<T>(Action operation, string artifact) where T : Exception
    {
        bool rejected = false;
        try { operation(); }
        catch (T) { rejected = true; }
        Check(rejected, artifact);
    }

    private static void Catalog(Oracle oracle)
    {
        Context = "compiled original catalog";
        var forms = new Dictionary<string, SourceFormation>();
        foreach (SourcePlayDefinition play in Book.Plays.Values) forms[play.Formation.Identity] = play.Formation;
        oracle.Send("data");
        string line;
        while ((line = oracle.Read()) != "END")
        {
            string[] row = line.Split(' ');
            int index;
            switch (row[0])
            {
                case "F":
                    index = I(row[2]); SourceFormationSlot slot = forms[row[1]].Slots[index];
                    FloatEqual(Float(row[3]), slot.Offset.X, line + ".X");
                    FloatEqual(Float(row[4]), slot.Offset.Z, line + ".Z");
                    Equal(I(row[5]), (int)slot.Mode, line + ".Mode");
                    Equal(I(row[6]), (int)slot.Control, line + ".Control");
                    Equal(row[7], slot.Script, line + ".Script");
                    break;
                case "D":
                    SourcePlayDefinition play = Book.GetPlay(row[1]);
                    Equal(row[2], play.Formation.Identity, line + ".Formation");
                    Equal(I(row[3]), (int)play.Flags, line + ".Flags");
                    for (int i = 0; i < 7; i++) Equal(row[i + 4], play.Routes[i].Identity, line + ".Route" + i);
                    break;
                case "R": Equal(I(row[3]), Book.GetRoute(row[1]).WordAt(I(row[2])), line); break;
                case "P":
                    var family = (SourceCpuPlayFamily)Enum.Parse(typeof(SourceCpuPlayFamily), row[1]);
                    SourceWeightedPlayPool pool = Book.GetPool(family); index = I(row[3]);
                    Equal(I(row[2]), pool.Entries.Count, line + ".Count");
                    Equal(row[4], pool.Entries[index].Identity, line + ".Index");
                    SourcePlayDefinition resolved = Book.ResolveCpuPlay(new SourceCpuPlayDecision(true, family,
                        SourceMatchFlow.CpuPlayPoolSize(family) == 0 ? -1 : index));
                    Check(ReferenceEquals(pool.Entries[index], resolved), line + ".ResolvedIdentity");
                    break;
                case "H": FloatEqual(Float(row[3]), Book.HandicapValue(row[1], I(row[2])), line); break;
                case "T": FloatEqual(Float(row[2]), Book.Directions.Sin(I(row[1])), line); break;
                case "V": Equal(I(row[2]), Book.Directions.StickHeading(I(row[1])), line); break;
                default: throw new InvalidDataException(line);
            }
            NativeCatalog++;
        }
        Equal(88, Book.Plays.Count, "active catalog play cardinality");
        Equal(140, Book.Routes.Count, "active catalog route cardinality");
        Check(ReferenceEquals(Book.GetPool(SourceCpuPlayFamily.ShortOffense).Entries[27],
            Book.GetPool(SourceCpuPlayFamily.ShortOffense).Entries[28]), "weighted play_switch duplicates at original ordinals 27/28");
        Equal("play_sweep_rgt", Book.GetPool(SourceCpuPlayFamily.ShortOffense).Entries[51].Identity, "original short offense final index");
        Equal("play_da_bomb", Book.GetPool(SourceCpuPlayFamily.LongOffense).Entries[52].Identity, "original long offense final index");
    }

    private static void Numeric(Oracle oracle)
    {
        var fixture = new Tape(391);
        for (int i = 0; i < 20000; i++)
        {
            Context = "unchanged int_time/intercept_time/ptangle/dir2stick numeric case " + i;
            var a = new SourcePlanar(fixture.Quarter(250), fixture.Quarter(180));
            var b = new SourcePlanar(fixture.Quarter(250), fixture.Quarter(180));
            var v = new SourcePlanar(fixture.Quarter(2), fixture.Quarter(2));
            float speed = (i % 10 == 0) ? 0f : (i % 10 == 1) ? (float)Math.Sqrt(v.X * v.X + v.Z * v.Z) : 1.15f + (i % 4) * 0.1f;
            float adjust = i % 11;
            if (i % 25 == 0) b = a;
            string[] row = oracle.Ask(string.Join(" ", "numeric", F(a.X), F(a.Z), F(b.X), F(b.Z), F(v.X), F(v.Z), F(speed), F(adjust)));
            FloatEqual(Float(row[1]), SourceDroneAI.InterceptTime(a, speed, b, v, adjust), "IT_LOWEST");
            FloatEqual(Float(row[2]), SourceDroneAI.InterceptTime(a, speed, b, v, adjust, true), "IT_HIGHEST");
            Equal(I(row[3]), SourceDroneAI.PursuitInterceptTicks(a, b, v, speed), "pursuit ticks");
            Equal(I(row[4]), Book.Directions.Heading(a, b), "ptangle");
            Equal(I(row[5]), Book.Directions.DirectionToStick(Book.Directions.Heading(a, b)), "dir2stick");
            NativeNumerics++;
        }
    }

    private static void Lineups(Oracle oracle)
    {
        string[] defenses = { "play_safe_cover", "play_medium_zone", "play_near_zone", "play_goal_line" };
        int[] lines = { 0, 35, 100 };
        float[] hashes = { -60f, 0f, 60f };
        foreach (SourcePlayDefinition offense in Book.Plays.Values.Where(p => p.Side == SourcePlaySide.Offense || p.Side == SourcePlaySide.Kickoff))
        {
            string[] opponents = offense.Side == SourcePlaySide.Kickoff ? new[] { "play_kick_return" } : defenses;
            foreach (string defenseName in opponents)
                foreach (int side in new[] { 0, 1 })
                    foreach (bool flip in new[] { false, true })
                        foreach (int los in lines)
                            foreach (float hash in hashes)
                            {
                                Context = "original nfl.c lineup_players " + offense.Identity + "/" + defenseName +
                                    " side=" + side + " flip=" + flip + " los=" + los + " hash=" + hash;
                                var f = new Fixture(oracle, 812, side, offense.Identity, defenseName, los);
                                SourceFormationLayout layout = SourcePlaybook.LineUp(offense, Book.GetPlay(defenseName),
                                    new SourceLineupInput(side, los, hash, flip));
                                string[] row = oracle.Ask("lineup " + (flip ? 1 : 0) + " " + F(hash));
                                int at = 1;
                                foreach (SourceFormationActor actor in layout.Actors)
                                {
                                    FloatEqual(Float(row[at++]), actor.Position.X, "source lineup X");
                                    FloatEqual(Float(row[at++]), actor.Position.Z, "source lineup hash/flip/weak/cover Z");
                                    Equal(I(row[at++]), (int)actor.Slot.Mode, "source lineup role mode");
                                    Equal(I(row[at++]), actor.Facing, "source lineup team facing");
                                    Equal(I(row[at++]), actor.CoverageTarget, "source lineup cross-actor coverage");
                                    Equal(row[at++], actor.Slot.Script, "source lineup actual script");
                                }
                                Equal(row.Length, at, "lineup trace field count"); LineupCases++;
                            }
        }
    }

    private static void DrawContext(SourceRandomDraw draw, SourceRandomContext context, int index)
    {
        Equal(context.SourceTick, draw.Site.SourceTick, "RNG source tick");
        Equal(context.ActorId, draw.Site.ActorId, "RNG actor");
        Equal(context.Process, draw.Site.Process, "RNG process");
        Equal(context.ProcessOrder, draw.Site.ProcessOrder, "RNG process order");
        Equal(context.FirstCallIndex + index, draw.Site.CallIndex, "RNG within-process call order");
        Equal(context.Profile, draw.Site.Profile, "RNG source profile");
    }

    private static void NativeDraw(string[] row, ref int at, SourceRandomDraw draw, int order)
    {
        Equal(SourceRandomKind.RandRange, draw.Site.Kind, "original randrng call kind");
        Equal(I(row[at++]), draw.Site.BoundOrModulus, "RNG bound");
        Equal(I(row[at++]), draw.Value, "RNG derived value");
        int raw = I(row[at++]);
        Equal(raw == -1 ? (int?)null : raw, draw.Raw, "explicit raw input/zero-bound consumption");
        Equal((long)order, draw.Order, "global RNG wrapper order");
        Equal(draw.Site.BoundOrModulus != 0, draw.Site.ConsumesPlatformValue, "RNG raw consumption contract");
        RandomCalls++;
    }

    private static void CompareStep(string[] row, SourceRouteStep result, Fixture f, int actor, int oldDrawCount)
    {
        SourceRouteState s = result.State;
        Equal("S", row[0], "native state marker"); Equal(row[1], s.Program.Identity, "program identity");
        int[] actual = { s.WordIndex, (int)s.Flags, s.Stick, s.OldStick, s.StickTicks, (int)s.Buttons, (int)s.OldButtons,
            s.ButtonTicks, (int)s.Job, s.Target, s.PassReactTicks, s.WaitTicks, s.BlockTicks, s.FakeTicks, s.CatchTicks,
            s.EvadeTicks, s.ThinkTicks, s.BlitzTicks, s.MoveTicks, s.OrderTicks, s.OrderStick, s.FakeZoneTicks };
        string[] labels = { "word", "flags", "stick", "old_stick", "os_time", "buttons", "old_buttons", "ob_time", "job",
            "target", "pass_react_time", "wait_time", "block_time", "fake_time", "catch_time", "evade_time", "think_time",
            "blitz_time", "move_time", "order_time", "order_dir", "fake_zone_time" };
        int at = 2;
        for (int i = 0; i < actual.Length; i++) Equal(I(row[at++]), actual[i], labels[i]);
        float[] floats = { s.Mark.X, s.Mark.Z, s.Ghost.X, s.Ghost.Z, s.GhostVelocity.X, s.GhostVelocity.Z,
            s.ZoneLowX, s.ZoneHighX, s.ZoneLowZ, s.ZoneHighZ, s.ZonePoint.X, s.ZonePoint.Z, s.ZoneVelocity.X, s.ZoneVelocity.Z };
        for (int i = 0; i < floats.Length; i++) FloatEqual(Float(row[at++]), floats[i], "state float " + i);
        Equal(row[at++], result.Script ?? "-", "drone_script"); Equal(row[at++], s.PendingCatch ?? "-", "pending_catch");
        Equal(I(row[at++]), result.ThrowTarget, "drone_throw_target");
        if (result.ThrowTarget >= 0) Equal(result.ThrowTarget, s.LastThrowTarget, "pure state retains hurdle/next-tick throw target");
        Equal(I(row[at++]), result.ResetNonAButtonTime ? 0 : 17, "non_buta_time effect");
        Equal(I(row[at++]), result.ForceNonTurboOneTick ? 1 : 17, "non_turbo_time effect");
        int actualStick = result.CommandWritten ? s.Stick : f.Actors[actor].Stick;
        int buttons = result.ButtonsWritten ? (int)s.Buttons : f.ActualButtons[actor];
        Equal(I(row[at++]), actualStick, "actual stick assignment");
        Equal(I(row[at++]), buttons, "actual button assignment");
        if (!result.CommandWritten) RetainedCommands++;
        if (!result.ButtonsWritten) RetainedButtons++;
        int draws = I(row[at++]); Equal(draws, result.RandomCalls, "RNG call count");
        Equal(draws, f.Rng.Trace.Count - oldDrawCount, "RNG trace count");
        for (int i = 0; i < draws; i++)
        {
            SourceRandomDraw draw = f.Rng.Trace[oldDrawCount + i];
            NativeDraw(row, ref at, draw, oldDrawCount + i);
            DrawContext(draw, new SourceRandomContext(f.Tick, actor, "actor_proc", actor, Book.Profile), i);
        }
        f.States[actor] = s;
        foreach (SourceDefenderEffect effect in result.DefenderEffects)
        {
            f.States[effect.ActorId] = f.States[effect.ActorId].Apply(effect);
            CrossActorEffects++;
        }
        for (int i = 0; i < 14; i++)
        {
            Equal(I(row[at++]), f.States[i].FakeTicks, "cross-actor fake_time " + i);
            Equal(I(row[at++]), f.States[i].OrderTicks, "cross-actor order_time " + i);
            Equal(I(row[at++]), f.States[i].OrderStick, "cross-actor order_dir " + i);
        }
        Equal(row.Length, at, "native trace field count");
        f.ChangeActor(actor, job: s.Job, target: s.Target, stick: actualStick);
        f.ActualButtons[actor] = buttons;
        if (result.Script != null) ScriptsVisited.Add(result.Script);
        NativeSteps++;
    }

    private static SourceRouteStep Step(Oracle oracle, Fixture f, int actor, bool update = false, bool stickOnly = false,
        SourceDroneSnapContext? snap = null, SourceDroneKickerContext? kicker = null,
        SourceDroneButtons humanNewButtons = SourceDroneButtons.None)
    {
        SourceAiFrame frame = f.Frame();
        SourceBlockAssignments blocking = null;
        IReadOnlyList<SourceReceiverAssessment> receivers = null;
        if (update)
        {
            blocking = SourceDroneAI.AssignBlockers(frame);
            receivers = SourceDroneAI.UpdateReceiverData(frame);
            foreach (int blocker in blocking.Blockers) f.ChangeActor(blocker, target: blocking.TargetsByActor[blocker]);
            frame = f.Frame();
        }
        f.EnsureNativeTape(oracle);
        f.SendActors(oracle); f.SendBallClock(oracle);
        if (kicker.HasValue && kicker.Value.Stage == SourceKickerStage.PreKickoff)
            oracle.Send(string.Join(" ", "clock", f.Tick, f.SnapTick, f.Camera, 2, (int)f.Flags));
        int before = f.Rng.Trace.Count;
        var input = new SourceRouteInput(frame, actor, f.Parameters,
            new SourceRandomContext(f.Tick, actor, "actor_proc", actor, Book.Profile), stickOnly,
            f.Plan.MotionDirection, f.Actors[f.Rules.OffenseSide * 7 + 3].Stick, humanNewButtons,
            0, 0, false, false, blocking, receivers, f.States,
            (actor / 7 == f.Rules.OffenseSide ? f.Offense : f.Defense).Formation.Slots[actor % 7], f.TargetFacing[actor], snap, kicker);
        int pc = f.States[actor].WordIndex;
        if (pc < f.States[actor].Program.WordCount) OpcodesVisited.Add(f.States[actor].Program.WordAt(pc));
        SourceRouteStep result = SourceDroneAI.StepRoute(f.States[actor], input, f.Rng);
        string[] row = oracle.Ask("step " + actor + " " + (stickOnly ? 1 : 0) + " " + (update ? 1 : 0));
        try { CompareStep(row, result, f, actor, before); }
        catch
        {
            File.WriteAllText(Path.Combine(Here, "last-case.json"), JsonSerializer.Serialize(new {
                Context, Native = row, Actual = result.State, Actor = f.Actors[actor],
                Rng = f.Rng.Trace.Skip(before).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        return result;
    }

    private static void Routes(Oracle oracle, int ticks)
    {
        int number = 0;
        foreach (SourceRouteProgram route in Book.Routes.Values)
        {
            for (int side = 0; side < 2; side++)
            {
                var f = new Fixture(oracle, (uint)(400 + number * 3 + side), side);
                int actor = side * 7 + 4;
                bool zone = route.WordAt(0) == (int)SourceRouteOpcode.Zone;
                if (zone) actor = (1 - side) * 7 + 3;
                SourceActorMode mode = zone ? SourceActorMode.DefensiveBackPreSnap : SourceActorMode.Receiver;
                f.States[actor] = SourceRouteState.Begin(route, zone ? side * 7 + 4 : (1 - side) * 7 + 3).WithQuarterbackPlan(f.Plan);
                f.ChangeActor(actor, mode: mode, target: f.States[actor].Target, job: SourceActorJob.None);
                f.ResetNative(oracle, (uint)(400 + number * 3 + side));
                int animationTicks = 0;
                for (int tick = 0; tick < ticks; tick++)
                {
                    f.Tick = 200 + tick;
                    Context = "actual route " + route.Identity + " offenseSide=" + side + " sourceTick=" + f.Tick;
                    SourceAiActor a = f.Actors[actor];
                    SourceAiActorFlags flags = a.Flags & ~SourceAiActorFlags.LowerUninterruptible;
                    if (animationTicks-- > 0) flags |= SourceAiActorFlags.LowerUninterruptible;
                    f.ChangeActor(actor, flags: flags);
                    SourceRouteStep result = Step(oracle, f, actor);
                    if (result.Script != null || result.WaveArm) animationTicks = 3;
                    float speed = (result.State.Buttons & SourceDroneButtons.C) != 0 ? 1.45f : 1.15f;
                    var velocity = new SourcePlanar(result.DesiredMovement.X * speed, result.DesiredMovement.Z * speed);
                    SourcePlanar pos = f.Actors[actor].Position;
                    // Synthetic input trajectory, not a claim about original player physics.
                    f.ChangeActor(actor, position: new SourcePlanar(Math.Max(-460.2f, Math.Min(460.2f, pos.X + velocity.X)),
                        Math.Max(-175.05f, Math.Min(175.05f, pos.Z + velocity.Z))), velocity: velocity);
                }
                Check(RouteSidesVisited.Add(route.Identity + "|" + side), "each actual route/side executed exactly once");
            }
            number++;
        }
    }

    private static void ReceiverAndBlocking(Oracle oracle)
    {
        var values = new Tape(1194);
        for (int i = 0; i < 1200; i++)
        {
            Context = "active update_receiver_data/find_receiver/assign_blockers fixture " + i;
            var f = new Fixture(oracle, (uint)(7000 + i), i % 2, humanMask: i % 7 == 0 ? 3 : 0,
                handicap0: i % 12, handicap1: (i / 12) % 12);
            f.Fire = i % 3 == 0; f.FireReceiver = f.Rules.OffenseSide * 7 + 4;
            for (int id = 0; id < 14; id++)
            {
                SourceActorMode mode = id / 7 == f.Rules.OffenseSide
                    ? id % 7 == 3 ? SourceActorMode.Quarterback : id % 7 < 3 ? SourceActorMode.Line : SourceActorMode.Receiver
                    : id % 7 < 3 ? SourceActorMode.Line : SourceActorMode.DefensiveBack;
                SourceActorJob job = id / 7 == f.Rules.OffenseSide
                    ? id % 7 < 3 ? SourceActorJob.Block : SourceActorJob.None
                    : id % 7 < 3 ? SourceActorJob.Rush : id % 2 == 0 ? SourceActorJob.Cover : SourceActorJob.Zone;
                f.ChangeActor(id, new SourcePlanar(values.Quarter(300), values.Quarter(160)),
                    new SourcePlanar(values.Quarter(2), values.Quarter(2)), mode, job,
                    values.Next(9) == 0 ? SourceAiActorFlags.Reacting : SourceAiActorFlags.None,
                    target: id / 7 == f.Rules.OffenseSide ? -1 : f.Rules.OffenseSide * 7 + 4,
                    station: values.Next(6) == 0 ? 0 : -1);
            }
            if (i % 17 == 0)
            {
                int qb = f.Rules.OffenseSide * 7 + 3, rec = f.Rules.OffenseSide * 7 + 4;
                f.ChangeActor(qb, new SourcePlanar(0, 0));
                f.ChangeActor(rec, new SourcePlanar(39f, 0));
            }
            int carrier = f.Rules.OffenseSide * 7 + 3;
            f.Ball = new SourceAiBall(SourceBallType.Pass, f.Actors[carrier].Position, default(SourcePlanar), default(SourcePlanar), carrier);
            f.ResetNative(oracle, (uint)(7000 + i));
            f.SendActors(oracle); f.SendBallClock(oracle);
            SourceAiFrame frame = f.Frame();
            IReadOnlyList<SourceReceiverAssessment> receivers = SourceDroneAI.UpdateReceiverData(frame);
            string[] row = oracle.Ask("receivers");
            Equal(I(row[1]), receivers.Count, "source eligible receiver count");
            int at = 2;
            foreach (SourceReceiverAssessment receiver in receivers)
            {
                Equal(I(row[at++]), receiver.ActorId, "receiver original order");
                Equal(I(row[at++]), receiver.Coverage, "predicted coverage");
                Equal(I(row[at++]), receiver.LikelyYards, "predicted post-catch yards/fire");
            }
            for (int cover = 0; cover <= 2; cover++)
                for (int gain = -1; gain <= 30; gain++)
                    Equal(I(row[at++]), SourceDroneAI.FindReceiver(receivers, cover, gain), "active find_receiver cover=" + cover + " yards=" + gain);
            Equal(row.Length, at, "receiver trace field count"); ReceiverCases++;
            SourceBlockAssignments blocks = SourceDroneAI.AssignBlockers(frame);
            row = oracle.Ask("assignments"); at = 2;
            Equal(I(row[1]), blocks.Rushers.Count, "eligible rusher count");
            bool equalTimes = blocks.RushTimes.Count != blocks.RushTimes.Distinct().Count();
            Equal(equalTimes, blocks.HasUnverifiedEqualTimeOrder, "equal-time target qsort limitation is exposed");
            for (int rusher = 0; rusher < blocks.Rushers.Count; rusher++)
            {
                Equal(I(row[at++]), blocks.Rushers[rusher], "rusher original actor order");
                Equal(I(row[at++]), blocks.RushTimes[rusher], "rusher intercept urgency ticks");
                int nativeUrgency = I(row[at++]);
                if (!equalTimes) Equal(nativeUrgency, blocks.UrgencyIndices[rusher], "urgency permutation");
            }
            int count = I(row[at++]); Equal(count, blocks.Blockers.Count, "eligible blocker count");
            for (int blocker = 0; blocker < count; blocker++)
            {
                int id = I(row[at++]), target = I(row[at++]);
                Equal(id, blocks.Blockers[blocker], "blocker original actor order");
                if (!equalTimes) Equal(target, blocks.TargetsByActor[id], "optimized source block assignment");
            }
            Equal(row.Length, at, "blocking trace field count"); BlockCases++;
        }
    }

    private static void ActiveAi(Oracle oracle)
    {
        for (int scenario = 0; scenario < 48; scenario++)
        {
            int side = scenario % 2;
            var f = new Fixture(oracle, (uint)(9900 + scenario), side, defense: scenario % 3 == 0 ? "play_medium_zone" : "play_safe_cover",
                humanMask: scenario % 4 == 0 ? (1 << (1 - side)) : 0, handicap0: scenario % 12, handicap1: (scenario + 4) % 12);
            int actor = scenario < 24 ? side * 7 + 3 : (1 - side) * 7 + 3;
            bool qb = scenario < 24;
            f.ChangeActor(actor, mode: qb ? SourceActorMode.Quarterback : SourceActorMode.DefensiveBack,
                job: qb ? SourceActorJob.None : SourceActorJob.Cover, target: qb ? -1 : side * 7 + 4);
            f.Plan = new SourceQuarterbackPlan(8f * 7.8f, scenario % 2 == 0 ? 5 : 20, -2, 1, scenario % 5 == 0);
            f.States[actor] = f.States[actor].WithQuarterbackPlan(f.Plan);
            if (!qb)
                f.States[actor] = f.States[actor].WithFlags(f.States[actor].Flags |
                    (scenario % 3 == 0 ? SourceDroneFlags.SmartIntercept : SourceDroneFlags.None));
            f.ResetNative(oracle, (uint)(9900 + scenario));
            for (int tick = 0; tick < 75; tick++)
            {
                f.Tick = 200 + tick; f.SnapTick = 200;
                Context = "active " + (qb ? "_drone_qb" : "_drone_db") + " scenario " + scenario + " sourceTick " + f.Tick;
                if (!qb && tick >= 10 && tick < 45)
                {
                    int receiver = side * 7 + 4;
                    var target = f.Actors[receiver].Position;
                    f.Ball = new SourceAiBall(SourceBallType.Pass, target, new SourcePlanar(2f, 0.25f), target,
                        -1, receiver, side * 7 + 3, tick - 10, 30, 25f, 0.2f);
                }
                else if (!qb && tick >= 45)
                {
                    int receiver = side * 7 + 4;
                    f.Ball = new SourceAiBall(SourceBallType.Pass, f.Actors[receiver].Position,
                        default(SourcePlanar), f.Actors[receiver].Position, receiver);
                }
                SourceRouteStep result = Step(oracle, f, actor, update: true);
                if (result.ThrowTarget >= 0) f.ChangeActor(actor, flags: f.Actors[actor].Flags | SourceAiActorFlags.Throwing);
                SourcePlanar pos = f.Actors[actor].Position;
                var velocity = new SourcePlanar(result.DesiredMovement.X * 1.3f, result.DesiredMovement.Z * 1.3f);
                f.ChangeActor(actor, position: new SourcePlanar(pos.X + velocity.X, pos.Z + velocity.Z), velocity: velocity);
            }
        }
    }

    private static void Initialization(Oracle oracle)
    {
        string[] off = { "play_gen_1", "play_punt", "play_field_goal", "play_fake_punt", "play_QBdeep", "play_kickoff_short" };
        string[] def = { "play_safe_cover", "play_suicide_blitz", "play_mix_it_up", "play_stuff_it", "play_fake_zone" };
        for (int scenario = 0; scenario < 300; scenario++)
        {
            Context = "unchanged init_drones_prelineup/postlineup " + scenario;
            int side = scenario % 2;
            string offense = off[scenario % off.Length];
            string defense = offense == "play_kickoff_short" ? "play_kick_return_short" : def[scenario % def.Length];
            uint seed = (uint)(19200 + scenario);
            var f = new Fixture(oracle, seed, side, offense, defense, los: (scenario * 7) % 101,
                humanMask: (scenario / 2) % 4, handicap0: scenario % 12, handicap1: (scenario / 7) % 12);
            f.Quarter = scenario % 7; f.InPlay = false;
            f.Rules = new SourceRulesState(side, side, scenario % 4 + 1, f.Rules.LineOfScrimmage, f.Rules.FirstDown,
                offense == "play_kickoff_short" ? SourcePlayType.Kickoff : SourcePlayType.Normal);
            SourceFormationLayout layout = SourcePlaybook.LineUp(f.Offense, f.Defense, new SourceLineupInput(side, f.Rules.LineOfScrimmage, 0));
            for (int i = 0; i < 14; i++)
                f.ChangeActor(i, position: layout.Actors[i].Position, mode: layout.Actors[i].Slot.Mode,
                    job: SourceActorJob.None, flags: SourceAiActorFlags.None, target: -1, stick: 24);
            f.ResetNative(oracle, seed);
            f.EnsureNativeTape(oracle);
            var preContext = new SourceRandomContext(f.Tick, -1, "game_proc:prelineup", 0, Book.Profile);
            SourceDroneInitialization pre = SourceDroneAI.InitializePreLineup(f.Frame(), f.Offense, f.Defense,
                f.Parameters, f.Quarter, preContext, f.Rng);
            string[] row = oracle.Ask("preinit");
            int at = 1;
            for (int i = 0; i < 14; i++)
            {
                Equal(I(row[at++]), (int)pre.States[i].Flags, "prelineup actor " + i + " flags");
                Equal(I(row[at++]), pre.States[i].BlitzTicks, "prelineup actor " + i + " blitz ticks");
                f.States[i] = pre.States[i];
            }
            int draws = I(row[at++]); Equal(draws, pre.RandomCalls, "prelineup calls");
            Equal(draws, f.Rng.Trace.Count, "prelineup global wrapper count");
            for (int i = 0; i < draws; i++)
            {
                NativeDraw(row, ref at, f.Rng.Trace[i], i);
                DrawContext(f.Rng.Trace[i], preContext, i);
            }
            Equal(row.Length, at, "prelineup trace shape");
            for (int i = 0; i < 14; i++) f.ChangeActor(i, target: layout.Actors[i].CoverageTarget);
            f.SendActors(oracle); f.SendBallClock(oracle);
            int before = f.Rng.Trace.Count;
            for (int i = (1 - side) * 7; i < (1 - side) * 7 + 7; i++)
            {
                var defenderContext = new SourceRandomContext(f.Tick, i, "game_proc:init_defender", 10 + i, Book.Profile);
                int start = f.Rng.Trace.Count;
                SourceRouteStep initialized = SourceDroneAI.InitializePreSnapDecision(f.States[i],
                    new SourceRouteInput(f.Frame(), i, f.Parameters, defenderContext,
                        droneStates: f.States, formationSlot: f.Defense.Formation.Slots[i % 7]), f.Rng);
                for (int j = start; j < f.Rng.Trace.Count; j++) DrawContext(f.Rng.Trace[j], defenderContext, j - start);
                Equal(f.Rng.Trace.Count - start, initialized.RandomCalls, "raw defender initialization RNG count");
                f.States[i] = initialized.State;
                f.ChangeActor(i, job: initialized.State.Job, target: initialized.State.Target);
                Check(!initialized.CommandWritten && !initialized.ButtonsWritten, "post-lineup raw dispatcher does not write actor switches");
            }
            var postContext = new SourceRandomContext(f.Tick, -1, "game_proc:postlineup_random", 100, Book.Profile);
            int postStart = f.Rng.Trace.Count;
            SourceDroneInitialization post = SourceDroneAI.InitializePostLineupRandom(f.Frame(), f.Parameters, postContext, f.Rng);
            for (int j = postStart; j < f.Rng.Trace.Count; j++) DrawContext(f.Rng.Trace[j], postContext, j - postStart);
            Equal(f.Rng.Trace.Count - postStart, post.RandomCalls, "postlineup random phase count");
            row = oracle.Ask("postinit"); at = 1;
            FloatEqual(Float(row[at++]), post.QuarterbackPlan.DropbackUnits, "postlineup dropback");
            Equal(I(row[at++]), post.QuarterbackPlan.GainSought, "postlineup gain sought");
            Equal(I(row[at++]), post.QuarterbackPlan.RollDirection, "postlineup roll uninitialized");
            Equal(I(row[at++]), post.QuarterbackPlan.MotionDirection, "shipping motion_dir forced +1 AFTER draw");
            Equal(I(row[at++]), post.QuarterbackPlan.HurdlePass ? 1 : 0, "postlineup hurdle flag");
            for (int i = 0; i < 14; i++)
            {
                SourceRouteState state = f.States[i];
                Equal(row[at++], state.Program.Identity, "initialized route identity " + i);
                int[] actual = { state.WordIndex, (int)state.Flags, state.BlitzTicks, (int)f.Actors[i].Job, state.Target,
                    state.Stick, state.OldStick, state.StickTicks, (int)state.Buttons, state.ButtonTicks,
                    state.ThinkTicks, state.FakeZoneTicks };
                foreach (int value in actual) Equal(I(row[at++]), value, "initialized actor " + i);
                foreach (float value in new[] { state.ZoneLowX, state.ZoneHighX, state.ZoneLowZ, state.ZoneHighZ })
                    FloatEqual(Float(row[at++]), value, "initialized zone " + i);
                Equal(I(row[at++]) != 0, post.UnblockableByActor[i], "source superman permission " + i);
            }
            draws = I(row[at++]); Equal(draws, f.Rng.Trace.Count - before, "entire postlineup ordered calls including defensive initialization");
            for (int i = 0; i < draws; i++) NativeDraw(row, ref at, f.Rng.Trace[before + i], before + i);
            Equal(row.Length, at, "postlineup trace shape");
            InitCases++;
        }
    }

    private static void RoleModes(Oracle oracle)
    {
        for (int scenario = 0; scenario < 48; scenario++)
        {
            int side = scenario % 2, role = scenario % 3;
            var f = new Fixture(oracle, (uint)(25000 + scenario), side, handicap0: scenario % 12, handicap1: (scenario / 3) % 12);
            int actor = role == 0 ? side * 7 : role == 1 ? (1 - side) * 7 : side * 7 + 4;
            f.ChangeActor(actor, mode: role < 2 ? SourceActorMode.Line : SourceActorMode.Receiver,
                target: role == 0 ? (1 - side) * 7 : -1, job: role == 0 ? SourceActorJob.Block : SourceActorJob.Rush);
            if (role == 2) f.Ball = new SourceAiBall(SourceBallType.Pass, f.Actors[actor].Position,
                default(SourcePlanar), f.Actors[actor].Position, actor);
            f.ResetNative(oracle, (uint)(25000 + scenario));
            for (int tick = 0; tick < 40; tick++)
            {
                f.Tick = 200 + tick;
                Context = "active " + (role == 2 ? "_drone_carrier" : "_drone_line") + " scenario " + scenario + " tick " + f.Tick;
                SourceRouteStep result = Step(oracle, f, actor, update: true, stickOnly: scenario % 5 == 0);
                SourcePlanar p = f.Actors[actor].Position;
                var velocity = new SourcePlanar(result.DesiredMovement.X * 1.35f, result.DesiredMovement.Z * 1.35f);
                f.ChangeActor(actor, position: new SourcePlanar(p.X + velocity.X, p.Z + velocity.Z), velocity: velocity);
                if (role == 2) f.Ball = new SourceAiBall(SourceBallType.Pass, f.Actors[actor].Position,
                    default(SourcePlanar), f.Actors[actor].Position, actor);
            }
        }
        for (int scenario = 0; scenario < 64; scenario++)
        {
            int side = scenario % 2, actor = side * 7 + 3;
            var f = new Fixture(oracle, (uint)(26000 + scenario), side);
            f.InPlay = false;
            f.Plan = new SourceQuarterbackPlan(8f * 7.8f, 5, 0, scenario % 3 - 1, false);
            f.ChangeActor(actor, mode: SourceActorMode.QuarterbackPreSnap);
            f.ResetNative(oracle, (uint)(26000 + scenario));
            for (int tick = 0; tick < 60; tick++)
            {
                f.Tick = 200 + tick;
                int mask = scenario % 16, offAudible = tick % 11 == 0 ? 1 : 0, defAudible = tick % 13 == 0 ? 1 : 0;
                int motionStick = tick % 5 == 0 ? 21 : 24;
                f.ChangeActor(side * 7 + 4, stick: motionStick);
                oracle.Send(string.Join(" ", "controls", scenario % 2, mask, offAudible, defAudible,
                    f.Plan.MotionDirection, motionStick, side * 7 + 3, 0));
                Context = "active _drone_qbps station/audible/motion/modulo scenario " + scenario + " tick " + f.Tick;
                Step(oracle, f, actor, snap: new SourceDroneSnapContext(mask, scenario % 2 != 0, offAudible, defAudible, motionStick));
            }
        }
        for (int scenario = 0; scenario < 32; scenario++)
        {
            int side = scenario % 2, actor = side * 7 + 6;
            var f = new Fixture(oracle, (uint)(27000 + scenario), side, offense: "play_field_goal");
            f.ChangeActor(actor, mode: SourceActorMode.Kicker);
            f.ResetNative(oracle, (uint)(27000 + scenario));
            for (int stage = 0; stage < 4; stage++)
            {
                f.Tick = 200 + stage;
                f.PlayOver = stage == 0; f.InPlay = stage == 3;
                var buttons = (SourceDroneButtons)(scenario % 16);
                int qb = side * 7 + 3;
                f.Ball = new SourceAiBall(SourceBallType.FieldGoal, f.Actors[qb].Position, default(SourcePlanar),
                    f.Actors[qb].Position, qb, qb);
                oracle.Send(string.Join(" ", "controls", 0, 0, 0, 0, 0, 24, qb, (int)buttons));
                Context = "active _drone_kicker source stage " + stage + " scenario " + scenario;
                Step(oracle, f, actor, kicker: new SourceDroneKickerContext((SourceKickerStage)stage, buttons));
            }
        }
    }

    private static void DefinedReceiverBallBranches(Oracle oracle)
    {
        for (int scenario = 0; scenario < 96; scenario++)
        {
            int side = scenario % 2, actor = side * 7 + 4, qb = side * 7 + 3;
            var f = new Fixture(oracle, (uint)(29000 + scenario), side, humanMask: scenario % 4 == 0 ? 1 << side : 0);
            f.ChangeActor(actor, position: new SourcePlanar(SourcePlaybook.WorldX(45, side), 0), mode: SourceActorMode.Receiver);
            f.ResetNative(oracle, (uint)(29000 + scenario));
            for (int phase = 0; phase < 3; phase++)
            {
                f.Tick = 200 + phase;
                bool humanJump = scenario % 4 == 0;
                SourcePlanar actorPosition = f.Actors[actor].Position;
                var target = new SourcePlanar(actorPosition.X + (phase == 0 ? 20f : 1f), actorPosition.Z);
                SourcePlanar velocity = scenario % 3 == 0 ? default(SourcePlanar) : new SourcePlanar(2f, 0);
                SourcePlanar ballPosition = scenario % 3 == 0 ? target : actorPosition;
                f.Ball = new SourceAiBall(SourceBallType.Pass, ballPosition, velocity, target, -1, actor, qb,
                    phase == 2 ? 20 : 5, 30, 25f, 0.1f);
                oracle.Send("buttons " + qb + " 0 " + (humanJump ? 1 : 0));
                Context = "defined _drone_wpn ball pursuit/canned/human-jump branch " + scenario + " phase " + phase;
                Step(oracle, f, actor, humanNewButtons: humanJump ? SourceDroneButtons.A : SourceDroneButtons.None);
            }
        }
    }

    private static void SwitchTiming(Oracle oracle)
    {
        int scenario = 0;
        for (int side = 0; side < 2; side++)
            for (int actorSide = 0; actorSide < 2; actorSide++)
                for (int stage = 0; stage < 3; stage++)
                    foreach (int think in new[] { 0, 1, 3 })
                        foreach (bool stickOnly in new[] { false, true })
                            foreach (int stickTicks in new[] { 30, 31 })
                            {
                                int actor = actorSide * 7 + 4;
                                var f = new Fixture(oracle, (uint)(31000 + scenario), side);
                                f.PlayOver = stage == 0; f.InPlay = stage == 2;
                                f.ChangeActor(actor, mode: SourceActorMode.Lineup, stick: 45);
                                SourceRouteState state = f.States[actor];
                                state.ThinkTicks = think; state.EvadeTicks = 2; state.BlitzTicks = 1;
                                state.Stick = 3; state.OldStick = 23; state.StickTicks = stickTicks;
                                state.Buttons = SourceDroneButtons.A; state.OldButtons = SourceDroneButtons.B;
                                state.ButtonTicks = 7;
                                f.States[actor] = state;
                                f.ActualButtons[actor] = (int)(SourceDroneButtons.B | SourceDroneButtons.C);
                                f.ResetNative(oracle, (uint)(31000 + scenario));
                                oracle.Send(string.Join(" ", "timers", actor, think, 2, 1, 3, 23, stickTicks, 1, 2, 7));
                                oracle.Send("buttons " + actor + " " + f.ActualButtons[actor] + " 0");
                                for (int tick = 0; tick < 3; tick++)
                                {
                                    f.Tick = 200 + tick;
                                    Context = "original get_drone_switches retained controls/timers scenario " + scenario +
                                        " side=" + side + " actorSide=" + actorSide + " stage=" + stage +
                                        " think=" + think + " stickOnly=" + stickOnly + " smoothing=" + stickTicks + " tick=" + tick;
                                    Step(oracle, f, actor, stickOnly: stickOnly);
                                    SwitchTimingTicks++;
                                }
                                scenario++;
                            }
    }

    private static void GuardsAndRng()
    {
        Context = "explicit source guards/RNG provenance";
        Reject<ArgumentOutOfRangeException>(() => new SourcePlanar(float.NaN, 0), "NaN source coordinate rejected");
        Reject<ArgumentOutOfRangeException>(() => new SourcePlanar(0, float.PositiveInfinity), "infinite source coordinate rejected");
        Reject<InvalidOperationException>(() => new SourceRouteProgram("bad_route", "fixture_blob", 1, new[] { 999 }), "unknown opcode fails with source identity");
        Reject<InvalidOperationException>(() => new SourceRouteProgram("bad_face", "fixture_blob", 1, new[] { 2 }), "truncated opcode parameters rejected");
        Reject<InvalidOperationException>(() => new SourceRouteProgram("bad_branch", "fixture_blob", 1, new[] { -3, 1 }), "branch into operand rejected");
        var provenance = new SourceRandomProvenance(Book.Profile, "synthetic explicit oracle tape", "raw samples", SourceRandomEvidence.RecordedOracle);
        var context = new SourceRandomContext(57, 3, "player_proc", 3, Book.Profile);
        var negative = new SourceRandomDraw(context.At(SourceRandomKind.RandRange, -3, "negative-bound", 0), 11, 0, provenance);
        Equal(2, negative.Value, "signed C random()%negative bound");
        var zero = new SourceRandomDraw(context.At(SourceRandomKind.RandRange, 0, "zero-bound", 1), null, 1, provenance);
        Equal(0, zero.Value, "randrng(0) exact zero without raw draw");
        var raw = new SourceRandomDraw(context.At(SourceRandomKind.CStandardRand, 33, "raw-C-rand", 2), 100, 2, provenance);
        Equal(1, raw.Value, "raw C modulo interpretation"); Equal(100, raw.MatchFlowValue, "MatchFlow retains raw C rand argument");
        var tape = new SourceRecordedRandom(new[] { negative, zero, raw }, provenance);
        Equal(2, tape.Draw(negative.Site).Value, "recorded negative draw");
        Equal(0, tape.Draw(zero.Site).Value, "recorded zero draw");
        Reject<InvalidOperationException>(() => tape.Draw(context.At(SourceRandomKind.RandRange, 33, "raw-C-rand", 2)), "randrng vs raw C rand divergence rejected");
        Equal(100, tape.Draw(raw.Site).Raw.Value, "recorded raw C draw remains unconsumed on divergence");
        Reject<InvalidOperationException>(() => tape.Draw(raw.Site), "oracle exhaustion rejected");
        Reject<InvalidOperationException>(() => new SourceUnavailableRandom(Book.Profile, "caller must supply seed provenance").Draw(raw.Site), "missing exact algorithm never silently substitutes .NET/libc/time seed");
        var input = new StringReader("source-playbook/v1\trev\tprofile\nroute\tbad_identity\tblob\t1\t999\n");
        try { SourcePlaybook.LoadConverted(input); throw new AssertionFailure("Missing malformed route failure."); }
        catch (FormatException e) { Check(e.Message.Contains("bad_identity") && e.Message.Contains("999"), "invalid converted route names source identity and opcode"); }
        var simple = new[] { new SourceReceiverAssessment(4, 0, 1, 4, 39f, 1, default(SourcePlanar)) };
        Equal(-1, SourceDroneAI.FindReceiver(simple, 0, 0), "strict five-yard exclusion exactly at YD5");
        var actors = new SourceAiActor[14];
        for (int i = 0; i < 14; i++)
            actors[i] = new SourceAiActor(i, new SourcePlanar(i * 10f, 0), default(SourcePlanar),
                i == 3 ? SourceActorMode.Quarterback : SourceActorMode.Receiver);
        var rules = new SourceRulesState(0, 0, 1, 35, 45);
        var ball = new SourceAiBall(SourceBallType.Pass, actors[3].Position, default(SourcePlanar), actors[3].Position, 3);
        var distances = new float[14, 14];
        var angles = new int[14, 14];
        distances[4, 7] = 321f; angles[4, 7] = 123;
        var cached = new SourceAiFrame(actors, Book.Directions, rules, ball, 0, humanTeamMask: 3,
            sourceDistances: distances, sourceAngles: angles);
        Check(cached.UsesSuppliedSourceGeometry, "caller-owned cached original geometry branch");
        Equal(321f, cached.Distance(4, 7), "do not recompute original cached distance from newer actor positions");
        Equal(123, cached.Angle(4, 7), "do not recompute original cached angle from newer actor positions");
        distances[4, 7] = 1f; angles[4, 7] = 2;
        Equal(321f, cached.Distance(4, 7), "cached distance matrix is copied");
        Equal(123, cached.Angle(4, 7), "cached angle matrix is copied");
        Reject<ArgumentException>(() => new SourceAiFrame(actors, Book.Directions, rules, ball, 0,
            sourceDistances: distances), "half-supplied geometry rejected");
        distances[0, 1] = float.NaN;
        Reject<ArgumentOutOfRangeException>(() => new SourceAiFrame(actors, Book.Directions, rules, ball, 0,
            sourceDistances: distances, sourceAngles: angles), "NaN cached source geometry rejected");
        var state = SourceRouteState.Begin(Book.GetRoute("pr_goto_endzone"), -1, SourceDroneFlags.Motion);
        var assignment = SourceDroneAI.AssignCoverage(10, state, 4, state, false, 4, 2);
        Equal(4, assignment.Defender.Target, "drone_cover_assign defender target");
        Equal(10, assignment.Receiver.Target, "drone_cover_assign receiver target");
        Check((assignment.Defender.Flags & SourceDroneFlags.CanCheapShot) != 0, "drone defender heats up against the next fire receiver");
        assignment = SourceDroneAI.AssignCoverage(10, state, 4, state, true, 4, 2);
        Check((assignment.Defender.Flags & SourceDroneFlags.CanCheapShot) == 0, "human defending team does not get drone heating cheap-shot flag");
        var changed = SourceDroneAI.OnPossessionChanged(state, 1, 0, Book);
        Equal("db_rush_qb", changed.Program.Identity, "turnover defense switches to exact original db_rush_qb");
        Equal(state.Flags, changed.Flags, "source possession script reset does not silently reset drone flags");
        Equal("ds_block", SourceDroneAI.OnPossessionChanged(state, 0, 0, Book).Program.Identity, "turnover offense exact ds_block");
        Equal("ds_improvise", SourceDroneAI.AbortRoute(state, Book).Program.Identity, "OOB route abort exact original ds_improvise");
        var program = new SourceRouteProgram("explicit-adapter-controls", "synthetic-not-original-C", 1,
            new[] { 1, -1, -3, 5, -1, 2, 0, 4, 0, 0 });
        var parameters = new SourceAiParameters(Book, 5, 5);
        state = SourceRouteState.Begin(program);
        actors[4] = new SourceAiActor(4, actors[4].Position, default(SourcePlanar), SourceActorMode.ReceiverPreSnap);
        var frame = new SourceAiFrame(actors, Book.Directions, rules, ball, 0, humanTeamMask: 3, inPlay: false);
        var step = SourceDroneAI.StepRoute(state, new SourceRouteInput(frame, 4, parameters,
            new SourceRandomContext(0, 4, "actor_proc", 4, Book.Profile)));
        Equal(0, step.State.WordIndex, "pre-snap receiver does not execute route early");
        actors[4] = new SourceAiActor(4, actors[4].Position, default(SourcePlanar), SourceActorMode.Receiver);
        frame = new SourceAiFrame(actors, Book.Directions, rules, ball, 1, humanTeamMask: 3);
        step = SourceDroneAI.StepRoute(step.State, new SourceRouteInput(frame, 4, parameters,
            new SourceRandomContext(1, 4, "actor_proc", 4, Book.Profile)));
        Equal(7, step.State.WordIndex, "explicit noop/branch reaches original RunYards state branch");
        frame = new SourceAiFrame(actors, Book.Directions, rules, ball, 2, humanTeamMask: 3);
        var endInput = new SourceRouteInput(frame, 4, parameters, new SourceRandomContext(2, 4, "actor_proc", 4, Book.Profile));
        step = SourceDroneAI.StepRoute(step.State, endInput);
        Check(step.State.Ended && step.State.Stick == 24, "explicit adapter End holds, never a universal run-straight fallback");
        Reject<InvalidOperationException>(() => SourceDroneAI.StepRoute(step.State, endInput), "same source tick cannot execute twice");
        var restart = SourceRouteState.Begin(new SourceRouteProgram("restart-loop", "synthetic-not-original-C", 1, new[] { -2 }));
        Reject<InvalidOperationException>(() => SourceDroneAI.StepRoute(restart, endInput), "restart without a yielding source branch is diagnosed");
        var cpu = new SourceCpuPlayInput(rules, 0, 0, 0x00020000, 0, 0);
        SourceCpuPlayDecision pick = SourceMatchFlow.CpuPickPlay(cpu);
        Check(!pick.IsResolved && pick.RandomRequest.IsRequired, "CPU selection uses existing source flow requests");
        var cpuContext = new SourceRandomContext(7, -1, "play_select", 0, Book.Profile);
        SourceRandomSite site = cpuContext.At(pick.RandomRequest.Kind, pick.RandomRequest.BoundOrModulus,
            "SourceMatchFlow." + pick.RandomRequest.Call, pick.RandomRequest.CallIndex);
        var cpuTape = new SourceRecordedRandom(new[] { new SourceRandomDraw(site, 51, 0, provenance) }, provenance);
        int value = SourceRandom.FulfillMatchRequest(cpuTape, pick.RandomRequest, cpuContext).MatchFlowValue;
        pick = SourceMatchFlow.CpuPickPlay(new SourceCpuPlayInput(rules, 0, 0, 0x00020000, 0, 0,
            draws: new SourceRandomDraws(value)));
        Check(pick.IsResolved && Book.ResolveCpuPlay(pick).Identity == Book.GetPool(pick.Family).Entries[pick.PoolIndex].Identity,
            "real CPU request -> explicit draw -> original weighted pool ordinal without another draw or copied branch logic");
    }

    private static int Main(string[] args)
    {
        bool quick = false;
        try
        {
            string catalog = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--work-root": Here = Path.GetFullPath(args[++i]); break;
                    case "--catalog": catalog = Path.GetFullPath(args[++i]); break;
                    case "--oracle": OraclePath = Path.GetFullPath(args[++i]); break;
                    case "--quick": quick = true; break;
                    default: throw new ArgumentException("Unsupported checks option: " + args[i]);
                }
            }
            if (Here == null || catalog == null || OraclePath == null || !Directory.Exists(Here) || !File.Exists(OraclePath))
                throw new ArgumentException("Explicit existing --work-root, --catalog and --oracle are required.");
            Context = "converted catalog load";
            using (TextReader reader = File.OpenText(catalog)) Book = SourcePlaybook.LoadConverted(reader);
            Equal("OriginalRelease57Hz-USE_OLD_QB_FALSE", Book.Profile, "only pinned original profile is supported");
            using (var oracle = new Oracle())
            {
                Catalog(oracle);
                Numeric(oracle);
                if (!quick) Lineups(oracle);
                GuardsAndRng();
                if (!quick) Routes(oracle, 750);
                ReceiverAndBlocking(oracle);
                if (!quick)
                {
                    ActiveAi(oracle);
                    Initialization(oracle);
                    RoleModes(oracle);
                    DefinedReceiverBallBranches(oracle);
                    SwitchTiming(oracle);
                }
            }
            var receipt = new { schema = "source-playbook-checks/v1", status = "passed", passed = true, assertions = Assertions,
                compiled_original_catalog_records = NativeCatalog, original_numeric_cases = NativeNumerics,
                original_source_actor_decision_ticks = NativeSteps, original_receiver_frames = ReceiverCases,
                original_block_assignment_frames = BlockCases, route_programs = Book.Routes.Count, route_side_runs = RouteSidesVisited.Count,
                original_lineup_inputs = LineupCases, original_initialization_inputs = InitCases,
                original_switch_timing_ticks = SwitchTimingTicks,
                original_rng_calls = RandomCalls, command_retained_ticks = RetainedCommands,
                buttons_retained_ticks = RetainedButtons, cross_actor_effects = CrossActorEffects,
                observed_entry_opcodes = OpcodesVisited.OrderBy(x => x).ToArray(), observed_scripts = ScriptsVisited.OrderBy(x => x).ToArray(),
                full_gameplay_parity_claimed = false, scope = quick ? "quick" : "all",
                boundary = "Host-compiled pinned active C functions, synthetic positions/animation feedback/explicit raw input tapes; not original full player physics, Unity native, MIPS executable, target PRNG, qsort equal-key order, scheduler or a complete match." };
            string json = JsonSerializer.Serialize(receipt);
            File.WriteAllText(Path.Combine(Here, "checks-result.json"), json + "\n");
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception e)
        {
            var assertion = e as AssertionFailure;
            var failure = new { schema = "source-playbook-checks/v1", status = "failed", passed = false,
                assertions = Assertions, context = Context, failed_assertion = assertion == null ? null : assertion.Artifact,
                failure_kind = assertion == null ? "error" : "assertion", exception_type = e.GetType().FullName,
                error = e.Message, scope = quick ? "quick" : "all", full_gameplay_parity_claimed = false };
            string json = JsonSerializer.Serialize(failure);
            if (Here != null && Directory.Exists(Here))
            {
                File.WriteAllText(Path.Combine(Here, "checks-result.json"), json + "\n");
                File.WriteAllText(Path.Combine(Here, "checks-failure.txt"), e.ToString());
            }
            Console.WriteLine(json);
            Console.Error.WriteLine(e.ToString());
            return 1;
        }
    }
}
