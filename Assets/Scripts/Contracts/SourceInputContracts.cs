using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public enum SourcePlaybookKind { Offense, Defense }
    public enum SourcePlayPool
    {
        Punt = 0, FakePunt = 1, FieldGoal = 2, ShortOffense = 3, LongOffense = 4,
        PuntReturn = 5, BlockFieldGoal = 6, GoalLineDefense = 7, BlitzDefense = 8,
        ShortDefense = 9, LongDefense = 10, ShortDefenseCustom = 11, LongDefenseCustom = 12
    }

    [Flags]
    public enum SourceButtons { None = 0, A = 1, B = 2, C = 4 }

    public enum SourceAction
    {
        None, SelectPlay, Snap, Pass, Tackle, SwitchPlayer, Continue, SelectPat,
        SelectKickoff, Kick, Punt, Lateral, Audible, Spin, Juke, Hurdle, Dive,
        Jump, Swat, Catch, StiffArm
    }

    public readonly struct SourcePlaySelection
    {
        private readonly int slotIndex;
        private readonly int pageIndex;
        private readonly int poolIndex;
        public bool IsSpecified { get { return !string.IsNullOrEmpty(PlaySymbol); } }
        public string PlaySymbol { get; }
        public SourcePlaybookKind Playbook { get; }
        public int SlotIndex { get { return IsSpecified ? slotIndex : -1; } }
        public int PageIndex { get { return IsSpecified ? pageIndex : -1; } }
        public SourcePlayPool? Pool { get; }
        public int PoolIndex { get { return IsSpecified ? poolIndex : -1; } }

        public SourcePlaySelection(string playSymbol, SourcePlaybookKind playbook,
            int slotIndex = -1, int pageIndex = -1, SourcePlayPool? pool = null, int poolIndex = -1)
        {
            if (string.IsNullOrWhiteSpace(playSymbol))
                throw new ArgumentException("An original play symbol is required.", nameof(playSymbol));
            SourceContractGuard.Range((int)playbook, 0, 1, nameof(playbook));
            SourceContractGuard.Range(slotIndex, -1, int.MaxValue, nameof(slotIndex));
            SourceContractGuard.Range(pageIndex, -1, int.MaxValue, nameof(pageIndex));
            SourceContractGuard.Range(poolIndex, -1, int.MaxValue, nameof(poolIndex));
            if (pool.HasValue)
                SourceContractGuard.Range((int)pool.Value, 0, 12, nameof(pool));
            if (pool.HasValue != (poolIndex >= 0))
                throw new ArgumentException("A weighted pool and its original ordinal must be supplied together.", nameof(poolIndex));
            PlaySymbol = playSymbol;
            Playbook = playbook;
            this.slotIndex = slotIndex;
            this.pageIndex = pageIndex;
            Pool = pool;
            this.poolIndex = poolIndex;
        }
    }

    public readonly struct SourceCommand
    {
        private readonly int targetPlayerId;
        private readonly int audibleSlot;
        public bool IsSpecified { get; }
        public SourceAction Action { get; }
        public SourcePlaySelection Play { get; }
        public int TargetPlayerId { get { return IsSpecified ? targetPlayerId : -1; } }
        public SourcePatOption PatChoice { get; }
        public SourceKickoffType? KickoffChoice { get; }
        public int AudibleSlot { get { return IsSpecified ? audibleSlot : -1; } }

        public SourceCommand(SourceAction action, SourcePlaySelection play = default(SourcePlaySelection),
            int targetPlayerId = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourceKickoffType? kickoffChoice = null, int audibleSlot = -1)
        {
            SourceContractGuard.Range((int)action, 0, 20, nameof(action));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            SourceContractGuard.Range((int)patChoice, 0, 2, nameof(patChoice));
            if (kickoffChoice.HasValue)
                SourceContractGuard.Range((int)kickoffChoice.Value, 0, 2, nameof(kickoffChoice));
            SourceContractGuard.Range(audibleSlot, -1, int.MaxValue, nameof(audibleSlot));
            Action = action;
            Play = play;
            this.targetPlayerId = targetPlayerId;
            PatChoice = patChoice;
            KickoffChoice = kickoffChoice;
            this.audibleSlot = audibleSlot;
            IsSpecified = true;
        }
    }

    public readonly struct SourceStationInput
    {
        private readonly int stationId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public int StickX { get; }
        public int StickZ { get; }
        public SourceButtons Held { get; }
        public SourceButtons Pressed { get; }
        public SourceButtons Released { get; }
        public SourceCommand Command { get; }

        public SourceStationInput(int stationId, int stickX = 0, int stickZ = 0,
            SourceButtons held = SourceButtons.None, SourceButtons pressed = SourceButtons.None,
            SourceButtons released = SourceButtons.None, SourceCommand command = default(SourceCommand))
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Range(stickX, -3, 3, nameof(stickX));
            SourceContractGuard.Range(stickZ, -3, 3, nameof(stickZ));
            SourceContractGuard.Buttons(held, nameof(held));
            SourceContractGuard.Buttons(pressed, nameof(pressed));
            SourceContractGuard.Buttons(released, nameof(released));
            this.stationId = stationId;
            StickX = stickX;
            StickZ = stickZ;
            Held = held;
            Pressed = pressed;
            Released = released;
            Command = command;
            IsSpecified = true;
        }
    }

    public sealed class SourceInputFrame
    {
        public ReadOnlyCollection<SourceStationInput> Stations { get; }

        public SourceInputFrame(IReadOnlyList<SourceStationInput> stations)
        {
            if (stations == null)
                throw new ArgumentNullException(nameof(stations));
            if (stations.Count > SourceMatchLimits.MaximumHumanStations)
                throw new ArgumentException("At most four source stations can submit input.", nameof(stations));
            var copy = new SourceStationInput[stations.Count];
            var seen = new bool[SourceMatchLimits.MaximumHumanStations];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceStationInput station = stations[i];
                if (!station.IsSpecified || seen[station.StationId])
                    throw new ArgumentException("Source station inputs must be specified and unique.", nameof(stations));
                seen[station.StationId] = true;
                copy[i] = station;
            }
            Stations = Array.AsReadOnly(copy);
        }
    }

    public readonly struct SourceStationView
    {
        private readonly int stationId;
        private readonly int controlledPlayerId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public int ControlledPlayerId { get { return IsSpecified ? controlledPlayerId : -1; } }
        public float Turbo01 { get; }

        public SourceStationView(int stationId, int controlledPlayerId, float turbo01)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Player(controlledPlayerId, nameof(controlledPlayerId));
            SourceContractGuard.Finite(turbo01, nameof(turbo01));
            if (turbo01 < 0f || turbo01 > 1f)
                throw new ArgumentOutOfRangeException(nameof(turbo01));
            this.stationId = stationId;
            this.controlledPlayerId = controlledPlayerId;
            Turbo01 = turbo01;
            IsSpecified = true;
        }
    }

    public readonly struct SourceActorView
    {
        private readonly int controllerStationId;
        public bool IsSpecified { get; }
        public int ControllerStationId { get { return IsSpecified ? controllerStationId : -1; } }
        public SourceRosterIdentity Roster { get; }
        public int NoCatchSourceTicks { get; }

        public SourceActorView(int controllerStationId, SourceRosterIdentity roster = default(SourceRosterIdentity),
            int noCatchSourceTicks = 0)
        {
            SourceContractGuard.Station(controllerStationId, true, nameof(controllerStationId));
            SourceContractGuard.Range(noCatchSourceTicks, 0, int.MaxValue, nameof(noCatchSourceTicks));
            this.controllerStationId = controllerStationId;
            Roster = roster;
            NoCatchSourceTicks = noCatchSourceTicks;
            IsSpecified = true;
        }
    }

    public readonly struct SourceBallControlView
    {
        private readonly int controllerStationId;
        private readonly int controllerPlayerId;
        public bool IsSpecified { get; }
        public int ControllerStationId { get { return IsSpecified ? controllerStationId : -1; } }
        public int ControllerPlayerId { get { return IsSpecified ? controllerPlayerId : -1; } }
        public TeamId? LastPossession { get; }
        public int PossessionTicks { get; }
        public bool PossessionCreditEligible { get; }

        public SourceBallControlView(int controllerStationId, int controllerPlayerId,
            TeamId? lastPossession = null, int possessionTicks = 0, bool possessionCreditEligible = false)
        {
            SourceContractGuard.Station(controllerStationId, true, nameof(controllerStationId));
            SourceContractGuard.Player(controllerPlayerId, nameof(controllerPlayerId));
            if (lastPossession.HasValue)
                SourceContractGuard.Team(lastPossession.Value, nameof(lastPossession));
            SourceContractGuard.Range(possessionTicks, 0, int.MaxValue, nameof(possessionTicks));
            this.controllerStationId = controllerStationId;
            this.controllerPlayerId = controllerPlayerId;
            LastPossession = lastPossession;
            PossessionTicks = possessionTicks;
            PossessionCreditEligible = possessionCreditEligible;
            IsSpecified = true;
        }
    }

    public interface IPendingSourceInput
    {
        void ClearPendingInput();
    }
}
