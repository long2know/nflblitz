#if UNITY_EDITOR
using System;
using System.IO;
using Blitz.AssetImport;
using Blitz.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blitz.Editor
{
    public static class SourceEnvironmentChecks
    {
        [Serializable]
        private sealed class NativeReceipt
        {
            public string status;
            public string unityVersion;
            public string manifestSha256;
            public int sourcePolysets;
            public int sourceVertices;
            public int sourceTriangles;
            public int nativeVerticesCompared;
            public int nativeIndicesCompared;
            public int nativeTexturesCompared;
            public int rgbaBytesCompared;
            public int runtimeDrawPieces;
            public int visibilityVolumesNotRendered;
            public string qualification;
        }

        public static void VerifyFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string root = Required(args, "-environmentBundleRoot");
            string expectedSha256 = Required(args, "-environmentManifestSha256");
            var source = new AuthorizedAssetRoot(root);
            SourceEnvironmentBundle bundle = SourceEnvironmentReader.Read(source.ReadBytes("environment.json", 1024 * 1024),
                expectedSha256, path => source.ReadBytes(path, 32 * 1024 * 1024));
            Synthetic();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SourceEnvironmentImporter.PrefabPath);
            Require(asset != null, "Persisted original environment prefab is missing.");
            SourceEnvironmentProvider prefab = asset.GetComponent<SourceEnvironmentProvider>();
            Require(prefab != null && prefab.ManifestSha256 == expectedSha256, "Persisted original environment identity differs.");
            prefab.ValidateGeneratedWorld();
            var report = new NativeReceipt
            {
                status = "PASS-native-original-source-environment",
                unityVersion = Application.unityVersion,
                manifestSha256 = expectedSha256,
                sourcePolysets = bundle.Manifest.meshes.Length,
                runtimeDrawPieces = bundle.Manifest.draws.Length,
                visibilityVolumesNotRendered = bundle.Manifest.zoneVolumes.Length,
                qualification = "Native persisted vertices/indices/UVs/material references, full original RGBA pixels/mips and runtime factory verified. Browser render acceptance is separate; no full gameplay/HUD/audio/camera fidelity claim."
            };
            for (int i = 0; i < bundle.Meshes.Length; i++)
            {
                SourceEnvironmentMeshInfo info = bundle.Manifest.meshes[i];
                SourceEnvironmentMeshData data = bundle.Meshes[i];
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SourceEnvironmentImporter.Destination + "/mesh-" + i.ToString("D3") + ".asset");
                Require(mesh != null && mesh.vertexCount == data.Positions.Length / 3 && mesh.subMeshCount == data.Indices.Length,
                    "Native mesh count differs: " + info.name);
                Vector3[] positions = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                for (int vertex = 0; vertex < positions.Length; vertex++)
                {
                    Vector3 p = positions[vertex];
                    Vector2 uv = uvs[vertex];
                    Require(p.x == data.Positions[vertex * 3] && p.y == data.Positions[vertex * 3 + 1] &&
                        p.z == data.Positions[vertex * 3 + 2] &&
                        uv.x == data.Uvs[vertex * 2] && uv.y == data.Uvs[vertex * 2 + 1],
                        "Native source vertex/UV differs: " + info.name + "/" + vertex);
                }
                for (int submesh = 0; submesh < data.Indices.Length; submesh++)
                {
                    int[] indices = mesh.GetTriangles(submesh);
                    Require(indices.Length == data.Indices[submesh].Length, "Native source index count differs: " + info.name);
                    for (int index = 0; index < indices.Length; index++)
                        Require(indices[index] == data.Indices[submesh][index], "Native original winding/index differs: " + info.name);
                    report.nativeIndicesCompared += indices.Length;
                }
                report.sourceVertices += info.sourceVertexCount;
                report.sourceTriangles += info.triangleCount;
                report.nativeVerticesCompared += positions.Length;
            }
            for (int i = 0; i < bundle.Manifest.textures.Length; i++)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    SourceEnvironmentImporter.Destination + "/texture-" + i.ToString("D3") + ".asset");
                Require(texture != null && texture.isReadable, "Exact persisted original pixels unavailable for native comparison.");
                SourceEnvironmentImporter.VerifyTexturePixels(texture, bundle.TextureRgba[i], bundle.Manifest.textures[i]);
                report.nativeTexturesCompared++;
                report.rgbaBytesCompared += bundle.TextureRgba[i].Length;
            }
            for (int i = 0; i < bundle.Manifest.draws.Length; i++)
            {
                SourceEnvironmentDraw draw = bundle.Manifest.draws[i];
                MeshRenderer renderer = prefab.transform.GetChild(i).GetComponent<MeshRenderer>();
                Require(renderer != null && renderer.name == draw.sourceSet + " / " + draw.name,
                    "Original native draw sequence differs: " + draw.name);
                int[] slots = bundle.Meshes[draw.meshIndex].MaterialSlots;
                Material[] materials = renderer.sharedMaterials;
                for (int j = 0; j < slots.Length; j++)
                {
                    SourceEnvironmentSlot slot = bundle.Manifest.slots[slots[j]];
                    Texture2D expectedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        SourceEnvironmentImporter.Destination + "/texture-" + slot.textureIndex.ToString("D3") + ".asset");
                    Require(materials[j].mainTexture == expectedTexture && materials[j].renderQueue == draw.renderQueue &&
                        Math.Abs((double)materials[j].GetFloat("_Cutoff") - draw.state.alphaValue / 255.0) < 0.0000001 &&
                        materials[j].GetFloat("_AlphaTest") == (draw.state.alphaFunction == "GR_CMP_GREATER" ? 1 : 0) &&
                        materials[j].GetFloat("_ConstantAlphaSource") == (draw.state.alphaSource == "GR_ALPHASOURCE_CC_ALPHA" ? 1 : 0) &&
                        materials[j].GetFloat("_TextureTimesAlpha") == (draw.state.colorFunction == "GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA" ? 1 : 0) &&
                        materials[j].GetFloat("_DepthBias") == -draw.state.depthBias &&
                        materials[j].GetInt("_ZTest") == (int)(draw.state.depthFunction == "GR_CMP_ALWAYS"
                            ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Less),
                        "Original native material/texture/alpha/order differs: " + draw.name + "/" + slot.selectedName +
                        "; cutoff=" + materials[j].GetFloat("_Cutoff").ToString("R") +
                        "; sourceThreshold=" + draw.state.alphaValue + "/255");
                }
            }
            SourceEnvironmentProvider instance = null;
            try
            {
                instance = SourceEnvironmentProvider.AttachRequired(prefab, null);
                Require(instance.transform.localScale == Vector3.one / 7.8f &&
                    instance.transform.localPosition == Vector3.zero && instance.transform.localRotation == Quaternion.identity,
                    "Original environment actor/camera yard-space adapter differs.");
                instance.ValidateGeneratedWorld();
                MeshRenderer firstDraw = instance.transform.GetChild(0).GetComponent<MeshRenderer>();
                Material[] originalMaterials = firstDraw.sharedMaterials;
                Material invalid = new Material(originalMaterials[0]) { mainTexture = null };
                try
                {
                    Material[] changed = (Material[])originalMaterials.Clone();
                    changed[0] = invalid;
                    firstDraw.sharedMaterials = changed;
                    bool rejected = false;
                    try { instance.ValidateGeneratedWorld(); }
                    catch (SourceEnvironmentException error)
                    {
                        rejected = error.Message.Contains("Missing/changed original environment material");
                    }
                    Require(rejected, "Missing exact original material did not fail explicitly; generic fallback is forbidden.");
                }
                finally
                {
                    firstDraw.sharedMaterials = originalMaterials;
                    Object.DestroyImmediate(invalid);
                }
            }
            finally { if (instance != null) Object.DestroyImmediate(instance.gameObject); }
            string receipt = Optional(args, "-environmentCheckReceipt", "Build/source-environment-native-proof.json");
            Require(receipt.StartsWith("Build/", StringComparison.Ordinal), "Native receipt must stay in ignored Build.");
            var destination = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            string receiptPath = destination.NewPath(receipt);
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath));
            using (var stream = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.WriteLine(JsonUtility.ToJson(report, true));
            Debug.Log("PASS: persisted native original environment geometry, UVs, winding, every RGBA pixel/mip, material identities, draw order, 18 nonrendered boxes, yard-space factory, strict URL/missing-world checks. " + receipt);
        }

        public static void BuildIsolatedPreview()
        {
            string project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            Require(project.EndsWith("/Build/world-preview/native-project", StringComparison.Ordinal),
                "World-only preview build is restricted to the explicitly isolated ignored project, never the main project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Require(AssetDatabase.LoadAssetAtPath<GameObject>(SourceEnvironmentImporter.PrefabPath) != null,
                "Original environment must be imported before preview build.");
            Type bootstrap = Type.GetType("Blitz.Presentation.SourceWorldInspection, Assembly-CSharp");
            Require(bootstrap != null, "The explicitly isolated inspection bootstrap is missing.");
            new GameObject("Source world inspection / no gameplay substitute").AddComponent(bootstrap);
            string scene = SourceEnvironmentImporter.Destination + "/WorldPreview.unity";
            Require(EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scene),
                "Isolated world preview scene was not saved.");
            PlayerSettings.productName = "Original field2 / stadpie environment inspection";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene }, locationPathName = "Build/WebGL", target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            Require(report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0,
                "Isolated original environment WebGL build failed.");
            Debug.Log("PASS: isolated source-world-only inspection player built; no gameplay/HUD/actors/camera parity assertion.");
        }

        public static void Synthetic()
        {
            Require(SourceEnvironmentProvider.RequestedMode("https://local/?environment=source&actors=source") == "source" &&
                SourceEnvironmentProvider.RequestedMode("https://local/?environment=graybox") == "graybox" &&
                SourceEnvironmentProvider.RequestedMode("https://local/") == "",
                "Environment source/graybox/auto URL contract differs.");
            foreach (string bad in new[] { "?environment=unknown", "?environment=source&environment=graybox", "?environment", "?environment=" })
            {
                bool rejected = false;
                try { SourceEnvironmentProvider.RequestedMode("https://local/" + bad); }
                catch (SourceEnvironmentException) { rejected = true; }
                Require(rejected, "Invalid source environment URL was accepted: " + bad);
            }
            bool missing = false;
            try { SourceEnvironmentProvider.AttachRequired(null, null); }
            catch (SourceEnvironmentException error) { missing = error.Message.Contains("no procedural substitute"); }
            Require(missing, "Missing required original world was replaced by generic geometry.");
            Debug.Log("PASS: source environment mode parsing and explicit missing-world rejection.");
        }

        private static string Required(string[] args, string key)
        {
            string value = Optional(args, key, "");
            Require(value.Length > 0, "Explicit original environment argument required: " + key);
            return value;
        }

        private static string Optional(string[] args, string key, string fallback)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return fallback;
        }

        private static void Require(bool condition, string identity)
        {
            if (!condition) throw new SourceEnvironmentException(identity);
        }
    }
}
#endif
