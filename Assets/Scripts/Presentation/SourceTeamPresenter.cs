using System;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceTeamPresenter
    {
        private readonly SourceTeamPresentationAsset assets;
        private readonly Cache[] cache = new Cache[SliceRules.PlayersPerTeam * 2];
        private UnityEngine.Shader sourceFlashShader;
        public void BindSourceFireFlash(UnityEngine.Shader shader)
        {
            SourceFireGuard.Require(shader != null && shader.name == "Blitz/SourcePlayerFlashPortable" && shader.isSupported,
                "Bind the actual authored source-white shader,not a fallback tint.");
            sourceFlashShader = shader;
        }
        public SourceTeamRosterLibrary Rosters { get { return assets.Rosters; } }
        private sealed class Cache
        {
            internal SourceTeamAppearance Appearance;
            internal SourceTeamDrawState Draw;
            internal int Side0, Side1, Home;
        }

        public SourceTeamPresenter(SourceTeamPresentationAsset assets)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            this.assets = assets;
            assets.Load();
        }

        public void Apply(SourcePlayerRig rig, PlayerView actor, ISourcePresentationState snapshot)
        {
            SourceTeamDrawState draw = SourceTeamPresentationModel.DrawState(snapshot, actor);
            Apply(rig, actor, snapshot.Match.Configuration, (int)snapshot.Presentation.DesignatedHomeSide,
                snapshot.Ball.CarrierId, draw);
        }

        public void Apply(SourcePlayerRig rig, PlayerView actor, SourceMatchConfiguration match,
            int designatedHomeSide, int carrierId, SourceTeamDrawState draw)
        {
            if (rig == null || match == null) throw new ArgumentNullException(rig == null ? nameof(rig) : nameof(match));
            if (actor.Id < 0 || actor.Id >= cache.Length) throw new ArgumentOutOfRangeException(nameof(actor));
            if (!actor.Source.IsSpecified || actor.Id / 7 != (int)actor.Team ||
                draw.CustomHeadIndex >= 0 && actor.Source.ControllerStationId < 0)
                throw new InvalidOperationException("Original team actor/station/roster identity is inconsistent.");
            Cache current = cache[actor.Id];
            int side0 = match.HomeTeam.TeamIndex, side1 = match.AwayTeam.TeamIndex;
            if (current == null || current.Side0 != side0 || current.Side1 != side1 || current.Home != designatedHomeSide ||
                !draw.IsSpecified || !SameDraw(current.Draw, draw) ||
                !actor.Source.Roster.IsSpecified || current.Appearance.Player.Profile.Identity.TeamIndex != actor.Source.Roster.TeamIndex ||
                current.Appearance.Player.Profile.Identity.RosterIndex != actor.Source.Roster.RosterIndex ||
                current.Appearance.ModelScale != actor.ModelScale ||
                ((current.Appearance.Model.Index & 12) != 0) != (actor.Id == carrierId && !draw.PhantomBall))
            {
                SourceTeamAppearance appearance = SourceTeamPresentationModel.Select(assets.Rosters,
                    side0, side1, designatedHomeSide, actor, carrierId, draw);
                current = new Cache { Appearance = appearance, Draw = draw, Side0 = side0, Side1 = side1, Home = designatedHomeSide };
                cache[actor.Id] = current;
            }
            rig.ApplyTeamAppearance(assets, current.Appearance);
            if (sourceFlashShader != null)
                rig.ApplySourceFireFlash(new SourceFirePlayerFlashState(actor.Source.Presentation.RawFlags), sourceFlashShader);
        }

        private static bool SameDraw(SourceTeamDrawState a, SourceTeamDrawState b)
        {
            return a.IsSpecified == b.IsSpecified && a.HighCount == b.HighCount && a.QuarterbackMode == b.QuarterbackMode &&
                a.Flip == b.Flip && a.PhantomBall == b.PhantomBall && a.JumboBall == b.JumboBall &&
                a.CustomHeadIndex == b.CustomHeadIndex && a.HelmetPopped == b.HelmetPopped;
        }
    }
}
