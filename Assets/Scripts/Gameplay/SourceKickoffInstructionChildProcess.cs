using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    // plyrinfo.c:4516..4684 at the accepted source pin. Resources, helper primitives
    // and function-return retirement remain explicit, mandatory narrow bindings.
    internal sealed class SourceKickoffInstructionChildProcess : ISourceKickoffInstructionChildContinuation
    {
        private enum Stage { Entry, Waiting, Terminal, Faulted }
        private readonly SourceKickoffInstructionChildInvocation invocation;
        private readonly ISourceInstructionAdmissionState state;
        private readonly ISourceKickoffInstructionChildInputs inputs;
        private readonly SourceKickoffInstructionChildBinding binding;
        private readonly Action<int> createFlash;
        private readonly Action<SourceKickoffInstructionChildSound, Action> soundOutput;
        private Stage stage;
        private int slot;
        private float x, y;
        private ISourceKickoffActivePlayerWait wait;
        private ISourceKickoffInstructionChildEffects Effects { get { return binding.Effects; } }

        internal SourceKickoffInstructionChildProcess(SourceKickoffInstructionChildInvocation invocation,
            ISourceInstructionAdmissionState state, ISourceKickoffInstructionChildInputs inputs,
            SourceKickoffInstructionChildBinding binding, Action<int> createFlash,
            Action<SourceKickoffInstructionChildSound, Action> soundOutput = null)
        {
            this.invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
            this.createFlash = createFlash ?? throw new ArgumentNullException(nameof(createFlash));
            this.soundOutput = soundOutput;
        }

        public SourceKickoffInstructionChildStep Advance()
        {
            if (stage == Stage.Faulted || stage == Stage.Terminal)
                throw new InvalidOperationException("A terminal or failed source kickoff child cannot be replayed.");
            try
            {
                if (stage == Stage.Entry)
                {
                    if (binding.Operands.DbgKoDefined)
                        return Return(SourceKickoffInstructionChildReturnReason.DbgKoCompileTimeReturn);
                    slot = invocation.Argument0;
                    if (slot < 0 || slot > 3)
                        throw new ArgumentOutOfRangeException("slot", "The portable child supports the four observed table rows.");
                    x = Coordinate(inputs.ReadFourPlayerVersion(), binding.Operands.XComponentIndex);
                    y = Coordinate(inputs.ReadFourPlayerVersion(), binding.Operands.YComponentIndex);
                    if ((state.ReadPlayerStatus() & (1 << slot)) == 0)
                        return Die();
                    if (state.ReadGamesPlayed(slot) > 2)
                        return Die();

                    createFlash(slot);
                    Effects.SetTextZIncrement(0.0f);
                    // The native static pointer is used only in this uninterrupted
                    // setup. Cleanup below is by ID/mask, never by this handle.
                    ISourceInstructionPlate plate = Effects.BeginPlate("playresult", "SCR_PLATE_TID",
                        new SourceInstructionPoint(x, y, 1.4f));
                    if (plate == null)
                        throw new InvalidOperationException("The source kickoff child requires an actual beginobj return.");
                    Effects.SetPlateZAngle(plate, 256.04f);
                    Effects.SetPlateHScale(plate, 1.45f);
                    Effects.SetPlateObjectId(plate, "OID_IDIOTBOX", slot);
                    Effects.GeneratePlateVertices(plate);
                    Effects.SetTextFont(SourceKickoffInstructionChildFont.Bast10);
                    Effects.SetStringId(0x34 + slot);
                    Effects.SetTextColor(HeaderColor(inputs.ReadFourPlayerVersion()));
                    Effects.SetTextPosition(new SourceInstructionPoint(x - 5, y + 90, 1.0f));
                    Effects.Print(new SourceKickoffInstructionChildTextRequest(
                        SourceKickoffInstructionChildTextKind.Header, 1, 4583, slot + 1));

                    // The C body does not establish a callback ordering contract
                    // within equality operands. Both separate control reads remain live.
                    if (inputs.ReadBallCarrier() == inputs.ReadPlayerControl(slot))
                        Carrier();
                    else if (inputs.ReadPlayerTeam(inputs.ReadPlayerControl(slot)) == inputs.ReadOffensiveSide())
                        OffenseSupport();
                    else
                        Defense();

                    int tsec = inputs.ReadTsec();
                    if (tsec < 0 || tsec > int.MaxValue / 6)
                        throw new ArgumentOutOfRangeException("tsec", "The portable child requires nonnegative 6*tsec in Int32.");
                    wait = binding.Waits.Create(40, 6 * tsec, slot);
                    if (wait == null)
                        throw new InvalidOperationException("The source kickoff child requires an actual active-player wait.");
                    stage = Stage.Waiting;
                }
                SourceKickoffActivePlayerWaitStep step = wait.Advance();
                if (step.Kind == SourceKickoffActivePlayerWaitStepKind.Sleep)
                    return SourceKickoffInstructionChildStep.Sleep(step.SleepTicks);
                if (step.Kind != SourceKickoffActivePlayerWaitStepKind.Returned)
                    throw new InvalidOperationException("The active-player wait supplied no declared progress.");
                if (soundOutput == null) Effects.PlaySound(SourceKickoffInstructionChildSound.Ricochet);
                else soundOutput(SourceKickoffInstructionChildSound.Ricochet,
                    () => Effects.PlaySound(SourceKickoffInstructionChildSound.Ricochet));
                Effects.DeleteStrings(0x34 + slot, uint.MaxValue);
                Effects.DeletePlateClass("OID_IDIOTBOX", slot, uint.MaxValue);
                return Return(SourceKickoffInstructionChildReturnReason.NormalBodyEnd);
            }
            catch { stage = Stage.Faulted; throw; }
        }

        private SourceKickoffInstructionChildStep Die()
        {
            stage = Stage.Terminal;
            return SourceKickoffInstructionChildStep.NativeDie(0);
        }

        private SourceKickoffInstructionChildStep Return(SourceKickoffInstructionChildReturnReason reason)
        {
            stage = Stage.Terminal;
            return SourceKickoffInstructionChildStep.SourceFallthrough(reason);
        }

        private float Coordinate(int version, int component)
        {
            RequireVersion(version);
            if (component == 0)
            {
                if (version == 0)
                    return slot == 0 ? binding.Operands.BarX1 : slot == 1 ? binding.Operands.BarX2 : 0.0f;
                return 64.0f + 128.0f * slot;
            }
            return version == 1 && (slot == 1 || slot == 3) ? 180.0f : 150.0f;
        }

        private static void RequireVersion(int version)
        {
            if (version < 0 || version > 1)
                throw new ArgumentOutOfRangeException(nameof(version), "The portable child supports table versions zero and one, not Boolean normalization.");
        }

        private SourceKickoffInstructionChildColor HeaderColor(int version)
        {
            RequireVersion(version);
            if (slot == 0) return SourceKickoffInstructionChildColor.LightBlue;
            if (slot == 1) return version == 0
                ? SourceKickoffInstructionChildColor.LightRed : SourceKickoffInstructionChildColor.LightGreen;
            if (version == 0) return SourceKickoffInstructionChildColor.NativeZero;
            return slot == 2 ? SourceKickoffInstructionChildColor.LightRed : SourceKickoffInstructionChildColor.LightYellow;
        }

        private void Print(SourceKickoffInstructionChildTextKind kind, int ordinal, int nativeLine, int offset)
        {
            Effects.SetTextPosition(new SourceInstructionPoint(x, y + offset, 1.0f));
            Effects.Print(new SourceKickoffInstructionChildTextRequest(kind, ordinal, nativeLine));
        }

        private void PrintLow(SourceKickoffInstructionChildTextKind kind, int ordinal, int nativeLine,
            int offset, int lowDelta)
        {
            Print(kind, ordinal, nativeLine, offset + (inputs.ReadIsLowResolution() != 0 ? lowDelta : 0));
        }

        private void Carrier()
        {
            Effects.SetTextFont(SourceKickoffInstructionChildFont.Bast10);
            Effects.SetTextColor(SourceKickoffInstructionChildColor.LightCyan);
            Print(SourceKickoffInstructionChildTextKind.CarrierStatus, 1, 4591, 75);
            Effects.SetTextFont(SourceKickoffInstructionChildFont.Bast8T);
            Effects.SetTextColor(SourceKickoffInstructionChildColor.LightYellow);
            Print(SourceKickoffInstructionChildTextKind.SpeedAdvice, 1, 4596, 55);
            PrintLow(SourceKickoffInstructionChildTextKind.SpeedAdvice, 2, 4598, 43, -2);
            Print(SourceKickoffInstructionChildTextKind.SpinAdvice, 1, 4601, 23);
            PrintLow(SourceKickoffInstructionChildTextKind.SpinAdvice, 2, 4603, 11, -1);
            PrintLow(SourceKickoffInstructionChildTextKind.SpinAdvice, 3, 4605, -1, -3);
            Print(SourceKickoffInstructionChildTextKind.StiffArmAdvice, 1, 4608, -21);
            PrintLow(SourceKickoffInstructionChildTextKind.StiffArmAdvice, 2, 4610, -33, -1);
            Print(SourceKickoffInstructionChildTextKind.HurdleAdvice, 1, 4613, -53);
            PrintLow(SourceKickoffInstructionChildTextKind.HurdleAdvice, 2, 4615, -65, -2);
            PrintLow(SourceKickoffInstructionChildTextKind.HurdleAdvice, 3, 4617, -77, -3);
        }

        private void OffenseSupport()
        {
            Effects.SetTextFont(SourceKickoffInstructionChildFont.Bast8T);
            Effects.SetTextColor(SourceKickoffInstructionChildColor.LightYellow);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 1, 4627, 55, 1);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 2, 4629, 43, -1);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 3, 4631, 31, -2);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 4, 4633, 19, -3);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 5, 4635, 7, -4);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 6, 4637, -5, -6);
            PrintLow(SourceKickoffInstructionChildTextKind.BlockingAdvice, 7, 4639, -17, -7);
            PrintLow(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 1, 4642, -37, -6);
            PrintLow(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 2, 4644, -49, -7);
            PrintLow(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 3, 4646, -61, -8);
            PrintLow(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 4, 4648, -73, -10);
        }

        private void Defense()
        {
            Effects.SetTextFont(SourceKickoffInstructionChildFont.Bast8T);
            Effects.SetTextColor(SourceKickoffInstructionChildColor.LightYellow);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 1, 4656, 55, 1);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 2, 4658, 43, -1);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 3, 4660, 31, -2);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 4, 4662, 19, -3);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 5, 4664, 7, -5);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 6, 4666, -5, -6);
            PrintLow(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 7, 4668, -17, -8);
            PrintLow(SourceKickoffInstructionChildTextKind.DiveTackleAdvice, 1, 4671, -37, -7);
            PrintLow(SourceKickoffInstructionChildTextKind.DiveTackleAdvice, 2, 4673, -49, -9);
            PrintLow(SourceKickoffInstructionChildTextKind.DiveTackleAdvice, 3, 4675, -61, -10);
        }
    }

    internal sealed class SourceKickoffOwnedActivePlayerWaitFactory : ISourceKickoffActivePlayerWaitFactory
    {
        private readonly ISourceInstructionAdmissionState state;
        private readonly SourceKickoffInstructionOwnedHelpers helpers;
        internal SourceKickoffOwnedActivePlayerWaitFactory(ISourceInstructionAdmissionState state,
            SourceKickoffInstructionOwnedHelpers helpers)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }
        public ISourceKickoffActivePlayerWait Create(int minimumTicks, int timeoutTicks, int slot)
        { return new SourceKickoffOwnedActivePlayerWait(state, helpers, minimumTicks, timeoutTicks, slot); }
    }

    // attract.c:2054..2067: each Advance resumes the prior sleep, not a new poll.
    internal sealed class SourceKickoffOwnedActivePlayerWait : ISourceKickoffActivePlayerWait
    {
        private enum Stage { Entry, AfterSleep, Returned, Faulted }
        private readonly ISourceInstructionAdmissionState state;
        private readonly SourceKickoffInstructionOwnedHelpers helpers;
        private readonly int minimumTicks, timeoutTicks, slot;
        private int ticks;
        private bool done;
        private Stage stage;
        internal SourceKickoffOwnedActivePlayerWait(ISourceInstructionAdmissionState state,
            SourceKickoffInstructionOwnedHelpers helpers, int minimumTicks, int timeoutTicks, int slot)
        {
            if (minimumTicks < 0) throw new ArgumentOutOfRangeException(nameof(minimumTicks));
            if (timeoutTicks < 0) throw new ArgumentOutOfRangeException(nameof(timeoutTicks));
            if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
            this.minimumTicks = minimumTicks; this.timeoutTicks = timeoutTicks; this.slot = slot;
            ticks = 0; done = false;
        }
        public SourceKickoffActivePlayerWaitStep Advance()
        {
            if (stage == Stage.Returned || stage == Stage.Faulted)
                throw new InvalidOperationException("A returned or failed active-player wait cannot be replayed.");
            try
            {
                if (stage == Stage.Entry)
                {
                    stage = Stage.AfterSleep;
                    return SourceKickoffActivePlayerWaitStep.Sleep(1);
                }
                ticks++;
                if ((state.ReadPlayerStatus() & (1 << slot)) != 0
                    && ((helpers.Switches.ReadCurrentSwitchWord() >> (8 * slot))
                        & (helpers.Player1ButtonAMask | helpers.Player1ButtonBMask | helpers.Player1ButtonCMask)) != 0
                    && ticks >= minimumTicks)
                    done = true;
                if (ticks < timeoutTicks && !done)
                    return SourceKickoffActivePlayerWaitStep.Sleep(1);
                stage = Stage.Returned;
                return SourceKickoffActivePlayerWaitStep.Returned();
            }
            catch { stage = Stage.Faulted; throw; }
        }
    }

    internal sealed class SourceKickoffOwnedFlashFactory : ISourceKickoffFlashContinuationFactory
    {
        private readonly SourcePortableProcessHost host;
        private readonly SourceKickoffInstructionOwnedHelpers helpers;
        internal SourceKickoffOwnedFlashFactory(SourcePortableProcessHost host, SourceKickoffInstructionOwnedHelpers helpers)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }
        public ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionFlashInvocation invocation)
        { return new SourceKickoffOwnedFlashProcess(invocation, host, helpers); }
    }

    // plyrinfo.c:4804..4822. Capture player_blocks[args[0]], never player control.
    internal sealed class SourceKickoffOwnedFlashProcess : ISourceKickoffInstructionChildContinuation
    {
        private enum Stage { Entry, AfterWhiteSleep, AfterClearSleep, Returned, Faulted }
        private readonly SourceKickoffInstructionFlashInvocation invocation;
        private readonly SourcePortableProcessHost host;
        private readonly SourceKickoffInstructionOwnedHelpers helpers;
        private ISourceKickoffPlayerObject player;
        private int delay, existenceId;
        private Stage stage;
        internal SourceKickoffOwnedFlashProcess(SourceKickoffInstructionFlashInvocation invocation,
            SourcePortableProcessHost host, SourceKickoffInstructionOwnedHelpers helpers)
        {
            this.invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }
        public SourceKickoffInstructionChildStep Advance()
        {
            if (stage == Stage.Returned || stage == Stage.Faulted)
                throw new InvalidOperationException("A returned or failed source flash cannot be replayed.");
            try
            {
                if (host.Caller != invocation.Serial)
                    throw new InvalidOperationException("The owned flash requires its actual dispatcher/host wake.");
                if (stage == Stage.Entry)
                {
                    player = helpers.PlayerObjects.ReadPlayerBlock(invocation.Argument0);
                    if (player == null)
                        throw new InvalidOperationException("The source flash requires the actual captured player object.");
                    delay = invocation.Argument1;
                    existenceId = invocation.Argument2;
                    if (delay < 1 || delay > 0x7fff)
                        throw new ArgumentOutOfRangeException("delay", "The portable flash requires a positive host sleep operand.");
                }
                else if (stage == Stage.AfterWhiteSleep)
                {
                    player.WriteFlags(player.ReadFlags() & ~helpers.FlashWhiteMask);
                    stage = Stage.AfterClearSleep;
                    return SourceKickoffInstructionChildStep.Sleep(delay);
                }
                else
                {
                    host.Record("ExistsCall", invocation.Serial, existenceId, -1);
                    if (!host.Exists(existenceId, -1))
                    {
                        stage = Stage.Returned;
                        return SourceKickoffInstructionChildStep.SourceFallthrough(
                            SourceKickoffInstructionChildReturnReason.NormalBodyEnd);
                    }
                }
                player.WriteFlags(player.ReadFlags() | helpers.FlashWhiteMask);
                stage = Stage.AfterWhiteSleep;
                return SourceKickoffInstructionChildStep.Sleep(delay);
            }
            catch { stage = Stage.Faulted; throw; }
        }
    }
}
