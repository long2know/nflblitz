using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static partial class Program
{
    private static int canonicalResultTimingAssertions;
    private static bool contactWindowProductionCallers;
    private static readonly Dictionary<string, object> resultTimingEvidence = new Dictionary<string, object>();

    private static void C2ResultTimingCheck(bool condition, string identity)
    {
        canonicalResultTimingAssertions++;
        Check(condition, "C2 result timing: " + identity);
    }

    private const string LiveDroneOriginalBuilder = @"
import hashlib,json,os,sys
from pathlib import Path
from unittest.mock import patch
root=Path.cwd()
sys.path.insert(0,str(root/'tests/playbooks'))
import oracle
source,output=map(lambda x:Path(x).resolve(),sys.argv[1:])
recipe=json.loads((root/'tools/playbooks/recipe.json').read_text())
driver_path=root/'tests/playbooks/driver.c'
original_read=Path.read_text
driver=original_read(driver_path)
old=' geometry();\n if(update){assign_blockers();update_receiver_data();}'
new=' if(update){assign_blockers();update_receiver_data();}'
if driver.count(old)!=1: raise RuntimeError('Expected exactly one step geometry boundary')
driver=driver.replace(old,new)
old='  else if(!strcmp(token,""step""))step();'
new='  else if(!strcmp(token,""cache""))geometry();\n'+old
if driver.count(old)!=1: raise RuntimeError('Expected exactly one step command boundary')
driver=driver.replace(old,new)
def adapted(path,*args,**kwargs):
    return driver if path==driver_path else original_read(path,*args,**kwargs)
with patch.object(Path,'read_text',adapted):
    receipt=oracle.build(source,output,recipe,os.environ.copy())
receipt['cache_boundary']={
    'geometry_before_earlier_player_callbacks':True,
    'live_variables_after_earlier_player_callbacks':True,
    'original_drone_function_expression_rewrites':0,
    'existing_test_support_not_written':True,
    'driver_sha256':hashlib.sha256(driver.encode()).hexdigest()}
(output/'live-drone-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps({'status':'passed','selected_original_functions':receipt['selected_function_count']}))
";

    private sealed class LiveDroneOriginal : IDisposable
    {
        private readonly System.Diagnostics.Process process;
        private bool failedRead;
        internal readonly List<string> Commands = new List<string>();
        internal LiveDroneOriginal(string executable)
        {
            process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false
            });
        }
        internal void Send(params object[] values)
        {
            string command = string.Join(" ", values.Select(v => v is float
                ? ((float)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)));
            Commands.Add(command);
            process.StandardInput.WriteLine(command);
        }
        internal string[] Step()
        {
            Send("step", 11, 0, 0); process.StandardInput.Flush();
            return ReadReply();
        }
        internal string[] ReadReply()
        {
            process.StandardInput.Flush();
            string line = process.StandardOutput.ReadLine();
            if (line == null) throw new IOException("Original C exited: " + process.StandardError.ReadToEnd());
            return line.Split(' ');
        }
        internal string ReadJson(string command)
        {
            Send(command); process.StandardInput.Flush();
            string line = process.StandardOutput.ReadLine();
            if (line == null)
            {
                failedRead = true;
                throw new IOException("Original C exited: " + process.StandardError.ReadToEnd());
            }
            return line;
        }
        public void Dispose()
        {
            if (!process.HasExited)
            {
                Send("quit"); process.StandardInput.Flush();
                if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(); }
            }
            int code = process.ExitCode;
            string error = process.StandardError.ReadToEnd();
            process.Dispose();
            if (code != 0 && !failedRead) throw new IOException("Original C failed: " + code + " " + error);
        }
    }

    private static GameSimulation LiveDroneFixture(float defenderZ = 80f, SourceMatchConfiguration match = null)
    {
        var configuration = match ?? new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(2, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First);
        GameSimulation game = real.Create(configuration: configuration,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            randomFactory: seed => SourceRandom.CreatePortableLibrary(seed,
                "Nov10 live-drone caller; unchanged seed1; not original target PRNG evidence"));
        lastGame = game;
        SimulationState m = game.Model;
        m.SourceTick = 100; m.SourceSnapTick = 0;
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 60, SourcePlayType.Normal);
        m.Possession = TeamId.Home; m.SourceLastPossession = 0;
        m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
        m.SourceDefensePlay = real.Playbook.GetPlay("play_safe_cover");
        m.SourceFlags = SourceGameFlags.None; m.CarrierId = 3;
        m.BallKind = SourceBallKind.Pass; m.BallMode = BallMode.Held;
        m.ThrowerId = 3; m.TargetPlayerId = 4;
        m.CatchSourceTick = 0; m.SourcePlayEndX = 0f;
        m.PassTicksElapsed = 45; m.PassTicksTotal = 80;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead; actor.SourceControllerStation = -1;
            actor.SourceJob = SourceActorJob.None; actor.SourceTarget = -1;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0f;
            actor.SourceRoot = default(SourceVector3); actor.SourceFriction = 0f;
            actor.SourceRawFlags = 0x2; actor.SourceBackpedal = actor.SourceThrowing = false;
            actor.SourceButtons = actor.SourceNewButtons = SourceDroneButtons.None;
            actor.SourceStick = actor.SourcePreviousStick = 24;
            actor.PendingThrowTarget = -1;
            actor.AnimationState.Primary.SetMovementMode(0u);
            actor.AnimationState.Secondary.SetMovementMode(0u);
            actor.Facing = actor.TargetFacing = actor.Team == TeamId.Home ? 256 : 768;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(-240f + actor.Id * 30f, 0f, 130f));
            m.SourceDrones[actor.Id] = SourceRouteState.Begin(
                (actor.Team == TeamId.Home ? m.SourceOffensePlay : m.SourceDefensePlay).Routes[actor.Id % 7]);
        }
        ActorState qb = m.Players[3], receiver = m.Players[4], defender = m.Players[11];
        qb.SourceMode = SourceActorMode.Quarterback; qb.Role = ActorRole.Quarterback;
        receiver.SourceMode = SourceActorMode.Receiver; receiver.Role = ActorRole.Receiver;
        defender.SourceMode = SourceActorMode.DefensiveBack; defender.Role = ActorRole.Defender;
        defender.SourceJob = SourceActorJob.Cover; defender.SourceTarget = 4;
        m.SourceDrones[11] = m.SourceDrones[11].WithTarget(4);
        SourcePlayerDriver.SetSourcePosition(qb, new SourceVector3(-100f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(receiver, new SourceVector3(0f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(defender, new SourceVector3(0f, 0f, defenderZ));
        SourceBallDriver.SetPosition(m, new SourceVector3(-100f, 14f, 0f));
        SourceBallDriver.SetTarget(m, 0f, 0f);
        Cache(game); m.SourceCameraHeading = 0;
        m.SourceAiFrame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
        return game;
    }

    private static void LiveDroneActorInput(LiveDroneOriginal original, GameSimulation game, int id)
    {
        ActorState actor = game.Model.Players[id];
        SourceVector3 p = SourcePlayerDriver.WorldPosition(actor);
        SourceAiActor current = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true).Actors[id];
        original.Send("actor", id, p.X, p.Z, actor.SourceVelocity.X, actor.SourceVelocity.Z, p.Y,
            (int)actor.SourceMode, (int)actor.SourceJob, actor.SourceTarget, (int)current.Flags,
            actor.SourceControllerStation, actor.Facing, actor.TargetFacing, actor.SourceStick, actor.PendingThrowTarget);
    }

    private static void LiveDroneBallInput(LiveDroneOriginal original, GameSimulation game)
    {
        SimulationState m = game.Model;
        SourceAiBall ball = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true).Ball;
        original.Send("ball", m.SourceBallPosition.X, m.SourceBallPosition.Z, m.SourceBallVelocity.X,
            m.SourceBallVelocity.Z, m.SourceBallTarget.X, m.SourceBallTarget.Z,
            m.SourceBallPosition.Y, m.SourceBallVerticalVelocity, m.CarrierId, m.TargetPlayerId, m.ThrowerId,
            (int)ball.Type, m.PassTicksElapsed, m.PassTicksTotal, ball.Bounced ? 1 : 0);
    }

    private static SourceGameEvent[] LiveDroneSourceEvents(GameSimulation game)
    {
        return ((IEnumerable<SourceGameEvent>)typeof(GameSimulation).GetField("sourceStepEvents",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(game)).ToArray();
    }

    private static void LiveDroneCase(string name, string executable, Action<GameSimulation> change)
    {
        GameSimulation game = LiveDroneFixture(name == "earlier-receiver-physics" ? 75.5f : 80f);
        SimulationState m = game.Model;
        var random = (SourceInjectedRandom)game.SourceRandomService;
        using (var original = new LiveDroneOriginal(executable))
        {
            original.Send("reset", 0, 30, 60, 1, 0, 0, (int)m.SourceFlags, 3, 3, 5, 5, 0, -1, 0f,
                "play_spider", "play_safe_cover", 62.4f, 10, 0, 1, 0);
            for (int id = 0; id < 14; id++) LiveDroneActorInput(original, game, id);
            LiveDroneBallInput(original, game); original.Send("cache");
            float[,] distances = (float[,])m.SourceDistances.Clone();
            int[,] angles = (int[,])m.SourceAngles.Clone();
            change(game);
            SourceRouteState before = m.SourceDrones[11];
            for (int id = 0; id < 14; id++) LiveDroneActorInput(original, game, id);
            LiveDroneBallInput(original, game);
            original.Send("clock", 100, 0, 0f, m.Phase == GamePhase.LiveBall ? 3 : 0, (int)m.SourceFlags);
            original.Send("route", 11, before.Program.Identity, (int)before.Flags, before.BlitzTicks);
            original.Send("timers", 11, before.ThinkTicks, before.EvadeTicks, before.BlitzTicks,
                before.Stick, before.OldStick, before.StickTicks,
                (int)before.Buttons, (int)before.OldButtons, before.ButtonTicks);
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 11, 12);
            int start = random.Trace.Count;
            Bind<Func<ActorState, PlayerCommand, FieldVector>>(game, "SourceDroneMove")(m.Players[11], default(PlayerCommand));
            SourceRandomDraw[] draws = random.Trace.Skip(start).ToArray();
            original.Send(new object[] { "tape", draws.Length }.Concat(draws.Select(d => (object)d.Raw.Value)).ToArray());
            string[] expected = original.Step();
            SourceRouteState actual = m.SourceDrones[11];
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
                name, sourceTick = m.SourceTick, actor = 11, original = string.Join(" ", expected),
                actual = new { actual.Stick, buttons = (int)actual.Buttons, job = (int)m.Players[11].SourceJob,
                    actual.Target, actual.PassReactTicks, actual.ThinkTicks },
                rawDraws = draws.Select(d => new { d.Raw, d.Value, bound = d.Site.BoundOrModulus,
                    d.Site.SourceIdentity, d.Site.SourceTick, d.Site.ActorId, d.Site.CallIndex }),
                commands = original.Commands.ToArray(), c2Events = game.C2Events,
                sourceEvents = LiveDroneSourceEvents(game),
                full_gameplay_parity_claimed = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            Check(actual.Stick == int.Parse(expected[4]), name + ": live original stick");
            Check((int)actual.Buttons == int.Parse(expected[7]), name + ": live original buttons");
            Check((int)m.Players[11].SourceJob == int.Parse(expected[10]), name + ": live original job");
            Check(actual.Target == int.Parse(expected[11]), name + ": original target");
            Check(actual.PassReactTicks == int.Parse(expected[12]), name + ": original reaction timer");
            Check(actual.ThinkTicks == int.Parse(expected[18]), name + ": original think timer");
            Check(draws.Length == int.Parse(expected[45]), name + ": exact original RNG draw count");
            for (int i = 0; i < draws.Length; i++)
                Check(draws[i].Site.BoundOrModulus == int.Parse(expected[46 + i * 3])
                    && draws[i].Value == int.Parse(expected[47 + i * 3])
                    && draws[i].Raw == int.Parse(expected[48 + i * 3]), name + ": ordered original RNG " + i);
            for (int i = 0; i < 14; i++)
                for (int j = 0; j < 14; j++)
                    Check(Bits(m.SourceDistances[i, j]) == Bits(distances[i, j]) && m.SourceAngles[i, j] == angles[i, j],
                        name + ": pre-player geometry remains frozen " + i + "/" + j);
        }
    }

    private static void LiveDroneCallerChecks(string originalRoot)
    {
        Check(real.IsC2M1Profile, "live-drone cases use actual M1 caller composition and original animation programs");
        string worker = Path.Combine(output, "live-drone-original.py");
        File.WriteAllText(worker, LiveDroneOriginalBuilder);
        string native = Path.Combine(output, "original-live-drone");
        RunHeadingProcess("python3", new[] { "-B", worker, originalRoot, native }, "original-live-drone-build.log");
        string executable = Path.Combine(native, "source-oracle");
        var failures = new List<string>();
        foreach (var item in new[] {
            new KeyValuePair<string, Action<GameSimulation>>("earlier-receiver-position", game =>
                SourcePlayerDriver.SetSourcePosition(game.Model.Players[4], new SourceVector3(0f, 0f, 160f))),
            new KeyValuePair<string, Action<GameSimulation>>("earlier-receiver-physics", game => {
                ActorState receiver = game.Model.Players[4];
                receiver.SourceVelocity = new FieldVector(0f, 1.15f);
                Bind<Action<ActorState>>(game, "IntegrateSourceMovementPlayer")(receiver);
                Check(SourcePlayerDriver.WorldPosition(receiver).Z == 1.15f,
                    "actual preceding source player physics advances the receiver one ordinary tick");
            }),
            new KeyValuePair<string, Action<GameSimulation>>("earlier-qb-release", game => {
                game.Model.Players[11].SourceJob = SourceActorJob.Rush;
                game.Model.Players[11].SourceTarget = -1;
                game.Model.SourceDrones[11] = game.Model.SourceDrones[11].WithTarget(-1);
                game.Model.SourceAiFrame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
                game.Model.Players[3].PendingThrowTarget = 4;
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 3, 4);
                Bind<Action<int>>(game, "ReleaseSourcePass")(3);
                Check(game.Model.CarrierId == -1 && game.Model.PassTicksElapsed == 0,
                    "actual original release callback runs before later defender");
                SourceGameEvent[] released = LiveDroneSourceEvents(game)
                    .Where(e => e.SourceTick == 100 && e.Type == SourceEventType.PassReleased).ToArray();
                Check(released.Length == 1 && released[0].ActorPlayerId == 3 && released[0].TargetPlayerId == 4,
                    "actual original release emits one source100 passer3 target4 event");
            }),
            new KeyValuePair<string, Action<GameSimulation>>("earlier-player-down", game => {
                game.Model.CarrierId = 4;
                game.Model.SourceAiFrame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 4, 5);
                Bind<Action<int>>(game, "SourcePlayerDown")(4);
                Check(game.Model.Phase == GamePhase.DeadBall, "actual original player_down ends live mode");
                SourceC2Event[] events = game.C2Events.Where(e => e.SourceTick == 100).ToArray();
                Check(events.Length == 2 && events[0].Kind == SourceC2EventKind.WhistleSpawned
                    && events[1].Kind == SourceC2EventKind.PlayOverQueued,
                    "actual player_down preserves source100 whistle creation before play-over queue");
            })
        })
        {
            try { LiveDroneCase(item.Key, executable, item.Value); }
            catch (ContactAssertion error) { failures.Add(error.Message); }
        }
        try { LiveDroneDetectorCache(); }
        catch (ContactAssertion error) { failures.Add(error.Message); }
        Check(failures.Count == 0, string.Join("; ", failures));
    }

    private static void LiveDroneDetectorCache()
    {
        GameSimulation game = LiveDroneFixture();
        SimulationState m = game.Model;
        m.SourceTick = m.SourceProcesses.SourceTick;
        foreach (ActorState actor in m.Players) actor.SourceRawFlags |= 0x20;
        m.Players[0].SourceRawFlags &= ~0x20;
        m.Players[1].SourceRawFlags &= ~0x20;
        SourcePlayerDriver.SetSourcePosition(m.Players[0], new SourceVector3(0f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(m.Players[1], new SourceVector3(8f, 0f, 0f));
        Cache(game);
        Bind<Action>(game, "SourceContacts")();
        SourceContactEvent[] detectorEvents = game.ContactEvents.ToArray();
        Check(m.SourceDistances[0, 1] == 8f
            && SourcePlayerDriver.WorldPosition(m.Players[0]).X == -1f
            && SourcePlayerDriver.WorldPosition(m.Players[1]).X == 9f,
            "actual detect caches eight before original body collision separates teammate roots to ten");
        Check(detectorEvents.Length == 1 && detectorEvents[0].Kind == SourceContactEventKind.Collision
            && detectorEvents[0].ActorPlayerId == 0 && detectorEvents[0].OtherPlayerId == 1
            && detectorEvents[0].AttackMode == 0,
            "actual prior detector emits exactly the ordered teammate body contact, not a synthetic cache write");
        m.SourceProcesses.SuspendMultiple(0x0500, -1);
        int drawsBefore = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        Wake(game);
        File.WriteAllText(Path.Combine(output, "previous-detector-cache.json"), JsonSerializer.Serialize(new {
            sourceTick = m.SourceTick, previousDetectorDistance = 8f,
            consumerDistance = m.SourceAiFrame.Distance(0, 1), newDetectorDistance = m.SourceDistances[0, 1],
            detectorEvents, nextDetectorEvents = game.ContactEvents, actorProcessesSuspended = true,
            rngDraws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count - drawsBefore,
            sourceAuthority = "player.c:pre_player_proc calls drone_update only; detect.c:540-552 is cache writer",
            full_gameplay_parity_claimed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(((SourceInjectedRandom)game.SourceRandomService).Trace.Count == drawsBefore,
            "isolated retained-cache Step consumes no additional RNG");
        Check(m.SourceAiFrame.Distance(0, 1) == 8f && m.SourceDistances[0, 1] == 10f,
            "next real Step pre-player consumes prior detector cache, not recomputed post-response positions");
    }

    private const string PossessionRecoveryOriginalBuilder = @"
import hashlib,json,os,sys
from pathlib import Path
from unittest.mock import patch
root=Path.cwd()
sys.path.insert(0,str(root/'tests/playbooks'))
import oracle
source,output=map(lambda x:Path(x).resolve(),sys.argv[1:])
documents=oracle.verify_inputs(source)
player=documents['player.c'].decode('latin1')
catch=oracle.body(player,'standard_catch')
marker='for( i = 0; i < NUM_PLAYERS; i++ )'
begin=catch.index(marker,catch.index('game_info.old_los = FIELDX'))
end=catch.index('drone_poss_change();',begin)+len('drone_poss_change();')
block=catch[begin:end]
if block.count('~PF_SPEED_OVERRIDE')!=1: raise RuntimeError('Expected original recovery speed-latch clear')
drone_change=oracle.body(documents['drone.c'].decode('latin1'),'drone_poss_change')
farthest=oracle.body(player,'farthest_teammate')
extra=drone_change+'\n'+farthest+'\nstatic void recovery(void) {\n'
extra+='game_info.off_side=ni();int change=ni(),i;for(i=0;i<14;i++){player_blocks[i].odata.flags=ni();player_blocks[i].odata.plyrmode=ni();}\n'
extra+='if(change){\n'+block+'\n}\nprintf(""R"");for(int i=0;i<14;i++)printf("" %d %d"",player_blocks[i].odata.flags,player_blocks[i].odata.plyrmode);puts("""");fflush(stdout);\n}\n'
extra+='static void farthest_input(void){int target=ni();for(int i=0;i<14;i++){player_blocks[i].plyrnum=i;player_blocks[i].team=i/7;player_blocks[i].station=ni();distxz[target][i]=nf();}printf(""F %d\\n"",farthest_teammate(player_blocks+target));fflush(stdout);}\n'
extra+='static void diver_input(void){int me=ni();for(int i=0;i<14;i++){int seq=ni();player_blocks[i].plyrnum=i;player_blocks[i].team=i/7;player_blocks[i].odata.adata[0].seq_index=seq==1?SEQ_T_CRZDIV:seq==2?SEQ_T_SPRHED:0;player_blocks[i].throw_target=ni();distxz[me][i]=nf();}printf(""D %d\\n"",find_diver(player_blocks+me));fflush(stdout);}\n'
driver_path=root/'tests/playbooks/driver.c'
original_read=Path.read_text
driver=original_read(driver_path)
old='int main(void){'
if driver.count(old)!=1: raise RuntimeError('Expected original test command entry')
driver=driver.replace(old,extra+'\n'+old)
old='  else if(!strcmp(token,""step""))step();'
if driver.count(old)!=1: raise RuntimeError('Expected original test command dispatcher')
driver=driver.replace(old,'  else if(!strcmp(token,""recovery""))recovery();\n  else if(!strcmp(token,""farthest""))farthest_input();\n  else if(!strcmp(token,""diver""))diver_input();\n'+old)
def adapted(path,*args,**kwargs):
    return driver if path==driver_path else original_read(path,*args,**kwargs)
recipe=json.loads((root/'tools/playbooks/recipe.json').read_text())
try:
    with patch.object(Path,'read_text',adapted):
        receipt=oracle.build(source,output,recipe,os.environ.copy())
except Exception:
    if (output/'compiler.log').is_file(): print((output/'compiler.log').read_text(),file=sys.stderr)
    raise
receipt['recovery_fragment']={'path':'player.c','symbol':'standard_catch:defensive-transition',
    'git_blob_sha1':oracle.blob_sha1(documents['player.c']),
    'sha256':hashlib.sha256(block.encode('latin1')).hexdigest(),'expression_rewrites':0,
    'drone_poss_change_sha256':hashlib.sha256(drone_change.encode('latin1')).hexdigest(),
    'farthest_teammate_sha256':hashlib.sha256(farthest.encode('latin1')).hexdigest()}
(output/'recovery-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
";

    private static void PossessionRecoveryChecks(string originalRoot)
    {
        string worker = Path.Combine(output, "recovery-original.py");
        File.WriteAllText(worker, PossessionRecoveryOriginalBuilder);
        string native = Path.Combine(output, "original-recovery");
        RunHeadingProcess("python3", new[] { "-B", worker, originalRoot, native }, "original-recovery-build.log");
        string executable = Path.Combine(native, "source-oracle");
        var failures = new List<string>();
        foreach (int side in new[] { 0, 1 })
            foreach (string kind in new[] { "fumble", "bounced-lateral", "same-team", "too-early" })
            {
                try { PossessionRecoveryCase(executable, side, kind); }
                catch (ContactAssertion error) { failures.Add(error.Message); }
            }
        foreach (int side in new[] { 0, 1 })
            foreach (string kind in new[] { "airborne-distance", "prior-detector", "equal-distance" })
            {
                try { CatchControlCacheCase(executable, side, kind); }
                catch (ContactAssertion error) { failures.Add(error.Message); }
            }
        try { PublicRecoverySpeedStep(); }
        catch (ContactAssertion error) { failures.Add(error.Message); }
        Check(failures.Count == 0, string.Join("; ", failures));
    }

    private static void PublicRecoverySpeedStep()
    {
        GameSimulation game = LiveDroneFixture();
        SimulationState m = game.Model;
        m.SourceTick = m.SourceProcesses.SourceTick;
        m.Possession = TeamId.Away; m.SourceLastPossession = 1;
        m.SourceRules = new SourceRulesState(1, 1, 1, 30, 60, SourcePlayType.Normal);
        m.SourceFlags = SourceGameFlags.BallCrossedLine | SourceGameFlags.OverLineSpeedsSet | SourceGameFlags.Fumble;
        m.CarrierId = -1; m.BallMode = BallMode.InFlight; m.BallKind = SourceBallKind.Fumble;
        m.SourceBallFlags = SourceBallFlags.Bounce; m.PassTicksElapsed = 31; m.PassTicksTotal = 0;
        m.TargetPlayerId = -1; m.ThrowerId = 10;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead; actor.SourceControllerStation = -1;
            actor.SourceRawFlags = (int)(SourceMovementFlags.NoCollision | SourceMovementFlags.SpeedOverride);
            actor.SourceOverrideSpeed = 1.85f; actor.SourceOverrideTime = 0;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0f;
            actor.SourceTarget = -1; actor.SourceJob = SourceActorJob.None; actor.NoCatchTicks = 0;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 25f - 150f, 0f, 130f));
        }
        m.Players[3].SourceControllerStation = 0; m.SourceStationPlayers[0] = 3;
        m.Players[10].SourceControllerStation = 2; m.SourceStationPlayers[2] = 10;
        ActorState catcher = m.Players[4], runner = m.Players[5];
        catcher.SourceMode = SourceActorMode.DefensiveBack; catcher.Role = ActorRole.Defender;
        runner.SourceJob = SourceActorJob.Block; runner.SourceTarget = 9;
        m.SourceDrones[5] = m.SourceDrones[5].WithTarget(9);
        SourcePlayerDriver.SetSourcePosition(catcher, default(SourceVector3));
        SourcePlayerDriver.SetSourcePosition(runner, new SourceVector3(-60f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(m.Players[9], new SourceVector3(40f, 0f, 0f));
        SourceBallDriver.SetPosition(m, default(SourceVector3)); SourceBallDriver.SetTarget(m, 0f, 0f);
        Cache(game);
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            SourceVector3 focus = game.SourceBallWorldPosition;
            var eye = new SourceVector3(focus.X, 85f, focus.Z - 120f);
            SourceCinematicTransform frame = SourceCinematicTransform.FromState(
                new SourceCinematicSeed(eye, focus, 150f, 0f, -.4f));
            game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512f, 384f), tick);
            return 0;
        };
        var random = (SourceInjectedRandom)game.SourceRandomService;
        int before = random.Trace.Count;
        Wake(game);
        File.WriteAllText(Path.Combine(output, "public-Step-recovery-speed.json"), JsonSerializer.Serialize(new {
            sourceTick = m.SourceTick, m.CarrierId, m.Possession,
            flags = m.Players.Select(a => a.SourceRawFlags).ToArray(),
            runnerVelocity = runner.SourceVelocity, runnerMode = runner.SourceMode,
            events = game.SourceEvents, draws = random.Trace.Skip(before).Select(d => new {
                d.Raw, d.Value, d.Site.SourceIdentity, d.Site.ActorId, d.Site.CallIndex }),
            boundary = "declared live loose-ball frame -> real public Step; no direct catch/AI/movement dispatch"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(m.SourceTick == 1 && m.CarrierId == 4 && m.Possession == TeamId.Home,
            "public Step performs real defender recovery before later offensive runner");
        Check(m.Players.All(a => (a.SourceRawFlags & (int)SourceMovementFlags.SpeedOverride) == 0),
            "public Step recovery clears original speed override on all fourteen actors");
        Check(runner.SourceMode == SourceActorMode.Receiver && Bits(runner.SourceVelocity.X) == Bits(1.15f)
            && runner.SourceVelocity.Z == 0f,
            "public Step later new-offense runner uses original1.15, not inherited1.85");
        Check(random.Trace.Count == before, "public Step isolated loose recovery has no extra RNG");
    }

    private static void CatchControlCacheCase(string executable, int side, string kind)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away), new SourceStationConfiguration(3, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First);
        GameSimulation game = LiveDroneFixture(match: configuration);
        SimulationState m = game.Model;
        int catcherId = side * 7 + 4, first = side * 7 + 3, second = side * 7 + 5;
        ActorState catcher = m.Players[catcherId];
        m.Possession = (TeamId)(1 - side); m.SourceLastPossession = 1 - side;
        m.SourceRules = new SourceRulesState(1 - side, 1 - side, 1, 30, 60, SourcePlayType.Normal);
        m.SourceFlags = SourceGameFlags.BallCrossedLine | SourceGameFlags.OverLineSpeedsSet;
        m.CarrierId = -1; m.BallMode = BallMode.InFlight; m.BallKind = SourceBallKind.Fumble;
        m.PassTicksElapsed = 31; m.PassTicksTotal = 0; m.TargetPlayerId = -1;
        m.ThrowerId = (1 - side) * 7 + 3;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceRawFlags = (int)SourceMovementFlags.NoCollision;
            actor.SourceMode = actor.Team == m.Possession ? SourceActorMode.Receiver : SourceActorMode.DefensiveBack;
            actor.Role = actor.Team == m.Possession ? ActorRole.Receiver : ActorRole.Defender;
            actor.NoCatchTicks = 0;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 15f - 100f, 0f, 130f));
        }
        for (int station = 0; station < 4; station++)
        {
            int id = station / 2 * 7 + (station % 2 == 0 ? 3 : 5);
            m.SourceStationPlayers[station] = id; m.Players[id].SourceControllerStation = station;
        }
        SourcePlayerDriver.SetSourcePosition(catcher, default(SourceVector3));
        SourcePlayerDriver.SetSourcePosition(m.Players[first], kind == "airborne-distance"
            ? new SourceVector3(20f, 60f, 0f) : new SourceVector3(kind == "prior-detector" ? 29.5f : 30f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(m.Players[second], kind == "equal-distance"
            ? new SourceVector3(0f, 0f, 30f) : new SourceVector3(30f, 0f, 0f));
        SourceBallDriver.SetPosition(m, default(SourceVector3));
        Cache(game); m.SourceCameraHeading = 0;
        Bind<Action>(game, "SourceContacts")();
        float distanceFirst = m.SourceDistances[catcherId, first], distanceSecond = m.SourceDistances[catcherId, second];
        int expectedPlayer;
        using (var original = new LiveDroneOriginal(executable))
        {
            var command = new List<object> { "farthest", catcherId };
            foreach (ActorState actor in m.Players)
            { command.Add(actor.SourceControllerStation); command.Add(m.SourceDistances[catcherId, actor.Id]); }
            original.Send(command.ToArray()); expectedPlayer = int.Parse(original.ReadReply()[1]);
        }
        int expectedStation = m.Players[expectedPlayer].SourceControllerStation;
        if (kind == "prior-detector")
        {
            m.Players[first].SourceVelocity = new FieldVector(1.15f, 0f);
            Bind<Action<ActorState>>(game, "IntegrateSourceMovementPlayer")(m.Players[first]);
        }
        var random = (SourceInjectedRandom)game.SourceRandomService;
        int before = random.Trace.Count;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", catcherId, catcherId + 1);
        Bind<Action<ActorState>>(game, "TrySourceCatch")(catcher);
        SourceGameEvent[] control = LiveDroneSourceEvents(game).Where(e => e.Type == SourceEventType.ControllerChanged).ToArray();
        string name = "catch-control-side" + side + "-" + kind;
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
            name, sourceTick = m.SourceTick, catcherId, expectedPlayer, expectedStation,
            actualStation = catcher.SourceControllerStation, distanceFirst, distanceSecond,
            currentFirst = SourcePlayerDriver.WorldPosition(m.Players[first]),
            currentSecond = SourcePlayerDriver.WorldPosition(m.Players[second]),
            sourceEvents = LiveDroneSourceEvents(game), rngDraws = random.Trace.Count - before
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(m.CarrierId == catcherId && (int)m.Possession == side, name + ": real loose-ball catch");
        Check(catcher.SourceControllerStation == expectedStation, name + ": original cached farthest controller");
        Check(control.Length == 1 && control[0].StationId == expectedStation
            && control[0].ActorPlayerId == catcherId && control[0].TargetPlayerId == expectedPlayer,
            name + ": exact immediate controller-transfer event");
        Check(m.SourceStationPlayers[expectedStation] == catcherId
            && m.Players[expectedPlayer].SourceControllerStation == -1, name + ": exclusive transferred ownership");
        Check(random.Trace.Count == before, name + ": original controller selection uses zero RNG");
        Check(m.SourceDistances[catcherId, first] == distanceFirst
            && m.SourceDistances[catcherId, second] == distanceSecond, name + ": original detector cache retained");
    }

    private static void PossessionRecoveryCase(string executable, int side, string kind)
    {
        GameSimulation game = LiveDroneFixture();
        SimulationState m = game.Model;
        bool sameTeam = kind == "same-team", eligible = kind != "too-early";
        int recovering = sameTeam ? 1 - side : side, previous = 1 - side;
        int catcherId = recovering * 7 + 4, runnerId = recovering * 7 + 5;
        string name = "recovery-side" + side + "-" + kind;
        ActorState catcher = m.Players[catcherId], runner = m.Players[runnerId];
        m.Possession = (TeamId)previous; m.SourceLastPossession = previous;
        m.SourceRules = new SourceRulesState(previous, previous, 1, 30, 60, SourcePlayType.Normal);
        m.SourceFlags = SourceGameFlags.BallCrossedLine | SourceGameFlags.OverLineSpeedsSet;
        m.CarrierId = -1; m.BallMode = BallMode.InFlight;
        m.BallKind = kind == "bounced-lateral" ? SourceBallKind.Lateral : SourceBallKind.Fumble;
        m.SourceBallFlags = SourceBallFlags.Bounce;
        m.PassTicksElapsed = eligible ? 31 : 30; m.PassTicksTotal = 0;
        m.ThrowerId = previous * 7 + 3; m.TargetPlayerId = -1;
        m.Players[3].SourceControllerStation = 0; m.SourceStationPlayers[0] = 3;
        m.Players[10].SourceControllerStation = 2; m.SourceStationPlayers[2] = 10;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = actor.Team == m.Possession ? SourceActorMode.Receiver : SourceActorMode.DefensiveBack;
            actor.Role = actor.Team == m.Possession ? ActorRole.Receiver : ActorRole.Defender;
            actor.SourceRawFlags = (int)(SourceMovementFlags.OnScreen | SourceMovementFlags.SpeedOverride);
            actor.SourceOverrideSpeed = 1.85f; actor.SourceOverrideTime = 0;
            actor.NoCatchTicks = 0;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 15f - 100f, 0f, 100f));
        }
        SourcePlayerDriver.SetSourcePosition(catcher, new SourceVector3(0f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(runner, new SourceVector3(-20f, 0f, 80f));
        SourceBallDriver.SetPosition(m, SourcePlayerDriver.WorldPosition(catcher));
        Cache(game); m.SourceCameraHeading = 0;
        var random = (SourceInjectedRandom)game.SourceRandomService;
        int drawsBefore = random.Trace.Count;
        int[] beforeFlags = m.Players.Select(a => a.SourceRawFlags).ToArray();
        int[] beforeModes = m.Players.Select(a => (int)a.SourceMode).ToArray();
        string[] expected;
        using (var original = new LiveDroneOriginal(executable))
        {
            var command = new List<object> { "recovery", recovering, eligible && !sameTeam ? 1 : 0 };
            for (int id = 0; id < 14; id++) { command.Add(beforeFlags[id]); command.Add(beforeModes[id]); }
            original.Send(command.ToArray()); expected = original.ReadReply();
        }
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", catcherId, catcherId + 1);
        Bind<Action<ActorState>>(game, "TrySourceCatch")(catcher);
        int[] afterFlags = m.Players.Select(a => a.SourceRawFlags).ToArray();
        int[] afterModes = m.Players.Select(a => (int)a.SourceMode).ToArray();
        if (eligible)
        {
            runner.SourceStick = 21; runner.SourcePreviousStick = 24;
            runner.SourceButtons = SourceDroneButtons.None; runner.Turbo = false;
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", runnerId, runnerId + 1);
            Bind<Func<ActorState, bool, SourceMovementResult>>(game, "RunSourceMovement")(runner, false);
        }
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
            name, sourceTick = m.SourceTick, catcherId, runnerId, beforeFlags, beforeModes,
            expectedOriginal = expected, afterFlags, afterModes, m.CarrierId, m.Possession,
            runnerVelocity = runner.SourceVelocity, sourceEvents = LiveDroneSourceEvents(game),
            draws = random.Trace.Skip(drawsBefore).Select(d => new { d.Raw, d.Value, d.Site.SourceIdentity }),
            full_gameplay_parity_claimed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(m.CarrierId == (eligible ? catcherId : -1) && (int)m.Possession == (eligible ? recovering : previous),
            name + ": actual deterministic loose-ball catch preserves eligibility/possession");
        for (int id = 0; id < 14; id++)
        {
            Check(afterFlags[id] == int.Parse(expected[1 + id * 2]), name + ": original actor speed-latch flags " + id);
            Check(afterModes[id] == int.Parse(expected[2 + id * 2]), name + ": original actor mode " + id);
        }
        Check(random.Trace.Count == drawsBefore, name + ": catch/next ordinary movement consume no RNG");
        if (eligible)
            Check(Bits(runner.SourceVelocity.X) == Bits(sameTeam ? 1.85f : 1.15f)
                && runner.SourceVelocity.Z == 0f,
                name + ": next real ordinary runner uses cleared-latch source speed");
    }

    private const string ThrowMonitorOriginalBuilder = @"
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
root=Path.cwd()
sys.path.insert(0,str(root/'tests/playbooks'))
import oracle
source,output=map(lambda x:Path(x).resolve(),sys.argv[1:])
documents=oracle.verify_inputs(source)
player=documents['player.c'].decode('latin1')
monitor=oracle.body(player,'qb_throw_monitor')
release=oracle.body(player,'qb_release_ball')
lateral=oracle.body(player,'lateral_ball')
qb_mode=oracle.body(player,'_mode_qb')
special=qb_mode[qb_mode.index('// allow qb to throw ball from hurdle'):qb_mode.index('// TEMP',qb_mode.index('// allow qb to throw ball from hurdle'))]
if 'BF_TURBO_PASS' in oracle.strip_comments(special): raise RuntimeError('Original special branch unexpectedly writes turbo flag')
vels=oracle.body(player,'set_ball_vels')
speed_begin=vels.index('if ( ((ppdata->station >= 0)')
speed_end=vels.index('throw_joint =',speed_begin)
speed_choice=vels[speed_begin:speed_end]
mode_assignment='pobj->plyrmode = MODE_WPN;'
if lateral.count(mode_assignment)!=1: raise RuntimeError('Expected original lateral actor-mode assignment')
begin=release.index('if ((ISHUMAN(ppdata)) && (target_human))')
end=release.index('#if 1',begin)
override=release[begin:end]
decl=monitor[monitor.index('{')+1:monitor.index('target_human = FALSE;')]
tail=monitor[monitor.index('last_pass_hist[0]'):monitor.rfind('}')]
header=documents['include/player.h'].decode('latin1')
mode=re.search(r'^#define\s+MODE_WPN\s+.*$',header,re.M)[0]
human=re.search(r'^#define ISHUMAN\(x\).*$',header,re.M)[0]
drone=re.search(r'^#define ISDRONE\(x\).*$',header,re.M)[0]
unit=r'''#include <stdio.h>
#include <stdlib.h>
#include <setjmp.h>
#include <string.h>
#define FALSE 0
#define TRUE 1
#define P_A 1
#define P_B 2
#define P_ABCD_MASK 15
typedef struct { int plyrmode; } obj_3d;
typedef struct { obj_3d odata; int station,team,plyrnum,throw_target,but_hist[5]; } fbplyr_data;
static fbplyr_data player_blocks[14];
static int target_human,last_pass_hist[5],current_switches,sleep_ticks;
static jmp_buf stopped;
static int get_player_sw_current(void){return current_switches;}
static void sleep(int ticks){sleep_ticks=ticks;longjmp(stopped,1);}
static void die(int value){(void)value;longjmp(stopped,2);}
'''
unit+='\n'+mode+'\n'+human+'\n'+drone+'\n'+monitor
for name in ('BALL_VELOCITY','BALL_YARDS','BALL_YMAX','TIME_MAX','TBALL_VELOCITY','TBALL_YMAX',
             'TTIME_MAX','FBALL_YARDS','FBALL_VELOCITY','FBALL_YMAX','FTIME_MAX'):
    unit+='\n'+re.search(r'^#define\s+'+name+r'\s+.*$',player,re.M)[0]
unit+='\n#define GRAVITY -0.08f\n#define YD2UN 7.8f\n'
unit+='\n'+re.search(r'^#define\s+BF_TURBO_PASS\s+.*$',header,re.M)[0]+'\n'
unit+='\nstatic float special_speed(int flags){float ball_velocity,ball_ymax,time_max;int pup_fastpass=0;'
unit+='struct {int team_fire[2];} game_info={{0,0}};struct {int flags;} ball_obj={flags};fbplyr_data *ppdata=player_blocks+3;\n'+speed_choice+'\nreturn ball_velocity;}\n'
unit+='\nstatic void monitor_tail(int *args){'+decl+tail+'}\n'
unit+='\nstatic void release_target(fbplyr_data *ppdata){int i;obj_3d *ptgt,*pobj=(obj_3d*)ppdata;\n'+override+'\n}\n'
unit+='\nstatic void lateral_tail(obj_3d *pobj){'+mode_assignment+'}\n'
unit+=r'''int main(int argc,char **argv){
 if(argc==2&&!strcmp(argv[1],""lateral"")){obj_3d value={5};lateral_tail(&value);printf(""%d\n"",value.plyrmode);return 0;}
 if(argc==3&&!strcmp(argv[1],""special-speed"")){printf(""%.9g\n"",special_speed(atoi(argv[2])));return 0;}
 if(argc!=13)return 2;
 int side=atoi(argv[1]),actor=side*7+3,args[1]={actor},first;
 for(int i=0;i<14;i++){player_blocks[i].station=-1;player_blocks[i].team=i/7;player_blocks[i].plyrnum=i;player_blocks[i].odata.plyrmode=MODE_WPN;}
 player_blocks[actor].station=atoi(argv[2]);target_human=1;
 first=setjmp(stopped);if(first==0)qb_throw_monitor(args);
 int first_flag=target_human;
 player_blocks[actor].station=atoi(argv[3]);
 for(int i=0;i<5;i++)player_blocks[actor].but_hist[i]=atoi(argv[5+i]);
 current_switches=atoi(argv[4])<<(atoi(argv[3])<0?0:atoi(argv[3])*8);
 int second=setjmp(stopped);if(first==1&&second==0)monitor_tail(args);
 player_blocks[actor].throw_target=atoi(argv[10]);
 player_blocks[side*7+5].odata.plyrmode=atoi(argv[11]);player_blocks[side*7+5].station=atoi(argv[12]);
 release_target(player_blocks+actor);
 printf(""M %d %d %d %d %d"",first,sleep_ticks,first_flag,target_human,player_blocks[actor].throw_target);
 for(int i=0;i<5;i++)printf("" %d"",last_pass_hist[i]);puts("""");return 0;
}
'''
output.mkdir(parents=True,exist_ok=True)
(output/'monitor-unit.c').write_text(unit)
command=['gcc','-std=c99','-O0','-fno-fast-math',str(output/'monitor-unit.c'),'-o',str(output/'monitor-oracle')]
p=subprocess.run(command,capture_output=True,text=True,env=os.environ)
(output/'compiler.log').write_text(p.stdout+p.stderr)
if p.returncode:raise RuntimeError(p.stderr)
receipt={'source_revision':oracle.PINS['revision'],'player_blob_sha1':oracle.blob_sha1(documents['player.c']),
    'monitor_sha256':hashlib.sha256(monitor.encode('latin1')).hexdigest(),
    'tail_sha256':hashlib.sha256(tail.encode('latin1')).hexdigest(),
    'release_override_sha256':hashlib.sha256(override.encode('latin1')).hexdigest(),
    'lateral_mode_assignment_sha256':hashlib.sha256(mode_assignment.encode('latin1')).hexdigest(),
    'special_throw_branch_sha256':hashlib.sha256(special.encode('latin1')).hexdigest(),
    'set_ball_vels_speed_choice_sha256':hashlib.sha256(speed_choice.encode('latin1')).hexdigest(),
    'expression_rewrites':0,'raw_button_boundary':'logical original A/B bits1/2 packed in source station byte',
    'sleep_boundary':'original first entry yields via setjmp; original post-sleep block resumes with same actor argument',
    'native_target_stack_or_scheduler_executed':False,'random_calls':0}
(output/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
";

    private static PlayerCommand ThrowMonitorInput(int station, SourceButtons held, int stickZ = 0)
    {
        var stations = new SourceStationInput[4];
        for (int id = 0; id < 4; id++)
            stations[id] = new SourceStationInput(id, stickZ: id == station ? stickZ : 0,
                held: id == station ? held : SourceButtons.None,
                digitalCurrent: id != station || stickZ == 0 ? SourceDigitalDirections.None
                    : stickZ < 0 ? SourceDigitalDirections.Down : SourceDigitalDirections.Up);
        return new PlayerCommand(stationInputs: new SourceInputFrame(stations, new SourcePhysicalSwitchFrame(stations)));
    }

    private static GameSimulation ThrowMonitorFixture(int side, string pattern)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away), new SourceStationConfiguration(3, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First);
        GameSimulation game = LiveDroneFixture(match: configuration);
        SimulationState m = game.Model;
        m.SourceTick = m.SourceProcesses.SourceTick;
        m.Possession = (TeamId)side; m.SourceLastPossession = side; m.ClockActive = true;
        m.SourceRules = new SourceRulesState(side, side, 1, 30, 60, SourcePlayType.Normal);
        m.SourceFlags = SourceGameFlags.None; m.CarrierId = side * 7 + 3;
        m.BallMode = BallMode.Held; m.BallKind = SourceBallKind.Pass; m.TargetPlayerId = -1;
        m.PassTicksElapsed = m.PassTicksTotal = 0; m.SourceBallFlags = SourceBallFlags.None;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead; actor.SourceControllerStation = -1;
            actor.SourceRawFlags = (int)SourceMovementFlags.NoCollision;
            actor.SourceButtons = actor.SourceOldButtons = SourceDroneButtons.None;
            actor.SourceJob = SourceActorJob.None; actor.SourceTarget = -1;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0;
            actor.NoCatchTicks = 0; actor.PendingThrowTarget = -1;
            Array.Clear(actor.SourceButtonHistory, 0, actor.SourceButtonHistory.Length);
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 25f - 150f, 0f, 130f));
        }
        for (int station = 0; station < 4; station++)
        {
            int id = station / 2 * 7 + (station % 2 == 0 ? 3 : 5);
            m.SourceStationPlayers[station] = id; m.Players[id].SourceControllerStation = station;
        }
        float direction = side == 0 ? 1f : -1f;
        for (int index = 3; index <= 6; index++)
        {
            ActorState actor = m.Players[side * 7 + index];
            actor.SourceMode = index == 3 ? SourceActorMode.Quarterback : SourceActorMode.Receiver;
            actor.Role = index == 3 ? ActorRole.Quarterback : ActorRole.Receiver;
            SourceRouteState drone = m.SourceDrones[actor.Id]; drone.ThinkTicks = 100; m.SourceDrones[actor.Id] = drone;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(
                (index == 3 ? -200f : index == 4 ? -110f : index == 5 ? -90f : -80f) * direction,
                0f, index == 5 ? 50f : index == 6 ? -50f : 0f));
        }
        ActorState qb = m.Players[m.CarrierId];
        qb.Facing = qb.TargetFacing = qb.UpperFacing = qb.TargetUpperFacing = side == 0 ? 256 : 768;
        if (pattern == "old-A" || pattern == "recent-A")
        {
            qb.SourceButtons = qb.SourceOldButtons = SourceDroneButtons.A;
            if (pattern == "old-A")
                for (int i = 0; i < qb.SourceButtonHistory.Length; i++) qb.SourceButtonHistory[i] = SourceDroneButtons.A;
        }
        typeof(GameSimulation).GetField("sourceTargetHuman",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(game, true);
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", qb.Id, qb.Id + 1);
        qb.AnimationState.FullBody("qb_stance_anim", qb, m.SourceTick, Bind<Func<int, int>>(game, "SourceDraw"),
            AnimationSelectionEvidence.SourceVerified);
        SourceBallDriver.SetPosition(m, SourcePlayerDriver.WorldPosition(qb));
        Cache(game);
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            SourceVector3 focus = game.SourceBallWorldPosition;
            var eye = new SourceVector3(focus.X, 85f, focus.Z - 120f);
            SourceCinematicTransform frame = SourceCinematicTransform.FromState(
                new SourceCinematicSeed(eye, focus, 150f, 0f, -.4f));
            game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512f, 384f), tick);
            return 0;
        };
        return game;
    }

    private static void ThrowMonitorChecks(string originalRoot)
    {
        string worker = Path.Combine(output, "throw-monitor-original.py");
        File.WriteAllText(worker, ThrowMonitorOriginalBuilder);
        string native = Path.Combine(output, "original-throw-monitor");
        RunHeadingProcess("python3", new[] { "-B", worker, originalRoot, native }, "throw-monitor-C-build.log");
        var failures = new List<string>();
        foreach (int side in new[] { 0, 1 })
            foreach (string pattern in new[] { "fresh-A", "no-A", "A-only", "stale-A", "old-A", "recent-A" })
            {
                try { ThrowMonitorCase(Path.Combine(native, "monitor-oracle"), side, pattern); }
                catch (ContactAssertion error) { failures.Add(error.Message); }
            }
        foreach (int side in new[] { 0, 1 })
        {
            try { LateralActorModeCase(Path.Combine(native, "monitor-oracle"), side); }
            catch (ContactAssertion error) { failures.Add(error.Message); }
        }
        Check(failures.Count == 0, string.Join("; ", failures));
    }

    private static void LateralActorModeCase(string executable, int side)
    {
        GameSimulation game = ThrowMonitorFixture(side, "no-A");
        SimulationState m = game.Model;
        int qbId = side * 7 + 3, target = side * 7 + 5;
        SourcePlayerDriver.SetSourcePosition(m.Players[target],
            new SourceVector3(side == 0 ? -230f : 230f, 0f, 0f));
        Cache(game);
        SourceGameEvent released = default(SourceGameEvent);
        for (int wake = 0; wake < 50 && !released.IsSpecified; wake++)
        {
            Wake(game, ThrowMonitorInput(side * 2, SourceButtons.B, -3));
            released = game.SourceEvents.FirstOrDefault(e => e.Type == SourceEventType.LateralReleased && e.ActorPlayerId == qbId);
        }
        int expected = int.Parse(RunHeadingProcess(executable, new[] { "lateral" }, "lateral-mode-C.log").Trim());
        File.WriteAllText(Path.Combine(output, "public-lateral-mode-side" + side + ".json"), JsonSerializer.Serialize(new {
            side, sourceTick = m.SourceTick, released, actualMode = (int)m.Players[qbId].SourceMode,
            expectedMode = expected, m.ThrowerId, m.TargetPlayerId, m.CarrierId,
            boundary = "declared live QB -> actual public rear-stick B -> original lateral animation callback"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(released.IsSpecified && released.TargetPlayerId == target, "lateral-side" + side + ": actual public QB lateral release");
        Check((int)m.Players[qbId].SourceMode == expected, "lateral-side" + side + ": original release switches QB actor to weapon");
        Check(m.ThrowerId == qbId && m.TargetPlayerId == target && m.CarrierId == -1,
            "lateral-side" + side + ": original loose-ball ownership at release");
    }

    private static void ThrowMonitorCase(string executable, int side, string pattern)
    {
        GameSimulation game = ThrowMonitorFixture(side, pattern);
        SimulationState m = game.Model;
        int qbId = side * 7 + 3, station = side * 2, intent = side * 7 + 4;
        var wakes = new List<object>();
        SourceProcessView born = default(SourceProcessView), first = default(SourceProcessView);
        bool firstFlag = true;
        SourceDroneButtons[] history = null;
        SourceGameEvent released = default(SourceGameEvent);
        SourceButtons secondButtons = pattern == "fresh-A" || pattern == "old-A" || pattern == "recent-A"
            ? SourceButtons.A | SourceButtons.B : pattern == "A-only" ? SourceButtons.A : SourceButtons.B;
        for (int wake = 1; wake <= 50 && !released.IsSpecified; wake++)
        {
            SourceButtons buttons = wake == 1 ? pattern == "old-A" || pattern == "recent-A"
                    ? SourceButtons.A | SourceButtons.B : SourceButtons.B
                : wake == 2 ? pattern == "stale-A" || pattern == "old-A" || pattern == "recent-A"
                    ? SourceButtons.A | SourceButtons.B : SourceButtons.B
                : wake == 3 ? secondButtons : SourceButtons.B;
            if (wake == 3) history = m.Players[qbId].SourceButtonHistory.Take(5).ToArray();
            Wake(game, ThrowMonitorInput(station, buttons));
            SourceProcessView process = m.SourceProcesses.Processes.FirstOrDefault(p => p.Function == "qb_throw_monitor");
            if (wake == 1) born = process;
            if (wake == 2)
            {
                first = process;
                firstFlag = (bool)typeof(GameSimulation).GetField("sourceTargetHuman",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(game);
            }
            wakes.Add(new { sourceTick = m.SourceTick, buttons = (int)buttons,
                target = m.Players[qbId].PendingThrowTarget, process });
            released = game.SourceEvents.FirstOrDefault(e => e.Type == SourceEventType.PassReleased && e.ActorPlayerId == qbId);
        }
        if (history == null) throw new InvalidOperationException("The selected original throw released before the declared monitor's second wake.");
        var arguments = new List<string> { side.ToString(), station.ToString(), station.ToString(), ((int)secondButtons).ToString() };
        arguments.AddRange(history.Select(v => ((int)v).ToString()));
        arguments.AddRange(new[] { intent.ToString(), ((int)SourceActorMode.Receiver).ToString(), (station + 1).ToString() });
        string[] native = RunHeadingProcess(executable, arguments, "throw-monitor-side" + side + "-" + pattern + ".log")
            .Trim().Split(' ');
        SourceDroneButtons[] copied = (SourceDroneButtons[])typeof(GameSimulation).GetField("sourceLastPassHistory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(game);
        string name = "throw-monitor-side" + side + "-" + pattern;
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
            name, native, history = history.Select(v => (int)v), copied = copied.Select(v => (int)v), born, first, firstFlag,
            released, wakes, finalProcesses = m.SourceProcesses.Processes,
            boundary = "declared live QB/previous target flag -> public raw B/ABC Steps -> actual animation release"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(released.IsSpecified && released.ActorPlayerId == qbId, name + ": actual public throw releases");
        Check(released.TargetPlayerId == int.Parse(native[5]), name + ": original late human-target override");
        Check(born.Serial != 0 && born.Id == 0x0591 && born.BirthTick == 1 && born.RunLevel == 0,
            name + ": actual request admits original monitor process");
        Check(first.Serial != 0 && first.LastWakeTick == 2 && first.SleepRemaining == 1
            && native[1] == "1" && native[2] == "1" && !firstFlag && native[3] == "0",
            name + ": first real monitor wake clears target and sleeps one");
        Check(!m.SourceProcesses.Processes.Any(p => p.Function == "qb_throw_monitor"),
            name + ": monitor terminates after original second-wake decision");
        Check((native[4] == "1") == (pattern == "fresh-A" || pattern == "recent-A"),
            name + ": native three-history A guard");
        Check(copied.Select(v => (int)v).SequenceEqual(native.Skip(6).Select(int.Parse)),
            name + ": actual child copies original five history slots");
    }

    private static readonly Dictionary<string, float[]> specialThrowObservations = new Dictionary<string, float[]>();

    private static void SpecialThrowChecks(string originalRoot)
    {
        specialThrowObservations.Clear();
        string worker = Path.Combine(output, "special-throw-original.py");
        File.WriteAllText(worker, ThrowMonitorOriginalBuilder);
        string native = Path.Combine(output, "original-special-throw");
        RunHeadingProcess("python3", new[] { "-B", worker, originalRoot, native }, "special-throw-C-build.log");
        var failures = new List<string>();
        foreach (int side in new[] { 0, 1 })
            foreach (bool previousTurbo in new[] { false, true })
                foreach (bool heldTurbo in new[] { false, true })
                {
                    try { SpecialThrowCase(Path.Combine(native, "monitor-oracle"), side, previousTurbo, heldTurbo); }
                    catch (ContactAssertion error) { failures.Add(error.Message); }
                }
        foreach (int side in new[] { 0, 1 })
            foreach (bool previousTurbo in new[] { false, true })
            {
                string key = side + "/" + previousTurbo + "/";
                try
                {
                    float[] off = specialThrowObservations[key + false], on = specialThrowObservations[key + true];
                    Check(off.Select(Bits).SequenceEqual(on.Select(Bits)),
                        "special-side" + side + "-old" + previousTurbo + ": ignored live C preserves exact release origin/velocity/time");
                }
                catch (ContactAssertion error) { failures.Add(error.Message); }
            }
        Check(failures.Count == 0, string.Join("; ", failures));
    }

    private static void SpecialThrowCase(string executable, int side, bool previousTurbo, bool heldTurbo)
    {
        GameSimulation game = ThrowMonitorFixture(side, "no-A");
        SimulationState m = game.Model;
        int qbId = side * 7 + 3;
        ActorState qb = m.Players[qbId];
        m.SourceBallFlags = previousTurbo ? SourceBallFlags.TurboPass : SourceBallFlags.None;
        Wake(game, ThrowMonitorInput(side * 2, SourceButtons.A));
        string hurdle = qb.AnimationState.Primary.SequenceName;
        Wake(game, ThrowMonitorInput(side * 2, SourceButtons.B | (heldTurbo ? SourceButtons.C : 0)));
        bool captured = qb.SourceCapturedControlBlock, pendingTurbo = qb.PendingThrowTurbo;
        SourceGameEvent released = default(SourceGameEvent);
        for (int wake = 0; wake < 50 && !released.IsSpecified; wake++)
        {
            Wake(game, ThrowMonitorInput(side * 2, SourceButtons.B | (heldTurbo ? SourceButtons.C : 0)));
            released = game.SourceEvents.FirstOrDefault(e => e.Type == SourceEventType.PassReleased && e.ActorPlayerId == qbId);
        }
        float speed = float.Parse(RunHeadingProcess(executable, new[] { "special-speed",
            previousTurbo ? ((int)SourceBallFlags.TurboPass).ToString() : "0" }, "special-speed-C.log"),
            System.Globalization.CultureInfo.InvariantCulture);
        specialThrowObservations[side + "/" + previousTurbo + "/" + heldTurbo] = new[] {
            m.SourceBallOrigin.X, m.SourceBallOrigin.Y, m.SourceBallOrigin.Z, m.SourceBallVelocity.X,
            m.SourceBallVerticalVelocity, m.SourceBallVelocity.Z, (float)m.PassTicksTotal };
        string name = "special-side" + side + "-old" + previousTurbo + "-held" + heldTurbo;
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
            name, sourceTick = m.SourceTick, hurdle, captured, pendingTurbo, previousTurbo, heldTurbo,
            expectedSourceSpeed = speed, actualFlags = (int)m.SourceBallFlags,
            actualVelocity = m.SourceBallVelocity, released,
            boundary = "declared live QB -> actual public A/hurdle -> subsequent B/C special throw -> original release"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(hurdle == "HURDLE" && !captured, name + ": actual special uninterruptible throw caller reached");
        Check(released.IsSpecified && released.SourceTick == 9 && released.TargetPlayerId == side * 7 + 4,
            name + ": actual original throw releases at source9");
        Check(pendingTurbo == previousTurbo, name + ": special caller retains prior ball turbo flag");
        Check(((m.SourceBallFlags & SourceBallFlags.TurboPass) != 0) == previousTurbo
            && speed == (previousTurbo ? 5.70f : 5.50f), name + ": original release speed-selection flag");
    }

    private static void DiverBridgeChecks(string originalRoot)
    {
        string worker = Path.Combine(output, "diver-original.py");
        File.WriteAllText(worker, PossessionRecoveryOriginalBuilder);
        string native = Path.Combine(output, "original-diver");
        RunHeadingProcess("python3", new[] { "-B", worker, originalRoot, native }, "diver-C-build.log");
        var failures = new List<string>();
        foreach (int side in new[] { 0, 1 })
            foreach (float distance in new[] { 20f, 40f, 40.000004f, 200f })
            {
            try { DiverBridgeCase(Path.Combine(native, "source-oracle"), side, distance); }
            catch (ContactAssertion error) { failures.Add(error.Message); }
            }
        Check(failures.Count == 0, string.Join("; ", failures));
    }

    private static void DiverBridgeCase(string executable, int side, float distance)
    {
        GameSimulation game = LiveDroneFixture();
        SimulationState m = game.Model;
        int qbId = side * 7 + 3, diverId = (1 - side) * 7 + 4;
        m.Possession = (TeamId)side; m.CarrierId = qbId; m.TargetPlayerId = qbId;
        m.SourceRules = new SourceRulesState(side, side, 1, 30, 60, SourcePlayType.Normal);
        ActorState qb = m.Players[qbId], diver = m.Players[diverId];
        qb.SourceMode = SourceActorMode.Quarterback; qb.Role = ActorRole.Quarterback;
        diver.SourceMode = SourceActorMode.DefensiveBack; diver.Role = ActorRole.Defender;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceRawFlags |= (int)SourceMovementFlags.NoCollision;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 25f - 150f, 0f, 130f));
        }
        SourcePlayerDriver.SetSourcePosition(qb, default(SourceVector3));
        SourcePlayerDriver.SetSourcePosition(diver, new SourceVector3(distance, 0f, 0f));
        diver.Facing = diver.TargetFacing = diver.UpperFacing = diver.TargetUpperFacing = 768;
        Cache(game);
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", diverId, diverId + 1);
        diver.AnimationState.FullBody("t_crzdiv_anim", diver, m.SourceTick, Bind<Func<int, int>>(game, "SourceDraw"),
            AnimationSelectionEvidence.SourceVerified);
        var random = (SourceInjectedRandom)game.SourceRandomService;
        int drawsBefore = random.Trace.Count;
        SourceAiFrame frame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
        int expected;
        using (var original = new LiveDroneOriginal(executable))
        {
            var command = new List<object> { "diver", qbId };
            foreach (ActorState actor in m.Players)
            {
            string sequence = actor.AnimationState.Primary.SequenceName;
            command.Add(sequence == "T_CRZDIV" ? 1 : sequence == "T_SPRHED" ? 2 : 0);
            command.Add(actor.PendingThrowTarget); command.Add(m.SourceDistances[qbId, actor.Id]);
            }
            original.Send(command.ToArray()); expected = int.Parse(original.ReadReply()[1]);
        }
        int actual = (int)typeof(SourceDroneAI).GetMethod("FindDiver",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { frame, qbId });
        string name = "diver-side" + side + "-distance" + distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
            name, qbId, diverId, distance, expected, actual,
            actualSequence = diver.AnimationState.Primary.SequenceName, actualTarget = diver.PendingThrowTarget,
            projectedTarget = frame.Actors[diverId].AttackTarget, projectedFlags = frame.Actors[diverId].Flags,
            diver.SourceAttackMode, extraRng = random.Trace.Count - drawsBefore,
            scope = "actual source dive script -> actual production AI frame -> unchanged original find_diver; undefined downstream evade_dir excluded"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(diver.AnimationState.Primary.SequenceName == "T_CRZDIV" || diver.AnimationState.Primary.SequenceName == "T_SPRHED",
            name + ": actual original dive branch selected without seed or script substitution");
        int expectedTarget = distance == 200f ? -1 : qbId;
        Check(diver.PendingThrowTarget == expectedTarget, name + ": actual original target acquisition");
        Check(frame.Actors[diverId].AttackTarget == expectedTarget, name + ": production bridge preserves throw_target");
        Check((frame.Actors[diverId].Flags & SourceAiActorFlags.DiveAttack) != 0,
            name + ": production bridge preserves active dive sequence");
        Check(actual == expected && expected == (distance <= 40f ? diverId : -1),
            name + ": unchanged original forty-unit diver boundary");
        Check(random.Trace.Count == drawsBefore, name + ": bridge and original classification consume zero RNG");
    }

    private const string MovementWindowOriginalBuilder = @"
import hashlib,json,os,sys
from pathlib import Path
from unittest.mock import patch
root=Path.cwd()
sys.path.insert(0,str(root/'tests/movement'))
import oracle
source,output=map(lambda x:Path(x).resolve(),sys.argv[1:])
driver_path=root/'tests/movement/driver.c'
original_read=Path.read_bytes
driver=original_read(driver_path)
old=b'        dump_state(p,allowed,human,resource);\n'
if driver.count(old)!=1: raise RuntimeError('Expected exact original test output boundary')
driver=driver.replace(old,old+b'        fflush(stdout);\n')
old=b'        reset();fbplyr_data *p=&player_blocks[3];obj_3d *o=&p->odata;\n'
if driver.count(old)!=1: raise RuntimeError('Expected exact original test command boundary')
driver=driver.replace(old,b'        if(!strncmp(line,""quit"",4))break;\n'+old)
class CallerOracle(oracle.Oracle):
    def __init__(self):
        self.artifacts=output
        self.artifacts.mkdir(parents=True,exist_ok=True)
        self.sources={}
        for name,pin in oracle.PINS['blobs'].items():
            raw=original_read(source/name)
            if oracle.blob_sha1(raw)!=pin: raise RuntimeError('Original movement source pin differs: '+name)
            self.sources[name]=raw
        self.selections=[];self.sites={}
        def adapted(path):
            return driver if path==driver_path else original_read(path)
        with patch.object(Path,'read_bytes',adapted): self.build()
native=CallerOracle()
(output/'caller-receipt.json').write_text(json.dumps({
    'source':oracle.PINS,'compiler':native.compiler,'selected_original_windows':native.selections,
    'test_transport_only_change':'fflush after JSON dump; explicit quit command',
    'original_function_expression_rewrites':0,
    'animation_boundary':'Existing explicit metadata-only original animation entry; observed managed side uses actual VM/public Step',
    'native_target_ABI_or_PRNG_claimed':False},indent=2)+'\n')
";

    private static GameSimulation MovementWindowFixture(int field = 0, bool friction = false)
    {
        GameSimulation game = real.Create(configuration: new SourceMatchConfiguration(
            SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(2, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First),
            randomFactory: seed => SourceRandom.CreatePortableLibrary(seed, "M1 remaining public caller cases; unchanged seed1"),
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            movementSettings: new SourceMovementSettings(field, 0, false, 0, 0, false));
        lastGame = game;
        SimulationState m = game.Model;
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall; m.ClockActive = true;
        m.Possession = TeamId.Home; m.SourceLastPossession = 0; m.CarrierId = 4;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 60, SourcePlayType.Normal);
        m.SourceFlags = SourceGameFlags.BallCrossedLine | SourceGameFlags.OverLineSpeedsSet;
        m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
        m.SourceDefensePlay = real.Playbook.GetPlay("play_safe_cover");
        m.BallMode = BallMode.Held; m.BallKind = SourceBallKind.Pass; m.TargetPlayerId = -1;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead; actor.SourceControllerStation = -1;
            actor.SourceRawFlags = (int)SourceMovementFlags.NoCollision;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = actor.SourceFriction = 0f;
            actor.SourceButtons = actor.SourceOldButtons = SourceDroneButtons.None;
            actor.SourceStick = actor.SourcePreviousStick = 24;
            actor.SourceJob = SourceActorJob.None; actor.SourceTarget = -1;
            actor.AnimationState.Primary.SetMovementMode(0); actor.AnimationState.Secondary.SetMovementMode(0);
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 25f - 150f, 0f, 130f));
        }
        m.Players[10].SourceControllerStation = 2; m.SourceStationPlayers[2] = 10;
        ActorState mover = m.Players[4];
        mover.SourceMode = SourceActorMode.Receiver; mover.Role = ActorRole.Receiver;
        mover.SourceControllerStation = 0; m.SourceStationPlayers[0] = m.ControlledPlayerId = 4;
        mover.Facing = mover.TargetFacing = mover.UpperFacing = mover.TargetUpperFacing = 256;
        SourcePlayerDriver.SetSourcePosition(mover, default(SourceVector3));
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 4, 5);
        mover.AnimationState.FullBody(friction ? "hurdle_anim" : "run_wb_anim", mover, 0,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        if (friction) { mover.SourceVelocity = new FieldVector(1.5f, .75f); mover.SourceFriction = .2f; }
        SourceBallDriver.SetPosition(m, new SourceVector3(0f, 14f, 0f)); Cache(game); m.SourceCameraHeading = 0;
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            SourceVector3 focus = game.SourceBallWorldPosition;
            var eye = new SourceVector3(focus.X, 85f, focus.Z - 120f);
            SourceCinematicTransform frame = SourceCinematicTransform.FromState(
                new SourceCinematicSeed(eye, focus, 150f, 0f, -.4f));
            game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512f, 384f), tick);
            return 0;
        };
        return game;
    }

    private static string MovementNativeCommand(SourceMovementState state, string operation,
        IDictionary<string, object> context, IEnumerable<int> raw)
    {
        var tokens = new List<string> { "Op=" + operation };
        foreach (var field in typeof(SourceMovementState).GetFields())
        {
            object value = field.GetValue(state);
            string text = value is float ? ((float)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : value is Enum ? Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (field.Name.EndsWith("Sequence", StringComparison.Ordinal) && string.IsNullOrEmpty(text)) text = "-";
            tokens.Add("s." + field.Name + "=" + text);
        }
        foreach (var item in context)
            tokens.Add("c." + item.Key + "=" + (item.Value is float
                ? ((float)item.Value).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : Convert.ToString(item.Value, System.Globalization.CultureInfo.InvariantCulture)));
        tokens.Add("Raw=" + string.Join(",", raw));
        return string.Join(" ", tokens);
    }

    private static void MovementWindowTick(LiveDroneOriginal native, GameSimulation game, string name,
        int ordinal, SourceButtons buttons, int field, int actorId = 4, int backpedalHeading = -1)
    {
        ActorState mover = game.Model.Players[actorId];
        SourceMovementState before = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(mover);
        float resource = game.Model.SourceStationTurbo[0];
        var context = new Dictionary<string, object> {
            { "OffenseSide", 0 }, { "Phase", 2 }, { "GameFlags", (int)game.Model.SourceFlags }, { "PlayType", 0 },
            { "Carrier", game.Model.CarrierId },
            { "CarrierMode", game.Model.CarrierId < 0 ? 0 : (int)game.Model.Players[game.Model.CarrierId].SourceMode },
            { "BallType", (int)GameSimulation.SourceBallTypeFor(game.Model.BallKind) },
            { "IntendedReceiver", game.Model.TargetPlayerId },
            { "CameraHeading", 0 }, { "LineOfScrimmage", 30 }, { "Handicap", mover.SourceHandicap },
            { "Field", field }, { "InputStick", ordinal }, { "InputButtons", (int)buttons },
            { "StationTurbo", resource }, { "StationActive", 1 }, { "StationControlled", 1 },
            { "StationHeld", (buttons & SourceButtons.C) != 0 ? 1 : 0 }, { "StationDefends", 0 } };
        if (backpedalHeading >= 0)
        {
            context["InputBackpedal"] = 1;
            context["BackpedalTargetHeading"] = backpedalHeading;
        }
        float expectedResource;
        using (var document = JsonDocument.Parse(native.ReadJson(MovementNativeCommand(before, "station", context, new int[0]))))
            expectedResource = BitConverter.Int32BitsToSingle(unchecked((int)document.RootElement.GetProperty("Turbo").GetUInt32()));
        context["InputTurbo"] = context["TeamIndexedTurbo"] = expectedResource;
        var stations = new[] {
            new SourceStationInput(0, 3 - ordinal % 7, 3 - ordinal / 7, buttons, digitalCurrent: SourceDigitalDirections.None),
            new SourceStationInput(2, digitalCurrent: SourceDigitalDirections.None) };
        var random = (SourceInjectedRandom)game.SourceRandomService; int firstDraw = random.Trace.Count;
        Wake(game, new PlayerCommand(stationInputs: new SourceInputFrame(stations)));
        SourceMovementState after = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(mover);
        SourceRandomDraw[] movementDraws = random.Trace.Skip(firstDraw).Where(d => d.Site.ActorId == actorId
            && d.Site.SourceIdentity.StartsWith("player.c:", StringComparison.Ordinal)).ToArray();
        int[] raw = movementDraws.Where(d => d.Raw.HasValue).Select(d => d.Raw.Value).ToArray();
        using (var document = JsonDocument.Parse(native.ReadJson(MovementNativeCommand(before, "input", context, new int[0]))))
        {
            JsonElement expectedInput = document.RootElement.GetProperty("State");
            Check(after.NonTurboTime == expectedInput.GetProperty("NonTurboTime").GetInt32()
                && (int)(after.Flags & SourceMovementFlags.Turbo) ==
                    (expectedInput.GetProperty("Flags").GetInt32() & (int)SourceMovementFlags.Turbo),
                name + ": exhaustion/repress counters and turbo gate match original");
        }
        using (var document = JsonDocument.Parse(native.ReadJson(MovementNativeCommand(before, "step", context, raw))))
        {
            JsonElement expected = document.RootElement.GetProperty("State");
            File.AppendAllText(Path.Combine(output, "movement-window-trace.jsonl"), JsonSerializer.Serialize(new {
                name, sourceTick = game.SourceTick, ordinal, buttons = (int)buttons, resource,
                actualResource = game.Model.SourceStationTurbo[0], expectedResource, before, after,
                expected = expected.Clone(), raw, actualScript = mover.AnimationState.Primary.ScriptSymbol,
                nativeDraws = document.RootElement.GetProperty("Draws").Clone(), actualDraws = movementDraws,
                nativeCommands = document.RootElement.GetProperty("Commands").Clone(),
                events = game.SourceEvents.Select(e => new { e.Type, e.ActorPlayerId, e.TargetPlayerId, e.Value }).ToArray()
            }, new JsonSerializerOptions { IncludeFields = true }) + "\n");
            Check(Bits(game.Model.SourceStationTurbo[0]) == Bits(expectedResource), name + ": actual station resource matches original");
            Check(after.Stick == expected.GetProperty("Stick").GetInt32(), name + ": actual49-way ordinal matches original");
            Check(Bits(after.VelocityX) == expected.GetProperty("VelocityX").GetUInt32()
                && Bits(after.VelocityZ) == expected.GetProperty("VelocityZ").GetUInt32(),
                name + ": public movement/physics velocity matches unchanged original C");
            Check(Bits(after.Friction) == expected.GetProperty("Friction").GetUInt32(),
                name + ": actual selected field friction matches original");
            Check(after.TeamSpinCount == expected.GetProperty("TeamSpinCount").GetInt32(),
                name + ": real repress spin count matches original");
            JsonElement[] draws = document.RootElement.GetProperty("Draws").EnumerateArray().ToArray();
            Check(draws.Length == movementDraws.Length && draws.Select((d, i) =>
                d.GetProperty("Bound").GetInt32() == movementDraws[i].Site.BoundOrModulus
                && (d.GetProperty("Raw").ValueKind == JsonValueKind.Null ? !movementDraws[i].Raw.HasValue
                    : movementDraws[i].Raw == d.GetProperty("Raw").GetInt32())).All(equal => equal),
                name + ": original movement consumes exact ordered raw calls/bounds");
        }
    }

    private static void MovementWindowChecks(string originalRoot)
    {
        string worker = Path.Combine(output, "movement-window-original.py");
        File.WriteAllText(worker, MovementWindowOriginalBuilder);
        string destination = Path.Combine(output, "original-movement-window");
        RunHeadingProcess("python3", new[] { "-B", worker, originalRoot, destination }, "movement-window-C-build.log");
        using (var native = new LiveDroneOriginal(Path.Combine(destination, "original-movement")))
        {
            for (int ordinal = 0; ordinal < 49; ordinal++)
                MovementWindowTick(native, MovementWindowFixture(), "49-way/" + ordinal, ordinal, SourceButtons.None, 0);
            foreach (float resource in new[] { 0f, .1f, .35f, .350001f })
            {
                GameSimulation game = MovementWindowFixture(); game.Model.SourceStationTurbo[0] = resource;
                MovementWindowTick(native, game, "exhaust/" + resource, 21, SourceButtons.C, 0);
                MovementWindowTick(native, game, "recharge/" + resource, 21, SourceButtons.None, 0);
            }
            foreach (int gap in new[] { 1, 8, 9 })
            {
                GameSimulation game = MovementWindowFixture();
                MovementWindowTick(native, game, "repress/" + gap + "/press", 21, SourceButtons.C, 0);
                for (int release = 0; release < gap; release++)
                    MovementWindowTick(native, game, "repress/" + gap + "/release" + release, 21, SourceButtons.None, 0);
                MovementWindowTick(native, game, "repress/" + gap + "/again", 21, SourceButtons.C, 0);
                Check(game.Model.Players[4].AnimationState.Primary.ScriptSymbol == (gap < 9 ? "spin_anim" : "run_t_wb_anim"),
                    "actual public turbo release" + gap + " selects original spin-or-run script");
            }
            for (int field = 0; field < 7; field++)
                MovementWindowTick(native, MovementWindowFixture(field, true), "friction/" + field, 24, SourceButtons.None, field);
            foreach (float resource in new[] { 99.6f, 99.9f, 100f })
            {
                GameSimulation game = MovementWindowFixture(); game.Model.SourceStationTurbo[0] = resource;
                MovementWindowTick(native, game, "recharge-cap/" + resource, 24, SourceButtons.None, 0);
            }
            foreach (SourceBallKind kind in new[] { SourceBallKind.Pass, SourceBallKind.Lateral })
                foreach (int target in new[] { -1, 4, 5 })
                {
                    GameSimulation game = MovementWindowFixture();
                    game.Model.SourceStationTurbo[0] = 0f;
                    game.Model.CarrierId = -1; game.Model.BallMode = BallMode.InFlight;
                    game.Model.BallKind = kind; game.Model.TargetPlayerId = target;
                    game.Model.PassTicksElapsed = 20; game.Model.PassTicksTotal = 100;
                    SourceBallDriver.SetPosition(game.Model, new SourceVector3(200f, 100f, 0f));
                    SourceBallDriver.SetTarget(game.Model, 200f, 0f);
                    SourceRouteState prior = game.Model.SourceDrones[4];
                    prior.ThinkTicks = 100; prior.Stick = prior.OldStick = 24;
                    game.Model.SourceDrones[4] = prior;
                    Cache(game);
                    MovementWindowTick(native, game, "intended-exhaust/" + kind + "/" + target, 24, SourceButtons.C, 0);
                }
            {
                GameSimulation game = MovementWindowFixture(); SimulationState m = game.Model;
                ActorState qb = m.Players[3], receiver = m.Players[4], defender = m.Players[11];
                receiver.SourceControllerStation = -1;
                SourceRouteState waiting = m.SourceDrones[4]; waiting.ThinkTicks = 100;
                waiting.Stick = waiting.OldStick = 24; m.SourceDrones[4] = waiting;
                qb.SourceControllerStation = 0; qb.SourceMode = SourceActorMode.Quarterback; qb.Role = ActorRole.Quarterback;
                m.SourceStationPlayers[0] = m.ControlledPlayerId = m.CarrierId = 3;
                m.SourceFlags = SourceGameFlags.None;
                SourcePlayerDriver.SetSourcePosition(qb, new SourceVector3(-200f, 0f, 0f));
                defender.SourceMode = SourceActorMode.DefensiveBack; defender.Role = ActorRole.Defender;
                defender.SourceJob = SourceActorJob.Cover; defender.SourceTarget = 4;
                defender.Facing = defender.TargetFacing = defender.UpperFacing = defender.TargetUpperFacing = 768;
                SourcePlayerDriver.SetSourcePosition(defender, new SourceVector3(10f, 0f, 0f));
                SourceRouteState cover = SourceRouteState.Begin(m.SourceDefensePlay.Routes[4], 4);
                cover.Job = SourceActorJob.Cover; m.SourceDrones[11] = cover;
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 11, 12);
                defender.AnimationState.FullBody("run_anim", defender, 0,
                    Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
                SourceBallDriver.SetPosition(m, new SourceVector3(-200f, 14f, 0f));
                Cache(game);
                Check(m.SourceAngles[11, 4] == 768 && defender.SourceControllerStation == -1 && qb.SourceControllerStation == 0
                    && SourceMatchRules.FieldPosition(-200f, 0) < m.SourceRules.LineOfScrimmage,
                    "backpedal caller starts actual cover geometry with genuine human QB, not a synthetic QB plan");
                MovementWindowTick(native, game, "actual-DB-cover-backpedal", 21, SourceButtons.None, 0, 11, 768);
                Check(defender.SourceBackpedal && defender.SourceBackpedalTarget == 4 && defender.Facing == 768
                    && defender.SourceStick == 21 && defender.SourceVelocity.X > 0f,
                    "actual AI cover selects opposite travel/facing and original backpedal consumer");
            }
        }
    }

    private static void ContactWindowChecks(string originalRoot)
    {
        Check(real.IsC2M1Profile, "contact-window selector requires genuine M1 composition");
        contactWindowProductionCallers = true;
        BlockAndPuppet();
        ContactBeforeBall();
        DeadBallPushAndGetUp();
        SpearHelmet();
        LateBodySelection();
        HelpUp();
        File.WriteAllText(Path.Combine(output, "contact-window-scenarios.json"),
            JsonSerializer.Serialize(kernelEvidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SourceC2PendingEnd VerifyQueuedPlayerDown(GameSimulation game, ActorState victim, long hit)
    {
        Check(game.Model.Phase == GamePhase.DeadBall && victim.Down && game.SourceTick > hit,
            "actual C2 H_FRONT1 ANI_CODE player_down queues the real result after impact/flight, not a fall timer");
        SourceC2PendingEnd pending = game.Model.SourceC2PendingResult;
        C2ResultTimingCheck(game.SourceTick == 135 && pending != null && pending.QueuedTick == 135 &&
            pending.Epoch == 1 && pending.PlayerId == 4 && pending.AttackerId == 11 &&
            pending.Input.Cause == SourcePlayEndCause.CarrierTackledInBounds,
            "actual source135 saves exactly epoch1/carrier4/attacker11/in-bounds result input");
        C2ResultTimingCheck(game.Model.SourceRules.Down == 1 && game.Model.SourceRules.LineOfScrimmage == 30 &&
            game.Model.SourceRules.FirstDown == 100 && !game.Model.SourceResultView.IsSpecified &&
            game.Model.SourceFlags == (SourceGameFlags)148480 && pending.Input.Flags == game.Model.SourceFlags &&
            JsonSerializer.Serialize(pending.Input.State) == JsonSerializer.Serialize(game.Model.SourceRules),
            "queued state preserves source flags0x24400 and pre-classification Down1/LOS30/FD100 without normalization");
        SourceC2Event[] events = game.C2Events.ToArray();
        C2ResultTimingCheck(events.Length == 2 && events[0].Kind == SourceC2EventKind.WhistleSpawned &&
            events[1].Kind == SourceC2EventKind.PlayOverQueued && events[0].Epoch == 0 && events[1].Epoch == 1 &&
            events.All(e => e.SourceTick == 135 && e.Process == "player_proc" && e.ProcessOrder == 6 && e.ActorId == 4),
            "original qcreate whistle precedes play-over assignment in actual player-proc/order6");
        SourceC2WhistleView whistle = game.C2State.WhistleProcesses.Single();
        C2ResultTimingCheck(whistle.Id == 1 && whistle.CreatedSourceTick == 135 &&
            whistle.FirstWakePending && whistle.SleepRemaining == 1 && game.C2State.ResultPending &&
            !game.SourceEvents.Any(e => e.Type == SourceEventType.PlayResolved) &&
            !events.Any(e => e.Kind == SourceC2EventKind.ResultCommitted),
            "creation does not run whistle or commit a result in the same actor wake");
        SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.ToArray();
        string rows = string.Join("\n", draws.Select(draw => string.Join("|", new[] {
            draw.Order.ToString(), draw.Raw.ToString(), draw.Value.ToString(), draw.Site.SourceTick.ToString(),
            draw.Site.Process, draw.Site.ActorId.ToString(), draw.Site.ProcessOrder.ToString(),
            draw.Site.CallIndex.ToString(), draw.Site.SourceIdentity, draw.Site.BoundOrModulus.ToString() })));
        C2ResultTimingCheck(draws.Length == 71 && game.Model.SourceRandomDraws == 71 &&
            Hash(Encoding.UTF8.GetBytes(rows)) == "a165e0239cb144673be7a9f7c9fc942852a6fb128f22d994b0a27ceb860e8730" &&
            !draws.Any(draw => draw.Site.SourceIdentity.StartsWith("player.c:whistle_proc:", StringComparison.Ordinal)),
            "every recorded71 raw/site/order prefix draw survives; qcreate consumes no chance or index draw");
        resultTimingEvidence["queued"] = new {
            tick = game.SourceTick, pending.QueuedTick, pending.Epoch, pending.PlayerId, pending.AttackerId, pending.Input,
            game.Model.SourceRules, game.Model.SourceFlags, game.Model.SourceResultView, game.C2State,
            events, queuedRawDraws = draws, rawPrefixSha256 = Hash(Encoding.UTF8.GetBytes(rows)),
            victim.Down, victim.SourceCelebrateTime, victim.SourceLateHittable,
            victim.AnimationState.Primary.ScriptSymbol, victim.AnimationState.Primary.SequenceName,
            victim.AnimationState.Primary.FrameOrdinal
        };
        return pending;
    }

    private static int VerifyNextSourceResult(GameSimulation game, SourceC2PendingEnd saved)
    {
        SourceRandomDraw[] newDraws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Skip(71).ToArray();
        C2ResultTimingCheck(game.SourceTick == 136 && newDraws.Length > 0 &&
            newDraws[0].Site.SourceTick == 136 && newDraws[0].Site.Process == "whistle_proc" &&
            newDraws[0].Site.ActorId == -1 && newDraws[0].Site.ProcessOrder == 0 &&
            newDraws[0].Site.CallIndex == 0 && newDraws[0].Site.BoundOrModulus == 100 &&
            newDraws[0].Site.SourceIdentity == "player.c:whistle_proc:6014",
            "first EXISTING next-source wake136 draws original whistle chance first at actor-1/order0/call0");
        SourceC2Event[] events = game.C2Events.ToArray();
        int first = Array.FindIndex(events, e => e.Kind == SourceC2EventKind.WhistleFirstWake);
        int committed = Array.FindIndex(events, e => e.Kind == SourceC2EventKind.ResultCommitted);
        C2ResultTimingCheck(first >= 0 && committed > first &&
            events.Count(e => e.Kind == SourceC2EventKind.WhistleFirstWake) == 1 &&
            events.Count(e => e.Kind == SourceC2EventKind.ResultCommitted) == 1 &&
            events[first].Process == "whistle_proc" && events[first].ProcessOrder == 0 && events[first].ActorId == -1 &&
            events[committed].SourceTick == 136 && events[committed].Process == "game_proc" &&
            events[committed].ProcessOrder == 1 && events[committed].Epoch == saved.Epoch &&
            events[committed].TauntedAfter == false,
            "head whistle runs BEFORE once-only game-proc/order1/epoch1 commit; observe latch on commit event");
        C2ResultTimingCheck(game.Model.SourceC2PendingResult == null && !game.C2State.ResultPending &&
            game.C2State.EndEpoch == 1 && game.Model.SourcePendingTackleAttacker == -1 &&
            game.Model.SourcePendingTackleVictim == -1,
            "commit clears exactly the saved pending epoch and resolution tackle markers");
        SourcePlayResolution expected = SourceMatchRules.Resolve(saved.Input);
        SourceMatchResult expectedView = new SourceMatchResult((SourcePlayKind)saved.Input.State.PlayType,
            (SourceResultKind)expected.Classification.Result, (SourceResultStatus)expected.Classification.Status,
            (SourcePlayEndKind)saved.Input.Cause, expected.Side0Points, expected.Side1Points,
            expected.JustGotFirstDown, expected.TurnoverOnDowns);
        SourceGameEvent[] resolved = game.SourceEvents.Where(e => e.Type == SourceEventType.PlayResolved).ToArray();
        C2ResultTimingCheck(resolved.Length == 1 && resolved[0].SourceTick == 136 &&
            resolved[0].ActorPlayerId == 4 && resolved[0].TargetPlayerId == 11 &&
            JsonSerializer.Serialize(resolved[0].Result) == JsonSerializer.Serialize(expectedView) &&
            JsonSerializer.Serialize(game.Model.SourceResultView) == JsonSerializer.Serialize(expectedView),
            "one external source136 PlayResolved uses actor4/target11 and exact saved-input classification");
        C2ResultTimingCheck(JsonSerializer.Serialize(game.Model.SourceRules) == JsonSerializer.Serialize(expected.State) &&
            game.Model.SourceFlags == expected.Flags && game.Model.SourceLastResult == expected.Classification.Result &&
            Bits(game.Model.SourcePlayEndX) == Bits(expected.PlayEndX) &&
            game.Model.SourceRules.Down == 2 && game.Model.SourceRules.LineOfScrimmage == 50 &&
            game.Model.SourceRules.FirstDown == 100 && expected.Classification.Result == SourcePlayResult.Down &&
            expected.Side0Points == 0 && expected.Side1Points == 0 && game.Model.HomeScore == 0 && game.Model.AwayScore == 0,
            "pinned Resolve(savedInput) commits exact Down/Down2/LOS50/FD100/no-points bookkeeping");
        int chance = newDraws[0].Value;
        SourceC2WhistleView[] remaining = game.C2State.WhistleProcesses.Where(p => p.Id == 1).ToArray();
        C2ResultTimingCheck(chance >= 25 ? remaining.Length == 0 :
            remaining.Length == 1 && !remaining[0].FirstWakePending && remaining[0].SleepRemaining == 30,
            "actual chance>=25 removes process; ONLY success installs original sleep30");
        C2ResultTimingCheck(!events.Any(e => e.Kind == SourceC2EventKind.WhistleEmission) &&
            newDraws.Count(draw => draw.Site.Process == "whistle_proc") == 1 &&
            !newDraws.Any(draw => draw.Site.Process == "whistle_proc" && draw.Site.BoundOrModulus == 2),
            "source136 has no immediate index2/audio; earliest successful emission is source166");
        resultTimingEvidence["nextSourceWake"] = new {
            tick = game.SourceTick, chance, earliestSuccessfulWhistleTick = 166, events, newDraws,
            saved.Input, expected, game.Model.SourceRules, game.Model.SourceFlags, game.Model.SourceResultView,
            game.C2State, resolved
        };
        return chance;
    }
    private static PlayerCommand RawPair(SourceButtons home = SourceButtons.None,
        SourceButtons away = SourceButtons.None, SourceButtons homePressed = SourceButtons.None,
        SourceButtons awayPressed = SourceButtons.None)
    {
        return new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(0, 0, 0, home, homePressed),
            new SourceStationInput(1, 0, 0, away, awayPressed) }));
    }

    private static void Stand(GameSimulation game, ActorState actor)
    {
        actor.SourceMode = SourceActorMode.Dead;
        actor.AnimationState.FullBody("qb_stance_anim", actor, game.SourceTick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
    }
    private static GameSimulation StandingPair(float distance = 9f, GamePhase phase = GamePhase.LiveBall,
        Func<SourceRandomSite, int> raw = null, bool baseOnly = false, int second = 11)
    {
        GameSimulation game = Create(raw, baseOnly: baseOnly);
        PairFixture(game, second: second, separation: distance, phase: phase);
        Stand(game, game.Model.Players[4]); Stand(game, game.Model.Players[second]);
        foreach (ActorState actor in new[] { game.Model.Players[4], game.Model.Players[second] })
        {
            ISourceAnimationClip clip = library.GetClip("QBSTANCE");
            SourceVector3 next = clip.GetFrame(clip.SelectedSourceFrames[1]).ScaledRoot;
            FieldVector drift = SourceAnimationLayerState.Rotate(next.X * actor.ModelScale - actor.SourceRoot.X,
                next.Z * actor.ModelScale - actor.SourceRoot.Z, actor.Facing, actor.SourceDirections);
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(
                (actor.Id == 4 ? 0f : distance) - drift.X, 0f, -drift.Z));
        }
        game.Model.CarrierId = -1; game.Model.BallMode = BallMode.InFlight; game.Model.BallKind = SourceBallKind.Fumble;
        game.Model.SourceBallFlags = SourceBallFlags.Freeze;
        SourceBallDriver.SetPosition(game.Model, new SourceVector3(200f, 90f, 0f));
        Cache(game);
        return game;
    }

    private static void Observe(string name, GameSimulation game, params int[] actors)
    {
        kernelEvidence.Add(new {
            name, source_tick = game.SourceTick, phase = game.Model.Phase.ToString(),
            carrier = game.Model.CarrierId, flags = (uint)game.Model.SourceFlags,
            hit_spot = new[] { game.Model.SourcePlayEndX, game.Model.SourcePlayEndZ },
            pending_attacker = game.Model.SourcePendingTackleAttacker,
            pending_victim = game.Model.SourcePendingTackleVictim,
            events = game.ContactEvents.Select(e => new {
                kind = e.Kind.ToString(), e.SourceTick, e.ProcessOrder, e.ActorPlayerId, e.OtherPlayerId,
                e.AttackMode, e.Direction4, e.Direction8, e.ScriptSymbol,
                world = new[] { e.WorldPosition.X, e.WorldPosition.Y, e.WorldPosition.Z } }).ToArray(),
            actors = actors.Select(id => game.Model.Players[id]).Select(a => new {
                a.Id, mode = a.SourceMode.ToString(), script = a.AnimationState.Primary.ScriptSymbol,
                sequence = a.AnimationState.Primary.SequenceName, frame = a.AnimationState.Primary.FrameOrdinal,
                modes = a.AnimationState.Primary.Mode, flags = GameSnapshot.SourceActorFlags(a),
                world = new[] { SourcePlayerDriver.WorldPosition(a).X, SourcePlayerDriver.WorldPosition(a).Y,
                    SourcePlayerDriver.WorldPosition(a).Z }, root = new[] { a.SourceRoot.X, a.SourceRoot.Y, a.SourceRoot.Z },
                velocity = new[] { a.SourceVelocity.X, a.SourceVerticalVelocity, a.SourceVelocity.Z },
                hit = a.SourceHitPartnerId, puppet = a.SourcePuppetLinkId, a.SourceBlockCount,
                a.SourceAttackMode, a.SourceAttackTime, a.Down }).ToArray(),
            random = ((SourceInjectedRandom)game.SourceRandomService).Trace.Where(r => r.Site.SourceTick == game.SourceTick)
                .Select(r => new { r.Order, r.Raw, r.Value, r.Site.Process, r.Site.ActorId,
                    r.Site.ProcessOrder, r.Site.CallIndex, r.Site.SourceIdentity, r.Site.BoundOrModulus }).ToArray()
        });
    }

    private static void KernelChecks()
    {
        VerifyPrograms();
        var newGame = Create();
        Check(newGame.ContactEvents.Count == 0, "original initialization has no detector wake");
        newGame.Step(default(PlayerCommand));
        Check(newGame.SourceTick == 0 && newGame.ContactEvents.Count == 0, "skipped host frame does not execute contact");
        BodyBoundaries();
        BodyTackleAndDown();
        RawSpinEvades();
        BlockAndPuppet();
        ContactBeforeBall();
        DeadBallPushAndGetUp();
        SpearHelmet();
        LateBodySelection();
        HelpUp();
        MissingAssetsAndUndefinedInputs();
    }

    private static void BodyBoundaries()
    {
        foreach (float distance in new[] { 9.999f, 10f, 10.001f })
        {
            GameSimulation game = StandingPair(distance);
            Wake(game, RawPair());
            bool collided = game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision);
            Check(collided == (distance < 10f), "actual kernel strict body10 boundary " + distance);
            Check((game.Model.Players[4].SourceHitPartnerId == 11) == collided,
                "actual kernel bilateral hit pointer only at reached do_collision " + distance);
            if (collided)
            {
                float gap = game.SourceActorWorldPosition(11).X - game.SourceActorWorldPosition(4).X;
                Check(Math.Abs(gap - 10f) < .00001f && game.Model.SourceDistances[4, 11] < 10f,
                    "actual body displacement uses frozen pre-response 3D cache and does not overwrite source velocities");
            }
            Observe("strict-body-" + distance, game, 4, 11);
        }
        foreach (string gate in new[] { "PF_NOCOLL", "MODE_NOCYL", "airborne3D" })
        {
            GameSimulation game = StandingPair(6f);
            if (gate == "PF_NOCOLL") game.Model.Players[4].SourceRawFlags |= 0x20;
            if (gate == "MODE_NOCYL") game.Model.Players[11].AnimationState.Primary.AddMode(0x200);
            if (gate == "airborne3D")
            {
                SourcePlayerDriver.SetSourcePosition(game.Model.Players[11], new SourceVector3(6f, 9f, 0f));
                game.Model.Players[11].SourceVerticalVelocity = .04f;
            }
            Wake(game, RawPair());
            Check(!game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision),
                "actual kernel no body collision at active source " + gate);
            Observe(gate, game, 4, 11);
        }
        GameSimulation same = StandingPair(9f, second: 5);
        same.Model.Players[5].SourceControllerStation = -1;
        same.Model.SourceStationPlayers[1] = -1;
        Wake(same, RawPair());
        Check(same.ContactEvents.Count(e => e.Kind == SourceContactEventKind.Collision) == 1
            && same.ContactEvents.All(e => e.Kind != SourceContactEventKind.AnimationChanged)
            && same.Model.Players[4].AnimationState.Primary.SequenceName == "QBSTANCE",
            "actual same-team body cylinders are not an opposing tackle or globally ignored");
        Observe("same-team-body", same, 4, 5);
        GameSimulation dead = StandingPair(9f, GamePhase.DeadBall);
        dead.Model.Players[11].AnimationState.Primary.AddMode(2u);
        Wake(dead, RawPair());
        Check(dead.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision)
            && dead.Model.Players[4].SourceHitPartnerId == 11,
            "actual dead-ball detector remains awake; UNINT tackler falls through to source cylinder response");
        Observe("deadball-unint-body", dead, 4, 11);
    }

    private static void BodyTackleAndDown()
    {
        var values = new Random(37);
        GameSimulation game = Create(site => site.SourceIdentity == "plyrseq.c:h_front1_anim" && site.BoundOrModulus == 100
            ? 0 : site.Process.StartsWith("detect", StringComparison.Ordinal)
                ? site.BoundOrModulus == 7 ? 2 : 99 : values.Next(0, 0x1000000));
        PairFixture(game);
        Stand(game, game.Model.Players[11]);
        Wake(game, RawPair());
        ActorState victim = game.Model.Players[4], attacker = game.Model.Players[11];
        Check(victim.AnimationState.Primary.ScriptSymbol == "h_front1_anim"
            && attacker.AnimationState.Primary.ScriptSymbol == "t_stshld_anim",
            "actual source body contact tackles a human WITHOUT A/legacy Tackle action using exact non-turbo table");
        Check(game.Model.Phase == GamePhase.LiveBall && (game.Model.SourceFlags & SourceGameFlags.PlayEnding) != 0
            && game.Model.SourcePendingTackleAttacker == 11 && game.Model.SourcePendingTackleVictim == 4
            && !victim.Down, "actual source impact stops progress but does NOT fabricate an immediate player_down");
        Check(victim.SourceHitPartnerId == 11 && attacker.SourceHitPartnerId == 4,
            "do_collision sets both real actor pointers before their immediate scripts");
        Check(victim.Facing == ((attacker.Facing + 512) & 1023) && victim.SourceVelocity.X < -1f,
            "actual rotate_anim and ANI_SET_FACE_REL_VEL use collided actor heading, never victim/target-radius facing");
        SourceCameraCue camera = game.PresentationCues.Cues.OfType<SourceCameraCue>()
            .Single(c => c.Kind == SourcePresentationCueKind.ContactCameraSelection);
        Check(camera.Context.SourcePath == "detect.c" && camera.Context.ExecutingSymbol == "evade_vs_tackle"
            && camera.Context.SourceLine == 2494 && camera.Selection.EntryCount == 8,
            "actual reached source contact csi_fs_endplay selection/caller is produced once");
        Observe("human-no-A-real-body-tackle", game, 4, 11);
        long hit = game.SourceTick;
        for (int i = 0; i < 120 && game.Model.Phase == GamePhase.LiveBall; i++) Wake(game, RawPair());
        SourceC2PendingEnd savedResult = null;
        if (!game.SourceC2Enabled)
        Check(game.Model.Phase == GamePhase.DeadBall && victim.Down && game.SourceTick > hit
            && game.SourceEvents.Any(e => e.Type == SourceEventType.PlayResolved),
            "actual H_FRONT1 ANI_CODE player_down resolves the real match after impact/flight, not a fall timer");
        else savedResult = VerifyQueuedPlayerDown(game, victim, hit);
        long downTick = game.SourceTick;
        Check(victim.AnimationState.Primary.ScriptSymbol == "m_wob_fu_anim" && !victim.SourceLateHittable,
            "actual post-player_down original plyr_have_ball enters real wobble interpolation BEFORE the late-hit flag");
        int commits = 0, resolutions = 0, whistleChance = -1;
        for (int i = 0; i < 10; i++)
        {
            Wake(game, RawPair());
            if (game.SourceC2Enabled && i == 0) whistleChance = VerifyNextSourceResult(game, savedResult);
            Check(victim.SourceLateHittable == (game.SourceTick - downTick >= 10),
                "actual ten-source-tick wobble ANI_INTTOSEQ precedes PF_LATEHITABLE " + i);
            if (game.SourceC2Enabled)
            {
                commits += game.C2Events.Count(e => e.Kind == SourceC2EventKind.ResultCommitted);
                resolutions += game.SourceEvents.Count(e => e.Type == SourceEventType.PlayResolved);
                C2ResultTimingCheck(commits == 1 && resolutions == 1 && game.C2State.EndEpoch == 1 &&
                    !game.C2State.ResultPending && game.SourceTick == downTick + i + 1 &&
                    !game.C2Events.Any(e => e.Kind == SourceC2EventKind.WhistleEmission),
                    "every existing wobble wake retains one commit/resolution and no premature whistle " + i);
            }
        }
        Check(game.State.Players[4].Source.Contact.HitPartnerPlayerId == 11
            && game.State.Players[4].Source.Contact.PuppetPlayerId == -1,
            "published actual source contact state is immutable and independently specified");
        Observe("real-player-down-and-wobble", game, 4, 11);
        if (game.SourceC2Enabled)
        {
            resultTimingEvidence["unchangedTenWakeWindow"] = new {
                downTick, lastTick = game.SourceTick, commits, resolutions, whistleChance, victim.SourceLateHittable,
                game.C2State, canonicalResultTimingAssertions, noAddedWakeOrInputChange = true
            };
            File.WriteAllText(Path.Combine(output, "c2-player-down-result-timing.json"),
                JsonSerializer.Serialize(resultTimingEvidence, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
    }

    private static void RawSpinEvades()
    {
        var random = new Random(551);
        GameSimulation game = Create(site => site.Process.StartsWith("detect", StringComparison.Ordinal) ? 0 : random.Next(0, 0x1000000));
        PairFixture(game, separation: 8f);
        Stand(game, game.Model.Players[11]);
        game.Model.Players[4].NonTurboTicks = 1;
        Wake(game, RawPair(SourceButtons.C, homePressed: SourceButtons.C));
        Check(game.Model.SourceSpinCount[0] == 1 && game.Model.Players[4].AnimationState.Primary.SequenceName == "SPIN",
            "raw human turbo edge causally produces the actual M1 spin before C1, not an injected evade label");
        Check(game.Model.Players[11].AnimationState.Primary.ScriptSymbol == "h_flailm_anim"
            && game.Model.SourcePendingTackleVictim == -1
            && (game.Model.SourceFlags & SourceGameFlags.PlayEnding) == 0,
            "actual source spin avoidance flails the tackler and leaves the carrier/play live");
        Observe("raw-C-spin-evades-contact", game, 4, 11);
    }

    private static void BlockAndPuppet()
    {
        foreach (bool humanRusher in new[] { true, false })
        {
            GameSimulation game = Create(site => site.Process.StartsWith("detect", StringComparison.Ordinal) ? 0 : 99);
            PairFixture(game, first: 1, second: 8);
            Stand(game, game.Model.Players[1]); Stand(game, game.Model.Players[8]);
            game.Model.CarrierId = 3; game.Model.Players[3].SourceMode = SourceActorMode.Quarterback;
            game.Model.Players[1].SourceJob = SourceActorJob.Block;
            game.Model.Players[1].SourceControllerStation = -1;
            game.Model.SourceStationPlayers[0] = -1;
            game.Model.Players[8].SourceControllerStation = humanRusher ? 1 : -1;
            game.Model.SourceStationPlayers[1] = humanRusher ? 8 : -1;
            Wake(game, RawPair());
            ActorState blocker = game.Model.Players[1], rusher = game.Model.Players[8];
            Check(rusher.SourceBlockCount == 1 && game.Model.SourcePendingTackleVictim == -1,
                "actual JOB_BLOCK caller increments the original rusher count and does not tackle the unrelated carrier");
            if (humanRusher)
            {
                Check(blocker.AnimationState.Primary.ScriptSymbol == "pushbl1_anim"
                    && rusher.AnimationState.Primary.ScriptSymbol == "pushbl1_anim"
                    && blocker.SourcePuppetLinkId == -1 && rusher.SourcePuppetLinkId == -1,
                    "actual human-rusher probability columns choose concrete non-puppet block animations");
            }
            else
            {
                Check(blocker.SourcePuppetLinkId == 8 && rusher.SourcePuppetLinkId == 1
                    && blocker.AnimationState.Primary.ScriptSymbol == "tie1c_v_anim"
                    && rusher.AnimationState.Primary.ScriptSymbol == "tie1c_a_anim",
                    "actual drone-rusher block table creates BOTH real puppet scripts/links");
                uint modes = blocker.AnimationState.Primary.Mode | rusher.AnimationState.Primary.Mode;
                Check((modes & 0x00c00000u) == 0x00c00000u,
                    "actual puppet/puppeteer modes come from original ANI programs, not animation labels");
                for (int i = 0; i < 12; i++) Wake(game, RawPair());
                Check(blocker.AnimationState.Primary.FrameOrdinal > 0 && rusher.AnimationState.Primary.FrameOrdinal > 0
                    && blocker.Facing == rusher.Facing && blocker.SourceHeight == rusher.SourceHeight,
                    "actual linked actors execute selected WAN frames and original set_puppet_pos facing/Y consequences");
                SourceMovementState before = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(blocker);
                Bind<Action<ActorState>>(game, "IntegrateSourceMovementPlayer")(blocker);
                SourceVector3 after = game.SourceActorWorldPosition(1);
                Check(Bits(before.X) == Bits(after.X) && Bits(before.Y) == Bits(after.Y)
                    && Bits(before.Z) == Bits(after.Z) && Bits(before.VelocityY) == Bits(blocker.SourceVerticalVelocity),
                    "puppet victim does not integrate gravity independently");
            }
            Observe(humanRusher ? "human-concrete-block" : "drone-concrete-puppet-block", game, 1, 8);
        }
    }

    private static void DeadBallPushAndGetUp()
    {
        GameSimulation game = StandingPair(9f, GamePhase.DeadBall, site => 99);
        game.Model.CarrierId = 3; game.Model.BallMode = BallMode.Dead;
        ActorState attacker = game.Model.Players[11], victim = game.Model.Players[4];
        attacker.Facing = attacker.TargetFacing = 768;
        attacker.AnimationState.FullBody("t_push_anim", attacker, game.SourceTick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        bool attacked = false, getUp = false, returned = false;
        for (int i = 0; i < 180; i++)
        {
            Wake(game, RawPair());
            attacked |= game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision && e.AttackMode == 1);
            getUp |= victim.Action == ActorAction.GetUp && victim.AnimationState.Primary.SequenceName == "GET_UP_B";
            if (attacked && getUp && victim.SourceImmediateGeneralRuns > 0) { returned = true; break; }
        }
        Check(attacked, "actual UNINT attack ANI_ATTACK_ON capsule executes during DEAD BALL with no carrier-only dispatcher");
        Check(getUp && returned && !victim.Down && victim.SourcePuppetLinkId == -1,
            "actual noncarrier push reaction reaches original get-up clip/callback and immediate GeneralRun");
        Check(game.Model.Phase == GamePhase.DeadBall && game.Model.SourcePendingTackleVictim == -1,
            "noncarrier/deadball attack changes actor state without fabricating a live-ball tackle");
        Observe("deadball-attack-reaction-real-getup", game, 4, 11);
    }

    private static void ContactBeforeBall()
    {
        GameSimulation game = StandingPair(9f, raw: site => 99);
        game.Model.Players[4].SourceJob = SourceActorJob.Block;
        game.Model.SourceBallFlags = SourceBallFlags.None;
        game.Model.SourceBallVerticalVelocity = -1f;
        SourceBallDriver.SetPosition(game.Model, new SourceVector3(200f, 1f, 0f));
        Wake(game, RawPair());
        SourceRandomDraw[] trace = ((SourceInjectedRandom)game.SourceRandomService).Trace
            .Where(r => r.Site.SourceTick == game.SourceTick).ToArray();
        SourceRandomDraw[] contact = trace.Where(r => r.Site.Process == "detect_proc").ToArray();
        SourceRandomDraw[] ball = trace.Where(r => r.Site.Process == "ball_proc").ToArray();
        Check(contact.Length >= 2 && ball.Length > 0 && contact.Max(r => r.Order) < ball.Min(r => r.Order)
            && game.Model.SourceBallBounces == 1,
            "actual RL_COLLIS wake/VM/selection completes BEFORE real ball_proc bounce RNG, not detect_after_ball");
        Check(game.Model.Players[4].SourceBlockCount + game.Model.Players[11].SourceBlockCount == 1,
            "the measured contact-before-ball row actually executes the original block result producer");
        Observe("real-collision-before-ball-bounce", game, 4, 11);
    }

    private static void SpearHelmet()
    {
        foreach (bool secretHead in new[] { false, true })
        {
            var random = new Random(552);
            GameSimulation game = Create(site => site.SourceIdentity.StartsWith("player.c:maybe_pop_helmet", StringComparison.Ordinal)
                ? 0 : site.Process.StartsWith("detect", StringComparison.Ordinal) ? 99 : random.Next(0, 0x1000000),
                presentation: new SourcePresentationSettings(TeamId.Home, new[] { secretHead ? 3 : -1, -1, -1, -1 }, 0));
            PairFixture(game, separation: 16f);
            Stand(game, game.Model.Players[4]); Stand(game, game.Model.Players[11]);
            game.Model.CarrierId = 4; game.Model.BallMode = BallMode.Held;
            ActorState attacker = game.Model.Players[11], victim = game.Model.Players[4];
            attacker.AnimationState.FullBody("t_sprhed_anim", attacker, game.SourceTick,
                Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
            bool hit = false;
            for (int i = 0; i < 25; i++)
            {
                Wake(game, RawPair());
                if (game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision && e.AttackMode == 5)) { hit = true; break; }
            }
            Check(hit && attacker.SourceAttackMode == 0 && victim.AnimationState.Primary.ScriptSymbol != "qb_stance_anim",
                "actual source spear ANI_ATTACK_ON reaches camera-capsule dispatcher and concrete reaction");
            Check(victim.SourceHelmetPopped == !secretHead,
                "actual spear->maybe_pop_helmet preserves human secret-head guard " + secretHead);
            SourceRandomDraw[] helmet = ((SourceInjectedRandom)game.SourceRandomService).Trace
                .Where(r => r.Site.SourceIdentity.StartsWith("player.c:maybe_pop_helmet", StringComparison.Ordinal)).ToArray();
            Check(helmet.Length == (secretHead ? 1 : 2) && helmet.All(r => r.Site.BoundOrModulus == 100),
                "helmet always draws first, then short-circuits exact secret-head or second probability input");
            Observe(secretHead ? "actual-spear-secret-head-guard" : "actual-spear-helmet-pop", game, 4, 11);
        }
    }

    private static void PrepareWobbleContact(GameSimulation game, ActorState victim, ActorState other)
    {
        other.SourceRawFlags |= 0x20;
        victim.AnimationState.FullBody("m_wob_fu_anim", victim, game.SourceTick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        for (int i = 0; i < 10; i++) Wake(game, RawPair());
        Check(victim.SourceLateHittable, "actual source wobble interpolation produces the subsequent contact input flag");
        SourcePlayerDriver.SetSourcePosition(victim, new SourceVector3(0f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(other, new SourceVector3(8f, 0f, 0f));
        victim.SourceVelocity = other.SourceVelocity = default(FieldVector);
        other.SourceRawFlags &= ~0x20;
    }

    private static void HelpUp()
    {
        GameSimulation game = StandingPair(9f, GamePhase.DeadBall, site => 99, second: 5);
        ActorState down = game.Model.Players[4], helper = game.Model.Players[5];
        helper.SourceControllerStation = -1; game.Model.SourceStationPlayers[1] = -1;
        game.Model.CarrierId = 4; game.Model.BallMode = BallMode.Dead;
        PrepareWobbleContact(game, down, helper);
        Wake(game, RawPair());
        Check(down.SourcePuppetLinkId == 5 && helper.SourcePuppetLinkId == 4
            && down.AnimationState.Primary.ScriptSymbol == "p_hlpup_back_v_anim"
            && helper.AnimationState.Primary.ScriptSymbol == "p_hlpup_back_a_anim"
            && !down.SourceLateHittable,
            "actual same-team late-hittable source call creates help-up puppet and clears original victim flag");
        Observe("source-same-team-help-up", game, 4, 5);
        bool released = false;
        for (int i = 0; i < 150; i++)
        {
            Wake(game, RawPair());
            if (game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.PuppetReleased)) { released = true; break; }
        }

        Check(released && down.SourcePuppetLinkId == -1 && helper.SourcePuppetLinkId == -1
            && ((down.AnimationState.Primary.Mode | helper.AnimationState.Primary.Mode) & 0x00c00000u) == 0,
            "actual help-up ANI_CODE release_puppet clears bilateral links/modes at the real source frame");
        Observe("source-puppet-release", game, 4, 5);
    }

    private static void LateBodySelection()
    {
        GameSimulation game = StandingPair(9f, GamePhase.DeadBall, site => 2);
        ActorState victim = game.Model.Players[4], attacker = game.Model.Players[11];
        game.Model.CarrierId = 4; game.Model.BallMode = BallMode.Dead;
        PrepareWobbleContact(game, victim, attacker);
        Wake(game, RawPair());
        Check(victim.SourceLateHittable && attacker.AnimationState.Primary.ScriptSymbol == "m_nostomp",
            "real wobble flag and human late body contact select original m_nostomp table entry without action heuristics");
        Check(attacker.TargetFacing == game.Model.SourceAngles[11, 4] && attacker.SourceAttackMode == 0,
            "actual reachable late-hit face_carrier updates target facing, not an invented attack or facing snap");
        Observe("real-late-body-nostomp", game, 4, 11);
    }

    private static void MissingAssetsAndUndefinedInputs()
    {
        GameSimulation game = Create(site => 0, baseOnly: true);
        PairFixture(game, first: 1, second: 8);
        Stand(game, game.Model.Players[1]); Stand(game, game.Model.Players[8]);
        game.Model.CarrierId = 3; game.Model.Players[3].SourceMode = SourceActorMode.Quarterback;
        game.Model.Players[1].SourceJob = SourceActorJob.Block;
        game.Model.Players[1].SourceControllerStation = game.Model.Players[8].SourceControllerStation = -1;
        game.Model.SourceStationPlayers[0] = game.Model.SourceStationPlayers[1] = -1;
        bool failed = false;
        try { Wake(game, RawPair()); }
        catch (InvalidOperationException e) { failed = e.Message.Contains("missing clip TIE1C_A"); }
        Check(failed, "immutable157 cannot silently substitute FALL_04 for a reached required C1 TIE1C_A asset");
        Observe("visible-immutable157-asset-dependency", game, 1, 8);
        int draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        long failedTick = game.SourceTick;
        failed = false;
        try { game.Step(default(PlayerCommand)); }
        catch (InvalidOperationException e) { failed = e.Message.Contains("Restart is required"); }
        Check(failed && game.SourceTick == failedTick && ((SourceInjectedRandom)game.SourceRandomService).Trace.Count == draws,
            "partial C1 VM failure cannot retry, publish an old clip, or consume another RNG draw before Restart");
        game.Restart();
        Check(game.SourceTick == 0 && game.Model.SourceAnimationFailure == null && game.ContactEvents.Count == 0,
            "explicit Restart resets the failed C1 execution and transient events, never replays a failed contact");
        game = StandingPair(8f);
        ActorState attacker = game.Model.Players[11];
        attacker.SourceAttackMode = 1; attacker.SourceAttackJointA = attacker.SourceAttackJointB = 4;
        attacker.SourceAttackTime = 20;
        failed = false;
        try { Wake(game, RawPair()); }
        catch (NotSupportedException e) { failed = e.Message.Contains("spherical attack zone"); }
        Check(failed, "actual source spherical-attack lockup is visible unsupported, never a fabricated radius hit");
        Observe("visible-source-spherical-lockup", game, 4, 11);
    }
}
