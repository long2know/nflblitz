using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class SourceAffineChecks
{
    internal static void Run()
    {
        Program.Equal("affine interpolation interface stays two-method", 2, typeof(ISourcePoseInterpolation).GetMethods().Length);
        CheckHierarchy();
        CheckBindings();
        CheckSampler();
        CheckGeometry();
    }

    private static ISourcePoseInterpolation Operation()
    {
        var quarter = new float[257];
        for (int i = 0; i < quarter.Length; i++) quarter[i] = i / 256f;
        var immutable = Array.AsReadOnly(quarter);
        return SourceAnimationSampling.CreateOriginalPoseInterpolation(index => immutable[index]);
    }

    private static SourceAnimationLayer Layer(ISourcePoseInterpolation operation, int facing = 0,
        ushort mask = SourceTiming.AllJoints, int first = 5, int next = 5, float fraction = 0,
        SourceJointPose origin = null, float transition = 1, long instance = 9)
    {
        return new SourceAnimationLayer(SourcePoseInterpolationPolicy.OriginalPortable, operation, facing,
            "authored_affine", instance, new SourceAnimationSample("OWN_AFFINE", first, next, fraction),
            jointMask: mask, transitionFromPose: origin, transition01: transition);
    }

    private static SourceVector3[] Pivots()
    {
        var values = new SourceVector3[SourceTiming.JointCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SourceVector3(0.375f + i * 0.3125f, 1.25f + i * 0.4375f, -0.625f + i * 0.1875f);
        return values;
    }

    private static SourceJointPose Pose(bool nonunit, bool alternate = false)
    {
        var values = new SourceRotation[SourceTiming.JointCount];
        for (int i = 0; i < values.Length; i++)
        {
            float component = 0.6f + (nonunit ? 1f / 131072f : 0f);
            int axis = (i + (alternate ? 1 : 0)) % 3;
            values[i] = new SourceRotation(axis == 0 ? component : 0f,
                axis == 1 ? -component : 0f, axis == 2 ? component : 0f, 0.8f);
        }
        return new SourceJointPose(values);
    }

    private static PlayerView View(SourceAnimationLayer layer, SourceVector3 root, long tick = 40,
        SourceAnimationLayer upper = default(SourceAnimationLayer), ActorAction action = ActorAction.Run,
        FieldVector position = default(FieldVector), float scale = 1.375f)
    {
        return new PlayerView(3, TeamId.Home, position, default(FieldVector), 1, height: 0.75f,
            facingDirection: new FieldVector(0, 1), role: ActorRole.Receiver,
            animation: new ActorAnimationView(tick, action, AnimationSelectionEvidence.Approximate, layer, root, upper),
            modelScale: scale);
    }

    private static void CheckHierarchy()
    {
        SourceVector3[] pivots = Pivots();
        var root = new SourceVector3(0.875f, 2.375f, -1.625f);
        ISourcePoseInterpolation operation = Operation();
        foreach (bool nonunit in new[] { false, true })
        foreach (int heading in new[] { 0, 91, 256, 721, -37 })
        foreach (float scale in new[] { 1f, 1.375f })
        {
            SourceJointPose lower = Pose(nonunit), upper = Pose(nonunit, true);
            SourceAnimationLayer layer = Layer(operation, heading);
            FieldVector direction = layer.ResolveSourceFacing();
            SourceEvaluatedPose actual = SourceAnimationMatrices.EvaluateSource(pivots, lower, layer, direction, root, scale);
            ExpectedJoint[] expected = Reference(pivots, lower, direction, root, scale);
            CheckPose("raw full hierarchy", expected, actual);
            Program.Throws<InvalidOperationException>("raw Rotation unavailable",
                () => GC.KeepAlive(actual.Joints[14].Rotation), "no quaternion");
            Program.Throws<InvalidOperationException>("raw vertex scale cannot be applied twice",
                () => SourceAnimationMatrices.TransformVertex(actual.Joints[14], default(SourceVector3), scale + 1),
                "already includes");
            var point = new SourceVector3(-0.3125f, 0.6875f, 1.125f);
            for (int i = 0; i < expected.Length; i++)
            {
                float[] b = expected[i].Basis;
                SourceVector3 p = expected[i].Position;
                VectorBits("raw pivot-centered vertex", new SourceVector3(
                    p.X + (b[0] * point.X + b[1] * point.Y + b[2] * point.Z),
                    p.Y + (b[3] * point.X + b[4] * point.Y + b[5] * point.Z),
                    p.Z + (b[6] * point.X + b[7] * point.Y + b[8] * point.Z)),
                    SourceAnimationMatrices.TransformVertex(actual.Joints[i], point, scale));
            }
            SourceAnimationLayer lowerLayer = Layer(operation, heading, SourceTiming.LowerBodyJoints);
            SourceAnimationLayer upperLayer = Layer(operation, heading + 173, SourceTiming.UpperBodyJoints);
            SourceEvaluatedPose combined = SourceAnimationMatrices.EvaluateSource(pivots, lower, lowerLayer,
                direction, root, scale, upper, upperLayer, upperLayer.ResolveSourceFacing());
            ExpectedJoint[] upperExpected = Reference(pivots, upper, upperLayer.ResolveSourceFacing(), root, scale);
            for (int i = 0; i < expected.Length; i++)
                if ((SourceTiming.UpperBodyJoints & (1 << i)) != 0) expected[i] = upperExpected[i];
            CheckPose("raw independent two-part", expected, combined);
        }
        SourceJointPose legacyPose = Pose(false);
        SourceEvaluatedPose legacy = SourceAnimationMatrices.Evaluate(pivots, legacyPose, new FieldVector(0, 1), root, 1.375f);
        Program.Check("default Evaluate remains legacy", !legacy.IsRawAffine);
        Program.Check("legacy quaternion remains usable", !float.IsNaN(legacy.Joints[14].Rotation.W));
        var stored = new SourceRotation(0, 0, 0, 1.00002f);
        Program.Equal("legacy joint constructor retains exact quaternion", Program.Bits(stored.W),
            Program.Bits(new SourceJointTransform(root, stored).Rotation.W));
        Program.Equal("global legacy normalization remains", 1f, SourceAnimationSampling.Normalize(stored).W);
        SourceEvaluatedPose legacyAgain = SourceAnimationMatrices.Evaluate(pivots, legacyPose, new FieldVector(0, 1), root, 1.375f);
        for (int i = 0; i < legacy.Joints.Count; i++)
        {
            VectorBits("legacy repeated position", legacy.Joints[i].Position, legacyAgain.Joints[i].Position);
            Program.Equal("legacy repeated quaternion", legacy.Joints[i].Rotation, legacyAgain.Joints[i].Rotation);
        }
    }

    private static void CheckBindings()
    {
        ISourcePoseInterpolation operation = Operation();
        SourceJointPose pose = Pose(true);
        SourceVector3[] pivots = Pivots();
        SourceAnimationLayer source = Layer(operation, 1007);
        var noFacing = new SourceAnimationLayer(SourcePoseInterpolationPolicy.OriginalPortable, operation,
            "authored_affine", 9, new SourceAnimationSample("OWN_AFFINE", 5, 5));
        Program.Check("old source constructor still supports direct SampleLayer",
            ReferenceEquals(pose, SourceAnimationSampling.SampleLayer(new Library(pose, pose), noFacing)));
        Program.Throws<InvalidOperationException>("old source constructor does not invent facing",
            () => noFacing.RequireSourceAffineBinding(), "SourceFacing");
        var noHeading = Layer(new InterpolationOnly());
        Program.Throws<NotSupportedException>("interpolation-only binding cannot imply affine capability",
            () => noHeading.RequireSourceAffineBinding(), "heading capability");
        Program.Throws<ArgumentNullException>("null source operation",
            () => Layer(null));
        Program.Throws<ArgumentOutOfRangeException>("unknown layer policy",
            () => new SourceAnimationLayer((SourcePoseInterpolationPolicy)99, operation, "authored", 1,
                new SourceAnimationSample("OWN_AFFINE", 5, 5)));
        Program.Throws<InvalidOperationException>("incompatible source bindings",
            () => source.RequireSameSourceAffineBinding(Layer(Operation())), "incompatible");
        Program.Throws<InvalidOperationException>("legacy is not source by library presence",
            () => default(SourceAnimationLayer).RequireSourceAffineBinding(), "OriginalPortable");
        Program.Throws<PlayerAssetException>("invalid source mask",
            () => SourceAnimationMatrices.EvaluateSource(pivots, pose, Layer(operation, mask: 1),
                new FieldVector(0, 1), default(SourceVector3)), "masks");
        Program.Throws<PlayerAssetException>("zero source heading rejected",
            () => SourceAnimationMatrices.EvaluateSource(pivots, pose, source,
                default(FieldVector), default(SourceVector3)), "nonzero");
        Program.Throws<PlayerAssetException>("nonfinite source heading rejected",
            () => SourceAnimationMatrices.EvaluateSource(pivots, pose, source,
                new FieldVector(float.NaN, 1), default(SourceVector3)), "finite");
        var invalid = new SourceRotation[SourceTiming.JointCount];
        for (int i = 0; i < invalid.Length; i++) invalid[i] = new SourceRotation(0, 0, 0, 1);
        invalid[14] = new SourceRotation(0, 0, 0, float.PositiveInfinity);
        Program.Throws<PlayerAssetException>("nonfinite raw quaternion rejected without normalization",
            () => SourceAnimationMatrices.EvaluateSource(pivots, new SourceJointPose(invalid), source,
                new FieldVector(0, 1), default(SourceVector3)), "finite");
        var unavailable = Layer(SourceAnimationSampling.CreateOriginalPoseInterpolation(index => float.NaN));
        Program.Throws<InvalidOperationException>("unavailable quarter row is explicit",
            () => unavailable.ResolveSourceFacing(), "unavailable");
        FieldInfo binding = typeof(SourceAnimationLayer).GetField("poseInterpolation", BindingFlags.Instance | BindingFlags.NonPublic);
        Program.Check("binding remains private readonly NonSerialized", binding != null && binding.IsPrivate &&
            binding.IsInitOnly && binding.IsDefined(typeof(NonSerializedAttribute), false));
        object detached = source;
        binding.SetValue(detached, null);
        var unbound = (SourceAnimationLayer)detached;
        Program.Throws<InvalidOperationException>("metadata cannot restore a numerical binding",
            () => unbound.RequireSourceAffineBinding(), "unavailable");
        Program.Throws<InvalidOperationException>("missing binding cannot pass an evaluator endpoint",
            () => SourceAnimationMatrices.EvaluateSource(pivots, pose, unbound,
                new FieldVector(0, 1), default(SourceVector3)), "unavailable");
        foreach (PropertyInfo property in typeof(SourceAnimationLayer).GetProperties())
            Program.Check("no public affine provider " + property.Name,
                property.PropertyType != typeof(ISourcePoseAffineHeading) &&
                property.PropertyType != typeof(ISourcePoseInterpolation) &&
                !typeof(Delegate).IsAssignableFrom(property.PropertyType));
        string json = JsonSerializer.Serialize(source);
        Program.Check("serialized facing is value-only", json.Contains("\"SourceFacing\":1007") &&
            !json.Contains("readQuarterSine") && !json.Contains("ISourcePoseAffineHeading"));
        FieldVector facing = source.ResolveSourceFacing();
        Program.Equal("integer facing sine uses authored quarter folding", Program.Bits(-17f / 256f), Program.Bits(facing.X));
        Program.Equal("integer facing cosine uses authored quarter folding", Program.Bits(239f / 256f), Program.Bits(facing.Z));
        var legacy = new SourceAnimationLayer("authored_affine", 9, source.Sample);
        Program.Check("old constructor remains legacy without facing", legacy.SourceFacing == null &&
            legacy.PoseInterpolationPolicy == SourcePoseInterpolationPolicy.LegacyNormalized);
    }

    private static void CheckSampler()
    {
        SourceVector3[] pivots = Pivots();
        SourceJointPose first = Pose(true), second = Pose(true, true), marker = Pose(false, true);
        var library = new Library(first, second);
        var spy = new InterpolationSpy(marker);
        var beforeRoot = new SourceVector3(-0.5f, 1.75f, 0.875f);
        var afterRoot = new SourceVector3(1.25f, 3.5f, -0.375f);
        PlayerView previous = View(Layer(spy, 992), beforeRoot);
        PlayerView current = View(Layer(spy, 39, first: 23, next: 23), afterRoot, 41);
        foreach (float fraction in new[] { 0f, 0.375f, 1f })
        {
            spy.Calls = 0;
            SourceEvaluatedPose actual = SourceActorPoseSampler.Evaluate(library, pivots, previous, current, fraction);
            Program.Equal("source render Blend uses actual bound operation", fraction == 1f ? 0 : 1, spy.Calls);
            if (fraction != 1f)
            {
                Program.Check("render Blend source is actual previous selection", ReferenceEquals(first, spy.From));
                Program.Check("render Blend destination is actual current selection", ReferenceEquals(second, spy.To));
                Program.Equal("render Blend retains fraction", fraction, spy.Amount);
            }
            FieldVector a = previous.Animation.Primary.ResolveSourceFacing(), b = current.Animation.Primary.ResolveSourceFacing();
            FieldVector heading = fraction == 0 ? a : fraction == 1 ? b : SourceAnimationSampling.InterpolateHeading(a, b, fraction);
            var root = new SourceVector3(beforeRoot.X + (afterRoot.X - beforeRoot.X) * fraction,
                beforeRoot.Y + (afterRoot.Y - beforeRoot.Y) * fraction,
                beforeRoot.Z + (afterRoot.Z - beforeRoot.Z) * fraction);
            CheckPose("source sampled presentation route", Reference(pivots, fraction == 1 ? second : marker,
                heading, root, current.ModelScale), actual);
        }
        spy.Calls = 0;
        SourceAnimationLayer nested = Layer(spy, first: 5, next: 23, fraction: 0.25f,
            origin: first, transition: 0.5f);
        PlayerView nestedView = View(nested, afterRoot);
        SourceEvaluatedPose nestedPose = SourceActorPoseSampler.Evaluate(library, pivots, nestedView, nestedView, 1);
        Program.Equal("actual frame then transition source calls", 2, spy.Calls);
        CheckPose("frame-transition actual raw consumer", Reference(pivots, marker, nested.ResolveSourceFacing(),
            afterRoot, nestedView.ModelScale), nestedPose);
        var previousTwoPart = View(Layer(spy, 992, SourceTiming.LowerBodyJoints), beforeRoot,
            upper: Layer(spy, 39, SourceTiming.UpperBodyJoints, first: 23));
        var currentTwoPart = View(Layer(spy, 39, SourceTiming.LowerBodyJoints, first: 23), afterRoot, 41,
            upper: Layer(spy, 992, SourceTiming.UpperBodyJoints));
        const float interior = 0.375f;
        var interpolatedRoot = new SourceVector3(
            beforeRoot.X + (afterRoot.X - beforeRoot.X) * interior,
            beforeRoot.Y + (afterRoot.Y - beforeRoot.Y) * interior,
            beforeRoot.Z + (afterRoot.Z - beforeRoot.Z) * interior);
        FieldVector lowerHeading = SourceAnimationSampling.InterpolateHeading(
            previousTwoPart.Animation.Primary.ResolveSourceFacing(), currentTwoPart.Animation.Primary.ResolveSourceFacing(), interior);
        FieldVector upperHeading = SourceAnimationSampling.InterpolateHeading(
            previousTwoPart.Animation.Secondary.ResolveSourceFacing(), currentTwoPart.Animation.Secondary.ResolveSourceFacing(), interior);
        ExpectedJoint[] lowerExpected = Reference(pivots, marker, lowerHeading, interpolatedRoot, currentTwoPart.ModelScale);
        ExpectedJoint[] upperExpected = Reference(pivots, marker, upperHeading, interpolatedRoot, currentTwoPart.ModelScale);
        for (int i = 0; i < lowerExpected.Length; i++)
            if ((SourceTiming.UpperBodyJoints & (1 << i)) != 0) lowerExpected[i] = upperExpected[i];
        spy.Calls = 0;
        CheckPose("actual secondary source pose and facing presentation", lowerExpected,
            SourceActorPoseSampler.Evaluate(library, pivots, previousTwoPart, currentTwoPart, interior));
        Program.Equal("both eligible layers use bound render Blend", 2, spy.Calls);
        spy.Calls = 0;
        PlayerView changedInstance = View(Layer(spy, 39, first: 23, instance: 10), afterRoot, 41);
        CheckPose("instance discontinuity retains current pose and heading but interpolated root",
            Reference(pivots, second, changedInstance.Animation.Primary.ResolveSourceFacing(), interpolatedRoot, changedInstance.ModelScale),
            SourceActorPoseSampler.Evaluate(library, pivots, previous, changedInstance, interior));
        Program.Equal("instance discontinuity never invents a render blend", 0, spy.Calls);
        var legacyUpper = new SourceAnimationLayer("authored_affine", 9,
            new SourceAnimationSample("OWN_AFFINE", 5, 5), jointMask: SourceTiming.UpperBodyJoints);
        PlayerView mixed = View(Layer(spy, 39, SourceTiming.LowerBodyJoints), afterRoot, upper: legacyUpper);
        Program.Throws<InvalidOperationException>("selected secondary numerical policy cannot silently differ",
            () => SourceActorPoseSampler.Evaluate(library, pivots, mixed, mixed, 1), "mixed");
        PlayerView changedBinding = View(Layer(Operation(), first: 23), afterRoot, 41);
        Program.Throws<InvalidOperationException>("eligible render interpolation rejects replaced binding",
            () => SourceActorPoseSampler.Evaluate(library, pivots, previous, changedBinding, 0.375f), "incompatible");
        foreach (ActorAction action in new[] { ActorAction.Contact, ActorAction.PreSnap })
        {
            PlayerView stopped = View(Layer(spy, 39, first: 23), afterRoot, 41, action: action);
            Program.Check("unchanged contact/reline interpolation suppression", !SourceActorPoseSampler.CanInterpolate(previous, stopped));
            CheckPose("suppression uses actual current raw pose", Reference(pivots, second,
                stopped.Animation.Primary.ResolveSourceFacing(), afterRoot, stopped.ModelScale),
                SourceActorPoseSampler.Evaluate(library, pivots, previous, stopped, 0));
        }
        PlayerView rewound = View(Layer(spy), afterRoot, 1);
        PlayerView teleported = View(Layer(spy), afterRoot, 41, position: new FieldVector(30, 0));
        Program.Check("rewind eligibility unchanged", !SourceActorPoseSampler.CanInterpolate(previous, rewound));
        Program.Check("teleport eligibility unchanged", !SourceActorPoseSampler.CanInterpolate(previous, teleported));
        ISourcePoseInterpolation original = Operation();
        PlayerView originalView = View(Layer(original), afterRoot);
        CheckPose("actual original factory raw sampler", Reference(pivots, first,
            originalView.Animation.Primary.ResolveSourceFacing(), afterRoot, originalView.ModelScale),
            SourceActorPoseSampler.Evaluate(library, pivots, originalView, originalView, 1));
        var legacyLayer = new SourceAnimationLayer("authored_affine", 9, new SourceAnimationSample("OWN_AFFINE", 5, 5),
            facingDirection: new FieldVector(0, 1));
        PlayerView legacy = View(legacyLayer, afterRoot);
        Program.Check("legacy sampler stays normalized carrier", !SourceActorPoseSampler.Evaluate(library, pivots,
            legacy, legacy, 1).IsRawAffine);
    }

    private static void CheckGeometry()
    {
        SourceVector3[] pivots = Pivots();
        SourceJointPose pose = Pose(true);
        var library = new Library(pose, pose);
        ISourcePoseInterpolation operation = Operation();
        SourceAnimationLayer layer = Layer(operation, 117);
        var root = new SourceVector3(1.375f, 2.125f, -0.875f);
        PlayerView actor = View(layer, root, position: new FieldVector(53.25f, -1.5f));
        ExpectedJoint[] expected = Reference(pivots, pose, layer.ResolveSourceFacing(), root, actor.ModelScale);
        var geometry = new SourceCatchGeometry(library, pivots);
        float[] matrix = { 0.75f, -0.125f, 0.375f, 0.25f, 0.25f, 0.875f, -0.125f, -0.5f,
            -0.375f, 0.25f, 0.75f, 0.625f };
        var eye = new SourceVector3(-0.75f, 1.125f, 2.25f);
        var canonical = new SourceVector3(1.5f, -0.625f, 0.875f);
        var camera = new SourceMovementCameraFrame(eye, matrix, 512, 384);
        IReadOnlyList<SourceVector3> cameraJoints = geometry.CameraJoints(actor, canonical, camera);
        float rx = canonical.X - eye.X, ry = canonical.Y - eye.Y, rz = canonical.Z - eye.Z;
        for (int i = 0; i < expected.Length; i++)
        {
            SourceVector3 p = expected[i].Position;
            VectorBits("actual WorldJoint raw jpos", new SourceVector3(
                (actor.Position.X - 50f) * SourceTiming.SourceUnitsPerYard + p.X,
                actor.Height * SourceTiming.SourceUnitsPerYard + p.Y,
                actor.Position.Z * SourceTiming.SourceUnitsPerYard - p.Z), geometry.WorldJoint(actor, i));
            VectorBits("actual CameraJoints raw jpos", new SourceVector3(
                matrix[0] * p.X + matrix[1] * p.Y - matrix[2] * p.Z +
                    (matrix[0] * rx + matrix[1] * ry + matrix[2] * rz + matrix[3]),
                matrix[4] * p.X + matrix[5] * p.Y - matrix[6] * p.Z +
                    (matrix[4] * rx + matrix[5] * ry + matrix[6] * rz + matrix[7]),
                matrix[8] * p.X + matrix[9] * p.Y - matrix[10] * p.Z +
                    (matrix[8] * rx + matrix[9] * ry + matrix[10] * rz + matrix[11])), cameraJoints[i]);
        }
    }

    private sealed class ExpectedJoint
    {
        internal float[] Basis;
        internal SourceVector3 Position;
    }

    // Scalar authored reference: no production quaternion, matrix or vertex helpers.
    private static ExpectedJoint[] Reference(SourceVector3[] pivots, SourceJointPose pose,
        FieldVector heading, SourceVector3 root, float scale)
    {
        var matrices = new float[SourceTiming.JointCount][];
        var result = new ExpectedJoint[SourceTiming.JointCount];
        var actor = new float[] { heading.Z * scale, 0, heading.X * scale, root.X,
            0, scale, 0, root.Y, -heading.X * scale, 0, heading.Z * scale, root.Z };
        for (int joint = 0; joint < result.Length; joint++)
        {
            int index = SourceAnimationSkeleton.ParentIndices[joint];
            float[] parent = index < 0 ? actor : matrices[index];
            float[] p = { pivots[joint].X, pivots[joint].Y, -pivots[joint].Z };
            var position = new float[3];
            for (int row = 0; row < 3; row++)
                position[row] = parent[row * 4] * p[0] + parent[row * 4 + 1] * p[1] +
                    parent[row * 4 + 2] * p[2] + parent[row * 4 + 3];
            SourceRotation q = pose.Rotations[joint];
            float[] twice = { q.X * 2f, q.Y * 2f, q.Z * 2f };
            float xx = q.X * twice[0], xy = q.X * twice[1], xz = q.X * twice[2];
            float yy = q.Y * twice[1], yz = q.Y * twice[2], zz = q.Z * twice[2];
            float wx = q.W * twice[0], wy = q.W * twice[1], wz = q.W * twice[2];
            var local = new float[] { 1f - (yy + zz), xy - wz, xz + wy, p[0],
                xy + wz, 1f - (xx + zz), yz - wx, p[1], xz - wy, yz + wx, 1f - (xx + yy), p[2] };
            var completed = new float[12];
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float sum = parent[row * 4] * local[column];
                    sum += parent[row * 4 + 1] * local[4 + column];
                    sum += parent[row * 4 + 2] * local[8 + column];
                    if (column == 3) sum += parent[row * 4 + 3];
                    completed[row * 4 + column] = sum;
                }
                completed[row * 4 + 3] -= completed[row * 4] * p[0] +
                    completed[row * 4 + 1] * p[1] + completed[row * 4 + 2] * p[2];
            }
            matrices[joint] = completed;
            result[joint] = new ExpectedJoint {
                Position = new SourceVector3(position[0], position[1], -position[2]),
                Basis = new[] { completed[0], completed[1], -completed[2],
                    completed[4], completed[5], -completed[6], -completed[8], -completed[9], completed[10] }
            };
        }
        return result;
    }

    private static void CheckPose(string label, ExpectedJoint[] expected, SourceEvaluatedPose actual)
    {
        Program.Check(label + " raw representation", actual.IsRawAffine);
        Program.Equal(label + " all real joints", SourceTiming.JointCount, actual.Joints.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            SourceJointTransform joint = actual.Joints[i];
            VectorBits(label + " jpos " + i, expected[i].Position, joint.Position);
            SourceAffineBasis b = joint.AffineBasis;
            float[] values = { b.M00, b.M01, b.M02, b.M10, b.M11, b.M12, b.M20, b.M21, b.M22 };
            for (int component = 0; component < values.Length; component++)
                Program.Equal(label + " basis " + i + "/" + component,
                    Program.Bits(expected[i].Basis[component]), Program.Bits(values[component]));
        }
    }

    private static void VectorBits(string label, SourceVector3 expected, SourceVector3 actual)
    {
        Program.Equal(label + " X", Program.Bits(expected.X), Program.Bits(actual.X));
        Program.Equal(label + " Y", Program.Bits(expected.Y), Program.Bits(actual.Y));
        Program.Equal(label + " Z", Program.Bits(expected.Z), Program.Bits(actual.Z));
    }

    private class InterpolationOnly : ISourcePoseInterpolation
    {
        public SourceRotation Slerp(SourceRotation from, SourceRotation to, float amount) { return to; }
        public virtual SourceJointPose Blend(SourceJointPose from, SourceJointPose to, float amount) { return to; }
    }

    private sealed class InterpolationSpy : InterpolationOnly, ISourcePoseAffineHeading
    {
        private readonly SourceJointPose marker;
        internal int Calls;
        internal float Amount;
        internal SourceJointPose From, To;
        internal InterpolationSpy(SourceJointPose marker) { this.marker = marker; }
        public override SourceJointPose Blend(SourceJointPose from, SourceJointPose to, float amount)
        {
            Calls++; From = from; To = to; Amount = amount;
            return marker;
        }
        public FieldVector Facing(int heading)
        {
            int value = heading & 1023;
            return value < 512 ? new FieldVector(0.5f, 0.75f) : new FieldVector(-0.25f, 0.875f);
        }
    }

    private sealed class Library : ISourceAnimationLibrary
    {
        private readonly SourceAnimationClip clip;
        internal Library(SourceJointPose first, SourceJointPose second)
        {
            clip = new SourceAnimationClip("OWN_AFFINE", "authored/affine", "authored", "authored", "authored",
                0, 24, new[] { 5, 23 }, new[] {
                    new SourceAnimationFrame(5, default(SourceVector3), default(SourceVector3), first),
                    new SourceAnimationFrame(23, default(SourceVector3), default(SourceVector3), second)
                });
        }
        public ISourceAnimationClip GetClip(string sequenceName)
        {
            if (sequenceName != "OWN_AFFINE") throw new KeyNotFoundException("Unknown authored affine clip.");
            return clip;
        }
    }
}
