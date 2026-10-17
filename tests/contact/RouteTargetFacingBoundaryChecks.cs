using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class RouteTargetFacingObservation
{
    internal static Action<SourceRouteInput> AfterConstruction;
    internal static void Observe(SourceRouteInput input)
    {
        if (AfterConstruction != null) AfterConstruction(input);
    }
}

internal static partial class Program
{
    private const string RouteTargetWitness =
        "tests/movement-callers/.artifacts/current-target-facing-trace/first-public-smoke-failure.json";
    private const string RouteTargetWitnessSha = "37ba5d3b9f22ef15ca28f6469dcfa999eeb0a882be00a863022c290cb0164a53";

    private const string RouteTargetOraclePython = @"
import json, os, subprocess, sys
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path('tests/playbooks').resolve()))
import oracle
source,destination=map(lambda p:Path(p).resolve(),sys.argv[1:])
destination.mkdir(parents=True,exist_ok=True)
driver_path=oracle.SUITE/'driver.c'
original_read=Path.read_text
driver=original_read(driver_path,encoding='utf-8')
if driver.count('int main(void){')!=1:raise RuntimeError('Expected exact existing P1 protocol driver')
driver=driver.replace('int main(void){','static int unused_existing_main(void){')
driver+=r'''
static unsigned route_bits(float value){unsigned result;memcpy(&result,&value,4);return result;}
int main(void){
 int raw,world,side,camera;float distance;
 while(scanf(""%d %d %d %d %f"",&raw,&world,&side,&camera,&distance)==5){
  memset(player_blocks,0,sizeof(player_blocks));memset(drone_blocks,0,sizeof(drone_blocks));
  memset(&game_info,0,sizeof(game_info));memset(&ball_obj,0,sizeof(ball_obj));
  memset(distxz,0,sizeof(distxz));memset(p2p_angles,0,sizeof(p2p_angles));
  rng_count=raw_length=raw_cursor=0;p_status=0;
  game_info.off_side=1-side;game_info.off_init=1-side;game_info.down=1;game_info.los=30;game_info.first_down=50;
  game_info.game_mode=GM_IN_PLAY;game_info.ball_carrier=-1;
  game_info.team_play[game_info.off_side]=get_play(""play_spider"");
  game_info.team_play[!game_info.off_side]=get_play(""play_safe_cover"");
  game_info.team_handicap[0]=game_info.team_handicap[1]=5;
  for(int i=0;i<14;i++){player_blocks[i].plyrnum=i;player_blocks[i].team=i/7;player_blocks[i].station=-1;}
  int id=side*7+6;fbplyr_data *p=player_blocks+id;drone_t *d=drone_blocks+id;
  p->odata.plyrmode=MODE_DB;p->odata.fwd=p->odata.tgt_fwd=raw;
  d->pscript=get_route(""db_rush_qb"");d->target=-1;d->stick=d->old_stick=24;d->os_time=d->ob_time=1000;
  ball_obj.type=LB_FUMBLE;ball_obj.int_receiver=-1;ball_obj.who_threw=-1;
  ball_obj.odata.x=10.0f*isin(world);ball_obj.odata.z=10.0f*icos(world);
  ball_obj.tx=distance*isin(world);ball_obj.tz=distance*icos(world);
  cambot.theta=(float)camera;
  int ball_heading=ptangle(&p->odata.x,&ball_obj.odata.x);
  float target[3]={ball_obj.tx,0.0f,ball_obj.tz};
  float actual_distance=pdistxz(&p->odata.x,target);
  get_drone_switches(p,0);
  int word;route_id(d->pscript,&word);
  printf(""{\""raw\"":%d,\""world\"":%d,\""side\"":%d,\""camera\"":%d,\""distance\"":%u,"",raw,world,side,camera,route_bits(distance));
  printf(""\""ball\"":[%u,%u],\""target\"":[%u,%u],\""ballHeading\"":%d,\""actualDistance\"":%u,"",
    route_bits(ball_obj.odata.x),route_bits(ball_obj.odata.z),route_bits(ball_obj.tx),route_bits(ball_obj.tz),ball_heading,route_bits(actual_distance));
  printf(""\""stick\"":%d,\""buttons\"":%d,\""oldStick\"":%d,\""oldButtons\"":%d,\""stickTicks\"":%d,\""buttonTicks\"":%d,"",
    p->stick_cur,p->but_cur,d->old_stick,d->old_but,d->os_time,d->ob_time);
  printf(""\""flags\"":%d,\""job\"":%d,\""word\"":%d,\""thinkTicks\"":%d,\""draws\"":%d,\""facingAfter\"":%d,\""targetAfter\"":%d}\n"",
    d->flags,p->job,word,d->think_time,rng_count,p->odata.fwd,p->odata.tgt_fwd);
 }
 return ferror(stdin)?2:0;
}
'''
def adapted(path,*args,**kwargs):
 return driver if path==driver_path else original_read(path,*args,**kwargs)
