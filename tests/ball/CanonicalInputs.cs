using System;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class BallTestFactory
{
    internal static GameSimulation Create(FullKernelInputs inputs, int seed = 1,
        SourceMatchConfiguration configuration = null, Func<int, ISourceRandom> randomFactory = null,
        SourcePresentationSettings presentation = null, SourceHandicapSettings handicapSettings = null)
    {
        presentation = presentation ?? new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        if (inputs.IsC2Profile)
        {
            configuration = configuration ?? new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
        }
        if (handicapSettings != null)
            return inputs.Create(seed, configuration, randomFactory, presentation, handicapSettings: handicapSettings);
        return inputs.Create(seed, configuration, randomFactory, presentation);
    }
}
