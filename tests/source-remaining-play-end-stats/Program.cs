using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class SourceRemainingPlayEndStatsChecks
{
    private sealed class NamedAssertion : Exception
    {
        internal readonly string Field;
        internal NamedAssertion(string field) { Field = field; }
    }

    private static readonly string[] StatNames =
    {
        "returnYards", "kickReturns", "rushingYards", "rushes", "sackYards",
        "sacks", "totalYards", "passingYards", "completions", "passTouchdowns"
    };
    private static readonly string[] StatUnits =
    {
        "field-yards", "count", "field-yards", "count", "field-yards",
        "count", "field-yards", "field-yards", "count", "count"
    };
    private static readonly List<object> Fields = new List<object>();
    private static readonly List<string> Cases = new List<string>();
    private static SimulationState model;
    private static SourceAnimationState[] animations;
    private static JsonElement current;
    private static string currentName;
    private static int fieldIndex;

    private static int[][] Arrays(SimulationState state)
    {
        return new[]
        {
            state.SourceReturnYards, state.SourceKickReturns, state.SourceRushingYards,
            state.SourceRushes, state.SourceSackYards, state.SourceSacks, state.SourceTotalYards,
            state.SourcePassingYards, state.SourcePassCompletions, state.SourcePassTouchdowns
        };
    }

    private static void Observe(string suffix, string unit, object actual)
    {
        JsonElement expectedFields = current.GetProperty("fields");
        if (fieldIndex >= expectedFields.GetArrayLength())
            throw new InvalidOperationException("Undeclared observation: " + currentName + "." + suffix);
        JsonElement expectedField = expectedFields[fieldIndex++];
        string name = currentName + "." + suffix;
        if (expectedField.GetProperty("name").GetString() != name
            || expectedField.GetProperty("unit").GetString() != unit)
            throw new InvalidOperationException("Frozen field order/unit mismatch: " + name);
        JsonElement expected = expectedField.GetProperty("value");
        bool equal;
        switch (expected.ValueKind)
        {
            case JsonValueKind.Number:
                equal = actual != null && expected.GetDouble() == Convert.ToDouble(actual);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                equal = actual is bool && (bool)actual == expected.GetBoolean();
                break;
            case JsonValueKind.Null:
                equal = actual == null;
                break;
            case JsonValueKind.String:
                equal = actual is string && (string)actual == expected.GetString();
                break;
            default:
                throw new InvalidOperationException("Only literal scalar expectations are admitted.");
        }
        Fields.Add(new { name, unit, expected = expected.Clone(), actual });
        if (!equal) throw new NamedAssertion(name);
    }

    private static void PutVectors(SimulationState state, JsonElement vectors)
    {
        int[][] arrays = Arrays(state);
        for (int side = 0; side < 2; side++)
            for (int index = 0; index < arrays.Length; index++)
                arrays[index][side] = vectors[side][index].GetInt32();
    }

    private static void Reset()
    {
        foreach (int[] array in Arrays(model)) Array.Clear(array, 0, array.Length);
        int[][] sentinels =
        {
            model.SourceTackles, model.SourceOffensivePlays, model.SourceLostFumbles,
            model.SourceInterceptions, model.SourceDeflections, model.SourceFumbles,
            model.SourceBobbles, model.SourceLastReceiver, model.SourceConsecutiveCompletions,
            model.SourceConsecutiveLosses
        };
        int[,] values = { {17,19}, {23,29}, {3,4}, {5,6}, {7,8}, {9,10}, {11,12},
            {4,11}, {13,14}, {15,16} };
        for (int index = 0; index < sentinels.Length; index++)
            for (int side = 0; side < 2; side++)
                sentinels[index][side] = values[index, side];
        model.SourceTeamFire[0] = true;
        model.SourceTeamFire[1] = false;
        model.SourceTimeOfPossession = new SourceTimeOfPossessionView(37, 41);
        model.SourceRandomDraws = 0;
        model.SourceForwardCatcher = 4;
        model.SourceKickReturnCatch = 0;
        model.SourceCatchX = 0f;
        model.SourceRules = new SourceRulesState(0, 0, 1, 30, 90);
        model.Possession = TeamId.Home;
        model.SourceFlags = SourceGameFlags.None;
        model.BallKind = SourceBallKind.Unspecified;
        model.SourceLastResult = SourcePlayResult.Down;
        model.SourceLastPossession = 0;
        model.CarrierId = 4;
        model.LineOfScrimmage = 30f;
        model.FirstDownLine = 90f;
        model.Phase = GamePhase.DeadBall;
        model.SourceMatchPhase = SourceMatchPhase.PlayResult;
    }

    private static void ObserveModel(SimulationState state, string prefix, JsonElement order, bool rushFirst = false)
    {
        int[][] arrays = Arrays(state);
        int[] indices = rushFirst ? new[] {3,0,1,2,4,5,6,7,8,9} : new[] {0,1,2,3,4,5,6,7,8,9};
        foreach (JsonElement item in order.EnumerateArray())
        {
            int side = item.GetInt32();
            string team = side == 0 ? "home" : "away";
            foreach (int index in indices)
                Observe(prefix + "." + team + "." + StatNames[index], StatUnits[index], arrays[index][side]);
        }
    }

    private static void ObserveSnapshot(GameSnapshot snapshot, string prefix)
    {
        SourceMatchView match = snapshot.Match;
        SourcePlayEndStatsView group = match.PlayEndStats;
        SourceCompletedPassStatsView completed = match.CompletedPassStats;
        int[][] values =
        {
            new[] { group.HomeReturnYards, group.HomeKickReturns, group.HomeRushingYards, group.HomeRushes,
                group.HomeSackYards, group.HomeSacks, completed.HomeTotalYards, completed.HomePassingYards,
                match.PassCompletions.HomeCompletions, completed.HomePassTouchdowns },
            new[] { group.AwayReturnYards, group.AwayKickReturns, group.AwayRushingYards, group.AwayRushes,
                group.AwaySackYards, group.AwaySacks, completed.AwayTotalYards, completed.AwayPassingYards,
                match.PassCompletions.AwayCompletions, completed.AwayPassTouchdowns }
        };
        for (int side = 0; side < 2; side++)
            for (int index = 0; index < StatNames.Length; index++)
                Observe(prefix + "." + (side == 0 ? "home" : "away") + "." + StatNames[index],
                    StatUnits[index], values[side][index]);
    }

    private static void ObservePreservation()
    {
        string[] names = { "tackles", "offensivePlays", "lostFumbles", "interceptions", "deflections",
            "fumbles", "bobbles", "lastReceiver", "consecutiveCompletions", "consecutiveLosses" };
        int[][] arrays = { model.SourceTackles, model.SourceOffensivePlays, model.SourceLostFumbles,
            model.SourceInterceptions, model.SourceDeflections, model.SourceFumbles, model.SourceBobbles,
            model.SourceLastReceiver, model.SourceConsecutiveCompletions, model.SourceConsecutiveLosses };
        for (int side = 0; side < 2; side++)
        {
            string prefix = "sentinels." + (side == 0 ? "home" : "away") + ".";
            for (int index = 0; index < names.Length; index++)
                Observe(prefix + names[index], index == 7 ? "actor-id" : "count", arrays[index][side]);
            Observe(prefix + "fire", "bool", model.SourceTeamFire[side]);
            Observe(prefix + "possessionTime", "seconds", side == 0
                ? model.SourceTimeOfPossession.HomeSeconds : model.SourceTimeOfPossession.AwaySeconds);
        }
        Observe("sentinels.randomDraws", "count", model.SourceRandomDraws);
        Observe("sentinels.forwardCatcher", "actor-id", model.SourceForwardCatcher);
        for (int id = 0; id < model.Players.Length; id++)
        {
            Observe("actors." + id + ".sameAnimation", "bool",
                object.ReferenceEquals(model.Players[id].AnimationState, animations[id]));
            Observe("actors." + id + ".primaryFrame", "frame", model.Players[id].AnimationState.Primary.FrameOrdinal);
            Observe("actors." + id + ".secondaryFrame", "frame", model.Players[id].AnimationState.Secondary.FrameOrdinal);
        }
    }

    private static void ObservePriorViews(GameSnapshot snapshot)
    {
        SourceMatchView match = snapshot.Match;
        Observe("prior.home.interceptions", "count", match.Interceptions.HomeInterceptionsMade);
        Observe("prior.away.interceptions", "count", match.Interceptions.AwayInterceptionsMade);
        Observe("prior.home.possessionTime", "seconds", match.TimeOfPossession.HomeSeconds);
        Observe("prior.away.possessionTime", "seconds", match.TimeOfPossession.AwaySeconds);
        Observe("prior.home.fire", "bool", match.HomeGameplay.OnFire);
        Observe("prior.away.fire", "bool", match.AwayGameplay.OnFire);
        Observe("prior.home.consecutiveCompletions", "count", match.HomeGameplay.ConsecutiveCompletions);
        Observe("prior.away.consecutiveCompletions", "count", match.AwayGameplay.ConsecutiveCompletions);
        Observe("prior.home.consecutiveLosses", "count", match.HomeGameplay.ConsecutiveLosses);
        Observe("prior.away.consecutiveLosses", "count", match.AwayGameplay.ConsecutiveLosses);
    }

    private static SourcePlayResolution Apply(JsonElement input)
    {
        var rules = new SourceRulesState(input.GetProperty("offense").GetInt32(),
            input.GetProperty("initial").GetInt32(), input.GetProperty("down").GetInt32(),
            input.GetProperty("los").GetInt32(), input.GetProperty("first").GetInt32(),
            (SourcePlayType)input.GetProperty("type").GetInt32());
        var ending = new SourcePlayEndInput(rules,
            (SourcePlayEndCause)input.GetProperty("cause").GetInt32(),
            (SourceBallType)input.GetProperty("ball").GetInt32(),
            (SourceGameFlags)input.GetProperty("flags").GetInt32(),
            input.GetProperty("last").GetInt32(), input.GetProperty("x").GetSingle(), 0f,
            input.GetProperty("fumbleX").GetSingle(),
            (SourcePlayResult)input.GetProperty("previous").GetInt32());
        model.SourceRules = rules;
        model.Possession = (TeamId)input.GetProperty("modelPossession").GetInt32();
        model.LineOfScrimmage = input.GetProperty("displayLos").GetSingle();
        model.CarrierId = input.GetProperty("carrier").GetInt32();
        model.SourceKickReturnCatch = input.GetProperty("savedCatch").GetInt32();
        SourcePlayResolution resolution = SourceMatchRules.Resolve(ending);
        if (!resolution.TransitionApplied) throw new InvalidOperationException("Invalid literal ending input.");
        model.AccumulateSourcePlayEndStats(ending, resolution, model.CarrierId);
        model.SourceRules = resolution.State;
        model.Possession = (TeamId)resolution.State.OffenseSide;
        return resolution;
    }

    private static void DispatchCase(JsonElement data)
    {
        SourcePlayResolution resolution = Apply(data.GetProperty("input"));
        Observe("classification.result", "enum", (int)resolution.Classification.Result);
        Observe("classification.side", "side", resolution.Classification.OffenseSide);
        Observe("resolution.fieldEnd", "field-yards", resolution.FieldEnd);
        if (data.GetProperty("checkRespot").GetBoolean())
            Observe("resolution.playEndX", "source-world", resolution.PlayEndX);
        if (data.GetProperty("checkNext").GetBoolean())
        {
            Observe("resolution.nextSide", "side", resolution.State.OffenseSide);
            Observe("resolution.nextLos", "field-yards", resolution.State.LineOfScrimmage);
        }
        ObserveModel(model, "model", data.GetProperty("modelOrder"));
        GameSnapshot first = new GameSnapshot(model);
        ObserveSnapshot(first, "snapshot");
        GameSnapshot second = new GameSnapshot(model);
        ObserveSnapshot(second, "snapshot2");
        ObservePreservation();
        ObservePriorViews(second);
    }

    private static void CaptureCase(JsonElement data)
    {
        model.SourceKickReturnCatch = 17;
        model.Possession = (TeamId)data.GetProperty("side").GetInt32();
        model.SourceRules = new SourceRulesState((int)model.Possession, (int)model.Possession, 1, 30, 90);
        foreach (JsonElement step in data.GetProperty("steps").EnumerateArray())
        {
            model.BallKind = (SourceBallKind)step.GetProperty("kind").GetInt32();
            model.SourceCatchX = step.GetProperty("x").GetSingle();
            model.CaptureSourceKickReturnCatch(step.GetProperty("offense").GetBoolean());
        }
        Observe("model.savedCatch", "field-yards", model.SourceKickReturnCatch);
        if (data.GetProperty("commit").ValueKind == JsonValueKind.Object)
            Apply(data.GetProperty("commit"));
        ObserveModel(model, "model", data.GetProperty("modelOrder"));
        ObserveSnapshot(new GameSnapshot(model), "snapshot");
        GameSnapshot second = new GameSnapshot(model);
        ObserveSnapshot(second, "snapshot2");
        ObservePreservation();
        ObservePriorViews(second);
    }

    private static SourcePlayEndStatsView Group(int[] a)
    {
        return new SourcePlayEndStatsView(a[0], a[1], a[2], a[3], a[4], a[5],
            a[6], a[7], a[8], a[9], a[10], a[11]);
    }

    private static SourceMatchView Extended(SourcePlayEndStatsView group,
        SourceCompletedPassStatsView completed, SourcePassCompletionsView completions,
        SourceInterceptionsView interceptions, SourceTimeOfPossessionView time,
        SourceMatchConfiguration configuration)
    {
        return new SourceMatchView(group, completed, completions, interceptions, time, configuration,
            SourcePlayKind.Normal, SourceMatchPhase.PlayResult, new SourcePeriodView(0),
            new SourcePossessionView(TeamId.Home, TeamId.Away, TeamId.Home, TeamId.Away, SourceCaptainSlot.Second),
            new[] { new SourceQuarterScore(0, 11, 13) }, new SourceStationView[0],
            new SourceMatchResult(SourcePlayKind.Normal, SourceResultKind.NewPossession,
                SourceResultStatus.Classified, SourcePlayEndKind.CarrierTackledInBounds, 2, 3, true, true),
            default(SourcePlaySelection), default(SourcePlaySelection), 17, SourcePatOption.TwoPointPlay,
            SourcePatStatus.RunTwoPointPlay, SourceKickoffType.Quick, TeamId.Away, TeamId.Home, true,
            new SourceTeamGameplayView(TeamId.Home, true, 4, 13, 15, 5, 6),
            new SourceTeamGameplayView(TeamId.Away, false, 11, 14, 16, 7, 8), 4, true, false, 2,
            SourceGameplayCompatibility.Portable, 0, null, null,
            default(SourceRandomSelectionView), default(SourceRandomSelectionView), null);
    }

    private static void ArgumentProbe(Action action)
    {
        ArgumentException error = null;
        try { action(); }
        catch (ArgumentException exception) { error = exception; }
        Observe("exception.parameter", "parameter", error == null ? null : error.ParamName);
        Observe("exception.kind", "tag", error is ArgumentNullException ? "null"
            : error is ArgumentOutOfRangeException ? "range" : error == null ? "none" : "argument");
    }

    private static void ApiCase(JsonElement data)
    {
        SourceMatchConfiguration configuration = Program.Configuration();
        var group = Group(new[] {3,4,1,2,5,6,3,4,7,8,5,6});
        var completed = new SourceCompletedPassStatsView(11, 22, 101, 203, 1, 2);
        var completions = new SourcePassCompletionsView(5, 7);
        var interceptions = new SourceInterceptionsView(5, 6);
        var time = new SourceTimeOfPossessionView(37, 41);
        string operation = data.GetProperty("operation").GetString();
        if (operation == "signed")
        {
            SourcePlayEndStatsView signed = null;
            bool accepted = true;
            try { signed = Group(new[] {-3,-4,0,0,-5,-6,0,0,-7,-8,0,0}); }
            catch (ArgumentException) { accepted = false; }
            Observe("accepts-negative-yards", "bool", accepted);
            int[] yards = { signed.HomeReturnYards, signed.AwayReturnYards,
                signed.HomeRushingYards, signed.AwayRushingYards, signed.HomeSackYards, signed.AwaySackYards };
            int[] counts = { signed.HomeKickReturns, signed.AwayKickReturns,
                signed.HomeRushes, signed.AwayRushes, signed.HomeSacks, signed.AwaySacks };
            for (int i = 0; i < 6; i++) Observe("yards." + i, "field-yards", yards[i]);
            for (int i = 0; i < 6; i++) Observe("counts." + i, "count", counts[i]);
            return;
        }
        if (operation == "negative")
        {
            int[] values = new int[12];
            values[data.GetProperty("negativeIndex").GetInt32()] = -1;
            ArgumentProbe(() => Group(values));
            return;
        }
        if (operation == "null")
        {
            int operand = data.GetProperty("nullOperand").GetInt32();
            ArgumentProbe(() => Extended(operand == 0 ? null : group,
                operand == 1 ? null : completed, operand == 2 ? null : completions,
                operand == 3 ? null : interceptions, operand == 4 ? null : time,
                operand == 5 ? null : configuration));
            return;
        }
        if (operation == "old")
        {
            int overload = data.GetProperty("overload").GetInt32();
            var period = new SourcePeriodView(0);
            var possession = new SourcePossessionView(TeamId.Home, TeamId.Home,
                TeamId.Home, TeamId.Home, SourceCaptainSlot.First);
            var scores = new[] { new SourceQuarterScore(0, 11, 13) };
            var stations = new SourceStationView[0];
            SourceMatchView old;
            switch (overload)
            {
                case 1: old = new SourceMatchView(configuration, SourcePlayKind.Normal,
                    SourceMatchPhase.PlayResult, period, possession, scores, stations); break;
                case 2: old = new SourceMatchView(time, configuration, SourcePlayKind.Normal,
                    SourceMatchPhase.PlayResult, period, possession, scores, stations); break;
                case 3: old = new SourceMatchView(interceptions, time, configuration, SourcePlayKind.Normal,
                    SourceMatchPhase.PlayResult, period, possession, scores, stations); break;
                case 4: old = new SourceMatchView(completions, interceptions, time, configuration,
                    SourcePlayKind.Normal, SourceMatchPhase.PlayResult, period, possession, scores, stations); break;
                case 5: old = new SourceMatchView(completed, completions, interceptions, time, configuration,
                    SourcePlayKind.Normal, SourceMatchPhase.PlayResult, period, possession, scores, stations); break;
                default: throw new InvalidOperationException("Unknown old overload.");
            }
            Observe("newGroupNull", "bool", old.PlayEndStats == null);
            Observe("completedSame", "bool", object.ReferenceEquals(old.CompletedPassStats, completed));
            Observe("completionsSame", "bool", object.ReferenceEquals(old.PassCompletions, completions));
            Observe("interceptionsSame", "bool", object.ReferenceEquals(old.Interceptions, interceptions));
            Observe("timeSame", "bool", object.ReferenceEquals(old.TimeOfPossession, time));
            Observe("configurationSame", "bool", object.ReferenceEquals(old.Configuration, configuration));
            Observe("score.home", "points", old.QuarterScores[0].HomeScore);
            Observe("score.away", "points", old.QuarterScores[0].AwayScore);
            Observe("stations", "count", old.Stations.Count);
            Observe("selectionCountdown", "ticks", old.SelectionCountdown);
            return;
        }
        if (operation != "forward") throw new InvalidOperationException("Unknown API literal.");
        SourceMatchView view = Extended(group, completed, completions, interceptions, time, configuration);
        Observe("newGroupSame", "bool", object.ReferenceEquals(view.PlayEndStats, group));
        Observe("completedSame", "bool", object.ReferenceEquals(view.CompletedPassStats, completed));
        Observe("completionsSame", "bool", object.ReferenceEquals(view.PassCompletions, completions));
        Observe("interceptionsSame", "bool", object.ReferenceEquals(view.Interceptions, interceptions));
        Observe("timeSame", "bool", object.ReferenceEquals(view.TimeOfPossession, time));
        Observe("configurationSame", "bool", object.ReferenceEquals(view.Configuration, configuration));
        Observe("playKind", "enum", (int)view.PlayKind);
        Observe("phase", "enum", (int)view.Phase);
        Observe("period", "period", view.Period.QuarterIndex);
        Observe("possession.offense", "side", (int)view.Possession.Offense);
        Observe("possession.initial", "side", (int)view.Possession.InitialOffense);
        Observe("possession.last", "side", (int)view.Possession.LastPossession);
        Observe("possession.first", "side", (int)view.Possession.FirstPossession);
        Observe("possession.captain", "enum", (int)view.Possession.Captain);
        Observe("score.home", "points", view.QuarterScores[0].HomeScore);
        Observe("score.away", "points", view.QuarterScores[0].AwayScore);
        Observe("stations", "count", view.Stations.Count);
        Observe("result.kind", "enum", (int)view.Result.Kind);
        Observe("result.homePoints", "points", view.Result.HomePoints);
        Observe("result.awayPoints", "points", view.Result.AwayPoints);
        Observe("result.firstDown", "bool", view.Result.JustGotFirstDown);
        Observe("result.turnover", "bool", view.Result.TurnoverOnDowns);
        Observe("homePlaySpecified", "bool", view.HomePlay.IsSpecified);
        Observe("awayPlaySpecified", "bool", view.AwayPlay.IsSpecified);
        Observe("selectionCountdown", "ticks", view.SelectionCountdown);
        Observe("patChoice", "enum", (int)view.PatChoice);
        Observe("patStatus", "enum", (int)view.PatStatus);
        Observe("kickoff", "enum", (int)view.Kickoff.Value);
        Observe("kickingTeam", "side", (int)view.KickingTeam.Value);
        Observe("receivingTeam", "side", (int)view.ReceivingTeam.Value);
        Observe("flipped", "bool", view.OffenseFlipped.Value);
        Observe("homeGameplay.handicap", "index", view.HomeGameplay.Handicap);
        Observe("awayGameplay.handicap", "index", view.AwayGameplay.Handicap);
        Observe("homeGameplay.effective", "index", view.HomeGameplay.EffectiveHandicap);
        Observe("awayGameplay.effective", "index", view.AwayGameplay.EffectiveHandicap);
        Observe("forwardCatcher", "actor-id", view.ForwardCatcherPlayerId);
        Observe("dronesLose", "bool", view.DronesLose);
        Observe("dronesWinning", "bool", view.DronesWinning);
        Observe("repetition", "count", view.HandicapRepetitionBonus);
        Observe("compatibility", "enum", (int)view.Compatibility);
        Observe("repairCount", "count", view.CompatibilityRepairCount);
        Observe("repairs", "count", view.CompatibilityRepairs.Count);
        Observe("selectionTimerNull", "bool", view.SelectionTimer == null);
        Observe("audiblesNull", "bool", view.Audibles == null);
    }

    private static void SupportCase(JsonElement data)
    {
        string operation = data.GetProperty("operation").GetString();
        JsonElement order = data.GetProperty("modelOrder");
        if (operation == "copy")
        {
            PutVectors(model, data.GetProperty("initial"));
            GameSnapshot old = new GameSnapshot(model);
            PutVectors(model, data.GetProperty("changed"));
            Apply(data.GetProperty("input"));
            ObserveSnapshot(old, "oldSnapshot");
            ObserveModel(model, "model", order);
            GameSnapshot next = new GameSnapshot(model);
            ObserveSnapshot(next, "snapshot");
            ObservePreservation();
            ObservePriorViews(next);
        }
        else if (operation == "read")
        {
            GameSnapshot first = new GameSnapshot(model);
            GameSnapshot second = new GameSnapshot(model);
            ObserveModel(model, "model", order, true);
            ObserveSnapshot(first, "snapshot");
            ObserveSnapshot(second, "snapshot2");
            Observe("model.savedCatch", "field-yards", model.SourceKickReturnCatch);
            ObservePreservation();
            ObservePriorViews(second);
        }
        else if (operation == "fresh")
        {
            PutVectors(model, data.GetProperty("old"));
            var fresh = new SimulationState();
            int[][] oldArrays = Arrays(model), newArrays = Arrays(fresh);
            for (int i = 0; i < 6; i++)
            {
                Observe("fresh." + i + ".length", "count", newArrays[i].Length);
                Observe("fresh." + i + ".separateOld", "bool", !object.ReferenceEquals(oldArrays[i], newArrays[i]));
                for (int j = i + 1; j < 6; j++)
                    Observe("fresh." + i + ".separate." + j, "bool", !object.ReferenceEquals(newArrays[i], newArrays[j]));
            }
            ObserveModel(fresh, "freshModel", order);
            ObserveModel(model, "oldModel", order);
            ObservePreservation();
        }
        else if (operation == "legacy")
        {
            GameSnapshot neutral = new GameSnapshot(new SimulationState());
            Observe("legacy.matchNull", "bool", neutral.Match == null);
            Observe("legacy.players", "count", neutral.Players.Count);
        }
        else throw new InvalidOperationException("Unknown support literal.");
    }

    private static void MixedCase(JsonElement data)
    {
        JsonElement order = data.GetProperty("modelOrder");
        PutVectors(model, data.GetProperty("initial"));
        var retained = new List<GameSnapshot> { new GameSnapshot(model) };
        int index = 0;
        foreach (JsonElement stage in data.GetProperty("stages").EnumerateArray())
        {
            index++;
            SourcePlayResolution resolution = Apply(stage.GetProperty("input"));
            Observe("stage" + index + ".result", "enum", (int)resolution.Classification.Result);
            Observe("stage" + index + ".fieldEnd", "field-yards", resolution.FieldEnd);
            ObserveModel(model, "stage" + index + ".model", order);
            GameSnapshot snapshot = new GameSnapshot(model);
            retained.Add(snapshot);
            ObserveSnapshot(snapshot, "stage" + index + ".snapshot");
        }
        for (int i = 0; i < retained.Count; i++)
            ObserveSnapshot(retained[i], "retained" + i);
        ObservePreservation();
        ObservePriorViews(retained[retained.Count - 1]);
    }

    private static void Report(string status, string failure = null, string error = null)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "source-remaining-play-end-component/v1", status, cases = Cases,
            fieldCount = Fields.Count, fields = Fields, failure, error,
            fixture = Program.FixtureDiagnostics(), genuineSimulationExecuted = false
        }));
    }

    private static int Main(string[] args)
    {
        try
        {
            string inventory = null, selected = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--inventory" && i + 1 < args.Length) inventory = args[++i];
                else if (args[i] == "--case" && i + 1 < args.Length) selected = args[++i];
                else throw new ArgumentException("Unknown component argument.");
            }
            if (inventory == null) throw new ArgumentException("A frozen literal inventory is required.");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(inventory)))
            {
                model = Program.ComponentState();
                animations = new SourceAnimationState[model.Players.Length];
                for (int i = 0; i < animations.Length; i++) animations[i] = model.Players[i].AnimationState;
                foreach (JsonElement data in document.RootElement.GetProperty("cases").EnumerateArray())
                {
                    string name = data.GetProperty("name").GetString();
                    if (selected != null && selected != name) continue;
                    Reset();
                    current = data;
                    currentName = name;
                    fieldIndex = 0;
                    Cases.Add(name);
                    switch (data.GetProperty("kind").GetString())
                    {
                        case "dispatch": DispatchCase(data); break;
                        case "capture": CaptureCase(data); break;
                        case "api": ApiCase(data); break;
                        case "support": SupportCase(data); break;
                        case "mixed": MixedCase(data); break;
                        default: throw new InvalidOperationException("Unknown literal case kind.");
                    }
                    if (fieldIndex != data.GetProperty("fields").GetArrayLength())
                        throw new InvalidOperationException("Missing frozen fields: " + name);
                }
                if (Cases.Count == 0) throw new ArgumentException("Selected case is absent.");
                Report("passed");
                return 0;
            }
        }
        catch (NamedAssertion failure)
        {
            Report("assertion-failed", failure.Field);
            return 2;
        }
        catch (Exception error)
        {
            Report("error", error: error.ToString());
            return 3;
        }
    }
}
