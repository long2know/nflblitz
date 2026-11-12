using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Blitz.Contracts;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

internal static class KickoffStepCosts
{
    private sealed class Allocations : EventListener
    {
        internal readonly Dictionary<string, long> SampledBytes = new Dictionary<string, long>();
        internal string[] PayloadNames;
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)1);
        }
        protected override void OnEventWritten(EventWrittenEventArgs value)
        {
            if (value.EventName == null || !value.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal))
                return;
            lock (SampledBytes)
            {
                if (PayloadNames == null) PayloadNames = value.PayloadNames.ToArray();
                int name = value.PayloadNames.IndexOf("TypeName");
                int amount = value.PayloadNames.IndexOf("AllocationAmount64");
                if (name < 0 || amount < 0) return;
                string type = Convert.ToString(value.Payload[name]);
                long previous;
                SampledBytes.TryGetValue(type, out previous);
                SampledBytes[type] = previous + Convert.ToInt64(value.Payload[amount]);
            }
        }
    }

    private sealed class Cost
    {
        public long Host { get; set; }
        public long SourceBefore { get; set; }
        public long SourceAfter { get; set; }
        public string Phase { get; set; }
        public double InputAndSimulationMilliseconds { get; set; }
        public double SoundDrainMilliseconds { get; set; }
        public double PresentationAndFlushMilliseconds { get; set; }
        public double ObservationMilliseconds { get; set; }
        public long AllocatedBytes { get; set; }
    }

    private static double Milliseconds(long before, long after)
    { return (after - before) * 1000d / Stopwatch.Frequency; }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                .Replace("-", "").ToLowerInvariant();
    }

    private static void Prepend(FixedStepBridge bridge, string eventName, Action observer)
    {
        FieldInfo field = typeof(FixedStepBridge).GetField(eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException("The actual bridge event changed: " + eventName);
        field.SetValue(bridge, Delegate.Combine(observer, (Delegate)field.GetValue(bridge)));
    }

    private static int Main(string[] args)
    {
        if (args.Length != 4 && (args.Length != 5 || args[4] != "--pcm-only"))
            throw new ArgumentException("Supply real data, Checks DLL, new output JSON, required Core SHA256, optionally --pcm-only.");
        string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
        if (File.Exists(output)) throw new InvalidOperationException("Never overwrite an earlier cost observation.");
        if (Hash(typeof(PlayerHost).Assembly.Location) != args[3])
            throw new InvalidOperationException("The selected existing Core PE was not the actual runtime dependency.");
        long loading = Stopwatch.GetTimestamp();
        var data = new PlayerData(new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            path => File.ReadAllBytes(Path.Combine(root, path))));
        double loadMilliseconds = Milliseconds(loading, Stopwatch.GetTimestamp());
        if (args.Length == 5)
        {
            var resources = new List<object>();
            foreach (int id in new[] { 148, 17 })
            {
                var binding = data.AudioAdmission.RequireProgram("players", id);
                var pcm = binding.Program.Pcm;
                var copyPcm = (Func<byte[]>)pcm.GetType().GetMethod("CopyPcm16LittleEndian",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .CreateDelegate(typeof(Func<byte[]>), pcm);
                long before = Stopwatch.GetTimestamp(), allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                byte[] first = copyPcm();
                double cold = Milliseconds(before, Stopwatch.GetTimestamp());
                long coldAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                var hot = new List<double>();
                for (int i = 0; i < 100; i++)
                {
                    before = Stopwatch.GetTimestamp();
                    byte[] copy = copyPcm();
                    hot.Add(Milliseconds(before, Stopwatch.GetTimestamp()));
                    GC.KeepAlive(copy);
                }
                resources.Add(new { bank = "players", playlist = id, pcm.Sha256, pcm.SampleCount,
                    bytes = first.Length, coldCopyMilliseconds = cold, coldAllocatedBytes = coldAllocated,
                    hotCopies = hot.Count, hotMeanMilliseconds = hot.Average(),
                    hotMaximumMilliseconds = hot.Max() });
            }
            var resultPcm = new {
                schema = "actual-managed-immutable-pcm-costs/v1",
                actualCoreSha256 = Hash(typeof(PlayerHost).Assembly.Location),
                manifestSha256 = data.Catalog.ManifestSha256, loadMilliseconds, resources,
                actualGameConstructions = 0, actualSourceSteps = 0, clockOrDecoderStateMutation = false,
                scope = "Managed immutable PCM copy only; excludes browser SHA/marshal/createBuffer costs and cannot prove an audio deadline fix."
            };
            File.WriteAllText(output, JsonSerializer.Serialize(resultPcm));
            Console.WriteLine(JsonSerializer.Serialize(resultPcm));
            return 0;
        }
        Assembly checks = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        Type sinkType = checks.GetType("Checks+CompleteAvSink", true);
        var sink = (IRenderSink)Activator.CreateInstance(sinkType, true);
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, __) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        var host = new PlayerHost(data, sink, recordStore: store);
        Action<int, double> raw = (button, x) =>
            host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { button, 0, 0, 0 });
        Action<int, double> tap = (button, x) => {
            raw(0, 0); raw(button, x); raw(0, 0);
            host.Advance(0, 512, 384, 512, 384, 1);
            for (int i = 0; i < 70 && host.Session.UsingSourcePreGameInput; i++)
            {
                raw(0, 0);
                host.Advance(1d / 57, 512, 384, 512, 384, 1);
            }
        };
        host.Advance(0, 512, 384, 512, 384, 1);
        tap(4, 0); tap(0, 1); tap(1, 0);
        if (host.Session.Stage != SourceCabinetMenuStage.Teams)
            throw new InvalidOperationException("The real guest menu did not reach actual team selection.");
        tap(1, 0); tap(1, 0); tap(1, 0);
        if (!host.Session.Playing || host.Game.SourceTick != 0)
            throw new InvalidOperationException("The actual public team selection did not create its source-zero game.");
        raw(0, 0); raw(0, 0); host.Advance(0, 512, 384, 512, 384, 1);
        host.ObserveFrameHostTime(0);

        var costs = new List<Cost>();
        Cost current = null;
        long start = 0, boundary = 0, allocated = 0;
        host.Bridge.BeforeHostStep += state => {
            current = new Cost { Host = state.Tick + 1, SourceBefore = host.Game.SourceTick,
                Phase = state.Phase.ToString() };
            allocated = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
        };
        Prepend(host.Bridge, "AfterSimulationStep", () => {
            long now = Stopwatch.GetTimestamp();
            current.InputAndSimulationMilliseconds = Milliseconds(start, now);
            boundary = now;
        });
        host.Bridge.AfterSimulationStep += () => {
            long now = Stopwatch.GetTimestamp();
            current.SoundDrainMilliseconds = Milliseconds(boundary, now);
            boundary = now;
        };
        host.Bridge.AfterSourcePresentation += () => {
            long now = Stopwatch.GetTimestamp();
            current.PresentationAndFlushMilliseconds = Milliseconds(boundary, now);
            boundary = now;
        };
        host.Bridge.AfterHostStep += state => {
            long now = Stopwatch.GetTimestamp();
            current.ObservationMilliseconds = Milliseconds(boundary, now);
            current.SourceAfter = host.Game.SourceTick;
            current.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            costs.Add(current);
        };
        var frames = new List<object>();
        var allocations = new Allocations();
        double observed = 0;
        while (host.Game.SourceTick < 760)
        {
            observed += 1d / 60;
            host.CaptureObserved(new double[8], new int[4], observed);
            long before = Stopwatch.GetTimestamp(), first = host.Game.State.Tick;
            int steps = host.AdvanceObserved(1d / 60, observed, 512, 384, 512, 384, 1);
            frames.Add(new { firstHost = first, lastHost = host.Game.State.Tick,
                sourceTick = host.Game.SourceTick, steps,
                milliseconds = Milliseconds(before, Stopwatch.GetTimestamp()) });
        }
        var summary = costs.GroupBy(c => c.Phase).Select(group => new {
            phase = group.Key, hostSteps = group.Count(),
            simulationMs = group.Sum(c => c.InputAndSimulationMilliseconds),
            soundMs = group.Sum(c => c.SoundDrainMilliseconds),
            presentationMs = group.Sum(c => c.PresentationAndFlushMilliseconds),
            observationMs = group.Sum(c => c.ObservationMilliseconds),
            allocatedBytes = group.Sum(c => c.AllocatedBytes)
        }).ToArray();
        allocations.Dispose();
        object allocationSamples;
        lock (allocations.SampledBytes)
            allocationSamples = allocations.SampledBytes.OrderByDescending(pair => pair.Value).Take(20)
                .Select(pair => new { type = pair.Key, sampledBytes = pair.Value }).ToArray();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        FieldInfo modelField = host.Game.GetType().GetField("Model", hidden);
        object model = modelField != null ? modelField.GetValue(host.Game)
            : host.Game.GetType().GetProperty("Model", hidden).GetValue(host.Game);
        Type snapshotType = host.Game.GetType().Assembly.GetType("Blitz.Gameplay.GameSnapshot", true);
        ConstructorInfo snapshot = snapshotType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 2);
        long sourceAtSnapshotStart = host.Game.SourceTick, hostAtSnapshotStart = host.Game.State.Tick;
        FieldInfo drawsField = model.GetType().GetField("SourceRandomDraws",
            BindingFlags.Instance | BindingFlags.NonPublic);
        long drawsBeforeSnapshots = (long)drawsField.GetValue(model);
        long snapshotStart = Stopwatch.GetTimestamp(), snapshotAllocated = GC.GetAllocatedBytesForCurrentThread();
        object lastSnapshot = null;
        for (int i = 0; i < 200; i++) lastSnapshot = snapshot.Invoke(new[] { model, (object)(-1) });
        var snapshotCost = new {
            iterations = 200, milliseconds = Milliseconds(snapshotStart, Stopwatch.GetTimestamp()),
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - snapshotAllocated,
            scope = "Data-only construction at one unchanged model; no source/host step."
        };
        GC.KeepAlive(lastSnapshot);
        if (host.Game.SourceTick != sourceAtSnapshotStart || host.Game.State.Tick != hostAtSnapshotStart
            || (long)drawsField.GetValue(model) != drawsBeforeSnapshots)
            throw new InvalidOperationException("The data-only snapshot probe changed an actual clock or random draw.");
        var result = new {
            schema = "actual-managed-kickoff-step-costs/v1",
            actualCore = typeof(PlayerHost).Assembly.Location,
            actualCoreSha256 = Hash(typeof(PlayerHost).Assembly.Location),
            actualChecksSha256 = Hash(args[1]), manifestSha256 = data.Catalog.ManifestSha256,
            publicGuestSetup = true, coreStateOrClockMutation = false,
            namedBrowserReproduction = false, wasmOrGpuOrDeadlineQualification = false,
            frozen09afSourceClosureAuthenticated = false,
            scope = "Managed actual-source stage/allocation costs only; not browser CPU attribution.",
            finalSource = host.Game.SourceTick, finalHost = host.Game.State.Tick, observed,
            summary, snapshotCost, allocationSamples, allocationPayloadNames = allocations.PayloadNames, costs, frames
        };
        File.WriteAllText(output, JsonSerializer.Serialize(result));
        Console.WriteLine(JsonSerializer.Serialize(new { result.schema, result.finalSource,
            result.finalHost, result.scope, summary, snapshotCost, allocationSamples, output }));
        return 0;
    }
}
