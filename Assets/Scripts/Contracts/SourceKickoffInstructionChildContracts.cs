using System;

namespace Blitz.Contracts
{
    public enum SourceKickoffInstructionChildFont { Bast10, Bast8T }
    public enum SourceKickoffInstructionChildColor
    { NativeZero, LightBlue, LightRed, LightGreen, LightYellow, LightCyan }
    public enum SourceKickoffInstructionChildTextKind
    {
        Header, CarrierStatus, SpeedAdvice, SpinAdvice, StiffArmAdvice, HurdleAdvice,
        BlockingAdvice, LateralPositionAdvice, DefenderSelectionAdvice, DiveTackleAdvice
    }

    // Semantic format slots, not copied native strings or guessed resource values.
    public readonly struct SourceKickoffInstructionChildTextRequest
    {
        public SourceKickoffInstructionChildTextKind Kind { get; }
        public int Ordinal { get; }
        public int NativePrintLine { get; }
        public int? DisplayPlayerNumber { get; }
        public string NativeSecondArgumentSymbols { get { return "HJUST_CENTER|VJUST_CENTER"; } }
        internal SourceKickoffInstructionChildTextRequest(SourceKickoffInstructionChildTextKind kind,
            int ordinal, int nativePrintLine, int? displayPlayerNumber = null)
        {
            Kind = kind; Ordinal = ordinal; NativePrintLine = nativePrintLine;
            DisplayPlayerNumber = displayPlayerNumber;
        }
    }

    public sealed class SourceKickoffInstructionChildSound
    {
        public string BankSymbol { get { return "plyr_bnk_str"; } }
        public string SoundSymbol { get { return "RICOCHET_SND"; } }
        public string VolumeSymbol { get { return "VOLUME1"; } }
        public int Pan { get { return 127; } }
        public string PrioritySymbol { get { return "PRIORITY_LVL4"; } }
        private SourceKickoffInstructionChildSound() { }
        public static readonly SourceKickoffInstructionChildSound Ricochet =
            new SourceKickoffInstructionChildSound();
    }

    public sealed class SourceKickoffInstructionChildOperands
    {
        public bool DbgKoDefined { get; }
        public float BarX1 { get; }
        public float BarX2 { get; }
        public int XComponentIndex { get; }
        public int YComponentIndex { get; }
        public SourceKickoffInstructionChildOperands(bool dbgKoDefined, float barX1, float barX2,
            int xComponentIndex, int yComponentIndex)
        {
            // These are portable binding constraints, not extra native guards.
            if (!SourceInstructionRaster.Finite(barX1)) throw new ArgumentOutOfRangeException(nameof(barX1));
            if (!SourceInstructionRaster.Finite(barX2)) throw new ArgumentOutOfRangeException(nameof(barX2));
            if (xComponentIndex < 0 || xComponentIndex > 1)
                throw new ArgumentOutOfRangeException(nameof(xComponentIndex));
            if (yComponentIndex < 0 || yComponentIndex > 1)
                throw new ArgumentOutOfRangeException(nameof(yComponentIndex));
            DbgKoDefined = dbgKoDefined; BarX1 = barX1; BarX2 = barX2;
            XComponentIndex = xComponentIndex; YComponentIndex = yComponentIndex;
        }
    }

    public interface ISourceKickoffInstructionChildInputs : ISourceKickoffInstructionCoordinatorInputs
    {
        int ReadBallCarrier();
        int ReadPlayerTeam(int nativeControlIndex);
        int ReadOffensiveSide();
        int ReadIsLowResolution();
        int ReadTsec();
    }

    // The caller owns every operation and its order. The service resolves only
    // the indicated native resource/effect and shares text state across children.
    public interface ISourceKickoffInstructionChildEffects
    {
        void SetTextZIncrement(float value);
        ISourceInstructionPlate BeginPlate(string imageSymbol, string textureSymbol, SourceInstructionPoint point);
        void SetPlateZAngle(ISourceInstructionPlate plate, float value);
        void SetPlateHScale(ISourceInstructionPlate plate, float value);
        void SetPlateObjectId(ISourceInstructionPlate plate, string baseIdSymbol, int slotOffset);
        void GeneratePlateVertices(ISourceInstructionPlate plate);
        void SetTextFont(SourceKickoffInstructionChildFont font);
        void SetStringId(int id);
        void SetTextColor(SourceKickoffInstructionChildColor color);
        void SetTextPosition(SourceInstructionPoint point);
        void Print(SourceKickoffInstructionChildTextRequest request);
        void PlaySound(SourceKickoffInstructionChildSound sound);
        void DeleteStrings(int id, uint mask);
        void DeletePlateClass(string baseIdSymbol, int slotOffset, uint mask);
    }

