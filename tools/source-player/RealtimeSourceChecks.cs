using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static partial class Checks
{
    private sealed class RealtimeRandom : ISourceRandom
    {
        private readonly ISourceRandom source = SourcePreGameSoundService.CreateManagedPreviewRandom(7);
        internal readonly List<string> Draws = new();
        public SourceRandomProvenance Provenance => source.Provenance;
        public SourceRandomDraw Draw(SourceRandomSite site)
        {
            SourceRandomDraw result = source.Draw(site);
            Draws.Add(JsonSerializer.Serialize(result));
            return result;
        }
    }

    private static void VerifyRealtimeSource(PlayerData data)
    {
        var assets = new RenderAssets(data, new RecordingSink());
        var changes = new[] {
            (0d, 0, 0d), (0d, 0, 0d), (.055, 1, 0d), (.061, 0, 0d),
            (.140, 4, 0d), (.143, 5, 0d), (.180, 0, 0d),
            (.290, 2, 0d), (.295, 0, 0d), (.701, 7, 1d),
            (.714, 0, -1d), (.779, 0, 0d)
        };
        (GameSimulation Game, List<string> Inputs, List<string> Sounds, List<string> Random) Run(double[] frames)
        {
            var random = new RealtimeRandom();
            var game = KernelBinding.Create(data, randomFactory: _ => random);
            var camera = new PreviewCamera(data, assets, game);
            var bridge = new FixedStepBridge(game);
            var clockDriver = new SourceRealtimeFrameClock();
            var timeline = new SourceObservedInputTimeline();
            var buffer = new SourceCabinetInputBuffer(data.Configuration.Match, preserveSourceDigitalChanges: true);
            buffer.Clear();
            var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
            var inputs = new List<string>();
            var sounds = game.TakeSourceBootSoundOutputs().Select(value => JsonSerializer.Serialize(value)).ToList();
            int nextChange = 0;
            double elapsed = 0;
            long sourceBefore = game.SourceTick;
            void Capture(double[] axes, int[] buttons)
            {
                var physical = Enumerable.Range(0, 4).Select(id => {
                    var q = quantizer.Analog((float)axes[id * 2], (float)axes[id * 2 + 1]);
                    return new SourceStationInput(id, q.StickX, q.StickZ, (SourceButtons)buttons[id],
                        digitalCurrent: (SourceDigitalDirections)q.DigitalDirections);
                }).ToArray();
                var stations = data.Configuration.Match.Stations.Select(s => {
                    var q = quantizer.Analog((float)axes[s.StationId * 2], (float)axes[s.StationId * 2 + 1]);
                    return new SourceCabinetSample(s.StationId, q, (SourceButtons)buttons[s.StationId]);
                }).ToArray();
                buffer.Capture(stations, new SourcePhysicalSwitchFrame(physical));
            }
            SourceInputFrame Next()
            {
                timeline.Deliver(false, game.State.Tick + 1, Capture);
                SourceInputFrame frame = buffer.NextHostFrame();
                inputs.Add(JsonSerializer.Serialize(frame));
                return frame;
            }
            bridge.SourcePresentationCueRaised += camera.Cue;
            bridge.BeforeHostStep += _ => sourceBefore = game.SourceTick;
            bridge.AfterSimulationStep += () => {
                if (game.SourceTick > sourceBefore)
                    Check(game.ConsumedSourceSwitches != null &&
                        game.ConsumedSourceSwitches.SourceTick == game.SourceTick &&
                        game.ConsumedSourceSwitches.InputEpoch == game.SourceInputEpoch,
                        "A fresh neutral-release latch needs both real repeated neutral observations before the first57Hz scan; no fabricated physical zero fallback.");
                foreach (var call in game.TakeSourceDirectSoundCalls())
                    sounds.Add("direct:" + JsonSerializer.Serialize(call));
                foreach (var call in game.TakeSourceProcessSoundCalls())
                    sounds.Add("process:" + JsonSerializer.Serialize(call));
                foreach (var call in game.TakeSourceSoundControls())
                    sounds.Add("control:" + JsonSerializer.Serialize(call));
                foreach (var dispatch in game.TakeSourceSoundVmDispatches().Dispatches)
                    sounds.Add("vm:" + JsonSerializer.Serialize(new {
                        dispatch.OutputSequence, dispatch.RandomContext, dispatch.Status,
                        calls = dispatch.Result?.Calls.Select(c => new {
                            c.Bank, c.PlaylistId, c.Volume, c.Pan, c.PriorityWord, c.SubcallOrder
                        }).ToArray()
                    }));
            };
            foreach (double frameTime in frames)
            {
                elapsed += frameTime;
                while (nextChange < changes.Length && changes[nextChange].Item1 <= elapsed + 1e-10)
                {
                    var change = changes[nextChange++];
                    double[] axes = new double[8]; axes[0] = change.Item3;
                    int[] buttons = new[] { change.Item2, change.Item2 & 2, change.Item2 & 4, change.Item2 & 1 };
                    timeline.Add(false, SourceObservedInputTimeline.TargetTick(change.Item1, 0, 0,
                        game.State.Tick), axes, buttons);
                }
                clockDriver.Advance(frameTime, delta => bridge.AdvanceSource(delta, Next));
            }
            Check(timeline.PendingCount == 0 && clockDriver.PendingSeconds < FixedStepBridge.StepSeconds,
                "Measured four-slot changes and elapsed debt must drain without loss after bounded catch-up.");
            Check(timeline.Captures == changes.Length,
                "The observed FIFO must retain repeated actual neutral samples, not only changed levels.");
            timeline.Add(false, game.State.Tick + 10, new double[8], new[] { 7, 7, 7, 7 });
            long beforePause = game.State.Tick;
            int beforeDraws = random.Draws.Count;
            bridge.SetActive(false); buffer.Clear(); timeline.Clear(); clockDriver.Clear();
            Check(bridge.AdvanceSource(10, Next) == 0 && game.State.Tick == beforePause &&
                random.Draws.Count == beforeDraws && timeline.PendingCount == 0 && timeline.LifecycleCleared == 1,
                "Only the explicit pause boundary clears old transient changes; paused time must not consume input, RNG or source wakes.");
            bridge.SetActive(true);
            Check(bridge.AdvanceSource(0, Next) == 0, "A genuine zero-time resume callback must not synthesize a wake.");
            Capture(new double[8], new int[4]);
            clockDriver.Advance(FixedStepBridge.StepSeconds, delta => bridge.AdvanceSource(delta, Next));
            Check(game.State.Tick == beforePause + 1,
                "Actual neutral capture resumes the same game once, without replaying the discarded held ABC.");
            return (game, inputs, sounds, random.Draws);
        }
        double[] regular = Enumerable.Repeat(1d / 60, 240).ToArray();
        double[] stalled = new[] { .008, .141, .032, .157, .01, .097, .127 };
        double remaining = 4 - stalled.Sum();
        var partition = stalled.Concat(Enumerable.Repeat(1d / 60, (int)Math.Floor(remaining * 60))).ToList();
        partition.Add(4 - partition.Sum());
        var a = Run(regular); var b = Run(partition.ToArray());
        Check(a.Game.State.Tick == b.Game.State.Tick && a.Game.SourceTick == b.Game.SourceTick,
            "Temporal partition must preserve actual host/source clocks without source setters.");
        Check(a.Inputs.SequenceEqual(b.Inputs),
            "Held ABC, same-time multiple edges and all physical stations must retain their exact consumed FIFO order during historical debt.");
        Check(a.Sounds.SequenceEqual(b.Sounds),
            "Real boot/process/VM/direct sound outputs and their common producer ordinals must not change with host frame partition.");
        Check(a.Random.SequenceEqual(b.Random),
            "The actual source RNG raw draws/sites/order must remain identical with retained elapsed and measured input timing.");
        Check(a.Game.State.Players.Select(p => p.Position).SequenceEqual(b.Game.State.Players.Select(p => p.Position)),
            "Actual player state must remain equivalent, not merely counters/metadata.");
        Console.WriteLine("PASS actual source realtime partition: " + assertions +
            " assertions; physical4/ABC FIFO, real source RNG/output ordinals, bounded debt drain and actual player state.");
    }
}
