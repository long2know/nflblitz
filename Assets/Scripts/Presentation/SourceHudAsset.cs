using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using UnityEngine;

namespace Blitz.Presentation
{
    [Serializable]
    public sealed class SourceHudTextureBytes
    {
        [SerializeField] private string id;
        [SerializeField] private TextAsset data;
        public string Id { get { return id; } }
        public TextAsset Data { get { return data; } }
        public SourceHudTextureBytes(string id, TextAsset data) { this.id = id; this.data = data; }
    }

    public sealed class SourceHudAsset : ScriptableObject
    {
        public const string ResourceKey = "BlitzSourceInterface/original-interface";
        public const string PreparedManifestSha256 = "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435";
        [SerializeField] private TextAsset manifest;
        [SerializeField] private string manifestSha256;
        [SerializeField] private SourceHudTextureBytes[] textureBytes;
        [SerializeField] private Shader sourceHudShader;
        private SourceInterfaceData data;
        private Dictionary<string, Texture2D> textures;
        private Material material;

        public SourceInterfaceData Data { get { Load(); return data; } }
        public Material Material { get { Load(); return material; } }

        public void Initialize(TextAsset originalManifest, string trustedHash, SourceHudTextureBytes[] originals, Shader shader)
        {
            if (data != null) throw new InvalidOperationException("Original HUD asset is immutable after loading.");
            manifest = originalManifest; manifestSha256 = trustedHash;
            textureBytes = originals == null ? null : (SourceHudTextureBytes[])originals.Clone(); sourceHudShader = shader;
            Load();
        }

        public void Load()
        {
            if (data != null) return;
            if (manifest == null || manifestSha256 != PreparedManifestSha256 || textureBytes == null ||
                sourceHudShader == null || sourceHudShader.name != "Hidden/Blitz/SourceHud")
                throw new SourceInterfaceException("Missing/pin-mismatched original HUD Resources/" + ResourceKey +
                    ". Prepare tools/interface and import its exact original glyph/atlas/plane bundle. No Arial/texture substitute is permitted.");
            SourceInterfaceData parsed = SourceInterfaceData.Read(manifest.bytes, manifestSha256);
            var loaded = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (SourceHudTextureBytes binding in textureBytes)
            {
                if (binding == null || binding.Data == null || loaded.ContainsKey(binding.Id) || !parsed.Textures.ContainsKey(binding.Id))
                    throw new SourceInterfaceException("Missing/duplicate original HUD texture byte binding.");
                SourceInterfaceTexture definition = parsed.Textures[binding.Id];
                byte[] rgba = parsed.RequireTextureBytes(binding.Id, binding.Data.bytes);
                var texture = new Texture2D(definition.Width, definition.Height, TextureFormat.RGBA32, false);
                texture.name = "source-interface-" + definition.Sha256;
                texture.filterMode = definition.PointSampled ? FilterMode.Point : FilterMode.Bilinear;
                texture.wrapMode = binding.Id == "interface" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                texture.LoadRawTextureData(rgba); texture.Apply(false, true);
                loaded.Add(binding.Id, texture);
            }
            if (loaded.Count != parsed.Textures.Count)
                throw new SourceInterfaceException("An original HUD/selection texture was omitted; no transparent fallback is allowed.");
            material = new Material(sourceHudShader) { name = "Original interface source-alpha renderer" };
            textures = loaded; data = parsed;
        }

        public Texture2D Texture(string id)
        {
            Load();
            Texture2D texture;
            if (!textures.TryGetValue(id, out texture)) throw new SourceInterfaceException("Missing original HUD texture: " + id);
            return texture;
        }
    }
}
