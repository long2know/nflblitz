using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class Program
{
    private static int assertions;
    private const string AnimationHash = "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9";
    private const string ImpactHash = "738d712b6f80d8a9456338548e7bbce1fdf88baa46f342a7c45efffd02a8f23a";
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
            InterpolationContract();
            if (args.Length != 0)
            {
                Check(args.Length == 8, "all actual animation/base/fixture/impact/old-impact/registry/HUD/output paths explicit");
                Actual(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
            }
            Console.WriteLine("PASS Contact272 presentation: " + assertions +
                " production assertions; actualData=" + (args.Length != 0) + "; no Editor/native/DSP/full-match proof");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CONTACT ASSERTION: " + error.Message);
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Identity()
    {
        SourceTeamResourceProfile profile = SourceTeamResourceProfile.Contact272;
        Equal(profile.StableId, "original-teams-contact272", "explicit Contact272 stable identity");
        Equal(profile.AnimationResourceKey, "SourceData/local-plyr266-contact272-9cae962b", "new272 resource key not old157");
        Equal(profile.AnimationSha256, AnimationHash, "trusted new272 manifest identity");
        Equal(SourceTeamResourceProfile.ContactImpactSha256, ImpactHash, "trusted new C1 impact generation");
        Equal(profile.ClipCount, 272, "new272 exact clip count");
        Equal(profile.FrameCount, 24892, "new272 exact selected frame count");
        Check(profile.IsContact272 && !profile.IsMovement157 && profile.UsesMovementData, "distinct contact profile retains mandatory M1 data");
        Check(ReferenceEquals(SourceTeamResourceProfile.FromStableId(profile.StableId), profile), "stable lookup selects actual contact profile");
        Equal(profile.TeamResourceKey, "BlitzSourcePlayers/original-teams-contact272/team-presentation", "new canonical team key");
        Equal(profile.ImpactResourceKey, "BlitzSourcePlayers/original-teams-contact272/impact-library", "new canonical impact key");
        Equal(profile.AnimationAssetPath, "Assets/Imported/original-teams-contact272/Resources/" +
            profile.AnimationResourceKey + ".asset", "new canonical animation asset path");
        Equal(SourceInterfacePackagePlan.ContactServicesResourceKey, "BlitzSourceInterface/original-contact272-soundvm-match-services", "new C1 services key");
        Equal(SourceInterfacePackagePlan.SoundVmServicesResourceKey, "BlitzSourceInterface/original-movement157-soundvm-match-services", "historical sound services key unchanged");
        Equal(SourceInterfacePackagePlan.MovementServicesResourceKey, "BlitzSourceInterface/original-movement157-match-services", "historical movement services key unchanged");
        Equal(SourceInterfacePackagePlan.ServicesResourceKey, "BlitzSourceInterface/original-match-services", "historical B1 services key unchanged");
        Equal(SourceTeamResourceProfile.Movement157.AnimationSha256, "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e", "historical157 manifest unchanged");
        Equal(SourceTeamResourceProfile.Movement157.AnimationResourceKey, "SourceData/local-plyr266-movement157-f0ae9395", "historical157 resource unchanged");
        Equal(SourceTeamResourceProfile.Movement157.FrameCount, 13490, "historical157 frames unchanged");
        Equal(SourceTeamResourceProfile.B1Final117.AnimationSha256, "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274", "historical117 manifest unchanged");
        Equal(SourceTeamResourceProfile.B1Final117.FrameCount, 10288, "historical117 frames unchanged");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsSha256, GameSimulation.SourceSoundVmDataSha256, "same mandatory SAFN pin as actual Core");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsBytes, 1983, "complete SAFN length");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsResourceKey, "SourceData/sound-functions-safn1-4a557304", "unchanged shared SAFN key, not old services alias");
        Reject<SourceInterfaceException>(() => profile.RequireInventory(SourceTeamResourceProfile.MovementAnimationSha256, 157, 13490), "157 cannot be272");
        Reject<SourceInterfaceException>(() => profile.RequireInventory(SourceTeamResourceProfile.MovementAnimationSha256, 272, 24892), "correct272 counts cannot conceal wrong manifest");
        Reject<SourceInterfaceException>(() => profile.RequireInventory(AnimationHash, 271, 24892), "missing contact clip cannot be accepted");
        Reject<SourceInterfaceException>(() => profile.RequireInventory(AnimationHash, 272, 24891), "missing selected frame cannot be accepted");
        Reject<SourceInterfaceException>(() => profile.RequireLibrary(null), "no null/adapter production library");
        Reject<SourceInterfaceException>(() => SourceInterfacePackagePlan.WithContact272(null), "no inferred movement or sound inputs");
        Reject<SourceInterfaceException>(() => SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Imported/x/Resources/" +
            SourceInterfacePackagePlan.SoundVmServicesResourceKey + ".asset", SourceInterfacePackagePlan.ContactServicesResourceKey), "old sound resource cannot be C1 output");
        Reject<SourceInterfaceException>(() => SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Resources/" +
            SourceInterfacePackagePlan.ContactServicesResourceKey + ".asset", SourceInterfacePackagePlan.ContactServicesResourceKey), "no original resource in tracked asset roots");
        profile.RequireInventory(AnimationHash, 272, 24892);
        assertions++;
    }

    private static PlayerView Basic(long tick, SourceActorContactView contact, ActorAction action = ActorAction.Run)
    {
        var sample = new SourceAnimationSample("EXPLICIT_CONTRACT_FIXTURE", 0, 0);
        var animation = new ActorAnimationView(tick, action, AnimationSelectionEvidence.SourceVerified,
            new SourceAnimationLayer("explicit_contract_fixture", 1, sample), default(SourceVector3));
        return new PlayerView(4, TeamId.Home, new FieldVector(50 + tick * .01f, 0), default(FieldVector), 1,
            facingDirection: new FieldVector(1, 0), role: ActorRole.Receiver, animation: animation,
            source: new SourceActorView(0, contact: contact));
    }

    private static void InterpolationContract()
    {
        var clear = new SourceActorContactView(-1, -1, 0, 0, 0, 0);
        PlayerView before = Basic(4, clear), after = Basic(5, clear);
        Check(SourceActorPoseSampler.CanInterpolate(before, after), "ordinary M1 movement still interpolates");
        foreach (SourceActorContactView active in new[] {
            new SourceActorContactView(11, -1, 0, 0, 0, 0),
            new SourceActorContactView(-1, 11, 0, 0, 0, 0),
            new SourceActorContactView(-1, -1, 0, 4, 12, 0) })
        {
            Check(!SourceActorPoseSampler.CanInterpolate(before, Basic(5, active)), "real hit/puppet/attack output stays at current source snapshot");
            Check(!SourceActorPoseSampler.CanInterpolate(Basic(4, active), after), "contact release boundary does not blend old puppet output");
        }
        Check(!SourceActorPoseSampler.CanInterpolate(before, Basic(5, new SourceActorContactView(-1, -1, 5, 0, 0, 0))), "actual release tick is a presentation seam");
        Check(!SourceActorPoseSampler.CanInterpolate(before, Basic(5, clear, ActorAction.Contact)), "contact animation action cannot invent render pose");
        Check(!SourceActorPoseSampler.CanInterpolate(Basic(4, default(SourceActorContactView)), after), "unknown contact state is not inferred from role");
    }

    private static SourceAnimationLibrary Library(string root)
    {
        return SourceAnimationLibrary.Read(File.ReadAllBytes(Path.Combine(root, "export-inventory.json")),
            path => File.ReadAllBytes(Path.Combine(root, path)));
    }

    private static void Actual(string bundle, string baseBundle, string fixture, string impactPath, string oldImpactPath,
        string registry, string hudRoot, string output)
    {
        SourceAnimationLibrary library = Library(bundle), old = Library(baseBundle);
        SourceTeamResourceProfile.Contact272.RequireLibrary(library);
        Equal(library.FrameCount, 24892, "actual new272 selected frames");
        Equal(library.SequenceNames.Count, 272, "actual new272 clips");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact272.RequireLibrary(old), "actual typed immutable157 is incompatible before C1 construction");
        foreach (string name in old.SequenceNames)
        {
            ISourceAnimationClip previous = old.GetClip(name), current = library.GetClip(name);
            Equal(current.DecodedSha256, previous.DecodedSha256, "all157 decoded bytes preserved: " + name);
            Check(current.SelectedSourceFrames.SequenceEqual(previous.SelectedSourceFrames), "all157 source index maps preserved: " + name);
            foreach (int index in previous.SelectedSourceFrames)
            {
                SourceAnimationFrame a = previous.GetFrame(index), b = current.GetFrame(index);
                Equal(a.AuthoringRoot, b.AuthoringRoot, "all157 actual authoring roots preserved");
                Equal(a.ScaledRoot, b.ScaledRoot, "all157 actual scaled roots preserved");
                Check(a.Pose.Rotations.SequenceEqual(b.Pose.Rotations), "all157 actual fifteen-bone frames preserved");
            }
        }
        SourceAnimationClip replay = (SourceAnimationClip)library.GetClip("R_SKIP");
        Check(replay.ConverterReplay != null && replay.ConverterReplay.Policy == "wan2seq-retained-limb/v1", "authentic R_SKIP retained-limb qualification preserved");
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(registry)))
        foreach (JsonElement entry in document.RootElement.GetProperty("clips").EnumerateArray())
        {
            string name = entry.GetProperty("runtime_name").GetString();
            ISourceAnimationClip clip = library.GetClip(name);
            Equal(clip.DecodedSha256, entry.GetProperty("sha256").GetString(), "actual disjoint C1 clip hash: " + name);
            Equal(clip.SourcePath, entry.GetProperty("source_path").GetString(), "actual C1 WAN source identity: " + name);
            Check(clip.SelectedSourceFrames.SequenceEqual(entry.GetProperty("selected_source_frame_indices").EnumerateArray().Select(i => i.GetInt32())), "exact C1 selected/source ordinal map: " + name);
            foreach (int frame in clip.SelectedSourceFrames)
                Equal(clip.GetFrame(frame).Pose.Rotations.Count, 15, "actual C1 frame has fifteen original joints");
        }
        Equal(library.GetClip("TIE1C_A").SequenceName, "TIE1C_A", "newly reachable TIE1C_A is real, not157 alias");
        byte[] manifest = File.ReadAllBytes(Path.Combine(bundle, "export-inventory.json"));
        Reject<PlayerAssetException>(() => SourceAnimationLibrary.Read(manifest, path =>
            path == "clips/TIE1C_A.json" ? null : File.ReadAllBytes(Path.Combine(bundle, path))), "missing actual newly reachable clip fails loader");
        Reject<PlayerAssetException>(() => SourceAnimationLibrary.Read(manifest, path => {
            byte[] bytes = File.ReadAllBytes(Path.Combine(bundle, path));
            if (path == "clips/TIE1C_A.json") bytes[bytes.Length / 2] ^= 1;
            return bytes;
        }), "actual clip byte mutation fails manifest pin");
        byte[] changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("\"original-teams-contact272\"", "\"wrong-contact-generation\""));
        Check(!manifest.SequenceEqual(changed), "well-formed actual manifest mutation changed bytes");
        SourceAnimationLibrary repinned = SourceAnimationLibrary.Read(changed, path => File.ReadAllBytes(Path.Combine(bundle, path)));
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact272.RequireLibrary(repinned), "self-consistent wrong manifest still fails trusted272 identity");
        SourceCinematicLibrary impact = SourceCinematicLibrary.Read(File.ReadAllBytes(impactPath), ImpactHash);
        SourceTeamResourceProfile.Contact272.RequireImpact(impact);
        SourceCinematicLibrary historical = SourceCinematicLibrary.Read(File.ReadAllBytes(oldImpactPath), "794b1fa2865733677d08d5c465dad6a002966750a7d0523138f61f3a25be1fb3");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact272.RequireImpact(historical), "old impact catalog cannot masquerade as C1 generation");
        Equal(impact.ContactAnimationManifestSha256, library.InventorySha256, "impact bound to same actual272 manifest");
        Equal(impact.CameraOperandBindings.Count, 25, "all exact C1 camera operand/opcode source pairs");
        Equal(impact.AnimationCues.Count, 469, "complete old416 plus25 camera operands plus28 speech sites");
        foreach (SourceCinematicCueDefinition cue in historical.AnimationCues)
            Check(impact.RequireExecutedOpcode(cue.Script, cue.Line, cue.Kind).Arguments.SequenceEqual(cue.Arguments), "every old camera/callback site preserved");
        foreach (SourceCinematicCameraBinding binding in impact.CameraOperandBindings)
            Equal(impact.RequireExecutedOpcode(binding.Script, binding.OperandLine, "camera").Arguments[0], binding.Table, "real exact C1 .word site selects unchanged table");
        foreach (string table in historical.Tables.Keys)
        {
            Equal(impact.Tables[table].Count, historical.Tables[table].Count, "all original weighted table entries preserved");
            for (int i = 0; i < historical.Tables[table].Count; i++)
            {
                SourceCinematicEntry a = historical.Selected(table, i), b = impact.Selected(table, i);
                Equal(a.RawMode, b.RawMode, "same original camera mode");
                Equal(a.ModeTicks, b.ModeTicks, "same original camera timing");
                Equal(a.TravelTicks, b.TravelTicks, "same original camera travel timing");
                Equal(a.ThetaDegrees, b.ThetaDegrees, "same original camera theta");
                Equal(a.PhiDegrees, b.PhiDegrees, "same original camera phi");
                Equal(a.Distance, b.Distance, "same original camera distance");
                Check(a.ControlPoints.SequenceEqual(b.ControlPoints), "same actual original camera geometry");
            }
        }
        Check(impact.ShakerExponent.SequenceEqual(historical.ShakerExponent), "same actual original shaker values");
        FullKernelInputs real = FullKernelInputs.Load(fixture, movementLibrary: true);
        foreach (SourceAudioVmBinding binding in real.SoundFunctions.VmBindings)
        {
            SourceCinematicCueDefinition definition = impact.RequireExecutedOpcode(binding.ScriptSymbol, binding.SourceLine, "callback");
            Equal(definition.Arguments[0], binding.FunctionSymbol, "six synchronous SoundVM sites retain exact source tuple");
            SourceAnimationInstruction instruction = SourceAnimationPrograms.Get(binding.ScriptSymbol).Instructions.Single(
                item => item.Op == SourceAnimationOp.PresentationCallback && item.SourceLine == binding.SourceLine);
            Equal(instruction.A, binding.Argument0, "six exact compiled SoundVM argument0 values retained without guessing source macros");
            Equal(instruction.B, binding.Argument1, "six exact compiled SoundVM argument1 values retained");
            Equal(instruction.C, binding.Argument2, "six exact compiled SoundVM argument2 values retained");
        }
        FactoryAndPose(library, real, impact);
        Package(real, hudRoot, Path.GetDirectoryName(fixture), output);
    }

    private static SourceVector3[] Pivots(FullKernelInputs real)
    {
        var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
        return real.Player.Nodes.Select(node => conversion.Convert(node.BindPivot))
            .Select(point => new SourceVector3(point.X, point.Y, point.Z)).ToArray();
    }

    private static object Field(object value, string name)
    { return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }

    private static GameSimulation Factory(ISourceAnimationLibrary library, FullKernelInputs real, SourceAudioFunctionData sound)
    {
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }), new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First);
        return SourceContactComposition.CreateSimulation(library, Pivots(real), real.Playbook, config, real.Rosters,
            new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0), real.MovementRuntime,
            new SourceMovementSettings(0, 0, false, 0, 0, false), sound);
    }

    private static void FactoryAndPose(SourceAnimationLibrary library, FullKernelInputs real, SourceCinematicLibrary impact)
    {
        Reject<SourceInterfaceException>(() => Factory(real.Animations, real, real.SoundFunctions), "factory rejects actual old157 before game construction");
        Reject<SourceInterfaceException>(() => Factory(library, real, null), "factory requires actual SAFN before first wake");
        GameSimulation game = Factory(library, real, real.SoundFunctions);
        Check(ReferenceEquals(Field(game, "sourceAnimations"), library), "actual Core receives SAME canonical272 instance");
        object geometry = Field(game, "sourceGeometry");
        Check(geometry is ISourceActorCameraGeometry && ReferenceEquals(Field(geometry, "library"), library), "actual collision/camera geometry receives SAME272 instance");
        Check(ReferenceEquals(Field(game, "sourceSoundVmData"), real.SoundFunctions), "exact SAFN bound before factory returns/first wake");
        game.SourceCameraHeadingProvider = (snapshot, tick) => FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
        game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand));
        Equal(game.SourceTick, 1L, "actual bound production272 factory executes first original source wake");
        game.Restart();
        Check(ReferenceEquals(Field(game, "sourceSoundVmData"), real.SoundFunctions), "restart retains immutable SAFN binding");

        ActorState attacker = game.Model.Players[4], victim = game.Model.Players[11];
        attacker.SourceHitPartnerId = victim.Id; victim.SourceHitPartnerId = attacker.Id;
        attacker.SourcePuppetLinkId = victim.Id; victim.SourcePuppetLinkId = attacker.Id;
        attacker.Facing = attacker.TargetFacing = 256; victim.Facing = victim.TargetFacing = 768;
        SourcePlayerDriver.SetSourcePosition(attacker, new SourceVector3(4, 0, 2));
        SourcePlayerDriver.SetSourcePosition(victim, new SourceVector3(10, 0, 2));
        Func<int, int> draw = (Func<int, int>)typeof(GameSimulation).GetMethod("SourceDraw",
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
            .CreateDelegate(typeof(Func<int, int>), game);
        attacker.AnimationState.FullBody("tie1c_a_anim", attacker, 0, draw, AnimationSelectionEvidence.SourceVerified);
        victim.AnimationState.FullBody("tie1c_v_anim", victim, 0, draw, AnimationSelectionEvidence.SourceVerified);
        for (int i = 1; i <= 5; i++)
        {
            game.Model.SourceTick = i;
            attacker.AnimationState.Primary.Advance(attacker, i, draw);
            victim.AnimationState.Primary.Advance(victim, i, draw);
        }
        PlayerView previous = GameSnapshot.SourcePlayerView(victim, 5);
        SourcePlayerDriver.SetSourcePosition(attacker, new SourceVector3(8, 0, -1));
        attacker.Facing = attacker.TargetFacing = 320;
        game.Model.SourceTick = 6;
        attacker.AnimationState.Primary.Advance(attacker, 6, draw);
        victim.AnimationState.Primary.Advance(victim, 6, draw);
        PlayerView current = GameSnapshot.SourcePlayerView(victim, 6);
        Equal(current.Animation.Primary.Sample.SequenceName, "TIE1C_V", "actual newly reachable C1 victim program executes original clip");
        Equal(GameSnapshot.SourcePlayerView(attacker, 6).Animation.Primary.Sample.SequenceName, "TIE1C_A", "actual newly reachable C1 attacker program executes original clip");
        Equal(current.Source.Contact.PuppetPlayerId, attacker.Id, "renderer input retains actual Core puppet link");
        Check(current.Position.X != previous.Position.X || current.Position.Z != previous.Position.Z, "actual Core puppet position changes meaningfully");
        Check(!SourceActorPoseSampler.CanInterpolate(previous, current), "actual C1 snapshot cannot use render-only interpolation");
        SourceVector3[] pivots = Pivots(real);
        SourceEvaluatedPose expected = SourceActorPoseSampler.Evaluate(library, pivots, current, current, 1);
        foreach (float interpolation in new[] { 0f, .25f, .75f, 1f })
        {
            SourceEvaluatedPose consumed = SourceActorPoseSampler.Evaluate(library, pivots, previous, current, interpolation);
            for (int joint = 0; joint < 15; joint++)
            {
                Equal(consumed.Joints[joint].Position, expected.Joints[joint].Position, "actual C1 source root/current-frame position not invented");
                Equal(consumed.Joints[joint].Rotation, expected.Joints[joint].Rotation, "actual C1 current-facing joint pose not interpolated fiction");
                SourceVector3 world = ((ISourceActorGeometry)geometry).WorldJoint(current, joint);
                SourceVector3 local = expected.Joints[joint].Position;
                Near(world.X, SourceMatchRules.SourceWorldFromAbsoluteYards(current.Position.X) + local.X, "actual Core/render root placement seam");
                Near(world.Y, current.Height * SourceTiming.SourceUnitsPerYard + local.Y, "actual Core/render height seam");
                Near(world.Z, current.Position.Z * SourceTiming.SourceUnitsPerYard - local.Z, "actual Core/render handedness seam");
            }
        }
        SourceSoundVmDispatch[] unsupported = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
        Check(unsupported.Any(d => d.Status == SourceSoundVmStatus.Unsupported && d.OriginalCueContext.SourceLine == 9709),
            "actual C1 extra sound tuple remains explicit Unsupported, never invented resolved silence");
        SourceSoundVmDispatch raw = unsupported.First(d => d.OriginalCueContext.SourceLine == 9709);
        var playback = new SourceCinematicPlayback(impact, new SourceCinematicSeed(new SourceVector3(-120, 90, -20),
            new SourceVector3(0, 15, 0), 150, 1, -.4f));
        Reject<InvalidOperationException>(() => playback.ConsumeResolvedSourceSound(raw), "unimplemented real contact callback remains visible to presentation consumer");
    }

    private static void Package(FullKernelInputs real, string hud, string fixtureRoot, string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "configuration.json"), Config, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), Bootstrap, new UTF8Encoding(false));
        SourceInterfacePackagePlan m1 = SourceInterfacePackagePlan.CreateMovement157(hud, "interface.json", SourceInterfacePackagePlan.HudSha256,
            Path.Combine(fixtureRoot, "playbook"), "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            output, "configuration.json", Hash(Encoding.UTF8.GetBytes(Config)), fixtureRoot, "movement-runtime.json",
            SourceInterfaceMovementData.ContentSha256, output, "bootstrap.json", Hash(Encoding.UTF8.GetBytes(Bootstrap)));
        SourceInterfacePackagePlan sound = SourceInterfacePackagePlan.WithSourceSoundVm(m1,
            Path.Combine(fixtureRoot, "audio-functions"), "sound-functions.safn", SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        SourceInterfacePackagePlan current = SourceInterfacePackagePlan.WithContact272(sound);
        Equal(current.SelectedServicesResourceKey, SourceInterfacePackagePlan.ContactServicesResourceKey, "actual C1 plan selects only new services key");
        Check(current.Profile.IsContact272 && current.HasSoundVm && current.Profile.UsesMovementData, "real C1 plan requires movement plus SAFN");
        Equal(m1.SelectedServicesResourceKey, SourceInterfacePackagePlan.MovementServicesResourceKey, "old M1 plan not mutated");
        Equal(sound.SelectedServicesResourceKey, SourceInterfacePackagePlan.SoundVmServicesResourceKey, "old sound plan not mutated");
        Equal(Hash(current.CopyManifest()), Hash(sound.CopyManifest()), "actual HUD bytes unchanged");
        Equal(Hash(current.CopyConfiguration()), Hash(sound.CopyConfiguration()), "actual selected teams/jersey/HUD/head/pup config unchanged");
        Equal(Hash(current.CopyPlaybook()), Hash(sound.CopyPlaybook()), "actual P1 bytes unchanged");
        Equal(Hash(current.CopyMovement()), Hash(sound.CopyMovement()), "actual M1 tables unchanged");
        Equal(Hash(current.CopyBootstrap()), Hash(sound.CopyBootstrap()), "explicit movement/source-DIP bootstrap unchanged");
        Equal(current.SoundVmData.PayloadSha256, real.SoundFunctions.PayloadSha256, "plan/factory share exact SAFN content");
        Equal(current.Configuration.Presentation.PupBigBall, -7, "whole signed original pup value retained");
        Check(current.Configuration.Presentation.StationHeads.SequenceEqual(new[] { -1, 1, 18, 19 }), "actual station head selections retained");
        Equal(current.MovementBootstrap.HorizontalResolution, 512f, "source-DIP horizontal raster retained");
        Equal(current.MovementBootstrap.VerticalResolution, 256f, "source-DIP vertical raster retained");
        byte[] copy = current.CopySoundVmFunctions(); copy[0] ^= 1;
        Equal(Hash(current.CopySoundVmFunctions()), SourceInterfacePackagePlan.SoundVmFunctionsSha256, "mandatory SAFN bytes cannot be changed by a caller");
        Reject<SourceInterfaceException>(() => SourceInterfacePackagePlan.WithContact272(current), "cannot rebind current profile as old sound input");
        Check(!Directory.Exists(Path.Combine(output, "Assets")), "pure preflight never manufactures fake native resources");
    }

    private static string Hash(byte[] bytes)
    { using (SHA256 algorithm = SHA256.Create()) return string.Concat(algorithm.ComputeHash(bytes).Select(b => b.ToString("x2"))); }
    private static void Near(float actual, float expected, string label)
    { Check(Math.Abs(actual - expected) < .00002f, label); }
    private static void Equal<T>(T actual, T expected, string label)
    { Check(EqualityComparer<T>.Default.Equals(actual, expected), label); }
    private static void Check(bool condition, string label)
    { assertions++; if (!condition) throw new InvalidOperationException(label); }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Check(rejected, label + " must reject");
    }
}
