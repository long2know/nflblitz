using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public static class KernelBinding
{
    public const string Factory = "SourceC2Composition.CreateM1Simulation";
    public const string PreSnapSwitchFactory = "SourceC2Composition.CreatePreSnapSwitchSimulation";
    public const string DefaultAudiblesFactory = SourceInterfacePackagePlan.DefaultAudiblesFactory;

    public static string FactoryFor(SourceTeamResourceProfile profile) =>
        profile.IsCompleteOriginalPrematch303 ? SourceTeamResourceProfile.CompleteOriginalPrematchFactory :
        profile.IsCompleteOriginalFinal303 ? SourceTeamResourceProfile.CompleteOriginalFinalFactory :
        profile.IsCompleteOriginalRecords303 ? SourceTeamResourceProfile.CompleteOriginalRecordsFactory :
        profile.IsCompleteOriginalSourceBanks303 ? SourceTeamResourceProfile.CompleteOriginalSourceBanksFactory :
        profile.IsCompleteOriginal303 ? SourceTeamResourceProfile.CompleteOriginalFactory :
        profile.IsContact282C2M1DefaultAudibles ? DefaultAudiblesFactory :
        profile.IsContact282C2M1PreSnapSwitch ? PreSnapSwitchFactory :
        profile.IsContact281C2M1 ? Factory :
        throw new InvalidDataException("No portable host factory for this exact resource profile.");

    public static GameSimulation Create(PlayerData data, PreviewOptions options = null,
        Func<int, ISourceRandom> randomFactory = null, int seed = 1,
        SourceMatchConfiguration matchConfiguration = null,
        SourceCabinetContinuationPolicy continuation = SourceCabinetContinuationPolicy.KeepParticipatingStations,
        bool newPlayerSession = false, SourceVersusSoundBankState soundBankState = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= PreviewOptions.FromUrl("", data.Catalog.Profile.StableId);
        DataCatalog.Require(options.Profile == data.Catalog.Profile.StableId,
            "Preview options and authenticated data catalog profiles differ; no factory fallback.");
        DataCatalog.Require(matchConfiguration == null || options.GameflowEnabled,
            "Session choices require the explicit source-gameflow=portable composition.");
        DataCatalog.Require(!newPlayerSession || options.GameflowEnabled && matchConfiguration != null &&
            data.Catalog.Profile.UsesPreSnapSwitchServices,
            "Known-none new players require a deliberate Portable FourStation pre-snap-switch session, not a base/default factory promotion.");
        SourceMatchConfiguration configuration = matchConfiguration ?? data.Configuration.Match;
        if (options.GameflowEnabled) data.ValidateCabinetConfiguration(configuration);
        if (options.FireEnabled && data.Fire == null)
            throw new InvalidDataException("source-fire=portable requires the authenticated original SDL/17-TGA fire bundle before simulation.");
        if (options.FireEnabled) { data.Fire.RequireDust(); data.Fire.RequireHelmet(); }
        SourceFireGameplayConfiguration fire = options.FireEnabled ?
            new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(data.Fire), 0, SourceFireCull.Off,
                (identity, side) => SourceTeamPresentationModel.LooseHelmetDecal(data.Rosters, data.Teams,
                    configuration, data.Configuration.Presentation, identity, side)) : null;
        SourceKickMeterMetrics meters = options.KickMeters == SourceKickMeterMode.PortableAuthored ?
            data.Hud.CreatePortableAuthoredKickMeterMetrics() : null;
        DataCatalog.Require(soundBankState == null || data.Catalog.Profile.IsCompleteOriginalPrematch303,
            "A carried original global bank state belongs only to its explicit pregame/versus composition.");
        string[] sourceTeamBanks = data.Catalog.Profile.IsCompleteOriginal303 ?
            Enumerable.Range(0, 31).Select(data.Rosters.GetSourceSoundBankName).ToArray() : null;
        SourceC2Bootstrap completeBootstrap = sourceTeamBanks == null ? null : soundBankState == null ?
            SourceC2Bootstrap.FromCompleteOriginalVersusSoundBanks(data.Callbacks, sourceTeamBanks) :
            SourceC2Bootstrap.FromCompleteOriginalVersusSoundBanks(data.Callbacks, sourceTeamBanks, soundBankState);
        GameSimulation game = data.Catalog.Profile.IsCompleteOriginalPrematch303
            ? SourceC2Composition.CreateCompleteOriginalPrematchSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, completeBootstrap,
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire,
                kickMeterMetrics: meters, defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact)
            : data.Catalog.Profile.IsCompleteOriginalFinal303
            ? SourceC2Composition.CreateCompleteOriginalFinalSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, completeBootstrap,
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire,
                kickMeterMetrics: meters, defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact)
            : data.Catalog.Profile.IsCompleteOriginalRecords303
            ? SourceC2Composition.CreateCompleteOriginalRecordsSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, completeBootstrap,
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire,
                kickMeterMetrics: meters, defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact)
            : data.Catalog.Profile.IsCompleteOriginalSourceBanks303
            ? SourceC2Composition.CreateCompleteOriginalSourceBanksSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, completeBootstrap,
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire,
                kickMeterMetrics: meters, defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact)
            : data.Catalog.Profile.IsCompleteOriginal303
            ? SourceC2Composition.CreateCompleteOriginalSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, completeBootstrap,
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire,
                kickMeterMetrics: meters, defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact)
            : data.Catalog.Profile.IsContact282C2M1DefaultAudibles
            ? SourceC2Composition.CreateDefaultAudiblesSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire,
                kickMeterMetrics: meters, defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact)
            : data.Catalog.Profile.IsContact282C2M1PreSnapSwitch
            ? SourceC2Composition.CreatePreSnapSwitchSimulation(data.Animations, data.ReferencePivots, data.Playbook,
                configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
                randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire, kickMeterMetrics: meters)
            : SourceC2Composition.CreateM1Simulation(data.Animations, data.ReferencePivots, data.Playbook,
            configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
            data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
            randomFactory: randomFactory, seed: seed, compatibility: options.Compatibility, fireEffects: fire, kickMeterMetrics: meters);
        game.BindSourcePossessionSoundData(data.PossessionSound);
        if (options.GameflowEnabled)
            game.BindSourceCabinetFlow(new SourceCabinetFlowConfiguration(
                configuration.Stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId),
                new SourceCabinetButtonMasks(0xf0, 0x70, 0xf0f0f0f0u), continuation,
                SourceCabinetWaitInitialization.ZeroCountersOnEntry));
        if (newPlayerSession)
            game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(configuration), data.Hud);
        return game;
    }

    public static bool IsPortable(GameSimulation game) => game.Compatibility == SourceGameplayCompatibility.Portable &&
        ((ISourceMatchState)game.State).Match.Compatibility == SourceGameplayCompatibility.Portable;

    public static object Describe(GameSimulation game, SourceTeamResourceProfile profile = null)
    {
        SourceMatchView match = ((ISourceMatchState)game.State).Match;
        return new {
            policy = game.Compatibility.ToString(), snapshotPolicy = match.Compatibility.ToString(),
            portableBound = IsPortable(game), factory = profile?.IsCompleteOriginalPrematch303 == true ?
                SourceTeamResourceProfile.CompleteOriginalPrematchFactory : profile?.IsCompleteOriginalFinal303 == true ?
                SourceTeamResourceProfile.CompleteOriginalFinalFactory : profile?.IsCompleteOriginalRecords303 == true ?
                SourceTeamResourceProfile.CompleteOriginalRecordsFactory : profile?.IsCompleteOriginalSourceBanks303 == true ?
                SourceTeamResourceProfile.CompleteOriginalSourceBanksFactory :
                game.C2State.DataSha256 == SourceTeamResourceProfile.CompleteOriginalCallbackSha256
                ? SourceTeamResourceProfile.CompleteOriginalFactory : game.SourceDefaultAudiblesEnabled ? DefaultAudiblesFactory :
                game.SourceDefensivePreSnapSwitchEnabled ? PreSnapSwitchFactory : Factory,
            resourceProfile = profile?.IsCompleteOriginalPrematch303 == true ?
                SourceTeamResourceProfile.CompleteOriginalPrematchStableId : profile?.IsCompleteOriginalFinal303 == true ?
                SourceTeamResourceProfile.CompleteOriginalFinalStableId : profile?.IsCompleteOriginalRecords303 == true ?
                SourceTeamResourceProfile.CompleteOriginalRecordsStableId : profile?.IsCompleteOriginalSourceBanks303 == true ?
                SourceTeamResourceProfile.CompleteOriginalSourceBanksStableId :
                game.C2State.DataSha256 == SourceTeamResourceProfile.CompleteOriginalCallbackSha256
                ? SourceTeamResourceProfile.CompleteOriginalStableId : game.SourceDefaultAudiblesEnabled ? SourceTeamResourceProfile.DefaultAudiblesStableId :
                game.SourceDefensivePreSnapSwitchEnabled ? SourceTeamResourceProfile.PreSnapSwitchStableId :
                SourceTeamResourceProfile.C2M1StableId,
            defaultAudiblesEnabled = game.SourceDefaultAudiblesEnabled,
            sourceGameflow = ((ISourceCabinetFlowState)game.State).CabinetFlow == null ? "unbound" : "portable",
            customPlayInventory = game.SourceCustomPlaySession == null ? "unavailable/unbound" :
                "explicit new-player known-none; original page skipping and source forced-choice seam",
            featureContract = profile?.IsCompleteOriginalPrematch303 == true ?
                SourceTeamResourceProfile.CompleteOriginalPrematchFeatureContract : profile?.IsCompleteOriginalRecords303 == true ?
                SourceTeamResourceProfile.CompleteOriginalRecordsFeatureContract :
                game.C2State.DataSha256 == SourceTeamResourceProfile.CompleteOriginalCallbackSha256
                ? SourceTeamResourceProfile.CompleteOriginalFeatureContract :
                game.SourceDefaultAudiblesEnabled ? SourceInterfacePackagePlan.DefaultAudiblesFeatureContract : null,
            defensivePreSnapSwitchEnabled = game.SourceDefensivePreSnapSwitchEnabled,
            sourceFire = game.SourceFireEnabled ? "portable" : "disabled",
            kickMeters = ((ISourceKickMeterState)game.State).KickMeterMode.ToString(),
            repairCount = match.CompatibilityRepairCount,
            repairs = match.CompatibilityRepairs.Select(repair => new {
                sequence = repair.Sequence, sourceTick = repair.SourceTick, kind = repair.Kind.ToString(),
                subject = repair.SubjectId, rawIndex = repair.RawIndex, effectiveIndex = repair.EffectiveIndex,
                sourceContext = repair.SourceContext, retainedStick = repair.RetainedStick
            }).ToArray(),
            teamHandicap = new[] {
                new { team = "Home", raw = match.HomeGameplay.RawHandicap, effective = match.HomeGameplay.EffectiveHandicap },
                new { team = "Away", raw = match.AwayGameplay.RawHandicap, effective = match.AwayGameplay.EffectiveHandicap }
            },
            limitation = "Explicit M1 Portable policy: handicap consumers saturate to0..11; nonnegative block counts use the last original entry; targetless running-push drones retain prior valid steering; carrierless seeking defensive switches defer while retaining the controlled actor and stations, without an angle read, replacement target or RNG draw. Only bound portable cabinet flow also defers the original carrierless PlayOver new-A late-hit target read after standard movement, retaining raw ABC and independent B/C; strict/unbound guards remain. Raw values remain preserved and each repair is reported. No arcade/native fidelity claim."
        };
    }
}
