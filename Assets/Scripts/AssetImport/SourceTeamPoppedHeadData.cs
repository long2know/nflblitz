using System;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class SourceTeamPoppedHeadData : ISourceTeamPoppedHeadData
    {
        public const string Schema = "original-source-authored-popped-head/v1";
        public const string Origin = "original-phead-SDL-source-authored-reconstruction-not-native-head.h";
        public const string SourceRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string SourceSha256 = "3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257";
        public const string HeaderSha256 = "f835cda5498db7b3f2783b3cfda99d07a2ef765f821f1655c1b4be3d555a57b5";
        public const int HeadJoint = 8;
        public const int RawTextureCode = 4;
        public const int LogicalTextureSlot = RawTextureCode / 4;
        public string ContentSha256 { get; }
        public SourceLimb Limb { get; }
        public bool NativeGeneratedHeaderEquivalent { get { return false; } }

        private SourceTeamPoppedHeadData(string hash, SourceLimb limb)
        { ContentSha256 = hash; Limb = limb; }

        public static SourceTeamPoppedHeadData Read(byte[] manifest, string expectedSha256, Func<string, byte[]> readVerifiedFile)
        {
            if (readVerifiedFile == null) throw new ArgumentNullException(nameof(readVerifiedFile));
            RosterGuard.Hex(expectedSha256, 64, "popped-head manifest SHA256");
            RosterGuard.Require(manifest != null && manifest.Length >= 2 && manifest.Length <= 65536 &&
                AuthorizedAssetRoot.Sha256(manifest) == expectedSha256, "Popped-head manifest content pin mismatch.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(manifest), "original popped head");
            RosterGuard.Require(root.Text("schema") == Schema && root.Text("origin") == Origin &&
                root.Text("sourceRevision") == SourceRevision && root.Path("sourcePath") == "models/phead.sdl" &&
                root.Text("sourceSha256") == SourceSha256 &&
                root.Text("sourceGitBlobSha1") == "23410e01bb6db9e5fd9bacdd9440691147046798",
                "Original phead source/schema/origin pin mismatch; no replacement head is accepted.");
            root.ExactInteger("sourceBytes", 39272);
            string sourcePath = root.Path("sourcePayload"), headerPath = root.Path("headerPath");
            RosterGuard.Require(sourcePath == "models/phead.sdl" && headerPath == "phead.source-authored.h" &&
                root.Text("headerSha256") == HeaderSha256, "Popped-head source-authored header identity changed.");
            int headerBytes = root.Integer("headerBytes", 1, 128 * 1024);
            RosterGuard.Require(root.Number("sourceScale", 1.2, 1.2) == 1.2, "Original phead cvtmod factor is1.2.");
            root.ExactInteger("sourceHeadJoint", HeadJoint);
            root.ExactInteger("rawTextureCode", RawTextureCode);
            RosterGuard.Require(root.Integer("logicalTextureSlot", 0, 8) == LogicalTextureSlot,
                "Popped-head raw4/logical HEDSHU1 mapping mismatch.");
            root.ExactInteger("sourceSUnits", 256); root.ExactInteger("sourceTUnits", 256);
            root.ExactInteger("vertices", 83); root.ExactInteger("textureCoordinates", 161);
            root.ExactInteger("triangles", 157);
            RosterGuard.Require(!root.Boolean("nativeGeneratedHeaderEquivalent"),
                "SDL reconstruction must not claim recovered native generated head.h.");
            RosterGuard.Require(root.Text("triangleOrderOrigin") ==
                "Source SDL polygon order; original -z native qsort permutation unqualified.",
                "Popped-head triangle-order provenance is required.");
            root.Text("recipe");
            ValidateCalibration(root.Array("calibration"));
            root.Done();
            byte[] source = readVerifiedFile(sourcePath), header = readVerifiedFile(headerPath);
            RosterGuard.Require(source != null && source.Length == 39272 && AuthorizedAssetRoot.Sha256(source) == SourceSha256,
                "Exact original models/phead.sdl is required; no generic/custom-head substitute.");
            RosterGuard.Require(header != null && header.Length == headerBytes && AuthorizedAssetRoot.Sha256(header) == HeaderSha256,
                "The calibrated source-authored phead geometry/header content pin changed.");
            var model = new StringBuilder(AuthorizedAssetRoot.DecodeUtf8(header, headerPath));
            model.Append("\nLIMB *phead_source_model[]={&limb_phead_obj,NULL};\nchar *phead_source_materials[]={");
            foreach (string slot in SourceTeamGeneratedModels.MaterialSlots) model.Append('"').Append(slot).Append("\",");
            model.Append("NULL};");
            const string skeleton = "SNODE phead_source_skeleton[]={{0,0,0,SN_HEADLIMB,{0,0,0}}};" +
                "LIMB *phead_source_order[]={&limb_phead_obj};";
            SourcePlayer parsed = GeneratedPlayerReader.Read(model.ToString(), headerPath, skeleton,
                "phead.source-mapping.h", new PlayerSymbolSelection("phead_source_model", "phead_source_skeleton",
                    "phead_source_order", "phead_source_materials"));
            SourceLimb limb = parsed.Limbs[0];
            RosterGuard.Require(limb.Vertices.Count == 83 && limb.TextureCoordinates.Count == 161 &&
                limb.Faces.Count == 157 && limb.Normals == null, "Original phead geometry inventory changed.");
            foreach (SourceFace face in limb.Faces)
                RosterGuard.Require(face.MaterialIndex == LogicalTextureSlot,
                    "Original TRI texture4 is byte-stride4 HEDSHU slot1, not logical SLVMSK slot4.");
            return new SourceTeamPoppedHeadData(expectedSha256, limb);
        }

        private static void ValidateCalibration(object[] values)
        {
            RosterGuard.Require(values.Length == 2, "Both original SDL/generated calibration pairs are required.");
            string[] names = { "plyr266", "plyr498" };
            string[] sdl = { "37dfe2afcef46c9ebfa21298486454a6112aaaf40cf38b979925449e53fe9294",
                "194152a05820fb4f134ff47ca48898e4fc217d0729d74fdbee456fb12ceaf2a3" };
            string[] headers = { "9d91e22e0f80dc369a52c8956c6fb3f7b37504a3b266a8cba4f5125d93741149",
                "2f170cd7d052d541c2ee3259252b0c8653e161195c38bc85d95ec63a7c422621" };
            for (int i = 0; i < values.Length; i++)
            {
                RosterObject proof = RosterJson.Object(values[i], "phead calibration");
                RosterGuard.Require(proof.Text("model") == names[i] && proof.Text("sdlSha256") == sdl[i] &&
                    proof.Text("generatedHeaderSha256") == headers[i], "Original phead calibration input pins changed.");
                object[] polysets = proof.Array("polysets");
                RosterGuard.Require(polysets.Length == 15, "Each original calibration model has fifteen limbs.");
                int skinCorners = 0;
                foreach (object value in polysets)
                {
                    RosterObject poly = RosterJson.Object(value, "phead calibration polyset");
                    poly.Text("polyset"); poly.Integer("vertices", 1, 512); poly.Integer("faces", 1, 1024);
                    RosterGuard.Require(poly.Boolean("exactCoordinates") && poly.Boolean("exactTopologyWindingMaterials"),
                        "Phead calibration must prove the actual geometry/topology/material mapping.");
                    skinCorners += poly.Integer("skinUvCorners", 0, 3072);
                    double limit = poly.Number("skinUvPrecisionBoundFromSixDecimalSdl", 0.000128, 0.000128);
                    poly.Number("maxSkinUvSourceUnitDelta", 0, limit);
                    poly.Text("packedUniformUvs");
                    poly.Done();
                }
                RosterGuard.Require(skinCorners > 0, "Actual HEDSHU skin UV calibration is missing.");
                proof.Done();
            }
        }
    }
}
