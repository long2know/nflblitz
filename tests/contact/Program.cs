using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal sealed class ContactAssertion : Exception
{
    internal ContactAssertion(string identity) : base(identity) { }
}

internal static partial class Program
{
    private static FullKernelInputs real;
    private static FullKernelInputs legacy157;
    private static GameSimulation lastGame;
    private static ISourceAnimationLibrary library;
    private static SourceCatchGeometry geometry;
    private static int assertions;
    private static string output;
    private static string inputManifest;
    private static string inputArtifacts;
    private static readonly List<object> kernelEvidence = new List<object>();
    private static readonly List<string> puppetInputs = new List<string>();
    private static readonly List<string> puppetOutputs = new List<string>();
    private static Dictionary<string, JsonElement[]> programs;
    private static int emittedDetectorCases, emittedSegmentCases, verifiedDetectorCases, verifiedSegmentCases;
    private static string[] sequenceNames;
    private const string ExtraSha = "907e2d368394e5c9db1bebdf218abae62bb3b841230293670cb3d2db48e05fcd";

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new ContactAssertion("C1 ASSERTION: " + identity);
    }
    private static T Bind<T>(GameSimulation game, string name)
    {
        return (T)(object)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
            .CreateDelegate(typeof(T), game);
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static uint Bits(float number) { return unchecked((uint)BitConverter.SingleToInt32Bits(number)); }
    private static float Float(string number) { return BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(number))); }

    private static void Load(string manifest, string artifacts, string soundFunctions, string canonicalInputs = null,
        string m1Inputs = null)
    {
        inputManifest = manifest;
        inputArtifacts = artifacts;
        real = FullKernelInputs.Load(manifest, movementLibrary: true, soundFunctionsPath: soundFunctions,
            c2InputsPath: canonicalInputs, c2M1InputsPath: m1Inputs);
        bool actual = canonicalInputs == null && m1Inputs == null
            ? real.IsContactProfile && real.Animations.InventorySha256 == FullKernelInputs.ContactAnimationSha256
                && real.Animations.SequenceNames.Count == 272 && real.Animations.FrameCount == 24892
            : real.IsC2Profile && real.Animations.InventorySha256 == SourceTeamResourceProfile.C2AnimationSha256
                && real.Animations.SequenceNames.Count == 281 && real.Animations.FrameCount == 26230;
        Check(actual, canonicalInputs == null && m1Inputs == null
            ? "current C1 generation loads actual production272, never a merged157 adapter or relabeled old fixture"
            : "current canonical281 generation loads every actual clip/frame with exact production pins, never a272 alias");
        legacy157 = FullKernelInputs.Load(manifest, movementLibrary: true, soundFunctionsPath: soundFunctions,
            legacyAnimationLibrary: true);
        byte[] extra = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest)),
            "contact-supplemental/export-inventory.json"));
        Check(Hash(extra) == ExtraSha && legacy157.Animations.InventorySha256 == FullKernelInputs.MovementAnimationSha256,
            "separate actual115 and immutable true157 reference identities are retained");
        library = real.Animations;
        geometry = real.Geometry;
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(artifacts, "source-programs.json"))))
            programs = document.RootElement.EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.EnumerateArray().Select(row => row.Clone()).ToArray());
    }

    private static GameSimulation Create(Func<SourceRandomSite, int> raw = null, SourceMovementSettings settings = null,
        SourcePresentationSettings presentation = null, bool baseOnly = false, bool bindSound = true)
    {
        Func<int, ISourceRandom> random = seed =>
        {
            var values = new Random(37);
            return new SourceInjectedRandom(raw ?? (site => site.Process.StartsWith("detect", StringComparison.Ordinal)
                ? site.BoundOrModulus == 7 ? 2 : 99 : values.Next(0, 0x1000000)),
                new SourceRandomProvenance("C1/explicit-test-input", "Named scripted raw inputs; NOT historical platform PRNG",
                    "source contact branch boundaries", SourceRandomEvidence.ExternalUnverified));
        };
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First);
        SourcePresentationSettings actualPresentation = presentation ??
            new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        SourceMovementSettings actualSettings = settings ?? new SourceMovementSettings(0, 0, false, 0, 0, false);
        GameSimulation game;
        if (!baseOnly && bindSound && real.IsC2Profile)
            game = SourceC2Composition.CreateSimulation(library, real.ReferencePivots, real.Playbook,
                configuration, real.Rosters, actualPresentation, real.MovementRuntime, actualSettings,
                real.SoundFunctions, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(real.C2Data), random);
        else if (!baseOnly && bindSound)
            game = SourceContactComposition.CreateSimulation(library, real.ReferencePivots, real.Playbook,
                configuration, real.Rosters, actualPresentation, real.MovementRuntime, actualSettings,
                real.SoundFunctions, random);
        else
        {
            FullKernelInputs selected = baseOnly ? legacy157 : real;
            game = new GameSimulation(selected.Animations, selected.Geometry, real.Playbook, configuration,
                random, 1, real.Rosters, actualPresentation, real.MovementRuntime, actualSettings);
            if (bindSound) game.BindSourceSoundVmData(real.SoundFunctions);
        }
        game.SourceCameraHeadingProvider = (snapshot, tick) => FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
        lastGame = game;
        foreach (ActorState actor in game.Model.Players)
        {
            Action<ActorState> actual = actor.AnimationState.Primary.PuppetPosition;
            actor.AnimationState.Primary.PuppetPosition = executing =>
            {
                int linked = executing.SourcePuppetLinkId;
                if (linked < 0) { actual(executing); return; }
                ActorState attacker = (executing.AnimationState.Primary.Mode & 0x00800000u) != 0
                    ? game.Model.Players[linked] : executing;
                ActorState victim = attacker == executing ? game.Model.Players[linked] : executing;
                SourceVector3 position = SourcePlayerDriver.WorldPosition(attacker);
                ISourceAnimationClip clip = library.GetClip(victim.AnimationState.Primary.SequenceName);
                int frame = victim.AnimationState.Primary.FrameOrdinal;
                if (frame < 0) throw new ContactAssertion("actual puppet callback requires an actual selected victim frame");
                SourceVector3 root = clip.GetFrame(clip.SelectedSourceFrames[frame]).ScaledRoot;
                int index = puppetInputs.Count;
                var input = new List<string> { "P", index.ToString() };
                input.AddRange(new[] { position.X, position.Y, position.Z, attacker.SourceRoot.X, attacker.SourceRoot.Y,
                    attacker.SourceRoot.Z, attacker.SourceVelocity.X, attacker.SourceVerticalVelocity, attacker.SourceVelocity.Z }
                    .Select(v => Bits(v).ToString()));
                input.Add(attacker.Facing.ToString()); input.Add(attacker.TargetFacing.ToString());
                input.AddRange(new[] { victim.ModelScale, root.X, root.Y, root.Z }.Select(v => Bits(v).ToString()));
                actual(executing);
                SourceVector3 after = SourcePlayerDriver.WorldPosition(victim);
                var observed = new List<string> { "P", index.ToString() };
                observed.AddRange(new[] { after.X, after.Y, after.Z, victim.SourceRoot.X, victim.SourceRoot.Y,
                    victim.SourceRoot.Z, victim.SourceVelocity.X, victim.SourceVerticalVelocity, victim.SourceVelocity.Z }
                    .Select(v => Bits(v).ToString()));
                observed.Add(victim.Facing.ToString()); observed.Add(victim.TargetFacing.ToString());
                puppetInputs.Add(string.Join("\t", input)); puppetOutputs.Add(string.Join("\t", observed));
            };
        }
        return game;
    }

    private static void PairFixture(GameSimulation game, int first = 4, int second = 11, float separation = 9f,
        GamePhase phase = GamePhase.LiveBall)
    {
        SimulationState m = game.Model;
        m.SourceTick = 100;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
        m.SourceDefensePlay = real.Playbook.GetPlay("play_safe_cover");
        m.Phase = phase; m.Possession = TeamId.Home; m.SourceLastPossession = 0;
        m.SourceFlags = SourceGameFlags.None; m.CarrierId = first; m.BallMode = BallMode.Held;
        m.BallKind = SourceBallKind.Pass; m.CatchSourceTick = 0; m.LiveTicks = 200;
        m.SourceNoFumbles = true;
        for (int i = 0; i < 14; i++)
        {
            ActorState actor = m.Players[i];
            actor.SourceMode = SourceActorMode.Dead;
            actor.SourceControllerStation = -1; actor.SourceJob = SourceActorJob.None;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0f;
            actor.SourceRawFlags |= 0x20; actor.SourceUnblockable = false;
            actor.SourceThrowing = actor.SourceLateHittable = actor.SourceHelmetPopped = false;
            actor.SourcePuppetLinkId = -1; actor.SourceHitPartnerId = -1; actor.Down = false;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(-300f + 40f * i, 0f, 130f));
        }
        ActorState a = m.Players[first], d = m.Players[second];
        a.SourceRawFlags &= ~0x20; d.SourceRawFlags &= ~0x20;
        a.SourceMode = SourceActorMode.Receiver; d.SourceMode = SourceActorMode.DefensiveBack;
        a.Role = ActorRole.Receiver; d.Role = ActorRole.Defender;
        a.SourceControllerStation = 0; d.SourceControllerStation = 1;
        m.SourceStationPlayers[0] = first; m.SourceStationPlayers[1] = second; m.ControlledPlayerId = first;
        a.Facing = a.TargetFacing = a.UpperFacing = a.TargetUpperFacing = 256;
        d.Facing = d.TargetFacing = d.UpperFacing = d.TargetUpperFacing = 768;
        SourcePlayerDriver.SetSourcePosition(a, new SourceVector3(0f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(d, new SourceVector3(separation, 0f, 0f));
        var draw = Bind<Func<int, int>>(game, "SourceDraw");
        a.AnimationState.FullBody("run_wb_anim", a, m.SourceTick, draw, AnimationSelectionEvidence.SourceVerified);
        d.AnimationState.FullBody("run_anim", d, m.SourceTick, draw, AnimationSelectionEvidence.SourceVerified);
        a.SourceVelocity = d.SourceVelocity = default(FieldVector);
        SourceBallDriver.SetPosition(m, new SourceVector3(0f, 14f, 0f));
        m.SourceBallTarget = m.SourceBallPosition;
        Cache(game);
    }

    private static void Cache(GameSimulation game)
    {
    SourceVector3 focus = game.SourceBallWorldPosition;
    var eye = new SourceVector3(focus.X - 120f, 85f, focus.Z - 40f);
    float theta = (float)Math.Atan2(focus.X - eye.X, focus.Z - eye.Z);
    SourceCinematicTransform transform = SourceCinematicTransform.FromState(
        new SourceCinematicSeed(eye, focus, 150f, theta, -.4f));
    game.Model.SourceMovementCamera = new SourceMovementCameraFrame(transform.Position, transform.Transform, 512f, 384f);
    game.Model.SourceMovementCameraTick = game.SourceTick;
    game.Model.SourceCameraHeading = (int)(theta * 1024f / SourceCinematicMath.TwoPi) & 1023;
    Bind<Action>(game, "CacheSourceGeometry")();
        Bind<Action>(game, "CacheSourcePlayerCameraJoints")();
    }

    private static void Wake(GameSimulation game, PlayerCommand command = default(PlayerCommand))
    {
        long tick = game.SourceTick;
        do { game.Step(command); } while (game.SourceTick == tick);
    }
    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object> {
            { "schema", "source-contact-checks/v1" }, { "status", "error" },
            { "full_contact_or_game_parity", false }, { "native_or_browser_acceptance", false } };
        try
        {
            string canonicalInputs = null;
            if (args.Length >= 2 && args[args.Length - 2] == "--c2-inputs")
            {
                canonicalInputs = args[args.Length - 1];
                args = args.Take(args.Length - 2).ToArray();
            }
            bool heading = args.Length == 6 && args[2] == "heading-boundary";
            bool movementBearing = args.Length == 6 && args[2] == "movement-bearing";
            bool routeTarget = args.Length == 6 && args[2] == "route-target";
            bool c2 = args.Length == 8 && (args[2] == "c2" || args[2] == "c2-natural");
            bool m1Sound = args.Length == 9 && args[2] == "c2-m1-sound";
            if (args.Length != 5 && !heading && !movementBearing && !routeTarget && !c2 && !m1Sound) throw new ArgumentException("contact272-fixture.json artifacts inputs|emit|verify|kernel|sound-short|heading-boundary|movement-bearing|route-target|c2|c2-natural|c2-m1-sound output-directory sound-functions.safn [original-source-root] [c2-inputs] [explicit-c2-original-oracle] [old-nine-inputs]");
            output = Path.GetFullPath(args[3]); Directory.CreateDirectory(output);
            Load(args[0], args[1], args[4], canonicalInputs, m1Sound ? args[6] : null);
            if (args[2] == "emit" || args[2] == "verify")
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(args[1], "native/receipt.json"))))
                    sequenceNames = document.RootElement.GetProperty("sequence_names").EnumerateArray().Select(v => v.GetString()).ToArray();
            if (args[2] == "inputs") InputChecks();
            else if (args[2] == "emit") EmitOracleInputs();
            else if (args[2] == "verify") VerifyOracle();
            else if (args[2] == "kernel") KernelChecks();
            else if (args[2] == "kernel-result-timing") BodyTackleAndDown();
            else if (args[2] == "sound-short") SoundIntegrationChecks();
            else if (heading) HeadingBoundaryChecks(args[5]);
            else if (movementBearing) MovementBearingBoundaryChecks(args[5]);
            else if (routeTarget) RouteTargetFacingBoundaryChecks(args[5]);
            else if (c2) C2CallbackChecks(args[5], args[6], args[7], args[2] == "c2-natural");
            else if (m1Sound) C2M1SoundChecks(args[6], args[7], args[8]);
            else throw new ArgumentException("Unknown source contact test layer.");
            result["status"] = "passed"; result["assertions"] = assertions;
            result["source_programs"] = programs.Count;
            result["source_instructions"] = programs.Values.Sum(rows => rows.Length);
            result["base157_sha256"] = legacy157.Animations.InventorySha256;
            result["production272_sha256"] = FullKernelInputs.ContactAnimationSha256;
            result["production_profile"] = real.IsC2M1Profile ? SourceTeamResourceProfile.C2M1StableId
                : real.IsC2Profile ? SourceTeamResourceProfile.C2StableId : SourceTeamResourceProfile.ContactStableId;
            result["real_input_fixture_sha256"] = real.ManifestSha256;
            result["actual_animation_clips"] = real.Animations.SequenceNames.Count;
            result["actual_animation_frames"] = real.Animations.FrameCount;
            if (real.IsC2Profile) result["c2_animation_sha256"] = real.Animations.InventorySha256;
            result["extra115_sha256"] = ExtraSha; result["kernel_scenarios"] = kernelEvidence.Count;
            result["actual_bound_SAFN"] = real.SoundFunctions.PayloadSha256;
            result["sound_short_scenarios"] = soundEvidence.Count;
            result["actual_synchronous_tuple_lines"] = observedSoundTupleLines.OrderBy(n => n).ToArray();
            result["actual_kernel_puppet_callbacks"] = puppetInputs.Count;
            result["added_canonical_result_timing_assertions"] = canonicalResultTimingAssertions;
            result["retained_C1_assertions"] = assertions - canonicalResultTimingAssertions;
            result["emitted_detector_cases"] = emittedDetectorCases;
            result["emitted_segment_cases"] = emittedSegmentCases;
            result["verified_detector_cases"] = verifiedDetectorCases;
            result["verified_segment_cases"] = verifiedSegmentCases;
            if (c2 || m1Sound) AddC2Result(result);
            if (args[2] == "kernel")
            {
                File.WriteAllLines(Path.Combine(output, "actual-kernel-puppet-inputs.tsv"), puppetInputs);
                File.WriteAllLines(Path.Combine(output, "actual-kernel-puppet-outputs.tsv"), puppetOutputs);
            }
            if (kernelEvidence.Count > 0) File.WriteAllText(Path.Combine(output, "kernel-scenarios.json"),
                JsonSerializer.Serialize(kernelEvidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
        }
        catch (ContactAssertion error)
        {
            result["status"] = "failed"; result["failed_assertion"] = error.Message;
            if (lastGame != null && output != null)
            {
                string path = Path.Combine(output, "first-assertion-failure.json");
                File.WriteAllText(path, JsonSerializer.Serialize(new {
                    schema = "source-current281-contact-assertion-failure/v1",
                    failedAssertion = error.Message, assertions,
                    actualAnimationSha256 = real.Animations.InventorySha256,
                    sourceTick = lastGame.SourceTick, phase = lastGame.Model.Phase,
                    lastGame.C2State, lastGame.SourceEvents, lastGame.ContactEvents, lastGame.C2Events,
                    lastGame.Model.SourceRandomProcess, lastGame.Model.SourceRandomActor,
                    lastGame.Model.SourceRandomProcessOrder, lastGame.Model.SourceRandomCallIndex,
                    lastGame.Model.SourceRandomDraws, lastGame.Model.SourceFlags,
                    lastGame.Model.SourceRules, lastGame.Model.SourceLastResult, lastGame.Model.SourceResultView,
                    lastGame.Model.CarrierId, lastGame.Model.TargetPlayerId,
                    lastGame.Model.SourcePlayEndX, lastGame.Model.SourcePlayEndZ,
                    pendingResult = lastGame.Model.SourceC2PendingResult == null ? null : (object)new {
                        lastGame.Model.SourceC2PendingResult.QueuedTick, lastGame.Model.SourceC2PendingResult.Epoch,
                        lastGame.Model.SourceC2PendingResult.PlayerId, lastGame.Model.SourceC2PendingResult.AttackerId,
                        lastGame.Model.SourceC2PendingResult.Input },
                    actors = lastGame.Model.Players.Select(actor => new {
                        actor.Id, actor.Down, actor.Facing, actor.TargetFacing, actor.SourceMode,
                        actor.SourceHitPartnerId, actor.SourcePuppetLinkId, actor.SourceLateHittable,
                        world = SourcePlayerDriver.WorldPosition(actor),
                        actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                        actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId
                    }).ToArray(),
                    allSharedRawDraws = ((SourceInjectedRandom)lastGame.SourceRandomService).Trace,
                    traceOnly = true, inputVectorOrAssertionChanged = false
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                result["failure_trace"] = path;
            }
            result["assertions"] = assertions; Console.WriteLine(JsonSerializer.Serialize(result)); return 1;
        }
        catch (Exception error)
        {
            result["error"] = error.ToString(); result["assertions"] = assertions;
            if (lastGame != null)
            {
                var failure = new {
                    exception_type = error.GetType().FullName, error.Message,
                    source_tick = lastGame.SourceTick, phase = lastGame.Model.Phase.ToString(),
                    lastGame.Model.SourceAnimationFailure, lastGame.Model.SourceRandomProcess,
                    lastGame.Model.SourceRandomActor, lastGame.Model.SourceRandomProcessOrder,
                    lastGame.Model.SourceRandomCallIndex,
                    actors = lastGame.Model.Players.Select(actor => new {
                        actor.Id, mode = actor.SourceMode.ToString(), actor.SourceHitPartnerId, actor.SourcePuppetLinkId,
                        actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                        actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId,
                        next_program_counter = typeof(SourceAnimationLayerState).GetField("pc",
                            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.AnimationState.Primary)
                    }).ToArray(),
                    random = ((SourceInjectedRandom)lastGame.SourceRandomService).Trace.Select(draw => new {
                        draw.Order, draw.Raw, draw.Value, draw.Site.SourceTick, draw.Site.Process, draw.Site.ActorId,
                        draw.Site.ProcessOrder, draw.Site.CallIndex, draw.Site.SourceIdentity, draw.Site.BoundOrModulus
                    }).ToArray()
                };
                File.WriteAllText(Path.Combine(output, "first-software-or-input-failure.json"),
                    JsonSerializer.Serialize(failure, new JsonSerializerOptions { WriteIndented = true }));
                result["actual_failure_trace"] = Path.Combine(output, "first-software-or-input-failure.json");
            }
            Console.WriteLine(JsonSerializer.Serialize(result)); return 2;
        }
    }
}
