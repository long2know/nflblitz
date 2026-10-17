using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Editor;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static class Program
{
    private static int assertions;
    private const string Bootstrap = "{\"schema\":\"original-source-movement-bootstrap/v1\",\"field_index\":6," +
        "\"infinite_turbo_team_mask\":2,\"speed_powerup\":true,\"fast_turbo_team_mask\":1," +
        "\"run_out_of_bounds_team_mask\":3,\"violence_powerup\":false,\"source_hres\":512,\"source_vres\":256}";
    private const string Configuration = "{\"schema\":\"original-source-match-package/v1\",\"profile\":1,\"station_layout\":4," +
        "\"home_team\":{\"team\":0,\"records\":[0,1,2,3,4,5,6]},\"away_team\":{\"team\":1,\"records\":[7,8,9,10,11,12,13]}," +
        "\"stations\":[{\"id\":0,\"team\":0,\"initial_player_id\":3,\"profile\":{\"team\":0,\"record\":3}}]," +
        "\"first_possession\":1,\"captain\":1,\"time_adjustment\":4,\"difficulty\":7,\"continue_after_cabinet_gate\":false," +
        "\"home_play\":null,\"away_play\":null,\"presentation\":{\"designated_home_side\":1,\"station_heads\":[-1,1,18,19],\"pup_bigball\":-7}," +
        "\"native_gamepads_enabled\":false}";

    private static int Main(string[] args)
    {
        try
        {
            Clock();
            Profiles();
            BootstrapSettings();
            if (args.Length > 0 && args[0] != "-") Oracle(args[0]);
            if (args.Length > 1 && args[1] != "-") Bundle(args[1]);
            if (args.Length > 2) Package(args[2], args[3], args[4], args[5]);
            Console.WriteLine("PASS M1 source-camera/157 packaging: " + assertions + " actual production assertions; no native/game claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static SourceCinematicSeed Seed(float x = -120, float y = 100, float z = -30)
    {
        return new SourceCinematicSeed(new SourceVector3(x, y, z), new SourceVector3(12, 15, 9),
            160, (float)(Math.PI / 2), (float)(-25 * Math.PI / 180));
    }

    private static void Clock()
    {
        var clock = new SourceCameraFrameClock();
        Reject(() => clock.RequireAtTick(0, false), "initial camera seed is not an executed frame");
        SourceCinematicTransform frame = SourceCinematicTransform.FromState(Seed());
        clock.Publish(4, frame);
        Check(ReferenceEquals(clock.RequireAtTick(4, false), frame), "published pure frame is cached, not reconstructed from render");
        Reject(() => clock.RequireAtTick(3, false), "stale source frame");
        Reject(() => clock.RequireAtTick(5, false), "future source frame");
        Reject(() => clock.RequireAtTick(4, true), "menu suspended camera frame");
        Reject(() => clock.RequireAtTick(-1, false), "negative executing tick");
        Reject(() => clock.Publish(4, frame), "duplicate source wake");
        Reject(() => clock.Publish(2, frame), "rewound source wake without lifecycle reset");
        Reject(() => clock.Publish(5, null), "missing pure frame");
        Check(frame.Transform.Count == 12, "all original matrix12 floats retained");
        foreach (int index in new[] { 3, 7, 11 }) Equal(frame.Transform[index], 0f, "source transform zero affine column, no Unity translation");
        Equal(frame.Position.X, -120f, "position stays original source units");
        double forwardLength = Math.Sqrt(132d * 132 + 85d * 85 + 39d * 39);
        Near(frame.Transform[8], 132d / forwardLength, .0000001, "source forward X");
        Near(frame.Transform[9], -85d / forwardLength, .0000001, "source forward Y");
        Near(frame.Transform[10], 39d / forwardLength, .0000001, "source forward Z");
        Near(frame.Transform[0], 39d / Math.Sqrt(132d * 132 + 39d * 39), .0000001, "source above-cross-forward right X");
        Near(frame.Transform[2], -132d / Math.Sqrt(132d * 132 + 39d * 39), .0000001, "source right Z orientation");
        float saved = frame.Transform[0];
        Reject(() => ((IList<float>)frame.Transform)[0] = 7f, "matrix collection is immutable");
        Equal(frame.Transform[0], saved, "failed mutation cannot alter frame");
        clock.Publish(20, SourceCinematicTransform.FromState(Seed(-80)));
        Equal(clock.RequireAtTick(20, false).Position.X, -80f, "actual next post-suspension frame, not extrapolated ticks");
        clock.Reset();
        Reject(() => clock.RequireAtTick(20, false), "restart invalidates cached generation");
        clock.Publish(0, frame);
        Check(ReferenceEquals(clock.RequireAtTick(0, false), frame), "restart can begin an actual new source epoch");
        Equal(SourceCameraFrameClock.RequireOriginalRasterAspect(512, 384), 512f / 384f, "original high DIP raster");
        Equal(SourceCameraFrameClock.RequireOriginalRasterAspect(512, 256), 2f, "original low DIP raster");
        foreach (float[] raster in new[] { new[] { 1920f, 1080f }, new[] { 1920f, 384f }, new[] { 512f, 512f }, new[] { 0f, 384f },
            new[] { float.NaN, 384f }, new[] { 512f, float.PositiveInfinity } })
            Reject(() => SourceCameraFrameClock.RequireOriginalRasterAspect(raster[0], raster[1]), "not original DIP source dimensions");
    }

    private static void Profiles()
    {
        SourceTeamResourceProfile b1 = SourceTeamResourceProfile.B1Final117, m1 = SourceTeamResourceProfile.Movement157;
        Equal(b1.StableId, "original-teams", "B1 template identity preserved");
        Equal(b1.AnimationResourceKey, "BlitzSourcePlayers/original-teams/animation-library", "B1 resource key unchanged");
        Equal(m1.StableId, "original-teams-movement157", "NEW M1 prefab identity");
        Equal(m1.AnimationResourceKey, "SourceData/local-plyr266-movement157-f0ae9395", "NEW157 exact resource key");
        Equal(m1.AnimationSha256, "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e", "NEW157 exact immutable manifest pin");
        Equal(m1.TeamResourceKey, "BlitzSourcePlayers/original-teams-movement157/team-presentation", "NEW157 team data key");
        Equal(m1.ImpactResourceKey, "BlitzSourcePlayers/original-teams-movement157/impact-library", "NEW157 cinematic key");
        Equal(m1.AnimationAssetPath, "Assets/Imported/original-teams-movement157/Resources/SourceData/local-plyr266-movement157-f0ae9395.asset",
            "actual157 AssetDatabase path agrees with provider resource key");
        Check(m1.AnimationResourceKey != b1.AnimationResourceKey && m1.TeamResourceKey != b1.TeamResourceKey, "no old117 alias");
        m1.RequireInventory(SourceTeamResourceProfile.MovementAnimationSha256, 157, 13490); assertions++;
        b1.RequireInventory(SourceInterfacePackagePlan.AnimationSha256, 117, 10288); assertions++;
        Reject(() => m1.RequireInventory(SourceInterfacePackagePlan.AnimationSha256, 117, 10288), "old117 cannot become157");
        Reject(() => b1.RequireInventory(SourceTeamResourceProfile.MovementAnimationSha256, 157, 13490), "new157 cannot become117");
        Reject(() => m1.RequireInventory(SourceTeamResourceProfile.MovementAnimationSha256, 156, 13420), "partial156 not157");
        Reject(() => SourceTeamResourceProfile.FromStableId("original-teams-movement156"), "unknown old resource not guessed");
        SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Imported/new/Resources/" +
            SourceInterfacePackagePlan.MovementServicesResourceKey + ".asset", SourceInterfacePackagePlan.MovementServicesResourceKey);
        assertions++;
        Reject(() => SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Resources/" +
            SourceInterfacePackagePlan.MovementServicesResourceKey + ".asset", SourceInterfacePackagePlan.MovementServicesResourceKey), "no tracked original data output");
    }

    private static SourceInterfaceMovementBootstrap ReadBootstrap(string json)
    { byte[] bytes = Encoding.UTF8.GetBytes(json); return SourceInterfaceMovementBootstrap.Read(bytes, Hash(bytes)); }

    private static void BootstrapSettings()
    {
        SourceInterfaceMovementBootstrap bootstrap = ReadBootstrap(Bootstrap);
        Equal(bootstrap.HorizontalResolution, 512f, "explicit source hres");
        Equal(bootstrap.VerticalResolution, 256f, "explicit source vres not browser aspect");
        Equal(bootstrap.InfiniteTurboTeamMask, 2, "executing-side mask2 retained");
        Equal(bootstrap.FastTurboTeamMask, 1, "fast turbo mask1 retained");
        Equal(bootstrap.RunOutOfBoundsTeamMask, 3, "out of bounds mask3 retained");
        Check(bootstrap.SpeedPowerup && !bootstrap.ViolencePowerup, "global speed versus violence flags retained");
        var settings = new SourceMovementSettings(6, 2, true, 1, 3, false);
        SourceTeamInterfaceChecks.MovementEquals(bootstrap, bootstrap, settings); assertions++;
        Reject(() => SourceTeamInterfaceChecks.MovementEquals(bootstrap, bootstrap,
            new SourceMovementSettings(6, 1, true, 1, 3, false)), "team mask must not become bool");
        foreach (string invalid in new[] { Bootstrap.Replace("\"source_hres\":512", "\"source_hres\":1920"),
            Bootstrap.Replace("\"source_vres\":256", "\"source_vres\":1080"),
            Bootstrap.Replace("\"source_vres\":256", "\"source_vres\":256,\"extra\":0"),
            Bootstrap.Replace("\"speed_powerup\":true", "\"speed_powerup\":1"),
            Bootstrap.Replace("\"source_hres\":512,", ""), Bootstrap + " {}" })
            Reject(() => ReadBootstrap(invalid), "strict mandatory original settings/DIP");
    }

    private static void Oracle(string path)
    {
        int frames = 0;
        var clock = new SourceCameraFrameClock();
        foreach (string row in File.ReadLines(path))
        {
            string[] fields = row.Split('\t');
            float[] v = fields.Skip(3).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            var state = new SourceCinematicSeed(new SourceVector3(v[0], v[1], v[2]), new SourceVector3(v[3], v[4], v[5]),
                v[6], v[7], v[8], new SourceVector3(v[9], v[10], v[11]), v[12], v[13], v[14]);
            SourceCinematicTransform actual = SourceCinematicTransform.FromState(state);
            clock.Publish(frames, actual);
            SourceCinematicTransform cached = clock.RequireAtTick(frames, false);
            for (int index = 0; index < 12; index++)
                Equal(BitConverter.SingleToInt32Bits(cached.Transform[index]), BitConverter.SingleToInt32Bits(v[15 + index]),
                    "unchanged original-C cambot.xform binary32 frame" + frames + " element" + index);
            Equal(cached.Position.X, v[0], "original-C source camera X"); Equal(cached.Position.Y, v[1], "original-C source camera Y");
            Equal(cached.Position.Z, v[2], "original-C source camera Z"); frames++;
        }
        Check(frames > 15000, "full original7-table/25-entry camera campaign, not a chosen angle");
        Console.WriteLine("ORIGINAL C: " + frames + " source camera frames / ALL12 matrix floats binary32 exact");
    }

    private static void Bundle(string root)
    {
        byte[] manifest = File.ReadAllBytes(Path.Combine(root, "export-inventory.json"));
        Equal(Hash(manifest), SourceTeamResourceProfile.MovementAnimationSha256, "actual completed157 bundle manifest pin");
        SourceAnimationLibrary library = SourceAnimationLibrary.Read(manifest, path => File.ReadAllBytes(Path.Combine(root, path)));
        SourceTeamResourceProfile.Movement157.RequireInventory(Hash(manifest), library.SequenceNames.Count, library.FrameCount); assertions++;
        Equal(library.GetClip("R_SKIP").SelectedSourceFrames.Count, 70, "actual selected uncompressed R_SKIP converter replay present");
    }

    private static void Package(string hud, string book, string movement, string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "configuration.json"), Configuration, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), Bootstrap, new UTF8Encoding(false));
        Func<SourceInterfacePackagePlan> create = () => SourceInterfacePackagePlan.CreateMovement157(hud, "interface.json",
            SourceInterfacePackagePlan.HudSha256, book, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            output, "configuration.json", Hash(Encoding.UTF8.GetBytes(Configuration)),
            Path.GetDirectoryName(movement), Path.GetFileName(movement), SourceInterfaceMovementData.ContentSha256,
            output, "bootstrap.json", Hash(Encoding.UTF8.GetBytes(Bootstrap)));
        SourceInterfacePackagePlan plan = create();
        Equal(plan.Profile.StableId, SourceTeamResourceProfile.MovementStableId, "actual M1 package profile");
        Equal(plan.SelectedServicesResourceKey, "BlitzSourceInterface/original-movement157-match-services", "actual M1 services key");
        Equal(plan.Textures.Count, 3, "actual complete original3atlas package");
        Equal(plan.Playbook.Plays.Count, 88, "actual P1 play inventory");
        Equal(Hash(plan.CopyMovement()), SourceInterfaceMovementData.ContentSha256, "actual runtime movement bytes pinned");
        Equal(plan.MovementBootstrap.VerticalResolution, 256f, "original DIP copied, not rendered viewport");
        Equal(plan.Configuration.Presentation.PupBigBall, -7, "actual entire presentation pup int retained");
        byte[] copied = plan.CopyBootstrap(); copied[0] ^= 1;
        Equal(Hash(plan.CopyBootstrap()), Hash(Encoding.UTF8.GetBytes(Bootstrap)), "bootstrap bytes detached from consumers");
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), Bootstrap.Replace("\"source_vres\":256", "\"source_vres\":384"), new UTF8Encoding(false));
        Reject(() => create(), "real bootstrap content mutation fails expected explicit pin");
        Check(!Directory.Exists(Path.Combine(output, "Assets")), "pure preflight never fabricates imported resources");
    }

    private static string Hash(byte[] data)
    { using (SHA256 sha = SHA256.Create()) return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2"))); }
    private static void Equal<T>(T actual, T expected, string label)
    { Check(EqualityComparer<T>.Default.Equals(actual, expected), label); }
    private static void Near(double actual, double expected, double tolerance, string label)
    { Check(Math.Abs(actual - expected) <= tolerance, label); }
    private static void Check(bool value, string label)
    { assertions++; if (!value) throw new InvalidOperationException("FAIL movement-presentation: " + label); }
    private static void Reject(Action action, string label)
    { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, label + " must reject"); }
}
