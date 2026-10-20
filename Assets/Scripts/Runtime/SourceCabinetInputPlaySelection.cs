using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Runtime
{
    public enum SourceCabinetMenuEventKind { Moved, Edge, PageChanged, Flipped, Hidden, Selected, ForcedSelected }

    public readonly struct SourceCabinetMenuEvent
    {
        public long SourceTick { get; }
        public int StationId { get; }
        public TeamId Side { get; }
        public SourceCabinetMenuEventKind Kind { get; }
        public string SoundBank { get { return SoundCall.HasValue || SoundSymbol != null ? "CURSORS" : null; } }
        public string SoundSymbol { get; }
        public int? SoundCall { get; }
        internal SourceCabinetMenuEvent(long tick, int station, TeamId side, SourceCabinetMenuEventKind kind, string symbol, int? call)
        { SourceTick = tick; StationId = station; Side = side; Kind = kind; SoundSymbol = symbol; SoundCall = call; }
    }

    public sealed class SourceCabinetPlaySelection
    {
        private readonly SourceInterfaceData data;
        private readonly Func<SourcePlaySelection, bool, SourceCommand> select;
        private readonly int[] cursor = new int[2];
        private readonly int[] page = new int[2];
        private readonly int[] previousJoy = { -1, -1 };
        private readonly int[] up = new int[2];
        private readonly int[] left = new int[2];
        private readonly bool[] hidden = new bool[2];
        private readonly bool[] selected = new bool[2];
        private int menuSleepRemaining;
        private long lastSourceTick = -1;
        private bool inMenu;
        public bool Flipped { get; private set; }
        public TeamId LastInteractedSide { get; private set; }
        public event Action<SourceCabinetMenuEvent> EventRaised;

        public SourceCabinetPlaySelection(SourceInterfaceData data,
            Func<SourcePlaySelection, bool, SourceCommand> selectionCommand = null)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            select = selectionCommand ?? SelectionCommand;
        }

        public int Cursor(TeamId side) { return cursor[(int)side]; }
        public int Page(TeamId side) { return page[(int)side]; }
        public bool Hidden(TeamId side) { return hidden[(int)side]; }
        public bool Selected(TeamId side) { return selected[(int)side]; }

        public SourceInputFrame ApplyAtSourceTick(IGameState state, long sourceTick, SourceInputFrame frame)
        {
            if (state == null || frame == null) throw new ArgumentNullException(state == null ? nameof(state) : nameof(frame));
            var source = state as ISourceMatchState;
            if (source == null || source.Match == null) throw new SourceInterfaceException("Source selection needs the actual match snapshot.");
            if (sourceTick <= lastSourceTick || lastSourceTick >= 0 && sourceTick != lastSourceTick + 1)
                throw new SourceInterfaceException("Source play selection must execute once for every ordered SOURCE tick, never per render/host tick.");
            if (sourceTick != source.SourceTick)
                throw new SourceInterfaceException("Source selection needs the current dispatched SOURCE snapshot.");
            lastSourceTick = sourceTick;
            SourceMatchView match = source.Match;
            bool menu = match.Phase == SourceMatchPhase.PlaySelection || match.Phase == SourceMatchPhase.PatSelection;
            if (!menu) { inMenu = false; return frame; }
            if (!MenuConsumerAwake(state, sourceTick)) return frame;
            if (!inMenu)
            {
                ResetMenu(); menuSleepRemaining = 20; inMenu = true;
            }
            else if (menuSleepRemaining > 0) menuSleepRemaining--;
            var output = new SourceStationInput[frame.Stations.Count];
            for (int i = 0; i < output.Length; i++)
            {
                SourceStationInput input = frame.Stations[i];
                TeamId side = SourceCabinetStations.Team(match, input.StationId);
                SourceCommand command = input.Command;
                bool chosen = match.SelectionTimer != null && match.SelectionTimer.Selected(side);
                bool forced = match.SelectionTimer != null && match.SelectionTimer.ForceSelection &&
                    match.SelectionTimer.ForceTick < sourceTick;
                if (menuSleepRemaining == 0 && SourceCabinetStations.Captain(match, side) == input.StationId &&
                    !selected[(int)side] && !chosen && (forced || !command.IsSpecified ||
                        command.Action == SourceAction.None && !command.OffenseFlipped.HasValue))
                {
                    SourceCommand picked = forced ? ChooseCurrent(state, sourceTick, side, input.StationId, true)
                        : Process(state, sourceTick, side, input);
                    if (picked.IsSpecified)
                    {
                        if (!forced && command.IsSpecified && command.Action != SourceAction.None)
                            throw new SourceInterfaceException("Two ordered source menu commands cannot be merged into one station sample.");
                        command = picked;
                    }
                }
                output[i] = new SourceStationInput(input.StationId, input.StickX, input.StickZ, input.Held,
                    input.Pressed, input.Released, command);
            }
            return new SourceInputFrame(output);
        }

        private static bool MenuConsumerAwake(IGameState state, long sourceTick)
        {
            var processState = state as ISourcePossessionState;
            if (processState == null || processState.Processes == null)
                throw new SourceInterfaceException("Source menu input requires the actual portable process snapshot.");
            SourceProcessView? consumer = null;
            foreach (SourceProcessView process in processState.Processes)
                if (process.Function == "game_proc")
                {
                    if (consumer.HasValue || process.Id != 0x0010)
                        throw new SourceInterfaceException("Source menu input requires one actual game_proc consumer.");
                    consumer = process;
                }
            if (!consumer.HasValue)
                throw new SourceInterfaceException("Source menu input requires one actual game_proc consumer.");
            // The portable menu is owned by game_proc; an input scan is not its wake.
            return !consumer.Value.Suspended && consumer.Value.LastWakeTick == sourceTick;
        }

        private SourceCommand Process(IGameState state, long tick, TeamId side, SourceStationInput input)
        {
            var source = (ISourceMatchState)state;
            int index = (int)side;
            SourceMatchView match = source.Match;
            int stick = (3 - input.StickZ) * 7 + 3 - input.StickX;
            int joy = data.Input.Digital8[stick];
            if (joy != previousJoy[index])
            {
                previousJoy[index] = joy;
                int old = cursor[index];
                switch (joy)
                {
                    case 1:
                        if (cursor[index] == 0 && ++up[index] == 2)
                        {
                            hidden[index] = true;
                            Emit(tick, input.StationId, side, SourceCabinetMenuEventKind.Hidden, "HIDE_CURSOR_SND", 60);
                        }
                        if (cursor[index] > 2) cursor[index] -= 3;
                        break;
                    case 2:
                        if (cursor[index] < 6) { up[index] = 0; cursor[index] += 3; }
                        break;
                    case 4:
                        if (cursor[index] % 3 != 0) cursor[index]--;
                        else if (cursor[index] == 0) left[index]++;
                        break;
                    case 8:
                        if (cursor[index] % 3 != 2) { up[index] = 0; cursor[index]++; }
                        break;
                }
                if (cursor[index] != old)
                {
                    LastInteractedSide = side;
                    Emit(tick, input.StationId, side, SourceCabinetMenuEventKind.Moved, null, side == TeamId.Home ? 63 : 64);
                }
                else if (joy != 0 && hidden[index])
                    Emit(tick, input.StationId, side, SourceCabinetMenuEventKind.Edge, null, side == TeamId.Home ? 67 : 68);
            }
            bool offense = side == match.Possession.Offense;
            switch (input.Pressed)
            {
                case SourceButtons.C:
                    if (left[index] >= 2 && cursor[index] == 0 && joy == 4)
                        throw new SourceInterfaceException("Original RANDOM_SELECT needs play_select_cntrl source-process RNG draws; no render/host random selection is substituted.");
                    page[index] = (page[index] + 1) % (offense ? 4 : 2);
                    LastInteractedSide = side;
                    Emit(tick, input.StationId, side, SourceCabinetMenuEventKind.PageChanged, "SWITCH_SND", 23);
                    break;
                case SourceButtons.A:
                    if (offense)
                    {
                        Flipped = !Flipped; LastInteractedSide = side;
                        Emit(tick, input.StationId, side, SourceCabinetMenuEventKind.Flipped, "SWITCH2_SND", 19);
                        return new SourceCommand(SourceAction.None, offenseFlipped: Flipped);
                    }
                    break;
                case SourceButtons.B:
                    return ChooseCurrent(state, tick, side, input.StationId, false);
                // The shipping #if 1 switch has NO combined-button menu action, including on defense.
            }
            return default(SourceCommand);
        }

        private SourceCommand ChooseCurrent(IGameState state, long tick, TeamId side, int station, bool forced)
        {
            SourceMatchView match = ((ISourceMatchState)state).Match;
            int index = (int)side;
            bool offense = side == match.Possession.Offense;
            SourceInterfacePlaySlot slot = Slot(state, side, page[index], cursor[index]);
            SourceCommand result;
            if (offense && match.PlayKind == SourcePlayKind.ExtraPoint && page[index] == 0 && cursor[index] == 0)
                result = new SourceCommand(SourceAction.SelectPat, patChoice: SourcePatOption.FreeKick);
            else
            {
                if (slot.PlaySymbol == null)
                    throw new SourceInterfaceException(forced
                        ? "Forced custom selection requires actual custom-play inventory/decompression and play_select_cntrl RNG fallback; no CPU/configured/random substitute is permitted."
                        : "Original selected play requires unavailable custom-play data; no legacy play substitute is permitted.");
                SourcePlaybookKind kind = offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense;
                result = select(new SourcePlaySelection(slot.PlaySymbol, kind, cursor[index], page[index]), offense && Flipped);
                if (!result.IsSpecified || result.Action != SourceAction.SelectPlay)
                    throw new SourceInterfaceException("Original play selector did not return the actual source choice.");
            }
            selected[index] = true; LastInteractedSide = side;
            Emit(tick, station, side, forced ? SourceCabinetMenuEventKind.ForcedSelected : SourceCabinetMenuEventKind.Selected, null, null);
            return result;
        }

        public SourceInterfacePlaySlot Slot(IGameState state, TeamId side, int requestedPage, int requestedSlot)
        {
            var source = state as ISourceMatchState;
            if (source == null || source.Match == null) throw new SourceInterfaceException("Play pages require an actual source match.");
            SourceMatchView match = source.Match;
            bool offense = side == match.Possession.Offense;
            int offenseTeam = match.Possession.Offense == TeamId.Home ? match.Configuration.HomeTeam.TeamIndex : match.Configuration.AwayTeam.TeamIndex;
            SourceInterfacePlaySlot result = data.PageSlot(offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense,
                requestedPage, requestedSlot, offenseTeam);
            if (requestedPage != 0) return result;
            double los = state.OffenseDirection > 0 ? state.LineOfScrimmage : 100 - state.LineOfScrimmage;
            if (!offense && requestedSlot == 1 && los < 85) result = data.PlaySlots["def_slots"][21];
            bool fieldGoal = los >= 50 && match.PlayKind != SourcePlayKind.ExtraPoint &&
                (state.Down == 4 || (match.Period.QuarterIndex == 1 || match.Period.QuarterIndex >= 3)
                    && source.PackedGameTime < 0x1000u);
            if (fieldGoal)
            {
                if (requestedSlot == 6) result = data.PlaySlots[offense ? "off_slots" : "def_slots"][offense ? 38 : 19];
                else if (offense && requestedSlot == 7) result = data.PlaySlots["off_slots"][39];
            }
            if (los < 50 && state.Down == 4)
            {
                if (offense && requestedSlot == 6) result = data.PlaySlots["off_slots"][36];
                else if (requestedSlot == 7) result = data.PlaySlots[offense ? "off_slots" : "def_slots"][offense ? 37 : 20];
            }
            if (offense && match.PlayKind == SourcePlayKind.ExtraPoint && requestedSlot == 0)
                result = data.PlaySlots["off_slots"][40];
            return result;
        }

        public void Clear()
        {
            ResetMenu(); inMenu = false; lastSourceTick = -1;
        }

        public void ClearPendingInput()
        { previousJoy[0] = previousJoy[1] = -1; }

        private void ResetMenu()
        {
            Array.Clear(cursor, 0, 2); Array.Clear(page, 0, 2); Array.Clear(up, 0, 2); Array.Clear(left, 0, 2);
            Array.Clear(hidden, 0, 2); Array.Clear(selected, 0, 2); previousJoy[0] = previousJoy[1] = -1;
            Flipped = false; menuSleepRemaining = 0;
        }

        private static SourceCommand SelectionCommand(SourcePlaySelection play, bool flipped)
        {
            return new SourceCommand(SourceAction.SelectPlay, play,
                offenseFlipped: play.Playbook == SourcePlaybookKind.Offense ? (bool?)flipped : null);
        }

        private void Emit(long tick, int station, TeamId side, SourceCabinetMenuEventKind kind, string symbol, int? call)
        {
            Action<SourceCabinetMenuEvent> handler = EventRaised;
            if (handler != null) handler(new SourceCabinetMenuEvent(tick, station, side, kind, symbol, call));
        }
    }
}
