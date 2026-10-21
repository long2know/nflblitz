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
        internal long Instance;
        internal int Ordinal;
        internal string Sequence;
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
                Joints = joints.ToArray(), Pose = Pose(view),
                Instance = game.Model.Players[view.Id].AnimationState.Primary.InstanceId,
                Ordinal = game.Model.Players[view.Id].AnimationState.Primary.FrameOrdinal,
                Sequence = game.Model.Players[view.Id].AnimationState.Primary.SequenceName });
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

        internal CanonicalSample Captured(int actor) { return samples.SingleOrDefault(s => s.View.Id == actor); }
    }

    private static SourceCommand ReplaySourceCommand(JsonElement value)
    {
        if (!value.GetProperty("IsSpecified").GetBoolean()) return default(SourceCommand);
        JsonElement selection = value.GetProperty("Play");
        SourcePlaySelection play = !selection.GetProperty("IsSpecified").GetBoolean() ? default(SourcePlaySelection)
            : new SourcePlaySelection(selection.GetProperty("PlaySymbol").GetString(),
                (SourcePlaybookKind)selection.GetProperty("Playbook").GetInt32(),
                selection.GetProperty("SlotIndex").GetInt32(), selection.GetProperty("PageIndex").GetInt32(),
                selection.GetProperty("Pool").ValueKind == JsonValueKind.Null ? (SourcePlayPool?)null :
                    (SourcePlayPool)selection.GetProperty("Pool").GetInt32(),
                selection.GetProperty("PoolIndex").GetInt32());
        return new SourceCommand((SourceAction)value.GetProperty("Action").GetInt32(), play,
            value.GetProperty("TargetPlayerId").GetInt32(), (SourcePatOption)value.GetProperty("PatChoice").GetInt32(),
            value.GetProperty("KickoffChoice").ValueKind == JsonValueKind.Null ? (SourceKickoffType?)null :
                (SourceKickoffType)value.GetProperty("KickoffChoice").GetInt32(),
            value.GetProperty("AudibleSlot").GetInt32(),
            value.GetProperty("OffenseFlipped").ValueKind == JsonValueKind.Null ? (bool?)null :
                value.GetProperty("OffenseFlipped").GetBoolean());
    }

    private static PlayerCommand ReplayPublicCommand(JsonElement value)
    {
        JsonElement move = value.GetProperty("Move"), stations = value.GetProperty("StationInputs");
        SourceInputFrame frame = null;
        if (stations.ValueKind != JsonValueKind.Null)
            frame = new SourceInputFrame(stations.GetProperty("Stations").EnumerateArray().Select(input =>
                new SourceStationInput(input.GetProperty("StationId").GetInt32(), input.GetProperty("StickX").GetInt32(),
                    input.GetProperty("StickZ").GetInt32(), (SourceButtons)input.GetProperty("Held").GetInt32(),
                    (SourceButtons)input.GetProperty("Pressed").GetInt32(), (SourceButtons)input.GetProperty("Released").GetInt32(),
                    ReplaySourceCommand(input.GetProperty("Command")))).ToArray());
        return new PlayerCommand(new FieldVector(move.GetProperty("X").GetSingle(), move.GetProperty("Z").GetSingle()),
            value.GetProperty("Turbo").GetBoolean(), (InputAction)value.GetProperty("Action").GetInt32(),
            (PlayId)value.GetProperty("Play").GetInt32(), value.GetProperty("TargetPlayerId").GetInt32(),
            ReplaySourceCommand(value.GetProperty("Source")), frame);
    }

    private static PlayerCommand[] ReadPublicReplay(string path)
    {
        var commands = new List<PlayerCommand>();
        if (Path.GetExtension(path) == ".jsonl")
        {
            foreach (string line in File.ReadLines(path))
                using (JsonDocument row = JsonDocument.Parse(line))
                {
                    Check(row.RootElement.GetProperty("host").GetInt32() == commands.Count,
                        "M1 replay preserves every original recorded host, without added/rebased wakes");
                    commands.Add(ReplayPublicCommand(row.RootElement.GetProperty("command")));
                }
        }
        else
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path)))
                foreach (JsonElement row in document.RootElement.GetProperty("commands").EnumerateArray())
                {
                    Check(row.GetProperty("host").GetInt32() == commands.Count,
                        "M1 replay preserves every original recorded host, without added/rebased wakes");
                    commands.Add(ReplayPublicCommand(row.GetProperty("command")));
                }
        Check(commands.Count == 12000, "M1 replay requires the entire immutable original12000 command vector");
        return commands.ToArray();
    }

    private static bool m1ReplayPositive;
    private static object m1ReplayEvidence;

    private sealed class PreSnapPublicEdge
    {
        internal readonly int Host;
        internal readonly long BeforeTick;
        internal readonly PortableVisible Visible;
        internal readonly PlayerCommand Command;
        internal PreSnapPublicEdge(int host, GameSimulation game, PlayerCommand command)
        { Host = host; BeforeTick = game.SourceTick; Visible = PortableVisible.Project(game.State); Command = command; }
    }

    private static PreSnapPublicEdge CapturePublicB(int host, GameSimulation game, PlayerCommand command,
        PreSnapPublicEdge pending)
    {
        if (command.StationInputs != null && command.StationInputs.Stations.Any(input =>
            input.StationId == 0 && (input.Held & SourceButtons.B) != 0 &&
            (input.Pressed & SourceButtons.B) != 0))
            return new PreSnapPublicEdge(host, game, command);
        return pending;
    }

    private static object RequirePublicSwitchWitness(int host, GameSimulation game, SourceSoundVmDispatch sound,
        PreSnapPublicEdge edge)
    {
        Check(edge != null && edge.BeforeTick < game.SourceTick &&
            edge.Command.StationInputs.Stations.Count == 1 &&
            edge.Command.StationInputs.Stations[0].StationId == 0,
            "new B witness requires an actual captured fresh raw station0 edge, including a skipped host");
        SourceGameEvent transfer = game.SourceEvents.Single(e => e.Type == SourceEventType.ControllerChanged &&
            e.ActorPlayerId == sound.C2Context.ActorId && e.StationId == 0);
        PlayerView actor = game.State.Players.Single(p => p.Id == transfer.ActorPlayerId);
        Check(transfer.SourceTick == game.SourceTick && transfer.TargetPlayerId == edge.Visible.ControlledId &&
            transfer.ActorPlayerId == game.State.ControlledPlayerId &&
            transfer.ActorPlayerId != transfer.TargetPlayerId && actor.Team == TeamId.Home,
            "new B witness binds public prior/current ownership and the actual source transfer");
        Check(actor.Animation.Primary.ScriptSymbol == "m_hopclp_anim" &&
            actor.Animation.Primary.Sample.SequenceName == "M_HOPCLP" &&
            actor.Animation.SourceTick == game.SourceTick,
            "new B witness samples the real transferred actor's authentic immediate hop");
        Check(sound.Status == SourceSoundVmStatus.Resolved && sound.Result != null &&
            sound.Result.Calls.Count == 1 && sound.Result.RandomCalls == 0 &&
            sound.C2Context.SourceTick == game.SourceTick,
            "new B witness retains a synchronous resolved kind5 call without callback RNG");
        var call = sound.Result.Calls.Single();
        Check(call.PlaylistId == 127 && call.Volume == 160 && call.Pan == 127 && call.PriorityWord == 10 &&
            call.CalleeSourceSite.SourcePath == "sounds.c" && call.CalleeSourceSite.SourceLine == 1638 &&
            ReferenceEquals(call.C2Context, sound.C2Context) && ReferenceEquals(call.OriginalCue, sound.OriginalCue),
            "new B witness retains the exact original human sound tuple, cue and call context");
        SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots, actor, actor, 1f);
        Check(pose.Joints.Count == 15, "new B witness retains all fifteen actual evaluated joints");
        return new { host, game.SourceTick, rawHost = edge.Host, edge.BeforeTick, edge.Visible, edge.Command,
            transfer, sound.C2Context, sound.C2StateAtCall, sound.Status, original = sound.OriginalCue,
            calls = sound.Result.Calls.Select(c => new { c.Bank,c.PlaylistId,c.Volume,c.Pan,c.PriorityWord,c.CalleeSourceSite }),
            actor, real15LocalPose = pose.Joints.Select(j => j.Position).ToArray() };
    }

    private static void M1CompleteReplay(string replayPath, bool observed, bool historical)
    {
        Check(real.IsC2Profile && (real.IsC2M1Profile || real.IsPreSnapSwitchProfile) != historical,
            "M1 full public loop explicitly selects real successor factory, never bare272 or proxy constructor");
        PlayerCommand[] commands = ReadPublicReplay(replayPath);
        int[] rawVector = { 0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
            2147483645, 2147483646, 2147483647 };
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera: false, random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length],
            new SourceRandomProvenance("source-sound-smoke/raw-vector-v1",
                "Explicit repeated raw vector shared by ALL sites, not target libc PRNG",
                "canonical281 actual composition entry", SourceRandomEvidence.ExternalUnverified)));
        Check(shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560"),
            "M1 replay actual two bank draws precede original AI on the same provider");
        var witness = new CanonicalWitness(game, observed);
        if (observed) CanonicalCacheCapture.Observer = witness.Capture;
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
            witness.Camera(tick); return heading;
        };
        var options = new JsonSerializerOptions { IncludeFields = true };
        var firstPoses = new Dictionary<string, Tuple<long, int, string>>();
        var poses = new List<object>();
        var phaseCounts = new Dictionary<string, int>();
        var actionCounts = new Dictionary<string, int>();
        object firstNewCallback = null, positive = null, failure = null;
        Exception firstFailure = null;
        int completed = 0, rSkipFrames = 0;
        int preSnapSwitchWitnesses = 0;
        object firstPreSnapSwitch = null;
        PreSnapPublicEdge pendingPublicB = null;
        Action<Dictionary<string, int>, string> count = (values, key) => {
            int prior; values.TryGetValue(key, out prior); values[key] = prior + 1;
        };
        string streamPath = Path.Combine(outputRoot, "m1-public-replay.jsonl");
        using (var stream = new StreamWriter(streamPath, false, new UTF8Encoding(false)))
        {
            for (int host = 0; host < commands.Length; host++)
            {
                long before = game.SourceTick;
                PlayerCommand command = commands[host];
                if (real.IsPreSnapSwitchProfile)
                    pendingPublicB = CapturePublicB(host, game, command, pendingPublicB);
                try { game.Step(command); witness.Complete(before); }
                catch (Exception error)
                {
                    firstFailure = error;
                    failure = new { host, before, game.SourceTick, command, error = error.ToString(),
                        actual = CanonicalObservation(game, host, rawCursor, null), allSharedRawDraws = shared.Trace };
                    File.WriteAllText(Path.Combine(outputRoot, "first-m1-kernel-fault.json"),
                        JsonSerializer.Serialize(failure, options));
                }
                SourceSoundVmDispatch[] dispatches = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
                if (real.IsPreSnapSwitchProfile)
                    foreach (SourceSoundVmDispatch dispatch in dispatches.Where(d =>
                        d.C2Context != null && d.C2Context.Kind == SourceC2SoundFunctionKind.AttackSoundFixed))
                    {
                        object actual = RequirePublicSwitchWitness(host, game, dispatch, pendingPublicB);
                        if (pendingPublicB.Visible.Phase == GamePhase.PreSnap &&
                            pendingPublicB.Visible.Possession == TeamId.Away)
                        {
                            preSnapSwitchWitnesses++;
                            if (firstPreSnapSwitch == null) firstPreSnapSwitch = actual;
                        }
                    }
                if (game.SourceTick != before) pendingPublicB = null;
                object sounds = dispatches.Select(item => new {
                    item.Status, item.Detail, item.RandomContext, item.HasOriginalAnimationCue,
                    original = item.HasOriginalAnimationCue ? item.OriginalCue : null,
                    c2Context = CanonicalSoundContext(item.C2Context),
                    randomDraws = item.Result == null ? null : item.Result.RandomDraws,
                    calls = item.Result == null ? null : item.Result.Calls.Select(call => new {
                        call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord, call.SubcallOrder,
                        call.CalleeSourceSite, call.HasOriginalAnimationCue,
                        original = call.HasOriginalAnimationCue ? call.OriginalCue : null,
                        c2Context = CanonicalSoundContext(call.C2Context) }).ToArray()
                }).ToArray();
                foreach (SourceSoundVmDispatch dispatch in dispatches)
                    if (dispatch.HasOriginalAnimationCue && new[] { 6373, 6401, 6428, 6480, 6508 }
                        .Contains(dispatch.OriginalCue.Context.SourceLine) && firstNewCallback == null)
                        firstNewCallback = new { host, game.SourceTick, dispatch.OriginalCue,
                            dispatch.Status, c2 = CanonicalSoundContext(dispatch.C2Context),
                            firstNewRawOrder = dispatch.Result == null || dispatch.Result.RandomDraws.Count == 0
                                ? (long?)null : dispatch.Result.RandomDraws[0].Order };
                if (game.SourceTick != before && failure == null)
                    foreach (ActorState actor in game.Model.Players)
                    {
                        SourceAnimationLayerState layer = actor.AnimationState.Primary;
                        if (layer.SequenceName != "R_SKIP") continue;
                        rSkipFrames++;
                        if (game.SourceTick <= 2299 || !layer.IsSpecified || layer.SourceTick != game.SourceTick) continue;
                        ISourceAnimationClip clip = library.GetClip("R_SKIP");
                        Check(layer.FrameOrdinal >= 0 && layer.FrameOrdinal < clip.SelectedSourceFrames.Count,
                            "M1 real R_SKIP selected ordinal is valid after2299");
                        PlayerView view = GameSnapshot.SourcePlayerView(actor, game.SourceTick, true);
                        var joints = Enumerable.Range(0, 15).Select(j => game.Model.SourceCameraJoints[actor.Id, j]).ToArray();
                        if (observed)
                        {
                            CanonicalSample sample = witness.Captured(actor.Id);
                            Check(sample != null && sample.Instance == layer.InstanceId &&
                                sample.Sequence == "R_SKIP" && sample.Ordinal == layer.FrameOrdinal &&
                                sample.Joints.Where((joint, index) => !SameBits(joint, joints[index])).Count() == 0,
                                "M1 R_SKIP exact real PRE-contact instance/ordinal/all15 cache joints retained");
                            view = sample.View;
                        }
                        Check(game.Model.SourceCameraJointTick == game.SourceTick &&
                            joints.All(j => !float.IsNaN(j.X) && !float.IsInfinity(j.X) &&
                                !float.IsNaN(j.Y) && !float.IsInfinity(j.Y) && !float.IsNaN(j.Z) && !float.IsInfinity(j.Z)),
                            "M1 actual R_SKIP current15 joints are finite; no extra geometry calls");
                        SourceEvaluatedPose local = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots, view, view, 1f);
                        string localPose = JsonSerializer.Serialize(local.Joints.Select(j => j.Position).ToArray(), options);
                        poses.Add(new { host, game.SourceTick, actor = actor.Id, layer.InstanceId,
                            ordinal = layer.FrameOrdinal, selected = clip.SelectedSourceFrames[layer.FrameOrdinal],
                            real15Joints = joints, real15LocalPose = local.Joints.Select(j => j.Position).ToArray(), view });
                        string key = actor.Id + "/" + layer.InstanceId;
                        Tuple<long, int, string> first;
                        if (!firstPoses.TryGetValue(key, out first))
                            firstPoses[key] = Tuple.Create(game.SourceTick, layer.FrameOrdinal, localPose);
                        else if (game.SourceTick > first.Item1 && layer.FrameOrdinal != first.Item2 && localPose != first.Item3)
                        {
                            m1ReplayPositive = true;
                            positive = new { actor = actor.Id, layer.InstanceId, firstTick = first.Item1,
                                secondTick = game.SourceTick, firstOrdinal = first.Item2, secondOrdinal = layer.FrameOrdinal };
                        }
                    }
                ISourceMatchState state = (ISourceMatchState)game.State;
                count(phaseCounts, state.Phase + "/" + state.Match.Phase);
                count(actionCounts, command.Source.IsSpecified ? command.Source.Action.ToString() : command.Action.ToString());
                string fullHash = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                    CanonicalObservation(game, host, rawCursor, sounds), options));
                // Only the declared payload identity is omitted from this comparison projection;
                // complete actual state/hash and every raw draw are retained separately.
                object gameplay = new { game.State, game.SourceEvents, game.ContactEvents, game.C2Events,
                    game.Model.SourceFlags, game.Model.SourceLastResult, game.Model.SourceRules,
                    game.Model.SourceResultView, game.C2State.Taunted, game.C2State.AnnouncerBank,
                    game.C2State.TauntBank, game.C2State.EndEpoch, game.C2State.ResultPending,
                    game.C2State.WhistleProcesses, game.C2State.BankEpoch,
                    actors = game.Model.Players.Select(a => new { a.Id, a.Position, a.Velocity, a.Height,
                        a.Facing, a.TargetFacing, a.SourceMode, a.SourceJob, a.SourceTarget, a.SourceRawFlags,
                        a.SourceControllerStation, a.AnimationState.Primary.InstanceId,
                        a.AnimationState.Primary.ScriptSymbol, a.AnimationState.Primary.SequenceName,
                        a.AnimationState.Primary.FrameOrdinal }).ToArray(), cache = JointBits(game) };
                stream.WriteLine(JsonSerializer.Serialize(new { host, beforeSourceTick = before, game.SourceTick,
                    command, applied = game.Model.SourceCurrentInput, game.State.Phase, game.State.PlayNumber,
                    state.Match, game.State.Ball, actors = game.State.Players,
                    game.SourceEvents, game.C2Events, sounds, rawCursor,
                    fullActualStateEventCacheHash = fullHash,
                    gameplayPrefixHash = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(gameplay, options)) }, options));
                completed++;
                if (failure != null) break;
            }
        }
        CanonicalCacheCapture.Observer = null;
        m1ReplayEvidence = new {
            schema = "source-C2-M1-real-factory-public-replay/v1", observed, historical,
            gameplayCompatibility = game.Compatibility.ToString(),
            portableCorrectionsAreNotOriginalHardwareEvidence = game.Compatibility == SourceGameplayCompatibility.Portable,
            compatibilityRepairCount = game.Model.SourceCompatibilityRepairCount,
            compatibilityRepairs = game.Model.SourceCompatibilityRepairs.ToArray(),
            rawHandicap = game.Model.SourceTeamHandicap,
            effectiveHandicap = game.Model.SourceEffectiveTeamHandicap,
            profile = real.IsPreSnapSwitchProfile ? SourceTeamResourceProfile.PreSnapSwitchStableId :
                historical ? SourceTeamResourceProfile.C2StableId : SourceTeamResourceProfile.C2M1StableId,
            sourceData = real.C2Data.PayloadSha256, replayFileSha256 = CanonicalHash(File.ReadAllBytes(replayPath)),
            factory = real.IsPreSnapSwitchProfile ? "REAL SourceC2Composition.CreatePreSnapSwitchSimulation" :
                historical ? "REAL SourceC2Composition.CreateSimulation" : "REAL SourceC2Composition.CreateM1Simulation",
            completedHosts = completed, game.SourceTick, rawVector, rawCursor, rSkipFrames, positive,
            preSnapSwitchWitnesses, firstPreSnapSwitch,
            firstNewCallback, failure, phaseCounts, actionCounts, cache = witness.Summary(),
            originalPublicVectorRetainedWithoutStateConditionedRegeneration = true,
            sourceAudioDSPOrTargetRngOrFullGameAccepted = false };
        File.WriteAllText(Path.Combine(outputRoot, "m1-replay-result.json"), JsonSerializer.Serialize(m1ReplayEvidence, options));
        File.WriteAllText(Path.Combine(outputRoot, "m1-all-raw-draws.json"), JsonSerializer.Serialize(shared.Trace, options));
        File.WriteAllText(Path.Combine(outputRoot, "m1-R_SKIP-real-poses.json"), JsonSerializer.Serialize(poses, options));
        if (firstFailure != null) throw firstFailure;
        Check(failure == null, "M1 actual source replay stopped at first software fault; never rescue/skip a wake");
        Check(completed == 12000 && shared.Trace.Count == rawCursor,
            "M1 full immutable12000 replay retains every input and source draw");
        if (observed)
            Check(witness.Calls == witness.Frames * 14 && witness.CurrentRows == witness.Frames * 210 &&
                witness.ReferenceRows == witness.CurrentRows && witness.PreviousRows > 0 &&
                witness.ContactFrames > 0 && witness.DisplacedActors > 0,
                "M1 full actual-factory strict CURRENT/PREV14x15 witness includes every contact actor");
        if (!historical)
            Check(m1ReplayPositive, "M1 full public R_SKIP gate requires after2299 same-instance two ordinals and two real15 poses");
        if (real.IsPreSnapSwitchProfile)
            Check(preSnapSwitchWitnesses > 0, "new full profile requires its own real fresh-B transfer/hop/resolved-sound witness");
    }

    private static int AuthorPreSnapPublicPolicy(string policyPath, IDictionary<string, object> summary)
    {
        Check(real.IsPreSnapSwitchProfile && !real.IsC2M1Profile &&
            gameplayCompatibility == SourceGameplayCompatibility.Portable,
            "new authoring generation requires its actual new factory, never an old M1 alias");
        byte[] policyBytes = File.ReadAllBytes(policyPath);
        string policySha = CanonicalHash(policyBytes);
        int[] rawVector;
        using (JsonDocument document = JsonDocument.Parse(policyBytes))
        {
            JsonElement policy = document.RootElement;
            Check(policy.GetProperty("recordingGeneration").GetString() == "source-defensive-presnap-switch/v4" &&
                policy.GetProperty("maxHosts").GetInt32() == 12000 &&
                policy.GetProperty("maximumInitialWindows").GetInt32() == 1 &&
                policy.GetProperty("controllerSha256").GetString() ==
                    "e123e5db745acb3d4952dc0f56a2379eea36515b90bcd5c375dc769b797b330d",
                "new generation preserves the approved controller and exactly one authoring window");
            foreach (JsonProperty pin in policy.GetProperty("frozenCodeSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(Path.Combine(policy.GetProperty("worktree").GetString(),pin.Name))) ==
                    pin.Value.GetString(),"new authoring source freeze changed: "+pin.Name);
            foreach (JsonProperty pin in policy.GetProperty("frozenInputSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(pin.Name)) == pin.Value.GetString(),"new authoring data freeze changed: "+pin.Name);
            rawVector = policy.GetProperty("rawVector").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        }
        Check(rawVector.Length == 15 && CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(rawVector)) ==
            "bbf469b9c4de0ce8227ea126fa33510d927f815d879281dbcefd26dabc897827",
            "new authoring uses the same seed1/provider/unfiltered15-entry vector");
        string markerPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(policyPath)),"only-public-window-started.json");
        using (var marker = new FileStream(markerPath,FileMode.CreateNew))
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { policySha,maximumInitialWindows=1,maxHosts=12000,
                recordingGeneration="source-defensive-presnap-switch/v4" });
            marker.Write(bytes,0,bytes.Length);
        }
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera:false,random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length],new SourceRandomProvenance(
                "source-sound-smoke/raw-vector-v1","Same unfiltered raw vector shared by ALL sites, not native PRNG",
                "new282 actual composition entry; approved single human; explicit FourStation layout",SourceRandomEvidence.ExternalUnverified)),
            compatibility:SourceGameplayCompatibility.Portable);
        Check(game.SourceDefensivePreSnapSwitchEnabled && shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560"),
            "new authoring retains both actual bootstrap draws before AI on the same provider");
        var cache = new CanonicalWitness(game,false);
        game.SourceCameraHeadingProvider = (state,tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game,state,tick);
            cache.Camera(tick);return heading;
        };
        var controller = new PortablePublicController();
        var packets = new PortableCabinetPackets();
        var options = new JsonSerializerOptions { IncludeFields = true };
        var first = new Dictionary<string,Tuple<long,int,string>>();
        var poses = new List<object>();
        var switches = new List<object>();
        var rawDefendingB = new List<object>();
        var faults = new List<object>();
        PreSnapPublicEdge pendingPublicB = null;
        int attempted = 0,completed = 0,rSkipFrames = 0;
        object positive = null;
        string inputPath = Path.Combine(outputRoot,"captured-public-commands.jsonl");
        using (var commands = new StreamWriter(inputPath,false,new UTF8Encoding(false)))
        using (var observations = new StreamWriter(Path.Combine(outputRoot,"public-window-observations.jsonl"),false,new UTF8Encoding(false)))
        {
            for (int host=0;host<12000;host++)
            {
                PortableVisible visible = PortableVisible.Project(game.State);
                PortableIntent intent = controller.Choose(visible);
                PlayerCommand command = packets.Encode(intent);
                PreSnapPublicEdge captured = CapturePublicB(host,game,command,pendingPublicB);
                if (!ReferenceEquals(captured,pendingPublicB) && captured.Visible.Phase == GamePhase.PreSnap &&
                    captured.Visible.Possession == TeamId.Away)
                    rawDefendingB.Add(new { host,before=game.SourceTick,visible,intent,command });
                pendingPublicB = captured;
                commands.WriteLine(JsonSerializer.Serialize(new { host,command },options));commands.Flush();attempted++;
                long before = game.SourceTick;
                try { game.Step(command);cache.Complete(before);completed++; }
                catch (Exception error)
                {
                    object fault = new { host,before,game.SourceTick,command,error=error.ToString(),
                        actual=CanonicalObservation(game,host,rawCursor,null),allSharedRawDraws=shared.Trace };
                    faults.Add(fault);
                    File.WriteAllText(Path.Combine(outputRoot,"first-source-fault.json"),JsonSerializer.Serialize(fault,options));
                }
                SourceSoundVmDispatch[] sounds = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
                if (faults.Count == 0)
                    try
                    {
                        foreach (SourceSoundVmDispatch sound in sounds.Where(s=>s.C2Context != null &&
                            s.C2Context.Kind == SourceC2SoundFunctionKind.AttackSoundFixed))
                        {
                            object actual = RequirePublicSwitchWitness(host,game,sound,pendingPublicB);
                            if (pendingPublicB.Visible.Phase == GamePhase.PreSnap &&
                                pendingPublicB.Visible.Possession == TeamId.Away) switches.Add(actual);
                        }
                    }
                    catch (Exception error)
                    {
                        object fault = new { host,before,game.SourceTick,command,error=error.ToString(),
                            boundary="actual-public-B-witness",actual=CanonicalObservation(game,host,rawCursor,null),
                            allSharedRawDraws=shared.Trace };
                        faults.Add(fault);
                        File.WriteAllText(Path.Combine(outputRoot,"first-public-witness-fault.json"),
                            JsonSerializer.Serialize(fault,options));
                    }
                if (game.SourceTick != before) pendingPublicB = null;
                if (faults.Count != 0) break;
                if (game.SourceTick != before)
                    foreach (ActorState actor in game.Model.Players)
                    {
                        SourceAnimationLayerState layer = actor.AnimationState.Primary;
                        if (layer.SequenceName != "R_SKIP") continue;
                        rSkipFrames++;
                        if (game.SourceTick <= 2299 || !layer.IsSpecified || layer.SourceTick != game.SourceTick) continue;
                        PlayerView view = GameSnapshot.SourcePlayerView(actor,game.SourceTick,true);
                        SourceEvaluatedPose local = SourceActorPoseSampler.Evaluate(library,real.ReferencePivots,view,view,1f);
                        SourceVector3[] joints = Enumerable.Range(0,15).Select(j=>game.Model.SourceCameraJoints[actor.Id,j]).ToArray();
                        Check(game.Model.SourceCameraJointTick == game.SourceTick &&
                            joints.All(j=>!float.IsNaN(j.X)&&!float.IsInfinity(j.X)&&!float.IsNaN(j.Y)&&
                                !float.IsInfinity(j.Y)&&!float.IsNaN(j.Z)&&!float.IsInfinity(j.Z)),
                            "new recording retains the normal full gate's actual15 cached joints");
                        string pose = JsonSerializer.Serialize(local.Joints.Select(j=>j.Position).ToArray(),options);
                        poses.Add(new { host,game.SourceTick,actor=actor.Id,layer.InstanceId,ordinal=layer.FrameOrdinal,
                            real15Joints=joints,real15LocalPose=local.Joints.Select(j=>j.Position).ToArray(),view });
                        string key = actor.Id+"/"+layer.InstanceId;
                        Tuple<long,int,string> earlier;
                        if (!first.TryGetValue(key,out earlier)) first[key]=Tuple.Create(game.SourceTick,layer.FrameOrdinal,pose);
                        else if (positive == null && game.SourceTick > earlier.Item1 && layer.FrameOrdinal != earlier.Item2 && pose != earlier.Item3)
                            positive = new { actor=actor.Id,layer.InstanceId,firstTick=earlier.Item1,secondTick=game.SourceTick,
                                firstOrdinal=earlier.Item2,secondOrdinal=layer.FrameOrdinal };
                    }
                observations.WriteLine(JsonSerializer.Serialize(new { host,before,game.SourceTick,visible,intent,command,
                    game.State,game.SourceEvents,rawCursor,game.Model.SourceRandomProcess,game.Model.SourceRandomProcessOrder,
                    game.Model.SourceRandomCallIndex,
                    sounds=sounds.Select(s=>new { s.Status,c2Context=CanonicalSoundContext(s.C2Context),s.C2StateAtCall,
                        calls=s.Result == null ? null : s.Result.Calls.Select(c=>new {
                            c.Bank,c.PlaylistId,c.Volume,c.Pan,c.PriorityWord,c.CalleeSourceSite }) }),
                    stateEventCacheSha256=CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                        CanonicalObservation(game,host,rawCursor,null),options)) },options));
            }
        }
        Check(shared.Trace.Count == rawCursor,"new one-window recording retains every shared raw draw");
        object receipt = new { schema="presnap-switch-public-policy-first-window/v1",
            recordingGeneration="source-defensive-presnap-switch/v4",policySha256=policySha,
            maximumInitialWindows=1,initialExecutions=1,attemptedHosts=attempted,completedHosts=completed,game.SourceTick,
            rawCursor,rawVector,rSkipFrames,positive,failure=faults.FirstOrDefault(),faults,
            preSnapSwitchWitnesses=switches.Count,profile=SourceTeamResourceProfile.PreSnapSwitchStableId,
            rawDefendingBInputs=rawDefendingB.Count,
            sourceData=real.C2Data.PayloadSha256,factory="REAL SourceC2Composition.CreatePreSnapSwitchSimulation",
            gameplayCompatibility=game.Compatibility.ToString(),publicInputSha256=CanonicalHash(File.ReadAllBytes(inputPath)),
            cache=cache.Summary(),fullM1OrCIOrNativeOrWholeGameQualified=false };
        File.WriteAllText(Path.Combine(outputRoot,"public-window-result.json"),JsonSerializer.Serialize(receipt,options));
        File.WriteAllText(Path.Combine(outputRoot,"all-shared-raw-draws.json"),JsonSerializer.Serialize(shared.Trace,options));
        File.WriteAllText(Path.Combine(outputRoot,"R_SKIP-real-poses.json"),JsonSerializer.Serialize(poses,options));
        File.WriteAllText(Path.Combine(outputRoot,"pre-snap-switch-witnesses.json"),JsonSerializer.Serialize(switches,options));
        File.WriteAllText(Path.Combine(outputRoot,"raw-defending-B-inputs.json"),JsonSerializer.Serialize(rawDefendingB,options));
        summary["status"] = faults.Count != 0 ? "STOP-new-source-fault" :
            completed != 12000 || game.SourceTick != 11400 ? "STOP-incomplete-window" :
            positive == null ? "UNMET-public-R_SKIP" : switches.Count == 0 ? "UNMET-fresh-B-switch" :
            "recorded-new-window-full-qualification-required";
        summary["public_policy_receipt"]=receipt;summary["assertions"]=assertions;
        Console.WriteLine(JsonSerializer.Serialize(summary,options));
        return faults.Count != 0 || completed != 12000 ? 2 : positive == null || switches.Count == 0 ? 1 : 0;
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
