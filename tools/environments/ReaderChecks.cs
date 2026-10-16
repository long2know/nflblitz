using System;
using System.IO;
using System.Text;
using Blitz.AssetImport;

internal static class ReaderChecks
{
    private static int assertions;

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new Exception("Assertion failed: " + identity);
    }

    private static void Rejected(Action action, string identity)
    {
        try { action(); }
        catch (SourceEnvironmentException error)
        {
            Check(error.Message.Contains(identity), "exact rejection identity: " + identity + " / " + error.Message);
            return;
        }
        throw new Exception("Invalid converted environment was accepted: " + identity);
    }

    private static SourceEnvironmentMeshInfo ToyInfo(byte[] bytes)
    {
        return new SourceEnvironmentMeshInfo
        {
            meshPath = "synthetic/toy.meshbin", meshSha256 = AuthorizedAssetRoot.Sha256(bytes), meshBytes = bytes.Length,
            vertexCount = 3, triangleCount = 1, submeshCount = 1,
            materialSlots = new[] { 0 },
            boundsUnity = new[] { 0f, 0f, -3f, 2f, 0f, 0f }
        };
    }

    private static byte[] ToyMesh()
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("BZENM001"));
                writer.Write(3); writer.Write(1);
                foreach (float value in new[] { 0f, 0f, 0f, 0f, 0f, 2f, 0f, 0f, 1f, 0f, 0f, 0f, -3f, 0f, 1f })
                    writer.Write(value);
                writer.Write(0); writer.Write(3); writer.Write(0); writer.Write(1); writer.Write(2);
            }
            return stream.ToArray();
        }
    }

    private static void Synthetic()
    {
        byte[] bytes = ToyMesh();
        SourceEnvironmentMeshData mesh = SourceEnvironmentReader.ReadMesh(bytes, ToyInfo(bytes), 1);
        Check(mesh.Positions[3] == 2 && mesh.Positions[8] == -3 && mesh.Uvs[2] == 1 &&
            mesh.MaterialSlots[0] == 0 && mesh.Indices[0][2] == 2, "actual pure mesh positions/UV/winding/slot");
        byte[] corrupt = (byte[])bytes.Clone();
        corrupt[0] = 0;
        Rejected(() => SourceEnvironmentReader.ReadMesh(corrupt, ToyInfo(corrupt), 1), "magic");
        corrupt = (byte[])bytes.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes(float.NaN), 0, corrupt, 16, 4);
        Rejected(() => SourceEnvironmentReader.ReadMesh(corrupt, ToyInfo(corrupt), 1), "coordinate");
        corrupt = (byte[])bytes.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes(99), 0, corrupt, corrupt.Length - 4, 4);
        Rejected(() => SourceEnvironmentReader.ReadMesh(corrupt, ToyInfo(corrupt), 1), "index out of range");
        corrupt = (byte[])bytes.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes(float.PositiveInfinity), 0, corrupt, 28, 4);
        Rejected(() => SourceEnvironmentReader.ReadMesh(corrupt, ToyInfo(corrupt), 1), "UV");
        var trailing = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, trailing, 0, bytes.Length);
        Rejected(() => SourceEnvironmentReader.ReadMesh(trailing, ToyInfo(trailing), 1), "trailing bytes");
        SourceEnvironmentMeshInfo info = ToyInfo(bytes);
        info.boundsUnity[2] = -4;
        Rejected(() => SourceEnvironmentReader.ReadMesh(bytes, info, 1), "bounds");
        info = ToyInfo(bytes);
        info.vertexCount = 4;
        Rejected(() => SourceEnvironmentReader.ReadMesh(bytes, info, 1), "header/count");
        corrupt = (byte[])bytes.Clone();
        corrupt[16] = 1;
        Rejected(() => SourceEnvironmentReader.ReadMesh(corrupt, ToyInfo(bytes), 1), "SHA256");
        var pixels = new byte[] { 255, 0, 0, 255, 0, 255, 0, 0, 0, 0, 255, 127, 255, 255, 255, 255 };
        var texture = new SourceEnvironmentTextureInfo
        {
            sourcePath = "synthetic/toy.tga", sourceFormat = "TGA32", sourceGitBlobSha1 = new string('0', 40),
            sourceSha256 = new string('0', 64), rgbaPath = "synthetic/toy.rgba",
            samplingWrap = "GR_TEXTURECLAMP_WRAP",
            rgbaSha256 = AuthorizedAssetRoot.Sha256(pixels), rgbaBytes = pixels.Length,
            width = 2, height = 2, mipCount = 1, filterType = 1, hasAlpha = true
        };
        Check(SourceEnvironmentReader.ReadTexture(pixels, texture) == pixels, "exact RGBA bytes retained");
        texture.samplingWrap = "GR_TEXTURECLAMP_CLAMP";
        Rejected(() => SourceEnvironmentReader.ReadTexture(pixels, texture), "must restore WRAP");
        texture.samplingWrap = "GR_TEXTURECLAMP_WRAP";
        texture.sourceFormat = "WMS/unsupported";
        Rejected(() => SourceEnvironmentReader.ReadTexture(pixels, texture), "format");
        texture.sourceFormat = "TGA32"; texture.mipCount = 2;
        Rejected(() => SourceEnvironmentReader.ReadTexture(pixels, texture), "mip byte count");
        texture.mipCount = 1; texture.hasAlpha = false;
        Rejected(() => SourceEnvironmentReader.ReadTexture(pixels, texture), "alpha metadata");
    }

    private static void ManifestRejected(byte[] original, string before, string after, string identity)
    {
        string text = Encoding.UTF8.GetString(original);
        Check(text.Contains(before), "manifest mutation target exists: " + before);
        byte[] changed = Encoding.UTF8.GetBytes(text.Replace(before, after));
        Rejected(() => SourceEnvironmentReader.ReadManifest(changed, AuthorizedAssetRoot.Sha256(changed)), identity);
    }

    public static int Main(string[] args)
    {
        try
        {
            bool syntheticOnly = args.Length == 1 && args[0] == "--synthetic-only";
            if (!syntheticOnly && (args.Length != 2 || args[0].StartsWith("--", StringComparison.Ordinal)))
                throw new ArgumentException("Select --synthetic-only or an explicit absolute bundle root and manifest SHA256; no automatic fallback.");
            Synthetic();
            if (syntheticOnly)
            {
                Console.WriteLine("PASS: " + assertions + " synthetic-only source environment pure C# assertions; " +
                    "no original assets read; not original-geometry, native or browser proof.");
                return 0;
            }
            var root = new AuthorizedAssetRoot(args[0]);
            byte[] manifestBytes = root.ReadBytes("environment.json", 1024 * 1024);
            SourceEnvironmentBundle bundle = SourceEnvironmentReader.Read(manifestBytes, args[1],
                path => root.ReadBytes(path, 32 * 1024 * 1024));
            Check(bundle.Meshes.Length == 99 && bundle.Manifest.draws.Length == 78 &&
                bundle.TextureRgba.Length == 24 && bundle.Manifest.zoneVolumes.Length == 18, "actual original inventory");
            int indexCount = 0, vertexCount = 0, rgbaBytes = 0;
            for (int i = 0; i < bundle.Meshes.Length; i++)
            {
                SourceEnvironmentMeshData mesh = bundle.Meshes[i];
                Check(mesh.Positions.Length / 3 == bundle.Manifest.meshes[i].vertexCount &&
                    mesh.Uvs.Length / 2 == bundle.Manifest.meshes[i].vertexCount, "actual split position/UV counts " + i);
                foreach (int[] indices in mesh.Indices) indexCount += indices.Length;
                vertexCount += mesh.Positions.Length / 3;
            }
            foreach (byte[] pixels in bundle.TextureRgba) rgbaBytes += pixels.Length;
            Check(indexCount == 2517 * 3, "all 2517 original triangles decoded");
            ManifestRejected(manifestBytes, SourceEnvironmentReader.SourceCommit, new string('0', 40), "revision identity");
            ManifestRejected(manifestBytes, "32c1a1da778dc6be46f3c516e9fabd3804e657ae", new string('0', 40), "models/field2.sdl");
            ManifestRejected(manifestBytes, "GR_TEXTURECLAMP_CLAMP", "GR_TEXTURECLAMP_WRAP", "draw state");
            ManifestRejected(manifestBytes, "\"archiveUnitsPerYard\": 7.8", "\"archiveUnitsPerYard\": 1.0", "origin/axes/scale/winding");
            ManifestRejected(manifestBytes, "\"role\": \"zone-volume\"", "\"role\": \"render\"", "visibility-volume");
            ManifestRejected(manifestBytes, "\"renderQueue\": 1800", "\"renderQueue\": 3000", "ordering");
            Console.WriteLine("PASS: " + assertions + " source environment pure C# assertions; actual " +
                bundle.Meshes.Length + " meshes/" + vertexCount + " split vertices/" + indexCount +
                " indices/" + rgbaBytes + " exact original RGBA bytes.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
