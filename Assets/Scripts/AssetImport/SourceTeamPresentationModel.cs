using System;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public interface ISourceTeamPoppedHeadData
    {
        string ContentSha256 { get; }
    }

    public readonly struct SourceTeamDrawState
    {
        public bool IsSpecified { get; }
        public bool HighCount { get; }
        public bool QuarterbackMode { get; }
        public bool Flip { get; }
        public bool PhantomBall { get; }
        public bool JumboBall { get; }
        public bool HelmetPopped { get; }
        public int CustomHeadIndex { get; }

        public SourceTeamDrawState(bool highCount, bool quarterbackMode, bool flip,
            bool phantomBall, bool jumboBall, int customHeadIndex = -1, bool helmetPopped = false)
        {
            if (customHeadIndex < -1 || customHeadIndex >= 20)
                throw new ArgumentOutOfRangeException(nameof(customHeadIndex));
            IsSpecified = true;
            HighCount = highCount;
            QuarterbackMode = quarterbackMode;
            Flip = flip;
            PhantomBall = phantomBall;
            JumboBall = jumboBall;
            CustomHeadIndex = customHeadIndex;
            HelmetPopped = helmetPopped;
        }
    }

    public sealed class SourceTeamAppearance
    {
        public SourceRosterPlayer Player { get; }
        public SourceRosterUniform Uniform { get; }
        public SourceRosterModel Model { get; }
        public SourceRosterJersey Numbers { get; }
        public SourceRosterHead CustomHead { get; }
        public ISourceTeamPoppedHeadData PoppedHead { get; }
        public int DecalIndex { get; }
        public float ModelScale { get; }
        public string UniformIdentity { get { return Uniform.Slots[2].RuntimeFilename; } }
        public string SourcePlayerId { get { return Player.Profile.SourcePlayerId; } }
        public ReadOnlyCollection<int> DigitIndices { get { return Player.JerseyDigitIndices; } }

        internal SourceTeamAppearance(SourceRosterPlayer player, SourceRosterUniform uniform,
            SourceRosterModel model, SourceRosterJersey numbers, SourceRosterHead head, int decalIndex, float scale,
            ISourceTeamPoppedHeadData poppedHead)
        {
            Player = player;
            Uniform = uniform;
            Model = model;
            Numbers = numbers;
            CustomHead = head;
            DecalIndex = decalIndex;
            ModelScale = scale;
            PoppedHead = poppedHead;
        }
    }

    public static class SourceTeamPresentationModel
    {
        public static SourceTeamDrawState DrawState(ISourcePresentationState snapshot, PlayerView actor)
        {
            if (snapshot == null || snapshot.Presentation == null)
                throw new InvalidOperationException("Original home side, station heads and pup_bigball settings are unavailable.");
            return DrawState(snapshot.Presentation, actor, snapshot.Ball.Presentation);
        }

        public static SourceTeamDrawState DrawState(SourcePresentationSettings settings, PlayerView actor, SourceBallPresentationView ball)
        {
            if (settings == null)
                throw new InvalidOperationException("Original home side, station heads and pup_bigball settings are unavailable.");
            SourceActorPresentationView source = actor.Source.Presentation;
            if (!actor.Source.IsSpecified || !source.IsSpecified || !ball.IsSpecified)
                throw new InvalidOperationException("Original raw actor flags/plyrmode and ball flags must be published, not inferred from ActorRole/BallMode.");
            int head = -1, station = actor.Source.ControllerStationId;
            if (station >= 0) head = settings.StationHeads[station];
            return new SourceTeamDrawState((source.RawFlags & 0x04) != 0, source.RawPlayerMode == 5,
                (source.RawFlags & 0x01) != 0, (ball.RawFlags & 0x800) != 0,
                settings.PupBigBall != 0, head, (source.RawFlags & 0x00800000) != 0);
        }

        public static SourceTeamAppearance Select(SourceTeamRosterLibrary library, int side0Team,
            int side1Team, int designatedHomeSide, PlayerView actor, int carrierId, SourceTeamDrawState draw,
            ISourceTeamPoppedHeadData poppedHead = null)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (!draw.IsSpecified)
                throw new InvalidOperationException("Original player draw flags must be supplied explicitly; no visual mode is inferred from velocity.");
            if (!actor.Source.IsSpecified || !actor.Source.Roster.IsSpecified)
                throw new InvalidOperationException("Actor " + actor.Id + " has no original team/static-roster identity.");
            int side = (int)actor.Team;
            if (side < 0 || side > 1 || actor.Id < 0 || actor.Id >= SliceRules.PlayersPerTeam * 2 ||
                actor.Id / SliceRules.PlayersPerTeam != side)
                throw new InvalidOperationException("Original actor ID and match-side identity disagree.");
            int team = side == 0 ? side0Team : side1Team;
            if (actor.Source.Roster.TeamIndex != team)
                throw new InvalidOperationException("Actor " + actor.Id + " roster team does not match the selected source match team.");
            if (carrierId < -1 || carrierId >= SliceRules.PlayersPerTeam * 2)
                throw new ArgumentOutOfRangeException(nameof(carrierId));
            if (float.IsNaN(actor.ModelScale) || float.IsInfinity(actor.ModelScale) || actor.ModelScale <= 0f)
                throw new InvalidOperationException("Original actor scale must be finite and positive.");
            SourceRosterPlayer player = library.GetPlayerData(actor.Source.Roster);
            SourceRosterUniformPair pair = library.SelectMatchUniforms(side0Team, side1Team, designatedHomeSide);
            SourceUniformVariant variant = side == 0 ? pair.Side0 : pair.Side1;
            SourceRosterUniform uniform = library.GetUniform(team, variant, player.Skin);
            SourceHeldBallModel held = SourceHeldBallModel.None;
            if (actor.Id == carrierId && !draw.PhantomBall)
                held = draw.QuarterbackMode && draw.Flip ? SourceHeldBallModel.Left : SourceHeldBallModel.Right;
            SourceRosterModel model = library.ActorModels.SelectModel(player.BodySize, draw.HighCount, held, draw.JumboBall);
            SourceRosterJersey numbers = library.ActorModels.SelectJersey(player, draw.HighCount);
            SourceRosterHead head = draw.CustomHeadIndex < 0 ? null : library.Secrets.Heads[draw.CustomHeadIndex];
            if (head != null && actor.Source.ControllerStationId < 0)
                throw new InvalidOperationException("A source custom head belongs to a human station, not a static roster record or drone.");
            ISourceTeamPoppedHeadData selectedPoppedHead = head == null && draw.HelmetPopped ? poppedHead : null;
            if (head == null && draw.HelmetPopped && selectedPoppedHead == null)
                throw new InvalidOperationException("Original popped-head SDL reconstruction data is required for this raw helmet flag; no helmeted/generic head is substituted.");
            return new SourceTeamAppearance(player, uniform, model, numbers, head,
                side * 2 + uniform.DecalOffset, actor.ModelScale, selectedPoppedHead);
        }
    }
}
