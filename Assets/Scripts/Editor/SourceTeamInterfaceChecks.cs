using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Text;
using Blitz.Presentation;
using Blitz.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
#endif

namespace Blitz.Editor
{
    public static class SourceTeamInterfaceChecks
    {
        public static void ConfigurationEquals(SourceInterfacePackageConfiguration expected,
            SourceMatchConfiguration actual, SourcePresentationSettings presentation, bool nativeGamepads)
        {
            Require(expected != null && actual != null && presentation != null, "Actual saved configuration/settings are missing.");
            SourceMatchConfiguration match = expected.Match;
            Require(match.Profile == actual.Profile && match.StationLayout == actual.StationLayout &&
                match.FirstPossession == actual.FirstPossession && match.Captain == actual.Captain &&
                match.TimeAdjustment == actual.TimeAdjustment && match.Difficulty == actual.Difficulty &&
                match.ContinueAfterCabinetGate == actual.ContinueAfterCabinetGate, "Saved source match/cabinet scalar setting changed.");
            TeamEquals(match.HomeTeam, actual.HomeTeam); TeamEquals(match.AwayTeam, actual.AwayTeam);
            Require(match.Stations.Count == actual.Stations.Count, "Saved source station inventory changed.");
            for (int i = 0; i < match.Stations.Count; i++)
            {
                SourceStationConfiguration a = match.Stations[i], b = actual.Stations[i];
                Require(a.StationId == b.StationId && a.Team == b.Team && a.InitialPlayerId == b.InitialPlayerId &&
                    a.Profile.IsSpecified == b.Profile.IsSpecified && a.Profile.TeamIndex == b.Profile.TeamIndex &&
                    a.Profile.RosterIndex == b.Profile.RosterIndex, "Saved actual source station/profile identity changed.");
            }
            PlayEquals(match.HomePlay, actual.HomePlay); PlayEquals(match.AwayPlay, actual.AwayPlay);
            Require(expected.Presentation.DesignatedHomeSide == presentation.DesignatedHomeSide &&
                expected.Presentation.PupBigBall == presentation.PupBigBall &&
                expected.Presentation.StationHeads.SequenceEqual(presentation.StationHeads) &&
                expected.NativeGamepadsEnabled == nativeGamepads, "Saved actual home/head/pup/gamepad settings changed or defaulted.");
        }

        private static void TeamEquals(SourceTeamSelection a, SourceTeamSelection b)
        {
            Require(a.TeamIndex == b.TeamIndex && a.RosterIndices.SequenceEqual(b.RosterIndices),
                "Saved actual source team/seven static-record bindings changed.");
        }
        private static void PlayEquals(SourcePlaySelection a, SourcePlaySelection b)
        {
            Require(a.IsSpecified == b.IsSpecified && a.PlaySymbol == b.PlaySymbol && a.Playbook == b.Playbook &&
                a.SlotIndex == b.SlotIndex && a.PageIndex == b.PageIndex && a.Pool == b.Pool && a.PoolIndex == b.PoolIndex,
                "Saved original play/page/slot/weighted-pool identity changed.");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new SourceInterfaceException(message); }

