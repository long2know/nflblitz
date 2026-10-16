using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourceCatchGeometry : ISourceActorGeometry
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
    }
}
