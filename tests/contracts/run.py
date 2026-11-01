#!/usr/bin/env python3
"""Run actual-source DTO/rules checks and isolated offending-field mutations."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid


SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
RULE_SOURCES = ("SourceClock.cs", "SourceMatchRules.cs", "SourceMatchFlow.cs")
HUMAN_ONSIDE_CONTRACT = "source-human-onside-held-gesture/v1"
RETAINED_ASSERTIONS = 511
EXPECTED = {
    "promoted_dto_assertions": 177,
    "enum_mappings": 65,
    "additional_dto_assertions": 23,
    "presentation_dto_assertions": 189,
    "rules_flow_assertions": 122,
    "retained_assertions": 511,
    "human_onside_cases": 88,
    "human_onside_fields_per_case": 14,
    "human_onside_tuple_assertions": 1232,
    "human_onside_api_guard_assertions": 1,
    "human_onside_assertions": 1233,
    "assertions": 1744,
}
MUTATIONS = (
    ("roster-array-alias", "SourceConfigurationContracts.cs",
     "RosterIndices = Array.AsReadOnly(copy);",
     "RosterIndices = Array.AsReadOnly((int[])rosterIndices);",
     "SourceTeamSelection.RosterIndices detached"),
    ("score-array-alias", "SourceMatchContracts.cs",
     "QuarterScores = Array.AsReadOnly(scoresCopy);",
     "QuarterScores = Array.AsReadOnly((SourceQuarterScore[])quarterScores);",
     "SourceMatchView.QuarterScores detached per-period deltas"),
    ("selected-ordinal-leak", "SourceAnimationContracts.cs",
     "int frame = clip.SelectedSourceFrames[frameOrdinal];",
     "int frame = frameOrdinal;",
     "selected ordinals 1/2 map to authoring indices 3/5"),
    ("delayed-controller", "SourceInputContracts.cs",
     "public int ControllerPlayerId { get { return IsSpecified ? controllerPlayerId : -1; } }",
     "public int ControllerPlayerId { get { return IsSpecified && PossessionCreditEligible ? controllerPlayerId : -1; } }",
     "controller ownership independent from last_pos/credit duration"),
    ("nan-guard-removed", "SourceConfigurationContracts.cs",
     "if (float.IsNaN(value) || float.IsInfinity(value))",
     "if (float.IsInfinity(value))",
     "non-finite station turbo rejected"),
    ("flip-false-collapsed", "SourceInputContracts.cs",
     "OffenseFlipped = offenseFlipped;",
     "OffenseFlipped = offenseFlipped == true ? (bool?)true : null;",
     "SourceCommand.OffenseFlipped explicit false is specified and not unknown"),
    ("flip-only-unspecified", "SourceInputContracts.cs",
     "OffenseFlipped = offenseFlipped;\n            IsSpecified = true;",
     "OffenseFlipped = offenseFlipped;\n            IsSpecified = action != SourceAction.None;",
     "flip-only true command remains specified with SourceAction.None"),
    ("station-head-array-alias", "SourcePresentationContracts.cs",
     "StationHeads = Array.AsReadOnly(copy);",
     "StationHeads = Array.AsReadOnly((int[])stationHeads);",
     "SourcePresentationSettings.StationHeads detached station 0"),
    ("designated-home-side-forced", "SourcePresentationContracts.cs",
     "DesignatedHomeSide = designatedHomeSide;",
     "DesignatedHomeSide = TeamId.Home;",
     "SourcePresentationSettings.DesignatedHomeSide is source side1 not catalog index"),
    ("big-ball-int-collapsed", "SourcePresentationContracts.cs",
     "PupBigBall = pupBigBall;",
     "PupBigBall = pupBigBall == 0 ? 0 : 1;",
     "SourcePresentationSettings.PupBigBall retains whole source int"),
    ("actor-draw-flags-masked", "SourcePresentationContracts.cs",
     "RawFlags = rawFlags;\n            this.rawPlayerMode",
     "RawFlags = rawFlags & 5;\n            this.rawPlayerMode",
     "SourceActorPresentationView.RawFlags preserves helmet and unrelated signed bits"),
    ("actor-mode-replaced-with-role", "SourcePresentationContracts.cs",
     "this.rawPlayerMode = rawPlayerMode;",
     "this.rawPlayerMode = rawPlayerMode == 5 ? (int)ActorRole.Quarterback : rawPlayerMode;",
     "SourceActorPresentationView retains original player mode 5"),
    ("ball-phantom-dropped", "SourcePresentationContracts.cs",
     "public SourceBallPresentationView(int rawFlags)\n        {\n            RawFlags = rawFlags;",
     "public SourceBallPresentationView(int rawFlags)\n        {\n            RawFlags = rawFlags & ~0x800;",
     "SourceBallPresentationView.RawFlags preserves phantom independently of BallMode"),
    ("cue-array-alias", "SourcePresentationContracts.cs",
     "Cues = Array.AsReadOnly(copy);",
     "Cues = Array.AsReadOnly((SourcePresentationCue[])cues);",
     "SourcePresentationCueBatch.Cues detached from caller array"),
    ("duplicate-cue-order-allowed", "SourcePresentationContracts.cs",
     "current.ProcessOrder <= previous.ProcessOrder",
     "current.ProcessOrder < previous.ProcessOrder",
     "SourcePresentationCueBatch rejects duplicate within-tick instruction order"),
    ("cue-source-location-shifted", "SourcePresentationContracts.cs",
     "this.sourceLine = sourceLine;",
     "this.sourceLine = sourceLine + 1;",
     "SourcePresentationContext preserves exact executed script source location"),
    ("csi-ordinal-count-inclusive", "SourcePresentationContracts.cs",
     "SourceContractGuard.Range(selectedOrdinal, 0, entryCount - 1, nameof(selectedOrdinal));",
     "SourceContractGuard.Range(selectedOrdinal, 0, entryCount, nameof(selectedOrdinal));",
     "SourceCameraSelection rejects ordinal at actual entry count"),
    ("csi-selected-ordinal-collapsed", "SourcePresentationContracts.cs",
     "this.selectedOrdinal = selectedOrdinal;",
     "this.selectedOrdinal = 0;",
     "weighted CSI ordinals remain distinct without deduplication"),
    ("randrng1-raw-draw-dropped", "SourcePresentationContracts.cs",
     "RawRandomValue = rawRandomValue;",
     "RawRandomValue = entryCount > 1 ? rawRandomValue : null;",
     "randrng1 raw draw is not discarded because selected ordinal is0"),
    ("noncarrier-shaker-assignment-filtered", "SourcePresentationContracts.cs",
     "Amplitude = amplitude;",
     "Amplitude = context.ActorPlayerId == carrierPlayerId ? amplitude : 0;",
     "noncarrier shaker retains pre-guard amplitude total and actual carrier"),
    ("callback-minus1-clamped", "SourcePresentationContracts.cs",
     "Argument0 = argument0;",
     "Argument0 = Math.Max(0, argument0);",
     "original grnd_hit_snd arguments preserve both minus1 sentinels"),
)
ONSIDE_MUTATIONS = (
    ("onside-held-replaced-by-pressed", "SourceMatchFlow.cs",
     "(current.Held & chord) == chord", "(current.Pressed & chord) == chord",
     "human-onside/core/Two/Home/First/first-pos-Home/held-up/Kind: expected Onside, actual Quick"),
    ("onside-all-buttons-replaced-by-any", "SourceMatchFlow.cs",
     "(current.Held & chord) == chord", "(current.Held & chord) != SourceButtons.None",
     "human-onside/edge/02-four-split-buttons-AB-and-C/Kind: expected Quick, actual Onside"),
    ("onside-up-equality-replaced-by-inclusion", "SourceMatchFlow.cs",
     "current.DigitalCurrent == SourceDigitalDirections.Up",
     "(current.DigitalCurrent & SourceDigitalDirections.Up) == SourceDigitalDirections.Up",
     "human-onside/core/Two/Home/First/first-pos-Home/diagonal-up-left/Kind: expected Quick, actual Onside"),
    ("onside-kicking-team-filter-removed", "SourceMatchFlow.cs",
     "if (station.Team != (TeamId)input.State.OffenseSide)\n                        continue;", "",
     "human-onside/core/Two/Home/First/first-pos-Home/receiver-only-up/Kind: expected Quick, actual Onside"),
    ("onside-team-replaced-by-station-id", "SourceMatchFlow.cs",
     "station.Team != (TeamId)input.State.OffenseSide", "station.StationId != input.State.OffenseSide",
     "human-onside/core/Four/Away/First/first-pos-Home/held-up/Kind: expected Onside, actual Quick"),
    ("onside-second-teammate-ignored", "SourceMatchFlow.cs",
     "station.Team != (TeamId)input.State.OffenseSide",
     "station.Team != (TeamId)input.State.OffenseSide\n"
     "                        || (configuration.StationLayout == SourceStationLayout.FourStation && station.StationId % 2 != 0)",
     "human-onside/edge/01-four-second-teammate-noncaptain-held-up/Kind: expected Onside, actual Quick"),
    ("onside-stored-request-discarded", "SourceMatchFlow.cs",
     "input.RequestedOnsideKick || rawOnside", "rawOnside",
     "human-onside/edge/13-legacy-stored-true-null-frame/Kind: expected Onside, actual Quick"),
    ("onside-half-argument-discarded", "SourceMatchFlow.cs",
     "input.PackedTime, input.FirstKickoff, input.HalfKickoff, input.FirstPossessionSide,",
     "input.PackedTime, input.FirstKickoff, false, input.FirstPossessionSide,",
     "human-onside/edge/19-half-kickoff-raw-neutral-stored-true/Kind: expected Quick, actual Onside"),
    ("onside-first-argument-discarded", "SourceMatchFlow.cs",
     "input.PackedTime, input.FirstKickoff, input.HalfKickoff, input.FirstPossessionSide,",
     "input.PackedTime, false, input.HalfKickoff, input.FirstPossessionSide,",
     "human-onside/edge/16-first-kickoff-raw-chord-stored-false/Kind: expected Opening, actual Quick"),
)
COMPILED_MUTATIONS = MUTATIONS + ONSIDE_MUTATIONS
# Total checks, partial tuple checks (including the failure), completed tuples.
ONSIDE_FAILURE_COUNTS = {
    "onside-held-replaced-by-pressed": (512, 1, 0),
    "onside-all-buttons-replaced-by-any": (1422, 911, 65),
    "onside-up-equality-replaced-by-inclusion": (540, 29, 2),
    "onside-kicking-team-filter-removed": (554, 43, 3),
    "onside-team-replaced-by-station-id": (1184, 673, 48),
    "onside-second-teammate-ignored": (1408, 897, 64),
    "onside-stored-request-discarded": (1576, 1065, 76),
    "onside-half-argument-discarded": (1660, 1149, 82),
    "onside-first-argument-discarded": (1618, 1107, 79),
}
CALLER_SOURCE = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
FLOW_SOURCE = "Assets/Scripts/Gameplay/SourceMatchFlow.cs"
CALLER_PREEDIT_SHA256 = "726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091"
LEGACY_KICKOFF_BODY_SHA256 = "21ae41b62e3d6378509e624a68f98a49ba13b19057069732a41ba110a37ebb99"
LEGACY_CALL = (
    "SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(\n"
    "                Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, first, half,\n"
    "                Model.SourceFirstPossession, (int)Model.SourceCaptain, onside))"
)
CURRENT_CALL = (
    "SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(\n"
    "                Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, first, half,\n"
    "                Model.SourceFirstPossession, (int)Model.SourceCaptain, onside), "
    "sourceConfiguration, Model.SourceCurrentInput.StationInputs)"
)
CURRENT_ASSIGNMENT = "            Model.SourceCurrentInput = command;\n"
LATE_ASSIGNMENT_ANCHOR = "            if (Model.Phase == GamePhase.PreSnap && Model.SourceAudibleState != null)\n"
CPU_GUARD = "if (!first && !SourceContactDriver.IsHumanTeam(Model, Model.Possession))"
STATIC_CONTROLS = (
    ("caller-overload-omitted", CURRENT_CALL, LEGACY_CALL, "caller.plan.three-argument"),
    ("caller-frame-null", CURRENT_CALL, CURRENT_CALL.replace("Model.SourceCurrentInput.StationInputs", "null"),
     "caller.plan.current-frame-not-null"),
    ("caller-frame-stale", CURRENT_CALL, CURRENT_CALL.replace("Model.SourceCurrentInput.StationInputs", "sourceDigitalHeld"),
     "caller.plan.transformed-current-frame"),
    ("caller-current-assignment-late", CURRENT_ASSIGNMENT, LATE_ASSIGNMENT_ANCHOR,
     "caller.current-input.before-phase-dispatch"),
    ("caller-CPU-half-shortcut", CPU_GUARD, CPU_GUARD.replace("!first &&", "!first && !half &&"),
     "caller.cpu.guard-without-half-shortcut"),
)
CSHARP_TOKEN = re.compile(
    r"(?P<space>\s+)|(?P<comment>//[^\n]*|/\*[\s\S]*?\*/)"
    r'|(?P<literal>\$?@"(?:""|[^"])*"|(?:\$)?"(?:\\[\s\S]|[^"\\])*"'
    r"|'(?:\\[\s\S]|[^'\\])*')"
    r"|(?P<token>[A-Za-z_][A-Za-z_0-9]*|0[xX][0-9A-Fa-f]+|\d+|==|!=|<=|>=|&&|\|\||=>|\S)"
)


class StaticAssertionFailure(RuntimeError):
    def __init__(self, field):
        super().__init__(field)
        self.field = field


def static_check(condition, field):
    if not condition:
        raise StaticAssertionFailure(field)


def csharp_tokens(text):
    return [(match.group(), match.start(), match.end()) for match in CSHARP_TOKEN.finditer(text)
            if match.lastgroup not in ("space", "comment")]


def token_values(text):
    return [token[0] for token in csharp_tokens(text)]


def token_locations(tokens, expression):
    values = [token[0] for token in tokens]
    wanted = token_values(expression)
    return [index for index in range(len(values) - len(wanted) + 1)
            if values[index:index + len(wanted)] == wanted]


def matching_token(tokens, opening, left, right):
    depth = 0
    for index in range(opening, len(tokens)):
        if tokens[index][0] == left:
            depth += 1
        elif tokens[index][0] == right:
            depth -= 1
            if depth == 0:
                return index
    raise StaticAssertionFailure("caller.syntax.balanced-tokens")


def method_region(text, name, parameters):
    tokens = csharp_tokens(text)
    matches = token_locations(tokens, name + "(" + parameters + ") {")
    static_check(len(matches) == 1, "caller.method.unique-" + name)
    opening = matches[0] + len(token_values(name + "(" + parameters + ")"))
    closing = matching_token(tokens, opening, "{", "}")
    start = text.rfind("\n", 0, tokens[matches[0]][1]) + 1
    body = text.rfind("\n", 0, tokens[opening][1]) + 1
    end = text.find("\n", tokens[closing][2])
    return start, len(text) if end == -1 else end + 1, body


def method_text(text, name, parameters):
    start, end, _ = method_region(text, name, parameters)
    return text[start:end]


def plan_call(setup):
    tokens = csharp_tokens(setup)
    calls = token_locations(tokens, "SourceMatchFlow.PrepareKickoff(")
    static_check(len(calls) == 1, "caller.plan.single-actual-call")
    index = calls[0]
    static_check([token[0] for token in tokens[index - 3:index]] == ["SourceKickoffPlan", "plan", "="],
                 "caller.plan.assigned-to-actual-plan")
    opening = index + 3
    closing = matching_token(tokens, opening, "(", ")")
    arguments, current, depth = [], [], 0
    for token, _, _ in tokens[opening + 1:closing]:
        if token == "," and depth == 0:
            arguments.append(current)
            current = []
            continue
        current.append(token)
        if token in ("(", "[", "{"):
            depth += 1
        elif token in (")", "]", "}"):
            depth -= 1
    arguments.append(current)
    return arguments, setup[tokens[index][1]:tokens[closing][2]]


def _touchdown_inverse(raw, edits, size, digest, field):
    restored = raw
    for addition, previous in edits:
        static_check(restored.count(addition) == 1, field)
        restored = restored.replace(addition, previous, 1)
    encoded = restored.encode("utf-8")
    static_check(len(encoded) == size and hashlib.sha256(encoded).hexdigest() == digest, field)


def require_touchdown_stats_additions(caller):
    caller_anchor = (
        "            SourcePlayResolution resolution = SourceMatchRules.Resolve(endingInput);\n"
        "            if (!resolution.TransitionApplied)\n"
        "                throw new NotSupportedException(\"Invalid source result branch: play=\" + Model.SourceRules.PlayType\n"
        "                    + \" cause=\" + cause + \" kind=\" + Model.BallKind + \" sourceTick=\" + Model.SourceTick);\n"
        "            ApplySourceFirePlayEnd(endingInput, resolution);\n"
        "            Model.AccumulateSourcePlayEndStats(endingInput, resolution, Model.CarrierId);\n"
        "            Model.SourceLastResult = resolution.Classification.Result;\n"
    )
    common = method_text(caller, "ApplySourcePlayEnd",
        "SourcePlayEndCause cause, int playerId, FieldVector position, "
        "int attackerId, SourceC2PendingEnd pending = null")
    static_check(caller.count(caller_anchor) == common.count(caller_anchor) == 1
                 and len(token_locations(csharp_tokens(caller),
                     "Model.AccumulateSourcePlayEndStats(endingInput, resolution, Model.CarrierId);")) == 1,
                 "touchdown_caller_wiring")
    state = (ROOT / "Assets/Scripts/Gameplay/SimulationState.cs").read_bytes().decode("utf-8")
    _touchdown_inverse(state, (
        ("        internal readonly int[] SourceTouchdowns = new int[2];\n", ""),
        ("            if (result == SourcePlayResult.Touchdown)\n"
         "                SourceTouchdowns[side] = checked(SourceTouchdowns[side] + 1);\n", ""),
    ), 29456, "29e7f22f50c4f648715a9adf7df7198773f5ec7ca3394ca52047b4b586294799",
        "touchdown_owner_inverse")
    group = """    public sealed class SourceTouchdownStatsView
    {
        public int HomeTouchdowns { get; }
        public int AwayTouchdowns { get; }

        public SourceTouchdownStatsView(int homeTouchdowns, int awayTouchdowns)
        {
            if (homeTouchdowns < 0)
                throw new ArgumentOutOfRangeException(nameof(homeTouchdowns));
            if (awayTouchdowns < 0)
                throw new ArgumentOutOfRangeException(nameof(awayTouchdowns));
            HomeTouchdowns = homeTouchdowns;
            AwayTouchdowns = awayTouchdowns;
        }
    }

