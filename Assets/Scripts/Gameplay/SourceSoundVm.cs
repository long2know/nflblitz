using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;

namespace Blitz.Gameplay
{
    public enum SourceSoundVmStatus { Resolved, Unsupported, MissingInput }

    public sealed class SourceSoundVmDispatch
    {
        private readonly SourceCallbackCue originalCue;
        private readonly SourceAudioFunctionState? stateAtCall;
        public bool HasOriginalAnimationCue { get { return originalCue != null; } }
        public SourceCallbackCue OriginalCue
        {
            get
            {
                if (originalCue == null) throw new InvalidOperationException("Native process sound has no original animation cue.");
                return originalCue;
            }
        }
        public SourcePresentationContext OriginalCueContext { get { return OriginalCue.Context; } }
        public SourceC2SoundContext C2Context { get; }
        public SourceC2SoundState? C2StateAtCall { get; }
        public SourceRandomContext RandomContext { get; }
        public SourceAudioFunctionState StateAtCall
        {
            get
            {
                if (!stateAtCall.HasValue) throw new InvalidOperationException("Native process sound has no actor function state.");
                return stateAtCall.Value;
            }
        }
        public SourceSoundVmStatus Status { get; }
        public SourceAudioFunctionResult Result { get; }
        public string Detail { get; }
        public long OutputSequence { get; }

        internal SourceSoundVmDispatch(SourceCallbackCue cue, SourceRandomContext context,
            SourceAudioFunctionState state, SourceSoundVmStatus status, SourceAudioFunctionResult result, string detail,
            long outputSequence = 0)
        {
            if (outputSequence < 0) throw new ArgumentOutOfRangeException(nameof(outputSequence));
            originalCue = cue; RandomContext = context; stateAtCall = state;
            Status = status; Result = result; Detail = detail;
            OutputSequence = outputSequence;
        }

        internal SourceSoundVmDispatch(SourceC2SoundContext c2Context, SourceRandomContext context,
            SourceC2SoundState state, SourceAudioFunctionState? actorState, SourceSoundVmStatus status,
            SourceAudioFunctionResult result, string detail, long outputSequence = 0)
        {
            if (outputSequence < 0) throw new ArgumentOutOfRangeException(nameof(outputSequence));
            C2Context = c2Context; C2StateAtCall = state; RandomContext = context; stateAtCall = actorState;
            originalCue = c2Context.HasOriginalAnimationCue ? c2Context.OriginalAnimationCue : null;
            Status = status; Result = result; Detail = detail;
            OutputSequence = outputSequence;
        }
    }

    public sealed class SourceSoundVmBatch
    {
        public ReadOnlyCollection<SourceSoundVmDispatch> Dispatches { get; }
        internal SourceSoundVmBatch(IReadOnlyList<SourceSoundVmDispatch> dispatches)
        {
            var copy = new SourceSoundVmDispatch[dispatches.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = dispatches[i];
            Dispatches = Array.AsReadOnly(copy);
        }
    }

    public sealed partial class GameSimulation : ISourceCabinetSelectionSoundProducer, ISourceCabinetSoundBank
    {
        public const string SourceSoundVmDataSha256 =
            "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280";

        private SourceAudioFunctionData sourceSoundVmData;
        private SourceOriginalSoundBindings sourceOriginalSoundBindings;
        private sealed class SourceOriginalSpeechChild
        {
            internal long Serial;
            internal string Function;
            internal int Argument;
            internal int Stage;
        }
        private readonly List<SourceOriginalSpeechChild> sourceOriginalSpeechChildren = new List<SourceOriginalSpeechChild>();
        private readonly List<SourceSoundVmDispatch> sourceSoundVmPending = new List<SourceSoundVmDispatch>();
        private readonly List<SourceDirectSoundCall> sourceDirectSoundPending = new List<SourceDirectSoundCall>();
        private readonly List<SourceProcessSoundCall> sourceProcessSoundPending = new List<SourceProcessSoundCall>();
        private readonly List<SourceProcessSoundControl> sourceSoundControlPending = new List<SourceProcessSoundControl>();
        private readonly List<SourceBootSoundOutput> sourceBootSoundPending = new List<SourceBootSoundOutput>();
        private long sourceDirectSoundSequence;
        private long sourceOutputSequence;
        private bool sourceSelectionSoundCallback;

        private long NextSourceOutputSequence() { return checked(++sourceOutputSequence); }
        private bool sourceSoundVmFailed;

        public void BindSourceSoundVmData(SourceAudioFunctionData data)
        {
            if (Profile != SimulationProfile.OriginalRelease57Hz)
                throw new InvalidOperationException("Original sound-function data belongs to the explicit source profile.");
            if (Model.SourceTick != 0)
                throw new InvalidOperationException("Bind immutable source sound-function data before the first source wake.");
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.PayloadSha256 != SourceSoundVmDataSha256)
                throw new ArgumentException("The source VM requires the exact frozen original sound-function payload.", nameof(data));
            if (sourceSoundVmData != null && !ReferenceEquals(sourceSoundVmData, data))
                throw new InvalidOperationException("Original sound-function data is already bound.");
            sourceSoundVmData = data;
        }

