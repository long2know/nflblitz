using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.AssetImport
{
    public static class BindPoseMeshBuilder
    {
        public static ReadOnlyCollection<BindPoseLimbMesh> Build(SourcePlayer player,
            IList<PlayerMaterialBinding> bindings, PlayerCoordinateConversion conversion)
        {
            if (player == null || conversion == null) throw new ArgumentNullException(player == null ? "player" : "conversion");
            PlayerMaterialBinding[] ordered = OrderBindings(player, bindings);
            var result = new List<BindPoseLimbMesh>();
            for (int i = 0; i < player.Limbs.Count; i++)
            {
                SourceLimb limb = player.Limbs[i];
                SourceRigNode node = player.Nodes[i];
                AssetVector3 pivot = conversion.Convert(node.BindPivot);
                AssetVector3 parentPivot = node.ParentIndex < 0 ? new AssetVector3() :
                    conversion.Convert(player.Nodes[node.ParentIndex].BindPivot);
                var positions = new List<AssetVector3>();
                var uvs = new List<AssetVector2>();
                var sourceNormals = new List<AssetVector3>();
                var triangleGroups = new List<int>[ordered.Length];
                for (int m = 0; m < ordered.Length; m++) triangleGroups[m] = new List<int>();
                var seams = new Dictionary<Tuple<int, int, int>, int>();
                foreach (SourceFace face in limb.Faces)
                {
                    var indices = new int[3];
                    PlayerMaterialBinding binding = ordered[face.MaterialIndex];
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = face.VertexIndices[corner], st = face.TextureCoordinateIndices[corner];
                        var key = Tuple.Create(vertex, st, face.MaterialIndex);
                        int index;
                        if (!seams.TryGetValue(key, out index))
                        {
                            index = positions.Count;
                            seams.Add(key, index);
                            positions.Add(conversion.Convert(limb.Vertices[vertex]) - pivot);
                            AssetVector2 uv = limb.TextureCoordinates[st];
                            float u = uv.X / binding.SourceSUnits;
                            float v = uv.Y / binding.SourceTUnits;
                            uvs.Add(new AssetVector2(u, binding.FlipV ? 1f - v : v));
                            if (limb.Normals != null) sourceNormals.Add(Unit(conversion.Convert(limb.Normals[vertex])));
                        }
                        indices[corner] = index;
                    }
                    List<int> group = triangleGroups[face.MaterialIndex];
                    group.Add(indices[0]);
                    group.Add(indices[conversion.ReverseDeclaredTriangle ? 2 : 1]);
                    group.Add(indices[conversion.ReverseDeclaredTriangle ? 1 : 2]);
                }
                var groups = new List<int[]>();
                foreach (List<int> group in triangleGroups) groups.Add(group.ToArray());
                AssetVector3[] normals = limb.Normals == null ? RecalculateNormals(positions, groups) : sourceNormals.ToArray();
                result.Add(new BindPoseLimbMesh(limb.Symbol, pivot - parentPivot, positions.ToArray(),
                    normals, uvs.ToArray(), groups, limb.Normals == null));
            }
            return result.AsReadOnly();
        }

        public static PlayerMaterialBinding[] OrderBindings(SourcePlayer player, IList<PlayerMaterialBinding> bindings)
        {
            if (bindings == null) throw new PlayerAssetException("An explicit material map is required; no generic color fallback exists.");
            if (bindings.Count > PlayerImportLimits.Materials) throw new PlayerAssetException("Too many material-map entries (limit 128).");
            var map = new Dictionary<string, PlayerMaterialBinding>(StringComparer.Ordinal);
            var declared = new HashSet<string>(player.MaterialSlots, StringComparer.Ordinal);
            var unknown = new List<string>();
            foreach (PlayerMaterialBinding binding in bindings)
            {
                if (binding == null) throw new PlayerAssetException("Null material-map entry.");
                if (map.ContainsKey(binding.Slot)) throw new PlayerAssetException("Duplicate material mapping '" + binding.Slot + "'.");
                map.Add(binding.Slot, binding);
                if (!declared.Contains(binding.Slot)) unknown.Add(binding.Slot);
            }
            var missing = new List<string>();
            foreach (string slot in player.MaterialSlots) if (!map.ContainsKey(slot)) missing.Add(slot);
            if (missing.Count > 0 || unknown.Count > 0)
                throw new PlayerAssetException("Material mappings missing: [" + string.Join(", ", missing) +
                    "]; unknown mappings: [" + string.Join(", ", unknown) + "]. Exact explicit texture paths and UV conventions are required.");
            var result = new PlayerMaterialBinding[player.MaterialSlots.Count];
            for (int i = 0; i < result.Length; i++) result[i] = map[player.MaterialSlots[i]];
            return result;
        }

        private static AssetVector3[] RecalculateNormals(IList<AssetVector3> positions, IList<int[]> groups)
        {
            var sums = new double[positions.Count, 3];
            foreach (int[] triangles in groups)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    AssetVector3 a = positions[triangles[i]], b = positions[triangles[i + 1]], c = positions[triangles[i + 2]];
                    double ux = (double)b.X - a.X, uy = (double)b.Y - a.Y, uz = (double)b.Z - a.Z;
                    double vx = (double)c.X - a.X, vy = (double)c.Y - a.Y, vz = (double)c.Z - a.Z;
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    if (nx == 0 && ny == 0 && nz == 0)
                        throw new PlayerAssetException("Zero-area triangle encountered while calculating normals; no face was discarded.");
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[i + corner];
                        sums[vertex, 0] += nx; sums[vertex, 1] += ny; sums[vertex, 2] += nz;
                    }
                }
            }
            var normals = new AssetVector3[positions.Count];
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Unit(sums[i, 0], sums[i, 1], sums[i, 2]);
            return normals;
        }

        private static AssetVector3 Unit(AssetVector3 n) { return Unit(n.X, n.Y, n.Z); }
        private static AssetVector3 Unit(double x, double y, double z)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (!(length > 0) || double.IsInfinity(length) || double.IsNaN(length))
                throw new PlayerAssetException("Cannot normalize a zero/non-finite normal; inspect face winding and source normals.");
            return new AssetVector3((float)(x / length), (float)(y / length), (float)(z / length));
        }
    }
}
