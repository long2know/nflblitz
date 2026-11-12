using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class OriginalM1PreventExhibitionEntry
{
    private static int Main(string[] args) { return Program.OriginalM1PreventExhibitionMain(args); }
}

internal static partial class Program
{
    private const string PreventExhibitionCase = "running-taunt-exhibition-prevent-original-m1-v9-12000";
    private const string PreventExhibitionGeneration = "source-public-running-taunt-exhibition-original-m1/v9";
    private const string PreventExhibitionVersion = "source-running-taunt-exhibition-prevent-controller/v1";

    private sealed class PreventExhibitionController
    {
        private readonly OpenSpaceController controller = new OpenSpaceController();

        internal PortableIntent Choose(OpenSpaceVisible view)
        {
            PortableIntent original = controller.Choose(view);
            if (view.Phase != GamePhase.LiveBall || view.Possession != TeamId.Away) return original;
            OpenSpacePlayer[] humans = view.Players.Where(p => p.Id == view.ControlledId && p.Team == TeamId.Home).ToArray();
            OpenSpacePlayer[] carriers = view.Players.Where(p => p.Id == view.CarrierId).ToArray();
            if (humans.Length != 1 || carriers.Length != 1) return original;
            OpenSpacePlayer human = humans[0], carrier = carriers[0];
            if (carrier.Team == human.Team) return original;
            float fieldX = 100f - carrier.Position.X;
            if (fieldX < 75f || fieldX >= 95f) return original;
            float dx = human.Position.X - carrier.Position.X;
            float dz = human.Position.Z - carrier.Position.Z;
            SourceButtons held = original.Held & ~SourceButtons.A;
            if (dx * dx + dz * dz >= 64f)
                return new PortableIntent(held: held, decision: "exhibition-prevent-hold-eight-yard-cushion");
            if (dx == 0f && dz == 0f) dx = 1f;
            return PortablePublicController.World(dx, dz, held, "exhibition-prevent-retreat-eight-yard-cushion");
        }
    }

    private static bool SamePreventIntent(PortableIntent first, PortableIntent second)
    {
        return JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
    }

    private static OpenSpaceVisible PreventVisible(float carrierX, float humanX, float humanZ = 0f,
        float turbo = .8f, TeamId possession = TeamId.Away, TeamId carrierTeam = TeamId.Away,
        GamePhase phase = GamePhase.LiveBall, int carrierId = 12, int controlledId = 4)
    {
        return new OpenSpaceVisible(phase, possession, controlledId, carrierId, new[] {
            new OpenSpacePlayer(4, TeamId.Home, new FieldVector(humanX, humanZ), ActorRole.Defender, turbo),
            new OpenSpacePlayer(12, carrierTeam, new FieldVector(carrierX, 0f), ActorRole.Receiver),
            new OpenSpacePlayer(6, TeamId.Home, new FieldVector(50f, 15f), ActorRole.Defender),
            new OpenSpacePlayer(13, TeamId.Away, new FieldVector(45f, -15f), ActorRole.Receiver)
        });
    }

