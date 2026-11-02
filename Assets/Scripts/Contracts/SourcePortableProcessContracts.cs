namespace Blitz.Contracts
{
    public readonly struct SourceProcessView
    {
        public long Serial { get; }
        public int Id { get; }
        public string Function { get; }
        public int RunLevel { get; }
        public long BirthTick { get; }
        public ushort SleepWord { get; }
        public long LastWakeTick { get; }
        public bool Suspended { get { return (SleepWord & 0x8000) != 0; } }
        public int SleepRemaining { get { return SleepWord & 0x7fff; } }
        public SourceProcessView(long serial, int id, string function, int runLevel, long birthTick, ushort sleepWord,
            long lastWakeTick = -1)
        {
            Serial = serial; Id = id; Function = function; RunLevel = runLevel;
            BirthTick = birthTick; SleepWord = sleepWord;
            LastWakeTick = lastWakeTick;
        }
    }

    public readonly struct SourcePossessionProcessRecord
    {
        public long SourceTick { get; }
        public string Operation { get; }
        public long Serial { get; }
        public int Id { get; }
        public int Value { get; }
        public SourcePossessionProcessRecord(long tick, string operation, long serial, int id, int value)
        { SourceTick = tick; Operation = operation; Serial = serial; Id = id; Value = value; }
    }
}
