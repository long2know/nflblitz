using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Blitz.Contracts;
using D = Blitz.AssetImport.GeneratedCInitializer.Declaration;
using V = Blitz.AssetImport.GeneratedCInitializer.Value;

namespace Blitz.AssetImport
{
    public static class SourceTeamGeneratedModels
    {
        private static readonly PlayerCoordinateConversion Conversion = new PlayerCoordinateConversion(
            SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY, SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);

        public static SourcePlayer ReadModel(SourceTeamRosterLibrary library, int modelIndex,
            Func<string, string> readVerifiedHeader, int customHeadIndex = -1)
        {
            if (library == null || readVerifiedHeader == null)
                throw new ArgumentNullException(library == null ? nameof(library) : nameof(readVerifiedHeader));
            if (modelIndex < 0 || modelIndex >= library.ActorModels.Models.Count)
                throw new ArgumentOutOfRangeException(nameof(modelIndex));
            if (customHeadIndex < -1 || customHeadIndex >= library.Secrets.Heads.Count)
                throw new ArgumentOutOfRangeException(nameof(customHeadIndex));
            SourceRosterModel model = library.ActorModels.Models[modelIndex];
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var text = new StringBuilder();
            var order = new string[SourceTiming.JointCount];
            for (int i = 0; i < order.Length; i++)
            {
                SourceRosterAsset limb = model.Limbs[i];
                if (customHeadIndex >= 0 && i == library.ActorModels.HeadJointIndex)
                    limb = library.Secrets.Heads[customHeadIndex].Limb;
                if (!limb.AvailableInPinnedTree || string.IsNullOrEmpty(limb.Symbol))
                    throw new PlayerAssetException("Missing original model binding: " + limb.Id);
                if (paths.Add(limb.SourcePath)) text.Append(readVerifiedHeader(limb.SourcePath)).Append('\n');
                order[i] = limb.Symbol;
            }
            text.Append("LIMB *source_team_model[]={");
            foreach (string symbol in order) text.Append('&').Append(symbol).Append(',');
            text.Append("NULL};\nchar *source_team_materials[]={");
            foreach (string slot in MaterialSlots) text.Append('"').Append(slot).Append("\",");
            text.Append("NULL};\n");
            string skeleton = readVerifiedHeader(library.ActorModels.Skeleton.SourcePath);
            var mapping = new StringBuilder(skeleton).Append("\nLIMB *source_team_order[]={");
            for (int i = 0; i < order.Length; i++)
                mapping.Append(i == 0 ? "" : ",").Append('&').Append(order[i]);
            mapping.Append("};\n");
            return GeneratedPlayerReader.Read(text.ToString(), "converted/source-team-model.h",
                mapping.ToString(), "converted/source-team-skeleton.h",
                new PlayerSymbolSelection("source_team_model", library.ActorModels.SkeletonSymbol,
                    "source_team_order", "source_team_materials"));
        }

        public static ReadOnlyCollection<BindPoseLimbMesh> BuildModel(SourcePlayer model)
        {
            return BindPoseMeshBuilder.Build(model, MaterialBindings(), Conversion);
        }

        public static BindPoseLimbMesh BuildPoppedHead(SourcePlayer referenceModel, SourceTeamPoppedHeadData head)
        {
            if (referenceModel == null || head == null)
                throw new ArgumentNullException(referenceModel == null ? nameof(referenceModel) : nameof(head));
            if (referenceModel.Limbs.Count != SourceTiming.JointCount || referenceModel.Nodes.Count != SourceTiming.JointCount ||
                referenceModel.MaterialSlots.Count != MaterialSlots.Length)
                throw new PlayerAssetException("Original popped head requires the actual fifteen-joint/nine-slot team reference model.");
            return BuildPoppedHead(head, referenceModel.Nodes[SourceTeamPoppedHeadData.HeadJoint].BindPivot);
        }

        public static BindPoseLimbMesh BuildPoppedHead(SourceTeamPoppedHeadData head, AssetVector3 sourceBindPivot)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            var selected = new SourcePlayer(new PlayerSymbolSelection("phead_source_model", "phead_source_rig",
                "phead_source_order", "phead_source_materials"), new[] { head.Limb },
                new[] { new SourceRigNode(0, -1, sourceBindPivot, 4, new int[0]) }, MaterialSlots);
            return BuildModel(selected)[0];
        }

