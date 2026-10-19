using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static class Checks
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--free-ball-synthetic") { FreeBallChecks.Synthetic(); return 0; }
            if (args.Length < 1) throw new ArgumentException("Explicit prepared private data root required.");
            string root = Path.GetFullPath(args[0]);
            byte[] manifest = File.ReadAllBytes(Path.Combine(root, "manifest.json"));
            byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, path));
            var catalog = new DataCatalog(manifest, Read);
            var data = new PlayerData(catalog);
            if (args.Contains("--ball-trails-only")) { BallTrailChecks.Run(data); return 0; }
            if (args.Contains("--fire-adapter-only")) { FeatureChecks.Run(data); return 0; }
            if (args.Contains("--free-ball-only")) { FreeBallChecks.Run(data); return 0; }
            if (args.Length == 3 && args[1] == "--free-ball-reference") { FreeBallChecks.VerifyReference(data, args[2]); return 0; }
            Check(data.Animations.SequenceNames.Count == 281 && data.Animations.FrameCount == 26230,
                "The actual original281 library must feed gameplay and pose sampling.");
            Check(data.Callbacks.Profile == SourceC2CallbackData.M1Profile && data.Callbacks.Bindings.Count == 14,
                "The real M1 fourteen-binding bootstrap is required.");
            var sink = new RecordingSink();
            var host = new PlayerHost(data, sink);
            host.Advance(0, 1024, 768, 1024, 768, 1);
            Check(sink.State.GetProperty("actorCount").GetInt32() == 14 &&
                sink.State.GetProperty("jointsPerActor").GetInt32() == 15 && sink.Joints.Length == 14 * 15 * 16,
                "Actual production fourteen-actor/fifteen-joint render data is required, not static inspection.");
            Check(sink.Models.Count == 32 && sink.WorldDraws > 0 && sink.Textures.Count == data.Teams.Textures.Count + 27,
                "The actual complete original model selector, world and texture inventories must be uploaded.");
            Check(sink.HasOriginalBall, "The original medball2/HEDSHU free-ball mesh must be bound, never a hand extraction or sphere.");
            var actors = sink.State.GetProperty("actors").EnumerateArray().ToArray();
            Check(actors.Count(a => a.GetProperty("team").GetString() == "Home") == 7 &&
                actors.Count(a => a.GetProperty("team").GetString() == "Away") == 7,
                "Both source teams need their seven real original actor/roster identities.");
            var uniforms = actors.Select(a => a.GetProperty("uniformHash").GetString()).Distinct().ToArray();
            Check(uniforms.Length == 2 && actors.All(a => a.GetProperty("tint").GetString() == "none"),
                "Different original team texture bindings, not shared RGB meshes/tints, are mandatory.");
            Check(sink.State.GetProperty("hud").GetProperty("vertices").GetInt32() > 0 &&
                sink.HudTextures.All(t => t.StartsWith("hud/") && sink.Textures.ContainsKey(t)),
                "Actual original HUD atlas/font/draw commands must be consumed.");
            VerifyJointMath(data, host);
            var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
            var input = new SourceCabinetInputBuffer(data.Configuration.Match);
            input.Capture(new[] { new SourceCabinetSample(0, quantizer.SevenLevel(0, 0), SourceButtons.B) });
            input.Capture(new[] { new SourceCabinetSample(0, quantizer.SevenLevel(0, 0), SourceButtons.None) });
            Check(input.NextHostFrame().Stations[0].Pressed == SourceButtons.B &&
                input.NextHostFrame().Stations[0].Released == SourceButtons.B,
                "Real cabinet buffer preserves press/release pairs even between two source wakes.");
            var beforePose = actors.Select(a => a.GetProperty("poseDigest").GetString()).ToArray();
            int uploadsBefore = sink.Meshes.Count;
            for (int i = 0; i < 90; i++)
                host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            Check(host.Game.State.Tick == 90 && host.Game.SourceTick == 85 && host.SourceWakes == 85,
                "Actual 60Hz host/57Hz source skip/hold scheduling must run, with every source wake observed.");
            Check(host.Camera.LastSourceTick == host.Game.SourceTick &&
                host.Game.SourceCameraHeadingSampleTick == host.Game.SourceTick,
                "The actual current pre-player camera frame must feed M1, not an inspection/orbit camera.");
            var nextPose = sink.State.GetProperty("actors").EnumerateArray()
                .Select(a => a.GetProperty("poseDigest").GetString()).ToArray();
            Check(!beforePose.SequenceEqual(nextPose), "Real production animation must advance; static source meshes cannot pass.");
            Check(sink.Meshes.Count == uploadsBefore, "Per-frame poses must update matrices without re-uploading static geometry.");
            long tick = host.Game.SourceTick;
            host.SetActive(false);
            host.Advance(2, 1024, 768, 1024, 768, 1);
            Check(host.Game.SourceTick == tick, "Focus pause must not advance the source clock or catch up hidden time.");
            host.SetActive(true);
            host.Capture(new double[8], new int[4]);
            host.Advance(0, 360, 270, 360, 270, 1);
            Check(sink.State.GetProperty("hud").GetProperty("vertices").GetInt32() > 0,
                "Original HUD composition must survive actual portrait CSS metrics.");
            RejectMissingAndChanged(manifest, Read, catalog.Entry("model"));
            VerifyCausalInput(data);
            VerifyDeferredOriginalHud(data);
            FreeBallChecks.Run(data);
            if (data.Fire != null) FeatureChecks.Run(data);
            if (data.PoppedHead != null) PoppedHeadChecks.Run(data);
            if (args.Contains("--require-portable"))
                Check(KernelBinding.IsPortable(host.Game) && host.Game.Compatibility == SourceGameplayCompatibility.Portable,
                    "A player-ready test must reject an unbound portable factory even when strict rendering works.");
            Directory.CreateDirectory("tools/source-player/artifacts");
            File.WriteAllText("tools/source-player/artifacts/native-frame.json", host.LastStatus);
            Console.WriteLine(JsonSerializer.Serialize(new {
                status = "passed", assertions, factory = KernelBinding.Factory, policy = host.Game.Compatibility.ToString(),
                portableBound = KernelBinding.IsPortable(host.Game), sourceTick = host.Game.SourceTick,
                originalModels = sink.Models.Count, originalMeshes = sink.Meshes.Count,
                originalTextures = sink.Textures.Count, worldDraws = sink.WorldDraws,
                boundary = "Actual production loader/pose/input/57Hz host checks; not a browser, Unity build, M1 parity trace or native pixel qualification."
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void VerifyJointMath(PlayerData data, PlayerHost host)
    {
        foreach (PlayerView actor in host.Game.State.Players)
        {
            SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(data.Animations, data.ReferencePivots, actor, actor, 1);
            var matrix = new double[16];
            var draw = SourceInterfacePresentation.DrawState(actor, host.Game.State);
            var match = ((ISourceMatchState)host.Game.State).Match;
            var appearance = SourceTeamPresentationModel.Select(data.Rosters, match.Configuration.HomeTeam.TeamIndex,
                match.Configuration.AwayTeam.TeamIndex, (int)data.Configuration.Presentation.DesignatedHomeSide,
                actor, host.Game.State.Ball.CarrierId, draw);
            var mesh = SourceTeamGeneratedModels.BuildModel(
                SourceTeamGeneratedModels.ReadModel(data.Rosters, appearance.Model.Index, data.Header));
            for (int joint = 0; joint < 15; joint++)
            {
                var p = mesh[joint].Positions[0];
                SourceVector3 root = host.Game.SourceActorWorldPosition(actor.Id);
                FrameMath.Joint(matrix, 0, pose.Joints[joint], root, pose.ModelScale);
                SourceVector3 expected = SourceAnimationMatrices.TransformVertex(pose.Joints[joint],
                    new SourceVector3(p.X, p.Y, p.Z), pose.ModelScale);
                double x = matrix[0] * p.X + matrix[4] * p.Y - matrix[8] * p.Z + matrix[12];
                double y = matrix[1] * p.X + matrix[5] * p.Y - matrix[9] * p.Z + matrix[13];
                double z = matrix[2] * p.X + matrix[6] * p.Y - matrix[10] * p.Z + matrix[14];
                Check(Math.Abs(x - root.X - expected.X) < 0.0002 &&
                    Math.Abs(y - root.Y - expected.Y) < 0.0002 &&
                    Math.Abs(z - root.Z + expected.Z) < 0.0002,
                    "Browser joint matrix must match unchanged actual production pose transformation for actor " + actor.Id + " joint " + joint);
            }
        }
    }

    private static void VerifyCausalInput(PlayerData data)
    {
        var neutral = new PlayerHost(data, new RecordingSink());
        var controlled = new PlayerHost(data, new RecordingSink());
        bool diverged = false;
        for (int i = 0; i < 600 && !diverged; i++)
        {
            bool humanControl = neutral.Game.State.Phase == GamePhase.LiveBall && neutral.Game.State.Ball.CarrierId >= 0;
            neutral.Capture(new double[8], new int[4]);
            controlled.Capture(humanControl ? new double[] { 0, -1, 0, 0, 0, 0, 0, 0 } : new double[8],
                humanControl ? new[] { 4, 0, 0, 0 } : new int[4]);
            neutral.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            controlled.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            Check(neutral.Game.SourceTick == controlled.Game.SourceTick,
                "Input-functional counterparts must use the same real production source scheduler.");
            foreach (PlayerView actor in controlled.Game.State.Players)
                if (actor.Source.ControllerStationId >= 0)
                {
                    PlayerView baseline = neutral.Game.State.Players.First(p => p.Id == actor.Id);
                    diverged |= Math.Abs(actor.Position.X - baseline.Position.X) > .01 ||
                        Math.Abs(actor.Position.Z - baseline.Position.Z) > .01;
                }
        }
        Check(diverged, "Actual production controlled actor must causally differ with cabinet stick/turbo, not merely play a canned animation.");
    }

    private static void VerifyDeferredOriginalHud(PlayerData data)
    {
        var host = new PlayerHost(data, new RecordingSink());
        bool pending = false;
        for (int i = 0; i < 60 * 20; i++)
        {
            host.Capture(new double[8], new int[4]);
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            if (host.Game.C2State.ResultPending)
            {
                pending = true;
                Check(host.HudProcess.ResultPending && host.HudProcess.CommittedSourceTick == host.Game.SourceTick - 1,
                    "Actual C2 pending-result wake must hold the last committed original HUD, not fabricate a result or stop the kernel.");
            }
            else if (pending)
            {
                Check(((ISourceMatchState)host.Game.State).Match.Result.IsSpecified &&
                    host.HudProcess.CommittedSourceTick == host.Game.SourceTick &&
                    host.HudProcess.PendingResultWakes == 1,
                    "The actual original game_proc result must replace the held authored HUD on its next source wake.");
                return;
            }
        }
        Check(false, "The ordinary original kickoff never exercised its real C2 result/HUD commit boundary.");
    }

    private static void RejectMissingAndChanged(byte[] manifest, Func<string, byte[]> read, string modelPath)
    {
        foreach (bool missing in new[] { true, false })
        {
            bool rejected = false;
            try
            {
                var catalog = new DataCatalog(manifest, path => {
                    byte[] bytes = read(path);
                    if (path != modelPath) return bytes;
                    if (missing) return Array.Empty<byte>();
                    bytes[bytes.Length / 2] ^= 1;
                    return bytes;
                });
                catalog.Read(modelPath);
            }
            catch (InvalidDataException error)
            { rejected = error.Message.Contains(modelPath, StringComparison.Ordinal); }
            Check(rejected, "Mutation of the ACTUAL prepared original model must fail by named asset, not produce ready placeholders.");
        }
    }

    private sealed class RecordingSink : IRenderSink
    {
        internal readonly Dictionary<string, string> Textures = new();
        internal readonly HashSet<string> Meshes = new();
        internal readonly HashSet<int> Models = new();
        internal int WorldDraws;
        internal bool HasOriginalBall;
        internal double[] Joints = Array.Empty<double>();
        internal string[] HudTextures = Array.Empty<string>();
        internal JsonElement State;
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin)
        {
            Check(rgba.Length >= width * height * 4, "Original texture dimensions must match its real pixel bytes.");
            Textures.Add(id, sha256);
        }
        public void Mesh(string id, double[] vertices, int[] indices)
        {
            Check(vertices.Length % 5 == 0 && indices.Length % 3 == 0 && indices.All(i => i >= 0 && i < vertices.Length / 5),
                "Actual uploaded original mesh indices/UV vertex inventory must be valid: " + id);
            Check(Meshes.Add(id), "Static original geometry may only be registered once: " + id);
        }
        public void WorldDraw(string mesh, string texture, string state)
        {
            Check(Meshes.Contains(mesh) && Textures.ContainsKey(texture), "Actual world geometry/texture must be bound.");
            WorldDraws++;
        }
        public void Model(int id, string joints) { Check(Models.Add(id), "Duplicate original model selector."); }
        public void Appearance(int id, string metadata)
        {
            using var json = JsonDocument.Parse(metadata);
            Check(json.RootElement.GetProperty("tint").GetString() == "none", "Original uniforms cannot be replaced by tint.");
        }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) =>
            HasOriginalBall = Meshes.Contains(mesh) && Textures.ContainsKey(texture);
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags,
            double[] flashAlpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            Joints = joints;
            HudTextures = hudTextures;
            State = JsonDocument.Parse(status).RootElement.Clone();
        }
    }
}
