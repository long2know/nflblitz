using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;

namespace Blitz.AssetImport
{
    public struct AssetQuaternion
    {
        public readonly float X, Y, Z, W;
        public AssetQuaternion(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    }

    public sealed class SourcePoseJoint
    {
        public readonly string Name;
        public readonly int ParentIndex;
        public readonly AssetQuaternion Rotation;
        public readonly ReadOnlyCollection<double> SourceMatrix;
        internal SourcePoseJoint(string name, int parent, AssetQuaternion rotation, double[] matrix)
        {
            Name = name; ParentIndex = parent; Rotation = rotation; SourceMatrix = Array.AsReadOnly(matrix);
        }
    }

    public sealed class SourcePlayerPose
    {
        public readonly string Sha256, Sequence, SourcePath, SourceGitBlobSha1, SourceSha256;
        public readonly int SourceFrame, ExportedFrame, SourceFrameCount;
        public readonly AssetVector3 ScaledRootTranslation;
        public readonly ReadOnlyCollection<SourcePoseJoint> Joints;

        internal SourcePlayerPose(string hash, string sequence, string path, string blob, string sourceHash,
            int sourceFrame, int exportedFrame, int frameCount, AssetVector3 root, SourcePoseJoint[] joints)
        {
            Sha256 = hash; Sequence = sequence; SourcePath = path; SourceGitBlobSha1 = blob; SourceSha256 = sourceHash;
            SourceFrame = sourceFrame; ExportedFrame = exportedFrame; SourceFrameCount = frameCount;
            ScaledRootTranslation = root; Joints = Array.AsReadOnly(joints);
        }

        public AssetVector3 UnityAnchoredRootOffset(PlayerCoordinateConversion conversion)
        {
            // Active runtime uses object.y + ay; MODE_ANCHORXZ suppresses initial horizontal drift.
            return conversion.Convert(new AssetVector3(0, ScaledRootTranslation.Y, 0));
        }

        public ReadOnlyCollection<AssetQuaternion> UnityLocalRotations(PlayerCoordinateConversion conversion)
        {
            var rotations = new List<AssetQuaternion>();
            foreach (SourcePoseJoint joint in Joints)
            {
                AssetQuaternion q = joint.Rotation;
                AssetVector3 axis = conversion.Convert(new AssetVector3(q.X, q.Y, q.Z));
                // A quaternion's imaginary part is an axial vector, including reflection parity.
                rotations.Add(new AssetQuaternion(axis.X * conversion.Determinant, axis.Y * conversion.Determinant,
                    axis.Z * conversion.Determinant, q.W));
            }
            return rotations.AsReadOnly();
        }
    }

    public static class SourcePlayerPoseReader
    {
        [DataContract] private sealed class Document
        {
            [DataMember(IsRequired = true)] public string schema { get; set; }
            [DataMember(IsRequired = true)] public Joint[] joints { get; set; }
            [DataMember(IsRequired = true)] public Sequence sequence { get; set; }
            [DataMember(IsRequired = true)] public Root root_translation { get; set; }
            [DataMember(IsRequired = true)] public Provenance provenance { get; set; }
        }
        [DataContract] private sealed class Joint
        {
            [DataMember(IsRequired = true)] public string name { get; set; }
            [DataMember(IsRequired = true)] public int fbplyr_skel_index { get; set; }
            [DataMember(IsRequired = true)] public int source_limb_id { get; set; }
            [DataMember(IsRequired = true)] public int? parent_index { get; set; }
            [DataMember(IsRequired = true)] public double[] quaternion_xyzw { get; set; }
            [DataMember(IsRequired = true)] public double[] rotation_matrix_row_major { get; set; }
        }
        [DataContract] private sealed class Sequence
        {
            [DataMember(IsRequired = true)] public string runtime_name { get; set; }
            [DataMember(IsRequired = true)] public int limb_count { get; set; }
            [DataMember(IsRequired = true)] public int source_frame_index { get; set; }
            [DataMember(IsRequired = true)] public int exported_frame_index { get; set; }
            [DataMember(IsRequired = true)] public int source_frame_count { get; set; }
            [DataMember(IsRequired = true)] public int selected_frame_count { get; set; }
        }
        [DataContract] private sealed class Root
        {
            [DataMember(IsRequired = true)] public double[] authoring_xyz { get; set; }
            [DataMember(IsRequired = true)] public double runtime_scale { get; set; }
            [DataMember(IsRequired = true)] public double[] scaled_runtime_xyz { get; set; }
        }
        [DataContract] private sealed class Provenance
        {
            [DataMember(IsRequired = true)] public string path { get; set; }
            [DataMember(IsRequired = true)] public string git_blob_sha1 { get; set; }
            [DataMember(IsRequired = true)] public string sha256 { get; set; }
            [DataMember(IsRequired = true)] public bool content_pin_verified { get; set; }
        }

