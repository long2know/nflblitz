using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Blitz.Gameplay;

internal sealed class DefaultDiagnosticBudget
{
    private readonly string label;
    private readonly string output;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private SourceInjectedRandom random;
    private uint randomState;
    private int callbacks;
    private int values;
    private bool constructed;
    private bool failed;

    internal DefaultDiagnosticBudget(string output)
    {
        label = Environment.GetEnvironmentVariable("BLITZ_DEFAULT_VARIANT");
        string socketPath = Environment.GetEnvironmentVariable("BLITZ_DEFAULT_AUTHORITY");
        this.output = Path.GetFullPath(output);
        if ((label != "A" && label != "B" && label != "C") || string.IsNullOrEmpty(socketPath)
            || this.output != Environment.GetEnvironmentVariable("BLITZ_DEFAULT_OUTPUT"))
            throw new InvalidOperationException("DEFAULT DIAGNOSTIC: unbound driver");
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.ReceiveTimeout = socket.SendTimeout = 5000;
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        var stream = new NetworkStream(socket, true);
        reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 1024, true);
        writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, false);
        writer.NewLine = "\n";
        writer.AutoFlush = true;
    }

    private void Call(string operation, params long[] counters)
    {
        if (failed) throw new InvalidOperationException("DEFAULT DIAGNOSTIC: closed graph");
        try
        {
            string message = label + " " + operation;
            foreach (long counter in counters)
            {
                if (counter < 0) throw new InvalidOperationException("DEFAULT DIAGNOSTIC: negative counter");
                message += " " + counter.ToString(CultureInfo.InvariantCulture);
            }
            writer.WriteLine(message);
            if (reader.ReadLine() != "OK")
                throw new InvalidOperationException("DEFAULT DIAGNOSTIC: authority refused");
        }
        catch { failed = true; throw; }
    }

    private static bool SameFloat(float left, float right)
    {
        return BitConverter.ToInt32(BitConverter.GetBytes(left), 0) ==
            BitConverter.ToInt32(BitConverter.GetBytes(right), 0);
    }

    internal void VerifyCompiledNearFrameContract()
    {
        Call("pure-start", 8);
        // Pure scalar probes of the actual compiled Fraction method, not a VM,
        // clip, Game, source wake, random provider or recorded host expectation.
        Type type = typeof(SourceAnimationLayerState);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo fraction = type.GetMethod("Fraction", flags);
        FieldInfo cursor = type.GetField("floatingFrame", flags);
        PropertyInfo ordinal = type.GetProperty("FrameOrdinal", flags);
        PropertyInfo mode = type.GetProperty("Mode", flags);
        if (fraction == null || fraction.DeclaringType != type || fraction.ReturnType != typeof(float)
            || !fraction.IsPrivate || fraction.IsStatic || fraction.GetParameters().Length != 0
            || type.Assembly != typeof(DefaultDiagnosticBudget).Assembly
            || cursor == null || cursor.FieldType != typeof(float)
            || ordinal == null || ordinal.PropertyType != typeof(int)
            || mode == null || mode.PropertyType != typeof(uint))
            throw new InvalidOperationException("DEFAULT SOURCE CHECK: exact compiled Fraction seam missing");
        object layer = RuntimeHelpers.GetUninitializedObject(type);
        float threshold = 0.05f;
        float below = BitConverter.ToSingle(BitConverter.GetBytes(
            BitConverter.ToInt32(BitConverter.GetBytes(threshold), 0) - 1), 0);
        uint streaming = SourceAnimationModes.Streaming;
        uint[] modes = { 0u, streaming, streaming, streaming, streaming, streaming, streaming, streaming };
        int[] ordinals = { 0, 0, 1, 0, 0, 1, 1, 1 };
        float[] frames = { 1f / 16f, 0f, 1f + 1f / 64f, below, threshold,
            1f + 1f / 16f, 1f - 1f / 64f, 1f - 1f / 16f };
        // ANIM.C862..881 strict near-frame selection: keep the accumulator,
        // select zero only below the threshold; normal interpolation survives.
        float[] sourceExpected = { 0f, 0f, 0f, 0f, threshold, 1f / 16f, 0f, 0f };
        float[] legacyExpected = { 0f, 0f, 1f / 64f, below, threshold, 1f / 16f, 0f, 0f };
        int sourceMatches = 0, legacyMatches = 0, unrounded = 0;
        for (int index = 0; index < frames.Length; index++)
        {
            mode.SetValue(layer, modes[index], null);
            ordinal.SetValue(layer, ordinals[index], null);
            cursor.SetValue(layer, frames[index]);
            Call("pure-case", index);
            float actual = (float)fraction.Invoke(layer, new object[0]);
            if (SameFloat(actual, sourceExpected[index])) sourceMatches++;
            if (SameFloat(actual, legacyExpected[index])) legacyMatches++;
            if (SameFloat((float)cursor.GetValue(layer), frames[index])
                && (int)ordinal.GetValue(layer, null) == ordinals[index]
                && (uint)mode.GetValue(layer, null) == modes[index]) unrounded++;
        }
        Call("pure", frames.Length, sourceMatches, legacyMatches, unrounded);
    }

    internal T Construct<T>(Func<T> factory)
    {
        Call("construct");
        try { return factory(); }
        catch { failed = true; throw; }
    }

    internal ISourceRandom CreateRandom(int seed)
    {
        Call("stream", seed);
        if (random != null) throw new InvalidOperationException("DEFAULT DIAGNOSTIC: duplicate stream");
        randomState = unchecked((uint)seed) ^ 0x9E3779B9u;
        random = new SourceInjectedRandom(site => {
            Call("raw");
            callbacks++;
            int value = NextInt(16777216);
            Call("value");
            values++;
            return value;
        }, new SourceRandomProvenance("OriginalRelease57Hz/preview",
            "Explicit legacy LCG preview provider; NOT the historical platform random()/rand() body",
            "GameSimulation.Restart(" + seed + ")", SourceRandomEvidence.ExternalUnverified));
        return random;
    }

    private int NextInt(int maximum)
    {
        randomState = unchecked(randomState * 1664525u + 1013904223u);
        return (int)((randomState >> 8) % (uint)maximum);
    }

    internal void Constructed(ISourceRandom actual, long host, long source)
    {
        if (!object.ReferenceEquals(random, actual))
            throw new InvalidOperationException("DEFAULT DIAGNOSTIC: injected identity differs");
        Call("constructed", host, source);
        constructed = true;
    }

    internal void BeforeAdvance(long host, long source) { Call("advance", host, source); }
    internal void BeforeHost(long host, long source) { Call("host", host, source); }
    internal void AfterAdvance(long host, long source) { Call("advanced", host, source); }

    internal void Finish(SourceInjectedRandom actual)
    {
        if (!constructed || !object.ReferenceEquals(random, actual))
            throw new InvalidOperationException("DEFAULT DIAGNOSTIC: final identity differs");
        int consumed = 0;
        foreach (SourceRandomDraw draw in actual.Trace)
            if (draw.Raw.HasValue) consumed++;
        if (consumed != callbacks || callbacks != values)
            throw new InvalidOperationException("DEFAULT DIAGNOSTIC: callback/value counts differ");
        Call("finish", actual.Trace.Count, callbacks, values);
    }

    private static int FileId(string name)
    {
        switch (name)
        {
            case "default-host.jsonl": return 0;
            case "default-rng.json": return 1;
            case "default-menu.json": return 2;
            default: throw new InvalidOperationException("DEFAULT DIAGNOSTIC: unplanned output");
        }
    }

    internal StreamWriter OpenText(string name)
    {
        int id = FileId(name);
        var file = new FileStream(Path.Combine(output, name), FileMode.Open, FileAccess.Write, FileShare.None);
        if (file.Length != 0)
        {
            file.Dispose();
            throw new InvalidOperationException("DEFAULT DIAGNOSTIC: output is not fresh");
        }
        return new StreamWriter(new BudgetStream(this, file, id), new UTF8Encoding(false));
    }

    internal void WriteText(string name, string text)
    {
        using (StreamWriter destination = OpenText(name)) destination.Write(text);
    }

    private sealed class BudgetStream : Stream
    {
        private readonly DefaultDiagnosticBudget owner;
        private readonly Stream inner;
        private readonly int id;
        internal BudgetStream(DefaultDiagnosticBudget owner, Stream inner, int id)
        {
            this.owner = owner; this.inner = inner; this.id = id;
        }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { return inner.Length; } }
        public override long Position { get { return inner.Position; } set { throw new NotSupportedException(); } }
        public override void Flush() { inner.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            owner.Call("write", id, count);
            inner.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