        public SourceSoundVmBatch TakeSourceSoundVmDispatches()
        {
            var batch = new SourceSoundVmBatch(sourceSoundVmPending);
            sourceSoundVmPending.Clear();
            return batch;
        }

        public ReadOnlyCollection<SourceDirectSoundCall> TakeSourceDirectSoundCalls()
        {
            var calls = Array.AsReadOnly(sourceDirectSoundPending.ToArray());
            sourceDirectSoundPending.Clear();
            return calls;
        }

        public event Action<SourceProcessSoundCall> SourceProcessSoundRaised;

        public ReadOnlyCollection<SourceProcessSoundCall> TakeSourceProcessSoundCalls()
        {
            var calls = Array.AsReadOnly(sourceProcessSoundPending.ToArray());
            sourceProcessSoundPending.Clear();
            return calls;
        }

        public ReadOnlyCollection<SourceProcessSoundControl> TakeSourceSoundControls()
        {
            var controls = Array.AsReadOnly(sourceSoundControlPending.ToArray());
            sourceSoundControlPending.Clear();
            return controls;
        }

        public ReadOnlyCollection<SourceBootSoundOutput> TakeSourceBootSoundOutputs()
        {
            var outputs = Array.AsReadOnly(sourceBootSoundPending.ToArray());
            sourceBootSoundPending.Clear();
            return outputs;
        }

        public string SourceCursorSoundBank { get { return Model.SourceCurrentCursorBank; } }
        string ISourceCabinetSoundBank.SourceCursorSoundBank
        { get { return SourceCompleteOriginalEnabled ? Model.SourceCurrentCursorBank : null; } }

        private SourceProcessView RequireExecutingSourceSoundProcess()
        {
            SourcePortableProcessHost host = Model.SourceProcesses;
            if (!Model.SourceMode || sourceSoundVmFailed || host == null || host.Caller <= 0
                || Model.SourceTick <= 0 || host.SourceTick != Model.SourceTick)
                throw new InvalidOperationException("Sound output belongs to the actual executing source wake, never an observer or replay.");
            SourceProcessView process = host.Require(host.Caller);
            if (process.LastWakeTick != Model.SourceTick || process.Function != Model.SourceRandomProcess)
                throw new InvalidOperationException("Sound output requires this wake's actual process/call context.");
            return process;
        }

        private SourceProcessSoundCall EmitSourceProcessSound(SourceProcessSoundKind kind, string operand,
            string bank, int playlist, uint volume, uint pan, uint priority, int randomCalls = 0)
        {
            SourceProcessView process = RequireExecutingSourceSoundProcess();
            if (string.IsNullOrEmpty(bank))
            {
                sourceSoundVmFailed = true;
                throw new InvalidOperationException(operand + " requires its actual source-selected bank; no resolved fallback exists.");
            }
            var call = new SourceProcessSoundCall(kind, NextSourceOutputSequence(), Model.SourceTick,
                process.Serial, process.Function, Model.SourceRandomProcessOrder, Model.SourceDirectSoundSubcall++,
                operand, bank, playlist, volume, pan, priority, randomCalls);
            sourceProcessSoundPending.Add(call);
            try
            {
                Action<SourceProcessSoundCall> consumer = SourceProcessSoundRaised;
                if (consumer != null) consumer(call);
            }
            catch { sourceSoundVmFailed = true; throw; }
            return call;
        }

        private void EmitSourceSoundControl(SourceProcessSoundControlKind kind, string path, string function,
            int line, string bank = null, uint tracks = 0, uint value = 0)
        {
            SourceProcessView process = RequireExecutingSourceSoundProcess();
            sourceSoundControlPending.Add(new SourceProcessSoundControl(kind, NextSourceOutputSequence(),
                Model.SourceTick, process.Serial, process.Function, Model.SourceRandomProcessOrder,
                Model.SourceDirectSoundSubcall++, path, function, line, bank, tracks, value));
        }

        private void EmitSourceBootSound(string path, string function, int line, string bank = null,
            int? playlist = null, uint volume = 0, uint pan = 0, uint priority = 0,
            SourceProcessSoundControlKind? control = null, uint tracks = 0, uint value = 0)
        {
            if (!SourceCompleteOriginalEnabled
                || sourceC2Bootstrap.EntryKind != SourceC2EntryKind.CompleteOriginalVersusSoundBanks
                || Model.Tick != 0 || Model.SourceTick != 0)
                throw new InvalidOperationException("Boot sound belongs only to its named complete-original constructor entry.");
            sourceBootSoundPending.Add(new SourceBootSoundOutput(NextSourceOutputSequence(), sourceBootSoundPending.Count,
                path, function, line, bank, playlist, volume, pan, priority, control, tracks, value));
        }

