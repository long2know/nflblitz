using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.AssetImport;
using Blitz.Presentation;

internal sealed class MovementCallerAssertion : Exception
{
    internal MovementCallerAssertion(string identity) : base(identity) { }
}

internal static partial class Program
{
    private static FullKernelInputs real;
    private static ISourceAnimationLibrary library;
    private static SourceCatchGeometry geometry;
    private static SourcePlaybook playbook;
    private static ISourceTeamRoster rosters;
    private static SourceMovementRuntimeData movement;
    private static int assertions;
    private static bool mutationRun;
    private static string outputRoot;
    private static string fixtureRoot;
    private static bool missingRSkip;
    private static long smokeSourceTick;
    private static int smokeRSkipFrames;
    private static int smokeCacheAssertions;
    private static int smokeSoundDispatches;
    private static int smokeSoundDraws;
    private static bool smokeRSkipAdvanced;
    private static SourceCacheWitness smokeCache;
    private static GameSimulation activeGame;
    private static readonly List<GameSimulation> executionGames = new List<GameSimulation>();
    private static readonly List<SourceInjectedRandom> executionRandoms = new List<SourceInjectedRandom>();
    private static readonly Dictionary<GameSimulation, long> executionInitialTicks = new Dictionary<GameSimulation, long>();
    private static int executionConstructors, executionAnimationAdvances;
    private static long executionPreviousHostSteps;

    private static void RetainExecutionRandom(ISourceRandom random)
    {
        SourceInjectedRandom injected = random as SourceInjectedRandom;
        if (injected == null) throw new InvalidOperationException("Execution accounting requires the actual injected trace.");
        if (!executionRandoms.Contains(injected)) executionRandoms.Add(injected);
    }

    private static void RetainExecutionGame(GameSimulation game)
    {
        executionGames.Add(game);
        executionInitialTicks.Add(game, game.Model.Tick);
        RetainExecutionRandom(game.SourceRandomService);
    }

    private static void RetainExecutionEpoch(GameSimulation game)
    {
        executionPreviousHostSteps += game.Model.Tick - executionInitialTicks[game];
        RetainExecutionRandom(game.SourceRandomService);
    }

    private static void SaveExecutionCounts()
    {
        if (outputRoot == null || executionConstructors == 0) return;
        foreach (GameSimulation game in executionGames) RetainExecutionRandom(game.SourceRandomService);
        long hosts = executionPreviousHostSteps + executionGames.Sum(game => game.Model.Tick - executionInitialTicks[game]);
        long raw = executionRandoms.Sum(random => (long)random.Trace.Count(draw => draw.Raw.HasValue));
        long dispatches = executionRandoms.Sum(random => (long)random.Trace.Count);
        bool bounded = hosts >= 0 && hosts + executionAnimationAdvances <= 12000 && raw <= 32768;
        File.WriteAllText(Path.Combine(outputRoot, "execution-counts.json"), JsonSerializer.Serialize(new {
            schema = "movement-driver-execution-counts/v1", gameConstructors = executionConstructors,
            hostStepCalls = hosts, directAnimationAdvanceCalls = executionAnimationAdvances,
            hostOrAdvanceCalls = hosts + executionAnimationAdvances, unfilteredRawNextValues = raw,
            randomDispatches = dispatches, withinPerDriverBudget = bounded,
            noAdditionalRandomDrawsOrSourceWakes = true
        }));
        if (!bounded) throw new InvalidOperationException("Actual movement driver host/raw allocation exceeded.");
    }

    private static bool SameBits(SourceVector3 first, SourceVector3 second)
    {
        return BitConverter.SingleToInt32Bits(first.X) == BitConverter.SingleToInt32Bits(second.X)
            && BitConverter.SingleToInt32Bits(first.Y) == BitConverter.SingleToInt32Bits(second.Y)
            && BitConverter.SingleToInt32Bits(first.Z) == BitConverter.SingleToInt32Bits(second.Z);
    }

    private sealed class CacheSample
    {
        internal PlayerView View;
        internal SourceVector3 Root;
        internal SourceMovementCameraFrame Camera;
        internal SourceVector3[] Joints;
        internal string PoseBasis;
        internal string Script, Sequence;
        internal int Ordinal;
    }

    private sealed class SourceCacheWitness : ISourceActorGeometry, ISourceActorCameraGeometry
    {
        private readonly SourceCatchGeometry inner;
        private GameSimulation game;
        private List<CacheSample> samples = new List<CacheSample>();
        private CacheSample[] previous;
        private long tick = -1, previousTick = -1;
        private SourceMovementCameraFrame executingCamera;
        private int[] executingCameraBits;
        private readonly List<object> frames = new List<object>();
        private object contact474;
        internal int AddedAssertions, GeometryCalls, CurrentRows, PreviousRows, OrdinaryRows, ReferenceRows;
        internal int ContactFrames, DisplacedActors, FrameCount, LegacyRows, SkippedHosts, CurrentMatrixFrames;

        internal SourceCacheWitness(SourceCatchGeometry geometry) { inner = geometry; }
        internal void Bind(GameSimulation simulation) { game = simulation; }
        private void Assert(bool condition, string identity, bool retainedOldCheck = false)
        {
            if (!retainedOldCheck) AddedAssertions++;
            Check(condition, identity);
        }

        private static string PoseBasis(PlayerView actor)
        {
            return JsonSerializer.Serialize(new { actor.ModelScale, actor.FacingDirection, actor.Animation },
                new JsonSerializerOptions { IncludeFields = true });
        }

        private static int[] CameraBits(SourceMovementCameraFrame camera)
        {
            return camera.Transform.Concat(new[] { camera.Position.X, camera.Position.Y, camera.Position.Z,
                camera.HorizontalResolution, camera.VerticalResolution }).Select(BitConverter.SingleToInt32Bits).ToArray();
        }

        public SourceVector3 WorldJoint(PlayerView actor, int joint) { return inner.WorldJoint(actor, joint); }

