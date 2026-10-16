using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public sealed class SourceAnimationLibrary : ISourceAnimationLibrary
    {
        public const string SourceRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const string ConverterRevision = "1792293aaaea00a919b59e8c05716978e85b4333";
        public const double RootScale = 0.12898;
        private readonly Dictionary<string, ISourceAnimationClip> clips;
        public ReadOnlyCollection<string> SequenceNames { get; private set; }
        public int FrameCount { get; private set; }
        public string InventorySha256 { get; private set; }

        private SourceAnimationLibrary(Dictionary<string, ISourceAnimationClip> clips, string inventoryHash)
        {
            this.clips = clips;
            var names = new List<string>(clips.Keys);
            names.Sort(StringComparer.Ordinal);
            SequenceNames = names.AsReadOnly();
            foreach (ISourceAnimationClip clip in clips.Values) FrameCount += clip.SelectedSourceFrames.Count;
            InventorySha256 = inventoryHash;
        }

        public ISourceAnimationClip GetClip(string sequenceName)
        {
            ISourceAnimationClip clip;
            if (sequenceName == null || !clips.TryGetValue(sequenceName, out clip))
                throw new PlayerAssetException("Original animation sequence '" + sequenceName + "' is missing from the imported library.");
            return clip;
        }

        // The caller supplies bytes explicitly: an Editor-authorized root or bundled TextAssets, never a runtime path.
        public static SourceAnimationLibrary Read(byte[] inventoryBytes, Func<string, byte[]> readClipBytes)
        {
            if (readClipBytes == null) throw new ArgumentNullException(nameof(readClipBytes));
            Inventory inventory = Deserialize<Inventory>(inventoryBytes, "animation inventory", 1024 * 1024);
            Require(inventory != null && inventory.schema == "original-wan-export-inventory/v1" &&
                inventory.clips != null && inventory.clips.Length > 0 && inventory.clips.Length <= 512,
                "Expected a bounded original-wan-export-inventory/v1.");
            var result = new Dictionary<string, ISourceAnimationClip>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceIdentities = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (InventoryEntry entry in inventory.clips)
            {
                Require(entry != null, "Null animation inventory entry.");
                Identifier(entry.runtime_name, "inventory sequence");
                AuthorizedAssetRoot.ValidateRelativePath(entry.path);
                Require(entry.path.EndsWith(".json", StringComparison.Ordinal) && paths.Add(entry.path),
                    "Animation inventory clip paths must be unique portable JSON paths.");
                Hex(entry.sha256, 64, "decoded clip SHA256");
                Hex(entry.source_git_blob_sha1, 40, "source WAN Git blob");
                AuthorizedAssetRoot.ValidateRelativePath(entry.source_path);
                SourceAnimationClip clip = ReadClip(readClipBytes(entry.path), entry.path, entry.sha256);
                Require(clip.SequenceName == entry.runtime_name && clip.SourcePath == entry.source_path &&
                    clip.SourceGitBlobSha1 == entry.source_git_blob_sha1 &&
                    clip.SourceFrameCount == entry.source_frames && clip.SelectedSourceFrames.Count == entry.selected_frames &&
                    clip.SelectedSourceFrames[0] == entry.source_first &&
                    clip.SelectedSourceFrames[clip.SelectedSourceFrames.Count - 1] == entry.source_last,
                    entry.path + ": inventory/source identity, counts or selected boundaries do not match.");
                Require(!result.ContainsKey(clip.SequenceName), "Duplicate original animation sequence '" + clip.SequenceName + "'.");
                string sourceIdentity = clip.SourceGitBlobSha1 + "/" + clip.SourceSha256, previousIdentity;
                Require(!sourceIdentities.TryGetValue(clip.SourcePath, out previousIdentity) || previousIdentity == sourceIdentity,
                    entry.path + ": clips from the same original WAN disagree about its content identity.");
                sourceIdentities[clip.SourcePath] = sourceIdentity;
                result.Add(clip.SequenceName, clip);
            }
            return new SourceAnimationLibrary(result, AuthorizedAssetRoot.Sha256(inventoryBytes));
        }

        public static SourceAnimationClip ReadClip(byte[] bytes, string relativeLabel, string expectedDecodedSha256)
        {
            AuthorizedAssetRoot.ValidateRelativePath(relativeLabel);
            Hex(expectedDecodedSha256, 64, "expected decoded clip SHA256");
            Require(bytes != null && bytes.Length >= 2 && bytes.Length <= 8 * 1024 * 1024,
                relativeLabel + ": decoded clip JSON byte limit exceeded or empty.");
            Require(bytes != null && AuthorizedAssetRoot.Sha256(bytes) == expectedDecodedSha256,
                relativeLabel + ": decoded clip SHA256 does not match its explicit content pin.");
            Document doc = Deserialize<Document>(bytes, relativeLabel, 8 * 1024 * 1024);
            Require(doc != null && doc.schema == "original-wan-clip/v1" && doc.provenance != null &&
                doc.sequence != null && doc.format != null && doc.root_translation != null &&
                doc.validation != null && doc.joints != null && doc.frames != null && doc.record_layout != null,
                relativeLabel + ": expected complete original-wan-clip/v1.");
            Provenance provenance = doc.provenance;
            Require(provenance.repository == "historicalsource/nflblitz2000" && provenance.commit == SourceRevision &&
                provenance.content_pin_verified && provenance.byte_length > 0 && provenance.byte_length <= 64 * 1024 * 1024,
                relativeLabel + ": original source identity/content-pin provenance is invalid.");
            Hex(provenance.git_blob_sha1, 40, "source WAN Git blob");
            Hex(provenance.sha256, 64, "source WAN SHA256");
            AuthorizedAssetRoot.ValidateRelativePath(provenance.path);
            FormatSource converter = provenance.format_source;
            Require(converter != null && converter.repository == "historicalsource/midwayvideo" &&
                converter.commit == ConverterRevision && converter.path == "tools/wan2seq/wan2seq.c" &&
                converter.git_blob_sha1 == "33c7e489d9b7a40d3b0cd435236db8c357d143fe" &&
                Range(converter.matrix_conversion_lines, 585, 631),
                relativeLabel + ": missing pinned original matrix-conversion provenance.");
            Format format = doc.format;
            Require(format.container == "WIFF" && format.chunk == "3AN " &&
                format.container_endianness == "big" && format.animation_body_endianness == "little" &&
                format.source_long_bytes == 4 && format.source_float == "IEEE-754 binary32" && format.chunk_count > 0,
                relativeLabel + ": unsupported original WAN matrix format.");
            Sequence sequence = doc.sequence;
            Identifier(sequence.runtime_name, "sequence");
            Require(!string.IsNullOrEmpty(sequence.authoring_name) && sequence.authoring_name.Length <= 128 &&
                sequence.limb_count == SourceTiming.JointCount && doc.joints.Length == SourceTiming.JointCount &&
                sequence.source_frame_count > 0 && sequence.source_frame_count <= 4096 &&
                sequence.selected_frame_count > 0 && sequence.selected_frame_count <= sequence.source_frame_count &&
                sequence.selected_source_frame_indices != null &&
                sequence.selected_source_frame_indices.Length == sequence.selected_frame_count &&
                doc.frames.Length == sequence.selected_frame_count,
                relativeLabel + ": invalid source frame count, selected indices or fifteen-joint mapping.");
            Require(sequence.chunk_number >= 0 && sequence.chunk_number < format.chunk_count &&
                sequence.chunk_byte_offset >= 0 && sequence.chunk_payload_bytes > 0 &&
                (long)sequence.chunk_byte_offset + 8 + sequence.chunk_payload_bytes == sequence.chunk_end_byte_offset &&
                sequence.chunk_end_byte_offset <= provenance.byte_length,
                relativeLabel + ": invalid source matrix chunk bounds.");
            for (int i = 0; i < SourceTiming.JointCount; i++)
            {
                Joint joint = doc.joints[i];
                Require(joint != null && joint.name == SourceAnimationSkeleton.JointNames[i] &&
                    joint.fbplyr_skel_index == i && joint.source_limb_id == i + 1 &&
                    (i == 0 ? !joint.parent_index.HasValue : joint.parent_index == SourceAnimationSkeleton.ParentIndices[i]),
                    relativeLabel + ": original fbplyr_skel name/index/limb/parent mismatch at joint " + i + ".");
            }
            Require(doc.record_layout.Length == 17, relativeLabel + ": expected flags/root/fifteen original matrix records.");
            long previousEnd = (long)sequence.chunk_byte_offset + 8;
            for (int i = 0; i < doc.record_layout.Length; i++)
            {
                Record record = doc.record_layout[i];
                int id = i == 0 ? -1 : i == 1 ? -2 : i - 1;
                int stride = i == 0 ? 4 : i == 1 ? 12 : 48;
                Require(record != null && record.id == id && record.count == sequence.source_frame_count &&
                    record.stride == stride && record.record_offset >= previousEnd &&
                    record.data_offset == record.record_offset + 8 &&
                    (long)record.data_offset + (long)record.count * stride <= sequence.chunk_end_byte_offset,
                    relativeLabel + ": invalid original matrix-record provenance at record " + i + ".");
                previousEnd = (long)record.data_offset + (long)record.count * stride;
            }
            Validation validation = doc.validation;
            Require(validation.validated_source_frames == sequence.source_frame_count &&
                validation.validated_limb_frames == sequence.source_frame_count * SourceTiming.JointCount &&
                PositiveBound(validation.matrix_error_limit, 0.0001) &&
                ErrorBound(validation.max_matrix_determinant_error, validation.matrix_error_limit) &&
                ErrorBound(validation.max_matrix_orthogonality_error, validation.matrix_error_limit) &&
                ErrorBound(validation.max_quaternion_norm_error, 0.0001) &&
                Finite(validation.min_adjacent_source_quaternion_dot) &&
                validation.min_adjacent_source_quaternion_dot >= -0.000001 &&
                validation.min_adjacent_source_quaternion_dot <= 1.000001,
                relativeLabel + ": invalid matrix/quaternion validation provenance.");
            Require(Finite(doc.root_translation.runtime_scale) && Math.Abs(doc.root_translation.runtime_scale - RootScale) < 1e-12 &&
                doc.root_translation.space == "source model XYZ before player placement/facing and animation-mode-specific anchoring",
                relativeLabel + ": unsupported source root scale or space.");
            var frames = new SourceAnimationFrame[sequence.selected_frame_count];
            int previousSource = -1;
            for (int ordinal = 0; ordinal < frames.Length; ordinal++)
            {
                Frame raw = doc.frames[ordinal];
                int sourceFrame = sequence.selected_source_frame_indices[ordinal];
                Require(sourceFrame > previousSource && sourceFrame < sequence.source_frame_count && raw != null &&
                    raw.exported_frame_index == ordinal && raw.source_frame_index == sourceFrame &&
                    (raw.source_frame_flags & 0x4000) != 0 && raw.root_translation != null &&
                    raw.rotations_xyzw != null && raw.rotations_xyzw.Length == SourceTiming.JointCount,
                    relativeLabel + ": selected source/exported ordinal/flags mismatch at ordinal " + ordinal + ".");
                previousSource = sourceFrame;
                SourceVector3 authoring = Vector(raw.root_translation.authoring_xyz, "authoring root");
                SourceVector3 scaled = Vector(raw.root_translation.scaled_runtime_xyz, "scaled runtime root");
                for (int axis = 0; axis < 3; axis++)
                    Require(Math.Abs(raw.root_translation.authoring_xyz[axis] * RootScale -
                        raw.root_translation.scaled_runtime_xyz[axis]) <= 0.00001,
                        relativeLabel + ": source root scale mismatch at authoring frame " + sourceFrame + ".");
                var rotations = new SourceRotation[SourceTiming.JointCount];
                for (int joint = 0; joint < rotations.Length; joint++)
                {
                    double[] q = raw.rotations_xyzw[joint];
                    Components(q, 4, relativeLabel + " frame " + sourceFrame + " joint " + joint);
                    double norm = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
                    Require(Math.Abs(norm - 1) <= 0.0001,
                        relativeLabel + ": non-unit quaternion at authoring frame " + sourceFrame + ", joint " + joint + ".");
                    rotations[joint] = new SourceRotation((float)(q[0] / norm), (float)(q[1] / norm),
                        (float)(q[2] / norm), (float)(q[3] / norm));
                }
                frames[ordinal] = new SourceAnimationFrame(sourceFrame, authoring, scaled, new SourceJointPose(rotations));
            }
            return new SourceAnimationClip(sequence.runtime_name, provenance.path, provenance.git_blob_sha1,
                provenance.sha256, expectedDecodedSha256, sequence.animation_flags, sequence.source_frame_count,
                sequence.selected_source_frame_indices, frames);
        }

        private static T Deserialize<T>(byte[] bytes, string label, int maximumBytes)
        {
            Require(bytes != null && bytes.Length >= 2 && bytes.Length <= maximumBytes, label + ": JSON byte limit exceeded or empty.");
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 });
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 24, MaxArrayLength = 300000,
                    MaxStringContentLength = 4096, MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384 };
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas))
                    return (T)serializer.ReadObject(reader);
            }
            catch (SerializationException e) { throw new PlayerAssetException(label + ": invalid required clip JSON: " + e.Message); }
            catch (XmlException e) { throw new PlayerAssetException(label + ": invalid clip JSON: " + e.Message); }
        }

        private static SourceVector3 Vector(double[] values, string label)
        {
            Components(values, 3, label);
            return new SourceVector3((float)values[0], (float)values[1], (float)values[2]);
        }
        private static void Components(double[] values, int count, string label)
        {
            Require(values != null && values.Length == count, label + ": expected " + count + " finite components.");
            foreach (double value in values)
                Require(Finite(value) && Math.Abs(value) <= PlayerImportLimits.CoordinateMagnitude, label + ": nonfinite or huge component.");
        }
        private static bool Range(int[] values, int first, int last) { return values != null && values.Length == 2 && values[0] == first && values[1] == last; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool PositiveBound(double value, double maximum) { return Finite(value) && value > 0 && value <= maximum; }
        private static bool ErrorBound(double value, double maximum) { return Finite(value) && value >= 0 && value <= maximum; }
        private static void Require(bool condition, string message) { if (!condition) throw new PlayerAssetException(message); }
        private static void Hex(string value, int count, string label)
        {
            Require(value != null && value.Length == count, label + " must be lowercase hexadecimal.");
            foreach (char c in value)
                Require(c >= '0' && c <= '9' || c >= 'a' && c <= 'f', label + " must be lowercase hexadecimal.");
        }
        private static void Identifier(string value, string label)
        {
            Require(!string.IsNullOrEmpty(value) && value.Length <= 64, "Invalid " + label + ".");
            foreach (char c in value)
                Require(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_',
                    "Invalid portable " + label + ".");
        }

        [DataContract] private sealed class Inventory
        {
            [DataMember(IsRequired = true)] public string schema { get; set; }
            [DataMember(IsRequired = true)] public InventoryEntry[] clips { get; set; }
        }
        [DataContract] private sealed class InventoryEntry
        {
            [DataMember(IsRequired = true)] public string path { get; set; }
            [DataMember(IsRequired = true)] public string runtime_name { get; set; }
            [DataMember(IsRequired = true)] public string sha256 { get; set; }
            [DataMember(IsRequired = true)] public string source_git_blob_sha1 { get; set; }
            [DataMember(IsRequired = true)] public string source_path { get; set; }
            [DataMember(IsRequired = true)] public int selected_frames { get; set; }
            [DataMember(IsRequired = true)] public int source_frames { get; set; }
            [DataMember(IsRequired = true)] public int source_first { get; set; }
            [DataMember(IsRequired = true)] public int source_last { get; set; }
        }
        [DataContract] private sealed class Document
        {
            [DataMember(IsRequired = true)] public string schema { get; set; }
            [DataMember(IsRequired = true)] public Provenance provenance { get; set; }
            [DataMember(IsRequired = true)] public Format format { get; set; }
            [DataMember(IsRequired = true)] public Sequence sequence { get; set; }
            [DataMember(IsRequired = true)] public RootPolicy root_translation { get; set; }
            [DataMember(IsRequired = true)] public Joint[] joints { get; set; }
            [DataMember(IsRequired = true)] public Frame[] frames { get; set; }
            [DataMember(IsRequired = true)] public Record[] record_layout { get; set; }
            [DataMember(IsRequired = true)] public Validation validation { get; set; }
        }
        [DataContract] private sealed class Provenance
        {
            [DataMember(IsRequired = true)] public string repository { get; set; }
            [DataMember(IsRequired = true)] public string commit { get; set; }
            [DataMember(IsRequired = true)] public string path { get; set; }
            [DataMember(IsRequired = true)] public string git_blob_sha1 { get; set; }
            [DataMember(IsRequired = true)] public string sha256 { get; set; }
            [DataMember(IsRequired = true)] public bool content_pin_verified { get; set; }
            [DataMember(IsRequired = true)] public int byte_length { get; set; }
            [DataMember(IsRequired = true)] public FormatSource format_source { get; set; }
        }
        [DataContract] private sealed class FormatSource
        {
            [DataMember(IsRequired = true)] public string repository { get; set; }
            [DataMember(IsRequired = true)] public string commit { get; set; }
            [DataMember(IsRequired = true)] public string path { get; set; }
            [DataMember(IsRequired = true)] public string git_blob_sha1 { get; set; }
            [DataMember(IsRequired = true)] public int[] matrix_conversion_lines { get; set; }
        }
        [DataContract] private sealed class Format
        {
            [DataMember(IsRequired = true)] public string container { get; set; }
            [DataMember(IsRequired = true)] public string chunk { get; set; }
            [DataMember(IsRequired = true)] public string container_endianness { get; set; }
            [DataMember(IsRequired = true)] public string animation_body_endianness { get; set; }
            [DataMember(IsRequired = true)] public string source_float { get; set; }
            [DataMember(IsRequired = true)] public int source_long_bytes { get; set; }
            [DataMember(IsRequired = true)] public int chunk_count { get; set; }
        }
        [DataContract] private sealed class Sequence
        {
            [DataMember(IsRequired = true)] public string runtime_name { get; set; }
            [DataMember(IsRequired = true)] public string authoring_name { get; set; }
            [DataMember(IsRequired = true)] public uint animation_flags { get; set; }
            [DataMember(IsRequired = true)] public int limb_count { get; set; }
            [DataMember(IsRequired = true)] public int source_frame_count { get; set; }
            [DataMember(IsRequired = true)] public int selected_frame_count { get; set; }
            [DataMember(IsRequired = true)] public int chunk_number { get; set; }
            [DataMember(IsRequired = true)] public int chunk_byte_offset { get; set; }
            [DataMember(IsRequired = true)] public int chunk_payload_bytes { get; set; }
            [DataMember(IsRequired = true)] public int chunk_end_byte_offset { get; set; }
            [DataMember(IsRequired = true)] public int[] selected_source_frame_indices { get; set; }
        }
        [DataContract] private sealed class RootPolicy
        {
            [DataMember(IsRequired = true)] public double runtime_scale { get; set; }
            [DataMember(IsRequired = true)] public string space { get; set; }
        }
        [DataContract] private sealed class Joint
        {
            [DataMember(IsRequired = true)] public string name { get; set; }
            [DataMember(IsRequired = true)] public int fbplyr_skel_index { get; set; }
            [DataMember(IsRequired = true)] public int source_limb_id { get; set; }
            [DataMember(IsRequired = true)] public int? parent_index { get; set; }
        }
        [DataContract] private sealed class Frame
        {
            [DataMember(IsRequired = true)] public int exported_frame_index { get; set; }
            [DataMember(IsRequired = true)] public int source_frame_index { get; set; }
            [DataMember(IsRequired = true)] public int source_frame_flags { get; set; }
            [DataMember(IsRequired = true)] public FrameRoot root_translation { get; set; }
            [DataMember(IsRequired = true)] public double[][] rotations_xyzw { get; set; }
        }
        [DataContract] private sealed class FrameRoot
        {
            [DataMember(IsRequired = true)] public double[] authoring_xyz { get; set; }
            [DataMember(IsRequired = true)] public double[] scaled_runtime_xyz { get; set; }
        }
        [DataContract] private sealed class Record
        {
            [DataMember(IsRequired = true)] public int id { get; set; }
            [DataMember(IsRequired = true)] public int count { get; set; }
            [DataMember(IsRequired = true)] public int stride { get; set; }
            [DataMember(IsRequired = true)] public int record_offset { get; set; }
            [DataMember(IsRequired = true)] public int data_offset { get; set; }
        }
        [DataContract] private sealed class Validation
        {
            [DataMember(IsRequired = true)] public int validated_source_frames { get; set; }
            [DataMember(IsRequired = true)] public int validated_limb_frames { get; set; }
            [DataMember(IsRequired = true)] public double matrix_error_limit { get; set; }
            [DataMember(IsRequired = true)] public double max_matrix_determinant_error { get; set; }
            [DataMember(IsRequired = true)] public double max_matrix_orthogonality_error { get; set; }
            [DataMember(IsRequired = true)] public double max_quaternion_norm_error { get; set; }
            [DataMember(IsRequired = true)] public double min_adjacent_source_quaternion_dot { get; set; }
        }
    }

    public sealed class SourceAnimationClip : ISourceAnimationClip
    {
        private readonly Dictionary<int, SourceAnimationFrame> frames;
        private readonly ReadOnlyCollection<int> selected;
        public string SequenceName { get; private set; }
        public string SourceRevision { get { return SourceAnimationLibrary.SourceRevision; } }
        public string SourcePath { get; private set; }
        public string SourceGitBlobSha1 { get; private set; }
        public string SourceSha256 { get; private set; }
        public string DecodedSha256 { get; private set; }
        public uint SourceFlags { get; private set; }
        public int SourceFrameCount { get; private set; }
        public IReadOnlyList<int> SelectedSourceFrames { get { return selected; } }

        internal SourceAnimationClip(string name, string path, string blob, string sourceHash, string decodedHash,
            uint flags, int sourceCount, int[] selectedFrames, SourceAnimationFrame[] data)
        {
            SequenceName = name; SourcePath = path; SourceGitBlobSha1 = blob; SourceSha256 = sourceHash;
            DecodedSha256 = decodedHash; SourceFlags = flags; SourceFrameCount = sourceCount;
            selected = Array.AsReadOnly((int[])selectedFrames.Clone());
            frames = new Dictionary<int, SourceAnimationFrame>();
            foreach (SourceAnimationFrame frame in data) frames.Add(frame.SourceFrame, frame);
        }

        public SourceAnimationFrame GetFrame(int sourceFrame)
        {
            SourceAnimationFrame frame;
            if (!frames.TryGetValue(sourceFrame, out frame))
                throw new PlayerAssetException("Original animation '" + SequenceName + "' does not contain selected AUTHORING frame " +
                    sourceFrame + ". Convert exported script ordinals through SelectedSourceFrames.");
            return frame;
        }
    }

    public static class SourceAnimationSkeleton
    {
        public static readonly ReadOnlyCollection<string> JointNames = Array.AsReadOnly(new[] {
            "pelvis", "torso", "right_upper_arm", "right_forearm", "right_hand", "left_upper_arm",
            "left_forearm", "left_hand", "head", "right_thigh", "right_calf", "right_foot",
            "left_thigh", "left_calf", "left_foot" });
        public static readonly ReadOnlyCollection<int> ParentIndices = Array.AsReadOnly(new[] { -1, 0, 1, 2, 3, 1, 5, 6, 1, 0, 9, 10, 0, 12, 13 });
    }
}
