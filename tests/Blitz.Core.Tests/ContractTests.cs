using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class ContractTests
    {
        [Fact]
        public void ReferenceConstantsAreThirtyYardSevenPlayerFourQuarterSlice()
        {
            Assert.Equal(30, SliceRules.FirstDownYards);
            Assert.Equal(7, SliceRules.PlayersPerTeam);
            Assert.Equal(4, SliceRules.MaximumDown);
            Assert.Equal(6, SliceRules.TouchdownPoints);
            Assert.Equal(2, SliceRules.SafetyPoints);
            Assert.Equal(4, SliceRules.QuarterCount);
            Assert.Equal(7200, SliceRules.QuarterTicks);
            Assert.Equal(60, SliceRules.TicksPerSecond);
            Assert.Equal(300, SliceRules.PreSnapTicks);
            Assert.Equal(100f, SliceRules.FieldLengthYards);
            Assert.Equal(10f, SliceRules.EndZoneDepthYards);
            Check.Near(23.442307f, SliceRules.FieldHalfWidthYards);
        }

        [Fact]
        public void InitialSnapshotHasCompleteStableActorsAndScrimmage()
        {
            var game = Check.NewGame();
            var state = game.State;
            Assert.Equal(0, state.Tick);
            Assert.Equal(0, state.PlayNumber);
            Assert.Equal(GamePhase.PlaySelect, state.Phase);
            Assert.Equal(TeamId.Home, state.Possession);
            Assert.Equal(1, state.OffenseDirection);
            Assert.Equal(1, state.Quarter);
            Assert.Equal(7200, state.QuarterTicksRemaining);
            Assert.Equal(1, state.Down);
            Assert.Equal(20f, state.LineOfScrimmage);
            Assert.Equal(50f, state.FirstDownLine);
            Assert.Equal(0, state.HomeScore);
            Assert.Equal(0, state.AwayScore);
            Assert.Equal(3, state.ControlledPlayerId);
            Assert.Equal(PlayOutcome.None, state.LastOutcome);
            Assert.Equal(BallMode.Held, state.Ball.Mode);
            Assert.Equal(3, state.Ball.CarrierId);
            Assert.Equal(-1, state.Ball.TargetPlayerId);
            for (int i = 0; i < 14; i++)
            {
                Assert.Equal(i, state.Players[i].Id);
                Assert.Equal(i < 7 ? TeamId.Home : TeamId.Away, state.Players[i].Team);
                Assert.Equal(i % 7, state.Players[i].Id % 7);
            }
        }

        [Fact]
        public void DefaultCommandIsNeutralRegardlessOfUnusedZeroFields()
        {
            var game = Check.NewGame();
            var initial = game.State;
            var command = default(PlayerCommand);
            Assert.Equal(0, command.TargetPlayerId);
            for (int i = 0; i < 600; i++)
            {
                Check.Step(game, command);
            }
            Assert.Equal(600, game.State.Tick);
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            Assert.Equal(7200, game.State.QuarterTicksRemaining);
            Assert.Equal(0, game.State.PlayNumber);
            Assert.Equal(initial.LineOfScrimmage, game.State.LineOfScrimmage);
            Check.ActorsUnmoved(initial, game.State);
            Assert.Empty(game.Events);
        }

        [Theory]
        [InlineData(PlayId.Run)]
        [InlineData(PlayId.QuickPass)]
        public void OffensiveSelectionEntersPreSnapAndPublishesBothRoles(PlayId play)
        {
            var game = Check.NewGame();
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: play));
            Assert.Equal(GamePhase.PreSnap, game.State.Phase);
            Assert.Equal(play, game.State.HomePlay);
            Assert.True(game.State.AwayPlay == PlayId.Cover || game.State.AwayPlay == PlayId.Blitz);
            Assert.Equal(300, game.State.PreSnapTicksRemaining);
            Assert.Equal(7200, game.State.QuarterTicksRemaining);
            Assert.Equal(0, game.State.PlayNumber);
            Check.EventTypes(game.Events, GameEventType.PlaySelected);
            Assert.Equal(TeamId.Home, game.Events[0].Team);
        }

        [Theory]
        [InlineData(PlayId.Cover)]
        [InlineData(PlayId.Blitz)]
        [InlineData((PlayId)(-1))]
        [InlineData((PlayId)999)]
        public void InvalidOffensivePlaySelectionCannotStartOrMoveActors(PlayId play)
        {
            var game = Check.NewGame();
            var before = game.State;
            Check.Step(game, new PlayerCommand(new FieldVector(1, 1), true, InputAction.SelectPlay, play));
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            Assert.Equal(0, game.State.PlayNumber);
            Assert.Equal(7200, game.State.QuarterTicksRemaining);
            Check.ActorsUnmoved(before, game.State);
            Assert.Empty(game.Events);
        }

        [Theory]
        [InlineData(InputAction.Snap)]
        [InlineData(InputAction.Pass)]
        [InlineData(InputAction.Tackle)]
        [InlineData(InputAction.SwitchDefender)]
        [InlineData(InputAction.Continue)]
        [InlineData((InputAction)(-1))]
        [InlineData((InputAction)1000)]
        public void WrongPhaseAndUnknownActionsAreIgnoredAtSelection(InputAction action)
        {
            var game = Check.NewGame();
            var before = game.State;
            Check.Step(game, new PlayerCommand(new FieldVector(float.NaN, float.PositiveInfinity),
                true, action, (PlayId)1000, int.MaxValue));
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            Assert.Equal(1, game.State.Down);
            Assert.Equal(20f, game.State.LineOfScrimmage);
            Assert.Equal(50f, game.State.FirstDownLine);
            Check.ActorsUnmoved(before, game.State);
            Assert.Empty(game.Events);
        }

        [Fact]
        public void HomeAutoSnapUsesThreeHundredSubsequentTicksNotSelectionTick()
        {
            var game = Check.NewGame();
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay));
            var linedUp = game.State;
            for (int i = 1; i <= 299; i++)
            {
                Check.Step(game, new PlayerCommand(new FieldVector(1, 1), true, InputAction.Pass, targetPlayerId: 4));
                Assert.Equal(GamePhase.PreSnap, game.State.Phase);
                Assert.Equal(300 - i, game.State.PreSnapTicksRemaining);
                Assert.Equal(0, game.State.PlayNumber);
                Assert.Equal(7200, game.State.QuarterTicksRemaining);
                Check.ActorsUnmoved(linedUp, game.State);
                Assert.Empty(game.Events);
            }
            Check.Step(game);
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(0, game.State.PreSnapTicksRemaining);
            Assert.Equal(1, game.State.PlayNumber);
            Assert.Equal(7200, game.State.QuarterTicksRemaining);
            Check.ActorsUnmoved(linedUp, game.State);
            Check.EventTypes(game.Events, GameEventType.Snapped);
            Check.Step(game);
            Assert.Equal(7199, game.State.QuarterTicksRemaining);
            Assert.Equal(1, game.State.PlayNumber);
        }

        [Fact]
        public void ManualSnapDoesNotAlsoMoveOrSpendLiveClock()
        {
            var game = Check.NewGame();
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.QuickPass));
            var before = game.State;
            Check.Step(game, new PlayerCommand(new FieldVector(1, 0), true, InputAction.Snap));
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(1, game.State.PlayNumber);
            Assert.Equal(7200, game.State.QuarterTicksRemaining);
            Check.ActorsUnmoved(before, game.State);
            Check.EventTypes(game.Events, GameEventType.Snapped);
            Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            Assert.Equal(1, game.State.PlayNumber);
            Assert.Equal(7199, game.State.QuarterTicksRemaining);
            Assert.DoesNotContain(game.Events, item => item.Type == GameEventType.Snapped);
        }

        [Theory]
        [InlineData(InputAction.SelectPlay)]
        [InlineData(InputAction.Pass)]
        [InlineData(InputAction.Tackle)]
        [InlineData(InputAction.SwitchDefender)]
        [InlineData(InputAction.Continue)]
        [InlineData((InputAction)1234)]
        public void PreSnapRejectsOtherActionsWhileCountdownStillAdvances(InputAction action)
        {
            var game = Check.NewGame();
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay));
            var before = game.State;
            Check.Step(game, new PlayerCommand(new FieldVector(1, 1), true, action, PlayId.QuickPass, 4));
            Assert.Equal(GamePhase.PreSnap, game.State.Phase);
            Assert.Equal(299, game.State.PreSnapTicksRemaining);
            Assert.Equal(PlayId.Run, game.State.HomePlay);
            Assert.Equal(0, game.State.PlayNumber);
            Check.ActorsUnmoved(before, game.State);
            Assert.Empty(game.Events);
        }

        [Fact]
        public void ContinueFreezesDeadBallThenSelectsTheResolvedNextScrimmage()
        {
            var game = Check.NewGame();
            Check.SelectAndSnap(game);
            Check.FinishPlay(game);
            var dead = game.State;
            var outcome = dead.LastOutcome;
            Assert.NotEqual(PlayOutcome.None, outcome);
            foreach (InputAction action in new[]
            {
                InputAction.SelectPlay, InputAction.Snap, InputAction.Pass,
                InputAction.Tackle, InputAction.SwitchDefender, (InputAction)999
            })
            {
                Check.Step(game, new PlayerCommand(new FieldVector(1, 1), true, action, targetPlayerId: 4));
                Assert.Equal(GamePhase.DeadBall, game.State.Phase);
                Assert.Equal(dead.QuarterTicksRemaining, game.State.QuarterTicksRemaining);
                Assert.Equal(dead.LineOfScrimmage, game.State.LineOfScrimmage);
                Assert.Equal(dead.FirstDownLine, game.State.FirstDownLine);
                Assert.Equal(dead.Down, game.State.Down);
                Assert.Equal(outcome, game.State.LastOutcome);
                Check.ActorsUnmoved(dead, game.State);
                Assert.Empty(game.Events);
            }
            Check.Step(game, new PlayerCommand(action: InputAction.Continue));
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            Assert.Equal(dead.LineOfScrimmage, game.State.LineOfScrimmage);
            Assert.Equal(dead.Down, game.State.Down);
            Assert.Equal(outcome, game.State.LastOutcome);
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay));
            Assert.Equal(GamePhase.PreSnap, game.State.Phase);
            Assert.Equal(outcome, game.State.LastOutcome);
            Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            Assert.Equal(PlayOutcome.None, game.State.LastOutcome);
        }

        [Fact]
        public void RetainedSnapshotsPlayersAndEventsDoNotChangeAfterStepOrRestart()
        {
            var game = Check.NewGame();
            var first = game.State;
            var firstPlayers = first.Players;
            var firstFields = Check.StateFields(first);
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.QuickPass));
            var selected = game.State;
            var selectedFields = Check.StateFields(selected);
            var selectedEvents = game.Events;
            var selectedEventFields = Check.EventFields(selectedEvents);
            Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            var snappedEvents = game.Events;
            var snappedEventFields = Check.EventFields(snappedEvents);
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            for (int i = 0; i < 20; i++)
            {
                Check.Step(game);
            }
            game.Restart(190);
            Check.SameFields(firstFields, Check.StateFields(first), "retained initial snapshot");
            Check.SameFields(selectedFields, Check.StateFields(selected), "retained selected snapshot");
            Check.SameFields(selectedEventFields, Check.EventFields(selectedEvents), "retained selection events");
            Check.SameFields(snappedEventFields, Check.EventFields(snappedEvents), "retained snap events");
            Assert.Same(firstPlayers, first.Players);
            Assert.NotSame(first, game.State);
            Assert.NotSame(firstPlayers, game.State.Players);
            var mutablePlayers = firstPlayers as IList<PlayerView>;
            if (mutablePlayers != null)
            {
                Assert.True(mutablePlayers.IsReadOnly, "Player collection must not permit snapshot mutation.");
                Assert.Throws<NotSupportedException>(() => mutablePlayers[0] = firstPlayers[1]);
            }
            var mutableEvents = selectedEvents as IList<GameEvent>;
            if (mutableEvents != null)
            {
                Assert.True(mutableEvents.IsReadOnly, "Event collection must not permit mutation.");
                Assert.Throws<NotSupportedException>(() => mutableEvents[0] = default(GameEvent));
            }
            Assert.Empty(game.Events);
        }

        [Fact]
        public void EventsAreLastStepOnlyIncludingReplacementWithAnEmptyList()
        {
            var game = Check.NewGame();
            var empty = game.Events;
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay));
            var selected = game.Events;
            Check.EventTypes(selected, GameEventType.PlaySelected);
            Check.Step(game);
            Assert.Empty(game.Events);
            Assert.Empty(empty);
            Check.EventTypes(selected, GameEventType.PlaySelected);
            Assert.NotSame(selected, game.Events);
            Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            Check.EventTypes(game.Events, GameEventType.Snapped);
        }

        [Theory]
        [InlineData(GamePhase.PlaySelect)]
        [InlineData(GamePhase.PreSnap)]
        [InlineData(GamePhase.LiveBall)]
        [InlineData(GamePhase.DeadBall)]
        public void RestartFromEveryOrdinaryPhaseReproducesFreshSeed(GamePhase phase)
        {
            var game = Check.NewGame(17);
            if (phase != GamePhase.PlaySelect)
            {
                Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.QuickPass));
            }
            if (phase == GamePhase.LiveBall || phase == GamePhase.DeadBall)
            {
                Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            }
            if (phase == GamePhase.DeadBall)
            {
                Check.FinishPlay(game);
            }
            Assert.Equal(phase, game.State.Phase);
            game.Restart(91);
            var fresh = Check.NewGame(91);
            Check.SameFields(Check.StateFields(fresh.State), Check.StateFields(game.State), "restart");
            Check.SameFields(Check.EventFields(fresh.Events), Check.EventFields(game.Events), "restart events");
        }
    }
}
