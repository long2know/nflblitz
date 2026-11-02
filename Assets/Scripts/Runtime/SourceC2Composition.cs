using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Runtime
{
    public static class SourceC2Composition
    {
        public static GameSimulation CreateSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null)
        {
            return CreateChecked(SourceTeamResourceProfile.Contact281C2, animations, referencePivots, playbook,
                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics);
        }

        public static GameSimulation CreateM1Simulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null)
        {
            return CreateChecked(SourceTeamResourceProfile.Contact281C2M1, animations, referencePivots, playbook,
                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics);
        }

        public static GameSimulation CreatePreSnapSwitchSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null)
        {
            return CreateChecked(SourceTeamResourceProfile.Contact282C2M1PreSnapSwitch, animations, referencePivots,
                playbook, configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics);
        }

        public static GameSimulation CreateDefaultAudiblesSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null,
            SourceDefaultAudibleData defaultAudibles = null, SourceCinematicLibrary impactLibrary = null)
        {
            SourceInterfaceData.RequireDefaultAudibleAdmission(defaultAudibles, playbook);
            if (compatibility != SourceGameplayCompatibility.Portable)
                throw new NotSupportedException("Default audibles require explicit Portable admission before construction.");
            SourceTeamResourceProfile.Contact282C2M1DefaultAudibles.RequireImpact(impactLibrary);
            return CreateChecked(SourceTeamResourceProfile.Contact282C2M1DefaultAudibles, animations, referencePivots,
                playbook, configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics, defaultAudibles);
        }

        public static GameSimulation CreateCompleteOriginalSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null,
            SourceDefaultAudibleData defaultAudibles = null, SourceCinematicLibrary impactLibrary = null)
        {
            SourceInterfaceData.RequireDefaultAudibleAdmission(defaultAudibles, playbook);
            if (compatibility != SourceGameplayCompatibility.Portable)
                throw new NotSupportedException("CompleteOriginal303 requires explicit Portable admission before construction.");
            SourceTeamResourceProfile.CompleteOriginal303.RequireImpact(impactLibrary);
            return CreateChecked(SourceTeamResourceProfile.CompleteOriginal303, animations, referencePivots,
                playbook, configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics, defaultAudibles);
        }

        public static GameSimulation CreateCompleteOriginalSourceBanksSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null,
            SourceDefaultAudibleData defaultAudibles = null, SourceCinematicLibrary impactLibrary = null)
        {
            return CreateCompleteOriginalSimulation(animations, referencePivots, playbook, configuration,
                rosters, presentation, movementData, movementSettings, legacySoundFunctions, c2Bootstrap,
                randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics, defaultAudibles, impactLibrary);
        }

        public static GameSimulation CreateCompleteOriginalRecordsSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null,
            SourceDefaultAudibleData defaultAudibles = null, SourceCinematicLibrary impactLibrary = null)
        {
            return CreateCompleteOriginalSourceBanksSimulation(animations, referencePivots, playbook, configuration,
                rosters, presentation, movementData, movementSettings, legacySoundFunctions, c2Bootstrap,
                randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics, defaultAudibles, impactLibrary);
        }

        public static GameSimulation CreateCompleteOriginalFinalSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null,
            SourceDefaultAudibleData defaultAudibles = null, SourceCinematicLibrary impactLibrary = null)
        {
            SourceInterfaceData.RequireDefaultAudibleAdmission(defaultAudibles, playbook);
            if (compatibility != SourceGameplayCompatibility.Portable)
                throw new NotSupportedException("The final original composition requires its explicit Portable entry.");
            SourceTeamResourceProfile.CompleteOriginalFinal303.RequireImpact(impactLibrary);
            return CreateChecked(SourceTeamResourceProfile.CompleteOriginalFinal303, animations, referencePivots,
                playbook, configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics, defaultAudibles);
        }

        public static GameSimulation CreateCompleteOriginalPrematchSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null,
            SourceDefaultAudibleData defaultAudibles = null, SourceCinematicLibrary impactLibrary = null)
        {
            SourceInterfaceData.RequireDefaultAudibleAdmission(defaultAudibles, playbook);
            if (compatibility != SourceGameplayCompatibility.Portable)
                throw new NotSupportedException("The original pregame composition requires its explicit Portable entry.");
            SourceTeamResourceProfile.CompleteOriginalPrematch303.RequireImpact(impactLibrary);
            return CreateChecked(SourceTeamResourceProfile.CompleteOriginalPrematch303, animations, referencePivots,
                playbook, configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings, compatibility, fireEffects, kickMeterMetrics, defaultAudibles);
        }

        private static GameSimulation CreateChecked(SourceTeamResourceProfile profile, ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory, int seed, SourceHandicapSettings handicapSettings,
            SourceGameplayCompatibility compatibility, SourceFireGameplayConfiguration fireEffects,
            SourceKickMeterMetrics kickMeterMetrics, SourceDefaultAudibleData defaultAudibles = null)
        {
            if (fireEffects != null && !profile.UsesM1Services)
                throw new NotSupportedException("Authored fire effects require the explicit actual M1 factory.");
            if (compatibility != SourceGameplayCompatibility.StrictSource
                && (compatibility != SourceGameplayCompatibility.Portable || !profile.UsesM1Services))
                throw new NotSupportedException("Portable gameplay is supported only by the explicit M1 factory.");
            profile.RequireLibrary(animations);
            bool preSnapSwitch = profile.UsesPreSnapSwitchServices;
            string callbackHash = profile.IsCompleteOriginal303 ? SourceTeamResourceProfile.CompleteOriginalCallbackSha256 :
                preSnapSwitch ? SourceInterfacePackagePlan.PreSnapSwitchCallbackSha256
                : profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackSha256
                : SourceInterfacePackagePlan.C2CallbackSha256;
            string callbackProfile = profile.IsCompleteOriginal303 ? SourceC2CallbackData.CompleteOriginalProfile :
                preSnapSwitch ? SourceC2CallbackData.PreSnapSwitchProfile
                : profile.IsContact281C2M1 ? SourceC2CallbackData.M1Profile : SourceC2CallbackData.EntryProfile;
            if (c2Bootstrap == null || c2Bootstrap.EntryKind != (profile.IsCompleteOriginal303 ?
                    SourceC2EntryKind.CompleteOriginalVersusSoundBanks : SourceC2EntryKind.OriginalProgramBootSoundBanks) ||
                c2Bootstrap.Data.PayloadSha256 != callbackHash || c2Bootstrap.Data.Profile != callbackProfile ||
                c2Bootstrap.Data.AnimationManifestSha256 != profile.AnimationSha256 ||
                c2Bootstrap.Data.Bindings.Count != (preSnapSwitch ? 15 : profile.IsContact281C2M1 ? 14 : 9) ||
                c2Bootstrap.Data.HitExtensionData.PayloadSha256 != SourceInterfacePackagePlan.C2HitSha256)
                throw new SourceInterfaceException("C2 requires its explicit original sound-bank entry/bootstrap BEFORE constructor bank draws and initial AI.");
            if (legacySoundFunctions == null || legacySoundFunctions.PayloadSha256 != SourceInterfacePackagePlan.SoundVmFunctionsSha256)
                throw new SourceInterfaceException("C2 keeps the exact unchanged legacy six-binding SAFN before first wake.");
            if (referencePivots == null || referencePivots.Count != SourceTiming.JointCount ||
                playbook == null || configuration == null || rosters == null || presentation == null ||
                movementData == null || movementSettings == null)
                throw new SourceInterfaceException("C2 requires the actual fifteen reference pivots and explicit complete kernel inputs.");
            if (configuration.Profile != SourceSimulationProfile.OriginalRelease57Hz)
                throw new SourceInterfaceException("C2 requires its matching explicit OriginalRelease57Hz configuration before constructor execution.");
            if (preSnapSwitch && configuration.StationLayout == SourceStationLayout.TwoStation)
                for (int physical = 0; physical < 2; physical++)
                {
                    bool present = false;
                    foreach (SourceStationConfiguration station in configuration.Stations)
                        if (station.StationId == physical) present = true;
                    if (!present)
                        throw new NotSupportedException("pmisc.c:update_stations:" + (638 + physical) +
                            " indexes player_blocks[plyr_control[" + physical + "]==-1]; original TwoStation input is undefined.");
                }
            ISourceActorGeometry geometry = new SourceCatchGeometry(animations, referencePivots);
            var game = new GameSimulation(animations, geometry, playbook, configuration, randomFactory, seed,
                rosters, presentation, movementData, movementSettings, c2Bootstrap, handicapSettings, compatibility,
                fireEffects, kickMeterMetrics, defaultAudibles: defaultAudibles);
            game.BindSourceSoundVmData(legacySoundFunctions);
            return game;
        }
    }
}
