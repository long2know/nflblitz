using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private const string MovementBearingOraclePython = @"
import hashlib, json, sys
from pathlib import Path
from unittest.mock import patch
suite = Path('tests/movement').resolve()
sys.path.insert(0, str(suite))
import oracle as m
from cases import case, command, attach, identity_map
source, destination = map(lambda p: Path(p).resolve(), sys.argv[1:])
original_read = Path.read_bytes
class ScopedOracle(m.Oracle):
    def __init__(self):
        self.artifacts = destination
        self.artifacts.mkdir(parents=True, exist_ok=True)
        self.sources = {}
        for name, expected in m.PINS['blobs'].items():
            data = original_read(source / name)
            if m.blob_sha1(data) != expected:
                raise RuntimeError('Pinned original mismatch: ' + name)
            self.sources[name] = data
        self.selections, self.sites = [], {}
        types_path, driver_path = suite / 'boundary_types.c', suite / 'driver.c'
        types = original_read(types_path)
        old = b'static int bearing(int me,int him) { return bearings[me][him]; }'
        if types.count(old) != 1:
            raise RuntimeError('Expected exactly one existing metadata-only bearing seam')
        types = types.replace(old, self.function('pmisc.c', 'bearing') + b'\nstatic int scoped_bearings[14];\n')
        driver = original_read(driver_path)
        old = b'    int neighbor;\n'
        new = old + b'    if(sscanf(name,""a%dAngle"",&neighbor)==1 && strstr(name,""Angle"")) { if(neighbor<0||neighbor>13)exit(3);p2p_angles[p->plyrnum][neighbor]=atoi(value);return; }\n'
        if driver.count(old) != 1:
            raise RuntimeError('Expected exact native geometry transport seam')
        driver = driver.replace(old, new)
        old = b'        int allowed=active_tick?mode_allows(p):0,human=human_controls(p);'
        new = b'        for(int i=0;i<14;i++)scoped_bearings[i]=bearing(p->plyrnum,i);\n' + old
        if driver.count(old) != 1:
            raise RuntimeError('Expected exact pre-operation source producer seam')
        driver = driver.replace(old, new)
        old = b'    printf(""],\\\""Defense\\\"":["");'
        new = b'    printf(""],\\\""OriginalBearings\\\"":["");for(int i=0;i<14;i++)printf(""%s%d"",i?"","":"""",scoped_bearings[i]);\n' + old
        if driver.count(old) != 1:
            raise RuntimeError('Expected exact native result transport seam')
        driver = driver.replace(old, new)
        def adapted(path):
            if path == types_path: return types
            if path == driver_path: return driver
            return original_read(path)
        with patch.object(Path, 'read_bytes', adapted):
            self.build()
        self.bridge = {'replaced_only_existing_bearing_stub': True,
                       'new_geometry_tokens': 'a0Angle..a13Angle -> actual p2p_angles',
                       'bearings_recorded_before_original_standard_run': True,
                       'existing_metadata_animation_boundary_retained': True,
                       'original_functions_unmodified': True,
                       'original_test_support_files_not_written': True}
oracle = ScopedOracle()
data = oracle.execute(['data'])[0]
data['Tables'] = oracle.tables
rows, lines = [], []
def add(name, raw, angle, side=0, operation='standard', distance=10.0, tape=None, flags=10, carrier=True):
    actor = side * 7 + 3
    row = case(name, operation,
        dict(PlayerId=actor, Station=side, Mode=7, Heading=raw, TargetHeading=raw,
             UpperHeading=raw, UpperTargetHeading=raw, LowerSequence='R_TRB_WB',
             UpperSequence='RUN', Flags=flags, Buttons=4, Stick=21),
        dict(OffenseSide=side, Carrier=actor if carrier else -1, CameraHeading=side*512),
        [0]*256 if tape is None else tape)
    angles = [0]*14
    for other in range((1-side)*7,(1-side)*7+7):
        angles[other] = angle
        row['Context']['Distances'][other] = distance if other < (1-side)*7+2 else 100.0
    text = command(row) + ' ' + ' '.join('a%dAngle=%d'%(i,a) for i,a in enumerate(angles))
    row['Context']['SourceAngles'] = angles
    rows.append(row);lines.append(text)
