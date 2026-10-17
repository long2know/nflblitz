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

internal static class CanonicalCacheCapture
{
    internal static Action<GameSimulation, PlayerView, SourceVector3, SourceMovementCameraFrame,
        IReadOnlyList<SourceVector3>> Observer;

    internal static void Record(GameSimulation game, PlayerView view, SourceVector3 root,
        SourceMovementCameraFrame frame, IReadOnlyList<SourceVector3> joints)
    {
        if (Observer != null) Observer(game, view, root, frame, joints);
    }
}

internal static partial class Program
{
    private sealed class CanonicalSample
    {
        internal PlayerView View;
        internal SourceVector3 Root;
        internal SourceMovementCameraFrame Camera;
        internal SourceVector3[] Joints;
        internal string Pose;
    }

    private sealed class CanonicalWitness
    {
        private readonly GameSimulation game;
        private readonly bool observed;
        private readonly List<CanonicalSample> samples = new List<CanonicalSample>();
        private SourceVector3[,] previous;
        private long previousTick = -1, collectingTick = -1;
        internal int Frames, Calls, CurrentRows, PreviousRows, ReferenceRows, OrdinaryRows, ContactFrames, DisplacedActors;

        internal CanonicalWitness(GameSimulation simulation, bool instrumented)
        {
            game = simulation; observed = instrumented;
        }

        private static string Pose(PlayerView view)
        {
            return JsonSerializer.Serialize(new { view.ModelScale, view.FacingDirection, view.Animation },
                new JsonSerializerOptions { IncludeFields = true });
        }

        internal void Camera(long tick)
        {
            if (previous == null)
                Check(game.Model.SourceCameraJointTick == -1, "canonical initial cache is explicitly unavailable");
            else
            {
                Check(game.Model.SourceCameraJointTick == previousTick, "canonical preceding cache generation survives to camera stage");
                for (int id = 0; id < 14; id++)
                for (int joint = 0; joint < 15; joint++)
                {
                    Check(SameBits(game.Model.SourceCameraJoints[id, joint], previous[id, joint]),
                        "canonical previous14x15 cache bits survive contact until next last_player_proc");
                    PreviousRows++;
                }
            }
            Check(game.Model.SourceMovementCameraTick == tick, "canonical current camera precedes players");
            collectingTick = tick; samples.Clear();
        }

        internal void Capture(GameSimulation simulation, PlayerView view, SourceVector3 root,
            SourceMovementCameraFrame camera, IReadOnlyList<SourceVector3> joints)
        {
            long draws = simulation.Model.SourceRandomDraws;
            int cursor = simulation.Model.SourceRandomCallIndex;
            Check(observed && ReferenceEquals(simulation, game) && game.SourceTick == collectingTick &&
                game.Model.SourceRandomProcess == "player_proc" && game.Model.SourceCameraJointTick < game.SourceTick,
                "canonical isolated observer records only the actual post-player/pre-contact call");
            Check(samples.Count < 14 && view.Id == samples.Count, "canonical observer records each actual actor0..13 once");
            Check(SameBits(root, SourcePlayerDriver.WorldPosition(game.Model.Players[view.Id])) &&
                ReferenceEquals(camera, game.Model.SourceMovementCamera) && camera.Transform.Count == 12,
                "canonical actual call retains current live root and current camera");
            Check(Pose(view) == Pose(GameSnapshot.SourcePlayerView(game.Model.Players[view.Id], game.SourceTick, true)),
                "canonical actual call contains the exact live post-player pose, not post-contact resampling");
            Check(joints.Count == 15, "canonical observer records all15 actual returned joints");
            samples.Add(new CanonicalSample { View = view, Root = root, Camera = camera,
                Joints = joints.ToArray(), Pose = Pose(view) });
            Calls++;
            Check(simulation.Model.SourceRandomDraws == draws && simulation.Model.SourceRandomCallIndex == cursor,
                "canonical observer writes only test buffers and never draws or changes the source cursor");
        }

