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
            internal SourceContactSoundCue ContactCue;
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
        private bool sourceOriginalSaidLateHit;
        private int sourceOriginalLateHitSpeechIndex;
        private bool? sourceOriginalBadLanguageAdjustment;
        public bool SourceOriginalBadLanguageEnabled { get; private set; }

        public void BindSourceOriginalBadLanguageAdjustment(bool enabled)
        {
            if (!SourceCompleteProgramsEnabled || Model.SourceTick != 0)
                throw new InvalidOperationException("Bind the actual original bad-language adjustment before the first complete-original wake.");
            SourceOriginalBadLanguageEnabled = enabled;
            sourceOriginalBadLanguageAdjustment = enabled;
        }

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
        { get { return SourceCompleteProgramsEnabled ? Model.SourceCurrentCursorBank : null; } }

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
            string bank, int playlist, uint volume, uint pan, uint priority, int randomCalls = 0,
            SourceContactSoundCue contactCue = null, string calleePath = null, string calleeBlob = null,
            string calleeFunction = null, int calleeLine = 0)
        {
            SourceProcessView process = RequireExecutingSourceSoundProcess();
            if (string.IsNullOrEmpty(bank))
            {
                sourceSoundVmFailed = true;
                throw new InvalidOperationException(operand + " requires its actual source-selected bank; no resolved fallback exists.");
            }
            var call = new SourceProcessSoundCall(kind, NextSourceOutputSequence(), Model.SourceTick,
                process.Serial, process.Function, Model.SourceRandomProcessOrder, Model.SourceDirectSoundSubcall++,
                operand, bank, playlist, volume, pan, priority, randomCalls,
                contactCue, calleePath, calleeBlob, calleeFunction, calleeLine);
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
            if (!SourceCompleteProgramsEnabled
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
            if (!SourceCompleteProgramsEnabled || kind == SourceKickoffKind.Opening) return;
            int line = kind == SourceKickoffKind.Onside ? 983 : 1004;
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "nfl.c", "game_proc", line, tracks: 1);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "nfl.c", "game_proc", line + 1, tracks: 35);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "nfl.c:game_proc:" + (line + 2),
                Model.SourceGameTuneBank, 0, 255, 127, 0x8000);
        }

        private void BeginSourceOriginalStatsSound(SourceCabinetFlowStage stage)
        {
            if (!SourceCompleteProgramsEnabled) return;
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
            if (!SourceCompleteProgramsEnabled) return;
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
            if (!SourceCompleteProgramsEnabled || sourceCabinetFlow.CompletedQuarter == 1) return;
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

        private int DrawSourceOriginalSoundTable(string table, string function, int line)
        {
            SourceAudioWeightedTable values = sourceOriginalSoundBindings.Table(table);
            return values.Values[SourceDraw(values.Values.Count, "sounds.c:" + function + ":" + line)];
        }

        private void EmitSourceOriginalGameplayCall(string function, int line, string bank, int playlist,
            uint volume, uint priority, long drawsBefore)
        {
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "sounds.c:" + function + ":" + line,
                bank, playlist, volume, 127, priority, checked((int)(Model.SourceRandomDraws - drawsBefore)),
                calleePath: "sounds.c", calleeBlob: "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9",
                calleeFunction: function, calleeLine: line);
        }

        private void EmitSourceOriginalMovementSpeech(ActorState actor, SourceMovementCommandKind kind)
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            if (kind == SourceMovementCommandKind.HurdleSpeech)
            {
                if (SourceDraw(100, "sounds.c:hurdle_speech:610") < 25)
                    EmitSourceOriginalGameplayCall("hurdle_speech", 611, Model.SourceAnnouncerSwappedBank,
                        DrawSourceOriginalSoundTable("hurdle_sp", "hurdle_speech", 611), 225, 0xe28a, before);
            }
            else if (kind == SourceMovementCommandKind.SpinSpeech)
            {
                if (SourceDraw(100, "sounds.c:spin_move_speech:618") < 35)
                    EmitSourceOriginalGameplayCall("spin_move_speech", 619,
                        Model.SourceAnnouncerSwappedBank, 150, 225, 0xe28a, before);
            }
            else if (kind == SourceMovementCommandKind.StiffArmSpeech)
            {
                if (SourceDraw(100, "sounds.c:stiff_arm_speech:633") < 35)
                    EmitSourceOriginalGameplayCall("stiff_arm_speech", 634,
                        Model.SourceAnnouncerSwappedBank, 151, 255, 0xe285, before);
            }
            else throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private int SourceOriginalWeaponAnnouncer(int playerId)
        {
            var rosters = Model.SourceRosters as SourceTeamRosterLibrary;
            if (rosters == null || playerId < 0 || playerId >= 14)
                throw new SourceC2MissingStateException("actual source static_data weapon announcer");
            int team = Model.Possession == TeamId.Home
                ? sourceConfiguration.HomeTeam.TeamIndex : sourceConfiguration.AwayTeam.TeamIndex;
            return rosters.TeamData[team].Players[playerId % 7].AnnouncerIndex;
        }

        private void SourceOriginalThrowBallSounds()
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            const string function = "throw_ball_sounds";
            if (Model.BallKind == SourceBallKind.Lateral)
            {
                EmitSourceOriginalGameplayCall(function, 1013, Model.SourceCurrentPlayerBank, 159, 160, 5, before);
                return;
            }
            if ((Model.SourceBallFlags & SourceBallFlags.FirePass) != 0)
                EmitSourceOriginalGameplayCall(function, 1019, Model.SourceCurrentPlayerBank, 131, 205, 5, before);
            else
                EmitSourceOriginalGameplayCall(function, 1021, Model.SourceCurrentPlayerBank,
                    DrawSourceOriginalSoundTable("throw_ball_snds", function, 1021), 205, 5, before);
            int field = SourceMatchRules.FieldPosition(Model.SourceBallTarget.X, (int)Model.Possession);
            if (field < Model.SourceRules.LineOfScrimmage) return;
            int distance = field - Model.SourceRules.LineOfScrimmage;
            if (distance < 10)
                EmitSourceOriginalGameplayCall(function, 1031, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable("throw_short_sp", function, 1031), 255, 0xe28f, before);
            else if (distance < 25)
            {
                int quarterback = Model.Possession == TeamId.Home ? 3 : 10;
                if (Model.Players[quarterback].AnimationState.Primary.SequenceName == "HURDLE")
                    EmitSourceOriginalGameplayCall(function, 1035, Model.SourceAnnouncerSwappedBank,
                        DrawSourceOriginalSoundTable("throw_jump_sp", function, 1035), 255, 0xe28f, before);
                else if (SourceDraw(100, "sounds.c:throw_ball_sounds:1037") < 25)
                    EmitSourceOriginalGameplayCall(function, 1042, SourceOriginalTeamBank(Model.Possession),
                        SourceOriginalWeaponAnnouncer(Model.TargetPlayerId) + 3, 255, 0xe28f, before);
                else
                    EmitSourceOriginalGameplayCall(function, 1045, Model.SourceAnnouncerSwappedBank,
                        DrawSourceOriginalSoundTable("throw_med_sp", function, 1045), 255, 0xe28f, before);
            }
            else if (SourceDraw(100, "sounds.c:throw_ball_sounds:1049") < 10)
                EmitSourceOriginalGameplayCall(function, 1054, SourceOriginalTeamBank(Model.Possession),
                    SourceOriginalWeaponAnnouncer(Model.TargetPlayerId) + 7, 255, 0xe28f, before);
            else
                EmitSourceOriginalGameplayCall(function, 1057, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable("throw_long_sp", function, 1057), 255, 0xe28f, before);
        }

        private void SourceOriginalCrowdCheer(long drawsBefore)
        {
            EmitSourceOriginalGameplayCall("crowd_cheer_snds", 1475, "generic",
                DrawSourceOriginalSoundTable("crowd_cheers_snds", "crowd_cheer_snds", 1475),
                205, 10, drawsBefore);
        }

        private void BeginSourceOriginalPeriodSound()
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            QueueSourceOriginalGameplayChild("eoq_speech_proc", 0, 0);
        }

        private void SourceOriginalEndQuarterMusic()
        {
            if (!SourceCompleteProgramsEnabled) return;
            int quarter = Model.Quarter - 1;
            bool noDrumRoll = quarter == 1 || quarter >= 3 && Model.HomeScore != Model.AwayScore || quarter == 6;
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "plyrinfo.c", "show_end_quarter_proc", 4133, tracks: 1);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "plyrinfo.c", "show_end_quarter_proc", 4134, tracks: 35);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay,
                "plyrinfo.c:show_end_quarter_proc:" + (noDrumRoll ? 4138 : 4141), "generic",
                noDrumRoll ? 15 : 17, 235, 127, 0x8000);
        }

        private void SourceOriginalSelectionClockSpeech()
        {
            if (!SourceCompleteProgramsEnabled || Model.Quarter != 2 && Model.Quarter != 4 ||
                (Model.SourceClock.PackedTime & 0x00ffff00u) > 0x00001f00u) return;
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            int offense = Model.Possession == TeamId.Home ? Model.HomeScore : Model.AwayScore;
            int defense = Model.Possession == TeamId.Home ? Model.AwayScore : Model.HomeScore;
            if ((Model.SourceClock.PackedTime & 0x00ffff00u) <= 0x00000c00u &&
                offense < defense && defense - offense <= 7)
                EmitSourceOriginalGameplayCall("need_big_play_sp", 820, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable("critical_sp", "need_big_play_sp", 820), 225, 0xe285, before);
            else
            {
                int sound = DrawSourceOriginalSoundTable("clock_sp", "clock_low_sp", 796);
                if (sound != 0)
                    EmitSourceOriginalGameplayCall("clock_low_sp", 798, Model.SourceAnnouncerSwappedBank,
                        sound, 225, 0xe285, before);
            }
        }

        private void SourceOriginalClockWarningSound()
        {
            if (!SourceCompleteProgramsEnabled) return;
            SourceProcessView process = RequireExecutingSourceSoundProcess();
            if (process.Serial != ClockProcess || process.Function != "game_clock_proc")
                throw new InvalidOperationException("The low-clock sound belongs only to its actual game_clock_proc wake.");
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "nfl.c:game_clock_proc:2940",
                Model.SourceCurrentCursorBank, 36, 235, 127, 0,
                calleePath: "nfl.c", calleeBlob: "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
                calleeFunction: "game_clock_proc", calleeLine: 2940);
        }

        private void SourceOriginalFreePointPrelude()
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            EmitSourceSoundControl(SourceProcessSoundControlKind.StopTracks, "playsel.c", "play_select", 1484, tracks: 1);
            EmitSourceSoundControl(SourceProcessSoundControlKind.ReservedTracks, "playsel.c", "play_select", 1485, tracks: 35);
            foreach (SourceOriginalSpeechChild child in sourceOriginalSpeechChildren.ToArray())
                if (Model.SourceProcesses.Require(child.Serial).Id == 0x0f9d)
                    CompleteSourceOriginalGameplayChild(child);
        }

        private void SourceOriginalFreePointSound(bool good)
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            if (!good)
            {
                EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "playsel.c:play_select:1491",
                    Model.SourceAnnouncerSwappedBank, 116, 205, 127, 0xe281);
                return;
            }
            long before = Model.SourceRandomDraws;
            EmitSourceOriginalGameplayCall("extra_point_speech", 387, Model.SourceAnnouncerSwappedBank,
                DrawSourceOriginalSoundTable("field_goal_sp", "extra_point_speech", 387), 235, 0xe281, before);
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "playsel.c:play_select:1495",
                Model.SourceCurrentPlayerBank, 156, 250, 127, 1);
        }

        private void SourceOriginalFreePointExitSound()
        {
            if (!SourceCompleteProgramsEnabled) return;
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "playsel.c:play_select:1518",
                Model.SourceCurrentPlayerBank, 148, 115, 127, 0,
                calleePath: "playsel.c", calleeBlob: "f5e87b921a4038d03f55174bbd5425241939e798",
                calleeFunction: "play_select", calleeLine: 1518);
        }

        private void SourceOriginalCabinetTickSound()
        {
            if (!SourceCompleteProgramsEnabled) return;
            SourceProcessView process = RequireExecutingSourceSoundProcess();
            if (process.Serial != GameProcess)
                throw new InvalidOperationException("The cabinet tick belongs only to its actual game_proc wait continuation.");
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "tick_sndn",
                Model.SourceCurrentCursorBank, 17, 115, 127, 1,
                calleePath: "Game.c", calleeBlob: "a005a9b44f032080f3dd6341dd4f5eb6104d9cd7",
                calleeFunction: "wait_for_any_butn_press", calleeLine: 251);
        }

        private bool DispatchSourceOriginalPeriodSpeech(SourceOriginalSpeechChild child)
        {
            if (child.Function != "eoq_speech_proc") return false;
            if (child.Stage == 0)
            {
                child.Stage = 1; Model.SourceProcesses.Sleep(5); return true;
            }
            long before = Model.SourceRandomDraws;
            if (child.Stage == 2)
                EmitSourceOriginalGameplayCall(child.Function, 700, "tkperm", 142, 255, 0xe280, before);
            else
            {
                int quarter = Model.Quarter - 1;
                if (quarter < 3)
                    EmitSourceOriginalGameplayCall(child.Function, quarter == 0 ? 667 : quarter == 1 ? 670 : 673,
                        "tkperm", quarter == 0 ? 175 : quarter == 1 ? 39 : 176, 255, 0xe280, before);
                else if (Model.HomeScore == Model.AwayScore)
                    EmitSourceOriginalGameplayCall(child.Function, quarter == 6 ? 682 : 684,
                        "tkperm", quarter == 6 ? 135 : 40, 255, 0xe280, before);
                else
                {
                    TeamId winner = Model.HomeScore > Model.AwayScore ? TeamId.Home : TeamId.Away;
                    EmitSourceOriginalGameplayCall(child.Function, winner == TeamId.Home ? 691 : 696,
                        SourceOriginalTeamBank(winner), 1, 255, 0xe280, before);
                    int team = winner == TeamId.Home ? sourceConfiguration.HomeTeam.TeamIndex : sourceConfiguration.AwayTeam.TeamIndex;
                    child.Stage = 2;
                    Model.SourceProcesses.Sleep(sourceOriginalSoundBindings.Table("team_name_sp_ticks").Values[team]);
                    return true;
                }
            }
            CompleteSourceOriginalGameplayChild(child);
            return true;
        }

        private void SourceOriginalCatchSounds(ActorState actor)
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            EmitSourceOriginalGameplayCall("catch_sounds", 930, Model.SourceCurrentPlayerBank, 127, 235, 1, before);
            switch (Model.BallKind)
            {
                case SourceBallKind.Punt:
                case SourceBallKind.Kickoff:
                case SourceBallKind.FieldGoal:
                    SourceOriginalCrowdCheer(before);
                    break;
                case SourceBallKind.Pass:
                case SourceBallKind.Bobble:
                    if (SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(actor).X,
                        (int)Model.Possession) >= Model.SourceRules.LineOfScrimmage)
                        QueueSourceOriginalGameplayChild("catch_speech_proc", actor.Id, 0x0ccc);
                    break;
                case SourceBallKind.Fumble:
                    EmitSourceOriginalGameplayCall("catch_sounds", 949,
                        SourceOriginalTeamBank(Model.Possession), 0, 255, 0xe285, before);
                    break;
            }
        }

        private void SourceOriginalFumbleBallSounds()
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            EmitSourceOriginalGameplayCall("fumble_ball_sounds", 857, Model.SourceCurrentPlayerBank, 143, 160, 1, before);
            EmitSourceOriginalGameplayCall("fumble_ball_sounds", 858, Model.SourceAnnouncerSwappedBank,
                DrawSourceOriginalSoundTable("fumble_sp", "fumble_ball_sounds", 858), 255, 0xe280, before);
        }

        private void SourceOriginalBobbleBallSounds()
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            EmitSourceOriginalGameplayCall("bobble_ball_sounds", 865, Model.SourceCurrentPlayerBank, 148, 160, 1, before);
            EmitSourceOriginalGameplayCall("bobble_ball_sounds", 866, Model.SourceAnnouncerSwappedBank,
                DrawSourceOriginalSoundTable("bobble_sp", "bobble_ball_sounds", 866), 255, 0xe281, before);
        }

        private void SourceOriginalDeflectBallSound(bool batDown)
        {
            if (!SourceCompleteProgramsEnabled) return;
            RequireExecutingSourceSoundProcess();
            if (batDown)
            {
                EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "player.c:standard_catch:3752",
                    Model.SourceCurrentPlayerBank, 134, 235, 127, 0,
                    calleePath: "player.c", calleeBlob: SourceOriginalSoundBindings.PlayerGitBlob,
                    calleeFunction: "standard_catch", calleeLine: 3752);
                QueueSourceOriginalGameplayChild("delay_deflect_proc", 0, 0);
            }
            else if ((Model.SourceFlags & SourceGameFlags.OnsideKick) == 0)
                EmitSourceProcessSound(SourceProcessSoundKind.OriginalGameplay, "player.c:standard_catch:3759",
                    Model.SourceCurrentPlayerBank, 148, 235, 127, 1,
                    calleePath: "player.c", calleeBlob: SourceOriginalSoundBindings.PlayerGitBlob,
                    calleeFunction: "standard_catch", calleeLine: 3759);
        }

        private void QueueSourceOriginalGameplayChild(string function, int argument, int id)
        {
            var child = new SourceOriginalSpeechChild {
                Serial = ++sourceFireNextSerial, Function = function, Argument = argument
            };
            Model.SourceProcesses.Create(child.Serial, id, function, 0);
            sourceOriginalSpeechChildren.Insert(0, child);
        }

        private bool DispatchSourceOriginalGameplayChild(SourceOriginalSpeechChild child)
        {
            if (DispatchSourceOriginalPeriodSpeech(child)) return true;
            if (child.Function != "catch_speech_proc" && child.Function != "delay_deflect_proc") return false;
            if (child.Stage == 0)
            {
                child.Stage = 1;
                Model.SourceProcesses.Sleep(child.Function == "catch_speech_proc" ? 18 : 30);
                return true;
            }
            if (child.Stage == 2)
            {
                CompleteSourceOriginalGameplayChild(child);
                return true;
            }
            long before = Model.SourceRandomDraws;
            if (child.Function == "delay_deflect_proc")
            {
                EmitSourceOriginalGameplayCall("deflected_ball_sp", 849, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable("deflected_sp", "deflected_ball_sp", 849), 255, 0xe281, before);
                SourceOriginalCrowdCheer(before);
                CompleteSourceOriginalGameplayChild(child);
                return true;
            }
            int player = child.Argument;
            if (Model.CarrierId != player)
            {
                if (SourceDraw(100, "sounds.c:catch_speech_proc:969") < 30)
                    EmitSourceOriginalGameplayCall(child.Function, 970, "generic", 13, 205, 10, before);
                CompleteSourceOriginalGameplayChild(child);
                return true;
            }
            SourceOriginalCrowdCheer(before);
            int completions = Model.SourceConsecutiveCompletions[(int)Model.Players[player].Team];
            if (completions >= 2)
            {
                int line = completions == 2 ? 589 : completions == 3 ? 594 : 599;
                int sound = completions == 2 ? 180 : completions == 3 ? 181
                    : DrawSourceOriginalSoundTable("catch_fire_sp", "on_fire_sp", 599);
                EmitSourceOriginalGameplayCall("on_fire_sp", line, Model.SourceAnnouncerSwappedBank,
                    sound, 255, completions > 3 ? 0xe285u : 0xe281u, before);
                CompleteSourceOriginalGameplayChild(child);
                return true;
            }
            if (Model.BallKind == SourceBallKind.Bobble)
                EmitSourceOriginalGameplayCall(child.Function, 982, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable("catch_bobble_sp", child.Function, 982), 225, 0xe28a, before);
            else if (SourceDraw(100, "sounds.c:catch_speech_proc:984") < 45)
            {
                int sound = SourceOriginalWeaponAnnouncer(Model.CarrierId);
                if (sound != 0)
                {
                    EmitSourceOriginalGameplayCall(child.Function, 991, SourceOriginalTeamBank(Model.Possession),
                        sound, 205, 0xe285, before);
                    CompleteSourceOriginalGameplayChild(child);
                    return true;
                }
            }
            else
            {
                bool shortCatch = SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(Model.Players[player]).X,
                    (int)Model.Possession) - Model.SourceRules.LineOfScrimmage < 25;
                int line = shortCatch ? 996 : 998;
                EmitSourceOriginalGameplayCall(child.Function, line, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable(shortCatch ? "catch_short_sp" : "catch_long_sp", child.Function, line),
                    225, 0xe28a, before);
            }
            child.Stage = 2;
            Model.SourceProcesses.Sleep(90);
            return true;
        }

        private void CompleteSourceOriginalGameplayChild(SourceOriginalSpeechChild child)
        {
            Model.SourceProcesses.Kill(child.Serial);
            sourceOriginalSpeechChildren.Remove(child);
        }

        private void EmitSourceOriginalContactCall(SourceContactSoundCue cue, string bank, int playlist,
            uint volume, uint pan, uint priority, string path, string function, int line, long drawsBefore)
        {
            EmitSourceProcessSound(SourceProcessSoundKind.OriginalContact, path + ":" + function + ":" + line,
                bank, playlist, volume, pan, priority, checked((int)(Model.SourceRandomDraws - drawsBefore)),
                cue, path, path == "sounds.c" ? "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9" :
                    path == "player.c" ? SourceOriginalSoundBindings.PlayerGitBlob : SourceOriginalSoundBindings.ContactGitBlob,
                function, line);
        }

        private void QueueSourceOriginalContactChild(SourceContactSoundCue cue, string function)
        {
            var child = new SourceOriginalSpeechChild { Serial = ++sourceFireNextSerial, Function = function, ContactCue = cue };
            Model.SourceProcesses.Create(child.Serial, 0, function, 0);
            sourceOriginalSpeechChildren.Insert(0, child);
        }

        private bool ExecuteSourceOriginalContactSound(SourceContactSoundCue cue)
        {
            if (!SourceCompleteProgramsEnabled) return false;
            RequireSourceSoundVmBinding();
            sourceOriginalSoundBindings.RequireContact(cue);
            RequireExecutingSourceSoundProcess();
            long before = Model.SourceRandomDraws;
            try
            {
                string function = cue.FunctionSymbol;
                if (function == "crowd_cheer_snds" || function == "crowd_tackle_snds")
                {
                    int line = function == "crowd_cheer_snds" ? 1475 : 1488;
                    int id = DrawSourceOriginalSoundTable(function == "crowd_cheer_snds" ?
                        "crowd_cheers_snds" : "tackle_crowd_cheers_snds", function, line);
                    EmitSourceOriginalContactCall(cue, "generic", id, 205, 127, 10, "sounds.c", function, line, before);
                }
                else if (function == "monkey_on_back_sp")
                {
                    if (SourceDraw(100, "sounds.c:monkey_on_back_sp:573") < 30)
                        EmitSourceOriginalContactCall(cue, Model.SourceTauntBank, 38, 235, 38, 1, "sounds.c", function, 574, before);
                    else
                        EmitSourceOriginalContactCall(cue, Model.SourceAnnouncerSwappedBank,
                            DrawSourceOriginalSoundTable("monkey_tackle_sp", function, 576),
                            255, 127, 0xe281, "sounds.c", function, 576, before);
                }
                else if (function == "qb_duck_speech")
                    EmitSourceOriginalContactCall(cue, Model.SourceAnnouncerSwappedBank,
                        DrawSourceOriginalSoundTable("qb_duck_sp", function, 783), 235, 127, 5, "sounds.c", function, 783, before);
                else if (function == "snd_scall_bank")
                {
                    ReadOnlyCollection<string> args = cue.OriginalArguments;
                    string bank = args[0] == "plyr_bnk_str" ? Model.SourceCurrentPlayerBank :
                        args[0] == "ancr_swaped_bnk_str" ? Model.SourceAnnouncerSwappedBank :
                        throw new NotSupportedException("Unimplemented original contact bank operand.");
                    int id = args[1] == "OUTTA_MY_FACE_SP" ? 50 : args[1] == "P_HARD_HIT2_SND" ? 96 :
                        args[1] == "P_HARD_HIT3_SND" ? 94 : args[1] == "BOWLING_PIN_SND" ? 141 :
                        int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                    uint volume = args[2] == "VOLUME3" ? 205u : args[2] == "VOLUME4" ? 235u :
                        args[2] == "ANCR_VOL2" ? 255u : throw new NotSupportedException("Unimplemented original contact volume operand.");
                    uint priority = args[4] == "LVL3" ? 0xe285u : args[4] == "LVL4" ? 0xe281u :
                        args[4] == "PRIORITY_LVL3" ? 5u : args[4] == "PRIORITY_LVL4" ? 1u :
                        throw new NotSupportedException("Unimplemented original contact priority operand.");
                    EmitSourceOriginalContactCall(cue, bank, id, volume,
                        uint.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), priority,
                        cue.Context.SourcePath, cue.Context.ExecutingSymbol, cue.Context.SourceLine, before);
                }
                else if (function == "hit_intended_receiver_sp")
                    QueueSourceOriginalContactChild(cue, "hit_intended_receiver_sp_proc");
                else if (function == "qcreate")
                    QueueSourceOriginalContactChild(cue, "delay_plow_proc");
                else if (function == "late_hit_speech")
                {
                    if (!sourceOriginalSaidLateHit)
                    {
                        sourceOriginalSaidLateHit = true;
                        if (sourceOriginalLateHitSpeechIndex >= sourceOriginalSoundBindings.Table("late_hit_sp").Values.Count)
                            sourceOriginalLateHitSpeechIndex = 0;
                        QueueSourceOriginalContactChild(cue, "late_hit_speech_proc");
                    }
                }
                else throw new NotSupportedException("Unimplemented original contact sound function: " + function);
                return true;
            }
            catch { sourceSoundVmFailed = true; throw; }
        }

        private bool DispatchSourceOriginalContactChild(SourceOriginalSpeechChild child)
        {
            if (child.ContactCue == null) return false;
            if (child.Stage == 0)
            {
                child.Stage = 1;
                Model.SourceProcesses.Sleep(child.Function == "delay_plow_proc" ? 50 :
                    child.Function == "hit_intended_receiver_sp_proc" ? 19 : 20);
                return true;
            }
            long before = Model.SourceRandomDraws;
            if (child.Function == "delay_plow_proc")
                EmitSourceOriginalContactCall(child.ContactCue, Model.SourceAnnouncerSwappedBank,
                    DrawSourceOriginalSoundTable("head_plow_sp", "head_plow_speech", 873),
                    225, 127, 0xe28a, "sounds.c", "head_plow_speech", 873, before);
            else if (child.Function == "hit_intended_receiver_sp_proc")
            {
                if (Model.CarrierId != Model.TargetPlayerId)
                    EmitSourceOriginalContactCall(child.ContactCue, Model.SourceAnnouncerSwappedBank,
                        DrawSourceOriginalSoundTable("hit_receiver_sp", child.Function, 750),
                        255, 127, 0xe281, "sounds.c", child.Function, 750, before);
            }
            else if (Model.Phase != GamePhase.PlaySelect && Model.Phase != GamePhase.PreSnap)
            {
                SourceAudioWeightedTable table = sourceOriginalSoundBindings.Table(SourceOriginalBadLanguageEnabled ?
                    "late_hit_sp" : "no_swear_late_hit_sp");
                EmitSourceOriginalContactCall(child.ContactCue, Model.SourceAnnouncerSwappedBank,
                    table.Values[sourceOriginalLateHitSpeechIndex++], 225, 127, 0xe285,
                    "sounds.c", child.Function, SourceOriginalBadLanguageEnabled ? 917 : 919, before);
            }
            Model.SourceProcesses.Kill(child.Serial);
            sourceOriginalSpeechChildren.Remove(child);
            return true;
        }

        private void InitializeSourceSoundState()
        {
            sourceProcessSoundPending.Clear();
            sourceSoundControlPending.Clear();
            sourceBootSoundPending.Clear();
            sourceProcessCameras.Clear();
            sourceOriginalSpeechChildren.Clear();
            sourceOriginalSaidLateHit = false;
            sourceOriginalLateHitSpeechIndex = 0;
            SourceOriginalBadLanguageEnabled = sourceOriginalBadLanguageAdjustment ?? SourceOriginalSoundBindings.DefaultBadLanguageEnabled;
            sourceOriginalSoundBindings = SourceCompleteAv306Enabled
                ? SourceOriginalSoundBindings.ForCompleteAv306(sourceAnimations)
                : SourceCompleteOriginalEnabled
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
                    case "hut_snd":
                        result = SourceOriginalAudioFunctions.SelectHut(sourceOriginalSoundBindings,
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
                if (DispatchSourceOriginalContactChild(child)) return true;
                if (DispatchSourceOriginalGameplayChild(child)) return true;
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
