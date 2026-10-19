using System;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceCinematicPresenter
    {
        public SourceCinematicPlayback Playback { get; }
        public SourceCinematicPresenter(SourceCinematicPlayback playback)
        {
            Playback = playback ?? throw new ArgumentNullException(nameof(playback));
        }

        public bool Apply(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (!Playback.HasRenderFrame && !Playback.UsesOriginalNormalCamera) return false;
            SourceCinematicSeed frame = Playback.UsesOriginalNormalCamera ? Playback.SourceState : Playback.Frame.State;
            SourceVector3 eye = frame.Eye, focus = frame.Focus;
            camera.transform.position = new Vector3(eye.X, eye.Y, eye.Z) / SourceTiming.SourceUnitsPerYard;
            camera.transform.LookAt(new Vector3(focus.X, focus.Y, focus.Z) / SourceTiming.SourceUnitsPerYard);
            return true;
        }
    }
}
