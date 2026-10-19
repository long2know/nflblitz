using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Blitz.Presentation
{
    [Serializable]
    public sealed class SourceTeamLimbAsset
    {
        [SerializeField] private Mesh mesh;
        [SerializeField] private int[] sourceSlots;
        public Mesh Mesh { get { return mesh; } }
        public int SlotCount { get { return sourceSlots == null ? 0 : sourceSlots.Length; } }
        public int SourceSlot(int submesh) { return sourceSlots[submesh]; }
        public SourceTeamLimbAsset(Mesh mesh, int[] sourceSlots)
        {
            this.mesh = mesh; this.sourceSlots = (int[])sourceSlots.Clone();
            Validate();
        }
        public void Validate()
        {
            if (mesh == null || sourceSlots == null || sourceSlots.Length == 0 || mesh.subMeshCount != sourceSlots.Length)
                throw new InvalidOperationException("Original team limb mesh/source-material mapping is incomplete.");
            foreach (int slot in sourceSlots)
                if (slot < 0 || slot > 8) throw new InvalidOperationException("Original team limb material slot is out of range.");
        }
    }

    [Serializable]
    public sealed class SourceTeamModelAsset
    {
        [SerializeField] private int index;
        [SerializeField] private SourceTeamLimbAsset[] joints;
        public int Index { get { return index; } }
        public SourceTeamLimbAsset Joint(int joint) { return joints[joint]; }
        public SourceTeamModelAsset(int index, SourceTeamLimbAsset[] joints)
        {
            this.index = index; this.joints = (SourceTeamLimbAsset[])joints.Clone();
        }
        public void Validate()
        {
            if (index < 0 || index >= 32 || joints == null || joints.Length != 15)
                throw new InvalidOperationException("Original team model requires its exact selector and fifteen limbs.");
            foreach (SourceTeamLimbAsset joint in joints)
                if (joint == null) throw new InvalidOperationException("Missing original team model limb.");
                else joint.Validate();
        }
    }

    [Serializable]
    public sealed class SourceTeamHeadAsset
    {
        [SerializeField] private int index;
        [SerializeField] private SourceTeamLimbAsset limb;
        public int Index { get { return index; } }
        public SourceTeamLimbAsset Limb { get { return limb; } }
        public SourceTeamHeadAsset(int index, SourceTeamLimbAsset limb) { this.index = index; this.limb = limb; }
    }

    [Serializable]
    public sealed class SourceTeamPoppedHeadAsset
    {
        [SerializeField] private TextAsset manifest;
        [SerializeField] private TextAsset originalSdl;
        [SerializeField] private TextAsset sourceAuthoredHeader;
        [SerializeField] private string manifestSha256;
        [SerializeField] private SourceTeamLimbAsset limb;
        [SerializeField] private Vector3 sourceBindPivot;
        private SourceTeamPoppedHeadData data;
        private bool meshValidated;
        public SourceTeamLimbAsset Limb { get { Validate(); return limb; } }
        public SourceTeamPoppedHeadData Data { get { Validate(); return data; } }
        public void InvalidateValidation() { data = null; meshValidated = false; }

        public SourceTeamPoppedHeadAsset(TextAsset manifest, string hash, TextAsset source, TextAsset header,
            SourceTeamLimbAsset limb, AssetVector3 sourceBindPivot)
        {
            this.manifest = manifest; manifestSha256 = hash; originalSdl = source;
            sourceAuthoredHeader = header; this.limb = limb;
            this.sourceBindPivot = new Vector3(sourceBindPivot.X, sourceBindPivot.Y, sourceBindPivot.Z);
        }

        public void Validate()
        {
            if (data != null) return;
            if (manifest == null || originalSdl == null || sourceAuthoredHeader == null || limb == null)
                throw new InvalidOperationException("Original popped-head SDL/header/mesh provenance is incomplete.");
            limb.Validate();
            if (limb.SlotCount != 1 || limb.SourceSlot(0) != SourceTeamPoppedHeadData.LogicalTextureSlot)
                throw new InvalidOperationException("Original popped-head raw texture4 must bind logical HEDSHU slot1.");
            data = SourceTeamPoppedHeadData.Read(manifest.bytes, manifestSha256, path =>
            {
                if (path == "models/phead.sdl") return originalSdl.bytes;
                if (path == "phead.source-authored.h") return sourceAuthoredHeader.bytes;
                throw new InvalidOperationException("Unlisted original popped-head resource: " + path);
            });
        }

        public void ValidateMesh(SourcePlayerRig reference)
        {
            if (meshValidated) return;
            Validate();
            if (reference == null) throw new InvalidOperationException("Original phead needs its actual neck bind pivot.");
            var pivot = reference.GetUnityReferencePivot(SourceTeamPoppedHeadData.HeadJoint);
            // Rest-position parent subtraction/addition can round; mesh construction uses the
            // exact original serialized source pivot, never the reconstructed transform sum.
            float scale = Math.Max(1f, Math.Max(Math.Abs(sourceBindPivot.x),
                Math.Max(Math.Abs(sourceBindPivot.y), Math.Abs(sourceBindPivot.z))));
            float bound = 16f * 1.1920929e-7f * scale;
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale > PlayerImportLimits.CoordinateMagnitude ||
                Math.Abs(pivot.X - sourceBindPivot.x) > bound || Math.Abs(pivot.Y - sourceBindPivot.y) > bound ||
                Math.Abs(-pivot.Z - sourceBindPivot.z) > bound)
                throw new InvalidOperationException("Original phead bind pivot does not match the actual neck rig.");
            BindPoseLimbMesh expected = SourceTeamGeneratedModels.BuildPoppedHead(data,
                new AssetVector3(sourceBindPivot.x, sourceBindPivot.y, sourceBindPivot.z));
            Vector3[] vertices = limb.Mesh.vertices;
            Vector2[] uvs = limb.Mesh.uv;
            int[] triangles = limb.Mesh.GetTriangles(0);
            if (vertices.Length != expected.Positions.Count || uvs.Length != expected.Uvs.Count ||
                triangles.Length != expected.SubmeshTriangles[SourceTeamPoppedHeadData.LogicalTextureSlot].Count)
                throw new InvalidOperationException("Imported phead mesh inventory differs from its original SDL reconstruction.");
            for (int i = 0; i < vertices.Length; i++)
            {
                AssetVector3 point = expected.Positions[i]; AssetVector2 uv = expected.Uvs[i];
                if (vertices[i].x != point.X || vertices[i].y != point.Y || vertices[i].z != point.Z ||
                    uvs[i].x != uv.X || uvs[i].y != uv.Y)
                    throw new InvalidOperationException("Imported phead geometry/UV/bind pivot is not the actual source-authored mesh.");
            }
            for (int i = 0; i < triangles.Length; i++)
                if (triangles[i] != expected.SubmeshTriangles[SourceTeamPoppedHeadData.LogicalTextureSlot][i])
                    throw new InvalidOperationException("Imported phead topology/winding differs from original SDL.");
            meshValidated = true;
        }
    }

    [Serializable]
    public sealed class SourceTeamNumberAsset
    {
        [SerializeField] private string limbSymbol;
        [SerializeField] private int packedNumber;
        [SerializeField] private Mesh mesh;
        public string LimbSymbol { get { return limbSymbol; } }
        public int PackedNumber { get { return packedNumber; } }
        public Mesh Mesh { get { return mesh; } }
        public SourceTeamNumberAsset(string symbol, int packedNumber, Mesh mesh)
        { limbSymbol = symbol; this.packedNumber = packedNumber; this.mesh = mesh; }
    }

    [Serializable]
    public sealed class SourceTeamMaterialAsset
    {
        [SerializeField] private string sourceTextureId;
        [SerializeField] private string contentSha256;
        [SerializeField] private Material material;
        public string SourceTextureId { get { return sourceTextureId; } }
        public string ContentSha256 { get { return contentSha256; } }
        public Material Material { get { return material; } }
        public SourceTeamMaterialAsset(string id, string hash, Material material)
        { sourceTextureId = id; contentSha256 = hash; this.material = material; }
    }

    public sealed class SourceTeamPresentationAsset : ScriptableObject
    {
        [SerializeField] private TextAsset roster;
        [SerializeField] private TextAsset catalog;
        [SerializeField] private string rosterSha256;
        [SerializeField] private string catalogSha256;
        [SerializeField] private SourceTeamModelAsset[] models;
        [SerializeField] private SourceTeamHeadAsset[] heads;
        [SerializeField] private SourceTeamNumberAsset[] numbers;
        [SerializeField] private SourceTeamMaterialAsset[] materials;
        [SerializeField] private SourcePlayerRig template;
        [SerializeField] private SourceTeamPoppedHeadAsset poppedHead;
        [SerializeField] private bool sourceHeadExtensionPending;
        private SourceTeamRosterLibrary library;
        private SourceTeamPresentationData data;
        private Dictionary<string, Material> byTexture;
        private Dictionary<string, Mesh> byNumber;
        private readonly Dictionary<string, Material[]> materialSets = new Dictionary<string, Material[]>(StringComparer.Ordinal);

        public string CatalogSha256 { get { return catalogSha256; } }
        public string RosterSha256 { get { return rosterSha256; } }
        public SourceTeamRosterLibrary Rosters { get { Load(); return library; } }
        public SourceTeamPresentationData Catalog { get { Load(); return data; } }
        public SourcePlayerRig Template { get { Load(); return template; } }
        public int NumberMeshCount { get { Load(); return numbers.Length; } }
        public IReadOnlyList<SourceTeamMaterialAsset> MaterialInventory { get { Load(); return Array.AsReadOnly(materials); } }
        public SourceTeamModelAsset Model(int index) { Load(); return models[index]; }
        public SourceTeamHeadAsset Head(int index) { Load(); return heads[index]; }
        public SourceTeamPoppedHeadData PoppedHeadData { get { Load(); return poppedHead == null ? null : poppedHead.Data; } }

        public void ValidateGeneratedPresentation()
        {
            byTexture = null; byNumber = null; materialSets.Clear();
            if (poppedHead != null) poppedHead.InvalidateValidation();
            Load();
        }

        public void Initialize(TextAsset originalRosters, string rosterHash, TextAsset originalCatalog, string catalogHash,
            SourceTeamModelAsset[] modelAssets, SourceTeamHeadAsset[] headAssets,
            SourceTeamNumberAsset[] numberAssets, SourceTeamMaterialAsset[] materialAssets, SourcePlayerRig sourceTemplate,
            SourceTeamPoppedHeadAsset poppedHeadAsset = null)
        {
            roster = originalRosters; rosterSha256 = rosterHash; catalog = originalCatalog; catalogSha256 = catalogHash;
            models = (SourceTeamModelAsset[])modelAssets.Clone(); heads = (SourceTeamHeadAsset[])headAssets.Clone();
            numbers = (SourceTeamNumberAsset[])numberAssets.Clone(); materials = (SourceTeamMaterialAsset[])materialAssets.Clone();
            template = sourceTemplate;
            poppedHead = poppedHeadAsset;
            sourceHeadExtensionPending = poppedHeadAsset == null;
            library = null; data = null; byTexture = null; byNumber = null; materialSets.Clear();
            Load();
        }

        public void Load()
        {
            if (byTexture != null) return;
            if (roster == null || catalog == null || rosterSha256 != SourceTeamPresentationData.PinnedRosterSha256 ||
                models == null || models.Length != 32 || heads == null || heads.Length != 20 ||
                numbers == null || numbers.Length == 0 || materials == null || template == null || template.JointCount != 15)
                throw new InvalidOperationException("Original team presentation Resources are incomplete; matching uniforms will not be substituted.");
            library = SourceTeamRosterLibrary.Read(roster.bytes, rosterSha256);
            template.ValidateGeneratedRig();
            data = SourceTeamPresentationData.Read(catalog.bytes, catalogSha256, library);
            if (poppedHead != null) poppedHead.ValidateMesh(template);
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == null || models[i].Index != i)
                    throw new InvalidOperationException("Original model selector order changed at " + i + ".");
                models[i].Validate();
            }
            for (int i = 0; i < heads.Length; i++)
            {
                if (heads[i] == null || heads[i].Index != i || heads[i].Limb == null)
                    throw new InvalidOperationException("Original custom-head identity changed at " + i + ".");
                heads[i].Limb.Validate();
            }
            var textures = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (SourceTeamMaterialAsset entry in materials)
            {
                if (entry == null || entry.Material == null || entry.Material.mainTexture == null ||
                    textures.ContainsKey(entry.SourceTextureId))
                    throw new InvalidOperationException("Missing/duplicate original team material identity.");
                SourceTeamTexture record = null;
                foreach (SourceTeamTexture candidate in data.Textures)
                    if (candidate.Id == entry.SourceTextureId) { record = candidate; break; }
                if (record == null || record.Sha256 != entry.ContentSha256 ||
                    entry.Material.name != entry.SourceTextureId ||
                    entry.Material.mainTexture.name != TextureAssetName(record.Sha256) ||
                    entry.Material.mainTexture.width != record.Width || entry.Material.mainTexture.height != record.Height ||
                    entry.Material.HasProperty("_Color") && entry.Material.color != Color.white)
                    throw new InvalidOperationException("Original team material content/identity changed or has a non-white tint: " + entry.SourceTextureId);
                textures.Add(entry.SourceTextureId, entry.Material);
            }
            if (textures.Count != data.Textures.Count)
                throw new InvalidOperationException("An original team texture was silently omitted during Resources import.");
            var numberMeshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (SourceTeamNumberAsset number in numbers)
            {
                if (number == null || number.Mesh == null || number.Mesh.subMeshCount != 1)
                    throw new InvalidOperationException("Missing original packed-number overlay mesh.");
                numberMeshes.Add(NumberKey(number.LimbSymbol, number.PackedNumber), number.Mesh);
            }
            byTexture = textures; byNumber = numberMeshes;
        }

        public SourceTeamLimbAsset Limb(SourceTeamAppearance appearance, int joint)
        {
            Load();
            if (appearance == null || joint < 0 || joint >= 15) throw new ArgumentOutOfRangeException(nameof(joint));
            if (joint == library.ActorModels.HeadJointIndex && appearance.CustomHead != null)
                return heads[appearance.CustomHead.Index].Limb;
            if (joint == library.ActorModels.HeadJointIndex && appearance.PoppedHead != null)
            {
                if (poppedHead == null || poppedHead.Data.ContentSha256 != appearance.PoppedHead.ContentSha256)
                    throw new InvalidOperationException("The selected original popped-head data has no matching imported Unity mesh.");
                return poppedHead.Limb;
            }
            return models[appearance.Model.Index].Joint(joint);
        }

        public Material[] Materials(SourceTeamAppearance appearance, SourceTeamLimbAsset limb)
        {
            Load();
            string key = appearance.Player.Profile.Identity.TeamIndex + ":" + (int)appearance.Uniform.Variant +
                ":" + (int)appearance.Uniform.Skin + ":" + (appearance.CustomHead == null ? -1 : appearance.CustomHead.Index) +
                ":" + limb.Mesh.GetInstanceID();
            Material[] result;
            if (materialSets.TryGetValue(key, out result)) return result;
            result = new Material[limb.SlotCount];
            for (int i = 0; i < result.Length; i++)
            {
                int slot = limb.SourceSlot(i);
                string texture;
                if (slot < 7) texture = appearance.Uniform.Slots[slot].Id;
                else
                {
                    if (appearance.CustomHead == null || slot != 7)
                        throw new InvalidOperationException("Original custom-head texture slot has no actual station selection.");
                    texture = appearance.CustomHead.Texture.Id;
                }
                result[i] = RequiredMaterial(texture);
            }
            materialSets.Add(key, result);
            return result;
        }

        public Material NumberMaterial(SourceTeamAppearance appearance)
        {
            Load(); return RequiredMaterial(appearance.Uniform.Slots[library.ActorModels.NumberTextureSlot].Id);
        }

        public Texture LooseHelmetTexture(SourceLooseHelmetDecal decal)
        {
            Load();
            foreach (SourceTeamTexture texture in data.Textures)
                if (texture.Id == decal.TextureId && decal.IsSpecified && texture.Sha256 == decal.SourceSha256 &&
                    texture.SourcePath == decal.SourcePath && texture.Width == decal.Width && texture.Height == decal.Height)
                    return RequiredMaterial(texture.Id).mainTexture;
            throw new SourceFireBoundaryException("SOURCE_HELMET_DECAL_REQUIRED: no imported texture matches actual entry pdecal[2].");
        }

        public Mesh NumberMesh(SourceTeamAppearance appearance)
        {
            Load();
            string key = NumberKey(appearance.Numbers.LimbSymbol, appearance.Player.Profile.PackedJerseyNumber);
            Mesh result;
            if (!byNumber.TryGetValue(key, out result))
                throw new InvalidOperationException("Missing original BCD number/geometry binding: " + key);
            return result;
        }

        private Material RequiredMaterial(string id)
        {
            Material result;
            if (!byTexture.TryGetValue(id, out result))
                throw new InvalidOperationException("Missing original selected uniform/skin/head texture: " + id +
                    ". No matching-uniform, recolor or another-team atlas fallback is permitted.");
            return result;
        }

        private static string NumberKey(string symbol, int number) { return symbol + ":" + number; }
        public static string TextureAssetName(string sha256) { return "original-texture-" + sha256; }

