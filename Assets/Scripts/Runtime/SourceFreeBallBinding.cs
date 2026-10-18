using System;
using Blitz.Gameplay;
using Blitz.Presentation;

namespace Blitz.Runtime
{
    public static class SourceFreeBallBinding
    {
        public static SourceFreeBallDraw Capture(GameSimulation simulation)
        {
            if (simulation == null || simulation.Profile != SimulationProfile.OriginalRelease57Hz)
                throw new InvalidOperationException("Free-ball binding requires the actual Source57 simulation.");
            // Read-only same-assembly projection of already-maintained ball_proc fields.
            // No new simulation state, reconstruction, ticking, setters or physics changes.
            SimulationState model = simulation.Model;
            var state = new SourceFreeBallState(model.SourceTick,
                model.CarrierId == -1 || (model.SourceBallFlags & SourceBallFlags.Phantom) != 0,
                model.SourceBallPosition, model.SourceBallHeading, model.SourceBallElevation,
                model.SourceBallRoll, model.SourceBallScale, unchecked((int)model.SourceBallFlags),
                SourceInterfacePresentation.Settings(simulation.State).PupBigBall);
            return SourceFreeBallRenderModel.Evaluate(state, model.SourcePlaybook.Directions);
        }
    }
}
