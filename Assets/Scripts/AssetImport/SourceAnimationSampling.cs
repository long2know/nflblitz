using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public readonly struct SourceSampledAnimation
    {
        public SourceJointPose Pose { get; }
        public SourceVector3 ScaledRoot { get; }
        public SourceSampledAnimation(SourceJointPose pose, SourceVector3 scaledRoot) { Pose = pose; ScaledRoot = scaledRoot; }
    }

    public static class SourceAnimationSampling
    {
        public static ISourcePoseInterpolation CreateOriginalPoseInterpolation(Func<int, float> readQuarterSine)
        {
            return new OriginalPoseInterpolation(readQuarterSine);
        }

        public static SourceSampledAnimation Sample(ISourceAnimationLibrary library, SourceAnimationSample sample)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (!sample.IsSpecified) throw new PlayerAssetException("A source animation sample must explicitly identify a sequence.");
            UnitInterval(sample.Interpolation01, "source frame interpolation");
            ISourceAnimationClip clip = library.GetClip(sample.SequenceName);
            SourceAnimationFrame from = clip.GetFrame(sample.SourceFrame);
            SourceAnimationFrame to = clip.GetFrame(sample.NextSourceFrame);
            return new SourceSampledAnimation(Blend(from.Pose, to.Pose, sample.Interpolation01),
                Lerp(from.ScaledRoot, to.ScaledRoot, sample.Interpolation01));
        }

        public static SourceJointPose SampleLayer(ISourceAnimationLibrary library, SourceAnimationLayer layer)
        {
            if (!layer.IsSpecified || string.IsNullOrEmpty(layer.ScriptSymbol))
                throw new PlayerAssetException("A source animation layer requires an explicit script, sequence, frame and output mask.");
            UnitInterval(layer.Transition01, "source transition");
            if (layer.PoseInterpolationPolicy == SourcePoseInterpolationPolicy.OriginalPortable)
            {
                layer.RequireSourcePoseInterpolation();
                SourceJointPose sourcePose = SampleOriginal(library, layer).Pose;
                return layer.TransitionFromPose == null ? sourcePose
                    : layer.BlendSourcePose(layer.TransitionFromPose, sourcePose, layer.Transition01);
            }
            if (layer.PoseInterpolationPolicy != SourcePoseInterpolationPolicy.LegacyNormalized)
                throw new PlayerAssetException("Unknown source pose interpolation policy.");
            SourceJointPose pose = Sample(library, layer.Sample).Pose;
            return layer.TransitionFromPose == null ? pose : Blend(layer.TransitionFromPose, pose, layer.Transition01);
        }

        private static SourceSampledAnimation SampleOriginal(ISourceAnimationLibrary library, SourceAnimationLayer layer)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            SourceAnimationSample sample = layer.Sample;
            UnitInterval(sample.Interpolation01, "source frame interpolation");
            ISourceAnimationClip clip = library.GetClip(sample.SequenceName);
            SourceAnimationFrame from = clip.GetFrame(sample.SourceFrame);
            SourceAnimationFrame to = clip.GetFrame(sample.NextSourceFrame);
            if (from.Pose == null || to.Pose == null)
                throw new PlayerAssetException("The selected source frame has no pose.");
            SourceJointPose pose = sample.Interpolation01 == 0f || sample.SourceFrame == sample.NextSourceFrame
                ? from.Pose : layer.BlendSourcePose(from.Pose, to.Pose, sample.Interpolation01);
            return new SourceSampledAnimation(pose, Lerp(from.ScaledRoot, to.ScaledRoot, sample.Interpolation01));
        }

        // Only the script decides looping/end holds. This seam maps exported script ordinals to authoring IDs.
        public static SourceAnimationSample AtSelectedOrdinal(ISourceAnimationClip clip, double ordinal, bool loop)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (double.IsNaN(ordinal) || double.IsInfinity(ordinal) || ordinal < 0 || clip.SelectedSourceFrames.Count == 0)
                throw new PlayerAssetException("Invalid selected animation ordinal.");
            int count = clip.SelectedSourceFrames.Count;
            double position = loop ? ordinal % count : Math.Min(ordinal, count - 1);
            int first = (int)Math.Floor(position);
            int next = first + 1 < count ? first + 1 : loop ? 0 : first;
            return new SourceAnimationSample(clip.SequenceName, clip.SelectedSourceFrames[first],
                clip.SelectedSourceFrames[next], (float)(position - first));
        }

        public static SourceJointPose Blend(SourceJointPose from, SourceJointPose to, float amount)
        {
            if (from == null || to == null) throw new ArgumentNullException("source transition pose");
            UnitInterval(amount, "pose blend");
            var rotations = new SourceRotation[SourceTiming.JointCount];
            for (int i = 0; i < rotations.Length; i++)
                rotations[i] = Slerp(from.Rotations[i], to.Rotations[i], amount);
            return new SourceJointPose(rotations);
        }

        public static SourceRotation Slerp(SourceRotation from, SourceRotation to, float amount)
        {
            UnitInterval(amount, "quaternion blend");
            from = Normalize(from); to = Normalize(to);
            double dot = Dot(from, to);
            if (dot < 0) { to = new SourceRotation(-to.X, -to.Y, -to.Z, -to.W); dot = -dot; }
            dot = Math.Min(1d, dot);
            double a = 1d - amount, b = amount;
            if (1d - dot > 0.00001d)
            {
                double angle = Math.Acos(dot), inverseSin = 1d / Math.Sin(angle);
                a = Math.Sin((1d - amount) * angle) * inverseSin;
                b = Math.Sin(amount * angle) * inverseSin;
            }
            return NormalizeResult(new SourceRotation((float)(a * from.X + b * to.X), (float)(a * from.Y + b * to.Y),
                (float)(a * from.Z + b * to.Z), (float)(a * from.W + b * to.W)));
        }

        public static SourceRotation Normalize(SourceRotation q)
        {
            double norm = Dot(q, q);
            if (double.IsNaN(norm) || double.IsInfinity(norm) || Math.Abs(norm - 1) > 0.0002)
                throw new PlayerAssetException("Source quaternion must be finite and unit length.");
            return NormalizeResult(q);
        }

        private static SourceRotation NormalizeResult(SourceRotation q)
        {
            double norm = Dot(q, q);
            if (double.IsNaN(norm) || double.IsInfinity(norm) || norm < 1e-12)
                throw new PlayerAssetException("Interpolated source quaternion must be finite and nonzero.");
            double inverse = 1d / Math.Sqrt(norm);
            return new SourceRotation((float)(q.X * inverse), (float)(q.Y * inverse), (float)(q.Z * inverse), (float)(q.W * inverse));
        }

        public static SourceVector3 Lerp(SourceVector3 from, SourceVector3 to, float amount)
        {
            UnitInterval(amount, "root interpolation"); Finite(from, "source root"); Finite(to, "source root");
            return new SourceVector3(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount,
                from.Z + (to.Z - from.Z) * amount);
        }

        public static FieldVector Heading(FieldVector layer, FieldVector actor)
        {
            FieldVector heading = layer.X == 0 && layer.Z == 0 ? actor : layer;
            double norm = (double)heading.X * heading.X + (double)heading.Z * heading.Z;
            if (double.IsNaN(norm) || double.IsInfinity(norm) || norm < 1e-12)
                throw new PlayerAssetException("Source actor/layer facing must explicitly identify a finite nonzero heading.");
            double inverse = 1d / Math.Sqrt(norm);
            return new FieldVector((float)(heading.X * inverse), (float)(heading.Z * inverse));
        }

        public static FieldVector InterpolateHeading(FieldVector from, FieldVector to, float amount)
        {
            UnitInterval(amount, "heading interpolation");
            from = Heading(from, from); to = Heading(to, to);
            double a = Math.Atan2(from.X, from.Z), b = Math.Atan2(to.X, to.Z);
            double difference = Math.Atan2(Math.Sin(b - a), Math.Cos(b - a));
            double angle = a + difference * amount;
            return new FieldVector((float)Math.Sin(angle), (float)Math.Cos(angle));
        }

        public static void UnitInterval(float value, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                throw new PlayerAssetException(label + " must be finite within [0,1].");
        }
        public static void Finite(SourceVector3 value, string label)
        {
            if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z))
                throw new PlayerAssetException(label + " must be finite.");
        }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static double Dot(SourceRotation a, SourceRotation b)
        {
            return (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W;
        }

        private sealed class OriginalPoseInterpolation : ISourcePoseInterpolation, ISourcePoseAffineHeading
        {
            private const float Pi = 3.1415926f;
            private const float Epsilon = 0.00001f;
            private const float HalfPi = Pi * 0.5f;
            private readonly Func<int, float> readQuarterSine;

            internal OriginalPoseInterpolation(Func<int, float> readQuarterSine)
            {
                this.readQuarterSine = readQuarterSine ?? throw new ArgumentNullException(nameof(readQuarterSine));
            }

            public FieldVector Facing(int sourceFacing)
            {
                return new FieldVector(HeadingSine(sourceFacing), HeadingSine((sourceFacing & 1023) + 256));
            }

            private float HeadingSine(int heading)
            {
                int value = heading & 1023;
                if (value <= 256) return QuarterSine(value);
                if (value <= 512) return QuarterSine(512 - value);
                if (value <= 768) return -QuarterSine(value - 512);
                return -QuarterSine(1024 - value);
            }

            public SourceJointPose Blend(SourceJointPose source, SourceJointPose destination, float amount)
            {
                if (destination == null) throw new ArgumentNullException(nameof(destination));
                if (amount >= 1f || source == null)
                    return destination;
                UnitInterval(amount, "source pose blend");
                var rotations = new SourceRotation[SourceTiming.JointCount];
                for (int i = 0; i < rotations.Length; i++)
                {
                    SourceRotation p = source.Rotations[i], q = destination.Rotations[i];
                    float difference = (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)
                        + (p.Z - q.Z) * (p.Z - q.Z) + (p.W - q.W) * (p.W - q.W);
                    float opposite = (p.X + q.X) * (p.X + q.X) + (p.Y + q.Y) * (p.Y + q.Y)
                        + (p.Z + q.Z) * (p.Z + q.Z) + (p.W + q.W) * (p.W + q.W);
                    if (!(difference < opposite))
                        p = new SourceRotation(-p.X, -p.Y, -p.Z, -p.W);
                    rotations[i] = Slerp(p, q, amount);
                }
                return new SourceJointPose(rotations);
            }

            public SourceRotation Slerp(SourceRotation source, SourceRotation destination, float amount)
            {
                if (amount == 0f) return source;
                if (amount == 1f) return destination;
                UnitInterval(amount, "source quaternion blend");
                RequireFinite(source);
                RequireFinite(destination);
                float cosine = source.X * destination.X + source.Y * destination.Y
                    + source.Z * destination.Z + source.W * destination.W;
                float from, to;
                if (1f + cosine > Epsilon)
                {
                    if (1f - cosine > Epsilon)
                    {
                        float angle = Pi / 2f - AsinRadians(cosine);
                        float denominator = SinRadians(angle);
                        from = SinRadians((1f - amount) * angle) / denominator;
                        to = SinRadians(amount * angle) / denominator;
                    }
                    else
                    {
                        from = 1f - amount;
                        to = amount;
                    }
                }
                else
                {
                    destination = new SourceRotation(-source.Y, source.X, -source.W, source.Z);
                    from = SinRadians((1f - amount) * HalfPi);
                    to = SinRadians(amount * HalfPi);
                }
                return new SourceRotation(from * source.X + to * destination.X,
                    from * source.Y + to * destination.Y, from * source.Z + to * destination.Z,
                    from * source.W + to * destination.W);
            }

            private float QuarterSine(int index)
            {
                if (index < 0 || index > 256)
                    throw new NotSupportedException("Original inverse-sine table index is unqualified.");
                float value = readQuarterSine(index);
                if (!IsFinite(value) || value < 0f || value > 1f)
                    throw new InvalidOperationException("Original inverse-sine table value is unavailable.");
                return value;
            }

            private float AsinRadians(float value)
            {
                bool negative = value < 0f;
                float x = negative ? value * -1f : value;
                if (!(x >= 0f && x < 1f))
                    throw new NotSupportedException("Original inverse-sine table input is unqualified.");
                int low = 0, high = 256, mid = 0;
                bool bracketed = false;
                for (int budget = 0; budget < 32 && low < high; budget++)
                {
                    mid = (low + high) / 2;
                    if (QuarterSine(mid) < x)
                    {
                        if (QuarterSine(mid + 1) > x) { bracketed = true; break; }
                        low = mid + 1;
                    }
                    else
                    {
                        if (mid == 0)
                            throw new NotSupportedException("Original inverse-sine negative index is unqualified.");
                        if (QuarterSine(mid - 1) <= x) { mid--; bracketed = true; break; }
                        high = mid + 1;
                    }
                }
                if (!bracketed)
                    throw new InvalidOperationException("Original inverse-sine table does not bracket the input.");
                float lower = QuarterSine(mid), upper = QuarterSine(mid + 1);
                if (!(upper > lower))
                    throw new InvalidOperationException("Original inverse-sine table bracket is invalid.");
                float fraction = (x - lower) / (upper - lower);
                float angle = (mid + fraction) * Pi / 512f;
                return negative ? -angle : angle;
            }

            // pipe3D.c's float polynomial deliberately floors near-zero arguments.
            private static float SinRadians(float value)
            {
                if (value < 0.0001f && value >= 0f) value = 0.0001f;
                if (value > -0.0001f && value < 0f) value = -0.0001f;
                bool negative = value < 0f;
                if (negative) value = -value;
                if (value >= Pi / 2f) value = Pi - value;
                float square = value * value;
                float cube = value * square, fifth = cube * square, seventh = fifth * square;
                float result = value - cube * 0.166666f + fifth * 0.0083333f - seventh * 0.00020f;
                return negative ? -result : result;
            }

            private static void RequireFinite(SourceRotation value)
            {
                if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z) || !IsFinite(value.W))
                    throw new PlayerAssetException("Source quaternion components must be finite.");
            }
        }
    }

    public readonly struct SourceAffineBasis
    {
        public float M00 { get; }
        public float M01 { get; }
        public float M02 { get; }
        public float M10 { get; }
        public float M11 { get; }
        public float M12 { get; }
        public float M20 { get; }
        public float M21 { get; }
        public float M22 { get; }

        public SourceAffineBasis(float m00, float m01, float m02, float m10, float m11, float m12,
            float m20, float m21, float m22)
        {
            SourceAnimationSampling.Finite(new SourceVector3(m00, m01, m02), "raw affine row 0");
            SourceAnimationSampling.Finite(new SourceVector3(m10, m11, m12), "raw affine row 1");
            SourceAnimationSampling.Finite(new SourceVector3(m20, m21, m22), "raw affine row 2");
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
            M20 = m20; M21 = m21; M22 = m22;
        }

        public SourceVector3 TransformVector(SourceVector3 value)
        {
            return new SourceVector3(M00 * value.X + M01 * value.Y + M02 * value.Z,
                M10 * value.X + M11 * value.Y + M12 * value.Z,
                M20 * value.X + M21 * value.Y + M22 * value.Z);
        }
    }

    public readonly struct SourceJointTransform
    {
        private readonly SourceRotation rotation;
        private readonly SourceAffineBasis affineBasis;
        public SourceVector3 Position { get; }
        public bool IsRawAffine { get; }
        public float AppliedModelScale { get; }
        public SourceRotation Rotation
        {
            get
            {
                if (IsRawAffine) throw new InvalidOperationException("A raw affine joint has no quaternion Rotation.");
                return rotation;
            }
        }
        public SourceAffineBasis AffineBasis
        {
            get
            {
                if (!IsRawAffine) throw new InvalidOperationException("A legacy joint has no raw affine basis.");
                return affineBasis;
            }
        }
        public SourceJointTransform(SourceVector3 position, SourceRotation rotation)
        {
            Position = position; this.rotation = rotation;
            affineBasis = default(SourceAffineBasis); IsRawAffine = false; AppliedModelScale = 0f;
        }
        internal SourceJointTransform(SourceVector3 position, SourceAffineBasis basis, float modelScale)
        {
            SourceAnimationSampling.Finite(position, "raw joint position");
            Position = position; affineBasis = basis; AppliedModelScale = modelScale;
            rotation = default(SourceRotation); IsRawAffine = true;
        }
        public void RequireModelScale(float modelScale)
        {
            if (IsRawAffine && modelScale != AppliedModelScale)
                throw new InvalidOperationException("Raw affine basis already includes its evaluated model scale.");
        }
    }

    public sealed class SourceEvaluatedPose
    {
        public ReadOnlyCollection<SourceJointTransform> Joints { get; private set; }
        public float ModelScale { get; private set; }
        public bool IsRawAffine { get; private set; }
        internal SourceEvaluatedPose(SourceJointTransform[] joints, float modelScale)
        {
            if (joints == null || joints.Length != SourceTiming.JointCount)
                throw new PlayerAssetException("An evaluated pose requires fifteen joints.");
            IsRawAffine = joints[0].IsRawAffine;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].IsRawAffine != IsRawAffine)
                    throw new PlayerAssetException("An evaluated pose cannot mix raw and legacy joints.");
                joints[i].RequireModelScale(modelScale);
            }
            Joints = Array.AsReadOnly(joints); ModelScale = modelScale;
        }
    }

    // Outputs are Unity-basis pivot-local mesh matrices, in actor-scaled source units, before yards/placement.
    public static class SourceAnimationMatrices
    {
        public static SourceEvaluatedPose Evaluate(IReadOnlyList<SourceVector3> unityReferencePivots,
            SourceJointPose primaryPose, FieldVector primaryHeading, SourceVector3 resolvedSourceRoot,
            float modelScale = 1f, SourceJointPose secondaryPose = null,
            FieldVector secondaryHeading = default(FieldVector),
            ushort primaryMask = SourceTiming.AllJoints, ushort secondaryMask = 0)
        {
            if (unityReferencePivots == null || unityReferencePivots.Count != SourceTiming.JointCount || primaryPose == null)
                throw new PlayerAssetException("Source pose evaluation requires fifteen reference pivots and a primary pose.");
            if (float.IsNaN(modelScale) || float.IsInfinity(modelScale) || modelScale <= 0 || modelScale > 10)
                throw new PlayerAssetException("Roster model scale must be finite within (0,10].");
            SourceAnimationSampling.Finite(resolvedSourceRoot, "resolved actor root offset");
            if (secondaryPose == null ? primaryMask != SourceTiming.AllJoints || secondaryMask != 0 :
                primaryMask != SourceTiming.LowerBodyJoints || secondaryMask != SourceTiming.UpperBodyJoints)
                throw new PlayerAssetException("Source matrix output masks must be all joints, or disjoint lower 0x7e01 / upper 0x01fe.");
            var root = new SourceVector3(resolvedSourceRoot.X, resolvedSourceRoot.Y, -resolvedSourceRoot.Z);
            SourceJointTransform[] lower = Branch(unityReferencePivots, primaryPose, primaryHeading, root, modelScale);
            if (secondaryPose != null)
            {
                // The upper pelvis is evaluated even though its matrix is not drawn.
                SourceJointTransform[] upper = Branch(unityReferencePivots, secondaryPose, secondaryHeading, root, modelScale);
                for (int i = 0; i < lower.Length; i++)
                    if ((secondaryMask & (1 << i)) != 0) lower[i] = upper[i];
            }
            return new SourceEvaluatedPose(lower, modelScale);
        }

        public static SourceVector3 TransformVertex(SourceJointTransform joint, SourceVector3 unityMeshLocal, float modelScale)
        {
            if (joint.IsRawAffine)
            {
                joint.RequireModelScale(modelScale);
                SourceAnimationSampling.Finite(unityMeshLocal, "raw mesh local vertex");
                SourceVector3 point = Add(joint.Position, joint.AffineBasis.TransformVector(unityMeshLocal));
                SourceAnimationSampling.Finite(point, "raw transformed vertex");
                return point;
            }
            SourceVector3 rotated = Rotate(joint.Rotation, new SourceVector3(unityMeshLocal.X * modelScale,
                unityMeshLocal.Y * modelScale, unityMeshLocal.Z * modelScale));
            return Add(joint.Position, rotated);
        }

        public static SourceEvaluatedPose EvaluateSource(IReadOnlyList<SourceVector3> unityReferencePivots,
            SourceJointPose primaryPose, SourceAnimationLayer primaryLayer, FieldVector primaryHeading,
            SourceVector3 resolvedSourceRoot, float modelScale = 1f, SourceJointPose secondaryPose = null,
            SourceAnimationLayer secondaryLayer = default(SourceAnimationLayer),
            FieldVector secondaryHeading = default(FieldVector))
        {
            primaryLayer.RequireSourceAffineBinding();
            if (secondaryPose != null) primaryLayer.RequireSameSourceAffineBinding(secondaryLayer);
            else if (secondaryLayer.IsSpecified)
                throw new PlayerAssetException("A secondary layer requires its source pose.");
            if (unityReferencePivots == null || unityReferencePivots.Count != SourceTiming.JointCount || primaryPose == null)
                throw new PlayerAssetException("Source pose evaluation requires fifteen reference pivots and a primary pose.");
            if (float.IsNaN(modelScale) || float.IsInfinity(modelScale) || modelScale <= 0 || modelScale > 10)
                throw new PlayerAssetException("Roster model scale must be finite within (0,10].");
            SourceAnimationSampling.Finite(resolvedSourceRoot, "resolved actor root offset");
            if (!primaryLayer.IsSpecified || string.IsNullOrEmpty(primaryLayer.ScriptSymbol) ||
                (secondaryPose == null ? primaryLayer.JointMask != SourceTiming.AllJoints :
                primaryLayer.JointMask != SourceTiming.LowerBodyJoints ||
                secondaryLayer.JointMask != SourceTiming.UpperBodyJoints ||
                string.IsNullOrEmpty(secondaryLayer.ScriptSymbol)))
                throw new PlayerAssetException("Source matrix output masks must be all joints, or disjoint lower 0x7e01 / upper 0x01fe.");
            SourceJointTransform[] upper = secondaryPose == null ? null :
                RawBranch(unityReferencePivots, secondaryPose, secondaryHeading, resolvedSourceRoot, modelScale);
            SourceJointTransform[] lower = RawBranch(unityReferencePivots, primaryPose, primaryHeading,
                resolvedSourceRoot, modelScale);
            if (upper != null)
                for (int i = 0; i < lower.Length; i++)
                    if ((secondaryLayer.JointMask & (1 << i)) != 0) lower[i] = upper[i];
            return new SourceEvaluatedPose(lower, modelScale);
        }

        private static SourceJointTransform[] RawBranch(IReadOnlyList<SourceVector3> pivots,
            SourceJointPose pose, FieldVector heading, SourceVector3 root, float scale)
        {
            SourceAnimationSampling.Finite(new SourceVector3(heading.X, 0f, heading.Z), "source affine heading");
            if (heading.X == 0f && heading.Z == 0f)
                throw new PlayerAssetException("Source affine heading must be nonzero.");
            var actor = new float[12];
            actor[0] = heading.Z * scale; actor[2] = heading.X * scale;
            actor[5] = scale; actor[8] = -heading.X * scale; actor[10] = heading.Z * scale;
            actor[3] = root.X; actor[7] = root.Y; actor[11] = root.Z;
            var completed = new float[SourceTiming.JointCount][];
            var joints = new SourceJointTransform[SourceTiming.JointCount];
            for (int node = 0; node < joints.Length; node++)
            {
                SourceAnimationSampling.Finite(pivots[node], "reference pivot");
                float px = pivots[node].X, py = pivots[node].Y, pz = -pivots[node].Z;
                int parentIndex = SourceAnimationSkeleton.ParentIndices[node];
                float[] parent = parentIndex < 0 ? actor : completed[parentIndex];
                var position = new SourceVector3(
                    parent[0] * px + parent[1] * py + parent[2] * pz + parent[3],
                    parent[4] * px + parent[5] * py + parent[6] * pz + parent[7],
                    -(parent[8] * px + parent[9] * py + parent[10] * pz + parent[11]));
                float[] local = RawRotation(pose.Rotations[node]);
                local[3] = px; local[7] = py; local[11] = pz;
                float[] matrix = RawProduct(parent, local);
                matrix[3] -= matrix[0] * px + matrix[1] * py + matrix[2] * pz;
                matrix[7] -= matrix[4] * px + matrix[5] * py + matrix[6] * pz;
                matrix[11] -= matrix[8] * px + matrix[9] * py + matrix[10] * pz;
                SourceAnimationSampling.Finite(new SourceVector3(matrix[3], matrix[7], matrix[11]), "raw model-origin translation");
                var basis = new SourceAffineBasis(matrix[0], matrix[1], -matrix[2],
                    matrix[4], matrix[5], -matrix[6], -matrix[8], -matrix[9], matrix[10]);
                joints[node] = new SourceJointTransform(position, basis, scale);
                completed[node] = matrix;
            }
            return joints;
        }

        private static float[] RawRotation(SourceRotation q)
        {
            SourceAnimationSampling.Finite(new SourceVector3(q.X, q.Y, q.Z), "raw quaternion");
            if (float.IsNaN(q.W) || float.IsInfinity(q.W))
                throw new PlayerAssetException("Raw quaternion must be finite.");
            float x2 = q.X * 2.0f, y2 = q.Y * 2.0f, z2 = q.Z * 2.0f;
            float wx = q.W * x2, wy = q.W * y2, wz = q.W * z2;
            float xx = q.X * x2, xy = q.X * y2, xz = q.X * z2;
            float yy = q.Y * y2, yz = q.Y * z2, zz = q.Z * z2;
            var matrix = new float[12];
            matrix[0] = 1.0f - (yy + zz); matrix[1] = xy - wz; matrix[2] = xz + wy;
            matrix[4] = xy + wz; matrix[5] = 1.0f - (xx + zz); matrix[6] = yz - wx;
            matrix[8] = xz - wy; matrix[9] = yz + wx; matrix[10] = 1.0f - (xx + yy);
            return matrix;
        }

        private static float[] RawProduct(float[] parent, float[] local)
        {
            var result = new float[12];
            for (int row = 8; row >= 0; row -= 4)
            {
                result[row] = parent[row] * local[0] + parent[row + 1] * local[4] + parent[row + 2] * local[8];
                result[row + 1] = parent[row] * local[1] + parent[row + 1] * local[5] + parent[row + 2] * local[9];
                result[row + 2] = parent[row] * local[2] + parent[row + 1] * local[6] + parent[row + 2] * local[10];
                result[row + 3] = parent[row] * local[3] + parent[row + 1] * local[7] + parent[row + 2] * local[11] + parent[row + 3];
            }
            return result;
        }

        private static SourceJointTransform[] Branch(IReadOnlyList<SourceVector3> pivots, SourceJointPose pose,
            FieldVector direction, SourceVector3 root, float scale)
        {
            direction = SourceAnimationSampling.Heading(direction, direction);
            double angle = Math.Atan2(direction.X, direction.Z) / 2;
            // pipe3D.c roty is a source-basis rotation; C*R*C^-1 reverses its Y axis.
            var heading = new SourceRotation(0, -(float)Math.Sin(angle), 0, (float)Math.Cos(angle));
            var output = new SourceJointTransform[SourceTiming.JointCount];
            for (int i = 0; i < output.Length; i++)
            {
                SourceAnimationSampling.Finite(pivots[i], "reference pivot");
                int parent = SourceAnimationSkeleton.ParentIndices[i];
                SourceVector3 parentPivot = parent < 0 ? default(SourceVector3) : pivots[parent];
                var offset = new SourceVector3((pivots[i].X - parentPivot.X) * scale,
                    (pivots[i].Y - parentPivot.Y) * scale, (pivots[i].Z - parentPivot.Z) * scale);
                SourceRotation parentRotation = parent < 0 ? heading : output[parent].Rotation;
                SourceVector3 parentPosition = parent < 0 ? root : output[parent].Position;
                SourceRotation q = SourceAnimationSampling.Normalize(pose.Rotations[i]);
                var unity = new SourceRotation(-q.X, -q.Y, q.Z, q.W);
                output[i] = new SourceJointTransform(Add(parentPosition, Rotate(parentRotation, offset)),
                    Multiply(parentRotation, unity));
            }
            return output;
        }

        private static SourceRotation Multiply(SourceRotation a, SourceRotation b)
        {
            return SourceAnimationSampling.Normalize(new SourceRotation(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z));
        }
        private static SourceVector3 Rotate(SourceRotation q, SourceVector3 p)
        {
            float tx = 2 * (q.Y * p.Z - q.Z * p.Y), ty = 2 * (q.Z * p.X - q.X * p.Z), tz = 2 * (q.X * p.Y - q.Y * p.X);
            return new SourceVector3(p.X + q.W * tx + q.Y * tz - q.Z * ty,
                p.Y + q.W * ty + q.Z * tx - q.X * tz, p.Z + q.W * tz + q.X * ty - q.Y * tx);
        }
        private static SourceVector3 Add(SourceVector3 a, SourceVector3 b) { return new SourceVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    }
}
