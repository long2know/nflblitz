using System;
using System.Collections.Generic;
using System.Linq;
using D = Blitz.AssetImport.GeneratedCInitializer.Declaration;
using V = Blitz.AssetImport.GeneratedCInitializer.Value;

namespace Blitz.AssetImport
{
    public static class GeneratedPlayerReader
    {
        public static SourcePlayer Read(string modelText, string modelLabel, string skeletonText,
            string skeletonLabel, PlayerSymbolSelection selection)
        {
            if (selection == null) throw new ArgumentNullException("selection");
            AuthorizedAssetRoot.ValidateRelativePath(modelLabel);
            AuthorizedAssetRoot.ValidateRelativePath(skeletonLabel);
            Dictionary<string, D> model = GeneratedCInitializer.Parse(modelText, modelLabel);
            Dictionary<string, D> skeleton = GeneratedCInitializer.Parse(skeletonText, skeletonLabel);
            var all = new Dictionary<string, D>(model, StringComparer.Ordinal);
            foreach (D declaration in skeleton.Values)
            {
                if (all.ContainsKey(declaration.Name))
                    Fail(declaration.Location, "Duplicate symbol across model/skeleton: '" + declaration.Name + "'.");
                all.Add(declaration.Name, declaration);
            }
            if (all.Count > PlayerImportLimits.Declarations) throw new PlayerAssetException("Combined declaration limit exceeded.");
            foreach (D declaration in all.Values) ValidateShape(declaration);

            string[] modelOrder = References(Find(model, selection.ModelLimbs, "LIMB", true, new SourceLocation(modelLabel, 1, 1)), true);
            string[] rigOrder = References(Find(skeleton, selection.SkeletonLimbs, "LIMB", true, new SourceLocation(skeletonLabel, 1, 1)), false);
            string[] slots = Materials(Find(model, selection.Materials, "char", true, new SourceLocation(modelLabel, 1, 1)));
            D rig = Find(skeleton, selection.Skeleton, "SNODE", false, new SourceLocation(skeletonLabel, 1, 1));
            if (rig.Initializer.Items.Count > PlayerImportLimits.Nodes)
                Fail(rig.Location, "Skeleton exceeds the 256-node limit.");
            if (rigOrder.Length != rig.Initializer.Items.Count)
                Fail(rig.Location, "Skeleton node count " + rig.Initializer.Items.Count + " does not match mapping '" +
                    selection.SkeletonLimbs + "' count " + rigOrder.Length + ".");
            RequireUnique(modelOrder, model[selection.ModelLimbs].Location, "model limb");
            RequireUnique(rigOrder, skeleton[selection.SkeletonLimbs].Location, "skeleton limb");
            var modelSet = new HashSet<string>(modelOrder, StringComparer.Ordinal);
            var rigSet = new HashSet<string>(rigOrder, StringComparer.Ordinal);
            if (!modelSet.SetEquals(rigSet))
                Fail(rig.Location, "Mismatched limb mapping. Missing from skeleton: [" +
                    string.Join(", ", modelOrder.Where(n => !rigSet.Contains(n))) + "]; not in model: [" +
                    string.Join(", ", rigOrder.Where(n => !modelSet.Contains(n))) + "].");

            SourceRigNode[] nodes = ReadNodes(rig);
            var limbs = new SourceLimb[rigOrder.Length];
            int totalTriangles = 0;
            for (int i = 0; i < rigOrder.Length; i++)
            {
                D limb = Find(all, rigOrder[i], "LIMB", false, skeleton[selection.SkeletonLimbs].Initializer.Items[i].Location);
                if (limb.Array) Fail(limb.Location, "Expected a scalar LIMB.");
                List<V> fields = limb.Initializer.Items;
                int vertexCount = GeneratedCInitializer.Integer(fields[0]);
                int triangleCount = GeneratedCInitializer.Integer(fields[1]);
                if (vertexCount < 1 || vertexCount > 21846)
                    Fail(fields[0].Location, "LIMB vertex count must be 1..21846 (ushort stride-three addressing).");
                if (triangleCount < 1 || triangleCount > PlayerImportLimits.TotalTriangles - totalTriangles)
                    Fail(fields[1].Location, "LIMB triangle count is invalid or combined triangle limit exceeded.");
                totalTriangles += triangleCount;
                AssetVector3[] vertices = Vectors3(Find(all, Reference(fields[3], false), "VERTEX", false, fields[3].Location));
                if (vertexCount != vertices.Length)
                    Fail(limb.Location, "LIMB '" + limb.Name + "' declares " + vertexCount + " vertices but array has " + vertices.Length + ".");
                AssetVector3[] normals = IsNull(fields[2]) ? null :
                    Vectors3(Find(all, Reference(fields[2], false), "VERTEX", false, fields[2].Location));
                if (normals != null)
                {
                    if (normals.Length != vertexCount) Fail(fields[2].Location, "Normal count does not match LIMB vertex count.");
                    foreach (AssetVector3 normal in normals)
                        if (normal.X == 0 && normal.Y == 0 && normal.Z == 0)
                            Fail(fields[2].Location, "Source contains a zero normal.");
                }
                AssetVector2[] st = Vectors2(Find(all, Reference(fields[4], false), "ST", false, fields[4].Location));
                D tris = Find(all, Reference(fields[5], false), "TRI", false, fields[5].Location);
                SourceFace[] faces = Faces(tris, vertexCount, triangleCount, st.Length, slots.Length);
                limbs[i] = new SourceLimb(limb.Name, vertices, normals, st, faces);
            }
            return new SourcePlayer(selection, limbs, nodes, slots);
        }

