using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.SourcePlayer;

static class ArmRenderingChecks
{
    private static int assertions;
    private static readonly int[] Arms = { 2, 3, 4, 5, 6, 7 };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ARM RENDERING: " + message);
        assertions++;
    }

    public static void Run(PlayerData data, string? output)
    {
        var sink = new Sink();
        _ = new RenderAssets(data, sink);
        byte[] dark = sink.Textures["teams/runtime/ARMHND2.WMS"];
        byte[] light = sink.Textures["teams/runtime/ARMHND.WMS"];
        Require(dark.Length == 64 * 64 * 4 && Enumerable.Range(0, 4096).All(i => dark[i * 4 + 3] == 0) &&
            dark.Where((_, i) => i % 4 != 3).Any(value => value != 0),
            "The actual pinned dark-skin TGA has visible RGB and alpha0, not an intentional missing-arm mask.");
        Require(Enumerable.Range(0, 4096).All(i => light[i * 4 + 3] > 0),
            "The actual light-skin control must retain its nonzero authored alpha.");
        byte[] uniform = sink.Textures["teams/runtime/BEAHLOD.WMS"];
        Require(Enumerable.Range(0, uniform.Length / 4).Any(i => uniform[i * 4 + 3] == 0) &&
            Enumerable.Range(0, uniform.Length / 4).Any(i => uniform[i * 4 + 3] == 255),
            "Genuine original runtime uniform/number alpha exclusions must remain distinct from skin RGB.");

        int skinTriangles = 0, poseCases = 0;
        var modelProof = new List<object>();
        for (int modelIndex = 0; modelIndex < 32; modelIndex++)
        {
            var model = SourceTeamGeneratedModels.ReadModel(data.Rosters, modelIndex, data.Header);
            var metadata = sink.Models[modelIndex];
            var limbs = new List<object>();
            foreach (int joint in Arms)
            {
                SourceLimb limb = model.Limbs[joint];
                SourceRigNode node = model.Nodes[joint];
                JsonElement uploaded = metadata[joint];
                Require(node.Flags == 0 && uploaded.GetProperty("drawEnabled").GetBoolean(),
                    $"Original full-player skeleton must draw model{modelIndex}/joint{joint}.");
                var skin = limb.Faces.Where(face => face.MaterialIndex == 0).ToArray();
                Require(skin.Length > 0, $"Source-authored skin geometry is missing at model{modelIndex}/joint{joint}.");
                var part = uploaded.GetProperty("parts").EnumerateArray().Single(p => p.GetProperty("slot").GetInt32() == 0);
                string id = part.GetProperty("mesh").GetString()!;
                var mesh = sink.Meshes[id];
                Require(mesh.Indices.Length == skin.Length * 3,
                    $"Source skin triangles must not disappear in mesh/submesh transport: {id}.");
                for (int face = 0; face < skin.Length; face++)
                for (int corner = 0; corner < 3; corner++)
                {
                    AssetVector3 p = limb.Vertices[skin[face].VertexIndices[corner]];
                    AssetVector2 uv = limb.TextureCoordinates[skin[face].TextureCoordinateIndices[corner]];
                    int at = mesh.Indices[face * 3 + corner] * 5;
                    Require(Math.Abs(mesh.Vertices[at] - (p.X - node.BindPivot.X)) < .000002 &&
                        Math.Abs(mesh.Vertices[at + 1] - (p.Y - node.BindPivot.Y)) < .000002 &&
                        Math.Abs(mesh.Vertices[at + 2] - (p.Z - node.BindPivot.Z)) < .000002 &&
                        mesh.Vertices[at + 3] == uv.X / 256f && mesh.Vertices[at + 4] == 1f - uv.Y / 256f,
                        $"GPU skin positions/UVs/topology must retain original source corners: {id}.");
                }
                skinTriangles += skin.Length;
                limbs.Add(new { joint, symbol = limb.Symbol, sourceFaces = limb.Faces.Count,
                    skinFaces = skin.Length, uploadedSkinIndices = mesh.Indices.Length });
            }
            modelProof.Add(new { model = modelIndex, limbs });
        }

        var referenceModel = data.ReferencePlayer;
        foreach (string sequence in new[] { "QBSTANCE", "RUN" })
        {
            ISourceAnimationClip clip = data.Animations.GetClip(sequence);
            foreach (int ordinal in new[] { 0, clip.SelectedSourceFrames.Count / 2, clip.SelectedSourceFrames.Count - 1 }.Distinct())
            foreach (var heading in new[] { new FieldVector(1, 0), new FieldVector(-1, 0) })
            foreach (float scale in new[] { 1f, 1.2f })
            {
                SourceAnimationFrame frame = clip.GetFrame(clip.SelectedSourceFrames[ordinal]);
                var root = new SourceVector3(13, 7, -5);
                var world = new SourceVector3(31, 4, 17);
                var pose = SourceAnimationMatrices.Evaluate(data.ReferencePivots, frame.Pose, heading, root, scale);
                double[][] original = OriginalMatrices(referenceModel, frame.Pose, heading, root, scale);
                foreach (int joint in Arms)
                {
                    var packet = new double[16];
                    FrameMath.Joint(packet, 0, pose.Joints[joint], world, scale);
                    foreach (AssetVector3 p in referenceModel.Limbs[joint].Vertices)
                    {
                        AssetVector3 pivot = referenceModel.Nodes[joint].BindPivot;
                        double[] local = { p.X - pivot.X, p.Y - pivot.Y, p.Z - pivot.Z };
                        double[] source = { p.X, p.Y, p.Z };
                        for (int row = 0; row < 3; row++)
                        {
                            double actual = packet[12 + row], expected = original[joint][row * 4 + 3] +
                                (row == 0 ? world.X : row == 1 ? world.Y : world.Z);
                            for (int column = 0; column < 3; column++)
                            {
                                actual += packet[column * 4 + row] * local[column];
                                expected += original[joint][row * 4 + column] * source[column];
                            }
                            Require(Math.Abs(actual - expected) < .0005,
                                $"Actual arm pose transport must match original pivot/quaternion semantics: {sequence}/{ordinal}/joint{joint}.");
                        }
                    }
                }
                poseCases++;
            }
        }
        var proof = new {
            status = "passed", assertions, models = sink.Models.Count, skinTriangles, poseCases,
            sourceRevision = SourceAnimationLibrary.SourceRevision,
            manifestSha256 = data.Catalog.ManifestSha256,
            darkSkinTexture = data.Teams.Textures.Single(t => t.Id == "runtime/ARMHND2.WMS").Sha256,
            alphaZeroTexels = 4096, modelProof,
            boundary = "Actual pinned assets/mesh transport and source-formula pose comparisons; no simulation advancement, browser/native pixel or Unity execution."
        };
        if (output != null) File.WriteAllText(output, JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(new { proof.status, assertions, proof.models, skinTriangles, poseCases, proof.boundary }));
    }

    // Independent row-major source formula: pipe3D.c generate_matrices/quat2mat,
    // pinned blob0966ecaf0d7eef7d9c635a97899a421ce1eeea41, lines211-242/753-768.
    private static double[][] OriginalMatrices(Blitz.AssetImport.SourcePlayer model, SourceJointPose pose,
        FieldVector heading, SourceVector3 root, float scale)
    {
        double angle = Math.Atan2(heading.X, heading.Z), c = Math.Cos(angle) * scale, s = Math.Sin(angle) * scale;
        double[] placement = { c, 0, s, root.X, 0, scale, 0, root.Y, -s, 0, c, root.Z };
        var result = new double[15][];
        for (int joint = 0; joint < 15; joint++)
        {
            SourceRotation q = pose.Rotations[joint];
            double inverse = 1 / Math.Sqrt(q.X * (double)q.X + q.Y * (double)q.Y + q.Z * (double)q.Z + q.W * (double)q.W);
            double x = q.X * inverse, y = q.Y * inverse, z = q.Z * inverse, w = q.W * inverse;
            AssetVector3 p = model.Nodes[joint].BindPivot;
            double[] local = {
                1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), p.X,
                2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), p.Y,
                2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), p.Z
            };
            double[] parent = joint == 0 ? placement : result[model.Nodes[joint].ParentIndex];
            var matrix = new double[12];
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
            {
                for (int k = 0; k < 3; k++) matrix[row * 4 + column] += parent[row * 4 + k] * local[k * 4 + column];
                if (column == 3) matrix[row * 4 + column] += parent[row * 4 + 3];
            }
            for (int row = 0; row < 3; row++)
                matrix[row * 4 + 3] -= matrix[row * 4] * p.X + matrix[row * 4 + 1] * p.Y + matrix[row * 4 + 2] * p.Z;
            result[joint] = matrix;
        }
        return result;
    }

    private sealed class Sink : IRenderSink
    {
        public readonly Dictionary<string, byte[]> Textures = new();
        public readonly Dictionary<string, (double[] Vertices, int[] Indices)> Meshes = new();
        public readonly Dictionary<int, JsonElement> Models = new();
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin) => Textures.Add(id, rgba);
        public void Mesh(string id, double[] xyzuv, int[] indices) => Meshes.Add(id, (xyzuv, indices));
        public void Model(int id, string joints) { using var json = JsonDocument.Parse(joints); Models.Add(id, json.RootElement.Clone()); }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags, double[] flashAlpha, string metadata) { }
        public void Present(double[] viewProjection, double[] joints, int[] models, int[] appearances,
            double[] ball, double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status) =>
            throw new InvalidOperationException("Asset/pose checks must not advance a gameplay host.");
    }
}