recipe=json.loads(Path('tools/playbooks/recipe.json').read_text())
with patch.object(Path,'read_text',adapted):
 receipt=oracle.build(source,destination,recipe,os.environ)
executable=destination/'source-oracle'
rows=[]
for side in (0,1):
 for raw in (-1023,-960,-513,-437,-64,-63,-1,0,1,63,64,65,512,587,960,1023):
  for world in (0,1,63,64,65,587,960,1023):
   for camera in (0,256,1023):
    for distance in (0.0,1.9999999,2.0,2.0000002,3.9999998,4.0,4.0000005):
     rows.append('%d %d %d %d %.9g'%(raw,world,side,camera,distance))
result=subprocess.run([str(executable)],input='\n'.join(rows)+'\n',text=True,capture_output=True,env=os.environ,timeout=120)
if result.returncode:raise RuntimeError('Actual P1 C loose-ball execution failed: '+result.stderr)
values=[json.loads(line) for line in result.stdout.splitlines()]
if len(values)!=len(rows):raise RuntimeError('Actual C case inventory mismatch')
(destination/'cases.json').write_text(json.dumps({'Cases':values,'Receipt':receipt},indent=2)+'\n')
print(json.dumps({'actual_C_cases':len(values),'unchanged_full_drone_db':True,'new_driver_only_input_output':True}))
";

    private static void RouteTargetFacingBoundaryChecks(string originalRoot)
    {
        bool observedCopy = Environment.GetEnvironmentVariable("BLITZ_ROUTE_TARGET_OBSERVED") == "1";
        ActualRouteTargetTrajectory(observedCopy);
        if (observedCopy) return;
        SourceAiFrame frame = RouteGuardFrame();
        var parameters = new SourceAiParameters(real.Playbook, 5, 5);
        RouteTargetGuards(frame, parameters);
        string worker = Path.Combine(output, "route-target-original-worker.py");
        File.WriteAllText(worker, RouteTargetOraclePython);
        string native = Path.Combine(output, "original-P1");
        RunHeadingProcess("python3", new[] { worker, originalRoot, native }, "route-target-C.log");
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(native, "cases.json"))))
            CompareOriginalRouteTarget(document.RootElement);
        RunObservedRouteTargetCopy(frame, parameters, originalRoot);
    }

    private static SourceAiFrame RouteGuardFrame()
    {
        var actors = new SourceAiActor[14];
        for (int id = 0; id < 14; id++)
            actors[id] = new SourceAiActor(id, default(SourcePlanar), default(SourcePlanar),
                SourceActorMode.DefensiveBack, facing: 512, rawFacing: -512);
        return new SourceAiFrame(actors, real.Playbook.Directions, new SourceRulesState(0, 0, 1, 30, 50),
            new SourceAiBall(SourceBallType.Fumble, default(SourcePlanar), default(SourcePlanar),
                default(SourcePlanar), -1), 1);
    }

    private static SourceRouteInput RouteGuardInput(SourceAiFrame frame, SourceAiParameters parameters,
        int target = -1, int? raw = null)
    {
        return new SourceRouteInput(frame, 6, parameters,
            new SourceRandomContext(1, 6, "player_proc", 8, "OriginalRelease57Hz"),
            targetFacing: target, rawTargetFacing: raw);
    }

    private static void RouteTargetGuards(SourceAiFrame frame, SourceAiParameters parameters)
    {
        SourceRouteInput omitted = RouteGuardInput(frame, parameters);
        Check(omitted.TargetFacing == 512 && omitted.RawTargetFacing == 512,
            "route target old omitted -1 means CANONICAL current-facing fallback, not raw facing");
        SourceRouteInput rawMinusOne = RouteGuardInput(frame, parameters, 1023, -1);
        Check(rawMinusOne.TargetFacing == 1023 && rawMinusOne.RawTargetFacing == -1,
            "route target explicit RAW -1 is distinct from omitted/default -1 sentinel");
        foreach (int raw in new[] { -1023, -437, -1, 0, 1023 })
        {
            SourceRouteInput input = RouteGuardInput(frame, parameters, raw & 1023, raw);
            Check(input.TargetFacing == (raw & 1023) && input.RawTargetFacing == raw,
                "route target coherent original raw/canonical pair " + raw);
        }
        foreach (int invalid in new[] { int.MinValue, -1024, 1024, int.MaxValue })
            MovementBearingReject("rawTargetFacing",
                () => RouteGuardInput(frame, parameters, invalid & 1023, invalid),
                "route target strict original raw range " + invalid);
        MovementBearingReject("rawTargetFacing", () => RouteGuardInput(frame, parameters, 0, -437),
            "route target canonical/raw coherence is mandatory");
        foreach (int invalid in new[] { int.MinValue, -437, -2, 1024, int.MaxValue })
            MovementBearingReject("targetFacing", () => RouteGuardInput(frame, parameters, invalid),
                "route target old canonical/default input guard unchanged " + invalid);
    }

    private static void CompareOriginalRouteTarget(JsonElement native)
    {
        int cases = 0, stopped = 0, rawMaskDiscriminators = 0;
        var examples = new List<object>();
        var parameters = new SourceAiParameters(real.Playbook, 5, 5);
        foreach (JsonElement row in native.GetProperty("Cases").EnumerateArray())
        {
            int raw = MBInt(row, "raw"), side = MBInt(row, "side"), id = side * 7 + 6;
            SourcePlanar ball = new SourcePlanar(Float(row.GetProperty("ball")[0].GetUInt32().ToString(CultureInfo.InvariantCulture)),
                Float(row.GetProperty("ball")[1].GetUInt32().ToString(CultureInfo.InvariantCulture)));
            SourcePlanar target = new SourcePlanar(Float(row.GetProperty("target")[0].GetUInt32().ToString(CultureInfo.InvariantCulture)),
                Float(row.GetProperty("target")[1].GetUInt32().ToString(CultureInfo.InvariantCulture)));
            var actors = new SourceAiActor[14];
            for (int actor = 0; actor < 14; actor++)
                actors[actor] = new SourceAiActor(actor, default(SourcePlanar), default(SourcePlanar),
                    SourceActorMode.DefensiveBack, facing: actor == id ? raw & 1023 : 0,
                    rawFacing: actor == id ? raw : 0);
            var frame = new SourceAiFrame(actors, real.Playbook.Directions, new SourceRulesState(1 - side, 1 - side, 1, 30, 50),
                new SourceAiBall(SourceBallType.Fumble, ball, default(SourcePlanar), target, -1), 1,
                cameraHeading: MBInt(row, "camera"));
            string identity = "original _drone_db raw " + raw + "/world" + MBInt(row, "world")
                + "/side" + side + "/camera" + frame.CameraHeading + "/distanceBits" + row.GetProperty("distance").GetUInt32();
            var randomContext = new SourceRandomContext(1, id, "player_proc", id + 2, "OriginalRelease57Hz");
            var input = new SourceRouteInput(frame, id, parameters, randomContext,
                targetFacing: raw & 1023, rawTargetFacing: raw);
            var random = new SourceRecordedRandom(new SourceRandomDraw[0],
                new SourceRandomProvenance("OriginalRelease57Hz", "actual C loose-ball empty draw tape",
                    "original get_drone_switches/_drone_db", SourceRandomEvidence.RecordedOracle));
            SourceRouteStep step = SourceDroneAI.StepRoute(
                SourceRouteState.Begin(real.Playbook.GetRoute("db_rush_qb")), input, random);
            Check(real.Playbook.Directions.Heading(default(SourcePlanar), ball) == MBInt(row, "ballHeading")
                && Bits(default(SourcePlanar).DistanceTo(target)) == row.GetProperty("actualDistance").GetUInt32(),
                identity + "/actual original ptangle/pdistxz input geometry");
            Check(step.State.Stick == MBInt(row, "stick") && (int)step.State.Buttons == MBInt(row, "buttons"),
                identity + "/ACTUAL original loose-ball linear-raw commands/buttons/stick");
            Check(step.State.OldStick == MBInt(row, "oldStick") && (int)step.State.OldButtons == MBInt(row, "oldButtons")
                && step.State.StickTicks == MBInt(row, "stickTicks") && step.State.ButtonTicks == MBInt(row, "buttonTicks"),
                identity + "/original get_drone_switches smoothing and retained command state");
            Check((int)step.State.Flags == MBInt(row, "flags") && (int)step.State.Job == MBInt(row, "job")
                && step.State.WordIndex == MBInt(row, "word") && step.State.ThinkTicks == MBInt(row, "thinkTicks")
                && step.State.LastSourceTick == 1 && step.CommandWritten && step.ButtonsWritten && step.Facing == -1,
                identity + "/original route mutation and no-facing-write output sentinel");
            Check(step.RandomCalls == MBInt(row, "draws") && random.Consumed == 0
                && frame.Actors[id].RawFacing == MBInt(row, "facingAfter") && input.RawTargetFacing == MBInt(row, "targetAfter"),
                identity + "/original actor raw fields and exact no-extra-RNG");
            if (step.State.Stick == 24) stopped++;
            bool rawHold = Math.Abs(MBInt(row, "ballHeading") - raw) < 64
                && default(SourcePlanar).DistanceTo(target) <= 4f;
            bool maskedHold = Math.Abs(MBInt(row, "ballHeading") - (raw & 1023)) < 64
                && default(SourcePlanar).DistanceTo(target) <= 4f;
            if (rawHold != maskedHold) rawMaskDiscriminators++;
            if (rawHold != maskedHold && examples.Count < 8)
                examples.Add(new { raw, canonical = raw & 1023, ballHeading = MBInt(row, "ballHeading"),
                    distanceBits = row.GetProperty("actualDistance").GetUInt32(), originalStick = MBInt(row, "stick"),
                    actualStick = step.State.Stick, rawHold, maskedHold });
            cases++;
        }
        Check(cases == 5376 && stopped > 0 && stopped < cases && rawMaskDiscriminators > 0,
            "actual original _drone_db case inventory includes distinct stop/turn/move/raw-mask outcomes");
        File.WriteAllText(Path.Combine(output, "original-loose-ball-comparison.json"), JsonSerializer.Serialize(new {
            status = "passed", cases, stopped, rawMaskDiscriminators, examples,
            originalFullDroneDbAndGetDroneSwitches = true, ptanglePdistxzDir2stickUnmodified = true,
            actualStepRouteCompared = true, syntheticFunctionalFramesNotActual858DBOutcome = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CheckRouteRawPrefix(SourceInjectedRandom random, JsonElement reference)
    {
        Check(random.Trace.Count >= 1144 && reference.GetArrayLength() == 1144,
            "actual source858 original unfiltered raw prefix inventory1144");
        for (int i = 0; i < 1144; i++)
        {
            SourceRandomDraw draw = random.Trace[i];
            JsonElement expected = reference[i];
            int? raw = expected.GetProperty("Raw").ValueKind == JsonValueKind.Null ? (int?)null : MBInt(expected, "Raw");
            Check(draw.Order == expected.GetProperty("Order").GetInt64() && draw.Raw == raw
                && draw.Value == MBInt(expected, "Value") && draw.Site.SourceTick == expected.GetProperty("SourceTick").GetInt64()
                && draw.Site.Process == expected.GetProperty("Process").GetString() && draw.Site.ActorId == MBInt(expected, "ActorId")
                && draw.Site.ProcessOrder == MBInt(expected, "ProcessOrder") && draw.Site.CallIndex == MBInt(expected, "CallIndex")
                && draw.Site.SourceIdentity == expected.GetProperty("SourceIdentity").GetString()
                && draw.Site.BoundOrModulus == MBInt(expected, "BoundOrModulus"),
                "actual source858 SAME raw/order/actor/site/context prefix draw " + i);
        }
    }

    private static void ActualRouteTargetTrajectory(bool observedCopy)
    {
        byte[] witnessBytes = File.ReadAllBytes(RouteTargetWitness);
        Check(Hash(witnessBytes) == RouteTargetWitnessSha, "actual source858 approved raw argument-source witness pin");
        using (JsonDocument witness = JsonDocument.Parse(witnessBytes))
        {
            int[] vector = witness.RootElement.GetProperty("rawVector").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            int cursor = 0;
            SourceInjectedRandom random = null;
            var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
            GameSimulation game = real.Create(configuration: configuration,
                presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                randomFactory: seed => random = new SourceInjectedRandom(site => vector[cursor++ % vector.Length],
                    new SourceRandomProvenance("source-sound-smoke/raw-vector-v1",
                        "Explicit repeated raw test vector shared by ALL sites, never target libc PRNG",
                        "unfiltered public M1157 + synchronous original sound", SourceRandomEvidence.ExternalUnverified)));
            lastGame = game;
            bool actualInputObserved = false, actualDispatch = false;
            int inputRawCursor = -1, inputDrawCount = -1, inputProcessOrder = -1, inputCallIndex = -1;
            RouteTargetFacingObservation.AfterConstruction = input =>
            {
                if (input.Frame.SourceTick != 858 || input.ActorId != 7) return;
                ActorState actor = game.Model.Players[7];
                SourceRouteState route = game.Model.SourceDrones[7];
                inputRawCursor = cursor; inputDrawCount = random.Trace.Count;
                inputProcessOrder = input.RandomContext.ProcessOrder; inputCallIndex = input.RandomContext.FirstCallIndex;
                Check(actor.Facing == -437 && actor.TargetFacing == -437 && input.TargetFacing == 587
                    && input.RawTargetFacing == -437 && input.Frame.Actors[7].RawFacing == -437,
                    "ACTUAL source858 SourceDroneMove constructor receives RAW -437/canonical587, not dropped/masked raw");
                Check(actor.SourceMode == SourceActorMode.Line && actor.SourceJob == (SourceActorJob)1
                    && actor.SourceControllerStation == -1 && route.Program.Identity == "db_rush_qb"
                    && route.WordIndex == 0 && (int)route.Flags == 1146888 && route.LastSourceTick == 857,
                    "ACTUAL source858 MODE_LINE source route/flags/station retained, not invented DB branch");
                Check(inputRawCursor == 1144 && inputDrawCount == 1144 && inputProcessOrder == 9 && inputCallIndex == 0
                    && game.Model.SourceRandomProcess == "player_proc" && game.Model.SourceRandomActor == 7,
                    "ACTUAL source858 constructor shared1144-draw prefix/process9/call0, no added RNG");
                CheckRouteRawPrefix(random, witness.RootElement.GetProperty("allSharedRawDraws"));
                actualInputObserved = true;
            };
            int host = 0, deadWait = 0;
            while (game.SourceTick < 859 && host < 2000)
            {
                PlayerCommand command = default(PlayerCommand);
                if (game.State.Phase == GamePhase.PlaySelect)
                    command = new PlayerCommand(action: InputAction.SelectPlay,
                        play: game.State.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Cover);
                else if (game.State.Phase == GamePhase.PreSnap) command = new PlayerCommand(action: InputAction.Snap);
                else if (game.State.Phase == GamePhase.DeadBall)
                    command = ++deadWait > 30 ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand);
                else if (game.State.Phase == GamePhase.LiveBall)
                    command = new PlayerCommand(new FieldVector(0f, 1f), true,
                        game.State.Possession == TeamId.Home ? InputAction.None : InputAction.Tackle);
                if (game.State.Phase != GamePhase.DeadBall) deadWait = 0;
                try { game.Step(command); }
                catch (ArgumentOutOfRangeException error)
                {
                    if (game.SourceTick != 858 || game.Model.SourceRandomActor != 7
                        || error.ParamName != "targetFacing" && error.ParamName != "rawTargetFacing") throw;
                    throw new ContactAssertion("ROUTE ASSERTION: actual source858 raw target rejected before REAL AI dispatch");
                }
                if (game.SourceTick == 858)
                    actualDispatch = game.Model.SourceDrones[7].LastSourceTick == 858
                        && game.Model.SourceDrones[7].ThinkTicks == -357
                        && game.Model.Players[7].SourceMode == SourceActorMode.Line;
                host++;
            }
            Check(game.SourceTick >= 859 && actualDispatch, "actual source858 REAL Line AI dispatch progresses BEYOND858");
            CheckRouteRawPrefix(random, witness.RootElement.GetProperty("allSharedRawDraws"));
            Check(!observedCopy || actualInputObserved,
                "actual source858 constructor observation required; environment flag alone cannot pass");
            RouteTargetFacingObservation.AfterConstruction = null;
            File.WriteAllText(Path.Combine(output, observedCopy ? "actual-source858-observed.json" : "actual-source858-uninstrumented.json"),
                JsonSerializer.Serialize(new {
                    status = "passed", sourceTick = game.SourceTick, host, actualDispatch,
                    observedCopy, actualInputObserved, inputRawCursor, inputDrawCount, inputProcessOrder, inputCallIndex,
                    actualOldPrefix1144Identical = true, rawStateNotNormalizedByBoundary = true,
                    fullM1OrDBExecutionAt858Claimed = false, approvedSourceWitnessSha256 = RouteTargetWitnessSha
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void RunObservedRouteTargetCopy(SourceAiFrame frame, SourceAiParameters parameters, string originalRoot)
    {
        string sourceFile = null;
        try { RouteGuardInput(frame, parameters, 1024); }
        catch (ArgumentOutOfRangeException error)
        {
            foreach (StackFrame call in new StackTrace(error, true).GetFrames())
                if (call.GetFileName() != null && call.GetFileName().Replace('\\', '/').EndsWith(
                    "/Assets/Scripts/Gameplay/SourceDroneAI.cs", StringComparison.Ordinal))
                    sourceFile = call.GetFileName();
        }
        if (sourceFile == null) throw new InvalidDataException("Actual compiled SourceRouteInput PDB source path is required for isolated constructor observation.");
        const string suffix = "Assets/Scripts/Gameplay/SourceDroneAI.cs";
        string sourceRoot = sourceFile.Substring(0, sourceFile.Length - suffix.Length);
        string copy = Path.Combine(output, "observed-production-copy");
        string build = Path.Combine(output, "observed-build");
        var hashes = new Dictionary<string, string>();
        foreach (string group in new[] { "Gameplay", "Contracts", "AssetImport" })
            foreach (string path in Directory.GetFiles(Path.Combine(sourceRoot, "Assets/Scripts", group), "*.cs", SearchOption.AllDirectories))
                CopyRouteObservedSource(sourceRoot, copy, path, hashes);
        foreach (string name in new[] { "Presentation/SourceAudioFunctions.cs", "Presentation/SourceCinematicModel.cs",
            "Presentation/SourceC2SoundFunctions.cs", "Runtime/SourceContactComposition.cs", "Runtime/SourceC2Composition.cs" })
            CopyRouteObservedSource(sourceRoot, copy, Path.Combine(sourceRoot, "Assets/Scripts", name), hashes);
        string original = File.ReadAllText(Path.Combine(copy, suffix));
        const string marker = "            SnapContext = snapContext; KickerContext = kickerContext;";
        const string observe = "\n            global::RouteTargetFacingObservation.Observe(this);";
        Check(original.Split(new[] { marker }, StringSplitOptions.None).Length == 2,
            "exact source constructor observation insertion site, not a replacement implementation");
        string instrumented = original.Replace(marker, marker + observe);
        Check(instrumented.Replace(observe, "") == original,
            "removing sole test observer restores exact real SourceRouteInput bytes");
        File.WriteAllText(Path.Combine(copy, suffix), instrumented);
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        string childOutput = Path.Combine(output, "observed-evidence");
        foreach (string arg in new[] { "run", "--project", "tests/contact/ContactChecks.csproj", "--configuration", "Release",
            "--verbosity", "quiet", "-p:BlitzSourceRoot=" + copy + "/",
            "-p:BaseIntermediateOutputPath=" + build + "/obj/", "-p:BaseOutputPath=" + build + "/bin/", "--",
            inputManifest, inputArtifacts, "route-target", childOutput,
            "tools/audio/.artifacts/functions/contact-sound-v1/sound-functions.safn", originalRoot })
            start.ArgumentList.Add(arg);
        start.Environment["BLITZ_ROUTE_TARGET_OBSERVED"] = "1";
        int exit;
        string text;
        using (Process process = Process.Start(start))
        {
            text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(); exit = process.ExitCode;
        }
        File.WriteAllText(Path.Combine(output, "observed-child.log"), text);
        try
        {
            string result = text.Split('\n').Single(line => line.StartsWith("{\"schema\":", StringComparison.Ordinal));
            using (JsonDocument document = JsonDocument.Parse(result))
                Check(exit == 0 && document.RootElement.GetProperty("status").GetString() == "passed",
                    "ACTUAL source858 producer observation copy: " + result);
            Check(File.Exists(Path.Combine(childOutput, "actual-source858-observed.json")),
                "actual constructor observation receipt required, no stale-status-only success");
            File.WriteAllText(Path.Combine(output, "observed-source-closure.json"), JsonSerializer.Serialize(new {
                actualCompiledSourceRootFromPDB = sourceRoot, beforeInstrumentationSha256 = hashes,
                constructorSourceNormalizedExactly = true, insertion = observe.Trim(),
                productionHasNoObserver = !File.ReadAllText(sourceFile).Contains("RouteTargetFacingObservation.Observe"),
                childExitCode = exit
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Directory.Delete(copy, true);
            Directory.Delete(build, true);
        }
    }

    private static void CopyRouteObservedSource(string root, string copy, string path, Dictionary<string, string> hashes)
    {
        string relative = path.Substring(root.Length);
        byte[] bytes = File.ReadAllBytes(path);
        hashes.Add(relative, Hash(bytes));
        string destination = Path.Combine(copy, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.WriteAllBytes(destination, bytes);
    }
}
