using System;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceAnimationPlayer
    {
        private readonly SourcePlayerRig rig;
        private readonly ISourceAnimationLibrary library;
        private readonly SourceVector3[] referencePivots;
        public PlayerView AppliedView { get; private set; }
        public float RenderInterpolation01 { get; private set; }

        public SourceAnimationPlayer(SourcePlayerRig rig, ISourceAnimationLibrary library)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (library == null) throw new ArgumentNullException(nameof(library));
            this.rig = rig; this.library = library;
            rig.ValidateGeneratedRig();
            if (rig.JointCount != SourceTiming.JointCount)
                throw new InvalidOperationException("Original continuous player animation requires the fifteen-joint fbplyr_skel rig.");
            referencePivots = new SourceVector3[rig.JointCount];
            for (int i = 0; i < referencePivots.Length; i++)
            {
                if (rig.GetParentIndex(i) != SourceAnimationSkeleton.ParentIndices[i])
                    throw new InvalidOperationException("Original continuous player animation parent mapping changed at joint " + i + ".");
                referencePivots[i] = rig.GetUnityReferencePivot(i);
            }
        }

        public void Apply(PlayerView previous, PlayerView current, float renderInterpolation01)
        {
            SourceEvaluatedPose evaluated = SourceActorPoseSampler.Evaluate(library, referencePivots,
                previous, current, renderInterpolation01);
            rig.ApplyContinuousSourcePose(evaluated);
            AppliedView = current;
            RenderInterpolation01 = SourceActorPoseSampler.CanInterpolate(previous, current) ? renderInterpolation01 : 1;
        }
    }
}
