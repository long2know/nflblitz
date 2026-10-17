using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static readonly int[] HeadingInputs = {
        -1023, -1022, -961, -960, -959, -897, -896, -895, -769, -768, -767,
        -641, -640, -639, -513, -512, -511, -385, -384, -383, -257, -256, -255,
        -181, -180, -179, -161, -160, -159, -129, -128, -127, -67, -66, -65, -1,
        0, 1, 65, 66, 127, 128, 129, 159, 160, 161, 179, 180, 181, 255, 256,
        257, 383, 384, 385, 511, 512, 513, 639, 640, 641, 767, 768, 769,
        895, 896, 897, 958, 959, 960, 961, 1022, 1023
    };

    private static string OriginalHeadingFunction(string text, string name)
    {
        Match found = Regex.Match(text, @"(?m)^(?:static\s+)?(?:void|int|float)\s+" + Regex.Escape(name)
            + @"\s*\([^;{}]*\)\s*\{");
        if (!found.Success) throw new InvalidDataException("Missing exact original heading function " + name);
        int end = text.IndexOf("\n}", found.Index + found.Length, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Missing exact original heading function end " + name);
        return text.Substring(found.Index, end + 2 - found.Index);
    }

    private static string RunHeadingProcess(string file, IEnumerable<string> arguments, string log)
    {
        var start = new ProcessStartInfo(file) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        string work = Path.Combine(output, "original-C-work");
        Directory.CreateDirectory(work);
        start.Environment["TMPDIR"] = start.Environment["TMP"] = start.Environment["TEMP"] = work;
        using (Process process = Process.Start(start))
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            File.WriteAllText(Path.Combine(output, log), stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Original heading C process failed: " + file + "\n" + stderr);
            return stdout;
        }
    }

    private static void HeadingBoundaryChecks(string originalRoot)
    {
        string pmisc = File.ReadAllText(Path.Combine(originalRoot, "pmisc.c"));
        string drone = File.ReadAllText(Path.Combine(originalRoot, "drone.c"));
        string pipe = File.ReadAllText(Path.Combine(originalRoot, "lib/ani3d/pipe3D.c"));
        string trigs = File.ReadAllText(Path.Combine(originalRoot, "lib/ani3d/TRIGS.H"));
        foreach (KeyValuePair<string, string> pin in new Dictionary<string, string> {
            { "pmisc.c", "50426d40ecd03515cecd295fb80a63ffa0cb0683" },
            { "drone.c", "d4b636dcd8cc34109be5834b2be2b18b7c04439a" },
            { "lib/ani3d/pipe3D.c", "0966ecaf0d7eef7d9c635a97899a421ce1eeea41" },
            { "lib/ani3d/TRIGS.H", "a07d76798931f1240bdf7f3b8e753ebd7832763a" }
        })
        {
            byte[] data = File.ReadAllBytes(Path.Combine(originalRoot, pin.Key));
            byte[] header = Encoding.ASCII.GetBytes("blob " + data.Length + "\0");
            using (SHA1 algorithm = SHA1.Create())
                Check(BitConverter.ToString(algorithm.ComputeHash(header.Concat(data).ToArray()))
                    .Replace("-", "").ToLowerInvariant() == pin.Value,
                    "heading comparison exact original Git blob " + pin.Key);
        }
        string original = "#include <stdio.h>\n#include <stdlib.h>\n#include <stdint.h>\n#define ABS(x) abs(x)\n"
            + "typedef struct {struct {int fwd;} odata; int plyrnum,team;} fbplyr_data;\n"
            + "static fbplyr_data player_blocks[14];static int p2p_angles[14][14];static float distxz[14][14];\n"
            + trigs + "\n" + OriginalHeadingFunction(pipe, "isin") + "\n"
            + OriginalHeadingFunction(pipe, "icos") + "\n" + OriginalHeadingFunction(pmisc, "bearing") + "\n"
            + OriginalHeadingFunction(drone, "nearest_defender") + "\n"
            + OriginalHeadingFunction(drone, "clean_shot") + "\n" + OriginalHeadingFunction(drone, "dir2stick") + "\n"
            + "static unsigned bits(float f){union{float f;unsigned u;}v;v.f=f;return v.u;}\n"
            + "int main(void){int raw[]={"+ string.Join(",", HeadingInputs) + "};"
            + "for(int r=0;r<(int)(sizeof(raw)/sizeof(*raw));r++)for(int angle=0;angle<1024;angle++){"
            + "player_blocks[0].odata.fwd=raw[r];player_blocks[0].plyrnum=0;player_blocks[0].team=0;"
            + "for(int i=7;i<14;i++){p2p_angles[0][i]=(angle+(i-7)*137)&1023;distxz[0][i]=9.0f+(i-7);}"
            + "int b=bearing(0,7),near;float nearest=nearest_defender(player_blocks,&near);"
            + "printf(\"%d\\t%d\\t%d\\t%d\\t%d\\t%d\\t%u\\t%u\\t%d\\n\",raw[r],angle,b,"
            + "nearest<15.6f&&abs(bearing(0,near))<=128,b<=160,clean_shot(player_blocks,7),"
            + "bits(isin(raw[r])),bits(icos(raw[r])),dir2stick(raw[r]&1023));"
            + "}return 0;}\n";
        string source = Path.Combine(output, "heading-original.c"), binary = Path.Combine(output, "heading-original");
        File.WriteAllText(source, original, Encoding.ASCII);
        RunHeadingProcess("gcc", new[] { "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off",
            "-fexcess-precision=standard", source, "-o", binary }, "heading-C-compile.log");
        string result = RunHeadingProcess(binary, new string[0], "heading-C-execute.log");
        File.WriteAllText(Path.Combine(output, "heading-original-results.tsv"), result);
        CheckHeadingGuards();
        CompareOriginalHeadingRows(result);
        ActualSignedHeadingProducerAndBoundary();
    }

    private static void CheckHeadingGuards()
    {
        foreach (int invalid in new[] { int.MinValue, -1024, -1023, -66, -1, 1024, int.MaxValue })
        {
            bool rejected = false;
            try { new SourceAiActor(0, default(SourcePlanar), default(SourcePlanar), SourceActorMode.Receiver, facing: invalid); }
            catch (ArgumentOutOfRangeException e) { rejected = e.ParamName == "facing"; }
            Check(rejected, "canonical public AI facing guard remains strict " + invalid);
        }
        foreach (int invalid in new[] { int.MinValue, -1024, 1024, int.MaxValue })
        {
            bool rejected = false;
            try { new SourceAiActor(0, default(SourcePlanar), default(SourcePlanar), SourceActorMode.Receiver,
                facing: invalid & 1023, rawFacing: invalid); }
            catch (ArgumentOutOfRangeException e) { rejected = e.ParamName == "rawFacing"; }
            Check(rejected, "original signed-remainder input is bounded, not arbitrary invalid admission " + invalid);
        }
        bool mismatch = false;
        try { new SourceAiActor(0, default(SourcePlanar), default(SourcePlanar), SourceActorMode.Receiver,
            facing: 0, rawFacing: -66); }
        catch (ArgumentOutOfRangeException e) { mismatch = e.ParamName == "rawFacing"; }
        Check(mismatch, "raw/canonical heading views must identify the SAME circular orientation");
    }

    private static void CompareOriginalHeadingRows(string result)
    {
        MethodInfo clean = typeof(SourceDroneAI).GetMethod("CleanShot", BindingFlags.Static | BindingFlags.NonPublic);
        SourceRouteProgram receiverRoute = real.Playbook.GetRoute("pr_goto_endzone");
        var parameters = new SourceAiParameters(real.Playbook, 5, 5);
        int count = 0, negativeCanonicalCounterexamples = 0;
        foreach (string line in result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] values = line.Split('\t');
            int raw = int.Parse(values[0]), angle = int.Parse(values[1]);
            var actors = new SourceAiActor[14];
            for (int id = 0; id < 14; id++)
                actors[id] = new SourceAiActor(id, new SourcePlanar(id * 9f, id), default(SourcePlanar),
                    id == 3 ? SourceActorMode.Quarterback : SourceActorMode.Receiver,
                    facing: id == 0 ? raw & 1023 : 0, rawFacing: id == 0 ? raw : 0);
            var distances = new float[14, 14]; var angles = new int[14, 14];
            for (int id = 7; id < 14; id++)
            { angles[0, id] = (angle + (id - 7) * 137) & 1023; distances[0, id] = 9f + id - 7; }
            var frame = new SourceAiFrame(actors, real.Playbook.Directions, new SourceRulesState(0, 0, 1, 30, 50),
                new SourceAiBall(SourceBallType.Pass, default(SourcePlanar), default(SourcePlanar), default(SourcePlanar), 3),
                1, sourceDistances: distances, sourceAngles: angles);
            string identity = "original C raw/canonical AI boundary " + raw + "/" + angle;
            Check(frame.Actors[0].RawFacing == raw && frame.Actors[0].Facing == (raw & 1023), identity + " separate views");
            Check(frame.Bearing(0, 7) == int.Parse(values[2]), identity + " EXACT signed bearing");
            int nearestDefender;
            float nearestDistance = SourceDroneAI.NearestDefender(frame, 0, out nearestDefender);
            Check((nearestDistance < 15.6f && Math.Abs(frame.Bearing(0, nearestDefender)) <= 128) == (values[3] == "1"),
                identity + " actual original nearest_defender and receiver abs cone");
            Check((frame.Bearing(0, 7) <= 160) == (values[4] == "1"), identity + " carrier signed cone");
            Check((bool)clean.Invoke(null, new object[] { frame, 0, 7 }) == (values[5] == "1"), identity + " actual clean_shot consumer");
            SourceRouteStep receiver = SourceDroneAI.StepRoute(SourceRouteState.Begin(receiverRoute),
                new SourceRouteInput(frame, 0, parameters,
                    new SourceRandomContext(1, 0, "player_proc", 2, real.Playbook.Profile)));
            Check(((receiver.State.Buttons & SourceDroneButtons.B) != 0) == (values[3] == "1")
                && receiver.CommandWritten && receiver.ButtonsWritten && receiver.RandomCalls == 0,
                identity + " actual receiver running-push buttons match original abs cone without RNG");
            Check(Bits(real.Playbook.Directions.Sin(frame.Actors[0].Facing)) == uint.Parse(values[6])
                && Bits(real.Playbook.Directions.Cos(frame.Actors[0].Facing)) == uint.Parse(values[7]),
                identity + " canonical table indexing is original isin/icos equivalent");
            Check(real.Playbook.Directions.DirectionToStick(frame.Actors[0].Facing) == int.Parse(values[8]),
                identity + " source0..1023 dir2stick domain");
            int projected = (1024 + frame.Actors[0].Facing - angle) % 1024;
            if (projected > 512) projected = 1024 - projected;
            if (projected != frame.Bearing(0, 7)) negativeCanonicalCounterexamples++;
            count++;
        }
        Check(count == HeadingInputs.Length * 1024 && negativeCanonicalCounterexamples > 0,
            "native C negative and every circular boundary covered; blanket masking has real counterexamples");
        File.WriteAllText(Path.Combine(output, "heading-C-comparison-summary.json"), JsonSerializer.Serialize(new {
            cases = count, canonicalMaskCounterexamples = negativeCanonicalCounterexamples,
            rawActorOrVMMutated = false, originalSignedBearingRetained = true, actualCleanShotCompared = true,
            actualReceiverButtonCases = count,
            sourceReceiverAbs = "drone.c:2019-2022", sourceCarrierSigned = "drone.c:2096-2116",
            sourceCleanShotAbs = "drone.c:4521-4535", sourceBearing = "pmisc.c:608-618",
            sourceTrigIndex = "pipe3D.c:866-877", sourceDir2Stick = "drone.c:550-569"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ActualSignedHeadingProducerAndBoundary()
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(3, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
        GameSimulation game = real.Create(configuration: configuration,
            presentation: new SourcePresentationSettings(TeamId.Away, new[] { -1, 3, 19, -1 }, -2));
        lastGame = game;
        bool beforeTurn = false, afterTurn = false, actualBoundary = false;
        int rawBeforeProjection = 0, rawAfterProjection = 0, projected = 0, retained = 0;
        int boundaryRng = -1;
        ActorState observed = game.Model.Players[8];
        SourceAnimationLayerState layer = observed.AnimationState.Primary;
        Action<ActorState> original = layer.BeforeInstruction;
        layer.BeforeInstruction = actor =>
        {
            original(actor);
            var program = (SourceAnimationProgram)typeof(SourceAnimationLayerState).GetField("program",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layer);
            int pc = (int)typeof(SourceAnimationLayerState).GetField("pc",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layer) - 1;
            if (game.SourceTick == 443 && layer.ScriptSymbol == "tie2c_v_anim")
            {
                if (pc == 5 && program.Instructions[pc].Op == SourceAnimationOp.TurnHack)
                    beforeTurn = actor.Facing == 446 && program.Instructions[pc].A == 512;
                if (pc == 6) afterTurn = actor.Facing == -66;
            }
        };
        game.SourceCameraHeadingProvider = (snapshot, tick) =>
        {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
            if (tick == 444)
            {
                rawBeforeProjection = observed.Facing;
                int before = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
                SourceAiFrame frame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
                boundaryRng = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count - before;
                rawAfterProjection = observed.Facing; projected = frame.Actors[8].Facing; retained = frame.Actors[8].RawFacing;
                Check(rawBeforeProjection == -66 && rawAfterProjection == -66 && projected == 958 && retained == -66,
                    "actual source443 signed TURNHACK actor remains RAW -66 across real source444 AI projection");
                Check(frame.Actors[8].Target == observed.SourceTarget && frame.Actors[8].Mode == observed.SourceMode
                    && frame.Actors[8].Station == observed.SourceControllerStation && frame.Actors[8].Job == observed.SourceJob,
                    "actual AI boundary preserves actor mode/job/target/station, not a replacement snapshot");
                Check(boundaryRng == 0, "actual source444 AI projection consumes no RNG and does not normalize actor state");
                actualBoundary = true;
            }
            return heading;
        };
        int host = 0;
        while (game.SourceTick < 445 && host < 1000)
        {
            game.Step(game.State.Phase == GamePhase.DeadBall
                ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand));
            host++;
        }
        Check(beforeTurn && afterTurn && actualBoundary && game.SourceTick >= 445,
            "actual unfiltered public B1 kickoff reproduces446->-66 and progresses BEYOND original source444 guard fault");
        File.WriteAllText(Path.Combine(output, "heading-public-producer-boundary.json"), JsonSerializer.Serialize(new {
            schema = "source-heading-public-boundary/v1", status = "passed", host,
            actualSourceTick = game.SourceTick, actualBeforeTurn446 = beforeTurn, actualAfterTurnMinus66 = afterTurn,
            rawBeforeProjection, rawAfterProjection, canonicalAIHeading = projected, retainedOriginalRawHeading = retained,
            addedRngDraws = boundaryRng, actorStateMutation = false,
            sourceProducer = "plyrseq.c:9807-9808 / ANIM.C:2353-2372", actualPhase = game.Model.Phase.ToString()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
