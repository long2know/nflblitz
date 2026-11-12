using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourcePossessionSoundData
    {
        public const string ContentSha256 = "ac9bdf7c37c8f3d9d1e7184ca96e2d9c28aad2e157940d05194513b8f06da5df";
        private readonly int[] playlists;
        public uint Volume { get; }
        public uint Pan { get; }
        public uint Priority { get; }
        private SourcePossessionSoundData(byte[] bytes)
        {
            playlists = new int[5];
            for (int i = 0; i < playlists.Length; i++) playlists[i] = (int)Read(bytes, 8 + i * 4);
            Volume = Read(bytes, 28); Pan = Read(bytes, 32); Priority = Read(bytes, 36);
        }
        public static SourcePossessionSoundData Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 40) throw new ArgumentException("Actual pinned CAMBOT speech data is required.");
            bytes = (byte[])bytes.Clone();
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            if (hash != ContentSha256) throw new ArgumentException("Original CAMBOT speech data pin changed; no table or bank fallback.");
            return new SourcePossessionSoundData(bytes);
        }
        private static uint Read(byte[] bytes, int at)
        { return (uint)(bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24); }
        public int Playlist(int ordinal)
        {
            if (ordinal < 0 || ordinal >= playlists.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            return playlists[ordinal];
        }
    }

    public sealed partial class GameSimulation : ISourceCameraProcessServices
    {
        internal const long GameProcess = 1, BallProcess = 16, CameraProcess = 17, ClockProcess = 18,
            PrePlayerProcess = 19, PostPlayerProcess = 20, PlayerInfoProcess = 21, CursorProcess = 22, StatusProcess = 23;
        private sealed class MessageProcess
        {
            internal long Serial, EntryTick = -1;
            internal int Number, Horizontal, Vertical;
            internal readonly List<MessageSprite> Sprites = new List<MessageSprite>();
        }
        private sealed class MessageSprite
        {
            internal long Serial, Flasher;
            internal string Image;
            internal int Id, FlashStage;
            internal bool White, Chroma;
        }
        private readonly List<MessageProcess> sourceMessages = new List<MessageProcess>();
        private readonly List<SourcePossessionSpeech> sourcePossessionSpeech = new List<SourcePossessionSpeech>();
        private SourcePossessionSoundData sourcePossessionSoundData;
        private bool sourceProcessSelectionActive;
        public ISourceCameraProcessServices SourceCameraProcesses { get { return this; } }
        public ReadOnlyCollection<SourcePossessionProcessRecord> SourcePossessionProcessRecords
        { get { return Model.SourceProcesses.Records; } }
        public ReadOnlyCollection<SourcePossessionSpeech> SourcePossessionSpeechSubmissions
        { get { return Array.AsReadOnly(sourcePossessionSpeech.ToArray()); } }
        public event Action<SourcePossessionSpeech> SourcePossessionSpeechRaised;
        public void BindSourcePossessionSoundData(SourcePossessionSoundData data)
        {
            if (!Model.SourceMode || Model.SourceTick != 0 || data == null)
                throw new InvalidOperationException("Bind the immutable original message speech data before the first source dispatch.");
            if (sourcePossessionSoundData != null && !ReferenceEquals(sourcePossessionSoundData, data))
                throw new InvalidOperationException("Original message speech data is already bound.");
            sourcePossessionSoundData = data;
        }
        private void ExecuteSourceInterceptionSpeech(long serial)
        {
            if (sourcePossessionSoundData == null || string.IsNullOrEmpty(Model.SourceAnnouncerSwappedBank))
            {
                sourceSoundVmFailed = true;
                throw new InvalidOperationException("intercepted_ball_sp requires its pinned table and the actual selected ancr_swaped_bnk_str.");
            }
            int ordinal = SourceDraw(5, "sounds.c:intercepted_ball_sp:831");
            SourceProcessSoundCall output = EmitSourceProcessSound(SourceProcessSoundKind.PossessionSpeech,
                "intercepted_ball_sp", Model.SourceAnnouncerSwappedBank, sourcePossessionSoundData.Playlist(ordinal),
                sourcePossessionSoundData.Volume, sourcePossessionSoundData.Pan, sourcePossessionSoundData.Priority, 1);
            var call = new SourcePossessionSpeech(Model.SourceTick, serial, Model.SourceRandomProcessOrder,
                Model.SourceAnnouncerSwappedBank, ordinal, sourcePossessionSoundData.Playlist(ordinal),
                sourcePossessionSoundData.Volume, sourcePossessionSoundData.Pan, sourcePossessionSoundData.Priority, output);
            sourcePossessionSpeech.Add(call);
            Model.SourceProcesses.Record("Speech", serial, call.PlaylistId, unchecked((int)call.PriorityWord));
            Action<SourcePossessionSpeech> consumer = SourcePossessionSpeechRaised;
            if (consumer != null)
            {
                try { consumer(call); }
                catch { sourceSoundVmFailed = true; throw; }
            }
        }

        private void InitializeSourceProcesses()
        {
            ResetSourceRandomSelections();
            Model.SourceProcesses = new SourcePortableProcessHost();
            Model.SourcePossessionMessages = Array.AsReadOnly(new SourcePossessionMessageView[0]);
            sourceFireNextSerial = 32; sourceMessages.Clear(); sourcePossessionSpeech.Clear();
            ResetSourceThrowMonitors();
            sourceProcessSelectionActive = false;
            sourceSelectionTimer = null; sourceSelectionFinishPending = false; sourceSelectionCpuPatPending = false;
            sourceSelectionSounds.Clear();
            CaptureSourceSelectionTimer();
            var host = Model.SourceProcesses;
            host.Create(GameProcess, 0x0010, "game_proc", 0);
            for (int id = 0; id < 14; id++) host.Create(2 + id, 0x0500, "player_proc", 0x300 + id);
            host.Create(BallProcess, 0x0900, "ball_proc", 0x600);
            host.Create(CameraProcess, 0x1400, "cambot_proc", 0x100);
            host.Create(ClockProcess, 0x0f00, "game_clock_proc", 0);
            host.Create(PrePlayerProcess, 0x0501, "pre_player_proc", 0x2ff);
            host.Create(PostPlayerProcess, 0x0502, "last_player_proc", 0x30e);
            host.Create(PlayerInfoProcess, 0x0f80, "show_player_names_proc", 0);
            host.Create(CursorProcess, 0x0f81, "cursor_proc", 0x400);
            host.Create(StatusProcess, 0x0700, "score_status_box", 0);
            host.Create(24, 0x0a00, "detect_proc", 0x500);
            sourceInstructionDispatcher = new SourceInstructionDispatcher(host,
                EmitSourceInstructionSound, EmitSourceKickoffInstructionSound);
            InitializeSourceInstructionAdmission();
        }

        private bool EnterSourceProcess(long serial) { return Model.SourceProcesses.Visit(serial); }
        private void CompleteSourceProcess()
        {
            long caller = Model.SourceProcesses.Caller;
            Model.SourceProcesses.Complete();
            long child;
            while ((child = Model.SourceProcesses.NextImmediateAfter(caller)) != 0)
                DispatchSourceMessage(new SourceFireWakeSlot(SourceFireSlotKind.HostContinuation, child, "flash_obj_proc"));
        }
        private bool SourceCameraHasRetainedOutput
        {
            get
            {
                SourceProcessView p = Model.SourceProcesses.Require(CameraProcess);
                return Model.SourceMovementCamera != null && (p.Suspended || Model.SourceCameraPossessionContinuation);
            }
        }
        private void RequireCameraProcessService()
        {
            if (!executingSourceCamera || Model.SourceProcesses.Caller != CameraProcess)
                throw new InvalidOperationException("Possession services belong inside the actual executing CAMBOT continuation.");
        }
        bool ISourceCameraProcessServices.Exists(int id, int mask)
        { RequireCameraProcessService(); return Model.SourceProcesses.Exists(id, mask); }
        void ISourceCameraProcessServices.SuspendMultiple(int id, int mask)
        {
            RequireCameraProcessService();
            Model.SourceProcesses.Record("SuspendMask", CameraProcess, id, mask);
            Model.SourceProcesses.SuspendMultiple(id, mask);
            FireControl(SourceFireOperation.SuspendMask, "cambot.c", "cambot_special", 2953, id, mask);
        }
        void ISourceCameraProcessServices.ResumeMultiple(int id, int mask)
        {
            RequireCameraProcessService();
            Model.SourceProcesses.Record("ResumeMask", CameraProcess, id, mask);
            Model.SourceProcesses.ResumeMultiple(id, mask);
            FireControl(SourceFireOperation.ResumeMask, "cambot.c", "cambot_special",
                id == 0x0f80 ? 2954 : 2955, id, mask);
        }
        void ISourceCameraProcessServices.ResumeAll()
        {
            RequireCameraProcessService();
            Model.SourceProcesses.Record("ResumeAllCall", CameraProcess, 0, 0);
            Model.SourceProcesses.ResumeAll();
            FireControl(SourceFireOperation.ResumeAll, "cambot.c", "cambot_special", 2901);
        }
        void ISourceCameraProcessServices.SleepCamera(int ticks)
        {
            RequireCameraProcessService();
            Model.SourceProcesses.Sleep(ticks);
            Model.SourceCameraPossessionContinuation = true;
        }
        void ISourceCameraProcessServices.CreateMessage(int number)
        {
            RequireCameraProcessService();
            if (number != 8 && number != 13 && number != 15)
                throw new NotSupportedException("Only the active CAMBOT possession messages are qualified.");
            long serial = ++sourceFireNextSerial;
            Model.SourceProcesses.Create(serial, 0x0f15, "show_message_proc", 0);
            sourceMessages.Add(new MessageProcess { Serial = serial, Number = number });
        }

        private List<SourceFireWakeSlot> SourceMessageSlots()
        {
            var result = new List<SourceFireWakeSlot>();
            foreach (SourceProcessView p in Model.SourceProcesses.Processes)
                if (IsSourceMessageFunction(p.Function))
                    result.Add(new SourceFireWakeSlot(SourceFireSlotKind.HostContinuation, p.Serial, p.Function));
            return result;
        }

        private void DispatchSourceMessage(SourceFireWakeSlot slot)
        {
            if (sourceInstructionDispatcher != null && sourceInstructionDispatcher.Dispatch(slot.ProcessSerial,
                function => BeginSourceProcess(function, -1, 0))) return;
            if (DispatchSourceOriginalSpeechChild(slot.ProcessSerial)) return;
            if (DispatchSourceAudibleChild(slot.ProcessSerial)) return;
            if (DispatchSourceThrowMonitor(slot.ProcessSerial)) return;
            if (!EnterSourceProcess(slot.ProcessSerial)) return;
            BeginSourceProcess(slot.Function, -1, 0);
            try
            {
                foreach (MessageProcess message in sourceMessages)
                {
                    if (message.Serial == slot.ProcessSerial)
                    {
                        if (message.EntryTick < 0)
                        {
                            if (Model.SourceMovementCamera == null)
                                throw new InvalidOperationException("Message entry needs the actual CAMBOT source raster.");
                            message.EntryTick = Model.SourceTick;
                            message.Horizontal = (int)Model.SourceMovementCamera.HorizontalResolution;
                            message.Vertical = (int)Model.SourceMovementCamera.VerticalResolution;
                            Model.SourceProcesses.Record("Preloaded", message.Serial, 21003, message.Number);
                            if (message.Number != 15)
                            {
                                AddSourceMessageSprite(message, message.Number == 8 ? "msg_int" : "msg_trn");
                                AddSourceMessageSprite(message, message.Number == 8 ? "msg_int_c1" : "msg_trn_c1");
                            }
                            if (message.Number == 8) ExecuteSourceInterceptionSpeech(message.Serial);
                            Model.SourceProcesses.Sleep(100);
                        }
                        else
                        {
                            if (message.Number != 15)
                            {
                                Model.SourceProcesses.Record("DeleteSprites", message.Serial,
                                    message.Number == 8 ? 0x5678face : 0x800, -1);
                                int objectId = message.Number == 8 ? 0x5678face : 0x800;
                                foreach (MessageProcess owner in sourceMessages)
                                    owner.Sprites.RemoveAll(sprite => sprite.Id == objectId);
                                Model.SourceProcesses.Record("DeleteTextures", message.Serial, 21003, -1);
                            }
                            Model.SourceProcesses.Kill(message.Serial);
                            sourceMessages.Remove(message);
                        }
                        return;
                    }
                    foreach (MessageSprite sprite in message.Sprites)
                    {
                        if (sprite.Flasher != slot.ProcessSerial) continue;
                        if (sprite.FlashStage == 8)
                        {
                            sprite.White = sprite.Chroma = false;
                            Model.SourceProcesses.Record("RestoreSprite", sprite.Serial, sprite.Id, 0);
                            Model.SourceProcesses.Kill(sprite.Flasher);
                        }
                        else
                        {
                            sprite.White = sprite.Chroma = (sprite.FlashStage++ & 1) == 0;
                            Model.SourceProcesses.Record("FlashSprite", sprite.Serial, sprite.Id, sprite.White ? 1 : 0);
                            Model.SourceProcesses.Sleep(4);
                        }
                        return;
                    }
                }
                // Release-mode flash_obj_white returns when sprite_exists fails.
                Model.SourceProcesses.Kill(slot.ProcessSerial);
            }
            finally { CompleteSourceProcess(); }
        }

        private void CleanSourceAudibleTeam(int team)
        {
            SourceAudibleState a = Model.SourceAudibleState;
            if (a == null) return;
            int oid = 813 + team;
            a.NonAlpha.RemoveAll(sprite => sprite.Id == oid);
            a.Alpha.RemoveAll(sprite => sprite.Id == oid);
            foreach (SourceAudibleChild child in a.Children)
                if ((int)child.Sprite.Team == team && Model.SourceProcesses.Contains(child.Serial))
                {
                    child.Stage = SourceAudibleFlashStage.Killed;
                    child.LastProcess = Model.SourceProcesses.Require(child.Serial);
                    Model.SourceProcesses.Kill(child.Serial);
                }
            a.Backgrounds[team] = null;
        }

        private void PrintSourceAudible(ActorState actor, bool first)
        {
            int team = (int)actor.Team;
            CleanSourceAudibleTeam(team);
            var sprite = new SourceAudibleSprite { Serial = ++sourceFireNextSerial, Team = actor.Team,
                Parent = Model.SourceProcesses.Caller, Id = 813 + team };
            SourceAudibleState a = Model.SourceAudibleState;
            a.Objects.Add(sprite); a.NonAlpha.Add(sprite); a.Backgrounds[team] = sprite;
            Model.SourceProcesses.Record("BeginAudibleSprite", sprite.Serial, sprite.Id, team);
            if (!first) return;
            var child = new SourceAudibleChild { Serial = ++sourceFireNextSerial, Sprite = sprite,
                Stage = SourceAudibleFlashStage.PendingEntry, BirthTick = Model.SourceTick };
            a.Children.Add(child);
            Model.SourceProcesses.CreateImmediateAfterCaller(child.Serial, 0x1500 + team, "flash_obj_proc");
        }

        private bool DispatchSourceAudibleChild(long serial)
        {
            SourceAudibleState a = Model.SourceAudibleState;
            if (a == null) return false;
            SourceAudibleChild child = a.Children.Find(c => c.Serial == serial);
            if (child == null) return false;
            if (!EnterSourceProcess(serial)) return true;
            BeginSourceProcess("flash_obj_proc", -1, 0);
            try
            {
                bool exists = a.NonAlpha.Contains(child.Sprite) || a.Alpha.Contains(child.Sprite);
                child.CheckTick = Model.SourceTick; child.CheckExists = exists;
                Model.SourceProcesses.Record("SpriteExists", serial, (int)child.Sprite.Serial, exists ? 1 : 0);
                if (!exists)
                {
                    child.Stage = SourceAudibleFlashStage.MissingSprite;
                    child.LastProcess = Model.SourceProcesses.Require(serial);
                    Model.SourceProcesses.Kill(serial);
                    return true;
                }
                if (child.Stage == SourceAudibleFlashStage.PendingEntry)
                {
                    child.EntryTick = Model.SourceTick;
                    child.Color = child.Sprite.Color; child.Combiner = child.Sprite.Combiner;
                }
                else if (child.Stage == SourceAudibleFlashStage.WhiteSleep)
                {
                    child.Sprite.Combiner = child.Combiner.Value; child.Sprite.Chroma = 0;
                    child.Stage = SourceAudibleFlashStage.ColorSleep;
                    Model.SourceProcesses.Sleep(2);
                    return true;
                }
                else
                {
                    child.Cycles++;
                    if (child.Cycles == 3)
                    {
                        child.Sprite.Color = child.Color.Value; child.Sprite.Combiner = child.Combiner.Value;
                        child.Sprite.Chroma = 0; child.Stage = SourceAudibleFlashStage.Completed;
                        child.LastProcess = Model.SourceProcesses.Require(serial);
                        Model.SourceProcesses.Kill(serial);
                        return true;
                    }
                }
                child.Sprite.Combiner = 0x10; child.Sprite.Chroma = 1;
                child.Stage = SourceAudibleFlashStage.WhiteSleep;
                Model.SourceProcesses.Sleep(2);
                return true;
            }
            finally { CompleteSourceProcess(); }
        }

        private void AddSourceMessageSprite(MessageProcess message, string image)
        {
            var sprite = new MessageSprite { Serial = ++sourceFireNextSerial, Image = image,
                Id = message.Number == 8 ? 0x5678face : 0x800 };
            message.Sprites.Add(sprite);
            Model.SourceProcesses.Record("BeginSprite", sprite.Serial, sprite.Id, 91);
            sprite.Flasher = ++sourceFireNextSerial;
            Model.SourceProcesses.Create(sprite.Flasher, 0x0f10, "flash_obj_proc", 0);
        }

        internal SourcePossessionMessageView CaptureSourcePossessionMessage()
        {
            var messages = new List<SourcePossessionMessageView>();
            foreach (MessageProcess message in sourceMessages)
            {
                if (message.EntryTick >= 0 && (Model.SourceProcesses.Contains(message.Serial) || message.Sprites.Count > 0))
                {
                    var sprites = new List<SourcePossessionSprite>();
                    foreach (MessageSprite sprite in message.Sprites)
                        sprites.Add(new SourcePossessionSprite(sprite.Serial, sprite.Image, sprite.Id, sprite.White, sprite.Chroma));
                    messages.Add(new SourcePossessionMessageView(message.Number, message.Serial, message.EntryTick,
                        message.Horizontal, message.Vertical, sprites));
                }
            }
            Model.SourcePossessionMessages = messages.AsReadOnly();
            return messages.Count == 0 ? null : messages[messages.Count - 1];
        }

        private void StepSourceClockProcess()
        {
            if (!EnterSourceProcess(ClockProcess)) return;
            BeginSourceProcess("game_clock_proc", -1, 0);
            SourceClockStep clock = SourceClock.Step(Model.SourceClock,
                new SourceClockInput(Model.SourceTick, Model.ClockActive, Model.Quarter - 1, sourceConfiguration.TimeAdjustment));
            Model.SourceTimeOfPossession = SourceTimeOfPossession.Accumulate(Model.SourceTimeOfPossession,
                Model.Possession, clock.TimeOfPossessionDelta);
            Model.SourceClock = clock.State; UpdateSourceClockView();
            if (clock.LowClockWarning) SourceOriginalClockWarningSound();
            if (clock.Operation == SourceClockOperation.ProcessExit) Model.SourceProcesses.Kill(ClockProcess);
            else if (clock.SleepTicks > 0) Model.SourceProcesses.Sleep(clock.SleepTicks);
            CompleteSourceProcess();
        }
    }
}
