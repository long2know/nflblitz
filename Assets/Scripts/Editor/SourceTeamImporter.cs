#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blitz.Editor
{
    public static class SourceTeamImporter
    {
        public const string StableId = "original-teams";
        public const string Destination = "Assets/Imported/" + StableId;
        public const string ResourceFolder = Destination + "/Resources/BlitzSourcePlayers/" + StableId;

        public static void ImportFromCommandLine()
        { ImportFromArguments(Environment.GetCommandLineArgs(), SourceTeamResourceProfile.B1Final117); }

        public static void ImportMovement157FromCommandLine()
        { ImportFromArguments(Environment.GetCommandLineArgs(), SourceTeamResourceProfile.Movement157); }

        public static void ImportContact272FromCommandLine()
        { ImportFromArguments(Environment.GetCommandLineArgs(), SourceTeamResourceProfile.Contact272); }

        public static void ImportContact281C2FromCommandLine()
        { ImportFromArguments(Environment.GetCommandLineArgs(), SourceTeamResourceProfile.Contact281C2); }

        public static void ImportContact281C2M1FromCommandLine()
        { ImportFromArguments(Environment.GetCommandLineArgs(), SourceTeamResourceProfile.Contact281C2M1); }

        private static void ImportFromArguments(string[] args, SourceTeamResourceProfile profile)
        {
            string root = PlayerAssetImporter.RequiredArgument(args, "-teamAssetRoot");
            string hash = PlayerAssetImporter.RequiredArgument(args, "-teamCatalogSha256");
            string animationRoot = PlayerAssetImporter.RequiredArgument(args, "-teamAnimationRoot");
            string impactRoot = PlayerAssetImporter.RequiredArgument(args, "-teamImpactRoot");
            string impactHash = PlayerAssetImporter.RequiredArgument(args, "-teamImpactSha256");
            ImportProfile(root, hash, animationRoot, impactRoot, impactHash, profile);
        }

        public static SourceTeamPresentationAsset Import(string authorizedPreparedRoot, string expectedCatalogSha256,
            string final117AnimationRoot, string preparedImpactRoot, string expectedImpactSha256)
        {
            return ImportProfile(authorizedPreparedRoot, expectedCatalogSha256, final117AnimationRoot,
                preparedImpactRoot, expectedImpactSha256, SourceTeamResourceProfile.B1Final117);
        }

        public static SourceTeamPresentationAsset ImportMovement157(string authorizedPreparedRoot, string expectedCatalogSha256,
            string complete157AnimationRoot, string preparedImpactRoot, string expectedImpactSha256)
        {
            return ImportProfile(authorizedPreparedRoot, expectedCatalogSha256, complete157AnimationRoot,
                preparedImpactRoot, expectedImpactSha256, SourceTeamResourceProfile.Movement157);
        }

        public static SourceTeamPresentationAsset ImportContact272(string authorizedPreparedRoot, string expectedCatalogSha256,
            string production272AnimationRoot, string preparedImpactRoot, string expectedImpactSha256)
        {
            return ImportProfile(authorizedPreparedRoot, expectedCatalogSha256, production272AnimationRoot,
                preparedImpactRoot, expectedImpactSha256, SourceTeamResourceProfile.Contact272);
        }

        private static SourceTeamPresentationAsset ImportProfile(string authorizedPreparedRoot, string expectedCatalogSha256,
            string animationRoot, string preparedImpactRoot, string expectedImpactSha256, SourceTeamResourceProfile profile)
        {
            string destination = profile.Destination, resourceFolder = profile.TeamResourceFolder;
            SourceAnimationLibraryAsset sharedAnimations = null;
            if (profile.UsesC2Data)
            {
                string existing = SourceTeamInterfaceImporter.FindUniqueResourcePath("SourceAnimationLibraryAsset", profile.AnimationResourceKey);
                if (existing != null)
                {
                    sharedAnimations = AssetDatabase.LoadAssetAtPath<SourceAnimationLibraryAsset>(existing);
                    SourceTeamInterfaceImporter.RequireAnimationProfileAsset(profile, sharedAnimations);
                }
            }
            if (profile.IsContact272 || profile.UsesC2Data)
            {
                if (profile.IsContact272 && SourceTeamInterfaceImporter.FindUniqueResourcePath(
                    "SourceAnimationLibraryAsset", profile.AnimationResourceKey) != null)
                    throw new PlayerAssetException("The immutable Contact272 animation resource already exists.");
                foreach (string[] identity in new[] {
                    new[] { "SourceTeamPresentationAsset", profile.TeamResourceKey },
                    new[] { "SourceCinematicLibraryAsset", profile.ImpactResourceKey } })
                    if (SourceTeamInterfaceImporter.FindUniqueResourcePath(identity[0], identity[1]) != null)
                        throw new PlayerAssetException("Contact272 Resources identity already exists; no duplicate, overwrite or old-profile alias: " + identity[1]);
            }
            var input = new AuthorizedAssetRoot(authorizedPreparedRoot);
            byte[] rosterBytes = input.ReadBytes("rosters.json", 4 * 1024 * 1024);
            SourceTeamRosterLibrary rosters = SourceTeamRosterLibrary.Read(rosterBytes, SourceTeamPresentationData.PinnedRosterSha256);
            byte[] catalogBytes = input.ReadBytes("team-presentation.json", 1024 * 1024);
            SourceTeamPresentationData catalog = SourceTeamPresentationData.Read(catalogBytes, expectedCatalogSha256, rosters);
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            Func<string, string> read = path =>
            {
                string result;
                if (!headers.TryGetValue(path, out result))
                {
                    byte[] bytes = input.ReadBytes(path, PlayerImportLimits.SourceBytes);
                    catalog.VerifyInput(path, bytes);
                    result = AuthorizedAssetRoot.DecodeUtf8(bytes, path); headers.Add(path, result);
                }
                return result;
            };
            var sourceModels = new SourcePlayer[32];
            var modelData = new IList<BindPoseLimbMesh>[32];
            for (int i = 0; i < 32; i++)
            {
                sourceModels[i] = SourceTeamGeneratedModels.ReadModel(rosters, i, read);
                modelData[i] = SourceTeamGeneratedModels.BuildModel(sourceModels[i]);
            }
            var headData = new BindPoseLimbMesh[20];
            for (int i = 0; i < 20; i++)
                headData[i] = SourceTeamGeneratedModels.BuildModel(SourceTeamGeneratedModels.ReadModel(rosters, 0, read, i))[8];
            var animationInput = new AuthorizedAssetRoot(animationRoot);
            byte[] inventory = animationInput.ReadBytes("export-inventory.json", 1024 * 1024);
            if (AuthorizedAssetRoot.Sha256(inventory) != profile.AnimationSha256)
                throw new PlayerAssetException("Original " + profile.StableId + " import requires its exact " +
                    profile.ClipCount + "-clip manifest; old56/117/156 and new157 identities are not aliases.");
            var clips = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            SourceAnimationLibrary animations = SourceAnimationLibrary.Read(inventory, path =>
            {
                byte[] bytes = animationInput.ReadBytes(path, 8 * 1024 * 1024);
                clips.Add(path, bytes); return bytes;
            });
            profile.RequireInventory(AuthorizedAssetRoot.Sha256(inventory), animations.SequenceNames.Count, animations.FrameCount);
            var impactInput = new AuthorizedAssetRoot(preparedImpactRoot);
            byte[] impactBytes = impactInput.ReadBytes("impact-presentation.json", 1024 * 1024);
            profile.RequireImpact(SourceCinematicLibrary.Read(impactBytes, expectedImpactSha256));
            Shader shader = Shader.Find("Unlit/Transparent Cutout");
            if (shader == null) throw new PlayerAssetException("The original texture-decal material adapter needs Unlit/Transparent Cutout; no tinted shader fallback.");
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            output.NewPath(destination);
            Folder(output, "Assets/Imported"); Folder(output, destination);
            Folder(output, destination + "/Resources"); Folder(output, destination + "/Resources/BlitzSourcePlayers");
            Folder(output, resourceFolder); Folder(output, destination + "/Textures"); Folder(output, destination + "/Meshes");
            Folder(output, destination + "/Materials"); Folder(output, resourceFolder + "/Animations");
            if (profile.UsesMovementData) Folder(output, destination + "/Resources/SourceData");
            var materialEntries = new List<SourceTeamMaterialAsset>();
            var materialById = new Dictionary<string, Material>(StringComparer.Ordinal);
            var textureByHash = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (SourceTeamTexture record in catalog.Textures)
            {
                Texture2D texture;
                if (!textureByHash.TryGetValue(record.Sha256, out texture))
                {
                    byte[] bytes = input.ReadBytes(record.Path, PlayerImportLimits.TextureBytes);
                    catalog.VerifyTexture(record, bytes);
                    string path = destination + "/Textures/" + SourceTeamPresentationAsset.TextureAssetName(record.Sha256) + ".tga";
                    PlayerAssetImporter.WriteNew(output, path, bytes);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) throw new PlayerAssetException("Original team TGA not recognized: " + record.Id);
                    importer.textureType = TextureImporterType.Default;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = Math.Max(32, Math.Max(record.Width, record.Height));
                    importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = false; importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Bilinear;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null || texture.width != record.Width || texture.height != record.Height)
                        throw new PlayerAssetException("Native original texture dimensions changed: " + record.Id);
                    textureByHash.Add(record.Sha256, texture);
                }
                var material = new Material(shader) { name = record.Id, mainTexture = texture };
                material.SetFloat("_Cutoff", 1f / 255f);
                if (material.HasProperty("_Color")) material.color = Color.white;
                string materialPath = destination + "/Materials/" + record.Id.Substring("runtime/".Length).ToLowerInvariant() + ".mat";
                output.NewPath(materialPath); AssetDatabase.CreateAsset(material, materialPath);
                materialById.Add(record.Id, material);
                materialEntries.Add(new SourceTeamMaterialAsset(record.Id, record.Sha256, material));
            }
            var limbCache = new Dictionary<string, SourceTeamLimbAsset>(StringComparer.Ordinal);
            Func<BindPoseLimbMesh, SourceTeamLimbAsset> limb = data =>
            {
                SourceTeamLimbAsset result;
                if (limbCache.TryGetValue(data.Symbol, out result)) return result;
                Mesh mesh = PlayerAssetImporter.CreateUnityMesh(data);
                var slots = new List<int>();
                for (int slot = 0; slot < data.SubmeshTriangles.Count; slot++)
                    if (data.SubmeshTriangles[slot].Count != 0) slots.Add(slot);
                mesh.subMeshCount = slots.Count;
                for (int sub = 0; sub < slots.Count; sub++) mesh.SetTriangles(data.SubmeshTriangles[slots[sub]].ToArray(), sub, false);
                mesh.RecalculateBounds();
                string path = destination + "/Meshes/" + data.Symbol + ".asset";
                output.NewPath(path); AssetDatabase.CreateAsset(mesh, path);
                result = new SourceTeamLimbAsset(mesh, slots.ToArray()); limbCache.Add(data.Symbol, result);
                return result;
            };
            var models = new SourceTeamModelAsset[32];
            for (int i = 0; i < models.Length; i++)
                models[i] = new SourceTeamModelAsset(i, modelData[i].Select(limb).ToArray());
            var heads = new SourceTeamHeadAsset[20];
            for (int i = 0; i < heads.Length; i++) heads[i] = new SourceTeamHeadAsset(i, limb(headData[i]));
            string numberSource = read("models/jerseys.h");
            var numberCache = new Dictionary<string, SourceTeamNumberAsset>(StringComparer.Ordinal);
            foreach (SourceRosterTeam team in rosters.TeamData)
            foreach (SourceRosterPlayer player in team.Players)
            foreach (bool highCount in new[] { false, true })
            {
                string symbol = rosters.ActorModels.SelectJersey(player, highCount).LimbSymbol;
                int packed = player.Profile.PackedJerseyNumber;
                string key = symbol + "-" + packed;
                if (numberCache.ContainsKey(key)) continue;
                BindPoseLimbMesh data = SourceTeamGeneratedModels.BuildNumbers(numberSource, symbol, packed, sourceModels[0].Nodes[1].BindPivot);
                Mesh mesh = PlayerAssetImporter.CreateUnityMesh(data); mesh.name = key;
                string path = destination + "/Meshes/" + key + ".asset";
                output.NewPath(path); AssetDatabase.CreateAsset(mesh, path);
                numberCache.Add(key, new SourceTeamNumberAsset(symbol, packed, mesh));
            }
            SourceRosterUniform templateUniform = rosters.GetUniform(0, SourceUniformVariant.Home, SourceSkinSelector.White);
            SourceTeamModelAsset templateModel = models[0];
            GameObject hierarchy = PlayerAssetImporter.CreateHierarchy(profile.StableId, sourceModels[0], modelData[0],
                Enumerable.Range(0, 15).Select(i => templateModel.Joint(i).Mesh).ToArray(), null,
                joint => Enumerable.Range(0, templateModel.Joint(joint).SlotCount).Select(
                    sub => materialById[templateUniform.Slots[templateModel.Joint(joint).SourceSlot(sub)].Id]).ToArray());
            SourcePlayerRig template;
            try
            {
                string path = resourceFolder + "/player.prefab"; output.NewPath(path);
                template = PrefabUtility.SaveAsPrefabAsset(hierarchy, path).GetComponent<SourcePlayerRig>();
            }
            finally { Object.DestroyImmediate(hierarchy); }
            TextAsset savedRosters = Text(output, resourceFolder + "/rosters.json", rosterBytes);
            TextAsset savedCatalog = Text(output, resourceFolder + "/team-catalog.json", catalogBytes);
            var asset = ScriptableObject.CreateInstance<SourceTeamPresentationAsset>();
            asset.Initialize(savedRosters, SourceTeamPresentationData.PinnedRosterSha256, savedCatalog, expectedCatalogSha256,
                models, heads, numberCache.Values.ToArray(), materialEntries.ToArray(), template);
            AssetDatabase.CreateAsset(asset, resourceFolder + "/team-presentation.asset");
            if (sharedAnimations == null)
            {
                TextAsset savedInventory = Text(output, resourceFolder + "/Animations/inventory.json", inventory);
                var paths = new List<string>(); var texts = new List<TextAsset>();
                foreach (KeyValuePair<string, byte[]> clip in clips)
                {
                    paths.Add(clip.Key);
                    texts.Add(Text(output, resourceFolder + "/Animations/" + Path.GetFileName(clip.Key), clip.Value));
                }
                var animationAsset = ScriptableObject.CreateInstance<SourceAnimationLibraryAsset>();
                animationAsset.Initialize(savedInventory, profile.AnimationSha256, paths.ToArray(), texts.ToArray());
                output.NewPath(profile.AnimationAssetPath); AssetDatabase.CreateAsset(animationAsset, profile.AnimationAssetPath);
            }
            var impactAsset = ScriptableObject.CreateInstance<SourceCinematicLibraryAsset>();
            impactAsset.Initialize(Text(output, resourceFolder + "/impact-catalog.json", impactBytes), expectedImpactSha256);
            AssetDatabase.CreateAsset(impactAsset, resourceFolder + "/impact-library.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("PASS original opposing-team import:31 teams /496 records /32 model selectors /20 heads /" +
                materialEntries.Count + " original textures /" + numberCache.Count +
                " BCD number meshes /" + profile.ClipCount + " clips; explicit " + profile.StableId +
                ". No raw assets are tracked and old117/56 resources are unchanged.");
            return asset;
        }

        private static TextAsset Text(AuthorizedAssetRoot output, string path, byte[] bytes)
        {
            PlayerAssetImporter.WriteNew(output, path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (text == null || AuthorizedAssetRoot.Sha256(text.bytes) != AuthorizedAssetRoot.Sha256(bytes))
                throw new PlayerAssetException("Bundled original text bytes changed: " + path);
            return text;
        }

        private static void Folder(AuthorizedAssetRoot output, string path)
        {
            int slash = path.LastIndexOf('/');
            PlayerAssetImporter.EnsureFolder(output, path, path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
#endif
