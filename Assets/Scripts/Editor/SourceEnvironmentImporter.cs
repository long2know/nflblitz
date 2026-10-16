#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Blitz.AssetImport;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Blitz.Editor
{
    public static class SourceEnvironmentImporter
    {
        public const string Destination = "Assets/Imported/source-environment";
        public const string PrefabPath = Destination + "/Resources/" + SourceEnvironmentProvider.ResourceKey + ".prefab";

        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string root = Required(args, "-environmentBundleRoot");
            string sha256 = Required(args, "-environmentManifestSha256");
            Import(root, sha256);
        }

        public static SourceEnvironmentProvider Import(string explicitBundleRoot, string expectedManifestSha256)
        {
            var source = new AuthorizedAssetRoot(explicitBundleRoot);
            byte[] manifestBytes = source.ReadBytes("environment.json", 1024 * 1024);
            SourceEnvironmentBundle bundle = SourceEnvironmentReader.Read(manifestBytes, expectedManifestSha256,
                path => source.ReadBytes(path, 32 * 1024 * 1024));
            var destination = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            destination.NewPath(Destination);
            EnsureFolder(destination, "Assets/Imported");
            EnsureFolder(destination, Destination);
            WriteNew(destination, Destination + "/environment.json", manifestBytes);
            WriteNew(destination, Destination + "/OriginalEnvironment.shader", Encoding.UTF8.GetBytes(ShaderSource));
            AssetDatabase.ImportAsset(Destination + "/environment.json", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(Destination + "/OriginalEnvironment.shader", ImportAssetOptions.ForceSynchronousImport);
            TextAsset provenance = AssetDatabase.LoadAssetAtPath<TextAsset>(Destination + "/environment.json");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Destination + "/OriginalEnvironment.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new SourceEnvironmentException("Original environment shader did not compile; no generic material fallback.");
            var meshes = new Mesh[bundle.Meshes.Length];
            var textures = new Texture2D[bundle.TextureRgba.Length];
            GameObject hierarchy = null;
            try
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    meshes[i] = CreateMesh(bundle.Meshes[i], bundle.Manifest.meshes[i].sourceSet + "/" + bundle.Manifest.meshes[i].name);
                    AssetDatabase.CreateAsset(meshes[i], Destination + "/mesh-" + i.ToString("D3") + ".asset");
                    meshes[i].name = bundle.Manifest.meshes[i].sourceSet + "/" + bundle.Manifest.meshes[i].name;
                    EditorUtility.SetDirty(meshes[i]);
                }
                for (int i = 0; i < textures.Length; i++)
                {
                    textures[i] = CreateTexture(bundle.TextureRgba[i], bundle.Manifest.textures[i]);
                    AssetDatabase.CreateAsset(textures[i], Destination + "/texture-" + i.ToString("D3") + ".asset");
                    textures[i].name = bundle.Manifest.textures[i].sourcePath;
                    EditorUtility.SetDirty(textures[i]);
                }
                hierarchy = new GameObject("Original field2 / stadpie");
                var renderers = new MeshRenderer[bundle.Manifest.draws.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    SourceEnvironmentDraw draw = bundle.Manifest.draws[i];
                    var piece = new GameObject(draw.sourceSet + " / " + draw.name);
                    piece.transform.SetParent(hierarchy.transform, false);
                    piece.isStatic = true;
                    piece.AddComponent<MeshFilter>().sharedMesh = meshes[draw.meshIndex];
                    MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    int[] slots = bundle.Meshes[draw.meshIndex].MaterialSlots;
                    var materials = new Material[slots.Length];
                    for (int j = 0; j < slots.Length; j++)
                    {
                        SourceEnvironmentSlot slot = bundle.Manifest.slots[slots[j]];
                        if (slot.sourceSet != draw.sourceSet || slot.textureIndex < 0)
                            throw new SourceEnvironmentException("Missing exact original draw texture: " + draw.name + "/" + slot.selectedName);
                        Material material = CreateMaterial(shader, textures[slot.textureIndex], draw, slot.selectedName);
                        AssetDatabase.CreateAsset(material, Destination + "/draw-" + i.ToString("D3") + "-material-" + j.ToString("D2") + ".asset");
                        material.name = draw.name + "/" + slot.selectedName;
                        EditorUtility.SetDirty(material);
                        materials[j] = material;
                    }
                    renderer.sharedMaterials = materials;
                    renderers[i] = renderer;
                }
                SourceEnvironmentProvider provider = hierarchy.AddComponent<SourceEnvironmentProvider>();
                provider.InitializeGenerated(provenance, expectedManifestSha256, meshes, textures, renderers);
                EnsureFolder(destination, Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(hierarchy, PrefabPath);
                if (prefab == null) throw new SourceEnvironmentException("Original environment prefab was not persisted.");
                SourceEnvironmentProvider saved = prefab.GetComponent<SourceEnvironmentProvider>();
                saved.ValidateGeneratedWorld();
                AssetDatabase.SaveAssets();
                Debug.Log("PASS: persisted original source environment " + PrefabPath + "; 99 source polysets, " +
                    renderers.Length + " runtime draw pieces, 18 unrendered visibility volumes, " + textures.Length +
                    " exact original textures; manifest SHA256=" + expectedManifestSha256 + ".");
                return saved;
            }
            finally
            {
                if (hierarchy != null) Object.DestroyImmediate(hierarchy);
                foreach (Mesh mesh in meshes) if (mesh != null && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
                foreach (Texture2D texture in textures) if (texture != null && !AssetDatabase.Contains(texture)) Object.DestroyImmediate(texture);
            }
        }

        public static Mesh CreateMesh(SourceEnvironmentMeshData data, string name)
        {
            int count = data.Positions.Length / 3;
            var positions = new Vector3[count];
            var uvs = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]);
                uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
            }
            var mesh = new Mesh { name = name, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.subMeshCount = data.Indices.Length;
            for (int submesh = 0; submesh < data.Indices.Length; submesh++)
                mesh.SetTriangles(data.Indices[submesh], submesh, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Texture2D CreateTexture(byte[] rgba, SourceEnvironmentTextureInfo info)
        {
            SourceEnvironmentReader.ReadTexture(rgba, info);
            var texture = new Texture2D(info.width, info.height, TextureFormat.RGBA32, info.mipCount, false)
            {
                name = info.sourcePath,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = info.trilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
                anisoLevel = 0,
                mipMapBias = info.lodBias
            };
            int offset = 0;
            for (int mip = 0; mip < info.mipCount; mip++)
            {
                int length = Math.Max(1, info.width >> mip) * Math.Max(1, info.height >> mip);
                var pixels = new Color32[length];
                for (int pixel = 0; pixel < length; pixel++)
                {
                    int p = offset + pixel * 4;
                    pixels[pixel] = new Color32(rgba[p], rgba[p + 1], rgba[p + 2], rgba[p + 3]);
                }
                texture.SetPixels32(pixels, mip);
                offset += length * 4;
            }
            texture.Apply(false, false);
            VerifyTexturePixels(texture, rgba, info);
            return texture;
        }

        internal static void VerifyTexturePixels(Texture2D texture, byte[] rgba, SourceEnvironmentTextureInfo info)
        {
            if (texture.width != info.width || texture.height != info.height || texture.mipmapCount != info.mipCount ||
                texture.wrapMode != TextureWrapMode.Repeat || info.samplingWrap != "GR_TEXTURECLAMP_WRAP")
                throw new SourceEnvironmentException("Unity changed original texture dimensions/mips/bound WRAP sampler: " + info.sourcePath);
            int offset = 0;
            for (int mip = 0; mip < info.mipCount; mip++)
            {
                Color32[] pixels = texture.GetPixels32(mip);
                foreach (Color32 pixel in pixels)
                {
                    if (pixel.r != rgba[offset] || pixel.g != rgba[offset + 1] ||
                        pixel.b != rgba[offset + 2] || pixel.a != rgba[offset + 3])
                        throw new SourceEnvironmentException("Unity changed original texture channel/origin/mip pixels: " + info.sourcePath);
                    offset += 4;
                }
            }
            if (offset != rgba.Length) throw new SourceEnvironmentException("Unity original texture mip byte length differs: " + info.sourcePath);
        }

        private static Material CreateMaterial(Shader shader, Texture2D texture, SourceEnvironmentDraw draw, string selectedName)
        {
            SourceEnvironmentReader.ValidateDrawState(draw.state, draw.name);
            SourceEnvironmentDrawState state = draw.state;
            var material = new Material(shader) { name = draw.name + "/" + selectedName, mainTexture = texture, renderQueue = draw.renderQueue };
            material.SetInt("_ZTest", (int)(state.depthFunction == "GR_CMP_ALWAYS" ? CompareFunction.Always : CompareFunction.Less));
            material.SetInt("_SrcBlend", (int)Blend(state.rgbSource));
            material.SetInt("_DstBlend", (int)Blend(state.rgbDestination));
            material.SetInt("_SrcBlendA", (int)Blend(state.alphaSourceBlend));
            material.SetInt("_DstBlendA", (int)Blend(state.alphaDestinationBlend));
            material.SetInt("_Cull", (int)CullMode.Back);
            material.SetFloat("_AlphaTest", state.alphaFunction == "GR_CMP_GREATER" ? 1 : 0);
            material.SetFloat("_Cutoff", state.alphaValue / 255f);
            material.SetFloat("_TextureTimesAlpha", state.colorFunction == "GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA" ? 1 : 0);
            material.SetFloat("_ConstantAlphaSource", state.alphaSource == "GR_ALPHASOURCE_CC_ALPHA" ? 1 : 0);
            material.SetFloat("_ConstantAlpha", 1);
            material.SetFloat("_DepthBias", -state.depthBias);
            return material;
        }

        private static BlendMode Blend(string name)
        {
            if (name == "GR_BLEND_ONE") return BlendMode.One;
            if (name == "GR_BLEND_ZERO") return BlendMode.Zero;
            if (name == "GR_BLEND_SRC_ALPHA") return BlendMode.SrcAlpha;
            if (name == "GR_BLEND_ONE_MINUS_SRC_ALPHA") return BlendMode.OneMinusSrcAlpha;
            throw new SourceEnvironmentException("Unsupported original blend identity: " + name);
        }

        private static void EnsureFolder(AuthorizedAssetRoot guard, string path)
        {
            if (AssetDatabase.IsValidFolder(path)) { guard.ExistingDirectory(path); return; }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(guard, parent);
            guard.NewPath(path);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new SourceEnvironmentException("Could not reserve new environment asset folder: " + path);
        }

        private static void WriteNew(AuthorizedAssetRoot guard, string relative, byte[] data)
        {
            using (var stream = new FileStream(guard.NewPath(relative), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(data, 0, data.Length);
        }

        private static string Required(string[] args, string key)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key && !string.IsNullOrWhiteSpace(args[i + 1])) return args[i + 1];
            throw new SourceEnvironmentException("Explicit source environment command argument required: " + key);
        }

        private const string ShaderSource = @"Shader ""Blitz/OriginalEnvironment""
{
    Properties
    {
        _MainTex (""Exact original texture"", 2D) = ""white"" {}
        _ZTest (""Depth function"", Float) = 2
        _SrcBlend (""RGB source"", Float) = 1
        _DstBlend (""RGB destination"", Float) = 0
        _SrcBlendA (""Alpha source"", Float) = 1
        _DstBlendA (""Alpha destination"", Float) = 0
        _Cull (""Cull"", Float) = 2
        _AlphaTest (""Alpha test"", Float) = 0
        _Cutoff (""Original alpha threshold"", Float) = 0
        _TextureTimesAlpha (""Texture times alpha"", Float) = 0
        _ConstantAlphaSource (""Constant alpha source"", Float) = 0
        _ConstantAlpha (""Constant alpha"", Float) = 1
        _DepthBias (""Raster depth bias adapter"", Float) = 0
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite On
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            Offset 0, [_DepthBias]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            sampler2D _MainTex;
            float _AlphaTest, _Cutoff, _TextureTimesAlpha, _ConstantAlphaSource, _ConstantAlpha;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                color.a = lerp(color.a, _ConstantAlpha, _ConstantAlphaSource);
                if (_AlphaTest > 0.5 && color.a <= _Cutoff) discard;
                color.rgb *= lerp(1.0, color.a, _TextureTimesAlpha);
                return color;
            }
            ENDCG
        }
    }
}";
    }
}
#endif
