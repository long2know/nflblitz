using System;
using System.Linq;
using System.Reflection;
using Blitz.Contracts;
using Blitz.Gameplay;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class SourceEvadeTargetTests
    {
        [Theory]
        [InlineData(-300.6584f, 85.45954f, 1, true)]
        [InlineData(389.5f, 0f, 3, false)]
        public void QuarterbackDiverUsesRetainedInteriorCellAndOriginalActor(
            float x, float z, int expectedStick, bool tie)
        {
            var fixture = new Fixture(11, new SourcePlanar(x, z));
            SourceRouteStep step = SourceDroneAI.StepRoute(fixture.State, fixture.Input, fixture.Random);

            Assert.Equal(586, fixture.Frame.Angle(10, 13));
            Assert.Equal(expectedStick, step.State.Stick);
            Assert.Equal(SourceDroneButtons.D, step.State.Buttons);
            Assert.Equal("spin_anim", step.Script);
            Assert.Equal("drone.c:Quarterback", step.SourceBranch);
            Assert.Equal(-1, step.Facing);
            Assert.Equal(768, fixture.Frame.Actors[11].RawFacing);
            Assert.Equal(0, fixture.Frame.Actors[10].RawFacing);
            Assert.Equal(tie ? 4 : 3, step.RandomCalls);
            Assert.Equal(tie
                ? new[] { "choose_roll_dir:random_override", "choose_roll_dir:tie",
                    "evade_dir:tie", "_drone_qb:evade_kind" }
                : new[] { "choose_roll_dir:random_override", "choose_roll_dir:tie",
                    "_drone_qb:evade_kind" },
                fixture.Random.Trace.Select(draw => draw.Site.SourceIdentity).ToArray());
            Assert.Equal(tie ? new[] { 10, 3, 2, 1 } : new[] { 10, 3, 1 },
                fixture.Random.Trace.Select(draw => draw.Site.BoundOrModulus).ToArray());
            Assert.Equal(tie ? new int?[] { 2147483646, 2147483647, 0, 1 }
                : new int?[] { 2147483646, 2147483647, 0 },
                fixture.Random.Trace.Select(draw => draw.Raw).ToArray());
            Assert.All(fixture.Random.Trace, draw => {
                Assert.Equal(11, draw.Site.ActorId);
                Assert.Equal(9025, draw.Site.SourceTick);
            });
        }

        [Fact]
        public void QuarterbackDiverStillRejectsOutsideAllocationRowZero()
        {
            var fixture = new Fixture(0, new SourcePlanar(0f, 0f));
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => SourceDroneAI.StepRoute(fixture.State, fixture.Input, fixture.Random));
            Assert.Contains("_drone_qb:diver -> evade_dir(attacker=-1)", error.Message);
            Assert.Contains("actor 0 sourceTick 9025", error.Message);
            Assert.Equal(new[] { 10, 3 },
                fixture.Random.Trace.Select(draw => draw.Site.BoundOrModulus).ToArray());
        }

        [Fact]
        public void InteriorAngleProjectionRetainsCacheAndRejectsOtherInvalidIndices()
        {
            var fixture = new Fixture(11, new SourcePlanar(-300.6584f, 85.45954f));
            MethodInfo method = typeof(SourceAiFrame).GetMethod("EvadeTargetTableAngle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var read = (Func<int, int, int>)method.CreateDelegate(
                typeof(Func<int, int, int>), fixture.Frame);
            for (int row = 1; row < 14; row++)
                Assert.Equal(fixture.Frame.Angle(row - 1, 13), read(row, -1));
            Assert.Equal(586, read(11, -1));
            for (int row = 0; row < 14; row++)
                for (int column = 0; column < 14; column++)
                    Assert.Equal(fixture.Frame.Angle(row, column), read(row, column));
            foreach (int row in new[] { -1, 0, 14 })
                Assert.Throws<ArgumentOutOfRangeException>(() => read(row, -1));
            foreach (int column in new[] { -2, 14 })
                Assert.Throws<ArgumentOutOfRangeException>(() => read(11, column));
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Frame.Angle(11, -1));
        }

        private sealed class Fixture
        {
            internal readonly SourceAiFrame Frame;
            internal readonly SourceRouteState State;
            internal readonly SourceRouteInput Input;
            internal readonly SourceInjectedRandom Random;

            internal Fixture(int quarterback, SourcePlanar position)
            {
                // Public synthetic DTO tables, as in source-runtime-integration; not TRIGS.H accuracy evidence.
                var quarter = new float[257];
                for (int i = 0; i < quarter.Length; i++) quarter[i] = i / 256f;
                int[] sticks = {
                    128,96,52,0,972,928,896,160,128,76,0,948,896,864,204,180,128,0,896,844,820,
                    256,256,256,-1,768,768,768,308,332,384,512,640,692,716,352,384,436,512,588,
                    640,672,384,416,460,512,564,608,640
                };
                var directions = new SourceDirectionMath(quarter, sticks,
                    "synthetic-public-quarter/v1", "port-source-49-way-direction-literals");
                var route = new SourceRouteProgram("source_only_qb", "synthetic-public-route/v1",
                    1, new[] { (int)SourceRouteOpcode.End });
                var book = new SourcePlaybook("synthetic-public-catalog/v1", "source-only-evade/v1",
                    new SourcePlayDefinition[0], new[] { route }, new SourceWeightedPlayPool[0], directions);
                var actors = new SourceAiActor[14];
                var distances = new float[14, 14];
                var angles = new int[14, 14];
                for (int row = 0; row < 14; row++)
                {
                    actors[row] = new SourceAiActor(row, new SourcePlanar(100f, 0f),
                        default(SourcePlanar), SourceActorMode.Receiver);
                    for (int column = 0; column < 14; column++)
                    {
                        distances[row, column] = row == column ? 0f : 100f;
                        angles[row, column] = row * 14 + column;
                    }
                }
                // The load aliases row10/13, NOT actor10's body, which is deliberately outside the field.
                actors[10] = new SourceAiActor(10, new SourcePlanar(391f, 184f),
                    default(SourcePlanar), SourceActorMode.Receiver);
                actors[quarterback] = new SourceAiActor(quarterback, position, default(SourcePlanar),
                    SourceActorMode.Quarterback, flags: SourceAiActorFlags.ReverseRoute,
                    facing: 768, rawFacing: 768);
                int diver = quarterback == 0 ? 8 : 4;
                actors[diver] = new SourceAiActor(diver, new SourcePlanar(-308.37872f, 58.754963f),
                    default(SourcePlanar), SourceActorMode.DefensiveBack,
                    flags: SourceAiActorFlags.DiveAttack, attackTarget: quarterback);
                distances[quarterback, diver] = distances[diver, quarterback] = 28.98686f;
                angles[10, 13] = 586;
                int side = quarterback / 7;
                Frame = new SourceAiFrame(actors, directions, new SourceRulesState(side, side, 1, 90, 100),
                    new SourceAiBall(SourceBallType.Pass, position, default(SourcePlanar), position, quarterback),
                    9025, 8950, 203, sourceDistances: distances, sourceAngles: angles);
                // Freeze the incoming detector cache, not a recalculation from the deliberately different poses.
                angles[10, 13] = 257;
                State = SourceRouteState.Begin(route).WithQuarterbackPlan(
                    new SourceQuarterbackPlan(62.4f, 4, -2, 1, false));
                int[] raw = { 2147483646, 2147483647, 0, 1 };
                int cursor = 0;
                Random = new SourceInjectedRandom(site => raw[cursor++],
                    new SourceRandomProvenance("source-only-evade/v1",
                        "explicit observed raw suffix; not native PRNG",
                        "bounded original-C caller regression", SourceRandomEvidence.ExternalUnverified));
                Input = new SourceRouteInput(Frame, quarterback, new SourceAiParameters(book, 5, 11),
                    new SourceRandomContext(9025, quarterback, "player_proc", quarterback + 3, "source-only-evade/v1"),
                    blocking: new SourceBlockAssignments { Rushers = new int[0] },
                    receivers: new SourceReceiverAssessment[0],
                    compatibility: SourceGameplayCompatibility.Portable);
            }
        }
    }
}
