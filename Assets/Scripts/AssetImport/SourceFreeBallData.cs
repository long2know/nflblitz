using System;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class SourceFreeBallData
    {
        public const string Schema = "original-source-authored-free-ball/v1";
        public const string Origin = "original-medball2-SDL-source-authored-not-native-medball2.h";
        public const string SourceRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string SourceSha256 = "d54218dd7d0fc45fe7ad7072cb39b12913dfed80687999f69d5e93a269bc9704";
        public const string HeaderSha256 = "22b92aa3f0b113be4c18edef70a6503fac448c1d85ff187b84b68c23b8d2eada";
        public const string PreparedManifestSha256 = "3d7ab26acab8a968058c6644011e1ac503547bf6ae67705974e3848ee5e00549";
        public const string LimbSymbol = "limb_medball_obj_big";
        public const string TextureId = "runtime/HEDSHU.WMS";
        public const string AlternateTextureId = "runtime/HEDSHU2.WMS";
        public const string TextureSha256 = "b2a5795cc67286d6a9a1c60c21f92f393134ab81f18dedfad84ffe405edb3fa3";
        public const string AlternateTextureSha256 = "b59af47bf7baf6a2bcffacd4fb68af206229099102c00baf608d771f4ac5836e";
        public SourceLimb Limb { get; }
        public string ContentSha256 { get; }
        private readonly SourcePlayer model;

        private SourceFreeBallData(SourcePlayer model, string hash)
        { this.model = model; Limb = model.Limbs[0]; ContentSha256 = hash; }

        public static SourceFreeBallData Read(byte[] manifest, string expectedSha256, Func<string, byte[]> readFile)
        {
            if (readFile == null) throw new ArgumentNullException(nameof(readFile));
            RosterGuard.Require(expectedSha256 == PreparedManifestSha256 && manifest != null &&
                manifest.Length <= 65536 && AuthorizedAssetRoot.Sha256(manifest) == PreparedManifestSha256,
                "Exact calibrated original free-ball manifest required; no held-ball or generic mesh fallback.");
            // The complete deterministic manifest pin includes both original SDL/generated
            // calibration proofs, the factor1 recipe, zero pivot and sole raw0 decal mapping.
            byte[] source = readFile("models/medball2.sdl"), header = readFile("medball2.source-authored.h");
            RosterGuard.Require(source != null && source.Length == 13635 && AuthorizedAssetRoot.Sha256(source) == SourceSha256,
                "Original models/medball2.sdl content pin mismatch.");
            RosterGuard.Require(header != null && AuthorizedAssetRoot.Sha256(header) == HeaderSha256,
                "Original source-authored free-ball geometry content pin mismatch.");
            var text = new StringBuilder(AuthorizedAssetRoot.DecodeUtf8(header, "medball2.source-authored.h"));
            text.Append("\nLIMB *free_ball_model[]={&limb_medball_obj_big,NULL};");
            text.Append("\nchar *free_ball_materials[]={\"hedshu.wms\",NULL};");
            const string skeleton = "SNODE free_ball_rig[]={{0,0,0,0,{0,0,0}}};" +
                "LIMB *free_ball_order[]={&limb_medball_obj_big};";
            SourcePlayer parsed = GeneratedPlayerReader.Read(text.ToString(), "medball2.source-authored.h", skeleton,
                "free-ball.mapping.h", new PlayerSymbolSelection("free_ball_model", "free_ball_rig",
                    "free_ball_order", "free_ball_materials"));
            SourceLimb limb = parsed.Limbs[0];
            RosterGuard.Require(limb.Symbol == LimbSymbol && limb.Vertices.Count == 26 &&
                limb.TextureCoordinates.Count == 66 && limb.Faces.Count == 48,
                "Original medball2 geometry inventory changed.");
            foreach (SourceFace face in limb.Faces)
                RosterGuard.Require(face.MaterialIndex == 0, "Free ball binds its sole HEDSHU decal, not player material6.");
            return new SourceFreeBallData(parsed, expectedSha256);
        }

        public BindPoseLimbMesh BuildSourceMesh()
        {
            return BindPoseMeshBuilder.Build(model, new[] {
                new PlayerMaterialBinding("hedshu.wms", "textures/hedshu.wms.authoring.tga", 256, 256, true)
            }, new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
                SignedSourceAxis.PositiveZ, SourceTriangleOrder.Renderer321))[0];
        }

        public static void RequireTextures(SourceTeamPresentationData teams)
        {
            if (teams == null) throw new ArgumentNullException(nameof(teams));
            int found = 0;
            foreach (SourceTeamTexture texture in teams.Textures)
                if (texture.Id == TextureId || texture.Id == AlternateTextureId)
                {
                    bool alternate = texture.Id == AlternateTextureId;
                    RosterGuard.Require(texture.Sha256 == (alternate ? AlternateTextureSha256 : TextureSha256) &&
                        texture.Width == 64 && texture.Height == 64 &&
                        texture.SourcePath == (alternate ? "img3d/hedshu2.tga" : "img3d/hedshu.tga") &&
                        texture.Representation == "byte-identical-original-authoring-tga",
                        "Free-ball HEDSHU/HEDSHU2 authoring material pin mismatch; BALL.WMS is only the held-ball mapping.");
                    found++;
                }
            RosterGuard.Require(found == 2, "Both actual free-ball HEDSHU decal resources are required.");
        }
    }
}
