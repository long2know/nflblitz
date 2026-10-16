using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.AssetImport
{
    public sealed class SourceTeamTexture
    {
        public string Id { get; }
        public string SourcePath { get; }
        public string SourceGitBlobSha1 { get; }
        public string Representation { get; }
        public string Path { get; }
        public string Sha256 { get; }
        public int ByteLength { get; }
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyCollection<string> InputPaths { get; }

        internal SourceTeamTexture(string id, string sourcePath, string blob, string representation,
            string path, string sha256, int bytes, int width, int height, string[] inputPaths)
        {
            Id = id; SourcePath = sourcePath; SourceGitBlobSha1 = blob; Representation = representation;
            Path = path; Sha256 = sha256; ByteLength = bytes; Width = width; Height = height;
            InputPaths = Array.AsReadOnly(inputPaths);
        }
    }

    public sealed class SourceTeamPresentationData
    {
        public const string PinnedRosterSha256 = "f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b";
        public string ContentSha256 { get; }
        public string AuthorizationReference { get; }
        public ReadOnlyCollection<SourceTeamTexture> Textures { get; }
        public ReadOnlyDictionary<string, SourceRosterInputPin> Inputs { get; }
        public ReadOnlyCollection<string> MissingTextureIds { get; }
        public ReadOnlyCollection<string> Qualifications { get; }

        private SourceTeamPresentationData(string hash, string authorization, SourceTeamTexture[] textures,
            Dictionary<string, SourceRosterInputPin> inputs, string[] missing, string[] qualifications)
        {
            ContentSha256 = hash; AuthorizationReference = authorization;
            Textures = Array.AsReadOnly(textures); Inputs = new ReadOnlyDictionary<string, SourceRosterInputPin>(inputs);
            MissingTextureIds = Array.AsReadOnly(missing); Qualifications = Array.AsReadOnly(qualifications);
        }

        public static SourceTeamPresentationData Read(byte[] json, string expectedSha256, SourceTeamRosterLibrary roster)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            RosterGuard.Hex(expectedSha256, 64, "expected presentation SHA256");
            RosterGuard.Require(json != null && json.Length >= 2 && json.Length <= 1024 * 1024,
                "Team presentation catalog must be 2 bytes..1 MiB.");
            RosterGuard.Require(RosterGuard.Sha256(json) == expectedSha256, "Team presentation catalog content pin mismatch.");
            RosterGuard.Require(roster.ContentSha256 == PinnedRosterSha256, "Team presentation requires the final original roster dataset.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(json), "team presentation");
            RosterGuard.Require(root.Text("schema") == "original-team-presentation/v1" &&
                root.Text("source_revision") == roster.SourceRevision &&
                root.Text("source_tree") == SourceTeamRosterLibrary.PinnedSourceTree &&
                root.Text("roster_sha256") == roster.ContentSha256, "Team presentation source/roster pin mismatch.");
            string authorization = root.Text("authorization_reference", 128);
            foreach (char c in authorization)
                RosterGuard.Require(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' ||
                    c >= '0' && c <= '9' || c == '-' || c == '_', "Invalid local authorization identifier.");
            object[] rawInputs = root.Array("inputs");
            RosterGuard.Require(rawInputs.Length >= 4 && rawInputs.Length <= 512, "Invalid team source input count.");
            var inputs = new Dictionary<string, SourceRosterInputPin>(StringComparer.Ordinal);
            foreach (object raw in rawInputs)
            {
                RosterObject input = RosterJson.Object(raw, "team input");
                string path = input.Path("path");
                int bytes = input.Integer("bytes", 1, PlayerImportLimits.SourceBytes);
                string blob = input.Text("git_blob_sha1", 40), hash = input.Text("sha256", 64);
                RosterGuard.Hex(blob, 40, "team source Git blob"); RosterGuard.Hex(hash, 64, "team source SHA256");
                input.Done();
                RosterGuard.Require(!inputs.ContainsKey(path), "Duplicate team source input: " + path);
                inputs.Add(path, new SourceRosterInputPin(path, bytes, blob, hash));
            }
            object[] rawTextures = root.Array("textures");
            RosterGuard.Require(rawTextures.Length >= 1 && rawTextures.Length <= 256, "Invalid team texture count.");
            var textures = new SourceTeamTexture[rawTextures.Length];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var outputPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < textures.Length; i++)
            {
                RosterObject texture = RosterJson.Object(rawTextures[i], "team texture");
                string id = texture.Text("id"), path = texture.Path("path"), source = texture.Path("source_path");
                string blob = texture.Text("source_git_blob_sha1", 40), hash = texture.Text("sha256", 64);
                string representation = texture.Text("representation");
                string[] inputPaths = texture.Strings("input_paths", 1, 4);
                foreach (string originalPath in inputPaths) RosterGuard.Path(originalPath);
                int bytes = texture.Integer("bytes", 18, PlayerImportLimits.TextureBytes);
                int width = texture.Integer("width", 1, 256), height = texture.Integer("height", 1, 256);
                RosterGuard.Hex(blob, 40, "team texture Git blob"); RosterGuard.Hex(hash, 64, "team texture SHA256");
                texture.Done();
                SourceRosterAsset asset;
                RosterGuard.Require(roster.Assets.TryGetValue(id, out asset) && asset.Kind == "texture" &&
                    ids.Add(id) && outputPaths.Add(path), "Unknown/duplicate original team texture: " + id);
                SourceRosterInputPin input;
                RosterGuard.Require(inputs.TryGetValue(source, out input) && input.GitBlobSha1 == blob,
                    "Original texture has no matching source input pin: " + id);
                if (representation == "original-runtime-argb1555-level0")
                    RosterGuard.Require(asset.SourcePath == source && asset.GitBlobSha1 == blob &&
                        source.EndsWith(".wms", StringComparison.OrdinalIgnoreCase),
                        "A runtime atlas cannot be aliased to another team's source: " + id);
                else if (representation == "original-c-hack-authoring-atlas")
                {
                    RosterGuard.Require(!asset.AvailableInPinnedTree && source == "img3d/plyr.ld" &&
                        asset.RuntimeFilename.EndsWith("LOD.WMS", StringComparison.Ordinal) &&
                        inputPaths.Length == 4 && width == 128 && height == 256 &&
                        inputs.ContainsKey("img3d/uni.bat"),
                        "A missing LOD atlas requires its original c_hack recipe, four authored pieces and active conversion branch.");
                    string prefix = asset.RuntimeFilename.Substring(0, 3);
                    var pieces = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string originalPath in inputPaths)
                        RosterGuard.Require(originalPath.StartsWith("img3d/", StringComparison.Ordinal) &&
                            System.IO.Path.GetFileName(originalPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            originalPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
                            inputs.ContainsKey(originalPath) && pieces.Add(originalPath),
                            "Original packed atlas cannot borrow another team's authored pieces: " + id);
                }
                else
                {
                    RosterGuard.Require(representation == "byte-identical-original-authoring-tga" &&
                        !asset.AvailableInPinnedTree && source.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
                        System.IO.Path.GetFileNameWithoutExtension(source).Equals(
                            System.IO.Path.GetFileNameWithoutExtension(asset.RuntimeFilename), StringComparison.OrdinalIgnoreCase) &&
                        input.Sha256 == hash && input.ByteLength == bytes,
                        "A missing runtime texture requires its byte-identical, exact-name original authoring TGA: " + id);
                }
                if (representation != "original-c-hack-authoring-atlas")
                    RosterGuard.Require(inputPaths.Length == 1 && inputPaths[0] == source,
                        "An original texture must retain its precise input identity: " + id);
                textures[i] = new SourceTeamTexture(id, source, blob, representation, path, hash, bytes, width, height, inputPaths);
            }
            string[] missing = root.Strings("missing_texture_ids", 0, 256);
            foreach (string id in missing)
            {
                SourceRosterAsset asset;
                RosterGuard.Require(roster.Assets.TryGetValue(id, out asset) && asset.Kind == "texture" &&
                    !asset.AvailableInPinnedTree && ids.Add(id), "Invalid/duplicate missing texture identity: " + id);
            }
            foreach (SourceRosterAsset asset in roster.Assets.Values)
                if (asset.Kind == "texture") RosterGuard.Require(ids.Contains(asset.Id),
                    "Team texture catalog silently omitted an original identity: " + asset.Id);
            string[] qualifications = root.Strings("qualifications", 1, 32);
            root.Done();
            return new SourceTeamPresentationData(expectedSha256, authorization, textures, inputs, missing, qualifications);
        }

        public void VerifyInput(string relativePath, byte[] data)
        {
            SourceRosterInputPin input;
            RosterGuard.Require(Inputs.TryGetValue(relativePath, out input) && data != null &&
                data.Length == input.ByteLength && RosterGuard.Sha256(data) == input.Sha256,
                "Original team header/input content mismatch: " + relativePath);
        }

        public void VerifyTexture(SourceTeamTexture texture, byte[] data)
        {
            RosterGuard.Require(texture != null && Textures.Contains(texture) && data != null &&
                data.Length == texture.ByteLength && RosterGuard.Sha256(data) == texture.Sha256,
                "Converted original team texture content mismatch.");
            TgaImageInfo tga = TgaImageInfo.Read(data, texture.Path);
            RosterGuard.Require(tga.Width == texture.Width && tga.Height == texture.Height,
                "Converted original team texture dimensions changed.");
        }
    }
}
