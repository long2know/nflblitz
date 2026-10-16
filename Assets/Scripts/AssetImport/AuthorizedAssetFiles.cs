using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class AuthorizedAssetRoot
    {
        private readonly string root;

        public AuthorizedAssetRoot(string explicitlySuppliedRoot)
        {
            if (string.IsNullOrWhiteSpace(explicitlySuppliedRoot) || !Path.IsPathRooted(explicitlySuppliedRoot))
                throw new PlayerAssetException("Supply an explicit absolute authorized asset root; no default archive/cache is used.");
            string supplied = explicitlySuppliedRoot.TrimEnd(Path.DirectorySeparatorChar);
            root = Path.GetFullPath(explicitlySuppliedRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (root.Length == 0 || !string.Equals(root, supplied, StringComparison.Ordinal))
                throw new PlayerAssetException("Authorized root must be canonical, not a filesystem root, relative path, or dot-segment alias.");
            var ancestors = new List<DirectoryInfo>();
            DirectoryInfo directory = new DirectoryInfo(root);
            while (directory.Parent != null) { ancestors.Add(directory); directory = directory.Parent; }
            for (int i = ancestors.Count - 1; i >= 0; i--)
                CheckedEntry(ancestors[i].Parent.FullName, ancestors[i].Name, true);
        }

        public static void ValidateRelativePath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > 512 || Path.IsPathRooted(relative) || relative.IndexOf('\\') >= 0)
                throw new PlayerAssetException("Use a root-relative portable path with '/' separators (1..512 characters).");
            foreach (string part in relative.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.Length > 128 || part.EndsWith(".", StringComparison.Ordinal))
                    throw new PlayerAssetException("Empty/dot/trailing-dot/oversized path components are forbidden: '" + relative + "'.");
                foreach (char c in part)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.')
                        throw new PlayerAssetException("Non-portable path or alias syntax is forbidden: '" + relative + "'.");
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9'))
                    throw new PlayerAssetException("Reserved device alias is forbidden: '" + relative + "'.");
            }
        }

        public string ExistingFile(string relative)
        {
            ValidateRelativePath(relative);
            string current = root;
            string[] parts = relative.Split('/');
            for (int i = 0; i < parts.Length; i++)
                current = CheckedEntry(current, parts[i], i < parts.Length - 1);
            return current;
        }

        public string ExistingDirectory(string relative)
        {
            ValidateRelativePath(relative);
            string current = root;
            foreach (string part in relative.Split('/')) current = CheckedEntry(current, part, true);
            return current;
        }

        // Also used by the editor before it reserves a new, non-overwriting destination.
        public string NewPath(string relative)
        {
            ValidateRelativePath(relative);
            string current = root;
            string[] parts = relative.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string next = Path.Combine(current, parts[i]);
                if (Directory.Exists(current))
                {
                    List<string> matches = Matches(current, parts[i]);
                    if (matches.Count > 0)
                    {
                        if (i == parts.Length - 1)
                            throw new PlayerAssetException("Output already exists or has a casing alias: '" + relative + "'. Choose an unused stable id; nothing will be overwritten.");
                        next = CheckedEntry(current, parts[i], true);
                    }
                    else if (i == parts.Length - 1 && Matches(current, parts[i] + ".meta").Count > 0)
                        throw new PlayerAssetException("An orphan output .meta already exists for '" + relative + "'.");
                }
                current = next;
            }
            return current;
        }

        public byte[] ReadBytes(string relative, int maximumBytes)
        {
            string path = ExistingFile(relative);
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 1 || stream.Length > maximumBytes)
                    throw new PlayerAssetException("Input '" + relative + "' is empty or exceeds " + maximumBytes + " bytes.");
                bytes = new byte[(int)stream.Length];
                int count = 0;
                while (count < bytes.Length)
                {
                    int read = stream.Read(bytes, count, bytes.Length - count);
                    if (read == 0) throw new PlayerAssetException("Input changed/truncated while reading '" + relative + "'.");
                    count += read;
                }
                if (stream.ReadByte() != -1) throw new PlayerAssetException("Input grew while reading '" + relative + "'.");
            }
            ExistingFile(relative);
            return bytes;
        }

        public string ReadUtf8(string relative, int maximumBytes)
        {
            return DecodeUtf8(ReadBytes(relative, maximumBytes), relative);
        }

        public static string DecodeUtf8(byte[] bytes, string relative)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { throw new PlayerAssetException("Input '" + relative + "' is not valid UTF-8/ASCII source."); }
        }

        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var result = new StringBuilder(64);
                foreach (byte value in sha.ComputeHash(bytes)) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        private static string CheckedEntry(string parent, string exactName, bool directory)
        {
            List<string> matches = Matches(parent, exactName);
            if (matches.Count != 1 || !string.Equals(Path.GetFileName(matches[0]), exactName, StringComparison.Ordinal))
                throw new PlayerAssetException("Missing, ambiguous, or incorrectly cased path component '" + exactName + "'.");
            string entry = matches[0];
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new PlayerAssetException("Symlink/junction/reparse alias is forbidden: '" + exactName + "'.");
            if (((attributes & FileAttributes.Directory) != 0) != directory)
                throw new PlayerAssetException("Unexpected file/directory component '" + exactName + "'.");
            return entry;
        }

        private static List<string> Matches(string parent, string name)
        {
            if (!Directory.Exists(parent)) throw new PlayerAssetException("Required parent directory is missing.");
            var result = new List<string>();
            int entries = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
            {
                if (++entries > 100000) throw new PlayerAssetException("Directory exceeds the bounded path-inspection limit.");
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)) result.Add(entry);
            }
            return result;
        }
    }

    public sealed class TgaImageInfo
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int BitsPerPixel;
        public readonly int PixelOffset;
        public readonly bool TopOrigin;
        public readonly bool RightOrigin;

        private TgaImageInfo(int width, int height, int bits, int offset, int descriptor)
        {
            Width = width; Height = height; BitsPerPixel = bits; PixelOffset = offset;
            TopOrigin = (descriptor & 32) != 0; RightOrigin = (descriptor & 16) != 0;
        }

        public static TgaImageInfo Read(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length < 18 || bytes.Length > PlayerImportLimits.TextureBytes)
                throw new PlayerAssetException("TGA '" + label + "' has an invalid/bounded header length.");
            if (bytes[1] != 0 || bytes[2] != 2)
                throw new PlayerAssetException("TGA '" + label + "': bind-pose-v1 accepts only uncompressed, non-paletted true-color type 2; no WMS/WAN decoder is implied.");
            for (int i = 3; i < 8; i++)
                if (bytes[i] != 0) throw new PlayerAssetException("TGA '" + label + "' has unsupported color-map metadata.");
            int width = bytes[12] | bytes[13] << 8, height = bytes[14] | bytes[15] << 8;
            int bits = bytes[16], descriptor = bytes[17], alpha = descriptor & 15;
            if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16777216)
                throw new PlayerAssetException("TGA '" + label + "' dimensions exceed limits or are zero.");
            if ((bits != 24 && bits != 32) || (descriptor & 192) != 0 || (bits == 24 ? alpha != 0 : alpha != 0 && alpha != 8))
                throw new PlayerAssetException("TGA '" + label + "' requires 24/32-bit pixels and supported descriptor bits.");
            int offset = 18 + bytes[0];
            long end = offset + (long)width * height * (bits / 8);
            if (end > bytes.Length) throw new PlayerAssetException("TGA '" + label + "' pixel region is truncated.");
            if (end != bytes.Length)
            {
                byte[] signature = Encoding.ASCII.GetBytes("TRUEVISION-XFILE.\0");
                if (bytes.Length - end != 26) throw new PlayerAssetException("TGA '" + label + "' has unsupported trailing/extension data.");
                for (int i = 0; i < 8; i++)
                    if (bytes[(int)end + i] != 0) throw new PlayerAssetException("TGA '" + label + "' extension/developer offsets are unsupported.");
                for (int i = 0; i < signature.Length; i++)
                    if (bytes[(int)end + 8 + i] != signature[i]) throw new PlayerAssetException("TGA '" + label + "' has an invalid v2 footer.");
            }
            return new TgaImageInfo(width, height, bits, offset, descriptor);
        }
    }
}