        // Offline reference arithmetic uses captured arguments, never another geometry call or a post-contact replacement pose.
        private static SourceVector3[] Reference(CanonicalSample sample, SourceVector3 referenceRoot)
        {
            SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots,
                sample.View, sample.View, 1f);
            IReadOnlyList<float> m = sample.Camera.Transform;
            float rx = referenceRoot.X - sample.Camera.Position.X;
            float ry = referenceRoot.Y - sample.Camera.Position.Y;
            float rz = referenceRoot.Z - sample.Camera.Position.Z;
            float x = m[0] * rx + m[1] * ry + m[2] * rz + m[3];
            float y = m[4] * rx + m[5] * ry + m[6] * rz + m[7];
            float z = m[8] * rx + m[9] * ry + m[10] * rz + m[11];
            return pose.Joints.Select(joint => new SourceVector3(
                m[0] * joint.Position.X + m[1] * joint.Position.Y - m[2] * joint.Position.Z + x,
                m[4] * joint.Position.X + m[5] * joint.Position.Y - m[6] * joint.Position.Z + y,
                m[8] * joint.Position.X + m[9] * joint.Position.Y - m[10] * joint.Position.Z + z)).ToArray();
        }

        internal void Complete(long beforeTick)
        {
            if (game.SourceTick == beforeTick || game.SourceTick != collectingTick) return;
            Check(game.Model.SourceCameraJointTick == game.SourceTick, "canonical complete wake commits its current cache");
            if (observed)
            {
                Check(samples.Count == 14, "canonical isolated observer must prove all14 actual calls, not an environment flag");
                int rows = 0;
                foreach (CanonicalSample sample in samples)
                {
                    int id = sample.View.Id;
                    ActorState actor = game.Model.Players[id];
                    SourceVector3 afterRoot = SourcePlayerDriver.WorldPosition(actor);
                    bool displaced = !SameBits(afterRoot, sample.Root);
                    SourceVector3 referenceRoot = sample.Root;
                    SourceVector3[] reference = Reference(sample, referenceRoot);
                    bool ordinary = !displaced && Pose(GameSnapshot.SourcePlayerView(actor, game.SourceTick, true)) == sample.Pose;
                    if (displaced) DisplacedActors++;
                    for (int joint = 0; joint < 15; joint++)
                    {
                        Check(SameBits(sample.Joints[joint], reference[joint]),
                            "canonical ReferenceRows use exact PRE-contact call arguments and original matrix arithmetic");
                        ReferenceRows++;
                        Check(SameBits(game.Model.SourceCameraJoints[id, joint], sample.Joints[joint]),
                            "canonical current14x15 cache retains every actual PRE-contact returned bit");
                        CurrentRows++; rows++;
                        if (ordinary)
                        {
                            Check(SameBits(game.Model.SourceCameraJoints[id, joint], reference[joint]),
                                "canonical ordinary unchanged pose/root retains exact camera-joint equality");
                            OrdinaryRows++;
                        }
                    }
                }
                Check(rows == 210, "canonical all14x15 comparisons include EVERY displaced actor, with no dropped rows");
            }
            if (game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision)) ContactFrames++;
            previous = (SourceVector3[,])game.Model.SourceCameraJoints.Clone();
            previousTick = game.SourceTick; Frames++;
        }

        internal object Summary()
        {
            return new { observed, Frames, Calls, CurrentRows, PreviousRows, ReferenceRows, OrdinaryRows,
                ContactFrames, DisplacedActors, additionalGeometryCalls = 0,
                reference = "offline captured-view pose sampling/matrix arithmetic; not an independent original-C geometry oracle" };
        }
    }

    private static string CanonicalHash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static int[] JointBits(GameSimulation game)
    {
        var bits = new List<int>();
        for (int id = 0; id < 14; id++)
        for (int joint = 0; joint < 15; joint++)
        {
            SourceVector3 value = game.Model.SourceCameraJoints[id, joint];
            bits.Add(BitConverter.SingleToInt32Bits(value.X));
            bits.Add(BitConverter.SingleToInt32Bits(value.Y));
            bits.Add(BitConverter.SingleToInt32Bits(value.Z));
        }
        return bits.ToArray();
    }

    private static object CanonicalObservation(GameSimulation game, int host, int rawCursor, object dispatches)
    {
        return new { host, rawCursor, game.SourceTick, game.State, game.SourceEvents, game.ContactEvents,
            game.C2Events, game.C2State, dispatches,
            game.Model.SourceRandomDraws, game.Model.SourceRandomProcess, game.Model.SourceRandomActor,
            game.Model.SourceRandomProcessOrder, game.Model.SourceRandomCallIndex,
            game.Model.SourceFlags, game.Model.SourceLastResult, game.Model.SourceResultView,
            game.Model.SourceRules, game.Model.CarrierId, game.Model.TargetPlayerId, game.Model.ThrowerId,
            game.Model.SourcePlayEndX, game.Model.SourceLastPossession, game.Model.SourceCameraJointTick,
            game.Model.SourceTeamFire, game.Model.SourceSpinCount, game.Model.SourceTaunted,
            actors = game.Model.Players.Select(actor => new {
                actor.Id, actor.Position, actor.Height, actor.Facing, actor.TargetFacing, actor.SourceMode,
                actor.SourceJob, actor.SourceTarget, actor.SourceControllerStation, actor.SourceButtons,
                actor.SourceNewButtons, actor.SourceHitPartnerId, actor.SourcePuppetLinkId,
                world = SourcePlayerDriver.WorldPosition(actor),
                view = GameSnapshot.SourcePlayerView(actor, game.SourceTick, true),
                actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId
            }).ToArray(), cache = JointBits(game) };
    }

    private static object CanonicalSoundContext(SourceC2SoundContext context)
    {
        if (context == null) return null;
        return new { context.Kind, context.SourceTick, context.Process, context.ProcessOrder, context.FirstCallIndex,
            context.ActorId, context.SourcePath, context.FunctionSymbol, context.SourceLine, context.Argument0,
            context.Argument1, context.Argument2, context.InvocationId, context.HasOriginalAnimationCue };
    }

    private static void CanonicalEntry(bool observed)
    {
        int[] rawVector = { 0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
            2147483645, 2147483646, 2147483647 };
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera: false, random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length],
            new SourceRandomProvenance("source-sound-smoke/raw-vector-v1",
                "Explicit repeated raw vector shared by ALL sites, not target libc PRNG",
                "canonical281 actual composition entry", SourceRandomEvidence.ExternalUnverified)));
        Check(real.IsC2Profile && real.Animations.SequenceNames.Count == 281 && real.Animations.FrameCount == 26230,
            "canonical current281 library is complete and not a272/157 alias");
        Check(shared.Trace.Count >= 2 && shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560") &&
            game.C2State.AnnouncerBank == "tkbank3" && game.C2State.TauntBank == "taunts4",
            "canonical actual bootstrap consumes two original bank draws before initial AI");
        var witness = new CanonicalWitness(game, observed);
        if (observed) CanonicalCacheCapture.Observer = witness.Capture;
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
            witness.Camera(tick); return heading;
        };
        int deadWait = 0, completedHosts = 0, rSkipFrames = 0, sounds = 0;
        object failure = null;
        var options = new JsonSerializerOptions { IncludeFields = true };
        string tracePath = Path.Combine(outputRoot, "canonical-observable-stream.jsonl");
        using (var stream = new StreamWriter(tracePath, false, new UTF8Encoding(false)))
        {
            stream.WriteLine(JsonSerializer.Serialize(new { host = -1, game.SourceTick, rawCursor,
                sha256 = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                    CanonicalObservation(game, -1, rawCursor, null), options)) }));
            for (int host = 0; host < 12000; host++)
            {
                long beforeTick = game.SourceTick;
                GamePhase beforePhase = game.State.Phase;
                PlayerCommand command = default(PlayerCommand);
                if (beforePhase == GamePhase.PlaySelect)
                    command = new PlayerCommand(action: InputAction.SelectPlay,
                        play: game.State.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Cover);
                else if (beforePhase == GamePhase.PreSnap) command = new PlayerCommand(action: InputAction.Snap);
                else if (beforePhase == GamePhase.DeadBall)
                    command = ++deadWait > 30 ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand);
                else if (beforePhase == GamePhase.LiveBall)
                    command = new PlayerCommand(new FieldVector(0f, 1f), true,
                        game.State.Possession == TeamId.Home ? InputAction.None : InputAction.Tackle);
                if (beforePhase != GamePhase.DeadBall) deadWait = 0;
                try { game.Step(command); }
                catch (Exception error)
                {
                    failure = new { host, beforeTick, beforePhase, sourceTick = game.SourceTick,
                        exception = error.ToString(), command, rawCursor, rawVector,
                        game.Model.SourceRandomProcess, game.Model.SourceRandomActor,
                        game.Model.SourceRandomProcessOrder, game.Model.SourceRandomCallIndex,
                        game.Model.SourceAnimationFailure, actual = CanonicalObservation(game, host, rawCursor, null),
                        allSharedRawDraws = shared.Trace };
                    File.WriteAllText(Path.Combine(outputRoot, "first-canonical-software-failure.json"),
                        JsonSerializer.Serialize(failure, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                    throw;
                }
                witness.Complete(beforeTick);
                SourceSoundVmDispatch[] dispatches = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
                sounds += dispatches.Length;
                object sound = dispatches.Select(item => new {
                    item.Status, item.Detail, item.RandomContext, item.HasOriginalAnimationCue,
                    original = item.HasOriginalAnimationCue ? item.OriginalCue : null,
                    c2Context = CanonicalSoundContext(item.C2Context),
                    randomDraws = item.Result == null ? null : item.Result.RandomDraws,
                    calls = item.Result == null ? null : item.Result.Calls.Select(call => new {
                        call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord, call.SubcallOrder,
                        call.RandomCalls, call.CalleeSourceSite, call.HasOriginalAnimationCue,
                        original = call.HasOriginalAnimationCue ? call.OriginalCue : null,
                        c2Context = CanonicalSoundContext(call.C2Context)
                    }).ToArray()
                }).ToArray();
                stream.WriteLine(JsonSerializer.Serialize(new { host, game.SourceTick, rawCursor,
                    sha256 = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                        CanonicalObservation(game, host, rawCursor, sound), options)) }));
                rSkipFrames += game.Model.Players.Count(actor => actor.AnimationState.Primary.SequenceName == "R_SKIP");
                completedHosts++;
            }
        }
        CanonicalCacheCapture.Observer = null;
        Check(failure == null && completedHosts == 12000 && shared.Trace.Count == rawCursor,
            "canonical independent full unfiltered12000 host window retains every real shared draw");
        if (observed)
            Check(witness.Calls == witness.Frames * 14 && witness.CurrentRows == witness.Frames * 210 &&
                witness.ReferenceRows == witness.CurrentRows && witness.PreviousRows > 0 &&
                witness.OrdinaryRows > 0 && witness.ContactFrames > 0 && witness.DisplacedActors > 0,
                "canonical isolated strict witness retains every current/previous/reference/contact/ordinary row");
        File.WriteAllText(Path.Combine(outputRoot, "canonical-entry.json"), JsonSerializer.Serialize(new {
            schema = "source-canonical281-M1-entry/v1", status = "bounded12000-window-not-full-M1",
            observed, factory = "REAL SourceC2Composition", sourceTick = game.SourceTick, rawVector, rawCursor,
            completedHosts, assertions, rSkipFrames, sounds, cache = witness.Summary(),
            observableStreamSha256 = CanonicalHash(File.ReadAllBytes(tracePath)),
            allSharedRawDraws = shared.Trace, fullM1OrGameOrNativeAccepted = false,
            policy = "PORTABILITY_REPAIR:NO_ELIGIBLE_CANDIDATE; autonomous, NOT user-approved, NOT target-memory/ABI parity"
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }
}
