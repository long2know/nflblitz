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
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null)
        {
            return CreateChecked(SourceTeamResourceProfile.Contact281C2, animations, referencePivots, playbook,
                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings);
        }

        public static GameSimulation CreateM1Simulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourceHandicapSettings handicapSettings = null)
        {
            return CreateChecked(SourceTeamResourceProfile.Contact281C2M1, animations, referencePivots, playbook,
                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,
                c2Bootstrap, randomFactory, seed, handicapSettings);
        }

        private static GameSimulation CreateChecked(SourceTeamResourceProfile profile, ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData legacySoundFunctions, SourceC2Bootstrap c2Bootstrap,
            Func<int, ISourceRandom> randomFactory, int seed, SourceHandicapSettings handicapSettings)
        {
            profile.RequireLibrary(animations);
            if (c2Bootstrap == null || c2Bootstrap.EntryKind != SourceC2EntryKind.OriginalProgramBootSoundBanks ||
                c2Bootstrap.Data.PayloadSha256 != (profile.IsContact281C2M1
                    ? SourceInterfacePackagePlan.C2M1CallbackSha256 : SourceInterfacePackagePlan.C2CallbackSha256) ||
                c2Bootstrap.Data.Profile != (profile.IsContact281C2M1
                    ? SourceC2CallbackData.M1Profile : SourceC2CallbackData.EntryProfile) ||
                c2Bootstrap.Data.AnimationManifestSha256 != SourceTeamResourceProfile.C2AnimationSha256 ||
                c2Bootstrap.Data.HitExtensionData.PayloadSha256 != SourceInterfacePackagePlan.C2HitSha256)
                throw new SourceInterfaceException("C2 requires its explicit original sound-bank entry/bootstrap BEFORE constructor bank draws and initial AI.");
            if (legacySoundFunctions == null || legacySoundFunctions.PayloadSha256 != SourceInterfacePackagePlan.SoundVmFunctionsSha256)
                throw new SourceInterfaceException("C2 keeps the exact unchanged legacy six-binding SAFN before first wake.");
            if (referencePivots == null || referencePivots.Count != SourceTiming.JointCount ||
                playbook == null || configuration == null || rosters == null || presentation == null ||
                movementData == null || movementSettings == null)
                throw new SourceInterfaceException("C2 requires the actual fifteen reference pivots and explicit complete kernel inputs.");
            ISourceActorGeometry geometry = new SourceCatchGeometry(animations, referencePivots);
            var game = new GameSimulation(animations, geometry, playbook, configuration, randomFactory, seed,
                rosters, presentation, movementData, movementSettings, c2Bootstrap, handicapSettings);
            game.BindSourceSoundVmData(legacySoundFunctions);
            return game;
        }
    }
}