        private static void ValidateShape(D declaration)
        {
            List<V> rows = declaration.Initializer.Items;
            if (rows.Count == 0) Fail(declaration.Location, "Empty generated declaration '" + declaration.Name + "'.");
            if (declaration.Pointer)
            {
                bool nullSeen = false;
                foreach (V value in rows)
                {
                    if (nullSeen) Fail(value.Location, "NULL must be the final pointer-array entry.");
                    nullSeen = value.Kind == "symbol" && value.Text == "NULL";
                    if (!nullSeen && (declaration.Type == "char" ? value.Kind != "string" : value.Kind != "address"))
                        Fail(value.Location, declaration.Type == "char" ? "Expected material string or final NULL." : "Expected &limb_symbol or final NULL.");
                }
                return;
            }
            if (declaration.Type == "LIMB")
            {
                if (declaration.Array || rows.Count != 6) Fail(declaration.Location, "LIMB requires six scalar fields, not an array of LIMBs.");
                GeneratedCInitializer.Integer(rows[0]); GeneratedCInitializer.Integer(rows[1]);
                for (int i = 2; i < 6; i++) Reference(rows[i], i == 2);
                return;
            }
            foreach (V row in rows)
            {
                int count = declaration.Type == "VERTEX" ? 3 : declaration.Type == "ST" ? 2 : declaration.Type == "TRI" ? 7 : 5;
                Row(row, count);
                if (declaration.Type == "TRI")
                {
                    for (int i = 0; i < 7; i++)
                    {
                        int n = GeneratedCInitializer.Integer(row.Items[i]);
                        if (i < 6 ? n < 0 || n > ushort.MaxValue : n < short.MinValue || n > short.MaxValue)
                            Fail(row.Items[i].Location, "TRI field exceeds its original ushort/short range.");
                    }
                }
                else
                {
                    int components = declaration.Type == "SNODE" ? 3 : count;
                    for (int i = 0; i < components; i++) GeneratedCInitializer.FiniteFloat(row.Items[i]);
                    if (declaration.Type == "SNODE")
                    {
                        Flags(row.Items[3]);
                        Row(row.Items[4], 3);
                        foreach (V child in row.Items[4].Items) GeneratedCInitializer.Integer(child);
                    }
                }
            }
        }