for side in (0,1):
    for operation in ('standard','general'):
        for raw in (-1023,-512,-234,-66,-2,-1,0,512,1023):
            for angle in (0,1,63,64,65,512,960,1010,1023):
                add('signed/%s/side%d/raw%d/angle%d'%(operation,side,raw,angle),raw,angle,side,operation)
        for distance in (62.4,62.400005):
            add('distance/%s/side%d/%s'%(operation,side,distance),-234,1010,side,operation,distance)
        probability = data['Defense'][5]['Plow']
        add('probability-equal/%s/side%d'%(operation,side),-234,1010,side,operation,tape=[probability]*256)
        add('probability-above/%s/side%d'%(operation,side),-234,1010,side,operation,tape=[probability+1]*256)
        add('not-carrier/%s/side%d'%(operation,side),-234,1010,side,operation,carrier=False)
        add('dragging/%s/side%d'%(operation,side),-234,1010,side,operation,flags=10|0x100000)
results = oracle.execute(lines)
attach(rows,results,identity_map(oracle))
for row, result in zip(rows,results,strict=True):
    row['Context']['Bearings'] = result['OriginalBearings']
payload = {'schema':'source-movement-signed-bearing-C/v1','Source':m.PINS,'Data':data,'Cases':rows,
           'Compiler':oracle.compiler,'OriginalSelections':oracle.selections,'Bridge':oracle.bridge}
(destination/'cases.json').write_text(json.dumps(payload,indent=2)+'\n')
print(json.dumps({'original_C_cases':len(rows),'original_bearing_function_used':True,
                  'original_standard_and_general_run_used':True}))
