#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Blitz.AssetImport;
using Blitz.Presentation;
using Blitz.Runtime;
using UnityEditor;
using UnityEngine;

namespace Blitz.Editor
{
    public static class SourceTeamInterfaceImporter
    {
        public const string HudAssetSuffix = "/Resources/BlitzSourceInterface/original-interface.asset";
        public const string ServicesAssetSuffix = "/Resources/BlitzSourceInterface/original-match-services.asset";
        public const string ShaderName = "Hidden/Blitz/SourceHud";

        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = PlanFromArguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            Import(plan, id);
        }

        public static SourceInterfacePackagePlan PlanFromArguments(string[] args)
        {
            return SourceInterfacePackagePlan.Create(
                PlayerAssetImporter.RequiredArgument(args, "-interfaceAssetRoot"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceManifest"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceManifestSha256"),
                PlayerAssetImporter.RequiredArgument(args, "-interfacePlaybookRoot"),
                PlayerAssetImporter.RequiredArgument(args, "-interfacePlaybook"),
                PlayerAssetImporter.RequiredArgument(args, "-interfacePlaybookSha256"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceConfigurationRoot"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceConfiguration"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceConfigurationSha256"));
        }

        public static SourceInterfaceServices Import(SourceInterfacePackagePlan plan, string stableId)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            PlayerImportPlan.ValidateStableId(stableId);
            string destination = "Assets/Imported/" + stableId;
            string hudPath = destination + HudAssetSuffix, servicesPath = destination + ServicesAssetSuffix;
            SourceInterfacePackagePlan.RequireOutputIdentity(hudPath, SourceInterfacePackagePlan.HudResourceKey);
            SourceInterfacePackagePlan.RequireOutputIdentity(servicesPath, SourceInterfacePackagePlan.ServicesResourceKey);
            if (SourceHudAsset.PreparedManifestSha256 != SourceInterfacePackagePlan.HudSha256 ||
                SourceInterfaceServices.ExpandedAnimationSha256 != SourceInterfacePackagePlan.AnimationSha256 ||
                SourceInterfaceServices.PlaybookSha256 != SourceInterfacePackagePlan.PlaybookSha256)
                throw new SourceInterfaceException("Frozen B1 interface consumer pins differ; no alias or M1 substitution.");
            RejectExistingResources();
            SourceInterfacePackagePlan.RequireHudShaderSource(Path.GetDirectoryName(Application.dataPath));
            Shader shader = Shader.Find(ShaderName);
            if (shader == null || AssetDatabase.GetAssetPath(shader) != SourceInterfacePackagePlan.HudShaderPath ||
                ShaderUtil.ShaderHasError(shader))
                throw new SourceInterfaceException("Actual original HUD shader is missing, duplicated or has compile errors; no material fallback.");
            SourceAnimationLibraryAsset animations = Resources.Load<SourceAnimationLibraryAsset>(SourcePlayerProvider.TeamAnimationResourceKey);
            SourceTeamPresentationAsset teams = SourcePlayerProvider.ResolveRequiredTeamPresentation();
            if (animations == null || animations.InventorySha256 != SourceInterfacePackagePlan.AnimationSha256 ||
                AssetDatabase.GetAssetPath(animations) != "Assets/Imported/original-teams/Resources/" +
                    SourcePlayerProvider.TeamAnimationResourceKey + ".asset" ||
                teams.Rosters.ContentSha256 != SourceTeamPresentationData.PinnedRosterSha256)
                throw new SourceInterfaceException("Import the actual NEW original-teams/final117 resources first; old56/future156 are not aliases.");
            var actual = animations.Load() as SourceAnimationLibrary;
            if (actual == null || actual.SequenceNames.Count != 117 || actual.FrameCount != 10288)
                throw new SourceInterfaceException("Actual B1 animation content is not final117/10288.");
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            output.NewPath(destination);
            Folder(output, "Assets/Imported"); Folder(output, destination);
            Folder(output, destination + "/Resources"); Folder(output, destination + "/Resources/BlitzSourceInterface");
            string resource = destination + "/Resources/BlitzSourceInterface";
            Folder(output, resource + "/Bytes");
            TextAsset manifest = WriteText(output, resource + "/interface.json", plan.CopyManifest());
            var bytes = new List<SourceHudTextureBytes>();
            foreach (SourceInterfacePackageTexture texture in plan.Textures)
                bytes.Add(new SourceHudTextureBytes(texture.Definition.Id,
                    WriteText(output, resource + "/Bytes/" + texture.Definition.Id + ".bytes", texture.CopyBytes())));
            TextAsset book = WriteText(output, resource + "/playbook.bytes", plan.CopyPlaybook());
            WriteText(output, resource + "/configuration.json", plan.CopyConfiguration());
            var hud = ScriptableObject.CreateInstance<SourceHudAsset>();
            hud.Initialize(manifest, SourceInterfacePackagePlan.HudSha256, bytes.ToArray(), shader);
            output.NewPath(hudPath); AssetDatabase.CreateAsset(hud, hudPath);
            var services = ScriptableObject.CreateInstance<SourceInterfaceServices>();
            services.Initialize(animations, hud, book, plan.Configuration.Match, plan.Configuration.Presentation,
                plan.Configuration.NativeGamepadsEnabled);
            output.NewPath(servicesPath); AssetDatabase.CreateAsset(services, servicesPath);
            AssetDatabase.SaveAssets();
            SourceTeamInterfaceChecks.VerifySaved(plan, destination, false);
            Debug.Log("PASS saved actual original B1 HUD/services Resources; raw/converted art and all generated metadata stay ignored. " +
                "Framebuffer/native-game/browser proof is separate; no M1 expanded catalog or fake services loaded.");
            return services;
        }

        private static void RejectExistingResources()
        {
            foreach (string type in new[] { "SourceHudAsset", "SourceInterfaceServices" })
                foreach (string guid in AssetDatabase.FindAssets("t:" + type))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.IndexOf("/Resources/", StringComparison.Ordinal) >= 0)
                        throw new SourceInterfaceException("An immutable interface Resources asset already exists: " + path +
                            ". Inspect it; no overwrite, duplicate key, repair or deletion is performed.");
                }
        }
        private static TextAsset WriteText(AuthorizedAssetRoot output, string path, byte[] bytes)
        {
            PlayerAssetImporter.WriteNew(output, path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || AuthorizedAssetRoot.Sha256(asset.bytes) != AuthorizedAssetRoot.Sha256(bytes))
                throw new SourceInterfaceException("Native bundled original byte content changed: " + path);
            return asset;
        }
        private static void Folder(AuthorizedAssetRoot output, string path)
        {
            int slash = path.LastIndexOf('/');
            PlayerAssetImporter.EnsureFolder(output, path, path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
#endif
