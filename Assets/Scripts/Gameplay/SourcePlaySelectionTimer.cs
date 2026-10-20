using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    // timedown_play_select and its flash_text_proc child, on the shared process host.
    internal sealed class SourcePlaySelectionTimer
    {
        internal const int TimerId = 601, StringId = 80, MessageId = 0x0f10;
        private readonly SourcePortableProcessHost host;
        private readonly Func<long> nextSerial;
        private readonly Action<string> beginProcess;
        private readonly Action<SourceSelectionTimerSound> sound;
        private long flasher;
        private int stage, flashStage;
        private bool visible;
        private uint color = 0xffff0000u;
        internal long Serial { get; }
        internal int Number { get; private set; }
        internal bool ForceSelection { get; private set; }
        internal long ForceTick { get; private set; } = -1;

        internal SourcePlaySelectionTimer(SourcePortableProcessHost host, int initial,
            Func<long> nextSerial, Action<string> beginProcess, Action<SourceSelectionTimerSound> sound)
        {
            this.host = host; this.nextSerial = nextSerial;
            this.beginProcess = beginProcess; this.sound = sound;
            Number = initial; Serial = nextSerial();
            host.Create(Serial, TimerId, "timedown_play_select", 0);
        }

        internal SourceSelectionTimerView Capture(bool homeSelected, bool awaySelected)
        {
            return new SourceSelectionTimerView(Serial, Number, visible, color, ForceSelection,
                ForceTick, homeSelected, awaySelected);
        }

        internal void Dispatch(Func<int> instructionValue)
        {
            // A qcreate child is born sleep1; it cannot run inline in its parent's wake.
            if (flasher != 0 && host.Visit(flasher))
            {
                beginProcess("flash_text_proc");
                try
                {
                    if (flashStage != 0)
                    {
                        color = flashStage == 1 ? 0xffffffffu : 0xffff0000u;
                        host.Record("TimerColor", flasher, StringId, unchecked((int)color));
                    }
                    flashStage = flashStage == 1 ? 2 : 1;
                    host.Sleep(7);
                }
                finally { host.Complete(); }
            }
            if (!host.Visit(Serial)) return;
            beginProcess("timedown_play_select");
            try
            {
                if (stage == 2)
                {
                    DeleteNumber();
                    Number--;
                    if (Number < 0)
                    {
                        ForceSelection = true; ForceTick = host.SourceTick;
                        host.Record("TimerForce", Serial, TimerId, 1);
                        host.Kill(Serial);
                        return;
                    }
                    stage = 0;
                }
                if (stage == 0)
                {
                    visible = true; color = 0xffff0000u;
                    host.Record("TimerDisplay", Serial, StringId, Number);
                    flasher = nextSerial(); flashStage = 0;
                    host.Create(flasher, MessageId, "flash_text_proc", 0);
                    host.Record("TimerBeep", Serial, 36, Number);
                    sound(new SourceSelectionTimerSound(host.SourceTick, Serial, Number));
                    stage = 1;
                }
                if (instructionValue() >= 9)
                {
                    host.Sleep(1);
                    return;
                }
                stage = 2;
                host.Sleep(SourceTiming.DefaultTicksPerSecond + (Number == 0 ? 25 : 0));
            }
            finally { host.Complete(); }
        }

        private void DeleteNumber()
        {
            host.Record("TimerDeleteStrings", Serial, StringId, -1);
            visible = false;
            if (flasher != 0) { host.Kill(flasher); flasher = 0; }
        }

        internal void Stop()
        {
            host.Kill(Serial);
            DeleteNumber();
        }
    }

    public sealed partial class GameSimulation
    {
        private sealed class RandomSelection
        {
            internal int Counter = 1, SleepRemaining = 1, Pages, Flips;
            internal bool ExtraSleepPending = true;
        }

        private readonly RandomSelection[] sourceRandomSelections = new RandomSelection[2];
        private long sourceRandomSelectionFinishTick;
        private bool HasSourceRandomSelection
        { get { return sourceRandomSelections[0] != null || sourceRandomSelections[1] != null; } }
        private SourcePlaySelectionTimer sourceSelectionTimer;
        private bool sourceSelectionFinishPending;
        private bool sourceSelectionCpuPatPending;
        private TeamId sourceSelectionFinishSide;
        private readonly System.Collections.Generic.List<SourceSelectionTimerSound> sourceSelectionSounds =
            new System.Collections.Generic.List<SourceSelectionTimerSound>();

        // Input to the original predicate only. The portable menu has no native tutorial producer.
        public Func<int> SourcePlaySelectionInstructionValue { get; set; }
        public event Action<SourceSelectionTimerSound> SourceSelectionTimerSoundRaised;
        public System.Collections.ObjectModel.ReadOnlyCollection<SourceSelectionTimerSound> SourceSelectionTimerSounds
        { get { return Array.AsReadOnly(sourceSelectionSounds.ToArray()); } }
        public System.Collections.ObjectModel.ReadOnlyCollection<SourceSelectionTimerSound> TakeSourceSelectionTimerSounds()
        {
            var result = SourceSelectionTimerSounds;
            sourceSelectionSounds.Clear();
            return result;
        }

        private void StartSourceSelectionTimer()
        {
            StopSourceSelectionTimer();
            sourceSelectionTimer = new SourcePlaySelectionTimer(Model.SourceProcesses,
                SourceMatchFlow.PlaySelectionCountdownStart(Model.Quarter - 1), () => ++sourceFireNextSerial,
                function => BeginSourceProcess(function, -1, 0), call =>
                {
                    sourceSelectionSounds.Add(call);
                    try
                    {
                        Action<SourceSelectionTimerSound> handler = SourceSelectionTimerSoundRaised;
                        if (handler != null) handler(call);
                    }
                    catch { sourceSoundVmFailed = true; throw; }
                });
            sourceSelectionFinishSide = TeamId.Home;
            CaptureSourceSelectionTimer();
        }

        private void CaptureSourceSelectionTimer()
        {
            Model.SourceSelectionTimer = sourceSelectionTimer == null ? null :
                sourceSelectionTimer.Capture(Model.SourcePlaySelectionReady[0], Model.SourcePlaySelectionReady[1]);
            Model.SourceSelectionCountdown = sourceSelectionTimer == null ? -1 : sourceSelectionTimer.Number;
        }

        private void StepSourceSelectionTimer()
        {
            if (sourceSelectionTimer == null) return;
            sourceSelectionTimer.Dispatch(() => SourcePlaySelectionInstructionValue == null ? 0 : SourcePlaySelectionInstructionValue());
            CaptureSourceSelectionTimer();
        }

        private void StopSourceSelectionTimer()
        {
            if (sourceSelectionTimer != null) sourceSelectionTimer.Stop();
            sourceSelectionTimer = null; sourceSelectionFinishPending = false; sourceSelectionCpuPatPending = false;
            ResetSourceRandomSelections();
            CaptureSourceSelectionTimer();
        }

        private void ResetSourceRandomSelections()
        {
            Array.Clear(sourceRandomSelections, 0, sourceRandomSelections.Length);
            Array.Clear(Model.SourceRandomSelections, 0, Model.SourceRandomSelections.Length);
            sourceRandomSelectionFinishTick = -1;
        }

        private void StartSourceRandomSelection(TeamId side, int station)
        {
            int first = -1, second = -1;
            foreach (SourceStationConfiguration configured in sourceConfiguration.Stations)
                if (configured.Team == side)
                {
                    if (first < 0) first = configured.StationId;
                    else second = configured.StationId;
                }
            int captain = second < 0 || Model.SourceCaptain == SourceCaptainSlot.First ? first : second;
            int index = (int)side;
            if (Model.SourceProcesses.Caller != GameProcess || station != captain ||
                Model.Phase != GamePhase.PlaySelect || Model.SourcePlaySelectionReady[index] ||
                sourceRandomSelections[index] != null)
                throw new InvalidOperationException("Random selection must start once in the actual offensive/defensive captain's menu consumer.");
            sourceRandomSelections[index] = new RandomSelection();
            Model.SourceRandomSelections[index] = new SourceRandomSelectionView(Model.SourceTick, false, 0,
                SourceRandomSelectionAction.None);
        }

        private void AdvanceSourceRandomSelections()
        {
            if (Model.Phase != GamePhase.PlaySelect) return;
            if (Model.SourceProcesses.Caller != GameProcess)
                throw new InvalidOperationException("Random selection draws require the actual admitted game_proc menu consumer.");
            // playsel.c qcreates side0 then side1; equal-level children are inserted at the head.
            for (int side = 1; side >= 0; side--)
            {
                RandomSelection controller = sourceRandomSelections[side];
                if (controller == null || Model.SourcePlaySelectionReady[side]) continue;
                Model.SourceRandomSelections[side] = new SourceRandomSelectionView(Model.SourceTick, false, 0,
                    SourceRandomSelectionAction.None);
                if (--controller.SleepRemaining != 0) continue;
                // The sleep1 continuation tests select_random BEFORE entering the separate sleep2.
                if (controller.ExtraSleepPending)
                {
                    controller.ExtraSleepPending = false; controller.SleepRemaining = 2;
                    continue;
                }
                if (SourceSelectionTimedOut && sourceSelectionTimer.ForceTick < Model.SourceTick)
                {
                    Model.SourceRandomSelections[side] = new SourceRandomSelectionView(Model.SourceTick, true, 0,
                        SourceRandomSelectionAction.None);
                    continue;
                }
                controller.Counter++;
                int direction = SourceDraw(4, "playsel.c:play_select_cntrl:1763/side" + side);
                int joy = direction == 0 ? 1 : direction == 1 ? 8 : direction == 2 ? 4 : 2;
                controller.Counter++;
                bool offense = side == (int)Model.Possession;
                int button = SourceDraw(2, "playsel.c:play_select_cntrl:" + (offense ? "1898" : "1916") + "/side" + side);
                SourceRandomSelectionAction action = SourceRandomSelectionAction.None;
                if (offense && button == 1)
                {
                    if (controller.Flips < 4) { controller.Flips++; action = SourceRandomSelectionAction.Flip; }
                }
                else if ((offense && button == 0 || !offense && button == 1) && controller.Pages < 6)
                {
                    controller.Pages++; action = SourceRandomSelectionAction.ChangePage;
                }
                // The final iteration still consumes BOTH draws before overriding the button with ChoosePlay.
                if (controller.Counter > 30) action = SourceRandomSelectionAction.Choose;
                Model.SourceRandomSelections[side] = new SourceRandomSelectionView(Model.SourceTick, true, joy, action);
                controller.SleepRemaining = 1; controller.ExtraSleepPending = true;
            }
        }

        private void ObserveSourceRandomSelectionCaller()
        {
            if (!HasSourceRandomSelection || !SourceSelectionTimedOut || sourceSelectionFinishPending) return;
            sourceSelectionFinishPending = true;
            // play_select observes force after the controllers, then yields one final sleep1.
            sourceRandomSelectionFinishTick = Model.SourceTick + 1;
        }

        private bool SourceSelectionTimedOut
        {
            get { return sourceSelectionTimer != null && sourceSelectionTimer.ForceSelection; }
        }
    }
}
