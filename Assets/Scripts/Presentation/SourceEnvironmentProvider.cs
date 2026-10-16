using System;
using Blitz.AssetImport;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceEnvironmentProvider : MonoBehaviour
    {
        public const string ResourceKey = "BlitzSourceEnvironment/source-field2-stadpie/world";
        [SerializeField] private TextAsset provenance;
        [SerializeField] private string manifestSha256;
        [SerializeField] private Mesh[] sourceMeshes;
        [SerializeField] private Texture2D[] sourceTextures;
        [SerializeField] private MeshRenderer[] orderedDraws;

        public string ManifestSha256 { get { return manifestSha256; } }
        public TextAsset Provenance { get { return provenance; } }
        public int DrawCount { get { return orderedDraws == null ? 0 : orderedDraws.Length; } }

        public void InitializeGenerated(TextAsset manifest, string sha256, Mesh[] meshes,
            Texture2D[] textures, MeshRenderer[] renderers)
        {
            provenance = manifest;
            manifestSha256 = sha256;
            sourceMeshes = meshes;
            sourceTextures = textures;
            orderedDraws = renderers;
            ValidateGeneratedWorld();
        }

        public void ValidateGeneratedWorld()
        {
            if (provenance == null) throw new SourceEnvironmentException("Original environment provenance is missing; no generic world is substituted.");
            SourceEnvironmentManifest manifest = SourceEnvironmentReader.ReadManifest(provenance.bytes, manifestSha256);
            if (sourceMeshes == null || sourceMeshes.Length != manifest.meshes.Length ||
                sourceTextures == null || sourceTextures.Length != manifest.textures.Length ||
                orderedDraws == null || orderedDraws.Length != manifest.draws.Length)
                throw new SourceEnvironmentException("Original environment native mesh/texture/draw inventory is incomplete.");
            for (int i = 0; i < sourceMeshes.Length; i++)
            {
                SourceEnvironmentMeshInfo info = manifest.meshes[i];
                Mesh mesh = sourceMeshes[i];
                if (mesh == null || mesh.vertexCount != info.vertexCount || mesh.subMeshCount != info.submeshCount)
                    throw new SourceEnvironmentException("Missing/changed native original mesh: " + info.sourceSet + "/" + info.name);
            }
            for (int i = 0; i < sourceTextures.Length; i++)
            {
                SourceEnvironmentTextureInfo info = manifest.textures[i];
                Texture2D texture = sourceTextures[i];
                if (texture == null || texture.name != info.sourcePath || texture.width != info.width ||
                    texture.height != info.height || texture.mipmapCount != info.mipCount || texture.format != TextureFormat.RGBA32 ||
                    info.samplingWrap != "GR_TEXTURECLAMP_WRAP" || texture.wrapMode != TextureWrapMode.Repeat)
                    throw new SourceEnvironmentException("Missing/changed exact original texture: " + info.sourcePath);
            }
            for (int i = 0; i < orderedDraws.Length; i++)
            {
                SourceEnvironmentDraw draw = manifest.draws[i];
                MeshRenderer renderer = orderedDraws[i];
                MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh != sourceMeshes[draw.meshIndex] ||
                    renderer.sharedMaterials.Length != manifest.meshes[draw.meshIndex].submeshCount)
                    throw new SourceEnvironmentException("Missing/changed original draw piece: " + draw.name);
                Material[] materials = renderer.sharedMaterials;
                for (int group = 0; group < materials.Length; group++)
                {
                    Material material = materials[group];
                    int slot = manifest.meshes[draw.meshIndex].materialSlots[group];
                    SourceEnvironmentSlot binding = manifest.slots[slot];
                    if (material == null || material.shader == null || material.shader.name != "Blitz/OriginalEnvironment" ||
                        binding.sourceSet != draw.sourceSet || binding.textureIndex < 0 ||
                        material.mainTexture != sourceTextures[binding.textureIndex] || material.renderQueue != draw.renderQueue)
                        throw new SourceEnvironmentException("Missing/changed original environment material: " + draw.name);
                }
            }
            if (GetComponentsInChildren<MeshRenderer>(true).Length != manifest.draws.Length)
                throw new SourceEnvironmentException("Original environment contains unlisted rendered geometry (visibility boxes must not render).");
        }

        internal static SourceEnvironmentProvider ResolveWorldMode()
        {
            string mode = RequestedMode(Application.absoluteURL);
            if (mode == "graybox")
            {
                Debug.Log("Environment mode: explicitly selected procedural graybox; not original source art.");
                return null;
            }
            GameObject asset = Resources.Load<GameObject>(ResourceKey);
            if (asset == null)
            {
                if (mode == "source")
                    throw new SourceEnvironmentException("Original environment requested, but Resources/" + ResourceKey +
                        " is missing. Run the explicit original environment import; no procedural field/stadium substitute was created.");
                Debug.Log("Environment mode: graybox (auto, original environment not installed). " +
                    "Use ?environment=source to require the original world or ?environment=graybox explicitly.");
                return null;
            }
            SourceEnvironmentProvider prefab = asset.GetComponent<SourceEnvironmentProvider>();
            if (prefab == null) throw new SourceEnvironmentException("Installed original world has no verified SourceEnvironmentProvider.");
            prefab.ValidateGeneratedWorld();
            Debug.Log("Environment mode: original field2/stadpie; manifest SHA256=" + prefab.ManifestSha256 +
                ". Gameplay line/control overlays and HUD are separate ports, not original environment artwork.");
            return prefab;
        }

        public static SourceEnvironmentProvider AttachRequired(SourceEnvironmentProvider prefab, Transform parent)
        {
            if (prefab == null) throw new SourceEnvironmentException("Required original world is missing; no procedural substitute.");
            SourceEnvironmentProvider world = Instantiate(prefab, parent, false);
            world.name = "Original field2 / stadpie";
            world.transform.localPosition = Vector3.zero;
            world.transform.localRotation = Quaternion.identity;
            world.transform.localScale = Vector3.one / SourceEnvironmentReader.ArchiveUnitsPerYard;
            return world;
        }

        public static string RequestedMode(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            int start = url.IndexOf('?');
            if (start < 0) return "";
            string result = "";
            foreach (string pair in url.Substring(start + 1).Split('#')[0].Split('&'))
            {
                string[] parts = pair.Split('=');
                if (parts[0] != "environment") continue;
                if (result.Length > 0 || parts.Length != 2 || (parts[1] != "source" && parts[1] != "graybox"))
                    throw new SourceEnvironmentException("Environment mode must be a single ?environment=source or ?environment=graybox.");
                result = parts[1];
            }
            return result;
        }
    }
}
