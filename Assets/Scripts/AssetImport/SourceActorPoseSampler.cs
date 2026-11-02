using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public sealed class SourceLayerPoseObservation
    {
        public SourceJointPose CurrentSample { get; }
        public SourceJointPose PreviousSample { get; }
        public bool PreviousSampled { get; }
        public SourceJointPose PresentedPose { get; }
        public FieldVector Heading { get; }

        internal SourceLayerPoseObservation(SourceJointPose currentSample, SourceJointPose previousSample,
            bool previousSampled, SourceJointPose presentedPose, FieldVector heading)
        {
            CurrentSample = currentSample; PreviousSample = previousSample; PreviousSampled = previousSampled;
            PresentedPose = presentedPose; Heading = heading;
        }
    }

    public sealed class SourceActorPoseObservation
    {
        public int PreviousActorId { get; }
        public int ActorId { get; }
        public TeamId Team { get; }
        public ActorAnimationView PreviousAnimation { get; }
        public ActorAnimationView CurrentAnimation { get; }
        public float RenderInterpolation01 { get; }
        public bool CanInterpolate { get; }
        public float EffectiveInterpolation01 { get; }
        public SourceLayerPoseObservation Primary { get; }
        public SourceLayerPoseObservation Secondary { get; }
        public SourceVector3 RootOffsetSourceUnits { get; }
        public SourceEvaluatedPose EvaluatedPose { get; }

        internal SourceActorPoseObservation(PlayerView previous, PlayerView current, float renderInterpolation01,
            bool canInterpolate, float effectiveInterpolation01, SourceLayerPoseObservation primary,
            SourceLayerPoseObservation secondary, SourceVector3 root, SourceEvaluatedPose evaluatedPose)
        {
            PreviousActorId = previous.Id; ActorId = current.Id; Team = current.Team;
            PreviousAnimation = previous.Animation; CurrentAnimation = current.Animation;
            RenderInterpolation01 = renderInterpolation01; CanInterpolate = canInterpolate;
            EffectiveInterpolation01 = effectiveInterpolation01; Primary = primary; Secondary = secondary;
            RootOffsetSourceUnits = root; EvaluatedPose = evaluatedPose;
        }
    }

    public static class SourceActorPoseSampler
    {
        public static bool CanInterpolate(PlayerView previous, PlayerView current)
        {
            SourceActorContactView before = previous.Source.Contact, after = current.Source.Contact;
            if (ContactActive(before) || ContactActive(after) || before.IsSpecified != after.IsSpecified ||
                before.PuppetReleaseSourceTick != after.PuppetReleaseSourceTick ||
                previous.Animation.Action == ActorAction.Contact || current.Animation.Action == ActorAction.Contact)
                return false;
            long difference = current.Animation.SourceTick - previous.Animation.SourceTick;
            double x = current.Position.X - previous.Position.X, z = current.Position.Z - previous.Position.Z;
            return previous.Id == current.Id && previous.Animation.IsSpecified && current.Animation.IsSpecified &&
                previous.Animation.SourceTicksPerSecond == current.Animation.SourceTicksPerSecond &&
                difference >= 0 && difference <= 2 && x * x + z * z < 100 &&
                !float.IsNaN(previous.Height) && !float.IsInfinity(previous.Height) &&
                !(current.Animation.Action == ActorAction.PreSnap && previous.Animation.Action != ActorAction.PreSnap);
        }

        private static bool ContactActive(SourceActorContactView contact)
        {
            return contact.IsSpecified && (contact.PuppetPlayerId >= 0 || contact.AttackMode != 0 ||
                contact.HitPartnerPlayerId.HasValue && contact.HitPartnerPlayerId.Value >= 0);
        }

        public static SourceEvaluatedPose Evaluate(ISourceAnimationLibrary library,
            IReadOnlyList<SourceVector3> unityReferencePivots, PlayerView previous, PlayerView current, float renderInterpolation01)
        {
            return Evaluate(library, unityReferencePivots, previous, current, renderInterpolation01, false, out _);
        }

        public static SourceEvaluatedPose Evaluate(ISourceAnimationLibrary library,
            IReadOnlyList<SourceVector3> unityReferencePivots, PlayerView previous, PlayerView current,
            float renderInterpolation01, bool observe, out SourceActorPoseObservation observation)
        {
            observation = null;
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
                RequirePolicy(animation.Primary, animation.Primary.PoseInterpolationPolicy);
                if (animation.Secondary.IsSpecified)
                    RequirePolicy(animation.Secondary, animation.Primary.PoseInterpolationPolicy);
                SourceJointPose primary = Layer(library, previous.Animation.Primary, animation.Primary, amount, interpolate,
                    out SourceJointPose primaryCurrent, out SourceJointPose primaryPrevious, out bool primaryPreviousSampled);
                FieldVector primaryHeading = LayerHeading(previous, current, false, amount, interpolate);
                SourceJointPose secondaryCurrent = null, secondaryPrevious = null;
                bool secondaryPreviousSampled = false;
                SourceJointPose secondary = animation.Secondary.IsSpecified ?
                    Layer(library, previous.Animation.Secondary, animation.Secondary, amount, interpolate,
                        out secondaryCurrent, out secondaryPrevious, out secondaryPreviousSampled) : null;
                FieldVector secondaryHeading = secondary == null ? default(FieldVector) :
                    LayerHeading(previous, current, true, amount, interpolate);
                SourceVector3 root = interpolate ? SourceAnimationSampling.Lerp(previous.Animation.RootOffsetSourceUnits,
                    animation.RootOffsetSourceUnits, amount) : animation.RootOffsetSourceUnits;
                SourceEvaluatedPose evaluated;
                if (animation.Primary.PoseInterpolationPolicy == SourcePoseInterpolationPolicy.OriginalPortable)
                    evaluated = SourceAnimationMatrices.EvaluateSource(unityReferencePivots, primary, animation.Primary,
                        primaryHeading, root, current.ModelScale, secondary, animation.Secondary, secondaryHeading);
                else
                    evaluated = SourceAnimationMatrices.Evaluate(unityReferencePivots, primary, primaryHeading, root,
                        current.ModelScale, secondary, secondaryHeading, animation.Primary.JointMask,
                        secondary == null ? (ushort)0 : animation.Secondary.JointMask);
                if (observe)
                    observation = new SourceActorPoseObservation(previous, current, renderInterpolation01, interpolate, amount,
                        new SourceLayerPoseObservation(primaryCurrent, primaryPrevious, primaryPreviousSampled, primary, primaryHeading),
                        secondary == null ? null : new SourceLayerPoseObservation(secondaryCurrent, secondaryPrevious,
                            secondaryPreviousSampled, secondary, secondaryHeading), root, evaluated);
                return evaluated;
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
            SourceAnimationLayer current, float amount, bool interpolate, out SourceJointPose currentSample,
            out SourceJointPose previousSample, out bool previousSampled)
        {
            SourceJointPose pose = SourceAnimationSampling.SampleLayer(library, current);
            currentSample = pose; previousSample = null; previousSampled = false;
            if (!interpolate || amount == 1 || !previous.IsSpecified ||
                previous.JointMask != current.JointMask ||
                !SameInstance(previous, current) && current.TransitionFromPose == null)
                return pose;
            if (previous.PoseInterpolationPolicy != current.PoseInterpolationPolicy)
                throw new InvalidOperationException("Render interpolation cannot mix source numerical policies.");
            if (current.PoseInterpolationPolicy == SourcePoseInterpolationPolicy.OriginalPortable)
            {
                current.RequireSameSourceAffineBinding(previous);
                previousSample = SourceAnimationSampling.SampleLayer(library, previous);
                previousSampled = true;
                return current.BlendSourcePose(previousSample, pose, amount);
            }
            previousSample = SourceAnimationSampling.SampleLayer(library, previous);
            previousSampled = true;
            return SourceAnimationSampling.Blend(previousSample, pose, amount);
        }

        private static FieldVector LayerHeading(PlayerView previous, PlayerView current, bool secondary,
            float amount, bool interpolate)
        {
            SourceAnimationLayer layer = secondary ? current.Animation.Secondary : current.Animation.Primary;
            if (layer.PoseInterpolationPolicy == SourcePoseInterpolationPolicy.OriginalPortable)
                return SourceLayerHeading(previous, current, secondary, amount, interpolate);
            FieldVector heading = SourceAnimationSampling.Heading(layer.FacingDirection, current.FacingDirection);
            SourceAnimationLayer old = secondary ? previous.Animation.Secondary : previous.Animation.Primary;
            if (!interpolate || amount == 1 || !old.IsSpecified || old.JointMask != layer.JointMask ||
                !SameInstance(old, layer) && layer.TransitionFromPose == null)
                return heading;
            return SourceAnimationSampling.InterpolateHeading(
                SourceAnimationSampling.Heading(old.FacingDirection, previous.FacingDirection), heading, amount);
        }

        private static void RequirePolicy(SourceAnimationLayer layer, SourcePoseInterpolationPolicy policy)
        {
            if (layer.PoseInterpolationPolicy != policy ||
                policy != SourcePoseInterpolationPolicy.LegacyNormalized && policy != SourcePoseInterpolationPolicy.OriginalPortable)
                throw new InvalidOperationException("Unknown or mixed source numerical policies.");
            if (policy == SourcePoseInterpolationPolicy.OriginalPortable) layer.RequireSourceAffineBinding();
        }

        private static FieldVector SourceLayerHeading(PlayerView previous, PlayerView current, bool secondary,
            float amount, bool interpolate)
        {
            SourceAnimationLayer layer = secondary ? current.Animation.Secondary : current.Animation.Primary;
            FieldVector heading = layer.ResolveSourceFacing();
            SourceAnimationLayer old = secondary ? previous.Animation.Secondary : previous.Animation.Primary;
            if (!interpolate || amount == 1 || !old.IsSpecified || old.JointMask != layer.JointMask ||
                !SameInstance(old, layer) && layer.TransitionFromPose == null)
                return heading;
            layer.RequireSameSourceAffineBinding(old);
            FieldVector from = old.ResolveSourceFacing();
            if (amount == 0f) return from;
            return SourceAnimationSampling.InterpolateHeading(from, heading, amount);
        }

        private static bool SameInstance(SourceAnimationLayer previous, SourceAnimationLayer current)
        {
            return previous.InstanceId == current.InstanceId &&
                string.Equals(previous.ScriptSymbol, current.ScriptSymbol, StringComparison.Ordinal);
        }
    }
}