    private static int PreventExhibitionPolicyChecks()
    {
        var baseline = new OpenSpaceController();
        var wrapped = new PreventExhibitionController();
        Action<OpenSpaceVisible, int> unchanged = (view, hosts) => {
            for (int host = 0; host < hosts; host++)
                Check(SamePreventIntent(baseline.Choose(view), wrapped.Choose(view)),
                    "prevent wrapper delegates every input outside exact defensive band");
        };
        unchanged(PreventVisible(20f, 22f, phase: GamePhase.PlaySelect), 60);
        unchanged(PreventVisible(20f, 22f, phase: GamePhase.PreSnap), 90);
        unchanged(PreventVisible(20f, 22f, phase: GamePhase.DeadBall), 40);
        unchanged(PreventVisible(25.001f, 27.001f), 5);
        unchanged(PreventVisible(5f, 7f), 5);
        unchanged(PreventVisible(1f, 3f), 5);
        unchanged(PreventVisible(20f, 22f, possession: TeamId.Home), 5);
        unchanged(PreventVisible(20f, 22f, carrierTeam: TeamId.Home), 5);
        unchanged(PreventVisible(80f, 82f, carrierTeam: TeamId.Home), 5);
        unchanged(PreventVisible(20f, 22f, carrierId: -1), 5);
        unchanged(PreventVisible(20f, 22f, controlledId: -1), 5);
        var offense = new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Home, 4, 4, new[] {
            new OpenSpacePlayer(4, TeamId.Home, new FieldVector(82f, 0f), ActorRole.Receiver),
            new OpenSpacePlayer(12, TeamId.Away, new FieldVector(60f, 0f), ActorRole.Defender)
        });
        unchanged(offense, 100);
        foreach (float x in new[] { 25f, 20f, 5.001f })
        {
            var view = PreventVisible(x, x + 2f);
            PortableIntent original = baseline.Choose(view), actual = wrapped.Choose(view);
            PortableIntent expected = PortablePublicController.World(2f, 0f, original.Held & ~SourceButtons.A, "expected");
            Check(actual.X == expected.X && actual.Z == expected.Z &&
                actual.Decision == "exhibition-prevent-retreat-eight-yard-cushion",
                "prevent wrapper retreats directly away only inside inclusive75 exclusive95");
            Check((actual.Held & SourceButtons.C) == (original.Held & SourceButtons.C) &&
                (actual.Held & SourceButtons.A) == SourceButtons.None,
                "prevent wrapper preserves original turbo and removes only defensive tackle");
        }
        foreach (float gap in new[] { 7f, 7.999f, 8f, 9f })
        {
            var view = PreventVisible(20f, 20f + gap);
            PortableIntent original = baseline.Choose(view), actual = wrapped.Choose(view);
            Check(gap < 8f ? actual.X != 0 || actual.Z != 0 : actual.X == 0 && actual.Z == 0,
                "prevent wrapper exact8 yard threshold retreats below and holds at or above");
            Check(actual.Held == (original.Held & ~SourceButtons.A),
                "prevent wrapper never invents or drops original defensive turbo output");
        }
        var lateral = PreventVisible(20f, 20f, -2f);
        PortableIntent lateralActual = wrapped.Choose(lateral);
        PortableIntent lateralExpected = PortablePublicController.World(0f, -2f, SourceButtons.C, "expected");
        Check(lateralActual.X == lateralExpected.X && lateralActual.Z == lateralExpected.Z,
            "prevent wrapper retreats from published lateral position without carrier-velocity prediction");
        var low = PreventVisible(20f, 22f, turbo: .05f);
        PortableIntent lowOriginal = baseline.Choose(low), lowActual = wrapped.Choose(low);
        Check(lowActual.Held == (lowOriginal.Held & ~SourceButtons.A) && lowActual.Held == SourceButtons.None,
            "prevent wrapper preserves original low-resource C release");
        PortableIntent overlap = wrapped.Choose(PreventVisible(20f, 20f));
        PortableIntent overlapExpected = PortablePublicController.World(1f, 0f, SourceButtons.C, "expected");
        Check(overlap.X == overlapExpected.X && overlap.Z == overlapExpected.Z,
            "prevent wrapper coincident public points use the prospectively fixed positiveX escape");
        unchanged(offense, 60);
        Check(executionConstructors == 0 && executionGames.Count == 0 && executionAnimationAdvances == 0,
            "prevent synthetic checks never construct a game or advance source animation");
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-prevent-exhibition-policy-checks/v1",
            status = "passed", assertions, actualGameConstructions = executionConstructors, actualGameSteps = 0,
            directAnimationAdvanceCalls = executionAnimationAdvances }));
        return 0;
    }

    internal static int OriginalM1PreventExhibitionMain(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "original-policy-checks") return OpenSpacePolicyChecks();
            if (args.Length == 1 && args[0] == "prevent-policy-checks") return PreventExhibitionPolicyChecks();
            if (args.Length != 5)
                throw new ArgumentException("OriginalM1PreventExhibitionChecks fixture output sound-functions original-m1-inputs policy");
            outputRoot = Path.GetFullPath(args[1]);
            fixtureRoot = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            Directory.CreateDirectory(outputRoot);
            gameplayCompatibility = SourceGameplayCompatibility.Portable;
            real = FullKernelInputs.Load(args[0], movementLibrary: true,
                soundFunctionsPath: args[2], c2M1InputsPath: args[3]);
            library = real.Animations; geometry = real.Geometry; playbook = real.Playbook;
            rosters = real.Rosters; movement = real.MovementRuntime;
            return AuthorOriginalM1PreventExhibition(args[4]);
        }
        catch (MovementCallerAssertion error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-prevent-exhibition-policy-checks/v1",
                status = "failed", assertions, failed_assertion = error.Message,
                actualGameConstructions = executionConstructors, actualGameSteps = 0 }));
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }
        finally { SaveExecutionCounts(); }
    }

    private static int AuthorOriginalM1PreventExhibition(string policyPath)
    {
        Check(real.IsC2M1Profile && !real.IsPreSnapSwitchProfile &&
            real.Animations.SequenceNames.Count == 281 && real.Animations.FrameCount == 26230,
            "prevent exhibition requires actual original281/26230, never enabled282");
        byte[] policyBytes = File.ReadAllBytes(policyPath);
        string policySha = CanonicalHash(policyBytes);
        int[] rawVector;
        using (JsonDocument document = JsonDocument.Parse(policyBytes))
        {
            JsonElement policy = document.RootElement;
            string root = policy.GetProperty("worktree").GetString();
            Check(policy.GetProperty("case").GetString() == PreventExhibitionCase &&
                policy.GetProperty("recordingGeneration").GetString() == PreventExhibitionGeneration &&
                policy.GetProperty("version").GetString() == PreventExhibitionVersion &&
                policy.GetProperty("baseControllerVersion").GetString() == OpenSpaceVersion &&
                policy.GetProperty("controllerSha256").GetString() ==
                    "7dfbfa794319d371b2b5e73a5c4f1d297b2a8788946cdab2041ca30ab56b8da1" &&
                policy.GetProperty("maxHosts").GetInt32() == 12000 &&
                policy.GetProperty("maximumInitialWindows").GetInt32() == 1 &&
                policy.GetProperty("seed").GetInt32() == 1 &&
                policy.GetProperty("providerProfile").GetString() == "source-sound-smoke/raw-vector-v1" &&
                policy.GetProperty("sourceManifestSha256").GetString() ==
                    "a061fa40aa67ce792672a13ade4aef7ad1eda0fc141d1694e1c0e9b4ec52c507" &&
                policy.GetProperty("configurationSha256").GetString() ==
                    "1d377a5738f41be8393fbd7bb77789f6fb665643b22e4dbc0f6a32908ba10239" &&
                policy.GetProperty("startupObject").GetString() == "OriginalM1PreventExhibitionEntry" &&
                Path.GetFullPath(policyPath) == Path.Combine(root,
                    ".squad/.scratch/original-m1-v9-prevent-authority/prospective-policy.json"),
                "prevent exhibition requires distinct frozen final-window authority and unchanged base controller");
            foreach (JsonProperty pin in policy.GetProperty("frozenCodeSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(Path.Combine(root, pin.Name))) == pin.Value.GetString(),
                    "prevent exhibition frozen code changed: " + pin.Name);
            foreach (JsonProperty pin in policy.GetProperty("frozenInputSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(pin.Name)) == pin.Value.GetString(),
                    "prevent exhibition frozen input changed: " + pin.Name);
            Check(CanonicalHash(File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location)) ==
                policy.GetProperty("entrypointSha256").GetString(),
                "prevent exhibition executes the sealed separate authoring PE");
            rawVector = policy.GetProperty("rawVector").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        }
        Check(rawVector.Length == 15 && CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(rawVector)) ==
            "bbf469b9c4de0ce8227ea126fa33510d927f815d879281dbcefd26dabc897827",
            "prevent exhibition retains unchanged seed1 unfiltered raw provider");
        using (var marker = new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(policyPath)),
            "only-public-window-started.json"), FileMode.CreateNew))
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { policySha, maximumInitialWindows = 1,
                maxHosts = 12000, recordingGeneration = PreventExhibitionGeneration });
            marker.Write(bytes, 0, bytes.Length);
            marker.Flush(true);
        }
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera: false, random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length], new SourceRandomProvenance(
                "source-sound-smoke/raw-vector-v1", "Same unfiltered raw vector shared by ALL sites, not native PRNG",
                "original281 M1 exhibition; one human; original seed1", SourceRandomEvidence.ExternalUnverified)),
            compatibility: SourceGameplayCompatibility.Portable);
        Check(!game.SourceDefensivePreSnapSwitchEnabled && shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560"),
            "prevent exhibition retains both original bootstrap draws and disabled successor switch");
        var cache = new CanonicalWitness(game, false);
        game.SourceCameraHeadingProvider = (state, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, state, tick);
            cache.Camera(tick); return heading;
        };
        var controller = new PreventExhibitionController();
        var packets = new PortableCabinetPackets();
        var options = new JsonSerializerOptions { IncludeFields = true };
        var first = new Dictionary<string, Tuple<long, int, string>>();
        var poses = new List<object>();
        var faults = new List<object>();
        int attempted = 0, completed = 0, rSkipFrames = 0, preventHosts = 0;
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
                if (intent.Decision != null && intent.Decision.StartsWith("exhibition-prevent-", StringComparison.Ordinal))
                    preventHosts++;
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
                                "prevent exhibition R_SKIP is an actual selected original ordinal after2299");
                            PlayerView view = GameSnapshot.SourcePlayerView(actor, game.SourceTick, true);
                            SourceEvaluatedPose local = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots, view, view, 1f);
                            SourceVector3[] joints = Enumerable.Range(0, 15).Select(j => game.Model.SourceCameraJoints[actor.Id, j]).ToArray();
                            Check(game.Model.SourceCameraJointTick == game.SourceTick && local.Joints.Count == 15 &&
                                joints.All(j => !float.IsNaN(j.X) && !float.IsInfinity(j.X) && !float.IsNaN(j.Y) &&
                                    !float.IsInfinity(j.Y) && !float.IsNaN(j.Z) && !float.IsInfinity(j.Z)),
                                "prevent exhibition R_SKIP retains all15 actual cached and local pose joints");
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
                    afterVisible = OpenSpaceVisible.Project(game.State), game.State.Phase, game.State.PlayNumber,
                    game.State.Ball, game.SourceEvents, rawCursor,
                    stateEventCacheSha256 = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                        CanonicalObservation(game, host, rawCursor, null), options)) }, options));
                if (faults.Count != 0) break;
            }
        }
        Check(shared.Trace.Count == rawCursor, "prevent exhibition retains ALL unfiltered shared raw draws");
        object receipt = new { schema = "source-original-M1-prevent-exhibition-first-window/v1",
            version = PreventExhibitionVersion, baseControllerVersion = OpenSpaceVersion,
            recordingGeneration = PreventExhibitionGeneration, policySha256 = policySha,
            maximumInitialWindows = 1, initialExecutions = 1, attemptedHosts = attempted, completedHosts = completed,
            game.SourceTick, rawCursor, rawVector, rSkipFrames, positive, failure = faults.FirstOrDefault(), faults,
            preventHosts, profile = SourceTeamResourceProfile.C2M1StableId, sourceData = real.C2Data.PayloadSha256,
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
