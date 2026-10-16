#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Blitz.AssetImport;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Blitz.Editor
{
    [Serializable]
    public sealed class PlayerTextureMapEntry
    {
        public string slot;
        public string texturePath;
        public float sourceSUnits;
        public float sourceTUnits;
        public bool flipV;
        public float alphaCutoff;
    }

    [Serializable]
    public sealed class PlayerAssetImportRequest
    {
        public int schemaVersion;
        public bool reuseAuthorizationConfirmed;
        public string authorizationReference;
        public string stableId;
        public string modelHeader;
        public string skeletonHeader;
        public string modelLimbArray;
        public string skeletonArray;
        public string skeletonLimbArray;
        public string materialArray;
        public string unityX;
        public string unityY;
        public string unityZ;
        public string sourceTriangleOrder;
        public PlayerTextureMapEntry[] materialMap;
    }

    public static class PlayerAssetImporter
    {
        [MenuItem("Tools/Source Assets/Import Explicitly Authorized Bind Pose")]
        public static void ImportFromSelectedRequest()
        {
            string root = EditorUtility.OpenFolderPanel("Select explicitly authorized asset root (nothing is downloaded)", "", "");
            if (string.IsNullOrEmpty(root)) return;
            var guard = new AuthorizedAssetRoot(root);
            string requestFile = EditorUtility.OpenFilePanel("Select import request JSON inside that root", root, "json");
            if (string.IsNullOrEmpty(requestFile)) return;
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!requestFile.StartsWith(prefix, StringComparison.Ordinal))
                throw new PlayerAssetException("Import request must be inside the explicitly selected authorized root.");
            string relative = requestFile.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/');
            PlayerAssetImportRequest request = ReadRequest(guard, relative);
            if (!request.reuseAuthorizationConfirmed ||
                !EditorUtility.DisplayDialog("Authorized assets only",
                    "Continue only if you have permission to convert, use, and bundle every supplied mesh, skeleton, and texture. Public availability is not permission. This produces a bind-pose-only prefab, not original animation.",
                    "I confirm authorized reuse", "Cancel"))
                throw new PlayerAssetException("Asset reuse authorization was not confirmed; import cancelled.");
            SourcePlayerRig prefab = ImportBindPose(root, request);
            Selection.activeObject = prefab.gameObject;
        }

        // Explicit invocation only; there are no InitializeOnLoad hooks, source-cache fallbacks, or network requests.
        public static void ImportAuthorizedBindPoseFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string root = RequiredArgument(args, "-playerAssetRoot");
            string relativeRequest = RequiredArgument(args, "-playerAssetRequest");
            PlayerAssetImportRequest request = ReadRequest(new AuthorizedAssetRoot(root), relativeRequest);
            ImportBindPose(root, request);
        }

        private static PlayerImportPlan CreatePlan(string explicitlyAuthorizedRoot, PlayerAssetImportRequest request)
        {
            if (request == null || request.schemaVersion != 1) throw new PlayerAssetException("Expected player import request schemaVersion 1.");
            if (!request.reuseAuthorizationConfirmed)
                throw new PlayerAssetException("Asset reuse authorization is not confirmed; no source inputs were read.");
            var symbols = new PlayerSymbolSelection(request.modelLimbArray, request.skeletonArray,
                request.skeletonLimbArray, request.materialArray);
            var conversion = new PlayerCoordinateConversion(ParseEnum<SignedSourceAxis>(request.unityX, "unityX"),
                ParseEnum<SignedSourceAxis>(request.unityY, "unityY"), ParseEnum<SignedSourceAxis>(request.unityZ, "unityZ"),
                ParseEnum<SourceTriangleOrder>(request.sourceTriangleOrder, "sourceTriangleOrder"));
            if (request.materialMap == null) throw new PlayerAssetException("Explicit materialMap is required.");
            var map = new List<PlayerMaterialBinding>();
            foreach (PlayerTextureMapEntry entry in request.materialMap)
            {
                if (entry == null) throw new PlayerAssetException("Null materialMap entry.");
                map.Add(new PlayerMaterialBinding(entry.slot, entry.texturePath, entry.sourceSUnits, entry.sourceTUnits, entry.flipV, entry.alphaCutoff));
            }
            PlayerImportPlan plan = PlayerImportPlan.Create(explicitlyAuthorizedRoot, request.reuseAuthorizationConfirmed,
                request.authorizationReference, request.stableId, request.modelHeader, request.skeletonHeader, symbols, map, conversion);
            return plan;
        }

        public static SourcePlayerRig ImportBindPose(string explicitlyAuthorizedRoot, PlayerAssetImportRequest request)
        {
            PlayerImportPlan plan = CreatePlan(explicitlyAuthorizedRoot, request);
            if (plan.Player.Nodes.Any(n => (n.Flags & 1) != 0))
                throw new PlayerAssetException("SN_DBLSIDE needs an explicitly verified two-sided shader adapter; bind-pose-v1 will not silently ignore it.");
            Shader shader = Shader.Find("Standard");
            if (shader == null) throw new PlayerAssetException("Required built-in Standard shader is unavailable; no material/texture fallback is used.");
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            var destinationGuard = new AuthorizedAssetRoot(projectRoot);
            string destination = "Assets/Imported/" + plan.StableId;
            destinationGuard.NewPath(destination);
            EnsureFolder(destinationGuard, "Assets/Imported", "Assets", "Imported");
            EnsureFolder(destinationGuard, destination, "Assets/Imported", plan.StableId);
            EnsureFolder(destinationGuard, destination + "/Resources", destination, "Resources");
            EnsureFolder(destinationGuard, destination + "/Resources/BlitzSourcePlayers", destination + "/Resources", "BlitzSourcePlayers");
            string resourceFolder = destination + "/Resources/BlitzSourcePlayers/" + plan.StableId;
            EnsureFolder(destinationGuard, resourceFolder, destination + "/Resources/BlitzSourcePlayers", plan.StableId);

            var materials = new Material[plan.Textures.Count];
            var meshes = new Mesh[plan.Meshes.Count];
            GameObject hierarchy = null;
            try
            {
                // Failures leave only this newly reserved output for inspection, never delete/overwrite user assets.
                WriteNew(destinationGuard, destination + "/started.json",
                    Encoding.UTF8.GetBytes("{\"stage\":\"incomplete-until-Resources-prefab-and-provenance.json-exist\"}\n"));
                for (int i = 0; i < plan.Textures.Count; i++)
                {
                    AuthorizedPlayerTexture source = plan.Textures[i];
                    string texturePath = destination + "/texture-" + i.ToString("D3") + ".tga";
                    WriteNew(destinationGuard, texturePath, source.CopyBytes());
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if (importer == null) throw new PlayerAssetException("Unity did not recognize TGA '" + source.Binding.TexturePath + "'.");
                    importer.textureType = TextureImporterType.Default;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = Math.Max(32, Math.Max(source.Info.Width, source.Info.Height));
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = false;
                    importer.mipmapEnabled = false;
                    importer.isReadable = false;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (texture == null || texture.width != source.Info.Width || texture.height != source.Info.Height)
                        throw new PlayerAssetException("Unity texture dimensions changed for '" + source.Binding.TexturePath + "'.");
                    var material = new Material(shader) { name = source.Binding.Slot, mainTexture = texture, color = Color.white };
                    material.SetFloat("_Glossiness", 0f);
                    if (source.Binding.AlphaCutoff > 0)
                    {
                        material.SetFloat("_Mode", 1f);
                        material.SetFloat("_Cutoff", source.Binding.AlphaCutoff);
                        material.SetOverrideTag("RenderType", "TransparentCutout");
                        material.EnableKeyword("_ALPHATEST_ON");
                        material.DisableKeyword("_ALPHABLEND_ON");
                        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.SetInt("_SrcBlend", (int)BlendMode.One);
                        material.SetInt("_DstBlend", (int)BlendMode.Zero);
                        material.SetInt("_ZWrite", 1);
                        material.renderQueue = (int)RenderQueue.AlphaTest;
                    }
                    string materialPath = destination + "/material-" + i.ToString("D3") + ".asset";
                    destinationGuard.NewPath(materialPath);
                    AssetDatabase.CreateAsset(material, materialPath);
                    materials[i] = material;
                }
                for (int i = 0; i < meshes.Length; i++)
                {
                    meshes[i] = CreateUnityMesh(plan.Meshes[i]);
                    string meshPath = destination + "/limb-" + i.ToString("D3") + ".asset";
                    destinationGuard.NewPath(meshPath);
                    AssetDatabase.CreateAsset(meshes[i], meshPath);
                }
                hierarchy = CreateHierarchy(plan.StableId, plan.Player, plan.Meshes, meshes, materials);
                string prefabPath = resourceFolder + "/player.prefab";
                destinationGuard.NewPath(prefabPath);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(hierarchy, prefabPath);
                if (prefab == null) throw new PlayerAssetException("Unity failed to persist the generated bind-pose prefab.");
                SourcePlayerRig rig = prefab.GetComponent<SourcePlayerRig>();
                if (rig == null) throw new PlayerAssetException("Generated prefab lost its source-rig component.");
                rig.ValidateGeneratedRig();
                WriteNew(destinationGuard, destination + "/provenance.json", Encoding.UTF8.GetBytes(plan.ProvenanceJson()));
                AssetDatabase.ImportAsset(destination + "/provenance.json", ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();
                Debug.Log("Authorized source bind-pose prefab created: " + prefabPath +
                    ". Source model units retained; no WAN/ANI animation, uniform-remapping, gameplay-scale, or lighting-parity claim.");
                return rig;
            }
            finally
            {
                if (hierarchy != null) Object.DestroyImmediate(hierarchy);
                foreach (Mesh mesh in meshes)
                    if (mesh != null && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
                foreach (Material material in materials)
                    if (material != null && !AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
            }
        }

        public static void VerifySyntheticAdapter()
        {
            const string model = "VERTEX v[3]={{0,0,0},{2,0,0},{0,2,0}}; ST s[]={{0,0},{2,0},{0,2}};" +
                "TRI t[]={{0,3,6,0,1,2,0},{0,0,0,0,0,0,-20}}; LIMB a={3,1,NULL,v,s,t};" +
                "LIMB *parts[]={&a,NULL}; char *slots[]={\"original-test.wms\",NULL};";
            const string skeleton = "SNODE rig[]={{1,0,0,0,{0,0,0}}}; LIMB *order[]={&a};";
            SourcePlayer player = GeneratedPlayerReader.Read(model, "synthetic-model.h", skeleton, "synthetic-rig.h",
                new PlayerSymbolSelection("parts", "rig", "order", "slots"));
            var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
                SignedSourceAxis.PositiveZ, SourceTriangleOrder.Declared123);
            var data = BindPoseMeshBuilder.Build(player,
                new[] { new PlayerMaterialBinding("original-test.wms", "original-test.tga", 2, 2, false) }, conversion);
            Shader shader = Shader.Find("Standard");
            if (shader == null) throw new PlayerAssetException("Native synthetic check requires built-in Standard shader.");
            Mesh mesh = null; Material material = null; Texture2D texture = null; GameObject hierarchy = null;
            SourcePlayerRig instance = null;
            try
            {
                mesh = CreateUnityMesh(data[0]);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
                material = new Material(shader) { mainTexture = texture };
                hierarchy = CreateHierarchy("synthetic-adapter-check", player, data, new[] { mesh }, new[] { material });
                instance = SourcePlayerRig.InstantiateRequired(hierarchy.GetComponent<SourcePlayerRig>(), null);
                if (mesh.vertexCount != 3 || mesh.triangles.Length != 3 || mesh.uv[1] != new Vector2(1, 0) ||
                    mesh.normals[0] != Vector3.forward || instance.JointCount != 1 ||
                    instance.GetJoint(0).localPosition != Vector3.right ||
                    instance.GetJoint(0).TransformPoint(mesh.vertices[0]) != Vector3.zero ||
                    instance.GetJoint(0).GetComponent<MeshRenderer>().sharedMaterial.mainTexture != texture)
                    throw new PlayerAssetException("Native synthetic mesh/UV/normal/pivot/material/prefab factory assertion failed.");
                instance.SetUnityLocalRotation(0, Quaternion.Euler(0, 0, 90));
                Vector3 moved = instance.GetJoint(0).TransformPoint(mesh.vertices[0]);
                if (Vector3.Distance(moved, new Vector3(1, -1, 0)) > 0.0001f)
                    throw new PlayerAssetException("Native synthetic joint pivot-rotation assertion failed.");
                instance.ResetBindPose();
                if (instance.GetJoint(0).localRotation != Quaternion.identity)
                    throw new PlayerAssetException("Native synthetic bind-pose reset assertion failed.");
                bool missingRejected = false;
                try { SourcePlayerRig.InstantiateRequired(null, null); }
                catch (InvalidOperationException e) { missingRejected = e.Message.Contains("no primitive actor"); }
                if (!missingRejected) throw new PlayerAssetException("Missing source-prefab mode was not explicitly rejected.");
                Debug.Log("PASS: original synthetic native mesh/UV/normal/pivot/material/hierarchy/rotation/reset/missing-prefab checks; no asset files imported.");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance.gameObject);
                if (hierarchy != null) Object.DestroyImmediate(hierarchy);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (material != null) Object.DestroyImmediate(material);
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        [Serializable]
        private sealed class StaticPoseProof
        {
            public string stage, sequence, poseSha256, sourceWan, sourceWanGitBlob, sourceWanSha256;
            public string localQuaternionSha256, prefabPath, rootPolicy;
            public int sourceFrame, exportedFrame, joints, verifiedVertices;
            public float maxSourceMatrixVertexError;
            public Vector3 anchoredRootOffset, headCentroid, rightFootCentroid, leftFootCentroid;
        }

        // Deliberate narrow update of our generated prefab only; mesh/material/UV inputs are fingerprint-protected.
        public static void ApplyAuthorizedStaticPoseFromCommandLine()
        {
            StaticPoseCommand(true);
        }

        public static void VerifyAuthorizedStaticPoseFromCommandLine()
        {
            StaticPoseCommand(false);
        }

        private static void StaticPoseCommand(bool apply)
        {
            string[] args = Environment.GetCommandLineArgs();
            string rootPath = RequiredArgument(args, "-playerAssetRoot");
            var inputs = new AuthorizedAssetRoot(rootPath);
            PlayerAssetImportRequest request = ReadRequest(inputs, RequiredArgument(args, "-playerAssetRequest"));
            PlayerImportPlan plan = CreatePlan(rootPath, request);
            string poseRelative = RequiredArgument(args, "-playerPose");
            if (!poseRelative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new PlayerAssetException("Decoded source pose must be explicit root-relative JSON.");
            byte[] bytes = inputs.ReadBytes(poseRelative, 256 * 1024);
            SourcePlayerPose pose = SourcePlayerPoseReader.Read(bytes, poseRelative, RequiredArgument(args, "-playerPoseSha256"), plan.Player);
            var outputs = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            string destination = "Assets/Imported/" + plan.StableId;
            outputs.ExistingDirectory(destination);
            if (outputs.ReadUtf8(destination + "/provenance.json", 1024 * 1024) != plan.ProvenanceJson())
                throw new PlayerAssetException("Existing generated geometry/material provenance differs from this request; refusing a pose-only update.");
            var protectedFiles = Directory.GetFiles(outputs.ExistingDirectory(destination), "*", SearchOption.TopDirectoryOnly)
                .Where(p => Path.GetFileName(p).StartsWith("limb-", StringComparison.Ordinal) ||
                    Path.GetFileName(p).StartsWith("material-", StringComparison.Ordinal) ||
                    Path.GetFileName(p).StartsWith("texture-", StringComparison.Ordinal) ||
                    Path.GetFileName(p) == "provenance.json" || Path.GetFileName(p) == "source-archive-provenance.json")
                .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).ToArray();
            string[] before = protectedFiles.Select(p => AuthorizedAssetRoot.Sha256(File.ReadAllBytes(p))).ToArray();
            string prefabPath = destination + "/Resources/BlitzSourcePlayers/" + plan.StableId + "/player.prefab";
            outputs.ExistingFile(prefabPath);
            GameObject loaded = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                SourcePlayerRig rig = loaded.GetComponent<SourcePlayerRig>();
                if (rig == null || rig.StableId != plan.StableId) throw new PlayerAssetException("Existing prefab is not our selected generated rig.");
                rig.ValidateGeneratedRig();
                if (loaded.transform.localPosition != Vector3.zero || loaded.transform.localRotation != Quaternion.identity ||
                    loaded.transform.localScale != Vector3.one)
                    throw new PlayerAssetException("Generated prefab root was modified; refusing to hide an unknown basis/scale override.");
                if (apply && !rig.HasStaticSourcePose)
                {
                    EnsureFolder(outputs, destination + "/Debug", destination, "Debug");
                    string backup = destination + "/Debug/authoring-bind-pose.prefab";
                    outputs.NewPath(backup);
                    if (!AssetDatabase.CopyAsset(prefabPath, backup))
                        throw new PlayerAssetException("Could not preserve the authoring-bind debug prefab; original prefab was not changed.");
                    string poseAssetPath = destination + "/source-pose.json";
                    WriteNew(outputs, poseAssetPath, bytes);
                    AssetDatabase.ImportAsset(poseAssetPath, ImportAssetOptions.ForceSynchronousImport);
                    TextAsset poseAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(poseAssetPath);
                    if (poseAsset == null || AuthorizedAssetRoot.Sha256(poseAsset.bytes) != pose.Sha256)
                        throw new PlayerAssetException("Persisted decoded source-pose content pin changed.");
                    Quaternion[] rotations = pose.UnityLocalRotations(plan.Conversion)
                        .Select(q => new Quaternion(q.X, q.Y, q.Z, q.W)).ToArray();
                    rig.ApplyStaticSourcePose(rotations, Vector(pose.UnityAnchoredRootOffset(plan.Conversion)),
                        pose.Sequence, pose.SourceFrame, pose.Sha256, poseAsset);
                    StaticPoseProof proof = VerifyStaticPose(plan, pose, rig, prefabPath);
                    string expectedQuaternionHash = proof.localQuaternionSha256;
                    if (PrefabUtility.SaveAsPrefabAsset(loaded, prefabPath) == null)
                        throw new PlayerAssetException("Could not save narrow posed-prefab update.");
                    SourcePlayerRig saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<SourcePlayerRig>();
                    StaticPoseProof savedProof = VerifyStaticPose(plan, pose, saved, prefabPath);
                    if (savedProof.localQuaternionSha256 != expectedQuaternionHash)
                        throw new PlayerAssetException("Saved prefab quaternion hash differs from verified source-pose application.");
                    WriteNew(outputs, destination + "/pose-application-report.json",
                        Encoding.UTF8.GetBytes(JsonUtility.ToJson(savedProof, true) + "\n"));
                    AssetDatabase.ImportAsset(destination + "/pose-application-report.json", ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    VerifyStaticPose(plan, pose, rig, prefabPath);
                }
                for (int i = 0; i < protectedFiles.Length; i++)
                    if (before[i] != AuthorizedAssetRoot.Sha256(File.ReadAllBytes(protectedFiles[i])))
                        throw new PlayerAssetException("Pose-only update changed protected geometry/material/texture provenance: " + Path.GetFileName(protectedFiles[i]));
                Debug.Log("PASS: actual static source pose " + pose.Sequence + " frame" + pose.SourceFrame +
                    "; source matrices, parent-local basis, root anchor, saved quaternion/content hashes and unchanged mesh/UV/material inputs verified. No continuous animation or verified pre-snap claim.");
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
        }

        private static StaticPoseProof VerifyStaticPose(PlayerImportPlan plan, SourcePlayerPose pose,
            SourcePlayerRig rig, string prefabPath)
        {
            rig.ValidateGeneratedRig();
            if (!rig.HasStaticSourcePose || rig.PoseSequence != pose.Sequence || rig.PoseSourceFrame != pose.SourceFrame ||
                rig.PoseSha256 != pose.Sha256 || rig.PoseSource == null ||
                AuthorizedAssetRoot.Sha256(rig.PoseSource.bytes) != pose.Sha256)
                throw new PlayerAssetException("Actual prefab static-pose source/frame/hash metadata mismatch.");
            var reference = new SourcePoseEvaluator(plan.Player, pose, plan.Conversion);
            var quaternionBytes = new List<byte>();
            var proof = new StaticPoseProof {
                stage = SourcePlayerRig.StaticPoseStage, sequence = pose.Sequence, sourceFrame = pose.SourceFrame,
                exportedFrame = pose.ExportedFrame, poseSha256 = pose.Sha256, sourceWan = pose.SourcePath,
                sourceWanGitBlob = pose.SourceGitBlobSha1, sourceWanSha256 = pose.SourceSha256,
                joints = rig.JointCount, anchoredRootOffset = Vector(pose.UnityAnchoredRootOffset(plan.Conversion)),
                prefabPath = prefabPath, rootPolicy = "MODE_ANCHORXZ: initial X/Z held by actor; original scaled Y applied inside rig, appearance only"
            };
            for (int i = 0; i < rig.JointCount; i++)
            {
                Transform joint = rig.GetJoint(i);
                Quaternion q = joint.localRotation;
                foreach (float component in new[] { q.x, q.y, q.z, q.w })
                {
                    byte[] part = BitConverter.GetBytes(component);
                    if (!BitConverter.IsLittleEndian) Array.Reverse(part);
                    quaternionBytes.AddRange(part);
                }
                Mesh mesh = joint.GetComponent<MeshFilter>().sharedMesh;
                Vector3 center = Vector3.zero;
                foreach (Vector3 local in mesh.vertices)
                {
                    Vector3 actual = rig.transform.InverseTransformPoint(joint.TransformPoint(local));
                    Vector3 expected = Vector(reference.MeshVertexWorld(i, new AssetVector3(local.x, local.y, local.z)));
                    float error = Vector3.Distance(actual, expected);
                    proof.maxSourceMatrixVertexError = Mathf.Max(error, proof.maxSourceMatrixVertexError);
                    if (error > 0.00005f)
                        throw new PlayerAssetException("Original active matrix vs actual prefab vertex mismatch: " + plan.Player.Limbs[i].Symbol + ", error " + error);
                    center += actual; proof.verifiedVertices++;
                }
                center /= mesh.vertexCount;
                string name = pose.Joints[i].Name;
                if (name == "head") proof.headCentroid = center;
                if (name == "right_foot") proof.rightFootCentroid = center;
                if (name == "left_foot") proof.leftFootCentroid = center;
            }
            if (pose.Sequence == "QBSTANCE" &&
                (proof.headCentroid.y <= proof.rightFootCentroid.y || proof.headCentroid.y <= proof.leftFootCentroid.y))
                throw new PlayerAssetException("Verified QBSTANCE prefab still has head below feet; no standing claim is permitted.");
            proof.localQuaternionSha256 = AuthorizedAssetRoot.Sha256(quaternionBytes.ToArray());
            return proof;
        }

        private static PlayerAssetImportRequest ReadRequest(AuthorizedAssetRoot root, string relative)
        {
            if (relative == null || !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new PlayerAssetException("Select an explicit root-relative JSON request.");
            string json = root.ReadUtf8(relative, 65536);
            PlayerAssetImportRequest request = JsonUtility.FromJson<PlayerAssetImportRequest>(json);
            if (request == null) throw new PlayerAssetException("Import request JSON is empty/invalid.");
            return request;
        }
        private static T ParseEnum<T>(string value, string field) where T : struct
        {
            T parsed;
            if (string.IsNullOrEmpty(value) || !Enum.TryParse(value, false, out parsed) || Enum.GetName(typeof(T), parsed) != value)
                throw new PlayerAssetException("Explicit named " + field + " is required (no numeric/default convention).");
            return parsed;
        }
        private static string RequiredArgument(string[] args, string name)
        {
            string value = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != name) continue;
                if (value != null || i + 1 == args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new PlayerAssetException("Missing/duplicate command-line argument " + name + ".");
                value = args[++i];
            }
            if (value == null) throw new PlayerAssetException("Required explicit argument " + name + " was not supplied.");
            return value;
        }
        private static void EnsureFolder(AuthorizedAssetRoot guard, string path, string parent, string leaf)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                guard.ExistingDirectory(path);
                guard.ExistingFile(path + ".meta");
                return;
            }
            guard.NewPath(path);
            string guid = AssetDatabase.CreateFolder(parent, leaf);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.GUIDToAssetPath(guid) != path)
                throw new PlayerAssetException("Could not reserve exact new asset folder '" + path + "'; no overwrite was attempted.");
            guard.ExistingDirectory(path);
            guard.ExistingFile(path + ".meta");
        }
        private static void WriteNew(AuthorizedAssetRoot guard, string relative, byte[] bytes)
        {
            string path = guard.NewPath(relative);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
        }
        private static Mesh CreateUnityMesh(BindPoseLimbMesh data)
        {
            var mesh = new Mesh { name = data.Symbol + " / bind pose",
                indexFormat = data.Positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = data.Positions.Select(Vector).ToArray();
            mesh.normals = data.Normals.Select(Vector).ToArray();
            mesh.uv = data.Uvs.Select(v => new Vector2(v.X, v.Y)).ToArray();
            mesh.subMeshCount = data.SubmeshTriangles.Count;
            for (int m = 0; m < data.SubmeshTriangles.Count; m++) mesh.SetTriangles(data.SubmeshTriangles[m].ToArray(), m, false);
            mesh.RecalculateBounds();
            return mesh;
        }
        private static GameObject CreateHierarchy(string stableId, SourcePlayer player, IList<BindPoseLimbMesh> data,
            Mesh[] meshes, Material[] materials)
        {
            var root = new GameObject(stableId + " / bind-pose-only / source units");
            var nodes = new Transform[player.Nodes.Count];
            try
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    var joint = new GameObject("joint-" + i.ToString("D3") + "-" + player.Limbs[i].Symbol);
                    nodes[i] = joint.transform;
                    nodes[i].SetParent(root.transform, false);
                    joint.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    MeshRenderer renderer = joint.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials;
                    renderer.enabled = player.Nodes[i].DrawEnabled;
                }
                for (int i = 0; i < nodes.Length; i++)
                {
                    int parent = player.Nodes[i].ParentIndex;
                    nodes[i].SetParent(parent < 0 ? root.transform : nodes[parent], false);
                    nodes[i].localPosition = Vector(data[i].LocalRestOffset);
                    nodes[i].localRotation = Quaternion.identity; nodes[i].localScale = Vector3.one;
                }
                root.AddComponent<SourcePlayerRig>().InitializeBindPose(stableId, nodes,
                    player.Nodes.Select(n => n.ParentIndex).ToArray(), player.Nodes.Select(n => n.Flags).ToArray());
                return root;
            }
            catch
            {
                Object.DestroyImmediate(root);
                throw;
            }
        }
        private static Vector3 Vector(AssetVector3 value) { return new Vector3(value.X, value.Y, value.Z); }
    }
}
#endif