        private static SourceFace[] Faces(D declaration, int vertexCount, int faceCount, int stCount, int materialCount)
        {
            List<V> rows = declaration.Initializer.Items;
            if (rows.Count != faceCount + 1)
                Fail(declaration.Location, "TRI array must contain exactly ntris faces and one final -20 terminator.");
            V terminator = rows[rows.Count - 1];
            if (GeneratedCInitializer.Integer(terminator.Items[6]) != -20 ||
                terminator.Items.Take(6).Any(v => GeneratedCInitializer.Integer(v) != 0))
                Fail(terminator.Location, "Expected canonical TRI terminator {0,0,0,0,0,0,-20}.");
            int[] uvForVertex = Enumerable.Repeat(-1, vertexCount).ToArray();
            int material = -1;
            var result = new SourceFace[faceCount];
            for (int face = 0; face < faceCount; face++)
            {
                V row = rows[face];
                int[] vertices = new int[3], uv = new int[3], rawUv = new int[3];
                int texture = GeneratedCInitializer.Integer(row.Items[6]);
                if (texture >= 0)
                {
                    if (texture % 4 != 0) Fail(row.Items[6].Location, "Material texture code must be a multiple of four.");
                    material = texture / 4;
                    if (material >= materialCount) Fail(row.Items[6].Location, "Material index " + material + " exceeds the selected material slots.");
                }
                else if (texture != -4 && texture != -8 && texture != -12 && texture != -16)
                    Fail(row.Items[6].Location, "Unsupported/early TRI texture code " + texture + "; only -4/-8/-12/-16 carry state and final -20 terminates.");
                if (material < 0) Fail(row.Location, "First face must explicitly select a material; state does not leak between limbs.");
                int updates = texture >= 0 ? 3 : 4 + texture / 4;
                for (int corner = 0; corner < 3; corner++)
                {
                    int packedVertex = GeneratedCInitializer.Integer(row.Items[corner]);
                    if (packedVertex % 3 != 0)
                        Fail(row.Items[corner].Location, "TRI vertex index " + packedVertex + " is not a multiple of three.");
                    vertices[corner] = packedVertex / 3;
                    if (vertices[corner] >= vertexCount) Fail(row.Items[corner].Location, "TRI vertex index is outside the LIMB vertex array.");
                    rawUv[corner] = GeneratedCInitializer.Integer(row.Items[corner + 3]);
                    if ((corner < updates) == (rawUv[corner] == 255))
                        Fail(row.Items[corner + 3].Location, "TRI UV-update prefix does not match texture code " + texture + ".");
                    if (rawUv[corner] != 255)
                    {
                        if (rawUv[corner] >= stCount) Fail(row.Items[corner + 3].Location, "TRI UV index " + rawUv[corner] + " is outside the ST array.");
                        uvForVertex[vertices[corner]] = rawUv[corner];
                    }
                }
                if (vertices[0] == vertices[1] || vertices[0] == vertices[2] || vertices[1] == vertices[2])
                    Fail(row.Location, "Repeated vertex in a face is not supported; no face is silently discarded.");
                for (int corner = 0; corner < 3; corner++)
                {
                    uv[corner] = uvForVertex[vertices[corner]];
                    if (uv[corner] < 0) Fail(row.Location, "UV 255 reuses uninitialized state for vertex " + vertices[corner] + ".");
                }
                result[face] = new SourceFace(vertices, uv, material, row.Location);
            }
            return result;
        }

        private static SourceRigNode[] ReadNodes(D declaration)
        {
            int count = declaration.Initializer.Items.Count;
            int[] parents = Enumerable.Repeat(-1, count).ToArray();
            var children = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                V row = declaration.Initializer.Items[i];
                children[i] = new List<int>();
                bool zeroSeen = false;
                foreach (V value in row.Items[4].Items)
                {
                    int relative = GeneratedCInitializer.Integer(value);
                    if (relative == 0) { zeroSeen = true; continue; }
                    if (zeroSeen) Fail(value.Location, "Skeleton child slots must be contiguous before zero terminators.");
                    long target = (long)i + relative;
                    if (target < 0 || target >= count) Fail(value.Location, "Self-relative child points outside the skeleton.");
                    int child = (int)target;
                    if (parents[child] != -1) Fail(value.Location, "Skeleton node " + child + " has duplicate/multiple parents.");
                    parents[child] = i; children[i].Add(child);
                }
            }
            byte[] colors = new byte[count];
            for (int i = 0; i < count; i++) Visit(i, children, colors, declaration);
            if (parents[0] != -1) Fail(declaration.Location, "Skeleton root node 0 has a parent.");
            for (int i = 1; i < count; i++)
                if (parents[i] == -1) Fail(declaration.Initializer.Items[i].Location, "Skeleton node " + i + " is missing a parent/unreachable from root 0.");
            var result = new SourceRigNode[count];
            for (int i = 0; i < count; i++)
            {
                V row = declaration.Initializer.Items[i];
                result[i] = new SourceRigNode(i, parents[i], Vector3(row), Flags(row.Items[3]), children[i].ToArray());
            }
            return result;
        }

