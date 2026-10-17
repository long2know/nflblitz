using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blitz.Runtime;
using Blitz.AssetImport;
using Blitz.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Blitz.Editor
{
    public static class WebGlBuild
    {
        public const string EditorVersion = "6000.3.24f1";
        public const string ScenePath = "Assets/Scenes/Boot.unity";
        public const string OutputPath = "Build/WebGL";

        [MenuItem("NFL Blitz 2000/Prepare Boot Scene")]
        public static void PrepareProject()
        { PrepareProfile(SourceTeamResourceProfile.Contact272); }

        public static void PrepareContact281C2Project()
        { PrepareProfile(SourceTeamResourceProfile.Contact281C2); }

        private static void PrepareProfile(SourceTeamResourceProfile profile)
        {
            if (Application.unityVersion != EditorVersion)
                throw new BuildFailedException("Use the pinned Unity " + EditorVersion + " editor, not " + Application.unityVersion + ".");
            FixedStepBridgeChecks.Verify();
            HudTypographyChecks.Verify();
            if (profile.IsContact281C2)
            {
                SourceInterfaceServices services = Resources.Load<SourceInterfaceServices>(SourceInterfaceServices.C2ResourceKey);
                if (services == null || !services.Profile.IsContact281C2)
                    throw new BuildFailedException("Explicit C2 build requires actual canonical281/C2 services, not a stock/272 fallback.");
                profile.RequireLibrary(services.Animations);
                SourceTeamPresentationAsset teams = Resources.Load<SourceTeamPresentationAsset>(profile.TeamResourceKey);
                if (teams == null || teams.Template.StableId != profile.StableId)
                    throw new BuildFailedException("Explicit C2 build requires its actual canonical team/template resources.");
                SourcePlayerRig template = teams.Template;
                profile.RequireImpact(SourcePlayerProvider.ResolveRequiredCinematicLibrary(template));
                if (services.C2Data.PayloadSha256 != SourceInterfacePackagePlan.C2CallbackSha256)
                    throw new BuildFailedException("Explicit C2 build callback data changed.");
            }
            ConfigurePlayer();
            EnsureShaders();
            CreateBootScene(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("NFL Blitz 2000 port prepared. Open Assets/Scenes/Boot.unity and press Play.");
        }

        public static void Build()
        { BuildProfile(SourceTeamResourceProfile.Contact272); }

        public static void BuildContact281C2()
        { BuildProfile(SourceTeamResourceProfile.Contact281C2); }

        private static void BuildProfile(SourceTeamResourceProfile profile)
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputPath));
            string buildId = Environment.GetEnvironmentVariable("BLITZ_WEBGL_BUILD_ID");
            if (string.IsNullOrEmpty(buildId))
                buildId = Guid.NewGuid().ToString("N");
            Guid parsedId;
            if (!Guid.TryParseExact(buildId, "N", out parsedId))
                throw new BuildFailedException("BLITZ_WEBGL_BUILD_ID must be a 32-character hexadecimal invocation ID.");
            buildId = parsedId.ToString("N");
            PrepareOutput(output);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new BuildFailedException("The selected editor has no WebGL Build Support module.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new BuildFailedException("Launch the editor with -buildTarget WebGL (scripts/build-webgl.sh supplies it).");

            PrepareProfile(profile);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (report == null || report.summary.result != BuildResult.Succeeded ||
                report.summary.totalErrors != 0 || report.summary.totalSize == 0)
            {
                string result = report == null ? "no BuildReport" : report.summary.result + " / " + report.summary.totalErrors + " errors";
                throw new BuildFailedException("Unity WebGL build failed: " + result + ". Inspect Logs/webgl-build.log.");
            }
            string[] artifacts = ValidateArtifacts(output);
            WriteGenerationProof(output, buildId, report.summary.totalSize, artifacts);
            Debug.Log("Verified real WebGL player: " + output + " / " + report.summary.totalSize + " bytes / " + report.summary.totalTime + ".");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "long2know";
            PlayerSettings.productName = "NFL Blitz 2000 - Unity Port";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.runInBackground = false;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.WebGL, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.initialMemorySize = 64;
            PlayerSettings.WebGL.maximumMemorySize = 256;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.wasm2023 = false;
            PlayerSettings.WebGL.template = "APPLICATION:Minimal";
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorSettings.serializationMode = SerializationMode.ForceText;

            UnityEngine.Object[] playerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (playerAssets.Length == 0)
                throw new BuildFailedException("Unity did not load the root ProjectSettings.asset.");
            SerializedObject settings = new SerializedObject(playerAssets[0]);
            SerializedProperty inputHandler = settings.FindProperty("activeInputHandler");
            if (inputHandler == null)
                throw new BuildFailedException("Cannot select the legacy keyboard input backend for this editor.");
            inputHandler.intValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureShaders()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets.Length == 0)
                throw new BuildFailedException("Unity did not create GraphicsSettings.asset.");
            SerializedObject graphics = new SerializedObject(assets[0]);
            SerializedProperty included = graphics.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
                throw new BuildFailedException("Cannot preserve the procedural Built-in shaders.");
            foreach (string shaderName in new[] { "Standard", "Unlit/Color" })
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                    throw new BuildFailedException("Required Built-in shader is unavailable: " + shaderName);
                bool exists = false;
                for (int i = 0; i < included.arraySize; i++)
                    exists |= included.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (!exists)
                {
                    int index = included.arraySize;
                    included.InsertArrayElementAtIndex(index);
                    included.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                }
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateBootScene(SourceTeamResourceProfile profile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("NFL Blitz 2000 / runtime");
            BlitzRuntime runtime = root.AddComponent<BlitzRuntime>();
            if (profile.IsContact281C2) runtime.ConfigureSourceResourceProfile(profile.StableId);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new BuildFailedException("Failed to persist " + ScenePath + ".");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            SceneAsset saved = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (saved == null || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)))
                throw new BuildFailedException("The Boot scene or its stable metadata could not be imported.");
        }

        private static void PrepareOutput(string output)
        {
            RejectLinkedPath(output);
            if (File.Exists(output))
                throw new BuildFailedException("The WebGL output must be a directory: " + output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
            {
                // Keep the fixed export path and Library/Bee caches; preserve old output, never reuse it.
                string previous = Path.Combine(Path.GetDirectoryName(output), ".webgl-previous-" + Guid.NewGuid().ToString("N"));
                RejectLinkedPath(previous);
                Directory.Move(output, previous);
            }
            Directory.CreateDirectory(output);
        }

        public static void VerifyArtifactGenerationChecks()
        {
            if (Application.unityVersion != EditorVersion)
                throw new BuildFailedException("Artifact selfchecks require Unity " + EditorVersion + ".");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string work = Path.Combine(project, "Build", ".webgl-selfcheck-" + Guid.NewGuid().ToString("N"));
            string output = Path.Combine(work, "WebGL");
            RejectLinkedPath(work);
            if (Directory.Exists(work) || File.Exists(work))
                throw new BuildFailedException("Artifact selfcheck directory already exists.");
            Directory.CreateDirectory(output);
            bool passed = false;
            try
            {
                File.WriteAllText(Path.Combine(output, "old-output.txt"), "old generation");
                PrepareOutput(output);
                string[] archives = Directory.GetDirectories(work, ".webgl-previous-*");
                if (Directory.GetFileSystemEntries(output).Length != 0 || archives.Length != 1 ||
                    File.ReadAllText(Path.Combine(archives[0], "old-output.txt")) != "old generation")
                    throw new BuildFailedException("Artifact selfcheck: exact output was not isolated/preserved.");

                string build = Path.Combine(output, "Build");
                Directory.CreateDirectory(build);
                string index = Path.Combine(output, "index.html");
                string loader = Path.Combine(build, "check.loader.js");
                string framework = Path.Combine(build, "check.framework.js");
                string wasm = Path.Combine(build, "check.wasm");
                string data = Path.Combine(build, "check.data");
                string html = "<html><body>SYNTHETIC ARTIFACT SELFCHECK, NOT A GAME" +
                    "<script src=\"Build/check.loader.js\"></script>" +
                    "Build/check.framework.js Build/check.wasm Build/check.data</body></html>";
                File.WriteAllText(index, html);
                File.WriteAllText(loader, "function createUnityInstance() {}");
                File.WriteAllText(framework, "// synthetic selfcheck");
                byte[] module = { 0, 97, 115, 109, 1, 0, 0, 0 };
                File.WriteAllBytes(wasm, module);
                File.WriteAllText(data, "synthetic selfcheck data");
                DateTime cachedTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                foreach (string artifact in new[] { index, loader, framework, wasm, data })
                    File.SetLastWriteTimeUtc(artifact, cachedTime);
                ValidateArtifacts(output);
                if (File.GetLastWriteTimeUtc(index) != cachedTime || File.GetLastWriteTimeUtc(wasm) != cachedTime)
                    throw new BuildFailedException("Artifact selfcheck changed cached file timestamps.");

                File.WriteAllBytes(wasm, new byte[] { 1 });
                ExpectArtifactFailure(() => ValidateArtifacts(output), "invalid WASM");
                File.WriteAllBytes(wasm, module);
                File.Move(data, Path.Combine(work, "held.data"));
                ExpectArtifactFailure(() => ValidateArtifacts(output), "missing payload");
                File.Move(Path.Combine(work, "held.data"), data);
                string duplicate = Path.Combine(build, "duplicate.framework.js");
                File.Copy(framework, duplicate);
                ExpectArtifactFailure(() => ValidateArtifacts(output), "duplicate payload");
                File.Delete(duplicate);
                File.WriteAllText(index, html.Replace("check.data", "missing.data"));
                ExpectArtifactFailure(() => ValidateArtifacts(output), "missing index reference");
                File.WriteAllText(index, html);
                string compressed = Path.Combine(output, "invalid.gz");
                File.WriteAllText(compressed, "synthetic compressed canary");
                ExpectArtifactFailure(() => ValidateArtifacts(output), "compressed payload");
                File.Delete(compressed);

                string[] artifacts = ValidateArtifacts(output);
                foreach (string artifact in artifacts)
                    File.SetLastWriteTimeUtc(artifact, cachedTime);
                string buildId = Guid.NewGuid().ToString("N");
                WriteGenerationProof(output, buildId, 1UL, artifacts);
                ArtifactCheckResult result = new ArtifactCheckResult
                {
                    status = "passed", checks = 8, fixtureOutput = output, buildId = buildId,
                    qualification = "Synthetic artifact selfchecks only; no player BuildReport or WebGL build."
                };
                string resultPath = Path.Combine(project, "Build", "artifact-selfcheck.json");
                RejectLinkedPath(resultPath);
                File.WriteAllText(resultPath,
                    JsonUtility.ToJson(result, true) + "\n", new UTF8Encoding(false));
                passed = true;
                Debug.Log("BLITZ_ARTIFACT_GENERATION_SELFCHECK_PASSED checks=8; synthetic only; no player build.");
            }
            finally
            {
                // Successful fixtures are verified by the shared shell gate, then removed by the caller.
                if (!passed)
                {
                    RejectLinkedPath(work);
                    Directory.Delete(work, true);
                }
            }
        }

        private static void ExpectArtifactFailure(Action action, string label)
        {
            try { action(); }
            catch (BuildFailedException) { return; }
            throw new BuildFailedException("Artifact selfcheck did not reject " + label + ".");
        }

        [Serializable]
        private sealed class ArtifactCheckResult
        {
            public string status;
            public int checks;
            public string fixtureOutput;
            public string buildId;
            public string qualification;
        }

        private static string[] ValidateArtifacts(string output)
        {
            RejectUnsafeTree(new DirectoryInfo(output));
            string index = Path.Combine(output, "index.html");
            RequirePayloadFile(index);
            string build = Path.Combine(output, "Build");
            if (!Directory.Exists(build))
                throw new BuildFailedException("Unity reported success without a Build/ payload directory.");
            string loader = RequireSingle(build, "*.loader.js");
            string framework = RequireSingle(build, "*.framework.js");
            string wasm = RequireSingle(build, "*.wasm");
            string data = RequireSingle(build, "*.data");
            string html = File.ReadAllText(index);
            foreach (string artifact in new[] { loader, framework, wasm, data })
                if (html.IndexOf(Path.GetFileName(artifact), StringComparison.Ordinal) < 0)
                    throw new BuildFailedException("index.html does not reference " + Path.GetFileName(artifact) + ".");
            if (File.ReadAllText(loader).IndexOf("createUnityInstance", StringComparison.Ordinal) < 0)
                throw new BuildFailedException("The emitted loader does not expose createUnityInstance.");
            using (FileStream stream = File.OpenRead(wasm))
            {
                byte[] expected = { 0, 97, 115, 109, 1, 0, 0, 0 };
                for (int i = 0; i < expected.Length; i++)
                {
                    if (stream.ReadByte() != expected[i])
                        throw new BuildFailedException("The emitted .wasm file has no valid WebAssembly header: " + wasm);
                }
            }
            return new[] { index, loader, framework, wasm, data };
        }

        private static string RequireSingle(string directory, string pattern)
        {
            string[] files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length != 1)
                throw new BuildFailedException("Expected exactly one uncompressed " + pattern + " payload; found " + files.Length + ".");
            RequirePayloadFile(files[0]);
            foreach (char c in Path.GetFileName(files[0]))
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-')
                    throw new BuildFailedException("Unsupported WebGL artifact filename: " + files[0]);
            return files[0];
        }

        private static void RequirePayloadFile(string path)
        {
            RejectLinkedPath(path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new BuildFailedException("Unity reported success without a nonempty artifact: " + path);
        }

        private static void RejectLinkedPath(string path)
        {
            for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new BuildFailedException("Linked WebGL output paths are not accepted: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static void RejectUnsafeTree(DirectoryInfo directory)
        {
            foreach (FileSystemInfo entry in directory.GetFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new BuildFailedException("Linked WebGL output is not accepted: " + entry.FullName);
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    RejectUnsafeTree((DirectoryInfo)entry);
                    continue;
                }
                string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (extension == ".gz" || extension == ".br" || extension == ".unityweb" ||
                    extension == ".ulf" || extension == ".alf" || extension == ".pem" ||
                    extension == ".key" || extension == ".pfx" || extension == ".p12" ||
                    entry.Name.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
                    throw new BuildFailedException("Compressed/private material is not accepted in uncompressed WebGL output: " + entry.Name);
            }
        }

        private static void WriteGenerationProof(string output, string buildId, ulong reportBytes, string[] artifacts)
        {
            StringBuilder proof = new StringBuilder();
            proof.Append("format=blitz-webgl-build-v1\nbuild-id=").Append(buildId)
                .Append("\nunity-version=").Append(EditorVersion)
                .Append("\nbuild-target=WebGL\nbuild-result=Succeeded\ntotal-errors=0\nbuild-report-bytes=")
                .Append(reportBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string artifact in artifacts)
            {
                string relative = artifact == Path.Combine(output, "index.html") ? "index.html" : "Build/" + Path.GetFileName(artifact);
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(artifact))
                    proof.Append("sha256=").Append(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant())
                        .Append(' ').Append(relative).Append('\n');
            }
            string pending = Path.Combine(output, "build-generation.txt.pending");
            File.WriteAllText(pending, proof.ToString(), new UTF8Encoding(false));
            File.Move(pending, Path.Combine(output, "build-generation.txt"));
        }
    }
}