        private string SourceOriginalTeamBank(TeamId side)
        {
            if (sourceC2Bootstrap.SourceTeamBankNames == null)
                throw new SourceC2MissingStateException("authenticated source team sound-bank names");
            int team = side == TeamId.Home ? sourceConfiguration.HomeTeam.TeamIndex : sourceConfiguration.AwayTeam.TeamIndex;
            return sourceC2Bootstrap.SourceTeamBankNames[team];
        }

        private string SourceOriginalEoqBank()
        {
            if (string.IsNullOrEmpty(Model.SourceEoqTuneBank))
                throw new SourceC2MissingStateException("actual selected end-of-quarter tune bank");
            return Model.SourceEoqTuneBank;
        }

        private void InitializeSourceOriginalVersusSound()
        {
            BeginSourceProcess("vs_screen", -1, 0);
            const string path = "lib/select/vsscreen.c", function = "vs_screen";
            EmitSourceBootSound(path, function, 1140, control: SourceProcessSoundControlKind.StopAll);
            EmitSourceBootSound(path, function, 1141, control: SourceProcessSoundControlKind.ReservedTracks, tracks: 35);
            EmitSourceBootSound(path, function, 1144, "generic", 16, 235, 127, 0x8000);
            EmitSourceBootSound(path, function, 1149, "intro", control: SourceProcessSoundControlKind.DeleteBank);
            EmitSourceBootSound(path, function, 1150, Model.SourceCurrentCursorBank, control: SourceProcessSoundControlKind.DeleteBank);
            Model.SourceCurrentCursorBank = "cursors2";
            EmitSourceBootSound(path, function, 1154, Model.SourceCurrentCursorBank, control: SourceProcessSoundControlKind.LoadBank);
            string[] tunes = { "main1", "main3", "main2", "main4", "otime" };
            Model.SourceGameTuneBank = tunes[SourceDraw(5, "lib/select/vsscreen.c:vs_screen:1159")];
            EmitSourceBootSound(path, function, 1160, Model.SourceGameTuneBank, control: SourceProcessSoundControlKind.LoadBank);
            EmitSourceBootSound(path, function, 1169, SourceOriginalTeamBank(TeamId.Home), control: SourceProcessSoundControlKind.LoadBank);
            if (sourceConfiguration.HomeTeam.TeamIndex != sourceConfiguration.AwayTeam.TeamIndex)
                EmitSourceBootSound(path, function, 1175, SourceOriginalTeamBank(TeamId.Away), control: SourceProcessSoundControlKind.LoadBank);
            EmitSourceBootSound(path, function, 1180, control: SourceProcessSoundControlKind.ReservedTracks, tracks: 35);
            BeginSourceProcess("InitGameProc", -1, 0);
        }

        private void InitializeSourceOriginalGameSound()
        {
            EmitSourceBootSound("nfl.c", "InitGameProc", 568, control: SourceProcessSoundControlKind.ReservedTracks, tracks: 35);
            EmitSourceBootSound("nfl.c", "InitGameProc", 598, control: SourceProcessSoundControlKind.StopTracks, tracks: 1);
            EmitSourceBootSound("nfl.c", "InitGameProc", 599, control: SourceProcessSoundControlKind.ReservedTracks, tracks: 35);
            EmitSourceBootSound("nfl.c", "InitGameProc", 601, Model.SourceGameTuneBank, 0, 255, 127, 0x8000);
            EmitSourceBootSound("nfl.c", "InitGameProc", 602, "generic", 1, 115, 127, 0x8080);
        }