        public static void MovementEquals(SourceInterfaceMovementBootstrap expected,
            SourceInterfaceMovementBootstrap actual, SourceMovementSettings settings)
        {
            Require(expected != null && actual != null && settings != null &&
                expected.ContentSha256 == actual.ContentSha256 &&
                expected.FieldIndex == actual.FieldIndex && expected.InfiniteTurboTeamMask == actual.InfiniteTurboTeamMask &&
                expected.SpeedPowerup == actual.SpeedPowerup && expected.FastTurboTeamMask == actual.FastTurboTeamMask &&
                expected.RunOutOfBoundsTeamMask == actual.RunOutOfBoundsTeamMask &&
                expected.ViolencePowerup == actual.ViolencePowerup &&
                expected.HorizontalResolution == actual.HorizontalResolution && expected.VerticalResolution == actual.VerticalResolution,
                "Saved explicit M1 bootstrap/settings/original DIP changed or were inferred.");
            Require(settings.FieldIndex == expected.FieldIndex && settings.InfiniteTurboTeamMask == expected.InfiniteTurboTeamMask &&
                settings.SpeedPowerup == expected.SpeedPowerup && settings.FastTurboTeamMask == expected.FastTurboTeamMask &&
                settings.RunOutOfBoundsTeamMask == expected.RunOutOfBoundsTeamMask &&
                settings.ViolencePowerup == expected.ViolencePowerup, "Saved actual M1 team masks/global powerups changed.");
        }

#if UNITY_EDITOR
        public static void VerifyFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromArguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, true);
        }

        public static void VerifyBindingsFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromArguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, false);
        }

        public static void VerifyMovement157FromCommandLine()
        { VerifyMovement157(true); }

        public static void VerifyMovement157BindingsFromCommandLine()
        { VerifyMovement157(false); }

        public static void VerifySoundVmFromCommandLine()
        { VerifySoundVm(true); }

        public static void VerifySoundVmBindingsFromCommandLine()
        { VerifySoundVm(false); }

        public static void VerifyContact272FromCommandLine()
        { VerifyContact272(true); }

        public static void VerifyContact272BindingsFromCommandLine()
        { VerifyContact272(false); }

        public static void VerifyContact281C2FromCommandLine() { VerifyContact281C2(true); }
        public static void VerifyContact281C2BindingsFromCommandLine() { VerifyContact281C2(false); }

        public static void VerifyContact281C2M1FromCommandLine() { VerifyContact281C2M1(true); }
        public static void VerifyContact281C2M1BindingsFromCommandLine() { VerifyContact281C2M1(false); }

        private static void VerifyContact281C2M1(bool pixels)
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromContact281C2M1Arguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, pixels);
        }

        private static void VerifyContact281C2(bool pixels)
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromContact281C2Arguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, pixels);
        }

        private static void VerifyContact272(bool pixels)
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromContact272Arguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, pixels);
        }

        private static void VerifySoundVm(bool pixels)
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromSoundVmArguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, pixels);
        }

        private static void VerifyMovement157(bool pixels)
        {
            string[] args = Environment.GetCommandLineArgs();
            SourceInterfacePackagePlan plan = SourceTeamInterfaceImporter.PlanFromMovement157Arguments(args);
            string id = PlayerAssetImporter.RequiredArgument(args, "-interfaceStableId");
            PlayerImportPlan.ValidateStableId(id);
            VerifySaved(plan, "Assets/Imported/" + id, pixels);
        }

        public static void VerifySaved(SourceInterfacePackagePlan plan, string destination, bool requireAtlasPixels)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            SourceTeamResourceProfile profile = plan.Profile;
            string servicesPath = destination + SourceTeamInterfaceImporter.ServicesSuffix(plan);
            SourceInterfacePackagePlan.RequireOutputIdentity(servicesPath, plan.SelectedServicesResourceKey);
            SourceInterfacePackagePlan.RequireHudShaderSource(Path.GetDirectoryName(Application.dataPath));
            SourceHudAsset hud = Resources.Load<SourceHudAsset>(SourceHudAsset.ResourceKey);
            SourceInterfaceServices services = AssetDatabase.LoadAssetAtPath<SourceInterfaceServices>(servicesPath);
            Require(hud != null && services != null, "Actual persisted original HUD/services assets are missing.");
            Require(SourceInterfaceServices.ResourceKeyFor(profile) == plan.SelectedServicesResourceKey &&
                Resources.Load<SourceInterfaceServices>(plan.SelectedServicesResourceKey) == services &&
                SourceTeamInterfaceImporter.FindUniqueResourcePath("SourceInterfaceServices", plan.SelectedServicesResourceKey) == servicesPath,
                "Actual services Resources lookup/profile differs or is duplicated.");
            ConfigurationEquals(plan.Configuration, services.Configuration(), services.PresentationSettings,
                services.NativeGamepadsEnabled);
            Require(services.Profile.StableId == profile.StableId, "Saved services explicit generation tag differs; old resources are not upgraded.");
            Require(ReferenceEquals(services.Hud, hud), "Saved services refer to a different original HUD.");
            if (profile.IsContact281C2M1)
            {
                SourceTeamInterfaceImporter.RequirePossessionSpeechResource();
                Require(hud.PossessionSound != null, "Saved M1 HUD must load the pinned canonical CAMBOT speech operands.");
            }
            SourceAnimationLibraryAsset animation = Resources.Load<SourceAnimationLibraryAsset>(profile.AnimationResourceKey);
            SourceTeamInterfaceImporter.RequireAnimationProfileAsset(profile, animation);
            Require(animation != null && animation.InventorySha256 == profile.AnimationSha256 &&
                ReferenceEquals(animation.Load(), services.Animations), "Saved services do not reuse the SAME explicit animation profile/library.");
            SourceAnimationLibrary actualLibrary = animation.Load() as SourceAnimationLibrary;
            Require(actualLibrary != null, "Saved actual typed animation library is missing.");
            profile.RequireInventory(animation.InventorySha256, actualLibrary.SequenceNames.Count, actualLibrary.FrameCount);
            if (profile.IsContact272 || profile.UsesC2Data)
            {
                SourceTeamPresentationAsset teams = Resources.Load<SourceTeamPresentationAsset>(profile.TeamResourceKey);
                Require(teams != null && teams.Template.StableId == profile.StableId,
                    "Saved Contact272 team/template canonical profile differs.");
                profile.RequireImpact(SourcePlayerProvider.ResolveRequiredCinematicLibrary(teams.Template));
            }
            Require(services.Playbook.Plays.Count == plan.Playbook.Plays.Count &&
                services.Playbook.Routes.Count == plan.Playbook.Routes.Count, "Saved actual converted P1 catalog inventory changed.");
            long boundBytes, pixelBytes;
            VerifyHudBindings(plan, hud, requireAtlasPixels, out boundBytes, out pixelBytes);
            var savedServices = new SerializedObject(services);
            TextAsset book = savedServices.FindProperty("originalPlaybook").objectReferenceValue as TextAsset;
            Require(book != null && AuthorizedAssetRoot.Sha256(book.bytes) == SourceInterfacePackagePlan.PlaybookSha256 &&
                AssetDatabase.GetAssetPath(book) == destination + "/Resources/BlitzSourceInterface/playbook.bytes",
                "Saved services P1 TextAsset bytes/reference changed.");
            Require(savedServices.FindProperty("originalAnimations").objectReferenceValue == animation &&
                savedServices.FindProperty("originalHud").objectReferenceValue == hud,
                "Persisted services reference different animation/HUD assets.");
            TextAsset config = AssetDatabase.LoadAssetAtPath<TextAsset>(destination + "/Resources/BlitzSourceInterface/configuration.json");
            Require(config != null && AuthorizedAssetRoot.Sha256(config.bytes) == plan.ConfigurationSha256,
                "Actual saved explicit configuration bytes changed.");
            if (profile.UsesMovementData)
            {
                TextAsset bootstrap = savedServices.FindProperty("originalMovementBootstrap").objectReferenceValue as TextAsset;
                Require(bootstrap != null && AuthorizedAssetRoot.Sha256(bootstrap.bytes) == plan.BootstrapSha256 &&
                    AssetDatabase.GetAssetPath(bootstrap) == destination + "/Resources/BlitzSourceInterface/movement-bootstrap.json" &&
                    savedServices.FindProperty("movementBootstrapSha256").stringValue == plan.BootstrapSha256,
                    "Actual saved explicit M1 bootstrap reference/bytes/pin changed.");
                MovementEquals(plan.MovementBootstrap, services.MovementBootstrap, services.MovementSettings);
                string movementPath = SourceTeamInterfaceImporter.FindUniqueResourcePath("TextAsset", SourceInterfaceMovementData.ResourceKey);
                TextAsset movement = Resources.Load<TextAsset>(SourceInterfaceMovementData.ResourceKey);
                Require(movement != null && AssetDatabase.GetAssetPath(movement) == movementPath &&
                    movementPath.StartsWith("Assets/Imported/", StringComparison.Ordinal) &&
                    movementPath.EndsWith("/Resources/" + SourceInterfaceMovementData.ResourceKey + ".json", StringComparison.Ordinal) &&
                    movement.bytes.SequenceEqual(plan.CopyMovement()), "Saved actual runtime movement data is missing/duplicated/changed or outside ignored resources.");
                SourceInterfaceMovementData.Read(movement.bytes, SourceInterfaceMovementData.ContentSha256);
                Require(services.MovementData.Friction.SequenceEqual(plan.MovementRuntime.Friction) &&
                    services.MovementData.Defense.Count == plan.MovementRuntime.Defense.Count,
                    "Actual typed runtime movement data was not loaded from the pinned resource.");
                for (int i = 0; i < plan.MovementRuntime.Defense.Count; i++)
                {
                    SourceInterfaceDefenseRow expected = plan.MovementRuntime.Defense[i];
                    SourceMovementDefenseRow actual = services.MovementData.Defense[i];
                    Require(actual.LineSpeeds.SequenceEqual(expected.Line) && actual.BackSpeeds.SequenceEqual(expected.Back) &&
                        actual.AccelerationBase == expected.Base && actual.AccelerationRange == expected.Range &&
                        actual.PlowProbability == expected.Plow, "Actual typed movement defense row changed at " + i + ".");
                }
            }
            if (plan.HasSoundVm)
            {
                string soundPath = SourceTeamInterfaceImporter.FindUniqueResourcePath("TextAsset",
                    SourceInterfacePackagePlan.SoundVmFunctionsResourceKey);
                TextAsset sound = Resources.Load<TextAsset>(SourceInterfacePackagePlan.SoundVmFunctionsResourceKey);
                Require(sound != null && AssetDatabase.GetAssetPath(sound) == soundPath &&
                    soundPath.StartsWith("Assets/Imported/", StringComparison.Ordinal) &&
                    soundPath.EndsWith("/Resources/" + SourceInterfacePackagePlan.SoundVmFunctionsResourceKey + ".bytes", StringComparison.Ordinal) &&
                    sound.bytes.SequenceEqual(plan.CopySoundVmFunctions()),
                    "Saved exact SAFN1 resource is missing, aliased, duplicated, outside ignored Resources, or has changed bytes.");
                SourceInterfacePackagePlan.ReadSoundVmFunctions(sound.bytes, SourceInterfacePackagePlan.SoundVmFunctionsSha256);
                Require(SourceInterfaceServices.SoundVmFunctionsResourceKey == SourceInterfacePackagePlan.SoundVmFunctionsResourceKey &&
                    savedServices.FindProperty("originalSoundVmFunctions").objectReferenceValue == sound &&
                    savedServices.FindProperty("soundVmFunctionsSha256").stringValue == SourceInterfacePackagePlan.SoundVmFunctionsSha256,
                    "Saved sound services SAFN reference/pin is not the same canonical resource.");
                Require(services.SoundVmData != null &&
                    services.SoundVmData.PayloadSha256 == plan.SoundVmData.PayloadSha256,
                    "Saved sound services did not load the exact mandatory source SoundVM function data.");
            }
            if (plan.HasC2)
            {
                VerifyC2Text(savedServices, "originalC2Callbacks",
                    profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackResourceKey : SourceInterfacePackagePlan.C2CallbackResourceKey,
                    plan.CopyC2Callbacks(),
                    profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackSha256 : SourceInterfacePackagePlan.C2CallbackSha256);
                VerifyC2Text(savedServices, "originalC2HitExtension", SourceInterfacePackagePlan.C2HitResourceKey,
                    plan.CopyC2HitExtension(), SourceInterfacePackagePlan.C2HitSha256);
                Require(services.C2Data.PayloadSha256 == plan.C2Data.PayloadSha256 &&
                    services.C2Data.AnimationManifestSha256 == profile.AnimationSha256,
                    "Saved C2 data/library/bootstrap identity differs; C2 entry must precede initial AI draws.");
            }
            string report = "{\n\"schema\":\"source-interface-package-native/v2\",\n\"bindingsVerified\":true," +
                "\n\"atlasPixelsVerified\":" + (requireAtlasPixels ? "true" : "false") +
                ",\n\"atlasRawBytes\":" + boundBytes + ",\n\"nativeReadbackBytes\":" + pixelBytes +
                ",\n\"configurationSha256\":\"" + plan.ConfigurationSha256 +
                "\",\n\"hudManifestSha256\":\"" + SourceInterfacePackagePlan.HudSha256 +
                "\",\n\"animationManifestSha256\":\"" + profile.AnimationSha256 +
                "\",\n\"playbookSha256\":\"" + SourceInterfacePackagePlan.PlaybookSha256 +
                "\",\n\"profile\":\"" + profile.StableId +
                "\",\n\"bootstrapSha256\":\"" + plan.BootstrapSha256 +
                "\",\n\"servicesResourceKey\":\"" + plan.SelectedServicesResourceKey +
                "\",\n\"soundVmFunctionsSha256\":\"" + (plan.HasSoundVm ? plan.SoundVmData.PayloadSha256 : "") +
                "\",\n\"unityVersion\":\"" + Application.unityVersion +
                "\",\n\"graphicsDeviceType\":\"" + SystemInfo.graphicsDeviceType +
                "\",\n\"graphicsDeviceName\":\"" + Escape(SystemInfo.graphicsDeviceName) +
                "\",\n\"freshDomainRequired\":true,\n\"M1DataLoaded\":" + (profile.UsesMovementData ? "true" : "false") +
                ",\n\"gameOrBrowserParityClaimed\":false\n}\n";
            string output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, requireAtlasPixels ? "source-interface-native-pixels.json" :
                "source-interface-native-bindings.json"), report);
            Debug.Log("PASS actual saved " + profile.StableId + " HUD/services " + (requireAtlasPixels ?
                "bindings + ALL native atlas pixels" : "bindings ONLY; no framebuffer/native-game proof") +
                "; explicit settings and SAME selected animation library preserved.");
        }

        private static void VerifyC2Text(SerializedObject services, string field, string key, byte[] expected, string hash)
        {
            string path = SourceTeamInterfaceImporter.FindUniqueResourcePath("TextAsset", key);
            TextAsset actual = Resources.Load<TextAsset>(key);
            Require(actual != null && path != null && AssetDatabase.GetAssetPath(actual) == path &&
                path.StartsWith("Assets/Imported/", StringComparison.Ordinal) &&
                services.FindProperty(field).objectReferenceValue == actual && actual.bytes.SequenceEqual(expected) &&
                AuthorizedAssetRoot.Sha256(actual.bytes) == hash,
                "Saved C2 callback/ONE7538 resource is missing, duplicated, aliased, changed or not canonical: " + key);
        }

        internal static void VerifyHudBindings(SourceInterfacePackagePlan plan, SourceHudAsset hud, bool requireAtlasPixels,
            out long boundBytes, out long pixelBytes)
        {
            Require(hud != null, "Actual shared original HUD resource is missing.");
            SourceInterfacePackagePlan.RequireHudShaderSource(Path.GetDirectoryName(Application.dataPath));
            string hudPath = AssetDatabase.GetAssetPath(hud);
            SourceInterfacePackagePlan.RequireOutputIdentity(hudPath, SourceInterfacePackagePlan.HudResourceKey);
            Require(SourceTeamInterfaceImporter.FindUniqueResourcePath("SourceHudAsset", SourceInterfacePackagePlan.HudResourceKey) == hudPath &&
                Resources.Load<SourceHudAsset>(SourceHudAsset.ResourceKey) == hud, "Shared original HUD resource is duplicated or resolves another asset.");
            string folder = hudPath.Substring(0, hudPath.LastIndexOf('/'));
            hud.Load();
            var savedHud = new SerializedObject(hud);
            TextAsset manifest = savedHud.FindProperty("manifest").objectReferenceValue as TextAsset;
            Require(manifest != null && AuthorizedAssetRoot.Sha256(manifest.bytes) == SourceInterfacePackagePlan.HudSha256 &&
                savedHud.FindProperty("manifestSha256").stringValue == SourceInterfacePackagePlan.HudSha256 &&
                AssetDatabase.GetAssetPath(manifest) == folder + "/interface.json",
                "Persisted HUD manifest bytes/pin differ.");
            Shader shader = savedHud.FindProperty("sourceHudShader").objectReferenceValue as Shader;
            Require(shader != null && shader.name == SourceTeamInterfaceImporter.ShaderName &&
                hud.Material != null && hud.Material.shader == shader &&
                AssetDatabase.GetAssetPath(shader) == SourceInterfacePackagePlan.HudShaderPath &&
                !ShaderUtil.ShaderHasError(shader), "Missing/wrong actual original HUD shader/material reference.");
            SerializedProperty bytes = savedHud.FindProperty("textureBytes");
            Require(bytes != null && bytes.arraySize == plan.Textures.Count, "Saved original rawRGBA reference inventory changed.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            boundBytes = pixelBytes = 0;
            for (int i = 0; i < bytes.arraySize; i++)
            {
                SerializedProperty entry = bytes.GetArrayElementAtIndex(i);
                string id = entry.FindPropertyRelative("id").stringValue;
                TextAsset data = entry.FindPropertyRelative("data").objectReferenceValue as TextAsset;
                SourceInterfacePackageTexture expected = plan.Textures.FirstOrDefault(t => t.Definition.Id == id);
                Require(expected != null && data != null && ids.Add(id), "Unknown/duplicate/missing persisted original HUD texture.");
                byte[] raw = data.bytes;
                Require(AuthorizedAssetRoot.Sha256(raw) == expected.Definition.Sha256 &&
                    raw.SequenceEqual(expected.CopyBytes()) && AssetDatabase.GetAssetPath(data) ==
                    folder + "/Bytes/" + id + ".bytes",
                    "Actual saved rawRGBA bytes/path differ from original atlas identity: " + id);
                plan.Hud.RequireTextureBytes(id, raw);
                Texture2D texture = hud.Texture(id);
                Require(texture.name == "source-interface-" + expected.Definition.Sha256 &&
                    texture.width == expected.Definition.Width && texture.height == expected.Definition.Height &&
                    texture.format == TextureFormat.RGBA32 && texture.mipmapCount == 1 &&
                    texture.filterMode == (expected.Definition.PointSampled ? FilterMode.Point : FilterMode.Bilinear) &&
                    texture.wrapMode == (id == "interface" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat),
                    "Actual native original atlas content identity/geometry/sampler changed: " + id);
                boundBytes += raw.Length;
                if (requireAtlasPixels) pixelBytes += VerifyNativePixels(texture, raw);
            }
        }

        private static long VerifyNativePixels(Texture2D source, byte[] expected)
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null &&
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0,
                "Actual graphics device/copy support required for original atlas pixel proof. Null headless bindings are not a native pixel pass.");
            var descriptor = new RenderTextureDescriptor(source.width, source.height) {
                graphicsFormat = source.graphicsFormat, depthBufferBits = 0, msaaSamples = 1, mipCount = 1
            };
            var target = new RenderTexture(descriptor);
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                Require(target.Create(), "Could not create actual atlas readback target.");
                Graphics.CopyTexture(source, 0, 0, target, 0, 0);
                RenderTexture.active = target;
                readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); readback.Apply();
                Color32[] pixels = readback.GetPixels32();
                Require(pixels.Length * 4 == expected.Length, "Actual native atlas readback dimensions changed.");
                for (int i = 0; i < pixels.Length; i++)
                {
                    int offset = i * 4; Color32 pixel = pixels[i];
                    Require(pixel.r == expected[offset] && pixel.g == expected[offset + 1] &&
                        pixel.b == expected[offset + 2] && pixel.a == expected[offset + 3],
                        "Actual native original atlas RGBA readback differs at pixel " + i + ".");
                }
                return expected.Length;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null) Object.DestroyImmediate(readback);
                target.Release(); Object.DestroyImmediate(target);
            }
        }
        private static string Escape(string text) { return text.Replace("\\", "\\\\").Replace("\"", "\\\""); }
#endif
    }
}