#if UNITY_EDITOR
        // Explicit second import step on a freshly generated team asset. Existing
        // serialized assets lack the pending marker and cannot be overwritten.
        public static void ImportOriginalPoppedHeadFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string sourceRoot = HeadArgument(args, "-teamPoppedHeadRoot");
            string headHash = HeadArgument(args, "-teamPoppedHeadSha256");
            string teamRoot = HeadArgument(args, "-teamAssetRoot");
            string target = HeadArgument(args, "-teamPresentationAsset");
            AuthorizedAssetRoot.ValidateRelativePath(target);
            if (!target.StartsWith("Assets/Imported/", StringComparison.Ordinal) ||
                !target.EndsWith("/team-presentation.asset", StringComparison.Ordinal))
                throw new PlayerAssetException("Supply the exact newly generated team-presentation.asset under Assets/Imported.");
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            output.ExistingFile(target);
            SourceTeamPresentationAsset asset = AssetDatabase.LoadAssetAtPath<SourceTeamPresentationAsset>(target);
            if (asset == null || !asset.sourceHeadExtensionPending || asset.poppedHead != null)
                throw new PlayerAssetException("Phead import requires a fresh pending team asset; existing/prior head resources are never overwritten.");
            asset.Load();
            var input = new AuthorizedAssetRoot(sourceRoot);
            byte[] manifest = input.ReadBytes("popped-head.json", 65536);
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            SourceTeamPoppedHeadData head = SourceTeamPoppedHeadData.Read(manifest, headHash, path =>
            {
                byte[] bytes = input.ReadBytes(path, 128 * 1024); payloads.Add(path, bytes); return bytes;
            });
            var teams = new AuthorizedAssetRoot(teamRoot);
            SourcePlayer reference = SourceTeamGeneratedModels.ReadModel(asset.Rosters, 0, path =>
            {
                byte[] bytes = teams.ReadBytes(path, PlayerImportLimits.SourceBytes);
                asset.Catalog.VerifyInput(path, bytes);
                return AuthorizedAssetRoot.DecodeUtf8(bytes, path);
            });
            BindPoseLimbMesh geometry = SourceTeamGeneratedModels.BuildPoppedHead(reference, head);
            var vertices = new Vector3[geometry.Positions.Count];
            var normals = new Vector3[geometry.Normals.Count];
            var uv = new Vector2[geometry.Uvs.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                AssetVector3 p = geometry.Positions[i], n = geometry.Normals[i]; AssetVector2 st = geometry.Uvs[i];
                vertices[i] = new Vector3(p.X, p.Y, p.Z); normals[i] = new Vector3(n.X, n.Y, n.Z); uv[i] = new Vector2(st.X, st.Y);
            }
            var indices = new int[geometry.SubmeshTriangles[SourceTeamPoppedHeadData.LogicalTextureSlot].Count];
            geometry.SubmeshTriangles[SourceTeamPoppedHeadData.LogicalTextureSlot].CopyTo(indices, 0);
            string directory = target.Substring(0, target.LastIndexOf('/')) + "/OriginalPoppedHead";
            output.NewPath(directory);
            string parent = directory.Substring(0, directory.LastIndexOf('/'));
            string folderGuid = AssetDatabase.CreateFolder(parent, "OriginalPoppedHead");
            if (string.IsNullOrEmpty(folderGuid))
                throw new PlayerAssetException("Could not reserve a new non-overwriting original phead resource folder.");
            var mesh = new Mesh { name = "limb_phead_obj", vertices = vertices, normals = normals, uv = uv, subMeshCount = 1 };
            mesh.SetTriangles(indices, 0, false); mesh.RecalculateBounds();
            string meshPath = directory + "/phead-source-authored.asset";
            output.NewPath(meshPath); AssetDatabase.CreateAsset(mesh, meshPath);
            TextAsset savedManifest = HeadText(output, directory + "/popped-head.json", manifest);
            TextAsset savedSdl = HeadText(output, directory + "/phead.sdl.bytes", payloads["models/phead.sdl"]);
            TextAsset savedHeader = HeadText(output, directory + "/phead.source-authored.h.bytes", payloads["phead.source-authored.h"]);
            asset.poppedHead = new SourceTeamPoppedHeadAsset(savedManifest, headHash, savedSdl, savedHeader,
                new SourceTeamLimbAsset(mesh, new[] { SourceTeamPoppedHeadData.LogicalTextureSlot }),
                reference.Nodes[SourceTeamPoppedHeadData.HeadJoint].BindPivot);
            asset.sourceHeadExtensionPending = false;
            asset.ValidateGeneratedPresentation();
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
            Debug.Log("PASS original phead SDL source-authored import:83 vertices/157 triangles/raw4-HEDSHU1/joint8; native head.h/qsort/pixels unqualified.");
        }

        private static string HeadArgument(string[] args, string name)
        {
            string value = null;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == name)
                {
                    if (value != null || i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        throw new PlayerAssetException("Specify exactly one nonempty " + name + ".");
                    value = args[++i];
                }
            if (string.IsNullOrWhiteSpace(value)) throw new PlayerAssetException("Required explicit original phead import argument: " + name);
            return value;
        }

        private static TextAsset HeadText(AuthorizedAssetRoot output, string path, byte[] bytes)
        {
            string physical = output.NewPath(path);
            using (var stream = new FileStream(physical, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (text == null || AuthorizedAssetRoot.Sha256(text.bytes) != AuthorizedAssetRoot.Sha256(bytes))
                throw new PlayerAssetException("Original phead imported text content changed: " + path);
            return text;
        }
#endif
    }
}
