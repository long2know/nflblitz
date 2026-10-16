using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class AssetInputFingerprint
    {
        public readonly string Role;
        public readonly string RelativePath;
        public readonly string Sha256;
        public readonly int ByteLength;

        internal AssetInputFingerprint(string role, string relativePath, byte[] bytes)
        {
            Role = role; RelativePath = relativePath; Sha256 = AuthorizedAssetRoot.Sha256(bytes); ByteLength = bytes.Length;
        }
    }

    public sealed class AuthorizedPlayerTexture
    {
        public readonly PlayerMaterialBinding Binding;
        public readonly AssetInputFingerprint Fingerprint;
        public readonly TgaImageInfo Info;
        private readonly byte[] bytes;

        internal AuthorizedPlayerTexture(PlayerMaterialBinding binding, byte[] data)
        {
            Binding = binding; bytes = data;
            Info = TgaImageInfo.Read(data, binding.TexturePath);
            Fingerprint = new AssetInputFingerprint("texture:" + binding.Slot, binding.TexturePath, data);
        }
        public byte[] CopyBytes() { return (byte[])bytes.Clone(); }
    }

    public sealed class PlayerImportPlan
    {
        public const string Stage = "bind-pose-only-v1";
        public readonly string StableId;
        public readonly string AuthorizationReference;
        public readonly SourcePlayer Player;
        public readonly PlayerCoordinateConversion Conversion;
        public readonly ReadOnlyCollection<AssetInputFingerprint> Sources;
        public readonly ReadOnlyCollection<AuthorizedPlayerTexture> Textures;
        public readonly ReadOnlyCollection<BindPoseLimbMesh> Meshes;

        private PlayerImportPlan(string stableId, string authorizationReference, SourcePlayer player,
            PlayerCoordinateConversion conversion, AssetInputFingerprint[] sources,
            AuthorizedPlayerTexture[] textures, ReadOnlyCollection<BindPoseLimbMesh> meshes)
        {
            StableId = stableId; AuthorizationReference = authorizationReference; Player = player; Conversion = conversion;
            Sources = Array.AsReadOnly(sources); Textures = Array.AsReadOnly(textures); Meshes = meshes;
        }

        public static PlayerImportPlan Create(string authorizedRoot, bool reuseAuthorizationConfirmed,
            string authorizationReference, string stableId, string modelPath, string skeletonPath,
            PlayerSymbolSelection symbols, IList<PlayerMaterialBinding> materialMap, PlayerCoordinateConversion conversion)
        {
            if (!reuseAuthorizationConfirmed)
                throw new PlayerAssetException("Asset reuse authorization is not confirmed. No source/art input was read and no output was created.");
            ValidateId(stableId, "Stable id", false);
            ValidateId(authorizationReference, "Non-secret authorization reference", true);
            if (conversion == null) throw new PlayerAssetException("Explicit coordinate axes and winding are required; source units are not guessed to be yards.");
            RequireExtension(modelPath, ".h"); RequireExtension(skeletonPath, ".h");
            var root = new AuthorizedAssetRoot(authorizedRoot);
            byte[] model = root.ReadBytes(modelPath, PlayerImportLimits.SourceBytes);
            byte[] skeleton = root.ReadBytes(skeletonPath, PlayerImportLimits.SourceBytes);
            SourcePlayer player = GeneratedPlayerReader.Read(AuthorizedAssetRoot.DecodeUtf8(model, modelPath), modelPath,
                AuthorizedAssetRoot.DecodeUtf8(skeleton, skeletonPath), skeletonPath, symbols);
            PlayerMaterialBinding[] ordered = BindPoseMeshBuilder.OrderBindings(player, materialMap);
            var textures = new AuthorizedPlayerTexture[ordered.Length];
            long totalBytes = (long)model.Length + skeleton.Length;
            for (int i = 0; i < ordered.Length; i++)
            {
                RequireExtension(ordered[i].TexturePath, ".tga");
                byte[] texture = root.ReadBytes(ordered[i].TexturePath, PlayerImportLimits.TextureBytes);
                totalBytes += texture.Length;
                if (totalBytes > PlayerImportLimits.TotalInputBytes) throw new PlayerAssetException("Combined input byte limit exceeded.");
                textures[i] = new AuthorizedPlayerTexture(ordered[i], texture);
            }
            ReadOnlyCollection<BindPoseLimbMesh> meshes = BindPoseMeshBuilder.Build(player, ordered, conversion);
            return new PlayerImportPlan(stableId, authorizationReference, player, conversion,
                new[] { new AssetInputFingerprint("model", modelPath, model), new AssetInputFingerprint("skeleton", skeletonPath, skeleton) },
                textures, meshes);
        }

        public static void ValidateStableId(string stableId) { ValidateId(stableId, "Stable id", false); }

        private static void ValidateId(string id, string label, bool reference)
        {
            if (string.IsNullOrEmpty(id) || id.Length > (reference ? 128 : 64))
                throw new PlayerAssetException(label + " must be a short portable identifier, not a machine path or secret.");
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool letter = c >= 'a' && c <= 'z' || reference && c >= 'A' && c <= 'Z';
                if (!letter && !(c >= '0' && c <= '9') && !(i > 0 && (c == '-' || reference && (c == '_' || c == '.'))))
                    throw new PlayerAssetException(label + " contains unsupported characters (lowercase letters/digits/hyphens for stable ids).");
            }
        }

        private static void RequireExtension(string path, string extension)
        {
            AuthorizedAssetRoot.ValidateRelativePath(path);
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new PlayerAssetException("Expected an explicit " + extension + " path, got '" + path + "'.");
        }

        public string ProvenanceJson()
        {
            var json = new StringBuilder();
            json.Append("{\n  \"schemaVersion\":1,\n  \"stage\":").Append(Quote(Stage));
            json.Append(",\n  \"stableId\":").Append(Quote(StableId));
            json.Append(",\n  \"authorizationReference\":").Append(Quote(AuthorizationReference));
            json.Append(",\n  \"units\":\"unchanged source model units; not gameplay yards\"");
            json.Append(",\n  \"unityUnitsPerSourceUnit\":1,\n  \"animation\":\"none; no WAN/ANI decoding or animation parity\"");
            json.Append(",\n  \"pivotConvention\":\"model-space bind pivots; local joints = child pivot - parent pivot; mesh vertices = source vertex - own pivot\"");
            json.Append(",\n  \"normalConvention\":\"source normals if present; otherwise area-weighted normals on UV/material-split vertices; not original lighting\"");
            json.Append(",\n  \"materialConvention\":\"explicit mapping only; no inferred team remapping; UV divisors and V flip supplied per material\"");
            json.Append(",\n  \"unityAxes\":[").Append(Quote(Conversion.UnityX.ToString())).Append(',')
                .Append(Quote(Conversion.UnityY.ToString())).Append(',').Append(Quote(Conversion.UnityZ.ToString())).Append(']');
            json.Append(",\n  \"coordinateDeterminant\":").Append(Conversion.Determinant);
            json.Append(",\n  \"sourceTriangleOrder\":").Append(Quote(Conversion.SourceOrder.ToString()));
            json.Append(",\n  \"reverseDeclaredTriangle\":").Append(Conversion.ReverseDeclaredTriangle ? "true" : "false");
            json.Append(",\n  \"symbols\":{\"modelLimbs\":").Append(Quote(Player.Symbols.ModelLimbs))
                .Append(",\"skeleton\":").Append(Quote(Player.Symbols.Skeleton))
                .Append(",\"skeletonLimbs\":").Append(Quote(Player.Symbols.SkeletonLimbs))
                .Append(",\"materials\":").Append(Quote(Player.Symbols.Materials)).Append("},\n  \"inputs\":[");
            bool first = true;
            foreach (AssetInputFingerprint source in Sources) AppendFingerprint(json, source, ref first);
            foreach (AuthorizedPlayerTexture texture in Textures) AppendFingerprint(json, texture.Fingerprint, ref first);
            json.Append("],\n  \"materials\":[");
            for (int i = 0; i < Textures.Count; i++)
            {
                AuthorizedPlayerTexture texture = Textures[i];
                if (i > 0) json.Append(',');
                json.Append("{\"slot\":").Append(Quote(texture.Binding.Slot)).Append(",\"texturePath\":").Append(Quote(texture.Binding.TexturePath))
                    .Append(",\"sourceSUnits\":").Append(texture.Binding.SourceSUnits.ToString("R", CultureInfo.InvariantCulture))
                    .Append(",\"sourceTUnits\":").Append(texture.Binding.SourceTUnits.ToString("R", CultureInfo.InvariantCulture))
                    .Append(",\"flipV\":").Append(texture.Binding.FlipV ? "true" : "false")
                    .Append(",\"alphaCutoff\":").Append(texture.Binding.AlphaCutoff.ToString("R", CultureInfo.InvariantCulture))
                    .Append(",\"width\":").Append(texture.Info.Width).Append(",\"height\":").Append(texture.Info.Height)
                    .Append(",\"bitsPerPixel\":").Append(texture.Info.BitsPerPixel)
                    .Append(",\"topOrigin\":").Append(texture.Info.TopOrigin ? "true" : "false")
                    .Append(",\"rightOrigin\":").Append(texture.Info.RightOrigin ? "true" : "false").Append('}');
            }
            json.Append("],\n  \"nodes\":[");
            for (int i = 0; i < Player.Nodes.Count; i++)
            {
                if (i > 0) json.Append(',');
                SourceRigNode node = Player.Nodes[i];
                json.Append("{\"index\":").Append(i).Append(",\"parent\":").Append(node.ParentIndex)
                    .Append(",\"limb\":").Append(Quote(Player.Limbs[i].Symbol)).Append(",\"flags\":").Append(node.Flags)
                    .Append(",\"sourceBindPivot\":");
                AppendVector(json, node.BindPivot);
                json.Append(",\"convertedLocalRestOffset\":"); AppendVector(json, Meshes[i].LocalRestOffset);
                json.Append(",\"sourceVertexCount\":").Append(Player.Limbs[i].Vertices.Count)
                    .Append(",\"convertedVertexCount\":").Append(Meshes[i].Positions.Count)
                    .Append(",\"triangleCount\":").Append(Player.Limbs[i].Faces.Count)
                    .Append(",\"recalculatedNormals\":").Append(Meshes[i].RecalculatedNormals ? "true" : "false").Append('}');
            }
            return json.Append("]\n}\n").ToString();
        }

        private static void AppendFingerprint(StringBuilder json, AssetInputFingerprint input, ref bool first)
        {
            if (!first) json.Append(',');
            first = false;
            json.Append("{\"role\":").Append(Quote(input.Role)).Append(",\"path\":").Append(Quote(input.RelativePath))
                .Append(",\"sha256\":").Append(Quote(input.Sha256)).Append(",\"bytes\":").Append(input.ByteLength).Append('}');
        }
        private static void AppendVector(StringBuilder json, AssetVector3 vector)
        {
            json.Append('[').Append(vector.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vector.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(vector.Z.ToString("R", CultureInfo.InvariantCulture)).Append(']');
        }
        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4"));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
    }
}
