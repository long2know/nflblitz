using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static NativeReference reference;
    private static int meterFlows;
    private static int referenceCalls;
    private static int physicsCases;
    private static bool sawPuntOvershoot, sawPunt25, sawPunt90, sawGoalClamp, sawGoal100;
    private static readonly int[] Raw99 = Enumerable.Repeat(99, 16).ToArray();

    private sealed class NativeReference : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder transcript = new StringBuilder();
        private readonly string path;
        internal NativeReference(string binary, string transcriptPath)
        {
            path = transcriptPath;
            process = Process.Start(new ProcessStartInfo(binary) {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        internal string Ask(params object[] values)
        {
            string command = string.Join(" ", values.Select(value => value is float
                ? ((float)value).ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture)));
            transcript.AppendLine("> " + command);
            process.StandardInput.WriteLine(command); process.StandardInput.Flush();
            string line = process.StandardOutput.ReadLine();
            if (line == null) throw new InvalidOperationException("Original C exited before its response: " + process.StandardError.ReadToEnd());
            transcript.AppendLine(line); referenceCalls++;
            return line;
        }
        internal float[] Row(params object[] values)
        {
            return Ask(values).Split(' ').Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        }
        public void Dispose()
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(10000)) { process.Kill(); throw new InvalidOperationException("Original C did not exit."); }
            string stderr = process.StandardError.ReadToEnd();
            File.WriteAllText(path, transcript.ToString());
            Check(process.ExitCode == 0 && stderr.Length == 0, "actual original C exits zero with empty stderr");
            process.Dispose();
        }
    }

    private static void Same(float actual, float expected, string name)
    {
        Check(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(expected),
            name + ": actual=" + actual.ToString("R", CultureInfo.InvariantCulture)
                + " original-C=" + expected.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void CompareMeter(SourceKickMeterView view, float[] row, long created, string label)
    {
        Check(row.Length == 16, "original meter result has the exact16 observed fields");
        int[] actual = { view.PositionFixed10000, view.VelocityFixed10000, view.LimitFixed10000,
            view.SpriteOffsetPixels, view.MarkerOffsetPixels, view.MarkerLeftFixed10000, view.MarkerRightFixed10000,
            view.Highlighted ? 1 : 0 };
        for (int i = 0; i < actual.Length; i++)
            Check(actual[i] == (int)row[i], label + ": original-C meter field[" + i + "]");
        Same(view.Wind, row[8], label + ": original-C wind");
        Same(view.AccuracyPercent, row[9], label + ": original-C accuracy");
        Check(view.PuntStrength == (int)row[10], label + ": original-C punt power");
        Check(view.ShowMeterFlag == (row[11] != 0), label + ": original-C showkmtr lifecycle");
        Check(view.NextWakeSourceTick == (row[13] < 0 ? -1 : created + (long)row[13]),
            label + ": original-C continuation sleep/wake tick");
    }

    private static void MeterFlow(SourceKickMeterKind kind, TeamId side, int attribute, int windRaw,
        int windSign, int stop, bool timeout, bool fake)
    {
        int rawStrength = (stop & 1) == 0 ? 99 : 0;
        Check(reference.Ask("M", (int)kind, (int)side, attribute, fake ? 1 : 0,
            windRaw, windSign, rawStrength, 99, 99) == "READY", "start actual original C meter continuation");
        float wind = windRaw * .1f;
        if (wind != 0f && windSign == 0) wind = -wind;
        if (kind == SourceKickMeterKind.Punt) wind = 0f;
        var process = new SourceKickMeterProcess(metrics, kind, side, (int)side, attribute, fake, 0,
            wind, 50 + rawStrength % 50);
        int autoDraws = 0;
        Func<int, int> draw = bound => { autoDraws++; return 99 % bound; };
        string label = kind + "/side" + side + "/attr" + attribute + "/stop" + stop + "/auto" + timeout + "/fake" + fake;
        for (int tick = 1; tick <= stop; tick++)
        {
            process.Wake(tick, true, false, 285 - tick, false, draw);
            float[] original = reference.Row("T", tick, 1, 0, 285 - tick);
            CompareMeter(process.View, original, 0, label);
            Check(process.View.Owner == side && process.View.SpecialTeamsAttribute == attribute,
                "original process owner and actual TA_SPTEAMS index are retained");
            if (kind == SourceKickMeterKind.Punt && Math.Abs(process.View.PositionFixed10000) > process.View.LimitFixed10000)
                sawPuntOvershoot = true;
            if (kind == SourceKickMeterKind.FieldGoal && Math.Abs(process.View.PositionFixed10000) == process.View.LimitFixed10000)
                sawGoalClamp = true;
        }
        int counter = timeout ? 0 : 200;
        bool committed = process.Wake(stop + 1, true, true, counter, false, draw);
        float[] locked = reference.Row("T", stop + 1, 1, (int)SourceGameFlags.HikingBall, counter);
        CompareMeter(process.View, locked, 0, label);
        Check(committed == !fake, "fake meter exits without committing a genuine kick result");
        if (!fake && !timeout && kind == SourceKickMeterKind.Punt)
        {
            sawPunt25 |= process.View.PuntStrength == 25;
            sawPunt90 |= process.View.PuntStrength == 90;
        }
        sawGoal100 |= kind == SourceKickMeterKind.FieldGoal && Math.Abs(process.View.AccuracyPercent) >= 100f;
        if (timeout && !fake)
        {
            for (int tick = stop + 2; tick <= stop + 4; tick++)
            {
                process.Wake(tick, false, false, counter, false, draw);
                CompareMeter(process.View, reference.Row("T", tick, 2, 0, counter), 0, label);
            }
            process.Wake(stop + 5, false, false, counter, true, draw);
            CompareMeter(process.View, reference.Row("T", stop + 5, 3, 0, counter), 0, label);
        }
        else
        {
            for (int tick = stop + 2; tick <= stop + 31; tick++)
            {
                process.Wake(tick, false, false, counter, false, draw);
                CompareMeter(process.View, reference.Row("T", tick, 2, 0, counter), 0, label);
            }
        }
        Check(process.View.Status == SourceKickMeterStatus.Complete, "real original exit clears the meter after its exact lifetime");
        int initialDraws = kind == SourceKickMeterKind.Punt || windRaw == 0 ? 1 : 2;
        Check((int)locked[14] == initialDraws + autoDraws, "original init/timeout random bounds and call count");
        meterFlows++;
    }

    private static void MeterReferenceChecks()
    {
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
        for (int attribute = 0; attribute < 6; attribute++)
        foreach (SourceKickMeterKind kind in new[] { SourceKickMeterKind.FieldGoal, SourceKickMeterKind.Punt })
        {
            int[,] winds = kind == SourceKickMeterKind.FieldGoal
                ? new[,] { { 0, 0 }, { 7, 0 }, { 7, 1 }, { 10, 0 }, { 10, 1 } } : new[,] { { 0, 0 } };
            for (int w = 0; w < winds.GetLength(0); w++)
            {
                foreach (int stop in new[] { 10, 11, 12, 20, 24, 26, 40 })
                    MeterFlow(kind, side, attribute, winds[w, 0], winds[w, 1], stop, false, false);
                MeterFlow(kind, side, attribute, winds[w, 0], winds[w, 1], 11, true, false);
                MeterFlow(kind, side, attribute, winds[w, 0], winds[w, 1], 12, false, true);
            }
        }
        Check(meterFlows == 648, "exact648 original-C meter flows, no inventory rebase");
        Check(sawPuntOvershoot && sawPunt25 && sawPunt90 && sawGoalClamp && sawGoal100,
            "original-C cases actually cover punt overshoot/min25/max90 and FG clamp/over100 accuracy");
    }

    private static float[] PuntReference(TeamId side, int los, int strength, bool flip, SourceVector3 cameraJoint,
        SourceMovementCameraFrame camera, int[] tape)
    {
        var command = new List<object> { "P", (int)side, los, strength, flip ? 1 : 0,
            cameraJoint.X, cameraJoint.Y, cameraJoint.Z };
        command.AddRange(camera.Transform.Select(value => (object)value));
        command.Add(camera.Position.X); command.Add(camera.Position.Y); command.Add(camera.Position.Z);
        command.AddRange(tape.Select(value => (object)value));
        return reference.Row(command.ToArray());
    }

    private static float[] GoalReference(TeamId side, int los, int counter, float percent, SourceVector3 ball,
        SourceVector3 target, int handicap, int quarter, int packed, int ownScore, int otherScore,
        bool fire, bool perfect, bool noAssist, bool dronesLose, bool human, int[] tape)
    {
        var command = new List<object> { "F", (int)side, los, counter, percent, ball.X, ball.Y, ball.Z,
            target.X, target.Z, handicap, quarter - 1, packed, ownScore, otherScore,
            fire ? 1 : 0, perfect ? 1 : 0, noAssist ? 1 : 0, dronesLose ? 1 : 0, human ? 1 : 0 };
        command.AddRange(tape.Select(value => (object)value));
        return reference.Row(command.ToArray());
    }

    private static void CompareBall(SourceVector3 point, SourceVector3 velocity, SourceVector3 target, int total, float[] row, string label)
    {
        float[] actual = { point.X, point.Y, point.Z, velocity.X, velocity.Y, velocity.Z, target.X, target.Z };
        for (int i = 0; i < actual.Length; i++) Same(actual[i], row[i], label + ": original-C launch field[" + i + "]");
        Check(total == (int)row[8], label + ": original-C flight total");
        Check(row[9] == 0f, label + ": original-C launch flight elapsed starts zero");
    }

    private static void PhysicsReferenceChecks(FullKernelInputs inputs)
    {
        int test = 0;
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
        foreach (int los in new[] { 0, 1, 30, 89, 99, 100 })
        foreach (int strength in new[] { 25, 26, 50, 89, 90, 99 })
        foreach (bool flip in new[] { false, true })
        foreach (int raw in new[] { 0, 100, 200 })
        {
            var identity = new SourceMovementCameraFrame(new SourceVector3(0, 0, 0),
                new[] { 1f, 0, 0, 0, 0, 1f, 0, 0, 0, 0, 1f, 0 }, 512, 384);
            var ankle = new SourceVector3(side == TeamId.Home ? -239.125f : 239.125f, 11.375f, -13.75f);
            int[] tape = Enumerable.Repeat(raw, 16).ToArray();
            float[] original = PuntReference(side, los, strength, flip, ankle, identity, tape);
            var model = new SimulationState { Possession = side, SourcePuntStrength = strength,
                SourceRules = new SourceRulesState((int)side, (int)side, 4, los, 100, SourcePlayType.Punt) };
            var actor = new ActorState((int)side * 7 + 3) { Flipped = flip };
            int draws = 0;
            SourceBallDriver.Punt(model, actor, ankle, bound => tape[draws++] % bound);
            CompareBall(model.SourceBallPosition, new SourceVector3(model.SourceBallVelocity.X,
                model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z), model.SourceBallTarget, model.PassTicksTotal,
                original, "punt-math");
            Check(draws == original[15] && original[16] == 201, "original punt consumes exactly one ordered201 draw");
            Check(model.SourceBallPitchRate == (int)original[10], "original punt pitch-45");
            physicsCases++;
        }
        test = 0;
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
        foreach (int los in new[] { 5, 50, 89, 95 })
        foreach (float percent in new[] { -153f, -100f, -1f, 0f, 1f, 100f, 153f })
        foreach (bool human in new[] { false, true })
        foreach (int counter in new[] { 0, 200 })
        {
            int index = test++, handicap = new[] { 0, 5, 11 }[index % 3], quarter = index % 2 == 0 ? 1 : 4;
            bool fire = index % 7 == 0, perfect = index % 11 == 0, noAssist = index % 3 == 0, lose = index % 5 == 0;
            int own = new[] { 0, 0, 3, 7 }[index % 4], other = new[] { 0, 3, 4, 0 }[index % 4];
            var point = new SourceVector3(SourceMatchRules.WorldPosition(los, (int)side)
                + (side == TeamId.Home ? -18f : 18f), 5.125f, -12.75f);
            var target = new SourceVector3(123.75f, 0, -19.125f);
            int[] tape = index % 2 == 0 ? Raw99 : new[] { 0, 72, 0, 1, 39, 85, 7, 3, 21, 52, 0, 1, 2, 3, 4, 5 };
            float[] original = GoalReference(side, los, counter, percent, point, target, handicap, quarter,
                0x1d00, own, other, fire, perfect, noAssist, lose, human, tape);
            SourceMatchConfiguration config = human ? Configuration(side) : new SourceMatchConfiguration(
                SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
                new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceStationConfiguration[0], side, SourceCaptainSlot.First);
            var model = new SimulationState { Possession = side, SourceConfiguration = config,
                SourcePlaybook = inputs.Playbook, SourceRules = new SourceRulesState((int)side, (int)side, 4, los, 100, SourcePlayType.FieldGoal),
                SourceHikeCounterAtSnap = counter, SourceKickMeterPercent = percent, SourceBallPosition = point,
                SourceBallTarget = target, Quarter = quarter, SourceClock = SourceClock.Create(0x1d00),
                SourceNoAssist = noAssist, SourcePerfectFieldGoal = perfect, SourceDronesLose = lose };
            model.SourceTeamFire[(int)side] = fire;
            if (side == TeamId.Home) { model.HomeScore = own; model.AwayScore = other; }
            else { model.AwayScore = own; model.HomeScore = other; }
            var actor = new ActorState((int)side * 7 + 6) { SourceHandicap = handicap };
            var bounds = new List<int>();
            SourceBallDriver.FieldGoal(model, actor, bound => { int value = tape[bounds.Count]; bounds.Add(bound); return value % bound; });
            CompareBall(model.SourceBallPosition, new SourceVector3(model.SourceBallVelocity.X,
                model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z), model.SourceBallTarget,
                model.PassTicksTotal, original, "FG-math");
            Check(bounds.Count == original[15] && bounds.SequenceEqual(original.Skip(16).Select(value => (int)value)),
                "original FG ordered random bounds and call count");
            physicsCases++;
        }
        Check(physicsCases == 656, "exact656 original-C velocity cases include432 punt and224 FG");
    }
}
