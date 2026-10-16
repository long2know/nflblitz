#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blitz.Editor
{
    public static class SourceAnimationChecks
    {
        private static int assertions;

        public static void VerifySynthetic()
        {
            assertions = 0;
            byte[] bytes = Encoding.UTF8.GetBytes(SyntheticClip());
            string hash = AuthorizedAssetRoot.Sha256(bytes);
            string inventory = "{\"schema\":\"original-wan-export-inventory/v1\",\"clips\":[{\"path\":\"own.json\"," +
                "\"runtime_name\":\"OWN\",\"selected_frames\":2,\"sha256\":\"" + hash +
                "\",\"source_first\":1,\"source_frames\":4,\"source_git_blob_sha1\":\"" + new string('a', 40) +
                "\",\"source_last\":3,\"source_path\":\"synthetic/own.wan\"}]}";
            SourceAnimationLibrary library = SourceAnimationLibrary.Read(Encoding.UTF8.GetBytes(inventory), path => bytes);
            ISourceAnimationClip clip = library.GetClip("OWN");
            Check(clip.SelectedSourceFrames.Count == 2 && clip.SelectedSourceFrames[0] == 1 &&
                clip.SelectedSourceFrames[1] == 3 && clip.SourceFrameCount == 4, "selected vs authoring frame mapping");
            Reject(() => clip.GetFrame(0), "unselected frame must not be interpreted as exported ordinal zero");
            Reject(() => library.GetClip("MISSING"), "missing clip fails instead of using a static pose");
            SourceAnimationSample loop = SourceAnimationSampling.AtSelectedOrdinal(clip, 1.5, true);
            Check(loop.SourceFrame == 3 && loop.NextSourceFrame == 1 && loop.Interpolation01 == 0.5f, "selected loop seam");
            SourceAnimationSample end = SourceAnimationSampling.AtSelectedOrdinal(clip, 50.5, false);
            Check(end.SourceFrame == 3 && end.NextSourceFrame == 3 && end.Interpolation01 == 0, "one-shot terminal hold");
            Reject(() => SourceAnimationLibrary.ReadClip(bytes, "own.json", new string('0', 64)), "decoded content pin");
            Reject(() => SourceAnimationSampling.Slerp(new SourceRotation(float.NaN, 0, 0, 1),
                new SourceRotation(0, 0, 0, 1), 0.5f), "nonfinite source rotation");
            Reject(() => SourceAnimationSampling.AtSelectedOrdinal(clip, double.PositiveInfinity, true), "nonfinite source time");
            Reject(() => SourceAnimationSampling.Sample(library, new SourceAnimationSample("OWN", 1, 3, float.NaN)), "nonfinite interpolation");
            SourceRotation identity = new SourceRotation(0, 0, 0, 1);
            RotationNear(SourceAnimationSampling.Slerp(identity, new SourceRotation(0, 0, 0, -1), 0.5f), identity,
                "hemisphere shortest arc");
            float small = 0.02f;
            SourceRotation close = SourceAnimationSampling.Slerp(identity,
                new SourceRotation(0, 0, Mathf.Sin(small), Mathf.Cos(small)), 0.5f);
            RotationNear(close, new SourceRotation(0, 0, Mathf.Sin(small / 2), Mathf.Cos(small / 2)), "near-linear blend renormalization");

            SourceJointPose poseA = clip.GetFrame(1).Pose, poseB = clip.GetFrame(3).Pose;
            SourceJointPose interrupted = SourceAnimationSampling.Blend(poseA, poseB, 0.25f);
            var transition = new SourceAnimationLayer("own_interrupt", 2, new SourceAnimationSample("OWN", 1, 1),
                transitionFromPose: interrupted, transition01: 0);
            RotationNear(SourceAnimationSampling.SampleLayer(library, transition).Rotations[0], interrupted.Rotations[0],
                "interrupted transition captures the actual blended pose, not an old clip frame");
            SourceJointPose nextBlend = SourceAnimationSampling.Blend(interrupted, poseA, 0.5f);
            RotationNear(nextBlend.Rotations[0], new SourceRotation(0, 0, Mathf.Sin(Mathf.PI / 16), Mathf.Cos(Mathf.PI / 16)),
                "interrupted transition midpoint");
            var pivots = new SourceVector3[15];
            for (int i = 0; i < pivots.Length; i++)
                pivots[i] = new SourceVector3(i == 0 ? 0 : i * 0.4f, i * 0.7f, i * 0.2f);
            var root = new SourceVector3(2, 10, 6);
            FieldVector forward = new FieldVector(0, 1), upperFacing = new FieldVector(1, 0);
            SourceEvaluatedPose lower = SourceAnimationMatrices.Evaluate(pivots, poseA, forward, root, 1.25f);
            SourceEvaluatedPose upper = SourceAnimationMatrices.Evaluate(pivots, poseB, upperFacing, root, 1.25f);
            SourceEvaluatedPose combined = SourceAnimationMatrices.Evaluate(pivots, poseA, forward, root, 1.25f,
                poseB, upperFacing, SourceTiming.LowerBodyJoints, SourceTiming.UpperBodyJoints);
            for (int i = 0; i < 15; i++)
            {
                SourceJointTransform expected = (SourceTiming.UpperBodyJoints & (1 << i)) == 0 ? lower.Joints[i] : upper.Joints[i];
                Near(Vector(combined.Joints[i].Position), Vector(expected.Position), "independent two-part matrix output " + i);
                RotationNear(combined.Joints[i].Rotation, expected.Rotation, "independent two-part rotation " + i);
            }
            Near(Vector(combined.Joints[0].Position), new Vector3(2, 10, -6), "resolved root is not scaled or added twice");
            Check(Vector3.Distance(Vector(combined.Joints[1].Position), Vector(lower.Joints[1].Position)) > 0.5f,
                "upper torso must not inherit lower pelvis/heading");

            GameObject hierarchy = null; Mesh mesh = null; Material material = null; Texture2D texture = null;
            try
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("Native synthetic animation checks require the Standard shader.");
                mesh = new Mesh { name = "original synthetic animation triangle", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 } };
                texture = new Texture2D(1, 1); texture.SetPixel(0, 0, Color.white); texture.Apply();
                material = new Material(shader) { mainTexture = texture };
                hierarchy = new GameObject("original synthetic source animation check");
                var joints = new Transform[15]; var parents = new int[15]; var flags = new int[15];
                for (int i = 0; i < joints.Length; i++)
                {
                    parents[i] = SourceAnimationSkeleton.ParentIndices[i];
                    joints[i] = new GameObject("own-joint-" + i).transform;
                    joints[i].SetParent(parents[i] < 0 ? hierarchy.transform : joints[parents[i]], false);
                    joints[i].localPosition = Vector(pivots[i]) - (parents[i] < 0 ? Vector3.zero : Vector(pivots[parents[i]]));
                    joints[i].gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    joints[i].gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                SourcePlayerRig rig = hierarchy.AddComponent<SourcePlayerRig>();
                rig.InitializeBindPose("original-synthetic-animation", joints, parents, flags);
                rig.ApplyContinuousSourcePose(combined);
                for (int i = 0; i < joints.Length; i++)
                foreach (Vector3 vertex in mesh.vertices)
                    Near(joints[i].TransformPoint(vertex),
                        Vector(SourceAnimationMatrices.TransformVertex(combined.Joints[i], Source(vertex), 1.25f)),
                        "native two-part selected hierarchy vertex " + i);
                Check(rig.HasContinuousPose, "native rig explicitly records continuous application");
                rig.ResetBindPose();
                Check(!rig.HasContinuousPose && joints[0].localScale == Vector3.one, "native reset clears prior scale/pose state");
                var adapter = new SourceAnimationPlayer(rig, library);
                PlayerView old = View(10, 1, new FieldVector(0, 0));
                PlayerView current = View(11, 3, new FieldVector(0, 0));
                adapter.Apply(old, current, 0);
                Near(joints[0].position, new Vector3(0, (float)(8 * SourceAnimationLibrary.RootScale), 0), "source root applied inside rig");
                adapter.Apply(old, current, 1);
                PlayerView rewind = View(1, 1, new FieldVector(0, 0));
                adapter.Apply(current, rewind, 0);
                RotationNear(Source(joints[0].rotation), identity, "source-tick rewind discards interpolation history");
                PlayerView teleport = View(12, 1, new FieldVector(80, 0));
                Check(!SourceActorPoseSampler.CanInterpolate(current, teleport), "teleport must not smear a fallen/old pose");
                PlayerView reline = View(12, 1, default(FieldVector), ActorAction.PreSnap);
                adapter.Apply(current, reline, 0);
                RotationNear(Source(joints[0].rotation), identity, "new lineup never interpolates a prior upside-down/contact pose");
                Reject(() => adapter.Apply(default(PlayerView), default(PlayerView), 1), "unspecified actor animation is visible error");
                Near(hierarchy.transform.position, Vector3.zero, "animation never moves the physics placement root");
            }
            finally
            {
                if (hierarchy != null) Object.DestroyImmediate(hierarchy);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (material != null) Object.DestroyImmediate(material);
                if (texture != null) Object.DestroyImmediate(texture);
            }
            Debug.Log("PASS: " + assertions + " native original-synthetic animation assertions: strict clip/map, shortest arc, loop/end, interrupted blend, independent two-part/root/scale, actual rig/reset/rewind/teleport/finiteness.");
        }

        public static void VerifyOriginalFromCommandLine()
        {
            VerifySynthetic();
            int syntheticAssertions = assertions;
            string[] args = Environment.GetCommandLineArgs();
            var clipsRoot = new AuthorizedAssetRoot(SourceAnimationImporter.RequiredArgument(args, "-animationAssetRoot"));
            byte[] inventory = clipsRoot.ReadBytes(SourceAnimationImporter.RequiredArgument(args, "-animationManifest"), 1024 * 1024);
            if (AuthorizedAssetRoot.Sha256(inventory) != SourceAnimationImporter.RequiredArgument(args, "-animationManifestSha256"))
                throw new PlayerAssetException("Native animation oracle manifest content pin mismatch.");
            var documents = new Dictionary<string, ClipRecords>(StringComparer.Ordinal);
            SourceAnimationLibrary library = SourceAnimationLibrary.Read(inventory, path =>
            {
                byte[] data = clipsRoot.ReadBytes(path, 8 * 1024 * 1024);
                documents.Add(Path.GetFileNameWithoutExtension(path), JsonUtility.FromJson<ClipRecords>(Encoding.UTF8.GetString(data)));
                return data;
            });
            var originals = new AuthorizedAssetRoot(SourceAnimationImporter.RequiredArgument(args, "-playerAssetRoot"));
            string requestPath = SourceAnimationImporter.RequiredArgument(args, "-playerAssetRequest");
            PlayerAssetImportRequest request = JsonUtility.FromJson<PlayerAssetImportRequest>(originals.ReadUtf8(requestPath, 65536));
            if (request == null || !request.reuseAuthorizationConfirmed || request.unityX != "PositiveX" ||
                request.unityY != "PositiveY" || request.unityZ != "NegativeZ")
                throw new PlayerAssetException("Native source oracle requires the explicitly selected proven player basis/request.");
            SourcePlayer player = GeneratedPlayerReader.Read(originals.ReadUtf8(request.modelHeader, PlayerImportLimits.SourceBytes),
                request.modelHeader, originals.ReadUtf8(request.skeletonHeader, PlayerImportLimits.SourceBytes), request.skeletonHeader,
                new PlayerSymbolSelection(request.modelLimbArray, request.skeletonArray, request.skeletonLimbArray, request.materialArray));
            SourcePlayerRig prefab = Resources.Load<GameObject>(SourcePlayerProvider.ResourceKey)?.GetComponent<SourcePlayerRig>();
            if (prefab == null) throw new PlayerAssetException("Native original animation oracle requires the imported source player.");
            SourcePlayerRig instance = SourcePlayerRig.InstantiateRequired(prefab, null);
            var proof = new NativeProof { schema = "source-animation-native-proof/v1", inventorySha256 = library.InventorySha256,
                clips = library.SequenceNames.Count, selectedFrames = library.FrameCount,
                frames = new List<NativeFrameProof>(), sourceRevision = SourceAnimationLibrary.SourceRevision };
            var wans = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                var adapter = new SourceAnimationPlayer(instance, library);
                foreach (string name in library.SequenceNames)
                {
                    ISourceAnimationClip clip = library.GetClip(name);
                    byte[] wan;
                    if (!wans.TryGetValue(clip.SourcePath, out wan))
                    {
                        wan = originals.ReadBytes(clip.SourcePath, 64 * 1024 * 1024);
                        if (AuthorizedAssetRoot.Sha256(wan) != clip.SourceSha256 || GitBlob(wan) != clip.SourceGitBlobSha1)
                            throw new PlayerAssetException("Native original WAN byte identity mismatch: " + clip.SourcePath);
                        wans.Add(clip.SourcePath, wan);
                    }
                    int[] ordinals = { 0, clip.SelectedSourceFrames.Count / 2, clip.SelectedSourceFrames.Count - 1 };
                    foreach (int ordinal in ordinals)
                    {
                        int sourceFrame = clip.SelectedSourceFrames[ordinal];
                        SourceAnimationFrame frame = clip.GetFrame(sourceFrame);
                        SourceAnimationSample sample = new SourceAnimationSample(name, sourceFrame, sourceFrame);
                        PlayerView view = OriginalView(sample, frame.ScaledRoot.Y);
                        adapter.Apply(view, view, 1);
                        double[][] matrices = OriginalMatrices(player, RawRotations(wan, documents[name], frame, proof),
                            frame.ScaledRoot.Y, 1, 0);
                        VerifyNativeVertices(instance, player, matrices, null, proof, name, sourceFrame);
                    }
                    SourceAnimationSample loop = SourceAnimationSampling.AtSelectedOrdinal(clip,
                        clip.SelectedSourceFrames.Count - 0.5, true);
                    SourceAnimationFrame last = clip.GetFrame(loop.SourceFrame), first = clip.GetFrame(loop.NextSourceFrame);
                    double[][] rotations = PoseMatrices(ReferenceBlend(last.Pose, first.Pose, 0.5f));
                    float y = (last.ScaledRoot.Y + first.ScaledRoot.Y) * 0.5f;
                    PlayerView seam = OriginalView(loop, y);
                    adapter.Apply(seam, seam, 1);
                    VerifyNativeVertices(instance, player, OriginalMatrices(player, rotations, y, 1, 0), null, proof, name + "/loop", loop.SourceFrame);
                }
                ISourceAnimationClip lower = library.GetClip("QBRUNF"), upper = library.GetClip("THROW_SR");
                SourceAnimationFrame low = lower.GetFrame(lower.SelectedSourceFrames[10]), top = upper.GetFrame(upper.SelectedSourceFrames[20]);
                const float scale = 1.3f;
                float rootY = low.ScaledRoot.Y * scale;
                var primary = new SourceAnimationLayer("qb_run_anim", 1, new SourceAnimationSample("QBRUNF", low.SourceFrame, low.SourceFrame),
                    jointMask: SourceTiming.LowerBodyJoints, facingDirection: new FieldVector(0, 1));
                var secondary = new SourceAnimationLayer("throw_short", 2, new SourceAnimationSample("THROW_SR", top.SourceFrame, top.SourceFrame),
                    jointMask: SourceTiming.UpperBodyJoints, facingDirection: new FieldVector(1, 0));
                var twoPart = new PlayerView(3, TeamId.Home, default(FieldVector), default(FieldVector), 1,
                    facingDirection: new FieldVector(0, 1), role: ActorRole.Quarterback, modelScale: scale,
                    animation: new ActorAnimationView(100, ActorAction.Throw, AnimationSelectionEvidence.Approximate,
                        primary, new SourceVector3(0, rootY, 0), secondary));
                adapter.Apply(twoPart, twoPart, 1);
                double[][] lowerMatrices = OriginalMatrices(player, RawRotations(wans[lower.SourcePath], documents["QBRUNF"], low, proof), rootY, scale, 0);
                double[][] upperMatrices = OriginalMatrices(player, RawRotations(wans[upper.SourcePath], documents["THROW_SR"], top, proof), rootY, scale, Math.PI / 2);
                VerifyNativeVertices(instance, player, lowerMatrices, upperMatrices, proof, "independent-two-part-scaled", low.SourceFrame);
                SourceAnimationFrame fromFrame = lower.GetFrame(lower.SelectedSourceFrames[5]);
                SourceAnimationFrame toFrame = lower.GetFrame(lower.SelectedSourceFrames[6]);
                const float renderAmount = 0.375f;
                PlayerView before = OriginalView(new SourceAnimationSample(lower.SequenceName, fromFrame.SourceFrame, fromFrame.SourceFrame),
                    fromFrame.ScaledRoot.Y, 100);
                PlayerView after = OriginalView(new SourceAnimationSample(lower.SequenceName, toFrame.SourceFrame, toFrame.SourceFrame),
                    toFrame.ScaledRoot.Y, 101);
                adapter.Apply(before, after, renderAmount);
                float renderRoot = fromFrame.ScaledRoot.Y + (toFrame.ScaledRoot.Y - fromFrame.ScaledRoot.Y) * renderAmount;
                VerifyNativeVertices(instance, player, OriginalMatrices(player,
                    PoseMatrices(ReferenceBlend(fromFrame.Pose, toFrame.Pose, renderAmount)), renderRoot, 1, 0),
                    null, proof, "source-snapshot-render-interpolation", toFrame.SourceFrame);
                SourceJointPose captured = ReferenceBlend(fromFrame.Pose, toFrame.Pose, 0.35f);
                const float interruptedAmount = 0.6f, interruptedRoot = 11.25f;
                PlayerView interrupted = OriginalView(new SourceAnimationSample(upper.SequenceName, top.SourceFrame, top.SourceFrame),
                    interruptedRoot, 102, captured, interruptedAmount);
                adapter.Apply(interrupted, interrupted, 1);
                VerifyNativeVertices(instance, player, OriginalMatrices(player,
                    PoseMatrices(ReferenceBlend(captured, top.Pose, interruptedAmount)), interruptedRoot, 1, 0),
                    null, proof, "interrupted-actual-pose-capture", top.SourceFrame);
                Check(proof.maxOriginalMatrixQuaternionError < 0.00001f, "all original matrices agree with decoded source quaternions");
                Check(proof.maxNativeVertexError < 0.0001f && proof.verifiedVertices == 343 * (library.SequenceNames.Count * 4 + 3),
                    "every first/middle/last/loop/two-part native 343-vertex sample verified");
                proof.syntheticAssertions = syntheticAssertions;
                string relative = SourceAnimationImporter.RequiredArgument(args, "-animationProofPath");
                var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
                File.WriteAllText(output.NewPath(relative), JsonUtility.ToJson(proof, true) + "\n", new UTF8Encoding(false));
                Debug.Log("PASS: original continuous native animation oracle: " + proof.clips + " clips / " + proof.selectedFrames +
                    " selected frames / " + proof.frames.Count + " first-middle-last-loop/two-part samples / " + proof.verifiedVertices +
                    " actual mesh vertices; max original-matrix/quaternion error=" + proof.maxOriginalMatrixQuaternionError +
                    "; max actual native vertex error=" + proof.maxNativeVertexError + ". Proof: " + relative);
            }
            finally { Object.DestroyImmediate(instance.gameObject); }
        }

        private static double[][] RawRotations(byte[] wan, ClipRecords records, SourceAnimationFrame frame, NativeProof proof)
        {
            var result = new double[15][];
            for (int i = 0; i < result.Length; i++)
            {
                if (records.record_layout[i + 2].id != i + 1) throw new PlayerAssetException("Native source record ordering changed.");
                int offset = records.record_layout[i + 2].data_offset + frame.SourceFrame * 48 + 12;
                double[] raw = Identity();
                double[] q = RotationMatrix(frame.Pose.Rotations[i]);
                for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    raw[r * 4 + c] = BitConverter.ToSingle(wan, offset + (r * 3 + c) * 4);
                    proof.maxOriginalMatrixQuaternionError = Mathf.Max(proof.maxOriginalMatrixQuaternionError,
                        (float)Math.Abs(raw[r * 4 + c] - q[r * 4 + c]));
                }
                result[i] = raw;
            }
            return result;
        }

        private static double[][] OriginalMatrices(SourcePlayer player, double[][] rotations, float rootY, float scale, double sourceYaw)
        {
            double[] basis = Identity();
            double sin = Math.Sin(sourceYaw), cos = Math.Cos(sourceYaw);
            basis[0] = cos * scale; basis[2] = sin * scale; basis[5] = scale;
            basis[8] = -sin * scale; basis[10] = cos * scale; basis[7] = rootY;
            var matrices = new double[15][];
            for (int i = 0; i < matrices.Length; i++)
            {
                SourceRigNode node = player.Nodes[i]; AssetVector3 p = node.BindPivot;
                double[] parent = node.ParentIndex < 0 ? basis : matrices[node.ParentIndex];
                matrices[i] = Multiply(Multiply(Multiply(parent, Translation(p.X, p.Y, p.Z)), rotations[i]), Translation(-p.X, -p.Y, -p.Z));
            }
            return matrices;
        }

        private static void VerifyNativeVertices(SourcePlayerRig rig, SourcePlayer player, double[][] lower, double[][] upper,
            NativeProof proof, string sequence, int sourceFrame)
        {
            var item = new NativeFrameProof { sequence = sequence, sourceFrame = sourceFrame };
            for (int i = 0; i < rig.JointCount; i++)
            {
                Transform joint = rig.GetJoint(i);
                double[] matrix = upper != null && (SourceTiming.UpperBodyJoints & (1 << i)) != 0 ? upper[i] : lower[i];
                AssetVector3 pivot = player.Nodes[i].BindPivot;
                foreach (Vector3 vertex in joint.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    double x = vertex.x + pivot.X, y = vertex.y + pivot.Y, z = -vertex.z + pivot.Z;
                    var expected = new Vector3((float)(matrix[0] * x + matrix[1] * y + matrix[2] * z + matrix[3]),
                        (float)(matrix[4] * x + matrix[5] * y + matrix[6] * z + matrix[7]),
                        (float)-(matrix[8] * x + matrix[9] * y + matrix[10] * z + matrix[11]));
                    Vector3 actual = rig.transform.InverseTransformPoint(joint.TransformPoint(vertex));
                    float error = Vector3.Distance(expected, actual);
                    if (error > 0.0001f || float.IsNaN(error))
                        throw new PlayerAssetException("Original animated matrix/native vertex mismatch: " + sequence +
                            " authoringFrame=" + sourceFrame + " joint=" + i + " error=" + error);
                    item.maxVertexError = Mathf.Max(item.maxVertexError, error);
                    item.vertices++;
                }
            }
            proof.maxNativeVertexError = Mathf.Max(proof.maxNativeVertexError, item.maxVertexError);
            proof.verifiedVertices += item.vertices; proof.frames.Add(item);
        }

        private static PlayerView View(long tick, int sourceFrame, FieldVector position, ActorAction action = ActorAction.Idle)
        {
            return new PlayerView(3, TeamId.Home, position, default(FieldVector), 1, height: 3,
                facingDirection: new FieldVector(0, 1), role: ActorRole.Quarterback,
                animation: new ActorAnimationView(tick, action, AnimationSelectionEvidence.Approximate,
                    new SourceAnimationLayer("own", 1, new SourceAnimationSample("OWN", sourceFrame, sourceFrame)),
                    new SourceVector3(0, (float)(8 * SourceAnimationLibrary.RootScale), 0)));
        }
        private static PlayerView OriginalView(SourceAnimationSample sample, float rootY, long sourceTick = 100,
            SourceJointPose transitionFrom = null, float transition01 = 1)
        {
            return new PlayerView(3, TeamId.Home, default(FieldVector), default(FieldVector), 1,
                facingDirection: new FieldVector(0, 1), role: ActorRole.Quarterback,
                animation: new ActorAnimationView(sourceTick, ActorAction.Idle, AnimationSelectionEvidence.Approximate,
                    new SourceAnimationLayer("native_oracle_sample", 1, sample,
                        transitionFromPose: transitionFrom, transition01: transition01), new SourceVector3(0, rootY, 0)));
        }
        private static SourceJointPose ReferenceBlend(SourceJointPose from, SourceJointPose to, float amount)
        {
            var rotations = new SourceRotation[15];
            for (int i = 0; i < rotations.Length; i++)
            {
                SourceRotation a = from.Rotations[i], b = to.Rotations[i];
                double an = Math.Sqrt((double)a.X*a.X + (double)a.Y*a.Y + (double)a.Z*a.Z + (double)a.W*a.W);
                double bn = Math.Sqrt((double)b.X*b.X + (double)b.Y*b.Y + (double)b.Z*b.Z + (double)b.W*b.W);
                double ax = a.X/an, ay = a.Y/an, az = a.Z/an, aw = a.W/an;
                double bx = b.X/bn, by = b.Y/bn, bz = b.Z/bn, bw = b.W/bn;
                if (ax*bx + ay*by + az*bz + aw*bw < 0) { bx = -bx; by = -by; bz = -bz; bw = -bw; }
                // Independent geodesic reference: a * exp(t * log(inverse(a) * b)).
                double x = aw*bx - ax*bw - ay*bz + az*by;
                double y = aw*by + ax*bz - ay*bw - az*bx;
                double z = aw*bz - ax*by + ay*bx - az*bw;
                double w = aw*bw + ax*bx + ay*by + az*bz;
                double length = Math.Sqrt(x*x + y*y + z*z);
                double angle = Math.Atan2(length, w) * amount;
                double factor = length < 1e-14 ? amount : Math.Sin(angle) / length;
                x *= factor; y *= factor; z *= factor; w = Math.Cos(angle);
                rotations[i] = new SourceRotation((float)(aw*x + ax*w + ay*z - az*y),
                    (float)(aw*y - ax*z + ay*w + az*x), (float)(aw*z + ax*y - ay*x + az*w),
                    (float)(aw*w - ax*x - ay*y - az*z));
            }
            return new SourceJointPose(rotations);
        }
        private static double[][] PoseMatrices(SourceJointPose pose)
        {
            var rotations = new double[15][];
            for (int i = 0; i < rotations.Length; i++) rotations[i] = RotationMatrix(pose.Rotations[i]);
            return rotations;
        }
        private static string SyntheticClip()
        {
            var joints = new StringBuilder(); var frames = new StringBuilder(); var records = new StringBuilder();
            int offset = 16;
            for (int record = 0; record < 17; record++)
            {
                if (record > 0) records.Append(',');
                int stride = record == 0 ? 4 : record == 1 ? 12 : 48, id = record == 0 ? -1 : record == 1 ? -2 : record - 1;
                records.Append("{\"id\":" + id + ",\"count\":4,\"stride\":" + stride +
                    ",\"record_offset\":" + offset + ",\"data_offset\":" + (offset + 8) + "}");
                offset += 8 + 4 * stride;
            }
            for (int i = 0; i < 15; i++)
            {
                if (i > 0) joints.Append(',');
                joints.Append("{\"name\":\"" + SourceAnimationSkeleton.JointNames[i] + "\",\"fbplyr_skel_index\":" + i +
                    ",\"source_limb_id\":" + (i + 1) + ",\"parent_index\":" +
                    (i == 0 ? "null" : SourceAnimationSkeleton.ParentIndices[i].ToString(CultureInfo.InvariantCulture)) + "}");
            }
            for (int frame = 0; frame < 2; frame++)
            {
                if (frame > 0) frames.Append(',');
                frames.Append("{\"exported_frame_index\":" + frame + ",\"source_frame_index\":" + (frame * 2 + 1) +
                    ",\"source_frame_flags\":16384,\"root_translation\":{\"authoring_xyz\":[0,8,0],\"scaled_runtime_xyz\":[0," +
                    (8 * SourceAnimationLibrary.RootScale).ToString("R", CultureInfo.InvariantCulture) + ",0]},\"rotations_xyzw\":[");
                for (int i = 0; i < 15; i++)
                {
                    if (i > 0) frames.Append(',');
                    frames.Append(frame == 1 && i == 0 ? "[0,0,1,0]" : "[0,0,0,1]");
                }
                frames.Append("]}");
            }
            return "{\"schema\":\"original-wan-clip/v1\",\"provenance\":{\"repository\":\"historicalsource/nflblitz2000\",\"commit\":\"" +
                SourceAnimationLibrary.SourceRevision + "\",\"path\":\"synthetic/own.wan\",\"git_blob_sha1\":\"" + new string('a', 40) +
                "\",\"sha256\":\"" + new string('b', 64) + "\",\"content_pin_verified\":true,\"byte_length\":" + (offset + 4) +
                ",\"format_source\":{\"repository\":\"historicalsource/midwayvideo\",\"commit\":\"" + SourceAnimationLibrary.ConverterRevision +
                "\",\"path\":\"tools/wan2seq/wan2seq.c\",\"git_blob_sha1\":\"33c7e489d9b7a40d3b0cd435236db8c357d143fe\",\"matrix_conversion_lines\":[585,631]}}," +
                "\"format\":{\"container\":\"WIFF\",\"chunk\":\"3AN \",\"container_endianness\":\"big\",\"animation_body_endianness\":\"little\"," +
                "\"source_float\":\"IEEE-754 binary32\",\"source_long_bytes\":4,\"chunk_count\":1}," +
                "\"sequence\":{\"runtime_name\":\"OWN\",\"authoring_name\":\"own.bip\",\"animation_flags\":16385,\"limb_count\":15," +
                "\"source_frame_count\":4,\"selected_frame_count\":2,\"selected_source_frame_indices\":[1,3],\"chunk_number\":0," +
                "\"chunk_byte_offset\":0,\"chunk_payload_bytes\":" + (offset - 4) + ",\"chunk_end_byte_offset\":" + (offset + 4) + "}," +
                "\"joints\":[" + joints + "],\"root_translation\":{\"runtime_scale\":0.12898,\"space\":" +
                "\"source model XYZ before player placement/facing and animation-mode-specific anchoring\"}," +
                "\"frames\":[" + frames + "],\"record_layout\":[" + records + "],\"validation\":{\"validated_source_frames\":4," +
                "\"validated_limb_frames\":60,\"matrix_error_limit\":0.0001,\"max_matrix_determinant_error\":0," +
                "\"max_matrix_orthogonality_error\":0,\"max_quaternion_norm_error\":0,\"min_adjacent_source_quaternion_dot\":0}}";
        }
        private static string GitBlob(byte[] bytes)
        {
            byte[] header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
            using (SHA1 sha = SHA1.Create())
            {
                byte[] content = new byte[header.Length + bytes.Length];
                Buffer.BlockCopy(header, 0, content, 0, header.Length); Buffer.BlockCopy(bytes, 0, content, header.Length, bytes.Length);
                return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
            }
        }
        private static double[] Identity() { return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }; }
        private static double[] Translation(double x, double y, double z)
        {
            double[] value = Identity(); value[3] = x; value[7] = y; value[11] = z; return value;
        }
        private static double[] Multiply(double[] a, double[] b)
        {
            var value = new double[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) for (int k = 0; k < 4; k++) value[r * 4 + c] += a[r * 4 + k] * b[k * 4 + c];
            return value;
        }
        private static double[] RotationMatrix(SourceRotation q)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            return new[] { 1 - 2 * (y*y+z*z), 2 * (x*y-z*w), 2 * (x*z+y*w), 0,
                2 * (x*y+z*w), 1 - 2 * (x*x+z*z), 2 * (y*z-x*w), 0,
                2 * (x*z-y*w), 2 * (y*z+x*w), 1 - 2 * (x*x+y*y), 0, 0, 0, 0, 1d };
        }
        private static Vector3 Vector(SourceVector3 value) { return new Vector3(value.X, value.Y, value.Z); }
        private static SourceVector3 Source(Vector3 value) { return new SourceVector3(value.x, value.y, value.z); }
        private static SourceRotation Source(Quaternion value) { return new SourceRotation(value.x, value.y, value.z, value.w); }
        private static void Near(Vector3 a, Vector3 b, string label) { Check(Vector3.Distance(a, b) < 0.0001f, label + ": " + a + " != " + b); }
        private static void RotationNear(SourceRotation a, SourceRotation b, string label)
        {
            double dot = (double)a.X*b.X + (double)a.Y*b.Y + (double)a.Z*b.Z + (double)a.W*b.W;
            Check(Math.Abs(Math.Abs(dot) - 1) < 0.00001, label);
        }
        private static void Reject(Action action, string label)
        {
            try { action(); } catch (PlayerAssetException) { Check(true, label); return; }
            throw new InvalidOperationException("Native animation expected rejection: " + label);
        }
        private static void Check(bool condition, string label)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Native animation assertion failed: " + label);
        }
        [Serializable] private sealed class ClipRecords { public RawRecord[] record_layout = null; }
        [Serializable] private sealed class RawRecord { public int id = 0, data_offset = 0; }
        [Serializable] private sealed class NativeFrameProof { public string sequence; public int sourceFrame, vertices; public float maxVertexError; }
        [Serializable] private sealed class NativeProof
        {
            public string schema, inventorySha256, sourceRevision;
            public string loopBoundaryScope = "Numeric last-to-first shortest-arc matrix diagnostics only; gameplay scripts, not clips, decide which actions loop.";
            public int clips, selectedFrames, verifiedVertices, syntheticAssertions;
            public float maxOriginalMatrixQuaternionError, maxNativeVertexError;
            public List<NativeFrameProof> frames;
        }
    }
}
#endif