    public enum SourceKickoffActivePlayerWaitStepKind { Sleep = 1, Returned = 2 }

    public readonly struct SourceKickoffActivePlayerWaitStep
    {
        public SourceKickoffActivePlayerWaitStepKind Kind { get; }
        public int SleepTicks { get; }
        private SourceKickoffActivePlayerWaitStep(SourceKickoffActivePlayerWaitStepKind kind, int ticks)
        { Kind = kind; SleepTicks = ticks; }
        public static SourceKickoffActivePlayerWaitStep Sleep(int ticks)
        {
            if (ticks < 1 || ticks > 0x7fff) throw new ArgumentOutOfRangeException(nameof(ticks));
            return new SourceKickoffActivePlayerWaitStep(SourceKickoffActivePlayerWaitStepKind.Sleep, ticks);
        }
        public static SourceKickoffActivePlayerWaitStep Returned()
        { return new SourceKickoffActivePlayerWaitStep(SourceKickoffActivePlayerWaitStepKind.Returned, 0); }
    }

    public interface ISourceKickoffActivePlayerWait
    {
        SourceKickoffActivePlayerWaitStep Advance();
    }

    public interface ISourceKickoffActivePlayerWaitFactory
    {
        ISourceKickoffActivePlayerWait Create(int minimumTicks, int timeoutTicks, int slot);
    }

    public sealed class SourceKickoffInstructionFlashInvocation
    {
        public long Serial { get; }
        public int ProcessId { get { return 0; } }
        public string Name { get { return "flash"; } }
        public string Function { get { return "flash_plyr_proc2"; } }
        public int Argument0 { get; }
        public int Argument1 { get { return 8; } }
        public int Argument2 { get { return 22; } }
        public int Argument3 { get { return 0; } }
        internal SourceKickoffInstructionFlashInvocation(long serial, int slot)
        { Serial = serial; Argument0 = slot; }
    }

