using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Runtime
{
    public readonly struct SourceCabinetStick
    {
        public bool IsSpecified { get; }
        public int Ordinal { get; }
        public int StickX { get { return 3 - Ordinal % 7; } }
        public int StickZ { get { return 3 - Ordinal / 7; } }
        public int DigitalDirections { get; }
        public int RelativeHeading1024 { get; }
        internal SourceCabinetStick(int ordinal, int digital, int heading)
        { Ordinal = ordinal; DigitalDirections = digital; RelativeHeading1024 = heading; IsSpecified = true; }
    }

    public sealed class SourceCabinetQuantizer
    {
        private readonly SourceInterfaceInputTables tables;
        public SourceCabinetQuantizer(SourceInterfaceInputTables tables)
        { this.tables = tables ?? throw new ArgumentNullException(nameof(tables)); }

        public SourceCabinetStick Raw49(byte rawHallByte)
        {
            int ordinal = tables.Raw49[rawHallByte];
            return new SourceCabinetStick(ordinal, tables.Digital8[ordinal], tables.Directions[ordinal]);
        }

        public SourceCabinetStick SevenLevel(int screenRight, int screenDown)
        {
            if (screenRight < -3 || screenRight > 3 || screenDown < -3 || screenDown > 3)
                throw new ArgumentOutOfRangeException(nameof(screenRight), "Seven-level cabinet axes require -3..3.");
            return Raw49((byte)(Hall(screenDown) << 4 | Hall(screenRight)));
        }

        public SourceCabinetStick Analog(float screenRight, float screenDown)
        {
            return SevenLevel(Level(screenRight), Level(screenDown));
        }

        private static int Level(float axis)
        {
            if (float.IsNaN(axis) || float.IsInfinity(axis))
                throw new ArgumentOutOfRangeException(nameof(axis), "Host gamepad axes must be finite.");
            double clamped = Math.Max(-1d, Math.Min(1d, axis));
            return (int)Math.Round(clamped * 3d, MidpointRounding.AwayFromZero);
        }

        private static int Hall(int level)
        {
            if (level < 0) return (1 << -level) - 1;
            return level == 0 ? 0 : (15 << (3 - level)) & 15;
        }

        public static SourceButtons ButtonsFromSourceRegister(uint current, int stationId)
        {
            ValidateStationId(stationId);
            return (SourceButtons)((current >> (stationId * 8 + 4)) & 7u);
        }

        public static void ValidateStationId(int stationId)
        { if (stationId < 0 || stationId >= 4) throw new ArgumentOutOfRangeException(nameof(stationId)); }
    }

    public readonly struct SourceCabinetSample
    {
        public int StationId { get; }
        public SourceCabinetStick Stick { get; }
        public SourceButtons Held { get; }
        public SourceCommand Command { get; }
        public SourceCabinetSample(int stationId, SourceCabinetStick stick, SourceButtons held,
            SourceCommand command = default(SourceCommand))
        {
            SourceCabinetQuantizer.ValidateStationId(stationId);
            if (!stick.IsSpecified) throw new ArgumentException("An acquired/quantized source stick is required.", nameof(stick));
            if (((int)held & ~7) != 0) throw new ArgumentOutOfRangeException(nameof(held));
            StationId = stationId; Stick = stick; Held = held; Command = command;
        }
    }

    public sealed class SourceCabinetInputBuffer
    {
        private readonly SourceMatchConfiguration configuration;
        private readonly Queue<SourceInputFrame> ordered = new Queue<SourceInputFrame>();
        private readonly SourceButtons[] previous = new SourceButtons[4];
        private SourceInputFrame held;
        public bool WaitingForNeutral { get; private set; }
        public int PendingFrames { get { return ordered.Count; } }

        public SourceCabinetInputBuffer(SourceMatchConfiguration configuration)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            held = Neutral();
        }

        public void Capture(IReadOnlyList<SourceCabinetSample> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (samples.Count != configuration.Stations.Count)
                throw new ArgumentException("Sample all configured source stations, not a team-side shortcut.", nameof(samples));
            var seen = new bool[4];
            var frame = new SourceStationInput[samples.Count];
            var latest = new SourceStationInput[samples.Count];
            bool edge = false, neutral = true;
            for (int i = 0; i < samples.Count; i++)
            {
                SourceCabinetSample sample = samples[i];
                bool configured = false;
                foreach (SourceStationConfiguration station in configuration.Stations)
                    if (station.StationId == sample.StationId) { configured = true; break; }
                if (!configured || seen[sample.StationId])
                    throw new ArgumentException("Unknown/duplicate source station " + sample.StationId + ".", nameof(samples));
                seen[sample.StationId] = true;
                SourceButtons pressed = sample.Held & ~previous[sample.StationId];
                SourceButtons released = previous[sample.StationId] & ~sample.Held;
                previous[sample.StationId] = sample.Held;
                bool command = sample.Command.IsSpecified &&
                    (sample.Command.Action != SourceAction.None || sample.Command.OffenseFlipped.HasValue);
                edge |= pressed != SourceButtons.None || released != SourceButtons.None || command;
                neutral &= sample.Held == SourceButtons.None && sample.Stick.Ordinal == 24 && !command;
                frame[i] = new SourceStationInput(sample.StationId, sample.Stick.StickX, sample.Stick.StickZ,
                    sample.Held, pressed, released, sample.Command);
                latest[i] = new SourceStationInput(sample.StationId, sample.Stick.StickX, sample.Stick.StickZ, sample.Held);
            }
            if (WaitingForNeutral)
            {
                if (neutral)
                {
                    WaitingForNeutral = false;
                    Array.Clear(previous, 0, previous.Length);
                }
                return;
            }
            held = new SourceInputFrame(latest);
            if (edge)
            {
                if (ordered.Count >= 8192)
                    throw new InvalidOperationException("Source cabinet edge backlog exceeded 8192; refusing to silently drop input.");
                ordered.Enqueue(new SourceInputFrame(frame));
            }
        }

        public SourceInputFrame NextHostFrame()
        {
            if (WaitingForNeutral) return Neutral();
            return ordered.Count == 0 ? held : ordered.Dequeue();
        }

        public void Clear(bool requireNeutral = true)
        {
            ordered.Clear();
            Array.Clear(previous, 0, previous.Length);
            held = Neutral();
            WaitingForNeutral = requireNeutral;
        }

        private SourceInputFrame Neutral()
        {
            var values = new SourceStationInput[configuration.Stations.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = new SourceStationInput(configuration.Stations[i].StationId);
            return new SourceInputFrame(values);
        }
    }

    public static class SourceCabinetStations
    {
        public static SourceStationView View(SourceMatchView match, int stationId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            foreach (SourceStationView station in match.Stations)
                if (station.StationId == stationId) return station;
            throw new SourceInterfaceException("No actual source station view for configured station " + stationId + ".");
        }

        public static TeamId Team(SourceMatchView match, int stationId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            foreach (SourceStationConfiguration station in match.Configuration.Stations)
                if (station.StationId == stationId)
                {
                    View(match, stationId);
                    return station.Team;
                }
            throw new SourceInterfaceException("Raw input names unconfigured source station " + stationId + ".");
        }

        public static int Captain(SourceMatchView match, TeamId side)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            int first = -1, second = -1;
            foreach (SourceStationConfiguration station in match.Configuration.Stations)
            {
                if (station.Team != side) continue;
                View(match, station.StationId);
                if (first < 0) first = station.StationId;
                else if (second < 0) second = station.StationId;
                else throw new SourceInterfaceException("Original team captain mapping permits at most two human teammates.");
            }
            // Configuration order is teammate order; IDs, sides and actor IDs remain independent.
            return second < 0 || match.Possession.Captain == SourceCaptainSlot.First ? first : second;
        }
    }

    public readonly struct SourceCabinetSwitchEdges
    {
        public uint Held { get; }
        public uint Closed { get; }
        public uint Opened { get; }
        internal SourceCabinetSwitchEdges(uint held, uint closed, uint opened)
        { Held = held; Closed = closed; Opened = opened; }
    }

    public sealed class SourceCabinetSwitchScanner
    {
        private uint current = uint.MaxValue;
        private uint previous = uint.MaxValue;
        private uint previousPrevious = uint.MaxValue;

        public SourceCabinetSwitchEdges Scan(uint activeLowRegister)
        {
            previousPrevious = previous; previous = current; current = activeLowRegister;
            return new SourceCabinetSwitchEdges(~current, (previous & previousPrevious) & ~current,
                ~(previous | previousPrevious) & current);
        }

        public void Clear()
        { current = previous = previousPrevious = uint.MaxValue; }
    }
}
