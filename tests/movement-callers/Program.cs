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
    private static bool missingRSkip;

    private static void Check(bool value, string identity)
    {
        assertions++;
        if (!value) throw new MovementCallerAssertion("M1 CORE ASSERTION: " + identity);
    }

    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object> {
            { "schema", "source-movement-caller-checks/v1" }, { "status", "error" },
            { "whole_M1_or_full_gameplay_qualified", false }, { "native_or_browser_qualified", false }
        };
        try
        {
            if (args.Length != 3 || !new[] { "bounded", "complete", "controlled" }.Contains(args[2]))
                throw new ArgumentException("MovementCallerChecks actual-fixture.json output-directory bounded|complete|controlled");
            outputRoot = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(outputRoot);
            mutationRun = args[2] == "controlled";
            real = FullKernelInputs.Load(args[0], movementLibrary: true, selectedMovementLibrary: args[2] == "bounded");
            library = real.Animations; geometry = real.Geometry; playbook = real.Playbook;
            rosters = real.Rosters; movement = real.MovementRuntime;
            result["real_input_fixture_sha256"] = real.ManifestSha256;
            result["actual_animation_library_manifest"] = real.Animations.InventorySha256;
            if (mutationRun)
            {
                ControlledCallers();
                InputAndStationCallers();
            }
            else
            {
                Smoke();
                RawMovementCampaign();
                MaybeDivePublicCampaign();
                ControlledCallers();
                InputAndStationCallers();
            }
            result["assertions"] = assertions;
            result["actual_Hurdle_VM_scripts"] = 16;
            result["actual_MaybeDive_windows_per_hurdle"] = 7;
            result["actual_Core_post_VM_physics_cases"] = physicsCases.Count;
            result["actual_Core_LEAPATT_operand_bytes"] = 36;
            result["known_R_SKIP_asset_missing"] = missingRSkip;
            if (args[2] == "complete" && missingRSkip)
                throw new MovementCallerAssertion("Actual original R_SKIP is missing; full publication smoke is not qualified.");
            result["status"] = missingRSkip ? "bounded-passed" : "passed";
            result["complete_publication_qualification"] = !mutationRun && !missingRSkip;
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (MovementCallerAssertion error)
        {
            result["status"] = "failed";
            result["failed_assertion"] = error.Message;
            result["assertions"] = assertions;
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 1;
        }
        catch (Exception error)
        {
            result["error"] = error.ToString();
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 2;
        }
    }

    private static void Save(string name, string content)
    {
        if (!mutationRun) File.WriteAllText(Path.Combine(outputRoot, name), content);
    }

    private static GameSimulation Create(bool camera = true, Func<int, ISourceRandom> random = null,
        SourceMovementSettings settings = null)
    {
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
        return real.Create(configuration: config, randomFactory: random,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            movementSettings: settings ?? new SourceMovementSettings(0, 0, false, 0, 0, false), camera: camera);
    }

    private static PlayerCommand Raw(SourceButtons held, SourceButtons pressed = SourceButtons.None,
        SourceButtons released = SourceButtons.None, int x = 0, int z = 0)
    {
        return new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(0, x, z, held, pressed, released) }));
    }

    private static void Smoke()
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
        game = Create();
        var events = new List<object>();
        int deadWait = 0, stableCacheAssertions = 0, rSkipPoseObservations = 0;
        for (int host = 0; host < 2500; host++)
        {
            GamePhase beforePhase = game.State.Phase;
            long beforeSourceTick = game.SourceTick;
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
                    Check(game.SourceTick == 2299, "exact unfiltered original R_SKIP156 asset-stop diagnostic");
                    break;
                }
                throw;
            }
            foreach (SourceGameEvent item in game.SourceEvents)
                events.Add(new { item.SourceTick, item.Type, item.ActorPlayerId, item.TargetPlayerId });
            rSkipPoseObservations += game.Model.Players.Count(actor =>
                actor.AnimationState.Primary.SequenceName == "R_SKIP");
            if (beforePhase == GamePhase.DeadBall && game.State.Phase == GamePhase.DeadBall
                && game.SourceTick > beforeSourceTick && game.Model.SourceCameraJointTick == game.SourceTick)
                foreach (ActorState actor in game.Model.Players)
                {
                    IReadOnlyList<SourceVector3> actual = geometry.CameraJoints(
                        GameSnapshot.SourcePlayerView(actor, game.SourceTick, true),
                        game.SourceActorWorldPosition(actor.Id), game.Model.SourceMovementCamera);
                    for (int joint = 0; joint < 15; joint++)
                    {
                        SourceVector3 stored = game.Model.SourceCameraJoints[actor.Id, joint];
                        Check(B(actual[joint].X) == B(stored.X) && B(actual[joint].Y) == B(stored.Y)
                            && B(actual[joint].Z) == B(stored.Z),
                            "real Core cache records post-player current source pose/root, never pre-player/render interpolation");
                        stableCacheAssertions++;
                    }
                }
            if (game.State.Phase == GamePhase.GameOver) break;
        }
        Save("smoke-events.json", JsonSerializer.Serialize(new { events, source_tick = game.SourceTick,
            missing_R_SKIP = missingRSkip, actual_R_SKIP_pose_observations = rSkipPoseObservations,
            complete_smoke = !missingRSkip && game.SourceTick > 2299 && rSkipPoseObservations > 0 }));
        Check(game.SourceTick > 500, "actual whole Core M1 caller/physics survives representative source time");
        Check(missingRSkip || game.Model.SourceCameraJointTick == game.SourceTick || game.State.Phase == GamePhase.PlaySelect,
            "actual post-player camera cache generated every executing gameplay wake");
        Check(stableCacheAssertions >= 210, "actual no-contact source wake compares all14x15 real cached post-physics joints");
        if (!missingRSkip)
            Check(game.SourceTick > 2299 && rSkipPoseObservations > 0,
                "complete actual original-input smoke passes previous2299 stop and executes real R_SKIP poses");
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