    public interface ISourceKickoffFlashContinuationFactory
    {
        ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionFlashInvocation invocation);
    }

    // UInt32 words and four byte-selected players are portable binding constraints,
    // not claims about the native switch/flags ABI or numeric symbol definitions.
    public interface ISourceKickoffCurrentSwitches
    {
        uint ReadCurrentSwitchWord();
    }

    public interface ISourceKickoffPlayerObject
    {
        uint ReadFlags();
        void WriteFlags(uint value);
    }

    public interface ISourceKickoffPlayerObjects
    {
        // The genuine player_blocks entry's obj_3d view, not a control lookup or flags snapshot.
        ISourceKickoffPlayerObject ReadPlayerBlock(int originalPlayerNumber);
    }

    public enum SourceKickoffInstructionFlashReturnAction
    { KeepLive = 1, ExternallyRetire = 2 }

    public interface ISourceKickoffInstructionFlashReturnBinding
    {
        SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(
            SourceKickoffInstructionFlashInvocation invocation);
    }

    public sealed class SourceKickoffInstructionOwnedHelpers
    {
        public uint Player1ButtonAMask { get; }
        public uint Player1ButtonBMask { get; }
        public uint Player1ButtonCMask { get; }
        public uint FlashWhiteMask { get; }
        public ISourceKickoffCurrentSwitches Switches { get; }
        public ISourceKickoffPlayerObjects PlayerObjects { get; }
        public ISourceKickoffInstructionFlashReturnBinding FlashReturns { get; }

        public SourceKickoffInstructionOwnedHelpers(uint player1ButtonAMask, uint player1ButtonBMask,
            uint player1ButtonCMask, uint flashWhiteMask, ISourceKickoffCurrentSwitches switches,
            ISourceKickoffPlayerObjects playerObjects, ISourceKickoffInstructionFlashReturnBinding flashReturns)
        {
            if (player1ButtonAMask == 0) throw new ArgumentOutOfRangeException(nameof(player1ButtonAMask));
            if (player1ButtonBMask == 0) throw new ArgumentOutOfRangeException(nameof(player1ButtonBMask));
            if (player1ButtonCMask == 0) throw new ArgumentOutOfRangeException(nameof(player1ButtonCMask));
            if (flashWhiteMask == 0) throw new ArgumentOutOfRangeException(nameof(flashWhiteMask));
            Player1ButtonAMask = player1ButtonAMask; Player1ButtonBMask = player1ButtonBMask;
            Player1ButtonCMask = player1ButtonCMask; FlashWhiteMask = flashWhiteMask;
            Switches = switches ?? throw new ArgumentNullException(nameof(switches));
            PlayerObjects = playerObjects ?? throw new ArgumentNullException(nameof(playerObjects));
            FlashReturns = flashReturns ?? throw new ArgumentNullException(nameof(flashReturns));
        }
    }

    public enum SourceKickoffInstructionChildReturnReason
    { DbgKoCompileTimeReturn = 1, NormalBodyEnd = 2 }
    public enum SourceKickoffInstructionChildReturnAction
    { KeepLive = 1, ExternallyRetire = 2 }

    public interface ISourceKickoffInstructionChildReturnBinding
    {
        SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(
            SourceKickoffInstructionChildInvocation invocation, SourceKickoffInstructionChildReturnReason reason);
    }

    public sealed class SourceKickoffInstructionChildBinding
    {
        public SourceKickoffInstructionChildOperands Operands { get; }
        public ISourceKickoffInstructionChildEffects Effects { get; }
        public ISourceKickoffActivePlayerWaitFactory Waits { get; }
        public ISourceKickoffFlashContinuationFactory Flashes { get; }
        public ISourceKickoffInstructionChildReturnBinding Returns { get; }
        public SourceKickoffInstructionOwnedHelpers OwnedHelpers { get; }
        public SourceKickoffInstructionChildBinding(SourceKickoffInstructionChildOperands operands,
            ISourceKickoffInstructionChildEffects effects, ISourceKickoffActivePlayerWaitFactory waits,
            ISourceKickoffFlashContinuationFactory flashes, ISourceKickoffInstructionChildReturnBinding returns)
        {
            Operands = operands ?? throw new ArgumentNullException(nameof(operands));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            Waits = waits ?? throw new ArgumentNullException(nameof(waits));
            Flashes = flashes ?? throw new ArgumentNullException(nameof(flashes));
            Returns = returns ?? throw new ArgumentNullException(nameof(returns));
        }

        public static SourceKickoffInstructionChildBinding WithOwnedHelpers(
            SourceKickoffInstructionChildOperands operands, ISourceKickoffInstructionChildEffects effects,
            SourceKickoffInstructionOwnedHelpers helpers, ISourceKickoffInstructionChildReturnBinding returns)
        { return new SourceKickoffInstructionChildBinding(operands, effects, helpers, returns); }

        private SourceKickoffInstructionChildBinding(SourceKickoffInstructionChildOperands operands,
            ISourceKickoffInstructionChildEffects effects, SourceKickoffInstructionOwnedHelpers helpers,
            ISourceKickoffInstructionChildReturnBinding returns)
        {
            Operands = operands ?? throw new ArgumentNullException(nameof(operands));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            OwnedHelpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
            Returns = returns ?? throw new ArgumentNullException(nameof(returns));
        }

        private SourceKickoffInstructionChildBinding(SourceKickoffInstructionChildBinding source,
            ISourceKickoffActivePlayerWaitFactory waits, ISourceKickoffFlashContinuationFactory flashes)
            : this(source.Operands, source.Effects, waits, flashes, source.Returns)
        { OwnedHelpers = source.OwnedHelpers; }

        internal SourceKickoffInstructionChildBinding BindOwnedFactories(
            ISourceKickoffActivePlayerWaitFactory waits, ISourceKickoffFlashContinuationFactory flashes)
        {
            if (OwnedHelpers == null || Waits != null || Flashes != null)
                throw new InvalidOperationException("Bind owned helpers only from an unbound owned-helper configuration.");
            return new SourceKickoffInstructionChildBinding(this, waits, flashes);
        }
    }
}
