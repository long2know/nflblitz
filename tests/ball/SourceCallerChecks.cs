using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class SourceCallerChecks
{
    private static int assertions;
    private static string oracleRoot;
    private static int fifoAssertions;
    internal static int AddedFifoAssertions { get { return fifoAssertions; } }
    private static void FifoCheck(bool condition, string identity)
    {
        fifoAssertions++;
        Check(condition, "FIFO compatibility: " + identity);
    }
    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new BallAssertionFailure("SOURCE CALLER ASSERTION: " + identity);
    }

    private static SourceMatchConfiguration Configuration()
    {
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(3, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
    }

    private static PlayerCommand Raw(SourceButtons held, SourceButtons pressed = SourceButtons.None,
        SourceButtons released = SourceButtons.None, SourceCommand command = default(SourceCommand),
        int x = 0, int z = 0)
    {
        return new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(3, x, z, held, pressed, released, command) }));
    }

    internal static int RunFlipOnly(FullKernelInputs real, string originalCases)
    {
        oracleRoot = originalCases;
        var settings = new SourcePresentationSettings(TeamId.Away, new[] { -1, 3, 19, -1 }, -2);
        FlipCases(() => BallTestFactory.Create(real, configuration: Configuration(), presentation: settings));
        return assertions;
    }

    private static PlayerCommand[] PendingPackets(GameSimulation game)
    {
        return ((Queue<PlayerCommand>)typeof(GameSimulation).GetField("pendingSourceActions",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game)).ToArray();
    }

    private static bool SameSourceCommand(SourceCommand first, SourceCommand second)
    {
        return JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
    }

    private static object FifoState(GameSimulation game)
    {
        return new { game.State.Tick, game.SourceTick, game.Model.SourceClockRemainder,
            phase = game.Model.Phase.ToString(), game.Model.SourceOffenseFlipped,
            published = ((ISourceMatchState)game.State).Match.OffenseFlipped,
            actualInput = game.Model.SourceCurrentInput, queue = PendingPackets(game),
            game.SourceEvents, game.C2Events,
            allSharedRawDraws = ((SourceInjectedRandom)game.SourceRandomService).Trace.ToArray() };
    }

    private static bool SamePacket(PlayerCommand actual, PlayerCommand expected)
    {
        return actual.Action == expected.Action && actual.Play == expected.Play &&
            actual.TargetPlayerId == expected.TargetPlayerId && SameSourceCommand(actual.Source, expected.Source);
    }

    private static int DeliverMenuPacket(GameSimulation game, PlayerCommand target, List<object> history,
        string label, bool alreadyQueued = false)
    {
        PlayerCommand[] prior = PendingPackets(game);
        PlayerCommand[] expected = alreadyQueued ? prior : prior.Concat(new[] { target }).ToArray();
        FifoCheck(expected.Length > 0 && SamePacket(expected[expected.Length - 1], target) &&
            (!alreadyQueued || expected.Length == 1), "known menu target is exact and submitted ONCE, never replaced or resubmitted");
        int remainder = game.Model.SourceClockRemainder;
        int bound = (60 * expected.Length - remainder + 56) / 57;
        int delivered = 0;
        for (int host = 0; host < bound; host++)
        {
            PlayerCommand[] queuedBefore = PendingPackets(game);
            FifoCheck(queuedBefore.Length == (host == 0 && !alreadyQueued ? prior.Length : expected.Length - delivered) &&
                queuedBefore.Select((packet, index) => SamePacket(packet, expected[delivered + index])).All(equal => equal),
                "menu queue before host exactly preserves every known predecessor/target tuple");
            long source = game.SourceTick;
            int nextRemainder = remainder + 57;
            bool executes = nextRemainder >= 60;
            game.Step(host == 0 && !alreadyQueued ? target : default(PlayerCommand));
            if (executes)
            {
                FifoCheck(game.SourceTick == source + 1 &&
                    SamePacket(game.Model.SourceCurrentInput, expected[delivered]),
                    "every executed menu wake delivers EXACT oldest predecessor or saved target packet");
                delivered++; nextRemainder -= 60;
            }
            else FifoCheck(game.SourceTick == source,
                "57/60 skipped host retains the exact pending menu packets without fictitious delivery");
            PlayerCommand[] after = PendingPackets(game);
            FifoCheck(game.Model.SourceClockRemainder == nextRemainder &&
                after.Length == expected.Length - delivered &&
                after.Select((packet, index) => SamePacket(packet, expected[delivered + index])).All(equal => equal),
                "menu queue after host exactly preserves order/count/content; no drop/filter/reorder");
            history.Add(new { scope = "NEW-source-synchronized-menu-history", label, host, bound,
                alreadyQueued, targetSubmittedThisHost = host == 0 && !alreadyQueued, delivered,
                expectedThisWake = executes ? (PlayerCommand?)expected[delivered - 1] : null,
                state = FifoState(game) });
            remainder = nextRemainder;
        }
        FifoCheck(delivered == expected.Length && PendingPackets(game).Length == 0 &&
            SamePacket(game.Model.SourceCurrentInput, target),
            "finite queue/clock-derived observation stops EXACTLY at saved target, not desired phase/result polling");
        return bound;
    }

    private static int OneNextSourceWake(GameSimulation game, PlayerCommand command = default(PlayerCommand))
    {
        long before = game.SourceTick;
        game.Step(command);
        int hosts = 1;
        if (game.SourceTick == before) { game.Step(default(PlayerCommand)); hosts++; }
        FifoCheck(game.SourceTick == before + 1, "observe exactly one next actually executed57Hz wake, never retry-until-green");
        return hosts;
    }

    private static void EmptyAndOrderedMenuCompatibility(GameSimulation game, List<object> history)
    {
        foreach (bool flip in new[] { false, true })
        {
            FifoCheck(PendingPackets(game).Length == 0, "empty-queue precondition for each nullable false/true edge");
            var command = new SourceCommand(SourceAction.None, offenseFlipped: flip);
            OneNextSourceWake(game, new PlayerCommand(source: command));
            FifoCheck(SameSourceCommand(game.Model.SourceCurrentInput.Source, command) &&
                PendingPackets(game).Length == 0 && game.Model.Phase == GamePhase.PlaySelect &&
                game.Model.SourceOffenseFlipped == flip && ((ISourceMatchState)game.State).Match.OffenseFlipped == flip,
                "empty-queue specified None flip is delivered on FIRST executed wake for " + flip);
            history.Add(new { scope = "NEW-empty-queue-menu-history", flip, state = FifoState(game) });
        }
        int idleHosts = 0;
        while (game.Model.SourceClockRemainder != 0 && idleHosts < 20)
        { game.Step(default(PlayerCommand)); idleHosts++; }
        FifoCheck(game.Model.SourceClockRemainder == 0 && PendingPackets(game).Length == 0 &&
            game.Model.Phase == GamePhase.PlaySelect,
            "public57/60 idle alignment enqueues no unadorned None packet or starvation");
        long source = game.SourceTick;
        var first = new SourceCommand(SourceAction.None, offenseFlipped: false);
        var second = new SourceCommand(SourceAction.None, offenseFlipped: true);
        game.Step(new PlayerCommand(source: first));
        PlayerCommand[] pending = PendingPackets(game);
        FifoCheck(game.SourceTick == source && pending.Length == 1 && SameSourceCommand(pending[0].Source, first),
            "first distinct menu packet is retained across a genuine skipped host wake");
        game.Step(new PlayerCommand(source: second));
        pending = PendingPackets(game);
        FifoCheck(game.SourceTick == source + 1 && SameSourceCommand(game.Model.SourceCurrentInput.Source, first) &&
            pending.Length == 1 && SameSourceCommand(pending[0].Source, second) &&
            !game.Model.SourceOffenseFlipped && ((ISourceMatchState)game.State).Match.OffenseFlipped == false,
            "two distinct queued menu packets deliver OLDEST first and retain the exact newer packet");
        history.Add(new { scope = "NEW-two-packet-order-history", idleHosts, first, second, state = FifoState(game) });
        OneNextSourceWake(game);
        FifoCheck(SameSourceCommand(game.Model.SourceCurrentInput.Source, second) && PendingPackets(game).Length == 0 &&
            game.Model.SourceOffenseFlipped && ((ISourceMatchState)game.State).Match.OffenseFlipped == true,
            "second distinct menu packet is delivered exactly once on the next executed wake");
        OneNextSourceWake(game);
        FifoCheck(!game.Model.SourceCurrentInput.Source.IsSpecified && PendingPackets(game).Length == 0 &&
            game.Model.SourceOffenseFlipped,
            "unadorned None never starves a menu packet and consumed flip is not redelivered");
        history.Add(new { scope = "NEW-exact-once-no-starvation-history", state = FifoState(game) });
    }

    internal static int Run(FullKernelInputs real, string originalCases)
    {
        oracleRoot = originalCases;
        var settings = new SourcePresentationSettings(TeamId.Away, new[] { -1, 3, 19, -1 }, -2);
        Func<GameSimulation> create = () => BallTestFactory.Create(real, configuration: Configuration(), presentation: settings);
        var game = create();
        var inputs = new List<SourceStationInput>();
        var inputTicks = new List<long>();
        game.SourceStationInputTransform = (snapshot, tick, frame) =>
        {
            Check(((ISourceMatchState)snapshot).SourceTick == tick, "transform receives executing tick snapshot");
            Check(snapshot.Tick == game.Model.Tick, "transform receives current pre-action host snapshot");
            Check(frame.Stations.Count == 1 && frame.Stations[0].StationId == 3, "actual independent station identity");
            inputs.Add(frame.Stations[0]); inputTicks.Add(tick);
            return frame;
        };
        game.Step(Raw(SourceButtons.A | SourceButtons.B | SourceButtons.C,
            SourceButtons.A | SourceButtons.B | SourceButtons.C));
        Check(inputs.Count == 0 && game.SourceTick == 0, "skipped first host tick does not consume/transform edge");
        game.Step(Raw(SourceButtons.None, released: SourceButtons.A | SourceButtons.B | SourceButtons.C, x: -3, z: 2));
        Check(inputs.Count == 1 && inputTicks[0] == 1, "ordered raw drain transforms exactly once on wake");
        Check(inputs[0].Pressed == (SourceButtons.A | SourceButtons.B | SourceButtons.C)
            && inputs[0].Released == inputs[0].Pressed && inputs[0].Held == SourceButtons.None,
            "complete between-wake press/release and simultaneous A/B/C preserved");
        Check(inputs[0].StickX == -3 && inputs[0].StickZ == 2, "latest exact raw49 coordinates survive drain");
        Check(!game.Model.SourceCurrentInput.StationInputs.Stations[0].Command.IsSpecified,
            "raw A/B/C is not collapsed into priority SourceAction");
        Check(!SourcePlayerDriver.ActorCommand(game.Model, game.Model.Players[game.State.ControlledPlayerId],
            game.Model.SourceCurrentInput).Source.IsSpecified, "ActorCommand also preserves raw masks without synthesizing action");
        game.Step(Raw(SourceButtons.None));
        Check(inputs[1].Pressed == SourceButtons.None && inputs[1].Released == SourceButtons.None,
            "latched edges are consumed once rather than replayed");
        Check(((ISourcePresentationState)game.State).Presentation.DesignatedHomeSide == TeamId.Away
            && ((ISourcePresentationState)game.State).Presentation.PupBigBall == -2,
            "actual designated home and entire source pup integer are not derived/defaulted");
        Check(((ISourcePresentationState)game.State).Presentation.StationHeads.SequenceEqual(new[] { -1, 3, 19, -1 }),
            "actual four station heads retain order and absence sentinels");
        Check(game.State.Players.All(p => p.Source.Presentation.IsSpecified
            && p.Source.Presentation.RawPlayerMode == (int)game.Model.Players[p.Id].SourceMode),
            "all fourteen exact raw modes published independently of ActorRole");
        Check(game.State.Ball.Presentation.RawFlags == unchecked((int)game.Model.SourceBallFlags),
            "entire original ball flag word published");
        ActorState flagActor = game.Model.Players[0];
        flagActor.SourceRawFlags = unchecked((int)0x80000404);
        flagActor.Flipped = true; flagActor.SourceHelmetPopped = true;
        int flags = GameSnapshot.SourcePlayerView(flagActor, game.SourceTick).Source.Presentation.RawFlags;
        Check(flags == unchecked((int)0x80800405), "unknown signed raw flag bits survive supported flag overlay");
        flagActor.SourceRawFlags = 0; flagActor.Flipped = false; flagActor.SourceHelmetPopped = false;

        game = create();
        SourceInputFrame cleared = null;
        game.SourceStationInputTransform = (s, t, f) => { cleared = f; return f; };
        game.Step(Raw(SourceButtons.A, SourceButtons.A));
        long beforeClear = game.State.Tick;
        ((IPendingSourceInput)game).ClearPendingInput();
        Check(game.State.Tick == beforeClear && game.SourceTick == 0, "focus clear neither advances nor resets match");
        game.Step(default(PlayerCommand));
        Check(cleared.Stations[0].Pressed == SourceButtons.None && cleared.Stations[0].Held == SourceButtons.None,
            "focus clear removes queued raw edge and held sample before first wake");
        game.Restart();
        game.Step(Raw(SourceButtons.B, SourceButtons.B));
        game.Restart();
        cleared = null;
        game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand));
        Check(cleared.Stations[0].Pressed == SourceButtons.None, "restart removes queued raw edge");

        game = create();
        for (int host = 0; host < 60; host++)
            game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.None)));
        long emptyTick = game.SourceTick;
        game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectKickoff)));
        if (game.SourceTick == emptyTick) game.Step(default(PlayerCommand));
        Check(game.Model.SourceCurrentInput.Source.Action == SourceAction.SelectKickoff,
            "explicit no-op SourceCommand frames do not backlog/starve real source edges");
        game = create();
        for (int host = 0; host < 60; host++)
            game.Step(Raw(SourceButtons.None, command: new SourceCommand(SourceAction.None)));
        emptyTick = game.SourceTick;
        game.Step(Raw(SourceButtons.None, command: new SourceCommand(SourceAction.SelectKickoff)));
        if (game.SourceTick == emptyTick) game.Step(default(PlayerCommand));
        Check(game.Model.SourceCurrentInput.StationInputs.Stations[0].Command.Action == SourceAction.SelectKickoff,
            "explicit no-op station commands do not backlog/starve real source edges");

        game = create();
        var sequence = new List<string>();
        game.SourceStationInputTransform = (s, t, f) => { sequence.Add("input:" + t); return f; };
        game.SourceCameraHeadingProvider = (s, t) =>
        {
            FullKernelInputs.SetControlledMovementCamera(game, s, t);
            sequence.Add("camera:" + t);
            Check(game.SourceCameraStageOrder >= 0, "camera exposes reserved actual execution ordinal");
            Check(((ISourceMatchState)s).SourceTick == t, "camera receives executing pre-actor snapshot");
            Check(game.Model.Players.All(a => a.AnimationState.Primary.SourceTick == 0),
                "camera really executes before any actor VM wakes on tick1");
            game.DrawSourceCameraRandom(3, "cambot.c:camera_shaker/fixture-x");
            game.DrawSourceCameraRandom(3, "cambot.c:camera_shaker/fixture-z");
            return 777;
        };
        game.Step(Raw(SourceButtons.None)); game.Step(Raw(SourceButtons.None));
        Check(sequence.SequenceEqual(new[] { "input:1", "camera:1" }), "game input precedes one source camera stage");
        Check(game.SourceCameraHeading == 777 && game.SourceCameraHeadingSampleTick == 1,
            "same executing camera heading is used by actor/AI stage, not render default");
        var trace = ((SourceInjectedRandom)game.SourceRandomService).Trace;
        SourceRandomDraw[] cameraDraws = trace.Where(x => x.Site.Process == "cambot_proc").ToArray();
        Check(cameraDraws.Length == 2 && cameraDraws[0].Site.SourceTick == 1
            && cameraDraws[0].Site.CallIndex == 0 && cameraDraws[1].Site.CallIndex == 1,
            "camera shaker uses shared ordered source RNG context");
        Check(trace.SkipWhile(x => x.Site.Process != "cambot_proc").Skip(2)
            .All(x => x.Site.ProcessOrder > cameraDraws[1].Site.ProcessOrder),
            "all later actor RNG contexts follow executing camera draws");
        bool rejected = false;
        try { game.DrawSourceCameraRandom(3, "outside"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "render/outside-camera RNG draw is rejected");
        game.SourceCameraHeadingProvider = (s, t) =>
        {
            FullKernelInputs.SetControlledMovementCamera(game, s, t);
            return 1024;
        };
        rejected = false;
        try { game.Step(default(PlayerCommand)); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid camera heading rejected instead of defaulted");

        int gateCases = GateCases(real);
        int receiverCases = ReceiverCases();
        int vmCases = VmCases(real);
        File.WriteAllText(Path.Combine(oracleRoot, "actual-caller-before-public-flow.json"),
            JsonSerializer.Serialize(new { status = "partial-not-complete-caller",
                gateCases, receiverCases, vmCases, assertions }));
        FlipCases(create);
        RawSnapAndPass(real);
        Check(gateCases == 800 && receiverCases == 12544 && vmCases == 4, "complete original caller inventory");
        return assertions;
    }

    private static int ReceiverCases()
    {
        int cases = 0, explicitTies = 0;
        foreach (string line in File.ReadLines(Path.Combine(oracleRoot, "caller-receiver-oracle.tsv")))
        {
            string[] t = line.Split('\t');
            int side = int.Parse(t[0]), stick = int.Parse(t[1]), quarterback = side * 7 + 3;
            var model = new SimulationState { Possession = (TeamId)side };
            ActorState actor = model.Players[quarterback];
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(0f, 0f,
                float.Parse(t[2], CultureInfo.InvariantCulture)));
            for (int slot = 0; slot < 3; slot++)
            {
                int offset = 3 + 5 * slot;
                ActorState target = model.Players[quarterback + 1 + slot];
                SourcePlayerDriver.SetSourcePosition(target, new SourceVector3(
                    float.Parse(t[offset], CultureInfo.InvariantCulture), 0f,
                    float.Parse(t[offset + 1], CultureInfo.InvariantCulture)));
                target.SourceMode = (SourceActorMode)int.Parse(t[offset + 2]);
                target.SourceJob = (SourceActorJob)int.Parse(t[offset + 3]);
                target.SourceControllerStation = int.Parse(t[offset + 4]);
            }
            try
            {
                int selected = SourcePassDriver.PickReceiver(model, actor, stick);
                Check(selected == int.Parse(t[18]), "actual C pick_receiver/target_cmp result " + cases);
            }
            catch (NotSupportedException e)
            {
                Check(t[19] == "1" && e.Message.Contains("qsort"),
                    "only actual source equal-key qsort ambiguity fails closed " + cases);
                explicitTies++;
            }
            cases++;
        }
        Check(explicitTies > 0 && explicitTies < cases, "target libc qsort equality remains explicit, not arbitrary actor fallback");
        return cases;
    }

    private static int GateCases(FullKernelInputs real)
    {
        int cases = 0;
        MethodInfo drawMethod = typeof(GameSimulation).GetMethod("SourceDraw", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null);
        foreach (string line in File.ReadLines(Path.Combine(oracleRoot, "caller-gate-oracle.tsv")))
        {
            int[] t = line.Split('\t').Select(int.Parse).ToArray();
            var provenance = new SourceRandomProvenance("source-caller-oracle", "injected original-C recorded inputs",
                "deterministic raw roll=" + t[3], SourceRandomEvidence.ExternalUnverified);
            var random = new SourceInjectedRandom(site => site.BoundOrModulus == 100 ? t[3] : 37, provenance);
            var game = BallTestFactory.Create(real, configuration: Configuration(), randomFactory: seed => random);
            game.Model.Phase = t[0] != 0 ? GamePhase.LiveBall : GamePhase.DeadBall;
            game.Model.CarrierId = t[2] != 0 ? 4 : 3;
            ActorState actor = game.Model.Players[4];
            actor.SourceHelmetPopped = t[1] != 0;
            SourceContactDriver.RecordCollision(actor, game.Model.Players[7]);
            var observed = new List<SourceAnimationInstruction>();
            Action<SourceAnimationLayerState, ActorState, SourceAnimationInstruction, int> production =
                actor.AnimationState.Primary.Presentation;
            actor.AnimationState.Primary.Presentation = (layer, value, instruction, order) =>
            { observed.Add(instruction); production(layer, value, instruction, order); };
            int start = random.Trace.Count;
            var draw = (Func<int, int>)drawMethod.CreateDelegate(typeof(Func<int, int>), game);
            actor.AnimationState.FullBody("fall_04", actor, game.SourceTick, draw, AnimationSelectionEvidence.SourceVerified);
            typeof(GameSimulation).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
            SourceCameraCue[] actual = game.PresentationCues.Cues.OfType<SourceCameraCue>().ToArray();
            SourceRandomDraw[] used = random.Trace.Skip(start).ToArray();
            bool selected = observed.Any(x => x.Op == SourceAnimationOp.CameraCue);
            Check(selected == (t[4] != 0), "actual original C plyr_have_ball gate " + cases);
            Check(actual.Length == t[4], "actual typed camera DTO count matches executed site " + cases);
            Check(used.Count(x => x.Site.BoundOrModulus == 100) == t[5],
                "predicate short-circuit original randrng(100) count " + cases);
            Check(used.Count(x => x.Site.BoundOrModulus == 1) == t[4],
                "original singleton select_csi_item still consumes randrng(1) " + cases);
            if (selected)
            {
                SourceRandomDraw selection = used.Last();
                Check(selection.Site.BoundOrModulus == 1 && selection.Value == 0 && selection.Raw.Value == 37,
                    "real shared service selection raw+ordinal " + cases);
                Check(actual[0].Selection.SelectedOrdinal == 0 && actual[0].Selection.EntryCount == 1
                    && actual[0].Selection.RawRandomValue == selection.Raw && actual[0].Selection.RandomSiteContext == selection.Site.ToString()
                    && actual[0].CarrierPlayerId == game.Model.CarrierId,
                    "published typed camera cue preserves real selected raw, site and actual carrier");
            }
            Check(observed[0].Text == "get_hit_snd" && observed[0].SourceLine == 15955,
                "initial change_anim executes sound before NEWSEQ/camera");
            Check(actor.AnimationState.Primary.SequenceName == "FALL_04",
                "camera execution remains after actual NEWSEQ rather than replacing pose");
            cases++;
        }
        return cases;
    }

    private static int VmCases(FullKernelInputs real)
    {
        int count = 0;
        foreach (string script in new[] { "fall_04", "h_front1_anim", "t_dshoul_anim", "c_dive_for_ball_anim" })
        {
            var game = BallTestFactory.Create(real, configuration: Configuration());
            game.Model.Phase = GamePhase.DeadBall;
            game.Model.CarrierId = script == "h_front1_anim" ? 3 : 4;
            ActorState actor = game.Model.Players[4];
            actor.SourceHelmetPopped = false;
            SourceContactDriver.RecordCollision(actor, game.Model.Players[7]);
            var observed = new List<Tuple<long, int, string, int, string, int, int, Tuple<int>>>();
            Action<SourceAnimationLayerState, ActorState, SourceAnimationInstruction, int> production =
                actor.AnimationState.Primary.Presentation;
            actor.AnimationState.Primary.Presentation = (layer, value, instruction, order) =>
            {
                observed.Add(Tuple.Create(layer.SourceTick, order, layer.ScriptSymbol, instruction.SourceLine,
                    instruction.Text, instruction.A, instruction.B, instruction.C));
                production(layer, value, instruction, order);
            };
            var method = typeof(GameSimulation).GetMethod("SourceDraw", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null);
            var draw = (Func<int, int>)method.CreateDelegate(typeof(Func<int, int>), game);
            var physics = (Action<ActorState>)typeof(SourcePlayerDriver).GetMethod("Physics",
                BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Action<ActorState>));
            actor.AnimationState.FullBody(script, actor, 0, draw, AnimationSelectionEvidence.SourceVerified);
            for (int tick = 1; tick <= 400; tick++)
            {
                game.Model.SourceTick = tick;
                SourceAnimationSignal signals = actor.AnimationState.Advance(actor, tick, draw);
                physics(actor);
                if ((signals & SourceAnimationSignal.GeneralRun) != 0) break;
            }
            for (int i = 1; i < observed.Count; i++)
                Check(observed[i].Item1 > observed[i - 1].Item1
                    || observed[i].Item1 == observed[i - 1].Item1 && observed[i].Item2 > observed[i - 1].Item2,
                    "actual VM instruction order " + script);
            typeof(GameSimulation).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
            SourcePresentationCue[] published = game.PresentationCues.Cues.ToArray();
            Check(published.Length == observed.Count, "every actually executed presentation instruction is detached/published " + script);
            if (script == "fall_04")
            {
                int down = observed.FindIndex(x => x.Item4 == 15995);
                Check(down >= 0 && observed[down].Item5 == "player_down", "actual fall04 PlayerDown callback observed");
                Check(observed[down + 1].Item4 == 15998 && observed[down + 1].Item5 == "grnd_hit_snd"
                    && observed[down + 1].Item6 == -1 && observed[down + 1].Item7 == 0 && observed[down + 1].Rest.Item1 == -1,
                    "actual ground sound follows PlayerDown with all signed arguments");
                Check(observed[down + 2].Item4 == 16000 && observed[down + 2].Item6 == 2
                    && observed[down + 2].Item7 == 10, "actual shaker(2,10) follows ground sound");
            }
            if (script == "h_front1_anim")
            {
                Check(observed.Any(x => x.Item4 == 5560 && x.Item6 == 3 && x.Item7 == 10),
                    "noncarrier H_FRONT1 shaker opcode is not filtered");
                SourceShakerCue shaker = published.OfType<SourceShakerCue>().Single(x => x.Context.SourceLine == 5560);
                Check(shaker.CarrierPlayerId == 3 && shaker.Context.ActorPlayerId == 4
                    && shaker.Amplitude == 3 && shaker.TotalTicks == 10,
                    "actual typed NONCARRIER shaker is forwarded with pre-guard amplitude/total and actual carrier");
            }
            if (script == "t_dshoul_anim")
                Check(observed.Any(x => x.Item4 == 2448 && x.Item6 == 57),
                    "actual pinned sndcalls.equ P_DIVEHIT_SND=57, not placeholder");
            if (script == "c_dive_for_ball_anim")
                Check(observed.Any(x => x.Item4 == 13875) && observed.Any(x => x.Item4 == 13877),
                    "actual dive ground sound then dust emitted after real AA_TOGROUND");
            count++;
        }
        return count;
    }

    private static void FlipCases(Func<GameSimulation> create)
    {
        GameSimulation normal = create(), flipped = create();
        var lineups = new Dictionary<GameSimulation, SourceVector3[]>();
        foreach (GameSimulation game in new[] { normal, flipped })
        {
            var previousInstruction = new Dictionary<int, object>();
            bool invalidHeadingSaved = false;
            foreach (ActorState actor in game.Model.Players)
                foreach (SourceAnimationLayerState layer in new[] { actor.AnimationState.Primary, actor.AnimationState.Secondary })
                {
                    SourceAnimationLayerState observedLayer = layer;
                    Action<ActorState> actual = observedLayer.BeforeInstruction;
                    observedLayer.BeforeInstruction = executing =>
                    {
                        actual(executing);
                        if (!invalidHeadingSaved && (executing.Facing < 0 || executing.Facing > 1023))
                        {
                            previousInstruction.TryGetValue(executing.Id, out object previous);
                            File.WriteAllText(Path.Combine(oracleRoot, "first-invalid-heading-producer.json"),
                                JsonSerializer.Serialize(new {
                                    status = "observed-invalid-AI-boundary-heading-not-normalized",
                                    sourceTick = game.SourceTick, executing.Id, executing.Facing, executing.TargetFacing,
                                    previousActualInstruction = previous, observedLayer.ScriptSymbol,
                                    observedLayer.SequenceName, observedLayer.FrameOrdinal,
                                    stateMutation = false, RNGDrawsAdded = 0
                                }, new JsonSerializerOptions { WriteIndented = true }));
                            invalidHeadingSaved = true;
                        }
                        var program = (SourceAnimationProgram)typeof(SourceAnimationLayerState).GetField("program",
                            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observedLayer);
                        int pc = (int)typeof(SourceAnimationLayerState).GetField("pc",
                            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observedLayer) - 1;
                        SourceAnimationInstruction instruction = program.Instructions[pc];
                        previousInstruction[executing.Id] = new {
                            sourceTick = game.SourceTick, observedLayer.ScriptSymbol, pc,
                            op = instruction.Op.ToString(), instruction.A, instruction.SourceLine,
                            facingBefore = executing.Facing, targetFacingBefore = executing.TargetFacing
                        };
                    };
                }
            for (int tick = 0; game.State.Phase != GamePhase.PlaySelect && tick < 5000; tick++)
            {
                var priorActors = game.Model.Players.Select(actor => new {
                    actor.Id, actor.Facing, actor.TargetFacing, actor.SourceMode,
                    actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                    actor.AnimationState.Primary.FrameOrdinal
                }).ToArray();
                var priorContact = game.ContactEvents.ToArray();
                long priorSourceTick = game.SourceTick;
                PlayerCommand command = game.State.Phase == GamePhase.DeadBall
                    ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand);
                try { game.Step(command); }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(oracleRoot, "first-public-caller-failure.json"),
                        JsonSerializer.Serialize(new {
                            schema = "source-contact-B1-public-caller-failure/v1", status = "failed",
                            scenario = "FlipCases/unfiltered-public-kickoff-before-playselect",
                            host = tick, sourceTick = game.SourceTick, priorSourceTick,
                            phase = game.Model.Phase.ToString(), exception = error.ToString(), command,
                            game.Model.SourceAnimationFailure, game.Model.SourceRandomProcess,
                            game.Model.SourceRandomActor, game.Model.SourceRandomProcessOrder, game.Model.SourceRandomCallIndex,
                            priorActors, priorContact,
                            actors = game.Model.Players.Select(actor => new {
                                actor.Id, actor.Facing, actor.TargetFacing, actor.SourceMode,
                                actor.SourceHitPartnerId, actor.SourcePuppetLinkId, actor.SourcePosition, actor.SourceRoot,
                                actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                                actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId,
                                next_program_counter = typeof(SourceAnimationLayerState).GetField("pc",
                                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.AnimationState.Primary)
                            }).ToArray(),
                            allSharedRawDraws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Select(draw => new {
                                draw.Order, draw.Raw, draw.Value, draw.Site.SourceTick, draw.Site.Process,
                                draw.Site.ActorId, draw.Site.ProcessOrder, draw.Site.CallIndex,
                                draw.Site.SourceIdentity, draw.Site.BoundOrModulus
                            }).ToArray(),
                            stateMutation = false, targetPrngOrWholeMatchClaimed = false
                        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                    throw;
                }
            }
            Check(game.State.Phase == GamePhase.PlaySelect, "public actual kickoff flow reaches source play selection");
        }
        long flipTick = flipped.SourceTick;
        PlayerCommand[] pendingBeforeFlip = PendingPackets(flipped);
        object beforeFlipOnly = new {
            flipped.State.Tick, flipped.SourceTick, phase = flipped.Model.Phase.ToString(),
            flipped.Model.SourceMatchPhase, flipped.Model.SourceOffenseFlipped,
            published = ((ISourceMatchState)flipped.State).Match,
            flipped.Model.SourceRules, flipped.C2State, flipped.Model.SourceCurrentInput
        };
        flipped.Step(new PlayerCommand(source: new SourceCommand(SourceAction.None, offenseFlipped: true)));
        if (flipped.SourceTick == flipTick) flipped.Step(default(PlayerCommand));
        if (!flipped.SourceC2Enabled && ((ISourceMatchState)flipped.State).Match.OffenseFlipped != true)
            File.WriteAllText(Path.Combine(oracleRoot, "first-flip-only-command-failure.json"),
                JsonSerializer.Serialize(new {
                    schema = "source-current281-B1-flip-only-failure/v1",
                    failedAssertion = "specified None flip-only command is a real retained menu edge",
                    beforeFlipOnly, flipTick,
                    unchangedSubmittedCommand = new PlayerCommand(source: new SourceCommand(SourceAction.None, offenseFlipped: true)),
                    after = new {
                        flipped.State.Tick, flipped.SourceTick, phase = flipped.Model.Phase.ToString(),
                        flipped.Model.SourceMatchPhase, flipped.Model.SourceOffenseFlipped,
                        published = ((ISourceMatchState)flipped.State).Match,
                        flipped.Model.SourceRules, flipped.C2State, flipped.Model.SourceCurrentInput,
                        flipped.Model.SourceRandomProcess, flipped.Model.SourceRandomActor,
                        flipped.Model.SourceRandomProcessOrder, flipped.Model.SourceRandomCallIndex,
                        flipped.SourceEvents, flipped.ContactEvents, flipped.C2Events,
                        allSharedRawDraws = ((SourceInjectedRandom)flipped.SourceRandomService).Trace
                    },
                    traceOnly = true, commandAssertionOrVectorChanged = false
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        if (flipped.SourceC2Enabled)
        {
            var savedFlip = new SourceCommand(SourceAction.None, offenseFlipped: true);
            PlayerCommand[] queue = PendingPackets(flipped);
            FifoCheck(flipTick == 438 && pendingBeforeFlip.Length == 1 &&
                pendingBeforeFlip[0].Action == InputAction.Continue && !pendingBeforeFlip[0].Source.IsSpecified &&
                flipped.SourceTick == 439 && flipped.Model.SourceCurrentInput.Action == InputAction.Continue &&
                !flipped.Model.SourceCurrentInput.Source.IsSpecified && flipped.Model.Phase == GamePhase.PlaySelect &&
                !flipped.Model.SourceOffenseFlipped && ((ISourceMatchState)flipped.State).Match.OffenseFlipped == false &&
                queue.Length == 1 && SameSourceCommand(queue[0].Source, savedFlip),
                "canonical source439 delivers older Continue while exact specified flip remains intact FIFO head");
            SourceRandomDraw[] prefix = ((SourceInjectedRandom)flipped.SourceRandomService).Trace.ToArray();
            string rows = string.Join("\n", prefix.Select(draw => string.Join("|", new[] {
                draw.Order.ToString(), draw.Raw.ToString(), draw.Value.ToString(), draw.Site.SourceTick.ToString(),
                draw.Site.Process, draw.Site.ActorId.ToString(), draw.Site.ProcessOrder.ToString(),
                draw.Site.CallIndex.ToString(), draw.Site.SourceIdentity, draw.Site.BoundOrModulus.ToString() })));
            string prefixSha;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                prefixSha = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rows))).Replace("-", "").ToLowerInvariant();
            FifoCheck(prefix.Length == 134 &&
                prefixSha == "913231dc9d3f40ce902a05a19619a9d5b2872bd7ee563f35d70810ac2f00a976",
                "unchanged old134 raw/site/order prefix precedes the explicitly NEW drain-observation extension");
            var history = new List<object> { new { scope = "UNCHANGED-134-draw-prefix-at439", state = FifoState(flipped) } };
            int drainHosts = DeliverMenuPacket(flipped, new PlayerCommand(source: savedFlip), history,
                "known-head-flip-after-unchanged134-prefix", alreadyQueued: true);
            FifoCheck(flipped.SourceTick == 440 && SameSourceCommand(flipped.Model.SourceCurrentInput.Source, savedFlip) &&
                PendingPackets(flipped).Length == 0 && flipped.Model.Phase == GamePhase.PlaySelect &&
                flipped.Model.SourceOffenseFlipped && ((ISourceMatchState)flipped.State).Match.OffenseFlipped == true,
                "NEW known-head drain observes actual source440 exact flip/empty queue/model AND published true");
            history.Add(new { scope = "NEW-one-known-head-drain-NOT-original-vector", drainHosts, state = FifoState(flipped) });
            EmptyAndOrderedMenuCompatibility(flipped, history);
            File.WriteAllText(Path.Combine(oracleRoot, "flip-source-synchronized-compatibility.json"),
                JsonSerializer.Serialize(new {
                    schema = "source-current281-B1-FIFO-compatibility/v1",
                    originalPrefixDraws = 134, prefixSha, originalSource439 = beforeFlipOnly, actual440 = history[1],
                    history, addedFifoAssertions = fifoAssertions, separatelyExtendedCompatibilityHistory = true,
                    originalCompleteVectorOrTargetSchedulerClaimed = false, queueOrRngStateMutation = false
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        Check(((ISourceMatchState)flipped.State).Match.OffenseFlipped == true,
            "specified None flip-only command is a real retained menu edge");
        Action<GameSimulation, bool> pick = (game, flip) =>
        {
            game.SourceCameraHeadingProvider = (state, tick) =>
            {
                FullKernelInputs.SetControlledMovementCamera(game, state, tick);
                if (state.Phase == GamePhase.PreSnap)
                    lineups[game] = Enumerable.Range(0, 14).Select(game.SourceActorWorldPosition).ToArray();
                return game.SourceCameraHeading;
            };
            var command = new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                new SourcePlaySelection("play_spider", SourcePlaybookKind.Offense), offenseFlipped: flip));
            if (game.SourceC2Enabled)
            {
                var delivery = new List<object>();
                DeliverMenuPacket(game, command, delivery, "saved-offense-play-spider");
                var defense = new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                    new SourcePlaySelection("play_safe_cover", SourcePlaybookKind.Defense)));
                DeliverMenuPacket(game, defense, delivery, "saved-defense-play-safe-cover");
                File.WriteAllText(Path.Combine(oracleRoot, flip ? "flipped-menu-delivery.json" : "normal-menu-delivery.json"),
                    JsonSerializer.Serialize(new {
                        schema = "source-current281-menu-delivery/v1", flip,
                        NEW_sourceSynchronizedCompatibilityHistory = true, unchangedCompleteOriginalVector = false,
                        noQueueStateRngMutationOrPhasePolling = true, delivery, final = FifoState(game)
                    }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                return;
            }
            long source = game.SourceTick;
            game.Step(command);
            if (game.SourceTick == source) game.Step(default(PlayerCommand));
            if (game.State.Phase == GamePhase.PlaySelect)
            {
                source = game.SourceTick;
                game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                    new SourcePlaySelection("play_safe_cover", SourcePlaybookKind.Defense))));
                if (game.SourceTick == source) game.Step(default(PlayerCommand));
            }
        };
        pick(normal, false); pick(flipped, true);
        Check(((ISourceMatchState)flipped.State).Match.OffenseFlipped == true
            && ((ISourceMatchState)normal.State).Match.OffenseFlipped == false,
            "nullable command becomes actual published source off_flip");
        if (normal.State.Phase != GamePhase.PreSnap || flipped.State.Phase != GamePhase.PreSnap)
            File.WriteAllText(Path.Combine(oracleRoot, "first-post-FIFO-lineup-failure.json"),
                JsonSerializer.Serialize(new {
                    schema = "source-current281-B1-post-FIFO-lineup-failure/v1",
                    failedAssertion = "source original play selection drives actual lineups",
                    assertions, fifoAssertions, normal = FifoState(normal), flipped = FifoState(flipped),
                    originalPickCodeAndRemainingMirroredAssertionsUnchanged = true,
                    noFurtherWakeOrQueueAdaptation = true
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Check(normal.State.Phase == GamePhase.PreSnap && flipped.State.Phase == GamePhase.PreSnap,
            "source original play selection drives actual lineups " + normal.State.Phase + "/" + flipped.State.Phase
                + " kind=" + normal.Model.SourceRules.PlayType);
        SourceFormationLayout expectedNormal = SourcePlaybook.LineUp(normal.Model.SourceOffensePlay,
            normal.Model.SourceDefensePlay, new SourceLineupInput((int)normal.Model.Possession,
                normal.Model.SourceRules.LineOfScrimmage,
                normal.Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard, false));
        SourceFormationLayout expectedFlipped = SourcePlaybook.LineUp(flipped.Model.SourceOffensePlay,
            flipped.Model.SourceDefensePlay, new SourceLineupInput((int)flipped.Model.Possession,
                flipped.Model.SourceRules.LineOfScrimmage,
                flipped.Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard, true));
        int changed = 0;
        for (int slot = 0; slot < 7; slot++)
        {
            int actor = normal.Model.OffenseOffset + slot;
            float a = lineups[normal][actor].Z, b = lineups[flipped][actor].Z;
            Check(a == expectedNormal.Actors[actor].Position.Z && b == expectedFlipped.Actors[actor].Position.Z,
                "off_flip passes exact source hash/offset binary32 order into actual formation " + actor);
            if (a != b) changed++;
        }
        Check(changed > 0, "off_flip really changes source roots rather than only the renderer");
        SourcePresentationCueBatch old = flipped.PresentationCues;
        int oldCount = old.Cues.Count;
        flipped.Step(default(PlayerCommand));
        Check(old.Cues.Count == oldCount && !ReferenceEquals(old, flipped.PresentationCues),
            "published source presentation batches are detached per host step");
    }

    private static void RawSnapAndPass(FullKernelInputs real)
    {
        SourceMatchConfiguration original = Configuration();
        var config = new SourceMatchConfiguration(original.Profile, original.StationLayout,
            original.HomeTeam, original.AwayTeam, original.Stations, TeamId.Away, original.Captain);
        var game = BallTestFactory.Create(real, configuration: config);
        var noTap = BallTestFactory.Create(real, configuration: config);
        Action<PlayerCommand> pairedStep = command => { game.Step(command); noTap.Step(command); };
        Action<PlayerCommand> pairedWake = command =>
        {
            long source = game.SourceTick;
            do { pairedStep(command); } while (game.SourceTick == source);
        };
        for (int host = 0; game.State.Phase != GamePhase.PlaySelect && host < 5000; host++)
            pairedStep(game.State.Phase == GamePhase.DeadBall ? new PlayerCommand(action: InputAction.Continue)
                : new PlayerCommand(new FieldVector(1f, 0f), true));
        Check(game.State.Phase == GamePhase.PlaySelect && game.State.Possession == TeamId.Home,
            "actual away kickoff/public home return reaches human offensive selection");
        long tick = game.SourceTick;
        pairedStep(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_spider", SourcePlaybookKind.Offense))));
        if (game.SourceTick == tick) pairedStep(default(PlayerCommand));
        Check(game.State.Phase == GamePhase.PreSnap, "real original human QB lineup reached");
        int quarterback = game.Model.OffenseOffset + 3;
        ActorState qb = game.Model.Players[quarterback];
        pairedWake(Raw(SourceButtons.B, SourceButtons.B));
        Check(qb.SourceButtons == SourceDroneButtons.B && qb.SourceOldButtons == SourceDroneButtons.None
            && qb.SourceNewButtons == SourceDroneButtons.B,
            "active M1 first held B sample produces original current/old/fresh buttons");
        Check(qb.SourceButtonHistory[0] == SourceDroneButtons.None,
            "active M1 button history captures physical previous sample before acquisition");
        Check(game.State.Phase == GamePhase.PreSnap && !game.SourceEvents.Any(e => e.Type == SourceEventType.Snapped),
            "fresh held B cannot bypass original pre-snap grace window");
        pairedWake(Raw(SourceButtons.B));
        Check(qb.SourceButtons == SourceDroneButtons.B && qb.SourceOldButtons == SourceDroneButtons.B
            && qb.SourceNewButtons == SourceDroneButtons.None && qb.SourceButtonHistory[0] == SourceDroneButtons.B,
            "active M1 repeated held B retains history without synthesizing a fresh press");
        pairedWake(Raw(SourceButtons.None, released: SourceButtons.B));
        Check(qb.SourceButtons == SourceDroneButtons.None && qb.SourceOldButtons == SourceDroneButtons.B
            && qb.SourceNewButtons == SourceDroneButtons.None && qb.SourceReleasedButtons == SourceDroneButtons.B,
            "active M1 physical B release is not a new press or synthesized held pulse");
        for (int host = 0; host < 100 && (game.Model.PreSnapTicksRemaining >=
            SourceClock.DefaultAutoHikeTicks - SourceClock.SourceTicksPerSecond
            || game.Model.SourceClockRemainder != 0); host++)
            pairedStep(Raw(SourceButtons.None));
        Check(game.State.Phase == GamePhase.PreSnap && game.Model.SourceClockRemainder == 0,
            "raw tap is deliberately placed across a skipped source wake after actual one-second gate");
        int beforeHike = game.Model.PreSnapTicksRemaining;
        Check(beforeHike > SourceClock.SourceTicksPerSecond
            && beforeHike < SourceClock.DefaultAutoHikeTicks - SourceClock.SourceTicksPerSecond,
            "manual tap fixture has passed grace but remains more than one second from automatic hike");
        Check(noTap.State.Phase == GamePhase.PreSnap && noTap.SourceTick == game.SourceTick
            && noTap.Model.PreSnapTicksRemaining == beforeHike,
            "paired public no-tap control reaches identical pre-snap source time and hike deadline");
        tick = game.SourceTick;
        game.Step(Raw(SourceButtons.B, SourceButtons.B));
        noTap.Step(Raw(SourceButtons.None));
        Check(game.SourceTick == tick, "raw snap press stays queued on skipped host tick");
        Check(noTap.SourceTick == tick && noTap.State.Phase == GamePhase.PreSnap,
            "no-tap control also skips the same host wake without consuming hike time");
        game.Step(Raw(SourceButtons.None, released: SourceButtons.B));
        noTap.Step(Raw(SourceButtons.None));
        Check((qb.SourceNewButtons & SourceDroneButtons.B) != 0 && qb.SourceButtons == SourceDroneButtons.None,
            "active M1 post-BeginTick latched fresh B survives complete between-wake tap with held zero");
        Check(qb.SourceOldButtons == SourceDroneButtons.None && qb.SourceButtonHistory[0] == SourceDroneButtons.None,
            "active M1 latched tap does not rewrite physical held-button history");
        Check(game.State.Phase == GamePhase.LiveBall, "complete B press/release actually snaps despite Held=0");
        SourceGameEvent[] snaps = game.SourceEvents.Where(e => e.Type == SourceEventType.Snapped).ToArray();
        Check(snaps.Length == 1 && snaps[0].SourceTick == tick + 1 && game.SourceTick == tick + 1
            && game.Model.SourceHikeCounterAtSnap == beforeHike - 1 && game.Model.SourceHikeCounterAtSnap > 0,
            "fresh raw B causes exactly one manual Snapped event on the first executed source wake, not auto-hike");
        Check(noTap.SourceTick == tick + 1 && noTap.State.Phase == GamePhase.PreSnap
            && noTap.Model.PreSnapTicksRemaining == beforeHike - 1
            && !noTap.SourceEvents.Any(e => e.Type == SourceEventType.Snapped),
            "negative public no-tap control stays pre-snap at identical source wake and positive countdown");
        File.WriteAllText(Path.Combine(oracleRoot, "raw-B-input-causality.json"),
            JsonSerializer.Serialize(new {
                schema = "actual-M1-public-fresh-press-causality/v1",
                state_mutation_used = false, station = 3, source_before = tick, source_after = game.SourceTick,
                countdown_before = beforeHike, actual_manual_hike_counter = game.Model.SourceHikeCounterAtSnap,
                actual_new_buttons = (int)qb.SourceNewButtons, actual_held_buttons = (int)qb.SourceButtons,
                actual_old_buttons = (int)qb.SourceOldButtons,
                actual_physical_history0 = (int)qb.SourceButtonHistory[0],
                actual_snap_source_ticks = snaps.Select(e => e.SourceTick).ToArray(),
                no_tap_source_tick = noTap.SourceTick, no_tap_phase = noTap.State.Phase.ToString(),
                no_tap_countdown = noTap.Model.PreSnapTicksRemaining,
                no_tap_snap_events = noTap.SourceEvents.Count(e => e.Type == SourceEventType.Snapped),
                original_player_blob = "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c"
            }, new JsonSerializerOptions { WriteIndented = true }));
        bool checkedConsumedPress = false;
        for (int host = 0; game.Model.CarrierId != quarterback && host < 100; host++)
        {
            long beforeSource = game.SourceTick;
            game.Step(Raw(SourceButtons.None));
            if (!checkedConsumedPress && game.SourceTick > beforeSource)
            {
                Check(qb.SourceNewButtons == SourceDroneButtons.None && qb.SourceButtons == SourceDroneButtons.None,
                    "active M1 latched fresh B is consumed once on the next executed neutral source wake");
                Check(!game.SourceEvents.Any(e => e.Type == SourceEventType.Snapped),
                    "consumed raw snap edge does not replay a second source Snapped event");
                checkedConsumedPress = true;
            }
        }
        Check(checkedConsumedPress, "actual post-snap flow observes a later source wake before QB catch");
        Check(game.Model.CarrierId == quarterback, "actual center wrist snap and QB catch, not state mutation");
        // Request the pass immediately after the real QB catch; M1 contact can legitimately interrupt a delayed throw.
        game.Step(Raw(SourceButtons.B, SourceButtons.B));
        game.Step(Raw(SourceButtons.None, released: SourceButtons.B));
        Check(game.Model.Players[quarterback].PendingThrowTarget >= 0,
            "complete raw B tap requests actual human QB pass without priority SourceAction conversion");
        bool released = false;
        var releaseTrace = new List<object>();
        for (int host = 0; host < 100 && !released; host++)
        {
            game.Step(Raw(SourceButtons.None));
            ActorState actor = game.Model.Players[quarterback];
            releaseTrace.Add(new { host = game.State.Tick, source = game.SourceTick, game.State.Phase,
                actor.PendingThrowTarget, actor.SourceMode, actor.Action, actor.SourceButtons,
                lower = actor.AnimationState.Primary.ScriptSymbol, lowerFrame = actor.AnimationState.Primary.FrameOrdinal,
                upper = actor.AnimationState.Secondary.ScriptSymbol, upperFrame = actor.AnimationState.Secondary.FrameOrdinal,
                actor.AnimationState.TwoPart, actor.AnimationState.Busy, game.Model.CarrierId,
                events = game.SourceEvents.Select(e => e.Type.ToString()).ToArray() });
            released |= game.SourceEvents.Any(e => e.Type == SourceEventType.PassReleased || e.Type == SourceEventType.LateralReleased);
        }
        if (!released)
            File.WriteAllText(Path.Combine(oracleRoot, "raw-B-release-failure.json"),
                JsonSerializer.Serialize(releaseTrace, new JsonSerializerOptions { WriteIndented = true }));
        Check(released, "raw B tap drives real selected throw animation/wrist release and typed public event");
    }
}