        public static SourcePlayerPose Read(byte[] bytes, string relativeLabel, string expectedSha256, SourcePlayer player)
        {
            AuthorizedAssetRoot.ValidateRelativePath(relativeLabel);
            RequireHex(expectedSha256, 64, "Expected decoded-pose SHA256");
            if (player == null) throw new ArgumentNullException("player");
            if (bytes == null || bytes.Length < 2 || bytes.Length > 256 * 1024)
                throw new PlayerAssetException("Decoded pose JSON must be 2..262144 bytes.");
            string hash = AuthorizedAssetRoot.Sha256(bytes);
            if (hash != expectedSha256) throw new PlayerAssetException(relativeLabel + ": decoded-pose SHA256 does not match the explicit expected content pin.");
            Document raw;
            try
            {
                var settings = new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 16384 };
                var serializer = new DataContractJsonSerializer(typeof(Document), settings);
                var quotas = new XmlDictionaryReaderQuotas {
                    MaxDepth = 16, MaxArrayLength = 8192, MaxStringContentLength = 2048,
                    MaxBytesPerRead = 4096, MaxNameTableCharCount = 4096
                };
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas))
                    raw = (Document)serializer.ReadObject(reader);
            }
            catch (SerializationException e) { throw new PlayerAssetException(relativeLabel + ": invalid bounded pose JSON: " + e.Message); }
            catch (XmlException e) { throw new PlayerAssetException(relativeLabel + ": invalid pose JSON: " + e.Message); }
            if (raw == null || raw.schema != "original-wan-first-pose/v1" || raw.sequence == null ||
                raw.root_translation == null || raw.provenance == null || raw.joints == null)
                throw new PlayerAssetException(relativeLabel + ": expected complete original-wan-first-pose/v1.");
            Sequence sequence = raw.sequence;
            Identifier(sequence.runtime_name, "sequence");
            if (raw.joints.Length != player.Nodes.Count || sequence.limb_count != player.Nodes.Count)
                throw new PlayerAssetException("Pose joint count does not match selected skeleton/model limb mapping.");
            if (sequence.source_frame_count < 1 || sequence.source_frame_count > 1024 ||
                sequence.selected_frame_count < 1 || sequence.selected_frame_count > sequence.source_frame_count ||
                sequence.source_frame_index < 0 || sequence.source_frame_index >= sequence.source_frame_count ||
                sequence.exported_frame_index != 0)
                throw new PlayerAssetException("Invalid source/exported first-frame indices or bounded frame count.");
            AuthorizedAssetRoot.ValidateRelativePath(raw.provenance.path);
            RequireHex(raw.provenance.git_blob_sha1, 40, "Source WAN Git blob");
            RequireHex(raw.provenance.sha256, 64, "Source WAN SHA256");
            if (!raw.provenance.content_pin_verified) throw new PlayerAssetException("Decoded pose does not attest a verified source-content pin.");
            Root root = raw.root_translation;
            Vector(root.authoring_xyz, 3, "authoring root");
            Vector(root.scaled_runtime_xyz, 3, "scaled root");
            if (!Finite(root.runtime_scale) || root.runtime_scale <= 0 || root.runtime_scale > 100)
                throw new PlayerAssetException("Root translation scale is invalid.");
            for (int i = 0; i < 3; i++)
                if (Math.Abs(root.authoring_xyz[i] * root.runtime_scale - root.scaled_runtime_xyz[i]) > 0.00001)
                    throw new PlayerAssetException("Scaled root translation does not match source authoring translation and declared scale.");
            var joints = new SourcePoseJoint[raw.joints.Length];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < joints.Length; i++)
            {
                Joint joint = raw.joints[i];
                if (joint == null) throw new PlayerAssetException("Missing pose joint " + i + ".");
                Identifier(joint.name, "joint");
                if (!names.Add(joint.name)) throw new PlayerAssetException("Duplicate pose joint name '" + joint.name + "'.");
                if (joint.fbplyr_skel_index != i || joint.source_limb_id != i + 1 ||
                    (player.Nodes[i].ParentIndex < 0 ? joint.parent_index.HasValue :
                        joint.parent_index != player.Nodes[i].ParentIndex))
                    throw new PlayerAssetException("Pose index/limb-ID/parent mapping mismatch at joint " + i + ".");
                Vector(joint.quaternion_xyzw, 4, "XYZW quaternion");
                Vector(joint.rotation_matrix_row_major, 9, "rotation matrix");
                double[] q = joint.quaternion_xyzw;
                double length = Math.Sqrt(q[0]*q[0]+q[1]*q[1]+q[2]*q[2]+q[3]*q[3]);
                if (Math.Abs(length - 1) > 0.0001) throw new PlayerAssetException("Pose quaternion must be unit length at joint " + i + ".");
                var rotation = new AssetQuaternion((float)(q[0]/length), (float)(q[1]/length), (float)(q[2]/length), (float)(q[3]/length));
                double[] fromQuaternion = SourcePoseEvaluator.QuaternionMatrix(rotation);
                double[] m = joint.rotation_matrix_row_major;
                for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    double dot = 0;
                    for (int k = 0; k < 3; k++) dot += m[r*3+k] * m[c*3+k];
                    if (Math.Abs(dot - (r == c ? 1 : 0)) > 0.0001 ||
                        Math.Abs(fromQuaternion[r*4+c] - m[r*3+c]) > 0.0001)
                        throw new PlayerAssetException("Pose rotation matrix/quaternion mismatch at joint " + i + ".");
                }
                double det = m[0]*(m[4]*m[8]-m[5]*m[7])-m[1]*(m[3]*m[8]-m[5]*m[6])+m[2]*(m[3]*m[7]-m[4]*m[6]);
                if (Math.Abs(det-1) > 0.0001) throw new PlayerAssetException("Pose matrix is not a proper rotation at joint " + i + ".");
                joints[i] = new SourcePoseJoint(joint.name, player.Nodes[i].ParentIndex, rotation, m);
            }
            return new SourcePlayerPose(hash, sequence.runtime_name, raw.provenance.path, raw.provenance.git_blob_sha1,
                raw.provenance.sha256, sequence.source_frame_index, sequence.exported_frame_index, sequence.source_frame_count,
                new AssetVector3((float)root.scaled_runtime_xyz[0], (float)root.scaled_runtime_xyz[1], (float)root.scaled_runtime_xyz[2]), joints);
        }
        private static void Identifier(string value, string label)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) throw new PlayerAssetException("Invalid pose " + label + " name.");
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_')
                    throw new PlayerAssetException("Non-portable pose " + label + " name.");
        }
        private static void Vector(double[] values, int count, string label)
        {
            if (values == null || values.Length != count) throw new PlayerAssetException("Expected " + count + " components for " + label + ".");
            foreach (double value in values)
                if (!Finite(value) || Math.Abs(value) > PlayerImportLimits.CoordinateMagnitude)
                    throw new PlayerAssetException("Nonfinite/huge " + label + " component.");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void RequireHex(string value, int count, string label)
        {
            if (value == null || value.Length != count) throw new PlayerAssetException(label + " must be lowercase hex.");
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new PlayerAssetException(label + " must be lowercase hex.");
        }
    }

    // Independent active-renderer reference: model-space pivots, parent-composed source matrices.
    public sealed class SourcePoseEvaluator
    {
        private readonly double[][] matrices;
        private readonly SourcePlayer player;
        private readonly PlayerCoordinateConversion conversion;
        public SourcePoseEvaluator(SourcePlayer player, SourcePlayerPose pose, PlayerCoordinateConversion conversion)
        {
            if (player == null || pose == null || conversion == null) throw new ArgumentNullException("pose reference arguments");
            if (player.Nodes.Count != pose.Joints.Count) throw new PlayerAssetException("Pose evaluator mapping count mismatch.");
            this.player = player; this.conversion = conversion;
            matrices = new double[player.Nodes.Count][];
            for (int i = 0; i < matrices.Length; i++) Build(i, pose);
        }
        private void Build(int index, SourcePlayerPose pose)
        {
            if (matrices[index] != null) return;
            SourceRigNode node = player.Nodes[index];
            if (node.ParentIndex >= 0) Build(node.ParentIndex, pose);
            double[] parent = node.ParentIndex < 0 ? Translation(new AssetVector3(0, pose.ScaledRootTranslation.Y, 0)) : matrices[node.ParentIndex];
            double[] rotation = Identity();
            for (int r=0;r<3;r++) for (int c=0;c<3;c++) rotation[r*4+c] = pose.Joints[index].SourceMatrix[r*3+c];
            AssetVector3 p = node.BindPivot;
            matrices[index] = Multiply(Multiply(Multiply(parent, Translation(p)), rotation),
                Translation(new AssetVector3(-p.X,-p.Y,-p.Z)));
        }
        public AssetVector3 SourceVertexWorld(int limb, AssetVector3 sourceVertex)
        {
            double[] m = matrices[limb];
            var p = new AssetVector3((float)(m[0]*sourceVertex.X+m[1]*sourceVertex.Y+m[2]*sourceVertex.Z+m[3]),
                (float)(m[4]*sourceVertex.X+m[5]*sourceVertex.Y+m[6]*sourceVertex.Z+m[7]),
                (float)(m[8]*sourceVertex.X+m[9]*sourceVertex.Y+m[10]*sourceVertex.Z+m[11]));
            return conversion.Convert(p);
        }
        public AssetVector3 MeshVertexWorld(int limb, AssetVector3 meshLocal)
        {
            AssetVector3 pivot = conversion.Convert(player.Nodes[limb].BindPivot);
            var converted = new AssetVector3(meshLocal.X+pivot.X,meshLocal.Y+pivot.Y,meshLocal.Z+pivot.Z);
            AssetVector3 x=conversion.Convert(new AssetVector3(1,0,0)), y=conversion.Convert(new AssetVector3(0,1,0)),
                z=conversion.Convert(new AssetVector3(0,0,1));
            var source = new AssetVector3(Dot(converted,x),Dot(converted,y),Dot(converted,z));
            return SourceVertexWorld(limb,source);
        }
        private static float Dot(AssetVector3 a, AssetVector3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        private static double[] Identity() { return new double[] {1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1}; }
        private static double[] Translation(AssetVector3 p)
        {
            double[] m=Identity(); m[3]=p.X;m[7]=p.Y;m[11]=p.Z;return m;
        }
        private static double[] Multiply(double[] a, double[] b)
        {
            var m=new double[16];
            for(int r=0;r<4;r++)for(int c=0;c<4;c++)for(int k=0;k<4;k++)m[r*4+c]+=a[r*4+k]*b[k*4+c];
            return m;
        }
        internal static double[] QuaternionMatrix(AssetQuaternion q)
        {
            double x=q.X,y=q.Y,z=q.Z,w=q.W;
            return new[] {1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),0,
                2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),0,
                2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),0,0,0,0,1d};
        }
    }
}
