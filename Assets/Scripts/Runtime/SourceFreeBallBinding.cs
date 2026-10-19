using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;
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

        public static ReadOnlyCollection<SourceFreeBallDraw> CaptureTrails(GameSimulation simulation)
        {
            if (simulation == null || simulation.Profile != SimulationProfile.OriginalRelease57Hz)
                throw new InvalidOperationException("Football-trail binding requires the actual Source57 simulation.");
            var draws = new List<SourceFreeBallDraw>();
            SourceFireRenderFrame frame = simulation.SourceFireFrame;
            if (frame == null) return draws.AsReadOnly();
            SimulationState model = simulation.Model;
            if (frame.SourceTick != model.SourceTick)
                throw new InvalidOperationException("Football ghosts must come from the completed current source dispatch.");
            int bigBall = SourceInterfacePresentation.Settings(simulation.State).PupBigBall;
            foreach (SourceBallTrail trail in frame.BallTrails)
                draws.Add(SourceFreeBallRenderModel.EvaluateTrail(trail, model.SourceTick, model.CarrierId,
                    unchecked((int)model.SourceBallFlags), bigBall, model.SourcePlaybook.Directions));
            return draws.AsReadOnly();
        }

        public static ReadOnlyCollection<SourceLooseHelmetDraw> CaptureHelmets(GameSimulation simulation)
        {
            if (simulation == null || simulation.Profile != SimulationProfile.OriginalRelease57Hz)
                throw new InvalidOperationException("Loose-helmet binding requires the actual Source57 simulation.");
            var draws = new List<SourceLooseHelmetDraw>();
            SourceFireRenderFrame frame = simulation.SourceFireFrame;
            if (frame == null) return draws.AsReadOnly();
            SourceFireGuard.Require(frame.SourceTick == simulation.Model.SourceTick,
                "Detached helmet draws must bind the actual completed current source dispatch.");
            foreach (SourceLooseHelmet helmet in frame.LooseHelmets)
                draws.Add(SourceFreeBallRenderModel.EvaluateHelmet(helmet, frame.SourceTick,
                    frame.GlobalBallFlags, simulation.Model.SourcePlaybook.Directions));
            return draws.AsReadOnly();
        }
    }
}