        private static void Visit(int node, IList<int>[] children, byte[] colors, D declaration)
        {
            if (colors[node] == 1) Fail(declaration.Initializer.Items[node].Location, "Skeleton cycle detected at node " + node + ".");
            if (colors[node] == 2) return;
            colors[node] = 1;
            foreach (int child in children[node]) Visit(child, children, colors, declaration);
            colors[node] = 2;
        }

        private static int Flags(V value)
        {
            int flags;
            if (value.Kind == "number") flags = GeneratedCInitializer.Integer(value);
            else if (value.Kind == "symbol" && value.Text == "SN_NODRAW") flags = 2;
            else if (value.Kind == "symbol" && value.Text == "SN_HEADLIMB") flags = 4;
            else if (value.Kind == "symbol" && value.Text == "SN_DBLSIDE") flags = 1;
            else { Fail(value.Location, "Unsupported skeleton flags; expected 0..7, SN_NODRAW, SN_HEADLIMB, or SN_DBLSIDE."); return 0; }
            if (flags < 0 || flags > 7) Fail(value.Location, "Unknown skeleton flag bits.");
            return flags;
        }

        private static AssetVector3[] Vectors3(D declaration)
        {
            return declaration.Initializer.Items.Select(Vector3).ToArray();
        }
        private static AssetVector3 Vector3(V row)
        {
            return new AssetVector3(GeneratedCInitializer.FiniteFloat(row.Items[0]),
                GeneratedCInitializer.FiniteFloat(row.Items[1]), GeneratedCInitializer.FiniteFloat(row.Items[2]));
        }
        private static AssetVector2[] Vectors2(D declaration)
        {
            return declaration.Initializer.Items.Select(row => new AssetVector2(
                GeneratedCInitializer.FiniteFloat(row.Items[0]), GeneratedCInitializer.FiniteFloat(row.Items[1]))).ToArray();
        }
        private static string[] References(D declaration, bool requireNull)
        {
            List<V> values = declaration.Initializer.Items;
            bool nullTerminated = IsNull(values[values.Count - 1]);
            if (requireNull && !nullTerminated) Fail(declaration.Location, "Model limb array must end with NULL.");
            string[] result = values.Take(values.Count - (nullTerminated ? 1 : 0)).Select(v => v.Text).ToArray();
            if (result.Length == 0 || result.Length > PlayerImportLimits.Nodes)
                Fail(declaration.Location, "Limb mapping count must be 1..256.");
            return result;
        }
        private static string[] Materials(D declaration)
        {
            List<V> values = declaration.Initializer.Items;
            if (!IsNull(values[values.Count - 1])) Fail(declaration.Location, "Material array must end with NULL.");
            string[] result = values.Take(values.Count - 1).Select(v => v.Text).ToArray();
            if (result.Length == 0 || result.Length > PlayerImportLimits.Materials)
                Fail(declaration.Location, "Material count must be 1..128.");
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in result)
            {
                AuthorizedAssetRoot.ValidateRelativePath(name);
                if (name.IndexOf('/') >= 0 || !unique.Add(name))
                    Fail(declaration.Location, "Duplicate/ambiguous or non-leaf material slot '" + name + "'.");
            }
            return result;
        }
        private static D Find(Dictionary<string, D> declarations, string symbol, string type, bool pointer, SourceLocation referenceLocation)
        {
            D result;
            if (!declarations.TryGetValue(symbol, out result))
                throw new PlayerAssetException(referenceLocation, "Missing required " + type + " symbol '" + symbol + "'.");
            if (result.Type != type || result.Pointer != pointer)
                Fail(result.Location, "Symbol '" + symbol + "' must be " + type + (pointer ? "*" : "") + ".");
            return result;
        }
        private static string Reference(V value, bool allowNull)
        {
            if (value.Kind != "symbol" || (!allowNull && value.Text == "NULL"))
                Fail(value.Location, "Expected a non-null array symbol" + (allowNull ? " or NULL" : "") + ".");
            return value.Text;
        }
        private static bool IsNull(V value) { return value.Kind == "symbol" && value.Text == "NULL"; }
        private static void RequireUnique(string[] values, SourceLocation location, string description)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
                if (!unique.Add(value)) Fail(location, "Duplicate " + description + " mapping '" + value + "'.");
        }
        private static void Row(V value, int count)
        {
            if (value.Kind != "list" || value.Items.Count != count)
                Fail(value.Location, "Expected a row with exactly " + count + " fields.");
        }
        private static void Fail(SourceLocation location, string message) { throw new PlayerAssetException(location, message); }
    }
}
