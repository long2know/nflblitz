using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    // Game.c:266-309, NOT wait_for_any_butn_press. The sole undefined-native
    // repair is an explicitly admitted zero initialization on each invocation.
    internal sealed class SourceAllButtonsWait
    {
        internal readonly int Minimum, Maximum;
        internal readonly int[] Times = new int[4];
        internal int Ticks, AcknowledgingStations;

        internal SourceAllButtonsWait(int minimum, int maximum)
        { Minimum = minimum; Maximum = maximum; }

        internal bool Resume(uint switches, SourceCabinetFlowConfiguration configuration)
        {
            Ticks++;
            if (Ticks >= Minimum)
            {
                for (int i = 0; i < Times.Length; i++)
                {
                    if ((configuration.ParticipatingStationMask & (1 << i)) == 0) continue;
                    uint buttons = switches >> (8 * i) & (uint)configuration.ButtonMasks.PlayerAbcdMask;
                    if (buttons == configuration.ButtonMasks.PlayerAbcMask)
                    {
                        if (++Times[i] >= 8) AcknowledgingStations |= 1 << i;
                    }
                    else Times[i] = 0;
                }
            }
            return AcknowledgingStations != 0 || Ticks >= Maximum;
        }
    }

    internal sealed class SourceCabinetFlowProcess : ISourceAnyButtonInputs
    {
        internal const int EndOfQuarterId = 0x0f9c, TimerId = 601;
        internal readonly SourceCabinetFlowConfiguration Configuration;
        internal SourceCabinetFlowStage Stage;
        internal long StartedTick;
        internal int Elapsed, Minimum, Maximum, AcknowledgingStations, ConfirmedStations;
        internal int? CompletedQuarter;
        internal int? CoachingBackground, CoachingTip;
        internal bool WaitingForRelease, ApplyingPeriod, ContinueAfterGate;
        internal uint Switches;
        internal SourceAllButtonsWait AllButtons;
        internal SourceAllButtonsWait EndOfQuarterButtons;
        internal SourceAnyButtonWait AnyButton;
        internal SourceMatchView FinalMatch;
        internal SourceFinalRecordCommit RecordCommit;
        internal bool RecordSaveRequired;
        internal SourceRulesState? SkippedPatCleanupRules;
        internal int FinalHomeScore, FinalAwayScore;
        internal long EndOfQuarterSerial, TimerSerial;
        internal int EndOfQuarterTimeout, MessageTimeout, TimerPhase, ShowingTicks;
        internal int Seconds;
        internal bool NumberVisible, TimerDone, EndOfQuarterStarted;

        internal SourceCabinetFlowProcess(SourceCabinetFlowConfiguration configuration)
        { Configuration = configuration; }

        internal void Enter(SourceCabinetFlowStage stage, long tick, int minimum = 0, int maximum = 0)
        {
            Stage = stage; StartedTick = tick; Elapsed = 0; Minimum = minimum; Maximum = maximum;
            AllButtons = null; AnyButton = null;
            WaitingForRelease = false;
        }

        internal SourceCabinetFlowView Capture()
        {
            SourceAllButtonsWait buttons = Stage == SourceCabinetFlowStage.EndOfQuarter ? EndOfQuarterButtons : AllButtons;
            return new SourceCabinetFlowView(Configuration, Stage, StartedTick, Elapsed, Minimum, Maximum,
                buttons == null ? new int[4] : buttons.Times, AcknowledgingStations, ConfirmedStations,
                CompletedQuarter, Stage == SourceCabinetFlowStage.ContinuationOffer ? (int?)Seconds : null,
                NumberVisible, WaitingForRelease, FinalMatch, FinalHomeScore, FinalAwayScore,
                CoachingBackground, CoachingTip);
        }

        internal uint ParticipatingButtons
        {
            get
            {
                uint result = 0;
                for (int i = 0; i < 4; i++)
                    if ((Configuration.ParticipatingStationMask & (1 << i)) != 0)
                        result |= Switches & ((uint)Configuration.ButtonMasks.PlayerAbcdMask << (8 * i));
                return result;
            }
        }

        internal void ObserveConfirmation(bool administrativeContinue)
        {
            if (administrativeContinue) ConfirmedStations |= Configuration.ParticipatingStationMask;
            if (ParticipatingButtons == 0) WaitingForRelease = false;
            else if (!WaitingForRelease)
                for (int i = 0; i < 4; i++)
                    if ((ParticipatingButtons & ((uint)Configuration.ButtonMasks.PlayerAbcdMask << (8 * i))) != 0)
                        ConfirmedStations |= 1 << i;
        }

        public int ReadPlayerSwitches() { return unchecked((int)Switches); }
        public int ReadShowPlyselIdiotBox() { return 0; }
    }

    internal sealed class SourceFreePointContinuation : ISourceAnyButtonInputs
    {
        internal readonly SourcePatDecision Decision;
        internal readonly long StartedTick;
        internal readonly int AllButtonsMask;
        internal bool Awarded;
        internal uint Switches;
        internal SourceAnyButtonWait Wait;

        internal SourceFreePointContinuation(SourcePatDecision decision, long startedTick, int allButtonsMask)
        { Decision = decision; StartedTick = startedTick; AllButtonsMask = allButtonsMask; }

        public int ReadPlayerSwitches() { return unchecked((int)Switches); }
        // playsel.c clears show_plysel_idiot_box before entering the free-point box.
        public int ReadShowPlyselIdiotBox() { return 0; }
    }

    public sealed partial class GameSimulation : ISourceCabinetFlowSimulation
    {
        private SourceCabinetFlowConfiguration sourceCabinetFlowConfiguration;
        private SourceCabinetFlowProcess sourceCabinetFlow;
        private ISourceRecordCommitBarrier sourceRecordCommitBarrier;
        private SourceFreePointContinuation sourceFreePointContinuation;
        private bool SourceFreePointPending { get { return sourceFreePointContinuation != null; } }

        public void BindSourceRecordCommitBarrier(ISourceRecordCommitBarrier barrier)
        {
            if (barrier == null) throw new ArgumentNullException(nameof(barrier));
            if (Compatibility != SourceGameplayCompatibility.Portable || !Model.SourceMode
                || sourceCabinetFlowConfiguration == null || sourceCustomPlaySession == null
                || sourceCustomPlaySession.SavedRecords == null)
                throw new InvalidOperationException("Record commit requires the actual Portable cabinet flow and saved record session.");
            if (Model.Tick != 0 || Model.SourceTick != 0 || sourceRecordCommitBarrier != null)
                throw new InvalidOperationException("Bind the final record commit barrier once before the first source wake.");
            sourceRecordCommitBarrier = barrier;
        }

        public void BindSourceCabinetFlow(SourceCabinetFlowConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (!Model.SourceMode || Compatibility != SourceGameplayCompatibility.Portable || Model.SourceTick != 0)
                throw new InvalidOperationException("Bind source-gameflow/v1 explicitly to Portable source gameplay before its first source wake.");
            int participants = 0;
            foreach (SourceStationConfiguration station in sourceConfiguration.Stations)
                participants |= 1 << station.StationId;
            if (configuration.ParticipatingStationMask != participants)
                throw new ArgumentException("Free-play participation must equal the configured human source stations.", nameof(configuration));
            if (sourceCabinetFlowConfiguration != null)
            {
                if (!ReferenceEquals(sourceCabinetFlowConfiguration, configuration))
                    throw new InvalidOperationException("source-gameflow/v1 is already bound.");
                return;
            }
            sourceCabinetFlowConfiguration = configuration;
            InitializeSourceCabinetFlow();
            Publish();
        }

        private void InitializeSourceCabinetFlow()
        {
            sourceFreePointContinuation = null;
            sourceCabinetFlow = sourceCabinetFlowConfiguration == null ? null
                : new SourceCabinetFlowProcess(sourceCabinetFlowConfiguration);
            CaptureSourceCabinetFlow();
        }

        private void CaptureSourceCabinetFlow()
        {
            Model.SourceCabinetFlow = sourceCabinetFlow == null ? null : sourceCabinetFlow.Capture();
        }

        private static uint SourceCabinetSwitches(SourceInputFrame frame)
        {
            uint switches = 0;
            if (frame != null)
                foreach (SourceStationInput input in frame.Stations)
                {
                    switches |= (uint)input.Held << (input.StationId * 8 + 4);
                    if (input.DigitalCurrent.HasValue)
                        switches |= (uint)input.DigitalCurrent.Value << (input.StationId * 8);
                }
            return switches;
        }

        private void BeginSourceCabinetResultWait()
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            if (flow == null) return;
            bool scorePlate = SourceMatchFlow.ResultBoxShowsScorePlate(Model.SourceLastResult);
            int minimum = scorePlate ? 70 : 30, maximum = scorePlate ? 265 : 205;
            flow.Enter(SourceCabinetFlowStage.ResultWait, Model.SourceTick, minimum, maximum);
            flow.AllButtons = new SourceAllButtonsWait(minimum, maximum);
            flow.AcknowledgingStations = 0;
            flow.CompletedQuarter = null;
            flow.SkippedPatCleanupRules = null;
            flow.CoachingBackground = null; flow.CoachingTip = null;
            CaptureSourceCabinetFlow();
        }

        private bool BeginSourceCabinetPeriod()
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            if (flow == null || flow.ApplyingPeriod || Model.SourceClock.PackedTime != 0
                || Model.SourceRules.PlayType == SourcePlayType.ExtraPoint
                || Model.Phase == GamePhase.LiveBall || Model.SourceC2PendingResult != null)
                return false;
            if (flow.Stage != SourceCabinetFlowStage.Inactive) return true;
            SourceFireSelectionExit();
            flow.Enter(SourceCabinetFlowStage.EndOfQuarter, Model.SourceTick, 40, 4 * SourceTiming.DefaultTicksPerSecond);
            flow.CompletedQuarter = Model.Quarter - 1;
            flow.EndOfQuarterTimeout = SourceTiming.DefaultTicksPerSecond * 10 - 1;
            flow.MessageTimeout = SourceTiming.DefaultTicksPerSecond * 6;
            flow.EndOfQuarterButtons = null; flow.AcknowledgingStations = 0;
            flow.EndOfQuarterStarted = false;
            BeginSourceOriginalPeriodSound();
            flow.EndOfQuarterSerial = ++sourceFireNextSerial;
            Model.SourceProcesses.Create(flow.EndOfQuarterSerial, SourceCabinetFlowProcess.EndOfQuarterId,
                "show_end_quarter_proc", 0);
            Model.Phase = GamePhase.DeadBall;
            Model.SourceMatchPhase = SourceMatchPhase.PeriodTransition;
            CaptureSourceCabinetFlow();
            return true;
        }

        private bool AdvanceSourceCabinetAnyWait(SourceCabinetFlowProcess flow)
        {
            return flow.AnyButton.Advance(flow,
                SourceCabinetWaitTick,
                ticks => Model.SourceProcesses.Sleep(ticks));
        }

        private void SourceCabinetWaitTick()
        {
            Model.SourceProcesses.Record("CabinetTickSound", GameProcess, 0, 0);
            SourceOriginalCabinetTickSound();
        }

        private void BeginSourceFreePointContinuation(SourcePatDecision pat)
        {
            if (sourceFreePointContinuation != null)
                throw new InvalidOperationException("The selected source free point already owns game_proc.");
            StopSourceSelectionTimer();
            sourceSelectionCpuPatPending = false;
            sourceSelectionFinishPending = false;
            int mask = sourceCabinetFlow == null ? unchecked((int)0xf0f0f0f0u)
                : unchecked((int)sourceCabinetFlow.Configuration.ButtonMasks.AllAbcdMask);
            sourceFreePointContinuation = new SourceFreePointContinuation(pat, Model.SourceTick, mask);
            Model.Phase = GamePhase.PlaySelect;
            Model.SourceMatchPhase = SourceMatchPhase.PatSelection;
            Model.SourceResultView = new SourceMatchResult(SourcePlayKind.ExtraPoint,
                pat.KickGood ? SourceResultKind.ExtraPoint : SourceResultKind.MissedKick);
            if (sourceCabinetFlow != null)
                sourceCabinetFlow.Enter(SourceCabinetFlowStage.ResultWait, Model.SourceTick, 15, 15);
            Model.SourceProcesses.Sleep(15);
            CaptureSourceCabinetFlow();
        }

        private bool StepSourceFreePointContinuation(PlayerCommand command)
        {
            SourceFreePointContinuation pending = sourceFreePointContinuation;
            if (pending == null) return false;
            pending.Switches = SourceCabinetSwitches(command.StationInputs);
            if (!pending.Awarded)
            {
                if (Model.SourceTick < pending.StartedTick + 15)
                    throw new InvalidOperationException("Original playsel.c sleep(15) resumed prematurely.");
                SourceOriginalFreePointPrelude();
                SourceOriginalFreePointSound(pending.Decision.KickGood);
                AwardSourcePat(pending.Decision);
                pending.Awarded = true;
                pending.Wait = new SourceAnyButtonWait(40, 5 * SourceTiming.DefaultTicksPerSecond, pending.AllButtonsMask);
                if (sourceCabinetFlow != null)
                    sourceCabinetFlow.Enter(SourceCabinetFlowStage.ResultWait, Model.SourceTick, 40,
                        40 + 5 * SourceTiming.DefaultTicksPerSecond);
            }
            if (pending.Wait.Advance(pending, SourceCabinetWaitTick, ticks => Model.SourceProcesses.Sleep(ticks)))
            {
                SourceOriginalFreePointExitSound();
                sourceFreePointContinuation = null;
                if (sourceCabinetFlow != null)
                    sourceCabinetFlow.Enter(SourceCabinetFlowStage.Inactive, Model.SourceTick);
                FinishSourcePat(pending.Decision);
            }
            CaptureSourceCabinetFlow();
            return true;
        }

        private void BeginSourceCabinetStats(SourceCabinetFlowStage stage, int minimum, int wait)
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            flow.Enter(stage, Model.SourceTick, minimum, minimum + wait);
            BeginSourceOriginalStatsSound(stage);
            if (stage == SourceCabinetFlowStage.CoachingTip)
            {
                // select.c:5675-5707/5731/5756: preserve both ordered draws even
                // when the next owner has not bound the background/text renderer.
                flow.CoachingBackground = SourceDraw(7, "select.c:coaching_tip_scrn:background");
                flow.CoachingTip = SourceDraw(30, "select.c:coaching_tip_scrn:hint");
                if (SourceCompleteProgramsEnabled) LoadSourceOriginalEoqSound(false);
            }
            flow.AnyButton = new SourceAnyButtonWait(minimum, wait,
                unchecked((int)flow.Configuration.ButtonMasks.AllAbcdMask));
            AdvanceSourceCabinetAnyWait(flow);
            CaptureSourceCabinetFlow();
        }

        private void SuspendSourceCabinetWorld()
        {
            Model.SourceProcesses.SuspendMultiple(0, 0x8000);
            if (!SourceFireEnabled) return;
            FireControl(SourceFireOperation.SuspendMask, "nfl.c", "eoq_handling", 2634, 0, 0x8000);
            FireControl(SourceFireOperation.HideAllObjects, "nfl.c", "eoq_handling", 2635);
        }

        private void ResumeSourceCabinetWorld()
        {
            if (SourceFireEnabled)
                FireControl(SourceFireOperation.UnhideAllObjects, "nfl.c", "start_new_quarter", 2702);
            Model.SourceProcesses.ResumeAll();
            if (!SourceFireEnabled) return;
            FireControl(SourceFireOperation.ResumeAll, "nfl.c", "start_new_quarter", 2704);
        }

        private void BeginSourceCabinetFinal()
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            if (flow == null || flow.FinalMatch != null) return;
            flow.FinalMatch = new GameSnapshot(Model).Match;
            flow.FinalHomeScore = Model.HomeScore; flow.FinalAwayScore = Model.AwayScore;
            int periods = flow.FinalMatch.Period.CompletedQuarterIndex.HasValue
                ? flow.FinalMatch.Period.CompletedQuarterIndex.Value + 1 : 0;
            flow.RecordSaveRequired = periods >= 4 && Model.HomeScore != Model.AwayScore || periods == 7;
            // nfl.c:2742-2753 does not save partial/declined early games.
            if (sourceRecordCommitBarrier != null && flow.RecordSaveRequired)
            {
                if (Model.SourceProcesses.Caller != GameProcess
                    || Model.SourceProcesses.Require(GameProcess).LastWakeTick != Model.SourceTick)
                    throw new InvalidOperationException("Final record commit belongs to the actual game_proc producer.");
                flow.RecordCommit = new SourceFinalRecordCommit(flow.FinalMatch, Model.HomeScore, Model.AwayScore,
                    flow.Configuration.ParticipatingStationMask, Model.SourceTick, GameProcess);
                flow.Enter(SourceCabinetFlowStage.RecordCommitWait, Model.SourceTick);
                sourceRecordCommitBarrier.BeginFinalCommit(flow.RecordCommit);
                ContinueSourceRecordCommit();
                CaptureSourceCabinetFlow();
                return;
            }
            BeginSourceCabinetStats(SourceCabinetFlowStage.FinalStats,
                SourceTiming.DefaultTicksPerSecond, SourceTiming.DefaultTicksPerSecond * 22);
        }

        private void ContinueSourceRecordCommit()
        {
            SourceRecordCommitBarrierState state = sourceRecordCommitBarrier.ReadFinalCommitState(sourceCabinetFlow.RecordCommit);
            if ((int)state < 0 || (int)state > 3)
                throw new InvalidOperationException("Unknown final record commit barrier state.");
            if (state == SourceRecordCommitBarrierState.Pending || state == SourceRecordCommitBarrierState.Failed)
            {
                Model.SourceProcesses.Sleep(1);
                return;
            }
            BeginSourceCabinetStats(SourceCabinetFlowStage.FinalStats,
                SourceTiming.DefaultTicksPerSecond, SourceTiming.DefaultTicksPerSecond * 22);
        }

        private void ApplySourceCabinetPeriod(bool continueAfterGate)
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            flow.ContinueAfterGate = continueAfterGate; flow.ApplyingPeriod = true;
            flow.Enter(SourceCabinetFlowStage.Inactive, Model.SourceTick);
            try { ContinueSource(); }
            finally { flow.ApplyingPeriod = false; CaptureSourceCabinetFlow(); }
        }

        private void BeginSourceCabinetOffer()
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            int fullMask = sourceConfiguration.StationLayout == SourceStationLayout.FourStation ? 15 : 3;
            if (flow.Configuration.ParticipatingStationMask == fullMask
                && flow.Configuration.ContinuationPolicy == SourceCabinetContinuationPolicy.KeepParticipatingStations)
            {
                ApplySourceCabinetPeriod(true);
                return;
            }
            flow.Enter(SourceCabinetFlowStage.ContinuationOffer, Model.SourceTick);
            BeginSourceOriginalOfferSound();
            flow.ConfirmedStations = 0; flow.WaitingForRelease = true;
            flow.Seconds = 9; flow.TimerPhase = 0; flow.TimerDone = false; flow.NumberVisible = false;
            flow.TimerSerial = ++sourceFireNextSerial;
            Model.SourceProcesses.Create(flow.TimerSerial, SourceCabinetFlowProcess.TimerId, "timedown", 0);
            CaptureSourceCabinetFlow();
        }

        private bool StepSourceCabinetFlow(PlayerCommand command)
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            if (flow == null || flow.Stage == SourceCabinetFlowStage.Inactive) return false;
            flow.Switches = SourceCabinetSwitches(command.StationInputs);
            if (flow.StartedTick == Model.SourceTick) return true;
            bool administrativeContinue = SourceActionFor(command) == SourceAction.Continue;
            if (command.StationInputs != null)
                foreach (SourceStationInput station in command.StationInputs.Stations)
                    administrativeContinue |= station.Command.IsSpecified && station.Command.Action == SourceAction.Continue;
            if (flow.Stage != SourceCabinetFlowStage.FinalConfirmation
                && flow.Stage != SourceCabinetFlowStage.NewMatchRequested) flow.Elapsed++;
            switch (flow.Stage)
            {
                case SourceCabinetFlowStage.ResultWait:
                    if (!flow.AllButtons.Resume(flow.Switches, flow.Configuration)) break;
                    flow.AcknowledgingStations = flow.AllButtons.AcknowledgingStations;
                    flow.Enter(SourceCabinetFlowStage.Inactive, Model.SourceTick);
                    ContinueSource();
                    break;
                case SourceCabinetFlowStage.EndOfQuarter:
                    if (Model.SourceProcesses.Contains(flow.EndOfQuarterSerial) && --flow.EndOfQuarterTimeout != 0) break;
                    SuspendSourceCabinetWorld();
                    int quarter = flow.CompletedQuarter.Value;
                    if (quarter >= 3 && (Model.HomeScore != Model.AwayScore || quarter == 6))
                        ApplySourceCabinetPeriod(true);
                    else if (quarter == 1)
                        BeginSourceCabinetStats(SourceCabinetFlowStage.HalftimeStats,
                            SourceTiming.DefaultTicksPerSecond, SourceTiming.DefaultTicksPerSecond * 22);
                    else
                        BeginSourceCabinetStats(SourceCabinetFlowStage.CoachingTip,
                            20, SourceTiming.DefaultTicksPerSecond * 10);
                    break;
                case SourceCabinetFlowStage.CoachingTip:
                case SourceCabinetFlowStage.HalftimeStats:
                    if (AdvanceSourceCabinetAnyWait(flow))
                    {
                        EndSourceOriginalStatsSound(flow.Stage);
                        BeginSourceCabinetOffer();
                    }
                    break;
                case SourceCabinetFlowStage.ContinuationOffer:
                    flow.ObserveConfirmation(administrativeContinue);
                    if (flow.TimerDone)
                        ApplySourceCabinetPeriod(flow.Configuration.ParticipatingStationMask != 0
                            && (flow.Configuration.ContinuationPolicy == SourceCabinetContinuationPolicy.KeepParticipatingStations
                                || flow.ConfirmedStations != 0));
                    break;
                case SourceCabinetFlowStage.FinalStats:
                    if (AdvanceSourceCabinetAnyWait(flow))
                    {
                        EndSourceOriginalStatsSound(flow.Stage);
                        flow.Enter(SourceCabinetFlowStage.FinalConfirmation, Model.SourceTick);
                        flow.WaitingForRelease = true;
                    }
                    break;
                case SourceCabinetFlowStage.RecordCommitWait:
                    ContinueSourceRecordCommit();
                    break;
                case SourceCabinetFlowStage.FinalConfirmation:
                    flow.ObserveConfirmation(false);
                    if (administrativeContinue || !flow.WaitingForRelease && flow.ParticipatingButtons != 0)
                        flow.Enter(SourceCabinetFlowStage.NewMatchRequested, Model.SourceTick);
                    break;
                case SourceCabinetFlowStage.NewMatchRequested:
                    break;
                default: throw new InvalidOperationException("Unknown source-gameflow/v1 continuation.");
            }
            CaptureSourceCabinetFlow();
            return true;
        }

        private void DispatchSourceCabinetChildren(SourceInputFrame inputs)
        {
            SourceCabinetFlowProcess flow = sourceCabinetFlow;
            if (flow == null) return;
            flow.Switches = SourceCabinetSwitches(inputs);
            SourcePortableProcessHost host = Model.SourceProcesses;
            if (flow.EndOfQuarterSerial != 0 && host.Visit(flow.EndOfQuarterSerial))
            {
                BeginSourceProcess("show_end_quarter_proc", -1, 0);
                try
                {
                    if (!flow.EndOfQuarterStarted)
                    {
                        SourceOriginalEndQuarterCamera();
                        flow.EndOfQuarterStarted = true;
                    }
                    if (flow.EndOfQuarterButtons == null)
                    {
                        if (host.Exists(0x0f10, -1) && --flow.MessageTimeout != 0) host.Sleep(1);
                        else
                        {
                            SourceOriginalEndQuarterMusic();
                            flow.EndOfQuarterButtons = new SourceAllButtonsWait(40, 4 * SourceTiming.DefaultTicksPerSecond);
                            host.Sleep(1);
                        }
                    }
                    else if (flow.EndOfQuarterButtons.Resume(flow.Switches, flow.Configuration))
                    {
                        flow.AcknowledgingStations = flow.EndOfQuarterButtons.AcknowledgingStations;
                        host.Kill(flow.EndOfQuarterSerial);
                    }
                    else host.Sleep(1);
                }
                finally { host.Complete(); }
            }
            CaptureSourceCabinetFlow();
            if (flow.TimerSerial == 0 || !host.Visit(flow.TimerSerial)) return;
            BeginSourceProcess("timedown", -1, 0);
            try
            {
                bool buttons = (flow.Switches & flow.Configuration.ButtonMasks.AllAbcdMask) != 0;
                // select.c:6359-6514. The free-play binding has no coin/start
                // register, credit changes, or participation changes to restart it.
                if (flow.TimerPhase == 3)
                {
                    if (--flow.Seconds < 0)
                    {
                        flow.Seconds = 0; flow.TimerDone = true;
                        host.Kill(flow.TimerSerial);
                        return;
                    }
                    flow.TimerPhase = 0;
                }
                if (flow.TimerPhase == 0)
                {
                    flow.NumberVisible = true;
                    host.Record("CabinetTimerDisplay", flow.TimerSerial, SourceCabinetFlowProcess.TimerId, flow.Seconds);
                    if (buttons)
                    {
                        flow.TimerPhase = 1;
                        host.Sleep(flow.CompletedQuarter.Value + 1 >= 4 && flow.Seconds >= 9 ? 60 : 3);
                        return;
                    }
                    flow.TimerPhase = 2; flow.ShowingTicks = SourceTiming.DefaultTicksPerSecond;
                }
                if (flow.TimerPhase == 2)
                {
                    if (buttons && !(flow.CompletedQuarter.Value + 1 >= 4
                        && flow.Configuration.ParticipatingStationMask == 0) || --flow.ShowingTicks == 0)
                        flow.TimerPhase = 1;
                    host.Sleep(1);
                    return;
                }
                flow.NumberVisible = false; flow.TimerPhase = 3;
                host.Record("CabinetTimerDelete", flow.TimerSerial, SourceCabinetFlowProcess.TimerId, flow.Seconds);
                host.Sleep(buttons ? 3 : 25);
            }
            finally { host.Complete(); CaptureSourceCabinetFlow(); }
        }
    }
}
