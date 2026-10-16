using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public static class SourceTiming
    {
        public const int DefaultTicksPerSecond = 57;
        public const int JointCount = 15;
        public const ushort AllJoints = 0x7fff;
        public const ushort LowerBodyJoints = 0x7e01;
        public const ushort UpperBodyJoints = 0x01fe;
        public const float SourceUnitsPerYard = 7.8f;
    }

    public enum AnimationSelectionEvidence { Unspecified, Approximate, SourceVerified }
    public enum ActorRole { Unspecified, Lineman, Quarterback, Receiver, Defender, Kicker }
    public enum ActorAction
    {
        Unspecified, PreSnap, Idle, Run, Backpedal, TurboRun, Spin, Juke,
        Hurdle, Dive, Throw, Catch, Intercept, Block, Contact, Fall, GetUp,
        Celebrate, Kick, Punt
    }

    public enum SourceBallKind { Unspecified, Pass, Fumble, Punt, Kickoff, FieldGoal, Bobble, Lateral }

    public readonly struct SourceVector3
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public SourceVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public readonly struct SourceRotation
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public SourceRotation(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    // Captures the actual transition origin, including interrupted blends/turn hacks.
    public sealed class SourceJointPose
    {
        public ReadOnlyCollection<SourceRotation> Rotations { get; }

        public SourceJointPose(IReadOnlyList<SourceRotation> rotations)
        {
            if (rotations == null)
                throw new ArgumentNullException(nameof(rotations));
            if (rotations.Count != SourceTiming.JointCount)
                throw new ArgumentException("A source player pose requires fifteen joint rotations.", nameof(rotations));
            var copy = new SourceRotation[rotations.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = rotations[i];
            Rotations = Array.AsReadOnly(copy);
        }
    }

    public readonly struct SourceAnimationFrame
    {
        public int SourceFrame { get; }
        public SourceVector3 AuthoringRoot { get; }
        public SourceVector3 ScaledRoot { get; }
        public SourceJointPose Pose { get; }

        public SourceAnimationFrame(int sourceFrame, SourceVector3 authoringRoot,
            SourceVector3 scaledRoot, SourceJointPose pose)
        {
            SourceFrame = sourceFrame;
            AuthoringRoot = authoringRoot;
            ScaledRoot = scaledRoot;
            Pose = pose;
        }
    }

    public interface ISourceAnimationClip
    {
        string SequenceName { get; }
        string SourceRevision { get; }
        string SourcePath { get; }
        string SourceGitBlobSha1 { get; }
        string SourceSha256 { get; }
        string DecodedSha256 { get; }
        uint SourceFlags { get; }
        int SourceFrameCount { get; }
        // Script frame ordinals index this map; its values are WAN authoring indices.
        IReadOnlyList<int> SelectedSourceFrames { get; }
        SourceAnimationFrame GetFrame(int sourceFrame);
    }

    public interface ISourceAnimationLibrary
    {
        ISourceAnimationClip GetClip(string sequenceName);
    }

    public readonly struct SourceAnimationSample
    {
        public string SequenceName { get; }
        // Published frame indices are authoring indices, not selected-array ordinals.
        public int SourceFrame { get; }
        public int NextSourceFrame { get; }
        public float Interpolation01 { get; }
        public bool IsSpecified { get { return !string.IsNullOrEmpty(SequenceName); } }

        public SourceAnimationSample(string sequenceName, int sourceFrame, int nextSourceFrame,
            float interpolation01 = 0f)
        {
            SequenceName = sequenceName;
            SourceFrame = sourceFrame;
            NextSourceFrame = nextSourceFrame;
            Interpolation01 = interpolation01;
        }

        public static SourceAnimationSample FromSelectedOrdinals(ISourceAnimationClip clip,
            int frameOrdinal, int nextFrameOrdinal, float interpolation01 = 0f)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (string.IsNullOrWhiteSpace(clip.SequenceName) || clip.SelectedSourceFrames == null
                || clip.SelectedSourceFrames.Count == 0 || clip.SourceFrameCount <= 0)
                throw new ArgumentException("A named clip with its selected-to-authoring frame map is required.", nameof(clip));
            SourceContractGuard.Range(frameOrdinal, 0, clip.SelectedSourceFrames.Count - 1, nameof(frameOrdinal));
            SourceContractGuard.Range(nextFrameOrdinal, 0, clip.SelectedSourceFrames.Count - 1, nameof(nextFrameOrdinal));
            SourceContractGuard.Finite(interpolation01, nameof(interpolation01));
            if (interpolation01 < 0f || interpolation01 > 1f)
                throw new ArgumentOutOfRangeException(nameof(interpolation01));
            int frame = clip.SelectedSourceFrames[frameOrdinal];
            int nextFrame = clip.SelectedSourceFrames[nextFrameOrdinal];
            SourceContractGuard.Range(frame, 0, clip.SourceFrameCount - 1, nameof(clip));
            SourceContractGuard.Range(nextFrame, 0, clip.SourceFrameCount - 1, nameof(clip));
            return new SourceAnimationSample(clip.SequenceName, frame, nextFrame, interpolation01);
        }
    }

    public readonly struct SourceAnimationLayer
    {
        public string ScriptSymbol { get; }
        public long InstanceId { get; }
        public SourceAnimationSample Sample { get; }
        public uint SourceModeBits { get; }
        public ushort JointMask { get; }
        public FieldVector FacingDirection { get; }
        public SourceJointPose TransitionFromPose { get; }
        public float Transition01 { get; }
        public bool IsSpecified { get { return Sample.IsSpecified && JointMask != 0; } }

        public SourceAnimationLayer(string scriptSymbol, long instanceId, SourceAnimationSample sample,
            uint sourceModeBits = 0, ushort jointMask = SourceTiming.AllJoints,
            SourceJointPose transitionFromPose = null, float transition01 = 1f,
            FieldVector facingDirection = default(FieldVector))
        {
            ScriptSymbol = scriptSymbol;
            InstanceId = instanceId;
            Sample = sample;
            SourceModeBits = sourceModeBits;
            JointMask = jointMask;
            FacingDirection = facingDirection;
            TransitionFromPose = transitionFromPose;
            Transition01 = transition01;
        }
    }

    public readonly struct ActorAnimationView
    {
        public long SourceTick { get; }
        public int SourceTicksPerSecond { get; }
        public ActorAction Action { get; }
        public AnimationSelectionEvidence SelectionEvidence { get; }
        public SourceAnimationLayer Primary { get; }
        public SourceAnimationLayer Secondary { get; }
        public SourceVector3 RootOffsetSourceUnits { get; }
        public bool IsSpecified { get { return SourceTicksPerSecond > 0 && Primary.IsSpecified; } }

        public ActorAnimationView(long sourceTick, ActorAction action,
            AnimationSelectionEvidence selectionEvidence, SourceAnimationLayer primary,
            SourceVector3 rootOffsetSourceUnits, SourceAnimationLayer secondary = default(SourceAnimationLayer),
            int sourceTicksPerSecond = SourceTiming.DefaultTicksPerSecond)
        {
            SourceTick = sourceTick;
            SourceTicksPerSecond = sourceTicksPerSecond;
            Action = action;
            SelectionEvidence = selectionEvidence;
            Primary = primary;
            Secondary = secondary;
            RootOffsetSourceUnits = rootOffsetSourceUnits;
        }
    }

    // Pure snapshot geometry: original centered source-world units, joints 0..14.
    public interface ISourceActorGeometry
    {
        SourceVector3 WorldJoint(PlayerView actor, int sourceJointIndex);
    }

    // Optional detached snapshot extension; existing IGameState/Step callers remain valid.
    public interface ISourceGameState : IGameState
    {
        long SourceTick { get; }
        int SourceTicksPerSecond { get; }
        uint PackedGameTime { get; }
        bool ClockActive { get; }
    }
}
