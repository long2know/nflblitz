using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class OriginalM1OpenSpaceEntry
{
    private static int Main(string[] args) { return Program.OriginalM1OpenSpaceMain(args); }
}

internal static partial class Program
{
    private const string OriginalM1Case = "trips-rollout-clearance-original-m1-v8-12000";
    private const string OriginalM1Generation = "source-public-open-space-original-m1/v8";

    internal static int OriginalM1OpenSpaceMain(string[] args)
    {
        if (args.Length == 1 && args[0] == "policy-checks") return OpenSpacePolicyChecks();
        try
        {
            if (args.Length != 5)
                throw new ArgumentException("OriginalM1OpenSpaceChecks fixture output sound-functions original-m1-inputs policy");
            outputRoot = Path.GetFullPath(args[1]);
            fixtureRoot = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            Directory.CreateDirectory(outputRoot);
            gameplayCompatibility = SourceGameplayCompatibility.Portable;
            real = FullKernelInputs.Load(args[0], movementLibrary: true,
                soundFunctionsPath: args[2], c2M1InputsPath: args[3]);
            library = real.Animations; geometry = real.Geometry; playbook = real.Playbook;
            rosters = real.Rosters; movement = real.MovementRuntime;
            return AuthorOriginalM1OpenSpace(args[4]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }
        finally { SaveExecutionCounts(); }
    }

    private static int AuthorOriginalM1OpenSpace(string policyPath)
    {
        Check(real.IsC2M1Profile && !real.IsPreSnapSwitchProfile &&
            real.Animations.SequenceNames.Count == 281 && real.Animations.FrameCount == 26230,
            "original-M1 authoring requires the actual281/26230 factory, never the enabled282 alias");
        byte[] policyBytes = File.ReadAllBytes(policyPath);
        string policySha = CanonicalHash(policyBytes);
        int[] rawVector;
        using (JsonDocument document = JsonDocument.Parse(policyBytes))
        {
            JsonElement policy = document.RootElement;
            string root = policy.GetProperty("worktree").GetString();
            Check(policy.GetProperty("case").GetString() == OriginalM1Case &&
                policy.GetProperty("recordingGeneration").GetString() == OriginalM1Generation &&
                policy.GetProperty("version").GetString() == OpenSpaceVersion &&
                policy.GetProperty("controllerSha256").GetString() ==
                    "7dfbfa794319d371b2b5e73a5c4f1d297b2a8788946cdab2041ca30ab56b8da1" &&
                policy.GetProperty("maxHosts").GetInt32() == 12000 &&
                policy.GetProperty("maximumInitialWindows").GetInt32() == 1 &&
                policy.GetProperty("seed").GetInt32() == 1 &&
                policy.GetProperty("providerProfile").GetString() == "source-sound-smoke/raw-vector-v1" &&
                policy.GetProperty("sourceManifestSha256").GetString() ==
                    "a061fa40aa67ce792672a13ade4aef7ad1eda0fc141d1694e1c0e9b4ec52c507" &&
                policy.GetProperty("configuration").GetProperty("stationLayout").GetInt32() == 2 &&
                policy.GetProperty("startupObject").GetString() == "OriginalM1OpenSpaceEntry" &&
                Path.GetFullPath(policyPath) == Path.Combine(root,
                    ".squad/.scratch/original-m1-v8-authority/prospective-policy.json"),
                "original-M1 authoring requires its own frozen one-window authority and unchanged controller");
            foreach (JsonProperty pin in policy.GetProperty("frozenCodeSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(Path.Combine(root, pin.Name))) == pin.Value.GetString(),
                    "original-M1 authoring frozen code changed: " + pin.Name);
            foreach (JsonProperty pin in policy.GetProperty("frozenInputSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(pin.Name)) == pin.Value.GetString(),
                    "original-M1 authoring frozen input changed: " + pin.Name);
            Check(CanonicalHash(File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location)) ==
                policy.GetProperty("entrypointSha256").GetString(),
                "original-M1 authoring executes its sealed separate entrypoint PE");
            rawVector = policy.GetProperty("rawVector").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        }
        Check(rawVector.Length == 15 && CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(rawVector)) ==
            "bbf469b9c4de0ce8227ea126fa33510d927f815d879281dbcefd26dabc897827",
            "original-M1 retains the original seed1 unfiltered global raw vector");
        using (var marker = new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(policyPath)),
            "only-public-window-started.json"), FileMode.CreateNew))
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { policySha, maximumInitialWindows = 1,
                maxHosts = 12000, recordingGeneration = OriginalM1Generation });
            marker.Write(bytes, 0, bytes.Length);
            marker.Flush(true);
        }
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera: false, random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length], new SourceRandomProvenance(
                "source-sound-smoke/raw-vector-v1", "Same unfiltered raw vector shared by ALL sites, not native PRNG",
                "original281 M1 composition; one human; original seed1", SourceRandomEvidence.ExternalUnverified)),
            compatibility: SourceGameplayCompatibility.Portable);
        Check(!game.SourceDefensivePreSnapSwitchEnabled && shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560"),
            "original-M1 retains both original bootstrap draws and disabled successor switch");
        var cache = new CanonicalWitness(game, false);
        game.SourceCameraHeadingProvider = (state, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, state, tick);
            cache.Camera(tick); return heading;
        };
        var controller = new OpenSpaceController();
        var packets = new PortableCabinetPackets();
        var options = new JsonSerializerOptions { IncludeFields = true };
        var first = new Dictionary<string, Tuple<long, int, string>>();
        var poses = new List<object>();
        var faults = new List<object>();
        int attempted = 0, completed = 0, rSkipFrames = 0;
        object positive = null;
        string inputPath = Path.Combine(outputRoot, "captured-public-commands.jsonl");
        using (var commands = new StreamWriter(inputPath, false, new UTF8Encoding(false)))
        using (var observations = new StreamWriter(Path.Combine(outputRoot, "public-window-observations.jsonl"),
            false, new UTF8Encoding(false)))
        {
            for (int host = 0; host < 12000; host++)
            {
                OpenSpaceVisible visible = OpenSpaceVisible.Project(game.State);
                PortableIntent intent = controller.Choose(visible);
                PlayerCommand command = packets.Encode(intent);
                commands.WriteLine(JsonSerializer.Serialize(new { host, command }, options));
                commands.Flush(); attempted++;
                long before = game.SourceTick;
                try
                {
                    game.Step(command); cache.Complete(before); completed++;
                    game.TakeSourceSoundVmDispatches();
                    if (game.SourceTick != before)
                        foreach (ActorState actor in game.Model.Players)
                        {
                            SourceAnimationLayerState layer = actor.AnimationState.Primary;
                            if (layer.SequenceName != "R_SKIP") continue;
                            rSkipFrames++;
                            if (game.SourceTick <= 2299 || !layer.IsSpecified || layer.SourceTick != game.SourceTick) continue;
                            ISourceAnimationClip clip = library.GetClip("R_SKIP");
                            Check(layer.FrameOrdinal >= 0 && layer.FrameOrdinal < clip.SelectedSourceFrames.Count,
                                "original-M1 R_SKIP is an actual selected original ordinal after2299");
                            PlayerView view = GameSnapshot.SourcePlayerView(actor, game.SourceTick, true);
                            SourceEvaluatedPose local = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots, view, view, 1f);
                            SourceVector3[] joints = Enumerable.Range(0, 15).Select(j => game.Model.SourceCameraJoints[actor.Id, j]).ToArray();
                            Check(game.Model.SourceCameraJointTick == game.SourceTick && local.Joints.Count == 15 &&
                                joints.All(j => !float.IsNaN(j.X) && !float.IsInfinity(j.X) && !float.IsNaN(j.Y) &&
                                    !float.IsInfinity(j.Y) && !float.IsNaN(j.Z) && !float.IsInfinity(j.Z)),
                                "original-M1 R_SKIP retains all15 actual cached and local pose joints");
                            string pose = JsonSerializer.Serialize(local.Joints.Select(j => j.Position).ToArray(), options);
                            poses.Add(new { host, game.SourceTick, actor = actor.Id, layer.InstanceId, ordinal = layer.FrameOrdinal,
                                real15Joints = joints, real15LocalPose = local.Joints.Select(j => j.Position).ToArray(), view });
                            string key = actor.Id + "/" + layer.InstanceId;
                            Tuple<long, int, string> earlier;
                            if (!first.TryGetValue(key, out earlier)) first[key] = Tuple.Create(game.SourceTick, layer.FrameOrdinal, pose);
                            else if (positive == null && game.SourceTick > earlier.Item1 &&
                                layer.FrameOrdinal != earlier.Item2 && pose != earlier.Item3)
                                positive = new { actor = actor.Id, layer.InstanceId, firstTick = earlier.Item1,
                                    secondTick = game.SourceTick, firstOrdinal = earlier.Item2, secondOrdinal = layer.FrameOrdinal };
                        }
                }
                catch (Exception error)
                {
                    object fault = new { host, before, game.SourceTick, command, error = error.ToString(),
                        actual = CanonicalObservation(game, host, rawCursor, null), allSharedRawDraws = shared.Trace };
                    faults.Add(fault);
                    File.WriteAllText(Path.Combine(outputRoot, "first-source-fault.json"), JsonSerializer.Serialize(fault, options));
                }
                observations.WriteLine(JsonSerializer.Serialize(new { host, before, game.SourceTick, visible, intent,
                    game.State.Phase, game.State.PlayNumber, game.State.Ball, game.SourceEvents, rawCursor,
                    stateEventCacheSha256 = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                        CanonicalObservation(game, host, rawCursor, null), options)) }, options));
                if (faults.Count != 0) break;
            }
        }
        Check(shared.Trace.Count == rawCursor, "original-M1 recording retains ALL unfiltered shared raw draws");
        object receipt = new { schema = "source-original-M1-open-space-first-window/v1", name = OpenSpaceName,
            version = OpenSpaceVersion, recordingGeneration = OriginalM1Generation, policySha256 = policySha,
            maximumInitialWindows = 1, initialExecutions = 1, attemptedHosts = attempted, completedHosts = completed,
            game.SourceTick, rawCursor, rawVector, rSkipFrames, positive, failure = faults.FirstOrDefault(), faults,
            profile = SourceTeamResourceProfile.C2M1StableId, sourceData = real.C2Data.PayloadSha256,
            factory = "REAL SourceC2Composition.CreateM1Simulation", gameplayCompatibility = game.Compatibility.ToString(),
            publicInputSha256 = CanonicalHash(File.ReadAllBytes(inputPath)), cache = cache.Summary(),
            fullM1OrCIOrNativeOrWholeGameQualified = false };
        File.WriteAllText(Path.Combine(outputRoot, "public-window-result.json"), JsonSerializer.Serialize(receipt, options));
        File.WriteAllText(Path.Combine(outputRoot, "all-shared-raw-draws.json"), JsonSerializer.Serialize(shared.Trace, options));
        File.WriteAllText(Path.Combine(outputRoot, "R_SKIP-real-poses.json"), JsonSerializer.Serialize(poses, options));
        string status = faults.Count != 0 ? "STOP-new-source-fault" :
            completed != 12000 || game.SourceTick != 11400 ? "STOP-incomplete-window" :
            positive == null ? "UNMET-public-R_SKIP" : "recorded-new-window-full-qualification-required";
        Console.WriteLine(JsonSerializer.Serialize(new { status, assertions, receipt }, options));
        return faults.Count != 0 || completed != 12000 || game.SourceTick != 11400 ? 2 : positive == null ? 1 : 0;
    }
}
