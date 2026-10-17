using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Runtime
{
    public static class SourceInterfacePresentation
    {
        public static SourcePresentationSettings Settings(IGameState state)
        {
            var source = state as ISourcePresentationState;
            if (source == null || source.Presentation == null)
                throw new SourceInterfaceException("Original renderer requires actual home-side/station-head/pup_bigball metadata; no TeamId/empty-head default is inferred.");
            return source.Presentation;
        }

        public static SourceTeamDrawState DrawState(PlayerView actor, IGameState state)
        {
            SourcePresentationSettings settings = Settings(state);
            if (!actor.Source.IsSpecified || !actor.Source.Presentation.IsSpecified || !state.Ball.Presentation.IsSpecified)
                throw new SourceInterfaceException("Original actor/ball raw presentation flags are unspecified.");
            int station = actor.Source.ControllerStationId;
            int head = -1;
            if (station >= 0)
            {
                var match = (ISourceMatchState)state;
                if (SourceCabinetStations.View(match.Match, station).ControlledPlayerId != actor.Id)
                    throw new SourceInterfaceException("Original head/held-ball mapping requires the actual station controller, not a captain/team offset.");
                head = settings.StationHeads[station];
            }
            int flags = actor.Source.Presentation.RawFlags;
            return new SourceTeamDrawState((flags & 0x04) != 0, actor.Source.Presentation.RawPlayerMode == 5,
                (flags & 0x01) != 0, (state.Ball.Presentation.RawFlags & 0x800) != 0,
                settings.PupBigBall != 0, head, (flags & 0x00800000) != 0);
        }
    }
}
