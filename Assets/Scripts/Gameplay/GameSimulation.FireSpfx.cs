using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        private readonly SourceFireGameplayConfiguration sourceFireConfiguration;
        private readonly SourceFireCachedPlayerFrame[] sourceFireCaches = new SourceFireCachedPlayerFrame[14];
        private readonly Dictionary<long, long> sourceFireWhistleSerials = new Dictionary<long, long>();
        private readonly List<SourceFireDirectSound> sourceFireSounds = new List<SourceFireDirectSound>();
        private SourceFireCameraFrame sourceFireCamera;
        private int sourceFireVerticalResolution;
        private long sourceFireNextSerial;
        private int sourceFireWakeOrdinal;
        private bool sourceFireSelectionActive;
        public bool SourceFireEnabled { get { return sourceFireConfiguration != null; } }
        public SourceFireRenderFrame SourceFireFrame { get; private set; }
        public SourceFireBallEffect SourceBallEffectSelection { get; private set; }
        public ReadOnlyCollection<SourceFireProcessView> SourceFireProcesses
        { get { return SourceFireEnabled ? FireRuntime.Processes : null; } }
        public ReadOnlyCollection<SourceFireRecord> SourceFireRecords
        { get { return SourceFireEnabled ? FireRuntime.CurrentRecords : null; } }
        public ReadOnlyCollection<SourceFireDirectSound> SourceFireSoundSubmissions
        { get { return Array.AsReadOnly(sourceFireSounds.ToArray()); } }
        private ISourceFireSpfxRuntime FireRuntime { get { return sourceFireConfiguration.Runtime; } }
        private int FirePcount { get { return unchecked(sourceFireConfiguration.LogicalPcountAtTickZero + (int)Model.SourceTick); } }

        private SourceFireHostCallbacks SourceFireCallbacks(Action<SourceFireWakeSlot, int> continuation)
        {
            return new SourceFireHostCallbacks(request =>
            {
                SourceFireGuard.Require(request.Site.SourceTick == Model.SourceTick, "FX draw must execute in its real source tick.");
                return SourceDraw(request.Bound, request.Site.SourcePath + ":" + request.Site.Function + ":" + request.Site.SourceLine);
            }, () => (int)MovementPhase, ReadSourceFireCache,
            () => new SourceFireBallWorldSample(Model.SourceTick, Model.SourceBallPosition, "actual Core ball world state at FX wake"),
            actor => Model.Players[actor].SourceRawFlags,
            (actor, flags, site) => Model.Players[actor].SourceRawFlags = flags,
            site => BeginSourceProcess(site.Function, site.ActorId, 0),
            continuation, sound =>
            {
                sourceFireSounds.Add(sound);
                return new SourceFireSoundSubmission(false, "Original ordered FX sound metadata; not resubmitted as VM sound or DSP playback.");
            }, () => ++sourceFireNextSerial,
            () => new SourceFireBallRenderSample(Model.SourceTick, Model.SourceBallPosition,
                Model.SourceBallHeading, Model.SourceBallElevation, Model.SourceBallRoll,
                Model.SourceBallScale, unchecked((int)Model.SourceBallFlags),
                "actual Core ball_proc render fields read at trail_ball_proc entry; no live pose reconstruction"),
            actor =>
            {
                int facing = Model.Players[actor].Facing;
                SourceDirectionMath directions = Model.SourcePlaybook.Directions;
                return new SourceVector3(.35f * directions.Sin(facing) + 0f * directions.Cos(facing), .10f,
                    .35f * directions.Cos(facing) - 0f * directions.Sin(facing));
            });
        }

        private void AdmitSourceDust(ActorState actor, int argument)
        {
            if (!SourceFireEnabled) return;
            foreach (SourceFireCommand command in SourceFireCommand.DustCloud(
                FireSite("player.c", "dust_cloud", 5885, actor.Id), argument))
                FireRuntime.Apply(command);
        }

        private void InitializeSourceFire()
        {
            Model.SourceFireEffectsEnabled = SourceFireEnabled;
            if (!SourceFireEnabled) return;
            sourceFireNextSerial = 32; sourceFireWakeOrdinal = 0;
            sourceFireWhistleSerials.Clear(); sourceFireSounds.Clear(); sourceFireCamera = null; SourceFireFrame = null;
            sourceFireSelectionActive = false;
            SourceBallEffectSelection = SourceFireBallEffect.None;
            Array.Clear(sourceFireCaches, 0, sourceFireCaches.Length);
            FireRuntime.Reset(new SourceFireBootstrap(SourceFireOrdering.PortableDeclaredSlots,
                SourceFireClockOrigin.PortableLogicalEpoch, SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan,
                0, FirePcount, sourceFireConfiguration.ExecutionOrigin + ";explicit-logical-int32-epoch=" + FirePcount));
            FireRuntime.BeginSourceTick(FireRuntime.PortableHeadThenHostTick(0, FirePcount, new SourceFireWakeSlot[0]),
                SourceFireCallbacks((slot, ordinal) => { throw new SourceFireBoundaryException("Unexpected initialization continuation."); }));
            FireControl(SourceFireOperation.ResetBallProcessEffect, "player.c", "ball_proc", 9941);
        }

        private PlayerCommand DispatchSourceFireLevelZero(PlayerCommand command)
        {
            sourceFireSounds.Clear();
            var slots = new List<SourceFireWakeSlot>();
            var whistles = new Dictionary<long, SourceC2WhistleProcess>();
            foreach (SourceC2WhistleProcess process in Model.SourceC2Whistles)
            {
                long serial;
                if (!sourceFireWhistleSerials.TryGetValue(process.Id, out serial))
                { serial = ++sourceFireNextSerial; sourceFireWhistleSerials.Add(process.Id, serial); }
                whistles.Add(serial, process);
                slots.Add(new SourceFireWakeSlot(SourceFireSlotKind.HostContinuation, serial, "whistle_proc"));
            }
            slots.Add(new SourceFireWakeSlot(SourceFireSlotKind.HostContinuation, 1, "game_proc"));
            PlayerCommand applied = command;
            FireRuntime.BeginSourceTick(FireRuntime.PortableHeadThenHostTick(Model.SourceTick, FirePcount, slots),
                SourceFireCallbacks((slot, ordinal) =>
                {
                    sourceFireWakeOrdinal = ordinal;
                    if (slot.Function == "game_proc") applied = SourceGameProc(command);
                    else
                    {
                        SourceC2WhistleProcess process;
                        SourceFireGuard.Require(whistles.TryGetValue(slot.ProcessSerial, out process), "Unknown actual C2 continuation.");
                        DispatchSourceC2Whistle(process);
                    }
                }));
            return applied;
        }

        private SourceFireSite FireSite(string path, string function, int line, int actor = -1)
        {
            long serial = actor >= 0 ? 2 + actor : function == "ball_proc" ? 16 : 1;
            int level = actor >= 0 ? 0x300 + actor : function == "ball_proc" ? 0x600 : 0;
            int ordinal = 0;
            foreach (SourceFireRecord record in FireRuntime.CurrentRecords)
                if (record.Site.NativeProcessSerial == serial && record.Site.WakeOrdinal == sourceFireWakeOrdinal)
                    ordinal = Math.Max(ordinal, record.Site.WithinWakeCallOrdinal + 1);
            return new SourceFireSite(Model.SourceTick, FirePcount, serial, level, sourceFireWakeOrdinal,
                ordinal, actor, path, function, line);
        }

        private void FireControl(SourceFireOperation operation, string path, string function, int line, int id = 0, int mask = -1)
        {
            if (SourceFireEnabled) FireRuntime.Apply(SourceFireCommand.Control(FireSite(path, function, line), operation, id, mask));
        }

        private void FireLineupScope(int side)
        {
            if (!SourceFireEnabled) return;
            FireRuntime.Apply(SourceFireCommand.ReinitializePlayerFlash(FireSite("nfl.c", "lineup_players", 3271),
                SourceFireLineupScope.FromOriginalSide(side)));
        }

        private void SourceFireSelectionEntry()
        {
            if (!SourceFireEnabled || sourceFireSelectionActive || sourceConfiguration.Stations.Count == 0) return;
            sourceFireSelectionActive = true;
            FireControl(SourceFireOperation.KillProcessGroup, "playsel.c", "play_select", 852, SourceFireIds.PlayerFlash);
            FireControl(SourceFireOperation.SuspendMask, "playsel.c", "play_select", 854, 0, 0x8000);
            FireControl(SourceFireOperation.ResumeMask, "playsel.c", "play_select", 855, SourceFireIds.Spfx);
            FireControl(SourceFireOperation.HideAllObjects, "playsel.c", "play_select", 859);
        }

        private void SourceFireSelectionExit()
        {
            if (!SourceFireEnabled || !sourceFireSelectionActive) return;
            FireControl(SourceFireOperation.KillProcessGroup, "playsel.c", "clobber_playsel", 777, SourceFireIds.Spfx);
            FireControl(SourceFireOperation.DeleteObjectGroup, "playsel.c", "clobber_playsel", 782, SourceFireIds.PuffObject);
            FireControl(SourceFireOperation.ResumeAll, "playsel.c", "play_select", 1530);
            FireControl(SourceFireOperation.UnhideAllObjects, "playsel.c", "play_select", 1531);
            sourceFireSelectionActive = false;
        }

        private void RetainSourceFireCache(int actor, IReadOnlyList<SourceVector3> joints, SourceMovementCameraFrame camera)
        {
            if (!SourceFireEnabled) return;
            SourceFireGuard.Require(camera.HorizontalResolution == 512 && Model.SourceMovementCameraTick == Model.SourceTick,
                "FX requires actual current source512 raster and paired camera producer tick.");
            if (sourceFireCamera == null || sourceFireCamera.SourceTick != Model.SourceTick)
                sourceFireCamera = new SourceFireCameraFrame(Model.SourceTick, camera.Position, camera.Transform,
                    "actual executing Core cambot/cache pair; portable host camera,not native camera fidelity");
            SourceFireGuard.Require(camera.VerticalResolution == 256 || camera.VerticalResolution == 384,
                "FX requires the actual source DIP vertical raster,not a guessed display size.");
            sourceFireVerticalResolution = (int)camera.VerticalResolution;
            sourceFireCaches[actor] = new SourceFireCachedPlayerFrame(actor, Model.SourceTick, sourceFireCamera, joints,
                "actual post-player/pre-contact CameraJoints result retained without resampling");
        }

        private SourceFireCachedPlayerFrame ReadSourceFireCache(int actor)
        {
            SourceFireGuard.Actor(actor);
            SourceFireCachedPlayerFrame cache = sourceFireCaches[actor];
            SourceFireGuard.Require(cache != null, "No actual paired actor15/cambot cache has been produced; no FX bone fallback.");
            if (cache.CacheSourceTick >= Model.SourceTick - 1) return cache;
            SourceFireGuard.Require(Model.Phase == GamePhase.PlaySelect,
                "Older FX cache without actual Core play-selection suspension is unsupported.");
            return new SourceFireCachedPlayerFrame(actor, cache.CacheSourceTick, cache.Camera, cache.CameraJoints,
                cache.CacheProvenance, "Core PlaySelect suspends player/cache producers; retaining their actual immutable prior camera pair");
        }

        private void CaptureSourceFireFrame()
        {
            if (!SourceFireEnabled) return;
            SourceFireGuard.Require(sourceFireCamera != null, "No actual source camera/cache exists for an enabled FX frame.");
            SourceFireFrame = FireRuntime.CaptureFrame(new SourceFireRenderBasis(sourceFireCamera,
                sourceFireVerticalResolution, sourceFireConfiguration.AuthoredCull));
        }

        private void MaybeSourcePlayerFire(ActorState actor, string function, int line)
        {
            if (!SourceFireEnabled) return;
            FireRuntime.MaybePlayerFire(FireSite("player.c", function, line, actor.Id),
                new SourceFirePlayerEmitterInput(actor.Id, Model.SourceTeamFire[(int)actor.Team],
                    actor.SourceControllerStation, actor.SourceVelocity.X, actor.SourceVelocity.Z,
                    GameSnapshot.SourceActorFlags(actor), (actor.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0,
                    actor.Team == Model.Possession, actor.SourceMode == SourceActorMode.Quarterback));
        }
    }
}
