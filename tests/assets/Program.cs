using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;

internal static class Program
{
    private const int ExpectedAssertions = 2751;
    // Entirely original, tiny two-piece geometry: no reference model coordinates or pixel payloads.
    internal const string Model = @"// Original synthetic shared quad, tested out of declaration order.
static const VERTEX points[4] = {
  {1.0f,2,0}, {3,2,0}, {3,4,0}, {1,4,0},
};
ST coords[]={{0,0},{4,0},{4,4},{0,4},{2,4}};
TRI faces[4]={
  {0,3,6, 0,1,2, 0},
  {9,0,6, 3,255,255, -12},
  {0,6,9, 4,255,255, -12},
  {0,0,0,0,0,0,-20}
};
LIMB limb_child={4,3,NULL,points,coords,faces};
LIMB limb_root={4,3,NULL,points,coords,faces};
LIMB *model_parts[3]={&limb_child,&limb_root,NULL};
char *material_slots[] ={""test-cloth.wms"", ""test-trim.wms"", NULL};
";
    internal const string Skeleton = @"/* Pivots are model-space, not cumulatively added. */
SNODE test_skeleton[2]={
  {1,2,0,0,{2-1,0,0}},
  {2,3,0,SN_HEADLIMB,{0,0,0}},
};
LIMB *rig_parts[]={&limb_root,&limb_child};
// Unselected variants may refer to other models, but are still grammar checked.
LIMB *unselected[]={&not_supplied};
";
    private static int assertions;
    private static string fixtureRoot;
    private static PlayerSymbolSelection Symbols
    {
        get { return new PlayerSymbolSelection("model_parts", "test_skeleton", "rig_parts", "material_slots"); }
    }
    private static PlayerCoordinateConversion Identity
    {
        get { return new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.PositiveZ, SourceTriangleOrder.Declared123); }
    }
    private static PlayerMaterialBinding[] Bindings
    {
        get { return new[] { new PlayerMaterialBinding("test-trim.wms", "textures/trim.tga", 4, 4, true),
            new PlayerMaterialBinding("test-cloth.wms", "textures/cloth.tga", 4, 4, false) }; }
    }

    private static int Main(string[] args)
    {
        bool cleanFixtures = false;
        try
        {
            if (args.Length > 1) throw new ArgumentException("Usage: ImportChecks [synthetic-fixture-directory]");
            fixtureRoot = Path.GetFullPath(args.Length == 0
                ? Path.Combine(AppContext.BaseDirectory, "synthetic-fixtures-" + Guid.NewGuid().ToString("N"))
                : args[0]);
            if (args.Length == 0)
            {
                if (Directory.Exists(fixtureRoot) || File.Exists(fixtureRoot))
                    throw new IOException("Synthetic fixture directory already exists: " + fixtureRoot);
                Directory.CreateDirectory(fixtureRoot);
                cleanFixtures = true;
            }
            WriteFixtures();
            ParserAndMesh();
            ParserFailures();
            Textures();
            FilesAndProvenance();
            assertions += PoseChecks.Run(Model, Skeleton);
            if (assertions != ExpectedAssertions)
                throw new Exception("Assertion inventory changed: expected " + ExpectedAssertions + ", actual " + assertions);
            Console.WriteLine("PASS: " + assertions + " artifact assertions / exact production C# 7.3 netstandard2.1 parser, mesh math, path guard, TGA and provenance; synthetic inputs only.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (cleanFixtures) Directory.Delete(fixtureRoot, true);
        }
    }

    private static SourcePlayer Read(string model = Model, string skeleton = Skeleton)
    {
        return GeneratedPlayerReader.Read(model, "models/tiny.h", skeleton, "models/rig.h", Symbols);
    }
    private static SourcePlayer ValidPlayer()
    {
        try { return Read(); }
        catch (Exception e) { throw new Exception("ASSERT valid synthetic player parses packed stride-three indices: " + e.Message, e); }
    }
    private static void ParserAndMesh()
    {
        SourcePlayer player = ValidPlayer();
        Equal(player.Limbs.Count, 2, "all limbs retained");
        Equal(player.MaterialSlots.Count, 2, "all slots retained even when unused");
        Equal(player.Limbs[0].Symbol, "limb_root", "skeleton mapping overrides reversed model declaration order");
        Equal(player.Limbs[1].Symbol, "limb_child", "child order");
        Equal(player.Nodes[0].ParentIndex, -1, "root parent");
        Equal(player.Nodes[1].ParentIndex, 0, "relative child index parent");
        Equal(player.Nodes[0].Children[0], 1, "child integer subtraction");
        Equal(player.Nodes[1].Flags, 4, "symbolic head flag");
        Check(player.Nodes[1].DrawEnabled, "head flag must not disable rendering");
        Equal(player.Limbs[0].Faces.Count, 3, "no faces discarded");
        Sequence(player.Limbs[0].Faces[0].VertexIndices, new[] { 0, 1, 2 }, "packed vertex indices divide by three");
        Sequence(player.Limbs[0].Faces[0].TextureCoordinateIndices, new[] { 0, 1, 2 }, "first face ST indices");
        Sequence(player.Limbs[0].Faces[1].TextureCoordinateIndices, new[] { 3, 0, 2 }, "UV 255 carries state by vertex, not corner");
        Sequence(player.Limbs[0].Faces[2].TextureCoordinateIndices, new[] { 4, 2, 3 }, "later ST update produces a UV seam");
        foreach (SourceFace face in player.Limbs[0].Faces) Equal(face.MaterialIndex, 0, "compressed texture code retains material");
        var meshes = BindPoseMeshBuilder.Build(player, Bindings, Identity);
        Equal(meshes.Count, 2, "one output mesh per rig node");
        Equal(meshes[0].Positions.Count, 5, "UV seam split vertex count");
        Vector(meshes[0].Positions[0], 0, 0, 0, "root mesh pivot subtraction");
        Vector(meshes[1].Positions[0], -1, -1, 0, "child mesh pivot subtraction");
        Vector(meshes[0].LocalRestOffset, 1, 2, 0, "root rest offset");
        Vector(meshes[1].LocalRestOffset, 1, 1, 0, "child rest offset subtracts parent pivot, not cumulative pivot");
        Vector(Add(Add(meshes[0].LocalRestOffset, meshes[1].LocalRestOffset), meshes[1].Positions[0]),
            1, 2, 0, "hierarchy reconstructs source vertex in bind pose");
        Near(meshes[0].Uvs[1].X, 1, "explicit U divisor");
        Near(meshes[0].Uvs[2].Y, 1, "explicit V divisor");
        Near(meshes[0].Uvs[4].X, 0.5f, "seam keeps independent ST index");
        Sequence(meshes[0].SubmeshTriangles[0], new[] { 0, 1, 2, 3, 0, 2, 4, 2, 3 }, "declared face indices");
        Equal(meshes[0].SubmeshTriangles[1].Count, 0, "unused material submesh retained");
        foreach (AssetVector3 normal in meshes[0].Normals) Vector(normal, 0, 0, 1, "area-weighted normal");
        Check(meshes[0].RecalculatedNormals, "missing source normals explicitly marked recalculated");

        string multi = Model.Replace("{0,6,9, 4,255,255, -12}", "{0,6,9, 4,2,3, 4}");
        SourcePlayer multiPlayer = Read(multi);
        var multiMesh = BindPoseMeshBuilder.Build(multiPlayer, Bindings, Identity)[0];
        Equal(multiPlayer.Limbs[0].Faces[2].MaterialIndex, 1, "material code divided by four");
        Equal(multiMesh.SubmeshTriangles[0].Count, 6, "material zero face group");
        Equal(multiMesh.SubmeshTriangles[1].Count, 3, "material one face group");
        foreach (int vertex in multiMesh.SubmeshTriangles[1])
        {
            int rawSt = vertex == multiMesh.SubmeshTriangles[1][0] ? 4 : vertex == multiMesh.SubmeshTriangles[1][1] ? 2 : 3;
            Near(multiMesh.Uvs[vertex].Y, 1 - multiPlayer.Limbs[0].TextureCoordinates[rawSt].Y / 4, "explicit per-material V flip");
        }

        var reflection = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ, SourceTriangleOrder.Declared123);
        Equal(reflection.Determinant, -1, "reflection determinant");
        var reflected = BindPoseMeshBuilder.Build(player, Bindings, reflection);
        Sequence(reflected[0].SubmeshTriangles[0].Take(3), new[] { 0, 2, 1 }, "reflection compensates triangle winding");
        Vector(reflected[0].Normals[0], 0, 0, -1, "reflected normal orientation");
        var runtime = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.PositiveZ, SourceTriangleOrder.Renderer321);
        Sequence(BindPoseMeshBuilder.Build(player, Bindings, runtime)[0].SubmeshTriangles[0].Take(3),
            new[] { 0, 2, 1 }, "explicit runtime 321 winding");
        var both = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
        Sequence(BindPoseMeshBuilder.Build(player, Bindings, both)[0].SubmeshTriangles[0].Take(3),
            new[] { 0, 1, 2 }, "reflection and renderer reversal cancel");
        var permuted = new PlayerCoordinateConversion(SignedSourceAxis.PositiveZ, SignedSourceAxis.NegativeX,
            SignedSourceAxis.PositiveY, SourceTriangleOrder.Declared123);
        Vector(permuted.Convert(new AssetVector3(2, 3, 4)), 4, -2, 3, "explicit signed axis permutation");
        Equal(permuted.Determinant, -1, "permuted determinant");
        int permutations = 0;
        foreach (SignedSourceAxis x in Enum.GetValues(typeof(SignedSourceAxis)))
        foreach (SignedSourceAxis y in Enum.GetValues(typeof(SignedSourceAxis)))
        foreach (SignedSourceAxis z in Enum.GetValues(typeof(SignedSourceAxis)))
        {
            if ((int)x / 2 == (int)y / 2 || (int)x / 2 == (int)z / 2 || (int)y / 2 == (int)z / 2) continue;
            var c = new PlayerCoordinateConversion(x, y, z, SourceTriangleOrder.Declared123);
            var m = BindPoseMeshBuilder.Build(player, Bindings, c)[1];
            AssetVector3 world = Add(c.Convert(player.Nodes[1].BindPivot), m.Positions[0]);
            AssetVector3 expected = c.Convert(player.Limbs[1].Vertices[0]);
            Vector(world, expected.X, expected.Y, expected.Z, "all signed coordinate maps preserve bind positions");
            Near(Length(m.Normals[0]), 1, "all signed coordinate maps have unit normals");
            permutations++;
        }
        Equal(permutations, 48, "all signed permutations checked");

        string withNormals = "VERTEX n[4]={{0,0,2},{0,0,2},{0,0,2},{0,0,2}};\n" + Model.Replace("3,NULL,points", "3,n,points");
        var normalMesh = BindPoseMeshBuilder.Build(Read(withNormals), Bindings, Identity)[0];
        Check(!normalMesh.RecalculatedNormals, "source normals retained when provided");
        Vector(normalMesh.Normals[0], 0, 0, 1, "source normal normalized");
        SourcePlayer noDraw = Read(Model, Skeleton.Replace("SN_HEADLIMB", "SN_NODRAW"));
        Check(!noDraw.Nodes[1].DrawEnabled && noDraw.Limbs.Count == 2, "no-draw flag preserved without discarding limb");
        Read(Model.Replace("1.0f", "+.1e1F"), Skeleton.Replace("2-1", "2 - 1 + 0"));
        assertions++;
    }

    private static void ParserFailures()
    {
        Bad(() => Read(Model.Replace("points[4]", "points[5]")), "Declared dimension", "dimensions match actual initializer");
        Bad(() => Read(Model + "\nVERTEX points[]={{0,0,0}};"), "Duplicate symbol", "duplicate declaration");
        Bad(() => Read(Model, Skeleton + "VERTEX points[]={{0,0,0}};"), "across model/skeleton", "cross-file duplicate symbol");
        Bad(() => Read(Model.Replace("points,coords,faces", "absent,coords,faces")), "Missing required VERTEX symbol 'absent'", "missing referenced array");
        Bad(() => Read(Model.Replace("{0,3,6,", "{0,4,6,")), "not a multiple of three", "invalid vertex stride");
        Bad(() => Read(Model.Replace("{0,3,6,", "{0,3,12,")), "outside the LIMB", "vertex out of range");
        Bad(() => Read(Model.Replace("0,1,2, 0", "0,1,9, 0")), "outside the ST", "UV out of range");
        Bad(() => Read(Model.Replace("0,1,2, 0", "0,1,2, 1")), "multiple of four", "material stride");
        Bad(() => Read(Model.Replace("0,1,2, 0", "0,1,2, 8")), "exceeds", "material out of range");
        Bad(() => Read(Model.Replace("3,255,255, -12", "3,255,255, -8")), "UV-update prefix", "compressed UV-code mismatch");
        Bad(() => Read(Model.Replace("0,1,2, 0", "255,255,255, -16")), "First face", "material state starts unset per limb");
        string missingUv = Model.Replace("9,0,6, 3,255,255, -12", "9,0,6, 255,255,255, -16");
        Bad(() => Read(missingUv), "uninitialized state for vertex 3", "UV reuse before vertex initialization");
        Bad(() => Read(Model.Replace("-20", "-16")), "terminator", "canonical TRI terminator");
        Bad(() => Read(Model.Replace("-12", "-24")), "Unsupported/early", "unknown negative texture code");
        Bad(() => Read(Model.Replace("-12", "-20")), "Unsupported/early", "early TRI terminator");
        Bad(() => Read(Model.Replace("{0,0,0,0,0,0,-20}", "{3,0,0,0,0,0,-20}")), "canonical TRI", "nonzero terminator payload");
        Bad(() => Read(Model.Replace("{4,3,NULL", "{5,3,NULL")), "declares 5 vertices", "limb vertex count mismatch");
        Bad(() => Read(Model.Replace("{4,3,NULL", "{4,2,NULL")), "exactly ntris", "limb triangle count mismatch");
        Bad(() => Read(Model.Replace("{0,3,6,", "{0,0,6,")), "Repeated vertex", "repeated triangle vertex is explicit failure");
        Bad(() => Read(Model.Replace("1.0f", "NaN")), "finite numeric coordinate", "nonfinite named literal");
        Bad(() => Read(Model.Replace("1.0f", "1e999")), "finite", "floating overflow");
        Bad(() => Read(Model.Replace("1.0f", "1000001")), "magnitude", "huge coordinate");
        Bad(() => Read(Model.Replace("1.0f", "1e")), "Malformed numeric exponent", "malformed exponent");
        Bad(() => Read(Model.Replace("{0,3,6,", "{0,3.0f,6,")), "Int32", "integer fields do not truncate floats");
        Bad(() => Read(Model.Replace("points[4]", "points[262145]")), "dimension", "bounded dimensions");
        Bad(() => Read(Model.Replace("0,1,2, 0", "0,1,65536, 0")), "ushort", "original integer field bounds");
        Bad(() => Read(Model.Replace("points[4]", "points[4][3]")), "Expected '='", "multiple dimensions unsupported");
        Bad(() => Read(Model + "void execute(){bad();}"), "Unsupported type", "arbitrary executable C rejected");
        Bad(() => Read("#include \"bad.h\"\n" + Model), "preprocessor", "preprocessor rejected");
        Bad(() => Read(Model.Replace("1.0f", "execute()")), "Unsupported character", "function call rejected");
        Bad(() => Read(Model + "/* broken"), "Unterminated block comment", "unterminated comment");
        Bad(() => Read(Model.Substring(0, Model.Length - 3)), "Expected", "unterminated initializer");
        Bad(() => Read(Model.Replace("\"test-cloth.wms\"", "\"test-cloth.wms\n\"")), "Unterminated", "unclosed string");
        Bad(() => Read(Model.Replace("\"test-cloth.wms\"", "\"test\\ncloth.wms\"")), "Only escaped", "unsupported string escape");
        Bad(() => Read(Model.Replace("\"test-trim.wms\", NULL", "\"test-trim.wms\"")), "must end with NULL", "missing material terminator");
        Bad(() => Read(Model.Replace("\"test-trim.wms\", NULL", "\"test-trim.wms\", \"NULL\"")), "must end with NULL", "string NULL cannot impersonate null pointer");
        Bad(() => Read(Model.Replace("model_parts[3]", "model_parts[]").Replace("&limb_child,&limb_root,NULL", "&limb_child,&limb_root")), "must end with NULL", "missing model terminator");
        Bad(() => Read(Model.Replace("&limb_child,&limb_root,NULL", "&limb_child,NULL,&limb_root")), "NULL must be the final", "early pointer terminator");
        Bad(() => Read(Model.Replace("\"test-trim.wms\"", "\"TEST-CLOTH.wms\"")), "Duplicate/ambiguous", "case-ambiguous material slot");
        Bad(() => Read(Model.Replace("&limb_child,&limb_root,NULL", "&limb_root,&limb_root,NULL")), "Duplicate model limb", "duplicate model mapping");
        Bad(() => Read(Model, Skeleton.Replace("&limb_root,&limb_child", "&limb_root,&limb_root")), "Duplicate skeleton limb", "duplicate skeleton mapping");
        Bad(() => Read(Model, Skeleton.Replace("&limb_root,&limb_child", "&limb_root,&missing")), "Mismatched limb mapping", "mismatched model and skeleton mappings");
        Bad(() => Read(Model, Skeleton.Replace("&limb_root,&limb_child", "&limb_root")), "does not match mapping", "missing skeleton mapping node");
        Bad(() => Read(Model, Skeleton.Replace("{2-1,0,0}", "{9,0,0}")), "outside the skeleton", "missing child node");
        Bad(() => Read(Model, Skeleton.Replace("{2-1,0,0}", "{0,1,0}")), "contiguous", "child holes rejected");
        Bad(() => Read(Model, Skeleton.Replace("{2-1,0,0}", "{1,1,0}")), "duplicate/multiple", "duplicate child link");
        Bad(() => Read(Model, Skeleton.Replace("{0,0,0}", "{-1,0,0}")), "cycle", "cycle includes root");
        Bad(() => Read(Model, Skeleton.Replace("{2-1,0,0}", "{0,0,0}")), "missing a parent", "disconnected skeleton");
        Bad(() => Read(Model, Skeleton.Replace("SN_HEADLIMB", "UNKNOWN_FLAG")), "Unsupported skeleton flags", "unknown skeleton flag symbol");
        Bad(() => Read(Model, Skeleton.Replace("SN_HEADLIMB", "8")), "Unknown skeleton flag bits", "unknown flag bits");
        Bad(() => new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.NegativeX,
            SignedSourceAxis.PositiveZ, SourceTriangleOrder.Declared123), "signed permutation", "duplicate coordinate axes");
        Bad(() => new PlayerCoordinateConversion((SignedSourceAxis)99, SignedSourceAxis.PositiveY,
            SignedSourceAxis.PositiveZ, SourceTriangleOrder.Declared123), "Unknown signed source axis", "invalid coordinate enum");
        Bad(() => new PlayerMaterialBinding("test-cloth.wms", "x.tga", float.NaN, 1, false), "UV divisors", "nonfinite UV basis");
        Bad(() => new PlayerMaterialBinding("test-cloth.wms", "x.tga", 0.5f, 1, false), "UV divisors", "sub-unit UV divisor rejected");
        Bad(() => new PlayerMaterialBinding("test-cloth.wms", "x.tga", 1, 1, false, float.NaN), "Alpha cutoff", "nonfinite alpha cutoff rejected");
        Bad(() => new PlayerMaterialBinding("test-cloth.wms", "x.tga", 1, 1, false, 1.1f), "Alpha cutoff", "alpha cutoff bounded");
        var original = Read();
        Bad(() => BindPoseMeshBuilder.Build(original, new PlayerMaterialBinding[0], Identity),
            "missing: [test-cloth.wms, test-trim.wms]", "missing mapping list includes exact slots");
        Bad(() => BindPoseMeshBuilder.Build(original, new[] { Bindings[0], Bindings[1],
            new PlayerMaterialBinding("unknown.wms", "x.tga", 1, 1, false) }, Identity), "unknown mappings: [unknown.wms]", "unknown mapping rejected");
        Bad(() => BindPoseMeshBuilder.Build(original, new[] { Bindings[0], Bindings[0] }, Identity), "Duplicate material mapping", "duplicate map key rejected");
        string flat = Model.Replace("{3,4,0}", "{5,2,0}").Replace("{1,4,0}", "{7,2,0}");
        Bad(() => BindPoseMeshBuilder.Build(Read(flat), Bindings, Identity), "Zero-area triangle", "zero area fails instead of dropping a face");
        Bad(() => Read(new string(' ', PlayerImportLimits.SourceBytes + 1)), "8 MiB", "source byte bound");
        try { Read(Model.Replace("{0,3,6,", "{0,4,6,")); }
        catch (PlayerAssetException e)
        {
            Equal(e.Location.File, "models/tiny.h", "exact error source label");
            Equal(e.Location.Line, 7, "exact error source line");
            Equal(e.Location.Column, 6, "exact error source column");
        }
    }

    private static byte[] Tga(int bits, bool footer = false)
    {
        int size = 18 + 2 * 2 * bits / 8;
        byte[] data = new byte[size + (footer ? 26 : 0)];
        data[2] = 2; data[12] = 2; data[14] = 2; data[16] = (byte)bits;
        data[17] = (byte)(32 + (bits == 32 ? 8 : 0));
        for (int i = 18; i < size; i++) data[i] = (byte)((i - 18) * 13 + 7);
        if (footer) Array.Copy(Encoding.ASCII.GetBytes("TRUEVISION-XFILE.\0"), 0, data, size + 8, 18);
        return data;
    }
    private static void Textures()
    {
        foreach (int bits in new[] { 24, 32 })
        foreach (bool footer in new[] { false, true })
        {
            TgaImageInfo info = TgaImageInfo.Read(Tga(bits, footer), "original.tga");
            Equal(info.Width, 2, "TGA width"); Equal(info.Height, 2, "TGA height");
            Equal(info.BitsPerPixel, bits, "TGA true-color depth");
            Equal(info.PixelOffset, 18, "TGA data offset");
            Check(info.TopOrigin && !info.RightOrigin, "TGA origin metadata");
        }
        byte[] id = Tga(24).Take(18).Concat(new byte[] { 65, 66 }).Concat(Tga(24).Skip(18)).ToArray();
        id[0] = 2;
        Equal(TgaImageInfo.Read(id, "id.tga").PixelOffset, 20, "TGA image id respected");
        byte[] right = Tga(32); right[17] |= 16;
        Check(TgaImageInfo.Read(right, "right.tga").RightOrigin, "TGA right origin retained for native importer");
        byte[] badType = Tga(24); badType[2] = 10;
        Bad(() => TgaImageInfo.Read(badType, "type.tga"), "type 2", "unknown/RLE TGA type explicitly unsupported");
        byte[] palette = Tga(24); palette[1] = 1;
        Bad(() => TgaImageInfo.Read(palette, "palette.tga"), "non-paletted", "paletted TGA rejected");
        byte[] zero = Tga(24); zero[12] = 0;
        Bad(() => TgaImageInfo.Read(zero, "zero.tga"), "dimensions", "zero dimension");
        byte[] huge = Tga(24); huge[12] = 255; huge[13] = 255;
        Bad(() => TgaImageInfo.Read(huge, "huge.tga"), "dimensions", "bounded texture dimensions");
        Bad(() => TgaImageInfo.Read(Tga(32).Take(20).ToArray(), "short.tga"), "truncated", "complete pixel region required");
        byte[] desc = Tga(24); desc[17] |= 1;
        Bad(() => TgaImageInfo.Read(desc, "alpha.tga"), "descriptor", "24-bit alpha descriptor rejected");
        byte[] interleaved = Tga(32); interleaved[17] |= 64;
        Bad(() => TgaImageInfo.Read(interleaved, "interleaved.tga"), "descriptor", "interleaved TGA rejected");
        byte[] badFooter = Tga(32, true); badFooter[badFooter.Length - 1] = 5;
        Bad(() => TgaImageInfo.Read(badFooter, "bad-footer.tga"), "invalid v2 footer", "invalid footer signature");
        byte[] extension = Tga(32, true); extension[extension.Length - 26] = 1;
        Bad(() => TgaImageInfo.Read(extension, "extension.tga"), "offsets", "unsupported TGA extensions explicit");
    }

    private static PlayerImportPlan Plan(string root = null, bool authorized = true, IList<PlayerMaterialBinding> bindings = null)
    {
        return PlayerImportPlan.Create(root ?? fixtureRoot, authorized, "original-synthetic-fixtures-v1", "synthetic-two-limb",
            "models/tiny.h", "models/rig.h", Symbols, bindings ?? Bindings, Identity);
    }
    private static void FilesAndProvenance()
    {
        PlayerImportPlan plan = Plan();
        Equal(plan.Sources.Count, 2, "model and skeleton hashes");
        Equal(plan.Textures.Count, 2, "texture inputs");
        Equal(plan.Textures[0].Binding.Slot, "test-cloth.wms", "map order normalized to declared slots");
        Equal(plan.Textures[1].Info.BitsPerPixel, 24, "actual texture header used");
        Equal(AuthorizedAssetRoot.Sha256(Encoding.ASCII.GetBytes("hello")),
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", "SHA-256 hello known answer");
        byte[] copy = plan.Textures[0].CopyBytes(); copy[18] ^= 128;
        Equal(plan.Textures[0].CopyBytes()[18], Tga(32, true)[18], "captured bytes are immutable to callers");
        string manifest = plan.ProvenanceJson();
        Check(!manifest.Contains(fixtureRoot) && !manifest.Contains(Environment.UserName + "/"),
            "provenance contains relative paths, no machine root");
        using (JsonDocument json = JsonDocument.Parse(manifest))
        {
            JsonElement root = json.RootElement;
            Equal(root.GetProperty("stage").GetString(), "bind-pose-only-v1", "honest bind-pose stage");
            Equal(root.GetProperty("unityUnitsPerSourceUnit").GetInt32(), 1, "no guessed yard scale");
            Equal(root.GetProperty("animation").GetString(), "none; no WAN/ANI decoding or animation parity", "animation limitation");
            Equal(root.GetProperty("nodes")[1].GetProperty("parent").GetInt32(), 0, "manifest parent");
            Equal(root.GetProperty("nodes")[1].GetProperty("convertedLocalRestOffset")[1].GetSingle(), 1f, "manifest pivot offset");
            Equal(root.GetProperty("nodes")[0].GetProperty("sourceVertexCount").GetInt32(), 4, "manifest source vertex count");
            Equal(root.GetProperty("nodes")[0].GetProperty("convertedVertexCount").GetInt32(), 5, "manifest seam vertex count");
            Equal(root.GetProperty("materials")[1].GetProperty("flipV").GetBoolean(), true, "manifest explicit UV flip");
            foreach (JsonElement input in root.GetProperty("inputs").EnumerateArray())
            {
                string path = input.GetProperty("path").GetString();
                byte[] actual = File.ReadAllBytes(Path.Combine(fixtureRoot, path));
                Equal(input.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actual)).ToLowerInvariant(),
                    "provenance hash matches exact source bytes: " + path);
                Equal(input.GetProperty("bytes").GetInt32(), actual.Length, "provenance byte length: " + path);
            }
        }
        Equal(Plan().ProvenanceJson(), manifest, "deterministic replay manifest");
        CultureInfo previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Equal(Plan().ProvenanceJson(), manifest, "culture-independent manifest"); }
        finally { CultureInfo.CurrentCulture = previous; }
        File.WriteAllText(Path.Combine(fixtureRoot, "manifest.json"), manifest, new UTF8Encoding(false));
        Bad(() => Plan(fixtureRoot + "/does-not-exist", false), "authorization is not confirmed", "authorization gate precedes filesystem access");
        Bad(() => new AuthorizedAssetRoot("relative"), "explicit absolute", "no implicit authorized root");
        Bad(() => new AuthorizedAssetRoot(fixtureRoot + "/../fixtures"), "canonical", "root dot alias");
        var guard = new AuthorizedAssetRoot(fixtureRoot);
        Equal(guard.ExistingFile("models/tiny.h"), Path.Combine(fixtureRoot, "models", "tiny.h"), "exact confined file");
        Equal(guard.ExistingDirectory("models"), Path.Combine(fixtureRoot, "models"), "exact confined directory");
        foreach (string invalid in new[] { "../outside.h", "/outside.h", "models/../tiny.h", "models//tiny.h",
            "models\\tiny.h", "models/./tiny.h", "models/tiny.h:alias", "file://bad", "models/tiny.h.", "NUL.h" })
        {
            Bad(() => guard.ExistingFile(invalid), null, "path alias rejected: " + invalid);
        }
        Bad(() => guard.ExistingFile("models/Tiny.h"), "incorrectly cased", "wrong file casing");
        string upperCaseFixture = Path.Combine(fixtureRoot, "models", "TINY.h");
        if (File.Exists(upperCaseFixture))
        {
            Bad(() => guard.ExistingFile("models/TINY.h"), "incorrectly cased", "case-insensitive filesystem still rejects aliases");
        }
        else
        {
            File.WriteAllText(upperCaseFixture, "original different-case fixture");
            Bad(() => guard.ExistingFile("models/tiny.h"), "ambiguous", "ambiguous input casing");
            File.Delete(upperCaseFixture);
        }
        Equal(guard.NewPath("output/new-id/new.asset"), Path.Combine(fixtureRoot, "output", "new-id", "new.asset"), "confined prospective output");
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "output/owned-id"));
        Bad(() => guard.NewPath("output/owned-id"), "already exists", "existing output folder protected even if empty");
        File.WriteAllText(Path.Combine(fixtureRoot, "output/orphan.meta"), "synthetic-meta");
        Bad(() => guard.NewPath("output/orphan"), "orphan", "orphan metadata protected");
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "output/UPPER"));
        Bad(() => guard.NewPath("output/upper/new.asset"), "incorrectly cased", "output ancestor case alias");
        string link = Path.Combine(fixtureRoot, "models/linked.h");
        File.CreateSymbolicLink(link, Path.Combine(fixtureRoot, "models/tiny.h"));
        Bad(() => guard.ExistingFile("models/linked.h"), "Symlink", "file symlink rejected");
        File.Delete(link);
        string directoryLink = Path.Combine(fixtureRoot, "linked-models");
        Directory.CreateSymbolicLink(directoryLink, Path.Combine(fixtureRoot, "models"));
        Bad(() => guard.ExistingFile("linked-models/tiny.h"), "Symlink", "directory symlink rejected");
        Bad(() => new AuthorizedAssetRoot(directoryLink), "Symlink", "symlink authorized root rejected");
        Directory.Delete(directoryLink);
        Bad(() => Plan(bindings: new[] { new PlayerMaterialBinding("test-cloth.wms", "../bad.tga", 4, 4, false), Bindings[0] }),
            "path components", "texture traversal confined");
        Bad(() => Plan(bindings: new[] { new PlayerMaterialBinding("test-cloth.wms", "textures/missing.tga", 4, 4, false), Bindings[0] }),
            "Missing", "missing mapped texture fails explicitly");
        Bad(() => Plan(bindings: new[] { new PlayerMaterialBinding("test-cloth.wms", "textures/cloth.wms", 4, 4, false), Bindings[0] }),
            "explicit .tga", "WMS not silently treated as TGA");
        byte[] sourceBefore = File.ReadAllBytes(Path.Combine(fixtureRoot, "models/tiny.h"));
        File.AppendAllText(Path.Combine(fixtureRoot, "models/tiny.h"), "\n// original provenance variation");
        Check(Plan().Sources[0].Sha256 != plan.Sources[0].Sha256, "source-byte change changes provenance hash");
        File.WriteAllBytes(Path.Combine(fixtureRoot, "models/tiny.h"), sourceBefore);
    }

    private static void WriteFixtures()
    {
        Directory.CreateDirectory(fixtureRoot);
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "models"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "textures"));
        File.WriteAllText(Path.Combine(fixtureRoot, "models/tiny.h"), Model, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fixtureRoot, "models/rig.h"), Skeleton, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(fixtureRoot, "textures/cloth.tga"), Tga(32, true));
        File.WriteAllBytes(Path.Combine(fixtureRoot, "textures/trim.tga"), Tga(24));
    }
    private static AssetVector3 Add(AssetVector3 a, AssetVector3 b) { return new AssetVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    private static float Length(AssetVector3 n) { return (float)Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z); }
    private static void Check(bool passed, string label)
    {
        assertions++;
        if (!passed) throw new Exception("ASSERT " + label);
    }
    private static void Equal<T>(T actual, T expected, string label)
    {
        Check(EqualityComparer<T>.Default.Equals(actual, expected), label + ": expected " + expected + ", actual " + actual);
    }
    private static void Near(float actual, float expected, string label)
    {
        Check(!float.IsNaN(actual) && Math.Abs(actual - expected) < 0.00001f, label + ": expected " + expected + ", actual " + actual);
    }
    private static void Vector(AssetVector3 actual, float x, float y, float z, string label)
    {
        Near(actual.X, x, label + " X"); Near(actual.Y, y, label + " Y"); Near(actual.Z, z, label + " Z");
    }
    private static void Sequence(IEnumerable<int> actual, IEnumerable<int> expected, string label)
    {
        Check(actual.SequenceEqual(expected), label + ": actual [" + string.Join(",", actual) + "]");
    }
    private static void Bad(Action action, string fragment, string label, bool run = true)
    {
        if (!run) return;
        try { action(); }
        catch (PlayerAssetException error)
        {
            Check(fragment == null || error.Message.Contains(fragment), label + " wrong diagnostic: " + error.Message);
            return;
        }
        throw new Exception("ASSERT " + label + ": expected explicit PlayerAssetException" + (fragment == null ? "" : " containing " + fragment));
    }
}
