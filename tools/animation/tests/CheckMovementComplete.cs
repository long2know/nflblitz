using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;

internal static class CheckMovementComplete
{
    private static int assertions;
    private static double maximumMatrixError;
    private static int malformedCases;

    private static void Check(bool value, string label)
    {
        assertions++;
        if (!value) throw new Exception("ASSERT " + label);
    }

    private static void Near(double a, double b, double tolerance, string label)
    {
        Check(!double.IsNaN(a) && !double.IsInfinity(a) && Math.Abs(a - b) <= tolerance,
            label + ": " + a + " versus " + b);
    }

    private static SourceAnimationLibrary Load(string root)
    {
        return SourceAnimationLibrary.Read(File.ReadAllBytes(Path.Combine(root, "export-inventory.json")),
            path => File.ReadAllBytes(Path.Combine(root, path)));
    }

    private static void CheckBase(string root, string baselineRoot, SourceAnimationLibrary library)
    {
        SourceAnimationLibrary baseline = Load(baselineRoot);
        Check(baseline.SequenceNames.Count == 156 && baseline.FrameCount == 13420, "frozen movement156 totals");
        using (JsonDocument a = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "export-inventory.json"))))
        using (JsonDocument b = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(baselineRoot, "export-inventory.json"))))
        {
            var entries = new Dictionary<string, string>();
            foreach (JsonElement entry in a.RootElement.GetProperty("clips").EnumerateArray())
                entries.Add(entry.GetProperty("runtime_name").GetString(), entry.GetRawText());
            foreach (JsonElement entry in b.RootElement.GetProperty("clips").EnumerateArray())
                Check(entries[entry.GetProperty("runtime_name").GetString()] == entry.GetRawText(),
                    "complete inventory retains every old entry byte-for-byte");
        }
        foreach (string name in baseline.SequenceNames)
        {
            byte[] before = File.ReadAllBytes(Path.Combine(baselineRoot, "clips", name + ".json"));
            byte[] after = File.ReadAllBytes(Path.Combine(root, "clips", name + ".json"));
            Check(Convert.ToBase64String(before) == Convert.ToBase64String(after), name + " immutable old clip bytes");
            ISourceAnimationClip clip = library.GetClip(name), old = baseline.GetClip(name);
            Check(clip.SourceFlags == old.SourceFlags && clip.SourceFrameCount == old.SourceFrameCount &&
                clip.SelectedSourceFrames.Count == old.SelectedSourceFrames.Count &&
                clip.SourcePath == old.SourcePath && clip.SourceGitBlobSha1 == old.SourceGitBlobSha1 &&
                clip.SourceSha256 == old.SourceSha256 && clip.DecodedSha256 == old.DecodedSha256,
                name + " typed source identity unchanged");
            Check(((SourceAnimationClip)clip).ConverterReplay == null, name + " no old clip gains a replay");
            for (int i = 0; i < clip.SelectedSourceFrames.Count; i++)
            {
                int index = clip.SelectedSourceFrames[i];
                Check(index == old.SelectedSourceFrames[i], name + " old selected map");
                SourceAnimationFrame frame = clip.GetFrame(index), previous = old.GetFrame(index);
                Check(frame.AuthoringRoot.X == previous.AuthoringRoot.X &&
                    frame.AuthoringRoot.Y == previous.AuthoringRoot.Y && frame.AuthoringRoot.Z == previous.AuthoringRoot.Z &&
                    frame.ScaledRoot.X == previous.ScaledRoot.X && frame.ScaledRoot.Y == previous.ScaledRoot.Y &&
                    frame.ScaledRoot.Z == previous.ScaledRoot.Z, name + " old typed roots unchanged");
                for (int joint = 0; joint < 15; joint++)
                {
                    SourceRotation q = frame.Pose.Rotations[joint], p = previous.Pose.Rotations[joint];
                    Check(q.X == p.X && q.Y == p.Y && q.Z == p.Z && q.W == p.W, name + " old typed rotations unchanged");
                }
            }
        }
    }

    private static void Bad(Action action, string fragment, string label)
    {
        try { action(); }
        catch (PlayerAssetException error)
        {
            Check(error.Message.Contains(fragment), label + " intended diagnostic: " + error.Message);
            malformedCases++;
            return;
        }
        throw new Exception("ASSERT " + label + " must be rejected by the production loader");
    }

    private static JsonObject ReplayEntry(JsonObject inventory)
    {
        foreach (JsonNode entry in inventory["clips"].AsArray())
            if (entry["runtime_name"].GetValue<string>() == "R_SKIP") return entry.AsObject();
        throw new Exception("ASSERT missing replay entry");
    }

    private static void CheckMalformed(string root)
    {
        byte[] inventoryBytes = File.ReadAllBytes(Path.Combine(root, "export-inventory.json"));
        byte[] clipBytes = File.ReadAllBytes(Path.Combine(root, "clips", "R_SKIP.json"));
        Bad(() => SourceAnimationLibrary.ReadClip(clipBytes, "clips/R_SKIP.json",
            AuthorizedAssetRoot.Sha256(clipBytes)), "explicit inventory qualification", "standalone replay opt-in");
        var mutations = new List<Tuple<string, Action<JsonObject, JsonObject>, string>>();
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("missing inventory opt-in",
            (entry, doc) => entry.Remove("converter_replay"), "explicit inventory qualification"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("missing clip qualification",
            (entry, doc) => doc.Remove("converter_replay"), "qualifications disagree"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("inventory/document replay mismatch",
            (entry, doc) => entry["converter_replay"]["retained_data_offset"] = 2820470, "qualifications disagree"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("changed retained-record metadata",
            (entry, doc) => {
                entry["converter_replay"]["retained_data_offset"] = 2820470;
                doc["converter_replay"]["retained_data_offset"] = 2820470;
            }, "audited pinned R_SKIP omission"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("false shipped claim",
            (entry, doc) => {
                entry["converter_replay"]["shipped_binary_verified"] = true;
                doc["converter_replay"]["shipped_binary_verified"] = true;
            }, "audited pinned R_SKIP omission"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("wrong source reference",
            (entry, doc) => {
                entry["converter_replay"]["source_reference_git_blob_sha1"] = new string('0', 40);
                doc["converter_replay"]["source_reference_git_blob_sha1"] = new string('0', 40);
            }, "audited pinned R_SKIP omission"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("unselected header",
            (entry, doc) => doc["sequence"]["animation_flags"] = 1, "unselected original animation flags"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("unsupported header flags",
            (entry, doc) => doc["sequence"]["animation_flags"] = 0x4002, "unsupported or unselected"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("unselected frame",
            (entry, doc) => doc["frames"][0]["source_frame_flags"] = 0, "selected source/exported ordinal/flags"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("missing required replay field",
            (entry, doc) => doc["converter_replay"].AsObject().Remove("frame_mapping"), "invalid required clip JSON"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("hidden v1 replay",
            (entry, doc) => {
                entry.Remove("converter_replay"); doc["schema"] = "original-wan-clip/v1";
            }, "must not be hidden"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("invented target hand record",
            (entry, doc) => doc["record_layout"].AsArray().Add(doc["record_layout"][5].DeepClone()),
            "exact actual original matrix record set"));
        mutations.Add(Tuple.Create<string, Action<JsonObject, JsonObject>, string>("non-unit emitted rotation",
            (entry, doc) => doc["frames"][0]["rotations_xyzw"][4] = new JsonArray(0, 0, 0, 0), "non-unit quaternion"));
        foreach (Tuple<string, Action<JsonObject, JsonObject>, string> mutation in mutations)
        {
            JsonObject inventory = JsonNode.Parse(inventoryBytes).AsObject();
            JsonObject doc = JsonNode.Parse(clipBytes).AsObject();
            JsonObject entry = ReplayEntry(inventory);
            mutation.Item2(entry, doc);
            byte[] changed = JsonSerializer.SerializeToUtf8Bytes(doc);
            entry["sha256"] = AuthorizedAssetRoot.Sha256(changed);
            Bad(() => SourceAnimationLibrary.Read(JsonSerializer.SerializeToUtf8Bytes(inventory),
                path => path == "clips/R_SKIP.json" ? changed : File.ReadAllBytes(Path.Combine(root, path))),
                mutation.Item3, mutation.Item1);
        }
    }

    private sealed class RawChunk
    {
        internal string Name;
        internal int Number, Flags, Frames;
        internal Dictionary<int, int> Records = new Dictionary<int, int>();
    }

    private static int Big(byte[] data, int p)
    {
        return data[p] << 24 | data[p + 1] << 16 | data[p + 2] << 8 | data[p + 3];
    }

    private static List<RawChunk> RawChunks(byte[] data)
    {
        Check(System.Text.Encoding.ASCII.GetString(data, 0, 4) == "WIFF" &&
            Big(data, 4) == data.Length - 8, "independent raw WIFF length");
        var result = new List<RawChunk>();
        for (int offset = 8; offset < data.Length;)
        {
            int end = offset + 8 + Big(data, offset + 4);
            Check(end <= data.Length, "independent raw chunk bound");
            if (System.Text.Encoding.ASCII.GetString(data, offset, 4) == "3AN ")
            {
                int body = offset + 16;
                string name = System.Text.Encoding.ASCII.GetString(data, body, 20).Split('\0')[0];
                var chunk = new RawChunk { Name = name, Number = Big(data, offset + 8),
                    Flags = BitConverter.ToInt32(data, body + 20) };
                Check(BitConverter.ToInt32(data, body + 24) == 15, "independent raw fifteen limbs");
                for (int p = body + 68;;)
                {
                    int kind = BitConverter.ToInt32(data, p); p += 4;
                    if (kind == 0) { Check(p == end, "independent raw terminator"); break; }
                    int count = BitConverter.ToInt32(data, p); p += 4;
                    Check(chunk.Frames == 0 || chunk.Frames == count, "independent raw record counts");
                    chunk.Frames = count;
                    chunk.Records.Add(kind, p);
                    p += count * (kind == -1 ? 4 : kind == -2 ? 12 : 48);
                    Check(p <= end, "independent raw record bound");
                }
                result.Add(chunk);
            }
            offset = end + (end & 1);
        }
        return result;
    }

    private static double[] Identity()
    {
        return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
    }
    private static double[] T(double x, double y, double z)
    {
        double[] m = Identity(); m[3] = x; m[7] = y; m[11] = z; return m;
    }
    private static double[] Multiply(double[] a, double[] b)
    {
        var result = new double[16];
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
            for (int k = 0; k < 4; k++) result[r * 4 + c] += a[r * 4 + k] * b[k * 4 + c];
        return result;
    }
    private static double[] RawMatrix(byte[] data, RawChunk chunk, int joint, int frame)
    {
        int address = chunk.Records[joint + 1] + frame * 48 + 12;
        double[] m = Identity();
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            m[r * 4 + c] = BitConverter.ToSingle(data, address + (r * 3 + c) * 4);
        return m;
    }
    private static double[] Heading(double angle, double scale, SourceVector3 root)
    {
        double c = Math.Cos(angle) * scale, s = Math.Sin(angle) * scale;
        return new double[] { c, 0, s, root.X, 0, scale, 0, root.Y, -s, 0, c, root.Z, 0, 0, 0, 1 };
    }

    private static void CheckIndependentPoses(SourceAnimationLibrary library, byte[] raw, SourcePlayer player)
    {
        List<RawChunk> chunks = RawChunks(raw);
        RawChunk target = chunks.Find(c => c.Name == "r_skip"), donor = chunks.Find(c => c.Name == "Huffrun2");
        Check(target != null && donor != null && target.Flags == 0x4000 && donor.Flags == 0x4000 &&
            target.Number == 13 && donor.Number == 12 && target.Frames == 70 && donor.Frames == 90,
            "independent raw selected/uncompressed target and retained writer");
        Check(!target.Records.ContainsKey(5) && target.Records.Count == 16 && donor.Records.ContainsKey(5),
            "independent raw target omits hand record; preceding writer defines it");
        var pivots = new SourceVector3[15];
        for (int joint = 0; joint < 15; joint++)
        {
            AssetVector3 p = player.Nodes[joint].BindPivot;
            pivots[joint] = new SourceVector3(p.X, p.Y, -p.Z);
        }
        ISourceAnimationClip clip = library.GetClip("R_SKIP");
        for (int frame = 0; frame < 70; frame++)
        {
            SourceAnimationFrame source = clip.GetFrame(frame);
            for (int profile = 0; profile < 3; profile++)
            {
                double angle = profile == 0 ? 0 : profile == 1 ? Math.PI / 2 : 0.67;
                float scale = profile == 0 ? 1 : profile == 1 ? 1.1f : .93f;
                var heading = new FieldVector((float)Math.Sin(angle), (float)Math.Cos(angle));
                var layer = new SourceAnimationLayer("r_skip_anim", 1,
                    SourceAnimationSample.FromSelectedOrdinals(clip, frame, frame), facingDirection: heading);
                var animation = new ActorAnimationView(frame, ActorAction.Run,
                    AnimationSelectionEvidence.SourceVerified, layer, source.ScaledRoot);
                var actor = new PlayerView(1, TeamId.Home, new FieldVector(), new FieldVector(), 1,
                    facingDirection: heading, role: ActorRole.Receiver, animation: animation, modelScale: scale);
                SourceEvaluatedPose actual = SourceActorPoseSampler.Evaluate(library, pivots, actor, actor, 1);
                var matrices = new double[15][];
                for (int joint = 0; joint < 15; joint++)
                {
                    int parent = player.Nodes[joint].ParentIndex;
                    double[] previous = parent < 0 ? Heading(angle, scale, source.ScaledRoot) : matrices[parent];
                    AssetVector3 pivot = player.Nodes[joint].BindPivot;
                    double[] rotation = RawMatrix(raw, joint == 4 ? donor : target, joint, frame);
                    matrices[joint] = Multiply(Multiply(Multiply(previous, T(pivot.X, pivot.Y, pivot.Z)),
                        rotation), T(-pivot.X, -pivot.Y, -pivot.Z));
                    foreach (AssetVector3 vertex in player.Limbs[joint].Vertices)
                    {
                        double[] m = matrices[joint];
                        double x = m[0] * vertex.X + m[1] * vertex.Y + m[2] * vertex.Z + m[3];
                        double y = m[4] * vertex.X + m[5] * vertex.Y + m[6] * vertex.Z + m[7];
                        double z = -(m[8] * vertex.X + m[9] * vertex.Y + m[10] * vertex.Z + m[11]);
                        SourceVector3 point = SourceAnimationMatrices.TransformVertex(actual.Joints[joint],
                            new SourceVector3(vertex.X - pivot.X, vertex.Y - pivot.Y, -(vertex.Z - pivot.Z)), scale);
                        maximumMatrixError = Math.Max(maximumMatrixError,
                            Math.Max(Math.Abs(point.X - x), Math.Max(Math.Abs(point.Y - y), Math.Abs(point.Z - z))));
                        Near(point.X, x, 0.0002, "independent raw matrix/production actor pose X");
                        Near(point.Y, y, 0.0002, "independent raw matrix/production actor pose Y");
                        Near(point.Z, z, 0.0002, "independent raw matrix/production actor pose Z");
                    }
                }
            }
        }
    }

    private static void CheckOracle(ISourceAnimationClip clip, string path)
    {
        using (var binary = new BinaryReader(File.OpenRead(path)))
        {
            Check(System.Text.Encoding.ASCII.GetString(binary.ReadBytes(8)) == "WANORCL1" &&
                binary.ReadInt32() == 70, "independent original C oracle header");
            for (int frame = 0; frame < 70; frame++)
            {
                Check(binary.ReadInt32() == frame && binary.ReadInt32() == 0x4000, "C original selected frame identity");
                SourceAnimationFrame actual = clip.GetFrame(frame);
                Near(actual.AuthoringRoot.X, binary.ReadSingle(), 0, "C raw root X");
                Near(actual.AuthoringRoot.Y, binary.ReadSingle(), 0, "C raw root Y");
                Near(actual.AuthoringRoot.Z, binary.ReadSingle(), 0, "C raw root Z");
                Near(actual.ScaledRoot.X, binary.ReadSingle(), 0.00002, "C scaled root X");
                Near(actual.ScaledRoot.Y, binary.ReadSingle(), 0.00002, "C scaled root Y");
                Near(actual.ScaledRoot.Z, binary.ReadSingle(), 0.00002, "C scaled root Z");
                for (int joint = 0; joint < 15; joint++)
                {
                    SourceRotation q = actual.Pose.Rotations[joint];
                    Near(q.X, binary.ReadSingle(), 0.000002, "original C quaternion X");
                    Near(q.Y, binary.ReadSingle(), 0.000002, "original C quaternion Y");
                    Near(q.Z, binary.ReadSingle(), 0.000002, "original C quaternion Z");
                    Near(q.W, binary.ReadSingle(), 0.000002, "original C quaternion W");
                }
                for (int joint = 0; joint < 15; joint++)
                    Check(binary.ReadInt32() == (joint == 4 ? 12 : 13), "original C per-frame last writer");
                binary.ReadBytes(60);
            }
            Check(binary.BaseStream.Position == binary.BaseStream.Length, "complete C oracle consumed");
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 8)
                throw new ArgumentException("bundle157, bundle156, rawWAN, C-oracle.bin, model.h, skeleton.h, bundle56, bundle117");
            SourceAnimationLibrary library = Load(args[0]);
            Check(library.SequenceNames.Count == 157 && library.FrameCount == 13490, "complete immutable 157/13490 totals");
            CheckBase(args[0], args[1], library);
            Check(Load(args[6]).SequenceNames.Count == 56 && Load(args[6]).FrameCount == 3731, "old56 loader unchanged");
            Check(Load(args[7]).SequenceNames.Count == 117 && Load(args[7]).FrameCount == 10288, "old117 loader unchanged");
            SourceAnimationClip clip = (SourceAnimationClip)library.GetClip("R_SKIP");
            Check(clip.SourceFlags == 0x4000 && clip.SourceFrameCount == 70 &&
                clip.SelectedSourceFrames.Count == 70 && clip.ConverterReplay != null &&
                clip.ConverterReplay.MissingSourceLimbId == 5 && clip.ConverterReplay.RetainedAuthoringName == "Huffrun2" &&
                clip.ConverterReplay.RetainedDataOffset == 2820422 && !clip.ConverterReplay.ShippedBinaryVerified,
                "typed raw selected/uncompressed flags and honest retained-record provenance");
            for (int ordinal = 0; ordinal < 70; ordinal++)
                Check(clip.SelectedSourceFrames[ordinal] == ordinal, "R_SKIP original source frame map");
            SourceAnimationSample seam = SourceAnimationSampling.AtSelectedOrdinal(clip, 69.5, true);
            Check(seam.SourceFrame == 69 && seam.NextSourceFrame == 0 && seam.Interpolation01 == .5f,
                "production selected-ordinal loop seam; no invented closing frame");
            SourceAnimationSampling.Sample(library, seam);
            CheckMalformed(args[0]);
            CheckOracle(clip, args[3]);
            SourcePlayer player = GeneratedPlayerReader.Read(File.ReadAllText(args[4]), "models/plyr266.h",
                File.ReadAllText(args[5]), "models/skeleton.h",
                new PlayerSymbolSelection("plyr266_limbs", "fbplyr_skel", "model266_limbs", "plyr266_textures"));
            CheckIndependentPoses(library, File.ReadAllBytes(args[2]), player);
            Console.WriteLine(JsonSerializer.Serialize(new {
                schema = "source-rskip-production-library-check/v1",
                clips = library.SequenceNames.Count, selectedFrames = library.FrameCount,
                assertions = assertions, oldSelectedClipsByteIdentical = 156,
                independentRawMatrixFrames = 70, independentPoseProfiles = 3,
                originalCQuaternionComparisons = 1050, maximumMatrixVertexError = maximumMatrixError,
                malformedCases = malformedCases,
                inventorySha256 = library.InventorySha256,
                productionLanguage = "C#7.3", productionFramework = "netstandard2.1",
                UnityOrGameplayAcceptance = false, shippedBinaryVerified = false
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
