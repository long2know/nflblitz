using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    [Serializable, DataContract]
    public sealed class SourceFireInputRecord
    {
        [DataMember] public string path;
        [DataMember] public string payloadPath;
        [DataMember] public int bytes;
        [DataMember] public string gitBlobSha1;
        [DataMember] public string sha256;
    }

    [Serializable, DataContract]
    public sealed class SourceFireTextureRecord
    {
        [DataMember] public int inputIndex;
        [DataMember] public int width;
        [DataMember] public int height;
        [DataMember] public int bitsPerPixel;
        [DataMember] public int alphaBits;
        [DataMember] public bool topOrigin;
        [DataMember] public bool rightOrigin;
        [DataMember] public int integerAspect;
        [DataMember] public string aspectOrigin;
        [DataMember] public string nonPixelDataOrigin;
    }

    [Serializable, DataContract]
    public sealed class SourceFireSpfxManifest
    {
        [DataMember] public string schema;
        [DataMember] public string stableId;
        [DataMember] public string sourceRepository;
        [DataMember] public string sourceCommit;
        [DataMember] public string sourceTree;
        [DataMember] public string toolsCommit;
        [DataMember] public bool reuseAuthorizationConfirmed;
        [DataMember] public string authorizationReference;
        [DataMember] public string inputOrigin;
        [DataMember] public bool nativeConvertedTextureEquivalent;
        [DataMember] public bool generatedSpfxHeaderEquivalent;
        [DataMember] public float sourceGeometryFactor;
        [DataMember] public string uvOrigin;
        [DataMember] public string sourceSdlVersion;
        [DataMember] public string geometryParser;
        [DataMember] public string meshPath;
        [DataMember] public int meshBytes;
        [DataMember] public string meshSha256;
        [DataMember] public SourceFireInputRecord[] inputs;
        [DataMember] public SourceFireTextureRecord[] frames;
        [DataMember] public SourceFireInputRecord[] dustInputs;
        [DataMember] public SourceFireTextureRecord[] dustFrames;
        [DataMember] public SourceFireInputRecord helmetInput;
        [DataMember] public string helmetMeshPath;
        [DataMember] public int helmetMeshBytes;
        [DataMember] public string helmetMeshSha256;
        [DataMember] public string helmetGeometryOrigin;
        [DataMember] public string helmetUvOrigin;
        [DataMember] public float helmetGeometryFactor;
        [DataMember] public int helmetIntegerAspect;
        [DataMember] public bool nativeGeneratedHelmetHeaderEquivalent;
    }

    public sealed class SourceFireTextureFrame
    {
        private readonly byte[] rgba;
        public int Width { get; }
        public int Height { get; }
        public int IntegerAspect { get; }
        public string SourcePath { get; }
        public string SourceSha256 { get; }
        internal SourceFireTextureFrame(int width, int height, int aspect, string path, string sha256, byte[] pixels)
        { Width = width; Height = height; IntegerAspect = aspect; SourcePath = path; SourceSha256 = sha256; rgba = (byte[])pixels.Clone(); }
        public byte[] CopyRgba() { return (byte[])rgba.Clone(); }
    }

    public sealed class SourceFireGeometry
    {
        public ReadOnlyCollection<SourceVector3> Positions { get; }
        public ReadOnlyCollection<float> RawAuthoringUvs { get; }
        public ReadOnlyCollection<int> VertexIndices { get; }
        public ReadOnlyCollection<int> UvIndices { get; }
        internal SourceFireGeometry(SourceVector3[] positions, float[] uvs, int[] vertices, int[] uvIndices)
        { Positions = Array.AsReadOnly(positions); RawAuthoringUvs = Array.AsReadOnly(uvs);
            VertexIndices = Array.AsReadOnly(vertices); UvIndices = Array.AsReadOnly(uvIndices); }
    }

    public sealed class SourceFireSpfxBundle
    {
        public string ManifestSha256 { get; }
        public string AuthorizationReference { get; }
        public string InputOrigin { get { return SourceFireSpfxReader.InputOrigin; } }
        public bool NativeConvertedTextureEquivalent { get { return false; } }
        public bool GeneratedSpfxHeaderEquivalent { get { return false; } }
        public SourceFireGeometry Geometry { get; }
        public SourceFireGeometry HelmetGeometry { get; }
        public ReadOnlyCollection<SourceFireTextureFrame> Frames { get; }
        public ReadOnlyCollection<SourceFireTextureFrame> DustFrames { get; }
        public ReadOnlyCollection<SourceFireTextureFrame> Textures { get; }
        internal SourceFireSpfxBundle(string manifestSha, string authorizationReference, SourceFireGeometry geometry,
            SourceFireTextureFrame[] frames, SourceFireTextureFrame[] dustFrames = null, SourceFireGeometry helmetGeometry = null)
        {
            ManifestSha256 = manifestSha; AuthorizationReference = authorizationReference; Geometry = geometry;
            Frames = Array.AsReadOnly(frames); DustFrames = Array.AsReadOnly(dustFrames ?? new SourceFireTextureFrame[0]);
            HelmetGeometry = helmetGeometry;
            var all = new List<SourceFireTextureFrame>(frames); all.AddRange(DustFrames); Textures = all.AsReadOnly();
        }
        public void RequireDust()
        {
            SourceFireGuard.Require(DustFrames.Count == SourceFireIds.DustFrameCount,
                "SOURCE_DUST_DATA_REQUIRED: source-fire=portable dust_cloud requires the authenticated v2 bundle with all ten original DIRT TGAs; reprepare, no smoke fallback.");
        }
        public SourceFireTextureFrame Texture(SourceFireTextureKind kind, int index)
        {
            SourceFireGuard.Require(kind == SourceFireTextureKind.Trail || kind == SourceFireTextureKind.Dust, "Unknown source effect texture family.");
            if (kind == SourceFireTextureKind.Dust) RequireDust();
            var frames = kind == SourceFireTextureKind.Dust ? DustFrames : Frames;
            SourceFireGuard.Require(index >= 0 && index < frames.Count, "Source effect selected a missing original texture frame.");
            return frames[index];
        }
        public void RequireHelmet()
        {
            SourceFireGuard.Require(HelmetGeometry != null,
                "SOURCE_HELMET_DATA_REQUIRED: source-fire=portable loosehlm requires the authenticated v3 original PHELM SDL mesh; reprepare, no phead/ball/sphere fallback.");
        }
    }

    public static class SourceFireSpfxReader
    {
        public const string Schema = "blitz-source-fire-spfx-portable/v1";
        public const string DustSchema = "blitz-source-fire-spfx-portable/v2";
        public const string HelmetSchema = "blitz-source-fire-spfx-portable/v3";
        public const string HelmetGeometryOrigin = "original-phelm-SDL-factor1.2-authoring-origin-not-native-helm.h";
        public const string HelmetUvOrigin = "original-cvtmod-normalized-six-decimal-ST-ratio4-bottom-left";
        public const string HelmetSourceSha256 = "03130e8e5dab04b4d5bd64290595b884bfb1e007440d9d228db9419f47614e5a";
        public const string HelmetGeometrySha256 = "d8c14967916b24b3135839680f1419c0af1a6aeceea34f3aa6a10d8127ff59c9";
        public const string HelmetMeshId = "original/loose-helmet/limb_phelm_obj";
        public const string InputOrigin = "original-authoring-TGA32-and-SDL-portable-not-WMS";
        public const string AspectOrigin = "decoded-authoring-dimensions-Glide-enum-not-observed-WMS-header";
        public const string UvOrigin = "raw-SDL-ST-normalized-Unity-bottom-left-not-generated-spfx.h";
        public const string DustMetadataOrigin = "authenticated-TGA-v2-tag20-developer-area-excluded-from-pixel-view";
        public const string SourceCommit = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        private static readonly string[] Blobs = {
            "bee1894074a84b1cfdb1a9099e51ccf9afa5fca8",
            "fde8e36b9220b2eb3da8b6abf1aa7ea4ed96d8cf", "8c081b861ebd3f3f8adc691f57df1a253ee10802",
            "072800796bfbe02b8687b8b70a196541a3e376e6", "001509070e9f7ffcc904f3225bbd349ec1da2d4f",
            "53f8dcc2fd992667da2702a234c0b09808d2fa42", "e50465b1af090236e4adee9049f1822010ca455d",
            "e70ad0819cebb1f0b30d71d615ad7fd1bde483bb", "595d3ee60c1ac60dcb331c2992a9c909bb00af33",
            "aea9dba145c089bba00d1be6aacdbc477b866384", "511adc7b8c381ea0c27755c6ee14353f6ea08a8d",
            "5f0c93512b8bc02ac920541bf628f1f3133a5299", "985362b88f3b3d892fddeba495128f5639a7446f",
            "ada8172d3687218b46afa4af61eeadab52d57133", "5b0c6bd286af37a1e93d0e3b5f8df7380908782d",
            "b0332d728c30c6166ae499cb152ec952147f3e61", "c5070f3f51525726f5a86c8749d180d92d458e57",
            "bf4705fd2342b18f4f071bd75d9c057b6d998795"
        };
        private static readonly string[] DustBlobs = {
            "0d78216c4051c5bc64be90d53f91a27b8180c351", "bc38f27e7c37fd5f0eea0677e57749daa405a5a1",
            "eb4782ebd8f7acd76d9f76cc93613cc4483c5d60", "349726235a6ded4b4032317f304c2adf6d7f4018",
            "4d67c17a22ae91eea722f7292e0a2eb9962ccfe4", "46afae76a0cac1d0ae47a517462f5574c0055edc",
            "ed283cd05fb7d00477c1119167c8b07302032aa3", "158e4243f7826b45583ae5a249995cdf0107dc75",
            "5d424954d314604338b66738c6d90fe817bba9ac", "6ee890862ab035a73452342e9dcb7c4bf1e05b3e"
        };

        public static SourceFireSpfxBundle ReadAuthorized(string absoluteBundleRoot, string relativeManifest, string expectedManifestSha256)
        {
            var root = new AuthorizedAssetRoot(absoluteBundleRoot);
            return Read(root.ReadBytes(relativeManifest, 65536), expectedManifestSha256, p => root.ReadBytes(p, 65536));
        }

        public static SourceFireSpfxBundle Read(byte[] manifestBytes, string expectedManifestSha256, Func<string, byte[]> readFile)
        {
            SourceFireGuard.Require(readFile != null && manifestBytes != null && manifestBytes.Length <= 65536,
                "Supply the bounded manifest and explicit authorized/bundled file reader.");
            CheckHash(manifestBytes, expectedManifestSha256, "manifest");
            SourceFireSpfxManifest manifest;
            using (var stream = new MemoryStream(manifestBytes, false))
                manifest = (SourceFireSpfxManifest)new DataContractJsonSerializer(typeof(SourceFireSpfxManifest)).ReadObject(stream);
            SourceFireGuard.Require(manifest != null && (manifest.schema == Schema || manifest.schema == DustSchema ||
                manifest.schema == HelmetSchema) &&
                manifest.stableId == SourceFireIds.PortableAssetSet &&
                manifest.sourceRepository == "historicalsource/nflblitz2000" && manifest.sourceCommit == SourceCommit &&
                manifest.sourceTree == "a7e0352125d46075b7d98e87253ab89b9160a4c7" &&
                manifest.toolsCommit == "1792293aaaea00a919b59e8c05716978e85b4333", "Wrong fire schema/source/catalog identity.");
            SourceFireGuard.Require(manifest.reuseAuthorizationConfirmed, "Explicit local reuse authorization required; public source is not a license.");
            SourceFireGuard.Identity(manifest.authorizationReference, "authorization reference");
            SourceFireGuard.Require(manifest.inputOrigin == InputOrigin && !manifest.nativeConvertedTextureEquivalent &&
                !manifest.generatedSpfxHeaderEquivalent && manifest.sourceGeometryFactor == 4f && manifest.uvOrigin == UvOrigin,
                "Portable authoring input cannot be labelled native WMS/TEXUS or generated spfx.h.");
            SourceFireGuard.Require(manifest.sourceSdlVersion == "7.51" &&
                manifest.geometryParser == "existing-cv-st-triangle-reader-with-explicit-7.51-header-adapter",
                "The original7.51 SDL grammar adapter must be explicit.");
            SourceFireGuard.Require(manifest.inputs != null && manifest.inputs.Length == 18 && manifest.frames != null &&
                manifest.frames.Length == 17, "Exactly one pinned original SDL and all seventeen ordered original TRAIL TGAs required.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var frames = new SourceFireTextureFrame[17];
            for (int i = 0; i < 18; i++)
            {
                SourceFireInputRecord record = manifest.inputs[i];
                string path = i == 0 ? "models/spfx.sdl" : "img3d/trail" + i + ".tga";
                SourceFireGuard.Require(record != null && record.path == path && record.gitBlobSha1 == Blobs[i] &&
                    record.bytes == (i == 0 ? 7640 : 4140), "Wrong original input pin/order/length: " + path);
                AuthorizedAssetRoot.ValidateRelativePath(record.payloadPath);
                SourceFireGuard.Require(record.payloadPath == "inputs/" + path + ".bytes" && paths.Add(record.payloadPath),
                    "Wrong/duplicate authorized source payload: " + path);
                byte[] original = readFile(record.payloadPath);
                SourceFireGuard.Require(original != null && original.Length == record.bytes, "Missing/truncated original: " + path);
                CheckHash(original, record.sha256, path);
                SourceFireGuard.Require(GitBlob(original) == record.gitBlobSha1, "Wrong original Git blob: " + path);
                if (i > 0) frames[i - 1] = DecodeOriginalTga(original, manifest.frames[i - 1], path, record.sha256, i);
            }
            var dust = new SourceFireTextureFrame[manifest.schema != Schema ? SourceFireIds.DustFrameCount : 0];
            SourceFireGuard.Require(manifest.schema != Schema
                ? manifest.dustInputs != null && manifest.dustInputs.Length == 10 &&
                  manifest.dustFrames != null && manifest.dustFrames.Length == 10
                : manifest.dustInputs == null && manifest.dustFrames == null,
                "SOURCE_DUST_DATA_REQUIRED: v2 requires exactly ten ordered authenticated DIRT inputs/frames; v1 cannot claim dust data.");
            for (int i = 0; i < dust.Length; i++)
            {
                SourceFireInputRecord record = manifest.dustInputs[i];
                string path = "img3d/dirt" + (i + 1) + ".tga";
                SourceFireGuard.Require(record != null && record.path == path && record.gitBlobSha1 == DustBlobs[i] &&
                    record.bytes == 4458, "Wrong original DIRT input pin/order/length: " + path);
                AuthorizedAssetRoot.ValidateRelativePath(record.payloadPath);
                SourceFireGuard.Require(record.payloadPath == "inputs/" + path + ".bytes" && paths.Add(record.payloadPath),
                    "Wrong/duplicate authorized DIRT payload: " + path);
                byte[] original = readFile(record.payloadPath);
                SourceFireGuard.Require(original != null && original.Length == record.bytes, "Missing/truncated original DIRT: " + path);
                CheckHash(original, record.sha256, path);
                SourceFireGuard.Require(GitBlob(original) == record.gitBlobSha1, "Wrong original DIRT Git blob: " + path);
                SourceFireGuard.Require(manifest.dustFrames[i] != null &&
                    manifest.dustFrames[i].nonPixelDataOrigin == DustMetadataOrigin,
                    "Original DIRT nonpixel TGA developer-area adapter must be explicit.");
                dust[i] = DecodeOriginalTga(original, manifest.dustFrames[i], path, record.sha256, i);
            }
            AuthorizedAssetRoot.ValidateRelativePath(manifest.meshPath);
            SourceFireGuard.Require(manifest.meshPath == "geometry.bytes" && paths.Add(manifest.meshPath), "Wrong prepared geometry identity.");
            byte[] geometry = readFile(manifest.meshPath);
            SourceFireGuard.Require(geometry != null && geometry.Length == manifest.meshBytes, "Missing/truncated prepared SDL geometry.");
            CheckHash(geometry, manifest.meshSha256, "portable SDL geometry");
            SourceFireGeometry helmetGeometry = null;
            if (manifest.schema == HelmetSchema)
            {
                SourceFireInputRecord input = manifest.helmetInput;
                SourceFireGuard.Require(input != null && input.path == "models/phelm.sdl" &&
                    input.payloadPath == "inputs/models/phelm.sdl.bytes" && input.bytes == 19258 &&
                    input.gitBlobSha1 == "d086f6944207a0ff2fa906ef7fc9692dcfb289ec" && input.sha256 == HelmetSourceSha256 &&
                    paths.Add(input.payloadPath) && manifest.helmetGeometryOrigin == HelmetGeometryOrigin &&
                    manifest.helmetUvOrigin == HelmetUvOrigin && manifest.helmetGeometryFactor == 1.2f &&
                    manifest.helmetIntegerAspect == 4 && !manifest.nativeGeneratedHelmetHeaderEquivalent,
                    "SOURCE_HELMET_DATA_REQUIRED: exact PHELM authoring input, factor1.2/ratio4 and explicit non-native origin required.");
                byte[] original = readFile(input.payloadPath);
                SourceFireGuard.Require(original != null && original.Length == input.bytes &&
                    GitBlob(original) == input.gitBlobSha1, "Wrong original PHELM Git blob.");
                CheckHash(original, HelmetSourceSha256, input.path);
                SourceFireGuard.Require(manifest.helmetMeshPath == "helmet-geometry.bytes" &&
                    manifest.helmetMeshSha256 == HelmetGeometrySha256 &&
                    paths.Add(manifest.helmetMeshPath), "Wrong prepared PHELM geometry identity.");
                byte[] mesh = readFile(manifest.helmetMeshPath);
                SourceFireGuard.Require(mesh != null && mesh.Length == manifest.helmetMeshBytes, "Missing/truncated PHELM geometry.");
                CheckHash(mesh, manifest.helmetMeshSha256, "PHELM authoring geometry");
                helmetGeometry = ReadHelmetGeometry(mesh);
            }
            else
                SourceFireGuard.Require(manifest.helmetInput == null && manifest.helmetMeshPath == null,
                    "Legacy fire generations cannot claim unvalidated loose-helmet resources.");
            return new SourceFireSpfxBundle(expectedManifestSha256, manifest.authorizationReference, ReadGeometry(geometry), frames, dust, helmetGeometry);
        }

        public static SourceFireGeometry ReadHelmetGeometry(byte[] bytes)
        {
            SourceFireGuard.Require(bytes != null && bytes.Length == 3752, "Exact PHELM authoring geometry length required.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                SourceFireGuard.Require(Encoding.ASCII.GetString(reader.ReadBytes(8)) == "BZHLG001" &&
                    reader.ReadInt32() == 63 && reader.ReadInt32() == 105 && reader.ReadInt32() == 89,
                    "Wrong original PHELM geometry format/inventory.");
                var positions = new SourceVector3[63]; var uvs = new float[210];
                var vertices = new int[267]; var uvIndices = new int[267];
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = new SourceVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    SourceFireGuard.Finite(positions[i]);
                }
                for (int i = 0; i < uvs.Length; i++)
                {
                    uvs[i] = reader.ReadSingle(); SourceFireGuard.Finite(uvs[i]);
                    SourceFireGuard.Require(uvs[i] >= 0f && uvs[i] <= 1f, "PHELM normalized authoring UV out of bounds.");
                }
                for (int i = 0; i < vertices.Length; i++)
                { vertices[i] = reader.ReadInt32(); SourceFireGuard.Require(vertices[i] >= 0 && vertices[i] < 63, "PHELM vertex index out of range."); }
                for (int i = 0; i < uvIndices.Length; i++)
                { uvIndices[i] = reader.ReadInt32(); SourceFireGuard.Require(uvIndices[i] >= 0 && uvIndices[i] < 105, "PHELM UV index out of range."); }
                return new SourceFireGeometry(positions, uvs, vertices, uvIndices);
            }
        }

        public static int AspectFromDecodedDimensions(int width, int height)
        {
            SourceFireGuard.Require(width > 0 && height > 0 && width <= 256 && height <= 256 &&
                (width & (width - 1)) == 0 && (height & (height - 1)) == 0, "Glide authoring aspect requires power-of-two dimensions <=256.");
            int w = width, h = height, aspect = 3;
            while (w > h) { w >>= 1; aspect--; }
            while (h > w) { h >>= 1; aspect++; }
            SourceFireGuard.Require(w == h && aspect >= 0 && aspect <= 6, "Authoring aspect outside pinned Glide enum 0..6.");
            return aspect;
        }

        public static SourceFireTextureFrame DecodeOriginalTga(byte[] bytes, SourceFireTextureRecord record,
            string label, string sha256, int inputIndex)
        {
            if (record != null && record.nonPixelDataOrigin == DustMetadataOrigin)
            {
                SourceFireGuard.Require(bytes != null && bytes.Length == 4458 && label.StartsWith("img3d/dirt", StringComparison.Ordinal),
                    "Original DIRT requires its authenticated TGA v2 developer-area adapter, not an arbitrary trailing-data bypass.");
                var metadata = new byte[344];
                Buffer.BlockCopy(bytes, 4114, metadata, 0, metadata.Length);
                CheckHash(metadata, "4de6fb5218a64c3a2ee6c5fd52d32bbf3359295ed4033501c3b9044e89bc89a5", "original DIRT nonpixel developer directory/tag20/footer");
                var pixels = new byte[4114];
                Buffer.BlockCopy(bytes, 0, pixels, 0, pixels.Length);
                bytes = pixels;
            }
            TgaImageInfo info = TgaImageInfo.Read(bytes, label);
            SourceFireGuard.Require(info.Width == 32 && info.Height == 32 && info.BitsPerPixel == 32 && (bytes[17] & 15) == 8 &&
                !info.TopOrigin && !info.RightOrigin && bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0 && bytes[11] == 0,
                "Original TRAIL/DIRT requires32x32 BGRA8/alpha8, zero offsets, bottom-left origin.");
            int aspect = AspectFromDecodedDimensions(info.Width, info.Height);
            SourceFireGuard.Require(record != null && record.inputIndex == inputIndex && record.width == info.Width &&
                record.height == info.Height && record.bitsPerPixel == info.BitsPerPixel && record.alphaBits == 8 &&
                record.topOrigin == info.TopOrigin && record.rightOrigin == info.RightOrigin &&
                record.integerAspect == aspect && record.aspectOrigin == AspectOrigin, "Texture metadata/aspect origin disagrees with decoded original TGA: " + label);
            var rgba = new byte[info.Width * info.Height * 4];
            for (int p = 0; p < rgba.Length; p += 4)
            {
                rgba[p] = bytes[info.PixelOffset + p + 2]; rgba[p + 1] = bytes[info.PixelOffset + p + 1];
                rgba[p + 2] = bytes[info.PixelOffset + p]; rgba[p + 3] = bytes[info.PixelOffset + p + 3];
            }
            return new SourceFireTextureFrame(info.Width, info.Height, aspect, label, sha256, rgba);
        }

        public static SourceFireGeometry ReadGeometry(byte[] bytes)
        {
            SourceFireGuard.Require(bytes != null && bytes.Length == 148, "Exact portable SDL geometry length required.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                SourceFireGuard.Require(Encoding.ASCII.GetString(reader.ReadBytes(8)) == "BZFXG001" &&
                    reader.ReadInt32() == 4 && reader.ReadInt32() == 4 && reader.ReadInt32() == 2, "Wrong SDL geometry format/counts.");
                var positions = new SourceVector3[4];
                var uvs = new float[8];
                var vertices = new int[6];
                var uvIndices = new int[6];
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                for (int i = 0; i < 4; i++)
                {
                    positions[i] = new SourceVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    SourceFireGuard.Finite(positions[i]);
                    SourceFireGuard.Require(positions[i].Z == 0 && Math.Abs(positions[i].X) == 4f && Math.Abs(positions[i].Y) == 4f,
                        "Portable SDL geometry must retain verified CVTMOD factor4 source units, never a normalized unit quad.");
                    minX = Math.Min(minX, positions[i].X); maxX = Math.Max(maxX, positions[i].X);
                    minY = Math.Min(minY, positions[i].Y); maxY = Math.Max(maxY, positions[i].Y);
                }
                SourceFireGuard.Require(minX == -4f && maxX == 4f && minY == -4f && maxY == 4f, "Incomplete original scaled SDL bounds.");
                for (int i = 0; i < 8; i++)
                {
                    uvs[i] = reader.ReadSingle(); SourceFireGuard.Finite(uvs[i]);
                    SourceFireGuard.Require(uvs[i] >= 0 && uvs[i] <= 1, "Original SDL UV out of bounds.");
                }
                for (int i = 0; i < 6; i++) { vertices[i] = reader.ReadInt32(); SourceFireGuard.Require(vertices[i] >= 0 && vertices[i] < 4, "SDL vertex index out of range."); }
                for (int i = 0; i < 6; i++) { uvIndices[i] = reader.ReadInt32(); SourceFireGuard.Require(uvIndices[i] >= 0 && uvIndices[i] < 4, "SDL UV index out of range."); }
                for (int i = 0; i < 6; i += 3)
                {
                    SourceVector3 a = positions[vertices[i]], b = positions[vertices[i + 1]], c = positions[vertices[i + 2]];
                    SourceFireGuard.Require(Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) == 64f,
                        "Degenerate/changed original SDL triangle area.");
                }
                return new SourceFireGeometry(positions, uvs, vertices, uvIndices);
            }
        }

        private static void CheckHash(byte[] bytes, string expected, string label)
        {
            SourceFireGuard.Require(bytes != null && expected != null && expected.Length == 64 &&
                AuthorizedAssetRoot.Sha256(bytes) == expected, "Wrong SHA256: " + label);
        }
        private static string GitBlob(byte[] bytes)
        {
            byte[] header = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            var input = new byte[header.Length + bytes.Length]; Buffer.BlockCopy(header, 0, input, 0, header.Length);
            Buffer.BlockCopy(bytes, 0, input, header.Length, bytes.Length);
            using (SHA1 sha = SHA1.Create())
            { var result = new StringBuilder(40); foreach (byte value in sha.ComputeHash(input)) result.Append(value.ToString("x2")); return result.ToString(); }
        }
    }
}
