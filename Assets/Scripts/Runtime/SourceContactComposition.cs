using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Runtime
{
    public static class SourceContactComposition
    {
        public static GameSimulation CreateSimulation(ISourceAnimationLibrary animations,
            IReadOnlyList<SourceVector3> referencePivots, SourcePlaybook playbook,
            SourceMatchConfiguration configuration, ISourceTeamRoster rosters, SourcePresentationSettings presentation,
            SourceMovementRuntimeData movementData, SourceMovementSettings movementSettings,
            SourceAudioFunctionData soundFunctions, Func<int, ISourceRandom> randomFactory = null, int seed = 1,
            SourceHandicapSettings handicapSettings = null)
        {
            SourceTeamResourceProfile.Contact272.RequireLibrary(animations);
            if (referencePivots == null || referencePivots.Count != SourceTiming.JointCount)
                throw new SourceInterfaceException("Contact272 geometry needs all fifteen actual imported reference pivots.");
            if (soundFunctions == null || soundFunctions.PayloadSha256 != SourceInterfacePackagePlan.SoundVmFunctionsSha256)
                throw new SourceInterfaceException("Contact272 requires exact trusted SAFN data before construction/first source wake.");
            if (configuration == null || rosters == null || presentation == null ||
                movementData == null || movementSettings == null || playbook == null)
                throw new SourceInterfaceException("Contact272 requires explicit original match/roster/presentation/movement/bootstrap inputs.");
            ISourceActorGeometry geometry = new SourceCatchGeometry(animations, referencePivots);
            var game = new GameSimulation(animations, geometry, playbook, configuration, randomFactory, seed,
                rosters, presentation, movementData, movementSettings, handicapSettings: handicapSettings);
            game.BindSourceSoundVmData(soundFunctions);
            return game;
        }
    }
}
