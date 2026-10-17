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
        public const string MovementServicesAssetSuffix = "/Resources/BlitzSourceInterface/original-movement157-match-services.asset";
        public const string SoundVmServicesAssetSuffix = "/Resources/BlitzSourceInterface/original-movement157-soundvm-match-services.asset";
        public const string ContactServicesAssetSuffix = "/Resources/BlitzSourceInterface/original-contact272-soundvm-match-services.asset";
        public const string ShaderName = "Hidden/Blitz/SourceHud";

        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Import(PlanFromArguments(args), PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId"));
        }

        public static void ImportMovement157FromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Import(PlanFromMovement157Arguments(args), PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId"));
        }

        public static void ImportSoundVmFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Import(PlanFromSoundVmArguments(args), PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId"));
        }

        public static void ImportContact272FromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Import(PlanFromContact272Arguments(args), PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId"));
        }

        public static void ImportContact281C2FromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Import(PlanFromContact281C2Arguments(args), PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId"));
        }

        public static void ImportContact281C2M1FromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Import(PlanFromContact281C2M1Arguments(args), PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId"));
        }

        public static SourceInterfacePackagePlan PlanFromArguments(string[] args)
        { return PlanFromArguments(args, false); }

        public static SourceInterfacePackagePlan PlanFromMovement157Arguments(string[] args)
        { return PlanFromArguments(args, true); }

        public static SourceInterfacePackagePlan PlanFromSoundVmArguments(string[] args)
        {
            return SourceInterfacePackagePlan.WithSourceSoundVm(PlanFromMovement157Arguments(args),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceSoundVmRoot"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceSoundVmData"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceSoundVmSha256"));
        }

        public static SourceInterfacePackagePlan PlanFromContact272Arguments(string[] args)
        { return SourceInterfacePackagePlan.WithContact272(PlanFromSoundVmArguments(args)); }

        public static SourceInterfacePackagePlan PlanFromContact281C2Arguments(string[] args)
        {
            return SourceInterfacePackagePlan.WithContact281C2(PlanFromContact272Arguments(args),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceC2Root"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceC2Data"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceC2HitExtension"));
        }

        public static SourceInterfacePackagePlan PlanFromContact281C2M1Arguments(string[] args)
        {
            return SourceInterfacePackagePlan.WithContact281C2M1(PlanFromContact272Arguments(args),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceC2Root"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceC2Data"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceC2HitExtension"));
        }

        private static SourceInterfacePackagePlan PlanFromArguments(string[] args, bool movement157)
        {
            string hudRoot = PlayerAssetImporter.RequiredArgument(args, "-interfaceAssetRoot");
            string manifest = PlayerAssetImporter.RequiredArgument(args, "-interfaceManifest");
            string hudHash = PlayerAssetImporter.RequiredArgument(args, "-interfaceManifestSha256");
            string bookRoot = PlayerAssetImporter.RequiredArgument(args, "-interfacePlaybookRoot");
            string book = PlayerAssetImporter.RequiredArgument(args, "-interfacePlaybook");
            string bookHash = PlayerAssetImporter.RequiredArgument(args, "-interfacePlaybookSha256");
            string configRoot = PlayerAssetImporter.RequiredArgument(args, "-interfaceConfigurationRoot");
            string config = PlayerAssetImporter.RequiredArgument(args, "-interfaceConfiguration");
            string configHash = PlayerAssetImporter.RequiredArgument(args, "-interfaceConfigurationSha256");
            if (!movement157)
                return SourceInterfacePackagePlan.Create(hudRoot, manifest, hudHash, bookRoot, book, bookHash,
                    configRoot, config, configHash);
            return SourceInterfacePackagePlan.CreateMovement157(hudRoot, manifest, hudHash, bookRoot, book, bookHash,
                configRoot, config, configHash,
                PlayerAssetImporter.RequiredArgument(args, "-interfaceMovementRoot"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceMovement"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceMovementSha256"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceBootstrapRoot"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceBootstrap"),
                PlayerAssetImporter.RequiredArgument(args, "-interfaceBootstrapSha256"));
        }

        public static SourceInterfaceServices Import(SourceInterfacePackagePlan plan, string stableId)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            PlayerImportPlan.ValidateStableId(stableId);
            SourceTeamResourceProfile profile = plan.Profile;
            string destination = "Assets/Imported/" + stableId;
            string hudPath = destination + HudAssetSuffix;
            string servicesPath = destination + ServicesSuffix(plan);
            SourceInterfacePackagePlan.RequireOutputIdentity(hudPath, SourceInterfacePackagePlan.HudResourceKey);
            SourceInterfacePackagePlan.RequireOutputIdentity(servicesPath, plan.SelectedServicesResourceKey);
            if (SourceHudAsset.PreparedManifestSha256 != SourceInterfacePackagePlan.HudSha256 ||
                (!profile.IsContact272 && !profile.UsesC2Data) || !plan.HasSoundVm ||
                SourceInterfaceServices.PlaybookSha256 != SourceInterfacePackagePlan.PlaybookSha256 ||
                SourceInterfaceServices.ResourceKeyFor(profile) != plan.SelectedServicesResourceKey)
                throw new SourceInterfaceException("Import explicit Contact272 or Contact281C2 services. Historical B1/Movement157/SoundVM resources remain distinct; use their matching historical engine, never upgrade them silently.");
            if (plan.HasSoundVm && SourceInterfaceServices.SoundVmFunctionsResourceKey != SourceInterfacePackagePlan.SoundVmFunctionsResourceKey)
                throw new SourceInterfaceException("SoundVM data consumer/import identity differs; no old-profile alias.");
            if (FindUniqueResourcePath("SourceInterfaceServices", plan.SelectedServicesResourceKey) != null)
                throw new SourceInterfaceException("The selected immutable match-services resource already exists; no overwrite or duplicate key.");
            SourceInterfacePackagePlan.RequireHudShaderSource(Path.GetDirectoryName(Application.dataPath));
            Shader shader = Shader.Find(ShaderName);
            if (shader == null || AssetDatabase.GetAssetPath(shader) != SourceInterfacePackagePlan.HudShaderPath ||
                ShaderUtil.ShaderHasError(shader))
                throw new SourceInterfaceException("Actual original HUD shader is missing, duplicated or has errors; no fallback.");
            SourceAnimationLibraryAsset animations = Resources.Load<SourceAnimationLibraryAsset>(profile.AnimationResourceKey);
            RequireAnimationProfileAsset(profile, animations);
            SourceAnimationLibrary actual = animations.Load() as SourceAnimationLibrary;
            if (actual == null) throw new SourceInterfaceException("The actual typed original animation library is required.");
            profile.RequireInventory(animations.InventorySha256, actual.SequenceNames.Count, actual.FrameCount);
            SourceTeamPresentationAsset teams = Resources.Load<SourceTeamPresentationAsset>(profile.TeamResourceKey);
            if (teams == null || teams.Template.StableId != profile.StableId ||
                teams.Rosters.ContentSha256 != SourceTeamPresentationData.PinnedRosterSha256)
                throw new SourceInterfaceException("Missing/wrong actual opposing-team/profile/roster resources.");

            string existingHudPath = FindUniqueResourcePath("SourceHudAsset", SourceInterfacePackagePlan.HudResourceKey);
            SourceHudAsset hud = existingHudPath == null ? null : AssetDatabase.LoadAssetAtPath<SourceHudAsset>(existingHudPath);
            if (hud != null)
            {
                long bytes, pixels;
                SourceTeamInterfaceChecks.VerifyHudBindings(plan, hud, false, out bytes, out pixels);
            }
            TextAsset existingMovement = null;
            TextAsset existingSoundVm = null;
            if (profile.UsesMovementData)
            {
                string path = FindUniqueResourcePath("TextAsset", SourceInterfaceMovementData.ResourceKey);
                if (path != null)
                {
                    existingMovement = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (existingMovement == null) throw new SourceInterfaceException("Movement resource is not an actual TextAsset.");
                    SourceInterfaceMovementData.Read(existingMovement.bytes, SourceInterfaceMovementData.ContentSha256);
                }
            }
            if (plan.HasSoundVm)
            {
                string soundPath = FindUniqueResourcePath("TextAsset", SourceInterfacePackagePlan.SoundVmFunctionsResourceKey);
                if (soundPath != null)
                {
                    existingSoundVm = AssetDatabase.LoadAssetAtPath<TextAsset>(soundPath);
                    if (existingSoundVm == null) throw new SourceInterfaceException("Actual SoundVM SAFN1 resource is not a TextAsset.");
                    SourceInterfacePackagePlan.ReadSoundVmFunctions(existingSoundVm.bytes, SourceInterfacePackagePlan.SoundVmFunctionsSha256);
                }
            }
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            output.NewPath(destination);
            Folder(output, "Assets/Imported"); Folder(output, destination); Folder(output, destination + "/Resources");
            Folder(output, destination + "/Resources/BlitzSourceInterface");
            string resource = destination + "/Resources/BlitzSourceInterface";
            if (hud == null)
            {
                Folder(output, resource + "/Bytes");
                TextAsset manifest = WriteText(output, resource + "/interface.json", plan.CopyManifest());
                var originals = new List<SourceHudTextureBytes>();
                foreach (SourceInterfacePackageTexture texture in plan.Textures)
                    originals.Add(new SourceHudTextureBytes(texture.Definition.Id,
                        WriteText(output, resource + "/Bytes/" + texture.Definition.Id + ".bytes", texture.CopyBytes())));
                hud = ScriptableObject.CreateInstance<SourceHudAsset>();
                hud.Initialize(manifest, SourceInterfacePackagePlan.HudSha256, originals.ToArray(), shader);
                output.NewPath(hudPath); AssetDatabase.CreateAsset(hud, hudPath);
            }
            TextAsset bookText = WriteText(output, resource + "/playbook.bytes", plan.CopyPlaybook());
            WriteText(output, resource + "/configuration.json", plan.CopyConfiguration());
            TextAsset bootstrapText = null;
            TextAsset soundVmText = existingSoundVm;
            if (profile.UsesMovementData)
            {
                if (existingMovement == null)
                {
                    Folder(output, destination + "/Resources/SourceData");
                    WriteText(output, destination + "/Resources/" + SourceInterfaceMovementData.ResourceKey + ".json", plan.CopyMovement());
                }
                bootstrapText = WriteText(output, resource + "/movement-bootstrap.json", plan.CopyBootstrap());
            }
            if (plan.HasSoundVm && soundVmText == null)
            {
                Folder(output, destination + "/Resources/SourceData");
                soundVmText = WriteText(output, destination + "/Resources/" +
                    SourceInterfacePackagePlan.SoundVmFunctionsResourceKey + ".bytes", plan.CopySoundVmFunctions());
            }
            var services = ScriptableObject.CreateInstance<SourceInterfaceServices>();
            if (profile.UsesC2Data)
            {
                string callbackKey = profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackResourceKey :
                    SourceInterfacePackagePlan.C2CallbackResourceKey;
                string callbackHash = profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackSha256 :
                    SourceInterfacePackagePlan.C2CallbackSha256;
                TextAsset callbacks = FindOrWriteC2(output, destination, callbackKey,
                    ".json", plan.CopyC2Callbacks(), callbackHash);
                TextAsset hit = FindOrWriteC2(output, destination, SourceInterfacePackagePlan.C2HitResourceKey,
                    ".bytes", plan.CopyC2HitExtension(), SourceInterfacePackagePlan.C2HitSha256);
                if (profile.IsContact281C2M1)
                    services.InitializeContact281C2M1(animations, hud, bookText, plan.Configuration.Match, plan.Configuration.Presentation,
                        plan.Configuration.NativeGamepadsEnabled, bootstrapText, plan.BootstrapSha256,
                        soundVmText, SourceInterfacePackagePlan.SoundVmFunctionsSha256, callbacks, hit);
                else
                    services.InitializeContact281C2(animations, hud, bookText, plan.Configuration.Match, plan.Configuration.Presentation,
                        plan.Configuration.NativeGamepadsEnabled, bootstrapText, plan.BootstrapSha256,
                        soundVmText, SourceInterfacePackagePlan.SoundVmFunctionsSha256, callbacks, hit);
            }
            else if (profile.IsContact272)
                services.InitializeContact272(animations, hud, bookText, plan.Configuration.Match, plan.Configuration.Presentation,
                    plan.Configuration.NativeGamepadsEnabled, bootstrapText, plan.BootstrapSha256,
                    soundVmText, SourceInterfacePackagePlan.SoundVmFunctionsSha256);
            else if (plan.HasSoundVm)
                services.Initialize(animations, hud, bookText, plan.Configuration.Match, plan.Configuration.Presentation,
                    plan.Configuration.NativeGamepadsEnabled, bootstrapText, plan.BootstrapSha256,
                    soundVmText, SourceInterfacePackagePlan.SoundVmFunctionsSha256);
            else if (profile.IsMovement157)
                services.Initialize(animations, hud, bookText, plan.Configuration.Match, plan.Configuration.Presentation,
                    plan.Configuration.NativeGamepadsEnabled, bootstrapText, plan.BootstrapSha256);
            else
                services.Initialize(animations, hud, bookText, plan.Configuration.Match, plan.Configuration.Presentation,
                    plan.Configuration.NativeGamepadsEnabled);
            output.NewPath(servicesPath); AssetDatabase.CreateAsset(services, servicesPath);
            AssetDatabase.SaveAssets();
            SourceTeamInterfaceChecks.VerifySaved(plan, destination, false);
            Debug.Log("PASS saved explicit " + plan.SelectedServicesResourceKey + " HUD/services Resources. Shared identical HUD/movement/SAFN bytes " +
                "are reused, not overwritten. Import/save is not framebuffer/native-game/browser proof; raw/generated data stays ignored.");
            return services;
        }

        internal static void RequireAnimationProfileAsset(SourceTeamResourceProfile profile, SourceAnimationLibraryAsset asset)
        {
            if (asset == null) throw new SourceInterfaceException("Import the exact canonical animation resource for " + profile.StableId + ".");
            string path = AssetDatabase.GetAssetPath(asset);
            bool allowed = path == profile.AnimationAssetPath || profile.UsesC2Data &&
                (path == SourceTeamResourceProfile.Contact281C2.AnimationAssetPath ||
                 path == SourceTeamResourceProfile.Contact281C2M1.AnimationAssetPath);
            if (!allowed || FindUniqueResourcePath("SourceAnimationLibraryAsset", profile.AnimationResourceKey) != path ||
                !ReferenceEquals(Resources.Load<SourceAnimationLibraryAsset>(profile.AnimationResourceKey), asset))
                throw new SourceInterfaceException("Animation resource must be unique and canonical; only byte-identical e874 animation is shared across explicit C2 generations.");
            profile.RequireLibrary(asset.Load());
        }

        private static TextAsset FindOrWriteC2(AuthorizedAssetRoot output, string destination, string key,
            string suffix, byte[] bytes, string hash)
        {
            string path = FindUniqueResourcePath("TextAsset", key);
            TextAsset asset = path == null ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                if (path != null) throw new SourceInterfaceException("C2 resource is not an actual TextAsset: " + key);
                Folder(output, destination + "/Resources/SourceData");
                asset = WriteText(output, destination + "/Resources/" + key + suffix, bytes);
            }
            if (AuthorizedAssetRoot.Sha256(asset.bytes) != hash)
                throw new SourceInterfaceException("Existing immutable C2 data differs; no overwrite or old-data alias.");
            return asset;
        }

        internal static string ServicesSuffix(SourceInterfacePackagePlan plan)
        { return "/Resources/" + plan.SelectedServicesResourceKey + ".asset"; }

        internal static string FindUniqueResourcePath(string type, string resourceKey)
        {
            string found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + type))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                int marker = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
                if (marker < 0) continue;
                string key = path.Substring(marker + "/Resources/".Length);
                int dot = key.LastIndexOf('.');
                if (dot >= 0) key = key.Substring(0, dot);
                if (!string.Equals(key, resourceKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) throw new SourceInterfaceException("Duplicate original Resources identity: " + resourceKey);
                found = path;
            }
            return found;
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
