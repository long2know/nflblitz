using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class PossessionChecks
{
    private static int assertions;
    internal static void Check(bool value, string name)
    { assertions++; if (!value) throw new InvalidOperationException(name); }
    internal static SourceCinematicLibrary Cinematics;
    internal static SourceNormalCameraData Normal;
    internal static SourcePossessionSoundData Speech;
    internal static FullKernelInputs Inputs;
    internal static SourceFireSpfxBundle FireData;
    private static SourceInterfaceData hudData;
    private static readonly List<string> cases = new List<string>();
    internal static readonly long[] BeforeCamera = { 25, 26, 27, 28, 1, 18, 21, 23 };
    internal static readonly long[] AfterCamera = { 19, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 22, 24, 16 };

    private static int Main(string[] args)
    {
        try
        {
            Inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
            Cinematics = SourceCinematicLibrary.Read(File.ReadAllBytes(args[3]), SourceTeamResourceProfile.C2M1ImpactSha256);
            Normal = SourceNormalCameraData.Read(File.ReadAllBytes(args[4]));
            Speech = SourcePossessionSoundData.Read(File.ReadAllBytes(args[5]));
            hudData = SourceInterfaceData.Read(File.ReadAllBytes(args[8]), SourceInterfacePackagePlan.HudSha256);
            FireData = SourceFireSpfxReader.Read(File.ReadAllBytes(Path.Combine(args[9], "manifest.json")), args[10],
                path => File.ReadAllBytes(Path.Combine(args[9], path)));
            string output = args[6];
            ProcessControls();
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(args[7])))
            {
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                    DeclaredSequence(item, output);
            }
            CoreIntegration();
            CabinetLifecycleChecks.Run(hudData, output);
            StaleCameraControl();
            SpeechDataControls(args[5]);
            OffenseTurnoverProducer();
            HudOutput(args[8]);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, cases }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("POSSESSION ASSERTION: " + error.Message + "\n" + error.StackTrace);
            return 1;
        }
    }

    internal static GameSimulation Game(int speechOrdinal = 0, bool fireEffects = false,
        SourceMatchConfiguration configurationOverride = null)
    {
        var configuration = configurationOverride ?? new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(4, Enumerable.Range(0, 7).ToArray()),
            new SourceTeamSelection(8, Enumerable.Range(0, 7).ToArray()),
            new[] { new SourceStationConfiguration(0, TeamId.Home, 3) },
            TeamId.Home, SourceCaptainSlot.First);
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        GameSimulation game = Inputs.Create(configuration: configuration, presentation: presentation,
            compatibility: SourceGameplayCompatibility.Portable, camera: false,
            fireEffects: fireEffects ? new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(FireData), 0,
                SourceFireCull.Off) : null,
            randomFactory: seed => new SourceInjectedRandom(site => site.BoundOrModulus == 5 ? speechOrdinal : 0,
                new SourceRandomProvenance("possession semantic controls", "declared supplied raw values",
                    "same Core stream, not original PRNG", SourceRandomEvidence.ExternalUnverified)));
        game.BindSourcePossessionSoundData(Speech);
        return game;
    }

    internal static void Case(string name) { cases.Add(name); }

    private static void ProcessControls()
    {
        var host = new SourcePortableProcessHost();
        host.Create(1, 0x1400, "cambot_proc", 0x100);
        host.Create(2, 0x0500, "player_proc", 0x300, 7);
        host.Create(3, 0x0f80, "show_player_names_proc", 0, 11);
        host.Create(4, 0x0f81, "cursor_proc", 0x400, 3);
        host.Create(5, 0x0f82, "nonmatching-hud", 0, 5);
        host.Create(6, 0x8001, "NODESTRUCT-control", 0, 2);
        host.BeginTick(1);
        Check(host.Visit(1), "actual CAMBOT caller wakes");
        host.SuspendMultiple(0, 0x8000);
        Check(!host.Require(1).Suspended && host.Require(2).SleepWord == 0x8007,
            "caller exception and sleeping low bits survive suspension");
        host.ResumeMultiple(0x0f80, unchecked((int)0xfffffffe));
        Check(host.Require(3).SleepWord == 11 && host.Require(4).SleepWord == 3 &&
            host.Require(5).SleepWord == 0x8005 && host.Require(6).SleepWord == 2,
            "fffffffe resumes only the two original low-bit HUD identities");
        host.Sleep(40); host.Complete();
        var detached = host.Records;
        int recorded = detached.Count;
        for (int tick = 2; tick < 41; tick++)
        {
            host.BeginTick(tick);
            if (tick == 2) Check(detached.Count == recorded && recorded > 0,
                "ordered source process records are immutable across later dispatches");
            Check(!host.Visit(1), "sleep40 cannot complete early");
            Check(!host.Visit(2) && host.Require(2).SleepWord == 0x8007,
                "suspended sleeping low bits do not count down");
        }
        host.BeginTick(41);
        Check(host.Visit(1), "sleep40 completes at its actual source boundary");
        host.ResumeAll(); host.Complete();
        Check(host.Visit(2) == false && host.Require(2).SleepWord == 6,
            "same-wake resumption visits a later slot without erasing its remaining sleep");
        host.SuspendMultiple(0x0f81, unchecked((int)0xfffffffe));
        Check(!host.Require(4).Suspended, "suspend matching does not mask the requested id");
        Check(host.Exists(0x0f81, unchecked((int)0xfffffffe)), "existp masks both operands");
        host.Kill(4);
        Check(!host.Contains(4) && host.Exists(0x0f81, unchecked((int)0xfffffffe)),
            "dead node is removed but a masked sibling still exists");
        cases.Add("original-short-process-controls");
    }

    internal static SourceCameraBoundary Boundary(long tick, JsonElement item)
    {
        int initial = tick == 1 ? 1 : 0;
        int side = initial != 0 ? 0 : item.GetProperty("off").GetInt32();
        int last = initial != 0 ? 0 : item.GetProperty("last").GetInt32();
        int mode = initial != 0 ? 1 : item.GetProperty("mode").GetInt32();
        if (item.TryGetProperty("secondAt", out JsonElement second) && tick >= second.GetInt32())
        { side = 1 - side; last = 1 - last; }
        if (item.TryGetProperty("menuAt", out JsonElement menu) && tick >= menu.GetInt32()) mode = 1;
        var actors = new SourceCameraActor[14];
        for (int id = 0; id < 14; id++)
            actors[id] = new SourceCameraActor(id, 7, 0, 256, new SourceVector3(-180 + id * 12, 0, id - 7),
                new SourceVector3(0, 0, 100));
        return new SourceCameraBoundary(tick, 0, mode, initial != 0 ? 0 : item.GetProperty("flags").GetInt32(),
            0, initial != 0 ? 0 : item.GetProperty("result").GetInt32(), 1, side, last, 30, last == 0 ? 3 : 10,
            false, false, 100, 0,
            new SourceCameraBall(new SourceVector3(-150, 12, 0), new SourceVector3(4, 2, 0), default(SourceVector3),
                new SourceVector3(100, 0, 0), new SourceVector3(0, 0, 100), item.GetProperty("type").GetInt32(),
                item.GetProperty("ballFlags").GetInt32(), 0, 3, 10), actors,
            initial == 0 && item.GetProperty("turnover").GetBoolean(), item.GetProperty("state").GetInt32());
    }

    private static void DeclaredSequence(JsonElement item, string output)
    {
        string name = item.GetProperty("name").GetString();
        GameSimulation game = Game(item.GetProperty("ordinal").GetInt32());
        var playback = new SourceCinematicPlayback(Cinematics, Normal, Boundary(1, item));
        var rows = new List<object>();
        game.ProbeAddProcessControls();
        int completions = 0;
        for (int tick = 1; tick <= item.GetProperty("ticks").GetInt32(); tick++)
        {
            SourceCameraBoundary boundary = Boundary(tick, item);
            game.ProbeDeclaredTick(boundary, playback, item, ref completions);
            foreach (SourcePossessionSpeech speech in game.SourcePossessionSpeechSubmissions)
            {
                SourceRandomDraw draw = ((SourceInjectedRandom)game.SourceRandomService).Trace.Last();
                Check(game.SourcePossessionSpeechSubmissions.Count == 1 && speech.Bank == game.C2State.AnnouncerBank &&
                    speech.PlaylistId == Speech.Playlist(item.GetProperty("ordinal").GetInt32()) &&
                    speech.Volume == Speech.Volume && speech.Pan == Speech.Pan && speech.PriorityWord == Speech.Priority &&
                    !speech.PcmOrDspPlayback && draw.Site.Process == "show_message_proc" &&
                    draw.Site.SourceTick == tick && draw.Site.BoundOrModulus == 5 &&
                    draw.Site.SourceIdentity == "sounds.c:intercepted_ball_sp:831",
                    "interception speech retains the real selected announcer bank, weighted operand and shared process RNG without claiming playback");
            }
            var state = (ISourcePossessionState)game.State;
            SourcePossessionMessageView message = state.PossessionMessage;
            var words = state.Processes.OrderBy(p => p.Serial).Select(p => new long[] { p.Serial, p.Id, p.SleepWord }).ToArray();
            var sprites = state.PossessionMessages.SelectMany(m => m.Sprites).Select(p => new long[] {
                p.Serial, p.ObjectId, p.WhiteFlash ? 1 : 0, p.ChromaKey ? 1 : 0 }).ToArray();
            var operations = game.SourcePossessionProcessRecords.Where(p =>
                p.Operation == "Create" || p.Operation == "SuspendMask" || p.Operation == "ResumeMask" ||
                p.Operation == "ResumeAllCall" || p.Operation == "Preloaded" || p.Operation == "BeginSprite" ||
                p.Operation == "Speech" || p.Operation == "DeleteSprites" || p.Operation == "DeleteTextures" ||
                p.Operation == "Sleep" && p.Value != 1).Select(p => new object[] { p.Operation, p.Id, p.Value }).ToArray();
            rows.Add(new { tick, previous = playback.PossessionOffensePrevious, timeout = playback.PossessionTimeout,
                completions, words, sprites, operations });
            if (tick == 2 && item.GetProperty("message").GetInt32() >= 0)
                Check(playback.IsPossessionSleeping && !playback.CameraFeedback.CameraCompleted && playback.LastCameraTick == 1,
                    "selection retains the prior output and suspends before integration");
            if (tick == 41 && item.GetProperty("message").GetInt32() >= 0)
                Check(playback.IsPossessionSleeping && playback.LastCameraTick == 1,
                    "sleep40 retains the same output through the last sleeping dispatch");
            if (tick == 42 && item.GetProperty("message").GetInt32() >= 0)
                Check(!playback.IsPossessionSleeping && playback.PossessionOffensePrevious == -1 &&
                    playback.PossessionTimeout == 342 && playback.OriginalNormal.NormalWakes == 2 && completions == 2,
                    "sleep continuation sets timeout after40 without re-running normal preparation");
        }
        int expectedMessage = item.GetProperty("message").GetInt32();
        Check(game.ProbeMessagesCreated == (expectedMessage < 0 ? 0 : item.TryGetProperty("secondAt", out _) ? 2 : 1),
            "exact active predicate selects its original message or the no-message countercontrol: " + name);
        File.WriteAllText(Path.Combine(output, name + ".managed.json"), JsonSerializer.Serialize(rows));
        game.Restart();
        playback.Reset(((ISourceCameraState)game.State).CameraBoundary);
        Check(!playback.IsPossessionSleeping && playback.PossessionTimeout == 0 &&
            playback.PossessionOffensePrevious == 0 && !game.SourcePossessionSpeechSubmissions.Any() &&
            !((ISourcePossessionState)game.State).Processes.Any(p => p.Id == 0x0f15 || p.Id == 0x0f10),
            "new match resets message nodes and the suspended CAMBOT stack");
        cases.Add(name);
    }

    private static void CoreIntegration()
    {
        foreach (bool fire in new[] { false, true })
        {
        GameSimulation game = Game(fireEffects: fire);
        var actualSelector = new SourceCabinetPlaySelection(hudData);
        game.SourceStationInputTransform = actualSelector.ApplyAtSourceTick;
        game.Model.Quarter = 2;
        var playback = new SourceCinematicPlayback(Cinematics, Normal, ((ISourceCameraState)game.State).CameraBoundary);
        game.SourceCameraHeadingProvider = (state, tick) =>
        {
            playback.ObserveCameraProcessSuspension(false);
            playback.CameraSourceTick(((ISourceCameraState)state).CameraBoundary, 512, 384,
                game.DrawSourceCameraRandom, game.SourceCameraProcesses);
            game.ApplySourceCameraFeedback(playback.CameraFeedback);
            if (playback.CameraFeedback.CameraCompleted)
            {
                SourceCinematicTransform frame = playback.RequireSourceCameraAtTick(tick);
                game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512, 384), tick);
            }
            else playback.ObserveCameraProcessSuspension(true);
            return playback.SourceHeading1024;
        };
        Step(game, 1);
        SourceCinematicCueDefinition shake = Cinematics.AnimationCues.First(cue => cue.Kind == "shaker");
        playback.ExecuteShakerOpcode(1, int.MaxValue / 2, 3, 3, shake.Script, shake.Line);
        game.ProbePossessionChange();
        Step(game, 2);
        SourceVector3 ball = game.Model.SourceBallPosition;
        var positions = game.Model.Players.Select(p => p.Position).ToArray();
        var frames = game.Model.Players.Select(p => p.AnimationState.Primary.FrameOrdinal).ToArray();
        uint clock = game.State is ISourceGameState ? ((ISourceGameState)game.State).PackedGameTime : 0;
        int liveTicks = game.Model.LiveTicks;
        Check(((ISourcePossessionState)game.State).Processes.Single(p => p.Id == 0x0700).LastWakeTick == 2 &&
            !new SourceHudState(game.State, hudData).StatusProcessSuspended,
            "earlier score/HUD source slot already ran before the initial camera suspension");
        string fx = fire ? FxWords(game) : null;
        if (fire) Check(game.SourceFireProcesses.Count > 0 && game.SourceFireProcesses.All(p => (p.SleepField & 0x8000) != 0),
            "actual admitted FX nodes receive the same suspension mask");
        for (int tick = 3; tick <= 102; tick++)
        {
            Step(game, tick);
            Check(game.Model.Players.Select((p, i) => p.Position.X == positions[i].X && p.Position.Z == positions[i].Z &&
                p.AnimationState.Primary.FrameOrdinal == frames[i]).All(value => value) &&
                game.Model.SourceBallPosition.X == ball.X && game.Model.SourceBallPosition.Y == ball.Y &&
                game.Model.SourceBallPosition.Z == ball.Z, "actual Core actors animation and ball freeze under possession suspension");
            Check(((ISourceGameState)game.State).PackedGameTime == clock && game.Model.LiveTicks == liveTicks,
                "actual game_proc and clock freeze rather than burning hidden ticks");
            Check(game.SourcePossessionProcessRecords.Any(p => p.Operation == "Wake" && p.Serial == GameSimulation.PostPlayerProcess) &&
                game.SourcePossessionProcessRecords.Any(p => p.Operation == "Wake" && p.Serial == GameSimulation.CursorProcess),
                "allowed post-player and low-bit HUD continuation really execute");
            Check(tick >= 42 || game.SourceCameraStageOrder == -1,
                "sleeping CAMBOT has no fabricated producer stage");
            if (fire) Check(FxWords(game) == fx && !game.SourceFireRecords.Any(p => p.Kind == "Wake"),
                "actual FX low-bit sleeps and process cursors freeze without hidden RNG wakes");
            if (tick == 41)
                Check(playback.NeedsShakeDraws && !((SourceInjectedRandom)game.SourceRandomService).Trace.Any(draw =>
                    draw.Site.SourceTick >= 2 && draw.Site.SourceIdentity.StartsWith("cambot.c:10", StringComparison.Ordinal)),
                    "pending original shaker consumes no RNG during the real camera sleep");
            if (tick == 42)
            {
                string[] actual = ((SourceInjectedRandom)game.SourceRandomService).Trace.Where(draw => draw.Site.SourceTick == 42 &&
                    draw.Site.SourceIdentity.StartsWith("cambot.c:10", StringComparison.Ordinal))
                    .Select(draw => draw.Site.SourceIdentity).ToArray();
                Check(actual.SequenceEqual(new[] { "cambot.c:1009/shaker-X", "cambot.c:1010/shaker-Z" }),
                    "pending shaker resumes with exactly the original X then Z draws after camera continuation; observed=" +
                    string.Join("|", actual) + ";cameraTick=" + playback.LastCameraTick + ";needs=" + playback.NeedsShakeDraws);
            }
        }
        StepCommand(game, 103, new PlayerCommand(move: new FieldVector(1, 0), turbo: true));
        Check(!((ISourcePossessionState)game.State).Processes.Single(p => p.Serial == GameSimulation.GameProcess).Suspended &&
            game.SourcePossessionProcessRecords.Any(p => p.Operation == "Wake" && p.Serial == 2) &&
            !game.SourcePossessionProcessRecords.Any(p => p.Operation == "Wake" && p.Serial == GameSimulation.ClockProcess) &&
            !game.SourcePossessionProcessRecords.Any(p => p.Operation == "Wake" && p.Serial == GameSimulation.StatusProcess) &&
            new SourceHudState(game.State, hudData).StatusProcessSuspended,
            "message cleanup resumes later actors this wake but earlier clock only next dispatch");
        Check(game.Model.SourceCurrentInput.Move.X == 1 && game.Model.SourceCurrentInput.Turbo,
            "later resumed player slots observe current physical input even before the earlier game_proc resumes");
        Step(game, 104);
        Check(game.SourcePossessionProcessRecords.Any(p => p.Operation == "Wake" && p.Serial == GameSimulation.ClockProcess),
            "clock resumes at its actual next level-zero visit");
        if (fire) Check(game.SourceFireRecords.Any(p => p.Kind == "Wake"),
            "actual suspended FX resumes at its next declared level-zero visit");
        game.ProbeTurnoverOnDowns(false);
        Step(game, 105);
        StepCommand(game, 106, new PlayerCommand(action: InputAction.Continue));
        Check(game.State.Phase == GamePhase.PlaySelect &&
            ((ISourcePossessionState)game.State).Processes.Single(p => p.Serial == GameSimulation.CameraProcess).Suspended,
            "actual menu transition suspends CAMBOT after the completed message");
        playback.ObserveCameraProcessSuspension(true);
        Step(game, 109);
        Check(playback.LastCameraTick == 105 && game.SourceCameraStageOrder == -1,
            "actual menu wakes retain the last completed camera without integrating");
        StepCommand(game, 110, new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.Cover));
        Check(game.State.Phase == GamePhase.PreSnap && playback.LastCameraTick == 110 &&
            !playback.IsPossessionSleeping && ((ISourcePossessionState)game.State).PossessionMessage == null,
            "actual new-play selection resumes the one camera owner with no stale message epoch");
        game.Restart();
        Check(((ISourceCameraState)game.State).CameraBoundary.OffenseTurnover == false, "new match clears actual off_turnover");
        cases.Add(fire ? "actual-Core-FX-freeze-and-resumption-boundary" : "actual-Core-freeze-and-resumption-boundary");
        }
    }

    private static string FxWords(GameSimulation game)
    { return string.Join("|", game.SourceFireProcesses.Select(p => p.Serial + ":" + p.SleepField + ":" + p.Cursor + ":" + p.ObjectSerial)); }

    private static void StaleCameraControl()
    {
        GameSimulation game = Game();
        game.SourceCameraHeadingProvider = (state, tick) => tick == 1
            ? FullKernelInputs.SetControlledMovementCamera(game, state, tick) : 0;
        Step(game, 1);
        bool rejected = false;
        try { Step(game, 2); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("M1 requires actual cambot"); }
        Check(rejected, "normal sleep1 is not permission to retain a stale or omitted camera output");
        cases.Add("no-unqualified-stale-camera-retention");
    }

    private static void SpeechDataControls(string speechPath)
    {
        byte[] altered = File.ReadAllBytes(speechPath);
        altered[8] ^= 1;
        bool rejected = false;
        try { SourcePossessionSoundData.Read(altered); }
        catch (ArgumentException error) { rejected = error.Message.Contains("pin changed"); }
        Check(rejected, "changed original speech operands fail the actual trusted pin");
        GameSimulation game = Game();
        game.Model.Quarter = 2;
        var playback = new SourceCinematicPlayback(Cinematics, Normal, ((ISourceCameraState)game.State).CameraBoundary);
        game.SourceCameraHeadingProvider = (state, tick) =>
        {
            playback.ObserveCameraProcessSuspension(false);
            playback.CameraSourceTick(((ISourceCameraState)state).CameraBoundary, 512, 384,
                game.DrawSourceCameraRandom, game.SourceCameraProcesses);
            game.ApplySourceCameraFeedback(playback.CameraFeedback);
            if (playback.CameraFeedback.CameraCompleted)
            {
                SourceCinematicTransform frame = playback.RequireSourceCameraAtTick(tick);
                game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512, 384), tick);
            }
            else playback.ObserveCameraProcessSuspension(true);
            return playback.SourceHeading1024;
        };
        Step(game, 1);
        game.ProbePossessionChange();
        game.Model.BallKind = SourceBallKind.Pass;
        game.ProbeUnbindSpeech();
        Step(game, 2);
        rejected = false;
        try { Step(game, 3); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("intercepted_ball_sp requires"); }
        Check(rejected, "missing original speech data is a visible failure, not silence or a guessed playlist");
        rejected = false;
        try { Step(game, 4); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("restart is required"); }
        Check(rejected, "failed message sound cannot retry its partially executed object/RNG process");
        game.Restart();
        game.BindSourcePossessionSoundData(Speech);
        Check(game.SourceTick == 0 && game.SourcePossessionSpeechSubmissions.Count == 0,
            "restart clears message-sound failstop without fabricating a completed submission");
        cases.Add("message-speech-pin-missing-input-and-failstop");
    }

    private static void Step(GameSimulation game, long tick)
    { while (game.SourceTick < tick) game.Step(default(PlayerCommand)); }
    private static void StepCommand(GameSimulation game, long tick, PlayerCommand command)
    {
        game.Step(command);
        while (game.SourceTick < tick) game.Step(command.Action == InputAction.None ? command : default(PlayerCommand));
    }

    private static void OffenseTurnoverProducer()
    {
        foreach (bool incomplete in new[] { false, true })
        {
            GameSimulation game = Game();
            game.ProbeTurnoverOnDowns(incomplete);
            Check(((ISourceCameraState)game.State).CameraBoundary.OffenseTurnover,
                "off_turnover is produced by the real fourth-down result rule");
            game.ProbeContinue();
            Check(!((ISourceCameraState)game.State).CameraBoundary.OffenseTurnover,
                "result-box teardown clears off_turnover before the next play");
        }
        cases.Add("actual-nfl-off-turnover-producer-and-teardown");
    }

    private static void HudOutput(string hudPath)
    {
        var data = SourceInterfaceData.Read(File.ReadAllBytes(hudPath), SourceInterfacePackagePlan.HudSha256);
        GameSimulation game = Game();
        var composer = new SourceHudComposer(data);
        game.Model.SourcePossessionMessage = new SourcePossessionMessageView(8, 33, 1, 512, 384,
            new[] { new SourcePossessionSprite(34, "msg_int", 0x5678face, true, true),
                new SourcePossessionSprite(36, "msg_int_c1", 0x5678face, true, true) });
        game.ProbePublish();
        var viewport = new SourceHudViewport(1024, 768, 1024, 768, 1);
        var primitives = composer.Compose(game.State, viewport, null, false, false)
            .Where(p => p.Element.StartsWith("possession/", StringComparison.Ordinal)).ToArray();
        Check(primitives.Length == 1 && primitives[0].Element == "possession/msg_int+msg_int_c1" &&
            primitives[0].Texture == "interface-white", "HUD consumes the actual ordered pair as its complete original authored union, never duplicate imagery");
        SourceInterfaceImage image = data.Image("msg_int");
        Check(primitives[0].Bounds.X == (256 - image.AnchorX) * 2 &&
            primitives[0].Bounds.Y == (192 - 55 - image.AnchorY) * 2,
            "possession pair retains the original source-raster center plus55 and anchors");
        game.Model.SourcePossessionMessage = new SourcePossessionMessageView(15, 33, 1, 512, 256,
            new SourcePossessionSprite[0]);
        game.ProbePublish();
        Check(!composer.Compose(game.State, viewport, null, false, false).Any(p => p.Element.StartsWith("possession/", StringComparison.Ordinal)),
            "punt return intentionally produces no overlay");
        byte[] mask = SourceHudTextureMask.WhiteFlash(new byte[] { 1, 2, 3, 77, 0, 0, 0, 255 });
        Check(mask.SequenceEqual(new byte[] { 255, 255, 255, 77, 255, 255, 255, 0 }),
            "ONE/chroma mask preserves authored alpha and rejects the original black key");
        cases.Add("shared-authored-HUD-pair-flasher-and-punt-control");
    }
}

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        internal int ProbeMessagesCreated;
        internal void ProbeAddProcessControls()
        {
            Model.SourceProcesses.Create(25, 0x0f98, "declared-FX-control", 0);
            Model.SourceProcesses.Create(26, 0x8001, "NODESTRUCT-control", 0);
            Model.SourceProcesses.Create(27, 0x0f82, "nonmatching-HUD-control", 0);
            Model.SourceProcesses.Create(28, 0x0f99, "already-sleeping-control", 0, 7);
        }
        internal void ProbePublish()
        {
            Model.SourcePossessionMessages = Array.AsReadOnly(Model.SourcePossessionMessage == null
                ? new SourcePossessionMessageView[0] : new[] { Model.SourcePossessionMessage });
            Publish();
        }
        internal void ProbeUnbindSpeech() { sourcePossessionSoundData = null; }
        internal void ProbeDeclaredTick(SourceCameraBoundary boundary, SourceCinematicPlayback playback,
            JsonElement item, ref int completions)
        {
            Model.SourceTick = boundary.SourceTick;
            Model.SourceProcesses.BeginTick(Model.SourceTick);
            Model.SourceDispatchOrdinal = Model.SourcePresentationOrder = 0;
            sourcePossessionSpeech.Clear();
            if (item.TryGetProperty("killAt", out JsonElement kill) && Model.SourceTick == kill.GetInt32())
                foreach (SourceProcessView p in Model.SourceProcesses.Processes)
                    if (p.Id == 0x0f15) Model.SourceProcesses.Kill(p.Serial);
            if (item.TryGetProperty("suspendAt", out JsonElement suspend) && Model.SourceTick == suspend.GetInt32())
                Model.SourceProcesses.SuspendMultiple(0x0f15, -1);
            if (item.TryGetProperty("resumeAt", out JsonElement resume) && Model.SourceTick == resume.GetInt32())
                Model.SourceProcesses.ResumeAll();
            foreach (SourceFireWakeSlot slot in SourceMessageSlots()) DispatchSourceMessage(slot);
            foreach (long serial in PossessionChecks.BeforeCamera)
                if (EnterSourceProcess(serial)) CompleteSourceProcess();
            if (EnterSourceProcess(CameraProcess))
            {
                Model.SourceCameraPossessionContinuation = false;
                BeginSourceProcess("cambot_proc", -1, 0x100);
                executingSourceCamera = true;
                try
                {
                    playback.ObserveCameraProcessSuspension(false);
                    playback.CameraSourceTick(boundary, 512, 384, DrawSourceCameraRandom, SourceCameraProcesses);
                    ApplySourceCameraFeedback(playback.CameraFeedback);
                    if (playback.CameraFeedback.CameraCompleted)
                    {
                        completions++;
                        SourceCinematicTransform frame = playback.RequireSourceCameraAtTick(Model.SourceTick);
                        SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512, 384), Model.SourceTick);
                    }
                    else { ProbeMessagesCreated++; playback.ObserveCameraProcessSuspension(true); }
                }
                finally { executingSourceCamera = false; CompleteSourceProcess(); }
            }
            foreach (long serial in PossessionChecks.AfterCamera)
                if (EnterSourceProcess(serial)) CompleteSourceProcess();
            Model.SourcePossessionMessage = CaptureSourcePossessionMessage();
            Publish();
        }
        internal void ProbePossessionChange()
        {
            if (SourceFireEnabled) AdmitSourceDust(Model.Players[3], 1);
            Model.Phase = GamePhase.LiveBall; Model.SourceMatchPhase = SourceMatchPhase.LiveBall;
            Model.SourceLastPossession = 1; Model.Possession = TeamId.Away; Model.CarrierId = 10;
            Model.BallKind = SourceBallKind.Fumble;
            Model.SourceBallFlags = SourceBallFlags.None;
            Model.SourceRules = new SourceRulesState(1, 0, 1, 30, 40);
            Model.ClockActive = true;
        }
        internal void ProbeTurnoverOnDowns(bool incomplete)
        {
            Model.Phase = GamePhase.LiveBall; Model.SourceMatchPhase = SourceMatchPhase.LiveBall;
            Model.SourceRules = new SourceRulesState(0, 0, 4, 30, 40);
            Model.SourceFlags = SourceGameFlags.None;
            Model.SourceLastPossession = 0; Model.Possession = TeamId.Home;
            Model.SourcePlayEndX = SourceMatchRules.WorldPosition(31, 0);
            Model.BallKind = incomplete ? SourceBallKind.Pass : SourceBallKind.Fumble;
            ApplySourcePlayEnd(incomplete ? SourcePlayEndCause.DeadBallInBounds : SourcePlayEndCause.CarrierTackledInBounds,
                3, new FieldVector(31, 0), -1);
            Publish();
        }
        internal void ProbeContinue() { ContinueSource(); Publish(); }
    }
}
