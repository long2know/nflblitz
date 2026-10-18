using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Blitz.Presentation
{
    public sealed class SourceFreeBallAsset : ScriptableObject
    {
        public const string ResourceKey = "BlitzSourceFreeBall/original-medball2";
        [SerializeField] private TextAsset manifest;
        [SerializeField] private TextAsset originalSdl;
        [SerializeField] private TextAsset sourceHeader;
        [SerializeField] private string manifestSha256;
        [SerializeField] private Mesh mesh;
        [SerializeField] private SourceTeamPresentationAsset teamAssets;
        [SerializeField] private Shader sourceShader;
        private SourceFreeBallData data;
        public Mesh Mesh { get { Load(); return mesh; } }
        public SourceFreeBallData Data { get { Load(); return data; } }

        public void InitializeGenerated(TextAsset provenance, string hash, TextAsset sdl, TextAsset header,
            Mesh geometry, SourceTeamPresentationAsset actualTeams, Shader shader)
        {
            if (data != null) throw new InvalidOperationException("The original free-ball asset is immutable after validation.");
            manifest = provenance; manifestSha256 = hash; originalSdl = sdl; sourceHeader = header;
            mesh = geometry; teamAssets = actualTeams; sourceShader = shader;
            Load();
        }

        public void Load()
        {
            if (data != null) return;
            if (manifest == null || originalSdl == null || sourceHeader == null || mesh == null || teamAssets == null ||
                sourceShader == null || sourceShader.name != "Blitz/SourceFreeBallPortable")
                throw new InvalidOperationException("Missing required original free-ball Resources/" + ResourceKey +
                    ": import the calibrated medball2/HEDSHU bundle; no hand extraction, sphere or tint fallback.");
            SourceFreeBallData parsed = SourceFreeBallData.Read(manifest.bytes, manifestSha256, path =>
            {
                if (path == "models/medball2.sdl") return originalSdl.bytes;
                if (path == "medball2.source-authored.h") return sourceHeader.bytes;
                throw new InvalidOperationException("Unlisted original free-ball input: " + path);
            });
            teamAssets.Load();
            SourceFreeBallData.RequireTextures(teamAssets.Catalog);
            BindPoseLimbMesh expected = parsed.BuildSourceMesh();
            Vector3[] positions = mesh.vertices;
            Vector2[] uv = mesh.uv;
            if (mesh.name != SourceFreeBallData.LimbSymbol || mesh.subMeshCount != 1 ||
                positions.Length != expected.Positions.Count || uv.Length != expected.Uvs.Count)
                throw new InvalidOperationException("Imported free-ball mesh/pivot inventory differs from original medball2.");
            for (int i = 0; i < positions.Length; i++)
                if (positions[i].x != expected.Positions[i].X || positions[i].y != expected.Positions[i].Y ||
                    positions[i].z != expected.Positions[i].Z || uv[i].x != expected.Uvs[i].X || uv[i].y != expected.Uvs[i].Y)
                    throw new InvalidOperationException("Imported free-ball geometry/UV/pivot is not the unchanged authoring origin.");
            int[] triangles = mesh.GetTriangles(0);
            if (triangles.Length != expected.SubmeshTriangles[0].Count)
                throw new InvalidOperationException("Imported free-ball triangle inventory changed.");
            for (int i = 0; i < triangles.Length; i++)
                if (triangles[i] != expected.SubmeshTriangles[0][i])
                    throw new InvalidOperationException("Imported free-ball source321 winding/topology changed.");
            data = parsed;
        }

        public Material CreateMaterial(bool alternate)
        {
            Load();
            if (!sourceShader.isSupported)
                throw new InvalidOperationException("Original free-ball shader unsupported; no replacement shader is permitted.");
            string id = alternate ? SourceFreeBallData.AlternateTextureId : SourceFreeBallData.TextureId;
            foreach (SourceTeamMaterialAsset material in teamAssets.MaterialInventory)
                if (material.SourceTextureId == id)
                    return new Material(sourceShader) { name = "Source free-ball " + id, mainTexture = material.Material.mainTexture };
            throw new InvalidOperationException("Missing actual free-ball decal " + id);
        }

#if UNITY_EDITOR
        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            var input = new AuthorizedAssetRoot(Argument(args, "-freeBallRoot"));
            string hash = Argument(args, "-freeBallManifestSha256");
            string destination = Argument(args, "-freeBallOutput");
            string teamPath = Argument(args, "-freeBallTeamAsset");
            AuthorizedAssetRoot.ValidateRelativePath(destination);
            AuthorizedAssetRoot.ValidateRelativePath(teamPath);
            if (!destination.StartsWith("Assets/Imported/", StringComparison.Ordinal) ||
                !destination.EndsWith("/Resources/BlitzSourceFreeBall", StringComparison.Ordinal) ||
                !teamPath.StartsWith("Assets/Imported/", StringComparison.Ordinal))
                throw new PlayerAssetException("Use a new imported Resources/BlitzSourceFreeBall folder and actual imported team asset.");
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            output.NewPath(destination);
            SourceTeamPresentationAsset teams = AssetDatabase.LoadAssetAtPath<SourceTeamPresentationAsset>(teamPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Scripts/Presentation/SourceFreeBall.shader");
            if (teams == null || shader == null)
                throw new PlayerAssetException("Actual original team presentation and authored free-ball shader required.");
            byte[] provenance = input.ReadBytes("free-ball.json", 65536);
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            SourceFreeBallData parsed = SourceFreeBallData.Read(provenance, hash, path =>
            { byte[] bytes = input.ReadBytes(path, 65536); payloads.Add(path, bytes); return bytes; });
            SourceFreeBallData.RequireTextures(teams.Catalog);
            BindPoseLimbMesh source = parsed.BuildSourceMesh();
            var positions = new Vector3[source.Positions.Count];
            var uv = new Vector2[source.Uvs.Count];
            for (int i = 0; i < positions.Length; i++)
            {
                AssetVector3 p = source.Positions[i]; AssetVector2 st = source.Uvs[i];
                positions[i] = new Vector3(p.X, p.Y, p.Z); uv[i] = new Vector2(st.X, st.Y);
            }
            var triangles = new int[source.SubmeshTriangles[0].Count];
            source.SubmeshTriangles[0].CopyTo(triangles, 0);
            EnsureFolder(destination);
            var geometry = new Mesh { name = SourceFreeBallData.LimbSymbol, vertices = positions, uv = uv, subMeshCount = 1 };
            geometry.SetTriangles(triangles, 0, false); geometry.RecalculateBounds();
            string meshPath = destination + "/medball2-source-authored.asset";
            output.NewPath(meshPath); AssetDatabase.CreateAsset(geometry, meshPath);
            TextAsset manifestAsset = SaveText(output, destination + "/free-ball.json", provenance);
            TextAsset sdl = SaveText(output, destination + "/medball2.sdl.bytes", payloads["models/medball2.sdl"]);
            TextAsset header = SaveText(output, destination + "/medball2.source-authored.h.bytes", payloads["medball2.source-authored.h"]);
            var asset = CreateInstance<SourceFreeBallAsset>();
            asset.InitializeGenerated(manifestAsset, hash, sdl, header, geometry, teams, shader);
            string assetPath = destination + "/original-medball2.asset";
            output.NewPath(assetPath); AssetDatabase.CreateAsset(asset, assetPath); AssetDatabase.SaveAssets();
            Debug.Log("PASS source-authored original medball2/HEDSHU free-ball import; native medball2.h/WMS/pixels unqualified.");
        }

        private static string Argument(string[] args, string name)
        {
            string value = null;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == name)
                {
                    if (value != null || i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        throw new PlayerAssetException("Specify exactly one " + name);
                    value = args[++i];
                }
            if (string.IsNullOrWhiteSpace(value)) throw new PlayerAssetException("Required original free-ball argument: " + name);
            return value;
        }

        private static void EnsureFolder(string path)
        {
            string parent = path.Substring(0, path.LastIndexOf('/')), name = path.Substring(path.LastIndexOf('/') + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
                throw new PlayerAssetException("Could not create fresh free-ball Resources folder.");
        }

        private static TextAsset SaveText(AuthorizedAssetRoot root, string path, byte[] bytes)
        {
            using (var stream = new FileStream(root.NewPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || AuthorizedAssetRoot.Sha256(asset.bytes) != AuthorizedAssetRoot.Sha256(bytes))
                throw new PlayerAssetException("Original free-ball text import changed content: " + path);
            return asset;
        }
#endif
    }
}
