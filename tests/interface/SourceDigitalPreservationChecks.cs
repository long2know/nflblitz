using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;

internal static class SourceDigitalPreservationChecks
{
    private sealed class FieldFailure : Exception
    {
        internal readonly string Field;
        internal readonly object Expected;
        internal readonly object Actual;

        internal FieldFailure(string field, object expected, object actual) : base(field)
        { Field = field; Expected = expected; Actual = actual; }
    }

    private sealed class Fixture
    {
        internal readonly string Name;
        internal readonly int[] ConfigurationIds;
        internal readonly int[] SampleIds;
        internal readonly int[] Actors;
        internal readonly int Home;
        internal readonly int Away;
        internal readonly SourceMatchConfiguration Configuration;

        internal Fixture(bool four)
        {
            Name = four ? "four" : "two";
            ConfigurationIds = four ? new[] { 3, 0, 2, 1 } : new[] { 2, 0 };
            SampleIds = four ? new[] { 1, 3, 0, 2 } : new[] { 0, 2 };
            Actors = four ? new[] { 3, 10, 4, 11 } : new[] { 3, 10 };
            Home = four ? 3 : 2;
            Away = 0;
            var stations = four ? new[] {
                new SourceStationConfiguration(3, TeamId.Home, 3),
                new SourceStationConfiguration(0, TeamId.Away, 10),
                new SourceStationConfiguration(2, TeamId.Home, 4),
                new SourceStationConfiguration(1, TeamId.Away, 11)
            } : new[] {
                new SourceStationConfiguration(2, TeamId.Home, 3),
                new SourceStationConfiguration(0, TeamId.Away, 10)
            };
            var records = new[] { 0, 1, 2, 3, 4, 5, 6 };
            Configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                four ? SourceStationLayout.FourStation : SourceStationLayout.TwoStation,
                new SourceTeamSelection(5, records), new SourceTeamSelection(1, records),
                stations, TeamId.Home, SourceCaptainSlot.Second);
        }
    }

    private static readonly string[] Modes = { "preserve", "explicit-false", "legacy" };
    private static readonly Dictionary<string, int> ExpectedCounts = new Dictionary<string, int> {
        { "fixture", 20 }, { "D1", 508 }, { "D2", 344 }, { "D3", 508 },
        { "D4", 1608 }, { "D5", 3984 }, { "D6", 5172 }, { "D7", 620 }, { "D8", 298980 }
    };
    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    private static readonly List<string> Completed = new List<string>();
    private static string group = "setup";
    private static string context = "setup";
    private static int assertions;
    private static SourceInterfaceInputTables tables;
    private static SourceCabinetQuantizer quantizer;
    private static Fixture[] fixtures;

    private static SourceInterfaceInputTables Tables()
    {
        var raw = new int[256];
        var digital = new int[49];
        var directions = new int[49];
        for (int i = 0; i < raw.Length; i++) raw[i] = 24;
        raw[1] = 3; raw[2] = 45; raw[3] = 10;
        digital[3] = 1; digital[45] = 2; digital[10] = 1;
        directions[24] = -1;
        return new SourceInterfaceInputTables(raw, digital, directions);
    }

    private static void Equal<T>(T actual, T expected, string field)
    {
        assertions++;
        Counts[group] = Counts[group] + 1;
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new FieldFailure(group + "." + context + "." + field, expected, actual);
    }

    private static void Reject<T>(Action action, string field) where T : Exception
    {
        bool rejected = false;
        try { action(); }
        catch (T) { rejected = true; }
        Equal(rejected, true, field);
    }

    private static void Group(string name, Action action)
    {
        group = name; context = "all"; Counts[name] = 0;
        action();
        if (Counts[name] != ExpectedCounts[name])
            throw new FieldFailure(name + ".AssertionBudget", ExpectedCounts[name], Counts[name]);
        Completed.Add(name);
    }

    private static void EachTarget(int mode, Action<Fixture, int, int> action)
    {
        foreach (Fixture fixture in fixtures)
            foreach (int target in new[] { fixture.Home, fixture.Away })
            {
                context = Modes[mode] + "." + fixture.Name + "." + (target == fixture.Home ? "home" : "away");
                action(fixture, target, mode);
            }
    }

    private static SourceCabinetInputBuffer Buffer(Fixture fixture, int mode)
    {
#if DIGITAL_UNITY
        if (mode == 0)
            return new SourceCabinetUnityInput(fixture.Configuration, tables, nativeGamepadsEnabled: false,
                preserveSourceDigitalChanges: true).Buffer;
        if (mode == 1)
            return new SourceCabinetUnityInput(fixture.Configuration, tables, nativeGamepadsEnabled: false,
                preserveSourceDigitalChanges: false).Buffer;
        return new SourceCabinetUnityInput(fixture.Configuration, tables, nativeGamepadsEnabled: false).Buffer;
#else
        if (mode == 0) return new SourceCabinetInputBuffer(fixture.Configuration, preserveSourceDigitalChanges: true);
        if (mode == 1) return new SourceCabinetInputBuffer(fixture.Configuration, preserveSourceDigitalChanges: false);
        return new SourceCabinetInputBuffer(fixture.Configuration);
#endif
    }

    private static SourceCabinetSample[] Samples(Fixture fixture, int first = -1, byte raw = 0,
        int buttons = 0, SourceCommand command = default(SourceCommand),
        int second = -1, byte secondRaw = 0, int secondButtons = 0)
    {
        var samples = new SourceCabinetSample[fixture.SampleIds.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            int id = fixture.SampleIds[i];
            samples[i] = new SourceCabinetSample(id,
                quantizer.Raw49(id == first ? raw : id == second ? secondRaw : (byte)0),
                (SourceButtons)(id == first ? buttons : id == second ? secondButtons : 0),
                id == first ? command : default(SourceCommand));
        }
        return samples;
    }

    private static void Capture(SourceCabinetInputBuffer buffer, Fixture fixture, int target,
        byte raw, int buttons = 0, SourceCommand command = default(SourceCommand))
    { buffer.Capture(Samples(fixture, target, raw, buttons, command)); }

    private static int[] Row(int id, int x = 0, int z = 0, int held = 0, int pressed = 0,
        int released = 0, int digital = 0, int command = 0, int action = 0, int flip = -1)
    { return new[] { id, x, z, held, pressed, released, digital, command, action, flip }; }

    private static void Frame(SourceInputFrame frame, int[] ids, string phase, params int[][] changes)
    {
        Equal(frame != null, true, phase + ".Specified");
        Equal(frame.Stations.Count, ids.Length, phase + ".StationCount");
        for (int i = 0; i < ids.Length; i++)
        {
            int[] row = Row(ids[i]);
            foreach (int[] change in changes) if (change[0] == ids[i]) row = change;
            SourceStationInput actual = frame.Stations[i];
            string field = phase + ".station[" + ids[i] + "]";
            Equal(actual.StationId, row[0], field + ".StationId");
            Equal(actual.IsSpecified, true, field + ".IsSpecified");
            Equal(actual.StickX, row[1], field + ".StickX");
            Equal(actual.StickZ, row[2], field + ".StickZ");
            Equal((int)actual.Held, row[3], field + ".Held");
            Equal((int)actual.Pressed, row[4], field + ".Pressed");
            Equal((int)actual.Released, row[5], field + ".Released");
            Equal(actual.DigitalCurrent.HasValue, true, field + ".DigitalCurrentSpecified");
            Equal((int)actual.DigitalCurrent.Value, row[6], field + ".DigitalCurrent");
            Equal(actual.Command.IsSpecified, row[7] == 1, field + ".CommandSpecified");
            Equal((int)actual.Command.Action, row[8], field + ".Action");
            Equal(actual.Command.OffenseFlipped.HasValue, row[9] >= 0, field + ".FlipSpecified");
            Equal(actual.Command.OffenseFlipped.GetValueOrDefault(), row[9] == 1, field + ".Flip");
        }
    }

    private static SourceStationInput At(SourceInputFrame frame, int station)
    {
        foreach (SourceStationInput input in frame.Stations)
            if (input.StationId == station) return input;
        throw new FieldFailure(group + "." + context + ".StationLookup", station, "absent");
    }

    private static void FixtureFields()
    {
        byte[] raw = { 0, 1, 2, 3 };
        int[] ordinal = { 24, 3, 45, 10 };
        int[] x = { 0, 0, 0, 0 };
        int[] z = { 0, 3, -3, 2 };
        int[] digital = { 0, 1, 2, 1 };
        int[] heading = { -1, 0, 0, 0 };
        for (int i = 0; i < raw.Length; i++)
        {
            context = "raw[" + raw[i] + "]";
            SourceCabinetStick actual = quantizer.Raw49(raw[i]);
            Equal(actual.Ordinal, ordinal[i], "Ordinal");
            Equal(actual.StickX, x[i], "StickX");
            Equal(actual.StickZ, z[i], "StickZ");
            Equal(actual.DigitalDirections, digital[i], "DigitalDirections");
            Equal(actual.RelativeHeading1024, heading[i], "RelativeHeading1024");
        }
    }

    private static void D1(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1);
        Capture(buffer, fixture, target, 0);
        Equal(buffer.PendingFrames, 2, "captured.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "up", Row(target, z: 3, digital: 1));
        Equal(buffer.PendingFrames, 1, "after-up.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "neutral");
        Equal(buffer.PendingFrames, 0, "after-neutral.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "held-neutral");
        Equal(buffer.PendingFrames, 0, "held.PendingFrames");
    }

    private static void D2(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1);
        Capture(buffer, fixture, target, 0);
        Equal(buffer.PendingFrames, 0, "coalesced.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "latest-neutral");
        Equal(buffer.PendingFrames, 0, "drained.PendingFrames");
    }

    private static void D3(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1);
        Capture(buffer, fixture, target, 1);
        Capture(buffer, fixture, target, 1);
        Equal(buffer.PendingFrames, 1, "repeated-up.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "single-up", Row(target, z: 3, digital: 1));
        Equal(buffer.PendingFrames, 0, "drained.PendingFrames");
        Capture(buffer, fixture, target, 3);
        Equal(buffer.PendingFrames, 0, "different49-same-digital.PendingFrames");
        SourceInputFrame latest = buffer.NextHostFrame();
        Frame(latest, fixture.SampleIds, "latest49", Row(target, z: 2, digital: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "held49", Row(target, z: 2, digital: 1));
        var scan = new SourceDigitalSwitchScan(target, 301L, At(latest, target).DigitalCurrent.Value,
            SourceDigitalDirections.Up, SourceDigitalDirections.Up);
        Equal((int)scan.Close, 0, "held.Close");
    }

    private static void D4(Fixture fixture, int mode)
    {
        context = Modes[mode] + "." + fixture.Name + ".both-teams";
        Equal(fixture.Configuration.Stations.Count, fixture.ConfigurationIds.Length, "configuration.Count");
        for (int i = 0; i < fixture.ConfigurationIds.Length; i++)
        {
            SourceStationConfiguration station = fixture.Configuration.Stations[i];
            Equal(station.StationId, fixture.ConfigurationIds[i], "configuration[" + i + "].StationId");
            Equal(station.Team, i % 2 == 0 ? TeamId.Home : TeamId.Away, "configuration[" + i + "].Team");
            Equal(station.InitialPlayerId, fixture.Actors[i], "configuration[" + i + "].InitialPlayerId");
        }
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        buffer.Capture(Samples(fixture, fixture.Home, 1, 1, second: fixture.Away, secondRaw: 2, secondButtons: 4));
        buffer.Capture(Samples(fixture, fixture.Home, 0, second: fixture.Away, secondRaw: 2, secondButtons: 4));
        buffer.Capture(Samples(fixture));
        Equal(buffer.PendingFrames, 3, "interleaved.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "both-held",
            Row(fixture.Home, z: 3, held: 1, pressed: 1, digital: 1),
            Row(fixture.Away, z: -3, held: 4, pressed: 4, digital: 2));
        Equal(buffer.PendingFrames, 2, "after-both.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "home-release",
            Row(fixture.Home, released: 1), Row(fixture.Away, z: -3, held: 4, digital: 2));
        Equal(buffer.PendingFrames, 1, "after-home.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "away-release", Row(fixture.Away, released: 4));
        Equal(buffer.PendingFrames, 0, "after-away.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "both-neutral");
        Equal(buffer.PendingFrames, 0, "held.PendingFrames");
        Reject<ArgumentException>(() => buffer.Capture(new[] { Samples(fixture)[0] }), "omitted.Rejected");
        SourceCabinetSample[] duplicate = Samples(fixture);
        duplicate[1] = duplicate[0];
        Reject<ArgumentException>(() => buffer.Capture(duplicate), "duplicate.Rejected");
        if (fixture.ConfigurationIds.Length == 2)
        {
            SourceCabinetSample[] unknown = Samples(fixture);
            unknown[0] = new SourceCabinetSample(3, quantizer.Raw49(0), SourceButtons.None);
            Reject<ArgumentException>(() => buffer.Capture(unknown), "unconfigured.Rejected");
        }
        else
            Reject<ArgumentOutOfRangeException>(() => new SourceCabinetSample(4, quantizer.Raw49(0), SourceButtons.None),
                "unconfigured.Rejected");
        buffer = Buffer(fixture, mode);
        buffer.Capture(Samples(fixture, fixture.Home, 1));
        Equal(buffer.PendingFrames, mode == 0 ? 1 : 0, "digital-home.PendingFrames");
        buffer.Capture(Samples(fixture, fixture.Home, 3, second: fixture.Away, secondRaw: 1));
        Equal(buffer.PendingFrames, mode == 0 ? 2 : 0, "digital-away.PendingFrames");
        buffer.Capture(Samples(fixture, fixture.Home, 0, second: fixture.Away, secondRaw: 3));
        Equal(buffer.PendingFrames, mode == 0 ? 3 : 0, "digital-home-release.PendingFrames");
        if (mode == 0)
        {
            Frame(buffer.NextHostFrame(), fixture.SampleIds, "digital-home", Row(fixture.Home, z: 3, digital: 1));
            Frame(buffer.NextHostFrame(), fixture.SampleIds, "digital-both",
                Row(fixture.Home, z: 2, digital: 1), Row(fixture.Away, z: 3, digital: 1));
            Frame(buffer.NextHostFrame(), fixture.SampleIds, "digital-home-release",
                Row(fixture.Away, z: 2, digital: 1));
        }
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "digital-away-held", Row(fixture.Away, z: 2, digital: 1));
        Equal(buffer.PendingFrames, 0, "digital-isolated.PendingFrames");
    }

    private static void D5(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1, 7);
        Capture(buffer, fixture, target, 0);
        Equal(buffer.PendingFrames, 2, "ABC.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "ABC-press",
            Row(target, z: 3, held: 7, pressed: 7, digital: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "ABC-release", Row(target, released: 7));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "ABC-held-drain");
        Equal(buffer.PendingFrames, 0, "ABC-drained.PendingFrames");
        Capture(buffer, fixture, target, 0, 1);
        Capture(buffer, fixture, target, 0);
        Capture(buffer, fixture, target, 0, command: new SourceCommand(SourceAction.Continue));
        Capture(buffer, fixture, target, 0, command: new SourceCommand(SourceAction.None, offenseFlipped: true));
        Capture(buffer, fixture, target, 0, command: new SourceCommand(SourceAction.None));
        Equal(buffer.PendingFrames, 4, "button-command.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "button-press", Row(target, held: 1, pressed: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "button-release", Row(target, released: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "actionable", Row(target, command: 1, action: 6));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "flip-only", Row(target, command: 1, flip: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "command-free-held");
        Equal(buffer.PendingFrames, 0, "commands-drained.PendingFrames");
    }

    private static void D6(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1, 7);
        Capture(buffer, fixture, target, 0);
        Capture(buffer, fixture, target, 1, 7);
        Equal(buffer.PendingFrames, 3, "before-clear.PendingFrames");
        buffer.Clear(true);
        Equal(buffer.WaitingForNeutral, true, "clear-true.WaitingForNeutral");
        Equal(buffer.PendingFrames, 0, "clear-true.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.ConfigurationIds, "clear-true-neutral");
        Capture(buffer, fixture, target, 1, 7);
        Equal(buffer.WaitingForNeutral, true, "blocked-ABC.WaitingForNeutral");
        Equal(buffer.PendingFrames, 0, "blocked-ABC.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.ConfigurationIds, "blocked-ABC-neutral");
        Capture(buffer, fixture, target, 1);
        Equal(buffer.WaitingForNeutral, true, "blocked-Up.WaitingForNeutral");
        Equal(buffer.PendingFrames, 0, "blocked-Up.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.ConfigurationIds, "no-fake-release");
        Capture(buffer, fixture, target, 0);
        Equal(buffer.WaitingForNeutral, false, "neutral-rearm.WaitingForNeutral");
        Equal(buffer.PendingFrames, 0, "neutral-rearm.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.ConfigurationIds, "neutral-rearm");
        Capture(buffer, fixture, target, 1, 7);
        Equal(buffer.PendingFrames, 1, "fresh.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "fresh-ABC",
            Row(target, z: 3, held: 7, pressed: 7, digital: 1));
        Equal(buffer.PendingFrames, 0, "fresh-drained.PendingFrames");
        Capture(buffer, fixture, target, 0);
        Capture(buffer, fixture, target, 1, 7);
        Equal(buffer.PendingFrames, 2, "before-clear-false.PendingFrames");
        buffer.Clear(false);
        Equal(buffer.WaitingForNeutral, false, "clear-false.WaitingForNeutral");
        Equal(buffer.PendingFrames, 0, "clear-false.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.ConfigurationIds, "clear-false-neutral");
        Capture(buffer, fixture, target, 1, 7);
        Equal(buffer.PendingFrames, 1, "immediate-fresh.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "immediate-fresh",
            Row(target, z: 3, held: 7, pressed: 7, digital: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "immediate-held",
            Row(target, z: 3, held: 7, digital: 1));
        Equal(buffer.PendingFrames, 0, "immediate-held.PendingFrames");
        buffer.Clear(false);
        Equal(buffer.PendingFrames, 0, "digital-clear-false.PendingFrames");
        Equal(buffer.WaitingForNeutral, false, "digital-clear-false.WaitingForNeutral");
        Capture(buffer, fixture, target, 1);
        Equal(buffer.PendingFrames, mode == 0 ? 1 : 0, "digital-immediate-fresh.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "digital-immediate-fresh", Row(target, z: 3, digital: 1));
        Equal(buffer.PendingFrames, 0, "digital-immediate-drained.PendingFrames");
        Capture(buffer, fixture, target, 3);
        Equal(buffer.PendingFrames, 0, "digital-latest49.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "digital-latest49", Row(target, z: 2, digital: 1));
    }

    private static void D7(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1);
        Capture(buffer, fixture, target, 0);
        Equal(buffer.PendingFrames, 2, "payload.PendingFrames");
        SourceInputFrame up = buffer.NextHostFrame(), neutral = buffer.NextHostFrame();
        Frame(up, fixture.SampleIds, "actual-up", Row(target, z: 3, digital: 1));
        Frame(neutral, fixture.SampleIds, "actual-neutral");
        // Each row is an independent synthetic SOURCE-scan input, not a history update or host cadence.
        SourceInputFrame[] payloads = { up, up, up, neutral, up, neutral, neutral, up };
        long[] ticks = { 401L, 402L, 403L, 404L, 405L, 406L, 407L, 408L };
        int[] current = { 1, 1, 1, 0, 1, 0, 0, 1 };
        int[] prior1 = { 0, 1, 1, 1, 0, 1, 0, 0 };
        int[] prior2 = { 0, 0, 1, 1, 1, 0, 1, 0 };
        int[] close = { 1, 0, 0, 0, 0, 0, 0, 1 };
        for (int i = 0; i < ticks.Length; i++)
        {
            SourceStationInput input = At(payloads[i], target);
            string field = "synthetic-row[" + i + "]";
            Equal(input.DigitalCurrent.HasValue, true, field + ".PayloadSpecified");
            Equal((int)input.DigitalCurrent.Value, current[i], field + ".PayloadCurrent");
            var scan = new SourceDigitalSwitchScan(input.StationId, ticks[i], input.DigitalCurrent.Value,
                (SourceDigitalDirections)prior1[i], (SourceDigitalDirections)prior2[i]);
            Equal(scan.IsSpecified, true, field + ".IsSpecified");
            Equal(scan.StationId, target, field + ".StationId");
            Equal(scan.SourceTick, ticks[i], field + ".SourceTick");
            Equal((int)scan.Current, current[i], field + ".Current");
            Equal((int)scan.Prior1, prior1[i], field + ".Prior1");
            Equal((int)scan.Prior2, prior2[i], field + ".Prior2");
            Equal((int)scan.Close, close[i], field + ".Close");
        }
    }

    private static void D8(Fixture fixture, int target, int mode)
    {
        SourceCabinetInputBuffer buffer = Buffer(fixture, mode);
        Capture(buffer, fixture, target, 1, 1);
        Capture(buffer, fixture, target, 2);
        Capture(buffer, fixture, target, 1, 4, new SourceCommand(SourceAction.Continue));
        Capture(buffer, fixture, target, 3, 4);
        Equal(buffer.PendingFrames, 3, "mixed.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "mixed-first",
            Row(target, z: 3, held: 1, pressed: 1, digital: 1));
        Equal(buffer.PendingFrames, 2, "mixed-after-first.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "mixed-second",
            Row(target, z: -3, released: 1, digital: 2));
        Equal(buffer.PendingFrames, 1, "mixed-after-second.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "mixed-third",
            Row(target, z: 3, held: 4, pressed: 4, digital: 1, command: 1, action: 6));
        Equal(buffer.PendingFrames, 0, "mixed-drained.PendingFrames");
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "latest-held", Row(target, z: 2, held: 4, digital: 1));
        Frame(buffer.NextHostFrame(), fixture.SampleIds, "latest-held-repeat", Row(target, z: 2, held: 4, digital: 1));
        Equal(buffer.PendingFrames, 0, "latest-held.PendingFrames");
        buffer = Buffer(fixture, mode);
        for (int i = 0; i < 8192; i++) Capture(buffer, fixture, target, 0, (i & 1) == 0 ? 1 : 0);
        Equal(buffer.PendingFrames, 8192, "limit.PendingFrames");
        Reject<InvalidOperationException>(() => Capture(buffer, fixture, target, 0, 1), "overflow.Rejected");
        Equal(buffer.PendingFrames, 8192, "after-overflow.PendingFrames");
        for (int i = 0; i < 8192; i++)
        {
            SourceInputFrame frame = buffer.NextHostFrame();
            SourceStationInput input = At(frame, target);
            int held = (i & 1) == 0 ? 1 : 0;
            int released = (i & 1) == 0 ? 0 : 1;
            Equal((int)input.Held, held, "limit[" + i + "].Held");
            Equal((int)input.Pressed, held, "limit[" + i + "].Pressed");
            Equal((int)input.Released, released, "limit[" + i + "].Released");
            if (i == 0) Frame(frame, fixture.SampleIds, "limit-first", Row(target, held: 1, pressed: 1));
            if (i == 8191) Frame(frame, fixture.SampleIds, "limit-last", Row(target, released: 1));
        }
        Equal(buffer.PendingFrames, 0, "limit-drained.PendingFrames");
        buffer.Clear(false);
        Equal(buffer.PendingFrames, 0, "limit-cleared.PendingFrames");
        Equal(buffer.WaitingForNeutral, false, "limit-cleared.WaitingForNeutral");
        Frame(buffer.NextHostFrame(), fixture.ConfigurationIds, "limit-cleared-neutral");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute()
    {
        tables = Tables(); quantizer = new SourceCabinetQuantizer(tables);
        fixtures = new[] { new Fixture(false), new Fixture(true) };
        Group("fixture", FixtureFields);
        Group("D1", () => EachTarget(0, D1));
        Group("D2", () => { EachTarget(1, D2); EachTarget(2, D2); });
        Group("D3", () => EachTarget(0, D3));
        Group("D4", () => {
            for (int mode = 0; mode < 3; mode++)
                foreach (Fixture fixture in fixtures) D4(fixture, mode);
        });
        Group("D5", () => { for (int mode = 0; mode < 3; mode++) EachTarget(mode, D5); });
        Group("D6", () => { for (int mode = 0; mode < 3; mode++) EachTarget(mode, D6); });
        Group("D7", () => EachTarget(0, D7));
        Group("D8", () => { for (int mode = 0; mode < 3; mode++) EachTarget(mode, D8); });
        if (assertions != 311744) throw new FieldFailure("Plan.TotalAssertions", 311744, assertions);
    }

    private static void InstallResolver(string[] args)
    {
#if DIGITAL_UNITY
        if (args.Length != 1 || !Path.IsPathRooted(args[0]))
            throw new ArgumentException("An explicit absolute genuine Unity metadata root is required.");
        string directory = args[0];
        AssemblyLoadContext.Default.Resolving += (loadContext, name) =>
        {
            if (name.Name != "UnityEngine.CoreModule" && name.Name != "UnityEngine.IMGUIModule" &&
                name.Name != "UnityEngine.InputLegacyModule") return null;
            return loadContext.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll"));
        };
#else
        if (args.Length != 0) throw new ArgumentException("Pure synthetic checks take no external inputs.");
#endif
    }

    private static object[] LoadedUnity()
    {
        var loaded = new List<object>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = assembly.GetName().Name;
            if (name == "UnityEngine.CoreModule" || name == "UnityEngine.IMGUIModule" ||
                name == "UnityEngine.InputLegacyModule")
                loaded.Add(new { name = name, identity = assembly.FullName, path = assembly.Location });
        }
        return loaded.ToArray();
    }

    public static int Main(string[] args)
    {
        string partition = "pure";
        string variant = "none";
#if DIGITAL_UNITY
        partition = "actual-constructor";
#if UNITY_EDITOR
        variant = "UNITY_EDITOR";
#else
        variant = "UNITY_WEBGL";
#endif
#endif
        try
        {
            InstallResolver(args);
            Execute();
            Console.WriteLine(JsonSerializer.Serialize(new {
                schema = "source-digital-preservation-checks/v1", status = "passed",
                partition = partition, variant = variant, assertions = assertions, expected_assertions = 311744,
                expected_groups = ExpectedCounts, groups = Counts, completed_groups = Completed,
                unity_loaded = LoadedUnity(), actual_game_simulation_history_claimed = false,
                source_decode_scheduler_native_or_physical_sampling_claimed = false
            }));
            return 0;
        }
        catch (Exception error)
        {
            FieldFailure field = error as FieldFailure;
            Console.WriteLine(JsonSerializer.Serialize(new {
                schema = "source-digital-preservation-checks/v1", status = field == null ? "error" : "failed",
                partition = partition, variant = variant, assertions = assertions, expected_assertions = 311744,
                expected_groups = ExpectedCounts, groups = Counts, completed_groups = Completed,
                failed_field = field == null ? null : field.Field,
                expected = field == null ? null : field.Expected, actual = field == null ? null : field.Actual,
                exception_type = error.GetType().FullName, error = error.ToString(),
                unity_loaded = LoadedUnity(), actual_game_simulation_history_claimed = false,
                source_decode_scheduler_native_or_physical_sampling_claimed = false
            }));
            return field == null ? 2 : 1;
        }
    }
}