        private void CompleteSourceOriginalQuarterSound()
        {
            EmitSourceSoundControl(SourceProcessSoundControlKind.LoadBank, "nfl.c", "start_new_quarter", 2666, Model.SourceTauntBank);
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopAll, "nfl.c", "start_new_quarter", 2677);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "nfl.c", "start_new_quarter", 2680, tracks: 35);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "nfl.c:start_new_quarter:2684",
                Model.SourceGameTuneBank, 0, 255, 127, 0x8000);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "nfl.c:start_new_quarter:2685",
                "generic", 1, 115, 127, 0x8080);
        }

        private void RestartSourceOriginalKickoffMusic(SourceKickoffKind kind)
        {
            if (!SourceCompleteOriginalEnabled || kind == SourceKickoffKind.Opening) return;
            int line = kind == SourceKickoffKind.Onside ? 983 : 1004;
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "nfl.c", "game_proc", line, tracks: 1);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "nfl.c", "game_proc", line + 1, tracks: 35);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "nfl.c:game_proc:" + (line + 2),
                Model.SourceGameTuneBank, 0, 255, 127, 0x8000);
        }

        private void BeginSourceOriginalStatsSound(SourceCabinetFlowStage stage)
        {
            if (!SourceCompleteOriginalEnabled) return;
            if (stage == SourceCabinetFlowStage.CoachingTip)
            {
                EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "lib/select/select.c", "coaching_tip_scrn", 5721, tracks: 2);
                EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "lib/select/select.c", "coaching_tip_scrn", 5722, tracks: 35);
                EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "select.c:coaching_tip_scrn:5724",
                    Model.SourceCurrentPlayerBank, 148, 115, 127, 1);
                return;
            }
            bool final = stage == SourceCabinetFlowStage.FinalStats;
            if (!final && stage != SourceCabinetFlowStage.HalftimeStats) return;
            if (final && sourceCabinetFlow.RecordSaveRequired)
                EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "nfl.c:game_over:2750",
                    Model.SourceCurrentPlayerBank, 148, 160, 127, 0);
            LoadSourceOriginalEoqSound(final);
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "lib/select/select.c", "team_stats_page",
                final ? 1323 : 1313, tracks: 1);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "lib/select/select.c", "team_stats_page",
                final ? 1324 : 1314, tracks: 35);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay,
                final ? "select.c:team_stats_page:1326" : "select.c:team_stats_page:1316",
                final ? "gameover" : SourceOriginalEoqBank(), 0, 255, 127, 0x8080);
        }

        private void LoadSourceOriginalEoqSound(bool final)
        {
            string function = final ? "load_gameover_tunes" : "load_tunes";
            EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "lib/select/select.c", function,
                final ? 7317 : 7280, Model.SourceTauntBank);
            EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "lib/select/select.c", function,
                final ? 7318 : 7281, Model.SourceAnnouncerSwappedBank);
            if (!final)
            {
                int quarter = sourceCabinetFlow.CompletedQuarter.Value;
                string[] tunes = { "buyin", "halftime", "buyin", "buyin", "buyin", "buyin", "buyin" };
                Model.SourceEoqTuneBank = tunes[quarter];
            }
            EmitSourceSoundControl(SourceProcessSoundControlKind.LoadBank, "lib/select/select.c", function,
                final ? 7320 : 7284, final ? "gameover" : Model.SourceEoqTuneBank);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "lib/select/select.c", function,
                final ? 7324 : 7288, tracks: 35);
        }

        private void EndSourceOriginalStatsSound(SourceCabinetFlowStage stage)
        {
            if (!SourceCompleteOriginalEnabled) return;
            if (stage == SourceCabinetFlowStage.HalftimeStats)
            {
                EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "lib/select/select.c", "team_stats_page", 1712, tracks: 2);
                EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "lib/select/select.c", "team_stats_page", 1713, tracks: 35);
                EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "select.c:team_stats_page:1716",
                    "generic", 16, 255, 127, 0x8000);
            }
            else if (stage == SourceCabinetFlowStage.FinalStats)
            {
                EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "nfl.c", "game_over", 2761, tracks: 1);
                EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "nfl.c", "game_over", 2762, tracks: 2);
                EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "nfl.c", "game_over", 2763, tracks: 35);
                EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "nfl.c", "game_over", 2767, SourceOriginalEoqBank());
                EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "nfl.c", "game_over", 2768, SourceOriginalTeamBank(TeamId.Away));
                EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "nfl.c", "game_over", 2769, SourceOriginalTeamBank(TeamId.Home));
                EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "nfl.c", "game_over", 2770, Model.SourceGameTuneBank);
                EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "nfl.c", "game_over", 2771, Model.SourceCurrentCursorBank);
                EmitSourceSoundControl(SourceProcessSoundControlKind.DeleteBank, "nfl.c", "game_over", 2772, "gameover");
                Model.SourceCurrentCursorBank = "cursors";
                EmitSourceSoundControl(SourceProcessSoundControlKind.LoadBank, "nfl.c", "game_over", 2775, Model.SourceCurrentCursorBank);
                EmitSourceSoundControl(SourceProcessSoundControlKind.LoadBank, "nfl.c", "game_over", 2778, "intro");
                EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "nfl.c", "game_over", 2783, tracks: 35);
            }
        }

        private void BeginSourceOriginalOfferSound()
        {
            if (!SourceCompleteOriginalEnabled || sourceCabinetFlow.CompletedQuarter == 1) return;
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "lib/select/select.c", "buyin_screen", 5213, tracks: 1);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "lib/select/select.c", "buyin_screen", 5215, tracks: 35);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "select.c:buyin_screen:5217",
                SourceOriginalEoqBank(), 0, 255, 127, 0x8080);
        }

        SourceProcessSoundCall ISourceCabinetSelectionSoundProducer.SubmitSourceCabinetSelectionSound(
            long sourceTick, int stationId, TeamId side, SourceCabinetSelectionSound sound)
        {
            SourceProcessView process = RequireExecutingSourceSoundProcess();
            if (!sourceSelectionSoundCallback || process.Serial != GameProcess || sourceTick != Model.SourceTick
                || Model.Phase != GamePhase.PlaySelect || (int)side < 0 || (int)side > 1
                || Model.SourcePlaySelectionReady[(int)side])
                throw new InvalidOperationException("Cabinet sound requires the current actual game_proc selection callback.");
            int first = -1, second = -1;
            foreach (SourceStationConfiguration station in sourceConfiguration.Stations)
                if (station.Team == side)
                { if (first < 0) first = station.StationId; else second = station.StationId; }
            int captain = second < 0 || Model.SourceCaptain == SourceCaptainSlot.First ? first : second;
            if (stationId != captain || captain < 0)
                throw new InvalidOperationException("Cabinet sound belongs to the actual controlling captain.");
            SourceRandomSelectionView random = Model.SourceRandomSelections[(int)side];
            if (sound != SourceCabinetSelectionSound.RandomArmed
                && (!random.IsSpecified || !random.ControllerAwake || random.SourceTick != sourceTick))
                throw new InvalidOperationException("Random cabinet sound requires this side's current source controller wake.");
            int playlist;
            uint priority = 10;
            switch (sound)
            {
                case SourceCabinetSelectionSound.Moved:
                case SourceCabinetSelectionSound.Selected: playlist = side == TeamId.Home ? 63 : 64; break;
                case SourceCabinetSelectionSound.Edge: playlist = side == TeamId.Home ? 67 : 68; priority = 15; break;
                case SourceCabinetSelectionSound.PageChanged: playlist = 23; priority = 5; break;
                case SourceCabinetSelectionSound.Flipped: playlist = 19; break;
                case SourceCabinetSelectionSound.Hidden:
                case SourceCabinetSelectionSound.RandomArmed: playlist = 60; break;
                default: throw new ArgumentOutOfRangeException(nameof(sound));
            }
            return EmitSourceProcessSound(SourceProcessSoundKind.CabinetSelection, sound.ToString(),
                Model.SourceCurrentCursorBank, playlist, 235, 127, priority);
        }

        private void SubmitSourceAudibleSound(ActorState actor, int playlist, int line)
        {
            long serial = Model.SourceProcesses.Caller;
            if (serial == 0) throw new InvalidOperationException("Direct audible output requires the actual executing caller.");
            var call = new SourceDirectSoundCall(++sourceDirectSoundSequence, Model.SourceTick, serial,
                Model.SourceProcesses.Require(serial).Function, Model.SourceRandomProcessOrder,
                actor.Id, actor.SourceControllerStation, actor.Team, "player.c", "audible_check", line,
                Model.SourceCurrentCursorBank, playlist, 235, actor.Team == TeamId.Home ? 0u : 255u, 15, Model.SourceDirectSoundSubcall++,
                NextSourceOutputSequence());
            sourceDirectSoundPending.Add(call);
            Model.SourceAudibleState.Calls.Add(call);
        }

        private void InitializeSourceSoundState()
        {
            sourceProcessSoundPending.Clear();
            sourceSoundControlPending.Clear();
            sourceBootSoundPending.Clear();
            sourceOriginalSpeechChildren.Clear();
            sourceOriginalSoundBindings = SourceCompleteOriginalEnabled
                ? SourceOriginalSoundBindings.ForCompleteOriginal303(sourceAnimations) : null;
            sourceSelectionSoundCallback = false;
            // Game.c:83 (a005a9b4...), and nfl.c:677's new-game player_blocks memset.
            Model.SourceCurrentPlayerBank = "players";
            Model.SourceCurrentCursorBank = sourceC2Bootstrap != null && sourceC2Bootstrap.VersusBankState != null
                ? sourceC2Bootstrap.VersusBankState.CurrentCursorBank : "cursors";
            Model.SourceEoqTuneBank = null;
            foreach (ActorState actor in Model.Players) actor.SourceHitPartnerId = -1;
        }

        private void RequireSourceSoundVmBinding()
        {
            if (sourceSoundVmFailed)
                throw new InvalidOperationException("A source sound function failed after actual execution; restart is required, never retry or rewind it.");
            if (sourceSoundVmData == null)
                throw new InvalidOperationException("Bind the exact original SourceAudioFunctionData before the first source wake; no muted or post-host fallback exists.");
        }

        private SourceRandomContext SourceAnimationRandomContext(ActorState actor)
        {
            if (Model.SourceRandomActor != actor.Id)
            {
                int offset = Model.SourceRandomCallIndex;
                string process = Model.SourceRandomProcess;
                BeginSourceProcess(process, actor.Id, Model.SourceRandomProcessOrder);
                Model.SourceRandomCallIndex = offset;
            }
            return CurrentSourceRandomContext();
        }

        private SourceAudioFunctionState SourceSoundState(ActorState actor)
        {
            SourceAudioHitPartner hit = default(SourceAudioHitPartner);
            if (actor.SourceHitPartnerId.HasValue)
            {
                int id = actor.SourceHitPartnerId.Value;
                if (id == -1) hit = SourceAudioHitPartner.Absent;
                else if (id >= 0 && id < Model.Players.Length)
                {
                    ActorState partner = Model.Players[id];
                    hit = SourceAudioHitPartner.Present((int)partner.SourceMode, partner.SourceControllerStation);
                }
                else throw new InvalidOperationException("Invalid actual source pobj_hit actor identity.");
            }
            return new SourceAudioFunctionState(GameSnapshot.SourceActorFlags(actor), (int)actor.SourceMode,
                actor.Id, actor.SourceControllerStation, hit, Model.CarrierId, Model.TargetPlayerId,
                Model.SourceCurrentPlayerBank);
        }

        private void ExecuteSourceSoundVm(ActorState actor, SourceCallbackCue cue)
        {
            RequireSourceSoundVmBinding();
            bool preSnapSwitchSound = SourceDefensivePreSnapSwitchEnabled
                && cue.Context.SourcePath == "plyrseq.c" && cue.Context.ExecutingSymbol == "m_hopclp_anim";
            if (preSnapSwitchSound && (cue.Kind != SourcePresentationCueKind.SoundCallback
                || cue.Context.SourceLine != 3409 || cue.FunctionSymbol != "attack_snd"
                || cue.Argument0 != 127 || cue.Argument1 != 0 || cue.Argument2 != 0))
                throw new NotSupportedException("The enabled original hop supports only its exact attack_snd(127,0,0) cue.");
            if (SourceC2Enabled)
            {
                foreach (SourceC2VmBinding candidate in sourceC2Bootstrap.Data.Bindings)
                {
                    if (!candidate.Matches(cue)) continue;
                    if (preSnapSwitchSound && candidate.FunctionKind != SourceC2SoundFunctionKind.AttackSoundFixed)
                        throw new NotSupportedException("The enabled original hop requires its exact fixed attack-sound binding.");
                    // Immediate hop entry belongs to the caller's existing ordered process, not a new destination wake.
                    SourceRandomContext execution = preSnapSwitchSound
                        ? new SourceRandomContext(Model.SourceTick, actor.Id, Model.SourceRandomProcess,
                            Model.SourceRandomProcessOrder, sourceRandom.Provenance.Profile, Model.SourceRandomCallIndex)
                        : SourceAnimationRandomContext(actor);
                    var c2Context = new SourceC2SoundContext(candidate.FunctionKind, Model.SourceTick, execution.Process,
                        execution.ProcessOrder, actor.Id, cue.Context.SourcePath, cue.FunctionSymbol,
                        cue.Context.SourceLine, cue.Argument0, cue.Argument1, cue.Argument2, cue,
                        firstCallIndex: execution.FirstCallIndex);
                    SourceAudioFunctionState actorState = SourceSoundState(actor);
                    ExecuteSourceC2Sound(c2Context, C2SoundState(actorState), actorState, execution);
                    return;
                }
            }
            if (preSnapSwitchSound)
            {
                sourceSoundVmFailed = true;
                throw new NotSupportedException("The enabled original hop has no authenticated fixed attack-sound binding; "
                    + "an Unsupported or legacy ground selector is not a successful fallback.");
            }
            SourceAudioVmBinding supplemental;
            if (sourceOriginalSoundBindings != null && sourceOriginalSoundBindings.TryGet(cue, out supplemental))
            {
                ExecuteSourceOriginalSoundVm(actor, cue, supplemental);
                return;
            }
            SourceAudioVmBinding binding = null;
            foreach (SourceAudioVmBinding candidate in sourceSoundVmData.VmBindings)
                if (candidate.SourcePath == cue.Context.SourcePath && candidate.ScriptSymbol == cue.Context.ExecutingSymbol
                    && candidate.SourceLine == cue.Context.SourceLine && candidate.FunctionSymbol == cue.FunctionSymbol
                    && candidate.Argument0 == cue.Argument0 && candidate.Argument1 == cue.Argument1
                    && candidate.Argument2 == cue.Argument2) { binding = candidate; break; }
            if (binding == null)
            {
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, CurrentSourceRandomContext(),
                    SourceSoundState(actor), SourceSoundVmStatus.Unsupported, null,
                    "This original function/source tuple has no implemented sound/SPFX selector; the original cue is retained.",
                    NextSourceOutputSequence()));
                return;
            }
            SourceRandomContext context = SourceAnimationRandomContext(actor);
            SourceAudioFunctionState state = SourceSoundState(actor);
            try
            {
                SourceAudioFunctionResult result = SourceAudioFunctions.Select(sourceSoundVmData, cue, state,
                    sourceRandom, context);
                AdvanceSourceSoundRandom(context, result);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, context, state,
                    SourceSoundVmStatus.Resolved, result, null, NextSourceOutputSequence()));
            }
            catch (SourceAudioMissingInputException error)
            {
                AdvanceSourceSoundRandom(context, error.PartialResult);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, context, state,
                    SourceSoundVmStatus.MissingInput, error.PartialResult, error.Field, NextSourceOutputSequence()));
                sourceSoundVmFailed = true;
                throw;
            }
        }

        private void AdvanceSourceSoundRandom(SourceRandomContext context, SourceAudioFunctionResult result)
        {
            Model.SourceRandomCallIndex = checked(context.FirstCallIndex + result.RandomCalls);
            Model.SourceRandomDraws += result.RandomCalls;
        }

        private static SourceAudioFunctionResult EmptySourceSoundResult()
        {
            return new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall>(), new List<SourceRandomDraw>());
        }

        private SourceC2SoundContext SourceOriginalCueContext(SourceCallbackCue cue, SourceRandomContext execution,
            SourceC2SoundFunctionKind kind)
        {
            return new SourceC2SoundContext(kind, Model.SourceTick, execution.Process, execution.ProcessOrder,
                cue.Context.ActorPlayerId, cue.Context.SourcePath, cue.FunctionSymbol, cue.Context.SourceLine,
                cue.Argument0, cue.Argument1, cue.Argument2, cue, firstCallIndex: execution.FirstCallIndex);
        }

        private void ExecuteSourceOriginalSoundVm(ActorState actor, SourceCallbackCue cue, SourceAudioVmBinding binding)
        {
            sourceOriginalSoundBindings.Require(cue);
            SourceRandomContext execution = SourceAnimationRandomContext(actor);
            SourceAudioFunctionState state = SourceSoundState(actor);
            try
            {
                SourceAudioFunctionResult result;
                switch (binding.FunctionSymbol)
                {
                    case "get_hit_snd":
                    case "grnd_hit_snd":
                        result = SourceAudioFunctions.SelectCompleteOriginal(sourceSoundVmData,
                            sourceOriginalSoundBindings, cue, state, sourceRandom, execution);
                        break;
                    case "attack_snd":
                        result = SourceOriginalAudioFunctions.SelectAttack(sourceOriginalSoundBindings,
                            cue, state, sourceRandom, execution);
                        break;
                    case "taunt_speech":
                        result = SourceOriginalAudioFunctions.SelectTaunt(sourceOriginalSoundBindings,
                            sourceC2Bootstrap.Data, SourceOriginalCueContext(cue, execution, SourceC2SoundFunctionKind.TauntSpeech),
                            C2SoundState(state), sourceRandom, execution);
                        break;
                    case "tackle_spch":
                        if (Model.CarrierId == actor.Id)
                        {
                            string function = actor.SourceMode == SourceActorMode.Quarterback ? "sacked_sp_proc"
                                : cue.Argument1 > 0 ? "tackled_sp_proc" : "tackled2_sp_proc";
                            var child = new SourceOriginalSpeechChild { Serial = ++sourceFireNextSerial,
                                Function = function, Argument = cue.Argument1 > 0 ? cue.Argument1 : cue.Argument0 };
                            Model.SourceProcesses.Create(child.Serial, 0, function, 0);
                            sourceOriginalSpeechChildren.Insert(0, child);
                        }
                        result = EmptySourceSoundResult();
                        break;
                    case "plyr_open_sp":
                        int id;
                        for (id = 0; id < Model.Players.Length; id++)
                            if (id != actor.Id && Model.Players[id].Team != actor.Team
                                && Model.SourceDistances[actor.Id, id] / SourceTiming.SourceUnitsPerYard < 4f) break;
                        // sounds.c:560 deliberately includes the final actor's index.
                        result = id >= Model.Players.Length - 1
                            ? SourceOriginalAudioFunctions.SelectSpeechTable(sourceOriginalSoundBindings,
                                SourceOriginalCueContext(cue, execution, SourceC2SoundFunctionKind.OriginalSpeech),
                                Model.SourceCurrentPlayerBank, sourceRandom, execution)
                            : EmptySourceSoundResult();
                        break;
                    default: throw new NotSupportedException("Unimplemented closed original sound function: " + binding.FunctionSymbol);
                }
                AdvanceSourceSoundRandom(execution, result);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, execution, state,
                    SourceSoundVmStatus.Resolved, result, null, NextSourceOutputSequence()));
            }
            catch (SourceAudioMissingInputException error)
            {
                AdvanceSourceSoundRandom(execution, error.PartialResult);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, execution, state,
                    SourceSoundVmStatus.MissingInput, error.PartialResult, error.Field, NextSourceOutputSequence()));
                sourceSoundVmFailed = true;
                throw;
            }
        }

        private bool DispatchSourceOriginalSpeechChild(long serial)
        {
            SourceOriginalSpeechChild child = sourceOriginalSpeechChildren.Find(item => item.Serial == serial);
            if (child == null) return false;
            if (!EnterSourceProcess(serial)) return true;
            BeginSourceProcess(child.Function, -1, 0);
            try
            {
                if (child.Stage == 0)
                {
                    if (child.Function == "tackled2_sp_proc" && Model.SourceProcesses.Exists(0x0ccc, -1))
                    { Model.SourceProcesses.Sleep(1); return true; }
                    child.Stage = 1;
                    Model.SourceProcesses.Sleep(child.Function == "sacked_sp_proc" ? 5
                        : child.Function == "tackled_sp_proc" ? 7 : 10);
                    return true;
                }
                if (child.Function == "tackled_sp_proc" && Model.SourceProcesses.Exists(0x0ccc, -1))
                { Model.SourceProcesses.Sleep(1); return true; }
                if (child.Function == "sacked_sp_proc" || MovementPhase == SourceMovementPhase.PlayOver)
                {
                    if (child.Function == "tackled_sp_proc")
                        EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "sounds.c:tackled_sp_proc:1434",
                            "tkperm", child.Argument, 255, 127, 0xe285);
                    else if (child.Function == "sacked_sp_proc" || child.Argument == 0 || child.Argument == 1)
                    {
                        int line = child.Function == "sacked_sp_proc" ? 1407 : child.Argument == 0 ? 1461 : 1463;
                        SourceRandomContext execution = CurrentSourceRandomContext();
                        var context = new SourceC2SoundContext(SourceC2SoundFunctionKind.OriginalSpeech,
                            Model.SourceTick, child.Function, execution.ProcessOrder, -1, "sounds.c", child.Function, line,
                            child.Argument, invocationId: serial, firstCallIndex: execution.FirstCallIndex);
                        SourceAudioFunctionResult result = SourceOriginalAudioFunctions.SelectSpeechTable(
                            sourceOriginalSoundBindings, context, Model.SourceAnnouncerSwappedBank, sourceRandom, execution);
                        AdvanceSourceSoundRandom(execution, result);
                        sourceSoundVmPending.Add(new SourceSoundVmDispatch(context, execution,
                            new SourceC2SoundState(Model.SourceTauntBank, Model.SourceCurrentPlayerBank), null,
                            SourceSoundVmStatus.Resolved, result, null, NextSourceOutputSequence()));
                    }
                }
                Model.SourceProcesses.Kill(serial);
                sourceOriginalSpeechChildren.Remove(child);
                return true;
            }
            catch { sourceSoundVmFailed = true; throw; }
            finally { CompleteSourceProcess(); }
        }

        private SourceC2SoundState C2SoundState(SourceAudioFunctionState actorState)
        {
            SourceAudioHitPartner hit = actorState.HitPartner;
            bool? present = hit.Presence == SourceAudioHitPresence.Unknown ? (bool?)null
                : hit.Presence == SourceAudioHitPresence.Present;
            return new SourceC2SoundState(Model.SourceTauntBank, Model.SourceCurrentPlayerBank,
                actorState.ActorFlags, actorState.ActorRawPlayerMode, actorState.ActorPlayerNumber,
                actorState.ActorStation, present, hit.RawPlayerMode, hit.Station,
                actorState.CurrentBallCarrier, actorState.CurrentIntendedReceiver);
        }

        private void ExecuteSourceC2NativeSound(SourceC2SoundContext context)
        {
            RequireSourceSoundVmBinding();
            ExecuteSourceC2Sound(context, new SourceC2SoundState(Model.SourceTauntBank, Model.SourceCurrentPlayerBank),
                null, CurrentSourceRandomContext());
        }

        private void ExecuteSourceC2Sound(SourceC2SoundContext context, SourceC2SoundState state,
            SourceAudioFunctionState? actorState, SourceRandomContext execution)
        {
            try
            {
                SourceAudioFunctionResult result = SourceC2SoundFunctions.Select(sourceC2Bootstrap.Data,
                    context.Kind, context, state, sourceRandom, execution);
                if (context.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                    (result == null || result.RandomCalls != 0 || result.Calls.Count != 1 ||
                        result.Calls[0] == null || result.Calls[0].SubcallOrder != 0 ||
                        result.Calls[0].RandomCalls != 0 || !ReferenceEquals(result.Calls[0].C2Context, context) ||
                        !result.Calls[0].HasOriginalAnimationCue ||
                        !ReferenceEquals(result.Calls[0].OriginalCue, context.OriginalAnimationCue)))
                {
                    if (result != null) AdvanceSourceSoundRandom(execution, result);
                    sourceSoundVmFailed = true;
                    throw new InvalidOperationException("Fixed hop sound requires one real cue-paired original call and zero random draws.");
                }
                AdvanceSourceSoundRandom(execution, result);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(context, execution, state, actorState,
                    SourceSoundVmStatus.Resolved, result, null, NextSourceOutputSequence()));
            }
            catch (SourceAudioMissingInputException error)
            {
                AdvanceSourceSoundRandom(execution, error.PartialResult);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(context, execution, state, actorState,
                    SourceSoundVmStatus.MissingInput, error.PartialResult, error.Field, NextSourceOutputSequence()));
                sourceSoundVmFailed = true;
                throw;
            }
        }
    }
}
