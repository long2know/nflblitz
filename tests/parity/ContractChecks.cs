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
    internal static void Run(string fixturePath, FullKernelInputs fullKernelInputs)
    {
        using (JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath)))
        {
            Program.Equal("fixture.schema", "synthetic-contract-traces/v1",
                fixture.RootElement.GetProperty("schema").GetString());
            CheckDtos();
            CheckPortableLibraryBehavior();
            RunSourcePoseMath();
            CheckProductionClipDetachment();
            var library = new SyntheticLibrary(fixture.RootElement.GetProperty("source_animation"));
            CheckSelectedOrdinalTrace(library, fixture.RootElement.GetProperty("source_animation"));
            CheckTwoPartsAndInterruptedBlend(library);
            CheckFailures(library);
            HostAdapterChecks.Run(fullKernelInputs, fixture.RootElement.GetProperty("host_clock"));
        }
    }

    private static SourceRandomDraw PortableDraw(ISourceRandom random, SourceRandomKind kind, int call, int bound = 0)
    {
        return random.Draw(new SourceRandomSite(kind, bound, 0, -1, "portable-library-test", 0,
            call, random.Provenance.Profile, "portable-library-test"));
    }

    private static void CheckPortableLibraryBehavior()
    {
        int[] seeds = { 0, 1, 2000, -1, int.MinValue };
        int[][] expected = {
            new[] { 1990395451, 1950343301, 404384196, 1972190027, 2046836396, 153545033, 1326305297, 730514944 },
            new[] { 2078917053, 143302914, 1027100827, 1953210302, 755253631, 2002600785, 1405390230, 45248011 },
            new[] { 792457667, 2084548885, 297131356, 519962043, 156651292, 298206177, 582381345, 291216368 },
            new[] { 1901873848, 1609900040, 1929151212, 1991169752, 1190935513, 451972930, 1247220364, 1415781877 },
            new[] { 916653627, 1950343301, 1478126020, 1972190027, 2046836396, 1227286857, 1326305297, 730514944 }
        };
        int[] standard = { 25282560, 434248667, 386136297, 1298942295, 1583707548, 1073817532, 1247883909, 1850779798 };
        for (int seed = 0; seed < seeds.Length; seed++)
        {
            ISourceRandom random = SourceRandom.CreatePortableLibrary(seeds[seed], "portable-library regression");
            Program.Equal("randrng zero consumes no library value", (int?)null,
                PortableDraw(random, SourceRandomKind.RandRange, 0).Raw);
            for (int i = 0; i < expected[seed].Length; i++)
            {
                SourceRandomDraw range = PortableDraw(random, SourceRandomKind.RandRange, i * 2 + 1, -101);
                Program.Equal("portable random sequence including zero/high-bit seeds", expected[seed][i], range.Raw.Value);
                Program.Equal("negative source range preserves remainder", expected[seed][i] % -101, range.Value);
                Program.Equal("rand has independent state, unaffected by random seed/draws", standard[i],
                    PortableDraw(random, SourceRandomKind.CStandardRand, i * 2 + 2).Raw.Value);
            }
        }
        ISourceRandom cabinet = SourcePreGameSoundService.CreatePortableRandom(1);
        Program.Equal("pregame uses the same portable library, not the legacy preview", expected[1][0],
            PortableDraw(cabinet, SourceRandomKind.RandRange, 0, 100).Raw.Value);
        Program.Equal("pregame does not consume independent rand during random selection", standard[0],
            PortableDraw(cabinet, SourceRandomKind.CStandardRand, 1).Raw.Value);

        foreach (TeamId team in new[] { TeamId.Home, TeamId.Away })
        {
            var model = new SimulationState { Possession = team, SourceCompatibility = SourceGameplayCompatibility.Portable };
            int quarterback = (int)team * 7 + 3;
            ActorState actor = model.Players[quarterback];
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(0, 0, 0));
            for (int slot = 0; slot < 3; slot++)
            {
                ActorState target = model.Players[quarterback + slot + 1];
                target.SourceMode = SourceActorMode.Receiver;
                target.SourceControllerStation = 0;
                SourcePlayerDriver.SetSourcePosition(target,
                    new SourceVector3(team == TeamId.Home ? 10 : -10, 0, 0));
            }
            Program.Equal("portable all-equal left receiver", quarterback + 1, SourcePassDriver.PickReceiver(model, actor, 21));
            Program.Equal("portable all-equal middle receiver", quarterback + 2, SourcePassDriver.PickReceiver(model, actor, 24));
            Program.Equal("portable all-equal right receiver", quarterback + 3, SourcePassDriver.PickReceiver(model, actor, 27));
            float direction = team == TeamId.Home ? 1f : -1f;
            for (int slot = 0; slot < 3; slot++)
                SourcePlayerDriver.SetSourcePosition(model.Players[quarterback + slot + 1],
                    new SourceVector3(direction * 10, 0, direction * (slot == 2 ? 10 : 20)));
            Program.Equal("portable minimum-sentinel receiver", quarterback + 3, SourcePassDriver.PickReceiver(model, actor, 21));
            Program.Equal("portable tied middle retains sentinel-swap order", quarterback + 2, SourcePassDriver.PickReceiver(model, actor, 24));
            Program.Equal("portable tied right retains sentinel-swap order", quarterback + 1, SourcePassDriver.PickReceiver(model, actor, 27));
            model.SourceCompatibility = SourceGameplayCompatibility.StrictSource;
            Program.Throws<NotSupportedException>("strict source still requires native tie evidence",
                () => SourcePassDriver.PickReceiver(model, actor, 24), "qsort");
        }
    }

    internal static void RunSourcePoseMath()
    {
        var quarter = new float[257];
        for (int i = 0; i < quarter.Length; i++) quarter[i] = i / 256f;
        SourceDirectionMath directions = PoseMathDirections(quarter);
        ISourcePoseInterpolation interpolation =
            SourceAnimationSampling.CreateOriginalPoseInterpolation(directions.Sin);
        Program.Throws<ArgumentNullException>("source kernel requires an explicit table accessor",
            () => SourceAnimationSampling.CreateOriginalPoseInterpolation(null));
        CheckSourcePoseEndpointsAndSigns(interpolation, quarter);
        CheckSourcePoseBranches(quarter, directions, interpolation);
        CheckSourcePoseLayers(quarter, interpolation);
    }

    private static SourceDirectionMath PoseMathDirections(float[] quarter)
    {
        var sticks = new int[49];
        int next = 0;
        for (int i = 0; i < sticks.Length; i++)
            sticks[i] = i == 24 ? -1 : next++ % 32 * 32;
        return new SourceDirectionMath(quarter, sticks, "synthetic-pose-quarter", "synthetic-pose-sticks");
    }

    private static void CheckSourcePoseEndpointsAndSigns(ISourcePoseInterpolation interpolation, float[] quarter)
    {
        float negativeZero = PoseMathFloat(0x80000000u);
        var source = new SourceRotation(negativeZero, 0f, negativeZero, 1.00002f);
        var destination = new SourceRotation(0.5f, negativeZero, 0.25f, 0.75f);
        RotationBits("source raw zero endpoint copies components", source,
            SourcePoseMath.Slerp(source, destination, 0f, interpolation));
        RotationBits("source raw one endpoint copies components", destination,
            interpolation.Slerp(source, destination, 1f));
        SourceJointPose pose = RepeatedPose(source), target = RepeatedPose(destination);
        Program.Check("source null origin returns destination reference",
            ReferenceEquals(target, SourcePoseMath.Blend(null, target, 0.25f, interpolation)));
        Program.Check("source completed blend returns destination reference",
            ReferenceEquals(target, SourcePoseMath.Blend(pose, target, 1f, interpolation)));
        Program.Check("source completion preserves the producer greater-than-one fast path",
            ReferenceEquals(target, SourcePoseMath.Blend(pose, target, 1.25f, interpolation)));
        Program.Throws<ArgumentNullException>("missing source operation cannot take completion fast path",
            () => SourcePoseMath.Blend(null, target, 1f, null));
        Program.Throws<ArgumentNullException>("missing source operation cannot take raw endpoint",
            () => SourcePoseMath.Slerp(source, destination, 0f, null));

        var identity = new SourceRotation(0, 0, 0, 1);
        var opposite = new SourceRotation(0, 0, 0, -1);
        RotationBits("source closer sign is retained", identity,
            interpolation.Blend(RepeatedPose(identity), RepeatedPose(identity), 0f).Rotations[0]);
        RotationBits("source sign preparation negates source not destination", identity,
            interpolation.Blend(RepeatedPose(opposite), RepeatedPose(identity), 0.25f).Rotations[0]);
        Program.Equal("legacy hemisphere retains old destination-negating result", -1f,
            SourcePoseMath.Slerp(opposite, identity, 0.25f).W);
        var tieSource = new SourceRotation(1, 0, 0, 0);
        var tieDestination = new SourceRotation(0, 1, 0, 0);
        RotationBits("source equal-distance tie negates source before zero endpoint",
            new SourceRotation(-1, negativeZero, negativeZero, negativeZero),
            interpolation.Blend(RepeatedPose(tieSource), RepeatedPose(tieDestination), 0f).Rotations[0]);
        RotationBits("source orthogonal midpoint uses the defined zero table bracket",
            ExpectedSourceRotation(tieSource, tieDestination, 0.5f, quarter, false),
            interpolation.Slerp(tieSource, tieDestination, 0.5f));

        var nonUnit = new SourceRotation(0, 0, 0, 1.00002f);
        RotationBits("source linear result is not normalized", nonUnit,
            interpolation.Slerp(nonUnit, nonUnit, 0.5f));
        Program.Equal("global producer Normalize remains normalized", 1f, SourcePoseMath.Normalize(nonUnit).W);
        Program.Equal("legacy producer Slerp remains normalized", 1f,
            SourcePoseMath.Slerp(nonUnit, nonUnit, 0.5f).W);
        Program.Equal("legacy sampler Slerp remains normalized", 1f,
            SourceAnimationSampling.Slerp(nonUnit, nonUnit, 0.5f).W);
        Program.Equal("legacy renderer-time Blend remains normalized", 1f,
            SourceAnimationSampling.Blend(RepeatedPose(nonUnit), RepeatedPose(nonUnit), 0.5f).Rotations[0].W);
    }

    private static void CheckSourcePoseBranches(float[] quarter, SourceDirectionMath directions,
        ISourcePoseInterpolation interpolation)
    {
        var source = new SourceRotation(0, 0, 0, 1);
        var destination = new SourceRotation(0, 0.6f, 0, 0.8f);
        SourceRotation expected = ExpectedSourceRotation(source, destination, 0.25f, quarter, false);
        SourceRotation actual = interpolation.Slerp(source, destination, 0.25f);
        RotationBits("source curved table and polynomial arithmetic", expected, actual);
        RotationBits("source negative cosine uses signed table angle",
            ExpectedSourceRotation(source, new SourceRotation(0, 0.6f, 0, -0.8f), 0.25f, quarter, false),
            interpolation.Slerp(source, new SourceRotation(0, 0.6f, 0, -0.8f), 0.25f));
        RotationBits("source near-zero polynomial argument floor",
            ExpectedSourceRotation(source, destination, 0.000001f, quarter, false),
            interpolation.Slerp(source, destination, 0.000001f));

        var changedQuarter = (float[])quarter.Clone();
        changedQuarter[204] += 1f / 1024f;
        SourceDirectionMath changedDirections = PoseMathDirections(changedQuarter);
        ISourcePoseInterpolation changed =
            SourceAnimationSampling.CreateOriginalPoseInterpolation(changedDirections.Sin);
        SourceRotation changedActual = changed.Slerp(source, destination, 0.25f);
        RotationBits("source uses the injected synthetic bracket",
            ExpectedSourceRotation(source, destination, 0.25f, changedQuarter, false), changedActual);
        Program.Check("changing only the synthetic table changes the result",
            Program.Bits(actual.Y) != Program.Bits(changedActual.Y) ||
                Program.Bits(actual.W) != Program.Bits(changedActual.W));
        RotationBits("existing operation retains its original immutable provider", actual,
            interpolation.Slerp(source, destination, 0.25f));

        int reads = 0;
        ISourcePoseInterpolation guarded = SourceAnimationSampling.CreateOriginalPoseInterpolation(index =>
        {
            reads++;
            if (index < 0 || index > 256) throw new InvalidOperationException("synthetic quarter index escaped");
            return directions.Sin(index);
        });
        var axis = new SourceRotation(1, 0, 0, 0);
        // Adjacent binary32 dots straddle the original float EPSILON guard.
        var linear = new SourceRotation(PoseMathFloat(0x3f7fff59u), 0, 0, 0);
        var curved = new SourceRotation(PoseMathFloat(0x3f7fff58u), 0, 0, 0);
        RotationBits("source float guard linear side",
            new SourceRotation(0.75f + 0.25f * linear.X, 0, 0, 0),
            guarded.Slerp(axis, linear, 0.25f));
        Program.Equal("linear side does not read the angle table", 0, reads);
        RotationBits("source float guard curved side",
            ExpectedSourceRotation(axis, curved, 0.25f, quarter, false),
            guarded.Slerp(axis, curved, 0.25f));
        Program.Check("curved side reads the actual table", reads > 0);
        reads = 0;
        RotationBits("zero inverse angle follows source search without wrapping the quarter table",
            ExpectedSourceRotation(axis, new SourceRotation(0, 1, 0, 0), 0.5f, quarter, false),
            guarded.Slerp(axis, new SourceRotation(0, 1, 0, 0), 0.5f));
        Program.Check("defined zero reads its actual table bracket", reads > 0);

        float halfPi = 3.1415926f * 0.5f;
        var rawOpposite = new SourceRotation(-1, 0, 0, 0);
        RotationBits("source raw antipodal branch uses captured float HALFPI",
            new SourceRotation(ExpectedSourceSine(0.75f * halfPi),
                ExpectedSourceSine(0.25f * halfPi), 0, 0),
            SourcePoseMath.Slerp(axis, rawOpposite, 0.25f, interpolation));
        Program.Equal("source Blend prepares sign before raw antipodal decision", -1f,
            interpolation.Blend(RepeatedPose(axis), RepeatedPose(rawOpposite), 0.25f).Rotations[0].X);
    }

    private static void CheckSourcePoseLayers(float[] quarter, ISourcePoseInterpolation interpolation)
    {
        var nonUnit = new SourceRotation(0, 0, 0, 1.00002f);
        SourceJointPose selected = RepeatedPose(nonUnit), next = AuthoredPose(0.55f, 1);
        var library = new PoseMathLibrary(selected, next);
        var zeroSample = new SourceAnimationSample(PoseMathLibrary.Sequence, 11, 43, 0f);
        var zeroLayer = PoseMathLayer(interpolation, zeroSample);
        Program.Check("source zero-frame sample retains selected-pose reference",
            ReferenceEquals(selected, SourceAnimationSampling.SampleLayer(library, zeroLayer)));
        var sameFrame = PoseMathLayer(interpolation,
            new SourceAnimationSample(PoseMathLibrary.Sequence, 11, 11, 0.25f));
        Program.Check("source same-frame sample retains selected-pose reference",
            ReferenceEquals(selected, SourceAnimationSampling.SampleLayer(library, sameFrame)));
        var frameBlend = PoseMathLayer(interpolation,
            new SourceAnimationSample(PoseMathLibrary.Sequence, 11, 43, 0.25f));
        PoseBits("source distinct selected-frame interpolation",
            ExpectedSourcePose(selected, next, 0.25f, quarter),
            SourceAnimationSampling.SampleLayer(library, frameBlend));
        var legacy = new SourceAnimationLayer("synthetic_pose_math", 42, zeroSample);
        Program.Equal("legacy layer constructor stays explicitly unqualified",
            SourcePoseInterpolationPolicy.LegacyNormalized, legacy.PoseInterpolationPolicy);
        Program.Equal("default layer does not infer source policy",
            SourcePoseInterpolationPolicy.LegacyNormalized, default(SourceAnimationLayer).PoseInterpolationPolicy);
        Program.Equal("legacy zero-frame sampler keeps old normalization", 1f,
            SourceAnimationSampling.SampleLayer(library, legacy).Rotations[0].W);
        Program.Throws<InvalidOperationException>("legacy layer cannot use source fast path",
            () => legacy.BlendSourcePose(null, next, 1f), "OriginalPortable");
        Program.Throws<ArgumentNullException>("declared source layer requires binding even without a transition",
            () => new SourceAnimationLayer(SourcePoseInterpolationPolicy.OriginalPortable, null,
                "synthetic_pose_math", 42, zeroSample));
        Program.Throws<ArgumentOutOfRangeException>("unknown numerical policy cannot take a fast path",
            () => new SourceAnimationLayer((SourcePoseInterpolationPolicy)99, interpolation,
                "synthetic_pose_math", 42, zeroSample));
        Program.Throws<ArgumentException>("table presence cannot promote a legacy tag",
            () => new SourceAnimationLayer(SourcePoseInterpolationPolicy.LegacyNormalized, interpolation,
                "synthetic_pose_math", 42, zeroSample));

        SourceJointPose from = AuthoredPose(0.1f, 0), middle = AuthoredPose(0.55f, 1);
        SourceJointPose destination = AuthoredPose(-0.35f, 2);
        SourceJointPose expectedPrior = ExpectedSourcePose(from, middle, 0.2f, quarter);
        SourceJointPose actualPrior = SourcePoseMath.Blend(from, middle, 0.2f, interpolation);
        PoseBits("source authored nested prior", expectedPrior, actualPrior);
        SourceJointPose expected = ExpectedSourcePose(expectedPrior, destination, 0.25f, quarter);
        SourceJointPose actual = SourcePoseMath.Blend(actualPrior, destination, 0.25f, interpolation);
        PoseBits("source authored nested final", expected, actual);
        var nestedLibrary = new PoseMathLibrary(from, destination);
        var target = new SourceAnimationSample(PoseMathLibrary.Sequence, 43, 43, 0f);
        var nested = PoseMathLayer(interpolation, target, actualPrior, 0.25f);
        var copied = nested;
        var view = new ActorAnimationView(7, ActorAction.Idle, AnimationSelectionEvidence.Approximate,
            copied, default(SourceVector3));
        Program.Check("source snapshot retains exact transition-origin reference",
            ReferenceEquals(actualPrior, view.Primary.TransitionFromPose));
        Program.Equal("source snapshot copies explicit numerical policy",
            SourcePoseInterpolationPolicy.OriginalPortable, view.Primary.PoseInterpolationPolicy);
        PoseBits("producer and exported helper use one source contract", expected,
            SourceAnimationSampling.SampleLayer(nestedLibrary, view.Primary));
        Program.Check("sampling does not rewrite captured transition origin",
            ReferenceEquals(actualPrior, nested.TransitionFromPose));
        Program.Check("completed source transition returns selected target without normalization",
            ReferenceEquals(destination, SourceAnimationSampling.SampleLayer(nestedLibrary,
                PoseMathLayer(interpolation, target, actualPrior, 1f))));

        var binding = typeof(SourceAnimationLayer).GetField("poseInterpolation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Program.Check("snapshot capability is private readonly and nonserialized",
            binding != null && binding.IsPrivate && binding.IsInitOnly &&
                binding.IsDefined(typeof(NonSerializedAttribute), false));
        foreach (var property in typeof(SourceAnimationLayer).GetProperties())
            Program.Check("snapshot has no public table or operation property " + property.Name,
                property.PropertyType != typeof(ISourcePoseInterpolation) &&
                property.PropertyType != typeof(float[]) &&
                !typeof(Delegate).IsAssignableFrom(property.PropertyType));
        string serialized = JsonSerializer.Serialize(nested);
        using (JsonDocument document = JsonDocument.Parse(serialized))
            Program.Equal("serialized snapshot exposes only the small numerical tag",
                (int)SourcePoseInterpolationPolicy.OriginalPortable,
                document.RootElement.GetProperty("PoseInterpolationPolicy").GetInt32());
        Program.Check("serialized synthetic snapshot does not expose the provider",
            !serialized.Contains("readQuarterSine") && !serialized.Contains("TrigBlob") &&
                !serialized.Contains("ISourcePoseInterpolation"));
    }

    private static SourceAnimationLayer PoseMathLayer(ISourcePoseInterpolation interpolation,
        SourceAnimationSample sample, SourceJointPose origin = null, float fraction = 1f)
    {
        return new SourceAnimationLayer(SourcePoseInterpolationPolicy.OriginalPortable, interpolation,
            "synthetic_pose_math", 42, sample, transitionFromPose: origin, transition01: fraction);
    }

    private static float PoseMathFloat(uint bits)
    {
        return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
    }

    private static SourceJointPose RepeatedPose(SourceRotation rotation)
    {
        var values = new SourceRotation[SourceTiming.JointCount];
        for (int i = 0; i < values.Length; i++) values[i] = rotation;
        return new SourceJointPose(values);
    }

    private static SourceJointPose AuthoredPose(float angle, int signOffset)
    {
        var values = new SourceRotation[SourceTiming.JointCount];
        for (int joint = 0; joint < values.Length; joint++)
        {
            double value = angle + joint * 0.01d;
            float sine = (float)Math.Sin(value), cosine = (float)Math.Cos(value);
            float sign = (joint + signOffset) % 3 == 0 ? -1f : 1f;
            values[joint] = new SourceRotation(joint % 3 == 0 ? sine * sign : 0f,
                joint % 3 == 1 ? sine * sign : 0f, joint % 3 == 2 ? sine * sign : 0f, cosine * sign);
        }
        return new SourceJointPose(values);
    }

    private static void RotationBits(string label, SourceRotation expected, SourceRotation actual)
    {
        Program.Equal(label + ".X", Program.Bits(expected.X), Program.Bits(actual.X));
        Program.Equal(label + ".Y", Program.Bits(expected.Y), Program.Bits(actual.Y));
        Program.Equal(label + ".Z", Program.Bits(expected.Z), Program.Bits(actual.Z));
        Program.Equal(label + ".W", Program.Bits(expected.W), Program.Bits(actual.W));
    }

    private static void PoseBits(string label, SourceJointPose expected, SourceJointPose actual)
    {
        Program.Equal(label + ".joint-count", SourceTiming.JointCount, actual.Rotations.Count);
        for (int joint = 0; joint < SourceTiming.JointCount; joint++)
            RotationBits(label + ".joint-" + joint, expected.Rotations[joint], actual.Rotations[joint]);
    }

    private static SourceJointPose ExpectedSourcePose(SourceJointPose source, SourceJointPose destination,
        float amount, float[] quarter)
    {
        var values = new SourceRotation[SourceTiming.JointCount];
        for (int joint = 0; joint < values.Length; joint++)
            values[joint] = ExpectedSourceRotation(source.Rotations[joint], destination.Rotations[joint],
                amount, quarter, true);
        return new SourceJointPose(values);
    }

    // Independent source-derived expectations scan our authored table, not the producer's binary search.
    private static SourceRotation ExpectedSourceRotation(SourceRotation source, SourceRotation destination,
        float amount, float[] quarter, bool prepareSource)
    {
        var p = new[] { source.X, source.Y, source.Z, source.W };
        var q = new[] { destination.X, destination.Y, destination.Z, destination.W };
        if (prepareSource)
        {
            float difference = 0f, opposite = 0f;
            for (int i = 0; i < 4; i++)
            {
                float d = p[i] - q[i], s = p[i] + q[i];
                difference += d * d; opposite += s * s;
            }
            if (!(difference < opposite))
                for (int i = 0; i < 4; i++) p[i] = -p[i];
        }
        if (amount == 0f) return new SourceRotation(p[0], p[1], p[2], p[3]);
        if (amount == 1f) return destination;
        float cosine = p[0] * q[0];
        for (int i = 1; i < 4; i++) cosine += p[i] * q[i];
        float a = 1f - amount, b = amount;
        if (1f + cosine <= 0.00001f)
        {
            q = new[] { -p[1], p[0], -p[3], p[2] };
            float halfPi = 3.1415926f * 0.5f;
            a = ExpectedSourceSine(a * halfPi); b = ExpectedSourceSine(b * halfPi);
        }
        else if (1f - cosine > 0.00001f)
        {
            float magnitude = cosine < 0f ? -cosine : cosine;
            int lower = magnitude == quarter[0] ? 0 : -1;
            for (int i = 0; lower < 0 && i < 256; i++)
                if (quarter[i] < magnitude && magnitude <= quarter[i + 1]) { lower = i; break; }
            if (lower < 0) throw new InvalidOperationException("Synthetic source expectation is not bracketed.");
            float part = (magnitude - quarter[lower]) / (quarter[lower + 1] - quarter[lower]);
            float asin = (lower + part) * 3.1415926f / 512f;
            if (cosine < 0f) asin = -asin;
            float omega = 3.1415926f / 2f - asin;
            float denominator = ExpectedSourceSine(omega);
            a = ExpectedSourceSine((1f - amount) * omega) / denominator;
            b = ExpectedSourceSine(amount * omega) / denominator;
        }
        var result = new float[4];
        for (int i = 0; i < 4; i++) result[i] = a * p[i] + b * q[i];
        return new SourceRotation(result[0], result[1], result[2], result[3]);
    }

    private static float ExpectedSourceSine(float value)
    {
        if (value >= 0f && value < 0.0001f) value = 0.0001f;
        if (value < 0f && value > -0.0001f) value = -0.0001f;
        bool negative = value < 0f;
        if (negative) value = -value;
        if (value >= 3.1415926f / 2f) value = 3.1415926f - value;
        float squared = value * value, cubed = value * squared;
        float fifth = cubed * squared, seventh = fifth * squared;
        float result = value - cubed * 0.166666f;
        result += fifth * 0.0083333f;
        result -= seventh * 0.00020f;
        return negative ? -result : result;
    }

    private sealed class PoseMathLibrary : ISourceAnimationLibrary
    {
        internal const string Sequence = "SYNTHETIC_POSE_MATH";
        private readonly SourceAnimationClip clip;

        internal PoseMathLibrary(SourceJointPose first, SourceJointPose second)
        {
            clip = new SourceAnimationClip(Sequence, "synthetic/pose-math", "synthetic", "synthetic",
                "synthetic", 0, 44, new[] { 11, 43 }, new[] {
                    new SourceAnimationFrame(11, new SourceVector3(0, 2, 0), new SourceVector3(0, 2, 0), first),
                    new SourceAnimationFrame(43, new SourceVector3(0, 4, 0), new SourceVector3(0, 4, 0), second)
                });
        }

        public ISourceAnimationClip GetClip(string sequenceName)
        {
            if (sequenceName != Sequence) throw new KeyNotFoundException("Unknown authored pose-math sequence.");
            return clip;
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
        // The source running throw retains MODE_TWOPART; the planted throw deliberately clears it.
        state.Split("qb_run_anim", "qb_rthrow_sr_anim", actor, 1, random);
        ActorAnimationView split = state.View(actor, 1);
        Program.Equal("two-part primary output mask", SourceTiming.LowerBodyJoints, split.Primary.JointMask);
        Program.Equal("two-part secondary output mask", SourceTiming.UpperBodyJoints, split.Secondary.JointMask);
        Program.Check("two-part independently specified layers", split.Primary.IsSpecified && split.Secondary.IsSpecified);
        Program.Equal("lower independent +X heading", 1f, split.Primary.FacingDirection.X);
        Program.Equal("upper independent +Z heading", 1f, split.Secondary.FacingDirection.Z);
        Program.Equal("lower script", "qb_run_anim", split.Primary.ScriptSymbol);
        Program.Equal("upper script", "qb_rthrow_sr_anim", split.Secondary.ScriptSymbol);
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
