#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Blitz.Editor
{
    public static class SourceTeamChecks
    {
        private static long assertions, vertices, indices, rgbaBytes;

        public static void VerifyFromCommandLine()
        { VerifyProfile(SourceTeamResourceProfile.B1Final117); }

        public static void VerifyMovement157FromCommandLine()
        { VerifyProfile(SourceTeamResourceProfile.Movement157); }

        public static void VerifyContact272FromCommandLine()
        { VerifyProfile(SourceTeamResourceProfile.Contact272); }

        public static void VerifyContact281C2FromCommandLine()
        { VerifyProfile(SourceTeamResourceProfile.Contact281C2); }

        private static void VerifyProfile(SourceTeamResourceProfile profile)
        {
            assertions = vertices = indices = rgbaBytes = 0;
            string[] args = Environment.GetCommandLineArgs();
            var input = new AuthorizedAssetRoot(PlayerAssetImporter.RequiredArgument(args, "-teamAssetRoot"));
            SourceTeamPresentationAsset asset = Resources.Load<SourceTeamPresentationAsset>(profile.TeamResourceKey);
            if (asset == null) throw new PlayerAssetException("Native check needs the actual persisted original team Resources asset.");
            asset.ValidateGeneratedPresentation();
            Equal(asset.Template.StableId, profile.StableId, "actual saved template/profile identity");
            SourceTeamRosterLibrary library = asset.Rosters;
            SourceTeamPresentationData catalog = asset.Catalog;
            SourceAnimationLibraryAsset animations = Resources.Load<SourceAnimationLibraryAsset>(profile.AnimationResourceKey);
            Check(animations != null, "actual persisted explicit animation Resources");
            Equal(animations.InventorySha256, profile.AnimationSha256, "actual immutable animation profile manifest identity");
            Equal(AssetDatabase.GetAssetPath(animations), profile.AnimationAssetPath, "actual saved animation Resources path/profile");
            ISourceAnimationLibrary animationLibrary = animations.Load();
            SourceAnimationLibrary actualLibrary = animationLibrary as SourceAnimationLibrary;
            Check(actualLibrary != null, "actual typed animation library");
            profile.RequireInventory(animations.InventorySha256, actualLibrary.SequenceNames.Count, actualLibrary.FrameCount);
            Func<string, string> read = path =>
            {
                byte[] bytes = input.ReadBytes(path, PlayerImportLimits.SourceBytes); catalog.VerifyInput(path, bytes);
                return AuthorizedAssetRoot.DecodeUtf8(bytes, path);
            };
            SourcePlayer basePlayer = null;
            for (int m = 0; m < 32; m++)
            {
                SourcePlayer source = SourceTeamGeneratedModels.ReadModel(library, m, read);
                if (m == 0) basePlayer = source;
                IList<BindPoseLimbMesh> meshes = SourceTeamGeneratedModels.BuildModel(source);
                for (int j = 0; j < 15; j++) MeshEquals(meshes[j], asset.Model(m).Joint(j));
            }
            for (int h = 0; h < 20; h++)
                MeshEquals(SourceTeamGeneratedModels.BuildModel(SourceTeamGeneratedModels.ReadModel(library, 0, read, h))[8],
                    asset.Head(h).Limb);
            var materialById = new Dictionary<string, SourceTeamMaterialAsset>(StringComparer.Ordinal);
            foreach (SourceTeamMaterialAsset material in asset.MaterialInventory) materialById.Add(material.SourceTextureId, material);
            foreach (SourceTeamTexture source in catalog.Textures)
            {
                Texture2D texture = materialById[source.Id].Material.mainTexture as Texture2D;
                byte[] bytes = input.ReadBytes(source.Path, PlayerImportLimits.TextureBytes); catalog.VerifyTexture(source, bytes);
                Check(texture != null && texture.isReadable && texture.wrapMode == TextureWrapMode.Repeat &&
                    texture.filterMode == FilterMode.Bilinear && texture.mipmapCount == 1, "original native team texture format/sampler " + source.Id);
                Color32[] actual = texture.GetPixels32();
                TgaImageInfo info = TgaImageInfo.Read(bytes, source.Path);
                Equal(actual.Length, info.Width * info.Height, "native original texture pixels " + source.Id);
                for (int y = 0; y < info.Height; y++)
                for (int x = 0; x < info.Width; x++)
                    Equal(actual[y * info.Width + x], Pixel(bytes, info, x, y), "actual original RGBA pixel " + source.Id);
                rgbaBytes += actual.Length * 4L;
                Equal(materialById[source.Id].Material.name, source.Id, "persisted original material identity");
                Check(!materialById[source.Id].Material.HasProperty("_Color") ||
                    materialById[source.Id].Material.color == Color.white, "no team multiplicative tint");
            }
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            string jerseySource = read("models/jerseys.h");
            foreach (SourceRosterTeam team in library.TeamData)
            foreach (SourceRosterPlayer player in team.Players)
            foreach (bool high in new[] { false, true })
            {
                PlayerView view = SourceTeamPoseFixture.View(player, 3, new FieldVector(45, 0),
                    animationLibrary.GetClip("QBSTANCE"),
                    0, 256, 0, "native_explicit_pose_fixture", 0);
                SourceTeamAppearance appearance = SourceTeamPresentationModel.Select(library, team.Roster.TeamIndex,
                    (team.Roster.TeamIndex + 1) % 31, 0, view, 3, new SourceTeamDrawState(high, true, false, false, false));
                string key = appearance.Numbers.LimbSymbol + ":" + player.Profile.PackedJerseyNumber;
                if (!numbers.Add(key)) continue;
                Mesh mesh = asset.NumberMesh(appearance);
                MeshEquals(SourceTeamGeneratedModels.BuildNumbers(jerseySource, appearance.Numbers.LimbSymbol,
                    player.Profile.PackedJerseyNumber, basePlayer.Nodes[1].BindPivot), new SourceTeamLimbAsset(mesh, new[] { 0 }));
            }
            Equal(numbers.Count, asset.NumberMeshCount, "actual unique original BCD number inventory");
            Material bad = materialById["runtime/FALALOD.WMS"].Material;
            Texture original = bad.mainTexture;
            try
            {
                bad.mainTexture = materialById["runtime/CARHLOD.WMS"].Material.mainTexture;
                Reject(() => asset.ValidateGeneratedPresentation(), "content/identity", "real matching-opponent-texture mutant");
            }
            finally { bad.mainTexture = original; asset.ValidateGeneratedPresentation(); }
            SourceCinematicLibrary cinematics = SourcePlayerProvider.ResolveRequiredCinematicLibrary(asset.Template);
            string images = VerifyActorsAndCamera(asset, animationLibrary, cinematics, input);
            string report = "{\n\"schema\":\"source-team-impact-native/v1\",\n\"assertions\":" + assertions +
                ",\n\"actualVertices\":" + vertices + ",\n\"actualIndices\":" + indices +
                ",\n\"actualOriginalRgbaBytes\":" + rgbaBytes + ",\n\"teams\":31,\"records\":496,\"modelSelectors\":32,\"customHeads\":20" +
                ",\n\"numberMeshes\":" + numbers.Count + ",\n\"catalogSha256\":\"" + asset.CatalogSha256 +
                "\",\n\"animationManifestSha256\":\"" + animations.InventorySha256 +
                "\",\n\"impactCatalogSha256\":\"" + cinematics.ContentSha256 +
                "\",\n\"unityVersion\":\"" + Application.unityVersion + "\",\n\"graphicsDeviceType\":\"" +
                SystemInfo.graphicsDeviceType + "\",\n\"graphicsDeviceName\":\"" + Escape(SystemInfo.graphicsDeviceName) +
                "\",\n\"imageProof\":" + images +
                ",\n\"eligibilityFixture\":\"Original GM_PLAY_OVER; PF_POP_HELMET off; actor/carrier3; recorded plyr_have_ball randrng100=0 then CSI randrng1=0; not a Core decision trace\"" +
                ",\n\"fixture\":\"Explicit original source presentation fixture; not Core gameplay/contact/animation-VM or browser parity\",\n\"fullGameplayParityClaimed\":false\n}\n";
            string build = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build");
            Directory.CreateDirectory(build);
            File.WriteAllText(Path.Combine(build, "source-team-impact-native.json"), report);
            Debug.Log("PASS original team/impact native: " + assertions + " assertions /" + vertices + " vertices /" +
                indices + " indices /" + rgbaBytes + " original RGBA bytes; actual hardware/image status is in the receipt.");
        }

        private static string VerifyActorsAndCamera(SourceTeamPresentationAsset assets, ISourceAnimationLibrary library,
            SourceCinematicLibrary cinematics, AuthorizedAssetRoot input)
        {
            GameObject root = new GameObject("EXPLICIT original team/impact native fixture / not gameplay");
            var rigs = new List<SourcePlayerRig>();
            try
            {
                var presenter = new SourceTeamPresenter(assets);
                var home = assets.Rosters.Lineups.Select(0, "native_fixture_standard_lineup", true);
                var away = assets.Rosters.Lineups.Select(1, "native_fixture_standard_lineup", false);
                var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
                    home, away, new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away) },
                    TeamId.Home, SourceCaptainSlot.First);
                for (int id = 0; id < 14; id++)
                {
                    SourceTeamSelection selection = id < 7 ? home : away;
                    SourceRosterPlayer player = assets.Rosters.GetPlayerData(new SourceRosterIdentity(selection.TeamIndex, selection.RosterIndices[id % 7]));
                    float x = id < 7 ? (id == 3 ? -5f : -1f) : 4f;
                    float z = (id % 7 - 3) * 2.2f;
                    var view = SourceTeamPoseFixture.View(player, id, new FieldVector(50 + x, z), library.GetClip("QBSTANCE"),
                        0, id < 7 ? 256 : 768, 0, "explicit_native_pose_samples", id == 3 ? 0 : id == 10 ? 1 : -1);
                    SourcePlayerRig rig = SourcePlayerRig.InstantiateRequired(assets.Template, root.transform);
                    rig.transform.localPosition = new Vector3(x, 0, z); rig.transform.localScale = Vector3.one / 7.8f;
                    presenter.Apply(rig, view, config, 0, 3, new SourceTeamDrawState(false, id == 3, false, false, false));
                    var animation = new SourceAnimationPlayer(rig, library); animation.Apply(view, view, 1f);
                    rig.ValidateGeneratedRig(); rigs.Add(rig);
                    Equal(rig.TeamAppearance.Player.Profile.SourcePlayerId, player.Profile.SourcePlayerId, "native actual roster identity");
                    Equal(rig.UniformIdentity, id < 7 ? "CARHLOD.WMS" : "FALALOD.WMS", "native distinguishable original opposing uniforms");
                    foreach (Material material in rig.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials))
                        Check(!material.name.Contains(id < 7 ? "FAL" : "CAR"), "actual renderer material cannot borrow opposing uniform");
                }
                Camera camera = new GameObject("Original presentation fixture camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false); camera.nearClipPlane = .3f; camera.farClipPlane = 230f;
                camera.fieldOfView = (float)(2d * Math.Atan(192d / 664d) * 180d / Math.PI); camera.aspect = 16f / 9f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.03f, .05f, .08f);
                float phi = (float)(-25d * Math.PI / 180d), d = 30f * 7.8f;
                var seed = new SourceCinematicSeed(new SourceVector3(-d * (float)Math.Cos(phi), 15.6f - d * (float)Math.Sin(phi), 0),
                    new SourceVector3(0, 15.6f, 0), d, (float)(Math.PI / 2d), phi);
                camera.transform.position = new Vector3(seed.Eye.X, seed.Eye.Y, seed.Eye.Z) / 7.8f;
                camera.transform.LookAt(new Vector3(0, 2, 0));
                bool homeVisible, awayVisible;
                SourceTeamVisibility.ProjectBounds(rigs[3], camera, out homeVisible);
                SourceTeamVisibility.ProjectBounds(rigs[10], camera, out awayVisible);
                Check(homeVisible && awayVisible, "both actual opponent body bounds project visibly, not just14 actors/HUD");
                string normalImage = Capture(camera, "original-opposing-teams.tga");
                int homePixels = normalImage == "null" ? 0 : VisibleJerseySamples(rigs[3], camera, assets, input, "runtime/CARHLOD.WMS", "runtime/FALALOD.WMS");
                int awayPixels = normalImage == "null" ? 0 : VisibleJerseySamples(rigs[10], camera, assets, input, "runtime/FALALOD.WMS", "runtime/CARHLOD.WMS");
                var playback = new SourceCinematicPlayback(cinematics, seed);
                SourceRosterPlayer carrier = rigs[3].TeamAppearance.Player;
                PlayerView impactPose = SourceTeamPoseFixture.View(carrier, 3, new FieldVector(45, 0),
                    library.GetClip("FALL_04"), 0, 256, 0, "fall_04", 0);
                new SourceAnimationPlayer(rigs[3], library).Apply(impactPose, impactPose, 1f);
                Check(playback.ExecuteCameraOpcode(0, 0, 3, "fall_04", 15969, 0), "actual eligible fall_04 camera table, not every tackle");
                var targets = new SourceCinematicTarget[14];
                for (int i = 0; i < 14; i++)
                    targets[i] = new SourceCinematicTarget(new SourceVector3(rigs[i].transform.position.x * 7.8f, 0,
                        rigs[i].transform.position.z * 7.8f), i < 7 ? 256 : 768);
                playback.CameraSourceTick(0, 1, targets, seed, false);
                var cinematicPresenter = new SourceCinematicPresenter(playback);
                cinematicPresenter.Apply(camera);
                Near(Vector3.Distance(camera.transform.position * 7.8f, new Vector3(playback.Frame.State.Eye.X,
                    playback.Frame.State.Eye.Y, playback.Frame.State.Eye.Z)), 0, .00001f, "actual native source-camera eye");
                string hitImage = Capture(camera, "original-fall04-source-rail.tga");
                playback.CameraSourceTick(1, 0, targets, seed, true);
                cinematicPresenter.Apply(camera);
                Near(Vector3.Distance(camera.transform.position * 7.8f, new Vector3(seed.Eye.X, seed.Eye.Y, seed.Eye.Z)), 0,
                    .00002f, "actual native new-play source return to unchanged normal camera");
                if (normalImage == "null") return "{\"status\":\"graphics-device-null-no-image-claim\",\"normal\":null,\"impact\":null}";
                Check(homePixels > 0 && awayPixels > 0, "actual framebuffer contains discriminating authored jersey pixels for BOTH original uniform IDs");
                return "{\"status\":\"actual-native-renderer-two-uniform-pixels-and-source-camera\",\"normal\":" +
                    normalImage + ",\"impact\":" + hitImage + ",\"homeDiscriminatingPixels\":" + homePixels +
                    ",\"awayDiscriminatingPixels\":" + awayPixels + "}";
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static string Capture(Camera camera, string filename)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "null";
            Texture2D image = Render(camera);
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build");
                Directory.CreateDirectory(directory);
                Color32[] pixels = image.GetPixels32();
                var bytes = new byte[18 + pixels.Length * 4];
                bytes[2] = 2; bytes[12] = 0; bytes[13] = 5; bytes[14] = 208; bytes[15] = 2;
                bytes[16] = 32; bytes[17] = 8;
                for (int i = 0; i < pixels.Length; i++)
                {
                    int offset = 18 + i * 4;
                    bytes[offset] = pixels[i].b; bytes[offset + 1] = pixels[i].g;
                    bytes[offset + 2] = pixels[i].r; bytes[offset + 3] = pixels[i].a;
                }
                File.WriteAllBytes(Path.Combine(directory, filename), bytes);
                return "{\"file\":\"" + filename + "\",\"sha256\":\"" + AuthorizedAssetRoot.Sha256(bytes) + "\"}";
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static Texture2D Render(Camera camera)
        {
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target);
            }
        }

        private static int VisibleJerseySamples(SourcePlayerRig rig, Camera camera, SourceTeamPresentationAsset assets,
            AuthorizedAssetRoot input, string expectedId, string opposingId)
        {
            SourceTeamTexture source = assets.Catalog.Textures.First(t => t.Id == expectedId);
            SourceTeamTexture other = assets.Catalog.Textures.First(t => t.Id == opposingId);
            byte[] bytes = input.ReadBytes(source.Path, PlayerImportLimits.TextureBytes);
            byte[] otherBytes = input.ReadBytes(other.Path, PlayerImportLimits.TextureBytes);
            TgaImageInfo info = TgaImageInfo.Read(bytes, source.Path), otherInfo = TgaImageInfo.Read(otherBytes, other.Path);
            Transform torso = rig.GetJoint(1);
            Mesh mesh = torso.GetComponent<MeshFilter>().sharedMesh;
            SourceTeamLimbAsset limb = assets.Limb(rig.TeamAppearance, 1);
            Texture2D framebuffer = Render(camera);
            try
            {
                Color32[] pixels = framebuffer.GetPixels32(); int matches = 0;
                Vector3[] vertices = mesh.vertices; Vector2[] uv = mesh.uv;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (limb.SourceSlot(sub) != 2) continue;
                    int[] triangles = mesh.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        Vector3 world = torso.TransformPoint((vertices[a] + vertices[b] + vertices[c]) / 3f);
                        Vector3 projected = camera.WorldToViewportPoint(world);
                        Vector2 st = (uv[a] + uv[b] + uv[c]) / 3f;
                        Color32 expected = Pixel(bytes, info, Mathf.Clamp((int)(st.x * info.Width), 0, info.Width - 1),
                            Mathf.Clamp((int)(st.y * info.Height), 0, info.Height - 1));
                        Color32 wrong = Pixel(otherBytes, otherInfo, Mathf.Clamp((int)(st.x * otherInfo.Width), 0, otherInfo.Width - 1),
                            Mathf.Clamp((int)(st.y * otherInfo.Height), 0, otherInfo.Height - 1));
                        if (expected.a < 250 || Difference(expected, wrong) < 100 || projected.z <= .3f ||
                            projected.x <= 0 || projected.x >= 1 || projected.y <= 0 || projected.y >= 1) continue;
                        int x = Mathf.Clamp((int)(projected.x * 1280), 0, 1279), y = Mathf.Clamp((int)(projected.y * 720), 0, 719);
                        if (Difference(pixels[y * 1280 + x], expected) < 75) matches++;
                    }
                }
                return matches;
            }
            finally { Object.DestroyImmediate(framebuffer); }
        }

        private static int Difference(Color32 a, Color32 b)
        { return Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b); }

        private static Color32 Pixel(byte[] bytes, TgaImageInfo info, int x, int y)
        {
            int channels = info.BitsPerPixel / 8;
            int row = info.TopOrigin ? info.Height - 1 - y : y;
            int column = info.RightOrigin ? info.Width - 1 - x : x;
            int offset = 18 + (row * info.Width + column) * channels;
            return new Color32(bytes[offset + 2], bytes[offset + 1], bytes[offset], channels == 4 ? bytes[offset + 3] : (byte)255);
        }

        private static void MeshEquals(BindPoseLimbMesh expected, SourceTeamLimbAsset actual)
        {
            Mesh mesh = actual.Mesh;
            Equal(mesh.vertexCount, expected.Positions.Count, "actual original mesh vertex count " + expected.Symbol);
            Vector3[] positions = mesh.vertices, normals = mesh.normals; Vector2[] uv = mesh.uv;
            for (int i = 0; i < positions.Length; i++)
            {
                Equal(positions[i], new Vector3(expected.Positions[i].X, expected.Positions[i].Y, expected.Positions[i].Z), "native original vertex " + expected.Symbol);
                Equal(normals[i], new Vector3(expected.Normals[i].X, expected.Normals[i].Y, expected.Normals[i].Z), "native original normal " + expected.Symbol);
                Equal(uv[i], new Vector2(expected.Uvs[i].X, expected.Uvs[i].Y), "native original UV " + expected.Symbol);
            }
            int group = 0;
            for (int slot = 0; slot < expected.SubmeshTriangles.Count; slot++)
            {
                if (expected.SubmeshTriangles[slot].Count == 0) continue;
                Equal(actual.SourceSlot(group), slot, "compacted submesh original material identity");
                Check(mesh.GetTriangles(group++).SequenceEqual(expected.SubmeshTriangles[slot]), "actual original indices " + expected.Symbol);
                indices += expected.SubmeshTriangles[slot].Count;
            }
            Equal(group, mesh.subMeshCount, "empty source submeshes do not cost draw submissions");
            vertices += positions.Length;
        }

        private static void Equal<T>(T actual, T expected, string label)
        { Check(EqualityComparer<T>.Default.Equals(actual, expected), label); }
        private static void Near(float actual, float expected, float tolerance, string label)
        { Check(!float.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance, label); }
        private static void Check(bool condition, string label)
        { assertions++; if (!condition) throw new PlayerAssetException("Native original team/impact check failed: " + label); }
        private static void Reject(Action action, string expected, string label)
        {
            bool rejected = false;
            try { action(); } catch (Exception error) { rejected = error.Message.Contains(expected); }
            Check(rejected, label);
        }
        private static string Escape(string text) { return text.Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }
}
#endif
