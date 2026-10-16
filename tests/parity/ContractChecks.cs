using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class ContractChecks
{
    internal static void Run(string fixturePath)
    {
        using (JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath)))
        {
            Program.Equal("fixture.schema", "synthetic-contract-traces/v1",
                fixture.RootElement.GetProperty("schema").GetString());
            CheckDtos();
            CheckProductionClipDetachment();
            var library = new SyntheticLibrary(fixture.RootElement.GetProperty("source_animation"));
            CheckSelectedOrdinalTrace(library, fixture.RootElement.GetProperty("source_animation"));
            CheckTwoPartsAndInterruptedBlend(library);
            CheckFailures(library);
            HostAdapterChecks.Run(library, fixture.RootElement.GetProperty("host_clock"));
        }
    }

    private static void CheckProductionClipDetachment()
    {
        var selected = new[] { 7, 31, 80 };
        var data = new SourceAnimationFrame[3];
        for (int i = 0; i < data.Length; i++)
            data[i] = new SourceAnimationFrame(selected[i], new SourceVector3(i, 2, 0),
                new SourceVector3(i, 3, 0), new SourceJointPose(IdentityRotations()));
        var clip = new SourceAnimationClip("SYNTHETIC", "synthetic/test-only", "synthetic",
            "synthetic", "synthetic", 0, 81, selected, data);
        selected[0] = 999;
        data[0] = default(SourceAnimationFrame);
        Program.Equal("actual clip detaches selected-frame producer array", 7, clip.SelectedSourceFrames[0]);
        Program.Equal("actual clip detaches frame producer array", 7, clip.GetFrame(7).SourceFrame);
        Program.Equal("actual clip keeps scaled root", 3f, clip.GetFrame(7).ScaledRoot.Y);
        Program.Equal("actual clip lookup uses original selected frame", 31, clip.GetFrame(31).SourceFrame);
        Program.Throws<PlayerAssetException>("actual clip rejects exported ordinal as authoring index",
            () => clip.GetFrame(0), "AUTHORING frame 0");
        Program.Throws<NotSupportedException>("actual clip selected mapping is read-only",
            () => ((IList<int>)clip.SelectedSourceFrames)[0] = 99);
        Program.Throws<ArgumentNullException>("actual library requires explicit byte provider",
            () => SourceAnimationLibrary.Read(new byte[] { 123, 125 }, null));
        Program.Throws<PlayerAssetException>("actual library fails missing inventory",
            () => SourceAnimationLibrary.Read(null, path => new byte[0]), "empty");
        Program.Throws<PlayerAssetException>("actual library fails missing clip bytes",
            () => SourceAnimationLibrary.ReadClip(null, "synthetic.json", new string('0', 64)),
            "synthetic.json", "empty");
    }

    private static SourceRotation[] IdentityRotations()
    {
        var rotations = new SourceRotation[SourceTiming.JointCount];
        for (int joint = 0; joint < rotations.Length; joint++)
            rotations[joint] = new SourceRotation(0, 0, 0, 1);
        return rotations;
    }

    private static void CheckDtos()
    {
        var values = IdentityRotations();
        var pose = new SourceJointPose(values);
        values[0] = new SourceRotation(1, 0, 0, 0);
        Program.Equal("pose copied from producer array", 1f, pose.Rotations[0].W);
        Program.Equal("pose joint count", 15, pose.Rotations.Count);
        Program.Throws<NotSupportedException>("pose read-only collection", () =>
            ((IList<SourceRotation>)pose.Rotations)[0] = values[0]);
        Program.Throws<ArgumentNullException>("null pose rejected", () => new SourceJointPose(null));
        Program.Throws<ArgumentException>("short pose rejected", () => new SourceJointPose(new SourceRotation[14]));
        Program.Throws<ArgumentException>("long pose rejected", () => new SourceJointPose(new SourceRotation[16]));
        Program.Equal("source default tick rate", 57, SourceTiming.DefaultTicksPerSecond);
        Program.Equal("all source joint outputs", (ushort)0x7fff, SourceTiming.AllJoints);
        Program.Equal("lower source matrix outputs", (ushort)0x7e01, SourceTiming.LowerBodyJoints);
        Program.Equal("upper source matrix outputs", (ushort)0x01fe, SourceTiming.UpperBodyJoints);
        Program.Equal("joint masks disjoint", 0, SourceTiming.LowerBodyJoints & SourceTiming.UpperBodyJoints);
        Program.Equal("joint masks complete", (int)SourceTiming.AllJoints,
            SourceTiming.LowerBodyJoints | SourceTiming.UpperBodyJoints);

        var sample = new SourceAnimationSample("SYNTHETIC", 7, 31, 0.25f);
        Program.Equal("source sample is specified", true, sample.IsSpecified);
        Program.Equal("source sample current WAN index", 7, sample.SourceFrame);
        Program.Equal("source sample next WAN index", 31, sample.NextSourceFrame);
        Program.Equal("source sample interpolation", 0.25f, sample.Interpolation01);
        Program.Equal("default sample unspecified", false, default(SourceAnimationSample).IsSpecified);
        var layer = new SourceAnimationLayer("synthetic_script", 42, sample, 4,
            SourceTiming.UpperBodyJoints, pose, 0.5f, new FieldVector(0, -1));
        Program.Equal("script identity", "synthetic_script", layer.ScriptSymbol);
        Program.Equal("instance identity", 42L, layer.InstanceId);
        Program.Equal("mode bits", 4u, layer.SourceModeBits);
        Program.Equal("joint output mask", SourceTiming.UpperBodyJoints, layer.JointMask);
        Program.Equal("transition pose identity", pose, layer.TransitionFromPose);
        Program.Equal("transition fraction", 0.5f, layer.Transition01);
        Program.Equal("independent layer facing", -1f, layer.FacingDirection.Z);
        Program.Equal("specified layer", true, layer.IsSpecified);
        Program.Equal("zero mask unspecified", false,
            new SourceAnimationLayer("script", 1, sample, jointMask: 0).IsSpecified);
        var animation = new ActorAnimationView(9, ActorAction.Throw, AnimationSelectionEvidence.Approximate,
            layer, new SourceVector3(0, 3, 0));
        Program.Equal("animation source tick", 9L, animation.SourceTick);
        Program.Equal("animation source rate", 57, animation.SourceTicksPerSecond);
        Program.Equal("approximate selectors never promoted", AnimationSelectionEvidence.Approximate, animation.SelectionEvidence);
        Program.Equal("actor action", ActorAction.Throw, animation.Action);
        Program.Equal("resolved root source units", 3f, animation.RootOffsetSourceUnits.Y);
        Program.Equal("specified animation", true, animation.IsSpecified);
        Program.Equal("default animation unspecified", false, default(ActorAnimationView).IsSpecified);

        var legacyPlayer = new PlayerView(3, TeamId.Home, default(FieldVector), default(FieldVector), 1);
        Program.Equal("legacy height", 0f, legacyPlayer.Height);
        Program.Equal("legacy model scale", 1f, legacyPlayer.ModelScale);
        Program.Equal("legacy role", ActorRole.Unspecified, legacyPlayer.Role);
        Program.Equal("legacy animation unspecified", false, legacyPlayer.Animation.IsSpecified);
        var player = new PlayerView(3, TeamId.Home, new FieldVector(20, 1), new FieldVector(2, 3),
            0.5f, 2, new FieldVector(1, 0), ActorRole.Quarterback, animation, 1.5f);
        Program.Equal("explicit actor height", 2f, player.Height);
        Program.Equal("explicit actor facing", 1f, player.FacingDirection.X);
        Program.Equal("explicit actor role", ActorRole.Quarterback, player.Role);
        Program.Equal("explicit actor animation", animation.SourceTick, player.Animation.SourceTick);
        Program.Equal("explicit actor model scale", 1.5f, player.ModelScale);
        var legacyBall = new BallView(BallMode.Held, default(FieldVector), 1, 3, -1);
        Program.Equal("legacy ball kind", SourceBallKind.Unspecified, legacyBall.Kind);
        Program.Equal("legacy ball last touch", -1, legacyBall.LastTouchPlayerId);
        var ball = new BallView(BallMode.InFlight, new FieldVector(30, 2), 4, -1, 6,
            SourceBallKind.Pass, new FieldVector(10, 2), 3, 3);
        Program.Equal("ball kind", SourceBallKind.Pass, ball.Kind);
        Program.Equal("ball horizontal velocity", 10f, ball.Velocity.X);
        Program.Equal("ball vertical velocity", 3f, ball.VerticalVelocity);
        Program.Equal("ball last touch actor", 3, ball.LastTouchPlayerId);
        Program.Equal("ball target actor", 6, ball.TargetPlayerId);

        var legacyEvent = new GameEvent(2, GameEventType.PassThrown, TeamId.Home, 6);
        Program.Equal("legacy pass receiver identity", 6, legacyEvent.PlayerId);
        Program.Equal("legacy event initiator unspecified", -1, legacyEvent.ActorPlayerId);
        Program.Equal("legacy event target unspecified", -1, legacyEvent.TargetPlayerId);
        Program.Equal("legacy event source tick unspecified", -1L, legacyEvent.SourceTick);
        var explicitEvent = new GameEvent(2, GameEventType.PassThrown, TeamId.Home, 6,
            actorPlayerId: 3, targetPlayerId: 6, sourceTick: 1);
        Program.Equal("explicit pass legacy receiver", 6, explicitEvent.PlayerId);
        Program.Equal("explicit pass initiator", 3, explicitEvent.ActorPlayerId);
        Program.Equal("explicit pass target", 6, explicitEvent.TargetPlayerId);
        Program.Equal("explicit pass source tick", 1L, explicitEvent.SourceTick);
    }

    private static void CheckSelectedOrdinalTrace(SyntheticLibrary library, JsonElement fixture)
    {
        var actor = new ActorState(3) { ModelScale = 1.5f };
        var state = new SourceAnimationLayerState(library, 3, false);
        int draws = 0;
        Func<int, int> random = bound => { draws++; return 0; };
        state.Change(fixture.GetProperty("script").GetString(), actor, 0, random);
        JsonElement expected = fixture.GetProperty("expected_selected_frames_at_ticks_0_to_6");
        for (int tick = 0; tick < expected.GetArrayLength(); tick++)
        {
            if (tick != 0) state.Advance(actor, tick, random);
            SourceAnimationLayer view = state.View(SourceTiming.AllJoints, actor.Facing);
            Program.Equal("selected source index tick " + tick, expected[tick].GetInt32(), view.Sample.SourceFrame);
            Program.Equal("selected ordinal tick " + tick, tick % 3, state.FrameOrdinal);
            Program.Equal("one-tick hold has no fractional invented frame " + tick, 0f, view.Sample.Interpolation01);
            Program.Equal("instance stable in loop " + tick, 1L, view.InstanceId);
            int current = state.FrameOrdinal;
            state.Advance(actor, tick, random);
            Program.Equal("same-source-tick duplicate is inert " + tick, current, state.FrameOrdinal);
        }
        Program.Equal("deterministic stance consumes no RNG", 0, draws);
        Program.Equal("selected final loop root scaled once", 3f, actor.SourceRoot.Y);
        state.Change("qb_stance_anim", actor, 7, random);
        Program.Equal("new script instance identity", 2L, state.InstanceId);
    }

    private static void CheckTwoPartsAndInterruptedBlend(SyntheticLibrary library)
    {
        var actor = new ActorState(3) { Facing = 256, UpperFacing = 256, TargetUpperFacing = 0 };
        var state = new SourceAnimationState(library, 3);
        Func<int, int> random = bound => 0;
        state.FullBody("qb_stance_anim", actor, 0, random, AnimationSelectionEvidence.Approximate);
        state.Split("qb_run_anim", "qb_throw_sr_anim", actor, 1, random);
        ActorAnimationView split = state.View(actor, 1);
        Program.Equal("two-part primary output mask", SourceTiming.LowerBodyJoints, split.Primary.JointMask);
        Program.Equal("two-part secondary output mask", SourceTiming.UpperBodyJoints, split.Secondary.JointMask);
        Program.Check("two-part independently specified layers", split.Primary.IsSpecified && split.Secondary.IsSpecified);
        Program.Equal("lower independent +X heading", 1f, split.Primary.FacingDirection.X);
        Program.Equal("upper independent +Z heading", 1f, split.Secondary.FacingDirection.Z);
        Program.Equal("lower script", "qb_run_anim", split.Primary.ScriptSymbol);
        Program.Equal("upper script", "qb_throw_sr_anim", split.Secondary.ScriptSymbol);
        state.FullBody("breath_anim", actor, 2, random, AnimationSelectionEvidence.Approximate);
        SourceJointPose interrupted = state.Primary.EvaluatePose();
        state.FullBody("breath3_anim", actor, 3, random, AnimationSelectionEvidence.Approximate);
        SourceAnimationLayer layer = state.View(actor, 3).Primary;
        Program.Check("interrupted blend captures pose", layer.TransitionFromPose != null);
        for (int joint = 0; joint < SourceTiming.JointCount; joint++)
        {
            Program.Equal("interrupted pose X joint " + joint, interrupted.Rotations[joint].X, layer.TransitionFromPose.Rotations[joint].X);
            Program.Equal("interrupted pose Y joint " + joint, interrupted.Rotations[joint].Y, layer.TransitionFromPose.Rotations[joint].Y);
            Program.Equal("interrupted pose Z joint " + joint, interrupted.Rotations[joint].Z, layer.TransitionFromPose.Rotations[joint].Z);
            Program.Equal("interrupted pose W joint " + joint, interrupted.Rotations[joint].W, layer.TransitionFromPose.Rotations[joint].W);
        }
        Program.Equal("full-body returns complete mask", SourceTiming.AllJoints, layer.JointMask);
        Program.Equal("full-body clears secondary snapshot", false, state.View(actor, 3).Secondary.IsSpecified);
        Program.Equal("world drift not exposed as second root X", 0f, state.View(actor, 3).RootOffsetSourceUnits.X);
        Program.Equal("world drift not exposed as second root Z", 0f, state.View(actor, 3).RootOffsetSourceUnits.Z);
    }

    private static void CheckFailures(SyntheticLibrary library)
    {
        var actor = new ActorState(3);
        var missing = new SourceAnimationLayerState(new MissingLibrary(), 3, false);
        Program.Throws<InvalidOperationException>("missing clip fails visibly",
            () => missing.Change("qb_stance_anim", actor, 0, bound => 0), "actor=3");
        Program.Throws<NotSupportedException>("unported script fails visibly",
            () => new SourceAnimationLayerState(library, 3, false).Change("not_a_ported_script", actor, 0, bound => 0),
            "not_a_ported_script");
    }

    private sealed class MissingLibrary : ISourceAnimationLibrary
    {
        public ISourceAnimationClip GetClip(string sequenceName)
        {
            throw new KeyNotFoundException("Deliberately missing synthetic " + sequenceName);
        }
    }

    internal sealed class SyntheticLibrary : ISourceAnimationLibrary
    {
        private readonly Dictionary<string, ISourceAnimationClip> clips = new Dictionary<string, ISourceAnimationClip>();

        internal SyntheticLibrary(JsonElement fixture)
        {
            var frames = new List<int>();
            var roots = new List<SourceVector3>();
            foreach (JsonElement frame in fixture.GetProperty("selected_source_frames").EnumerateArray())
                frames.Add(frame.GetInt32());
            foreach (JsonElement root in fixture.GetProperty("scaled_roots").EnumerateArray())
                roots.Add(new SourceVector3(root[0].GetSingle(), root[1].GetSingle(), root[2].GetSingle()));
            clips.Add(fixture.GetProperty("sequence").GetString(),
                new SyntheticClip(fixture.GetProperty("sequence").GetString(), frames, roots,
                    fixture.GetProperty("source_frame_count").GetInt32(), fixture.GetProperty("source_flags").GetUInt32()));
        }

        public ISourceAnimationClip GetClip(string sequenceName)
        {
            ISourceAnimationClip clip;
            if (!clips.TryGetValue(sequenceName, out clip))
            {
                var frames = new List<int>();
                var roots = new List<SourceVector3>();
                for (int i = 0; i < 128; i++)
                {
                    frames.Add(i * 3 + 7);
                    roots.Add(new SourceVector3(0, 2 + i * 0.01f, 0));
                }
                clip = new SyntheticClip(sequenceName, frames, roots, 389);
                clips.Add(sequenceName, clip);
            }
            return clip;
        }
    }

    private sealed class SyntheticClip : ISourceAnimationClip
    {
        private readonly Dictionary<int, SourceAnimationFrame> frames = new Dictionary<int, SourceAnimationFrame>();
        private readonly ReadOnlyCollection<int> selection;
        public string SequenceName { get; private set; }
        public string SourceRevision { get { return "synthetic-contract-fixture/v1"; } }
        public string SourcePath { get { return "tests/parity/fixtures/synthetic-contract-traces.json"; } }
        public string SourceGitBlobSha1 { get { return "synthetic-not-an-upstream-blob"; } }
        public string SourceSha256 { get { return "synthetic-not-an-upstream-motion-hash"; } }
        public string DecodedSha256 { get { return "synthetic-not-an-upstream-decoded-hash"; } }
        public uint SourceFlags { get; private set; }
        public int SourceFrameCount { get; private set; }
        public IReadOnlyList<int> SelectedSourceFrames { get { return selection; } }

        internal SyntheticClip(string sequence, List<int> selected, List<SourceVector3> roots, int sourceCount,
            uint flags = 0x4001)
        {
            SequenceName = sequence;
            SourceFrameCount = sourceCount;
            SourceFlags = flags;
            selection = Array.AsReadOnly(selected.ToArray());
            for (int ordinal = 0; ordinal < selected.Count; ordinal++)
                frames.Add(selected[ordinal], new SourceAnimationFrame(selected[ordinal],
                    roots[ordinal], roots[ordinal], new SourceJointPose(IdentityRotations())));
        }

        public SourceAnimationFrame GetFrame(int sourceFrame) { return frames[sourceFrame]; }
    }
}
