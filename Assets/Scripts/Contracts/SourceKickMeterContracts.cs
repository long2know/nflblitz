using System;

namespace Blitz.Contracts
{
    public enum SourceKickMeterMode { Disabled = 0, PortableAuthored = 1 }
    public enum SourceKickMeterKind { None = 0, FieldGoal = 1, Punt = 2 }
    public enum SourceKickMeterStatus { Unspecified, Starting, Tracking, Locked, TimeExpired, Complete, Cancelled }

    public static class SourceKickMeterOptions
    {
        public static SourceKickMeterMode FromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return SourceKickMeterMode.Disabled;
            int query = url.IndexOf('?'), fragment = url.IndexOf('#');
            if (query < 0 || fragment >= 0 && fragment < query) return SourceKickMeterMode.Disabled;
            string text = url.Substring(query + 1, (fragment < 0 ? url.Length : fragment) - query - 1);
            bool found = false;
            SourceKickMeterMode mode = SourceKickMeterMode.Disabled;
            foreach (string part in text.Split('&'))
            {
                int equals = part.IndexOf('=');
                string key = Uri.UnescapeDataString(equals < 0 ? part : part.Substring(0, equals));
                if (key != "source-kick-meters") continue;
                if (found) throw new ArgumentException("Specify source-kick-meters only once.", nameof(url));
                found = true;
                string value = Uri.UnescapeDataString(equals < 0 ? "" : part.Substring(equals + 1));
                if (value == "disabled") mode = SourceKickMeterMode.Disabled;
                else if (value == "portable-authored") mode = SourceKickMeterMode.PortableAuthored;
                else throw new NotSupportedException("source-kick-meters supports only disabled or portable-authored. "
                    + "Original generated native image_info_t metrics remain unknown.");
            }
            return mode;
        }
    }

    public sealed class SourceKickMeterMetrics
    {
        public const string AuthoredInterfaceSha256 = "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435";
        public SourceKickMeterMode Mode { get { return SourceKickMeterMode.PortableAuthored; } }
        public string ContentSha256 { get; }
        public int FieldGoalBarWidth { get; }
        public int FieldGoalMarkerWidth { get; }
        public int PuntMeterHeight { get; }
        public bool NativeGeneratedMetricsKnown { get { return false; } }

        // Only the existing strict authored-interface reader creates this opt-in input.
        internal SourceKickMeterMetrics(string hash, int barWidth, int markerWidth, int puntHeight)
        {
            if (hash != AuthoredInterfaceSha256) throw new ArgumentException("The exact authored HUD input is required.", nameof(hash));
            SourceContractGuard.Range(barWidth, 1, 2048, nameof(barWidth));
            SourceContractGuard.Range(markerWidth, 1, 2048, nameof(markerWidth));
            SourceContractGuard.Range(puntHeight, 1, 2048, nameof(puntHeight));
            ContentSha256 = hash;
            FieldGoalBarWidth = barWidth;
            FieldGoalMarkerWidth = markerWidth;
            PuntMeterHeight = puntHeight;
        }
    }

    public readonly struct SourceKickMeterView
    {
        public bool IsSpecified { get { return Kind != SourceKickMeterKind.None; } }
        public SourceKickMeterKind Kind { get; }
        public SourceKickMeterStatus Status { get; }
        public TeamId Owner { get; }
        public int SourceTeamIndex { get; }
        public int SpecialTeamsAttribute { get; }
        public bool Fake { get; }
        public long CreatedSourceTick { get; }
        public long SourceTick { get; }
        public long NextWakeSourceTick { get; }
        public long HideSourceTick { get; }
        public int PositionFixed10000 { get; }
        public int VelocityFixed10000 { get; }
        public int LimitFixed10000 { get; }
        public int SpriteOffsetPixels { get; }
        public int MarkerOffsetPixels { get; }
        public int MarkerLeftFixed10000 { get; }
        public int MarkerRightFixed10000 { get; }
        public bool Highlighted { get; }
        public float Wind { get; }
        public float AccuracyPercent { get; }
        public int PuntStrength { get; }
        public int BounceCount { get; }
        public bool ShowMeterFlag
        {
            get { return Status == SourceKickMeterStatus.Tracking || Status == SourceKickMeterStatus.Locked
                || Status == SourceKickMeterStatus.TimeExpired; }
        }

        internal SourceKickMeterView(SourceKickMeterKind kind, SourceKickMeterStatus status, TeamId owner,
            int sourceTeamIndex, int attribute, bool fake, long created, long tick, long nextWake, long hideTick,
            int position, int velocity, int limit, int spriteOffset, int markerOffset, int markerLeft,
            int markerRight, bool highlighted, float wind, float accuracy, int strength, int bounces)
        {
            Kind = kind; Status = status; Owner = owner; SourceTeamIndex = sourceTeamIndex;
            SpecialTeamsAttribute = attribute; Fake = fake; CreatedSourceTick = created; SourceTick = tick;
            NextWakeSourceTick = nextWake; HideSourceTick = hideTick; PositionFixed10000 = position;
            VelocityFixed10000 = velocity; LimitFixed10000 = limit; SpriteOffsetPixels = spriteOffset;
            MarkerOffsetPixels = markerOffset; MarkerLeftFixed10000 = markerLeft;
            MarkerRightFixed10000 = markerRight; Highlighted = highlighted; Wind = wind;
            AccuracyPercent = accuracy; PuntStrength = strength; BounceCount = bounces;
        }
    }

    // Explicit snapshot extension: disabled snapshots retain their existing public/serialized shape.
    public interface ISourceKickMeterState
    {
        SourceKickMeterMode KickMeterMode { get; }
        SourceKickMeterMetrics KickMeterMetrics { get; }
        SourceKickMeterView KickMeter { get; }
        SourceKickLaunchView LastKickLaunch { get; }
    }

    public readonly struct SourceKickLaunchView
    {
        public bool IsSpecified { get { return Kind != SourceKickMeterKind.None; } }
        public SourceKickMeterKind Kind { get; }
        public long SourceTick { get; }
        public int ActorPlayerId { get; }
        public int AnimationOrdinal { get; }
        // Observed at the original release caller, BEFORE ball_proc integrates this wake.
        public SourceVector3 Position { get; }
        public SourceVector3 Velocity { get; }
        public SourceVector3 Target { get; }
        public SourceVector3 KickerPosition { get; }
        public int TotalSourceTicks { get; }

        internal SourceKickLaunchView(SourceKickMeterKind kind, long sourceTick, int actor, int ordinal,
            SourceVector3 position, SourceVector3 velocity, SourceVector3 target,
            SourceVector3 kickerPosition, int totalTicks)
        {
            Kind = kind; SourceTick = sourceTick; ActorPlayerId = actor; AnimationOrdinal = ordinal;
            Position = position; Velocity = velocity; Target = target; KickerPosition = kickerPosition;
            TotalSourceTicks = totalTicks;
        }
    }
}
