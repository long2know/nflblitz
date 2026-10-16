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
            SourceJointPose pose = Sample(library, layer.Sample).Pose;
            return layer.TransitionFromPose == null ? pose : Blend(layer.TransitionFromPose, pose, layer.Transition01);
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
    }

    public readonly struct SourceJointTransform
    {
        public SourceVector3 Position { get; }
        public SourceRotation Rotation { get; }
        public SourceJointTransform(SourceVector3 position, SourceRotation rotation) { Position = position; Rotation = rotation; }
    }

    public sealed class SourceEvaluatedPose
    {
        public ReadOnlyCollection<SourceJointTransform> Joints { get; private set; }
        public float ModelScale { get; private set; }
        internal SourceEvaluatedPose(SourceJointTransform[] joints, float modelScale)
        {
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
            SourceVector3 rotated = Rotate(joint.Rotation, new SourceVector3(unityMeshLocal.X * modelScale,
                unityMeshLocal.Y * modelScale, unityMeshLocal.Z * modelScale));
            return Add(joint.Position, rotated);
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
