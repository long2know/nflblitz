using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public static class SourceFireIds
    {
        public const int Spfx = 0x0f98, Player = 0x0500, PlayerFlash = 0x1300;
        public const int PuffObject = 0x510, PuffDrawOrder = 0x00050001, FlashWhite = 0x400;
        public const int InPlay = 2, PlayOver = 3, FrameCount = 17;
        public const string PortableAssetSet = "source-fire-spfx-authoring-tga-sdl-v1";
    }

    public enum SourceFireEntry { PlayerBurst, PlayerPuff, BallPuff, FlashPlayerWhite }
    public enum SourceFireOperation
    {
        CreateProcess, KillProcessGroup, DeleteObjectGroup, SuspendMask, ResumeMask,
        ResumeAll, HideAllObjects, UnhideAllObjects, ResetLineupLatches, ReinitializePlayerFlash, ResetBallProcessEffect, DirectSoundCall
    }
    public enum SourceFireOrdering { VerifiedDispatchWitness, PortableDeclaredSlots }
    public enum SourceFireClockOrigin { VerifiedOriginalPcount, PortableLogicalEpoch }
    public enum SourceFireStalePolicy { RejectUndefinedAccess, PortableCancelDeletedAndFreezeOrphan }
    public enum SourceFireSlotKind { EffectProcess, HostContinuation }
    public enum SourceFireBallEffect { None, Fire, PlayerCloud, BallTrail, Blink }
    public enum SourceFireCull { Off, Front, Back }

    public sealed class SourceFireGameplayConfiguration
    {
        public ISourceFireSpfxRuntime Runtime { get; }
        public int LogicalPcountAtTickZero { get; }
        public SourceFireCull AuthoredCull { get; }
        public string ExecutionOrigin { get { return "portable-authored-effects-before-C2-whistles-before-game-proc"; } }
        public SourceFireGameplayConfiguration(ISourceFireSpfxRuntime runtime, int logicalPcountAtTickZero,
            SourceFireCull authoredCull)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            SourceFireGuard.Require((int)authoredCull >= 0 && (int)authoredCull <= 2, "Explicit portable effect-pass cull required.");
            LogicalPcountAtTickZero = logicalPcountAtTickZero; AuthoredCull = authoredCull;
        }
    }

    public static class SourceFireGameplayOptions
    {
        public static bool PortableRequested(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            int query = url.IndexOf('?'), fragment = url.IndexOf('#');
            if (query < 0 || fragment >= 0 && fragment < query) return false;
            bool seen = false, enabled = false;
            foreach (string part in url.Substring(query + 1).Split('#')[0].Split('&'))
            {
                string[] pair = part.Split('=');
                if (Uri.UnescapeDataString(pair[0]) != "source-fire") continue;
                SourceFireGuard.Require(!seen && pair.Length == 2, "Specify source-fire exactly once.");
                string value = Uri.UnescapeDataString(pair[1]);
                SourceFireGuard.Require(value == "portable" || value == "disabled", "source-fire must be portable or disabled.");
                seen = true; enabled = value == "portable";
            }
            return enabled;
        }
    }

    public readonly struct SourceFireLineupScope
    {
        private readonly int side;
        public bool IsSpecified { get; }
        public int OriginalSide
        {
            get
            {
                SourceFireGuard.Require(IsSpecified, "Supply an explicit original lineup side; default scope is invalid.");
                return side;
            }
        }
        public int FirstActor { get { return OriginalSide == 1 ? 7 : 0; } }
        public int EndActorExclusive { get { return OriginalSide == 0 ? 7 : 14; } }

        private SourceFireLineupScope(int side) { this.side = side; IsSpecified = true; }
        public static SourceFireLineupScope FromOriginalSide(int side)
        {
            SourceFireGuard.Require(side >= -1 && side <= 1, "Lineup side must be 0, 1, or explicit all-player -1.");
            return new SourceFireLineupScope(side);
        }
    }

    public sealed class SourceFireBoundaryException : InvalidOperationException
    {
        public SourceFireBoundaryException(string message) : base(message) { }
    }

    public static class SourceFireGuard
    {
        public static void Require(bool condition, string message)
        { if (!condition) throw new SourceFireBoundaryException(message); }
        public static void Identity(string value, string name)
        { Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 1024, "Missing/bounded " + name + "."); }
        public static void Actor(int actor, bool global = false)
        { Require(actor >= (global ? -1 : 0) && actor < 14, "Original fire actor must be 0..13 (or explicit global -1)."); }
        public static void Finite(float value)
        { Require(!float.IsNaN(value) && !float.IsInfinity(value), "Nonfinite source fire input."); }
        public static void Finite(SourceVector3 value)
        { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        public static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new T[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return Array.AsReadOnly(copy);
        }
    }

    public readonly struct SourceFireSite
    {
        public long SourceTick { get; }
        public int OriginalPcount { get; }
        public long NativeProcessSerial { get; }
        public int NativeRunLevel { get; }
        public int WakeOrdinal { get; }
        public int WithinWakeCallOrdinal { get; }
        public int ActorId { get; }
        public string SourcePath { get; }
        public string Function { get; }
        public int SourceLine { get; }
        public SourceFireSite(long sourceTick, int originalPcount, long nativeProcessSerial, int nativeRunLevel,
            int wakeOrdinal, int withinWakeCallOrdinal, int actorId, string sourcePath, string function, int sourceLine)
        {
            SourceFireGuard.Require(sourceTick >= 0 && nativeProcessSerial > 0 && nativeRunLevel >= 0 &&
                wakeOrdinal >= 0 && withinWakeCallOrdinal >= 0 && sourceLine > 0, "Incomplete source fire call context.");
            SourceFireGuard.Actor(actorId, true);
            SourceFireGuard.Identity(sourcePath, nameof(sourcePath)); SourceFireGuard.Identity(function, nameof(function));
            SourceTick = sourceTick; OriginalPcount = originalPcount; NativeProcessSerial = nativeProcessSerial;
            NativeRunLevel = nativeRunLevel; WakeOrdinal = wakeOrdinal; WithinWakeCallOrdinal = withinWakeCallOrdinal;
            ActorId = actorId; SourcePath = sourcePath; Function = function; SourceLine = sourceLine;
        }
        public SourceFireSite At(string function, int line, int ordinal, int actorId)
        {
            return new SourceFireSite(SourceTick, OriginalPcount, NativeProcessSerial, NativeRunLevel,
                WakeOrdinal, ordinal, actorId, "player.c", function, line);
        }
    }

    public readonly struct SourceFireArgs
    {
        public int A0 { get; }
        public int A1 { get; }
        public int A2 { get; }
        public int A3 { get; }
        public SourceFireArgs(int a0, int a1, int a2, int a3 = 0)
        { A0 = a0; A1 = a1; A2 = a2; A3 = a3; }
    }

    public sealed class SourceFireDirectSound
    {
        public SourceFireSite Site { get; }
        public string Bank { get; }
        public int Call { get; }
        public int Volume { get; }
        public int Pan { get; }
        public string PriorityExpression { get; }
        public int? ResolvedPriority { get; }
        public SourceFireDirectSound(SourceFireSite site, string bank, int call, int volume, int pan,
            string priorityExpression, int? resolvedPriority)
        {
            SourceFireGuard.Identity(bank, nameof(bank)); SourceFireGuard.Identity(priorityExpression, nameof(priorityExpression));
            SourceFireGuard.Require(call >= 0 && volume >= 0 && volume <= 255 && pan >= 0 && pan <= 255, "Invalid direct sound arguments.");
            Site = site; Bank = bank; Call = call; Volume = volume; Pan = pan;
            PriorityExpression = priorityExpression; ResolvedPriority = resolvedPriority;
        }
    }

    public readonly struct SourceFireSoundSubmission
    {
        public bool Resolved { get; }
        public string Evidence { get; }
        public SourceFireSoundSubmission(bool resolved, string evidence)
        { SourceFireGuard.Identity(evidence, nameof(evidence)); Resolved = resolved; Evidence = evidence; }
    }

    public sealed class SourceFireCommand
    {
        public SourceFireSite Site { get; }
        public SourceFireOperation Operation { get; }
        public SourceFireEntry Entry { get; }
        public SourceFireArgs Args { get; }
        public string ProcessName { get; }
        public int GroupId { get; }
        public int Mask { get; }
        public SourceFireDirectSound Sound { get; }
        public SourceFireLineupScope LineupScope { get; }
        private SourceFireCommand(SourceFireSite site, SourceFireOperation operation, SourceFireEntry entry,
            SourceFireArgs args, string name, int group, int mask, SourceFireDirectSound sound,
            SourceFireLineupScope lineupScope = default(SourceFireLineupScope))
        {
            Site = site; Operation = operation; Entry = entry; Args = args; ProcessName = name;
            GroupId = group; Mask = mask; Sound = sound; LineupScope = lineupScope;
        }
        public static SourceFireCommand Create(SourceFireSite site, SourceFireEntry entry, SourceFireArgs args)
        {
            SourceFireGuard.Require((int)entry >= 0 && (int)entry <= 3, "Unknown active fire entry.");
            if (entry != SourceFireEntry.BallPuff) SourceFireGuard.Actor(args.A0);
            if (entry == SourceFireEntry.PlayerPuff)
                SourceFireGuard.Require(args.A1 >= 0 && args.A1 < 15, "Original joint index must be 0..14.");
            if (entry == SourceFireEntry.PlayerBurst || entry == SourceFireEntry.FlashPlayerWhite)
                SourceFireGuard.Require(args.A1 >= 0 && args.A2 > 0 && args.A2 <= 32767, "Invalid original wave/flash count or short sleep.");
            if (entry == SourceFireEntry.BallPuff)
                SourceFireGuard.Require(args.A0 == 3 || args.A0 == 4, "Active original ball puff bound is 3 or 4.");
            string name = entry == SourceFireEntry.PlayerBurst ? "fburst" : entry == SourceFireEntry.PlayerPuff ? "ofire" :
                entry == SourceFireEntry.BallPuff ? "smkpuf" : "flash";
            int group = entry == SourceFireEntry.FlashPlayerWhite ? SourceFireIds.PlayerFlash :
                entry == SourceFireEntry.BallPuff ? SourceFireIds.Player : SourceFireIds.Spfx;
            return new SourceFireCommand(site, SourceFireOperation.CreateProcess, entry, args, name, group, -1, null);
        }
        public static SourceFireCommand Control(SourceFireSite site, SourceFireOperation operation, int id = 0, int mask = -1)
        {
            SourceFireGuard.Require(operation != SourceFireOperation.CreateProcess && operation != SourceFireOperation.DirectSoundCall &&
                (int)operation >= 1 && (int)operation <= 10, "Use the typed process/sound constructors.");
            SourceFireGuard.Require(operation != SourceFireOperation.ReinitializePlayerFlash,
                "Use ReinitializePlayerFlash with an explicit typed lineup scope, not a control id or site actor.");
            // All active fire killall sites pass -1. A partial mask needs the missing target NODESTRUCT definition.
            SourceFireGuard.Require(operation != SourceFireOperation.KillProcessGroup || mask == -1,
                "Partial kill mask requires verified target NODESTRUCT metadata; active source sites use all bits.");
            return new SourceFireCommand(site, operation, default(SourceFireEntry), default(SourceFireArgs), null, id, mask, null);
        }
        public static SourceFireCommand ReinitializePlayerFlash(SourceFireSite site, SourceFireLineupScope scope)
        {
            SourceFireGuard.Require(scope.IsSpecified, "Supply an explicit typed lineup scope for flash clearing.");
            return new SourceFireCommand(site, SourceFireOperation.ReinitializePlayerFlash, default(SourceFireEntry),
                default(SourceFireArgs), null, 0, 0, null, scope);
        }
        public static SourceFireCommand DirectSound(SourceFireDirectSound sound)
        {
            if (sound == null) throw new ArgumentNullException(nameof(sound));
            return new SourceFireCommand(sound.Site, SourceFireOperation.DirectSoundCall, default(SourceFireEntry),
                default(SourceFireArgs), null, 0, 0, sound);
        }
    }

    public readonly struct SourceFireWakeSlot
    {
        public SourceFireSlotKind Kind { get; }
        public long ProcessSerial { get; }
        public string Function { get; }
        public int NativeRunLevel { get; }
        public bool WitnessedRestartVisit { get; }
        public SourceFireWakeSlot(SourceFireSlotKind kind, long processSerial, string function, int nativeRunLevel = 0, bool witnessedRestartVisit = false)
        {
            SourceFireGuard.Require((int)kind >= 0 && (int)kind <= 1 && processSerial > 0 && nativeRunLevel == 0,
                "The fire dispatch seam is the explicitly ordered native level-zero slice only.");
            SourceFireGuard.Identity(function, nameof(function));
            Kind = kind; ProcessSerial = processSerial; Function = function; NativeRunLevel = nativeRunLevel;
            WitnessedRestartVisit = witnessedRestartVisit;
        }
    }

    public sealed class SourceFireTickInput
    {
        public long SourceTick { get; }
        public int OriginalPcount { get; }
        public ReadOnlyCollection<SourceFireWakeSlot> Slots { get; }
        public string OrderEvidence { get; }
        public bool SortOccurred { get; }
        public ReadOnlyCollection<long> KnownEffectListOrder { get; }
        public SourceFireTickInput(long sourceTick, int originalPcount, IReadOnlyList<SourceFireWakeSlot> slots,
            string orderEvidence, bool sortOccurred = false, IReadOnlyList<long> knownEffectListOrder = null)
        {
            SourceFireGuard.Require(sourceTick >= 0, "Invalid source dispatch tick.");
            SourceFireGuard.Identity(orderEvidence, nameof(orderEvidence));
            SourceTick = sourceTick; OriginalPcount = originalPcount; Slots = SourceFireGuard.Copy(slots);
            OrderEvidence = orderEvidence; SortOccurred = sortOccurred;
            KnownEffectListOrder = knownEffectListOrder == null ? null : SourceFireGuard.Copy(knownEffectListOrder);
        }
    }

    public sealed class SourceFireBootstrap
    {
        public SourceFireOrdering Ordering { get; }
        public SourceFireClockOrigin ClockOrigin { get; }
        public SourceFireStalePolicy StalePolicy { get; }
        public long FirstSourceTick { get; }
        public int FirstPcount { get; }
        public string EpochEvidence { get; }
        public SourceFireBootstrap(SourceFireOrdering ordering, SourceFireClockOrigin clockOrigin,
            SourceFireStalePolicy stalePolicy, long firstSourceTick, int firstPcount, string epochEvidence)
        {
            SourceFireGuard.Require((int)ordering >= 0 && (int)ordering <= 1 && (int)clockOrigin >= 0 && (int)clockOrigin <= 1 &&
                (int)stalePolicy >= 0 && (int)stalePolicy <= 1 && firstSourceTick >= 0, "Explicit fire bootstrap modes required.");
            SourceFireGuard.Require(ordering != SourceFireOrdering.VerifiedDispatchWitness ||
                clockOrigin == SourceFireClockOrigin.VerifiedOriginalPcount, "Strict execution needs verified original pcount and dispatch witness.");
            SourceFireGuard.Identity(epochEvidence, nameof(epochEvidence));
            Ordering = ordering; ClockOrigin = clockOrigin; StalePolicy = stalePolicy;
            FirstSourceTick = firstSourceTick; FirstPcount = firstPcount; EpochEvidence = epochEvidence;
        }
    }

    public sealed class SourceFireCameraFrame
    {
        public long SourceTick { get; }
        public SourceVector3 Position { get; }
        public ReadOnlyCollection<float> Transform { get; }
        public string Provenance { get; }
        public SourceFireCameraFrame(long sourceTick, SourceVector3 position, IReadOnlyList<float> transform, string provenance)
        {
            SourceFireGuard.Require(sourceTick >= 0 && transform != null && transform.Count == 12, "Supply the actual paired 3x4 cambot transform.");
            SourceFireGuard.Identity(provenance, nameof(provenance)); SourceFireGuard.Finite(position);
            foreach (float value in transform) SourceFireGuard.Finite(value);
            SourceTick = sourceTick; Position = position; Transform = SourceFireGuard.Copy(transform); Provenance = provenance;
        }
    }

    public sealed class SourceFireCachedPlayerFrame
    {
        public int ActorId { get; }
        public long CacheSourceTick { get; }
        public SourceFireCameraFrame Camera { get; }
        public ReadOnlyCollection<SourceVector3> CameraJoints { get; }
        public string CacheProvenance { get; }
        public string RetentionEvidence { get; }
        public SourceFireCachedPlayerFrame(int actorId, long cacheSourceTick, SourceFireCameraFrame pairedCamera,
            IReadOnlyList<SourceVector3> actualFifteenCameraJoints, string cacheProvenance, string retentionEvidence = null)
        {
            SourceFireGuard.Actor(actorId); SourceFireGuard.Identity(cacheProvenance, nameof(cacheProvenance));
            SourceFireGuard.Require(pairedCamera != null && actualFifteenCameraJoints != null &&
                actualFifteenCameraJoints.Count == 15, "Missing historical jpos[15]/cambot pair; no resampled wrist fallback.");
            SourceFireGuard.Require(cacheSourceTick >= 0 && pairedCamera.SourceTick == cacheSourceTick,
                "Actual cached joints and paired cambot must have the same producer tick; never pair old jpos with the new camera.");
            if (retentionEvidence != null) SourceFireGuard.Identity(retentionEvidence, nameof(retentionEvidence));
            foreach (SourceVector3 value in actualFifteenCameraJoints) SourceFireGuard.Finite(value);
            ActorId = actorId; Camera = pairedCamera; CameraJoints = SourceFireGuard.Copy(actualFifteenCameraJoints);
            CacheSourceTick = cacheSourceTick; CacheProvenance = cacheProvenance; RetentionEvidence = retentionEvidence;
        }
    }

    public readonly struct SourceFireBallWorldSample
    {
        public long SourceTick { get; }
        public SourceVector3 WorldPosition { get; }
        public string Provenance { get; }
        public SourceFireBallWorldSample(long sourceTick, SourceVector3 worldPosition, string provenance)
        {
            SourceFireGuard.Require(sourceTick >= 0, "Missing current ball sample tick.");
            SourceFireGuard.Finite(worldPosition); SourceFireGuard.Identity(provenance, nameof(provenance));
            SourceTick = sourceTick; WorldPosition = worldPosition; Provenance = provenance;
        }
    }

    public readonly struct SourceFireRandomRequest
    {
        public SourceFireSite Site { get; }
        public int Bound { get; }
        public string Family { get { return "randrng"; } }
        public SourceFireRandomRequest(SourceFireSite site, int bound)
        { SourceFireGuard.Require(bound > 0, "randrng requires its original positive bound."); Site = site; Bound = bound; }
    }

    public sealed class SourceFireHostCallbacks
    {
        public Func<SourceFireRandomRequest, int> Draw { get; }
        public Func<int> ReadRawGameMode { get; }
        public Func<int, SourceFireCachedPlayerFrame> ReadCachedPlayer { get; }
        public Func<SourceFireBallWorldSample> ReadBallWorld { get; }
        public Func<int, int> ReadRawActorFlags { get; }
        public Action<int, int, SourceFireSite> WriteRawActorFlags { get; }
        public Action<SourceFireSite> EnterEffectWake { get; }
        public Action<SourceFireWakeSlot, int> RunHostContinuation { get; }
        public Func<SourceFireDirectSound, SourceFireSoundSubmission> SubmitSound { get; }
        public Func<long> AllocateEffectProcessSerial { get; }
        public SourceFireHostCallbacks(Func<SourceFireRandomRequest, int> draw, Func<int> readRawGameMode,
            Func<int, SourceFireCachedPlayerFrame> readCachedPlayer, Func<SourceFireBallWorldSample> readBallWorld,
            Func<int, int> readRawActorFlags, Action<int, int, SourceFireSite> writeRawActorFlags,
            Action<SourceFireSite> enterEffectWake, Action<SourceFireWakeSlot, int> runHostContinuation,
            Func<SourceFireDirectSound, SourceFireSoundSubmission> submitSound, Func<long> allocateEffectProcessSerial)
        {
            Draw = draw ?? throw new ArgumentNullException(nameof(draw)); ReadRawGameMode = readRawGameMode ?? throw new ArgumentNullException(nameof(readRawGameMode));
            ReadCachedPlayer = readCachedPlayer ?? throw new ArgumentNullException(nameof(readCachedPlayer));
            ReadBallWorld = readBallWorld ?? throw new ArgumentNullException(nameof(readBallWorld));
            ReadRawActorFlags = readRawActorFlags ?? throw new ArgumentNullException(nameof(readRawActorFlags));
            WriteRawActorFlags = writeRawActorFlags ?? throw new ArgumentNullException(nameof(writeRawActorFlags));
            EnterEffectWake = enterEffectWake ?? throw new ArgumentNullException(nameof(enterEffectWake));
            RunHostContinuation = runHostContinuation ?? throw new ArgumentNullException(nameof(runHostContinuation));
            SubmitSound = submitSound ?? throw new ArgumentNullException(nameof(submitSound));
            AllocateEffectProcessSerial = allocateEffectProcessSerial ?? throw new ArgumentNullException(nameof(allocateEffectProcessSerial));
        }
    }

    public sealed class SourceFireProcessView
    {
        public long Serial { get; }
        public long BirthTick { get; }
        public SourceFireEntry Entry { get; }
        public int GroupId { get; }
        public ushort SleepField { get; }
        public int Cursor { get; }
        public int End { get; }
        public long ObjectSerial { get; }
        public SourceFireArgs Args { get; }
        public SourceFireProcessView(long serial, long birthTick, SourceFireEntry entry, int group, ushort sleepField,
            int cursor, int end, long objectSerial, SourceFireArgs args)
        { Serial = serial; BirthTick = birthTick; Entry = entry; GroupId = group; SleepField = sleepField;
            Cursor = cursor; End = end; ObjectSerial = objectSerial; Args = args; }
    }

    public readonly struct SourceFireParticle
    {
        public long ObjectSerial { get; }
        public long ProcessSerial { get; }
        public SourceVector3 WorldPosition { get; }
        public float VelocityY { get; }
        public int TextureFrame { get; }
        public int IntegerAspect { get; }
        public bool Hidden { get; }
        public int ObjectId { get { return SourceFireIds.PuffObject; } }
        public int DrawOrder { get { return SourceFireIds.PuffDrawOrder; } }
        public string AssetSet { get { return SourceFireIds.PortableAssetSet; } }
        public SourceFireParticle(long objectSerial, long processSerial, SourceVector3 position, float velocityY,
            int frame, int aspect, bool hidden)
        {
            SourceFireGuard.Require(objectSerial > 0 && processSerial > 0 && frame >= 0 && frame < 17 &&
                aspect >= 0 && aspect <= 6, "Invalid resolved fire object/frame/aspect.");
            SourceFireGuard.Finite(position); SourceFireGuard.Finite(velocityY);
            ObjectSerial = objectSerial; ProcessSerial = processSerial; WorldPosition = position;
            VelocityY = velocityY; TextureFrame = frame; IntegerAspect = aspect; Hidden = hidden;
        }
    }

    public sealed class SourceFireRecord
    {
        public string Kind { get; }
        public SourceFireSite Site { get; }
        public long ProcessSerial { get; }
        public long ObjectSerial { get; }
        public int Value { get; }
        public string Detail { get; }
        public SourceFireRecord(string kind, SourceFireSite site, long processSerial, long objectSerial, int value, string detail)
        { Kind = kind; Site = site; ProcessSerial = processSerial; ObjectSerial = objectSerial; Value = value; Detail = detail; }
    }

    public sealed class SourceFireRenderBasis
    {
        public SourceFireCameraFrame Camera { get; }
        public int HorizontalResolution { get { return 512; } }
        public int VerticalResolution { get; }
        public SourceFireCull InheritedCull { get; }
        public string ProjectionOrigin { get { return "portable-continuous-source-664-not-MIPS-clamp"; } }
        public string DepthOrigin { get { return "Unity-projection-reciprocal-depth-not-native-WBUFFER"; } }
        public SourceFireRenderBasis(SourceFireCameraFrame camera, int verticalResolution, SourceFireCull inheritedCull)
        {
            SourceFireGuard.Require(camera != null && (verticalResolution == 256 || verticalResolution == 384) &&
                (int)inheritedCull >= 0 && (int)inheritedCull <= 2, "Explicit source camera, DIP raster and inherited cull state required.");
            Camera = camera; VerticalResolution = verticalResolution; InheritedCull = inheritedCull;
        }
    }

    public sealed class SourceFireRenderFrame
    {
        public long SourceTick { get; }
        public int OriginalPcount { get; }
        public SourceFireBootstrap Bootstrap { get; }
        public string OrderEvidence { get; }
        public SourceFireRenderBasis Basis { get; }
        public string AssetManifestSha256 { get; }
        public ReadOnlyCollection<SourceFireParticle> Particles { get; }
        public ReadOnlyCollection<int> ActorRawFlags { get; }
        public ReadOnlyCollection<SourceFireRecord> Records { get; }
        public ReadOnlyCollection<string> PortableBoundaries { get; }
        public bool NativePixelsQualified { get { return false; } }
        public SourceFireRenderFrame(long sourceTick, int pcount, SourceFireBootstrap bootstrap, string orderEvidence,
            SourceFireRenderBasis basis, IReadOnlyList<SourceFireParticle> particles, IReadOnlyList<int> flags,
            IReadOnlyList<SourceFireRecord> records, IReadOnlyList<string> boundaries, string assetManifestSha256)
        {
            SourceFireGuard.Require(sourceTick >= 0 && bootstrap != null && basis != null &&
                flags != null && flags.Count == 14 && assetManifestSha256 != null && assetManifestSha256.Length == 64,
                "Incomplete immutable fire render frame or authenticated portable asset identity.");
            SourceTick = sourceTick; OriginalPcount = pcount; Bootstrap = bootstrap; OrderEvidence = orderEvidence; Basis = basis;
            Particles = SourceFireGuard.Copy(particles); ActorRawFlags = SourceFireGuard.Copy(flags);
            Records = SourceFireGuard.Copy(records); PortableBoundaries = SourceFireGuard.Copy(boundaries);
            AssetManifestSha256 = assetManifestSha256;
        }
    }

    public readonly struct SourceFirePlayerEmitterInput
    {
        public int ActorId { get; }
        public bool TeamFire { get; }
        public int Station { get; }
        public float NativeVelocityX { get; }
        public float NativeVelocityZ { get; }
        public int RawFlags { get; }
        public bool LowerModeReacting { get; }
        public bool Offense { get; }
        public bool QuarterbackMode { get; }
        public SourceFirePlayerEmitterInput(int actorId, bool teamFire, int station, float nativeVelocityX,
            float nativeVelocityZ, int rawFlags, bool lowerModeReacting, bool offense, bool quarterbackMode)
        {
            SourceFireGuard.Actor(actorId); SourceFireGuard.Finite(nativeVelocityX); SourceFireGuard.Finite(nativeVelocityZ);
            SourceFireGuard.Require(station >= -1 && station <= 3, "Supply original station, not a human-role inference.");
            ActorId = actorId; TeamFire = teamFire; Station = station; NativeVelocityX = nativeVelocityX;
            NativeVelocityZ = nativeVelocityZ; RawFlags = rawFlags; LowerModeReacting = lowerModeReacting;
            Offense = offense; QuarterbackMode = quarterbackMode;
        }
    }

    public readonly struct SourceFireBallEmitterInput
    {
        public SourceBallKind Kind { get; }
        public bool FirePass { get; }
        public bool TurboPass { get; }
        public bool Bounce { get; }
        public bool OffenseTeamFire { get; }
        public int PostIncrementFlightTime { get; }
        public SourceFireBallEmitterInput(SourceBallKind kind, bool firePass, bool turboPass, bool bounce,
            bool offenseTeamFire, int postIncrementFlightTime)
        {
            SourceFireGuard.Require(kind != SourceBallKind.Unspecified && Enum.IsDefined(typeof(SourceBallKind), kind) &&
                postIncrementFlightTime >= 0, "Explicit named ball type and post-increment flight time required.");
            Kind = kind; FirePass = firePass; TurboPass = turboPass; Bounce = bounce;
            OffenseTeamFire = offenseTeamFire; PostIncrementFlightTime = postIncrementFlightTime;
        }
    }

    public interface ISourceFireSpfxRuntime
    {
        void Reset(SourceFireBootstrap bootstrap);
        ReadOnlyCollection<SourceFireProcessView> Processes { get; }
        ReadOnlyCollection<SourceFireRecord> CurrentRecords { get; }
        SourceFireTickInput PortableHeadThenHostTick(long sourceTick, int pcount, IReadOnlyList<SourceFireWakeSlot> declaredHostSlots);
        void BeginSourceTick(SourceFireTickInput input, SourceFireHostCallbacks host);
        long Apply(SourceFireCommand command);
        SourceFireRenderFrame CaptureFrame(SourceFireRenderBasis basis);
        void MaybePlayerFire(SourceFireSite site, SourceFirePlayerEmitterInput input);
        SourceFireBallEffect BallFreeOrPhantomStep(SourceFireSite site, SourceFireBallEmitterInput input);
        void WeaponPreSnapTail(SourceFireSite site, int actorId, int lastReceiver, int consecutiveCompletions,
            bool teamFire, bool onsideKick, bool kickoff);
        void DefensivePreSnapTail(SourceFireSite site, int actorId, bool teamFire);
        void SuccessfulOffensiveCatch(SourceFireSite site, int actorId, int lastReceiver,
            int consecutiveCompletions, int rawTeamFire, bool fieldXAtOrBeyondLos);
        void DefensiveSecondLossFireSite(SourceFireSite site, string actualAnnouncerBank, int? selectedQbHitPartner,
            int? resolvedLevel4Priority);
    }
}
