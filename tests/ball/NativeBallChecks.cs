using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class NativeBallChecks
{
    private static int assertions;
    private static readonly Dictionary<int, int> CatchResults = new Dictionary<int, int>();
    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new BallAssertionFailure("SOURCE ASSERTION: " + identity);
    }
    private static int Int(string value) { return int.Parse(value, CultureInfo.InvariantCulture); }
    private static uint UInt(string value) { return uint.Parse(value, CultureInfo.InvariantCulture); }
    private static float Float(string value) { return BitConverter.Int32BitsToSingle(unchecked((int)UInt(value))); }
    private static uint Bits(float value) { return unchecked((uint)BitConverter.SingleToInt32Bits(value)); }
    private static SourceBallKind Kind(int native)
    {
        return new[] { SourceBallKind.Pass, SourceBallKind.Fumble, SourceBallKind.Punt,
            SourceBallKind.Kickoff, SourceBallKind.FieldGoal, SourceBallKind.Bobble, SourceBallKind.Lateral }[native];
    }
    private static SourceMatchConfiguration Configuration(int humans)
    {
        var stations = new List<SourceStationConfiguration>();
        if ((humans & 1) != 0) stations.Add(new SourceStationConfiguration(0, TeamId.Home));
        if ((humans & 2) != 0) stations.Add(new SourceStationConfiguration(1, TeamId.Away));
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }), stations, TeamId.Home, SourceCaptainSlot.First);
    }

    internal static int Run(FullKernelInputs real, string oracleRoot)
    {
        ISourceAnimationLibrary library = real.Animations;
        SourcePlaybook book = real.Playbook;
        SourceMatchConfiguration[] configurations = Enumerable.Range(0, 4).Select(Configuration).ToArray();
        SourcePlayDefinition fake = book.Plays.Values.First(p => (p.Flags & SourcePlayFlags.FakeFieldGoal) != 0);
        int catchCases = 0, deflectCases = 0, fumbleCases = 0, physicsCases = 0;
        foreach (string line in File.ReadLines(Path.Combine(oracleRoot, "oracle-cases.tsv")))
        {
            string[] t = line.Split('\t');
            var requested = new List<int>();
            int cursor = 0;
            if (t[0] == "C")
            {
                int side = Int(t[3]), id = Int(t[5]);
                var model = new SimulationState { SourceMode = true, Phase = GamePhase.LiveBall, CarrierId = -1,
                    Possession = (TeamId)side, SourceRules = new SourceRulesState(side, side, Int(t[28]), Int(t[29]), 100),
                    SourceConfiguration = configurations[Int(t[32])], SourcePlaybook = book,
                    BallKind = Kind(Int(t[2])), ThrowerId = Int(t[6]), TargetPlayerId = Int(t[7]),
                    PassTicksElapsed = Int(t[8]), PassTicksTotal = Int(t[9]),
                    SourceFlags = (SourceGameFlags)UInt(t[26]), SourceBallFlags = (SourceBallFlags)UInt(t[27]),
                    HomeScore = Int(t[30]), AwayScore = Int(t[31]), SourceNoInterceptions = (Int(t[34]) & (1 << side)) != 0,
                    SourceOffensePlay = Int(t[35]) != 0 ? fake : book.GetPlay("play_spider") };
                model.SourceTeamFire[0] = (Int(t[33]) & 1) != 0;
                model.SourceTeamFire[1] = (Int(t[33]) & 2) != 0;
                model.SourceLastReceiver[0] = model.SourceLastReceiver[1] = 0;
                ActorState actor = model.Players[id];
                actor.Role = ActorRole.Receiver; actor.Action = ActorAction.Catch;
                actor.AnimationState = new SourceAnimationState(library, id);
                actor.AnimationState.FullBody("qb_stance_anim", actor, 0, n => 0, AnimationSelectionEvidence.SourceVerified);
                SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(Float(t[12]), Float(t[13]), Float(t[14])));
                actor.SourceRoot = new SourceVector3(0f, Float(t[15]), 0f);
                actor.SourceHandicap = Int(t[11]); actor.NoCatchTicks = Int(t[10]);
                actor.AnimationState.Primary.AddMode(UInt(t[25]));
                SourceBallDriver.SetPosition(model, new SourceVector3(Float(t[16]), Float(t[17]), Float(t[18])));
                Func<int, int> random = bound =>
                {
                    requested.Add(bound);
                    return bound == 0 ? 0 : Int(t[36 + cursor++]) % bound;
                };
                SourceCatchResult result = SourceCatchResult.None;
                SourceVector3 body = SourcePlayerDriver.WorldPosition(actor);
                if (SourceCatchRules.ModeEligible(model, actor, random)
                    && SourceCatchRules.InCatchDistance(model.SourceBallPosition,
                        new SourceVector3(body.X, body.Y + actor.SourceRoot.Y, body.Z),
                        new SourceVector3(Float(t[19]), Float(t[20]), Float(t[21])),
                        new SourceVector3(Float(t[22]), Float(t[23]), Float(t[24]))))
                    result = SourceCatchRules.Evaluate(model, actor, random);
                string label = "original standard_catch case " + t[1];
                Check((int)result == Int(t[68]), label + " result " + result + " != " + t[68]);
                CheckBounds(requested, t[69], label);
                if (!CatchResults.ContainsKey((int)result)) CatchResults[(int)result] = 0;
                CatchResults[(int)result]++;
                catchCases++;
            }
            else if (t[0] == "D")
            {
                int id = Int(t[2]);
                var model = new SimulationState { SourcePlaybook = book, BallKind = SourceBallKind.Pass,
                    SourceBallFlags = (SourceBallFlags)UInt(t[9]), SourceBallVelocity = new FieldVector(Float(t[6]), Float(t[8])),
                    SourceBallVerticalVelocity = Float(t[7]) };
                SourceBallDriver.SetPosition(model, new SourceVector3(Float(t[3]), Float(t[4]), Float(t[5])));
                SourceBallDriver.SetTarget(model, 222f, 111f);
                Func<int, int> random = bound =>
                {
                    requested.Add(bound);
                    return bound == 0 ? 0 : Int(t[10 + cursor++]) % bound;
                };
                SourceBallDriver.Deflect(model, model.Players[id], random);
                string label = "original deflect_pass case " + t[1];
                float[] actual = { model.SourceBallVelocity.X, model.SourceBallVerticalVelocity,
                    model.SourceBallVelocity.Z, model.SourceBallTarget.X, model.SourceBallTarget.Z };
                for (int i = 0; i < actual.Length; i++)
                    Check(Bits(actual[i]) == UInt(t[42 + i]), label + " float" + i + " bits "
                        + Bits(actual[i]) + " != " + t[42 + i]);
                Check((uint)model.SourceBallFlags == UInt(t[47]), label + " flags");
                Check(model.SourceDeflections[(int)model.Players[id].Team] == Int(t[48]), label + " stat including zero velocity");
                CheckBounds(requested, t[49], label);
                deflectCases++;
            }
            else if (t[0] == "F")
            {
                int side = Int(t[3]), victimId = Int(t[4]), attackerId = Int(t[5]);
                var model = new SimulationState { SourceMode = true, SourcePlaybook = book,
                    Phase = GamePhase.LiveBall, CarrierId = victimId, Possession = (TeamId)side,
                    SourceRules = new SourceRulesState(side, side, 1, Int(t[12]), 100),
                    SourceConfiguration = configurations[Int(t[15])], SourceFlags = (SourceGameFlags)UInt(t[16]),
                    SourceBallFlags = (SourceBallFlags)UInt(t[17]), BallKind = Kind(Int(t[2])),
                    Quarter = Int(t[11]) + 1, SourceTick = 1000, CatchSourceTick = 1000 - Int(t[10]),
                    HomeScore = Int(t[13]), AwayScore = Int(t[14]), SourceVictimHeight = Float(t[36]),
                    SourceNoAssist = Int(t[21]) != 0, SourceNoFumbles = Int(t[22]) != 0,
                    SourceSuperDrones = Int(t[23]) != 0, SourceDronesLose = Int(t[24]) != 0,
                    SourceDronesWinning = Int(t[25]) != 0, SourceAbusingSpin = Int(t[27]) != 0,
                    SourceFumblesOnPlay = Int(t[34]), PassTicksElapsed = 9, PassTicksTotal = 55,
                    ThrowerId = 13, TargetPlayerId = 12 };
                model.SourceTeamFire[0] = (Int(t[18]) & 1) != 0;
                model.SourceTeamFire[1] = (Int(t[18]) & 2) != 0;
                model.SourceSpinCount[side] = Int(t[26]); model.SourceOffensivePlays[side] = Int(t[28]);
                model.SourceLostFumbles[side] = Int(t[29]); model.SourceLostFumbles[1 - side] = Int(t[30]);
                model.SourceInterceptions[1 - side] = Int(t[31]); model.SourceLastReceiver[side] = Int(t[32]);
                ActorState victim = model.Players[victimId], attacker = model.Players[attackerId];
                victim.SourceHandicap = Int(t[6]); attacker.SourceHandicap = Int(t[7]);
                victim.SourceControllerStation = Int(t[8]); attacker.SourceControllerStation = Int(t[9]);
                victim.SourceMode = (SourceActorMode)Int(t[33]);
                SourcePlayerDriver.SetSourcePosition(victim, new SourceVector3(Float(t[35]), Float(t[36]), Float(t[37])));
                SourceBallDriver.SetPosition(model, default(SourceVector3));
                SourceBallDriver.SetTarget(model, 222f, 111f);
                Func<int, int> random = bound =>
                {
                    requested.Add(bound);
                    return bound == 0 ? 0 : Int(t[43 + cursor++]) % bound;
                };
                int high;
                SourceBallLoss result = SourceContactDriver.FumbleOrBobble(model, victim, attacker,
                    Int(t[19]), Int(t[20]), random, out high);
                var wrist = new SourceVector3(Float(t[38]), Float(t[39]), Float(t[40]));
                if (result == SourceBallLoss.Fumble)
                {
                    SourceBallDriver.Fumble(model, victim, wrist, Float(t[41]) / 2f, Float(t[42]) / 2f, random);
                    model.SourceFumblesOnPlay++;
                }
                else if (result == SourceBallLoss.Bobble)
                {
                    float by = random(10000) > high ? -1f : 0f;
                    SourceBallDriver.Bobble(model, victim, wrist, Float(t[41]) / 1.5f, by, Float(t[42]) / 1.5f, random);
                }
                string label = "original fumble_or_bobble and launch case " + t[1];
                Check((int)result == Int(t[75]), label + " probability result");
                float[] actual = { model.SourceBallPosition.X, model.SourceBallPosition.Y, model.SourceBallPosition.Z,
                    model.SourceBallVelocity.X, model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z,
                    model.SourceBallTarget.X, model.SourceBallTarget.Z, model.SourceFumbleX };
                for (int i = 0; i < actual.Length; i++)
                    Check(Bits(actual[i]) == UInt(t[76 + i]), label + " float" + i + " bits "
                        + Bits(actual[i]) + " != " + t[76 + i]);
                Check(model.BallKind == Kind(Int(t[85])), label + " ball kind");
                Check((uint)model.SourceBallFlags == UInt(t[86]), label + " ball flags");
                Check(model.PassTicksElapsed == Int(t[87]), label + " flight time");
                Check(model.ThrowerId == Int(t[88]), label + " who_threw");
                Check(model.TargetPlayerId == Int(t[89]), label + " int_receiver");
                Check((uint)model.SourceFlags == UInt(t[90]), label + " game flags");
                Check(model.SourceFumbles[side] == Int(t[91]), label + " fumble count");
                Check(model.SourceBobbles[side] == Int(t[92]), label + " bobble count");
                Check(model.SourceFumblesOnPlay == Int(t[93]), label + " fumbles on play");
                CheckBounds(requested, t[94], label);
                fumbleCases++;
            }
            else if (t[0] == "A")
            {
                int side = Int(t[2]), nativeMode = Int(t[5]);
                var model = new SimulationState { SourceMode = true, SourcePlaybook = book,
                    Phase = nativeMode == 1 ? GamePhase.LiveBall : nativeMode == 2 ? GamePhase.DeadBall : GamePhase.PreSnap,
                    SourceRules = new SourceRulesState(side, side, 1, Int(t[4]), 100, (SourcePlayType)Int(t[3])),
                    Possession = (TeamId)side, SourceFlags = (SourceGameFlags)UInt(t[6]),
                    CarrierId = Int(t[7]), BallKind = Kind(Int(t[8])), SourceBallFlags = (SourceBallFlags)UInt(t[9]),
                    SourceBallBounces = Int(t[10]), SourceBallBounceTick = Int(t[11]), PassTicksElapsed = Int(t[12]),
                    SourceTick = 1000, SourceClock = SourceClock.Create(playTime: 123),
                    SourceBallVelocity = new FieldVector(Float(t[16]), Float(t[18])), SourceBallVerticalVelocity = Float(t[17]) };
                SourceBallDriver.SetPosition(model, new SourceVector3(Float(t[13]), Float(t[14]), Float(t[15])));
                SourceBallDriver.SetTarget(model, 222f, 111f);
                Func<int, int> random = bound =>
                {
                    requested.Add(bound);
                    return bound == 0 ? 0 : Int(t[19 + cursor++]) % bound;
                };
                SourceBallStep result = SourceBallDriver.Advance(model, random);
                string label = "original ball_proc physics case " + t[1];
                float[] actual = { model.SourceBallPosition.X, model.SourceBallPosition.Y, model.SourceBallPosition.Z,
                    model.SourceBallVelocity.X, model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z,
                    model.SourceBallTarget.X, model.SourceBallTarget.Z };
                for (int i = 0; i < actual.Length; i++)
                    Check(Bits(actual[i]) == UInt(t[51 + i]), label + " float" + i + " bits "
                        + Bits(actual[i]) + " != " + t[51 + i]);
                Check((uint)model.SourceBallFlags == UInt(t[59]), label + " ball flags");
                Check((uint)model.SourceFlags == UInt(t[60]), label + " game flags");
                Check(model.PassTicksElapsed == Int(t[61]), label + " flight time");
                Check(model.SourceBallBounces == Int(t[62]), label + " bounce count");
                Check(model.SourceBallBounceTick == Int(t[63]), label + " bounce time");
                Check(result.EndPlay == (Int(t[65]) != 0), label + " play-end assignment");
                if (result.EndPlay) Check((int)result.Cause == Int(t[65]), label + " play-end cause");
                Check(model.ClockActive == (Int(t[66]) != 0), label + " kickoff clock activation");
                Check(model.SourceClock.PlayTime == Int(t[67]), label + " kickoff play_time reset");
                CheckBounds(requested, t[68], label);
                physicsCases++;
            }
            else
                throw new BallAssertionFailure("Unknown original ball oracle case: " + t[0]);
        }
        Check(catchCases == 24000 && deflectCases == 4096 && fumbleCases == 10000 && physicsCases == 8000,
            "entire expected native oracle cardinality");
        Check(Enumerable.Range(0, 7).All(CatchResults.ContainsKey), "all original catch outcomes including punt-down exercised");
        return assertions;
    }

    internal static int RunOrientation(FullKernelInputs real, string oracleRoot)
    {
        int before = assertions, spinStep = 0;
        var counts = new Dictionary<string, int> { { "spin", 0 }, { "matrix", 0 }, { "motion", 0 }, { "held", 0 } };
        SimulationState spinning = null;
        foreach (string line in File.ReadLines(Path.Combine(oracleRoot, "orientation-cases.tsv")))
        {
            string[] t = line.Split('\t');
            if (t.Length != 43 || t[0] != "H" || !counts.ContainsKey(t[3]))
                throw new BallAssertionFailure("Unknown original orientation oracle row.");
            int side = Int(t[4]), mode = Int(t[6]);
            var model = new SimulationState
            {
                SourceMode = true, SourcePlaybook = real.Playbook, SourceTick = 1000,
                SourceRules = new SourceRulesState(side, side, 1, 30, 100, (SourcePlayType)Int(t[5])),
                Possession = (TeamId)side,
                Phase = mode == 1 ? GamePhase.LiveBall : mode == 2 ? GamePhase.DeadBall : GamePhase.PreSnap,
                SourceKickApproach = mode == 3, CarrierId = Int(t[7]), BallKind = Kind(Int(t[8])),
                SourceBallFlags = (SourceBallFlags)UInt(t[9]), SourceBallHeading = Int(t[10]),
                SourceBallElevation = Int(t[11]), SourceBallPitch = Int(t[12]),
                SourceBallPitchRate = Int(t[13]), SourceBallRoll = Int(t[14]),
                PassTicksElapsed = Int(t[15]), SourceFlags = (SourceGameFlags)UInt(t[16]),
                SourceBallPosition = new SourceVector3(Float(t[17]), Float(t[18]), Float(t[19])),
                SourceBallVelocity = new FieldVector(Float(t[20]), Float(t[22])),
                SourceBallVerticalVelocity = Float(t[21]),
                SourceBallTarget = new SourceVector3(Float(t[23]), 0f, Float(t[24])),
                SourceClock = SourceClock.Create(playTime: 123)
            };
            string label = "original ball_proc orientation " + t[3] + " case " + t[1] + " step " + t[2];
            if (t[3] == "spin")
            {
                Check(Int(t[2]) == spinStep++, label + " continuous next wake");
                if (spinning == null)
                {
                    model.SourceBallFlags = SourceBallFlags.White | SourceBallFlags.Bounce | SourceBallFlags.Bobble
                        | SourceBallFlags.FirePass | SourceBallFlags.TurboPass | SourceBallFlags.Freeze
                        | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost;
                    model.SourceBallElevation = 384;
                    model.SourceBallRoll = -100;
                    model.CarrierId = 3;
                    model.SourceBallVelocity = new FieldVector(2f, 3f);
                    model.SourceBallVerticalVelocity = 1f;
                    SourceBallDriver.Spin(model, new SourceVector3(13f, 9f, -7f));
                    spinning = model;
                }
                else
                {
                    model = spinning;
                    model.SourceTick++;
                }
                CheckOrientationState(model, t, 10, 9, 7, 15, 16, 17, label + " retained input");
            }
            int draws = 0;
            SourceBallStep result = SourceBallDriver.Advance(model, bound => { draws++; return 0; });
            CheckOrientationState(model, t, 25, 30, 31, 32, 33, 34, label);
            Check(!result.EndPlay, label + " no invented play-end");
            Check(draws == Int(t[42]), label + " original RNG count");
            counts[t[3]]++;
        }
        Check(counts["spin"] == 61, "all 61 consecutive original spin_ball wakes");
        Check(counts["matrix"] == 1280, "all original zero-horizontal phase/side/play/flag/elevation cases");
        Check(counts["motion"] == 420, "all retained moving velocity-pitch/pass-roll cases");
        Check(counts["held"] == 8, "all retained held/phantom boundary cases");
        return assertions - before;
    }

    private static void CheckOrientationState(SimulationState model, string[] t, int angles, int flags,
        int carrier, int flight, int gameFlags, int floats, string label)
    {
        int[] actualAngles = { model.SourceBallHeading, model.SourceBallElevation, model.SourceBallPitch,
            model.SourceBallPitchRate, model.SourceBallRoll };
        for (int i = 0; i < actualAngles.Length; i++)
            Check(actualAngles[i] == Int(t[angles + i]), label + " angle" + i
                + " " + actualAngles[i] + " != " + t[angles + i]);
        Check((uint)model.SourceBallFlags == UInt(t[flags]), label + " ball flags");
        Check(model.CarrierId == Int(t[carrier]), label + " carrier");
        Check(model.PassTicksElapsed == Int(t[flight]), label + " flight time");
        Check((uint)model.SourceFlags == UInt(t[gameFlags]), label + " game flags");
        float[] actual = { model.SourceBallPosition.X, model.SourceBallPosition.Y, model.SourceBallPosition.Z,
            model.SourceBallVelocity.X, model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z,
            model.SourceBallTarget.X, model.SourceBallTarget.Z };
        for (int i = 0; i < actual.Length; i++)
            Check(Bits(actual[i]) == UInt(t[floats + i]), label + " float" + i);
    }

    private static void CheckBounds(List<int> actual, string native, string label)
    {
        int[] values = native.Split(',').Select(Int).ToArray();
        Check(values[0] == actual.Count, label + " RNG draw count " + actual.Count + " != " + native);
        for (int i = 0; i < actual.Count; i++) Check(actual[i] == values[i + 1], label + " RNG bound " + i);
    }
}
