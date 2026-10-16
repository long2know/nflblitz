using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class Program
{
    private static int assertions;
    private static int executed;
    private static string currentCase;
    private static string currentField;
    private static JsonElement data;
    private static SourceDirectionMath directions;
    private static SourceMovementData movementData;
    private static readonly Dictionary<string, FieldInfo> fields = StateFields();
    private const string Profile = "OriginalRelease57Hz";

    private static Dictionary<string, FieldInfo> StateFields()
    {
        var result = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        foreach (FieldInfo field in typeof(SourceMovementState).GetFields(BindingFlags.Instance | BindingFlags.Public))
            result.Add(field.Name, field);
        return result;
    }

    private static int Main(string[] args)
    {
        try
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(args[0])))
            {
                JsonElement root = document.RootElement;
                data = root.GetProperty("Data");
                var constants = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (JsonProperty item in data.GetProperty("Constants").EnumerateObject())
                    constants.Add(item.Name, Float(item.Value));
                var tables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (JsonProperty item in data.GetProperty("Tables").EnumerateObject())
                {
                    var values = new List<string>();
                    foreach (JsonElement value in item.Value.EnumerateArray()) values.Add(value.GetString());
                    tables.Add(item.Name, values.AsReadOnly());
                }
                var sines = new List<float>();
                foreach (JsonElement value in data.GetProperty("Trigs").EnumerateArray()) sines.Add(Float(value));
                var angles = new List<int>();
                foreach (JsonElement value in data.GetProperty("Angles49").EnumerateArray()) angles.Add(value.GetInt32());
                directions = new SourceDirectionMath(sines, angles,
                    root.GetProperty("Source").GetProperty("blobs").GetProperty("lib/ani3d/TRIGS.H").GetString(),
                    root.GetProperty("Source").GetProperty("blobs").GetProperty("player.c").GetString());
                movementData = new SourceMovementData(constants, tables);
                foreach (JsonElement row in root.GetProperty("Cases").EnumerateArray()) Run(row);
                StrictBoundaryChecks(root.GetProperty("Cases")[0]);
                Console.WriteLine("{\"schema\":\"source-movement-checks/v1\",\"status\":\"passed\",\"cases\":" +
                    executed + ",\"assertions\":" + assertions + ",\"full_gameplay_parity_claimed\":false}");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine("{\"schema\":\"source-movement-checks/v1\",\"status\":\"failed\",\"cases\":" + executed +
                ",\"assertions\":" + assertions + ",\"case\":" + JsonSerializer.Serialize(currentCase) +
                ",\"field\":" + JsonSerializer.Serialize(currentField) + ",\"full_gameplay_parity_claimed\":false}");
            return 1;
        }
    }

    private static float Float(JsonElement element)
    {
        return BitConverter.ToSingle(BitConverter.GetBytes(element.GetUInt32()), 0);
    }

    private static uint Bits(float value)
    {
        return BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
    }

    private static int I(JsonElement value, string name) { return value.GetProperty(name).GetInt32(); }
    private static float F(JsonElement value, string name) { return value.GetProperty(name).GetSingle(); }
    private static bool B(JsonElement value, string name) { return I(value, name) != 0; }

    private static SourceMovementState State(JsonElement source)
    {
        object boxed = new SourceMovementState(I(source, "PlayerId"), I(source, "Station"), (SourceActorMode)I(source, "Mode"));
        foreach (JsonProperty item in source.EnumerateObject())
        {
            FieldInfo field = fields[item.Name];
            object value = field.FieldType == typeof(string) ? (object)item.Value.GetString()
                : field.FieldType == typeof(float) ? item.Value.GetSingle()
                : field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, item.Value.GetInt32())
                : (object)item.Value.GetInt32();
            field.SetValue(boxed, value);
        }
        return (SourceMovementState)boxed;
    }

    private static SourceMovementContext Context(JsonElement source, SourceMovementState state)
    {
        var neighbors = new List<SourceMovementNeighbor>();
        JsonElement distances = source.GetProperty("Distances"), bearings = source.GetProperty("Bearings");
        for (int i = 0; i < 14; i++)
        {
            float distance = i == I(source, "Carrier") && i != state.PlayerId ? F(source, "CarrierDistance") : distances[i].GetSingle();
            neighbors.Add(new SourceMovementNeighbor(i, distance, bearings[i].GetInt32()));
        }
        int handicap = I(source, "Handicap");
        return new SourceMovementContext(new SourceMovementTick(source.GetProperty("SourceTick").GetInt64(), B(source, "Execute")),
            directions, I(source, "OffenseSide"), (SourceMovementPhase)I(source, "Phase"), (SourceGameFlags)I(source, "GameFlags"),
            (SourcePlayType)I(source, "PlayType"), (SourcePlayFlags)I(source, "OffensePlayFlags"), (SourcePlayFlags)I(source, "DefensePlayFlags"),
            I(source, "Carrier"), (SourceActorMode)I(source, "CarrierMode"), (SourceBallType)I(source, "BallType"),
            I(source, "IntendedReceiver"), I(source, "BallTicksLeft"), F(source, "BallX"), F(source, "BallY"), F(source, "BallDistance"),
            I(source, "CameraHeading"), I(source, "LineOfScrimmage"), F(source, "LineZ"), F(source, "MotionSnapX"),
            I(source, "BackpedalTargetHeading"), F(source, "TeamIndexedTurbo"), B(source, "DefenseFire"), B(source, "SpeedPowerup"),
            B(source, "FastTurboPowerup"), B(source, "RunOutOfBoundsPowerup"), B(source, "ViolencePowerup"),
            B(source, "CarrierLateHittable"), F(source, "CarrierDistance"), F(source, "BallTargetX"), source.GetProperty("DroneScript").GetString(),
            B(source, "DroneScriptFullBody"), neighbors, I(data.GetProperty("Defense")[handicap], "Plow"));
    }

    private static void Equal<T>(string name, T expected, T actual)
    {
        currentField = name;
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(currentCase + "." + name + ": expected " + expected + ", actual " + actual);
    }

    private static void Run(JsonElement row)
    {
        currentCase = row.GetProperty("Name").GetString();
        SourceMovementState state = State(row.GetProperty("State"));
        JsonElement c = row.GetProperty("Context"), expected = row.GetProperty("Expected");
        SourceMovementContext context = Context(c, state);
        var randomContext = new SourceRandomContext(context.Tick.SourceTick, state.PlayerId, "player_proc", state.PlayerId + 1, Profile);
        var provenance = new SourceRandomProvenance(Profile, "explicit original-C boundary draw tape", currentCase, SourceRandomEvidence.RecordedOracle);
        var tape = new List<SourceRandomDraw>();
        int order = 0;
        foreach (JsonElement draw in expected.GetProperty("Draws").EnumerateArray())
        {
            var site = randomContext.At(SourceRandomKind.RandRange, I(draw, "Bound"), draw.GetProperty("Identity").GetString(), order);
            tape.Add(new SourceRandomDraw(site, draw.GetProperty("Raw").ValueKind == JsonValueKind.Null ? (int?)null : I(draw, "Raw"), order, provenance));
            order++;
        }
        var random = new SourceRecordedRandom(tape, provenance);
        var boundary = new Boundary(I(c, "ChangeValue"));
        var commands = new List<SourceMovementCommand>();
        uint breath = c.GetProperty("BreathControl").GetUInt32();
        int used = 0;
        bool allowed = SourceMovement.ModeAllowsRun(state, context), human = SourceMovement.HumanControls(state, context);
        float turbo = F(c, "StationTurbo");
        IReadOnlyList<SourceMovementDefenseSpeed> defense = new List<SourceMovementDefenseSpeed>().AsReadOnly();
        int leapVictim = 0, leapTicks = 0, leapBytes = 0;
        bool returnFromMode = false;
        string operation = row.GetProperty("Operation").GetString();
        Action<SourceMovementResult> apply = result =>
        {
            state = result.State;
            commands.AddRange(result.Commands);
            used += result.RandomCalls;
            if (result.BreathControl.HasValue) breath = result.BreathControl.Value;
        };
        Func<SourceRandomContext> next = () => new SourceRandomContext(context.Tick.SourceTick, state.PlayerId,
            "player_proc", state.PlayerId + 1, Profile, used);
        Func<SourceMovementSwitches> switches = () => new SourceMovementSwitches(I(c, "InputStick"),
            (SourceDroneButtons)I(c, "InputButtons"), B(c, "InputBackpedal"), F(c, "InputTurbo"));
        Action<SourceEvasionResult> evade = result => { apply(result.Movement); returnFromMode |= result.ReturnFromMode; };
        switch (operation)
        {
            case "input": state = SourceMovement.BeginTick(state, switches(), context); break;
            case "turn": state = SourceMovement.Turn(state, context.Tick); break;
            case "standard": apply(SourceMovement.StandardRun(state, context, movementData, boundary, random, next(), breath)); break;
            case "quarterback": apply(SourceMovement.QuarterbackRun(state, context, movementData, boundary, random, next())); break;
            case "general": apply(SourceMovement.GeneralRun(state, context, movementData, boundary, random, next(), breath)); break;
            case "physics": apply(SourceMovement.Integrate(state, context, Float(data.GetProperty("Friction")[I(c, "Field")]), boundary, next())); break;
            case "maybe_dive": apply(SourceEvasion.MaybeDive(state, context, boundary, random, next())); break;
            case "slower": allowed = context.Tick.Execute && SourceEvasion.SlowerSpin(state.TeamSpinCount); break;
            case "before": evade(SourceEvasion.WeaponBeforeRun(state, context, movementData, boundary, random, next())); break;
            case "weapon": evade(SourceEvasion.WeaponAfterRun(state, context, movementData, boundary, random, next(),
                B(c, "EnteredControlBlock"))); break;
            case "qb_action": evade(SourceEvasion.QuarterbackAfterRun(state, context, boundary, random, next(),
                B(c, "EnteredControlBlock"))); break;
            case "db_jump": apply(SourceEvasion.DefensiveBackJump(state, context, movementData, boundary, random, next(), B(c, "EnteredControlBlock"))); break;
            case "station": turbo = SourceMovement.StationTurbo(context.Tick, turbo, B(c, "StationActive"), B(c, "StationControlled"), context.Phase,
                B(c, "StationFire"), B(c, "StationInfinite"), B(c, "StationHeld"), B(c, "StationDefends")); break;
            case "defense":
                var modes = new List<SourceActorMode>();
                foreach (JsonElement mode in c.GetProperty("DefenderModes").EnumerateArray()) modes.Add((SourceActorMode)mode.GetInt32());
                var line = new List<float>(); var back = new List<float>();
                JsonElement h = data.GetProperty("Defense")[I(c, "Handicap")];
                foreach (JsonElement speed in h.GetProperty("Line").EnumerateArray()) line.Add(Float(speed));
                foreach (JsonElement speed in h.GetProperty("Back").EnumerateArray()) back.Add(Float(speed));
                defense = SourceMovement.AssignDefenseSpeeds(context.Tick, context.OffenseSide, modes, line, back,
                    I(h, "Base"), I(h, "Range"), (context.DefensePlayFlags & SourcePlayFlags.RedZone) != 0, random, next());
                used += defense.Count * 2;
                break;
            case "leap":
                JsonElement leap = row.GetProperty("Leap");
                var matrix = new List<float>();
                foreach (JsonElement entry in leap.GetProperty("Matrix").EnumerateArray()) matrix.Add(entry.GetSingle());
                if (context.Tick.Execute)
                    leapVictim = SourceEvasionLeap.ResolveVictim(I(leap, "VictimArgument"), context.Carrier,
                        I(leap, "ThrowTarget"), context.OffenseSide);
                var leapInput = new SourceEvasionLeapInput(
                    new SourceVector3(F(leap, "JointX"), F(leap, "JointY"), F(leap, "JointZ")),
                    new SourceVector3(F(leap, "TargetVelocityX"), F(leap, "TargetVelocityY"), F(leap, "TargetVelocityZ")),
                    matrix, new SourceVector3(F(leap, "CameraX"), F(leap, "CameraY"), F(leap, "CameraZ")),
                    new SourceVector3(F(leap, "OffsetX"), F(leap, "OffsetY"), F(leap, "OffsetZ")),
                    I(leap, "Time"), F(leap, "MaximumYVelocity"), F(leap, "MaximumPlanarVelocity"),
                    F(leap, "MinimumPlanarVelocity"), leapVictim == state.PlayerId);
                SourceEvasionLeapResult leapResult = SourceEvasionLeap.LeapAttack(state, context.Tick, directions, leapInput);
                state = leapResult.State; leapTicks = leapResult.FlightTicks; leapBytes = leapResult.ScriptBytesConsumed;
                Equal("Leap.Executed", context.Tick.Execute, leapResult.Executed);
                break;
            case "step":
                state = SourceMovement.BeginTick(state, switches(), context);
                if (context.Tick.Execute && (state.Mode == SourceActorMode.Receiver || state.Mode == SourceActorMode.DefensiveBack))
                { state.TurnRate = 64; state.UpperTurnRate = 48; }
                state = SourceMovement.Turn(state, context.Tick);
                if (state.Mode == SourceActorMode.Receiver)
                    evade(SourceEvasion.WeaponBeforeRun(state, context, movementData, boundary, random, next()));
                allowed = SourceMovement.ModeAllowsRun(state, context);
                if (allowed)
                {
                    if (state.Mode == SourceActorMode.Quarterback)
                    {
                        apply(SourceMovement.QuarterbackRun(state, context, movementData, boundary, random, next()));
                        evade(SourceEvasion.QuarterbackAfterRun(state, context, boundary, random, next(), true));
                    }
                    else
                    {
                        apply(SourceMovement.StandardRun(state, context, movementData, boundary, random, next(), breath));
                        if (state.Mode == SourceActorMode.Receiver)
                            evade(SourceEvasion.WeaponAfterRun(state, context, movementData, boundary, random, next(), true));
                        else if (state.Mode == SourceActorMode.DefensiveBack)
                            apply(SourceEvasion.DefensiveBackJump(state, context, movementData, boundary, random, next(), true));
                    }
                }
                apply(SourceMovement.Integrate(state, context, Float(data.GetProperty("Friction")[I(c, "Field")]), boundary, next()));
                break;
            case "allow": break;
            default: throw new InvalidOperationException("Unknown actual-C operation " + operation);
        }
        object actualState = state;
        foreach (JsonProperty item in expected.GetProperty("State").EnumerateObject())
        {
            FieldInfo field = fields[item.Name];
            object actual = field.GetValue(actualState);
            if (field.FieldType == typeof(float)) Equal("State." + item.Name, item.Value.GetUInt32(), Bits((float)actual));
            else if (field.FieldType == typeof(string)) Equal("State." + item.Name, item.Value.GetString(), (string)actual);
            else Equal("State." + item.Name, item.Value.GetInt32(), Convert.ToInt32(actual, CultureInfo.InvariantCulture));
        }
        Equal("Allowed", I(expected, "Allowed") != 0, allowed);
        Equal("Human", I(expected, "Human") != 0, human);
        Equal("Turbo", expected.GetProperty("Turbo").GetUInt32(), Bits(turbo));
        Equal("BreathControl", expected.GetProperty("BreathControl").GetUInt32(), breath);
        Equal("ReturnFromMode", I(expected, "ReturnFromMode") != 0, returnFromMode);
        JsonElement expectedCommands = expected.GetProperty("Commands");
        Equal("Commands.Count", expectedCommands.GetArrayLength(), commands.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            Equal("Commands[" + i + "].Kind", I(expectedCommands[i], "Kind"), (int)commands[i].Kind);
            Equal("Commands[" + i + "].Script", expectedCommands[i].GetProperty("Script").GetString(), commands[i].Script);
            Equal("Commands[" + i + "].TeamSpinCount-at-callback", I(expectedCommands[i], "TeamSpinCount"), boundary.Counts[i].Item1);
            Equal("Commands[" + i + "].TeamHurdleCount-at-callback", I(expectedCommands[i], "TeamHurdleCount"), boundary.Counts[i].Item2);
        }
        Equal("Random.Consumed", tape.Count, random.Consumed);
        Equal("Random.Count", tape.Count, used);
        Equal("Defense.Count", expected.GetProperty("Defense").GetArrayLength(), defense.Count);
        if (operation == "leap")
        {
            Equal("Leap.Victim", I(expected, "LeapVictim"), leapVictim);
            Equal("Leap.FlightTicks", I(expected, "LeapTicks"), leapTicks);
            Equal("Leap.ScriptBytesConsumed", I(expected, "LeapBytes"), leapBytes);
        }
        for (int i = 0; i < defense.Count; i++)
        {
            JsonElement wanted = expected.GetProperty("Defense")[i];
            Equal("Defense[" + i + "].PlayerId", I(wanted, "PlayerId"), defense[i].PlayerId);
            Equal("Defense[" + i + "].Speed", wanted.GetProperty("Speed").GetUInt32(), Bits(defense[i].Speed));
            Equal("Defense[" + i + "].OverrideTime", I(wanted, "OverrideTime"), defense[i].OverrideTime);
        }
        executed++;
    }

    private sealed class Boundary : ISourceMovementBoundary
    {
        private readonly int changeValue;
        internal readonly List<Tuple<int, int>> Counts = new List<Tuple<int, int>>();
        internal Boundary(int changeValue) { this.changeValue = changeValue; }
        public SourceMovementBoundaryResult Apply(SourceMovementState state, SourceMovementCommand command,
            SourceRandomContext context, ISourceRandom random)
        {
            Counts.Add(Tuple.Create(state.TeamSpinCount, state.TeamHurdleCount));
            if (command.Kind == SourceMovementCommandKind.BeginTwoPart)
            {
                state.UpperSequence = state.LowerSequence; state.UpperMode = state.LowerMode | SourceMovementModes.TwoPart;
                state.UpperHeading = state.Heading; state.UpperTargetHeading = state.TargetHeading;
            }
            else if (command.Kind == SourceMovementCommandKind.FullBody || command.Kind == SourceMovementCommandKind.LowerBody ||
                command.Kind == SourceMovementCommandKind.UpperBody)
            {
                if (command.Kind == SourceMovementCommandKind.FullBody) state.UpperMode &= ~(SourceMovementModes)0xffff;
                bool upper = command.Kind == SourceMovementCommandKind.UpperBody;
                SourceMovementModes mode = (upper ? state.UpperMode : state.LowerMode) & ~(SourceMovementModes)0xffff;
                mode &= ~SourceMovementModes.Interpolating;
                JsonElement response;
                if (data.GetProperty("Responses").TryGetProperty(command.Script, out response))
                {
                    int assigned = I(response, "Mode");
                    if (assigned >= 0) mode = (mode & ~(SourceMovementModes)0xffff) | (SourceMovementModes)assigned;
                    string sequence = response.GetProperty("Sequence").GetString();
                    if (sequence != "") { if (upper) state.UpperSequence = sequence; else state.LowerSequence = sequence; }
                    if (B(response, "Zero")) state.VelocityX = state.VelocityY = state.VelocityZ = 0f;
                    if (B(response, "HasVelocity"))
                    {
                        JsonElement velocity = response.GetProperty("Velocity");
                        float x = Float(velocity[0]), z = Float(velocity[2]);
                        state.VelocityX = z * directions.Sin(state.Heading) + x * directions.Cos(state.Heading);
                        state.VelocityY = Float(velocity[1]);
                        state.VelocityZ = z * directions.Cos(state.Heading) - x * directions.Sin(state.Heading);
                    }
                }
                if (upper) state.UpperMode = mode; else state.LowerMode = mode;
                state.Flags |= SourceMovementFlags.AnimationTick;
            }
            return new SourceMovementBoundaryResult(state, 0,
                command.Kind == SourceMovementCommandKind.ChangeWeaponPlayer ? changeValue : 0);
        }
    }

    private static void MustReject<T>(string name, Action action) where T : Exception
    {
        bool rejected = false;
        try { action(); }
        catch (T) { rejected = true; }
        Equal(name, true, rejected);
    }

    private sealed class InvalidBoundary : ISourceMovementBoundary
    {
        private readonly int behavior;
        internal InvalidBoundary(int behavior) { this.behavior = behavior; }
        public SourceMovementBoundaryResult Apply(SourceMovementState state, SourceMovementCommand command,
            SourceRandomContext context, ISourceRandom random)
        {
            if (behavior == 1) state.PlayerId = (state.PlayerId + 1) % 14;
            return new SourceMovementBoundaryResult(state, behavior == 2 ? 1 : 0);
        }
    }

    private sealed class WrongSiteRandom : ISourceRandom
    {
        public SourceRandomProvenance Provenance { get; } = new SourceRandomProvenance(
            Profile, "deliberately invalid recorded-site fixture", "no PRNG", SourceRandomEvidence.RecordedOracle);
        public SourceRandomDraw Draw(SourceRandomSite site)
        {
            var wrong = new SourceRandomSite(site.Kind, site.BoundOrModulus, site.SourceTick + 1,
                site.ActorId, site.Process, site.ProcessOrder, site.CallIndex, site.Profile, site.SourceIdentity);
            return new SourceRandomDraw(wrong, 0, 0, Provenance);
        }
    }

    private static void StrictBoundaryChecks(JsonElement fixture)
    {
        currentCase = "strict-api";
        bool rejected = false;
        try { new SourceMovementSwitches(49, SourceDroneButtons.None, false, 100f); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Equal("invalid stick rejected", true, rejected);
        rejected = false;
        try { new SourceMovementSwitches(24, SourceDroneButtons.None, false, float.NaN); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Equal("NaN station resource rejected", true, rejected);
        rejected = false;
        try { movementData.Constant("not-a-source-speed"); }
        catch (InvalidOperationException) { rejected = true; }
        Equal("missing movement tuning is not a guessed default", true, rejected);
        rejected = false;
        try { movementData.Script("not-a-source-table", 0); }
        catch (InvalidOperationException) { rejected = true; }
        Equal("missing selector table is not a generic fallback", true, rejected);
        SourceMovementState sample = State(fixture.GetProperty("State"));
        SourceMovementContext context = Context(fixture.GetProperty("Context"), sample);
        var randomContext = new SourceRandomContext(context.Tick.SourceTick, sample.PlayerId, "player_proc", 4, Profile);
        sample.Mode = SourceActorMode.Quarterback; sample.UpperMode = SourceMovementModes.None;
        MustReject<InvalidOperationException>("missing actual animation boundary is visible",
            () => SourceMovement.QuarterbackRun(sample, context, movementData, null, null, randomContext));
        MustReject<InvalidOperationException>("begin_twopart_mode cannot be silently omitted",
            () => SourceMovement.QuarterbackRun(sample, context, movementData, new InvalidBoundary(0), null, randomContext));
        MustReject<InvalidOperationException>("callback cannot replace the executing source actor",
            () => SourceMovement.QuarterbackRun(sample, context, movementData, new InvalidBoundary(1), null, randomContext));
        MustReject<InvalidOperationException>("callback cannot invent missing source RNG consumption",
            () => SourceMovement.QuarterbackRun(sample, context, movementData, new InvalidBoundary(2), null, randomContext));
        sample.Mode = SourceActorMode.Receiver; sample.NewButtons = SourceDroneButtons.B;
        sample.Buttons = SourceDroneButtons.B | SourceDroneButtons.C; sample.LowerMode = SourceMovementModes.None;
        sample.VelocityX = sample.VelocityZ = 0f;
        MustReject<InvalidOperationException>("reachable selector cannot guess a PRNG when its tape is missing",
            () => SourceEvasion.WeaponAfterRun(sample, context, movementData, new Boundary(0), null, randomContext, true));
        var line = new List<float>(); var back = new List<float>();
        foreach (JsonElement value in data.GetProperty("Defense")[5].GetProperty("Line").EnumerateArray()) line.Add(Float(value));
        foreach (JsonElement value in data.GetProperty("Defense")[5].GetProperty("Back").EnumerateArray()) back.Add(Float(value));
        MustReject<InvalidOperationException>("defense-speed assignment validates exact RNG source sites",
            () => SourceMovement.AssignDefenseSpeeds(context.Tick, 0,
                new[] { SourceActorMode.Line, SourceActorMode.Line, SourceActorMode.Line, SourceActorMode.DefensiveBack,
                    SourceActorMode.DefensiveBack, SourceActorMode.DefensiveBack, SourceActorMode.DefensiveBack },
                line, back, 10, 45, false, new WrongSiteRandom(), randomContext));
        MustReject<ArgumentOutOfRangeException>("zero-time leap is an explicit source singularity, not tuned",
            () => new SourceEvasionLeapInput(default(SourceVector3), default(SourceVector3), new float[12],
                default(SourceVector3), default(SourceVector3), 0, 0.7f, 2.2f, 1.65f, false));
    }
}
