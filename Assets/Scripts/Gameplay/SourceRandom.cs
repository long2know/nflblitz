using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Blitz.Gameplay
{
    public enum SourceRandomEvidence { RecordedOracle, ExternalUnverified, VerifiedAlgorithm }

    public readonly struct SourceRandomProvenance
    {
        public string Profile { get; }
        public string Implementation { get; }
        public string SeedProvenance { get; }
        public SourceRandomEvidence Evidence { get; }

        public SourceRandomProvenance(string profile, string implementation, string seedProvenance,
            SourceRandomEvidence evidence)
        {
            Profile = SourceP1Guard.Identity(profile, nameof(profile));
            Implementation = SourceP1Guard.Identity(implementation, nameof(implementation));
            SeedProvenance = SourceP1Guard.Identity(seedProvenance, nameof(seedProvenance));
            if (evidence < SourceRandomEvidence.RecordedOracle || evidence > SourceRandomEvidence.VerifiedAlgorithm)
                throw new ArgumentOutOfRangeException(nameof(evidence));
            Evidence = evidence;
        }
    }

    public readonly struct SourceRandomContext
    {
        public long SourceTick { get; }
        public int ActorId { get; }
        public string Process { get; }
        public int ProcessOrder { get; }
        public int FirstCallIndex { get; }
        public string Profile { get; }

        public SourceRandomContext(long sourceTick, int actorId, string process, int processOrder,
            string profile, int firstCallIndex = 0)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (actorId < -1 || actorId > 13) throw new ArgumentOutOfRangeException(nameof(actorId));
            if (processOrder < 0) throw new ArgumentOutOfRangeException(nameof(processOrder));
            if (firstCallIndex < 0) throw new ArgumentOutOfRangeException(nameof(firstCallIndex));
            SourceTick = sourceTick;
            ActorId = actorId;
            Process = SourceP1Guard.Identity(process, nameof(process));
            ProcessOrder = processOrder;
            FirstCallIndex = firstCallIndex;
            Profile = SourceP1Guard.Identity(profile, nameof(profile));
        }

        public SourceRandomSite At(SourceRandomKind kind, int boundOrModulus, string sourceIdentity, int relativeCall)
        {
            if (relativeCall < 0) throw new ArgumentOutOfRangeException(nameof(relativeCall));
            return new SourceRandomSite(kind, boundOrModulus, SourceTick, ActorId, Process, ProcessOrder,
                checked(FirstCallIndex + relativeCall), Profile, sourceIdentity);
        }
    }

    public readonly struct SourceRandomSite
    {
        public SourceRandomKind Kind { get; }
        public int BoundOrModulus { get; }
        public long SourceTick { get; }
        public int ActorId { get; }
        public string Process { get; }
        public int ProcessOrder { get; }
        public int CallIndex { get; }
        public string Profile { get; }
        public string SourceIdentity { get; }
        public bool ConsumesPlatformValue { get { return Kind == SourceRandomKind.CStandardRand || BoundOrModulus != 0; } }

        public SourceRandomSite(SourceRandomKind kind, int boundOrModulus, long sourceTick, int actorId,
            string process, int processOrder, int callIndex, string profile, string sourceIdentity)
        {
            if (kind != SourceRandomKind.RandRange && kind != SourceRandomKind.CStandardRand)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == SourceRandomKind.CStandardRand && boundOrModulus < 0)
                throw new ArgumentOutOfRangeException(nameof(boundOrModulus));
            var context = new SourceRandomContext(sourceTick, actorId, process, processOrder, profile, callIndex);
            Kind = kind;
            BoundOrModulus = boundOrModulus;
            SourceTick = context.SourceTick;
            ActorId = context.ActorId;
            Process = context.Process;
            ProcessOrder = context.ProcessOrder;
            CallIndex = context.FirstCallIndex;
            Profile = context.Profile;
            SourceIdentity = SourceP1Guard.Identity(sourceIdentity, nameof(sourceIdentity));
        }

        public override string ToString()
        {
            return SourceIdentity + " " + Kind + "(" + BoundOrModulus.ToString(CultureInfo.InvariantCulture) +
                ") tick=" + SourceTick.ToString(CultureInfo.InvariantCulture) +
                " actor=" + ActorId.ToString(CultureInfo.InvariantCulture) +
                " process=" + Process + "/" + ProcessOrder.ToString(CultureInfo.InvariantCulture) +
                " call=" + CallIndex.ToString(CultureInfo.InvariantCulture) + " profile=" + Profile;
        }

        internal bool SameAs(SourceRandomSite other)
        {
            return Kind == other.Kind && BoundOrModulus == other.BoundOrModulus && SourceTick == other.SourceTick &&
                ActorId == other.ActorId && Process == other.Process && ProcessOrder == other.ProcessOrder &&
                CallIndex == other.CallIndex && Profile == other.Profile && SourceIdentity == other.SourceIdentity;
        }
    }

    public readonly struct SourceRandomDraw
    {
        public SourceRandomSite Site { get; }
        public int? Raw { get; }
        public int Value { get; }
        public long Order { get; }
        public SourceRandomProvenance Provenance { get; }

        public SourceRandomDraw(SourceRandomSite site, int? raw, long order, SourceRandomProvenance provenance)
        {
            if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
            SourceP1Guard.Identity(site.SourceIdentity, nameof(site));
            SourceP1Guard.Identity(provenance.Implementation, nameof(provenance));
            if (site.Profile != provenance.Profile) throw new ArgumentException("RNG profile mismatch.", nameof(provenance));
            if (site.ConsumesPlatformValue != raw.HasValue)
                throw new ArgumentException("randrng(0) consumes no random(); all other calls require a raw value.", nameof(raw));
            if (raw.HasValue && raw.Value < 0) throw new ArgumentOutOfRangeException(nameof(raw));
            Site = site;
            Raw = raw;
            Order = order;
            Provenance = provenance;
            Value = site.Kind == SourceRandomKind.RandRange
                ? site.BoundOrModulus == 0 ? 0 : raw.Value % site.BoundOrModulus
                : site.BoundOrModulus == 0 ? raw.Value : raw.Value % site.BoundOrModulus;
        }

        // MatchFlow intentionally accepts the raw C rand() value, but a reduced randrng() result.
        public int MatchFlowValue { get { return Site.Kind == SourceRandomKind.CStandardRand ? Raw.Value : Value; } }
    }

    public interface ISourceRandom
    {
        SourceRandomProvenance Provenance { get; }
        SourceRandomDraw Draw(SourceRandomSite site);
    }

    public sealed class SourceRecordedRandom : ISourceRandom
    {
        private readonly SourceRandomDraw[] tape;
        private int cursor;
        public SourceRandomProvenance Provenance { get; }
        public int Consumed { get { return cursor; } }
        public int Remaining { get { return tape.Length - cursor; } }

        public SourceRecordedRandom(IEnumerable<SourceRandomDraw> tape, SourceRandomProvenance provenance)
        {
            if (tape == null) throw new ArgumentNullException(nameof(tape));
            if (provenance.Evidence != SourceRandomEvidence.RecordedOracle)
                throw new ArgumentException("A recorded tape is an oracle, not proof of a PRNG algorithm.", nameof(provenance));
            SourceP1Guard.Identity(provenance.Implementation, nameof(provenance));
            Provenance = provenance;
            this.tape = new List<SourceRandomDraw>(tape).ToArray();
            long previous = -1;
            foreach (SourceRandomDraw draw in this.tape)
            {
                if (draw.Order <= previous || draw.Provenance.Profile != provenance.Profile ||
                    draw.Provenance.Implementation != provenance.Implementation ||
                    draw.Provenance.SeedProvenance != provenance.SeedProvenance ||
                    draw.Provenance.Evidence != SourceRandomEvidence.RecordedOracle)
                    throw new ArgumentException("Tape provenance or draw order mismatch.", nameof(tape));
                previous = draw.Order;
            }
        }

        public SourceRandomDraw Draw(SourceRandomSite site)
        {
            if (cursor == tape.Length) throw new InvalidOperationException("Source RNG oracle exhausted at " + site + ".");
            SourceRandomDraw expected = tape[cursor];
            if (!expected.Site.SameAs(site))
                throw new InvalidOperationException("Source RNG order diverged. Expected " + expected.Site +
                    "; observed " + site + ".");
            cursor++;
            return expected;
        }
    }

    public sealed class SourceInjectedRandom : ISourceRandom
    {
        private readonly Func<SourceRandomSite, int> rawProvider;
        private readonly List<SourceRandomDraw> trace = new List<SourceRandomDraw>();
        private readonly ReadOnlyCollection<SourceRandomDraw> view;
        private SourceRandomSite previous;
        public SourceRandomProvenance Provenance { get; }
        public IReadOnlyList<SourceRandomDraw> Trace { get { return view; } }

        public SourceInjectedRandom(Func<SourceRandomSite, int> rawProvider, SourceRandomProvenance provenance)
        {
            this.rawProvider = rawProvider ?? throw new ArgumentNullException(nameof(rawProvider));
            SourceP1Guard.Identity(provenance.Implementation, nameof(provenance));
            Provenance = provenance;
            view = trace.AsReadOnly();
        }

        public SourceRandomDraw Draw(SourceRandomSite site)
        {
            if (site.Profile != Provenance.Profile) throw new ArgumentException("Source RNG profile mismatch.", nameof(site));
            if (trace.Count != 0 && (site.SourceTick < previous.SourceTick ||
                (site.SourceTick == previous.SourceTick && (site.ProcessOrder < previous.ProcessOrder ||
                (site.ProcessOrder == previous.ProcessOrder && (site.Process != previous.Process ||
                site.ActorId != previous.ActorId || site.CallIndex <= previous.CallIndex))))))
                throw new InvalidOperationException("Source RNG dispatch/call order regressed at " + site + ".");
            int? raw = site.ConsumesPlatformValue ? (int?)rawProvider(site) : null;
            var draw = new SourceRandomDraw(site, raw, trace.Count, Provenance);
            trace.Add(draw);
            previous = site;
            return draw;
        }
    }

    public sealed class SourceUnavailableRandom : ISourceRandom
    {
        public SourceRandomProvenance Provenance { get; }
        public SourceUnavailableRandom(string profile, string seedProvenance)
        {
            Provenance = new SourceRandomProvenance(profile, SourceRandom.MissingImplementation,
                seedProvenance, SourceRandomEvidence.ExternalUnverified);
        }
        public SourceRandomDraw Draw(SourceRandomSite site)
        {
            if (!site.ConsumesPlatformValue) return new SourceRandomDraw(site, null, 0, Provenance);
            throw new InvalidOperationException(SourceRandom.MissingImplementation + " Required at " + site + ".");
        }
    }

    public static class SourceRandom
    {
        public const string MissingImplementation =
            "Missing target /video/lib/libc.a random()/srandom() and rand()/srand() bodies; " +
            "midwayvideo@1792293 provides randper.c random()%val and DJGPP STDLIB.H declarations, not the linked PRNG implementation";

        public static SourceRandomDraw FulfillMatchRequest(ISourceRandom random, SourceRandomRequest request,
            SourceRandomContext context)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (!request.IsRequired) throw new ArgumentException("No random draw requested.", nameof(request));
            return random.Draw(context.At(request.Kind, request.BoundOrModulus,
                "SourceMatchFlow." + request.Call, request.CallIndex));
        }
    }

    internal sealed class SourceRandomCursor
    {
        private readonly ISourceRandom random;
        private readonly SourceRandomContext context;
        internal int Count { get; private set; }
        internal SourceRandomCursor(ISourceRandom random, SourceRandomContext context)
        {
            this.random = random;
            this.context = context;
        }
        internal int Range(int bound, string identity)
        {
            if (random == null) throw new InvalidOperationException("Explicit source RNG required at " + identity + ".");
            var site = context.At(SourceRandomKind.RandRange, bound, identity, Count);
            SourceRandomDraw draw = random.Draw(site);
            if (!draw.Site.SameAs(site)) throw new InvalidOperationException("RNG returned the wrong source call at " + identity + ".");
            Count++;
            return draw.Value;
        }
    }
}
