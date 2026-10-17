using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static readonly List<object> snapEvidence = new List<object>();
    private static int snapAssertions;
    private static void SnapCheck(bool condition, string identity)
    {
        snapAssertions++;
        if (!condition) throw new InvalidOperationException("SNAP_HIKE_ASSERTION: " + identity);
    }
    private static T SnapBind<T>(GameSimulation game, string method)
    {
        return (T)(object)typeof(GameSimulation).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
            .CreateDelegate(typeof(T), game);
    }

    private static int SnapHikeChecks(string[] args)
    {
        outputRoot = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(outputRoot);
        try
        {
            real = FullKernelInputs.Load(args[1], movementLibrary: true, soundFunctionsPath: args[2],
                c2M1InputsPath: args[3]);
            library = real.Animations; playbook = real.Playbook; geometry = real.Geometry;
            rosters = real.Rosters; movement = real.MovementRuntime;
            bool observed = args[5] == "observed";
            int referenceDelta = SnapHikeReferenceDelta();
            SnapHikeCase(false, observed, referenceDelta);
            SnapHikeCase(true, observed, referenceDelta);
            SnapHikeCurrentVariableFixture();
            var legacy = new GameSimulation(27);
            legacy.Step(new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.Run));
            legacy.Step(new PlayerCommand(action: InputAction.Snap));
            SnapCheck(legacy.Profile == SimulationProfile.LegacyBaseline60Hz &&
                legacy.State.Phase == GamePhase.LiveBall && legacy.State.Tick == 2,
                "Legacy60 select/snap same-host Live contract unchanged");
            File.WriteAllText(Path.Combine(outputRoot, "focused-result.json"), JsonSerializer.Serialize(new {
                status = "passed", snapAssertions, referenceDelta, observed, cases = snapEvidence,
                controlled_kernel_fixtures_NOT_public_R_SKIP_witness = true,
                source_contract = "player.c:1643-1696/5656-5679; plyrseq.c:8750-8758"
            }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
            Console.WriteLine("SNAP_HIKE_FOCUSED_PASS assertions=" + snapAssertions + " referenceDelta=" + referenceDelta);
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(outputRoot, "focused-result.json"), JsonSerializer.Serialize(new {
                status = "failed", snapAssertions, error = error.ToString(), cases = snapEvidence
            }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
            Console.WriteLine(error.ToString());
            return 1;
        }
    }

    private static int SnapHikeReferenceDelta()
    {
        SourceAnimationInstruction[] instructions = SourceAnimationPrograms.Get("center_snap_anim").Instructions;
        SnapCheck(instructions.Any(i => i.Op == SourceAnimationOp.Stream && i.A == 21 && i.B == 4) &&
            instructions.Count(i => i.Op == SourceAnimationOp.Callback &&
                i.A == (int)SourceAnimationSignal.HikeBall) == 1,
            "original center ANI_INTSTREAM21,4 precedes exactly one HikeBall opcode");
        var actor = new ActorState(8);
        actor.SourceDirections = real.Playbook.Directions;
        actor.AnimationState = new SourceAnimationState(library, actor.Id);
        long tick = 0, hike = -1;
        actor.AnimationState.BindExecution(null, null, null, signal => {
            if ((signal & SourceAnimationSignal.HikeBall) != 0) hike = tick;
        });
        actor.AnimationState.FullBody("center_snap_anim", actor, tick, n => 0, AnimationSelectionEvidence.SourceVerified);
        while (hike < 0 && ++tick < 30) actor.AnimationState.Advance(actor, tick, n => 0);
        SnapCheck(hike > 0 && hike < 30, "unchanged original VM derives release wake, not guessed host4 frames");
        return (int)hike;
    }

    private static GameSimulation SnapFixture(bool fieldGoal)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            fieldGoal ? new SourceStationConfiguration[0] :
                new[] { new SourceStationConfiguration(0, TeamId.Away) },
            TeamId.Away, SourceCaptainSlot.First);
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        GameSimulation game = real.Create(configuration: configuration, camera: false, presentation: presentation,
            randomFactory: seed => new SourceInjectedRandom(site => 99,
                new SourceRandomProvenance("snap-hike/controlled-raw99-all-sites",
                    "Declared controlled source-kernel case, not a public-input witness",
                    "snap-hike-source-operands", SourceRandomEvidence.ExternalUnverified)));
        // Initial controlled fixture preparation only; no changes during timing/replay.
        game.Model.Phase = GamePhase.PlaySelect;
        game.Model.SourceMatchPhase = SourceMatchPhase.PlaySelection;
        game.Model.Possession = TeamId.Away;
        game.Model.SourceLastPossession = 1;
        game.Model.SourceRules = new SourceRulesState(1, 1, fieldGoal ? 4 : 1, fieldGoal ? 89 : 30, 100, SourcePlayType.Normal);
        game.Model.TargetPlayerId = -1;
        game.Model.SourceFlags = SourceGameFlags.None;
        return game;
    }

    private static void SnapHikeCase(bool fieldGoal, bool observed, int referenceDelta)
    {
        GameSimulation game = SnapFixture(fieldGoal);
        var witness = new CanonicalWitness(game, observed);
        if (observed) CanonicalCacheCapture.Observer = witness.Capture;
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
            witness.Camera(tick); return heading;
        };
        var history = new List<object>();
        long requested = -1, released = -1, centerInstance = -1;
        int hikeEvents = 0;
        bool selected = false, selectedDefense = false, checkedReentry = false;
        SourceAiFrame savedFrame = null;
        for (int host = 0; host < 600; host++)
        {
            PlayerCommand command;
            if (!selected)
            {
                command = new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                    new SourcePlaySelection(fieldGoal ? "play_field_goal" : "play_spider", SourcePlaybookKind.Offense)));
                selected = true;
            }
            else if (game.State.Phase == GamePhase.PlaySelect && !selectedDefense && game.SourceTick > 0)
            {
                command = new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                    new SourcePlaySelection("play_safe_cover", SourcePlaybookKind.Defense)));
                selectedDefense = true;
            }
            else command = new PlayerCommand(source: new SourceCommand(SourceAction.Snap));
            long before = game.SourceTick;
            try { game.Step(command); witness.Complete(before); }
            catch (Exception error)
            {
                snapEvidence.Add(new { fieldGoal, host, game.SourceTick, error = error.ToString() });
                SnapCheck(false, "actual later kicker consumes current holder variables in SAME HikeBall wake");
            }
            if (game.SourceTick == before) continue;
            ActorState center = game.Model.Players[8];
            bool hiking = (game.Model.SourceFlags & SourceGameFlags.HikingBall) != 0;
            if (requested < 0 && hiking)
            {
                requested = game.SourceTick;
                centerInstance = center.AnimationState.Primary.InstanceId;
                SnapCheck(game.Model.Phase == GamePhase.PreSnap && game.Model.SourceMatchPhase == SourceMatchPhase.PreSnap &&
                    game.Model.CarrierId == 8 && game.Model.TargetPlayerId == -1,
                    "snap initiation remains PreSnap center-held receiver-1 until real HikeBall opcode");
                SnapCheck(center.AnimationState.Primary.ScriptSymbol == "center_snap_anim" && game.Model.ClockActive,
                    "original request starts actual center stream and clock without publishing Live");
            }
            hikeEvents += game.SourceEvents.Count(e => e.Type == SourceEventType.Snapped);
            if (requested >= 0 && game.SourceTick - requested < referenceDelta)
            {
                SnapCheck(game.Model.Phase == GamePhase.PreSnap && game.Model.CarrierId == 8 &&
                    game.Model.TargetPlayerId == -1 && hiking &&
                    center.AnimationState.Primary.InstanceId == centerInstance && hikeEvents == 0,
                    "every pre-release source wake preserves original center stream/receiver and no early Snapped event");
                if (!checkedReentry)
                {
                    long draws = game.Model.SourceRandomDraws;
                    int play = game.Model.PlayNumber, ordinal = center.AnimationState.Primary.FrameOrdinal;
                    SnapBind<Action<bool>>(game, "SnapSource")(false);
                    SnapCheck(center.AnimationState.Primary.InstanceId == centerInstance &&
                        center.AnimationState.Primary.FrameOrdinal == ordinal &&
                        game.Model.PlayNumber == play && game.Model.SourceRandomDraws == draws,
                        "existing hiking flag prevents reentrant snap/interpolation reset or duplicate request");
                    checkedReentry = true;
                }
            }
            if (requested >= 0 && game.Model.Phase == GamePhase.LiveBall)
            {
                released = game.SourceTick;
                SnapCheck(released - requested == referenceDelta &&
                    game.Model.TargetPlayerId == 10 && game.Model.ThrowerId == 8 &&
                    game.Model.BallKind == SourceBallKind.Lateral && hikeEvents == 1,
                    "real center opcode enters Live once on exact VM-derived source wake and marks holder10");
                savedFrame = game.Model.SourceAiFrame;
                SourceAiFrame current = SnapBind<Func<SourceAiFrame, SourceAiFrame>>(game,
                    "CurrentSourceKickerVariables")(savedFrame);
                SnapCheck(current.InPlay && current.Ball.IntendedReceiver == 10 &&
                    current.Ball.Carrier == game.Model.CarrierId &&
                    ReferenceEquals(game.Model.SourceAiFrame, savedFrame) &&
                    savedFrame.InPlay == false && savedFrame.Ball.IntendedReceiver == -1,
                    "current variable facade sees live holder10 without mutating immutable preactor AI frame");
                for (int a = 0; a < 14; a++)
                for (int b = 0; b < 14; b++)
                    SnapCheck(current.Distance(a, b) == savedFrame.Distance(a, b) &&
                        current.Angle(a, b) == savedFrame.Angle(a, b),
                        "current holder-variable facade preserves original cached geometry matrices");
                if (fieldGoal)
                    SnapCheck(game.Model.SourceDrones[13].LastSourceTick == released,
                        "actual kicker process resolves later in the SAME original HikeBall wake");
            }
            history.Add(new { host, game.SourceTick, phase = game.Model.Phase, requested, released,
                game.Model.CarrierId, game.Model.TargetPlayerId, game.Model.SourceFlags,
                script = center.AnimationState.Primary.ScriptSymbol,
                ordinal = center.AnimationState.Primary.FrameOrdinal, instance = center.AnimationState.Primary.InstanceId,
                events = game.SourceEvents });
            if (released >= 0) break;
        }
        SnapCheck(requested >= 0 && released >= 0 && checkedReentry, "bounded normal/FG case reaches actual request and release");
        SnapCheck(witness.Frames > 0 && (!observed || witness.Calls == witness.Frames * 14 &&
            witness.CurrentRows == witness.Frames * 210 && witness.ReferenceRows == witness.CurrentRows),
            "actual source cache retains all14x15 rows through snap/hike");
        snapEvidence.Add(new { fieldGoal, requested, released, referenceDelta, hikeEvents, history,
            cache = witness.Summary() });
        CanonicalCacheCapture.Observer = null;
    }

    private static void SnapHikeCurrentVariableFixture()
    {
        GameSimulation game = SnapFixture(true);
        game.SourceCameraHeadingProvider = (snapshot, tick) => FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
        game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_field_goal", SourcePlaybookKind.Offense))));
        game.Step(default(PlayerCommand));
        SourceAiFrame captured = game.Model.SourceAiFrame;
        SnapCheck(captured != null && captured.InPlay == false && captured.Ball.IntendedReceiver == -1,
            "declared immutable decision fixture really begins PreSnap receiver-1");
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.CarrierId = 10;
        game.Model.TargetPlayerId = 10;
        SourceAiFrame current = SnapBind<Func<SourceAiFrame, SourceAiFrame>>(game, "CurrentSourceKickerVariables")(captured);
        SnapCheck(current.Ball.Carrier == 10 && current.Ball.IntendedReceiver == 10 && current.InPlay &&
            captured.Ball.Carrier == 8 && captured.Ball.IntendedReceiver == -1 && !captured.InPlay,
            "live holder variables do not reinterpret/mutate a preactor decision fixture");
        game.Model.TargetPlayerId = -1;
        SourceAiFrame invalid = SnapBind<Func<SourceAiFrame, SourceAiFrame>>(game, "CurrentSourceKickerVariables")(captured);
        bool rejected = false;
        try
        {
            SourceRouteState freshDecision = SourceRouteState.Begin(game.Model.SourceDrones[13].Program,
                game.Model.SourceDrones[13].Target, game.Model.SourceDrones[13].Flags);
            SourceDroneAI.StepRoute(freshDecision,
                new SourceRouteInput(invalid, 13, game.Model.SourceAiParameters, new SourceRandomContext(game.SourceTick, 13,
                    "player_proc", 13, game.SourceRandomService.Provenance.Profile),
                    formationSlot: game.Model.SourceFormationSlots[13],
                    kickerContext: new SourceDroneKickerContext(SourceKickerStage.InPlay, SourceDroneButtons.None)),
                game.SourceRandomService);
        }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("holder int_receiver=-1"); }
        SnapCheck(rejected, "current live carrier with receiver-1 remains an explicit invalid-domain failure");
    }
}