        public IReadOnlyList<SourceVector3> CameraJoints(PlayerView actor, SourceVector3 root,
            SourceMovementCameraFrame camera)
        {
            Assert(game != null && game.SourceTick == tick && game.Model.SourceRandomProcess == "player_proc"
                && game.Model.SourceCameraJointTick < tick,
                "actual cache geometry is sampled only post-player and BEFORE RL_COLLIS, never refreshed after contact");
            Assert(samples.Count < 14 && actor.Id == samples.Count,
                "actual source cache observes every actor exactly once in original0..13 order");
            ActorState live = game.Model.Players[actor.Id];
            SourceVector3 liveRoot = SourcePlayerDriver.WorldPosition(live);
            Assert(SameBits(root, liveRoot), "actual source cache call uses the real post-player canonical root");
            Assert(ReferenceEquals(camera, executingCamera) && ReferenceEquals(camera, game.Model.SourceMovementCamera)
                && CameraBits(camera).SequenceEqual(executingCameraBits) && game.Model.SourceMovementCameraTick == tick,
                "actual source cache call uses this executing source camera matrix, never an old/world transform");
            long draws = game.Model.SourceRandomDraws;
            int cursor = game.Model.SourceRandomCallIndex;
            IReadOnlyList<SourceVector3> actual = inner.CameraJoints(actor, root, camera);
            IReadOnlyList<SourceVector3> reference = inner.CameraJoints(
                GameSnapshot.SourcePlayerView(live, tick, true), liveRoot, game.Model.SourceMovementCamera);
            Assert(actual.Count == 15 && reference.Count == 15,
                "actual source cache observer delegates all15 joints to the real production geometry");
            for (int joint = 0; joint < 15; joint++)
            {
                Assert(SameBits(actual[joint], reference[joint]),
                    "actual cache call contains the current post-player pose/root/current matrix " + actor.Id + "/" + joint);
                ReferenceRows++;
            }
            Assert(game.Model.SourceRandomDraws == draws && game.Model.SourceRandomCallIndex == cursor,
                "passive real geometry observations do not draw or change the source random cursor");
            samples.Add(new CacheSample { View = actor, Root = root, Camera = camera,
                Joints = actual.ToArray(), PoseBasis = PoseBasis(actor),
                Script = live.AnimationState.Primary.ScriptSymbol,
                Sequence = live.AnimationState.Primary.SequenceName,
                Ordinal = live.AnimationState.Primary.FrameOrdinal });
            GeometryCalls++;
            return actual;
        }

        internal void CameraStage(long sourceTick)
        {
            Assert(game.Model.SourceMovementCameraTick == sourceTick,
                "new current camera is supplied before actor VMs while previous jpos stays untouched");
            if (previous == null)
                Assert(game.Model.SourceCameraJointTick == -1,
                    "initial absent source jpos generation remains explicitly unavailable, not fabricated zero geometry");
            else
            {
                Assert(game.Model.SourceCameraJointTick == previousTick,
                    "pre-player source camera stage retains the preceding actual post-player cache generation");
                for (int id = 0; id < 14; id++)
                    for (int joint = 0; joint < 15; joint++)
                    {
                        Assert(SameBits(game.Model.SourceCameraJoints[id, joint], previous[id].Joints[joint]),
                            "previous14x15 source cache survives contact until next last_player_proc " + id + "/" + joint);
                        PreviousRows++;
                    }
            }
            tick = sourceTick;
            executingCamera = game.Model.SourceMovementCamera;
            executingCameraBits = CameraBits(executingCamera);
            samples = new List<CacheSample>();
        }

