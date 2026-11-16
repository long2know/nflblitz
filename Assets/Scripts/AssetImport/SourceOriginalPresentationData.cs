using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class SourceOriginalRaster
    {
        private readonly byte[] rgba;
        public string Id { get; }
        public string Kind { get; }
        public int Slot { get; }
        public int Width { get; }
        public int Height { get; }
        public int AnchorX { get; }
        public int AnchorY { get; }
        public string Sha256 { get; }
        public string SourcePath { get; }
        public string SourceGitBlob { get; }
        public string Representation { get; }
        public string TextureId { get { return Kind == "shadow" ? "original/" + Id : "hud/original/" + Id; } }
        internal SourceOriginalRaster(RosterObject value, Func<string, byte[]> read, Dictionary<string, byte[]> sources)
        {
            Id = value.Text("id"); Kind = value.Text("kind"); Slot = value.Integer("slot", -1, 3);
            Width = value.Integer("width", 1, 2048); Height = value.Integer("height", 1, 2048);
            AnchorX = value.Integer("anchorX", -2048, 2048); AnchorY = value.Integer("anchorY", -2048, 2048);
            string path = value.Path("path"); Sha256 = value.Text("sha256", 64);
            SourcePath = value.Path("sourcePath"); SourceGitBlob = value.Text("sourceGitBlob", 40);
            string sourceHash = value.Text("sourceSha256", 64);
            Representation = value.Text("representation", 256); value.Done();
            byte[] bytes;
            RosterGuard.Require(sources.TryGetValue(SourcePath, out bytes) && RosterGuard.Sha256(bytes) == sourceHash,
                "Original raster has no exact authenticated authoring input.");
            byte[] pixels = read(path);
            if (SourcePath.EndsWith(".TGA", StringComparison.Ordinal))
                RosterGuard.Require(RosterGuard.Sha256(SourceOriginalPresentationData.DecodeTga(bytes, SourcePath)) ==
                    RosterGuard.Sha256(pixels), "Original shadow pixels differ from the actual authenticated TGA.");
            RosterGuard.Identifier(Id);
            RosterGuard.Require((Kind == "shadow" ? Slot >= 0 && Width == 32 && Height == 32 : Kind == "image" && Slot == -1)
                && pixels.Length == Width * Height * 4 && RosterGuard.Sha256(pixels) == Sha256,
                "Original presentation raster size/content/domain differs: " + Id);
            rgba = (byte[])pixels.Clone();
        }
        public byte[] CopyRgba() { return (byte[])rgba.Clone(); }
    }

    public sealed class SourceOriginalPresentationData
    {
        public const string ManifestSha256 = "7f85b1848a61ba68f1e3e3e74f57d4ad6a7c517078f9b7e59d55a112acaa3f8d";
        public const string RecordsManifestSha256 = "a0bcad9c66d87fd596dd56c50df64e0124b27d4bd854adb69d3ae5a275200b22";
        public const string PrematchManifestSha256 = "a2ddcc4c6e346364e1b33ef9f50c28373507a071a381b7dd9188fb38a3d86433";
        public bool HasRecordsArt { get; }
        public bool HasPrematchArt { get; }
        private readonly Dictionary<string, SourceOriginalRaster> images;
        public ReadOnlyCollection<SourceOriginalRaster> Rasters { get; }
        public ReadOnlyCollection<SourceOriginalRaster> Shadows { get; }
        public ReadOnlyCollection<string> TeamWins { get; }
        private static readonly string[] ShadowPaths = {
            "img3d/PSSHOULD.TGA", "img3d/PSLIMB.TGA", "img3d/PSTORSO.TGA", "img3d/PSHAND.TGA"
        };
        private static readonly string[] ShadowBlobs = {
            "b05993dc691d1c6d7632c26fa37092d09ebba4d9", "a589bfe2eb87dea0fff00421fdd81c25aad65ea4",
            "cbe1aebd1249da69cf6136e1072c20dc532901c2", "e6f69144c2ff170d25afcc0398c0f2d68ed2c700"
        };

        private SourceOriginalPresentationData(List<SourceOriginalRaster> rasters, string[] wins, bool records, bool prematch)
        {
            HasRecordsArt = records; HasPrematchArt = prematch;
            Rasters = rasters.AsReadOnly();
            images = new Dictionary<string, SourceOriginalRaster>(StringComparer.Ordinal);
            var shadows = new SourceOriginalRaster[4];
            foreach (SourceOriginalRaster raster in rasters)
                if (raster.Kind == "shadow")
                {
                    RosterGuard.Require(shadows[raster.Slot] == null &&
                        raster.SourcePath == ShadowPaths[raster.Slot] && raster.SourceGitBlob == ShadowBlobs[raster.Slot],
                        "The four original shadow materials must occupy their exact source slots.");
                    shadows[raster.Slot] = raster;
                }
                else images.Add(raster.Id, raster);
            foreach (SourceOriginalRaster shadow in shadows)
                RosterGuard.Require(shadow != null, "A complete original shadow set is required; no missing material success.");
            RosterGuard.Require(images.Count == (prematch ? 303 : records ? 297 : 41) && wins.Length == 31,
                "The exact selected original cabinet/record image family is required.");
            foreach (string win in wins) Image(win);
            for (int i = 1; i <= 7; i++) Image("cheerpg" + i);
            Image("halfstat"); Image("finalstat"); Image("win_win");
            if (records)
                foreach (string id in new[] { "bckgrnd", "letters", "mpin", "mbg", "initplq1", "pinplq1",
                    "ynplq1", "mcreate", "menter", "mselect", "l_a", "r_a", "l_pdel", "r_pdel" }) Image(id);
            if (prematch)
                foreach (string id in new[] { "statpl_r", "statfade", "sstatplr", "mrecnf", "mrecf", "gridbox" }) Image(id);
            Shadows = Array.AsReadOnly(shadows); TeamWins = Array.AsReadOnly(wins);
        }

        public SourceOriginalRaster Image(string id)
        {
            SourceOriginalRaster image;
            if (!images.TryGetValue(id, out image))
                throw new PlayerAssetException("Missing authenticated original cabinet image: " + id);
            return image;
        }

        public static SourceOriginalPresentationData Read(byte[] manifest, Func<string, byte[]> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            string hash = manifest == null ? null : RosterGuard.Sha256(manifest);
            bool prematch = hash == PrematchManifestSha256;
            bool records = hash == RecordsManifestSha256 || prematch;
            RosterGuard.Require(manifest != null && manifest.Length <= 256 * 1024 &&
                (hash == ManifestSha256 || records), "The exact complete-original presentation package is required.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(manifest), "original presentation");
            RosterGuard.Require(root.Text("schema") == "original-complete-presentation/v1" &&
                root.Text("profile") == (prematch ? SourceTeamResourceProfile.CompleteOriginalPrematchStableId :
                    records ? SourceTeamResourceProfile.CompleteOriginalRecordsStableId :
                    SourceTeamResourceProfile.CompleteOriginalStableId) &&
                root.Text("sourceRevision") == SourceAnimationLibrary.SourceRevision,
                "Original presentation profile/source identity changed.");
            var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (object raw in root.Array("sources"))
            {
                RosterObject entry = RosterJson.Object(raw, "original presentation source");
                string path = entry.Path("path"), source = entry.Path("sourcePath");
                string blob = entry.Text("gitBlob", 40), inputHash = entry.Text("sha256", 64);
                int size = entry.Integer("bytes", 1, 16 * 1024 * 1024); entry.Done();
                byte[] bytes = read(path);
                RosterGuard.Require(bytes != null && bytes.Length == size && RosterGuard.Sha256(bytes) == inputHash &&
                    GitBlob(bytes) == blob, "Original presentation input identity differs: " + source);
                sources.Add(source, bytes);
            }
            RosterGuard.Require(sources.Count == (prematch ? 17 : records ? 14 : 9), "The exact source/art preparation closure is required.");
            var rasters = new List<SourceOriginalRaster>();
            foreach (object raw in root.Array("rasters"))
            {
                RosterObject entry = RosterJson.Object(raw, "original presentation raster");
                rasters.Add(new SourceOriginalRaster(entry, read, sources));
            }
            string[] wins = root.Strings("teamWins", 31, 31);
            root.Strings("qualifications", 2, 2); root.Done();
            return new SourceOriginalPresentationData(rasters, wins, records, prematch);
        }

        internal static byte[] DecodeTga(byte[] bytes, string label)
        {
            TgaImageInfo info = TgaImageInfo.Read(bytes, label);
            var pixels = new byte[info.Width * info.Height * 4];
            for (int y = 0; y < info.Height; y++)
                for (int x = 0; x < info.Width; x++)
                {
                    int from = info.PixelOffset + (y * info.Width + x) * 4;
                    int to = ((info.TopOrigin ? info.Height - 1 - y : y) * info.Width +
                        (info.RightOrigin ? info.Width - 1 - x : x)) * 4;
                    pixels[to] = bytes[from + 2]; pixels[to + 1] = bytes[from + 1];
                    pixels[to + 2] = bytes[from]; pixels[to + 3] = bytes[from + 3];
                }
            return pixels;
        }

        private static string GitBlob(byte[] bytes)
        {
            byte[] header = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            var blob = new byte[header.Length + bytes.Length];
            Buffer.BlockCopy(header, 0, blob, 0, header.Length); Buffer.BlockCopy(bytes, 0, blob, header.Length, bytes.Length);
            using (SHA1 sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(blob)).Replace("-", "").ToLowerInvariant();
        }
    }
}
