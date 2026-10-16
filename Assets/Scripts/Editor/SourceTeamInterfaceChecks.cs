using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
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

        public static void VerifySaved(SourceInterfacePackagePlan plan, string destination, bool requireAtlasPixels)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            string hudPath = destination + SourceTeamInterfaceImporter.HudAssetSuffix;
            string servicesPath = destination + SourceTeamInterfaceImporter.ServicesAssetSuffix;
            SourceInterfacePackagePlan.RequireOutputIdentity(hudPath, SourceInterfacePackagePlan.HudResourceKey);
            SourceInterfacePackagePlan.RequireOutputIdentity(servicesPath, SourceInterfacePackagePlan.ServicesResourceKey);
            SourceInterfacePackagePlan.RequireHudShaderSource(Path.GetDirectoryName(Application.dataPath));
            SourceHudAsset hud = AssetDatabase.LoadAssetAtPath<SourceHudAsset>(hudPath);
            SourceInterfaceServices services = AssetDatabase.LoadAssetAtPath<SourceInterfaceServices>(servicesPath);
            Require(hud != null && services != null, "Actual persisted original HUD/services assets are missing.");
            Require(Resources.Load<SourceHudAsset>(SourceHudAsset.ResourceKey) == hud &&
                Resources.Load<SourceInterfaceServices>(SourceInterfaceServices.ResourceKey) == services,
                "Actual Resources lookup resolves another asset/key; duplicate original-interface resources are not permitted.");
            hud.Load();
            ConfigurationEquals(plan.Configuration, services.Configuration(), services.PresentationSettings,
                services.NativeGamepadsEnabled);
            Require(ReferenceEquals(services.Hud, hud), "Saved services refer to a different original HUD.");
            SourceAnimationLibraryAsset animation = Resources.Load<SourceAnimationLibraryAsset>(SourcePlayerProvider.TeamAnimationResourceKey);
            Require(animation != null && animation.InventorySha256 == SourceInterfacePackagePlan.AnimationSha256 &&
                ReferenceEquals(animation.Load(), services.Animations), "Saved services do not reuse the SAME final117 animation library.");
            Require(services.Playbook.Plays.Count == plan.Playbook.Plays.Count &&
                services.Playbook.Routes.Count == plan.Playbook.Routes.Count, "Saved actual converted P1 catalog inventory changed.");
            var savedHud = new SerializedObject(hud);
            TextAsset manifest = savedHud.FindProperty("manifest").objectReferenceValue as TextAsset;
            Require(manifest != null && AuthorizedAssetRoot.Sha256(manifest.bytes) == SourceInterfacePackagePlan.HudSha256 &&
                savedHud.FindProperty("manifestSha256").stringValue == SourceInterfacePackagePlan.HudSha256 &&
                AssetDatabase.GetAssetPath(manifest) == destination + "/Resources/BlitzSourceInterface/interface.json",
                "Persisted HUD manifest bytes/pin differ.");
            Shader shader = savedHud.FindProperty("sourceHudShader").objectReferenceValue as Shader;
            Require(shader != null && shader.name == SourceTeamInterfaceImporter.ShaderName &&
                hud.Material != null && hud.Material.shader == shader &&
                AssetDatabase.GetAssetPath(shader) == SourceInterfacePackagePlan.HudShaderPath &&
                !ShaderUtil.ShaderHasError(shader), "Missing/wrong actual original HUD shader/material reference.");
            SerializedProperty bytes = savedHud.FindProperty("textureBytes");
            Require(bytes != null && bytes.arraySize == plan.Textures.Count, "Saved original rawRGBA reference inventory changed.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long boundBytes = 0, pixelBytes = 0;
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
                    destination + "/Resources/BlitzSourceInterface/Bytes/" + id + ".bytes",
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
            string report = "{\n\"schema\":\"source-interface-package-native/v1\",\n\"bindingsVerified\":true," +
                "\n\"atlasPixelsVerified\":" + (requireAtlasPixels ? "true" : "false") +
                ",\n\"atlasRawBytes\":" + boundBytes + ",\n\"nativeReadbackBytes\":" + pixelBytes +
                ",\n\"configurationSha256\":\"" + plan.ConfigurationSha256 +
                "\",\n\"hudManifestSha256\":\"" + SourceInterfacePackagePlan.HudSha256 +
                "\",\n\"animationManifestSha256\":\"" + SourceInterfacePackagePlan.AnimationSha256 +
                "\",\n\"playbookSha256\":\"" + SourceInterfacePackagePlan.PlaybookSha256 +
                "\",\n\"unityVersion\":\"" + Application.unityVersion +
                "\",\n\"graphicsDeviceType\":\"" + SystemInfo.graphicsDeviceType +
                "\",\n\"graphicsDeviceName\":\"" + Escape(SystemInfo.graphicsDeviceName) +
                "\",\n\"freshDomainRequired\":true,\n\"M1Loaded\":false,\n\"gameOrBrowserParityClaimed\":false\n}\n";
            string output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, requireAtlasPixels ? "source-interface-native-pixels.json" :
                "source-interface-native-bindings.json"), report);
            Debug.Log("PASS actual saved original HUD/services " + (requireAtlasPixels ? "bindings + ALL native atlas pixels" :
                "bindings ONLY; no atlas framebuffer/native-game proof") + "; explicit configuration and SAME final117 library preserved.");
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
