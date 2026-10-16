using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public static class SourceActorPoseSampler
    {
        public static bool CanInterpolate(PlayerView previous, PlayerView current)
        {
            long difference = current.Animation.SourceTick - previous.Animation.SourceTick;
            double x = current.Position.X - previous.Position.X, z = current.Position.Z - previous.Position.Z;
            return previous.Id == current.Id && previous.Animation.IsSpecified && current.Animation.IsSpecified &&
                previous.Animation.SourceTicksPerSecond == current.Animation.SourceTicksPerSecond &&
                difference >= 0 && difference <= 2 && x * x + z * z < 100 &&
                !float.IsNaN(previous.Height) && !float.IsInfinity(previous.Height) &&
                !(current.Animation.Action == ActorAction.PreSnap && previous.Animation.Action != ActorAction.PreSnap);
        }

        public static SourceEvaluatedPose Evaluate(ISourceAnimationLibrary library,
            IReadOnlyList<SourceVector3> unityReferencePivots, PlayerView previous, PlayerView current, float renderInterpolation01)
        {
            SourceAnimationSampling.UnitInterval(renderInterpolation01, "render interpolation");
            ActorAnimationView animation = current.Animation;
            SourceAnimationSampling.Finite(new SourceVector3(current.Position.X, current.Height, current.Position.Z),
                "Source actor " + current.Id + " placement/height");
            if (!animation.IsSpecified || animation.SourceTick < 0 || animation.SourceTicksPerSecond <= 0 ||
                animation.Action == ActorAction.Unspecified || current.Role == ActorRole.Unspecified)
                throw new PlayerAssetException("Source actor " + current.Id + " (" + current.Role + "/" + animation.Action +
                    ") has no explicit animation state. No static pose will be substituted.");
            bool interpolate = CanInterpolate(previous, current);
            float amount = interpolate ? renderInterpolation01 : 1;
            try
            {
                SourceJointPose primary = Layer(library, previous.Animation.Primary, animation.Primary, amount, interpolate);
                FieldVector primaryHeading = LayerHeading(previous, current, false, amount, interpolate);
                SourceJointPose secondary = animation.Secondary.IsSpecified ?
                    Layer(library, previous.Animation.Secondary, animation.Secondary, amount, interpolate) : null;
                FieldVector secondaryHeading = secondary == null ? default(FieldVector) :
                    LayerHeading(previous, current, true, amount, interpolate);
                SourceVector3 root = interpolate ? SourceAnimationSampling.Lerp(previous.Animation.RootOffsetSourceUnits,
                    animation.RootOffsetSourceUnits, amount) : animation.RootOffsetSourceUnits;
                return SourceAnimationMatrices.Evaluate(unityReferencePivots, primary, primaryHeading, root,
                    current.ModelScale, secondary, secondaryHeading, animation.Primary.JointMask,
                    secondary == null ? (ushort)0 : animation.Secondary.JointMask);
            }
            catch (PlayerAssetException e)
            {
                throw new PlayerAssetException("Actor " + current.Id + " (" + current.Role + "/" + animation.Action +
                    ") sourceTick=" + animation.SourceTick + " script=" + animation.Primary.ScriptSymbol +
                    " sequence=" + animation.Primary.Sample.SequenceName + " frame=" + animation.Primary.Sample.SourceFrame +
                    " secondary=" + animation.Secondary.ScriptSymbol + ": " + e.Message);
            }
        }

        private static SourceJointPose Layer(ISourceAnimationLibrary library, SourceAnimationLayer previous,
            SourceAnimationLayer current, float amount, bool interpolate)
        {
            SourceJointPose pose = SourceAnimationSampling.SampleLayer(library, current);
            if (!interpolate || amount == 1 || !previous.IsSpecified ||
                previous.JointMask != current.JointMask ||
                !SameInstance(previous, current) && current.TransitionFromPose == null)
                return pose;
            return SourceAnimationSampling.Blend(SourceAnimationSampling.SampleLayer(library, previous), pose, amount);
        }

        private static FieldVector LayerHeading(PlayerView previous, PlayerView current, bool secondary,
            float amount, bool interpolate)
        {
            SourceAnimationLayer layer = secondary ? current.Animation.Secondary : current.Animation.Primary;
            FieldVector heading = SourceAnimationSampling.Heading(layer.FacingDirection, current.FacingDirection);
            SourceAnimationLayer old = secondary ? previous.Animation.Secondary : previous.Animation.Primary;
            if (!interpolate || amount == 1 || !old.IsSpecified || old.JointMask != layer.JointMask ||
                !SameInstance(old, layer) && layer.TransitionFromPose == null)
                return heading;
            return SourceAnimationSampling.InterpolateHeading(
                SourceAnimationSampling.Heading(old.FacingDirection, previous.FacingDirection), heading, amount);
        }

        private static bool SameInstance(SourceAnimationLayer previous, SourceAnimationLayer current)
        {
            return previous.InstanceId == current.InstanceId &&
                string.Equals(previous.ScriptSymbol, current.ScriptSymbol, StringComparison.Ordinal);
        }
    }
}
