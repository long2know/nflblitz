using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourceCatchGeometry : ISourceActorGeometry, ISourceActorCameraGeometry
    {
        private readonly ISourceAnimationLibrary library;
        private readonly SourceVector3[] referencePivots;

        public SourceCatchGeometry(ISourceAnimationLibrary library, IReadOnlyList<SourceVector3> unityReferencePivots)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            if (unityReferencePivots == null)
                throw new ArgumentNullException(nameof(unityReferencePivots));
            if (unityReferencePivots.Count != SourceTiming.JointCount)
                throw new ArgumentException("Source gameplay needs the actual fifteen imported reference pivots.",
                    nameof(unityReferencePivots));
            referencePivots = new SourceVector3[SourceTiming.JointCount];
            for (int i = 0; i < referencePivots.Length; i++)
            {
                SourceAnimationSampling.Finite(unityReferencePivots[i], "source gameplay reference pivot " + i);
                referencePivots[i] = unityReferencePivots[i];
            }
        }

        public SourceVector3 WorldJoint(PlayerView actor, int sourceJointIndex)
        {
            if (sourceJointIndex < 0 || sourceJointIndex >= SourceTiming.JointCount)
                throw new ArgumentOutOfRangeException(nameof(sourceJointIndex));
            SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(library, referencePivots, actor, actor, 1f);
            SourceVector3 joint = pose.Joints[sourceJointIndex].Position;
            return new SourceVector3(SourceMatchRules.SourceWorldFromAbsoluteYards(actor.Position.X) + joint.X,
                actor.Height * SourceTiming.SourceUnitsPerYard + joint.Y,
                actor.Position.Z * SourceTiming.SourceUnitsPerYard - joint.Z);
        }

        public IReadOnlyList<SourceVector3> CameraJoints(PlayerView actor, SourceVector3 canonicalRoot,
            SourceMovementCameraFrame camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(library, referencePivots, actor, actor, 1f);
            ReadOnlyCollection<float> matrix = camera.Transform;
            float rx = canonicalRoot.X - camera.Position.X;
            float ry = canonicalRoot.Y - camera.Position.Y;
            float rz = canonicalRoot.Z - camera.Position.Z;
            float x = matrix[0] * rx + matrix[1] * ry + matrix[2] * rz + matrix[3];
            float y = matrix[4] * rx + matrix[5] * ry + matrix[6] * rz + matrix[7];
            float z = matrix[8] * rx + matrix[9] * ry + matrix[10] * rz + matrix[11];
            var joints = new SourceVector3[SourceTiming.JointCount];
            for (int i = 0; i < joints.Length; i++)
            {
                SourceVector3 local = pose.Joints[i].Position;
                joints[i] = new SourceVector3(
                    matrix[0] * local.X + matrix[1] * local.Y - matrix[2] * local.Z + x,
                    matrix[4] * local.X + matrix[5] * local.Y - matrix[6] * local.Z + y,
                    matrix[8] * local.X + matrix[9] * local.Y - matrix[10] * local.Z + z);
            }
            return Array.AsReadOnly(joints);
        }
    }
}
