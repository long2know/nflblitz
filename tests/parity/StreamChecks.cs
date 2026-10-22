using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class StreamChecks
{
    private const float Tolerance = 0.000001f;
    private const string ClipSha256 = "486f4fe78f13bc2a6c316df521ee6f46b2359e47e5e76ea08f90ed0f1378468e";

    internal static void RunSynthetic()
    {
        // Toy inputs exercise ANIM.C _stream_frame's branch, not native animation data.
        foreach (uint flags in new[] { 0x4000u, 0x4001u })
        {
            CheckFraction(flags, "below", 10, 201, 0.0497f, 0f);
            CheckFraction(flags, "at", 10, 200, 0.05f, 0.05f);
            CheckFraction(flags, "above", 10, 199, 0.0502f, 0.0502f);
            CheckFraction(flags, "ordinary", 1, 2, 0.5f, 0.5f);
            CheckFraction(flags, "near-one", 99, 100, 0.99f, 0.99f);
            CheckInitialAndCompletion(flags);
            CheckAnchors(flags, false);
            CheckAnchors(flags, true);
        }
    }

    internal static void RunAdmittedClip(string path)
    {
        ISourceAnimationClip clip = SourceAnimationLibrary.ReadClip(
            File.ReadAllBytes(path), "clips/PSDLINE1.json", ClipSha256);
        Program.Equal("admitted clip revision", "89f840d58d152a2a9f230cb8d0cdffbd554c8ded", clip.SourceRevision);
        Program.Equal("admitted clip name", "PSDLINE1", clip.SequenceName);
        Program.Equal("admitted clip source path", "anim/presnap.wan", clip.SourcePath);
        Program.Equal("admitted clip source blob", "5376f528be8489f8e19bec0082fc0fd1ad954769", clip.SourceGitBlobSha1);
        Program.Equal("admitted clip decoded pin", ClipSha256, clip.DecodedSha256);
        Program.Equal("admitted clip selected count", 150, clip.SelectedSourceFrames.Count);
        Program.Equal("admitted clip authoring count", 300, clip.SourceFrameCount);
        var library = new OneClip(clip);
        var actor = new ActorState(0) { ModelScale = 1f, Facing = 256, SourceCoordinates = true };
        var layer = new SourceAnimationLayerState(library, 0, false);
        layer.Change("lm_set1_anim", actor, 0, NoRandom);

        // Pinned ANIM.C 1790-1823 and 777-853: initial -1, then 49/69,
        // with truncation at each tick. Expected never executes a production VM/sampler.
        float cursor = 0f;
        for (int tick = 0; tick <= 24; tick++)
        {
            if (tick != 0)
            {
                cursor = Truncate(cursor + 49f / 69f);
                layer.Advance(actor, tick, NoRandom);
            }
            int ordinal = (int)cursor;
            float fraction = cursor - ordinal;
            float applied = Math.Abs(fraction) < 0.05f ? 0f : fraction;
            string label = "admitted PSDLINE1 component tick " + tick;
            Program.Equal(label + " script", "lm_set1_anim", layer.ScriptSymbol);
            Program.Equal(label + " ordinal", ordinal, layer.FrameOrdinal);
            Program.Equal(label + " unrounded cursor", cursor, Field<float>(layer, "floatingFrame"));
            Program.Equal(label + " stream count", tick, Field<int>(layer, "streamCount"));
            Program.Equal(label + " stream duration", 69, Field<int>(layer, "streamTicks"));
            Program.Equal(label + " streaming mode", SourceAnimationModes.Streaming, layer.Mode);
            SourceAnimationFrame first = clip.GetFrame(clip.SelectedSourceFrames[ordinal]);
            SourceAnimationFrame next = clip.GetFrame(clip.SelectedSourceFrames[ordinal + 1]);
            Vector(label + " root", Interpolate(first.ScaledRoot, next.ScaledRoot, applied), actor.SourceRoot);
            SourceAnimationLayer view = CheckView(label, layer, clip, ordinal, applied);
            Vector(label + " public sampled root", Interpolate(first.ScaledRoot, next.ScaledRoot, applied),
                SourceAnimationSampling.Sample(library, view.Sample).ScaledRoot);
            if (tick == 24)
            {
                Program.Equal(label + " independent selected ordinal", 17, ordinal);
                Program.Equal(label + " independent authoring frame", 34, first.SourceFrame);
                Program.Check(label + " nonzero fraction inside strict source branch", fraction > 0f && fraction < 0.05f);
                Program.Check(label + " source poses are distinguishable", PoseDifference(first.Pose, next.Pose) > 0.0001f);
                Pose(label + " VM selected pose", first.Pose, layer.EvaluatePose());
                Pose(label + " public sampled pose", first.Pose, SourceAnimationSampling.SampleLayer(library, view));
            }
        }
    }

    private static void CheckFraction(uint flags, string name, int frames, int ticks, float fraction, float applied)
    {
        string label = "synthetic stream " + flags + " " + name;
        var clip = new SyntheticClip(flags);
        var library = new OneClip(clip);
        var actor = new ActorState(3) { ModelScale = 1.5f, SourceCoordinates = true };
        SourceAnimationLayerState layer = Start(library, actor, frames, ticks, true, false, 0);
        layer.Advance(actor, 1, NoRandom);
        Program.Equal(label + " accumulator", fraction, Field<float>(layer, "floatingFrame"));
        Program.Equal(label + " duration", ticks, Field<int>(layer, "streamTicks"));
        Program.Equal(label + " count", 1, Field<int>(layer, "streamCount"));
        SyntheticSample(label, library, layer, actor, 0, applied);
        layer.Advance(actor, 1, NoRandom);
        Program.Equal(label + " duplicate tick accumulator", fraction, Field<float>(layer, "floatingFrame"));
        Program.Equal(label + " duplicate tick count", 1, Field<int>(layer, "streamCount"));
        float second = Truncate(fraction + (float)frames / ticks);
        layer.Advance(actor, 2, NoRandom);
        Program.Equal(label + " next tick preserves fractional progress", second, Field<float>(layer, "floatingFrame"));
        float remainder = second - (int)second;
        SyntheticSample(label + " next tick", library, layer, actor, (int)second,
            remainder < 0.05f ? 0f : remainder);
        Program.Equal(label + " instance", 1L, layer.InstanceId);
        Program.Equal(label + " source tick", 2L, layer.SourceTick);
    }

    private static void CheckInitialAndCompletion(uint flags)
    {
        var library = new OneClip(new SyntheticClip(flags, 3));
        var actor = new ActorState(3) { SourceCoordinates = true };
        SourceAnimationLayerState slow = Start(library, actor, 2, 3, false, false, 0);
        string label = "synthetic initial -1 slow stream " + flags;
        SyntheticSample(label, library, slow, actor, 0, 0f);
        Program.Equal(label + " initial count not incremented", 0, Field<int>(slow, "streamCount"));
        Program.Equal(label + " decremented duration", 2, Field<int>(slow, "streamTicks"));
        slow.Advance(actor, 1, NoRandom);
        SyntheticSample(label + " tick 1", library, slow, actor, 0, 0.5f);
        slow.Advance(actor, 2, NoRandom);
        SyntheticSample(label + " tick 2", library, slow, actor, 1, 0f);
        SourceVector3 beforeCompletion = actor.SourceRoot;
        slow.Advance(actor, 3, NoRandom);
        Program.Equal(label + " completion count", 3, Field<int>(slow, "streamCount"));
        Program.Equal(label + " completion selected ordinal", 1, slow.FrameOrdinal);
        Program.Equal(label + " completion ended", SourceAnimationModes.End, slow.Mode);
        Vector(label + " completion does not refresh root", beforeCompletion, actor.SourceRoot);
        CheckView(label + " completed", slow, library.Clip, 1, 0f);

        actor = new ActorState(3) { SourceCoordinates = true };
        SourceAnimationLayerState fast = Start(library, actor, 3, 2, false, false, 0);
        label = "synthetic initial -1 fast stream endpoint " + flags;
        SyntheticSample(label, library, fast, actor, 0, 0.5f);
        Program.Equal(label + " initial count incremented", 1, Field<int>(fast, "streamCount"));
        Program.Equal(label + " duration not adjusted", 2, Field<int>(fast, "streamTicks"));
        fast.Advance(actor, 1, NoRandom);
        SyntheticSample(label + " final frame", library, fast, actor, 2, 0f);
        fast.Advance(actor, 2, NoRandom);
        Program.Equal(label + " completion ordinal", 2, fast.FrameOrdinal);
        Program.Equal(label + " completion ended", SourceAnimationModes.End, fast.Mode);
        SyntheticSample(label + " completed", library, fast, actor, 2, 0f);
    }

    private static void CheckAnchors(uint flags, bool secondary)
    {
        var library = new OneClip(new SyntheticClip(flags));
        var actor = new ActorState(3) { ModelScale = 1.5f, SourceCoordinates = true };
        SourceAnimationLayerState layer = Start(library, actor, 10, 201, true, secondary, SourceAnimationModes.Anchor);
        actor.SourceRoot = new SourceVector3(13, 17, 19);
        actor.SourcePosition = new FieldVector(8, -12);
        actor.SourceHeight = 40;
        layer.Advance(actor, 1, NoRandom);
        string label = "synthetic near-frame anchors " + flags + " secondary=" + secondary;
        SourceAnimationLayer view = CheckView(label, layer, library.Clip, 0, 0f);
        Pose(label + " VM pose", library.Clip.GetFrame(view.Sample.SourceFrame).Pose, layer.EvaluatePose());
        Pose(label + " public pose", layer.EvaluatePose(), SourceAnimationSampling.SampleLayer(library, view));
        Vector(label + " root", secondary ? new SourceVector3(13, 17, 19) : new SourceVector3(3, 6, -9), actor.SourceRoot);
        Program.Equal(label + " XZ anchor X", 8f, actor.SourcePosition.X);
        Program.Equal(label + " XZ anchor Z", -12f, actor.SourcePosition.Z);
        Program.Equal(label + " Y uses selected frame", secondary ? 40f : 51f, actor.SourceHeight);
        Program.Equal(label + " anchor consumption", secondary ? SourceAnimationModes.Anchor : 0u,
            layer.Mode & SourceAnimationModes.Anchor);
        Program.Equal(label + " secondary mode preserved", secondary, (layer.Mode & SourceAnimationModes.Secondary) != 0);
    }

    private static SourceAnimationLayerState Start(OneClip library, ActorState actor, int frames, int ticks,
        bool selectFirst, bool secondary, uint mode)
    {
        var instructions = new List<SourceAnimationInstruction>
        {
            new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: library.Clip.SequenceName)
        };
        if (selectFirst) instructions.Add(new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1));
        if (mode != 0) instructions.Add(new SourceAnimationInstruction(SourceAnimationOp.SetMode, unchecked((int)mode)));
        instructions.Add(new SourceAnimationInstruction(SourceAnimationOp.Stream, frames, ticks));
        instructions.Add(new SourceAnimationInstruction(SourceAnimationOp.End));
        var extension = new Dictionary<string, SourceAnimationProgram>
        {
            { "synthetic_stream", new SourceAnimationProgram("synthetic_stream", instructions.ToArray()) }
        };
        var layer = new SourceAnimationLayerState(library, actor.Id, secondary, extension);
        layer.Change("synthetic_stream", actor, 0, NoRandom);
        return layer;
    }

    private static void SyntheticSample(string label, OneClip library, SourceAnimationLayerState layer,
        ActorState actor, int ordinal, float fraction)
    {
        SourceAnimationLayer view = CheckView(label, layer, library.Clip, ordinal, fraction);
        SourceVector3 from = SyntheticRoot(ordinal);
        SourceVector3 to = SyntheticRoot(Math.Min(ordinal + 1, library.Clip.SelectedSourceFrames.Count - 1));
        SourceVector3 root = Interpolate(from, to, fraction);
        Vector(label + " scaled root", new SourceVector3(root.X * actor.ModelScale, root.Y * actor.ModelScale,
            root.Z * actor.ModelScale), actor.SourceRoot);
        Vector(label + " public sampled root", root, SourceAnimationSampling.Sample(library, view.Sample).ScaledRoot);
        SourceJointPose expected = SyntheticPose(ordinal + fraction);
        Pose(label + " VM pose", expected, layer.EvaluatePose());
        Pose(label + " public sampled pose", expected, SourceAnimationSampling.SampleLayer(library, view));
        if (fraction == 0)
            Program.Check(label + " VM retains existing pose",
                ReferenceEquals(library.Clip.GetFrame(view.Sample.SourceFrame).Pose, layer.EvaluatePose()));
    }

    private static SourceAnimationLayer CheckView(string label, SourceAnimationLayerState layer,
        ISourceAnimationClip clip, int ordinal, float fraction)
    {
        SourceAnimationLayer view = layer.View(SourceTiming.AllJoints, 0);
        Program.Equal(label + " selected ordinal", ordinal, layer.FrameOrdinal);
        Program.Equal(label + " source frame", clip.SelectedSourceFrames[ordinal], view.Sample.SourceFrame);
        Program.Equal(label + " next source frame", clip.SelectedSourceFrames[
            fraction == 0 ? ordinal : Math.Min(ordinal + 1, clip.SelectedSourceFrames.Count - 1)], view.Sample.NextSourceFrame);
        Program.Equal(label + " public sample fraction", fraction, view.Sample.Interpolation01);
        Program.Equal(label + " no frame-transition pose", null, view.TransitionFromPose);
        Program.Equal(label + " no frame-transition fraction", 1f, view.Transition01);
        return view;
    }

    private static float Truncate(float value) { return (int)(value * 10000f) / 10000f; }
    private static int NoRandom(int bound) { throw new InvalidOperationException("Fixed stream component requested RNG."); }
    private static T Field<T>(SourceAnimationLayerState layer, string name)
    {
        return (T)typeof(SourceAnimationLayerState).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layer);
    }

    private static SourceVector3 Interpolate(SourceVector3 from, SourceVector3 to, float fraction)
    {
        return new SourceVector3(from.X + (to.X - from.X) * fraction,
            from.Y + (to.Y - from.Y) * fraction, from.Z + (to.Z - from.Z) * fraction);
    }

    private static void Near(string label, float expected, float actual)
    {
        Program.Check(label + " maximum absolute tolerance=" + Tolerance,
            !float.IsNaN(actual) && !float.IsInfinity(actual) && Math.Abs(expected - actual) <= Tolerance);
    }

    private static void Vector(string label, SourceVector3 expected, SourceVector3 actual)
    {
        Near(label + ".X", expected.X, actual.X);
        Near(label + ".Y", expected.Y, actual.Y);
        Near(label + ".Z", expected.Z, actual.Z);
    }

    private static void Pose(string label, SourceJointPose expected, SourceJointPose actual)
    {
        Program.Check(label + " maximum quaternion component difference <= " + Tolerance,
            PoseDifference(expected, actual) <= Tolerance);
    }

    private static float PoseDifference(SourceJointPose expected, SourceJointPose actual)
    {
        float maximum = 0;
        for (int joint = 0; joint < SourceTiming.JointCount; joint++)
        {
            SourceRotation a = expected.Rotations[joint], b = actual.Rotations[joint];
            foreach (float difference in new[] { Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y),
                Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W) })
                maximum = float.IsNaN(difference) ? float.PositiveInfinity : Math.Max(maximum, difference);
        }
        return maximum;
    }

    private static SourceVector3 SyntheticRoot(int ordinal)
    {
        return new SourceVector3(2 + 8 * ordinal, 4 + 8 * ordinal, -6 + 12 * ordinal);
    }

    private static SourceJointPose SyntheticPose(float ordinal)
    {
        // Closed-form rotations about one axis; independent of both production slerps.
        double angle = ordinal * Math.PI / 4;
        var rotations = new SourceRotation[SourceTiming.JointCount];
        for (int joint = 0; joint < rotations.Length; joint++)
            rotations[joint] = new SourceRotation(0, (float)Math.Sin(angle), 0, (float)Math.Cos(angle));
        return new SourceJointPose(rotations);
    }

    private sealed class OneClip : ISourceAnimationLibrary
    {
        internal readonly ISourceAnimationClip Clip;
        internal OneClip(ISourceAnimationClip clip) { Clip = clip; }
        public ISourceAnimationClip GetClip(string name)
        {
            if (name != Clip.SequenceName) throw new InvalidOperationException("Component requested another clip: " + name);
            return Clip;
        }
    }

    private sealed class SyntheticClip : ISourceAnimationClip
    {
        private readonly SourceAnimationFrame[] frames;
        private readonly IReadOnlyList<int> selected;
        public string SequenceName { get { return "SYNTHETIC_STREAM"; } }
        public string SourceRevision { get { return "synthetic-stream-branch/v1"; } }
        public string SourcePath { get { return "synthetic-not-source-data"; } }
        public string SourceGitBlobSha1 { get { return "synthetic"; } }
        public string SourceSha256 { get { return "synthetic"; } }
        public string DecodedSha256 { get { return "synthetic"; } }
        public uint SourceFlags { get; private set; }
        public int SourceFrameCount { get { return frames.Length * 3 + 7; } }
        public IReadOnlyList<int> SelectedSourceFrames { get { return selected; } }

        internal SyntheticClip(uint flags, int count = 128)
        {
            SourceFlags = flags;
            frames = new SourceAnimationFrame[count];
            var indices = new int[count];
            for (int ordinal = 0; ordinal < count; ordinal++)
            {
                indices[ordinal] = ordinal * 3 + 7;
                frames[ordinal] = new SourceAnimationFrame(indices[ordinal], SyntheticRoot(ordinal),
                    SyntheticRoot(ordinal), SyntheticPose(ordinal));
            }
            selected = Array.AsReadOnly(indices);
        }

        public SourceAnimationFrame GetFrame(int sourceFrame) { return frames[(sourceFrame - 7) / 3]; }
    }
}
