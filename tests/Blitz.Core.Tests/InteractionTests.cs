using System;
using Blitz.Contracts;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class InteractionTests
    {
        [Fact]
        public void DirectionIsNormalizedWithoutGivingDiagonalOrHugeInputExtraSpeed()
        {
            var axis = Check.NewGame();
            var diagonal = Check.NewGame();
            var huge = Check.NewGame();
            var enormous = Check.NewGame();
            foreach (var game in new[] { axis, diagonal, huge, enormous })
            {
                Check.SelectAndSnap(game);
            }
            var start = Check.Player(axis.State, 3).Position;
            Check.Step(axis, new PlayerCommand(new FieldVector(1, 0)));
            Check.Step(diagonal, new PlayerCommand(new FieldVector(1, 1)));
            Check.Step(huge, new PlayerCommand(new FieldVector(10000, 10000)));
            Check.Step(enormous, new PlayerCommand(new FieldVector(float.MaxValue, float.MaxValue)));
            var a = Check.Player(axis.State, 3).Position;
            var d = Check.Player(diagonal.State, 3).Position;
            float axisDistance = Distance(start, a);
            float diagonalDistance = Distance(start, d);
            Assert.True(axisDistance > 0, "A live directional command must actually move the controlled actor.");
            Check.Near(axisDistance, diagonalDistance);
            Check.Near(d.X, Check.Player(huge.State, 3).Position.X);
            Check.Near(d.Z, Check.Player(huge.State, 3).Position.Z);
            Check.Near(d.X, Check.Player(enormous.State, 3).Position.X);
            Check.Near(d.Z, Check.Player(enormous.State, 3).Position.Z);
            Check.Near(d.X - start.X, d.Z - start.Z);
        }

        [Fact]
        public void SubUnitMovementRetainsItsMagnitudeAndZeroRemainsStill()
        {
            var full = Check.NewGame();
            var half = Check.NewGame();
            var still = Check.NewGame();
            foreach (var game in new[] { full, half, still })
            {
                Check.SelectAndSnap(game);
            }
            var start = Check.Player(full.State, 3).Position;
            Check.Step(full, new PlayerCommand(new FieldVector(0, 1)));
            Check.Step(half, new PlayerCommand(new FieldVector(0, 0.5f)));
            Check.Step(still);
            float fullDistance = Distance(start, Check.Player(full.State, 3).Position);
            float halfDistance = Distance(start, Check.Player(half.State, 3).Position);
            Assert.True(fullDistance > 0);
            Check.Near(fullDistance * 0.5f, halfDistance);
            Check.Near(start.X, Check.Player(still.State, 3).Position.X);
            Check.Near(start.Z, Check.Player(still.State, 3).Position.Z);
        }

        [Theory]
        [InlineData(float.NaN, 1f, 0f, 1f)]
        [InlineData(float.PositiveInfinity, 1f, 0f, 1f)]
        [InlineData(float.NegativeInfinity, -1f, 0f, -1f)]
        [InlineData(1f, float.NaN, 1f, 0f)]
        [InlineData(-1f, float.PositiveInfinity, -1f, 0f)]
        [InlineData(0f, float.NegativeInfinity, 0f, 0f)]
        [InlineData(float.NaN, float.PositiveInfinity, 0f, 0f)]
        public void NonfiniteMovementComponentsBecomeZeroIndependently(float x, float z, float cleanX, float cleanZ)
        {
            var invalid = Check.NewGame();
            var sanitized = Check.NewGame();
            Check.SelectAndSnap(invalid);
            Check.SelectAndSnap(sanitized);
            for (int i = 0; i < 10; i++)
            {
                Check.Step(invalid, new PlayerCommand(new FieldVector(x, z), true));
                Check.Step(sanitized, new PlayerCommand(new FieldVector(cleanX, cleanZ), true));
                Check.SameFields(Check.StateFields(sanitized.State), Check.StateFields(invalid.State), "sanitized input tick " + i);
                Check.SameFields(Check.EventFields(sanitized.Events), Check.EventFields(invalid.Events), "sanitized events tick " + i);
            }
        }

        [Fact]
        public void TurboDrainsRechargesAndStaysFiniteAndBoundedAcrossACompleteDrive()
        {
            var game = Check.NewGame();
            Check.SelectAndSnap(game);
            float before = Check.Player(game.State, game.State.ControlledPlayerId).Turbo01;
            Check.Step(game, new PlayerCommand(new FieldVector(0, 1), true));
            float drained = Check.Player(game.State, game.State.ControlledPlayerId).Turbo01;
            Assert.True(drained < before, "Turbo must consume a finite resource while moving.");
            Check.Step(game, new PlayerCommand(new FieldVector(0, 1), false));
            float recharged = Check.Player(game.State, game.State.ControlledPlayerId).Turbo01;
            Assert.True(recharged > drained, "Releasing turbo must restore some charge.");
            for (int tick = 0; tick < 5000; tick++)
            {
                var phase = game.State.Phase;
                PlayerCommand command;
                if (phase == GamePhase.PlaySelect)
                {
                    command = new PlayerCommand(action: InputAction.SelectPlay,
                        play: game.State.Possession == TeamId.Home ? PlayId.Run : PlayId.Blitz);
                }
                else if (phase == GamePhase.PreSnap && game.State.Possession == TeamId.Home)
                {
                    command = new PlayerCommand(action: InputAction.Snap);
                }
                else if (phase == GamePhase.DeadBall)
                {
                    command = new PlayerCommand(action: InputAction.Continue);
                }
                else
                {
                    command = new PlayerCommand(new FieldVector(0, tick % 180 < 90 ? 1 : -1), tick % 240 < 180);
                }
                Check.Step(game, command);
            }
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void OnlyOwnEligibleReceiverCanBeTargetedAndPassLeavesQuarterbackControl(int target)
        {
            var game = Check.NewGame();
            Check.SelectAndSnap(game, PlayId.QuickPass);
            Assert.Equal(3, game.State.Ball.CarrierId);
            Assert.True(Check.Player(game.State, 3).Position.X <= game.State.LineOfScrimmage);
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: target));
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(BallMode.InFlight, game.State.Ball.Mode);
            Assert.Equal(-1, game.State.Ball.CarrierId);
            Assert.Equal(target, game.State.Ball.TargetPlayerId);
            Assert.Equal(3, game.State.ControlledPlayerId);
            Check.EventTypes(game.Events, GameEventType.PassThrown);
            Assert.Equal(TeamId.Home, game.Events[0].Team);
            Assert.Equal(target, game.Events[0].PlayerId);
        }

        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(-2)]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(int.MaxValue)]
        public void IneligibleAndOutOfRangePassTargetsAreIgnored(int target)
        {
            var game = Check.NewGame();
            var neutral = Check.NewGame();
            Check.SelectAndSnap(game, PlayId.QuickPass);
            Check.SelectAndSnap(neutral, PlayId.QuickPass);
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: target));
            Check.Step(neutral);
            Check.SameFields(Check.StateFields(neutral.State), Check.StateFields(game.State), "invalid pass target " + target);
            Check.SameFields(Check.EventFields(neutral.Events), Check.EventFields(game.Events), "invalid target events");
            Assert.Equal(BallMode.Held, game.State.Ball.Mode);
            Assert.Equal(3, game.State.Ball.CarrierId);
        }

        [Fact]
        public void AutomaticCatchTransfersBallAndControlAndCannotStartASecondPass()
        {
            var game = Check.NewGame();
            Check.SelectAndSnap(game, PlayId.QuickPass);
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            int throws = 1;
            int catches = 0;
            bool sawArc = game.State.Ball.Height > 0;
            for (int i = 0; i < 240 && game.State.Ball.Mode == BallMode.InFlight; i++)
            {
                Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 5));
                sawArc |= game.State.Ball.Height > 0;
                foreach (var item in game.Events)
                {
                    if (item.Type == GameEventType.PassThrown) throws++;
                    if (item.Type == GameEventType.PassCaught)
                    {
                        catches++;
                        Assert.Equal(TeamId.Home, item.Team);
                        Assert.Equal(4, item.PlayerId);
                    }
                }
            }
            Assert.True(sawArc, "Flight must expose a nonzero cosmetic arc, not teleport the ball.");
            Assert.Equal(1, throws);
            Assert.Equal(1, catches);
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(BallMode.Held, game.State.Ball.Mode);
            Assert.Equal(4, game.State.Ball.CarrierId);
            Assert.Equal(-1, game.State.Ball.TargetPlayerId);
            Assert.Equal(4, game.State.ControlledPlayerId);
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 5));
            Assert.DoesNotContain(game.Events, item => item.Type == GameEventType.PassThrown);
            Assert.NotEqual(BallMode.InFlight, game.State.Ball.Mode);
        }

        [Fact]
        public void CrossingSnapLineMakesQuarterbackForwardPassIllegal()
        {
            var game = Check.NewGame();
            Check.SelectAndSnap(game, PlayId.QuickPass);
            float los = game.State.LineOfScrimmage;
            for (int i = 0; i < 240 && Check.Player(game.State, 3).Position.X <= los &&
                game.State.Phase == GamePhase.LiveBall; i++)
            {
                Check.Step(game, new PlayerCommand(new FieldVector(1, 0), true));
            }
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.True(Check.Player(game.State, 3).Position.X > los, "Scenario must actually cross the snap LOS.");
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            Assert.DoesNotContain(game.Events, item => item.Type == GameEventType.PassThrown);
            Assert.NotEqual(BallMode.InFlight, game.State.Ball.Mode);
        }

        [Theory]
        [InlineData(InputAction.SelectPlay)]
        [InlineData(InputAction.Snap)]
        [InlineData(InputAction.Continue)]
        [InlineData(InputAction.Tackle)]
        [InlineData(InputAction.SwitchDefender)]
        [InlineData((InputAction)(-1))]
        [InlineData((InputAction)999)]
        public void IrrelevantLiveOffenseActionsDoNotChangeOtherwiseIdenticalStep(InputAction action)
        {
            var game = Check.NewGame();
            var neutral = Check.NewGame();
            Check.SelectAndSnap(game);
            Check.SelectAndSnap(neutral);
            Check.Step(game, new PlayerCommand(new FieldVector(0, 1), true, action, (PlayId)999, int.MaxValue));
            Check.Step(neutral, new PlayerCommand(new FieldVector(0, 1), true));
            Check.SameFields(Check.StateFields(neutral.State), Check.StateFields(game.State), "invalid live offense action");
            Check.SameFields(Check.EventFields(neutral.Events), Check.EventFields(game.Events), "invalid live offense events");
        }

        [Fact]
        public void HomeKeepsSevenStableDefendersAfterRealTurnoverAndAwayAutoSnapsInSixtyTicks()
        {
            var game = Check.NewGame();
            Check.GiveAwayPossession(game);
            Assert.InRange(game.State.ControlledPlayerId, 0, 6);
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.Blitz));
            Assert.Equal(PlayId.Blitz, game.State.HomePlay);
            Assert.True(game.State.AwayPlay == PlayId.Run || game.State.AwayPlay == PlayId.QuickPass);
            Assert.Equal(GamePhase.PreSnap, game.State.Phase);
            Assert.Equal(60, game.State.PreSnapTicksRemaining);
            var linedUp = game.State;
            int nearest = -1;
            double bestDistance = double.MaxValue;
            for (int id = 0; id < 7; id++)
            {
                var position = linedUp.Players[id].Position;
                double x = position.X - linedUp.Ball.Position.X;
                double z = position.Z - linedUp.Ball.Position.Z;
                double distance = x * x + z * z;
                if (distance < bestDistance)
                {
                    nearest = id;
                    bestDistance = distance;
                }
            }
            Assert.Equal(nearest, linedUp.ControlledPlayerId);
            for (int i = 1; i <= 59; i++)
            {
                Check.Step(game, new PlayerCommand(new FieldVector(1, 1), true, InputAction.Snap));
                Assert.Equal(GamePhase.PreSnap, game.State.Phase);
                Assert.Equal(60 - i, game.State.PreSnapTicksRemaining);
                Assert.Equal(linedUp.PlayNumber, game.State.PlayNumber);
                Check.ActorsUnmoved(linedUp, game.State);
            }
            Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(linedUp.PlayNumber + 1, game.State.PlayNumber);
            Assert.Equal(linedUp.QuarterTicksRemaining, game.State.QuarterTicksRemaining);
            Check.EventTypes(game.Events, GameEventType.Snapped);
            Assert.Equal(TeamId.Away, game.Events[0].Team);
            Assert.Equal(10, game.State.Ball.CarrierId);
        }

        [Theory]
        [InlineData(PlayId.Run)]
        [InlineData(PlayId.QuickPass)]
        [InlineData((PlayId)(-1))]
        [InlineData((PlayId)999)]
        public void DefensiveSelectionRejectsOffensiveAndInvalidPlayIds(PlayId play)
        {
            var game = Check.NewGame();
            Check.GiveAwayPossession(game);
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: play));
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            Assert.Empty(game.Events);
        }

        [Fact]
        public void SwitchDefenderSelectsNearestOtherHomePlayerWithStableTieBreak()
        {
            var game = Check.NewGame();
            Check.GiveAwayPossession(game);
            Check.SelectAndSnap(game);
            var before = game.State;
            int old = before.ControlledPlayerId;
            int expected = -1;
            double nearest = double.MaxValue;
            for (int id = 0; id < 7; id++)
            {
                if (id == old) continue;
                var player = before.Players[id];
                double x = player.Position.X - before.Ball.Position.X;
                double z = player.Position.Z - before.Ball.Position.Z;
                double distance = x * x + z * z;
                if (distance < nearest)
                {
                    nearest = distance;
                    expected = id;
                }
            }
            Check.Step(game, new PlayerCommand(action: InputAction.SwitchDefender));
            Assert.NotEqual(old, game.State.ControlledPlayerId);
            Assert.Equal(expected, game.State.ControlledPlayerId);
            Assert.InRange(game.State.ControlledPlayerId, 0, 6);
        }

        private static float Distance(FieldVector a, FieldVector b)
        {
            float x = a.X - b.X;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }
    }
}
