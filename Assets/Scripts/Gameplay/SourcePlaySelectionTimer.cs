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
            CaptureSourceSelectionTimer();
        }

        private bool SourceSelectionTimedOut
        {
            get { return sourceSelectionTimer != null && sourceSelectionTimer.ForceSelection; }
        }
    }
}
