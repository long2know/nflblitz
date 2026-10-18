using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public static class KernelBinding
{
    public const string Factory = "SourceC2Composition.CreateM1Simulation";

    public static GameSimulation Create(PlayerData data) =>
        SourceC2Composition.CreateM1Simulation(data.Animations, data.ReferencePivots, data.Playbook,
            data.Configuration.Match, data.Rosters, data.Configuration.Presentation, data.Movement,
            data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
            compatibility: SourceGameplayCompatibility.Portable);

    public static bool IsPortable(GameSimulation game) => game.Compatibility == SourceGameplayCompatibility.Portable &&
        ((ISourceMatchState)game.State).Match.Compatibility == SourceGameplayCompatibility.Portable;

    public static object Describe(GameSimulation game)
    {
        SourceMatchView match = ((ISourceMatchState)game.State).Match;
        return new {
            policy = game.Compatibility.ToString(), snapshotPolicy = match.Compatibility.ToString(),
            portableBound = IsPortable(game), factory = Factory,
            repairCount = match.CompatibilityRepairCount,
            repairs = match.CompatibilityRepairs.Select(repair => new {
                sequence = repair.Sequence, sourceTick = repair.SourceTick, kind = repair.Kind.ToString(),
                subject = repair.SubjectId, rawIndex = repair.RawIndex, effectiveIndex = repair.EffectiveIndex,
                sourceContext = repair.SourceContext
            }).ToArray(),
            teamHandicap = new[] {
                new { team = "Home", raw = match.HomeGameplay.RawHandicap, effective = match.HomeGameplay.EffectiveHandicap },
                new { team = "Away", raw = match.AwayGameplay.RawHandicap, effective = match.AwayGameplay.EffectiveHandicap }
            },
            limitation = "Explicit M1 Portable policy: original handicap consumer indices saturate to0..11; nonnegative block-count consumers use the last valid original entry. Raw values/counters remain preserved. No arcade/native fidelity claim."
        };
    }
}
