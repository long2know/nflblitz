using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;

internal static class Program
{
    private static long assertions;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1) throw new InvalidOperationException("Supply the generated original synthetic roster fixture.");
            CheckRoster(ReadRoster(args[0]), false);
            CheckRawPresentation(ReadRoster(args[0]));
            CheckSyntheticNumbers();
            CheckSyntheticCinematics();
            if (args.Length > 1 && args[1] != "-") CheckActual(args[1]);
            if (args.Length > 2) CheckCinematics(args[2], args.Length > 3 ? args[3] : null);
            Console.WriteLine("PASS: source presentation / " + assertions + " assertions / exact production helpers");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SourceTeamRosterLibrary ReadRoster(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return SourceTeamRosterLibrary.Read(bytes, Hash(bytes));
    }

    private static PlayerView Actor(SourceRosterPlayer player, int side, int slot, int station = -1)
    {
        return new PlayerView(side * 7 + slot, (TeamId)side, new FieldVector(35, 4), new FieldVector(8, -2), 1,
            modelScale: player.Profile.ModelScale,
            source: new SourceActorView(station, player.Profile.Identity));
    }

    private static void CheckRoster(SourceTeamRosterLibrary library, bool actual)
    {
        for (int side0 = 0; side0 < 31; side0++)
        for (int side1 = 0; side1 < 31; side1++)
        for (int home = 0; home < 2; home++)
        for (int side = 0; side < 2; side++)
        {
            bool clash = library.AwayJersey[Math.Min(side0, side1)][Math.Max(side0, side1)] != 0;
            SourceUniformVariant expected = clash && side != home ? SourceUniformVariant.Away : SourceUniformVariant.Home;
            SourceRosterPlayer player = library.GetTeamData(side == 0 ? side0 : side1).Players[side0 % 16];
            SourceTeamAppearance appearance = SourceTeamPresentationModel.Select(library, side0, side1, home,
                Actor(player, side, 3), -1, new SourceTeamDrawState(false, true, false, false, false));
            Equal(appearance.Uniform.Variant, expected, "active min/max clash matrix, not fixed physical away");
            Equal(appearance.Player.Profile.Identity.TeamIndex, side == 0 ? side0 : side1, "actual opposing roster team");
            Equal(appearance.UniformIdentity, library.GetTeamData(side == 0 ? side0 : side1).UniformPrefix +
                (expected == SourceUniformVariant.Home ? "H" : "A") + "LOD.WMS", "original uniform atlas identity");
            Equal(appearance.Uniform.Skin, player.Skin, "actual roster skin selector");
            Equal(appearance.DecalIndex, side * 2 + (player.Skin == SourceSkinSelector.White ? 0 : 1), "source decal offset");
            Equal(appearance.ModelScale, player.Profile.ModelScale, "actual original roster scale");
        }
        foreach (SourceRosterTeam team in library.TeamData)
        foreach (SourceRosterPlayer player in team.Players)
        for (int bits = 0; bits < 32; bits++)
        {
            bool high = (bits & 2) != 0, jumbo = (bits & 16) != 0, flip = (bits & 4) != 0;
            bool quarterback = (bits & 8) != 0, phantom = (bits & 1) != 0;
            PlayerView actor = Actor(player, 0, 3, 0);
            SourceTeamAppearance appearance = SourceTeamPresentationModel.Select(library, team.Roster.TeamIndex,
                (team.Roster.TeamIndex + 1) % 31, 0, actor, actor.Id,
                new SourceTeamDrawState(high, quarterback, flip, phantom, jumbo));
            int held = phantom ? 0 : quarterback && flip ? 4 : 8;
            int expected = (int)player.BodySize | (high ? 2 : 0) | held | (jumbo ? 16 : 0);
            Equal(appearance.Model.Index, expected, "source body/high-count/held/flip/jumbo model selector");
            int jersey = (int)player.BodySize | (high ? 2 : 0) | ((player.Profile.PackedJerseyNumber >> 4) != 0 ? 4 : 0);
            Equal(appearance.Numbers.Index, jersey, "actual one/two-digit number geometry selector");
            Equal(appearance.DigitIndices[0], Digit(player.Profile.PackedJerseyNumber >> 4), "BCD tens atlas index");
            Equal(appearance.DigitIndices[1], Digit(player.Profile.PackedJerseyNumber & 15), "BCD ones atlas index");
            Equal(appearance.SourcePlayerId, player.Profile.SourcePlayerId, "stable original roster binding");
        }
        SourceRosterPlayer one = library.GetTeamData(0).Players[3];
        PlayerView actor0 = Actor(one, 0, 3, 0);
        for (int head = 0; head < 20; head++)
        {
            SourceTeamAppearance appearance = SourceTeamPresentationModel.Select(library, 0, 1, 0, actor0, 3,
                new SourceTeamDrawState(false, true, false, false, false, head, true));
            Equal(appearance.CustomHead.Index, head, "actual human-station custom head; priority over popped helmet");
        }
        Reject(() => SourceTeamPresentationModel.Select(library, 0, 1, 0, actor0, 3, default(SourceTeamDrawState)),
            "draw flags", "no implicit all-zero source draw state");
        Reject(() => SourceTeamPresentationModel.Select(library, 1, 0, 0, actor0, 3,
            new SourceTeamDrawState(false, true, false, false, false)), "roster team", "no another-team static record");
        Reject(() => SourceTeamPresentationModel.Select(library, 0, 1, 0, Actor(one, 0, 3), 3,
            new SourceTeamDrawState(false, true, false, false, false, 0)), "human station", "custom head is not a drone roster field");
        Reject(() => SourceTeamPresentationModel.Select(library, 0, 1, 0, actor0, 3,
            new SourceTeamDrawState(false, true, false, false, false, -1, true)), "popped-head", "no fake popped helmet");
        Check(actual || library.TeamData.Count == 31, "fixture inventory");
    }

    private static int Digit(int value) { return value == 0 ? 9 : value - 1; }

    private static void CheckRawPresentation(SourceTeamRosterLibrary library)
    {
        SourceRosterPlayer player = library.GetTeamData(0).Players[3];
        var settings = new SourcePresentationSettings(TeamId.Away, new[] { -1, 17, -1, -1 }, -7);
        var actor = new PlayerView(3, TeamId.Home, new FieldVector(47, 2), new FieldVector(999, -999), 1,
            role: ActorRole.Lineman, source: new SourceActorView(1, player.Profile.Identity,
                presentation: new SourceActorPresentationView(0x00800005, 5)));
        SourceTeamDrawState draw = SourceTeamPresentationModel.DrawState(settings, actor, new SourceBallPresentationView(0x800));
        Check(draw.HighCount && draw.Flip && draw.QuarterbackMode && draw.PhantomBall && draw.JumboBall &&
            draw.HelmetPopped && draw.CustomHeadIndex == 17, "actual signed/raw source flags, MODE_QB, station head and pup int truth; not ActorRole/BallMode");
        Equal(settings.PupBigBall, -7, "raw pup_bigball int is not normalized in the shared DTO");
        Reject(() => SourceTeamPresentationModel.DrawState(null, actor, new SourceBallPresentationView(0)),
            "unavailable", "missing source settings do not become physical home/empty heads");
        Reject(() => SourceTeamPresentationModel.DrawState(settings, actor, default(SourceBallPresentationView)),
            "raw actor", "unknown ball flags do not become nonphantom");
        for (int heading = 0; heading < 1024; heading++)
        {
            double radians = heading * Math.PI / 512d;
            var current = new PlayerView(3, TeamId.Home, new FieldVector(47, 2), new FieldVector(999, -999), 1,
                height: 3, facingDirection: new FieldVector((float)Math.Sin(radians), (float)Math.Cos(radians)),
                source: new SourceActorView(1, player.Profile.Identity));
            SourceCinematicTarget target = SourceCinematicTarget.FromActor(current);
            Equal(target.Facing, heading, "exact1024 published source heading recovered without velocity or render interpolation");
            Equal(target.Position.X, -3f * SourceTiming.SourceUnitsPerYard, "source target centered X units");
            Equal(target.Position.Y, 3f * SourceTiming.SourceUnitsPerYard, "source target object Y, not pose/pelvis offset");
        }
        var nonGrid = new PlayerView(3, TeamId.Home, default(FieldVector), default(FieldVector), 1,
            facingDirection: new FieldVector((float)Math.Sin(.123), (float)Math.Cos(.123)),
            source: new SourceActorView(0, player.Profile.Identity));
        Reject(() => SourceCinematicTarget.FromActor(nonGrid), "1024-heading grid", "render-interpolated camera target facing rejected");
        var clock = new SourceCinematicProcessClock();
        Check(clock.BeginsNewPlay(1), "source camera starts was_playover1");
        clock.CompleteCameraTick(1); Equal(clock.WasPlayOver, 0, "source first camera wake decrements latch");
        clock.CompleteCameraTick(3); Equal(clock.WasPlayOver, 2, "actual GM_PLAY_OVER sets two camera wakes");
        for (int sourceMenuTick = 0; sourceMenuTick < 100; sourceMenuTick++)
            Equal(clock.WasPlayOver, 2, "suspended menu source ticks do not advance camera latch");
        Check(clock.BeginsNewPlay(0), "source GM_LINING_UP after play over reverts transient");
        clock.CompleteCameraTick(0); Check(clock.BeginsNewPlay(1), "source second pre-snap camera wake retains latch");
        clock.CompleteCameraTick(1); Check(!clock.BeginsNewPlay(1), "source latch expires after two executing camera wakes");
    }

    private static void CheckSyntheticNumbers()
    {
        const string source = "VERTEX v[]={{0,0,0},{2,0,0},{0,2,0},{2,2,0}};" +
            "ST st[]={{0,.75},{.098,.75},{0,.8125},{.098,.8125}};" +
            "TRI tri[]={{0,3,6,0,1,2,0},{9,6,3,3,255,255,1},{0,0,0,0,0,0,-20}};" +
            "LIMB limb_number={4,2,NULL,v,st,tri};";
        for (int tens = 0; tens < 10; tens++)
        for (int ones = 0; ones < 10; ones++)
        {
            BindPoseLimbMesh mesh = SourceTeamGeneratedModels.BuildNumbers(source, "limb_number", tens * 16 + ones,
                new AssetVector3(0, 1, 0));
            Equal(mesh.Positions.Count, 6, "number UV seams retain both digit selectors");
            Near(mesh.Uvs[0].X, Digit(tens) * .1f, 1e-7f, "packed BCD tens, not decimal integer");
            Near(mesh.Uvs[3].X, .098f + Digit(ones) * .1f, 1e-7f, "packed BCD ones, not duplicated tens");
            Near(mesh.Uvs[0].Y, .25f, 1e-7f, "number normalized atlas V origin");
            Equal(mesh.Positions[0].Y, -1f, "number mesh anchored to original torso pivot");
            Equal(mesh.SubmeshTriangles[0].Count, 6, "counted original number faces preserved");
        }
        Reject(() => SourceTeamGeneratedModels.BuildNumbers(source, "limb_number", 0xFA, new AssetVector3()), "BCD", "invalid packed digit");
        Reject(() => SourceTeamGeneratedModels.BuildNumbers(source.Replace("255,255,1", "255,255,2"),
            "limb_number", 0x54, new AssetVector3()), "selector", "number face cannot become body material slot");
        Reject(() => SourceTeamGeneratedModels.BuildNumbers(source.Replace("0,1,2,0", "255,1,2,0"),
            "limb_number", 0x54, new AssetVector3()), "uninitialized", "number UV reuse needs initialized vertex");
    }

    private static void CheckActual(string root)
    {
        SourceTeamRosterLibrary library = ReadRoster(Path.Combine(root, "rosters.json"));
        CheckRoster(library, true);
        byte[] json = File.ReadAllBytes(Path.Combine(root, "team-presentation.json"));
        SourceTeamPresentationData catalog = SourceTeamPresentationData.Read(json, Hash(json), library);
        Equal(catalog.Textures.Count, 86, "ALL31 actual H/A + skin +20 head texture identities");
        Equal(catalog.MissingTextureIds.Count, 0, "missing atlases resolved with actual original c_hack, not aliases");
        foreach (SourceTeamTexture texture in catalog.Textures)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, texture.Path));
            catalog.VerifyTexture(texture, bytes);
            byte[] bad = (byte[])bytes.Clone(); bad[bad.Length / 2] ^= 1;
            Reject(() => catalog.VerifyTexture(texture, bad), "content mismatch", "real emitted texture mutant: " + texture.Id);
            if (texture.Id.Contains("BRW") || texture.Id.Contains("TIT"))
                Equal(texture.Representation, "original-c-hack-authoring-atlas", "actual original packer proof, not Oilers atlas alias");
        }
        var headerCache = new Dictionary<string, string>(StringComparer.Ordinal);
        Func<string, string> read = path =>
        {
            string text;
            if (!headerCache.TryGetValue(path, out text))
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
                catalog.VerifyInput(path, bytes);
                text = AuthorizedAssetRoot.DecodeUtf8(bytes, path);
                headerCache.Add(path, text);
            }
            return text;
        };
        SourcePlayer basePlayer = null;
        int vertices = 0, indices = 0;
        for (int modelIndex = 0; modelIndex < 32; modelIndex++)
        {
            SourcePlayer player = SourceTeamGeneratedModels.ReadModel(library, modelIndex, read);
            if (modelIndex == 0) basePlayer = player;
            var meshes = SourceTeamGeneratedModels.BuildModel(player);
            Equal(player.Limbs.Count, 15, "actual model has fifteen original joint-ordered limbs");
            for (int j = 0; j < 15; j++)
            {
                Equal(player.Limbs[j].Symbol, library.ActorModels.Models[modelIndex].LimbSymbols[j],
                    "actual source selected limb, including held/jumbo hand geometry");
                Equal(meshes[j].SubmeshTriangles.Sum(group => group.Count), player.Limbs[j].Faces.Count * 3,
                    "original model indices preserved");
                vertices += meshes[j].Positions.Count;
                indices += player.Limbs[j].Faces.Count * 3;
            }
        }
        for (int head = 0; head < 20; head++)
        {
            SourcePlayer player = SourceTeamGeneratedModels.ReadModel(library, 0, read, head);
            Equal(player.Limbs[8].Symbol, library.Secrets.Heads[head].LimbSymbol, "actual selected custom-head limb");
            var meshes = SourceTeamGeneratedModels.BuildModel(player);
            Equal(meshes[8].SubmeshTriangles.Sum(group => group.Count), player.Limbs[8].Faces.Count * 3,
                "custom-head actual topology preserved");
            Check(meshes[8].SubmeshTriangles[7].Count > 0, "source custom head uses actual slot7");
        }
        string jerseys = read("models/jerseys.h");
        foreach (SourceRosterJersey jersey in library.ActorModels.Jerseys)
        for (int tens = 0; tens < 10; tens++)
        for (int ones = 0; ones < 10; ones++)
        {
            var mesh = SourceTeamGeneratedModels.BuildNumbers(jerseys, jersey.LimbSymbol, tens * 16 + ones, basePlayer.Nodes[1].BindPivot);
            Check(mesh.Positions.Count >= 12 && mesh.SubmeshTriangles[0].Count >= 24, "actual original number surface geometry");
            foreach (AssetVector2 uv in mesh.Uvs)
                Check(uv.X >= 0f && uv.X <= 1f && uv.Y >= 0f && uv.Y <= 1f, "original digits stay inside authored atlas");
        }
        Console.WriteLine("ACTUAL original team models: 32 selectors /20 heads / " + vertices + " split vertices / " + indices + " indices");
    }

    private static SourceCinematicSeed CameraSeed()
    {
        return new SourceCinematicSeed(new SourceVector3(-120, 100, -30), new SourceVector3(12, 15, 9), 160,
            (float)(Math.PI / 2d), (float)(-25d * Math.PI / 180d), new SourceVector3(.35f, -.1f, .22f),
            0, .0004f, -.0003f);
    }

    private static void CheckSyntheticCinematics()
    {
        SourceVector3[] rail = { new SourceVector3(1, 1, 1), new SourceVector3(2, 2, 1),
            new SourceVector3(3, 1, 1), new SourceVector3(4, 2, 1) };
        SourceVector3[] control = { new SourceVector3(.2f, .4f, .3f), new SourceVector3(.6f, .3f, .8f) };
        for (int mode = 4; mode <= 12; mode++)
        {
            var entry = new SourceCinematicEntry("synthetic", 0, 1, mode, "SYNTHETIC", 5, 3, 20, -30, 100,
                mode <= 6 ? new SourceVector3[0] : mode <= 9 ? control : rail);
            var model = new SourceCinematicModel(CameraSeed());
            Check(model.Select(entry), "explicit source special selection");
            SourceCinematicFrame frame = default(SourceCinematicFrame);
            for (int tick = 0; tick < 5; tick++)
            {
                frame = model.Step(new SourceCinematicTarget(new SourceVector3(20 + tick, 2, -8), 700));
                Check(frame.Active && frame.ElapsedTicks == tick + 1, "special camera advances once per source tick");
            }
            SourceVector3 old = frame.State.Eye;
            Check(!model.Select(new SourceCinematicEntry("none", 0, 1, 23, "NMODE_NOCSI", 0, 0, 0, 0, 0,
                new SourceVector3[0], true)), "source NMODE_NOCSI is not a generic impact shot");
            Near(model.State.Eye.X, old.X, 0, "no-selection preserves existing camera");
            frame = model.Step(new SourceCinematicTarget(new SourceVector3(30, 2, -8), 700));
            Check(!frame.Active && frame.ReturnedToNormal && !frame.SnapOnReturn, "exact odd-travel source expiry returns to normal");
            Equal(frame.ElapsedTicks, mode <= 6 ? 6 : 5,
                "original zoom increments after expiry; spline/rail return without another active special tick");
        }
        var snap = new SourceCinematicEntry("snap", 0, 1, 5, "CAM_ZO_NR", 357, 0, 135, -5, 70, new SourceVector3[0]);
        var camera = new SourceCinematicModel(CameraSeed());
        camera.Select(snap);
        SourceCinematicFrame snapped = camera.Step(new SourceCinematicTarget(new SourceVector3(20, 0, -8), 0));
        Near(snapped.State.Focus.Y, 13f, 1e-6f, "original zoom target is five feet, not normal camera six feet");
        Near(snapped.State.Distance, 70f, 0f, "source snap uses exact CSI distance, not generic zoom percentage");
        SourceCinematicFrame returned = camera.Step(new SourceCinematicTarget(new SourceVector3(20, 0, -8), 0), true);
        Check(returned.ReturnedToNormal && returned.SnapOnReturn, "source new-play interruption reverts snap mode immediately");
        Reject(() => camera.Select(new SourceCinematicEntry("bad", 0, 1, 10, "CAM_RAIL", 5, 3, 0, -20, 1,
            new SourceVector3[0])), "control-point", "rail source controls cannot be replaced by generic camera shake");
        int[][] joints = { new[] { 8, 0, 13, 10 }, new[] { 14, 11 }, new[] { 13, 10 }, new[] { 13 }, new[] { 10 } };
        for (int argument = 0; argument < joints.Length; argument++)
            Check(SourceImpactDust.SourceJoints(argument).SequenceEqual(joints[argument]), "actual dust callback joint selection " + argument);
        for (int initial = 0; initial < 3; initial++)
        {
            var dust = new SourceImpactDust(new SourceVector3(10, 50, 20), 256, initial);
            Check(dust.Step(false), "actual dust starts on ground, not the supplied joint Y");
            Equal(dust.TextureIndex, 0, "source dust initializes texture0 independently from randrng3");
            Near(dust.Position.X, 10.35f, 1e-6f, "source dust horizontal velocity .35/source tick");
            Near(dust.Position.Y, .1f, 0, "source dust vertical .1/source tick");
            dust.Step(false);
            Equal(dust.TextureIndex, 0, "source dust j>sleepTime, not >=");
            dust.Step(false);
            Equal(dust.TextureIndex, initial, "source dust first selected frame after exact wait");
            Check(!dust.Step(true) && !dust.IsAlive, "source pre-snap destroys dust, no persistent generic particle");
        }
        Equal(SourceCinematicDepthRange.RequiredFarPlane(new SourceVector3(), new SourceVector3(-3, -4, -12),
            new SourceVector3(3, 4, 12)), 230f, "graybox minimum depth is retained");
        Near(SourceCinematicDepthRange.RequiredFarPlane(new SourceVector3(10, 20, 30), new SourceVector3(-3, -4, -12),
            new SourceVector3(3, 4, 12), 1), (float)Math.Sqrt(13 * 13 + 24 * 24 + 42 * 42) + 1, 0,
            "finite Unity depth adapter covers farthest actual world bound, not blind600/source parity claim");
    }

    private static void CheckCinematics(string path, string oraclePath)
    {
        byte[] bytes = File.ReadAllBytes(path);
        SourceCinematicLibrary library = SourceCinematicLibrary.Read(bytes, Hash(bytes));
        Equal(library.Tables.Count, 7, "actual active impact/endplay CSI table inventory");
        Equal(library.Tables.Sum(pair => pair.Value.Count), 25, "weighted duplicate CSI entries remain separate");
        Equal(library.AnimationCues.Count, 416, "actual script opcode/callback inventory");
        SourceCinematicCueDefinition eligible = library.RequireExecutedOpcode("r_diveoh_anim", 1803, "camera");
        Equal(eligible.Arguments[0], "csi_zm_divetack", "actual eligible original dive callback");
        Reject(() => library.RequireExecutedOpcode("qb_stance_anim", 1803, "camera"), "generic tackle",
            "same line under wrong script cannot start impact camera");
        Reject(() => library.Selected("csi_sn_diveattack", 7), "ordinal", "no invented modulo/RNG shot");
        Check(library.Selected("csi_sn_diveattack", 6).IsNoSelection, "original seventh dive-attack choice explicitly does nothing");
        var shaker = new SourceImpactShaker(library.ShakerExponent);
        for (int i = 0; i < 64; i++)
            Near(library.ShakerExponent[i], (float)Math.Pow(.5d, i / 8d), 4e-8f, "original exponential table, not linear/random camera shake");
        foreach (int total in new[] { 1, 3, 10, 11, 57 })
        {
            shaker.ExecuteOpcode(3, total, 3, 3);
            for (int i = 0; i < total; i++)
            {
                SourceVector3 offset = shaker.Step(i % 3, (i + 1) % 3);
                float exponent = library.ShakerExponent[64 * i / total];
                Near(offset.X, exponent * (3 * (i % 3 - 1)), 0, "actual source shaker X draw/order/decay");
                Near(offset.Z, exponent * (3 * ((i + 1) % 3 - 1)), 0, "actual source shaker Z independent draw/order/decay");
                Equal(shaker.ElapsedTicks, i + 1, "actual shaker source tick count");
            }
            Check(!shaker.NeedsSourceRandomDraws, "original shaker ends exactly at total ticks");
            Reject(() => shaker.Step(1, 1), "inactive", "inactive shaker does not consume source RNG");
        }
        shaker.ExecuteOpcode(3, 10, 3, 3); shaker.Step(0, 2); shaker.Step(1, 1);
        shaker.ExecuteOpcode(7, 20, 4, 3);
        Equal(shaker.Amplitude, 7, "original noncarrier opcode still writes global amplitude before guard");
        Equal(shaker.TotalTicks, 20, "original noncarrier opcode still writes global duration before guard");
        Equal(shaker.ElapsedTicks, 2, "original noncarrier opcode does not reset shaker count");
        shaker.Reset(); Check(!shaker.NeedsSourceRandomDraws, "source restart clears shake state");
        CheckPlayback(library);
        if (oraclePath == null) return;
        string[] tables = { "csi_fs_endplay", "csi_fs_endqtr", "csi_sn_diveattack", "csi_zm_divetack",
            "csi_rs_suplex", "csi_rs_suplex_nf", "csi_zm_endplayfg" };
        SourceCinematicModel model = null;
        double maximum = 0;
        int samples = 0;
        foreach (string line in File.ReadLines(oraclePath))
        {
            string[] fields = line.Split('\t');
            Equal(fields.Length, 30, "actual C oracle row shape including full source cambot transform");
            int table = int.Parse(fields[0], CultureInfo.InvariantCulture);
            int ordinal = int.Parse(fields[1], CultureInfo.InvariantCulture);
            int tick = int.Parse(fields[2], CultureInfo.InvariantCulture);
            if (tick == 0)
            {
                model = new SourceCinematicModel(CameraSeed());
                model.Select(library.Selected(tables[table], ordinal));
            }
            SourceCinematicFrame frame = model.Step(new SourceCinematicTarget(
                new SourceVector3(20f + tick * .075f, (tick % 31) * .05f, -8f + (tick % 57) * .025f), 700));
            SourceCinematicSeed s = frame.State;
            float[] actual = { s.Eye.X, s.Eye.Y, s.Eye.Z, s.Focus.X, s.Focus.Y, s.Focus.Z, s.Distance,
                s.Theta, s.Phi, s.Velocity.X, s.Velocity.Y, s.Velocity.Z, s.DistanceVelocity, s.ThetaVelocity, s.PhiVelocity };
            for (int i = 0; i < actual.Length; i++)
            {
                float expected = float.Parse(fields[i + 3], CultureInfo.InvariantCulture);
                double error = Math.Abs(actual[i] - expected); maximum = Math.Max(maximum, error);
                Check(error == 0d, "unchanged original C " + tables[table] + "[" + ordinal + "] tick" + tick +
                    " field" + i + " actual=" + actual[i].ToString("R", CultureInfo.InvariantCulture) +
                    " expected=" + expected.ToString("R", CultureInfo.InvariantCulture));
            }
            SourceCinematicTransform transform = SourceCinematicTransform.FromState(s);
            Equal(transform.Position.X, s.Eye.X, "full source camera position, not Unity render transform");
            for (int i = 0; i < 12; i++)
                Equal(transform.Transform[i], float.Parse(fields[i + 18], CultureInfo.InvariantCulture),
                    "unchanged original C cambot.xform[" + i + "] " + tables[table] + " tick" + tick);
            samples++;
        }
        Equal(samples, 15242, "actual original camera functional sample inventory");
        Console.WriteLine("ACTUAL original camera C oracle: " + samples + " source ticks / maximum absolute error=" +
            maximum.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void CheckPlayback(SourceCinematicLibrary library)
    {
        Equal(library.ContactCameraSites.Count, 5, "actual active detect.c endplay selector sites");
        var targets = new SourceCinematicTarget[14];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = new SourceCinematicTarget(new SourceVector3(20 + i, i * .1f, -8 + i), 700);
        var actual = new SourceCinematicPlayback(library, CameraSeed());
        var control = new SourceCinematicPlayback(library, CameraSeed());
        foreach (SourceCinematicPlayback playback in new[] { actual, control })
        {
            Check(playback.ExecuteCameraOpcode(100, 3, 3, "fall_04", 15969, 0), "actual reached fall_04 source camera selection");
            Equal(playback.TargetActorId, 3, "selected camera follows actual opcode actor");
            SourceCinematicFrame first = playback.CameraSourceTick(100, 4, targets, CameraSeed(), false);
            Check(first.Active && first.Table == "csi_rs_suplex" && first.Ordinal == 0, "original suplex table identity retained");
            Equal(playback.LastCameraTick, 100L, "camera source tick, not host/render tick");
            Equal(playback.SourceHeading1024, (int)(first.State.Theta * (1024f / (float)(2d * Math.PI))) & 1023,
                "cabinet input uses un-interpolated source camera heading with truncation, not render transform");
            playback.CameraSourceTick(101, 0, targets, CameraSeed(), false);
        }
        Reject(() => actual.CameraSourceTick(101, 1, targets, CameraSeed(), false), "at most once", "duplicate source camera tick");
        Reject(() => actual.CameraSourceTick(103, 0, targets, CameraSeed(), false), "intermediate", "no missed-tick velocity camera extrapolation");
        actual.ExecuteShakerOpcode(102, 0, 3, 3, "fall_04", 16000);
        Reject(() => actual.CameraSourceTick(102, 1, targets, CameraSeed(), false), "two actual", "active original shake cannot invent RNG");
        SourceCinematicFrame shaken = actual.CameraSourceTick(102, 1, targets, CameraSeed(), false, 0, 2);
        SourceCinematicFrame plain = control.CameraSourceTick(102, 0, targets, CameraSeed(), false);
        Near(shaken.State.Eye.X, plain.State.Eye.X - 2f, 0f, "source shaker moves eye X exactly with source offset");
        Near(shaken.State.Focus.X, plain.State.Focus.X - 2f, 0f, "source shaker moves focus with eye, not generic roll");
        Near(shaken.State.Eye.Z, plain.State.Eye.Z + 2f, 0f, "source shaker has independent Z draw");
        SourceImpactCallback observed = null;
        actual.SourceCallback += callback => observed = callback;
        actual.ExecuteCallback(103, 0, 3, "fall_04", 15998);
        Check(observed != null && observed.ActorId == 3 && observed.SourceTick == 103 && observed.ProcessOrder == 0,
            "actual ordered ground callback identities");
        Check(observed.Definition.Arguments.SequenceEqual(new[] { "grnd_hit_snd", "-1", "0", "-1" }),
            "source sound callback arguments preserved, no generic impact clip");
        SourceCinematicFrame returned = actual.CameraSourceTick(103, 1, targets, CameraSeed(), true, 1, 1);
        Check(returned.ReturnedToNormal && returned.SnapOnReturn && !actual.IsTransientActive,
            "actual new-play interrupt returns rail to normal without generic slow motion");
        Near(returned.State.Focus.X, CameraSeed().Focus.X, 0f, "new-play normal focus target");
        actual.Reset(CameraSeed());
        Check(!actual.HasRenderFrame && !actual.NeedsShakeDraws && actual.LastCameraTick == -1,
            "restart clears original camera, RNG requirements and presentation history");
        var invalid = new SourceCinematicPlayback(library, CameraSeed());
        Reject(() => invalid.ExecuteCameraOpcode(1, 0, -1, "fall_04", 15969, 0), "actor", "camera cue requires actual actor");
        Reject(() => invalid.ExecuteCameraOpcode(1, 0, 3, "qb_stance_anim", 15969, 0), "generic tackle",
            "stance is not an eligible impact source opcode");
        invalid = new SourceCinematicPlayback(library, CameraSeed());
        Reject(() => invalid.ExecuteCallback(1, 0, 3, "fall_04", 15998), "No original effect/audio",
            "unbound source effects/audio fail explicitly rather than silently disappear");
        var typed = new SourceCinematicPlayback(library, CameraSeed());
        typed.CameraSourceTick(200, 0, targets, CameraSeed(), false);
        var cameraCue = new SourceCameraCue(new SourcePresentationContext(200, 1, 3, "plyrseq.c", "fall_04", 15969,
            4, "ANI_SETCAMBOT"), SourcePresentationCueKind.CameraOpcode,
            new SourceCameraSelection("csi_rs_suplex", 0, 1, 123, "recorded-original-randrng1-fixture"), 3);
        typed.Execute(new SourcePresentationCueBatch(new SourcePresentationCue[] { cameraCue }));
        Check(!typed.Frame.Active && typed.IsTransientActive, "post-camera actor cue N selects next camera wake N+1, not a second camera tick N");
        typed.CameraSourceTick(201, 0, targets, CameraSeed(), false);
        Check(typed.Frame.Active, "actual next source camera wake consumes selected table");
        SourceImpactCallback numeric = null; typed.SourceCallback += value => numeric = value;
        var down = new SourceCallbackCue(new SourcePresentationContext(201, 1, 3, "plyrseq.c", "fall_04", 15995, 4, "ANI_CODE"),
            SourcePresentationCueKind.PlayerDownCallback, "player_down", 0, 0, 0, 3);
        var sound = new SourceCallbackCue(new SourcePresentationContext(201, 2, 3, "plyrseq.c", "fall_04", 15998, 4, "ANI_CODE"),
            SourcePresentationCueKind.SoundCallback, "grnd_hit_snd", -1, 0, -1, 3);
        var shake = new SourceShakerCue(new SourcePresentationContext(201, 3, 3, "plyrseq.c", "fall_04", 16000, 4, "ANI_SHAKER"), 2, 10, 3);
        typed.Execute(new SourcePresentationCueBatch(new SourcePresentationCue[] { down, sound, shake }));
        Equal(typed.LastObservedPlayerDown.ProcessOrder, 1, "player_down observed once in source order, not routed as a missing renderer effect or re-executed physics");
        Check(numeric != null && ReferenceEquals(numeric.ActualSourceCue, sound) && numeric.ActualSourceCue.Argument0 == -1 &&
            numeric.ActualSourceCue.Argument2 == -1, "actual typed sound arguments/context preserved, not named/default substitute");
        typed.CameraSourceTick(202, 0, targets, CameraSeed(), false, 0, 2);
        int beforeSuspension = typed.Frame.ElapsedTicks;
        typed.ObserveCameraProcessSuspension(true);
        Reject(() => typed.CameraSourceTick(210, 0, targets, CameraSeed(), false, 1, 1), "suspended",
            "verified playsel suspended CAMBOT does not advance on menu ticks");
        typed.ObserveCameraProcessSuspension(false);
        typed.CameraSourceTick(220, 0, targets, CameraSeed(), false, 1, 1);
        Equal(typed.Frame.ElapsedTicks, beforeSuspension + 1, "explicit process suspension permits source-tick gap without inventing elapsed camera/shaker ticks");
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
    }

    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
    }
    private static void Equal<T>(T actual, T expected, string label)
    { Check(EqualityComparer<T>.Default.Equals(actual, expected), label + " actual=" + actual + " expected=" + expected); }
    private static void Near(float actual, float expected, float tolerance, string label)
    { Check(!float.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance, label + " actual=" + actual + " expected=" + expected); }
    private static void Reject(Action action, string expected, string label)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) { rejected = error.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0; }
        Check(rejected, label + " must reject with identity diagnostic: " + expected);
    }
}
