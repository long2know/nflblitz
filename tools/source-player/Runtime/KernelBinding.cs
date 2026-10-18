using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public static class KernelBinding
{
    public const string Factory = "SourceC2Composition.CreateM1Simulation";

    public static GameSimulation Create(PlayerData data, PreviewOptions options = null)
    {
        options ??= PreviewOptions.FromUrl("");
        if (options.FireEnabled && data.Fire == null)
            throw new InvalidDataException("source-fire=portable requires the authenticated original SDL/17-TGA fire bundle before simulation.");
        SourceFireGameplayConfiguration fire = options.FireEnabled ?
            new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(data.Fire), 0, SourceFireCull.Off) : null;
        SourceKickMeterMetrics meters = options.KickMeters == SourceKickMeterMode.PortableAuthored ?
            data.Hud.CreatePortableAuthoredKickMeterMetrics() : null;
        return SourceC2Composition.CreateM1Simulation(data.Animations, data.ReferencePivots, data.Playbook,
            data.Configuration.Match, data.Rosters, data.Configuration.Presentation, data.Movement,
            data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
            compatibility: options.Compatibility, fireEffects: fire, kickMeterMetrics: meters);
    }

    public static bool IsPortable(GameSimulation game) => game.Compatibility == SourceGameplayCompatibility.Portable &&
        ((ISourceMatchState)game.State).Match.Compatibility == SourceGameplayCompatibility.Portable;

    public static object Describe(GameSimulation game)
    {
        SourceMatchView match = ((ISourceMatchState)game.State).Match;
        return new {
            policy = game.Compatibility.ToString(), snapshotPolicy = match.Compatibility.ToString(),
            portableBound = IsPortable(game), factory = Factory,
            sourceFire = game.SourceFireEnabled ? "portable" : "disabled",
            kickMeters = ((ISourceKickMeterState)game.State).KickMeterMode.ToString(),
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
