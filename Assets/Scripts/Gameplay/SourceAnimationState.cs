using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal static class SourcePoseMath
    {
        internal static SourceRotation Multiply(SourceRotation a, SourceRotation b)
        {
            return Normalize(new SourceRotation(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z));
        }

        internal static SourceRotation Yaw(int heading)
        {
            double angle = heading * Math.PI / 1024d;
            return new SourceRotation(0f, (float)Math.Sin(angle), 0f, (float)Math.Cos(angle));
        }

        internal static SourceRotation Inverse(SourceRotation value)
        {
            return new SourceRotation(-value.X, -value.Y, -value.Z, value.W);
        }

        internal static SourceRotation Normalize(SourceRotation value)
        {
            double squared = (double)value.X * value.X + (double)value.Y * value.Y
                + (double)value.Z * value.Z + (double)value.W * value.W;
            if (double.IsNaN(squared) || double.IsInfinity(squared) || squared < 1e-12d)
                throw new InvalidOperationException("Invalid source joint quaternion.");
            double scale = 1d / Math.Sqrt(squared);
            return new SourceRotation((float)(value.X * scale), (float)(value.Y * scale),
                (float)(value.Z * scale), (float)(value.W * scale));
        }

        internal static SourceRotation Slerp(SourceRotation a, SourceRotation b, float t)
        {
            double dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W;
            if (dot < 0d)
            {
                dot = -dot;
                b = new SourceRotation(-b.X, -b.Y, -b.Z, -b.W);
            }
            dot = Math.Min(1d, dot);
            double from = 1d - t;
            double to = t;
            if (dot < 0.9995d)
            {
                double angle = Math.Acos(dot);
                double denominator = Math.Sin(angle);
                from = Math.Sin((1d - t) * angle) / denominator;
                to = Math.Sin(t * angle) / denominator;
            }
            return Normalize(new SourceRotation(
                (float)(a.X * from + b.X * to), (float)(a.Y * from + b.Y * to),
                (float)(a.Z * from + b.Z * to), (float)(a.W * from + b.W * to)));
        }

        internal static SourceJointPose Blend(SourceJointPose from, SourceJointPose to, float t)
        {
            if (t >= 1f || from == null)
                return to;
            var rotations = new SourceRotation[SourceTiming.JointCount];
            for (int i = 0; i < rotations.Length; i++)
                rotations[i] = Slerp(from.Rotations[i], to.Rotations[i], t);
            return new SourceJointPose(rotations);
        }

        internal static SourceJointPose Turn(SourceJointPose pose, int heading)
        {
            if (pose == null || (heading & 1023) == 0)
                return pose;
            var rotations = new SourceRotation[SourceTiming.JointCount];
            for (int i = 0; i < rotations.Length; i++)
                rotations[i] = pose.Rotations[i];
            rotations[0] = Multiply(Yaw(heading), rotations[0]);
            return new SourceJointPose(rotations);
        }

        internal static FieldVector Facing(int angle)
        {
            double radians = (angle & 1023) * Math.PI / 512d;
            return new FieldVector((float)Math.Sin(radians), (float)Math.Cos(radians));
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal sealed class SourceAnimationLayerState
    {
        private readonly ISourceAnimationLibrary library;
        private readonly int actorId;
        private SourceAnimationProgram program;
        private ISourceAnimationClip clip;
        private int pc;
        private int hold;
        private float floatingFrame;
        private float streamIncrement;
        private int streamCount;
        private int streamTicks;
        private int transitionCount;
        private int transitionTicks;
        private float transitionRootFrom;
        private float transitionRootTo;
        private SourceJointPose transitionFrom;
        private SourceJointPose pendingTransitionOrigin;
        private int transitionSourceOrdinal;
        private int transitionSourceFrameCount = 1;
        private int? pendingSourceOrdinal;
        private int? pendingSourceFrameCount;
        private long lastAdvanceTick = -1;
        private SourceAnimationSignal signals;

        internal int FrameOrdinal { get; private set; } = -1;
        internal uint Mode { get; private set; }
        internal long InstanceId { get; private set; }
        internal int VariableFrame = -1;
        internal int VariableCount = 4;
        internal string SequenceName { get { return clip == null ? null : clip.SequenceName; } }
        internal string ScriptSymbol { get { return program == null ? null : program.Symbol; } }
        internal bool IsSpecified { get { return clip != null && FrameOrdinal >= 0; } }
        internal bool IsUninterruptible { get { return (Mode & SourceAnimationModes.Uninterruptible) != 0; } }
        internal bool IsEnded { get { return (Mode & SourceAnimationModes.End) != 0; } }

        internal SourceAnimationLayerState(ISourceAnimationLibrary library, int actorId, bool secondary)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.actorId = actorId;
            Mode = secondary ? SourceAnimationModes.Secondary : 0;
        }

        internal void CopyFrom(SourceAnimationLayerState other)
        {
            program = other.program;
            clip = other.clip;
            pc = other.pc;
            hold = other.hold;
            floatingFrame = other.floatingFrame;
            streamIncrement = other.streamIncrement;
            streamCount = other.streamCount;
            streamTicks = other.streamTicks;
            transitionCount = other.transitionCount;
            transitionTicks = other.transitionTicks;
            transitionRootFrom = other.transitionRootFrom;
            transitionRootTo = other.transitionRootTo;
            transitionFrom = other.transitionFrom;
            pendingTransitionOrigin = other.pendingTransitionOrigin;
            transitionSourceOrdinal = other.transitionSourceOrdinal;
            transitionSourceFrameCount = other.transitionSourceFrameCount;
            pendingSourceOrdinal = other.pendingSourceOrdinal;
            pendingSourceFrameCount = other.pendingSourceFrameCount;
            FrameOrdinal = other.FrameOrdinal;
            InstanceId = other.InstanceId;
            lastAdvanceTick = other.lastAdvanceTick;
            Mode = other.Mode | SourceAnimationModes.Secondary | SourceAnimationModes.TwoPart;
        }

        internal void Change(string symbol, ActorState actor, long tick, Func<int, int> random,
            SourceJointPose actualOrigin = null, int headingCorrection = 0)
        {
            SourceJointPose origin = actualOrigin ?? EvaluatePose();
            if (origin != null)
                origin = SourcePoseMath.Turn(origin, headingCorrection);
            pendingTransitionOrigin = origin;
            pendingSourceOrdinal = SourceProgressOrdinal();
            pendingSourceFrameCount = SourceProgressFrameCount();
            program = RequireProgram(symbol);
            signals = SourceAnimationSignal.None;
            pc = 0;
            hold = 1;
            Mode &= 0xffff0000u;
            Mode &= ~SourceAnimationModes.Interpolating;
            transitionCount = 0;
            transitionTicks = 0;
            InstanceId = ++actor.AnimationSerial;
            lastAdvanceTick = -1;
            Advance(actor, tick, random);
        }

        internal void Advance(ActorState actor, long tick, Func<int, int> random)
        {
            if (program == null || tick == lastAdvanceTick)
                return;
            lastAdvanceTick = tick;
            if (IsEnded || --hold != 0)
                return;
            if ((Mode & SourceAnimationModes.Interpolating) != 0)
                InterpolateFrame(actor);
            if ((Mode & SourceAnimationModes.Streaming) != 0)
                StreamFrame(actor);
            int budget = 1024;
            while (hold == 0 && !IsEnded)
            {
                if (--budget == 0 || pc < 0 || pc >= program.Instructions.Length)
                    throw Failure("animation program did not reach a valid frame break");
                SourceAnimationInstruction instruction = program.Instructions[pc++];
                Execute(instruction, actor, random);
            }
            if (!IsSpecified)
                throw Failure("animation program has no displayable selected frame");
        }

        internal SourceAnimationSignal ConsumeSignals()
        {
            SourceAnimationSignal value = signals;
            signals = SourceAnimationSignal.None;
            return value;
        }

        internal SourceJointPose EvaluatePose()
        {
            if (clip == null)
                return null;
            SourceJointPose target = Frame(Math.Max(0, FrameOrdinal)).Pose;
            float fraction = Fraction();
            if (fraction > 0f && FrameOrdinal + 1 < clip.SelectedSourceFrames.Count)
                target = SourcePoseMath.Blend(target, Frame(FrameOrdinal + 1).Pose, fraction);
            return SourcePoseMath.Blend(transitionFrom, target, TransitionFraction());
        }

        internal SourceAnimationLayer View(ushort mask, int facing)
        {
            if (!IsSpecified)
                throw Failure("actor snapshot has no source animation");
            int nextOrdinal = Math.Min(FrameOrdinal + (Fraction() > 0f ? 1 : 0),
                clip.SelectedSourceFrames.Count - 1);
            var sample = new SourceAnimationSample(SequenceName, clip.SelectedSourceFrames[FrameOrdinal],
                clip.SelectedSourceFrames[nextOrdinal], Fraction());
            return new SourceAnimationLayer(ScriptSymbol, InstanceId, sample, Mode, mask,
                transitionTicks > 0 ? transitionFrom : null, TransitionFraction(), SourcePoseMath.Facing(facing));
        }

        internal void ClearLowModes()
        {
            Mode &= 0xffff0000u;
        }

        private float Fraction()
        {
            return (Mode & SourceAnimationModes.Streaming) == 0 ? 0f
                : Math.Max(0f, floatingFrame - FrameOrdinal);
        }

        private float TransitionFraction()
        {
            return transitionTicks <= 0 ? 1f : Math.Min(1f, (float)transitionCount / transitionTicks);
        }

        private void Execute(SourceAnimationInstruction i, ActorState actor, Func<int, int> random)
        {
            switch (i.Op)
            {
                case SourceAnimationOp.SetMode:
                    Mode = (Mode & 0xffff0000u) | unchecked((uint)i.A);
                    break;
                case SourceAnimationOp.NewSequence:
                    clip = RequireClip(i.Text);
                    FrameOrdinal = -1;
                    floatingFrame = -1f;
                    transitionTicks = 0;
                    pendingTransitionOrigin = null;
                    pendingSourceOrdinal = pendingSourceFrameCount = null;
                    Mode &= ~(SourceAnimationModes.Streaming | SourceAnimationModes.Interpolating);
                    break;
                case SourceAnimationOp.NextFrame:
                    NextFrame(actor, i.A);
                    break;
                case SourceAnimationOp.SkipFrames:
                    int count = i.A == -1 ? random(i.B) : i.A;
                    if (count < 0 || count > clip.SelectedSourceFrames.Count)
                        throw Failure("invalid SKIPFRAMES count " + count);
                    // _ani_skipframes applies each compressed frame's one-shot anchors.
                    if ((clip.SourceFlags & 1u) != 0)
                    {
                        while (count-- > 0)
                            NextFrame(actor, 0);
                    }
                    else
                    {
                        FrameOrdinal += count - 1;
                        NextFrame(actor, 0);
                    }
                    break;
                case SourceAnimationOp.RunToFrame:
                    if (i.A < 0 || i.A >= clip.SelectedSourceFrames.Count)
                        throw Failure("RUNTOFRAME target outside selected frames: " + i.A);
                    if (FrameOrdinal < i.A)
                    {
                        pc--;
                        NextFrame(actor, i.B);
                    }
                    break;
                case SourceAnimationOp.RunToEnd:
                    if (FrameOrdinal < clip.SelectedSourceFrames.Count - 1)
                    {
                        pc--;
                        NextFrame(actor, i.A);
                    }
                    break;
                case SourceAnimationOp.Interpolate:
                    BeginInterpolation(actor, i);
                    break;
                case SourceAnimationOp.Stream:
                    BeginStream(actor, i.A, i.B == -1 ? GroundTicks(actor) : i.B, false);
                    break;
                case SourceAnimationOp.StreamToEnd:
                    int frames = clip.SelectedSourceFrames.Count - 1 - FrameOrdinal;
                    if (frames > 0)
                        BeginStream(actor, frames, (int)(0.5f + i.X * frames), true);
                    break;
                case SourceAnimationOp.Jump:
                    pc = i.A;
                    break;
                case SourceAnimationOp.JumpIfNotSequence:
                    if (!string.Equals(SequenceName, i.Text, StringComparison.Ordinal))
                        pc = i.A;
                    break;
                case SourceAnimationOp.ChangeScript:
                    program = RequireProgram(i.Text);
                    pc = 0;
                    InstanceId = ++actor.AnimationSerial;
                    if (i.Text.StartsWith("get_up_", StringComparison.Ordinal))
                        actor.Action = ActorAction.GetUp;
                    break;
                case SourceAnimationOp.BranchIfHasBall:
                    if (actor.SourceHasBall)
                    {
                        program = RequireProgram(i.Text);
                        pc = 0;
                        InstanceId = ++actor.AnimationSerial;
                    }
                    break;
                case SourceAnimationOp.ZeroVelocity:
                    actor.SourceVelocity = default(FieldVector);
                    actor.SourceVerticalVelocity = 0f;
                    break;
                case SourceAnimationOp.SetVelocity:
                    actor.SourceVelocity = Rotate(i.X, i.Z, actor.Facing);
                    actor.SourceVerticalVelocity = i.Y;
                    break;
                case SourceAnimationOp.SetYVelocity:
                    actor.SourceVerticalVelocity = i.X;
                    break;
                case SourceAnimationOp.FacingVelocity:
                    actor.SourceVelocity = Rotate(0f, i.X, actor.Facing);
                    break;
                case SourceAnimationOp.Friction:
                    actor.SourceFriction = i.X;
                    break;
                case SourceAnimationOp.RotateHeading:
                    actor.Facing = (actor.Facing + i.A) & 1023;
                    actor.TargetFacing = actor.Facing;
                    break;
                case SourceAnimationOp.PrepareTransition:
                    VariableCount = i.A;
                    VariableFrame = i.B;
                    Mode |= SourceAnimationModes.PrepareTransition;
                    break;
                case SourceAnimationOp.Callback:
                    signals |= (SourceAnimationSignal)i.A;
                    if (((SourceAnimationSignal)i.A & SourceAnimationSignal.PlayerDown) != 0)
                        actor.Down = true;
                    break;
                case SourceAnimationOp.WaitCondition:
                    if (!(i.A == 1 ? actor.SourceBallKicked : actor.SourceInPlay))
                    {
                        pc--;
                        hold = 1;
                    }
                    break;
                case SourceAnimationOp.End:
                    Mode |= SourceAnimationModes.End;
                    break;
                default:
                    throw Failure("unimplemented opcode " + i.Op);
            }
        }

        private void BeginInterpolation(ActorState actor, SourceAnimationInstruction i)
        {
            SourceJointPose origin = pendingTransitionOrigin ?? EvaluatePose();
            pendingTransitionOrigin = null;
            int previousFrame = pendingSourceOrdinal ?? SourceProgressOrdinal();
            int previousCount = pendingSourceFrameCount ?? SourceProgressFrameCount();
            pendingSourceOrdinal = pendingSourceFrameCount = null;
            ISourceAnimationClip destination = RequireClip(i.Text);
            int ticks = i.A == -1 ? VariableCount : i.A;
            int ordinal = i.B == -2 ? VariableFrame : i.B;
            if (ticks <= 0)
                throw Failure("INTTOSEQ requires positive source ticks");
            if (ordinal == -1)
            {
                float proportional = previousCount <= 1 ? 0f
                    : (float)Math.Max(0, previousFrame) / (previousCount - 1)
                        * (destination.SelectedSourceFrames.Count - 1);
                ordinal = ((int)(proportional + 0.5f) + ticks) % destination.SelectedSourceFrames.Count;
            }
            if (ordinal < 0 || ordinal >= destination.SelectedSourceFrames.Count)
                throw Failure("INTTOSEQ target outside selected frames: " + ordinal);
            clip = destination;
            FrameOrdinal = ordinal;
            floatingFrame = ordinal;
            transitionFrom = origin ?? Frame(ordinal).Pose;
            transitionTicks = ticks;
            transitionCount = 0;
            transitionSourceOrdinal = previousFrame;
            transitionSourceFrameCount = previousCount;
            transitionRootFrom = actor.SourceRoot.Y;
            transitionRootTo = Frame(ordinal).ScaledRoot.Y * actor.ModelScale;
            Mode |= SourceAnimationModes.Interpolating;
            Mode &= ~SourceAnimationModes.Streaming;
            InterpolateFrame(actor);
            hold = 1;
        }

        private void InterpolateFrame(ActorState actor)
        {
            transitionCount++;
            hold = 1;
            if (transitionCount >= transitionTicks)
            {
                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY);
                return;
            }
            if ((Mode & SourceAnimationModes.Secondary) != 0)
                return;
            float y = (Mode & SourceAnimationModes.AnchorY) != 0
                ? transitionRootFrom
                : transitionRootFrom + (transitionRootTo - transitionRootFrom) * TransitionFraction();
            actor.SourceRoot = new SourceVector3(actor.SourceRoot.X, y, actor.SourceRoot.Z);
        }

        private void BeginStream(ActorState actor, int frames, int ticks, bool toEnd)
        {
            if (ticks <= 0 || frames <= 0)
                throw Failure("invalid stream duration/selected-frame count");
            if (FrameOrdinal == -1 && ticks > frames)
            {
                ticks--;
                if (!toEnd)
                    frames--;
            }
            if (ticks <= 0)
                throw Failure("stream entry adjustment left no ticks");
            floatingFrame = FrameOrdinal;
            streamCount = 0;
            streamTicks = ticks;
            streamIncrement = (float)frames / ticks;
            transitionTicks = 0;
            pendingTransitionOrigin = null;
            pendingSourceOrdinal = pendingSourceFrameCount = null;
            Mode |= SourceAnimationModes.Streaming;
            StreamFrame(actor);
            hold = 1;
        }

        private void StreamFrame(ActorState actor)
        {
            if (FrameOrdinal != -1 || streamIncrement >= 1f)
                streamCount++;
            if (streamCount > streamTicks)
            {
                Mode &= ~SourceAnimationModes.Streaming;
                FrameOrdinal = (int)(floatingFrame + 0.5f);
                hold = 0;
                return;
            }
            if (FrameOrdinal == -1 && streamIncrement < 1f)
                floatingFrame = 0f;
            else
                floatingFrame = (int)((floatingFrame + streamIncrement) * 10000f) / 10000f;
            floatingFrame = Math.Min(floatingFrame, clip.SelectedSourceFrames.Count - 1);
            FrameOrdinal = (int)floatingFrame;
            hold = 1;
            SourceAnimationFrame from = Frame(FrameOrdinal);
            int next = Math.Min(FrameOrdinal + 1, clip.SelectedSourceFrames.Count - 1);
            SourceAnimationFrame to = Frame(next);
            float t = floatingFrame - FrameOrdinal;
            SourceVector3 root = new SourceVector3(
                from.ScaledRoot.X + (to.ScaledRoot.X - from.ScaledRoot.X) * t,
                from.ScaledRoot.Y + (to.ScaledRoot.Y - from.ScaledRoot.Y) * t,
                from.ScaledRoot.Z + (to.ScaledRoot.Z - from.ScaledRoot.Z) * t);
            ApplyRoot(actor, root, from.ScaledRoot.Y);
        }

        private void NextFrame(ActorState actor, int ticks)
        {
            FrameOrdinal++;
            transitionTicks = 0;
            pendingTransitionOrigin = null;
            pendingSourceOrdinal = pendingSourceFrameCount = null;
            SourceAnimationFrame frame = Frame(FrameOrdinal);
            hold = ticks;
            ApplyRoot(actor, frame.ScaledRoot, frame.ScaledRoot.Y);
        }

        private void ApplyRoot(ActorState actor, SourceVector3 root, float anchorY)
        {
            if ((Mode & SourceAnimationModes.Secondary) != 0)
                return;
            var scaled = new SourceVector3(root.X * actor.ModelScale, root.Y * actor.ModelScale,
                root.Z * actor.ModelScale);
            if ((Mode & SourceAnimationModes.AnchorXZ) != 0)
                Mode &= ~SourceAnimationModes.AnchorXZ;
            else
            {
                FieldVector drift = Rotate(scaled.X - actor.SourceRoot.X, scaled.Z - actor.SourceRoot.Z, actor.Facing);
                actor.Position = PlayerSimulation.Add(actor.Position,
                    PlayerSimulation.Scale(drift, 1f / SourceTiming.SourceUnitsPerYard));
            }
            if ((Mode & SourceAnimationModes.AnchorY) != 0)
            {
                Mode &= ~SourceAnimationModes.AnchorY;
                actor.Height = Math.Max(0f, actor.Height
                    + (actor.SourceRoot.Y - anchorY * actor.ModelScale) / SourceTiming.SourceUnitsPerYard);
            }
            actor.SourceRoot = scaled;
        }

        private ISourceAnimationClip RequireClip(string name)
        {
            ISourceAnimationClip value;
            try { value = library.GetClip(name); }
            catch (Exception e) { throw Failure("missing clip " + name, e); }
            if (value == null || !string.Equals(value.SequenceName, name, StringComparison.Ordinal)
                || value.SelectedSourceFrames == null || value.SelectedSourceFrames.Count == 0
                || value.SourceFrameCount <= 0)
                throw Failure("invalid or missing clip " + name);
            int previous = -1;
            for (int n = 0; n < value.SelectedSourceFrames.Count; n++)
            {
                int selected = value.SelectedSourceFrames[n];
                if (selected <= previous || selected < 0 || selected >= value.SourceFrameCount)
                    throw Failure("invalid selected-frame mapping for " + name);
                previous = selected;
            }
            return value;
        }

        private SourceAnimationProgram RequireProgram(string symbol)
        {
            try { return SourceAnimationPrograms.Get(symbol); }
            catch (NotSupportedException e)
            {
                throw new NotSupportedException(Diagnostic("unported source script " + symbol), e);
            }
        }

        private int SourceProgressOrdinal()
        {
            // ANIM.C 1330-1465 keeps the source header during INTTOSEQ.
            // Its interrupted replay/proportional-frame hack selects the old
            // destination index only AFTER more than half the blend elapsed.
            return (Mode & SourceAnimationModes.Interpolating) != 0
                && transitionCount <= transitionTicks / 2 ? transitionSourceOrdinal : FrameOrdinal;
        }

        private int SourceProgressFrameCount()
        {
            return (Mode & SourceAnimationModes.Interpolating) != 0
                ? transitionSourceFrameCount : clip == null ? 1 : clip.SelectedSourceFrames.Count;
        }

        private SourceAnimationFrame Frame(int ordinal)
        {
            if (clip == null || ordinal < 0 || ordinal >= clip.SelectedSourceFrames.Count)
                throw Failure("selected frame ordinal out of range: " + ordinal);
            int authoringFrame = clip.SelectedSourceFrames[ordinal];
            SourceAnimationFrame frame;
            try { frame = clip.GetFrame(authoringFrame); }
            catch (Exception e) { throw Failure("missing authoring frame " + authoringFrame, e); }
            SourceVector3 root = frame.ScaledRoot;
            SourceVector3 authoring = frame.AuthoringRoot;
            if (frame.SourceFrame != authoringFrame || frame.Pose == null
                || !SourcePoseMath.IsFinite(root.X) || !SourcePoseMath.IsFinite(root.Y) || !SourcePoseMath.IsFinite(root.Z)
                || !SourcePoseMath.IsFinite(authoring.X) || !SourcePoseMath.IsFinite(authoring.Y)
                || !SourcePoseMath.IsFinite(authoring.Z))
                throw Failure("invalid authoring frame " + authoringFrame);
            for (int i = 0; i < SourceTiming.JointCount; i++)
            {
                SourceRotation q = frame.Pose.Rotations[i];
                double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
                if (double.IsNaN(norm) || double.IsInfinity(norm) || Math.Abs(norm - 1d) > 0.0002d)
                    throw Failure("invalid quaternion at authoring frame " + authoringFrame + ", joint " + i);
            }
            return frame;
        }

        private InvalidOperationException Failure(string message, Exception inner = null)
        {
            return new InvalidOperationException(Diagnostic(message), inner);
        }

        private string Diagnostic(string message)
        {
            return "Source animation actor=" + actorId + " script="
                + (ScriptSymbol ?? "<none>") + " sequence=" + (SequenceName ?? "<none>")
                + " ordinal=" + FrameOrdinal + ": " + message;
        }

        internal static FieldVector Rotate(float x, float z, int heading)
        {
            FieldVector facing = SourcePoseMath.Facing(heading);
            return new FieldVector(z * facing.X + x * facing.Z, z * facing.Z - x * facing.X);
        }

        internal static int GroundTicks(ActorState actor)
        {
            const float a = -0.04f;
            float b = actor.SourceVerticalVelocity;
            float c = actor.Height * SourceTiming.SourceUnitsPerYard;
            float discriminant = b * b - 4f * a * c;
            float squareRoot = (float)Math.Sqrt(Math.Max(0f, discriminant));
            float first = (-b + squareRoot) / (2f * a);
            float second = (-b - squareRoot) / (2f * a);
            int ticks = 1;
            if (first > 1f) ticks = (int)(first + 0.9f);
            if (second > 1f) ticks = (int)(second + 0.9f);
            return ticks;
        }
    }

    internal sealed class SourceAnimationState
    {
        internal readonly SourceAnimationLayerState Primary;
        internal readonly SourceAnimationLayerState Secondary;
        internal bool TwoPart { get; private set; }
        internal AnimationSelectionEvidence Evidence = AnimationSelectionEvidence.Approximate;

        internal SourceAnimationState(ISourceAnimationLibrary library, int actorId)
        {
            Primary = new SourceAnimationLayerState(library, actorId, false);
            Secondary = new SourceAnimationLayerState(library, actorId, true);
        }

        internal bool Busy
        {
            get { return Primary.IsUninterruptible || (TwoPart && Secondary.IsUninterruptible); }
        }

        internal void FullBody(string script, ActorState actor, long tick, Func<int, int> random,
            AnimationSelectionEvidence evidence)
        {
            SourceJointPose composite = CompositePose(actor);
            TwoPart = false;
            Secondary.ClearLowModes();
            Evidence = evidence;
            Primary.Change(script, actor, tick, random, composite);
        }

        internal void Split(string lowerScript, string upperScript, ActorState actor, long tick,
            Func<int, int> random)
        {
            if (!TwoPart)
            {
                Secondary.CopyFrom(Primary);
                actor.UpperFacing = actor.Facing;
                TwoPart = true;
            }
            if (Primary.ScriptSymbol != lowerScript)
            {
                Primary.VariableFrame = -1;
                Primary.VariableCount = 2;
                Primary.Change(lowerScript, actor, tick, random);
            }
            if (Secondary.ScriptSymbol != upperScript)
            {
                int correction = actor.UpperFacing - actor.TargetUpperFacing;
                actor.UpperFacing = actor.TargetUpperFacing;
                Secondary.VariableFrame = -1;
                Secondary.VariableCount = 4;
                Secondary.Change(upperScript, actor, tick, random, headingCorrection: correction);
            }
        }

        internal void UpperBody(string script, ActorState actor, long tick, Func<int, int> random,
            AnimationSelectionEvidence evidence)
        {
            if (!TwoPart)
            {
                Secondary.CopyFrom(Primary);
                actor.UpperFacing = actor.Facing;
                TwoPart = true;
            }
            Evidence = evidence;
            Secondary.Change(script, actor, tick, random);
        }

        internal SourceAnimationSignal Advance(ActorState actor, long tick, Func<int, int> random)
        {
            Primary.Advance(actor, tick, random);
            if (TwoPart)
                Secondary.Advance(actor, tick, random);
            return Primary.ConsumeSignals() | Secondary.ConsumeSignals();
        }

        internal ActorAnimationView View(ActorState actor, long tick)
        {
            var lower = Primary.View(TwoPart ? SourceTiming.LowerBodyJoints : SourceTiming.AllJoints, actor.Facing);
            var upper = TwoPart ? Secondary.View(SourceTiming.UpperBodyJoints, actor.UpperFacing)
                : default(SourceAnimationLayer);
            return new ActorAnimationView(tick, actor.Action, Evidence, lower,
                new SourceVector3(0f, actor.SourceRoot.Y, 0f), upper);
        }

        private SourceJointPose CompositePose(ActorState actor)
        {
            SourceJointPose lower = Primary.EvaluatePose();
            if (!TwoPart || lower == null)
                return lower;
            SourceJointPose upper = Secondary.EvaluatePose();
            var result = new SourceRotation[SourceTiming.JointCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = i >= 1 && i <= 8 ? upper.Rotations[i] : lower.Rotations[i];
            // Preserve the two independently evaluated pelvis branches when
            // collapsing back to a single hierarchy for a full-body transition.
            SourceRotation relativePelvis = SourcePoseMath.Multiply(
                SourcePoseMath.Inverse(lower.Rotations[0]),
                SourcePoseMath.Multiply(SourcePoseMath.Yaw(actor.UpperFacing - actor.Facing), upper.Rotations[0]));
            result[1] = SourcePoseMath.Multiply(relativePelvis, upper.Rotations[1]);
            return new SourceJointPose(result);
        }
    }
}
