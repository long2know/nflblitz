using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class SourceEnvironmentException : InvalidOperationException
    {
        public SourceEnvironmentException(string message) : base(message) { }
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentInput
    {
        [DataMember] public string path;
        [DataMember] public int bytes;
        [DataMember] public string gitBlobSha1;
        [DataMember] public string sha256;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentMeshInfo
    {
        [DataMember] public string sourceSet;
        [DataMember] public string name;
        [DataMember] public string role;
        [DataMember] public int sourceVertexCount;
        [DataMember] public int sourceUvCount;
        [DataMember] public int triangleCount;
        [DataMember] public int vertexCount;
        [DataMember] public int submeshCount;
        [DataMember] public int[] materialSlots;
        [DataMember] public float[] boundsSource;
        [DataMember] public float[] boundsUnity;
        [DataMember] public string meshPath;
        [DataMember] public string meshSha256;
        [DataMember] public int meshBytes;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentTextureInfo
    {
        [DataMember] public string sourcePath;
        [DataMember] public string sourceGitBlobSha1;
        [DataMember] public string sourceSha256;
        [DataMember] public string sourceFormat;
        [DataMember] public string samplingWrap;
        [DataMember] public uint[] sourceHeader;
        [DataMember] public string rgbaPath;
        [DataMember] public string rgbaSha256;
        [DataMember] public int rgbaBytes;
        [DataMember] public int width;
        [DataMember] public int height;
        [DataMember] public int mipCount;
        [DataMember] public int filterType;
        [DataMember] public float lodBias;
        [DataMember] public bool trilinear;
        [DataMember] public bool hasAlpha;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentSlot
    {
        [DataMember] public string sourceSet;
        [DataMember] public string sourceTexture;
        [DataMember] public string authoredName;
        [DataMember] public string selectedName;
        [DataMember] public int textureIndex;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentDrawState
    {
        [DataMember] public string depthMode;
        [DataMember] public string depthFunction;
        [DataMember] public string depthMask;
        [DataMember] public int depthBias;
        [DataMember] public string colorFunction;
        [DataMember] public string alphaSource;
        [DataMember] public string textureFunction;
        [DataMember] public string clampMode;
        [DataMember] public string rgbSource;
        [DataMember] public string rgbDestination;
        [DataMember] public string alphaSourceBlend;
        [DataMember] public string alphaDestinationBlend;
        [DataMember] public string chromaMode;
        [DataMember] public int chromaValue;
        [DataMember] public string alphaFunction;
        [DataMember] public int alphaValue;
        [DataMember] public string cullMode;
        [DataMember] public int zone1;
        [DataMember] public int zone2;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentDraw
    {
        [DataMember] public string sourceSet;
        [DataMember] public string name;
        [DataMember(Name = "pass")] public string renderPass;
        [DataMember] public string sourceTable;
        [DataMember] public int sourceOrder;
        [DataMember] public int meshIndex;
        [DataMember] public int renderQueue;
        [DataMember] public SourceEnvironmentDrawState state;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentZone
    {
        [DataMember] public int zone;
        [DataMember] public string name;
        [DataMember] public int meshIndex;
    }

    [Serializable, DataContract]
    public sealed class SourceEnvironmentManifest
    {
        [DataMember] public string schema;
        [DataMember] public string sourceRepository;
        [DataMember] public string sourceCommit;
        [DataMember] public string sourceRootTree;
        [DataMember] public string toolsRepository;
        [DataMember] public string toolsCommit;
        [DataMember] public string stableId;
        [DataMember] public bool reuseAuthorizationConfirmed;
        [DataMember] public string authorizationReference;
        [DataMember] public float archiveUnitsPerYard;
        [DataMember] public string unityAxes;
        [DataMember] public string sourceTriangleOrder;
        [DataMember] public bool reverseDeclaredTriangle;
        [DataMember] public float[] originSource;
        [DataMember] public float[] scaleSource;
        [DataMember] public int fieldSelector;
        [DataMember] public int stadiumSelector;
        [DataMember] public string homePrefix;
        [DataMember] public SourceEnvironmentInput[] inputs;
        [DataMember] public SourceEnvironmentMeshInfo[] meshes;
        [DataMember] public SourceEnvironmentTextureInfo[] textures;
        [DataMember] public SourceEnvironmentSlot[] slots;
        [DataMember] public SourceEnvironmentDraw[] draws;
        [DataMember] public SourceEnvironmentZone[] zoneVolumes;
        [DataMember] public string[] conversionNotes;
        [DataMember] public string upstreamNotice;
    }

    public sealed class SourceEnvironmentMeshData
    {
        public readonly float[] Positions;
        public readonly float[] Uvs;
        public readonly int[] MaterialSlots;
        public readonly int[][] Indices;

        internal SourceEnvironmentMeshData(float[] positions, float[] uvs, int[] slots, int[][] indices)
        {
            Positions = positions;
            Uvs = uvs;
            MaterialSlots = slots;
            Indices = indices;
        }
    }

    public sealed class SourceEnvironmentBundle
    {
        public readonly SourceEnvironmentManifest Manifest;
        public readonly string ManifestSha256;
        public readonly SourceEnvironmentMeshData[] Meshes;
        public readonly byte[][] TextureRgba;

        internal SourceEnvironmentBundle(SourceEnvironmentManifest manifest, string sha256,
            SourceEnvironmentMeshData[] meshes, byte[][] rgba)
        {
            Manifest = manifest;
            ManifestSha256 = sha256;
            Meshes = meshes;
            TextureRgba = rgba;
        }
    }

    public static class SourceEnvironmentReader
    {
        public const string SourceCommit = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string ToolsCommit = "1792293aaaea00a919b59e8c05716978e85b4333";
        public const string StableId = "source-field2-stadpie";
        public const float ArchiveUnitsPerYard = 7.8f;
        private static readonly Dictionary<string, string> RequiredBlobs = new Dictionary<string, string>
        {
            { "models/field2.sdl", "32c1a1da778dc6be46f3c516e9fabd3804e657ae" },
            { "models/stadpie.sdl", "e28653b5db04e609872aa4bdd35a969c22c97675" },
            { "models/models.mak", "75fb787ac9096101b51c920ba32cadbb593315f6" },
            { "data2.c", "ff86ddb4995e40974ec08470a44eb65a2dac17f3" },
            { "field.c", "2821ba68986cb7e62c42cf920747c5ba9b91ea37" },
            { "include/nstadlim.h", "0bcc934be450cfc528e29082d0cc138652c36edf" },
            { "include/id.h", "d2cc50865dd105c4143ac6b769765bd859cc4ff4" },
            { "lib/ani3d/pipe3D.c", "0966ecaf0d7eef7d9c635a97899a421ce1eeea41" }
        };

        public static SourceEnvironmentBundle Read(byte[] manifestBytes, string expectedManifestSha256,
            Func<string, byte[]> readBundleFile)
        {
            Require(readBundleFile != null, "An explicit local/bundled file reader is required.");
            SourceEnvironmentManifest manifest = ReadManifest(manifestBytes, expectedManifestSha256);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var meshes = new SourceEnvironmentMeshData[manifest.meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                SourceEnvironmentMeshInfo info = manifest.meshes[i];
                Require(paths.Add(info.meshPath), "Duplicate converted mesh path: " + info.meshPath);
                meshes[i] = ReadMesh(readBundleFile(info.meshPath), info, manifest.slots.Length);
            }
            var textures = new byte[manifest.textures.Length][];
            for (int i = 0; i < textures.Length; i++)
            {
                SourceEnvironmentTextureInfo info = manifest.textures[i];
                Require(paths.Add(info.rgbaPath), "Duplicate converted texture path: " + info.rgbaPath);
                textures[i] = ReadTexture(readBundleFile(info.rgbaPath), info);
            }
            return new SourceEnvironmentBundle(manifest, expectedManifestSha256, meshes, textures);
        }

        public static SourceEnvironmentManifest ReadManifest(byte[] bytes, string expectedSha256)
        {
            Hash(bytes, expectedSha256, "environment.json", 1024 * 1024);
            SourceEnvironmentManifest manifest;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                    manifest = (SourceEnvironmentManifest)new DataContractJsonSerializer(typeof(SourceEnvironmentManifest)).ReadObject(stream);
            }
            catch (Exception error) when (error is SerializationException || error is System.Xml.XmlException)
            {
                throw new SourceEnvironmentException("Invalid source environment manifest JSON: " + error.Message);
            }
            Require(manifest != null && manifest.schema == "blitz-source-environment/v1",
                "Expected source environment schema blitz-source-environment/v1.");
            Require(manifest.sourceRepository == "historicalsource/nflblitz2000" && manifest.sourceCommit == SourceCommit &&
                manifest.sourceRootTree == "a7e0352125d46075b7d98e87253ab89b9160a4c7" &&
                manifest.toolsRepository == "historicalsource/midwayvideo" && manifest.toolsCommit == ToolsCommit,
                "Source environment original/converter revision identity mismatch.");
            Require(manifest.stableId == StableId && manifest.reuseAuthorizationConfirmed &&
                !string.IsNullOrWhiteSpace(manifest.authorizationReference), "Explicit source environment local-use identity is required.");
            Require(manifest.archiveUnitsPerYard == ArchiveUnitsPerYard && manifest.unityAxes == "+X,+Y,-Z" &&
                manifest.sourceTriangleOrder == "Renderer321" && !manifest.reverseDeclaredTriangle &&
                Equal(manifest.originSource, new[] { 0f, 0f, 0f }) && Equal(manifest.scaleSource, new[] { 65f, 65f, 57.5f }),
                "Source environment origin/axes/scale/winding differ from the active original recipe.");
            Require(manifest.fieldSelector >= 0 && manifest.fieldSelector <= 6 &&
                manifest.stadiumSelector >= 0 && manifest.stadiumSelector <= 3 && manifest.homePrefix == "BEA",
                "Unsupported source environment selector/home-prefix identity.");
            Require(manifest.inputs != null && manifest.inputs.Length >= RequiredBlobs.Count && manifest.inputs.Length <= 128,
                "Source environment input fingerprints are missing/oversized.");
            var identities = new Dictionary<string, SourceEnvironmentInput>(StringComparer.Ordinal);
            foreach (SourceEnvironmentInput input in manifest.inputs)
            {
                Require(input != null, "Null original source environment input.");
                AuthorizedAssetRoot.ValidateRelativePath(input.path);
                Hex(input.gitBlobSha1, 40, input.path);
                Hex(input.sha256, 64, input.path);
                Require(input.bytes > 0 && input.bytes <= 32 * 1024 * 1024 && !identities.ContainsKey(input.path),
                    "Invalid/duplicate original environment input: " + input.path);
                identities.Add(input.path, input);
            }
            foreach (KeyValuePair<string, string> required in RequiredBlobs)
                Require(identities.ContainsKey(required.Key) && identities[required.Key].gitBlobSha1 == required.Value,
                    "Missing/mismatched required original environment input: " + required.Key);
            Require(manifest.meshes != null && manifest.meshes.Length == 99 && manifest.slots != null && manifest.slots.Length == 27 &&
                manifest.textures != null && manifest.textures.Length > 0 && manifest.textures.Length <= 64 &&
                manifest.draws != null && manifest.draws.Length == (manifest.stadiumSelector == 3 ? 12 : 78) &&
                manifest.zoneVolumes != null && manifest.zoneVolumes.Length == 18,
                "Active original environment mesh/material/draw/zone inventory differs.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            int sourceVertices = 0, triangles = 0, volumes = 0;
            foreach (SourceEnvironmentMeshInfo mesh in manifest.meshes)
            {
                Require(mesh != null && (mesh.sourceSet == "field2" || mesh.sourceSet == "stadpie"),
                    "Missing/unknown original environment mesh set.");
                AuthorizedAssetRoot.ValidateRelativePath(mesh.name);
                AuthorizedAssetRoot.ValidateRelativePath(mesh.meshPath);
                Require(names.Add(mesh.sourceSet + "/" + mesh.name) &&
                    (mesh.role == "render" || mesh.role == "zone-volume" || mesh.role == "inactive-authoring") &&
                    mesh.sourceVertexCount > 0 && mesh.sourceVertexCount <= 10240 && mesh.sourceUvCount > 0 && mesh.sourceUvCount <= 30720 &&
                    mesh.triangleCount > 0 && mesh.triangleCount <= 40960 &&
                    mesh.vertexCount > 0 && mesh.vertexCount <= mesh.triangleCount * 3 &&
                    mesh.submeshCount > 0 && mesh.submeshCount <= manifest.slots.Length &&
                    mesh.materialSlots != null && mesh.materialSlots.Length == mesh.submeshCount,
                    "Invalid original environment mesh counts/role: " + mesh.name);
                var materialSlots = new HashSet<int>();
                foreach (int slot in mesh.materialSlots)
                    Require(slot >= 0 && slot < manifest.slots.Length && materialSlots.Add(slot),
                        "Invalid/duplicate original mesh material identity: " + mesh.name);
                Bounds(mesh.boundsSource, mesh.name);
                Bounds(mesh.boundsUnity, mesh.name);
                Hex(mesh.meshSha256, 64, mesh.meshPath);
                sourceVertices += mesh.sourceVertexCount;
                triangles += mesh.triangleCount;
                if (mesh.role == "zone-volume") volumes++;
            }
            Require(sourceVertices == 2541 && triangles == 2517 && volumes == 18,
                "Active original environment source vertex/triangle/visibility-volume totals differ.");
            foreach (SourceEnvironmentSlot slot in manifest.slots)
            {
                Require(slot != null && (slot.sourceSet == "field2" || slot.sourceSet == "stadpie") &&
                    !string.IsNullOrEmpty(slot.authoredName) && !string.IsNullOrEmpty(slot.selectedName),
                    "Missing source environment material slot identity.");
                Require(slot.textureIndex >= 0 && slot.textureIndex < manifest.textures.Length ||
                    manifest.stadiumSelector == 3 && slot.sourceSet == "stadpie" && slot.textureIndex == -1,
                    "Missing original material texture: " + slot.sourceSet + "/" + slot.selectedName);
            }
            foreach (SourceEnvironmentTextureInfo texture in manifest.textures)
            {
                Require(texture != null && texture.sourcePath != null && identities.ContainsKey(texture.sourcePath) &&
                    identities[texture.sourcePath].gitBlobSha1 == texture.sourceGitBlobSha1 &&
                    identities[texture.sourcePath].sha256 == texture.sourceSha256,
                    "Original environment texture input/provenance identity differs.");
                AuthorizedAssetRoot.ValidateRelativePath(texture.rgbaPath);
                Hex(texture.rgbaSha256, 64, texture.rgbaPath);
            }
            var drawn = new HashSet<int>();
            foreach (SourceEnvironmentDraw draw in manifest.draws)
            {
                Require(draw != null && draw.meshIndex >= 0 && draw.meshIndex < manifest.meshes.Length &&
                    drawn.Add(draw.meshIndex), "Duplicate/out-of-range original environment draw.");
                SourceEnvironmentMeshInfo mesh = manifest.meshes[draw.meshIndex];
                Require(mesh.role == "render" && mesh.name == draw.name && mesh.sourceSet == draw.sourceSet,
                    "Original visibility volume/inactive limb cannot render: " + draw.name);
                Require((draw.renderPass == "field" || draw.renderPass == "stadium") && draw.renderQueue >= 1800 && draw.renderQueue < 2000 ||
                    (draw.renderPass == "field2" || draw.renderPass == "goalposts") && draw.renderQueue >= 2400 && draw.renderQueue < 2500,
                    "Original FIELD/PLAYER/FIELD2 ordering changed: " + draw.name);
                ValidateDrawState(draw.state, draw.name);
            }
            var zones = new HashSet<int>();
            foreach (SourceEnvironmentZone zone in manifest.zoneVolumes)
            {
                Require(zone != null && zone.zone >= 0 && zone.zone < 18 && zones.Add(zone.zone) &&
                    zone.meshIndex >= 0 && zone.meshIndex < manifest.meshes.Length,
                    "Invalid original stadium draw-zone volume.");
                SourceEnvironmentMeshInfo mesh = manifest.meshes[zone.meshIndex];
                Require(mesh.role == "zone-volume" && mesh.name == zone.name && mesh.sourceVertexCount == 8 &&
                    zone.name == "box" + (zone.zone % 3 + 1) + (char)('a' + zone.zone / 3),
                    "Original draw-zone/box semantic mapping differs.");
            }
            Require(manifest.conversionNotes != null && !string.IsNullOrEmpty(manifest.upstreamNotice),
                "Original environment provenance/notices are required.");
            return manifest;
        }

        public static SourceEnvironmentMeshData ReadMesh(byte[] data, SourceEnvironmentMeshInfo info, int slotCount)
        {
            Require(info != null, "Missing converted environment mesh identity.");
            Hash(data, info.meshSha256, info.meshPath, 8 * 1024 * 1024);
            Require(data.Length == info.meshBytes, "Converted mesh length mismatch: " + info.meshPath);
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data, false), Encoding.ASCII))
                {
                    Require(Encoding.ASCII.GetString(reader.ReadBytes(8)) == "BZENM001", "Wrong converted mesh magic: " + info.meshPath);
                    int count = reader.ReadInt32(), groups = reader.ReadInt32();
                    Require(count == info.vertexCount && count > 0 && count <= 122880 &&
                        groups == info.submeshCount && groups > 0 && groups <= slotCount,
                        "Converted mesh header/count mismatch: " + info.meshPath);
                    var positions = new float[count * 3];
                    var uvs = new float[count * 2];
                    var bounds = new[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
                    for (int i = 0; i < count; i++)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            float value = reader.ReadSingle();
                            Require(Finite(value) && Math.Abs(value) <= 100000f, "Nonfinite/oversized mesh coordinate: " + info.meshPath);
                            positions[i * 3 + axis] = value;
                            bounds[axis] = Math.Min(bounds[axis], value);
                            bounds[axis + 3] = Math.Max(bounds[axis + 3], value);
                        }
                        for (int axis = 0; axis < 2; axis++)
                        {
                            float value = reader.ReadSingle();
                            Require(Finite(value), "Nonfinite mesh UV: " + info.meshPath);
                            uvs[i * 2 + axis] = value;
                        }
                    }
                    Require(Equal(bounds, info.boundsUnity), "Converted mesh bounds mismatch: " + info.meshPath);
                    var slots = new int[groups];
                    var indices = new int[groups][];
                    var seen = new HashSet<int>();
                    int indexTotal = 0;
                    for (int group = 0; group < groups; group++)
                    {
                        int slot = reader.ReadInt32(), length = reader.ReadInt32();
                        Require(slot >= 0 && slot < slotCount && seen.Add(slot) &&
                            info.materialSlots != null && info.materialSlots.Length == groups && slot == info.materialSlots[group] &&
                            length > 0 && length % 3 == 0 && length <= 122880,
                            "Invalid mesh material/index count: " + info.meshPath);
                        slots[group] = slot;
                        indices[group] = new int[length];
                        for (int i = 0; i < length; i++)
                        {
                            int index = reader.ReadInt32();
                            Require(index >= 0 && index < count, "Converted mesh vertex index out of range: " + info.meshPath);
                            indices[group][i] = index;
                        }
                        indexTotal += length;
                    }
                    Require(indexTotal == info.triangleCount * 3 && reader.BaseStream.Position == data.Length,
                        "Converted mesh triangle total/trailing bytes differ: " + info.meshPath);
                    return new SourceEnvironmentMeshData(positions, uvs, slots, indices);
                }
            }
            catch (EndOfStreamException)
            {
                throw new SourceEnvironmentException("Truncated converted environment mesh: " + info.meshPath);
            }
        }

        public static byte[] ReadTexture(byte[] data, SourceEnvironmentTextureInfo info)
        {
            Require(info != null, "Missing original environment texture identity.");
            AuthorizedAssetRoot.ValidateRelativePath(info.sourcePath);
            AuthorizedAssetRoot.ValidateRelativePath(info.rgbaPath);
            Hex(info.sourceGitBlobSha1, 40, info.sourcePath);
            Hex(info.sourceSha256, 64, info.sourcePath);
            Hash(data, info.rgbaSha256, info.rgbaPath, 32 * 1024 * 1024);
            Require(info.width > 0 && info.height > 0 && info.width <= 4096 && info.height <= 4096 &&
                info.mipCount > 0 && info.mipCount <= 9 && info.filterType >= 0 && info.filterType <= 2 && Finite(info.lodBias),
                "Invalid original texture dimensions/mips/filter: " + info.sourcePath);
            Require(info.sourceFormat == "TGA24" || info.sourceFormat == "TGA32" ||
                info.sourceFormat == "WMS/ARGB1555" || info.sourceFormat == "WMS/ARGB4444" || info.sourceFormat == "WMS/RGB565",
                "Unsupported original texture format: " + info.sourcePath + "/" + info.sourceFormat);
            Require(info.samplingWrap == "GR_TEXTURECLAMP_WRAP",
                "Original texture binding must restore WRAP after limb CLAMP state: " + info.sourcePath);
            int length = 0;
            for (int mip = 0; mip < info.mipCount; mip++)
                length += Math.Max(1, info.width >> mip) * Math.Max(1, info.height >> mip) * 4;
            Require(length == info.rgbaBytes && data.Length == length, "Original texture mip byte count differs: " + info.sourcePath);
            bool alpha = false;
            for (int pixel = 3; pixel < data.Length; pixel += 4) alpha |= data[pixel] != 255;
            Require(alpha == info.hasAlpha, "Original texture alpha metadata differs: " + info.sourcePath);
            return data;
        }

        public static void ValidateDrawState(SourceEnvironmentDrawState state, string identity)
        {
            Require(state != null && state.depthMode == "GR_DEPTHBUFFER_WBUFFER" && state.depthMask == "FXTRUE" &&
                (state.depthFunction == "GR_CMP_LESS" || state.depthFunction == "GR_CMP_ALWAYS") &&
                (state.depthBias == 0 || state.depthBias == 10) &&
                (state.colorFunction == "GR_COLORCOMBINE_DECAL_TEXTURE" || state.colorFunction == "GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA") &&
                (state.alphaSource == "GR_ALPHASOURCE_TEXTURE_ALPHA" || state.alphaSource == "GR_ALPHASOURCE_CC_ALPHA") &&
                state.textureFunction == "GR_TEXTURECOMBINE_DECAL" && state.clampMode == "GR_TEXTURECLAMP_CLAMP" &&
                (state.rgbSource == "GR_BLEND_ONE" || state.rgbSource == "GR_BLEND_SRC_ALPHA") &&
                (state.rgbDestination == "GR_BLEND_ZERO" || state.rgbDestination == "GR_BLEND_ONE_MINUS_SRC_ALPHA") &&
                (state.alphaSourceBlend == "GR_BLEND_ONE" || state.alphaSourceBlend == "GR_BLEND_SRC_ALPHA") &&
                (state.alphaDestinationBlend == "GR_BLEND_ZERO" || state.alphaDestinationBlend == "GR_BLEND_ONE_MINUS_SRC_ALPHA") &&
                state.chromaMode == "GR_CHROMAKEY_DISABLE" && state.chromaValue == 0 &&
                (state.alphaFunction == "GR_CMP_ALWAYS" && state.alphaValue == 0 ||
                 state.alphaFunction == "GR_CMP_GREATER" && state.alphaValue == 64) &&
                state.cullMode == "GR_CULL_NEGATIVE" &&
                state.zone1 >= -1 && state.zone1 < 18 && state.zone2 >= -1 && state.zone2 < 18,
                "Unsupported original environment draw state: " + identity);
        }

        private static void Hash(byte[] bytes, string expected, string identity, int maximum)
        {
            Hex(expected, 64, identity);
            Require(bytes != null && bytes.Length > 0 && bytes.Length <= maximum &&
                AuthorizedAssetRoot.Sha256(bytes) == expected, "Converted environment SHA256/length mismatch: " + identity);
        }

        private static void Hex(string value, int length, string identity)
        {
            Require(value != null && value.Length == length, "Missing fingerprint: " + identity);
            foreach (char c in value)
                Require(c >= '0' && c <= '9' || c >= 'a' && c <= 'f', "Invalid fingerprint: " + identity);
        }

        private static void Bounds(float[] bounds, string identity)
        {
            Require(bounds != null && bounds.Length == 6, "Missing source environment bounds: " + identity);
            for (int i = 0; i < 3; i++)
                Require(Finite(bounds[i]) && Finite(bounds[i + 3]) && bounds[i] <= bounds[i + 3],
                    "Invalid source environment bounds: " + identity);
        }

        private static bool Equal(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new SourceEnvironmentException(message);
        }
    }
}
