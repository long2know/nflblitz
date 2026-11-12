using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    // playsel.c:1055..1211 is an inline play_select/game_proc continuation.
    // No new native process ID, stack layout or presentation-clock wait is used.
    internal sealed class SourcePlaySelectionIntroductionProcess
    {
        private readonly SourcePortableProcessHost host;
        private readonly long caller;
        private readonly ISourceInstructionAdmissionState state;
        private readonly ISourcePlaySelectionIntroductionInputs inputs;
        private readonly ISourcePlaySelectionIntroductionEffects effects;
        private readonly Action<SourcePlaySelectionIntroductionSound, Action> sound;
        private readonly int allAbcdMask;
        private readonly uint player1AbcMask;
        private ISourceInstructionFlasher flasher;
        private SourceAnyButtonWait anyButtonWait;
        private SourcePlaySelectionIntroductionStage stage;
        private int gate, timeout, waitTicks;
        private int? offense;
        private bool activeWaitEntered;
        internal long EntrySourceTick { get; }
        internal bool Completed
        { get { return stage == SourcePlaySelectionIntroductionStage.Returned || stage == SourcePlaySelectionIntroductionStage.Skipped; } }
        internal bool Active { get { return !Completed && stage != SourcePlaySelectionIntroductionStage.Faulted; } }
        internal bool Faulted { get { return stage == SourcePlaySelectionIntroductionStage.Faulted; } }
        internal SourcePlaySelectionIntroductionSnapshot Snapshot
        {
            get
            {
                return new SourcePlaySelectionIntroductionSnapshot(host.Require(caller), stage, EntrySourceTick,
                    gate, offense, waitTicks, anyButtonWait == null ? null : anyButtonWait.Snapshot);
            }
        }

        internal SourcePlaySelectionIntroductionProcess(SourcePortableProcessHost host, long caller,
            SourceInstructionAdmissionBinding admission, ISourcePlaySelectionIntroductionInputs inputs,
            ISourcePlaySelectionIntroductionEffects effects, uint player1AbcMask,
            Action<SourcePlaySelectionIntroductionSound, Action> sound)
        {
            this.host = host; this.caller = caller; state = admission.State;
            this.inputs = inputs; this.effects = effects; this.player1AbcMask = player1AbcMask;
            allAbcdMask = admission.Invocation.AllAbcdMask; this.sound = sound;
            EntrySourceTick = host.SourceTick;
        }

        internal void Advance()
        {
            if (Completed || Faulted || host.Caller != caller)
                throw new InvalidOperationException("The introductory card resumes only in its actual live game_proc continuation.");
            try
            {
                if (stage == SourcePlaySelectionIntroductionStage.Setup)
                {
                    gate = inputs.ReadShowPlyselIdiotBox();
                    bool inexperienced = false;
                    if (gate != 0)
                        for (int slot = 0; slot < 4; slot++)
                            if (state.ReadGamesPlayed(slot) < 3 && (state.ReadPlayerStatus() & (1 << slot)) != 0)
                            { inexperienced = true; break; }
                    if (!inexperienced)
                    {
                        WriteGate(0);
                        stage = SourcePlaySelectionIntroductionStage.Skipped;
                        host.Record("PlaySelectionIntroductionSkipped", caller, 0, 0);
                        return;
                    }
                    WriteGate(checked(gate + 10));
                    Setup();
                    int side = offense.Value;
                    if (inputs.ReadFourPlayerVersion() != 0)
                    {
                        if ((state.ReadPlayerStatus() & (1 << (side * 2))) == 0
                            && (state.ReadPlayerStatus() & (1 << (side * 2 + 1))) == 0)
                        { Finish(); return; }
                        stage = SourcePlaySelectionIntroductionStage.WaitingActiveTeam;
                    }
                    else if ((state.ReadPlayerStatus() & (side + 1)) != 0)
                        stage = SourcePlaySelectionIntroductionStage.WaitingActiveTeam;
                    else
                        stage = SourcePlaySelectionIntroductionStage.WaitingAnyButton;
                    int tsec = inputs.ReadTsec();
                    if (tsec < 0 || tsec > int.MaxValue / 11)
                        throw new ArgumentOutOfRangeException("tsec", "The source intro requires nonnegative 11*tsec in Int32.");
                    timeout = 11 * tsec;
                    if (stage == SourcePlaySelectionIntroductionStage.WaitingAnyButton)
                        anyButtonWait = new SourceAnyButtonWait(40, timeout, allAbcdMask);
                }
                if (stage == SourcePlaySelectionIntroductionStage.WaitingActiveTeam)
                {
                    // attract.c:2018..2051 sleeps BEFORE every test, including
                    // its first test and a max==0 invocation. The minimum is
                    // inside the total timeout, unlike wait_for_any_butn_press.
                    if (!activeWaitEntered)
                    { activeWaitEntered = true; host.Sleep(1); return; }
                    waitTicks++;
                    bool done = false;
                    if (offense.Value == 0)
                    {
                        if (ActiveButton(0)) done = true;
                        if (inputs.ReadFourPlayerVersion() != 0 && ActiveButton(1)) done = true;
                    }
                    else if (inputs.ReadFourPlayerVersion() != 0)
                    {
                        if (ActiveButton(2)) done = true;
                        if (ActiveButton(3)) done = true;
                    }
                    else if (ActiveButton(1)) done = true;
                    if (waitTicks < timeout && !done) { host.Sleep(1); return; }
                }
                else
                {
                    if (!anyButtonWait.Advance(inputs,
                        () => sound(SourcePlaySelectionIntroductionSound.Tick,
                            () => effects.PlaySound(SourcePlaySelectionIntroductionSound.Tick)), host.Sleep))
                        return;
                }
                Finish();
            }
            catch { stage = SourcePlaySelectionIntroductionStage.Faulted; throw; }
        }

        private bool ActiveButton(int station)
        {
            return (state.ReadPlayerStatus() & (1 << station)) != 0
                && ((uint)inputs.ReadPlayerSwitches() & (player1AbcMask << (station * 8))) != 0
                && waitTicks >= 40;
        }
        private void WriteGate(int value) { inputs.WriteShowPlyselIdiotBox(value); gate = value; }
        private void Finish()
        {
            sound(SourcePlaySelectionIntroductionSound.Ricochet,
                () => effects.PlaySound(SourcePlaySelectionIntroductionSound.Ricochet));
            effects.KillTextFlasher(flasher, 0);
            effects.DeleteStrings(0x34, uint.MaxValue);
            effects.DeletePlateClass("OID_MESSAGE", uint.MaxValue);
            WriteGate(checked(inputs.ReadShowPlyselIdiotBox() - 11));
            stage = SourcePlaySelectionIntroductionStage.Returned;
            host.Record("PlaySelectionIntroductionReturn", caller, 0, gate);
        }
        private void Plate(SourceInstructionPoint position, float width, float height)
        {
            ISourceInstructionPlate plate = effects.BeginPlate("playresult", "SCR_PLATE_TID", position);
            if (plate == null) throw new InvalidOperationException("The source introductory card requires its actual beginobj return.");
            effects.SetPlateWidthScale(plate, width);
            effects.SetPlateHeightScale(plate, height);
            effects.SetPlateId(plate, "OID_MESSAGE");
            effects.GeneratePlateVertices(plate);
        }
        private ISourceInstructionText Print(int line, string text, SourceInstructionPoint position,
            SourcePlaySelectionIntroductionColor? color)
        {
            effects.SetTextPosition(position);
            return PrintCurrent(line, text, color);
        }
        private ISourceInstructionText PrintCurrent(int line, string text, SourcePlaySelectionIntroductionColor? color)
        {
            ISourceInstructionText result = effects.Print(new SourcePlaySelectionIntroductionPrint(line,
                "\u001b%dj" + (color.HasValue ? "\u001b%dc" : "") + text, color));
            if (result == null) throw new InvalidOperationException("The introductory print requires its actual returned text handle.");
            return result;
        }
        private void HeadingFont()
        { effects.SetTextFont(inputs.ReadIsLowResolution() != 0 ? SourceInstructionFont.Bast23 : SourceInstructionFont.Bast18); }
        private void Setup()
        {
            SourceInstructionRaster raster = inputs.ReadInstructionRaster();
            if (raster.Width <= 0 || raster.Height <= 0)
                throw new InvalidOperationException("The introductory card requires the admitted source raster.");
            float x = raster.HalfWidth, y = raster.HalfHeight;
            Plate(new SourceInstructionPoint(x, y - 6, 1.4f), 1.52f, 1.9f);
            effects.SetStringId(0x34);
            HeadingFont();
            Print(1075, "GENERAL INFO", new SourceInstructionPoint(x, y + 38, 1.1f), SourcePlaySelectionIntroductionColor.LightYellow);
            effects.SetTextFont(inputs.ReadIsLowResolution() != 0 ? SourceInstructionFont.Bast10Low : SourceInstructionFont.Bast10);
            Print(1081, "HIDE CURSOR BY QUICKLY TAPPING",
                new SourceInstructionPoint(x, y + 8 + (inputs.ReadIsLowResolution() != 0 ? 3f : 0f), 1.1f),
                SourcePlaySelectionIntroductionColor.LightYellow);
            Print(1083, "UP TWICE USING JOYSTICK", new SourceInstructionPoint(x, y - 4, 1.1f), SourcePlaySelectionIntroductionColor.LightYellow);
            Print(1086, "BLUE BUTTON CHOOSES PLAY", new SourceInstructionPoint(x, y - 22, 1.1f), SourcePlaySelectionIntroductionColor.LightBlue);
            Print(1088, "RED BUTTON FLIPS PLAY", new SourceInstructionPoint(x, y - 40, 1.0f), SourcePlaySelectionIntroductionColor.LightRed);
            Print(1090, "WHITE BUTTON CHANGES PAGE", new SourceInstructionPoint(x, y - 58, 1.1f), SourcePlaySelectionIntroductionColor.White);
            effects.SetTextZIncrement(0);
            int side = inputs.ReadOffensiveSide();
            if (side < 0 || side > 1) throw new ArgumentOutOfRangeException("offensiveSide");
            offense = side;
            effects.DeleteStrings(side == 0 ? 0x77 : 0x78, uint.MaxValue);
            effects.SetTextFont(SourceInstructionFont.Bast10);
            Print(1099, "PAGE 1", new SourceInstructionPoint(side == 0 ? 170 : 348, 305, 1.8f), SourcePlaySelectionIntroductionColor.LightYellow);
            effects.DeleteStrings(side == 0 ? 0x78 : 0x77, uint.MaxValue);
            effects.SetTextFont(SourceInstructionFont.Bast10);
            Print(1104, "PAGE 1", new SourceInstructionPoint(side == 0 ? 348 : 170, 305, 1.8f), SourcePlaySelectionIntroductionColor.LightYellow);
            float offenseX = side == 0 ? 128 : 384, defenseX = side == 0 ? 384 : 128;
            Plate(new SourceInstructionPoint(offenseX, 60, 1.4f), 0.60f, 0.60f);
            HeadingFont();
            effects.SetStringId(0x34);
            effects.SetTextPosition(new SourceInstructionPoint(offenseX, 60, 1.1f));
            effects.SetTextColor(SourcePlaySelectionIntroductionColor.LightRed);
            ISourceInstructionText offenseText = PrintCurrent(side == 0 ? 1162 : 1124, "OFFENSE", null);
            Plate(new SourceInstructionPoint(defenseX, 60, 1.4f), 0.60f, 0.60f);
            HeadingFont();
            effects.SetStringId(0x34);
            effects.SetTextPosition(new SourceInstructionPoint(defenseX, 60, 1.1f));
            effects.SetTextColor(SourcePlaySelectionIntroductionColor.LightGreen);
            ISourceInstructionText defenseText = PrintCurrent(side == 0 ? 1180 : 1143, "DEFENSE", null);
            flasher = effects.CreateTextFlasher("pflash", 0, offenseText, defenseText, 0, 0);
            if (flasher == null) throw new InvalidOperationException("The introductory card requires the actual text-flasher return.");
        }
    }

    public sealed partial class GameSimulation
    {
        private ISourcePlaySelectionIntroductionInputs sourceIntroductionInputs;
        private ISourcePlaySelectionIntroductionEffects sourceIntroductionEffects;
        private SourcePlaySelectionIntroductionProcess sourceIntroduction;
        private SourcePlaySelectionIntroductionSound executingIntroductionSound;
        private SourceProcessSoundCall executingIntroductionSoundOutput;
        private bool introductionSoundObserved;
        public SourcePlaySelectionIntroductionSnapshot SourcePlaySelectionIntroduction
        { get { return sourceIntroduction == null ? null : sourceIntroduction.Snapshot; } }
        private bool SourcePlaySelectionIntroductionActive
        { get { return sourceIntroduction != null && sourceIntroduction.Active; } }

        public void BindSourcePlaySelectionIntroduction(ISourcePlaySelectionIntroductionInputs inputs,
            ISourcePlaySelectionIntroductionEffects effects)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            if (!Model.SourceMode || Model.SourceTick != 0 || Model.Tick != 0 || Model.SourceAnimationFailure != null
                || sourceInstructionAdmission == null || sourceIntroductionInputs != null
                || !ReferenceEquals(inputs, sourceInstructionAdmission.Invocation.Inputs))
                throw new InvalidOperationException("Bind the introductory continuation once before wake1 using the same actual instruction input owner.");
            if (sourceInstructionAdmission.KickoffCoordinator == null
                || sourceInstructionAdmission.KickoffCoordinator.SourceChild == null
                || sourceInstructionAdmission.KickoffCoordinator.SourceChild.OwnedHelpers == null)
                throw new NotSupportedException("The introductory active-team wait requires the existing explicitly bound source ABC masks.");
            sourceIntroductionInputs = inputs; sourceIntroductionEffects = effects;
            SourcePlaySelectionInstructionValue = inputs.ReadShowPlyselIdiotBox;
        }

        private void ResetSourcePlaySelectionIntroduction()
        {
            sourceIntroduction = null;
            executingIntroductionSound = null; executingIntroductionSoundOutput = null;
            introductionSoundObserved = false;
            if (sourceIntroductionInputs != null)
                SourcePlaySelectionInstructionValue = sourceIntroductionInputs.ReadShowPlyselIdiotBox;
        }

        private void BeginSourcePlaySelectionIntroduction()
        {
            if (sourceIntroductionInputs == null) return;
            try
            {
                if (Model.SourceProcesses.Caller != GameProcess || sourceIntroduction != null && !sourceIntroduction.Completed)
                    throw new InvalidOperationException("Begin the introductory card only at the actual new play_select caller.");
                SourceKickoffInstructionOwnedHelpers masks = sourceInstructionAdmission.KickoffCoordinator.SourceChild.OwnedHelpers;
                sourceIntroduction = new SourcePlaySelectionIntroductionProcess(Model.SourceProcesses, GameProcess,
                    sourceInstructionAdmission, sourceIntroductionInputs, sourceIntroductionEffects,
                    masks.Player1ButtonAMask | masks.Player1ButtonBMask | masks.Player1ButtonCMask,
                    ExecuteSourcePlaySelectionIntroductionSound);
                sourceIntroduction.Advance();
            }
            catch (Exception error) { FailSourceInstructionAdmission(error); throw; }
        }

        private bool AdvanceSourcePlaySelectionIntroduction()
        {
            if (sourceIntroduction == null || sourceIntroduction.Completed) return false;
            try { sourceIntroduction.Advance(); return true; }
            catch (Exception error) { FailSourceInstructionAdmission(error); throw; }
        }

        private void ExecuteSourcePlaySelectionIntroductionSound(SourcePlaySelectionIntroductionSound sound, Action effect)
        {
            RequireExecutingSourceSoundProcess();
            if (executingIntroductionSound != null || Model.SourceProcesses.Caller != GameProcess
                || !SourcePlaySelectionIntroductionActive)
                throw new InvalidOperationException("Introduction sound requires its actual executing game_proc continuation.");
            executingIntroductionSound = sound;
            try
            {
                if (ReferenceEquals(sound, SourcePlaySelectionIntroductionSound.Ricochet))
                    executingIntroductionSoundOutput = EmitSourceProcessSound(SourceProcessSoundKind.PlaySelectionIntroduction,
                        "RICOCHET_SND", Model.SourceCurrentPlayerBank, 148, 115, 127, 1);
                else if (ReferenceEquals(sound, SourcePlaySelectionIntroductionSound.Tick))
                    executingIntroductionSoundOutput = EmitSourceProcessSound(SourceProcessSoundKind.InstructionTick,
                        "tick_sndn", "cursors", 17, 115, 127, 1);
                else throw new NotSupportedException("Unknown source introductory sound operand.");
                effect();
            }
            finally
            {
                executingIntroductionSound = null; executingIntroductionSoundOutput = null;
                introductionSoundObserved = false;
            }
        }

        public SourceProcessSoundCall TakeExecutingSourcePlaySelectionIntroductionSound(SourcePlaySelectionIntroductionSound sound)
        {
            RequireExecutingSourceSoundProcess();
            if (sound == null || !ReferenceEquals(sound, executingIntroductionSound)
                || Model.SourceProcesses.Caller != GameProcess || !SourcePlaySelectionIntroductionActive
                || executingIntroductionSoundOutput == null || introductionSoundObserved
                || executingIntroductionSoundOutput.SourceTick != Model.SourceTick)
                throw new InvalidOperationException("An introductory sound can expose its exact stamped output once, inside its live typed callback only.");
            introductionSoundObserved = true;
            return executingIntroductionSoundOutput;
        }
    }
}