";

    private static void MovementBearingBoundaryChecks(string originalRoot)
    {
        ActualMovementBearingProducerAndCallback();
        MovementBearingGuards();
        string worker = Path.Combine(output, "original-bearing-movement-worker.py");
        File.WriteAllText(worker, MovementBearingOraclePython);
        string native = Path.Combine(output, "original-m1");
        RunHeadingProcess("python3", new[] { worker, originalRoot, native }, "native-worker.log");
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(native, "cases.json"))))
            CompareOriginalMovementBearing(document.RootElement);
    }

    private static void MovementBearingGuards()
    {
        foreach (int accepted in new[] { -1022, -512, -234, -220, -66, -1, 0, 1, 63, 64, 512 })
            Check(new SourceMovementNeighbor(13, 0f, accepted).Bearing == accepted,
                "M1 source signed bearing admitted unchanged " + accepted);
        foreach (int invalid in new[] { int.MinValue, -1023, 513, 1023, int.MaxValue })
            MovementBearingReject("bearing", () => new SourceMovementNeighbor(0, 1f, invalid),
                "M1 exact active producer bearing guard " + invalid);
        foreach (int invalid in new[] { -1, 14, int.MinValue, int.MaxValue })
            MovementBearingReject("playerId", () => new SourceMovementNeighbor(invalid, 1f, -220),
                "M1 actor index guard " + invalid);
        foreach (float invalid in new[] { -1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            MovementBearingReject("distance", () => new SourceMovementNeighbor(0, invalid, -220),
                "M1 finite nonnegative distance guard " + invalid);
    }

    private static void MovementBearingReject(string parameter, Action action, string identity)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentOutOfRangeException error) { rejected = error.ParamName == parameter; }
        Check(rejected, identity);
    }

    private static void ActualMovementBearingProducerAndCallback()
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(3, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
        GameSimulation game = real.Create(configuration: configuration,
            presentation: new SourcePresentationSettings(TeamId.Away, new[] { -1, 3, 19, -1 }, -2));
        lastGame = game;
        game.SourceCameraHeadingProvider = (snapshot, tick) =>
            FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
        ActorState actor = game.Model.Players[7];
        SourceAnimationLayerState layer = actor.AnimationState.Primary;
        Action<ActorState> before = layer.BeforeInstruction;
        bool observed = false;
        int beforeCallbacks = -1, contextRng = -1, rawBearing = 0;
        int rawFacing = 0, rawTarget = 0, cacheAngle = 0;
        float cacheDistance = 0f;
        layer.BeforeInstruction = executing =>
        {
            before(executing);
            int pc = (int)typeof(SourceAnimationLayerState).GetField("pc",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layer) - 1;
            if (game.SourceTick != 461 || layer.ScriptSymbol != "tie1c_v_anim" || pc != 9) return;
            rawFacing = executing.Facing; rawTarget = executing.TargetFacing;
            cacheAngle = game.Model.SourceAngles[7, 0]; cacheDistance = game.Model.SourceDistances[7, 0];
            int draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
            SourceMovementContext context = null;
            try { context = Bind<Func<ActorState, SourceMovementContext>>(game, "MovementContext")(executing); }
            catch (ArgumentOutOfRangeException error)
            {
                if (error.ParamName != "distance" && error.ParamName != "bearing") throw;
                throw new ContactAssertion("M1 ASSERTION: actual source461 original signed neighbor rejected before real general_run callback");
            }
            contextRng = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count - draws;
            rawBearing = context.Neighbors[0].Bearing;
            Check(rawFacing == -234 && rawTarget == -234 && cacheAngle == 1010
                && Bits(cacheDistance) == Bits(98.68426f) && rawBearing == -220,
                "actual source461 raw -234/cache1010/distance98.68426 produces original SIGNED -220, no abs/mask/clamp");
            Check(executing.Facing == rawFacing && executing.TargetFacing == rawTarget && contextRng == 0,
                "actual source461 MovementContext does not normalize actor/target or add RNG");
            Check(context.Neighbors.Count == 14 && Enumerable.Range(0, 14).All(id =>
                context.Neighbors[id].PlayerId == id &&
                Bits(context.Neighbors[id].Distance) == Bits(game.Model.SourceDistances[7, id])),
                "actual source461 neighbors preserve full source identity/order/cache, not injected labels");
            beforeCallbacks = executing.SourceImmediateGeneralRuns;
            observed = true;
        };
        int host = 0;
        while (game.SourceTick < 462 && host < 1000)
        {
            game.Step(game.State.Phase == GamePhase.DeadBall
                ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand));
            host++;
        }
        Check(observed && game.SourceTick >= 462 && actor.SourceImmediateGeneralRuns == beforeCallbacks + 1,
            "actual unfiltered public B1 source461 general_run callback EXECUTED and progressed BEYOND461");
        File.WriteAllText(Path.Combine(output, "actual-source461-callback.json"), JsonSerializer.Serialize(new {
            schema = "source-M1-signed-bearing-public-boundary/v1", status = "passed",
            actualSourceTick = game.SourceTick, host, actor = actor.Id, rawFacing, rawTarget,
            cacheAngle, cacheDistance, originalSignedBearing = rawBearing,
            contextAddedRngDraws = contextRng, normalizationByProjection = false,
            generalRunCallbacksBefore = beforeCallbacks, generalRunCallbacksAfter = actor.SourceImmediateGeneralRuns,
            actualCallbacksExecuted = actor.SourceImmediateGeneralRuns - beforeCallbacks,
            actualSAFN = "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280",
            targetPrngOrFullB1PassClaimed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int MBInt(JsonElement value, string name) { return value.GetProperty(name).GetInt32(); }
    private static float MBFloat(JsonElement value, string name) { return value.GetProperty(name).GetSingle(); }
    private static bool MBBool(JsonElement value, string name) { return MBInt(value, name) != 0; }

    private static SourceMovementState MBState(JsonElement source)
    {
        object state = new SourceMovementState(MBInt(source, "PlayerId"), MBInt(source, "Station"),
            (SourceActorMode)MBInt(source, "Mode"));
        foreach (JsonProperty item in source.EnumerateObject())
        {
            FieldInfo field = typeof(SourceMovementState).GetField(item.Name);
            field.SetValue(state, field.FieldType == typeof(string) ? (object)item.Value.GetString()
                : field.FieldType == typeof(float) ? item.Value.GetSingle()
                : field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, item.Value.GetInt32())
                : (object)item.Value.GetInt32());
        }
        return (SourceMovementState)state;
    }

    private static SourceMovementContext MBContext(JsonElement c, SourceMovementState state)
    {
        var neighbors = new SourceMovementNeighbor[14];
        for (int id = 0; id < 14; id++)
            neighbors[id] = new SourceMovementNeighbor(id, c.GetProperty("Distances")[id].GetSingle(),
                c.GetProperty("Bearings")[id].GetInt32());
        return new SourceMovementContext(new SourceMovementTick(c.GetProperty("SourceTick").GetInt64(), MBBool(c, "Execute")),
            real.Playbook.Directions, MBInt(c, "OffenseSide"), (SourceMovementPhase)MBInt(c, "Phase"),
            (SourceGameFlags)MBInt(c, "GameFlags"), (SourcePlayType)MBInt(c, "PlayType"),
            (SourcePlayFlags)MBInt(c, "OffensePlayFlags"), (SourcePlayFlags)MBInt(c, "DefensePlayFlags"),
            MBInt(c, "Carrier"), (SourceActorMode)MBInt(c, "CarrierMode"), (SourceBallType)MBInt(c, "BallType"),
            MBInt(c, "IntendedReceiver"), MBInt(c, "BallTicksLeft"), MBFloat(c, "BallX"), MBFloat(c, "BallY"),
            MBFloat(c, "BallDistance"), MBInt(c, "CameraHeading"), MBInt(c, "LineOfScrimmage"),
            MBFloat(c, "LineZ"), MBFloat(c, "MotionSnapX"), MBInt(c, "BackpedalTargetHeading"),
            MBFloat(c, "TeamIndexedTurbo"), MBBool(c, "DefenseFire"), MBBool(c, "SpeedPowerup"),
            MBBool(c, "FastTurboPowerup"), MBBool(c, "RunOutOfBoundsPowerup"), MBBool(c, "ViolencePowerup"),
            MBBool(c, "CarrierLateHittable"), MBFloat(c, "CarrierDistance"), MBFloat(c, "BallTargetX"),
            c.GetProperty("DroneScript").GetString(), MBBool(c, "DroneScriptFullBody"), neighbors,
            real.MovementRuntime.Defense[MBInt(c, "Handicap")].PlowProbability);
    }

    private static void CompareOriginalMovementBearing(JsonElement native)
    {
        JsonElement data = native.GetProperty("Data");
        foreach (JsonProperty constant in data.GetProperty("Constants").EnumerateObject())
            Check(Bits(real.MovementRuntime.Movement.Constant(constant.Name)) == constant.Value.GetUInt32(),
                "M1 actual runtime constant equals original C binary32 " + constant.Name);
        foreach (JsonProperty table in data.GetProperty("Tables").EnumerateObject())
        {
            Check(real.MovementRuntime.Movement.Count(table.Name) == table.Value.GetArrayLength(),
                "M1 actual runtime table count equals original C " + table.Name);
            for (int at = 0; at < table.Value.GetArrayLength(); at++)
                Check(real.MovementRuntime.Movement.Script(table.Name, at) == table.Value[at].GetString(),
                    "M1 actual runtime table operand equals original C " + table.Name + "/" + at);
        }
        int cases = 0, plows = 0, signedPredicateDiscriminators = 0;
        var evidence = new List<object>();
        foreach (JsonElement row in native.GetProperty("Cases").EnumerateArray())
        {
            string name = row.GetProperty("Name").GetString();
            SourceMovementState state = MBState(row.GetProperty("State"));
            JsonElement c = row.GetProperty("Context"), expected = row.GetProperty("Expected");
            SourceMovementContext context = MBContext(c, state);
            for (int id = 0; id < 14; id++)
            {
                int raw = (1024 + state.Heading - c.GetProperty("SourceAngles")[id].GetInt32()) % 1024;
                if (raw > 512) raw = 1024 - raw;
                Check(raw == context.Neighbors[id].Bearing, name + "/actual original pmisc bearing " + id);
                if (raw < -63) signedPredicateDiscriminators++;
            }
            var randomContext = new SourceRandomContext(context.Tick.SourceTick, state.PlayerId, "player_proc",
                state.PlayerId + 1, "OriginalRelease57Hz");
            var provenance = new SourceRandomProvenance("OriginalRelease57Hz", "explicit original-C boundary draw tape",
                name, SourceRandomEvidence.RecordedOracle);
            var tape = new List<SourceRandomDraw>();
            foreach (JsonElement draw in expected.GetProperty("Draws").EnumerateArray())
            {
                SourceRandomSite site = randomContext.At(SourceRandomKind.RandRange, MBInt(draw, "Bound"),
                    draw.GetProperty("Identity").GetString(), tape.Count);
                tape.Add(new SourceRandomDraw(site, draw.GetProperty("Raw").ValueKind == JsonValueKind.Null
                    ? (int?)null : MBInt(draw, "Raw"), tape.Count, provenance));
            }
            var random = new SourceRecordedRandom(tape, provenance);
            var boundary = new MovementBearingHeaderBoundary(data.GetProperty("Responses"));
            SourceMovementResult actual;
            try
            {
                actual = row.GetProperty("Operation").GetString() == "general"
                    ? SourceMovement.GeneralRun(state, context, real.MovementRuntime.Movement, boundary, random,
                        randomContext, c.GetProperty("BreathControl").GetUInt32())
                    : SourceMovement.StandardRun(state, context, real.MovementRuntime.Movement, boundary, random,
                        randomContext, c.GetProperty("BreathControl").GetUInt32());
            }
            catch (InvalidOperationException error)
            {
                throw new ContactAssertion("M1 ASSERTION: " + name +
                    "/actual original standard_run signed action/draw sequence: " + error.Message);
            }
            object boxed = actual.State;
            foreach (JsonProperty field in expected.GetProperty("State").EnumerateObject())
            {
                FieldInfo member = typeof(SourceMovementState).GetField(field.Name);
                object value = member.GetValue(boxed);
                Check(member.FieldType == typeof(float) ? Bits((float)value) == field.Value.GetUInt32()
                    : member.FieldType == typeof(string) ? (string)value == field.Value.GetString()
                    : Convert.ToInt32(value, CultureInfo.InvariantCulture) == field.Value.GetInt32(),
                    name + "/actual original standard_run mutation " + field.Name);
            }
            JsonElement commands = expected.GetProperty("Commands");
            Check(actual.Commands.Count == commands.GetArrayLength(), name + "/actual original standard_run command count");
            for (int at = 0; at < actual.Commands.Count; at++)
                Check((int)actual.Commands[at].Kind == MBInt(commands[at], "Kind")
                    && actual.Commands[at].Script == commands[at].GetProperty("Script").GetString()
                    && boundary.Counts[at].Item1 == MBInt(commands[at], "TeamSpinCount")
                    && boundary.Counts[at].Item2 == MBInt(commands[at], "TeamHurdleCount"),
                    name + "/actual original standard_run ordered animation consequence " + at);
            Check(actual.RandomCalls == tape.Count && random.Consumed == tape.Count,
                name + "/actual original standard_run exact RNG calls/identity/order");
            bool plow = actual.Commands.Any(command => command.Kind == SourceMovementCommandKind.UpperBody
                && command.Script == "running_plow_anim");
            if (plow) plows++;
            evidence.Add(new { name, state.PlayerId, rawHeading = state.Heading,
                bearings = context.Neighbors.Select(neighbor => neighbor.Bearing).ToArray(),
                originalCommands = commands.Clone(), actualCommands = actual.Commands.Select(command =>
                    new { kind = (int)command.Kind, command.Script }).ToArray(), plow, actual.RandomCalls });
            cases++;
        }
        Check(cases == 348 && plows > 0 && plows < cases && signedPredicateDiscriminators > 0,
            "original C signed/circular/plow/distance/probability/not-carrier/dragging cases actually discriminate outcomes");
        File.WriteAllText(Path.Combine(output, "native-standard-run-comparison.json"), JsonSerializer.Serialize(new {
            schema = "source-M1-original-bearing-standard-run/v1", status = "passed", cases, plows,
            signedPredicateDiscriminators, realRuntimeConstants = true, originalBearingStubReplacedByActualSource = true,
            explicitOriginalDrawTapes = true, metadataBoundaryNotFullAnimationVM = true,
            actualKernelVerifiedSeparately = true, evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class MovementBearingHeaderBoundary : ISourceMovementBoundary
    {
        private readonly JsonElement responses;
        internal readonly List<Tuple<int, int>> Counts = new List<Tuple<int, int>>();
        internal MovementBearingHeaderBoundary(JsonElement responses) { this.responses = responses; }
        public SourceMovementBoundaryResult Apply(SourceMovementState state, SourceMovementCommand command,
            SourceRandomContext context, ISourceRandom random)
        {
            Counts.Add(Tuple.Create(state.TeamSpinCount, state.TeamHurdleCount));
            if (command.Kind == SourceMovementCommandKind.BeginTwoPart)
            {
                state.UpperSequence = state.LowerSequence;
                state.UpperMode = state.LowerMode | SourceMovementModes.TwoPart;
                state.UpperHeading = state.Heading; state.UpperTargetHeading = state.TargetHeading;
            }
            else if (command.Kind == SourceMovementCommandKind.FullBody || command.Kind == SourceMovementCommandKind.LowerBody ||
                command.Kind == SourceMovementCommandKind.UpperBody)
            {
                if (command.Kind == SourceMovementCommandKind.FullBody) state.UpperMode &= ~(SourceMovementModes)0xffff;
                bool upper = command.Kind == SourceMovementCommandKind.UpperBody;
                SourceMovementModes mode = (upper ? state.UpperMode : state.LowerMode) & ~(SourceMovementModes)0xffff;
                mode &= ~SourceMovementModes.Interpolating;
                JsonElement response = responses.GetProperty(command.Script);
                int assigned = MBInt(response, "Mode");
                if (assigned >= 0) mode = (mode & ~(SourceMovementModes)0xffff) | (SourceMovementModes)assigned;
                string sequence = response.GetProperty("Sequence").GetString();
                if (sequence != "") { if (upper) state.UpperSequence = sequence; else state.LowerSequence = sequence; }
                if (MBBool(response, "Zero")) state.VelocityX = state.VelocityY = state.VelocityZ = 0f;
                if (MBBool(response, "HasVelocity"))
                {
                    JsonElement velocity = response.GetProperty("Velocity");
                    float x = Float(velocity[0].GetUInt32().ToString(CultureInfo.InvariantCulture));
                    float z = Float(velocity[2].GetUInt32().ToString(CultureInfo.InvariantCulture));
                    state.VelocityX = z * real.Playbook.Directions.Sin(state.Heading) + x * real.Playbook.Directions.Cos(state.Heading);
                    state.VelocityY = Float(velocity[1].GetUInt32().ToString(CultureInfo.InvariantCulture));
                    state.VelocityZ = z * real.Playbook.Directions.Cos(state.Heading) - x * real.Playbook.Directions.Sin(state.Heading);
                }
                if (upper) state.UpperMode = mode; else state.LowerMode = mode;
                state.Flags |= SourceMovementFlags.AnimationTick;
            }
            else throw new InvalidOperationException("Unqualified movement-bearing native boundary " + command.Kind);
            return new SourceMovementBoundaryResult(state, 0);
        }
    }
}
