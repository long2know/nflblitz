using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static class FreeBallChecks
{
    private static int assertions;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FREE BALL: " + message);
        assertions++;
    }

    private static SourceCinematicTransform TestCamera() => SourceCinematicTransform.FromState(
        new SourceCinematicSeed(new SourceVector3(17, 11, -80), new SourceVector3(-3, 5, 6), 90, 0, 0));

    public static void Synthetic()
    {
        var quarter = Enumerable.Range(0, 257).Select(i => (float)Math.Sin(i * Math.PI / 512)).ToArray();
        int cursor = 0;
        var sticks = Enumerable.Range(0, 49).Select(i => i == 24 ? -1 : cursor++ % 32 * 32).ToArray();
        var directions = new SourceDirectionMath(quarter, sticks, "synthetic-trig", "synthetic-sticks");
        foreach (var angle in new[] { (256, 0, 0), (0, 256, 0), (0, 0, 256), (137, -333, 1025), (-2073, 2049, -1137) })
        foreach (int big in new[] { 0, -3 })
        {
            var state = new SourceFreeBallState(19, true, new SourceVector3(7, 11, 13),
                angle.Item1, angle.Item2, angle.Item3, 2, 0, big);
            SourceFreeBallDraw draw = SourceFreeBallRenderModel.Evaluate(state, directions);
            Compare(draw.WorldMatrix, ClosedForm(state, directions), .000002,
                "Source rotation must retain Y * X(-elevation) * Z(roll), integer axes and source scale.");
            double[] packet = FrameMath.FreeBall(draw);
            Require(packet[1] == 19 && packet[16] == 7 && packet[17] == 11 && packet[18] == 13 &&
                packet[19] == 1, "Actual ball GPU packet must retain source translation and source tick.");
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
                Require(packet[4 + column * 4 + row] == draw.WorldMatrix[row * 4 + column],
                    "Actual ball GPU packet must bind every original rotation/scale matrix component.");
            var relative = draw.WorldMatrix.ToArray();
            SourceCinematicTransform camera = TestCamera();
            relative[3] -= camera.Position.X; relative[7] -= camera.Position.Y; relative[11] -= camera.Position.Z;
            Compare(draw.CameraMatrix(camera), ReferenceMultiply(camera.Transform.ToArray(), relative), .00002,
                "Source camera composition must subtract eye before camera * ball, never recenter or reflect its source pivot.");
        }
        bool rejected = false;
        try { SourceFreeBallRenderModel.Evaluate(default, directions); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Unspecified source state must fail explicitly.");
        rejected = false;
        try { SourceFreeBallData.Read(new byte[] { 123, 125 }, SourceFreeBallData.PreparedManifestSha256,
            _ => throw new InvalidOperationException("An invalid manifest reached original payload access.")); }
        catch (SourceRosterException error) when (error.Message.Contains("Exact calibrated original free-ball manifest")) { rejected = true; }
        Require(rejected, "Malformed original free-ball resources must fail before any payload/fallback access.");
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", syntheticFreeBallAssertions = assertions,
            boundary = "Public synthetic matrix/packet fixtures; not original-input, GPU or gameplay witnesses." }));
    }

    public static void Run(PlayerData data)
    {
        Synthetic();
        var sink = new Sink();
        var host = new PlayerHost(data, sink);
        host.Advance(0, 1024, 768, 1024, 768, 1);
        Require(sink.BallMesh == "original/free-ball/limb_medball_obj_big" && sink.BallVertices != null &&
            sink.BallIndices != null && sink.BallIndices.Length == 48 * 3,
            "Actual renderer must upload the original medball2 free-ball mesh, not held-ball geometry.");
        SourceLimb limb = data.FreeBall.Limb;
        foreach (string changed in new[] { "models/medball2.sdl", "medball2.source-authored.h" })
        {
            bool rejected = false;
            try
            {
                SourceFreeBallData.Read(data.Catalog.EntryBytes("freeBall"), data.Catalog.EntryHash("freeBall"), path =>
                {
                    byte[] bytes = data.Catalog.Read("free-ball/" + path).ToArray();
                    if (path == changed) bytes[0] ^= 1;
                    return bytes;
                });
            }
            catch (SourceRosterException error) when (error.Message.Contains("content pin mismatch")) { rejected = true; }
            Require(rejected, "Changed required original ball resources must fail by their content pin: " + changed);
        }
        for (int faceIndex = 0; faceIndex < limb.Faces.Count; faceIndex++)
        for (int drawCorner = 0; drawCorner < 3; drawCorner++)
        {
            int corner = drawCorner == 0 ? 0 : 3 - drawCorner;
            SourceFace face = limb.Faces[faceIndex];
            AssetVector3 point = limb.Vertices[face.VertexIndices[corner]];
            AssetVector2 uv = limb.TextureCoordinates[face.TextureCoordinateIndices[corner]];
            int index = sink.BallIndices![faceIndex * 3 + drawCorner] * 5;
            Require(sink.BallVertices![index] == point.X && sink.BallVertices[index + 1] == point.Y &&
                sink.BallVertices[index + 2] == point.Z && sink.BallVertices[index + 3] == uv.X / 256f &&
                sink.BallVertices[index + 4] == 1f - uv.Y / 256f,
                "Actual free-ball draw must retain original medball2 geometry and unchanged source pivot.");
        }
        Require(sink.BallTexture == "teams/" + SourceFreeBallData.TextureId &&
            sink.AlternateTexture == "teams/" + SourceFreeBallData.AlternateTextureId &&
            sink.BallMetadata.GetProperty("rawTextureCode").GetInt32() == 0 &&
            sink.BallMetadata.GetProperty("sourcePivot").GetString() == "authoring-origin-unchanged",
            "Actual free-ball renderer must bind HEDSHU/HEDSHU2 sole decals, never held-ball BALL/material6.");
        SourceFreeBallDraw initial = SourceFreeBallBinding.Capture(host.Game);
        Require(initial.State.SourceTick == 0 && initial.State.Roll == 0,
            "Actual free-ball spin must copy Core roll at its actual source tick, not a future/render tick.");
        var requests = new List<object>();
        void Record(SourceFreeBallState state, string origin)
        {
            SourceCinematicTransform camera = TestCamera();
            requests.Add(new { origin, sourceTick = state.SourceTick, visible = state.Visible,
                position = new[] { state.Position.X, state.Position.Y, state.Position.Z },
                heading = state.Heading, elevation = state.Elevation, roll = state.Roll, scale = state.Scale,
                rawFlags = state.RawFlags, pupBigBall = state.PupBigBall, carrier = state.Visible ? -1 : 3,
                cameraEye = new[] { camera.Position.X, camera.Position.Y, camera.Position.Z }, cameraMatrix = camera.Transform });
        }
        Record(initial.State, "actual-production-initial");
        bool flight = false, changing = false, held = false;
        long firstFlightTick = -1;
        float[]? firstFlightMatrix = null;
        int uploads = sink.BallUploads;
        for (int hostTick = 0; hostTick < 1200 && !(flight && changing && held); hostTick++)
        {
            var gameState = host.Game.State;
            if (gameState.Phase == GamePhase.PlaySelect || gameState.Phase == GamePhase.PreSnap)
                host.Capture(new double[8], new[] { hostTick % 20 < 5 ? 2 : 0, 0, 0, 0 });
            else host.Capture(new double[8], new int[4]);
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            SourceFreeBallDraw draw = SourceFreeBallBinding.Capture(host.Game);
            Require(draw.State.SourceTick == host.Game.SourceTick && draw.State.SourceTick == sink.Packet[1],
                "Actual free-ball state and rendered packet must share the executing Core source tick.");
            Require(draw.State.Visible == (host.Game.State.Ball.CarrierId == -1 ||
                (host.Game.State.Ball.Presentation.RawFlags & 0x800) != 0),
                "Actual free-ball visibility must consume carrier/BF_PHANTOM without changing possession.");
            double[] expectedPacket = FrameMath.FreeBall(draw);
            Require(sink.Packet.SequenceEqual(expectedPacket),
                "Actual PlayerHost must submit the authoritative ball matrix, not a disconnected renderer packet.");
            Compare(draw.WorldMatrix, ClosedForm(draw.State, data.Playbook.Directions), .000002,
                "Source rotation must retain Y * X(-elevation) * Z(roll), integer axes and source scale.");
            if (draw.State.Visible && host.Game.State.Ball.Mode == BallMode.InFlight && draw.State.Position.Y > 3)
            {
                if (!flight)
                {
                    flight = true; firstFlightTick = draw.State.SourceTick; firstFlightMatrix = draw.WorldMatrix.ToArray();
                    Record(draw.State, "actual-production-public-input-flight");
                }
                else if (draw.State.SourceTick > firstFlightTick && !draw.WorldMatrix.Take(3).SequenceEqual(firstFlightMatrix!.Take(3)))
                {
                    changing = true;
                    Record(draw.State, "actual-production-later-source-flight");
                }
            }
            if (!held && !draw.State.Visible && host.Game.State.Ball.CarrierId >= 0)
            {
                held = true;
                int carrier = host.Game.State.Ball.CarrierId;
                Require((sink.ModelIds[carrier] & 12) != 0,
                    "Retained held ball must remain original actor hand geometry, not a second free ball.");
                Record(draw.State, "actual-production-retained-held-ball");
            }
        }
        Require(flight && changing && held,
            "Legal production input must retain held geometry and expose source-tick-changing free-ball flight.");
        double[] before = sink.Packet.ToArray();
        host.Advance(0, 1024, 768, 1024, 768, 1);
        Require(sink.Packet.SequenceEqual(before) && sink.BallUploads == uploads,
            "Render-only frames must not invent spin ticks or re-upload static ball geometry.");
        foreach (int heading in new[] { -1025, 0, 137, 256, 1023, 2049 })
        foreach (int elevation in new[] { -333, 0, 256 })
        foreach (int roll in new[] { -60, 0, 1084 })
            Record(new SourceFreeBallState(0, true, new SourceVector3(9, 17, -31), heading, elevation, roll,
                2, (heading & 1) != 0 ? 0x41 : 0, elevation == 0 ? 0 : -2), "defined-domain-synthetic-angles-original-trig");
        Directory.CreateDirectory("tools/source-player/artifacts");
        File.WriteAllText("tools/source-player/artifacts/free-ball-reference-inputs.json", JsonSerializer.Serialize(requests, Json));
        File.WriteAllText("tools/source-player/artifacts/free-ball-geometry.json", JsonSerializer.Serialize(new {
            sourceSha256 = SourceFreeBallData.SourceSha256, geometrySha256 = SourceFreeBallData.HeaderSha256,
            vertices = sink.BallVertices, indices = sink.BallIndices, metadata = sink.BallMetadata
        }, Json));
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", originalFreeBallAssertions = assertions,
            actualFlight = flight, changingSourceOrientation = changing, retainedHeldGeometry = held,
            referenceCases = requests.Count, endSourceTick = host.Game.SourceTick,
            boundary = "Actual production original-input C# host; not browser/GPU/Unity/native pixel qualification." }));
    }

    public static void VerifyReference(PlayerData data, string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        int cases = 0;
        foreach (JsonElement row in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            JsonElement input = row.GetProperty("input");
            float[] p = input.GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            var state = new SourceFreeBallState(input.GetProperty("sourceTick").GetInt64(), input.GetProperty("visible").GetBoolean(),
                new SourceVector3(p[0], p[1], p[2]), input.GetProperty("heading").GetInt32(), input.GetProperty("elevation").GetInt32(),
                input.GetProperty("roll").GetInt32(), input.GetProperty("scale").GetSingle(),
                input.GetProperty("rawFlags").GetInt32(), input.GetProperty("pupBigBall").GetInt32());
            SourceFreeBallDraw draw = SourceFreeBallRenderModel.Evaluate(state, data.Playbook.Directions);
            Require(row.GetProperty("drawn").GetBoolean() == state.Visible, "Original C draw_ball carrier/phantom branch must match.");
            if (!state.Visible) continue;
            Compare(draw.CameraMatrix(TestCamera()), row.GetProperty("matrix").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
                .00003, "Actual free-ball camera matrix must match compiled original draw_ball/rotx/roty/rotz/mxm.");
            Require(row.GetProperty("texture").GetInt32() == (draw.Alternate ? 1 : 0) &&
                row.GetProperty("white").GetBoolean() == draw.White,
                "Actual free-ball alternate/white material must match compiled original draw_ball.");
            cases++;
        }
        Require(cases >= 54, "Original compiled transform reference must cover the defined integer-angle domain.");
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", originalCDrawComparisons = cases, assertions,
            boundary = "Extracted pinned original C, host compiler with FMA disabled; NOT native toolchain/global-render/pixel equivalence." }));
    }

    private static double[] ClosedForm(SourceFreeBallState s, SourceDirectionMath d)
    {
        double sx = d.Sin(-s.Elevation), cx = d.Cos(-s.Elevation), sy = d.Sin(s.Heading), cy = d.Cos(s.Heading);
        double sz = d.Sin(s.Roll), cz = d.Cos(s.Roll), scale = s.Scale * (s.PupBigBall != 0 ? 1.5 : 1);
        return new[] { (cy * cz + sy * sx * sz) * scale, (-cy * sz + sy * sx * cz) * scale, sy * cx * scale, s.Position.X,
            cx * sz * scale, cx * cz * scale, -sx * scale, s.Position.Y,
            (-sy * cz + cy * sx * sz) * scale, (sy * sz + cy * sx * cz) * scale, cy * cx * scale, s.Position.Z };
    }

    private static float[] ReferenceMultiply(float[] a, float[] b)
    {
        var result = new float[12];
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 4; column++)
        {
            double value = column == 3 ? a[row * 4 + 3] : 0;
            for (int k = 0; k < 3; k++) value += (double)a[row * 4 + k] * b[k * 4 + column];
            result[row * 4 + column] = (float)value;
        }
        return result;
    }

    private static void Compare(IEnumerable<float> actual, IEnumerable<float> expected, double tolerance, string message) =>
        Compare(actual, expected.Select(v => (double)v), tolerance, message);
    private static void Compare(IEnumerable<float> actual, IEnumerable<double> expected, double tolerance, string message)
    {
        float[] a = actual.ToArray(); double[] b = expected.ToArray();
        Require(a.Length == b.Length && a.Zip(b).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance), message);
    }

    private sealed class Sink : IRenderSink
    {
        internal double[]? BallVertices;
        internal int[]? BallIndices;
        internal int BallUploads;
        internal string? BallMesh, BallTexture, AlternateTexture;
        internal JsonElement BallMetadata;
        internal double[] Packet = Array.Empty<double>();
        internal int[] ModelIds = Array.Empty<int>();
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices)
        {
            if (id.StartsWith("original/free-ball/")) { BallVertices = vertices; BallIndices = indices; BallUploads++; }
        }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata)
        {
            BallMesh = mesh; BallTexture = texture; AlternateTexture = alternateTexture;
            BallMetadata = JsonDocument.Parse(metadata).RootElement.Clone();
        }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] flash, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] colors, string[] textures, int[] counts, string status)
        { Packet = ball.ToArray(); ModelIds = models.ToArray(); }
    }
}
