using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.SourcePlayer;

static class AffinePacketChecks
{
    private static int assertions;

    private static void Check(bool condition, string field)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Authored affine packet: " + field);
    }

    private static void EqualBits(double expected, double actual, string field) =>
        Check(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual), field);

    internal static void Run()
    {
        ISourcePoseInterpolation operation = SourceAnimationSampling.CreateOriginalPoseInterpolation(index => index / 256f);
        var layer = new SourceAnimationLayer(SourcePoseInterpolationPolicy.OriginalPortable, operation, 83,
            "own_packet", 2, new SourceAnimationSample("OWN_PACKET", 3, 3));
        var pivots = new SourceVector3[SourceTiming.JointCount];
        for (int i = 0; i < pivots.Length; i++)
            pivots[i] = new SourceVector3(i * 0.3125f + 0.125f, i * 0.4375f + 0.75f, i * -0.1875f - 0.375f);
        var animationRoot = new SourceVector3(0.375f, 2.875f, -1.25f);
        var placement = new SourceVector3(6.125f, 1.375f, -8.75f);
        SourceJointPose authored = Pose(false);
        foreach (float scale in new[] { 1f, 1.625f })
        {
            SourceEvaluatedPose pose = SourceAnimationMatrices.EvaluateSource(pivots, authored, layer,
                layer.ResolveSourceFacing(), animationRoot, scale);
            for (int i = 0; i < pose.Joints.Count; i++)
            {
                SourceJointTransform joint = pose.Joints[i];
                SourceAffineBasis b = joint.AffineBasis;
                var packet = Enumerable.Repeat(417d, 20).ToArray();
                FrameMath.Joint(packet, 2, joint, placement, pose.ModelScale);
                double[] expected = {
                    b.M00, b.M10, -b.M20, 0, b.M01, b.M11, -b.M21, 0,
                    -b.M02, -b.M12, b.M22, 0,
                    placement.X + joint.Position.X, placement.Y + joint.Position.Y,
                    placement.Z - joint.Position.Z, 1
                };
                for (int component = 0; component < 16; component++)
                    EqualBits(expected[component], packet[component + 2], $"raw source mapping {i}/{component}");
                Check(packet[0] == 417 && packet[1] == 417 && packet[18] == 417 && packet[19] == 417,
                    "packet offset sentinels");
                bool wrongScale = false;
                try { FrameMath.Joint(packet, 2, joint, placement, scale + 1f); }
                catch (InvalidOperationException e) { wrongScale = e.Message.Contains("already includes"); }
                Check(wrongScale, "no raw double scaling or inconsistent scale argument");
            }
            Check(Digest(pose) == ExpectedDigest(pose), "actual static raw digest component stream");
            bool invalidRoot = false;
            try { FrameMath.Joint(new double[16], 0, pose.Joints[0], new SourceVector3(float.NaN, 0, 0), scale); }
            catch (PlayerAssetException e) { invalidRoot = e.Message.Contains("finite"); }
            Check(invalidRoot, "nonfinite raw packet placement explicitly fails");
            var legacy = SourceAnimationMatrices.Evaluate(pivots, authored, new FieldVector(0, 1), animationRoot, scale);
            Check(!legacy.IsRawAffine && Digest(legacy) == ExpectedDigest(legacy), "legacy digest stays exact");
            var legacyPacket = new double[16];
            FrameMath.Joint(legacyPacket, 0, legacy.Joints[14], placement, scale);
            SourceRotation q = legacy.Joints[14].Rotation;
            double x = -q.X, y = -q.Y, z = q.Z, w = q.W;
            double[] legacyExpected = {
                (1 - 2 * (y * y + z * z)) * scale, 2 * (x * y + z * w) * scale, 2 * (x * z - y * w) * scale, 0,
                2 * (x * y - z * w) * scale, (1 - 2 * (x * x + z * z)) * scale, 2 * (y * z + x * w) * scale, 0,
                2 * (x * z + y * w) * scale, 2 * (y * z - x * w) * scale, (1 - 2 * (x * x + y * y)) * scale, 0,
                placement.X + legacy.Joints[14].Position.X, placement.Y + legacy.Joints[14].Position.Y,
                placement.Z - legacy.Joints[14].Position.Z, 1
            };
            for (int i = 0; i < 16; i++) EqualBits(legacyExpected[i], legacyPacket[i], "legacy packet " + i);
        }
        var zeroPivots = new SourceVector3[SourceTiming.JointCount];
        SourceEvaluatedPose first = SourceAnimationMatrices.EvaluateSource(zeroPivots, Pose(false), layer,
            layer.ResolveSourceFacing(), animationRoot, 1.625f);
        SourceEvaluatedPose second = SourceAnimationMatrices.EvaluateSource(zeroPivots, Pose(true), layer,
            layer.ResolveSourceFacing(), animationRoot, 1.625f);
        for (int i = 0; i < first.Joints.Count; i++)
            Check(first.Joints[i].Position.Equals(second.Joints[i].Position), "same-position digest witness");
        Check(Digest(first) != Digest(second), "raw basis must participate in actual PoseDigest");
        Check(Digest(first).Length == 16 && Digest(first).All(c => "0123456789abcdef".Contains(c)),
            "unchanged digest string shape");
        Check(assertions > 0, "nonzero assertions");
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "source-affine-authored-validation-receipt/v1", status = "passed-authored-affine-packet-only",
            assertions, synthetic_data_only = true, actual_frame_math_and_static_digest = true,
            file_or_source_input_loading = false, game_host_or_vm_executed = false,
            actor_or_animation_layer_state_constructed = false, browser_or_unity_executed = false,
            player_host_render_executed = false, private_reference_executed = false,
            full_gameplay_parity_claimed = false, all16 = "OPEN", fullWorld = "STOP"
        }));
    }

    internal static int RunRenderObservation()
    {
        int count = 0;
        int samplerCalls = 0, frameMathCalls = 0, receiptConstructions = 0;
        bool viewsConstructed = false;
        var failures = new List<string>();
        void Assert(bool condition, string field)
        {
            count++;
            if (!condition) failures.Add(field);
        }
        void FloatBits(float expected, float actual, string field) =>
            Assert(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual), field);
        void DoubleBits(double expected, double actual, string field) =>
            Assert(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual), field);
        void VectorBits(SourceVector3 expected, SourceVector3 actual, string field)
        {
            FloatBits(expected.X, actual.X, field + "/x");
            FloatBits(expected.Y, actual.Y, field + "/y");
            FloatBits(expected.Z, actual.Z, field + "/z");
        }
        void RotationBits(SourceRotation expected, SourceRotation actual, string field)
        {
            FloatBits(expected.X, actual.X, field + "/x");
            FloatBits(expected.Y, actual.Y, field + "/y");
            FloatBits(expected.Z, actual.Z, field + "/z");
            FloatBits(expected.W, actual.W, field + "/w");
        }
        void PoseBits(SourceEvaluatedPose expected, SourceEvaluatedPose actual, string field)
        {
            Assert(expected.IsRawAffine == actual.IsRawAffine, field + "/policy");
            FloatBits(expected.ModelScale, actual.ModelScale, field + "/scale");
            Assert(expected.Joints.Count == actual.Joints.Count, field + "/joint count");
            for (int i = 0; i < expected.Joints.Count; i++)
            {
                SourceJointTransform left = expected.Joints[i], right = actual.Joints[i];
                VectorBits(left.Position, right.Position, field + "/position/" + i);
                Assert(left.IsRawAffine == right.IsRawAffine, field + "/joint policy/" + i);
                FloatBits(left.AppliedModelScale, right.AppliedModelScale, field + "/applied scale/" + i);
                if (left.IsRawAffine && right.IsRawAffine)
                {
                    SourceAffineBasis a = left.AffineBasis, b = right.AffineBasis;
                    float[] before = { a.M00, a.M01, a.M02, a.M10, a.M11, a.M12, a.M20, a.M21, a.M22 };
                    float[] after = { b.M00, b.M01, b.M02, b.M10, b.M11, b.M12, b.M20, b.M21, b.M22 };
                    for (int component = 0; component < before.Length; component++)
                        FloatBits(before[component], after[component], field + "/basis/" + i + "/" + component);
                }
                else if (!left.IsRawAffine && !right.IsRawAffine)
                    RotationBits(left.Rotation, right.Rotation, field + "/rotation/" + i);
            }
        }
        void ReadOnlyWrite(Action write, string field)
        {
            bool rejected = false;
            try { write(); }
            catch (NotSupportedException) { rejected = true; }
            Assert(rejected, field);
        }
        try
        {
            ISourcePoseInterpolation operation = SourceAnimationSampling.CreateOriginalPoseInterpolation(index => index / 256f);
            SourceJointPose before = Pose(false), after = Pose(true);
            var library = new ObservationClip(before, after, operation);
            var pivots = new SourceVector3[SourceTiming.JointCount];
            for (int i = 0; i < pivots.Length; i++)
                pivots[i] = new SourceVector3(i * 0.3125f + 0.125f, i * 0.4375f + 0.75f, i * -0.1875f - 0.375f);
            var previousRoot = new SourceVector3(0.375f, 2.875f, -1.25f);
            var currentRoot = new SourceVector3(1.375f, 3.875f, -0.25f);
            var placement = new SourceVector3(6.125f, 1.375f, -8.75f);
            var retainedPoses = new SourceActorPoseObservation[2];
            var retainedPackets = new double[2][];
            var cases = new (string Name, bool Split, bool OldSecondary, bool OldPrimary, bool SameInstance,
                bool SameMask, bool Transition, bool Gap, float Alpha, bool Interpolate, bool PrimarySampled, bool SecondarySampled)[] {
                ("single-blend", false, false, true, true, true, false, false, 0.375f, true, true, false),
                ("split-blend", true, true, true, true, true, false, false, 0.375f, true, true, true),
                ("new-secondary", true, false, true, true, true, false, false, 0.375f, true, true, false),
                ("at-one", false, false, true, true, true, false, false, 1f, true, false, false),
                ("no-previous", false, false, false, true, true, false, false, 0.375f, false, false, false),
                ("snapshot-gap", false, false, true, true, true, false, true, 0.375f, false, false, false),
                ("new-instance", false, false, true, false, true, false, false, 0.375f, true, false, false),
                ("changed-mask", false, false, true, true, false, false, false, 0.375f, true, false, false),
                ("transition-origin", false, false, true, false, true, true, false, 0.375f, true, true, false),
                ("at-zero", false, false, true, true, true, false, false, 0f, true, true, false),
                ("split-new-primary", true, true, true, false, true, false, false, 0.375f, true, false, true)
            };
            foreach (bool raw in new[] { true, false })
            foreach (var item in cases)
            {
                int actorId = raw ? 0 : 1;
                string field = (raw ? "raw/" : "legacy/") + item.Name;
                SourceAnimationLayer Layer(string script, long instance, int frame, ushort mask, SourceJointPose? origin = null) =>
                    raw ? new SourceAnimationLayer(SourcePoseInterpolationPolicy.OriginalPortable, library, 83,
                        script, instance, new SourceAnimationSample("OWN_PACKET", frame, frame),
                        jointMask: mask, transitionFromPose: origin) :
                    new SourceAnimationLayer(script, instance, new SourceAnimationSample("OWN_PACKET", frame, frame),
                        jointMask: mask, transitionFromPose: origin, facingDirection: new FieldVector(0, 1));
                ushort primaryMask = item.Split ? SourceTiming.LowerBodyJoints : SourceTiming.AllJoints;
                SourceAnimationLayer oldPrimary = item.OldPrimary ? Layer("own_packet", item.SameInstance ? 2 : 1, 2,
                    item.SameMask ? primaryMask : SourceTiming.LowerBodyJoints) : default;
                SourceAnimationLayer oldSecondary = item.OldSecondary ?
                    Layer("own_packet_upper", 2, 3, SourceTiming.UpperBodyJoints) : default;
                SourceAnimationLayer primary = Layer("own_packet", 2, 3, primaryMask, item.Transition ? before : null);
                SourceAnimationLayer secondary = item.Split ? Layer("own_packet_upper", 2, 2, SourceTiming.UpperBodyJoints) : default;
                var oldAnimation = new ActorAnimationView(item.Gap ? 0 : 2, ActorAction.Run,
                    AnimationSelectionEvidence.Approximate, oldPrimary, previousRoot, oldSecondary);
                var animation = new ActorAnimationView(3, ActorAction.Run,
                    AnimationSelectionEvidence.Approximate, primary, currentRoot, secondary);
                var previous = new PlayerView(actorId, raw ? TeamId.Home : TeamId.Away, new FieldVector(50, 0),
                    default, 0, facingDirection: new FieldVector(0, 1), role: ActorRole.Defender,
                    animation: oldAnimation, modelScale: 1.625f);
                var current = new PlayerView(actorId, previous.Team, new FieldVector(50.5f, 0.25f),
                    default, 0, facingDirection: new FieldVector(0, 1), role: ActorRole.Defender,
                    animation: animation, modelScale: 1.625f);
                viewsConstructed = true;
                var requested = new List<int> { 3, 3 };
                if (item.PrimarySampled) requested.AddRange(new[] { 2, 2 });
                if (item.Split)
                {
                    requested.AddRange(new[] { 2, 2 });
                    if (item.SecondarySampled) requested.AddRange(new[] { 3, 3 });
                }
                void Sampling(string path)
                {
                    Assert(library.ClipCalls == requested.Count / 2, field + "/" + path + "/SampleLayer count");
                    Assert(library.FrameCalls == requested.Count, field + "/" + path + "/frame count");
                    Assert(library.RequestedFrames.SequenceEqual(requested), field + "/" + path + "/actual sample order");
                    if (raw)
                        Assert(library.Blends.Count == (item.Transition ? 1 : 0) + (item.PrimarySampled ? 1 : 0) +
                            (item.SecondarySampled ? 1 : 0), field + "/" + path + "/actual source blend count");
                }
                library.Reset();
                samplerCalls++;
                SourceEvaluatedPose original = SourceActorPoseSampler.Evaluate(library, pivots, previous, current, item.Alpha);
                Sampling("original");
                library.Reset();
                samplerCalls++;
                SourceEvaluatedPose unobserved = SourceActorPoseSampler.Evaluate(library, pivots, previous, current,
                    item.Alpha, false, out SourceActorPoseObservation absent);
                Sampling("not-requested");
                Assert(absent == null, field + "/not-requested observation absent");
                library.Reset();
                samplerCalls++;
                SourceEvaluatedPose evaluated = SourceActorPoseSampler.Evaluate(library, pivots, previous, current,
                    item.Alpha, true, out SourceActorPoseObservation observed);
                Sampling("requested");
                Assert(observed != null, field + "/requested observation present");
                if (observed == null) continue;
                Assert(ReferenceEquals(observed.EvaluatedPose, evaluated), field + "/same returned evaluated pose");
                Assert(observed.ActorId == actorId && observed.PreviousActorId == previous.Id && observed.Team == current.Team,
                    field + "/exact actor identity");
                Assert(observed.PreviousAnimation.Equals(oldAnimation) && observed.CurrentAnimation.Equals(animation),
                    field + "/actual previous/current animation views");
                FloatBits(item.Alpha, observed.RenderInterpolation01, field + "/requested alpha");
                Assert(observed.CanInterpolate == item.Interpolate, field + "/actual interpolation decision");
                FloatBits(item.Interpolate ? item.Alpha : 1f, observed.EffectiveInterpolation01, field + "/effective amount");
                SourceVector3 resolvedRoot = !item.Interpolate || item.Alpha == 1 ? currentRoot :
                    item.Alpha == 0 ? previousRoot : new SourceVector3(0.75f, 3.25f, -0.875f);
                VectorBits(resolvedRoot, observed.RootOffsetSourceUnits, field + "/actual resolved animation root");
                Assert(observed.Primary.PreviousSampled == item.PrimarySampled, field + "/primary sampled-previous decision");
                Assert((observed.Primary.PreviousSample != null) == item.PrimarySampled, field + "/primary previous presence");
                Assert(ReferenceEquals(observed.Primary.CurrentSample, observed.Primary.PresentedPose) == !item.PrimarySampled,
                    field + "/actual primary presentation-blend result");
                Assert((observed.Secondary != null) == item.Split, field + "/secondary presence");
                if (item.Split && observed.Secondary is { } secondaryObservation)
                {
                    Assert(secondaryObservation.PreviousSampled == item.SecondarySampled, field + "/secondary sampled-previous decision");
                    Assert((secondaryObservation.PreviousSample != null) == item.SecondarySampled, field + "/secondary previous presence");
                    Assert(ReferenceEquals(secondaryObservation.CurrentSample, secondaryObservation.PresentedPose) == !item.SecondarySampled,
                        field + "/actual secondary presentation-blend result");
                }
                if (raw)
                {
                    Assert(ReferenceEquals(observed.Primary.CurrentSample, after), field + "/same actual raw current SampleLayer result");
                    int blendIndex = 0;
                    if (item.Transition)
                        Assert(ReferenceEquals(observed.Primary.CurrentSample, library.Blends[blendIndex++].Result),
                            field + "/same current in-layer transition result");
                    if (item.PrimarySampled)
                    {
                        Assert(ReferenceEquals(observed.Primary.PreviousSample, before), field + "/same actual raw previous SampleLayer result");
                        var blend = library.Blends[blendIndex++];
                        Assert(ReferenceEquals(blend.From, observed.Primary.PreviousSample) &&
                            ReferenceEquals(blend.To, observed.Primary.CurrentSample) &&
                            ReferenceEquals(blend.Result, observed.Primary.PresentedPose),
                            field + "/same actual primary presentation blend operands and result");
                        FloatBits(item.Alpha, blend.Amount, field + "/actual primary presentation blend amount");
                    }
                    if (item.Split && observed.Secondary is { } rawSecondaryObservation)
                    {
                        Assert(ReferenceEquals(rawSecondaryObservation.CurrentSample, before), field + "/same raw secondary current result");
                        if (item.SecondarySampled)
                        {
                            Assert(ReferenceEquals(rawSecondaryObservation.PreviousSample, after), field + "/same raw secondary previous result");
                            var blend = library.Blends[blendIndex++];
                            Assert(ReferenceEquals(blend.From, rawSecondaryObservation.PreviousSample) &&
                                ReferenceEquals(blend.To, rawSecondaryObservation.CurrentSample) &&
                                ReferenceEquals(blend.Result, rawSecondaryObservation.PresentedPose),
                                field + "/same actual secondary presentation blend operands and result");
                            FloatBits(item.Alpha, blend.Amount, field + "/actual secondary presentation blend amount");
                        }
                    }
                    for (int i = 0; i < SourceTiming.JointCount; i++)
                        RotationBits(after.Rotations[i], observed.Primary.CurrentSample.Rotations[i], field + "/unnormalized current/" + i);
                    if (!item.PrimarySampled || item.Alpha == 0)
                    {
                        FloatBits(83f / 256f, observed.Primary.Heading.X, field + "/actual raw heading x");
                        FloatBits(173f / 256f, observed.Primary.Heading.Z, field + "/actual raw heading z");
                    }
                }
                else
                {
                    FloatBits(0f, observed.Primary.Heading.X, field + "/legacy heading x");
                    FloatBits(1f, observed.Primary.Heading.Z, field + "/legacy heading z");
                }
                Assert(evaluated.IsRawAffine == raw, field + "/policy retained");
                PoseBits(original, unobserved, field + "/original versus not-requested");
                PoseBits(original, evaluated, field + "/original versus requested");
                var originalPacket = new double[SourceTiming.JointCount * 16];
                var unobservedPacket = new double[originalPacket.Length];
                var observedPacket = new double[originalPacket.Length];
                for (int i = 0; i < SourceTiming.JointCount; i++)
                {
                    frameMathCalls++;
                    FrameMath.Joint(originalPacket, i * 16, original.Joints[i], placement, original.ModelScale);
                    frameMathCalls++;
                    FrameMath.Joint(unobservedPacket, i * 16, unobserved.Joints[i], placement, unobserved.ModelScale);
                    frameMathCalls++;
                    FrameMath.Joint(observedPacket, i * 16, evaluated.Joints[i], placement, evaluated.ModelScale);
                }
                for (int i = 0; i < originalPacket.Length; i++)
                {
                    DoubleBits(originalPacket[i], unobservedPacket[i], field + "/unobserved outgoing joint/" + i);
                    DoubleBits(originalPacket[i], observedPacket[i], field + "/observed outgoing joint/" + i);
                }
                if (item.Name == "single-blend")
                {
                    retainedPoses[actorId] = observed;
                    retainedPackets[actorId] = observedPacket;
                }
            }
            int[] models = { 3, 8 }, appearances = { 11, 12 };
            var actors = new[] {
                new SourceRenderActorReceipt(retainedPoses[0], placement, models[0], appearances[0]),
                new SourceRenderActorReceipt(retainedPoses[1], placement, models[1], appearances[1])
            };
            var outgoing = retainedPackets.SelectMany(packet => packet).ToArray();
            const string status = "{\"epoch\":7,\"frames\":19}";
            receiptConstructions++;
            var receipt = new SourceRenderFrameReceipt(7, 19, 20, 21, 2, 3, 57, 57, 0.375f,
                actors, outgoing, models, appearances, status);
            Assert(receipt.Epoch == 7 && receipt.Frame == 19 && receipt.PresentSequence == 19, "frame/epoch and existing Present ordinal");
            Assert(receipt.PreviousHostTick == 20 && receipt.CurrentHostTick == 21 &&
                receipt.PreviousSourceTick == 2 && receipt.CurrentSourceTick == 3 &&
                receipt.PreviousSourceTicksPerSecond == 57 && receipt.CurrentSourceTicksPerSecond == 57,
                "frame/exact previous/current snapshot tick metadata");
            FloatBits(0.375f, receipt.RenderInterpolation01, "frame/actual evaluation alpha");
            Assert(receipt.Status == status && receipt.Actors.Count == 2 && receipt.Joints.Count == 2 * 15 * 16,
                "frame/status and payload shape");
            receiptConstructions++;
            var missingTicks = new SourceRenderFrameReceipt(7, 19, 20, 21, null, null, null, null, 0.375f,
                actors, outgoing, models, appearances, status);
            Assert(!missingTicks.PreviousSourceTick.HasValue && !missingTicks.CurrentSourceTick.HasValue &&
                !missingTicks.PreviousSourceTicksPerSecond.HasValue && !missingTicks.CurrentSourceTicksPerSecond.HasValue,
                "frame/unavailable optional snapshot source metadata is not invented");
            for (int i = 0; i < actors.Length; i++)
            {
                SourceRenderActorReceipt actor = receipt.Actors[i];
                Assert(ReferenceEquals(actor.PoseObservation, retainedPoses[i]) &&
                    ReferenceEquals(actor.PoseObservation.EvaluatedPose, retainedPoses[i].EvaluatedPose),
                    "frame/same actor pose observation/" + i);
                Assert(actor.ActorId == i && actor.JointOffset == i * 15 * 16 && actor.ModelId == models[i] &&
                    actor.AppearanceId == appearances[i] && receipt.ModelIds[i] == models[i] &&
                    receipt.AppearanceIds[i] == appearances[i], "frame/exact actor-to-payload slots/" + i);
                VectorBits(placement, actor.WorldRoot, "frame/same outgoing world root/" + i);
                FloatBits(1.625f, actor.PoseObservation.EvaluatedPose.ModelScale, "frame/actual pose scale/" + i);
                for (int component = 0; component < retainedPackets[i].Length; component++)
                    DoubleBits(retainedPackets[i][component], receipt.Joints[actor.JointOffset + component],
                        "frame/exact pose-to-outgoing joint/" + i + "/" + component);
            }
            Array.Fill(outgoing, -417d);
            Array.Fill(models, -1);
            Array.Fill(appearances, -1);
            actors[0] = actors[1];
            Assert(receipt.Actors[0].ActorId == 0 && receipt.Actors[1].ActorId == 1, "frame/retained actor array copy");
            Assert(receipt.ModelIds[0] == 3 && receipt.ModelIds[1] == 8, "frame/retained outgoing model copy");
            Assert(receipt.AppearanceIds[0] == 11 && receipt.AppearanceIds[1] == 12, "frame/retained outgoing appearance copy");
            for (int i = 0; i < retainedPackets.Length; i++)
            for (int component = 0; component < retainedPackets[i].Length; component++)
                DoubleBits(retainedPackets[i][component], receipt.Joints[i * 15 * 16 + component],
                    "frame/retained outgoing joint copy/" + i + "/" + component);
            ReadOnlyWrite(() => ((IList<double>)receipt.Joints)[0] = 0, "frame/joints read-only");
            ReadOnlyWrite(() => ((IList<int>)receipt.ModelIds)[0] = 0, "frame/models read-only");
            ReadOnlyWrite(() => ((IList<int>)receipt.AppearanceIds)[0] = 0, "frame/appearances read-only");
            ReadOnlyWrite(() => ((IList<SourceRenderActorReceipt>)receipt.Actors)[0] = actors[1], "frame/actors read-only");
            ReadOnlyWrite(() => ((IList<SourceRotation>)retainedPoses[0].Primary.CurrentSample.Rotations)[0] = default,
                "pose/raw sample read-only");
            ReadOnlyWrite(() => ((IList<SourceJointTransform>)retainedPoses[0].EvaluatedPose.Joints)[0] = default,
                "pose/evaluated joints read-only");
        }
        catch (Exception error)
        {
            Assert(false, "unexpected " + error.GetType().Name + ": " + error.Message);
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "source-render-observation-authored-validation-receipt/v1",
            status = failures.Count == 0 ? "passed-synthetic-render-observation-only" : "failed-synthetic-render-observation-only",
            assertions = count, failures = failures.Count, failure_fields = failures,
            synthetic_data_only = true, file_or_source_input_loading = false,
            source_actor_pose_sampler_executed = samplerCalls > 0, source_actor_pose_sampler_calls = samplerCalls,
            frame_math_executed = frameMathCalls > 0, frame_math_calls = frameMathCalls,
            immutable_frame_receipt_constructor_executed = receiptConstructions > 0,
            immutable_frame_receipt_constructions = receiptConstructions, immutable_player_and_layer_views_constructed = viewsConstructed,
            mutable_gameplay_or_animation_layer_state_constructed = false, game_host_or_vm_executed = false,
            player_host_render_executed = false, actual_present_executed = false, browser_or_unity_executed = false,
            full_gameplay_parity_claimed = false, live_host_consumption = "source-wired; not live-qualified",
            all16 = "OPEN", fullWorld = "STOP", E1_E2_E3 = "unqualified"
        }));
        return failures.Count == 0 ? 0 : 1;
    }

    private sealed class ObservationClip : ISourceAnimationClip, ISourceAnimationLibrary,
        ISourcePoseInterpolation, ISourcePoseAffineHeading
    {
        private readonly SourceJointPose before, after;
        private readonly ISourcePoseInterpolation interpolation;
        public int ClipCalls { get; private set; }
        public int FrameCalls { get; private set; }
        public List<int> RequestedFrames { get; } = new();
        public List<(SourceJointPose From, SourceJointPose To, float Amount, SourceJointPose Result)> Blends { get; } = new();
        public string SequenceName => "OWN_PACKET";
        public string SourceRevision => "authored-synthetic-only";
        public string SourcePath => "authored-synthetic-only";
        public string SourceGitBlobSha1 => "";
        public string SourceSha256 => "";
        public string DecodedSha256 => "";
        public uint SourceFlags => 0;
        public int SourceFrameCount => 4;
        public IReadOnlyList<int> SelectedSourceFrames { get; } = Array.AsReadOnly(new[] { 2, 3 });

        internal ObservationClip(SourceJointPose before, SourceJointPose after, ISourcePoseInterpolation interpolation)
        { this.before = before; this.after = after; this.interpolation = interpolation; }
        public ISourceAnimationClip GetClip(string sequenceName)
        {
            if (sequenceName != SequenceName) throw new InvalidOperationException("Unexpected authored observation clip.");
            ClipCalls++;
            return this;
        }
        public SourceAnimationFrame GetFrame(int sourceFrame)
        {
            if (sourceFrame != 2 && sourceFrame != 3) throw new InvalidOperationException("Unexpected authored observation frame.");
            FrameCalls++;
            RequestedFrames.Add(sourceFrame);
            return new SourceAnimationFrame(sourceFrame, default, default, sourceFrame == 2 ? before : after);
        }
        public SourceJointPose Blend(SourceJointPose from, SourceJointPose to, float amount)
        {
            SourceJointPose result = interpolation.Blend(from, to, amount);
            Blends.Add((from, to, amount, result));
            return result;
        }
        public SourceRotation Slerp(SourceRotation from, SourceRotation to, float amount) =>
            interpolation.Slerp(from, to, amount);
        public FieldVector Facing(int sourceFacing) => ((ISourcePoseAffineHeading)interpolation).Facing(sourceFacing);
        internal void Reset() { ClipCalls = FrameCalls = 0; RequestedFrames.Clear(); Blends.Clear(); }
    }

    private static SourceJointPose Pose(bool alternate)
    {
        var rotations = new SourceRotation[SourceTiming.JointCount];
        for (int i = 0; i < rotations.Length; i++)
        {
            float value = 0.6f + 1f / 131072f;
            int axis = (i + (alternate ? 1 : 0)) % 3;
            rotations[i] = new SourceRotation(axis == 0 ? value : 0,
                axis == 1 ? -value : 0, axis == 2 ? value : 0, 0.8f);
        }
        return new SourceJointPose(rotations);
    }

    private static string Digest(SourceEvaluatedPose pose)
    {
        MethodInfo method = typeof(PlayerHost).GetMethod("PoseDigest", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actual PlayerHost.PoseDigest missing.");
        return (string)(method.Invoke(null, new object[] { pose })
            ?? throw new InvalidOperationException("Actual PlayerHost.PoseDigest returned null."));
    }

    private static string ExpectedDigest(SourceEvaluatedPose pose)
    {
        ulong hash = 14695981039346656037;
        if (pose.IsRawAffine)
        {
            hash = unchecked((hash ^ 1u) * 1099511628211);
            Add(pose.ModelScale);
        }
        foreach (SourceJointTransform joint in pose.Joints)
        {
            Add(joint.Position.X); Add(joint.Position.Y); Add(joint.Position.Z);
            if (joint.IsRawAffine)
            {
                SourceAffineBasis b = joint.AffineBasis;
                Add(b.M00); Add(b.M01); Add(b.M02); Add(b.M10); Add(b.M11); Add(b.M12);
                Add(b.M20); Add(b.M21); Add(b.M22);
            }
            else
            {
                SourceRotation rotation = joint.Rotation;
                Add(rotation.X); Add(rotation.Y); Add(rotation.Z); Add(rotation.W);
            }
        }
        return hash.ToString("x16");
        void Add(float value)
        {
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            hash = unchecked((hash ^ bits) * 1099511628211);
        }
    }
}
