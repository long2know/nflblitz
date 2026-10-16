using System;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    // Explicit inspection fixture, not a gameplay/script/physics selector.
    public static class SourceTeamPoseFixture
    {
        public static PlayerView View(SourceRosterPlayer player, int actorId, FieldVector position,
            ISourceAnimationClip clip, int selectedOrdinal, int heading, long sourceTick, string originalScript,
            int controllerStation = -1)
        {
            if (player == null || clip == null) throw new ArgumentNullException(player == null ? nameof(player) : nameof(clip));
            if (actorId < 0 || actorId >= 14 || heading < 0 || heading > 1023)
                throw new ArgumentOutOfRangeException(nameof(actorId));
            SourceAnimationSample sample = SourceAnimationSample.FromSelectedOrdinals(clip, selectedOrdinal, selectedOrdinal);
            SourceAnimationFrame frame = clip.GetFrame(sample.SourceFrame);
            float scale = player.Profile.ModelScale;
            double angle = heading * Math.PI / 512d;
            return new PlayerView(actorId, (TeamId)(actorId / 7), position, default(FieldVector), 1f,
                facingDirection: new FieldVector((float)Math.Sin(angle), (float)Math.Cos(angle)),
                role: ActorRole.Unspecified,
                animation: new ActorAnimationView(sourceTick, ActorAction.Idle, AnimationSelectionEvidence.Approximate,
                    new SourceAnimationLayer(originalScript, 1, sample),
                    new SourceVector3(0f, frame.ScaledRoot.Y * scale, 0f)),
                modelScale: scale, source: new SourceActorView(controllerStation, player.Profile.Identity));
        }
    }
}
