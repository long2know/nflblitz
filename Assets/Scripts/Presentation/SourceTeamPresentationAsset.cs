using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using UnityEngine;

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

        public void ValidateGeneratedPresentation()
        {
            byTexture = null; byNumber = null; materialSets.Clear(); Load();
        }

        public void Initialize(TextAsset originalRosters, string rosterHash, TextAsset originalCatalog, string catalogHash,
            SourceTeamModelAsset[] modelAssets, SourceTeamHeadAsset[] headAssets,
            SourceTeamNumberAsset[] numberAssets, SourceTeamMaterialAsset[] materialAssets, SourcePlayerRig sourceTemplate)
        {
            roster = originalRosters; rosterSha256 = rosterHash; catalog = originalCatalog; catalogSha256 = catalogHash;
            models = (SourceTeamModelAsset[])modelAssets.Clone(); heads = (SourceTeamHeadAsset[])headAssets.Clone();
            numbers = (SourceTeamNumberAsset[])numberAssets.Clone(); materials = (SourceTeamMaterialAsset[])materialAssets.Clone();
            template = sourceTemplate;
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
    }
}
