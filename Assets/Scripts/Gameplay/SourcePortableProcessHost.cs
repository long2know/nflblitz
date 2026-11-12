using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    // Only declared portable Core continuations are represented. Native list sorting,
    // stack/ABI restoration and unrelated Goose processes are not qualified here.
    public sealed class SourcePortableProcessHost
    {
        private sealed class Node
        {
            internal SourceProcessView Identity;
            internal ushort Sleep;
            internal long LastVisit = -1;
            internal long LastWake = -1;
            internal bool Immediate;
        }
        private readonly List<Node> nodes = new List<Node>();
        private readonly List<SourcePossessionProcessRecord> records = new List<SourcePossessionProcessRecord>();
        public long SourceTick { get; private set; }
        public long Caller { get; private set; }
        internal static SourcePortableProcessHost AtSourceTick(long sourceTick)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            return new SourcePortableProcessHost { SourceTick = sourceTick };
        }
        public ReadOnlyCollection<SourceProcessView> Processes
        {
            get
            {
                var result = new List<SourceProcessView>();
                foreach (Node n in nodes)
                    result.Add(new SourceProcessView(n.Identity.Serial, n.Identity.Id, n.Identity.Function,
                        n.Identity.RunLevel, n.Identity.BirthTick, n.Sleep, n.LastWake));
                return result.AsReadOnly();
            }
        }
        public ReadOnlyCollection<SourcePossessionProcessRecord> Records { get { return Array.AsReadOnly(records.ToArray()); } }
        public void BeginTick(long tick)
        {
            if (tick != SourceTick + 1 || Caller != 0)
                throw new InvalidOperationException("Visit every portable source dispatch exactly once.");
            SourceTick = tick; records.Clear();
        }
        public void Create(long serial, int id, string function, int level, ushort sleep = 1)
        {
            if (serial <= 0 || Find(serial) != null || string.IsNullOrEmpty(function))
                throw new ArgumentException("A fresh declared process identity is required.");
            nodes.Insert(0, new Node { Identity = new SourceProcessView(serial, id, function, level, SourceTick, sleep), Sleep = sleep });
            Record("Create", serial, id, sleep);
        }
        public bool Visit(long serial)
        {
            Node n = Find(serial);
            if (n == null) return false;
            if (Caller != 0 || n.LastVisit == SourceTick)
                throw new InvalidOperationException("A declared process slot cannot execute twice or inline in another wake.");
            n.LastVisit = SourceTick;
            if (n.Identity.BirthTick >= SourceTick && !n.Immediate || (short)n.Sleep <= 0) return false;
            n.Sleep--;
            if (n.Sleep != 0) return false;
            Caller = serial; n.LastWake = SourceTick; Record("Wake", serial, n.Identity.Id, 0);
            return true;
        }
        public void CreateImmediateAfterCaller(long serial, int id, string function)
        {
            Node caller = Find(Caller);
            if (caller == null || serial <= 0 || Find(serial) != null || string.IsNullOrEmpty(function))
                throw new InvalidOperationException("Immediate creation requires an executing caller and a fresh child.");
            nodes.Insert(nodes.IndexOf(caller) + 1, new Node {
                Identity = new SourceProcessView(serial, id, function, 0, SourceTick, 1), Sleep = 1, Immediate = true
            });
            Record("CreateImmediate", serial, id, 1);
        }
        internal long NextImmediateAfter(long caller)
        {
            int at = nodes.FindIndex(n => n.Identity.Serial == caller);
            if (at < 0) return 0;
            for (int i = at + 1; i < nodes.Count; i++)
            {
                Node n = nodes[i];
                if (!n.Immediate || n.Identity.BirthTick != SourceTick) break;
                if (n.LastVisit != SourceTick) return n.Identity.Serial;
            }
            return 0;
        }
        public void Complete()
        {
            Node n = Find(Caller);
            if (n != null && n.Sleep == 0) n.Sleep = 1;
            Caller = 0;
        }
        public void Sleep(int ticks)
        {
            Node n = Find(Caller);
            if (n == null || ticks < 1 || ticks > 0x7fff)
                throw new InvalidOperationException("sleep requires the actual executing continuation and a positive source delay.");
            n.Sleep = (ushort)ticks; Record("Sleep", Caller, n.Identity.Id, ticks);
        }
        public void Kill(long serial)
        {
            Node n = Find(serial);
            if (n == null) return;
            nodes.Remove(n); Record("Die", serial, n.Identity.Id, 0);
        }
        public bool Exists(int id, int mask)
        {
            foreach (Node n in nodes) if ((n.Identity.Id & mask) == (id & mask)) return true;
            return false;
        }
        public bool Contains(long serial) { return Find(serial) != null; }
        public void SuspendMultiple(int id, int mask)
        {
            foreach (Node n in nodes)
                if (n.Identity.Serial != Caller && (n.Identity.Id & mask) == id)
                { n.Sleep |= 0x8000; Record("Suspend", n.Identity.Serial, n.Identity.Id, n.Sleep); }
        }
        public void ResumeMultiple(int id, int mask)
        {
            foreach (Node n in nodes)
                if ((n.Identity.Id & mask) == id)
                { n.Sleep &= 0x7fff; Record("Resume", n.Identity.Serial, n.Identity.Id, n.Sleep); }
        }
        public void ResumeAll()
        {
            foreach (Node n in nodes)
            { n.Sleep &= 0x7fff; Record("ResumeAll", n.Identity.Serial, n.Identity.Id, n.Sleep); }
        }
        public SourceProcessView Require(long serial)
        {
            Node n = Find(serial);
            if (n == null) throw new InvalidOperationException("The actual declared process is no longer alive.");
            return new SourceProcessView(serial, n.Identity.Id, n.Identity.Function, n.Identity.RunLevel, n.Identity.BirthTick, n.Sleep, n.LastWake);
        }
        internal void Record(string operation, long serial, int id, int value)
        { records.Add(new SourcePossessionProcessRecord(SourceTick, operation, serial, id, value)); }
        private Node Find(long serial) { return nodes.Find(n => n.Identity.Serial == serial); }
    }
}
