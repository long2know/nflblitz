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

    internal static int Run(FullKernelInputs real, string originalCases)
    {
        oracleRoot = originalCases;
        var settings = new SourcePresentationSettings(TeamId.Away, new[] { -1, 3, 19, -1 }, -2);
        Func<GameSimulation> create = () => real.Create(configuration: Configuration(), presentation: settings);
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
            var game = real.Create(configuration: Configuration(), randomFactory: seed => random);
            game.Model.Phase = t[0] != 0 ? GamePhase.LiveBall : GamePhase.DeadBall;
            game.Model.CarrierId = t[2] != 0 ? 4 : 3;
            ActorState actor = game.Model.Players[4];
            actor.SourceHelmetPopped = t[1] != 0;
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
            var game = real.Create(configuration: Configuration());
            game.Model.Phase = GamePhase.DeadBall;
            game.Model.CarrierId = script == "h_front1_anim" ? 3 : 4;
            ActorState actor = game.Model.Players[4];
            actor.SourceHelmetPopped = false;
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
            for (int tick = 0; game.State.Phase != GamePhase.PlaySelect && tick < 5000; tick++)
                game.Step(game.State.Phase == GamePhase.DeadBall
                    ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand));
            Check(game.State.Phase == GamePhase.PlaySelect, "public actual kickoff flow reaches source play selection");
        }
        long flipTick = flipped.SourceTick;
        flipped.Step(new PlayerCommand(source: new SourceCommand(SourceAction.None, offenseFlipped: true)));
        if (flipped.SourceTick == flipTick) flipped.Step(default(PlayerCommand));
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
        var game = real.Create(configuration: config);
        for (int host = 0; game.State.Phase != GamePhase.PlaySelect && host < 5000; host++)
            game.Step(game.State.Phase == GamePhase.DeadBall ? new PlayerCommand(action: InputAction.Continue)
                : new PlayerCommand(new FieldVector(1f, 0f), true));
        Check(game.State.Phase == GamePhase.PlaySelect && game.State.Possession == TeamId.Home,
            "actual away kickoff/public home return reaches human offensive selection");
        long tick = game.SourceTick;
        game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_spider", SourcePlaybookKind.Offense))));
        if (game.SourceTick == tick) game.Step(default(PlayerCommand));
        Check(game.State.Phase == GamePhase.PreSnap, "real original human QB lineup reached");
        for (int host = 0; host < 100 && (game.Model.PreSnapTicksRemaining >=
            SourceClock.DefaultAutoHikeTicks - SourceClock.SourceTicksPerSecond
            || game.Model.SourceClockRemainder != 0); host++)
            game.Step(Raw(SourceButtons.None));
        Check(game.State.Phase == GamePhase.PreSnap && game.Model.SourceClockRemainder == 0,
            "raw tap is deliberately placed across a skipped source wake after actual one-second gate");
        tick = game.SourceTick;
        game.Step(Raw(SourceButtons.B, SourceButtons.B));
        Check(game.SourceTick == tick, "raw snap press stays queued on skipped host tick");
        game.Step(Raw(SourceButtons.None, released: SourceButtons.B));
        Check(game.State.Phase == GamePhase.LiveBall, "complete B press/release actually snaps despite Held=0");
        int quarterback = game.Model.OffenseOffset + 3;
        for (int host = 0; game.Model.CarrierId != quarterback && host < 100; host++)
            game.Step(Raw(SourceButtons.None));
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