        internal int AfterHostStep(long beforeTick, GamePhase beforePhase, int beforeCalls, int host)
        {
            if (game.SourceTick == beforeTick || game.SourceTick != tick)
            {
                Assert(GeometryCalls == beforeCalls && game.Model.SourceCameraJointTick == previousTick
                    && (game.SourceTick == beforeTick || game.Model.Phase == GamePhase.PlaySelect),
                    "skipped or source-suspended host wake cannot fabricate or refresh a player cache");
                SkippedHosts++;
                return 0;
            }
            Assert(samples.Count == 14 && GeometryCalls == beforeCalls + 14
                && game.Model.SourceCameraJointTick == tick,
                "actual source wake generates exactly one complete14x15 post-player cache");
            SourceMovementCameraFrame camera = samples[0].Camera;
            Assert(ReferenceEquals(camera, game.Model.SourceMovementCamera)
                && game.Model.SourceMovementCameraTick == tick
                && CameraBits(game.Model.SourceMovementCamera).SequenceEqual(executingCameraBits),
                "cached source joints and post-contact observations retain the same current source camera matrix");
            CurrentMatrixFrames++;
            bool retained = beforePhase == GamePhase.DeadBall && game.State.Phase == GamePhase.DeadBall;
            int rows = 0, retainedRows = 0, ordinary = 0;
            var displaced = new List<int>();
            for (int id = 0; id < 14; id++)
            {
                CacheSample sample = samples[id];
                ActorState live = game.Model.Players[id];
                SourceVector3 afterRoot = SourcePlayerDriver.WorldPosition(live);
                PlayerView afterView = GameSnapshot.SourcePlayerView(live, tick, true);
                IReadOnlyList<SourceVector3> afterJoints = inner.CameraJoints(afterView, afterRoot, camera);
                bool rootChanged = !SameBits(afterRoot, sample.Root);
                bool ordinaryBasis = !rootChanged && PoseBasis(afterView) == sample.PoseBasis;
                if (rootChanged) displaced.Add(id);
                IReadOnlyList<SourceVector3> expected = sample.Joints;
                for (int joint = 0; joint < 15; joint++)
                {
                    Assert(SameBits(game.Model.SourceCameraJoints[id, joint], expected[joint]),
                        "all source-time cached joint bits equal the actual PRE-RL_COLLIS geometry result " + id + "/" + joint,
                        retainedOldCheck: retained);
                    CurrentRows++; rows++;
                    if (retained) { LegacyRows++; retainedRows++; }
                    if (ordinaryBasis)
                    {
                        Assert(SameBits(game.Model.SourceCameraJoints[id, joint], afterJoints[joint]),
                            "ordinary unchanged pose/root still has exact post-step camera-joint equality " + id + "/" + joint);
                        OrdinaryRows++; ordinary++;
                    }
                }
            }
            Assert(rows == 14 * 15,
                "all14x15 cache comparisons include EVERY contact wake and EVERY displaced actor, without dropped checks");
            bool contact = game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision);
            if (contact) ContactFrames++;
            DisplacedActors += displaced.Count; FrameCount++;
            frames.Add(new { sourceTick = tick, host, cacheRows = rows, retainedOldRows = retainedRows,
                ordinaryRows = ordinary, contact, displacedActors = displaced.ToArray() });
            if (tick == 474)
            {
                CacheSample sample = samples[12];
                ActorState actor = game.Model.Players[12];
                SourceVector3 afterRoot = SourcePlayerDriver.WorldPosition(actor);
                PlayerView afterView = GameSnapshot.SourcePlayerView(actor, tick, true);
                IReadOnlyList<SourceVector3> after = inner.CameraJoints(afterView, afterRoot, camera);
                Assert(sample.Script == "spin_a_anim" && sample.Sequence == "SPIN_A" && sample.Ordinal == 26
                    && game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision
                        && e.ActorPlayerId == 12 && e.OtherPlayerId == 13)
                    && !SameBits(sample.Root, afterRoot) && sample.PoseBasis == PoseBasis(afterView),
                    "actual unfiltered source474 records12->13 cylinder root displacement with unchanged real SPIN_A26 pose");
                Assert(new[] { B(sample.Joints[0].X), B(sample.Joints[0].Y), B(sample.Joints[0].Z) }
                        .SequenceEqual(new[] { "1115870891", "3248888060", "1120589844" })
                    && new[] { B(after[0].X), B(after[0].Y), B(after[0].Z) }
                        .SequenceEqual(new[] { "1115875196", "3248881154", "1120592457" }),
                    "actual source474 pre/post pelvis bits reproduce the pinned failed-contract trace without state overrides");
                Assert(SameBits(game.Model.SourceCameraJoints[12, 0], sample.Joints[0])
                    && !SameBits(sample.Joints[0], after[0]),
                    "source474 cache correctly retains PRE-contact jpos despite observed POST-contact root delta");
                contact474 = new { sourceTick = tick, host, actor = 12, joint = 0,
                    sample.Script, sample.Sequence, sample.Ordinal, rootBefore = sample.Root, rootAfter = afterRoot,
                    rootDelta = new SourceVector3(afterRoot.X - sample.Root.X, afterRoot.Y - sample.Root.Y,
                        afterRoot.Z - sample.Root.Z),
                    cameraPosition = camera.Position, currentMatrix = camera.Transform,
                    currentMatrixBits = camera.Transform.Select(B).ToArray(),
                    cacheBits = Enumerable.Range(0, 15).Select(j => new[] {
                        B(game.Model.SourceCameraJoints[12, j].X), B(game.Model.SourceCameraJoints[12, j].Y),
                        B(game.Model.SourceCameraJoints[12, j].Z) }).ToArray(),
                    capturedPreContactBits = sample.Joints.Select(j => new[] { B(j.X), B(j.Y), B(j.Z) }).ToArray(),
                    actualPostContactBits = after.Select(j => new[] { B(j.X), B(j.Y), B(j.Z) }).ToArray(),
                    actualContacts = game.ContactEvents, all14x15Rows = rows };
            }
            previous = samples.ToArray(); previousTick = tick;
            return retainedRows;
        }

        internal object Evidence()
        {
            return new { schema = "source-time-M1-cache-contract/v1",
                trace = "source-sound-smoke/raw-vector-v1", FrameCount, GeometryCalls, CurrentRows,
                PreviousRows, ReferenceRows, OrdinaryRows, LegacyRows, ContactFrames, DisplacedActors, CurrentMatrixFrames,
                SkippedHosts, AddedAssertions, contact474, frames,
                geometry = "Passive wrapper returns unchanged actual SourceCatchGeometry results; SAME real272 library",
                stateMutation = false, siteBasedRandomFiltering = false, fullM1Qualified = false };
        }

        internal object Summary()
        {
            return new { FrameCount, GeometryCalls, CurrentRows, PreviousRows, ReferenceRows, OrdinaryRows,
                LegacyRows, ContactFrames, DisplacedActors, SkippedHosts, CurrentMatrixFrames, AddedAssertions,
                source474Observed = contact474 != null, lastObservedSourceTick = previousTick };
        }

        internal void RequireTarget()
        {
            Assert(contact474 != null && CurrentRows == FrameCount * 14 * 15
                && ReferenceRows == CurrentRows && PreviousRows == (FrameCount - 1) * 14 * 15
                && OrdinaryRows >= 210 && ContactFrames > 0 && DisplacedActors > 0,
                "targeted source474 proof retains every14x15/current-matrix/previous-cache/contact and ordinary comparison");
        }
    }

    private static void Check(bool value, string identity)
    {
        assertions++;
        if (!value) throw new MovementCallerAssertion("M1 CORE ASSERTION: " + identity);
    }

    private static int Main(string[] args)
    {
        if (args.Length == 6 && args[0] == "snap-hike") return SnapHikeChecks(args);
        if (args.Length == 1 && args[0] == "portable-public-policy-checks") return PortablePolicyChecks();
        var result = new Dictionary<string, object> {
            { "schema", "source-movement-caller-checks/v1" }, { "status", "error" },
            { "whole_M1_or_full_gameplay_qualified", false }, { "native_or_browser_qualified", false }
        };
        try
        {
            string c2Inputs = null;
            string c2M1Inputs = null;
            string c2PreSnapSwitchInputs = null;
            bool compatibilitySpecified = false;
            while (args.Length >= 2 && args[args.Length - 2].StartsWith("--", StringComparison.Ordinal))
            {
                string flag = args[args.Length - 2], value = args[args.Length - 1];
                if (flag == "--c2-m1-inputs" && c2M1Inputs == null) c2M1Inputs = value;
                else if (flag == "--c2-presnap-switch-inputs" && c2PreSnapSwitchInputs == null) c2PreSnapSwitchInputs = value;
                else if (flag == "--c2-inputs" && c2Inputs == null) c2Inputs = value;
                else if (flag == "--compatibility-old-inputs" && compatibilityOldInputs == null) compatibilityOldInputs = value;
                else if (flag == "--source-compatibility" && !compatibilitySpecified)
                {
                    gameplayCompatibility = SourceGameplayCompatibilityOptions.Parse(value);
                    compatibilitySpecified = true;
                }
                else throw new ArgumentException("Unknown or duplicate option " + flag);
                args = args.Take(args.Length - 2).ToArray();
            }
            bool canonical = args.Length == 5 && (args[2] == "canonical281" || args[2] == "canonical281-observed");
            bool m1Replay = args.Length == 6 && new[] {
                "m1-complete", "m1-complete-observed", "presnap-complete", "presnap-complete-observed",
                "historical-canonical-replay", "historical-canonical-replay-observed"
            }.Contains(args[2]);
            bool historicalReplay = m1Replay && args[2].StartsWith("historical-", StringComparison.Ordinal);
            bool portablePolicy = args.Length == 6 && args[2] == "portable-public-policy";
            bool preSnapPolicy = args.Length == 6 && args[2] == "presnap-public-policy";
            bool preSnapReplay = m1Replay && args[2].StartsWith("presnap-", StringComparison.Ordinal);
            if ((portablePolicy || preSnapPolicy) && (!compatibilitySpecified || gameplayCompatibility != SourceGameplayCompatibility.Portable))
                throw new ArgumentException("Portable public policy requires explicit --source-compatibility portable.");
            if (!canonical && !m1Replay && !portablePolicy && !preSnapPolicy && (args.Length != 3 && args.Length != 4 ||
                !new[] { "bounded", "complete", "controlled", "cache-contract", "campaigns", "compatibility", "carrierless-switch" }.Contains(args[2])))
                throw new ArgumentException("MovementCallerChecks actual-fixture.json output-directory bounded|complete|controlled|cache-contract|canonical281|canonical281-observed [oldSAFN] [c2-inputs]");
            outputRoot = Path.GetFullPath(args[1]);
            fixtureRoot = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            Directory.CreateDirectory(outputRoot);
            mutationRun = args[2] == "controlled";
            real = FullKernelInputs.Load(args[0], movementLibrary: true, selectedMovementLibrary: args[2] == "bounded",
                soundFunctionsPath: args.Length >= 4 ? args[3] : null,
                c2InputsPath: canonical || historicalReplay ? args[4] : c2Inputs,
                c2M1InputsPath: portablePolicy || m1Replay && !historicalReplay && !preSnapReplay ? args[4] : c2M1Inputs,
                c2PreSnapSwitchInputsPath: preSnapPolicy || preSnapReplay ? args[4] : c2PreSnapSwitchInputs);
            library = real.Animations; geometry = real.Geometry; playbook = real.Playbook;
            rosters = real.Rosters; movement = real.MovementRuntime;
            if (gameplayCompatibility == SourceGameplayCompatibility.Portable && !real.IsC2M1Profile && !real.IsPreSnapSwitchProfile)
                throw new ArgumentException("Portable gameplay requires --c2-m1-inputs and the actual M1 factory.");
            result["gameplay_compatibility"] = gameplayCompatibility.ToString();
            result["real_input_fixture_sha256"] = real.ManifestSha256;
            result["actual_animation_library_manifest"] = real.Animations.InventorySha256;
            if (portablePolicy) return AuthorPortablePublicPolicy(args[5], result);
            if (preSnapPolicy) return AuthorPreSnapPublicPolicy(args[5], result);
            if (args[2] == "compatibility" || args[2] == "carrierless-switch")
            {
                compatibilitySoundFunctions = args[3];
                if (args[2] == "compatibility") CompatibilityChecks();
                else CarrierlessSwitchChecks();
                result["status"] = "passed";
                result["assertions"] = assertions;
                result["mode"] = args[2];
                Console.WriteLine(JsonSerializer.Serialize(result));
                return 0;
            }
            if (m1Replay)
            {
                M1CompleteReplay(args[5], args[2].EndsWith("-observed", StringComparison.Ordinal), historicalReplay);
                result["status"] = historicalReplay ? "historical-canonical-vector-replayed" : "passed";
                result["assertions"] = assertions;
                result["mode"] = args[2];
                result["public_R_SKIP_gate_qualified"] = !historicalReplay && m1ReplayPositive;
                result["actual_factory_replay"] = m1ReplayEvidence;
                result["whole_M1_or_full_gameplay_qualified"] = false;
                result["complete_publication_qualification"] = false;
                Console.WriteLine(JsonSerializer.Serialize(result));
                return 0;
            }
            if (canonical)
            {
                CanonicalEntry(args[2] == "canonical281-observed");
                result["status"] = "canonical-entry-window-only";
                result["assertions"] = assertions;
                result["mode"] = args[2];
                result["full_M1_qualified"] = false;
                Console.WriteLine(JsonSerializer.Serialize(result));
                return 0;
            }
            if (args[2] == "cache-contract")
            {
                Smoke(cacheContractOnly: true);
                result["status"] = "cache-contract-passed";
                result["mode"] = "targeted-source474-cache-contract-only";
                result["assertions"] = assertions;
                result["retained_old_smoke_assertions"] = assertions - smokeCache.AddedAssertions;
                result["source_time_cache"] = smokeCache.Summary();
                result["public_smoke_trace"] = "source-sound-smoke/raw-vector-v1";
                result["public_smoke_source_tick"] = smokeSourceTick;
                result["complete_publication_qualification"] = false;
                Console.WriteLine(JsonSerializer.Serialize(result));
                return 0;
            }
            if (mutationRun)
            {
                ControlledCallers();
                InputAndStationCallers();
            }
            else
            {
                if (args[2] != "campaigns") Smoke();
                RawMovementCampaign();
                MaybeDivePublicCampaign();
                ControlledCallers();
                InputAndStationCallers();
            }
            result["assertions"] = assertions;
            result["retained_M1_assertions"] = assertions - (smokeCache == null ? 0 : smokeCache.AddedAssertions);
            if (smokeCache != null) result["source_time_cache"] = smokeCache.Summary();
            result["actual_Hurdle_VM_scripts"] = 16;
            result["actual_MaybeDive_windows_per_hurdle"] = 7;
            result["actual_Core_post_VM_physics_cases"] = physicsCases.Count;
            result["actual_Core_LEAPATT_operand_bytes"] = 36;
            result["known_R_SKIP_asset_missing"] = missingRSkip;
            result["public_smoke_trace"] = "source-sound-smoke/raw-vector-v1";
            result["public_smoke_source_tick"] = smokeSourceTick;
            result["public_smoke_R_SKIP_frames_after2299"] = smokeRSkipFrames;
            result["public_smoke_cache_assertions"] = smokeCacheAssertions;
            result["public_smoke_resolved_sound_dispatches"] = smokeSoundDispatches;
            result["public_smoke_sound_random_draws"] = smokeSoundDraws;
            result["public_smoke_R_SKIP_instance_advanced"] = smokeRSkipAdvanced;
            if (args[2] == "complete" && missingRSkip)
                throw new MovementCallerAssertion("Actual original R_SKIP is missing; full publication smoke is not qualified.");
            result["status"] = missingRSkip ? "bounded-passed" : "passed";
            result["complete_publication_qualification"] = args[2] == "complete" && !mutationRun && !missingRSkip;
            result["mode"] = args[2];
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (MovementCallerAssertion error)
        {
            result["status"] = "failed";
            result["failed_assertion"] = error.Message;
            result["assertions"] = assertions;
            if (smokeCache != null)
            {
                result["source_time_cache"] = smokeCache.Summary();
                Save("source-time-cache-failure.json", JsonSerializer.Serialize(
                    new { error = error.Message, actualObservations = smokeCache.Evidence() },
                    new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            }
            if (outputRoot != null && File.Exists(Path.Combine(outputRoot, "first-cache-contract-failure.json")))
                result["actual_cache_failure_trace"] = Path.Combine(outputRoot, "first-cache-contract-failure.json");
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 1;
        }
        catch (Exception error)
        {
            result["error"] = error.ToString();
            result["assertions"] = assertions;
            if (smokeCache != null) result["source_time_cache"] = smokeCache.Summary();
            if (outputRoot != null && File.Exists(Path.Combine(outputRoot, "first-public-smoke-failure.json")))
                result["actual_partial_smoke_trace"] = Path.Combine(outputRoot, "first-public-smoke-failure.json");
            if (outputRoot != null && activeGame != null)
            {
                string failurePath = Path.Combine(outputRoot, "first-current-source-failure.json");
                Save("first-current-source-failure.json", JsonSerializer.Serialize(new {
                    error = error.ToString(), actual = CanonicalObservation(activeGame, -1, -1, null),
                    allSharedRawDraws = (activeGame.SourceRandomService as SourceInjectedRandom) == null
                        ? null : ((SourceInjectedRandom)activeGame.SourceRandomService).Trace
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                result["actual_current_source_failure"] = failurePath;
            }
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 2;
        }
        finally
        {
            SaveExecutionCounts();
        }
    }

    private static void Save(string name, string content)
    {
        if (!mutationRun) File.WriteAllText(Path.Combine(outputRoot, name), content);
    }

    private static SourceGameplayCompatibility gameplayCompatibility = SourceGameplayCompatibility.StrictSource;

    private static GameSimulation Create(bool camera = true, Func<int, ISourceRandom> random = null,
        SourceMovementSettings settings = null, SourceCacheWitness cacheWitness = null,
        SourceGameplayCompatibility? compatibility = null, SourceHandicapSettings handicapSettings = null)
    {
        executionConstructors++;
        Func<int, ISourceRandom> countedRandom = random == null ? null : new Func<int, ISourceRandom>(seed => {
            ISourceRandom service = random(seed);
            RetainExecutionRandom(service);
            return service;
        });
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            real.IsPreSnapSwitchProfile ? SourceStationLayout.FourStation : SourceStationLayout.TwoStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        SourceMovementSettings movementSettings = settings ?? new SourceMovementSettings(0, 0, false, 0, 0, false);
        if (cacheWitness == null)
        {
            activeGame = real.Create(configuration: config, randomFactory: countedRandom,
                presentation: presentation, movementSettings: movementSettings, camera: camera,
                compatibility: compatibility ?? gameplayCompatibility, handicapSettings: handicapSettings);
            RetainExecutionGame(activeGame);
            return activeGame;
        }
        if (real.IsC2M1Profile || real.IsPreSnapSwitchProfile)
            throw new InvalidOperationException("M1 successor cache proof must use actual SourceC2Composition and canonical call-site observation, never a proxy constructor.");
        var game = new GameSimulation(real.Animations, cacheWitness, real.Playbook, config, countedRandom, 1,
            real.Rosters, presentation, real.MovementRuntime, movementSettings, handicapSettings: handicapSettings);
        game.BindSourceSoundVmData(real.SoundFunctions);
        activeGame = game;
        RetainExecutionGame(game);
        cacheWitness.Bind(game);
        if (camera)
            game.SourceCameraHeadingProvider = (snapshot, tick) =>
            {
                int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
                cacheWitness.CameraStage(tick);
                return heading;
            };
        return game;
    }

    private static PlayerCommand Raw(SourceButtons held, SourceButtons pressed = SourceButtons.None,
        SourceButtons released = SourceButtons.None, int x = 0, int z = 0)
    {
        return new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(0, x, z, held, pressed, released) }));
    }

    private static object ActualRouteArgumentSources(GameSimulation game)
    {
        int id = game.Model.SourceRandomActor;
        if (id < 0 || id >= game.Model.Players.Length) return null;
        ActorState actor = game.Model.Players[id];
        SourceRouteState route = game.Model.SourceDrones[id];
        return new {
            qualification = "Actual model/argument sources at exception; rejected SourceRouteInput was NOT constructed or normalized",
            actor.Id, actor.Facing, actor.TargetFacing, actor.SourceMode, actor.SourceJob,
            actor.SourceControllerStation, actor.SourceTarget, actor.SourceButtons, actor.SourceNewButtons,
            actor.SourcePuppetLinkId, actor.SourceHitPartnerId,
            actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
            actor.AnimationState.Primary.FrameOrdinal,
            nextProgramCounter = typeof(SourceAnimationLayerState).GetField("pc",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(actor.AnimationState.Primary),
            aiActor = game.Model.SourceAiFrame == null ? (object)null : game.Model.SourceAiFrame.Actors[id],
            routeIdentity = route.Program == null ? null : route.Program.Identity,
            routeSourceBlob = route.Program == null ? null : route.Program.SourceBlob,
            routeSourceLine = route.Program == null ? (int?)null : route.Program.SourceLine,
            route.WordIndex, route.Flags, route.Target, route.Job, route.LastSourceTick, route.Stick,
            route.Buttons, route.WaitTicks, route.ThinkTicks,
            formationSlot = game.Model.SourceFormationSlots == null ? (object)null : game.Model.SourceFormationSlots[id],
            game.Model.SourceMotionDirection, game.Model.SourceDronesLose, game.Model.SourceDronesWinning,
            game.Model.SourceSpinCount, game.Model.ThrowerId, game.Model.CarrierId, game.Model.TargetPlayerId,
            game.Model.SourceCurrentInput, game.Model.SourceRules,
            aiParameters = game.Model.SourceAiParameters == null ? null : (object)new {
                game.Model.SourceAiParameters.Side0Handicap, game.Model.SourceAiParameters.Side1Handicap },
            actualDistances = Enumerable.Range(0, 14).Select(other => game.Model.SourceDistances[id, other]).ToArray(),
            actualAngles = Enumerable.Range(0, 14).Select(other => game.Model.SourceAngles[id, other]).ToArray(),
            actualHandicapTableRecords = File.ReadAllLines(Path.Combine(fixtureRoot, "playbook/catalog.tsv"))
                .Where(line => line.StartsWith("handicap\t", StringComparison.Ordinal)).ToArray(),
            tableLibrarySha256 = FullKernelInputs.PlaybookSha256
        };
    }

    private static void Smoke(bool cacheContractOnly = false)
    {
        GameSimulation legacy = new GameSimulation(27);
        legacy.Step(default(PlayerCommand));
        Check(legacy.Profile == SimulationProfile.LegacyBaseline60Hz && legacy.State.Tick == 1,
            "legacy int-seed constructor does not require M1 data/camera");
        GameSimulation game = Create(false);
        bool failed = false;
        game.Step(default(PlayerCommand));
        try { game.Step(default(PlayerCommand)); } catch (InvalidOperationException e) { failed = e.Message.Contains("cambot"); }
        Check(failed, "missing actual executing camera fails, no original-profile legacy fallback");
        int[] rawVector = { 0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
            2147483645, 2147483646, 2147483647 };
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        smokeCache = new SourceCacheWitness(geometry);
        game = Create(random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length],
            new SourceRandomProvenance("source-sound-smoke/raw-vector-v1",
                "Explicit repeated raw test vector shared by ALL sites, never target libc PRNG",
                "unfiltered public M1157 + synchronous original sound", SourceRandomEvidence.ExternalUnverified)),
            cacheWitness: smokeCache,
            handicapSettings: cacheContractOnly ? HistoricalC1CacheHandicapSettings() : null);
        var events = new List<object>();
        var rSkipSamples = new List<object>();
        var rSkipOrdinals = new HashSet<int>();
        var soundDispatches = new List<object>();
        int deadWait = 0, stableCacheAssertions = 0, rSkipPoseObservations = 0;
        int firstRSkipActor = -1, firstRSkipOrdinal = -1;
        long firstRSkipInstance = -1, firstRSkipTick = -1;
        bool rSkipAdvanced = false;
        const int maximumHosts = 12000;
        int hostTicks = 0, resolvedSound = 0, soundDraws = 0;
        for (int host = 0; host < maximumHosts; host++)
        {
            GamePhase beforePhase = game.State.Phase;
            long beforeSourceTick = game.SourceTick;
            int beforeGeometryCalls = smokeCache.GeometryCalls;
            PlayerCommand command = default(PlayerCommand);
            if (game.State.Phase == GamePhase.PlaySelect)
                command = new PlayerCommand(action: InputAction.SelectPlay,
                    play: game.State.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Cover);
            else if (game.State.Phase == GamePhase.PreSnap)
                command = new PlayerCommand(action: InputAction.Snap);
            else if (game.State.Phase == GamePhase.DeadBall)
                command = ++deadWait > 30 ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand);
            else if (game.State.Phase == GamePhase.LiveBall)
                command = new PlayerCommand(new FieldVector(0f, 1f), true,
                    game.State.Possession == TeamId.Home ? InputAction.None : InputAction.Tackle);
            if (game.State.Phase != GamePhase.DeadBall) deadWait = 0;
            try { game.Step(command); }
            catch (Exception error)
            {
                if (error.Message.Contains("missing clip R_SKIP"))
                {
                    missingRSkip = true;
                    Check(real.Animations.InventorySha256 == FullKernelInputs.MovementSelectedSha256,
                        "only the explicit selected156 control may report an actual unfiltered missing R_SKIP");
                    break;
                }
                Save("first-public-smoke-failure.json", JsonSerializer.Serialize(new {
                    schema = "source-contact-M1-public-smoke-failure/v1",
                    status = "failed-not-complete-smoke", trace = "source-sound-smoke/raw-vector-v1",
                    actual_library = real.Animations.InventorySha256, host, hostTicks, maximumHosts,
                    sourceTick = game.SourceTick, priorSourceTick = beforeSourceTick, beforePhase,
                    phase = game.Model.Phase.ToString(), exception = error.ToString(), command,
                    rawVector, rawCursor, stableCacheAssertions, resolvedSound, soundDraws,
                    rSkipSamples, rSkipAdvanced, events, soundDispatches,
                    game.Model.SourceAnimationFailure, game.Model.SourceRandomProcess,
                    game.Model.SourceRandomActor, game.Model.SourceRandomProcessOrder, game.Model.SourceRandomCallIndex,
                    actors = game.Model.Players.Select(actor => new {
                        actor.Id, actor.Facing, actor.TargetFacing, actor.SourceMode, actor.SourceJob,
                        actor.SourceControllerStation, actor.SourceTarget, actor.SourceHitPartnerId, actor.SourcePuppetLinkId,
                        actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                        actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId
                    }).ToArray(),
                    actualRouteArgumentSources = ActualRouteArgumentSources(game),
                    allSharedRawDraws = shared.Trace.Select(draw => new {
                        draw.Order, draw.Raw, draw.Value, draw.Site.SourceTick, draw.Site.Process,
                        draw.Site.ActorId, draw.Site.ProcessOrder, draw.Site.CallIndex,
                        draw.Site.SourceIdentity, draw.Site.BoundOrModulus
                    }).ToArray(),
                    stateMutation = false, siteBasedRandomFiltering = false,
                    targetPrngCameraDspOrWholeMatchClaimed = false
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                throw;
            }
            foreach (SourceGameEvent item in game.SourceEvents)
                events.Add(new { item.SourceTick, item.Type, item.ActorPlayerId, item.TargetPlayerId });
            hostTicks++;
            foreach (SourceSoundVmDispatch item in game.TakeSourceSoundVmDispatches().Dispatches)
            {
                if (item.Status == SourceSoundVmStatus.Resolved)
                {
                    resolvedSound++;
                    soundDraws += item.Result.RandomCalls;
                }
                soundDispatches.Add(new { tick = item.OriginalCueContext.SourceTick,
                    actor = item.OriginalCueContext.ActorPlayerId, item.Status,
                    draws = item.Result == null ? 0 : item.Result.RandomCalls });
            }
            rSkipPoseObservations += game.Model.Players.Count(actor =>
                actor.AnimationState.Primary.SequenceName == "R_SKIP");
            if (game.SourceTick > 2299 && game.SourceTick > beforeSourceTick)
                foreach (ActorState actor in game.Model.Players)
                {
                    SourceAnimationLayerState layer = actor.AnimationState.Primary;
                    if (layer.SequenceName != "R_SKIP" || !layer.IsSpecified || layer.SourceTick != game.SourceTick)
                        continue;
                    ISourceAnimationClip actual = library.GetClip("R_SKIP");
                    Check(actual != null && layer.FrameOrdinal >= 0
                        && layer.FrameOrdinal < actual.SelectedSourceFrames.Count,
                        "post-gap unfiltered R_SKIP VM uses an actual selected ordinal from the real157 library");
                    IReadOnlyList<SourceVector3> pose = geometry.CameraJoints(
                        GameSnapshot.SourcePlayerView(actor, game.SourceTick, true),
                        game.SourceActorWorldPosition(actor.Id), game.Model.SourceMovementCamera);
                    Check(pose.Count == 15 && pose.All(joint => !float.IsNaN(joint.X) && !float.IsInfinity(joint.X)
                        && !float.IsNaN(joint.Y) && !float.IsInfinity(joint.Y)
                        && !float.IsNaN(joint.Z) && !float.IsInfinity(joint.Z)),
                        "post-gap actual R_SKIP fifteen-bone poses execute through real production geometry");
                    rSkipOrdinals.Add(layer.FrameOrdinal);
                    if (firstRSkipActor < 0)
                    {
                        firstRSkipActor = actor.Id; firstRSkipInstance = layer.InstanceId;
                        firstRSkipOrdinal = layer.FrameOrdinal; firstRSkipTick = game.SourceTick;
                    }
                    else if (actor.Id == firstRSkipActor && layer.InstanceId == firstRSkipInstance
                        && game.SourceTick > firstRSkipTick && layer.FrameOrdinal != firstRSkipOrdinal)
                        rSkipAdvanced = true;
                    rSkipSamples.Add(new { tick = game.SourceTick, actor = actor.Id, layer.ScriptSymbol,
                        layer.InstanceId, ordinal = layer.FrameOrdinal, selected = actual.SelectedSourceFrames[layer.FrameOrdinal],
                        root = game.SourceActorWorldPosition(actor.Id), joints = pose });
                }
            stableCacheAssertions += smokeCache.AfterHostStep(beforeSourceTick, beforePhase, beforeGeometryCalls, host);
            if (cacheContractOnly && game.SourceTick >= 474)
            {
                smokeCache.RequireTarget();
                break;
            }
            if (game.SourceTick >= 2375 && rSkipAdvanced && rSkipOrdinals.Count >= 2 && stableCacheAssertions >= 35910
                && resolvedSound > 0 && soundDraws > 0)
                break;
            if (game.State.Phase == GamePhase.GameOver) break;
        }
        smokeSourceTick = game.SourceTick;
        smokeRSkipFrames = rSkipOrdinals.Count;
        smokeCacheAssertions = stableCacheAssertions;
        smokeSoundDispatches = resolvedSound;
        smokeSoundDraws = soundDraws;
        smokeRSkipAdvanced = rSkipAdvanced;
        Save("smoke-events.json", JsonSerializer.Serialize(new { events, source_tick = game.SourceTick,
            missing_R_SKIP = missingRSkip, actual_R_SKIP_pose_observations = rSkipPoseObservations,
            complete_smoke = !missingRSkip && game.SourceTick > 2299 && rSkipAdvanced && rSkipOrdinals.Count >= 2 }));
        Save("sound-aware-smoke.json", JsonSerializer.Serialize(new {
            schema = "source-sound-aware-M1-smoke/v1", trace = "source-sound-smoke/raw-vector-v1",
            rawVector, rawCursor, hostTicks, maximumHosts, sourceTick = game.SourceTick,
            stableCacheAssertions, resolvedSound, soundDraws, rSkipSamples, rSkipAdvanced, soundDispatches,
            actualSharedRawDraws = shared.Trace.Count,
            allSharedRawDraws = shared.Trace.Select(draw => new {
                draw.Order, draw.Raw, draw.Value, draw.Site.SourceTick, draw.Site.Process,
                draw.Site.ActorId, draw.Site.ProcessOrder, draw.Site.CallIndex,
                draw.Site.SourceIdentity, draw.Site.BoundOrModulus
            }).ToArray(),
            tauntDraws = shared.Trace.Where(draw => draw.Site.SourceIdentity == "player.c:standard_run:taunt").ToArray(),
            stateMutation = false, siteBasedRandomFiltering = false,
            targetPrngCameraDspOrWholeMatchClaimed = false }));
        Save("source-time-cache-contract.json", JsonSerializer.Serialize(smokeCache.Evidence(),
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        if (cacheContractOnly) return;
        Check(game.SourceTick > 500, "actual whole Core M1 caller/physics survives representative source time");
        Check(missingRSkip || game.Model.SourceCameraJointTick == game.SourceTick || game.State.Phase == GamePhase.PlaySelect,
            "actual post-player camera cache generated every executing gameplay wake");
        Check(stableCacheAssertions >= 210, "actual no-contact source wake compares all14x15 real cached post-physics joints");
        if (!missingRSkip)
        {
            Check(game.SourceTick > 2299 && rSkipPoseObservations > 0 && rSkipAdvanced && rSkipOrdinals.Count >= 2,
                "complete actual original-input smoke passes previous2299 stop and executes real R_SKIP poses");
            Check(stableCacheAssertions >= 35910,
                "sound-aware smoke retains at least the prior171 complete14x15 post-player cache frames");
            Check(resolvedSound > 0 && soundDraws > 0,
                "public M1 smoke executes bound original sound synchronously on the same shared raw stream");
        }
    }

    private static void RawMovementCampaign()
    {
        var draws = new Random(17);
        Func<int, ISourceRandom> factory = seed => new SourceInjectedRandom(
            site => site.SourceIdentity == "player.c:standard_run:taunt" ? 0 : draws.Next(int.MaxValue),
            new SourceRandomProvenance("M1/caller-controlled-fixture",
                "Explicit test-only raw-input provider; not original platform PRNG", "named raw gestures",
                SourceRandomEvidence.ExternalUnverified));
        GameSimulation game = Create(random: factory);
        var seen = new HashSet<string>();
        var roots = new Dictionary<int, SourceVector3>();
        var events = new List<object>();
        SourceButtons old = SourceButtons.None;
        int changes = 0;
        for (int host = 0; host < 2400; host++)
        {
            PlayerCommand command;
            if (game.State.Phase == GamePhase.PlaySelect)
                command = new PlayerCommand(action: InputAction.SelectPlay,
                    play: game.State.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Cover);
            else if (game.State.Phase == GamePhase.DeadBall)
                command = new PlayerCommand(action: InputAction.Continue);
            else
            {
                SourceButtons buttons = host % 8 < 5 ? SourceButtons.C : SourceButtons.None;
                int cycle = host % 73;
                if (cycle == 12 || cycle == 15) buttons |= SourceButtons.A;
                if (game.State.Phase == GamePhase.PreSnap && host % 17 == 0) buttons |= SourceButtons.B;
                command = Raw(buttons, buttons & ~old, old & ~buttons, z: game.State.Phase == GamePhase.LiveBall ? 3 : 0);
                old = buttons;
            }
            game.Step(command);
            foreach (PlayerView player in game.State.Players)
            {
                if (player.Animation.IsSpecified) seen.Add(player.Animation.Primary.ScriptSymbol);
                SourceVector3 root = game.SourceActorWorldPosition(player.Id);
                SourceVector3 previous;
                if (roots.TryGetValue(player.Id, out previous) && (previous.X != root.X || previous.Y != root.Y || previous.Z != root.Z))
                    changes++;
                roots[player.Id] = root;
            }
            foreach (SourceGameEvent item in game.SourceEvents)
                events.Add(new { item.SourceTick, item.Type, item.ActorPlayerId, item.TargetPlayerId });
        }
        Check(changes > 1000, "actual M1 commands move canonical real roots/physics, not only renderer state");
        Check(seen.Contains("spin_anim"), "real raw C-release/repress selects actual spin script through caller");
        Check(seen.Any(name => name.Contains("hurd")), "real raw A selects an actual hurdle through caller");
        Save("raw-campaign-events.json", JsonSerializer.Serialize(new { source = game.SourceTick,
            scripts = seen.OrderBy(s => s).ToArray(), changes, events, stateMutation = false,
            originalCameraParityClaimed = false, controlledCameraInput = true }));
        Check(game.SourceTick == 2280, "representative actual raw campaign completes all2400 host/2280 source ticks");
        Check(game.Model.Players.Sum(a => a.SourceMaybeDiveCallbacks) > 0, "actual script MaybeDive callback windows execute in Core");
        Check(game.Model.Players.Sum(a => a.SourceImmediateGeneralRuns) > 0, "actual VM GeneralRun callbacks execute synchronously");
    }

    private static void MaybeDivePublicCampaign()
    {
        var raw = new Random(22);
        GameSimulation game = Create(random: seed => new SourceInjectedRandom(
            site => site.SourceIdentity == "player.c:standard_run:taunt" ? 0
                : site.SourceIdentity.Contains("hurdle") ? 0 : raw.Next(int.MaxValue),
            new SourceRandomProvenance("M1/maybe-dive-public", "Explicit controlled test input, no PRNG parity",
                "public A-release-A at observed source window", SourceRandomEvidence.ExternalUnverified)));
        SourceButtons previous = SourceButtons.None;
        int actorId = -1, phase = 0, airborne = 0;
        long hurdleTick = -1, releaseTick = -1;
        var evidence = new List<object>();
        for (int host = 0; host < 1800; host++)
        {
            PlayerCommand command;
            if (game.State.Phase == GamePhase.PlaySelect)
                command = new PlayerCommand(action: InputAction.SelectPlay,
                    play: game.State.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Cover);
            else if (game.State.Phase == GamePhase.DeadBall)
                command = new PlayerCommand(action: InputAction.Continue);
            else
            {
                SourceButtons buttons = SourceButtons.C;
                if (game.State.Phase == GamePhase.PreSnap && host % 29 == 0) buttons |= SourceButtons.B;
                int controlled = game.State.ControlledPlayerId;
                if (phase == 0 && game.State.Phase == GamePhase.LiveBall && game.Model.CarrierId == controlled
                    && controlled >= 0 && !game.Model.Players[controlled].AnimationState.Busy)
                { buttons |= SourceButtons.A; actorId = controlled; }
                if (phase == 2 && game.SourceTick > releaseTick) buttons |= SourceButtons.A;
                command = Raw(buttons, buttons & ~previous, previous & ~buttons,
                    z: game.State.Phase == GamePhase.LiveBall ? 3 : 0);
                previous = buttons;
            }
            game.Step(command);
            if (actorId >= 0)
            {
                ActorState actor = game.Model.Players[actorId];
                string script = actor.AnimationState.Primary.ScriptSymbol;
                if (phase == 0 && script.Contains("hurd"))
                { phase = 1; hurdleTick = game.SourceTick; }
                if (phase == 1 && game.SourceTick > hurdleTick && (actor.SourceButtons & SourceDroneButtons.A) == 0)
                { phase = 2; releaseTick = game.SourceTick; }
                evidence.Add(new { game.Model.SourceTick, script, actor.SourceNonATicks,
                    actor.SourceMaybeDiveCallbacks, actor.SourceLastMaybeDiveTick, actor.SourceHeight,
                    root = game.SourceActorWorldPosition(actorId), phase });
                if (script == "qb_slide_anim" || script == "r_diveoh_anim")
                {
                    phase = 3;
                    if (actor.SourceHeight > 0f) airborne++;
                }
                if (phase == 3 && airborne > 5 && actor.SourceHeight == 0f) break;
            }
        }
        Save("maybe-dive-public.json", JsonSerializer.Serialize(evidence));
        Check(phase == 3 && airborne > 5, "actual public A-release-A selects and physically executes slide/reach-dive");
        Check(evidence.Count > 5, "real source script/callback/height trajectory recorded");
    }
}
