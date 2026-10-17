using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static class Program
{
    private static int assertions;
    private const string Config = "{\"schema\":\"original-source-match-package/v1\",\"profile\":1,\"station_layout\":4," +
        "\"home_team\":{\"team\":0,\"records\":[0,1,2,3,4,5,6]},\"away_team\":{\"team\":1,\"records\":[0,1,2,3,4,5,6]}," +
        "\"stations\":[],\"first_possession\":1,\"captain\":1,\"time_adjustment\":4,\"difficulty\":7," +
        "\"continue_after_cabinet_gate\":false,\"home_play\":null,\"away_play\":null," +
        "\"presentation\":{\"designated_home_side\":1,\"station_heads\":[-1,1,18,19],\"pup_bigball\":-7},\"native_gamepads_enabled\":false}";
    private const string Bootstrap = "{\"schema\":\"original-source-movement-bootstrap/v1\",\"field_index\":6," +
        "\"infinite_turbo_team_mask\":2,\"speed_powerup\":true,\"fast_turbo_team_mask\":1," +
        "\"run_out_of_bounds_team_mask\":3,\"violence_powerup\":false,\"source_hres\":512,\"source_vres\":256}";

    private static int Main(string[] args)
    {
        try
        {
            Identity();
            if (args.Length > 0 && args[0] != "-") Data(args[0]);
            if (args.Length > 1 && args[1] != "-") Consumer(args[1]);
            if (args.Length > 2) Package(args[0], args[2], args[3], args[4], args[5]);
            Console.WriteLine("PASS source SoundVM packaging: " + assertions + " actual production assertions; no DSP/native/game proof");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Identity()
    {
        Equal(SourceInterfacePackagePlan.SoundVmServicesResourceKey, "BlitzSourceInterface/original-movement157-soundvm-match-services", "new distinct sound services key");
        Equal(SourceInterfacePackagePlan.MovementServicesResourceKey, "BlitzSourceInterface/original-movement157-match-services", "historical M1 services key unchanged");
        Equal(SourceInterfacePackagePlan.ServicesResourceKey, "BlitzSourceInterface/original-match-services", "historical B1 services key unchanged");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsResourceKey, "SourceData/sound-functions-safn1-4a557304", "new exact SAFN resource key");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsSha256, "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280", "exact frozen SAFN identity");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsSha256, GameSimulation.SourceSoundVmDataSha256, "importer and actual Core binding agree on payload");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsBytes, 1983, "complete exact SAFN1 byte count");
        Equal(SourceTeamResourceProfile.Movement157.AnimationResourceKey, "SourceData/local-plyr266-movement157-f0ae9395", "157 animation key not repointed");
        Equal(SourceTeamResourceProfile.Movement157.FrameCount, 13490, "157 frames unchanged");
        foreach (byte[] bytes in new[] { null, new byte[0], new byte[1982], new byte[1984], new byte[1983] })
            Reject(() => SourceInterfacePackagePlan.ReadSoundVmFunctions(bytes, SourceInterfacePackagePlan.SoundVmFunctionsSha256), "missing/empty/wrong/forged SAFN data");
        Reject(() => SourceInterfacePackagePlan.ReadSoundVmFunctions(new byte[1983], SourceInterfacePackagePlan.AnimationSha256), "old B1 identity cannot become sound data");
        Reject(() => SourceInterfacePackagePlan.WithSourceSoundVm(null, ".", "missing.bytes", SourceInterfacePackagePlan.SoundVmFunctionsSha256), "no implicit movement157 package");
        SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Imported/new/Resources/" +
            SourceInterfacePackagePlan.SoundVmServicesResourceKey + ".asset", SourceInterfacePackagePlan.SoundVmServicesResourceKey); assertions++;
        Reject(() => SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Imported/new/Resources/" +
            SourceInterfacePackagePlan.MovementServicesResourceKey + ".asset", SourceInterfacePackagePlan.SoundVmServicesResourceKey), "old M1 output not a new sound profile");
        Reject(() => SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Resources/" +
            SourceInterfacePackagePlan.SoundVmServicesResourceKey + ".asset", SourceInterfacePackagePlan.SoundVmServicesResourceKey), "no tracked raw data output");
    }

    private static void Data(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        SourceAudioFunctionData data = SourceInterfacePackagePlan.ReadSoundVmFunctions(bytes, SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        Equal(data.PayloadSha256, Hash(bytes), "actual authorized SAFN bytes match exact data");
        Equal(data.VmBindings.Count, 6, "exact six implemented ANI_CODE sites, not all callback/speech/SPFX");
        Equal(data.Bank, "players", "actual original function-bank identity");
        byte[] wrong = (byte[])bytes.Clone(); wrong[wrong.Length / 2] ^= 1;
        Reject(() => SourceInterfacePackagePlan.ReadSoundVmFunctions(wrong, SourceInterfacePackagePlan.SoundVmFunctionsSha256), "real content mutation fails frozen identity");
        Reject(() => SourceInterfacePackagePlan.ReadSoundVmFunctions(bytes, SourceTeamResourceProfile.MovementAnimationSha256), "new157 animation pin is not a sound-data pin");
    }

    private static SourceCinematicPlayback Playback(SourceCinematicLibrary library)
    {
        return new SourceCinematicPlayback(library, new SourceCinematicSeed(new SourceVector3(-120, 100, -30),
            new SourceVector3(12, 15, 9), 160, (float)(Math.PI / 2), (float)(-25 * Math.PI / 180)));
    }

    private static SourceCallbackCue Cue(int argument = 57)
    {
        return new SourceCallbackCue(new SourcePresentationContext(4, 20, 0, "plyrseq.c", "t_dshoul_anim", 2448),
            SourcePresentationCueKind.SoundCallback, "get_hit_snd", argument, 0, 0);
    }

    private static SourceSoundVmDispatch Dispatch(SourceCallbackCue cue, SourceSoundVmStatus status, SourceAudioFunctionResult result)
    { return new SourceSoundVmDispatch(cue, default(SourceRandomContext), new SourceAudioFunctionState(), status, result, "explicit transport test fixture"); }

    private static void Consumer(string catalogPath)
    {
        byte[] bytes = File.ReadAllBytes(catalogPath);
        SourceCinematicLibrary library = SourceCinematicLibrary.Read(bytes, "794b1fa2865733677d08d5c465dad6a002966750a7d0523138f61f3a25be1fb3");
        SourceCallbackCue cue = Cue();
        var empty = new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall>(), new List<SourceRandomDraw>());
        SourceSoundVmDispatch zero = Dispatch(cue, SourceSoundVmStatus.Resolved, empty);
        SourceCinematicPlayback playback = Playback(library);
        int genericCallbacks = 0, rawOutputs = 0;
        playback.SourceCallback += callback => genericCallbacks++;
        playback.ConsumeResolvedSourceSound(zero, dispatch => rawOutputs++);
        Equal(rawOutputs, 0, "legitimate resolved0 does not fabricate output");
        Equal(genericCallbacks, 0, "resolved callback never replays old generic selector path");
        Reject(() => playback.ConsumeResolvedSourceSound(zero), "resolved cue cannot be consumed twice");
        Reject(() => playback.Execute(new SourcePresentationCueBatch(new[] { cue })), "same OriginalCue cannot replay through old generic callback");
        Equal(genericCallbacks, 0, "duplicate generic path remains uncalled");
        Reject(() => Playback(library).Execute(new SourcePresentationCueBatch(new[] { Cue() })),
            "unpaired old callback remains unsupported, not post-host selection");
        Reject(() => Playback(library).ConsumeResolvedSourceSound(Dispatch(Cue(), SourceSoundVmStatus.Unsupported, empty)), "Unsupported is not resolved silence");
        Reject(() => Playback(library).ConsumeResolvedSourceSound(Dispatch(Cue(), SourceSoundVmStatus.MissingInput, empty)), "MissingInput is not resolved silence");
        Reject(() => Playback(library).ConsumeResolvedSourceSound(Dispatch(Cue(), SourceSoundVmStatus.Resolved, null)), "missing actual completed result is not silence");
        var badGround = new SourceCallbackCue(new SourcePresentationContext(4, 20, 0, "plyrseq.c", "t_dshoul_anim", 2461),
            SourcePresentationCueKind.SoundCallback, "grnd_hit_snd", 0, 0, 0);
        Reject(() => Playback(library).ConsumeResolvedSourceSound(Dispatch(badGround, SourceSoundVmStatus.Resolved, empty)),
            "actual original literal ground operand validation remains active");
        var call = new SourceAudioResolvedOriginalSoundCall(cue,
            new SourceAudioCalleeSite("f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9", "get_hit_snd", 100),
            0, "players", 57, uint.MaxValue, 127, uint.MaxValue, 0);
        var nonempty = new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall> { call }, new List<SourceRandomDraw>());
        SourceSoundVmDispatch output = Dispatch(cue, SourceSoundVmStatus.Resolved, nonempty);
        Reject(() => Playback(library).ConsumeResolvedSourceSound(output), "nonempty resolved raw call requires a real DSP output consumer");
        SourceCinematicPlayback delivered = Playback(library);
        delivered.ConsumeResolvedSourceSound(output, dispatch => {
            rawOutputs++;
            Check(ReferenceEquals(dispatch, output), "deliver the same immutable resolved result; no re-selection/copy");
            Equal(dispatch.Result.Calls[0].Volume, uint.MaxValue, "full raw volume preserved");
            Equal(dispatch.Result.Calls[0].PriorityWord, uint.MaxValue, "full raw priority word preserved");
        });
        Equal(rawOutputs, 1, "raw consumer called exactly once");
        Reject(() => delivered.ConsumeResolvedSourceSound(output, dispatch => rawOutputs++), "duplicate raw dispatch not delivered twice");
        Equal(rawOutputs, 1, "output count remains once");
        var wrongCall = new SourceAudioResolvedOriginalSoundCall(Cue(), call.CalleeSourceSite, 0, "players", 57, 1, 1, 1, 0);
        Reject(() => Playback(library).ConsumeResolvedSourceSound(Dispatch(cue, SourceSoundVmStatus.Resolved,
            new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall> { wrongCall }, new List<SourceRandomDraw>())),
            dispatch => rawOutputs++), "raw call must retain same OriginalCue reference");
        var down = new SourceCallbackCue(new SourcePresentationContext(4, 20, 0, "plyrseq.c", "fall_04", 15995),
            SourcePresentationCueKind.PlayerDownCallback, "player_down", 0, 0, 0);
        Reject(() => Playback(library).ConsumeResolvedSourceSound(Dispatch(down, SourceSoundVmStatus.Resolved, empty)), "PlayerDown already executed, never sound replay");
        SourceCinematicPlayback downPlayback = Playback(library);
        downPlayback.Execute(new SourcePresentationCueBatch(new[] { down }));
        Equal(downPlayback.LastObservedPlayerDown.SourceLine, 15995, "PlayerDown remains its existing ordering token, not a sound selector");
        Console.WriteLine("Actual original-catalog consumer: once-only0/nonempty/error transport; typed fixture, not a new sound selector/DSP oracle");
    }

    private static void Package(string sound, string hud, string book, string movement, string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "configuration.json"), Config, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), Bootstrap, new UTF8Encoding(false));
        SourceInterfacePackagePlan m1 = SourceInterfacePackagePlan.CreateMovement157(hud, "interface.json", SourceInterfacePackagePlan.HudSha256,
            book, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256, output, "configuration.json", Hash(Encoding.UTF8.GetBytes(Config)),
            Path.GetDirectoryName(movement), Path.GetFileName(movement), SourceInterfaceMovementData.ContentSha256,
            output, "bootstrap.json", Hash(Encoding.UTF8.GetBytes(Bootstrap)));
        Check(!m1.HasSoundVm && m1.SoundVmData == null, "old8 M1 plan stays explicitly old, never silently sound-upgraded");
        Equal(m1.SelectedServicesResourceKey, SourceInterfacePackagePlan.MovementServicesResourceKey, "old M1 selected services key retained");
        Reject(() => m1.CopySoundVmFunctions(), "old M1 plan cannot invent sound bytes");
        SourceInterfacePackagePlan current = SourceInterfacePackagePlan.WithSourceSoundVm(m1,
            Path.GetDirectoryName(sound), Path.GetFileName(sound), SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        Check(current.HasSoundVm, "new sound profile is explicit");
        Equal(current.SelectedServicesResourceKey, SourceInterfacePackagePlan.SoundVmServicesResourceKey, "new resource version selected only with actual sound input");
        Equal(current.Profile.AnimationResourceKey, m1.Profile.AnimationResourceKey, "same157 animation identity");
        Equal(Hash(current.CopyBootstrap()), Hash(m1.CopyBootstrap()), "same explicit original-DIP/M1 settings bytes");
        Equal(current.Configuration.Presentation.PupBigBall, -7, "same whole original presentation powerup");
        Equal(Hash(current.CopySoundVmFunctions()), SourceInterfacePackagePlan.SoundVmFunctionsSha256, "exact1983-byte resource output bytes");
        byte[] copy = current.CopySoundVmFunctions(); copy[0] ^= 1;
        Equal(Hash(current.CopySoundVmFunctions()), SourceInterfacePackagePlan.SoundVmFunctionsSha256, "saved sound bytes cannot be changed by a caller");
        Reject(() => SourceInterfacePackagePlan.WithSourceSoundVm(m1, output, "missing.safn", SourceInterfacePackagePlan.SoundVmFunctionsSha256), "missing actual sound file");
        Reject(() => SourceInterfacePackagePlan.WithSourceSoundVm(m1, Path.GetDirectoryName(sound), "../escape.safn", SourceInterfacePackagePlan.SoundVmFunctionsSha256), "no sound input traversal");
        Reject(() => SourceInterfacePackagePlan.WithSourceSoundVm(current, Path.GetDirectoryName(sound), Path.GetFileName(sound), SourceInterfacePackagePlan.SoundVmFunctionsSha256), "no sound rebind/alias");
        Check(!Directory.Exists(Path.Combine(output, "Assets")), "preflight makes no fake Unity resource or playback");
    }

    private static string Hash(byte[] bytes)
    { using (SHA256 sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))); }
    private static void Equal<T>(T actual, T expected, string label)
    { Check(EqualityComparer<T>.Default.Equals(actual, expected), label); }
    private static void Check(bool value, string label)
    { assertions++; if (!value) throw new InvalidOperationException("FAIL sound-packaging: " + label); }
    private static void Reject(Action action, string label)
    { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, label + " must reject"); }
}
