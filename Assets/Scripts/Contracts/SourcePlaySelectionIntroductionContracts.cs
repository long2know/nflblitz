using System;

namespace Blitz.Contracts
{
    public enum SourcePlaySelectionIntroductionColor { LightYellow, LightBlue, LightRed, LightGreen, White }
    public enum SourcePlaySelectionIntroductionStage
    { Setup, WaitingActiveTeam, WaitingAnyButton, Returned, Skipped, Faulted }

    public interface ISourcePlaySelectionIntroductionInputs : ISourceInstructionInputs
    {
        int ReadFourPlayerVersion();
        int ReadOffensiveSide();
        SourceInstructionRaster ReadInstructionRaster();
        void WriteShowPlyselIdiotBox(int value);
    }

    public readonly struct SourcePlaySelectionIntroductionPrint
    {
        public int NativePrintLine { get; }
        public string OriginalFormat { get; }
        public SourcePlaySelectionIntroductionColor? ColorOperand { get; }
        public SourceInstructionJustification Justification { get { return SourceInstructionJustification.Center; } }
        public string NativeSecondArgumentSymbols { get { return "HJUST_CENTER|VJUST_CENTER"; } }
        public SourcePlaySelectionIntroductionPrint(int nativePrintLine, string originalFormat,
            SourcePlaySelectionIntroductionColor? colorOperand)
        {
            if (nativePrintLine < 1) throw new ArgumentOutOfRangeException(nameof(nativePrintLine));
            if (string.IsNullOrEmpty(originalFormat)) throw new ArgumentException("An exact original print format is required.", nameof(originalFormat));
            if (colorOperand.HasValue && (colorOperand.Value < SourcePlaySelectionIntroductionColor.LightYellow
                || colorOperand.Value > SourcePlaySelectionIntroductionColor.White))
                throw new ArgumentOutOfRangeException(nameof(colorOperand));
            NativePrintLine = nativePrintLine; OriginalFormat = originalFormat; ColorOperand = colorOperand;
        }
    }

    public sealed class SourcePlaySelectionIntroductionSound
    {
        public bool Direct { get; }
        public string SoundSymbol { get; }
        public string BankSymbol { get { return Direct ? null : "plyr_bnk_str"; } }
        public string VolumeSymbol { get { return "VOLUME1"; } }
        public int Pan { get { return 127; } }
        public string PrioritySymbol { get { return Direct ? null : "PRIORITY_LVL4"; } }
        public int? RawPriority { get { return Direct ? (int?)1 : null; } }
        private SourcePlaySelectionIntroductionSound(bool direct, string symbol)
        { Direct = direct; SoundSymbol = symbol; }
        public static readonly SourcePlaySelectionIntroductionSound Ricochet =
            new SourcePlaySelectionIntroductionSound(false, "RICOCHET_SND");
        public static readonly SourcePlaySelectionIntroductionSound Tick =
            new SourcePlaySelectionIntroductionSound(true, "tick_sndn");
    }

    public interface ISourcePlaySelectionIntroductionEffects
    {
        ISourceInstructionPlate BeginPlate(string imageSymbol, string textureSymbol, SourceInstructionPoint position);
        void SetPlateWidthScale(ISourceInstructionPlate plate, float scale);
        void SetPlateHeightScale(ISourceInstructionPlate plate, float scale);
        void SetPlateId(ISourceInstructionPlate plate, string objectIdSymbol);
        void GeneratePlateVertices(ISourceInstructionPlate plate);
        void SetTextFont(SourceInstructionFont font);
        void SetStringId(int id);
        void SetTextColor(SourcePlaySelectionIntroductionColor color);
        void SetTextPosition(SourceInstructionPoint position);
        void SetTextZIncrement(float increment);
        ISourceInstructionText Print(SourcePlaySelectionIntroductionPrint request);
        ISourceInstructionFlasher CreateTextFlasher(string name, int processId,
            ISourceInstructionText first, ISourceInstructionText second, int thirdArgument, int fourthArgument);
        void KillTextFlasher(ISourceInstructionFlasher flasher, int signal);
        void DeleteStrings(int id, uint mask);
        void DeletePlateClass(string objectIdSymbol, uint mask);
        void PlaySound(SourcePlaySelectionIntroductionSound sound);
    }

    public sealed class SourcePlaySelectionIntroductionSnapshot
    {
        public SourceProcessView Process { get; }
        public SourcePlaySelectionIntroductionStage Stage { get; }
        public bool Active
        {
            get
            {
                return Stage == SourcePlaySelectionIntroductionStage.Setup
                    || Stage == SourcePlaySelectionIntroductionStage.WaitingActiveTeam
                    || Stage == SourcePlaySelectionIntroductionStage.WaitingAnyButton;
            }
        }
        public long EntrySourceTick { get; }
        public int Gate { get; }
        public int? OffensiveSide { get; }
        public int WaitTicks { get; }
        public SourceAnyButtonWaitSnapshot AnyButtonWait { get; }
        internal SourcePlaySelectionIntroductionSnapshot(SourceProcessView process, SourcePlaySelectionIntroductionStage stage,
            long entrySourceTick, int gate, int? offensiveSide, int waitTicks, SourceAnyButtonWaitSnapshot anyButtonWait)
        {
            Process = process; Stage = stage; EntrySourceTick = entrySourceTick;
            Gate = gate; OffensiveSide = offensiveSide; WaitTicks = waitTicks; AnyButtonWait = anyButtonWait;
        }
    }
}