"""
    constructor = """        public SourceMatchView(SourceTouchdownStatsView touchdownStats,
            SourceFourthDownAttemptsView fourthDownAttempts,
            SourceResultProgressionStatsView resultProgressionStats,
            SourcePlaceKickStatsView placeKickStats, SourcePlayEndStatsView playEndStats,
            SourceCompletedPassStatsView completedPassStats, SourcePassCompletionsView passCompletions,
            SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(fourthDownAttempts, resultProgressionStats, placeKickStats, playEndStats,
                completedPassStats, passCompletions, interceptions, timeOfPossession, configuration,
                playKind, phase, period, possession, quarterScores, stations, result, homePlay,
                awayPlay, selectionCountdown, patChoice, patStatus, kickoff, kickingTeam,
                receivingTeam, offenseFlipped, homeGameplay, awayGameplay, forwardCatcherPlayerId,
                dronesLose, dronesWinning, handicapRepetitionBonus, compatibility,
                compatibilityRepairCount, compatibilityRepairs, selectionTimer, homeRandomSelection,
                awayRandomSelection, audibles)
        {
            if (touchdownStats == null)
                throw new ArgumentNullException(nameof(touchdownStats));
            TouchdownStats = touchdownStats;
        }

"""
    contract = (ROOT / "Assets/Scripts/Contracts/SourceMatchContracts.cs").read_bytes().decode("utf-8")
    _touchdown_inverse(contract, (
        (group, ""), ("        public SourceTouchdownStatsView TouchdownStats { get; }\n", ""),
        (constructor, ""),
    ), 106285, "c34ccadde9942ed33d0f26b05cee4ca827211629a56926016dc69fb6b05bce69",
        "touchdown_contract_inverse")
    previous = "                Match = new SourceMatchView(new SourceFourthDownAttemptsView(\n"
    current = (
        "                Match = new SourceMatchView(new SourceTouchdownStatsView(\n"
        "                    model.SourceTouchdowns[(int)TeamId.Home],\n"
        "                    model.SourceTouchdowns[(int)TeamId.Away]),\n"
        "                    new SourceFourthDownAttemptsView(\n"
    )
    snapshot = (ROOT / "Assets/Scripts/Gameplay/GameSnapshot.cs").read_bytes().decode("utf-8")
    _touchdown_inverse(snapshot, ((current, previous),), 30181,
        "7f3fd96a9ea9917f40757f80df786a26a668999320e5721e980865102b290187",
        "touchdown_snapshot_wiring")
    return caller


def restore_place_kick_stats_caller(caller):
    before = (
        "            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(\n"
        "                Model.SourceRules, cause, SourceBallTypeFor(Model.BallKind), Model.SourceFlags,\n"
    )
    after = (
        "            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(\n"
        "                Model.SourcePlaybook, Model.SourceOffensePlay, Model.SourceRules, cause,\n"
        "                SourceBallTypeFor(Model.BallKind), Model.SourceFlags,\n"
    )
    common = method_text(caller, "ApplySourcePlayEnd",
        "SourcePlayEndCause cause, int playerId, FieldVector position, "
        "int attackerId, SourceC2PendingEnd pending = null")
    shape = "new SourcePlayEndInput(Model.SourcePlaybook, Model.SourceOffensePlay,"
    static_check(caller.count(after) == 1 and common.count(after) == 1
                 and len(token_locations(csharp_tokens(caller), shape)) == 1,
                 "caller.place-kick.exact-ending-input-identities")
    restored = caller.replace(after, before)
    static_check(len(restored.encode("utf-8")) == 157533 and
                 hashlib.sha256(restored.encode("utf-8")).hexdigest() ==
                 "7c72a709e5510f7580cae8d623201f454ea1f747acdd0687ea1941cee57d4f72",
                 "caller.place-kick.only-authorized-ending-input-edit")
    return restored


def restore_play_end_stats_caller(caller):
    grouped_call = "Model.AccumulateSourcePlayEndStats(endingInput, resolution, Model.CarrierId);"
    current_call = "Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);"
    commit = (
        "            ApplySourceFirePlayEnd(endingInput, resolution);\n"
        "            " + grouped_call + "\n"
        "            Model.SourceLastResult = resolution.Classification.Result;\n"
    )
    common = method_text(caller, "ApplySourcePlayEnd",
        "SourcePlayEndCause cause, int playerId, FieldVector position, "
        "int attackerId, SourceC2PendingEnd pending = null")
    static_check(caller.count(commit) == 1 and common.count(commit) == 1
                 and len(token_locations(csharp_tokens(caller), grouped_call)) == 1,
                 "caller.play-end-stats.exact-common-commit")
    kick_before = (
        "            Model.SourceBallFrozen = false;\n"
        "            Model.SourceFlags |= SourceGameFlags.BallKicked;\n"
    )
    kick_after = kick_before.replace(
        "            Model.SourceFlags",
        "            Model.SourceKickReturnCatch = -1;\n            Model.SourceFlags")
    kick = method_text(caller, "BeginSourceKick", "")
    static_check(caller.count(kick_after) == 1 and kick.count(kick_after) == 1,
                 "caller.play-end-stats.kick-reset")
    punt_before = "            Model.SourceFlags |= SourceGameFlags.BallPunted;\n"
    punt_after = punt_before + "            Model.SourceKickReturnCatch = -1;\n"
    punt = method_text(caller, "ReleaseSourcePunt", "int actorId")
    static_check(caller.count(punt_after) == 1 and punt.count(punt_after) == 1
                 and len(token_locations(csharp_tokens(caller),
                     "Model.SourceKickReturnCatch = -1;")) == 2,
                 "caller.play-end-stats.punt-reset")
    capture_before = (
        "            Model.ClockActive = Model.SourceRules.PlayType != SourcePlayType.ExtraPoint;\n"
        "            int field = SourceMatchRules.FieldPosition(Model.SourceCatchX, (int)Model.Possession);\n"
    )
    capture_line = "            Model.CaptureSourceKickReturnCatch(offense);\n"
    capture_after = capture_line + capture_before
    catch = method_text(caller, "TrySourceCatch", "ActorState actor")
    tokens = csharp_tokens(catch)
    change = token_locations(tokens, "if (actor.Team != Model.Possession)")
    capture = token_locations(tokens, "Model.CaptureSourceKickReturnCatch(offense);")
    static_check(len(change) == 1 and len(capture) == 1
                 and len(token_locations(csharp_tokens(caller),
                     "Model.CaptureSourceKickReturnCatch(offense);")) == 1
                 and caller.count(capture_after) == 1 and catch.count(capture_after) == 1,
                 "caller.play-end-stats.capture-post-possession")
    opening = change[0] + len(token_values("if (actor.Team != Model.Possession)"))
    closing = matching_token(tokens, opening, "{", "}")
    static_check(tokens[opening][0] == "{" and capture[0] > closing,
                 "caller.play-end-stats.capture-post-possession")
    restored = caller.replace(commit, commit.replace(grouped_call, current_call))
    restored = restored.replace(kick_after, kick_before)
    restored = restored.replace(punt_after, punt_before)
    restored = restored.replace(capture_after, capture_before)
    static_check(len(restored.encode("utf-8")) == 157386 and
                 hashlib.sha256(restored.encode("utf-8")).hexdigest() ==
                 "ddb34cac693331d566bdbca855f08fce92599952ef10a2751017a657188a73dd",
                 "caller.play-end-stats.only-four-authorized-edits")
    return restored


def restore_fourth_down_attempts_caller(caller):
    call = (
        "            Model.AccumulateSourceFourthDownAttemptAtSelectionReturn("
        "sourcePlaybook, Model.SourceOffensePlay);\n"
    )
    expression = (
        "Model.AccumulateSourceFourthDownAttemptAtSelectionReturn("
        "sourcePlaybook, Model.SourceOffensePlay);"
    )
    static_check(caller.count(call) == 1
                 and len(token_locations(csharp_tokens(caller), expression)) == 1,
                 "attempts.caller.unique-selection-return-hook")
    anchor = (
        "            Model.SourceDefensePlay = sourcePlaybook.GetPlay(Model.Possession == TeamId.Home\n"
        "                ? Model.SourceAwayPlay.PlaySymbol : Model.SourceHomePlay.PlaySymbol);\n"
        + call +
        "            SourcePlayType type = (Model.SourceOffensePlay.Flags & SourcePlayFlags.Punt) != 0 ? SourcePlayType.Punt\n"
    )
    selection = method_text(caller, "FinishSourcePlaySelection", "TeamId selectingSide")
    static_check(caller.count(anchor) == 1 and selection.count(anchor) == 1,
                 "attempts.caller.selection-return-before-type")
    restored = caller.replace(call, "", 1)
    static_check(len(restored.encode("utf-8")) == 157596
                 and hashlib.sha256(restored.encode("utf-8")).hexdigest() ==
                 "9cec31f06009bd34bc12584def86d0d42370756bd61a83f229e70bd06bb72ede",
                 "attempts.caller.exact-current48-inverse")
    return restored


def require_caller_wiring(flow_bytes, caller_bytes):
    flow, caller = flow_bytes.decode("utf-8"), caller_bytes.decode("utf-8")
    caller = require_touchdown_stats_additions(caller)
    caller = restore_fourth_down_attempts_caller(caller)
    caller = restore_place_kick_stats_caller(caller)
    caller = restore_play_end_stats_caller(caller)
    _, end, body = method_region(flow, "PrepareKickoff", "SourceKickoffInput input")
    static_check(hashlib.sha256(flow[body:end].encode("utf-8")).hexdigest() == LEGACY_KICKOFF_BODY_SHA256,
                 "flow.legacy-one-argument-body-preserved")
    setup = method_text(caller, "PrepareSourceKickoff", "bool first, bool half")
    arguments, call = plan_call(setup)
    static_check(len(arguments) == 3, "caller.plan.three-argument")
    expected_input = (
        "new SourceKickoffInput(Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, "
        "first, half, Model.SourceFirstPossession, (int)Model.SourceCaptain, onside)"
    )
    static_check(arguments[0] == token_values(expected_input), "caller.plan.original-input-arguments")
    static_check(arguments[1] == ["sourceConfiguration"], "caller.plan.configured-stations")
    static_check(arguments[2] != ["null"], "caller.plan.current-frame-not-null")
    static_check(arguments[2] == token_values("Model.SourceCurrentInput.StationInputs"),
                 "caller.plan.transformed-current-frame")
    proc = csharp_tokens(method_text(caller, "SourceGameProc", "PlayerCommand command"))
    transform = token_locations(proc, "command = TransformSourceStationInput(command);")
    assignment = token_locations(proc, "Model.SourceCurrentInput = command;")
    dispatch = token_locations(proc, "switch (Model.Phase)")
    pat = token_locations(proc, "ApplySourcePat(")
    continuation = token_locations(proc, "ContinueSource(")
    static_check(len(transform) == len(assignment) == len(dispatch) == 1 and pat and continuation
                 and transform[0] < assignment[0] < min(dispatch + pat + continuation),
                 "caller.current-input.before-phase-dispatch")
    static_check(len(token_locations(csharp_tokens(setup), CPU_GUARD)) == 1,
                 "caller.cpu.guard-without-half-shortcut")
    for name, parameters, call_shape in (
        ("InitializeSource", "", "PrepareSourceKickoff(true, false);"),
        ("ContinueSource", "", "PrepareSourceKickoff(false, Model.SourceHalfKickoff);"),
        ("AdvanceSourcePeriod", "", "PrepareSourceKickoff(false, decision.HalfKickoff);"),
        ("ApplySourcePat", "SourcePatDecision pat", "PrepareSourceKickoff(false, Model.SourceHalfKickoff);"),
    ):
        static_check(len(token_locations(csharp_tokens(method_text(caller, name, parameters)), call_shape)) == 1,
                     "caller.converges." + name)
    static_check(call == CURRENT_CALL and caller.count(CURRENT_CALL) == 1, "caller.plan.exact-authorized-call")
    completion_before = (
        "            ApplySourceFirePlayEnd(endingInput, resolution);\n"
        "            Model.SourceLastResult = resolution.Classification.Result;\n"
    )
    completion_after = (
        "            ApplySourceFirePlayEnd(endingInput, resolution);\n"
        "            Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);\n"
        "            Model.SourceLastResult = resolution.Classification.Result;\n"
    )
    static_check(caller.count(completion_after) == 1 and len(token_locations(csharp_tokens(caller),
                 "Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);")) == 1,
                 "caller.completed-pass.exact-authorized-insertion")
    caller = caller.replace(completion_after, completion_before)
    static_check(hashlib.sha256(caller.encode("utf-8")).hexdigest() ==
                 "6b200f1c377d6a3d7a9435541bcb3ee0a77f54ff61ec8a7fa1a0e35eef1c2884",
                 "caller.completed-pass.exact-prior-whole-file")
    restored = caller.replace(CURRENT_CALL, LEGACY_CALL)
    static_check(hashlib.sha256(restored.encode("utf-8")).hexdigest() == CALLER_PREEDIT_SHA256,
                 "caller.only-authorized-call-changed-captain-preserved")
    return {
        "status": "passed-static-only", "compiled": False, "simulation_executed": False,
        "production_file": CALLER_SOURCE, "source_sha256": hashlib.sha256(caller_bytes).hexdigest(),
        "preedit_source_sha256": CALLER_PREEDIT_SHA256,
        "legacy_one_argument_body_sha256": LEGACY_KICKOFF_BODY_SHA256,
    }


def replace_unique(text, anchor, replacement, label):
    if text.count(anchor) != 1:
        raise RuntimeError("Actual-source mutation anchor must be unique: " + label)
    return text.replace(anchor, replacement)


def mutate_static_caller(case, caller_bytes):
    name, anchor, replacement, _ = case
    caller = caller_bytes.decode("utf-8")
    if name == "caller-current-assignment-late":
        start, end, _ = method_region(caller, "SourceGameProc", "PlayerCommand command")
        proc = replace_unique(caller[start:end], anchor, "", name)
        proc = replace_unique(proc, replacement, anchor + replacement, name + "-placement")
        changed = caller[:start] + proc + caller[end:]
    else:
        changed = replace_unique(caller, anchor, replacement, name)
    return changed.encode("utf-8")


def static_caller_controls(flow_bytes, caller_bytes):
    results = []
    for case in STATIC_CONTROLS:
        name, _, _, expected = case
        mutated = mutate_static_caller(case, caller_bytes)
        try:
            require_caller_wiring(flow_bytes, mutated)
        except StaticAssertionFailure as error:
            if error.field != expected:
                raise RuntimeError("Static caller control failed an unrelated field: " + name + ": " + error.field)
        else:
            raise RuntimeError("Static caller control survived: " + name)
        results.append({
            "id": name, "rejected_by_static_field": expected, "status": "rejected-static-only",
            "compiled": False, "behaviorally_rejected": False, "simulation_executed": False,
            "mutated_source_sha256": hashlib.sha256(mutated).hexdigest(), "live_source_modified": False,
        })
    return results


def mutation_source_path(filename, contents):
    matches = [relative for relative in contents if Path(relative).name == filename]
    if len(matches) != 1:
        raise RuntimeError("Mutation must resolve one captured actual production file: " + filename)
    return matches[0]


def source_files():
    contracts = ROOT / "Assets/Scripts/Contracts"
    required = (
        "GameplayContracts.cs", "SourceAnimationContracts.cs", "SourceConfigurationContracts.cs",
        "SourceInputContracts.cs", "SourceMatchContracts.cs", "SourcePresentationContracts.cs",
    )
    if any(not (contracts / name).is_file() for name in required):
        raise RuntimeError("All six actual Contracts files are required; substitute DTOs are forbidden.")
    return sorted(contracts.rglob("*.cs")) + [
        ROOT / "Assets/Scripts/Gameplay" / name for name in RULE_SOURCES
    ]


def hashes():
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in source_files()}


def snapshot():
    contents = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in source_files()}
    captured = {name: hashlib.sha256(data).hexdigest() for name, data in contents.items()}
    if hashes() != captured:
        raise RuntimeError("Actual source inventory changed while capturing the isolated snapshot.")
    return contents, captured


def harness_snapshot():
    contents = {}
    for parent, directories, files in os.walk(SUITE):
        directories[:] = sorted(name for name in directories if name not in (".artifacts", "bin", "obj"))
        for name in sorted(files):
            path = Path(parent) / name
            if path.suffix in (".cs", ".csproj", ".py") or path.name == "README.md":
                contents[path.relative_to(SUITE)] = path.read_bytes()
    return contents


def require_baseline(exit_code, summary):
    if (exit_code != 0 or not isinstance(summary, dict)
            or summary.get("schema") != "actual-source-contract-checks/v1"
            or summary.get("status") != "passed"
            or summary.get("full_gameplay_parity_claimed") is not False
            or summary.get("human_onside_contract") != HUMAN_ONSIDE_CONTRACT
            or any(type(summary.get(key)) is not int or summary.get(key) != count for key, count in EXPECTED.items())):
        raise RuntimeError("Expected exact retained 511 + human-onside 1232 tuple / 1 API = 1744 checks; 65 mappings are included.")


def require_mutation(exit_code, summary, assertion):
    new_case = next((case for case in ONSIDE_MUTATIONS if case[-1] == assertion), None)
    maximum = EXPECTED["assertions"] if new_case else RETAINED_ASSERTIONS
    if (exit_code != 1 or not isinstance(summary, dict)
            or summary.get("schema") != "actual-source-contract-checks/v1"
            or summary.get("status") != "failed"
            or summary.get("failed_assertion") != assertion
            or type(summary.get("assertions")) is not int
            or not 0 < summary["assertions"] <= maximum):
        raise RuntimeError("Mutant did not fail its exact offending-field assertion: " + assertion)
    if new_case:
        total, partial, completed = ONSIDE_FAILURE_COUNTS[new_case[0]]
        required = {
            "human_onside_contract": HUMAN_ONSIDE_CONTRACT,
            "full_gameplay_parity_claimed": False,
            "promoted_dto_assertions": 177, "enum_mappings": 65, "additional_dto_assertions": 23,
            "presentation_dto_assertions": 189, "rules_flow_assertions": 122, "retained_assertions": 511,
            "human_onside_fields_per_case": 14, "human_onside_tuple_assertions": partial,
            "human_onside_api_guard_assertions": 0, "human_onside_assertions": partial,
            "human_onside_cases": completed, "assertions": total,
        }
        if any(type(summary.get(key)) is not type(value) or summary.get(key) != value for key, value in required.items()):
            raise RuntimeError("Human-onside mutant did not retain its frozen partial partitions: " + new_case[0])


def main():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    harness = harness_snapshot()
    workspace = ARTIFACTS / ("run-" + uuid.uuid4().hex)
    report = ARTIFACTS / "result.json"
    report.unlink(missing_ok=True)
    environment = dict(os.environ)
    cache = ARTIFACTS / "runtime-cache"
    cache.mkdir(exist_ok=True)
    environment.update(TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    evidence = {
        "schema": "actual-source-contract-regressions/v1",
        "status": "failed",
        "commands": [],
        "mutations": [],
        "static_caller_controls": [],
        "harness_sha256": {name.as_posix(): hashlib.sha256(data).hexdigest() for name, data in harness.items()},
        "profile": "Actual sources compiled as C#7.3 netstandard2.1, warnings-as-errors; net10.0 runner.",
        "full_gameplay_parity_claimed": False,
        "unity_editor_native_build_browser_or_hosting_executed": False,
        "human_onside_contract": HUMAN_ONSIDE_CONTRACT,
        "expected_assertion_partitions": EXPECTED,
        "expected_compiled_controls": 30,
        "expected_static_only_controls": 5,
        "compiled_game_simulation_claimed": False,
    }
    before = None
    wiring_before = None
    exit_code = 1

    def execute(label, source_root):
        project_root = workspace / label / "tests/contracts"
        for name, data in harness.items():
            copied = project_root / name
            copied.parent.mkdir(parents=True, exist_ok=True)
            copied.write_bytes(data)
        scratch = project_root / "build-scratch"
        scratch.mkdir()
        execute_env = dict(environment, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
        build = ["dotnet", "build", str(project_root / "ContractChecks.csproj"),
                 "--configuration", "Release", "--verbosity", "quiet",
                 "-p:BlitzSourceRoot=" + source_root.as_posix() + "/"]
        evidence["commands"].append(build)
        compiled = subprocess.run(build, cwd=ROOT, env=execute_env, capture_output=True,
                                  text=True, timeout=180)
        (ARTIFACTS / (label + "-build.log")).write_text(
            compiled.stdout + compiled.stderr, encoding="utf-8")
        if compiled.returncode:
            raise RuntimeError("Actual-source build failed, not an assertion rejection: " + label +
                               "; see " + label + "-build.log")
        command = ["dotnet", str(project_root / "bin/Release/net10.0/ContractChecks.dll")]
        evidence["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=execute_env, capture_output=True,
                                 text=True, timeout=60)
        (ARTIFACTS / (label + ".log")).write_text(process.stdout + process.stderr, encoding="utf-8")
        summaries = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = summaries[0] if len(summaries) == 1 else None
        return process.returncode, summary

    try:
        contents, before = snapshot()
        evidence["production_sha256_before"] = before
        wiring = (ROOT / CALLER_SOURCE).read_bytes()
        wiring_before = hashlib.sha256(wiring).hexdigest()
        evidence["static_caller_wiring"] = require_caller_wiring(contents[FLOW_SOURCE], wiring)
        evidence["static_caller_controls"] = static_caller_controls(contents[FLOW_SOURCE], wiring)
        evidence["caller_wiring_sha256_before"] = wiring_before
        if len(COMPILED_MUTATIONS) != 30 or len(STATIC_CONTROLS) != 5:
            raise RuntimeError("Expected exactly 30 compiled controls and 5 separate static caller controls.")

        def require_unchanged_sources(stage):
            if hashes() != before or hashlib.sha256((ROOT / CALLER_SOURCE).read_bytes()).hexdigest() != wiring_before:
                raise RuntimeError("Actual compiled source inventory or static caller changed during " + stage + ".")

        code, baseline = execute("baseline", ROOT)
        evidence["baseline_exit_code"] = code
        evidence["baseline"] = baseline
        require_baseline(code, baseline)
        require_unchanged_sources("the baseline")
        def mutation(case):
            name, filename, anchor, replacement, assertion = case
            source_root = workspace / name / "source"
            for relative, data in contents.items():
                path = source_root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            relative = mutation_source_path(filename, contents)
            target = source_root / relative
            original = target.read_text(encoding="utf-8")
            if original.count(anchor) != 1:
                raise RuntimeError("Actual-source mutation anchor must be unique: " + name)
            target.write_text(original.replace(anchor, replacement), encoding="utf-8")
            code, summary = execute(name, source_root)
            result = {
                "id": name, "production_file": relative, "before": anchor, "after": replacement,
                "source_sha256": before[relative],
                "mutated_source_sha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                "exit_code": code, "summary": summary, "live_source_modified": False,
            }
            require_mutation(code, summary, assertion)
            result["rejected_by_assertion"] = assertion
            result["behaviorally_rejected"] = True
            result["proof_kind"] = "compiled-actual-source-field-assertion"
            require_unchanged_sources("isolated mutations")
            print("PASS mutation: " + name + " -> " + assertion)
            return result
        evidence["mutations"] = run_mutations(COMPILED_MUTATIONS, mutation)
        code, final = execute("final-baseline", ROOT)
        evidence["final_baseline_exit_code"] = code
        evidence["final_baseline"] = final
        require_baseline(code, final)
        require_unchanged_sources("the final baseline")
        evidence["status"] = "passed"
        exit_code = 0
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        evidence["error"] = str(error)
        print("CONTRACT REGRESSIONS FAILED: " + str(error), file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        evidence["isolated_source_and_build_trees_removed"] = not workspace.exists()
        try:
            after = hashes()
            evidence["production_sha256_after"] = after
            evidence["production_hashes_unchanged"] = before is not None and after == before
        except (OSError, RuntimeError) as error:
            evidence["production_hashes_unchanged"] = False
            evidence["hash_error"] = str(error)
        if not evidence["production_hashes_unchanged"]:
            evidence["status"] = "inconclusive-sources-changed"
            exit_code = 1
        try:
            wiring_after = hashlib.sha256((ROOT / CALLER_SOURCE).read_bytes()).hexdigest()
            evidence["caller_wiring_sha256_after"] = wiring_after
            evidence["caller_wiring_hash_unchanged"] = wiring_before is not None and wiring_after == wiring_before
        except OSError as error:
            evidence["caller_wiring_hash_unchanged"] = False
            evidence["caller_wiring_hash_error"] = str(error)
        if not evidence["caller_wiring_hash_unchanged"]:
            evidence["status"] = "inconclusive-static-caller-changed"
            exit_code = 1
        evidence["harness_hashes_unchanged"] = harness == harness_snapshot()
        if not evidence["harness_hashes_unchanged"]:
            evidence["status"] = "inconclusive-harness-changed"
            exit_code = 1
        report.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print("Evidence: " + report.relative_to(ROOT).as_posix())
    if exit_code == 0:
        print("PASS: 1744 actual-source assertions (retained 511 + 1232 human-onside tuple fields + 1 API guard); "
              "30 compiled real-source mutants rejected; 5 separate static-only caller controls rejected; "
              "compiled-source, caller and harness hashes unchanged. No full Simulation execution is claimed.")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
