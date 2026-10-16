using System;
using UnityEngine;

namespace Blitz.Presentation
{
    [DisallowMultipleComponent]
    public sealed class SourcePlayerRig : MonoBehaviour
    {
        public const string ImportStage = "bind-pose-only-v1";
        public const string StaticPoseStage = "source-static-pose-v1";
        [SerializeField] private string stableId;
        [SerializeField] private string stage;
        [SerializeField] private Transform[] joints;
        [SerializeField] private Vector3[] restPositions;
        [SerializeField] private int[] parentIndices;
        [SerializeField] private int[] sourceFlags;
        [SerializeField] private string poseSequence;
        [SerializeField] private int poseSourceFrame;
        [SerializeField] private string poseSha256;
        [SerializeField] private TextAsset poseSource;

        public string StableId { get { return stableId; } }
        public string Stage { get { return stage; } }
        public int JointCount { get { return joints == null ? 0 : joints.Length; } }
        public bool HasStaticSourcePose { get { return stage == StaticPoseStage; } }
        public string PoseSequence { get { return poseSequence; } }
        public int PoseSourceFrame { get { return poseSourceFrame; } }
        public string PoseSha256 { get { return poseSha256; } }
        public TextAsset PoseSource { get { return poseSource; } }

        // Called by the explicit editor importer; no external source files are read in a player/WebGL build.
        public void InitializeBindPose(string id, Transform[] orderedJoints, int[] parents, int[] flags)
        {
            if (string.IsNullOrEmpty(id) || orderedJoints == null || parents == null || flags == null ||
                orderedJoints.Length < 1 || orderedJoints.Length > 256 ||
                parents.Length != orderedJoints.Length || flags.Length != orderedJoints.Length)
                throw new InvalidOperationException("Invalid generated source-rig metadata.");
            stableId = id; stage = ImportStage;
            joints = (Transform[])orderedJoints.Clone();
            parentIndices = (int[])parents.Clone(); sourceFlags = (int[])flags.Clone();
            restPositions = new Vector3[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) throw new InvalidOperationException("Missing generated joint " + i + ".");
                restPositions[i] = joints[i].localPosition;
            }
            ValidateGeneratedRig();
        }

        public void ValidateGeneratedRig()
        {
            if ((stage != ImportStage && stage != StaticPoseStage) || string.IsNullOrEmpty(stableId) || joints == null || joints.Length < 1 ||
                joints.Length > 256 || restPositions == null || parentIndices == null || sourceFlags == null ||
                restPositions.Length != joints.Length || parentIndices.Length != joints.Length || sourceFlags.Length != joints.Length)
                throw new InvalidOperationException("Source asset mode requires a generated bind-pose prefab with intact metadata; primitive replacement is disabled.");
            if (HasStaticSourcePose && (string.IsNullOrEmpty(poseSequence) || poseSourceFrame < 0 ||
                poseSha256 == null || poseSha256.Length != 64 || poseSource == null))
                throw new InvalidOperationException("Static source-pose provenance/frame is missing; refusing a mislabeled posed prefab.");
            for (int i = 0; i < joints.Length; i++)
            {
                int parent = parentIndices[i];
                if (joints[i] == null || parent < -1 || parent >= joints.Length || (i == 0 ? parent != -1 : parent == -1))
                    throw new InvalidOperationException("Missing/invalid generated source joint " + i + ".");
                Transform expectedParent = parent < 0 ? transform : joints[parent];
                if (joints[i].parent != expectedParent || !joints[i].IsChildOf(transform))
                    throw new InvalidOperationException("Generated source joint topology changed at " + i + ".");
                MeshFilter filter = joints[i].GetComponent<MeshFilter>();
                MeshRenderer renderer = joints[i].GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null ||
                    renderer.sharedMaterials.Length != filter.sharedMesh.subMeshCount)
                    throw new InvalidOperationException("Generated source mesh/materials missing at joint " + i + ".");
                foreach (Material material in renderer.sharedMaterials)
                    if (material == null || material.mainTexture == null)
                        throw new InvalidOperationException("Generated source texture missing at joint " + i + "; no color fallback is allowed.");
            }
        }

        public static SourcePlayerRig InstantiateRequired(SourcePlayerRig generatedPrefab, Transform parent)
        {
            if (generatedPrefab == null)
                throw new InvalidOperationException("Source asset mode selected without an authorized imported prefab. Import and assign one explicitly; no primitive actor will be substituted.");
            generatedPrefab.ValidateGeneratedRig();
            return Instantiate(generatedPrefab, parent, false);
        }

        public Transform GetJoint(int index)
        {
            if (joints == null || index < 0 || index >= joints.Length) throw new ArgumentOutOfRangeException("index");
            return joints[index];
        }

        // Future animation adapters must supply converted Unity-local rotations; this is not a WAN decoder.
        public void SetUnityLocalRotation(int index, Quaternion rotation)
        {
            double length = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y +
                (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
            if (!(length > 0) || double.IsInfinity(length) || double.IsNaN(length))
                throw new ArgumentException("Joint rotation must be finite and nonzero.", "rotation");
            double inverse = 1d / Math.Sqrt(length);
            GetJoint(index).localRotation = new Quaternion((float)(rotation.x * inverse), (float)(rotation.y * inverse),
                (float)(rotation.z * inverse), (float)(rotation.w * inverse));
        }

        public void ResetBindPose()
        {
            for (int i = 0; i < JointCount; i++)
            {
                joints[i].localPosition = restPositions[i];
                joints[i].localRotation = Quaternion.identity;
                joints[i].localScale = Vector3.one;
            }
            stage = ImportStage;
            poseSequence = null; poseSha256 = null; poseSource = null; poseSourceFrame = 0;
        }

        public void ApplyStaticSourcePose(Quaternion[] unityLocalRotations, Vector3 anchoredRootOffset,
            string sequence, int sourceFrame, string sourceSha256, TextAsset originalDecodedPose)
        {
            if (unityLocalRotations == null || unityLocalRotations.Length != JointCount ||
                string.IsNullOrEmpty(sequence) || sourceFrame < 0 || sourceSha256 == null ||
                sourceSha256.Length != 64 || originalDecodedPose == null ||
                float.IsNaN(anchoredRootOffset.x) || float.IsInfinity(anchoredRootOffset.x) ||
                float.IsNaN(anchoredRootOffset.y) || float.IsInfinity(anchoredRootOffset.y) ||
                float.IsNaN(anchoredRootOffset.z) || float.IsInfinity(anchoredRootOffset.z))
                throw new InvalidOperationException("Incomplete static source pose; no default standing rotations will be substituted.");
            for (int i = 0; i < JointCount; i++)
            {
                joints[i].localPosition = restPositions[i];
                SetUnityLocalRotation(i, unityLocalRotations[i]);
            }
            joints[0].localPosition += anchoredRootOffset;
            stage = StaticPoseStage; poseSequence = sequence; poseSourceFrame = sourceFrame;
            poseSha256 = sourceSha256; poseSource = originalDecodedPose;
            ValidateGeneratedRig();
        }
    }
}
