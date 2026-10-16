using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public sealed class SourceInterfaceException : InvalidOperationException
    {
        public SourceInterfaceException(string message) : base(message) { }
    }

    public sealed class SourceInterfaceInputTables
    {
        public ReadOnlyCollection<int> Raw49 { get; }
        public ReadOnlyCollection<int> Digital8 { get; }
        public ReadOnlyCollection<int> Directions { get; }

        public SourceInterfaceInputTables(IReadOnlyList<int> raw49, IReadOnlyList<int> digital8, IReadOnlyList<int> directions)
        {
            Raw49 = Copy(raw49, 256, 0, 48, "Goose raw49");
            Digital8 = Copy(digital8, 49, 0, 15, "Goose 49to8");
            Directions = Copy(directions, 49, -1, 1023, "player dir49");
            Require(Directions[24] == -1 && Digital8[24] == 0, "Original stick center must remain neutral.");
        }

        private static ReadOnlyCollection<int> Copy(IReadOnlyList<int> values, int count, int minimum, int maximum, string label)
        {
            Require(values != null && values.Count == count, label + ": wrong table length.");
            var copy = new int[count];
            for (int i = 0; i < count; i++)
            {
                Require(values[i] >= minimum && values[i] <= maximum, label + ": value outside source range.");
                copy[i] = values[i];
            }
            return Array.AsReadOnly(copy);
        }

        internal static void Require(bool value, string message)
        { if (!value) throw new SourceInterfaceException(message); }
    }

    public sealed class SourceInterfaceTexture
    {
        public string Id { get; }
        public string Path { get; }
        public string Sha256 { get; }
        public int Width { get; }
        public int Height { get; }
        public bool PointSampled { get; }
        internal SourceInterfaceTexture(string id, string path, string hash, int width, int height, bool point)
        { Id = id; Path = path; Sha256 = hash; Width = width; Height = height; PointSampled = point; }
    }

    public sealed class SourceInterfaceImage
    {
        public string Id { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int AnchorX { get; }
        public int AnchorY { get; }
        public string Source { get; }
        public string PixelsSha256 { get; }

        public SourceInterfaceImage(string id, int x, int y, int width, int height, int anchorX, int anchorY,
            string source, string pixelsSha256)
        {
            RosterGuard.Identifier(id);
            SourceInterfaceInputTables.Require(x >= 0 && y >= 0 && width > 0 && height > 0 &&
                width <= 2048 && height <= 2048 && Math.Abs(anchorX) <= 4096 && Math.Abs(anchorY) <= 4096,
                "Invalid original image bounds/anchor: " + id);
            Id = id; X = x; Y = y; Width = width; Height = height; AnchorX = anchorX; AnchorY = anchorY;
            Source = source; PixelsSha256 = pixelsSha256;
        }
    }

    public sealed class SourceInterfaceFont
    {
        public string Id { get; }
        public int Height { get; }
        public int SpaceWidth { get; }
        public int CharacterSpacing { get; }
        public ReadOnlyDictionary<char, SourceInterfaceImage> Glyphs { get; }

        public SourceInterfaceFont(string id, int height, int spaceWidth, int spacing,
            IDictionary<char, SourceInterfaceImage> glyphs)
        {
            RosterGuard.Identifier(id);
            SourceInterfaceInputTables.Require(height > 0 && height <= 128 && spaceWidth > 0 &&
                spaceWidth <= 128 && spacing >= -32 && spacing <= 32 && glyphs != null && glyphs.Count > 0,
                "Invalid source font metrics: " + id);
            var copy = new Dictionary<char, SourceInterfaceImage>();
            foreach (KeyValuePair<char, SourceInterfaceImage> glyph in glyphs)
            {
                SourceInterfaceInputTables.Require(glyph.Key >= '!' && glyph.Key <= '~' && glyph.Value != null,
                    "Invalid source font glyph: " + id);
                copy.Add(glyph.Key, glyph.Value);
            }
            Id = id; Height = height; SpaceWidth = spaceWidth; CharacterSpacing = spacing;
            Glyphs = new ReadOnlyDictionary<char, SourceInterfaceImage>(copy);
        }

        public SourceInterfaceImage Glyph(char character)
        {
            SourceInterfaceImage image;
            if (!Glyphs.TryGetValue(character, out image))
                throw new SourceInterfaceException("Original font " + Id + " has no glyph U+" + ((int)character).ToString("X4") +
                    "; no system-font or replacement-glyph fallback is permitted.");
            return image;
        }
    }

    public sealed class SourceInterfacePlaySlot
    {
        public string Name { get; }
        public string PlaySymbol { get; }
        public string NormalGraphic { get; }
        public string FlippedGraphic { get; }
        internal SourceInterfacePlaySlot(string name, string play, string normal, string flipped)
        { Name = name; PlaySymbol = play; NormalGraphic = normal; FlippedGraphic = flipped; }
    }

    public readonly struct SourceInterfacePageSlot
    {
        public string Table { get; }
        public int Index { get; }
        internal SourceInterfacePageSlot(string table, int index) { Table = table; Index = index; }
    }

    public readonly struct SourceInterfaceVertex
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float S { get; }
        public float T { get; }
        internal SourceInterfaceVertex(float x, float y, float z, float s, float t)
        { X = x; Y = y; Z = z; S = s; T = t; }
    }

    public sealed class SourceInterfaceTriangle
    {
        public string Texture { get; }
        public ReadOnlyCollection<SourceInterfaceVertex> Vertices { get; }
        internal SourceInterfaceTriangle(string texture, SourceInterfaceVertex[] vertices)
        { Texture = texture; Vertices = Array.AsReadOnly((SourceInterfaceVertex[])vertices.Clone()); }
    }

    public sealed class SourceInterfaceData
    {
        public const string SourceRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string SourceTree = "a7e0352125d46075b7d98e87253ab89b9160a4c7";
        public const string ToolsRevision = "1792293aaaea00a919b59e8c05716978e85b4333";
        public string ContentSha256 { get; }
        public SourceInterfaceInputTables Input { get; }
        public ReadOnlyDictionary<string, SourceInterfaceTexture> Textures { get; }
        public ReadOnlyDictionary<string, SourceInterfaceImage> Images { get; }
        public ReadOnlyDictionary<string, SourceInterfaceFont> Fonts { get; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<SourceInterfacePlaySlot>> PlaySlots { get; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<SourceInterfacePageSlot>> Pages { get; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<SourceInterfaceTriangle>> Planes { get; }
        public ReadOnlyCollection<string> TeamAbbreviations { get; }
        public ReadOnlyCollection<string> Qualifications { get; }

        private SourceInterfaceData(string hash, SourceInterfaceInputTables input,
            Dictionary<string, SourceInterfaceTexture> textures, Dictionary<string, SourceInterfaceImage> images,
            Dictionary<string, SourceInterfaceFont> fonts, Dictionary<string, ReadOnlyCollection<SourceInterfacePlaySlot>> slots,
            Dictionary<string, ReadOnlyCollection<SourceInterfacePageSlot>> pages,
            Dictionary<string, ReadOnlyCollection<SourceInterfaceTriangle>> planes, string[] teams, string[] qualifications)
        {
            ContentSha256 = hash; Input = input;
            Textures = new ReadOnlyDictionary<string, SourceInterfaceTexture>(textures);
            Images = new ReadOnlyDictionary<string, SourceInterfaceImage>(images);
            Fonts = new ReadOnlyDictionary<string, SourceInterfaceFont>(fonts);
            PlaySlots = new ReadOnlyDictionary<string, ReadOnlyCollection<SourceInterfacePlaySlot>>(slots);
            Pages = new ReadOnlyDictionary<string, ReadOnlyCollection<SourceInterfacePageSlot>>(pages);
            Planes = new ReadOnlyDictionary<string, ReadOnlyCollection<SourceInterfaceTriangle>>(planes);
            TeamAbbreviations = Array.AsReadOnly(teams); Qualifications = Array.AsReadOnly(qualifications);
        }

        public SourceInterfaceImage Image(string symbol)
        {
            SourceInterfaceImage image;
            if (!Images.TryGetValue(symbol, out image))
                throw new SourceInterfaceException("Missing actual original interface image: " + symbol + ".");
            return image;
        }

        public SourceInterfaceFont Font(string symbol)
        {
            SourceInterfaceFont font;
            if (!Fonts.TryGetValue(symbol, out font))
                throw new SourceInterfaceException("Missing actual original bitmap font: " + symbol + ".");
            return font;
        }

        public SourceInterfacePlaySlot PageSlot(SourcePlaybookKind kind, int page, int index, int sourceTeam)
        {
            SourceInterfaceInputTables.Require(page >= 0 && page < (kind == SourcePlaybookKind.Offense ? 5 : 2) &&
                index >= 0 && index < 9 && sourceTeam >= 0 && sourceTeam < 31, "Invalid original play page identity.");
            SourceInterfacePageSlot reference = Pages[(kind == SourcePlaybookKind.Offense ? "offense" : "defense") + ":" + page][index];
            if (kind == SourcePlaybookKind.Offense && page == 0 && index < 3)
                return PlaySlots["team_slots"][sourceTeam * 3 + index];
            return PlaySlots[reference.Table][reference.Index];
        }

        public byte[] RequireTextureBytes(string id, byte[] bytes)
        {
            SourceInterfaceTexture texture;
            SourceInterfaceInputTables.Require(Textures.TryGetValue(id, out texture) && bytes != null &&
                bytes.Length == (long)texture.Width * texture.Height * 4 && RosterGuard.Sha256(bytes) == texture.Sha256,
                "Missing/changed actual original interface texture: " + id);
            return (byte[])bytes.Clone();
        }

        public static SourceInterfaceData Read(byte[] bytes, string expectedSha256)
        {
            RosterGuard.Hex(expectedSha256, 64, "Trusted original interface content pin");
            SourceInterfaceInputTables.Require(bytes != null && bytes.Length > 0 && bytes.Length <= 8 * 1024 * 1024 &&
                RosterGuard.Sha256(bytes) == expectedSha256, "Original interface content pin mismatch.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(bytes), "original interface");
            SourceInterfaceInputTables.Require(root.Text("schema") == "original-source-interface/v1" &&
                root.Text("source_revision") == SourceRevision && root.Text("source_tree") == SourceTree &&
                root.Text("tools_revision") == ToolsRevision &&
                root.Text("profile") == "normal-release-57hz-authored-interface", "Unknown original interface profile.");
            var inputIdentities = new HashSet<string>(StringComparer.Ordinal);
            object[] inputs = root.Array("inputs");
            SourceInterfaceInputTables.Require(inputs.Length == 36, "Original interface source pin inventory changed.");
            foreach (object raw in inputs)
            {
                RosterObject input = RosterJson.Object(raw, "interface source pin");
                string repository = input.Text("repository"), path = input.Path("path");
                SourceInterfaceInputTables.Require((repository == "source" || repository == "tools") &&
                    inputIdentities.Add(repository + "/" + path), "Duplicate/unknown interface source identity.");
                RosterGuard.Hex(input.Text("git_blob"), 40, "Original interface Git blob");
                RosterGuard.Hex(input.Text("sha256"), 64, "Original interface input SHA256"); input.Done();
            }
            RosterObject controls = root.Object("quantization");
            var tables = new SourceInterfaceInputTables(controls.Integers("raw49", 256, 0, 48),
                controls.Integers("digital8", 49, 0, 15), controls.Integers("directions", 49, -1, 1023)); controls.Done();
            var textures = new Dictionary<string, SourceInterfaceTexture>(StringComparer.Ordinal);
            foreach (object raw in root.Array("textures"))
            {
                RosterObject texture = RosterJson.Object(raw, "interface texture");
                string id = texture.Text("id"), path = texture.Path("path"), hash = texture.Text("sha256");
                RosterGuard.Identifier(id); RosterGuard.Hex(hash, 64, "Interface texture content pin");
                int width = texture.Integer("width", 1, 4096), height = texture.Integer("height", 1, 4096);
                string filter = texture.Text("filter");
                SourceInterfaceInputTables.Require((filter == "point" || filter == "bilinear") && !textures.ContainsKey(id),
                    "Invalid/duplicate interface texture sampling/identity.");
                textures.Add(id, new SourceInterfaceTexture(id, path, hash, width, height, filter == "point")); texture.Done();
            }
            SourceInterfaceInputTables.Require(textures.Count == 3 && textures.ContainsKey("interface") &&
                textures.ContainsKey("playsel8") && textures.ContainsKey("psarrow1"), "Missing original interface texture identity.");
            var images = new Dictionary<string, SourceInterfaceImage>(StringComparer.Ordinal);
            SourceInterfaceTexture atlas = textures["interface"];
            foreach (object raw in root.Array("images"))
            {
                RosterObject image = RosterJson.Object(raw, "interface image");
                string id = image.Text("id"), hash = image.Text("pixels_sha256");
                RosterGuard.Hex(hash, 64, "Original image pixel identity");
                var record = new SourceInterfaceImage(id, image.Integer("x", 0, 4096), image.Integer("y", 0, 4096),
                    image.Integer("width", 1, 2048), image.Integer("height", 1, 2048),
                    image.Integer("anchor_x", -4096, 4096), image.Integer("anchor_y", -4096, 4096), image.Path("source"), hash);
                SourceInterfaceInputTables.Require(record.X + record.Width <= atlas.Width && record.Y + record.Height <= atlas.Height &&
                    !images.ContainsKey(id), "Original image exceeds actual atlas bounds or has duplicate identity: " + id);
                images.Add(id, record); image.Done();
            }
            SourceInterfaceInputTables.Require(images.Count > 100 && images.Count <= 2048, "Incomplete original interface image catalog.");
            var fonts = new Dictionary<string, SourceInterfaceFont>(StringComparer.Ordinal);
            foreach (object raw in root.Array("fonts"))
            {
                RosterObject font = RosterJson.Object(raw, "original bitmap font");
                string id = font.Text("id");
                int height = font.Integer("height", 1, 128), space = font.Integer("space", 1, 128), spacing = font.Integer("spacing", -32, 32);
                var glyphs = new Dictionary<char, SourceInterfaceImage>();
                foreach (object glyph in font.Array("glyphs"))
                {
                    object[] pair = RosterJson.Array(glyph);
                    SourceInterfaceInputTables.Require(pair.Length == 2, "Invalid original glyph record.");
                    char c = (char)RosterJson.Integer(pair[0], 33, 126);
                    string image = RosterJson.Text(pair[1], 128, false);
                    SourceInterfaceInputTables.Require(images.ContainsKey(image) && !glyphs.ContainsKey(c), "Missing/duplicate original glyph mapping.");
                    glyphs.Add(c, images[image]);
                }
                SourceInterfaceInputTables.Require(!fonts.ContainsKey(id), "Duplicate original font.");
                fonts.Add(id, new SourceInterfaceFont(id, height, space, spacing, glyphs)); font.Done();
            }
            SourceInterfaceInputTables.Require(fonts.Count == 6 && fonts.ContainsKey("bast8t") && fonts.ContainsKey("bast10") &&
                fonts.ContainsKey("bast13") && fonts.ContainsKey("bast18") && fonts.ContainsKey("bast25") && fonts.ContainsKey("clock12"),
                "Original source fonts cannot be silently omitted.");
            var slots = new Dictionary<string, ReadOnlyCollection<SourceInterfacePlaySlot>>(StringComparer.Ordinal);
            RosterObject rawSlots = root.Object("slots");
            ReadSlots(rawSlots, "off_slots", 41, slots); ReadSlots(rawSlots, "def_slots", 22, slots);
            ReadSlots(rawSlots, "team_slots", 93, slots); ReadSlots(rawSlots, "extra_slots", 1, slots); rawSlots.Done();
            var pages = new Dictionary<string, ReadOnlyCollection<SourceInterfacePageSlot>>(StringComparer.Ordinal);
            foreach (object raw in root.Array("pages"))
            {
                RosterObject page = RosterJson.Object(raw, "original play page");
                string side = page.Text("side"); int index = page.Integer("page", 0, 4);
                SourceInterfaceInputTables.Require(side == "offense" || side == "defense" && index < 2, "Invalid original play-page side/index.");
                object[] rawPage = page.Array("slots");
                SourceInterfaceInputTables.Require(rawPage.Length == 9, "Original play pages have nine slots.");
                var copy = new SourceInterfacePageSlot[9];
                for (int i = 0; i < copy.Length; i++)
                {
                    RosterObject slot = RosterJson.Object(rawPage[i], "original play-page reference");
                    string table = slot.Text("table"); int ordinal = slot.Integer("index", 0, 92);
                    SourceInterfaceInputTables.Require(slots.ContainsKey(table) && ordinal < slots[table].Count, "Missing original page-slot reference.");
                    copy[i] = new SourceInterfacePageSlot(table, ordinal); slot.Done();
                }
                string key = side + ":" + index;
                SourceInterfaceInputTables.Require(!pages.ContainsKey(key), "Duplicate original play page.");
                pages.Add(key, Array.AsReadOnly(copy)); page.Done();
            }
            SourceInterfaceInputTables.Require(pages.Count == 7, "Original play-page inventory changed.");
            string[] teams = root.Strings("team_abbreviations", 31, 31);
            var planes = new Dictionary<string, ReadOnlyCollection<SourceInterfaceTriangle>>(StringComparer.Ordinal);
            foreach (object raw in root.Array("planes"))
            {
                RosterObject plane = RosterJson.Object(raw, "original interface plane");
                string id = plane.Text("id"); RosterGuard.Identifier(id);
                var triangles = new List<SourceInterfaceTriangle>();
                foreach (object rawTriangle in plane.Array("triangles"))
                {
                    RosterObject triangle = RosterJson.Object(rawTriangle, "original plane triangle");
                    string texture = triangle.Text("texture");
                    SourceInterfaceInputTables.Require(textures.ContainsKey(texture), "Original plane references missing texture.");
                    object[] vertices = triangle.Array("vertices");
                    SourceInterfaceInputTables.Require(vertices.Length == 3, "Original plane triangle requires three corners.");
                    var copy = new SourceInterfaceVertex[3];
                    for (int i = 0; i < 3; i++)
                    {
                        object[] vertex = RosterJson.Array(vertices[i]);
                        SourceInterfaceInputTables.Require(vertex.Length == 5, "Invalid original plane vertex/UV shape.");
                        copy[i] = new SourceInterfaceVertex((float)RosterJson.Number(vertex[0], -4096, 4096),
                            (float)RosterJson.Number(vertex[1], -4096, 4096), (float)RosterJson.Number(vertex[2], -4096, 4096),
                            (float)RosterJson.Number(vertex[3], -64, 64), (float)RosterJson.Number(vertex[4], -64, 64));
                    }
                    triangles.Add(new SourceInterfaceTriangle(texture, copy)); triangle.Done();
                }
                SourceInterfaceInputTables.Require(triangles.Count > 0 && triangles.Count <= 2048 && !planes.ContainsKey(id),
                    "Invalid/duplicate original plane.");
                planes.Add(id, Array.AsReadOnly(triangles.ToArray())); plane.Done();
            }
            SourceInterfaceInputTables.Require(planes.Count == 104, "Missing original play diagram/cursor/plate geometry.");
            RosterGuard.Hex(root.Text("original_plane_header_sha256"), 64, "Original generated plane header pin");
            string[] qualifications = root.Strings("qualifications", 6, 6); root.Done();
            foreach (ReadOnlyCollection<SourceInterfacePlaySlot> table in slots.Values)
                foreach (SourceInterfacePlaySlot slot in table)
                {
                    RequireGraphic(slot.NormalGraphic, images, planes);
                    RequireGraphic(slot.FlippedGraphic, images, planes);
                }
            return new SourceInterfaceData(expectedSha256, tables, textures, images, fonts, slots, pages, planes, teams, qualifications);
        }

        private static void ReadSlots(RosterObject root, string name, int expected,
            Dictionary<string, ReadOnlyCollection<SourceInterfacePlaySlot>> result)
        {
            object[] raw = root.Array(name);
            SourceInterfaceInputTables.Require(raw.Length == expected, "Original slot count changed: " + name);
            var copy = new SourceInterfacePlaySlot[raw.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                RosterObject slot = RosterJson.Object(raw[i], "original play slot");
                copy[i] = new SourceInterfacePlaySlot(slot.Text("name"), slot.NullableText("play"),
                    slot.NullableText("normal"), slot.NullableText("flipped")); slot.Done();
                if (copy[i].PlaySymbol != null) RosterGuard.Identifier(copy[i].PlaySymbol);
            }
            result.Add(name, Array.AsReadOnly(copy));
        }

        private static void RequireGraphic(string symbol, Dictionary<string, SourceInterfaceImage> images,
            Dictionary<string, ReadOnlyCollection<SourceInterfaceTriangle>> planes)
        {
            SourceInterfaceInputTables.Require(symbol == null || images.ContainsKey(symbol) || planes.ContainsKey(symbol),
                "Missing actual original play-selection graphic: " + symbol);
        }
    }
}
