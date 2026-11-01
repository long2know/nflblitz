using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
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
        private MeshFilter numberFilter;
        private MeshRenderer numberRenderer;
        private SourceTeamPresentationAsset teamAssets;
        private int appliedModel = -1;
        private int appliedHead = -1;
        private string appliedUniform;
        private SourceSkinSelector appliedSkin;
        private string appliedPlayer;
        private SourceEvaluatedPose appliedPose;
        private readonly List<AffineMesh> affineMeshes = new List<AffineMesh>();
        private sealed class AffineMesh
        {
            internal MeshFilter Filter;
            internal Mesh Source, Clone;
            internal Vector3[] Vertices, Normals, PendingVertices, PendingNormals;
            internal Vector4[] Tangents, PendingTangents;
        }
        private Material[][] flashOriginals;
        private readonly System.Collections.Generic.Dictionary<Material, Material> flashMaterials =
            new System.Collections.Generic.Dictionary<Material, Material>();

        public string StableId { get { return stableId; } }
        public string Stage { get { return stage; } }
        public int JointCount { get { return joints == null ? 0 : joints.Length; } }
        public bool HasStaticSourcePose { get { return stage == StaticPoseStage; } }
        public string PoseSequence { get { return poseSequence; } }
        public int PoseSourceFrame { get { return poseSourceFrame; } }
        public string PoseSha256 { get { return poseSha256; } }
        public TextAsset PoseSource { get { return poseSource; } }
        public bool HasContinuousPose { get; private set; }
        public SourceTeamAppearance TeamAppearance { get; private set; }
        public string UniformIdentity { get { return TeamAppearance == null ? null : TeamAppearance.UniformIdentity; } }

        public void ApplyTeamAppearance(SourceTeamPresentationAsset assets, SourceTeamAppearance appearance)
        {
            if (assets == null || appearance == null)
                throw new ArgumentNullException(assets == null ? nameof(assets) : nameof(appearance));
            if (JointCount != SourceTiming.JointCount)
                throw new InvalidOperationException("Original team rendering requires the actual fifteen-joint player rig.");
            int head = appearance.CustomHead != null ? appearance.CustomHead.Index : appearance.PoppedHead != null ? -2 : -1;
            if (teamAssets == assets && appliedModel == appearance.Model.Index && appliedHead == head &&
                appliedUniform == appearance.UniformIdentity && appliedSkin == appearance.Uniform.Skin &&
                appliedPlayer == appearance.SourcePlayerId)
            {
                TeamAppearance = appearance;
                return;
            }
            ReleaseAffineMeshes();
            RestoreSourceFireMaterials();
            for (int i = 0; i < JointCount; i++)
            {
                SourceTeamLimbAsset limb = assets.Limb(appearance, i);
                joints[i].GetComponent<MeshFilter>().sharedMesh = limb.Mesh;
                joints[i].GetComponent<MeshRenderer>().sharedMaterials = assets.Materials(appearance, limb);
            }
            if (numberFilter == null)
            {
                Transform numbers = new GameObject("Original BCD jersey numbers / source torso matrix 1").transform;
                numbers.SetParent(joints[1], false);
                numberFilter = numbers.gameObject.AddComponent<MeshFilter>();
                numberRenderer = numbers.gameObject.AddComponent<MeshRenderer>();
                numberRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                numberRenderer.receiveShadows = false;
            }
            numberFilter.sharedMesh = assets.NumberMesh(appearance);
            numberRenderer.sharedMaterial = assets.NumberMaterial(appearance);
            teamAssets = assets; appliedModel = appearance.Model.Index; appliedHead = head;
            appliedUniform = appearance.UniformIdentity; appliedSkin = appearance.Uniform.Skin;
            appliedPlayer = appearance.SourcePlayerId; TeamAppearance = appearance;
            if (appliedPose != null && appliedPose.IsRawAffine) ApplyContinuousSourcePose(appliedPose);
        }

        public void ApplySourceFireFlash(SourceFirePlayerFlashState flash, Shader shader)
        {
            if (!flash.White) { RestoreSourceFireMaterials(); return; }
            if (flashOriginals != null) return;
            SourceFireGuard.Require(TeamAppearance != null && shader != null && shader.isSupported
                && shader.name == "Blitz/SourcePlayerFlashPortable", "Actual team textures and explicit authored flash shader required.");
            flashOriginals = new Material[JointCount + 1][];
            for (int i = 0; i <= JointCount; i++)
            {
                MeshRenderer renderer = i == JointCount ? numberRenderer : joints[i].GetComponent<MeshRenderer>();
                SourceFireGuard.Require(renderer != null, "Original limb/number material consumer missing.");
                Material[] original = renderer.sharedMaterials;
                flashOriginals[i] = original;
                var mapped = new Material[original.Length];
                for (int j = 0; j < original.Length; j++)
                {
                    Material source = original[j], material;
                    SourceFireGuard.Require(source != null && source.mainTexture != null, "No source player texture fallback during white flash.");
                    if (!flashMaterials.TryGetValue(source, out material))
                    {
                        material = new Material(shader) { name = source.name + "/source0x400-portable", mainTexture = source.mainTexture };
                        flashMaterials.Add(source, material);
                    }
                    mapped[j] = material;
                }
                renderer.sharedMaterials = mapped;
            }
        }

        private void RestoreSourceFireMaterials()
        {
            if (flashOriginals == null) return;
            for (int i = 0; i < flashOriginals.Length; i++)
            {
                MeshRenderer renderer = i == JointCount ? numberRenderer : joints[i].GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterials = flashOriginals[i];
            }
            flashOriginals = null;
        }

        private void OnDestroy()
        {
            ReleaseAffineMeshes();
            foreach (Material material in flashMaterials.Values) if (material != null) Destroy(material);
        }

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

        // Raw affine orientation is in the deformed mesh/applied value, not this container's TRS.
        public Transform GetJoint(int index)
        {
            if (joints == null || index < 0 || index >= joints.Length) throw new ArgumentOutOfRangeException("index");
            return joints[index];
        }

        public SourceVector3 GetUnityReferencePivot(int index)
        {
            GetJoint(index);
            Vector3 pivot = restPositions[index];
            int parent = parentIndices[index], depth = 0;
            while (parent >= 0)
            {
                if (++depth > JointCount) throw new InvalidOperationException("Source rig parent cycle.");
                pivot += restPositions[parent];
                parent = parentIndices[parent];
            }
            return new SourceVector3(pivot.x, pivot.y, pivot.z);
        }

        public int GetParentIndex(int index)
        {
            GetJoint(index);
            return parentIndices[index];
        }

        // Matrix outputs, not local-rotation overrides: upper and lower branches can have different pelvises.
        public void ApplyContinuousSourcePose(SourceEvaluatedPose pose)
        {
            if (pose == null || pose.Joints.Count != JointCount || JointCount != SourceTiming.JointCount)
                throw new InvalidOperationException("Continuous source pose/rig mapping requires fifteen joints.");
            if (pose.IsRawAffine)
            {
                ApplyRawAffinePose(pose);
                appliedPose = pose;
                HasContinuousPose = true;
                return;
            }
            ReleaseAffineMeshes();
            for (int i = 0; i < JointCount; i++) joints[i].localScale = Vector3.one;
            joints[0].localScale = Vector3.one * pose.ModelScale;
            for (int i = 0; i < JointCount; i++)
            {
                SourceJointTransform joint = pose.Joints[i];
                SourceVector3 p = joint.Position;
                SourceRotation q = joint.Rotation;
                joints[i].SetPositionAndRotation(transform.TransformPoint(new Vector3(p.X, p.Y, p.Z)),
                    transform.rotation * new Quaternion(q.X, q.Y, q.Z, q.W));
            }
            HasContinuousPose = true;
            appliedPose = pose;
        }

        public SourceJointTransform GetAppliedJointTransform(int index)
        {
            if (!HasContinuousPose || appliedPose == null)
                throw new InvalidOperationException("No continuous source pose is applied.");
            if (index < 0 || index >= appliedPose.Joints.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return appliedPose.Joints[index];
        }

        private void ApplyRawAffinePose(SourceEvaluatedPose pose)
        {
            for (int i = 0; i < JointCount; i++)
                PrepareAffineMesh(AffineMeshFor(joints[i].GetComponent<MeshFilter>()), pose.Joints[i].AffineBasis);
            if (TeamAppearance != null && numberFilter == null)
                throw new InvalidOperationException("Raw affine team rendering requires its torso-number mesh.");
            if (numberFilter != null) PrepareAffineMesh(AffineMeshFor(numberFilter), pose.Joints[1].AffineBasis);
            for (int i = 0; i < JointCount; i++) joints[i].localScale = Vector3.one;
            for (int i = 0; i < JointCount; i++)
            {
                SourceVector3 position = pose.Joints[i].Position;
                joints[i].SetPositionAndRotation(transform.TransformPoint(new Vector3(position.X, position.Y, position.Z)),
                    transform.rotation);
            }
            foreach (AffineMesh mesh in affineMeshes)
            {
                mesh.Clone.vertices = mesh.PendingVertices;
                if (mesh.PendingNormals.Length != 0) mesh.Clone.normals = mesh.PendingNormals;
                if (mesh.PendingTangents.Length != 0) mesh.Clone.tangents = mesh.PendingTangents;
                mesh.Clone.RecalculateBounds();
                mesh.Filter.sharedMesh = mesh.Clone;
            }
        }

        private AffineMesh AffineMeshFor(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Raw affine rendering requires the existing limb/number MeshFilter.");
            foreach (AffineMesh cached in affineMeshes)
                if (cached.Filter == filter)
                {
                    if (filter.sharedMesh != cached.Clone && filter.sharedMesh != cached.Source)
                        throw new InvalidOperationException("Raw affine mesh binding changed outside appearance rebinding.");
                    return cached;
                }
            Mesh source = filter.sharedMesh;
            if (!source.isReadable)
                throw new NotSupportedException("Raw affine rendering requires readable mesh attributes; no asset reimport fallback.");
            Vector3[] vertices = source.vertices, normals = source.normals;
            Vector4[] tangents = source.tangents;
            if (vertices.Length == 0 || normals.Length != 0 && normals.Length != vertices.Length ||
                tangents.Length != 0 && tangents.Length != vertices.Length)
                throw new NotSupportedException("Raw affine mesh attribute counts are unsupported.");
            var mesh = new AffineMesh {
                Filter = filter, Source = source, Clone = Instantiate(source),
                Vertices = vertices, Normals = normals, Tangents = tangents,
                PendingVertices = new Vector3[vertices.Length], PendingNormals = new Vector3[normals.Length],
                PendingTangents = new Vector4[tangents.Length]
            };
            mesh.Clone.name = source.name + "/instance-raw-affine";
            affineMeshes.Add(mesh);
            return mesh;
        }

        private static void PrepareAffineMesh(AffineMesh mesh, SourceAffineBasis basis)
        {
            Matrix4x4 linear = Matrix4x4.identity;
            linear.m00 = basis.M00; linear.m01 = basis.M01; linear.m02 = basis.M02;
            linear.m10 = basis.M10; linear.m11 = basis.M11; linear.m12 = basis.M12;
            linear.m20 = basis.M20; linear.m21 = basis.M21; linear.m22 = basis.M22;
            bool attributes = mesh.Normals.Length != 0 || mesh.Tangents.Length != 0;
            float determinant = attributes ? linear.determinant : 1f;
            if (attributes && (determinant == 0f || float.IsNaN(determinant) || float.IsInfinity(determinant)))
                throw new NotSupportedException("Raw affine lit normal/tangent consumer requires a finite nonsingular basis.");
            Matrix4x4 normals = attributes ? linear.inverse.transpose : Matrix4x4.identity;
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                Vector3 point = mesh.Vertices[i];
                SourceVector3 transformed = basis.TransformVector(new SourceVector3(point.x, point.y, point.z));
                SourceAnimationSampling.Finite(transformed, "raw deformed mesh vertex");
                mesh.PendingVertices[i] = new Vector3(transformed.X, transformed.Y, transformed.Z);
            }
            for (int i = 0; i < mesh.Normals.Length; i++)
                mesh.PendingNormals[i] = UnitAttribute(normals.MultiplyVector(mesh.Normals[i]));
            for (int i = 0; i < mesh.Tangents.Length; i++)
            {
                Vector4 tangent = mesh.Tangents[i];
                if (float.IsNaN(tangent.w) || float.IsInfinity(tangent.w))
                    throw new NotSupportedException("Raw affine tangent handedness must be finite.");
                Vector3 direction = UnitAttribute(linear.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)));
                mesh.PendingTangents[i] = new Vector4(direction.x, direction.y, direction.z,
                    determinant < 0f ? -tangent.w : tangent.w);
            }
        }

        private static Vector3 UnitAttribute(Vector3 value)
        {
            double lengthSquared = (double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z;
            if (!(lengthSquared > 0d) || double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared))
                throw new NotSupportedException("Raw affine normal/tangent transform is nonfinite or zero.");
            double inverse = 1d / Math.Sqrt(lengthSquared);
            return new Vector3((float)(value.x * inverse), (float)(value.y * inverse), (float)(value.z * inverse));
        }

        private void ReleaseAffineMeshes()
        {
            foreach (AffineMesh mesh in affineMeshes)
            {
                if (mesh.Filter != null && mesh.Filter.sharedMesh == mesh.Clone) mesh.Filter.sharedMesh = mesh.Source;
                if (mesh.Clone != null)
                {
                    if (Application.isPlaying) Destroy(mesh.Clone);
                    else DestroyImmediate(mesh.Clone);
                }
            }
            affineMeshes.Clear();
        }

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
            ReleaseAffineMeshes();
            appliedPose = null;
            for (int i = 0; i < JointCount; i++)
            {
                joints[i].localPosition = restPositions[i];
                joints[i].localRotation = Quaternion.identity;
                joints[i].localScale = Vector3.one;
            }
            stage = ImportStage;
            HasContinuousPose = false;
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
            ReleaseAffineMeshes();
            appliedPose = null;
            for (int i = 0; i < JointCount; i++)
            {
                joints[i].localPosition = restPositions[i];
                SetUnityLocalRotation(i, unityLocalRotations[i]);
            }
            joints[0].localPosition += anchoredRootOffset;
            stage = StaticPoseStage; poseSequence = sequence; poseSourceFrame = sourceFrame;
            HasContinuousPose = false;
            poseSha256 = sourceSha256; poseSource = originalDecodedPose;
            ValidateGeneratedRig();
        }
    }
}