        public static PlayerMaterialBinding[] MaterialBindings()
        {
            var result = new PlayerMaterialBinding[MaterialSlots.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = new PlayerMaterialBinding(MaterialSlots[i], "textures/slot-" + i + ".tga",
                    i >= 2 && i <= 5 ? 128 : 256, 256, true);
            return result;
        }

        public static string[] MaterialSlots
        {
            get
            {
                return new[] { "armhnd.wms", "hedshu.wms", "jersf.wms", "leghel.wms", "slvmsk.wms",
                    "jnum.wms", "ball.wms", "custom-head.wms", "second-custom-head.wms" };
            }
        }

        public static BindPoseLimbMesh BuildNumbers(string verifiedJerseysHeader, string limbSymbol,
            int packedNumber, AssetVector3 torsoPivot)
        {
            RosterGuard.Identifier(limbSymbol);
            int tens = packedNumber >> 4, ones = packedNumber & 15;
            if (packedNumber < 0 || packedNumber > 255 || tens > 9 || ones > 9)
                throw new PlayerAssetException("Original jersey number must be packed BCD, not a decimal integer.");
            Dictionary<string, D> data = GeneratedCInitializer.Parse(verifiedJerseysHeader, "models/jerseys.h");
            D limb = Find(data, limbSymbol, "LIMB", false);
            if (limb.Initializer.Items.Count != 6) throw new PlayerAssetException("Number LIMB requires six fields.");
            List<V> fields = limb.Initializer.Items;
            int nv = GeneratedCInitializer.Integer(fields[0]), nt = GeneratedCInitializer.Integer(fields[1]);
            if (nv < 3 || nv > 256 || nt < 1 || nt > 512 || fields[2].Text != "NULL")
                throw new PlayerAssetException("Unsupported original number LIMB counts/normals.");
            D vertices = Find(data, fields[3].Text, "VERTEX", true);
            D coordinates = Find(data, fields[4].Text, "ST", true);
            D triangles = Find(data, fields[5].Text, "TRI", true);
            if (vertices.Initializer.Items.Count != nv || coordinates.Initializer.Items.Count < 1 ||
                coordinates.Initializer.Items.Count > 256)
                throw new PlayerAssetException("Original number geometry counts disagree.");
            int rows = triangles.Initializer.Items.Count;
            if (rows != nt + 1 && rows != nt + 2)
                throw new PlayerAssetException("Number TRI has unexpected trailing rows.");
            V end = triangles.Initializer.Items[rows - 1];
            RequireRow(end, 7);
            for (int j = 0; j < 7; j++)
                if (GeneratedCInitializer.Integer(end.Items[j]) != (j == 6 ? -20 : 0))
                    throw new PlayerAssetException("Missing canonical number TRI terminator.");
            if (rows == nt + 2)
            {
                V padding = triangles.Initializer.Items[nt];
                RequireRow(padding, 7);
                for (int j = 0; j < 7; j++)
                    if (GeneratedCInitializer.Integer(padding.Items[j]) != (j == 6 ? 1 : 0))
                        throw new PlayerAssetException("Unexpected original single-digit number padding.");
            }
            var points = new AssetVector3[nv];
            for (int i = 0; i < nv; i++)
            {
                V v = vertices.Initializer.Items[i];
                RequireRow(v, 3);
                points[i] = new AssetVector3(GeneratedCInitializer.FiniteFloat(v.Items[0]),
                    GeneratedCInitializer.FiniteFloat(v.Items[1]), GeneratedCInitializer.FiniteFloat(v.Items[2]));
            }
            var uv = new List<AssetVector2>();
            var faces = new SourceFace[nt];
            var cachedSt = new int[nv];
            for (int i = 0; i < cachedSt.Length; i++) cachedSt[i] = -1;
            for (int i = 0; i < nt; i++)
            {
                V row = triangles.Initializer.Items[i];
                RequireRow(row, 7);
                int digit = GeneratedCInitializer.Integer(row.Items[6]);
                if (digit != 0 && digit != 1)
                    throw new PlayerAssetException("Number TRI selector must retain original tens/ones 0/1 identity.");
                int[] indices = new int[3], st = new int[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = GeneratedCInitializer.Integer(row.Items[corner]);
                    if (index < 0 || index % 3 != 0 || index / 3 >= nv)
                        throw new PlayerAssetException("Original number TRI vertex exceeds its stride-three array.");
                    index /= 3;
                    indices[corner] = index;
                    int coordinate = GeneratedCInitializer.Integer(row.Items[corner + 3]);
                    if (coordinate != 255)
                    {
                        if (coordinate < 0 || coordinate >= coordinates.Initializer.Items.Count)
                            throw new PlayerAssetException("Original number TRI coordinate is out of range.");
                        cachedSt[index] = coordinate;
                    }
                    if (cachedSt[index] < 0)
                        throw new PlayerAssetException("Original number TRI reuses an uninitialized vertex UV.");
                    V source = coordinates.Initializer.Items[cachedSt[index]];
                    RequireRow(source, 2);
                    int value = digit == 0 ? tens : ones;
                    int atlasIndex = value == 0 ? 9 : value - 1;
                    st[corner] = uv.Count;
                    uv.Add(new AssetVector2(GeneratedCInitializer.FiniteFloat(source.Items[0]) + atlasIndex * 0.1f,
                        GeneratedCInitializer.FiniteFloat(source.Items[1])));
                }
                if (indices[0] == indices[1] || indices[0] == indices[2] || indices[1] == indices[2])
                    throw new PlayerAssetException("A counted original number TRI is degenerate.");
                faces[i] = new SourceFace(indices, st, 0, row.Location);
            }
            var numberLimb = new SourceLimb(limbSymbol, points, null, uv.ToArray(), faces);
            var rig = new SourceRigNode(0, -1, torsoPivot, 0, new int[0]);
            var player = new SourcePlayer(new PlayerSymbolSelection("numbers", "torso", "numbers", "atlas"),
                new[] { numberLimb }, new[] { rig }, new[] { "jnum.wms" });
            return BindPoseMeshBuilder.Build(player, new[] {
                new PlayerMaterialBinding("jnum.wms", "textures/number-atlas.tga", 1, 1, true)
            }, Conversion)[0];
        }

        private static D Find(Dictionary<string, D> data, string symbol, string type, bool array)
        {
            D result;
            if (!data.TryGetValue(symbol, out result) || result.Type != type || result.Pointer || result.Array != array)
                throw new PlayerAssetException("Missing/wrong original number declaration: " + symbol);
            return result;
        }

        private static void RequireRow(V value, int count)
        {
            if (value.Kind != "list" || value.Items.Count != count)
                throw new PlayerAssetException(value.Location, "Original number initializer row shape changed.");
        }
    }
}
