using System;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class SourceKickMeterProcess
    {
        private static readonly float[] Speeds = {
            1.15f * 7.5f, 1.10f * 7.5f, 1.05f * 7.5f,
            1.00f * 7.5f, 0.95f * 7.5f, 0.90f * 7.5f
        };
        private readonly SourceKickMeterMetrics metrics;
        private readonly SourceKickMeterKind kind;
        private readonly TeamId owner;
        private readonly int sourceTeam;
        private readonly int attribute;
        private readonly bool fake;
        private readonly long created;
        private readonly float wind;
        private readonly int markerOffset;
        private readonly int markerLeft;
        private readonly int markerRight;
        private readonly int limit;
        private SourceKickMeterStatus status = SourceKickMeterStatus.Starting;
        private long tick;
        private long nextWake;
        private long hideTick = -1;
        private int position;
        private int velocity;
        private int spriteOffset;
        private bool highlighted;
        private float accuracy;
        private int strength;
        private int bounces;

        internal SourceKickMeterProcess(SourceKickMeterMetrics metrics, SourceKickMeterKind kind,
            TeamId owner, int sourceTeam, int attribute, bool fake, long created, float wind, int strength)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            if (kind != SourceKickMeterKind.FieldGoal && kind != SourceKickMeterKind.Punt)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (attribute < 0 || attribute >= Speeds.Length) throw new ArgumentOutOfRangeException(nameof(attribute));
            this.metrics = metrics; this.kind = kind; this.owner = owner; this.sourceTeam = sourceTeam;
            this.attribute = attribute; this.fake = fake; this.created = created; this.wind = wind;
            this.strength = strength; tick = created; nextWake = created + 1;
            velocity = (int)(Speeds[attribute] * 10000.0f);
            limit = kind == SourceKickMeterKind.FieldGoal
                ? (int)(metrics.FieldGoalBarWidth * 10000.0f * 0.5f)
                : (int)(metrics.PuntMeterHeight * (0.40f * 10000.0f));
            markerOffset = kind == SourceKickMeterKind.FieldGoal ? (int)(wind * (-10.0f * 4.0f)) : 0;
            int half = kind == SourceKickMeterKind.FieldGoal ? (metrics.FieldGoalMarkerWidth + 1) >> 1 : 0;
            markerLeft = (markerOffset - half) * 10000;
            markerRight = (markerOffset + half) * 10000;
        }

        internal SourceKickMeterView View
        {
            get
            {
                return new SourceKickMeterView(kind, status, owner, sourceTeam, attribute, fake, created,
                    tick, nextWake, hideTick, position, velocity, limit, spriteOffset, markerOffset,
                    markerLeft, markerRight, highlighted, wind, accuracy, strength, bounces);
            }
        }

        internal void Cancel(long sourceTick)
        {
            tick = sourceTick;
            status = SourceKickMeterStatus.Cancelled;
            nextWake = -1;
        }

        // Continuation of plyrinfo.c:682/893; this wake precedes the player processes.
        internal bool Wake(long sourceTick, bool preSnap, bool hiking, int autoHikeCounter, bool playOver,
            Func<int, int> random)
        {
            if (nextWake < 0 || sourceTick < nextWake) return false;
            if (sourceTick != nextWake)
                throw new InvalidOperationException("A kick meter needs every ordered source-process wake.");
            tick = sourceTick;
            nextWake = sourceTick + 1;
            if (status == SourceKickMeterStatus.Locked)
            {
                if (sourceTick == hideTick) { status = SourceKickMeterStatus.Complete; nextWake = -1; }
                return false;
            }
            if (status == SourceKickMeterStatus.TimeExpired)
            {
                if (playOver) { status = SourceKickMeterStatus.Complete; nextWake = -1; }
                return false;
            }
            if (preSnap && !hiking)
            {
                status = SourceKickMeterStatus.Tracking;
                position += velocity;
                if (kind == SourceKickMeterKind.FieldGoal)
                {
                    if (position > limit && velocity > 0 || position < -limit && velocity < 0)
                    {
                        position = velocity > 0 ? limit : -limit;
                        velocity = -velocity;
                        bounces++;
                    }
                    highlighted = position >= markerLeft && position <= markerRight;
                }
                else
                {
                    if (position > limit || position < -limit) { velocity = -velocity; bounces++; }
                    highlighted = velocity > 0 && position <= velocity && position >= -velocity
                        || velocity < 0 && position <= -velocity && position >= velocity;
                }
                spriteOffset = position / 10000;
                return false;
            }
            if (fake)
            {
                status = SourceKickMeterStatus.Complete;
                nextWake = -1;
                return false;
            }
            if (autoHikeCounter == 0)
            {
                status = SourceKickMeterStatus.TimeExpired;
                if (kind == SourceKickMeterKind.FieldGoal)
                    accuracy = (55 + random(35)) * ((random(2) << 1) - 1);
                if (playOver) { status = SourceKickMeterStatus.Complete; nextWake = -1; }
            }
            else
            {
                status = SourceKickMeterStatus.Locked;
                hideTick = sourceTick + 30;
                if (kind == SourceKickMeterKind.FieldGoal)
                    accuracy = (spriteOffset - markerOffset) * 200.0f / metrics.FieldGoalBarWidth;
                else
                {
                    position = Math.Max(-limit, Math.Min(limit, position));
                    strength = 65 * (limit - Math.Abs(position)) / limit + 25;
                }
                // sleep(30), not thirty render/host updates.
                nextWake = hideTick;
            }
            return true;
        }
    }

    public sealed partial class GameSimulation
    {
        private bool UsesPortableHumanKick(SourceKickMeterKind kind, TeamId team)
        {
            return sourceKickMeterMetrics != null && Model.SourceKickMeter != null
                && Model.SourceKickMeter.View.Kind == kind && !Model.SourceKickMeter.View.Fake
                && Model.SourceKickMeter.View.Owner == team;
        }

        private void InitializeSourceKickMeter()
        {
            Model.SourceKickMeter = null;
            Model.SourceKickLaunch = default(SourceKickLaunchView);
            if (sourceKickMeterMetrics == null || !SourceContactDriver.IsHumanTeam(Model, Model.Possession)) return;
            SourcePlayFlags flags = Model.SourceOffensePlay.Flags;
            SourceKickMeterKind kind = (flags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.FakeFieldGoal)) != 0
                ? SourceKickMeterKind.FieldGoal
                : (flags & (SourcePlayFlags.Punt | SourcePlayFlags.FakePunt)) != 0
                    ? SourceKickMeterKind.Punt : SourceKickMeterKind.None;
            if (kind == SourceKickMeterKind.None) return;
            SourceTeamRosterLibrary rosters = sourceRosters as SourceTeamRosterLibrary;
            if (rosters == null) throw new InvalidOperationException("Kick meters need the actual original team TA_SPTEAMS.");
            int team = Model.Possession == TeamId.Home ? sourceConfiguration.HomeTeam.TeamIndex
                : sourceConfiguration.AwayTeam.TeamIndex;
            bool fake = (flags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.Punt)) == 0;
            Model.SourceKickMeter = new SourceKickMeterProcess(sourceKickMeterMetrics, kind, Model.Possession,
                team, rosters.GetTeamData(team).Ratings.SpecialTeams, fake, Model.SourceTick,
                kind == SourceKickMeterKind.FieldGoal ? Model.SourceKickWind : 0f, Model.SourcePuntStrength);
        }

        private void StepSourceKickMeter()
        {
            SourceKickMeterProcess process = Model.SourceKickMeter;
            if (process == null) return;
            if (Model.Phase == GamePhase.PlaySelect || Model.Phase == GamePhase.GameOver)
            {
                if (process.View.ShowMeterFlag || process.View.Status == SourceKickMeterStatus.Starting)
                    process.Cancel(Model.SourceTick);
                return;
            }
            if (Model.SourceTick < process.View.NextWakeSourceTick || process.View.NextWakeSourceTick < 0) return;
            string name = process.View.Kind == SourceKickMeterKind.FieldGoal ? "kick_meter_proc" : "punt_meter_proc";
            BeginSourceProcess(name, -1, 0);
            int counter = (Model.SourceFlags & SourceGameFlags.HikingBall) != 0 || Model.SourceSnapTick >= process.View.CreatedSourceTick
                ? Model.SourceHikeCounterAtSnap : Model.PreSnapTicksRemaining;
            if (process.Wake(Model.SourceTick, Model.Phase == GamePhase.PreSnap,
                (Model.SourceFlags & SourceGameFlags.HikingBall) != 0, counter,
                Model.Phase == GamePhase.DeadBall, SourceDrawFor("plyrinfo.c:" + name + ":auto_hike")))
            {
                if (process.View.Kind == SourceKickMeterKind.FieldGoal)
                    Model.SourceKickMeterPercent = process.View.AccuracyPercent;
                else Model.SourcePuntStrength = process.View.PuntStrength;
            }
        }

        private SourceVector3 SourceKickCachedJoint(ActorState actor, int joint)
        {
            if (Model.SourceCameraJointTick != Model.SourceTick - 1 || Model.SourceMovementCameraTick != Model.SourceTick)
                throw new InvalidOperationException("Original kick release requires the previous player jpos cache and current camera matrix.");
            return ContactCachedWorldJoint(actor, joint);
        }

        private void CaptureSourceKickLaunch(ActorState actor, SourceKickMeterKind kind)
        {
            if (!UsesPortableHumanKick(kind, actor.Team)) return;
            Model.SourceKickLaunch = new SourceKickLaunchView(kind, Model.SourceTick, actor.Id,
                actor.AnimationState.Primary.FrameOrdinal, Model.SourceBallPosition,
                new SourceVector3(Model.SourceBallVelocity.X, Model.SourceBallVerticalVelocity, Model.SourceBallVelocity.Z),
                Model.SourceBallTarget, SourcePlayerDriver.WorldPosition(actor), Model.PassTicksTotal);
        }
    }
}
