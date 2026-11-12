using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static partial class Checks
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }

    private sealed class CompleteAudioSink : ISourceAudioSink
    {
        internal readonly Dictionary<string, int> Samples = new();
        internal readonly List<JsonElement> Commands = new();
        internal readonly List<object> SampleInputs = new();
        internal readonly List<string> Batches = new();
        internal bool HasNonzeroPcm;
        private readonly string? proofRoot;
        internal CompleteAudioSink(string? root = null)
        {
            if (root == null) return;
            proofRoot = Path.GetFullPath(root);
            if (Directory.Exists(proofRoot)) throw new InvalidOperationException("Choose a create-new original audio proof output.");
            Directory.CreateDirectory(proofRoot);
        }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata)
        {
            using var json = JsonDocument.Parse(metadata);
            HasNonzeroPcm |= pcm.Any(value => value != 0);
            Check(rate == 31250 && channels == 1 && id == "pcm16-" + DataCatalog.Hash(pcm) &&
                json.RootElement.GetProperty("sampleCount").GetInt32() * 2 == pcm.Length,
                "The real original PCM, not output metadata, must reach the consumer with its exact content hash.");
            if (Samples.TryGetValue(id, out int previous))
            {
                Check(previous == pcm.Length, "Reloaded immutable PCM must retain its exact content-addressed extent.");
                return;
            }
            Samples.Add(id, pcm.Length);
            if (proofRoot != null)
            {
                string path = id + ".pcm";
                File.WriteAllBytes(Path.Combine(proofRoot, path), pcm);
                SampleInputs.Add(new { id, path, rate, channels, metadata });
            }
        }
        public void AudioCommands(string text)
        {
            using var json = JsonDocument.Parse(text);
            Check(json.RootElement.GetProperty("schema").GetString() is "source-audio-playback/v2" or "source-audio-playback/v3" or "source-audio-playback/v4",
                "Actual compiled source playlists must not be relabeled as v1 one-shots.");
            Commands.AddRange(json.RootElement.GetProperty("commands").EnumerateArray().Select(c => c.Clone()));
            Batches.Add(text);
            if (proofRoot != null) File.AppendAllText(Path.Combine(proofRoot, "commands.jsonl"), text + "\n");
        }
        internal void Save()
        {
            if (proofRoot != null)
                File.WriteAllText(Path.Combine(proofRoot, "audio-proof.json"), JsonSerializer.Serialize(new {
                    schema = "actual-original-compiled-playlist-component/v1", directedComponentFixture = true,
                    naturalGameProducerClaimed = false, originalDataManifestSha256 = SourceTeamResourceProfile.CompleteOriginalManifestSha256,
                    samples = SampleInputs, batches = Batches
                }));
        }
    }

    private sealed class CompleteAvSink : RecordingSink, ISourceAudioSink, IRenderSink
    {
        internal readonly CompleteAudioSink Audio = new();
        internal readonly Dictionary<int, string> ModelDefinitions = new();
        internal readonly Dictionary<int, string> AppearanceDefinitions = new();
        internal bool CapturePresentPackets;
        internal object? LastPresent;
        void IRenderSink.Model(int id, string definition)
        { ModelDefinitions[id] = definition; Model(id, definition); }
        void IRenderSink.Appearance(int id, string definition)
        { AppearanceDefinitions[id] = definition; Appearance(id, definition); }
        void IRenderSink.Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            if (CapturePresentPackets)
                LastPresent = new {
                    camera = camera.ToArray(), joints = joints.ToArray(), models = models.ToArray(),
                    appearances = appearances.ToArray(), ball = ball.ToArray(), hudVertices = hudVertices.ToArray(),
                    hudColors = hudColors.ToArray(), hudTextures = hudTextures.ToArray(), hudCounts = hudCounts.ToArray(),
                    status = JsonSerializer.Deserialize<JsonElement>(status)
                };
            Present(camera, joints, models, appearances, ball, hudVertices, hudColors, hudTextures, hudCounts, status);
        }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata) =>
            Audio.AudioSample(id, rate, channels, pcm, metadata);
        public void AudioCommands(string text) => Audio.AudioCommands(text);
    }

    private static void WriteAvRuntimeInventory(PlayerData data, string output)
    {
        GameSimulation game = KernelBinding.Create(data);
        Type type = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourceAnimationPrograms")!;
        var programs = new Dictionary<string, object>(StringComparer.Ordinal);
        void Merge(object values)
        {
            foreach (System.Collections.DictionaryEntry value in (System.Collections.IDictionary)values)
                programs[(string)value.Key] = value.Value!;
        }
        Merge(type.GetField("Programs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
        Merge(FlowGet(FlowModel(game), "SourceC2Programs"));
        object? Field(object value, string name) =>
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value);
        var sequences = new SortedSet<string>(StringComparer.Ordinal);
        var rows = programs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => {
            var instructions = ((Array)Field(pair.Value, "Instructions")!).Cast<object>().Select((instruction, pc) => {
                string op = Field(instruction, "Op")!.ToString()!;
                string? text = (string?)Field(instruction, "Text");
                if (op is "NewSequence" or "Interpolate") sequences.Add(text!);
                return new {
                    pc, op, text, a = Field(instruction, "A"), b = Field(instruction, "B"), c = Field(instruction, "C"),
                    x = Field(instruction, "X"), y = Field(instruction, "Y"), z = Field(instruction, "Z"),
                    target = Field(instruction, "Target"), alternateSequence = Field(instruction, "AlternateSequence"),
                    branchPc = Field(instruction, "BranchPc"), timer = Field(instruction, "Timer"),
                    sourceLine = Field(instruction, "SourceLine"), maximumYVelocity = Field(instruction, "MaximumYVelocity"),
                    maximumPlanarVelocity = Field(instruction, "MaximumPlanarVelocity"),
                    minimumPlanarVelocity = Field(instruction, "MinimumPlanarVelocity")
                };
            }).ToArray();
            return new { symbol = pair.Key, instructions };
        }).ToArray();
        var clips = sequences.Select(name => {
            try
            {
                ISourceAnimationClip clip = data.Animations.GetClip(name);
                return new { name, present = true, count = clip.SelectedSourceFrames.Count,
                    frames = clip.SelectedSourceFrames.ToArray(), source = (string?)clip.SourcePath, blob = (string?)clip.SourceGitBlobSha1 };
            }
            catch (Exception)
            {
                return new { name, present = false, count = 0, frames = Array.Empty<int>(),
                    source = (string?)null, blob = (string?)null };
            }
        }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schema = "actual-compiled-a1-v1-runtime-inventory/v1", manifestSha256 = data.Catalog.ManifestSha256,
            sourceProfile = data.Catalog.Profile.StableId, programs = rows, clips,
            cameras = data.Impact.Tables.Select(table => new {
                name = table.Key, entries = table.Value.Select(entry => new {
                    entry.Ordinal, entry.SourceLine, entry.RawMode, entry.ModeSymbol, entry.ModeTicks,
                    entry.TravelTicks, entry.IsNoSelection
                })
            }),
            kickoffCameras = data.NormalCamera.Kickoff.Select(entry => new {
                entry.Sequence.Ordinal, entry.Sequence.RawMode, entry.Sequence.ModeTicks, entry.Sequence.TravelTicks, entry.AutoHikeTicks
            }),
            fieldGoalCameras = data.NormalCamera.FieldGoal.Select(entry => new {
                entry.Ordinal, entry.RawMode, entry.ModeTicks, entry.TravelTicks
            }),
            soundBindings = (data.Catalog.Profile.IsCompleteAv306
                ? SourceOriginalSoundBindings.ForCompleteAv306(data.Animations)
                : SourceOriginalSoundBindings.ForCompleteOriginal303(data.Animations)).Bindings,
            scope = "Compiled capability inventory only. Production coverage must be joined to actual source-step execution evidence."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(clips.All(clip => clip.present), "Every implemented compiled animation sequence must bind an authentic admitted clip.");
        Console.WriteLine("PASS compiled A1/V1 capability inventory: " + rows.Length +
            " programs; explicit branches/callbacks/opcodes and actual selected-frame maps; not a production-coverage claim.");
    }

    private static void StartOriginalGuest(PlayerHost host)
    {
        void Raw(int buttons = 0, double x = 0) =>
            host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { buttons, 0, 0, 0 });
        void Tap(int buttons = 0, double x = 0)
        {
            Raw(); Raw(buttons, x); Raw(); CabinetRender(host);
            for (int tick = 0; tick < 70 && host.Session.UsingSourcePreGameInput; tick++)
            { Raw(); host.Advance(1d / 57, 512, 384, 512, 384, 1); }
        }
        CabinetRender(host);
        Tap(4);
        if (host.Session.Records?.Active == true) { Tap(x: 1); Tap(1); }
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams, "Public original guest choice reaches the real team screen.");
        Tap(1); Tap(1); Tap(1);
        Check(host.Session.Playing && host.Game.SourceTick == 0,
            "The original guest/team choices construct a fresh source-zero game, never a fixture epoch.");
        Raw(); Raw(); CabinetRender(host);
    }

    private sealed class AvExecutionCoverage
    {
        private sealed class Hook
        {
            internal readonly AvExecutionCoverage Owner;
            internal readonly object Layer;
            internal readonly Delegate Original;
            internal Hook(AvExecutionCoverage owner, object layer, Delegate original)
            { Owner = owner; Layer = layer; Original = original; }
            internal void Before<T>(T actor)
            {
                ((Action<T>)Original)(actor);
                string script = (string)Layer.GetType().GetProperty("ScriptSymbol", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Layer)!;
                int pc = (int)Layer.GetType().GetProperty("LogicalInstructionIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Layer)! - 1;
                if (!Owner.instructions.TryGetValue(script, out var positions))
                    Owner.instructions.Add(script, positions = new());
                if (!positions.TryGetValue(pc, out var cases)) positions.Add(pc, cases = new());
                cases.Add(Owner.Case);
                Owner.Before?.Invoke(Layer, actor!);
            }
        }
        private readonly Dictionary<string, Dictionary<int, SortedSet<string>>> instructions = new();
        private readonly Dictionary<string, SortedSet<int>> frames = new();
        private readonly Dictionary<string, SortedSet<string>> sounds = new();
        private readonly Dictionary<string, SortedSet<string>> cameras = new();
        internal string Case = "public-initial-kickoff";
        internal Action<object, object>? Before;
        internal AvExecutionCoverage(PlayerHost host)
        {
            foreach (object actor in (Array)FlowGet(FlowModel(host.Game), "Players"))
            {
                object animation = FlowGet(actor, "AnimationState");
                foreach (string name in new[] { "Primary", "Secondary" })
                {
                    object layer = FlowGet(animation, name);
                    FieldInfo before = layer.GetType().GetField("BeforeInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var hook = new Hook(this, layer, (Delegate)before.GetValue(layer)!);
                    MethodInfo method = typeof(Hook).GetMethod("Before", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(actor.GetType());
                    before.SetValue(layer, method.CreateDelegate(before.FieldType, hook));
                }
            }
            host.Game.SourceProcessSoundRaised += call => {
                if (call.CalleeFunction == null) return;
                if (!sounds.TryGetValue(call.CalleeFunction, out var cases)) sounds.Add(call.CalleeFunction, cases = new());
                cases.Add(Case + ":" + call.CalleeSourceLine);
            };
            host.Bridge.SourcePresentationCueRaised += cue => {
                if (cue is not SourceCallbackCue sound || sound.Kind != SourcePresentationCueKind.SoundCallback) return;
                if (!sounds.TryGetValue(sound.FunctionSymbol, out var cases)) sounds.Add(sound.FunctionSymbol, cases = new());
                cases.Add(Case + ":" + sound.Context.SourceLine);
            };
        }
        internal void Frame(PlayerHost host)
        {
            if (host.Camera.Playback.Frame.Active)
            {
                string table = host.Camera.Playback.Frame.Table;
                if (!cameras.TryGetValue(table, out var cases)) cameras.Add(table, cases = new());
                cases.Add(Case + ":" + host.Camera.Playback.Frame.Ordinal);
            }
            foreach (PlayerView actor in host.Game.State.Players)
            foreach (SourceAnimationLayer layer in new[] { actor.Animation.Primary, actor.Animation.Secondary })
            {
                if (!layer.IsSpecified) continue;
                if (!frames.TryGetValue(layer.ScriptSymbol, out var observed))
                    frames.Add(layer.ScriptSymbol, observed = new());
                observed.Add(layer.Sample.SourceFrame);
            }
        }
        internal void Save(string output) => File.WriteAllText(output, JsonSerializer.Serialize(new {
            schema = "actual-public-source-step-a1-v1-coverage/v1", instructions, frames, sounds, cameras,
            qualification = "Only instructions/frames observed in successful actual guest/Capture/Advance/SourceStep cases. Per-program unvisited branches are not certified."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifyOriginalGameplaySounds(PlayerData data, string? turnOutput = null, string? coverageOutput = null)
    {
        var sink = new CompleteAvSink();
        var options = PreviewOptions.FromUrl("", data.Catalog.Profile.StableId);
        SourcePlayerRecordStore? store = null;
        if (options.RecordsEnabled)
        {
            store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
                () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
            store.LoadAsync().GetAwaiter().GetResult();
        }
        var randomSites = new List<SourceRandomSite>();
        var random = new SourceInjectedRandom(site => { randomSites.Add(site); return 99; },
            new SourceRandomProvenance("original-gameplay-av-caller", "explicit raw tape, not target PRNG",
                "constant99", SourceRandomEvidence.RecordedOracle));
        var host = new PlayerHost(data, sink, observeRendering: true, options: options,
            randomFactory: _ => random, recordStore: store);
        StartOriginalGuest(host);
        AvExecutionCoverage? coverage = coverageOutput == null ? null : new AvExecutionCoverage(host);
        void Step(int buttons = 0, double x = 0)
        {
            long tick = host.Game.SourceTick;
            for (int n = 0; n < 3 && host.Game.SourceTick == tick; n++)
            {
                host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { buttons, 0, 0, 0 });
                CabinetRender(host, 1);
            }
            Check(host.Game.SourceTick == tick + 1, "The real public host executes exactly the next source wake.");
            coverage?.Frame(host);
        }
        Step();
        object model = FlowModel(host.Game);
        if (coverage != null) coverage.Case = "declared-pass-catch";
        var actors = (Array)FlowGet(model, "Players");
        var driver = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!;
        var place = driver.GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        FlowSet(model, "Phase", GamePhase.LiveBall);
        FlowSet(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        FlowSet(model, "SourceRules", new SourceRulesState(0, 0, 1, 30, 60, SourcePlayType.Normal));
        FlowSet(model, "SourceOffensePlay", data.Playbook.GetPlay("play_spider"));
        FlowSet(model, "SourceDefensePlay", data.Playbook.GetPlay("play_safe_cover"));
        FlowSet(model, "SourceFlags", SourceGameFlags.PassThrown);
        FlowSet(model, "SourceLastPossession", 0);
        FlowSet(model, "Possession", TeamId.Home);
        FlowSet(model, "SourceKickApproach", false);
        FlowSet(model, "CarrierId", -1);
        FlowSet(model, "BallKind", SourceBallKind.Pass);
        FlowSet(model, "BallMode", BallMode.InFlight);
        FlowSet(model, "PassTicksElapsed", 31);
        FlowSet(model, "PassTicksTotal", 70);
        FlowSet(model, "ThrowerId", 3);
        FlowSet(model, "TargetPlayerId", 4);
        FlowSet(model, "SourceBallFlags", Enum.ToObject(FlowGet(model, "SourceBallFlags").GetType(), 0));
        for (int id = 0; id < 14; id++)
        {
            object actor = actors.GetValue(id)!;
            FlowSet(actor, "SourceMode", SourceActorMode.Dead);
            FlowSet(actor, "SourceControllerStation", id == 3 ? 0 : -1);
            FlowSet(actor, "SourceRawFlags", id == 4 ? 0 : 0x20);
            FlowSet(actor, "SourceVelocity", default(FieldVector));
            FlowSet(actor, "SourceVerticalVelocity", 0f);
            FlowSet(actor, "NoCatchTicks", id == 4 ? 0 : 200);
            FlowSet(actor, "SourceHitPartnerId", -1);
            FlowSet(actor, "SourcePuppetLinkId", -1);
            place.Invoke(null, new object[] { actor, new SourceVector3(id == 4 ? 0f : -300 + id * 35, 0f, id == 4 ? 0f : 150f) });
        }
        ((int[])FlowGet(model, "SourceStationPlayers"))[0] = 3;
        FlowSet(model, "ControlledPlayerId", 3);
        object receiver = actors.GetValue(4)!;
        FlowSet(receiver, "SourceMode", SourceActorMode.Receiver);
        FlowSet(receiver, "Role", ActorRole.Receiver);
        var root = (SourceVector3)FlowGet(receiver, "SourceRoot");
        FlowSet(model, "SourceBallPosition", new SourceVector3(0, root.Y, 0));
        FlowSet(model, "SourceBallVelocity", default(FieldVector));
        FlowSet(model, "SourceBallVerticalVelocity", 0f);
        var calls = new List<SourceProcessSoundCall>();
        host.Game.SourceProcessSoundRaised += calls.Add;
        Step();
        long caughtTick = host.Game.SourceTick;
        Check(host.Game.State.Ball.CarrierId == 4 &&
            calls.Any(call => call.CalleeFunction == "catch_sounds" && call.CalleeSourceLine == 930 &&
                call.PlaylistId == 127 && call.Bank == "players" && call.RandomCalls == 0),
            "Actual standard_catch must emit the original catch127 through the executing source process.");
        Check(FlowProcesses(host).Processes.Any(p => p.Function == "catch_speech_proc" && p.Id == 0x0ccc),
            "The actual catch producer creates its original speech PID, not a directly dispatched test child.");
        for (int tick = 0; tick < 18; tick++) Step();
        Check(!calls.Any(call => call.Process == "catch_speech_proc"),
            "The original catch child must not speak before first-wake plus CONTROL_TIME.");
        Step();
        SourceProcessSoundCall spoken = calls.Single(call => call.CalleeFunction == "catch_speech_proc");
        Check(spoken.SourceTick == caughtTick + 19 && spoken.CalleeSourceLine == 996 &&
            spoken.PlaylistId == 84 && spoken.Volume == 225 && spoken.PriorityWord == 0xe28a &&
            spoken.RandomCalls == 3 && spoken.Process == "catch_speech_proc",
            "Source catch continuation preserves exact19-tick wake, crowd/chance/table draw order and short-catch84.");
        Check(randomSites.Any(site => site.SourceIdentity == "sounds.c:catch_speech_proc:984") &&
            randomSites.Any(site => site.SourceIdentity == "sounds.c:catch_speech_proc:996") &&
            host.ProcessSoundDeliveries.Any(call => ReferenceEquals(call, spoken)),
            "The same actual child output and original RNG sites reach the public host consumer.");
        Check(sink.Audio.Commands.Any(command => command.GetProperty("outputSequence").GetInt64() == spoken.OutputSequence &&
                command.GetProperty("kind").GetString() is "play" or "priority-reject") &&
            sink.Audio.HasNonzeroPcm && host.SourceProcessSoundCallsTaken == host.SourceProcessSoundCallsDelivered,
            "Original catch producer/child outputs reach real decoded PCM and exact playback/priority commands.");
        for (int tick = 0; tick < 89; tick++) Step();
        Check(FlowProcesses(host).Processes.Any(p => p.Function == "catch_speech_proc"),
            "Ordinary original catch speech keeps CATCH_SPEECH_PID alive for the full90-tick hold.");
        Step();
        Check(!FlowProcesses(host).Processes.Any(p => p.Function == "catch_speech_proc"),
            "The actual public scheduler retires catch speech exactly after its90-tick hold.");
        object quarterback = actors.GetValue(3)!;
        FlowSet(receiver, "SourceControllerStation", -1);
        FlowSet(quarterback, "SourceControllerStation", 0);
        ((int[])FlowGet(model, "SourceStationPlayers"))[0] = 3;
        FlowSet(model, "ControlledPlayerId", 3);
        FlowSet(model, "CarrierId", 3);
        FlowSet(model, "BallMode", BallMode.Held);
        FlowSet(model, "SourceFlags", SourceGameFlags.None);
        FlowSet(quarterback, "SourceMode", SourceActorMode.Quarterback);
        FlowSet(quarterback, "Role", ActorRole.Quarterback);
        place.Invoke(null, new object[] { quarterback, new SourceVector3(-200, 0, 0) });
        object animation = FlowGet(quarterback, "AnimationState");
        object primary = animation.GetType().GetField("Primary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(animation)!;
        object secondary = animation.GetType().GetField("Secondary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(animation)!;
        primary.GetType().GetMethod("ClearLowModes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(primary, null);
        secondary.GetType().GetMethod("ClearLowModes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(secondary, null);
        bool streamed = false;
        if (coverage != null) coverage.Case = "raw-turbo-qb";
        for (int tick = 0; tick < 10; tick++)
        {
            SourceAnimationLayer before = host.Game.State.Players[3].Animation.Primary;
            Step(4, 1);
            ActorAnimationView view = host.Game.State.Players[3].Animation;
            Check(view.Primary.ScriptSymbol.StartsWith("qb_t_", StringComparison.Ordinal) && view.Secondary.IsSpecified &&
                view.Primary.JointMask == SourceTiming.LowerBodyJoints && view.Secondary.JointMask == SourceTiming.UpperBodyJoints,
                "Actual cabinet turbo QB selects source directional streaming programs on independent torso/legs.");
            if ((before.SourceModeBits & 0x80000) == 0 && (before.SourceModeBits & 8) == 0 &&
                (view.Primary.SourceModeBits & 8) != 0 && before.Sample.SequenceName == view.Primary.Sample.SequenceName)
            {
                ISourceAnimationClip clip = data.Animations.GetClip(before.Sample.SequenceName);
                int ordinal = clip.SelectedSourceFrames.ToList().IndexOf(before.Sample.SourceFrame);
                int frames = clip.SelectedSourceFrames.Count - 1 - ordinal;
                int ticks = (int)(.5f + .81f * frames);
                float expected = (int)((ordinal + (float)frames / ticks) * 10000f) / 10000f;
                int expectedOrdinal = (int)expected;
                float fraction = expected - expectedOrdinal;
                if (Math.Abs(fraction) < .05f) fraction = 0;
                Check(view.Primary.Sample.SourceFrame == clip.SelectedSourceFrames[expectedOrdinal] &&
                    view.Primary.Sample.Interpolation01 == fraction,
                    "Real QB stream entry uses original .81 ticks/frame, selected-frame map and four-decimal truncation.");
                streamed = true;
            }
        }
        Check(streamed, "Real turbo QB must execute its stream entry, not remain a static/interpolated pose.");
        SourceJointPose lowerOrigin = SourceAnimationSampling.SampleLayer(data.Animations,
            host.Game.State.Players[3].Animation.Primary);
        if (turnOutput != null)
        {
            object directions = FlowGet(quarterback, "SourceDirections");
            MethodInfo sine = directions.GetType().GetMethod("Sin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var quarterSine = Enumerable.Range(0, 257)
                .Select(index => (float)sine.Invoke(directions, new object[] { index })!).ToArray();
            MethodInfo turn = primary.GetType().GetMethod("TurnTransitionOrigin", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cases = new[] { -1025, -513, -1, 1, 3, 257, 511, 1024 }.Select(heading => {
                var actual = (SourceJointPose)turn.Invoke(primary, new object[] { lowerOrigin, heading })!;
                float[][] Values(SourceJointPose pose) => pose.Rotations.Select(q => new[] { q.X, q.Y, q.Z, q.W }).ToArray();
                return new { heading, source = Values(lowerOrigin), actual = Values(actual) };
            }).ToArray();
            File.WriteAllText(turnOutput, JsonSerializer.Serialize(new {
                schema = "actual-original-turn-operation-inputs/v1",
                sourceTick = host.Game.SourceTick, actor = 3, quarterSine, cases
            }));
        }
        FlowSet(quarterback, "SourceMode", SourceActorMode.Receiver);
        FlowSet(quarterback, "Role", ActorRole.Receiver);
        if (coverage != null) coverage.Case = "neutral-carrier-two-part-collapse";
        Step();
        ActorAnimationView stopped = host.Game.State.Players[3].Animation;
        Check(stopped.Primary.ScriptSymbol == "stance_wb_anim_int" && !stopped.Secondary.IsSpecified &&
            stopped.Primary.TransitionFromPose != null,
            "The actual neutral carrier callback collapses two-part running through the source stance transition.");
        for (int joint = 0; joint < 15; joint++)
        {
            SourceRotation expected = lowerOrigin.Rotations[joint], actual = stopped.Primary.TransitionFromPose!.Rotations[joint];
            Check(expected.X == actual.X && expected.Y == actual.Y && expected.Z == actual.Z && expected.W == actual.W,
                "Original change_anim retains primary VM pose, not an invented composite torso carryover: joint" + joint);
        }
        FlowSet(quarterback, "SourceMode", SourceActorMode.Quarterback);
        FlowSet(quarterback, "Role", ActorRole.Quarterback);
        FlowSet(receiver, "SourceJob", Enum.ToObject(FlowGet(receiver, "SourceJob").GetType(), 0));
        place.Invoke(null, new object[] { receiver, new SourceVector3(-120, 0, 0) });
        if (coverage != null) coverage.Case = "raw-B-throw";
        Step(2);
        Check(host.Game.State.Players[3].Source.Input.IsSpecified &&
            (host.Game.State.Players[3].Source.Input.Pressed & 2) != 0,
            "The public source snapshot observes the actual raw throw edge, not a test-injected sound call.");
        for (int tick = 0; tick < 40 && !calls.Any(call => call.CalleeFunction == "throw_ball_sounds"); tick++) Step();
        Check(calls.Any(call => call.CalleeFunction == "throw_ball_sounds"),
            "Actual raw B must release; source phase=" + host.Game.State.Phase + ";carrier=" + host.Game.State.Ball.CarrierId +
            ";script=" + host.Game.State.Players[3].Animation.Primary.ScriptSymbol +
            ";pending=" + FlowGet(quarterback, "PendingThrowTarget") + ";receiver mode/job=" +
            FlowGet(receiver, "SourceMode") + "/" + FlowGet(receiver, "SourceJob"));
        SourceProcessSoundCall release = calls.First(call => call.CalleeFunction == "throw_ball_sounds");
        Check(release.CalleeSourceLine == 1021 && release.Bank == "players" &&
            release.Volume == 205 && release.PriorityWord == 5 && release.RandomCalls == 1 &&
            randomSites.Any(site => site.SourceIdentity == "sounds.c:throw_ball_sounds:1021") &&
            host.ProcessSoundDeliveries.Any(call => ReferenceEquals(call, release)),
            "Actual raw B drives the original throw animation release, original sound draw and same-object PCM transport.");
        Check(sink.Audio.Commands.Any(command => command.GetProperty("outputSequence").GetInt64() == release.OutputSequence &&
            command.GetProperty("kind").GetString() is "play" or "priority-reject"),
            "The real animation release's throw sound reaches the production playback consumer.");
        Check(host.LatestRenderObservation?.CurrentSourceTick == host.Game.SourceTick &&
            host.LatestRenderObservation.Actors.Count == 14,
            "The same source wakes reach the actual fourteen-actor pose/matrix renderer.");
        if (coverageOutput != null) coverage!.Save(coverageOutput);
        Console.WriteLine("PASS original gameplay audio callers: " + assertions +
            " assertions; public guest/Capture/Advance/SourceStep, catch127 +19/90-tick speech84, .81 turbo QB, primary-only two-part collapse, PCM/playback and14-actor matrices. Declared ball/actor-placement fixtures; no natural-match/DSP/pixel identity claim.");
    }

    private static void VerifyOriginalPeriodAudio(PlayerData data)
    {
        Check(data.Catalog.Profile.IsCompleteAv306 && data.AudioAdmission != null,
            "The original assigned team-name programme needs its distinct, fully pinned AV306 admission.");
        var options = PreviewOptions.FromUrl("", data.Catalog.Profile.StableId);
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        var sink = new CompleteAvSink();
        var host = new PlayerHost(data, sink, observeRendering: true, options: options, recordStore: store);
        StartOriginalGuest(host);
        void Step(int buttons = 0)
        {
            long tick = host.Game.SourceTick;
            for (int attempt = 0; attempt < 3 && host.Game.SourceTick == tick; attempt++)
            {
                host.Capture(new double[8], new[] { buttons, 0, 0, 0 });
                CabinetRender(host, 1);
            }
            Check(host.Game.SourceTick == tick + 1, "The public host must execute exactly one original source wake.");
        }
        Step();
        object model = FlowModel(host.Game);
        FlowSet(model, "Quarter", 4);
        FlowSet(model, "SourcePeriodView", new SourcePeriodView(3));
        FlowSet(model, "HomeScore", 7);
        FlowSet(model, "AwayScore", 0);
        var scores = (int[,])FlowGet(model, "SourceQuarterScores");
        Array.Clear(scores); scores[3, 0] = 7;
        FlowSet(model, "SourceRules", new SourceRulesState(0, 0, 2, 40, 70, SourcePlayType.Normal));
        FlowSet(model, "SourceOffensePlay", data.Playbook.GetPlay("play_spider"));
        FlowSet(model, "SourceDefensePlay", data.Playbook.GetPlay("play_safe_cover"));
        FlowSet(model, "Possession", TeamId.Home);
        FlowSet(model, "SourceLastPossession", 0);
        FlowSet(model, "SourceLastResult", SourcePlayResult.Down);
        FlowSet(model, "SourceFlags", SourceGameFlags.None);
        FlowSet(model, "SourceKickApproach", false);
        FlowSet(model, "SourceClock", SourceClock.Create(0, firstWakeTick: host.Game.SourceTick + 1));
        FlowSet(model, "QuarterEndEmitted", false);
        FlowSet(model, "ClockActive", true);
        FlowSet(model, "Phase", GamePhase.LiveBall);
        FlowSet(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        FlowSet(model, "BallKind", SourceBallKind.Pass);
        FlowSet(model, "BallMode", BallMode.Held);
        FlowSet(model, "CarrierId", 4);
        FlowSet(model, "CatchSourceTick", host.Game.SourceTick - 18);
        Array actors = (Array)FlowGet(model, "Players");
        foreach (object actor in actors)
        {
            FlowSet(actor, "SourceMode", SourceActorMode.Dead);
            FlowSet(actor, "NoCatchTicks", 200);
        }
        object carrier = actors.GetValue(4)!;
        FlowSet(carrier, "SourceMode", SourceActorMode.Receiver);
        FlowSet(carrier, "Role", ActorRole.Receiver);
        FlowSet(carrier, "SourceJob", Enum.ToObject(FlowGet(carrier, "SourceJob").GetType(), 0));
        typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!
            .GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { carrier, new SourceVector3(0, 0, 200) });
        var calls = new List<SourceProcessSoundCall>();
        host.Game.SourceProcessSoundRaised += calls.Add;
        long firstWake = -1, birth = -1;
        for (int tick = 0; tick < 700 && !calls.Any(call => call.CalleeFunction == "eoq_speech_proc"); tick++)
        {
            Step(tick == 0 ? 0 : 7);
            foreach (SourceProcessView process in FlowProcesses(host).Processes.Where(p => p.Function == "eoq_speech_proc"))
            {
                birth = process.BirthTick;
                if (process.LastWakeTick >= 0 && firstWake < 0) firstWake = process.LastWakeTick;
            }
        }
        SourceProcessSoundCall[] names = calls.Where(call => call.Bank == "Cardinal" && call.PlaylistId == 1).ToArray();
        Check(names.Length == 1,
            "A real out-of-bounds result, result wait and EOQ scheduler must produce exactly one assigned cardinal1; actual=" +
            names.Length + "/" + host.Game.State.Phase + "/" + ((ISourceMatchState)host.Game.State).Match.Phase +
            ";teams=" + host.Session.CreateConfiguration().HomeTeam.TeamIndex + "/" +
            host.Session.CreateConfiguration().AwayTeam.TeamIndex + ";eoq=" +
            string.Join(",", calls.Where(call => call.CalleeFunction == "eoq_speech_proc").Select(call =>
                call.SourceTick + ":" + call.Bank + ":" + call.PlaylistId + ":" + call.CalleeSourceLine)));
        SourceProcessSoundCall name = names[0];
        Check(name.Process == "eoq_speech_proc" && name.CalleeFunction == "eoq_speech_proc" &&
            name.CalleeSourceLine == 691 && name.Volume == 255 && name.Pan == 127 && name.PriorityWord == 0xe280 &&
            name.RandomCalls == 0 && firstWake >= birth && name.SourceTick == firstWake + 5,
            "Original sounds.c691 must follow its actual first wake/sleep5 with exact team-name operands and no RNG.");
        Check(host.ProcessSoundDeliveries.Any(call => ReferenceEquals(call, name)),
            "The actual child output, not a directly dispatched duplicate, must reach the production Host consumer.");
        JsonElement[] plays = sink.Audio.Commands.Where(command =>
            command.GetProperty("kind").GetString() == "play" &&
            command.GetProperty("outputSequence").GetInt64() == name.OutputSequence).ToArray();
        const string pcm = "pcm16-c24cbe7710c6b01ac8c91090a03624446178604bc75c5f3fda30d5d211428825";
        Check(plays.Length == 1 && plays[0].GetProperty("sampleId").GetString() == pcm &&
            plays[0].GetProperty("sampleCount").GetInt32() == 88 * 240 &&
            plays[0].GetProperty("track").GetInt32() == 5 && !plays[0].GetProperty("loop").GetBoolean() &&
            plays[0].GetProperty("packageSha256").GetString() == "a042c7c07b4e6fc824c499dc050b6f4026d8a64fc8b53ab044e85d649bbd203a" &&
            sink.Audio.Samples.TryGetValue(pcm, out int bytes) && bytes == 88 * 240 * 2,
            "The actual assigned programme must reach track5 and its exact independently executed PCM, never another slot or silence.");
        for (int tick = 0; tick < 39; tick++) Step();
        Check(!calls.Any(call => call.CalleeFunction == "eoq_speech_proc" && call.PlaylistId == 142),
            "Original TCNV(36.0f) must hold the real winning-team child for its full40 source ticks.");
        Step();
        SourceProcessSoundCall win = calls.Single(call => call.CalleeFunction == "eoq_speech_proc" && call.PlaylistId == 142);
        Check(win.SourceTick == name.SourceTick + 40 && win.Bank == "tkperm" && win.CalleeSourceLine == 700 &&
            win.RandomCalls == 0 && host.ProcessSoundDeliveries.Any(call => ReferenceEquals(call, win)),
            "The actual same child must emit WIN_THE_GAME142 only after the original40-tick continuation.");
        long voice = plays[0].GetProperty("voiceId").GetInt64(), start = plays[0].GetProperty("atSample").GetInt64();
        Check(sink.Audio.Commands.Any(command => command.GetProperty("kind").GetString() == "stop" &&
            command.GetProperty("voiceId").GetInt64() == voice && command.GetProperty("reason").GetString() == "completed" &&
            command.GetProperty("atSample").GetInt64() == start + 88 * 240),
            "The real programme must stop on its exact independently qualified finite PCM boundary.");
        Check(host.Camera.Trace.Any(row => JsonSerializer.SerializeToElement(row).TryGetProperty("table", out var table) &&
            table.GetString() == "csi_fs_endqtr") && host.LatestRenderObservation?.CurrentSourceTick == host.Game.SourceTick,
            "The real EOQ selection must reach the original camera and the same-wake production renderer on AV306.");
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "actual-public-original-eoq-cardinal-pcm-20261110/v1", assertions,
            manifestSha256 = data.Catalog.ManifestSha256, profile = data.Catalog.Profile.StableId,
            directedCarrierOutOfBoundsAndPeriodFixture = true, directEndOrSoundDispatch = false,
            naturalMatchOrNativeDspClaimed = false, birth, firstWake, name.SourceTick,
            name.OutputSequence, name.ProcessSerial, name.DispatchOrder, name.SubcallOrder,
            name.Bank, name.PlaylistId, pcm, samples = 88 * 240, track = 5, winTick = win.SourceTick,
            sameActualOutputDelivered = true, finiteStopAtSample = start + 88 * 240,
            frameTick = host.LatestRenderObservation?.CurrentSourceTick
        }));
    }

    private sealed class AnimationInstructionObserver
    {
        internal readonly Dictionary<string, SortedSet<int>> Instructions = new();
        internal object Layer = null!;
        internal void Before<T>(T actor)
        {
            Type type = Layer.GetType();
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            string script = (string)type.GetProperty("ScriptSymbol", hidden)!.GetValue(Layer)!;
            int pc = (int)type.GetProperty("LogicalInstructionIndex", hidden)!.GetValue(Layer)! - 1;
            if (!Instructions.TryGetValue(script, out var positions)) Instructions.Add(script, positions = new());
            positions.Add(pc);
        }
    }

    private static void ExportOriginalAnimationCore(PlayerData data, string output)
    {
        GameSimulation game = KernelBinding.Create(data);
        object model = FlowModel(game);
        object originalActor = ((Array)FlowGet(model, "Players")).GetValue(3)!;
        object originalState = FlowGet(originalActor, "AnimationState");
        object originalLayer = FlowGet(originalState, "Primary");
        object interpolation = FlowGet(originalLayer, "poseInterpolation");
        object directions = FlowGet(originalActor, "SourceDirections");
        object extension = FlowGet(model, "SourceC2Programs");
        Type actorType = originalActor.GetType(), stateType = originalState.GetType();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var cases = new List<object>();
        var clips = new SortedSet<string>(StringComparer.Ordinal);
        object Property(object target, string name) =>
            target.GetType().GetProperty(name, hidden)!.GetValue(target)!;
        object[] Pose(SourceJointPose pose) => pose.Rotations.Select(q => (object)new[] { q.X, q.Y, q.Z, q.W }).ToArray();
        void Case(string name, string script, int ticks, int randomValue = 0, int interrupt = -1,
            bool torso = false, float scale = 1f)
        {
            object actor = Activator.CreateInstance(actorType, hidden, null, new object[] { 3 }, null)!;
            object state = Activator.CreateInstance(stateType, hidden, null,
                new object[] { data.Animations, 3, extension }, null)!;
            FlowSet(actor, "AnimationState", state);
            FlowSet(actor, "SourceDirections", directions);
            FlowSet(actor, "ModelScale", scale);
            FlowSet(actor, "Facing", 137);
            FlowSet(actor, "TargetFacing", 137);
            FlowSet(actor, "UpperFacing", 137);
            FlowSet(actor, "TargetUpperFacing", 137);
            FlowSet(actor, "SourceCoordinates", true);
            FlowSet(actor, "SourcePosition", new FieldVector(4, -7));
            FlowSet(actor, "SourceHeight", 1.5f);
            FlowSet(actor, "SourceInPlay", false);
            stateType.GetMethod("BindSourcePoseInterpolation", hidden)!.Invoke(state, new[] { interpolation });
            stateType.GetMethod("BindOriginalChangeSemantics", hidden)!.Invoke(state, null);
            foreach (string part in new[] { "Primary", "Secondary" })
                FlowSet(FlowGet(state, part), "SourceFrameInterpolationRoots", true);
            var observers = new List<AnimationInstructionObserver>();
            foreach (string part in new[] { "Primary", "Secondary" })
            {
                var observer = new AnimationInstructionObserver { Layer = FlowGet(state, part) };
                FieldInfo before = observer.Layer.GetType().GetField("BeforeInstruction", hidden)!;
                before.SetValue(observer.Layer, typeof(AnimationInstructionObserver).GetMethod("Before", hidden)!
                    .MakeGenericMethod(actorType).CreateDelegate(before.FieldType, observer));
                observers.Add(observer);
            }
            var actions = new List<object>();
            int randomCalls = 0;
            Func<int, int> random = bound => { randomCalls++; return randomValue % bound; };
            object Layer(object layer)
            {
                string sequence = (string)Property(layer, "SequenceName");
                clips.Add(sequence);
                return new {
                    script = Property(layer, "ScriptSymbol"), sequence,
                    ordinal = Property(layer, "FrameOrdinal"), mode = Property(layer, "Mode"),
                    hold = FlowGet(layer, "hold"), floatingFrame = FlowGet(layer, "floatingFrame"),
                    streamCount = FlowGet(layer, "streamCount"), streamTicks = FlowGet(layer, "streamTicks"),
                    transitionCount = Property(layer, "RawTransitionCount"),
                    transitionTicks = Property(layer, "RawTransitionTicks"),
                    sourceOrdinal = Property(layer, "RawTransitionSourceOrdinal"),
                    sourceSequence = Property(layer, "RawTransitionSourceSequenceName"),
                    pose = Pose((SourceJointPose)layer.GetType().GetMethod("EvaluatePose", hidden)!.Invoke(layer, null)!)
                };
            }
            void Record(string kind, long tick, string? selected = null, int vcount = 4, int turn = 0)
            {
                var root = (SourceVector3)FlowGet(actor, "SourceRoot");
                var position = (FieldVector)FlowGet(actor, "SourcePosition");
                bool twoPart = (bool)Property(state, "TwoPart");
                actions.Add(new {
                    kind, tick, script = selected, vcount, turn, randomCalls,
                    primary = Layer(FlowGet(state, "Primary")),
                    secondary = twoPart ? Layer(FlowGet(state, "Secondary")) : null,
                    root = new[] { root.X, root.Y, root.Z },
                    position = new[] { position.X, (float)FlowGet(actor, "SourceHeight"), position.Z }
                });
            }
            void Full(string selected, long tick)
            {
                stateType.GetMethod("FullBody", hidden)!.Invoke(state,
                    new object[] { selected, actor, tick, random, AnimationSelectionEvidence.SourceVerified });
                Record("full", tick, selected);
            }
            Full("qb_stance_anim", 0);
            if (interrupt >= 0)
            {
                FlowSet(FlowGet(state, "Primary"), "VariableFrame", 8);
                FlowSet(FlowGet(state, "Primary"), "VariableCount", 2);
                stateType.GetMethod("LowerBody", hidden)!.Invoke(state, new object[] { "qb_run_anim", actor, 1L, random });
                Record("lower", 1, "qb_run_anim", 2);
                for (long tick = 2; tick <= 9; tick++)
                {
                    stateType.GetMethod("Advance", hidden, null, new[] { actorType, typeof(long), typeof(Func<int, int>) }, null)!
                        .Invoke(state, new object[] { actor, tick, random });
                    Record("advance", tick);
                }
                Full("breath_anim", 10);
                for (long tick = 11; tick < 10 + interrupt; tick++)
                {
                    stateType.GetMethod("Advance", hidden, null, new[] { actorType, typeof(long), typeof(Func<int, int>) }, null)!
                        .Invoke(state, new object[] { actor, tick, random });
                    Record("advance", tick);
                }
                FlowSet(FlowGet(state, "Primary"), "VariableFrame", -1);
                FlowSet(FlowGet(state, "Primary"), "VariableCount", 4);
                Full(script, 10 + interrupt);
                for (long tick = 11 + interrupt; tick < 11 + interrupt + ticks; tick++)
                {
                    stateType.GetMethod("Advance", hidden, null, new[] { actorType, typeof(long), typeof(Func<int, int>) }, null)!
                        .Invoke(state, new object[] { actor, tick, random });
                    Record("advance", tick);
                }
            }
            else if (torso)
            {
                stateType.GetMethod("BeginTwoPart", hidden)!.Invoke(state, new[] { actor });
                var lower = FlowGet(state, "Primary"); var upper = FlowGet(state, "Secondary");
                FlowSet(lower, "VariableCount", 2); FlowSet(lower, "VariableFrame", -1);
                stateType.GetMethod("LowerBody", hidden)!.Invoke(state, new object[] { script, actor, 1L, random });
                Record("begin-lower", 1, script, 2);
                FlowSet(upper, "VariableCount", 4); FlowSet(upper, "VariableFrame", -1);
                uint mode = (uint)Property(upper, "Mode");
                upper.GetType().GetMethod("SetMovementMode", hidden)!.Invoke(upper, new object[] { mode | 0x00200000u });
                upper.GetType().GetMethod("SetMovementTurnHack", hidden)!.Invoke(upper, new object[] { -513 });
                stateType.GetMethod("UpperBody", hidden)!.Invoke(state, new object[] {
                    "qb_t_runl_anim", actor, 1L, random, AnimationSelectionEvidence.SourceVerified });
                Record("upper", 1, "qb_t_runl_anim", 4, -513);
                for (long tick = 2; tick <= ticks; tick++)
                {
                    stateType.GetMethod("Advance", hidden, null, new[] { actorType, typeof(long), typeof(Func<int, int>) }, null)!
                        .Invoke(state, new object[] { actor, tick, random });
                    Record("advance", tick);
                }
            }
            else
            {
                Full(script, 1);
                for (long tick = 2; tick <= ticks; tick++)
                {
                    stateType.GetMethod("Advance", hidden, null, new[] { actorType, typeof(long), typeof(Func<int, int>) }, null)!
                        .Invoke(state, new object[] { actor, tick, random });
                    Record("advance", tick);
                    var root = (SourceVector3)FlowGet(actor, "SourceRoot");
                    stateType.GetMethod("Advance", hidden, null, new[] { actorType, typeof(long), typeof(Func<int, int>) }, null)!
                        .Invoke(state, new object[] { actor, tick, random });
                    Check(root.Equals(FlowGet(actor, "SourceRoot")),
                        "A repeated same-source-tick observer must not advance a frame or apply root drift twice.");
                }
            }
            cases.Add(new { name, scale, heading = 137, randomValue, actions,
                instructionCoverage = observers.Select(observer => observer.Instructions).ToArray() });
        }
        Case("block-ready-skip-goto", "block_ready_anim_rnd", 190);
        Case("ordinary-frame-loop", "stance3pt_anim", 170);
        Case("random-last-entry", "stance3pt_anim", 100, 59);
        Case("slow-intstream-minus-one", "center_set_anim", 240, scale: 1.5f);
        Case("inttoseq-first-middle-last-loop", "breath_anim", 125, scale: 1.5f);
        Case("hold-waittrue", "run_hold_anim", 20);
        Case("normal-qb-loops", "qb_run_anim", 82);
        Case("turbo-qb-081-loops", "qb_t_run_anim", 82);
        Case("interrupted-before-half", "qb_t_run_anim", 40, interrupt: 3);
        Case("interrupted-after-half", "qb_t_run_anim", 40, interrupt: 11);
        Case("anchor-xz-y", "stance3pt_jumpback_anim", 150, scale: 1.5f);
        Case("independent-torso-turn-stream", "qb_run_anim", 90, torso: true);
        MethodInfo sine = directions.GetType().GetMethod("Sin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schema = "actual-original-animation-vm-observations/v1",
            manifestSha256 = data.Catalog.ManifestSha256, profile = data.Catalog.Profile.StableId,
            quarterSine = Enumerable.Range(0, 257).Select(index => (float)sine.Invoke(directions, new object[] { index })!).ToArray(),
            clips = clips.Select(name => {
                ISourceAnimationClip clip = data.Animations.GetClip(name);
                return new { name, flags = clip.SourceFlags, clip.SourcePath, clip.SourceGitBlobSha1,
                    clip.DecodedSha256, selectedSourceFrames = clip.SelectedSourceFrames,
                    frames = clip.SelectedSourceFrames.Select(frame => {
                        SourceAnimationFrame value = clip.GetFrame(frame);
                        return new { root = new[] { value.ScaledRoot.X, value.ScaledRoot.Y, value.ScaledRoot.Z }, pose = Pose(value.Pose) };
                    })
                };
            }), cases
        }));
        Console.WriteLine("EXPORTED actual original animation VM cases=" + cases.Count +
            "; real admitted clips, complete programs, full per-wake pose/root/frame trace; original-C comparison still required.");
    }

    private static void VerifyOriginalAnimationCallers(PlayerData data, string output)
    {
        var sink = new CompleteAvSink();
        var options = PreviewOptions.FromUrl("", data.Catalog.Profile.StableId);
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        var host = new PlayerHost(data, sink, observeRendering: true, options: options, recordStore: store);
        StartOriginalGuest(host);
        void Step(double x = 0, double y = 0, int buttons = 0)
        {
            long tick = host.Game.SourceTick;
            for (int attempt = 0; attempt < 3 && host.Game.SourceTick == tick; attempt++)
            {
                host.Capture(new[] { x, y, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { buttons, 0, 0, 0 });
                CabinetRender(host, 1);
            }
            Check(host.Game.SourceTick == tick + 1, "The original selector case requires the actual next public source wake.");
        }
        Step();
        object model = FlowModel(host.Game);
        Array actors = (Array)FlowGet(model, "Players");
        MethodInfo place = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!
            .GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        FlowSet(model, "Phase", GamePhase.LiveBall);
        FlowSet(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        FlowSet(model, "SourceRules", new SourceRulesState(0, 0, 2, 40, 70, SourcePlayType.Normal));
        FlowSet(model, "SourceOffensePlay", data.Playbook.GetPlay("play_spider"));
        FlowSet(model, "SourceDefensePlay", data.Playbook.GetPlay("play_safe_cover"));
        FlowSet(model, "SourceFlags", SourceGameFlags.None);
        FlowSet(model, "SourceLastPossession", 0);
        FlowSet(model, "Possession", TeamId.Home);
        FlowSet(model, "SourceKickApproach", false);
        FlowSet(model, "CarrierId", 3);
        FlowSet(model, "BallKind", SourceBallKind.Pass);
        FlowSet(model, "BallMode", BallMode.Held);
        foreach (object actor in actors)
        {
            FlowSet(actor, "SourceMode", SourceActorMode.Dead);
            FlowSet(actor, "SourceControllerStation", -1);
            FlowSet(actor, "NoCatchTicks", 200);
        }
        var coverage = new AvExecutionCoverage(host);
        object blocker = actors.GetValue(0)!;
        FlowSet(blocker, "SourceMode", SourceActorMode.LinePreSnap);
        FlowSet(blocker, "Role", ActorRole.Lineman);
        FlowSet(blocker, "SourceControllerStation", 0);
        ((int[])FlowGet(model, "SourceStationPlayers"))[0] = 0;
        FlowSet(model, "ControlledPlayerId", 0);
        place.Invoke(null, new object[] { blocker, new SourceVector3(-150, 0, 0) });
        coverage.Case = "actual-line-presnap-to-jumpback-to-block-ready";
        bool jumpback = false, restarted = false;
        for (int tick = 0; tick < 160 && !restarted; tick++)
        {
            Step(); coverage.Frame(host);
            SourceAnimationLayer layer = host.Game.State.Players[0].Animation.Primary;
            jumpback |= layer.ScriptSymbol == "stance3pt_jumpback_anim";
            if (layer.ScriptSymbol != "block_ready_anim_rnd") continue;
            restarted = true;
            Check(layer.Sample.SourceFrame == data.Animations.GetClip("BLK_RDY").SelectedSourceFrames[0],
                "Actual LinePreSnap caller→jumpback→block_ready must skip50 then original GOTO/NEWSEQ and display frame0, not50.");
        }
        Check(jumpback && restarted,
            "The original role producer, not a direct animation start, must traverse the complete jumpback/block-ready branch.");
        FlowSet(blocker, "SourceMode", SourceActorMode.Dead);
        FlowSet(blocker, "SourceControllerStation", -1);
        object quarterback = actors.GetValue(3)!;
        FlowSet(quarterback, "SourceControllerStation", 0);
        ((int[])FlowGet(model, "SourceStationPlayers"))[0] = 3;
        FlowSet(model, "ControlledPlayerId", 3);
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        int streamingEntries = 0;
        var streamStarts = new List<(object Layer, int Ordinal, int Frames, int Ticks, string Script)>();
        coverage.Before = (layer, actor) => {
            if (!ReferenceEquals(actor, quarterback)) return;
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            string script = (string)layer.GetType().GetProperty("ScriptSymbol", hidden)!.GetValue(layer)!;
            if (!script.StartsWith("qb_t_", StringComparison.Ordinal)) return;
            int pc = (int)layer.GetType().GetProperty("LogicalInstructionIndex", hidden)!.GetValue(layer)! - 1;
            object instruction = ((Array)FlowGet(FlowGet(layer, "program"), "Instructions")).GetValue(pc)!;
            if (FlowGet(instruction, "Op").ToString() != "StreamToEnd") return;
            int ordinal = (int)layer.GetType().GetProperty("FrameOrdinal", hidden)!.GetValue(layer)!;
            string sequence = (string)layer.GetType().GetProperty("SequenceName", hidden)!.GetValue(layer)!;
            int frames = data.Animations.GetClip(sequence).SelectedSourceFrames.Count - 1 - ordinal;
            if (frames > 0) streamStarts.Add((layer, ordinal, frames, (int)(.5f + .81f * frames), script));
        };
        var rows = new List<object>();
        foreach (bool turbo in new[] { false, true })
        foreach (var direction in new[] { (0d, 1d), (1d, 1d), (1d, 0d), (1d, -1d),
            (0d, -1d), (-1d, -1d), (-1d, 0d), (-1d, 1d) })
        {
            FlowSet(model, "SourceRules", new SourceRulesState(0, 0, 2, 70, 95, SourcePlayType.Normal));
            FlowSet(model, "SourceFlags", SourceGameFlags.None);
            FlowSet(quarterback, "SourceMode", SourceActorMode.Quarterback);
            FlowSet(quarterback, "Role", ActorRole.Quarterback);
            FlowSet(quarterback, "SourceVelocity", default(FieldVector));
            FlowSet(quarterback, "SourceVerticalVelocity", 0f);
            ((float[])FlowGet(model, "SourceStationTurbo"))[0] = 100f;
            place.Invoke(null, new object[] { quarterback, new SourceVector3(-150, 0, 0) });
            coverage.Case = "actual-qb-" + (turbo ? "turbo-" : "normal-") + direction;
            for (int tick = 0; tick < 55; tick++)
            {
                streamStarts.Clear();
                Step(direction.Item1, direction.Item2, turbo ? 4 : 0); coverage.Frame(host);
                ActorAnimationView view = host.Game.State.Players[3].Animation;
                selected.Add(view.Primary.ScriptSymbol);
                if (view.Secondary.IsSpecified) selected.Add(view.Secondary.ScriptSymbol);
                Check(view.Primary.ScriptSymbol.StartsWith(turbo ? "qb_t_" : "qb_", StringComparison.Ordinal) &&
                    (turbo || !view.Primary.ScriptSymbol.StartsWith("qb_t_", StringComparison.Ordinal)) &&
                    view.Secondary.IsSpecified && view.Primary.JointMask == SourceTiming.LowerBodyJoints &&
                    view.Secondary.JointMask == SourceTiming.UpperBodyJoints,
                    "The actual QB role and raw turbo operand must select independent authentic torso/leg programmes: " +
                    JsonSerializer.Serialize(new { turbo, x = direction.Item1, y = direction.Item2, tick, script = view.Primary.ScriptSymbol,
                        torso = view.Secondary.ScriptSymbol, lowerMask = view.Primary.JointMask,
                        upperMask = view.Secondary.JointMask, mode = FlowGet(quarterback, "SourceMode").ToString(),
                        role = host.Game.State.Players[3].Role, phase = host.Game.State.Phase,
                        stick = FlowGet(quarterback, "SourceStick"),
                        turboLeft = ((float[])FlowGet(model, "SourceStationTurbo"))[0] }));
                foreach (var entry in streamStarts)
                {
                    float expected = (int)((entry.Ordinal + (float)entry.Frames / entry.Ticks) * 10000f) / 10000f;
                    Check((float)FlowGet(entry.Layer, "floatingFrame") == expected &&
                        (int)FlowGet(entry.Layer, "streamTicks") == entry.Ticks,
                        "The actual QB selector's source-observed stream entry must execute original0.81 timing/truncation: " +
                        entry.Script + "/" + entry.Ordinal + "/" + entry.Frames + "/" + entry.Ticks);
                    streamingEntries++;
                }
            }
            rows.Add(new { turbo, x = direction.Item1, y = direction.Item2,
                sourceTick = host.Game.SourceTick, animation = host.Game.State.Players[3].Animation });
        }
        Check(streamingEntries >= 8, "Each actual turbo-QB direction must execute at least its independently qualified0.81 stream entry.");
        Step();
        Check(!host.Game.State.Players[3].Animation.Secondary.IsSpecified &&
            host.Game.State.Players[3].Animation.Primary.ScriptSymbol.Contains("stance", StringComparison.Ordinal),
            "Actual neutral QB input must end two-part locomotion through the original full-body stance.");
        foreach (bool turbo in new[] { false, true })
        {
            FlowSet(quarterback, "SourceMode", SourceActorMode.Receiver);
            FlowSet(quarterback, "Role", ActorRole.Receiver);
            FlowSet(quarterback, "SourceJob", Enum.ToObject(FlowGet(quarterback, "SourceJob").GetType(), 0));
            ((float[])FlowGet(model, "SourceStationTurbo"))[0] = 100f;
            place.Invoke(null, new object[] { quarterback, new SourceVector3(-150, 0, 0) });
            coverage.Case = "actual-receiver-" + (turbo ? "turbo" : "normal");
            for (int tick = 0; tick < 12; tick++) { Step(1, 0, turbo ? 4 : 0); coverage.Frame(host); }
            ActorAnimationView view = host.Game.State.Players[3].Animation;
            Check(!view.Primary.ScriptSymbol.StartsWith("qb_", StringComparison.Ordinal) &&
                !view.Secondary.IsSpecified && view.Primary.JointMask == SourceTiming.AllJoints,
                "The actual receiver role must select the original full-body run, never keep the QB torso/leg policy.");
            selected.Add(view.Primary.ScriptSymbol);
        }
        Check(host.LatestRenderObservation?.CurrentSourceTick == host.Game.SourceTick &&
            host.LatestRenderObservation.Actors.Count == 14,
            "The actual selector wakes must reach production actor pose/matrix rendering.");
        coverage.Save(output);
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "actual-public-original-animation-selectors-20261110/v1", assertions, selected,
            streamingEntries, jumpback, blockReadyFrame0 = restarted, rows,
            manifestSha256 = data.Catalog.ManifestSha256, renderSourceTick = host.LatestRenderObservation?.CurrentSourceTick,
            declaredActorRolePlacementFixture = true, directAnimationOrSelectorInvocation = false,
            naturalMatchOrPixelIdentityClaimed = false
        }));
    }

    private static void VerifyOriginalTeamSelection(PlayerData data, string output)
    {
        Check(data.Catalog.Profile.IsCompleteAv306TwoStationRecords,
            "The original animated team scene belongs to its newly admitted workflow, not a legacy menu alias.");
        var storage = new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false));
        var store = new SourcePlayerRecordStore(storage, SourceRecordSavePolicy.CustomBlockHistoryV3);
        store.LoadAsync().GetAwaiter().GetResult();
        var sink = new CompleteAvSink { CapturePresentPackets = true };
        var host = new PlayerHost(data, sink, observeRendering: true,
            options: PreviewOptions.FromUrl("", data.Catalog.Profile.StableId), recordStore: store);
        CabinetGuestToTeams(host);
        void Advance(double x = 0, int left = 0, int right = 0)
        {
            host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { left, right, 0, 0 });
            CabinetRender(host, 1);
        }
        void Tick(double x = 0, int left = 0, int right = 0)
        {
            long tick = host.PreGameSounds.SourceTick;
            for (int attempt = 0; attempt < 4 && host.PreGameSounds.SourceTick == tick; attempt++) Advance(x, left, right);
            Check(host.PreGameSounds.SourceTick == tick + 1,
                "The real accepted menu source clock must advance exactly one original wake.");
        }
        Check(host.TeamSelectionProcesses.Count == 2 && host.TeamSelectionPlayers.Count == 0,
            "Actual team-screen entry creates both source children; qcreate never executes a child at its birth.");
        for (int attempt = 0; attempt < 3 && host.TeamSelectionPlayers.Count != 2; attempt++) Tick();
        Check(host.TeamSelectionPlayers.Count == 2, "Both actual team-screen source children must reach the VM and renderer.");
        var first = host.TeamSelectionPlayers.OrderBy(player => player.Id).ToArray();
        long firstWake = host.PreGameSounds.SourceTick;
        var births = host.TeamSelectionProcesses.ToDictionary(process => process.Serial, process => process.BirthTick);
        Check(births.Count == 2 && births.Values.All(tick => tick > 0) &&
            host.TeamSelectionProcesses.All(process => process.LastWakeTick == process.BirthTick + 1),
            "Real nonzero menu entry must use a fresh host and preserve qcreate birth→next-wake ordering.");
        foreach (PlayerView player in first)
            Check(player.Animation.Primary.Sample.SourceFrame == 1,
                "Original tmsel initialization changes to frame0 then animates ONCE before its first sleep: first visible frame1.");
        var snapshots = new List<object>();
        string actualPacket = string.Empty;
        object? actualPresent = null;
        for (int wake = 0; wake < 160; wake++)
        {
            if (wake != 0) Tick();
            Check(host.Game.SourceTick == 0, "Menu actors must never advance or borrow the match's source-zero backdrop.");
            foreach (PlayerView player in host.TeamSelectionPlayers)
            {
                string script = player.Team == TeamId.Home ? "qb_ready_f_anim" : "qb_ready_anim";
                string clipName = player.Team == TeamId.Home ? "QB_RDYF" : "QB_RDY";
                ISourceAnimationClip clip = data.Animations.GetClip(clipName);
                int ordinal = (int)((host.PreGameSounds.SourceTick - firstWake + 1) % clip.SelectedSourceFrames.Count);
                Check(player.Id == (player.Team == TeamId.Home ? 3 : 10) &&
                    player.Source.Roster.RosterIndex == 3 && player.Source.ControllerStationId == -1 &&
                    !player.Source.Input.IsSpecified && player.Animation.Primary.ScriptSymbol == script &&
                    player.Animation.Primary.Sample.SourceFrame == clip.SelectedSourceFrames[ordinal] &&
                    ((player.Source.Presentation.RawFlags & 1) != 0) == (player.Team == TeamId.Home) &&
                    player.Animation.SourceTick == host.PreGameSounds.SourceTick,
                    "Actual tmsel source players must retain exact side/roster3/flip/hold/77-loop/no-human-input lineage.");
            }
            if (wake is 0 or 75 or 76 or 153 or 159)
            {
                using var status = JsonDocument.Parse(host.LastStatus);
                JsonElement packet = status.RootElement.GetProperty("teamSelectionPlayers");
                Check(packet.GetProperty("epoch").GetInt64() == host.Epoch &&
                    packet.GetProperty("sourceTick").GetInt64() == host.PreGameSounds.SourceTick &&
                    packet.GetProperty("projection").GetArrayLength() == 16 &&
                    packet.GetProperty("joints").GetArrayLength() == 480 &&
                    packet.GetProperty("models").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(new[] { 6, 10 }) &&
                    packet.GetProperty("beforeHudPrimitive").GetInt32() > 0 &&
                    packet.GetProperty("joints").EnumerateArray().All(value => double.IsFinite(value.GetDouble())),
                    "The real public selection must deliver both authentic MOD498 actors and full source matrices before foreground HUD.");
                snapshots.Add(packet.Clone());
                actualPacket = packet.GetRawText();
                actualPresent = sink.LastPresent;
            }
        }
        long heldTick = host.PreGameSounds.SourceTick;
        int[] heldFrames = host.TeamSelectionPlayers.Select(player => player.Animation.Primary.Sample.SourceFrame).ToArray();
        for (int redraw = 0; redraw < 3; redraw++) CabinetRender(host);
        Check(host.PreGameSounds.SourceTick == heldTick &&
            host.TeamSelectionPlayers.Select(player => player.Animation.Primary.Sample.SourceFrame).SequenceEqual(heldFrames),
            "Rendering without a source wake must hold both native poses, not animate per browser frame.");
        int oldTeam = host.Session.CreateConfiguration().HomeTeam.TeamIndex;
        long instance = host.TeamSelectionPlayers.Single(player => player.Id == 3).Animation.Primary.InstanceId;
        for (int move = 0; move < 6 && host.Session.CreateConfiguration().HomeTeam.TeamIndex == oldTeam; move++) Tick(1);
        Tick();
        Check(host.Session.CreateConfiguration().HomeTeam.TeamIndex != oldTeam &&
            host.TeamSelectionPlayers.Single(player => player.Id == 3).Source.Roster.TeamIndex ==
                host.Session.CreateConfiguration().HomeTeam.TeamIndex &&
            host.TeamSelectionPlayers.Single(player => player.Id == 3).Animation.Primary.InstanceId == instance &&
            host.TeamSelectionProcesses.All(process => births[process.Serial] == process.BirthTick),
            "Actual accepted team navigation must update static record/uniform without restarting the source player process or animation.");
        foreach (int side in new[] { 0, 1 })
        {
            Tick(left: side == 0 ? 1 : 0, right: side == 1 ? 1 : 0);
            Tick();
        }
        Check(host.Session.Stage != SourceCabinetMenuStage.Teams && host.TeamSelectionPlayers.Count == 0,
            "The actual menu transition must delete visible team objects, not leave a frozen preview actor.");
        using (var status = JsonDocument.Parse(host.LastStatus))
            Check(status.RootElement.GetProperty("teamSelectionPlayers").ValueKind == JsonValueKind.Null,
                "The production GPU packet must clear when the source team screen leaves.");
        for (int frame = 0; frame < 900 && !host.Session.Playing; frame++)
            Advance(left: frame % 8 < 3 ? 1 : 0, right: frame % 8 < 3 ? 1 : 0);
        Check(host.Session.Playing && host.TeamSelectionProcesses.Count == 0 &&
            host.TeamSelectionProcessRecords.Count(record => record.Operation == "Die") == 2,
            "The real source menu→match handoff must retire both child processes without retiming the match clock.");
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schema = "actual-public-tmsel-producer-20261110/v1", assertions,
            manifestSha256 = data.Catalog.ManifestSha256, firstWake, births, snapshots,
            actualPacket = JsonSerializer.Deserialize<JsonElement>(actualPacket),
            actualPresent,
            models = sink.ModelDefinitions.Where(pair => pair.Key is 6 or 10)
                .Select(pair => new { id = pair.Key, definition = JsonSerializer.Deserialize<JsonElement>(pair.Value) }).ToArray(),
            appearances = sink.AppearanceDefinitions.Select(pair =>
                new { id = pair.Key, definition = JsonSerializer.Deserialize<JsonElement>(pair.Value) }).ToArray(),
            actualMenuInputAndSourceClock = true, directAnimationOrSelectorInvocation = false,
            retiredAtActualHandoff = true, nativePixelsOrGlobalSchedulerClaimed = false
        }));
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "actual-public-tmsel-normal-flipped-result/v1", assertions, firstWake,
            actualBirths = births.Values, framesPerProgramme = 77, observedSourceWakes = 160,
            normal = "qb_ready_anim", flipped = "qb_ready_f_anim", fullMatricesPerFrame = 480,
            actualMenuInputAndSourceClock = true, hoverKeepsInstance = true,
            noRenderTimeAdvance = true, cleanupAtActualHandoff = true, gameSourceTick = host.Game.SourceTick
        }));
    }

    private static void VerifyOriginalHutSounds(PlayerData data)
    {
        var seen = new HashSet<int>();
        SourceSoundVmDispatch? point = null;
        foreach (int raw in new[] { 0, 1, 2, 3 })
        foreach (string script in new[] { "qbhut_knee_anim", "shotgun_anim", "qbset_anim",
        "qbptl_anim", "qbptr_anim", "qb_pumps_crowd_set_anim", "qb_claps_set_anim", "qb_rubs_knee_set_anim" })
        {
        var random = new SourceInjectedRandom(site => raw, new SourceRandomProvenance(
            "hut-callback-regression", "explicit raw tape, not target PRNG", "constant" + raw, SourceRandomEvidence.RecordedOracle));
        GameSimulation game = KernelBinding.Create(data, randomFactory: seed => random);
        game.TakeSourceSoundVmDispatches();
        object model = FlowModel(game);
        object actor = ((Array)FlowGet(model, "Players")).GetValue(0)!;
        object animation = FlowGet(actor, "AnimationState");
        Type animationType = animation.GetType();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        FlowSet(actor, "SourceRawFlags", 0);
        FlowSet(actor, "SourceControllerStation", raw == 3 ? 3 : -1);
        var begin = (Action<string, int, int>)typeof(GameSimulation).GetMethod("BeginSourceProcess",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(Action<string, int, int>), game);
        var draw = (Func<int, int>)typeof(GameSimulation).GetMethod("SourceDraw", BindingFlags.Instance |
            BindingFlags.NonPublic, null, new[] { typeof(int) }, null)!.CreateDelegate(typeof(Func<int, int>), game);
        var sink = new CompleteAudioSink();
        var playback = new SourceAudioPlayback(data.AudioAdmission!, sink, 57);
        playback.SetActive(true);
        playback.LoadBank("players");
        var presentation = new SourceCinematicPlayback(data.Impact,
            new SourceCinematicSeed(new SourceVector3(0, 10, -80), new SourceVector3(0, 0, 0), 90, 0, 0));
        var transport = new SourceSoundVmTransport();
        transport.BindOutput(playback);
        bool first = true;
        for (int tick = 1; tick <= 450; tick++)
        {
            FlowSet(model, "SourceTick", (long)tick);
            FlowSet(model, "SourcePresentationOrder", 0);
            begin("player_proc", 0, 0);
            if (tick == 1) animationType.GetMethod("FullBody", hidden)!.Invoke(animation,
                new object[] { script, actor, (long)tick, draw, AnimationSelectionEvidence.SourceVerified });
            else animationType.GetMethod("Advance", hidden, null,
                new[] { actor.GetType(), typeof(long), typeof(Func<int, int>) }, null)!.Invoke(animation,
                new object[] { actor, (long)tick, draw });
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            transport.Drain(batch);
            foreach (SourceSoundVmDispatch dispatch in batch.Dispatches)
            {
                if (dispatch.OriginalCue.FunctionSymbol != "hut_snd") continue;
                if (dispatch.OriginalCue.Argument0 == -2) point = dispatch;
                seen.Add(dispatch.OriginalCueContext.SourceLine);
                Check(dispatch.Status == SourceSoundVmStatus.Resolved && dispatch.Result.Calls.Count == 1,
                    "Actual original hut ANI_CODE must resolve one real sound call, not Unsupported/silence.");
                var call = dispatch.Result.Calls[0];
                Check(call.Bank == "players" && call.Volume == 160 && call.Pan == 127 && call.PriorityWord == 15 &&
                    call.CalleeSourceSite.FunctionSymbol == "hut_snd" &&
                    call.CalleeSourceSite.SourceLine == (dispatch.OriginalCue.Argument0 == -1 ? 1658 :
                        dispatch.OriginalCue.Argument0 == -2 ? 1660 : 1662) &&
                    call.PlaylistId == (dispatch.OriginalCue.Argument0 == -2 ? new[] { 38, 30, 31, 32 }[raw % 4] :
                        dispatch.OriginalCue.Argument0 == -1 ? new[] { 35, 38 }[raw % 2] : 35) &&
                    dispatch.Result.RandomCalls == (dispatch.OriginalCue.Argument0 < 0 ? 1 : 0),
                    "Hut retains actual table/sentinel, bank, callee, raw volume/pan/priority and shared RNG count.");
                    if (dispatch.Result.RandomCalls != 0)
                    {
                    SourceRandomDraw selected = dispatch.Result.RandomDraws[0];
                    Check(selected.Raw == raw && selected.Site.Kind == SourceRandomKind.RandRange &&
                        selected.Site.BoundOrModulus == (dispatch.OriginalCue.Argument0 == -1 ? 2 : 4) &&
                        selected.Site.CallIndex == dispatch.RandomContext.FirstCallIndex &&
                        selected.Site.SourceIdentity == "sounds.c:hut_snd:" + call.CalleeSourceSite.SourceLine &&
                        random.Trace.Any(actual => actual.Order == selected.Order && actual.Site.CallIndex == selected.Site.CallIndex),
                        "Hut selection must consume the actual shared stream at the original call cursor and exact table range.");
                    }
                    if (first)
                    {
                    int expectedTick = script == "qbhut_knee_anim" ? 65 : script == "shotgun_anim" ? 59 :
                        script == "qbset_anim" ? 55 : script.StartsWith("qbpt", StringComparison.Ordinal) ? 1 : -1;
                    if (expectedTick > 0) Check(tick == expectedTick,
                        "Original hut ANI_CODE executes after its exact frame delay, or synchronously before pointing.");
                    first = false;
                    }
                Check(ReferenceEquals(transport.TakeForCue(dispatch.OriginalCue), dispatch),
                    "Actual hut callback pairs with its original resolved request.");
                presentation.ConsumeResolvedSourceSound(dispatch, transport.DeliverResolved);
            }
            transport.CompletePresentationBatch();
            playback.Flush();
        }
        Check(sink.Commands.Any(command => command.GetProperty("kind").GetString() == "play") &&
            sink.Samples.Count > 0 && sink.HasNonzeroPcm,
            "Actual hut callbacks must reach decoded nonzero PCM and real playback commands.");
        }
        Check(seen.SetEquals(new[] { 86, 96, 105, 164, 173, 182, 216, 226, 235, 265, 290,
        8470, 8473, 8493, 8496, 8519, 8522 }), "All seventeen exact original hut callback sites must execute.");
        Check(point != null, "Actual pointing ANI_CODE supplies the sentinel regression context.");
        var bindings = SourceOriginalSoundBindings.ForCompleteOriginal303(data.Animations);
        bool missing = false;
        try
        {
        SourceOriginalAudioFunctions.SelectHut(bindings, point!.OriginalCue, default,
            new SourceInjectedRandom(site => 3, new SourceRandomProvenance(point.RandomContext.Profile,
                "explicit missing-input tape", "constant3", SourceRandomEvidence.RecordedOracle)), point.RandomContext);
        }
        catch (SourceAudioMissingInputException error)
        {
        missing = error.Field == "plyr_bnk_str" && error.PartialResult.RandomCalls == 1 &&
            error.PartialResult.Calls.Count == 0;
        }
        Check(missing, "Missing current player bank retains its consumed hut RNG draw and never fabricates output.");
        bool neighboringSiteRejected = false;
        try { data.Impact.RequireExecutedOpcode("qbhut_knee_anim", 87, "callback"); }
        catch (InvalidOperationException) { neighboringSiteRejected = true; }
        Check(neighboringSiteRejected, "Closed hut presentation metadata must not admit neighboring invented callback sites.");
        Console.WriteLine("PASS original hut callbacks: " + assertions +
        " assertions;17 real ANI_CODE sites, resolved requests and decoded PCM/play commands; no audition/native DSP claim.");
    }

    private sealed class MixerPreludeDriver : ISourcePreGameMenuDriver
    {
        private SourcePreGameMenuPhase phase = SourcePreGameMenuPhase.Joining;
        public SourcePreGameMenuRoute ReadRoute() => new(phase, 1, 0,
            new[] { SourcePreGameMenuScreen.Options, SourcePreGameMenuScreen.None,
                SourcePreGameMenuScreen.None, SourcePreGameMenuScreen.None });
        public void OnSourceMenuWake(SourcePreGameMenuWake wake)
        {
            if (!wake.IsOriginalProcess && (wake.ClosedButtons(0) & SourceButtons.C) != 0)
                phase = SourcePreGameMenuPhase.Options;
        }
    }

    private static void VerifyOriginalContactSounds(PlayerData data)
    {
        int audioRaw = 7;
        int helmetRaw = 0;
        var random = new SourceInjectedRandom(site => site.SourceIdentity.StartsWith("sounds.c:", StringComparison.Ordinal) ? audioRaw :
            site.SourceIdentity.StartsWith("player.c:maybe_pop_helmet:", StringComparison.Ordinal) ? helmetRaw :
            site.SourceIdentity == "detect.c:evade_vs_tackle:2429" ? 3 :
            site.SourceIdentity == "detect.c:evade_vs_tackle:2432" ? 0 : 99,
            new SourceRandomProvenance("original-contact-audio", "explicit raw tape, not target PRNG",
                "contact7/otherwise99", SourceRandomEvidence.RecordedOracle));
        GameSimulation game = KernelBinding.Create(data, randomFactory: seed => random);
        game.BindSourceOriginalBadLanguageAdjustment(false);
        object model = FlowModel(game);
        var actors = (Array)FlowGet(model, "Players");
        FlowSet(model, "SourceTick", 1L);
        FlowSet(model, "Phase", GamePhase.LiveBall);
        FlowSet(model, "Possession", TeamId.Home);
        FlowSet(model, "CarrierId", 4);
        FlowSet(model, "BallMode", BallMode.Held);
        FlowSet(model, "BallKind", SourceBallKind.Pass);
        FlowSet(model, "CatchSourceTick", 0L);
        FlowSet(model, "SourceNoFumbles", true);
        FlowSet(model, "SourceFlags", SourceGameFlags.None);
        FlowSet(model, "SourceOffensePlay", data.Playbook.GetPlay("play_spider"));
        FlowSet(model, "SourceDefensePlay", data.Playbook.GetPlay("play_safe_cover"));
        FlowSet(model, "SourceRules", new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var begin = FlowMethod<Action<string, int, int>>(game, "BeginSourceProcess");
        var draw = FlowMethod<Func<int, int>>(game, "SourceDraw");
        Type playerDriver = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!;
        MethodInfo position = playerDriver.GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        for (int id = 0; id < 14; id++)
        {
            object actor = actors.GetValue(id)!;
            FlowSet(actor, "SourceMode", id == 4 ? SourceActorMode.Receiver : id == 11 ? SourceActorMode.DefensiveBack : SourceActorMode.Dead);
            FlowSet(actor, "SourceRawFlags", id == 4 || id == 11 ? 2 : 0x20);
            FlowSet(actor, "SourceControllerStation", -1);
            FlowSet(actor, "SourceVelocity", default(FieldVector));
            FlowSet(actor, "SourceVerticalVelocity", 0f);
            FlowSet(actor, "SourceHitPartnerId", -1);
            FlowSet(actor, "SourcePuppetLinkId", -1);
            FlowSet(actor, "Down", false);
            FlowSet(actor, "Facing", id == 11 ? 768 : 256);
            FlowSet(actor, "TargetFacing", id == 11 ? 768 : 256);
            position.Invoke(null, new object[] { actor, new SourceVector3(id == 4 ? 0 : id == 11 ? 9 : -300 + id * 40, 0, id == 4 || id == 11 ? 0 : 130) });
            begin("player_proc", id, 0x300 + id);
            object animation = FlowGet(actor, "AnimationState");
            animation.GetType().GetMethod("FullBody", hidden)!.Invoke(animation, new object[] {
                id == 4 ? "run_wb_anim" : "run_anim", actor, 1L, draw, AnimationSelectionEvidence.SourceVerified });
        }
        SourceCinematicTransform camera = SourceCinematicTransform.FromState(
            new SourceCinematicSeed(new SourceVector3(-120, 85, -40), new SourceVector3(0, 14, 0), 150, 0, -.4f));
        FlowSet(model, "SourceMovementCamera", new SourceMovementCameraFrame(camera.Position, camera.Transform, 512, 384));
        FlowSet(model, "SourceMovementCameraTick", 1L);
        FlowMethod<Action>(game, "CacheSourceGeometry")();
        FlowMethod<Action>(game, "CacheSourcePlayerCameraJoints")();
        game.TakeSourceSoundVmDispatches();
        var processes = (SourcePortableProcessHost)FlowGet(model, "SourceProcesses");
        processes.BeginTick(1);
        Check(processes.Visit(24), "Actual source detect_proc enters its executing process, not an observer sound fixture.");
        begin("detect_proc", -1, 0x500);
        FlowMethod<Action>(game, "SourceContacts")();
        processes.Complete();
        SourceProcessSoundCall[] calls = game.TakeSourceProcessSoundCalls().ToArray();
#if BLITZ_NATIVE_AUDIO_API
        var native = new SourceNativePcmMixer(data.AudioAdmission!.Banks, data.AudioAdmission.AuthorizationReference,
            data.AudioAdmission.InitialBanks, data.AudioAdmission.InitialReservedTracks, 2);
        foreach (SourceBootSoundOutput boot in game.TakeSourceBootSoundOutputs()) native.Consume(boot);
        SourceSoundVmBatch nativeVm = game.TakeSourceSoundVmDispatches();
        var nativeInitial = calls.Select(call => (call.OutputSequence, Vm: (SourceSoundVmDispatch?)null, Process: (SourceProcessSoundCall?)call))
            .Concat(nativeVm.Dispatches.Select(dispatch => (dispatch.OutputSequence, Vm: (SourceSoundVmDispatch?)dispatch, Process: (SourceProcessSoundCall?)null)))
            .OrderBy(item => item.OutputSequence);
        foreach (var item in nativeInitial)
            if (item.Vm != null) native.ConsumeResolved(item.Vm);
            else native.Consume(item.Process!);
#endif
        Check(game.ContactEvents.Any(item => item.Kind == SourceContactEventKind.Collision) &&
            calls.Any(call => call.Bank == "generic"),
            "Actual original contact dispatch must execute its crowd selector and emit a real canonical sound request.");
        Check(game.UnresolvedContactSoundCalls.Count == 0,
            "Handled complete-original contact audio must not remain an undelivered unresolved record.");
        var sink = new CompleteAudioSink();
        var playback = new SourceAudioPlayback(data.AudioAdmission!, sink, 57);
        playback.SetActive(true);
        foreach (string bank in calls.Select(call => call.Bank).Distinct())
            playback.LoadBank(bank);
        foreach (SourceProcessSoundCall call in calls)
            playback.Consume(call);
        playback.Flush();
        Check(sink.HasNonzeroPcm && sink.Commands.Any(command => command.GetProperty("kind").GetString() == "play"),
            "Actual collision-produced sound requests must reach the original decoded PCM and real mixer play commands.");
        long tick = 1;
        var delivered = new List<SourceProcessSoundCall>(calls);
        var dispatchChild = FlowMethod<Func<long, bool>>(game, "DispatchSourceOriginalSpeechChild");
        void Deliver(SourceProcessSoundCall call)
        {
            Check(call.Kind == SourceProcessSoundKind.OriginalContact && call.OriginalContactCue != null &&
                call.CalleeSourceLine > 0 && call.CalleeSourceGitBlob.Length == 40,
                "Resolved original contact retains its actual call identity and authenticated callee.");
            if (!playback.IsBankLoaded(call.Bank)) playback.LoadBank(call.Bank);
            playback.Consume(call);
#if BLITZ_NATIVE_AUDIO_API
            native.Consume(call);
#endif
            delivered.Add(call);
        }
        SourceProcessSoundCall[] Emit(string function, string caller, int line, int raw, params string[] args)
        {
            audioRaw = raw;
            FlowSet(model, "SourceTick", ++tick);
            processes.BeginTick(tick);
            foreach (SourceProcessView process in processes.Processes.Where(item => new[] {
                "hit_intended_receiver_sp_proc", "late_hit_speech_proc", "delay_plow_proc" }.Contains(item.Function)).ToArray())
                dispatchChild(process.Serial);
            Check(processes.Visit(24), "Contact audio fixture uses the real next executing detect_proc.");
            begin("detect_proc", -1, 0x500);
            typeof(GameSimulation).GetMethod("SourceContactSound", hidden)!.Invoke(game,
                new object[] { actors.GetValue(4)!, function, caller, line, args });
            processes.Complete();
            SourceProcessSoundCall[] emitted = game.TakeSourceProcessSoundCalls().ToArray();
            foreach (SourceProcessSoundCall call in emitted) Deliver(call);
            playback.Flush();
            return emitted;
        }
        void Raw(SourceProcessSoundCall call, string bank, int id, uint volume, uint pan, uint priority, int draws)
        {
            Check(call.Bank == bank && call.PlaylistId == id && call.Volume == volume && call.Pan == pan &&
                call.PriorityWord == priority && call.RandomCalls == draws,
                "Real contact emitter preserves exact original bank, playlist, volume, pan, priority and draw count: " +
                call.Operand + " expected=" + bank + "/" + id + "/" + volume + "/" + pan + "/" + priority + "/" + draws +
                " actual=" + call.Bank + "/" + call.PlaylistId + "/" + call.Volume + "/" + call.Pan + "/" + call.PriorityWord + "/" + call.RandomCalls);
        }
        string taunt = (string)FlowGet(model, "SourceTauntBank"), announcer = (string)FlowGet(model, "SourceAnnouncerSwappedBank");
        Raw(Emit("crowd_tackle_snds", "att_push", 1250, 7).Single(), "generic", 12, 205, 127, 10, 1);
        Raw(Emit("crowd_cheer_snds", "att_dive", 1426, 1).Single(), "generic", 12, 205, 127, 10, 1);
        Raw(Emit("monkey_on_back_sp", "att_dive", 1476, 29).Single(), taunt, 38, 235, 38, 1, 1);
        Raw(Emit("monkey_on_back_sp", "att_dive", 1482, 30).Single(), announcer, 78, 255, 127, 0xe281, 2);
        Raw(Emit("qb_duck_speech", "att_dive", 1520, 2).Single(), announcer, 248, 235, 127, 5, 1);
        Raw(Emit("snd_scall_bank", "att_push", 1235, 0, "plyr_bnk_str", "OUTTA_MY_FACE_SP", "VOLUME3", "127", "LVL3").Single(),
            "players", 50, 205, 127, 0xe285, 0);
        Raw(Emit("snd_scall_bank", "att_push", 1251, 0, "plyr_bnk_str", "P_HARD_HIT2_SND", "VOLUME4", "127", "PRIORITY_LVL4").Single(),
            "players", 96, 235, 127, 1, 0);
        Raw(Emit("snd_scall_bank", "att_head", 2124, 0, "plyr_bnk_str", "BOWLING_PIN_SND", "VOLUME4", "127", "PRIORITY_LVL4").Single(),
            "players", 141, 235, 127, 1, 0);
        FlowSet(model, "TargetPlayerId", 11);
        Check(Emit("hit_intended_receiver_sp", "att_spear_head", 1976, 0).Length == 0,
            "Original receiver-hit qcreate emits no speech or RNG before its real delayed child.");
        long receiverBirth = tick;
        Check(Emit("late_hit_speech", "att_late_hit", 1771, 0).Length == 0,
            "Original late-hit speech schedules its real child instead of immediate audio.");
        long lateBirth = tick;
        Check(Emit("late_hit_speech", "att_late_hit", 1771, 0).Length == 0 &&
            processes.Processes.Count(item => item.Function == "late_hit_speech_proc") == 1,
            "Original per-play late-hit latch suppresses duplicate children.");
        Check(Emit("qcreate", "att_head", 2125, 3, "plow", "0", "delay_plow_proc", "0", "0", "0", "0").Length == 0,
            "Original plow caller queues the actual delay process with no immediate speech.");
        long plowBirth = tick;
        for (long end = plowBirth + 51; tick < end;)
        {
            FlowSet(model, "SourceTick", ++tick);
            FlowSet(model, "SourceDispatchOrdinal", 0);
            processes.BeginTick(tick);
            foreach (SourceProcessView process in processes.Processes.Where(item => new[] {
                "hit_intended_receiver_sp_proc", "late_hit_speech_proc", "delay_plow_proc" }.Contains(item.Function)).ToArray())
                dispatchChild(process.Serial);
            foreach (SourceProcessSoundCall call in game.TakeSourceProcessSoundCalls()) Deliver(call);
        }
        playback.Flush();
        SourceProcessSoundCall receiver = delivered.Single(call => call.CalleeFunction == "hit_intended_receiver_sp_proc");
        SourceProcessSoundCall late = delivered.Single(call => call.CalleeFunction == "late_hit_speech_proc");
        SourceProcessSoundCall plow = delivered.Single(call => call.CalleeFunction == "head_plow_speech");
        Raw(receiver, announcer, 134, 255, 127, 0xe281, 1);
        Raw(late, announcer, 140, 225, 127, 0xe285, 0);
        Raw(plow, announcer, 153, 225, 127, 0xe28a, 1);
        Check(receiver.SourceTick == receiverBirth + 20 && late.SourceTick == lateBirth + 21 &&
            plow.SourceTick == plowBirth + 51 && receiver.OriginalContactCue.Context.SourceTick == receiverBirth &&
            late.OriginalContactCue.Context.SourceTick == lateBirth && late.CalleeSourceLine == 919,
            "Actual child wakes preserve original CONTROL_TIME+1/20/50 sleeps, original caller identity and no-swear branch.");
        Check(game.UnresolvedContactSoundCalls.Count == 0 && delivered.All(call => sink.Commands.Any(command =>
            command.GetProperty("outputSequence").GetInt64() == call.OutputSequence &&
            command.GetProperty("bank").GetString() == call.Bank &&
            command.GetProperty("playlistId").GetInt32() == call.PlaylistId)),
            "Every resolved contact request reaches the real admitted mixer as a play or explicit priority rejection.");
        SourceProcessSoundCall[] Pop(bool fire, GamePhase phase)
        {
            ((bool[])FlowGet(model, "SourceTeamFire"))[0] = fire;
            FlowSet(model, "Phase", phase);
            FlowSet(actors.GetValue(4)!, "SourceHelmetPopped", false);
            FlowSet(model, "SourceTick", ++tick);
            processes.BeginTick(tick);
            Check(processes.Visit(6), "Helmet audio executes in its actual player_proc, not an observer callback.");
            begin("player_proc", 4, 0x304);
            typeof(GameSimulation).GetMethod("SourceMaybePopHelmet", hidden)!.Invoke(game, new[] { actors.GetValue(4)! });
            processes.Complete();
            SourceProcessSoundCall[] emitted = game.TakeSourceProcessSoundCalls().ToArray();
            foreach (SourceProcessSoundCall call in emitted) Deliver(call);
            playback.Flush();
            return emitted;
        }
        long beforeHelmet = (long)FlowGet(model, "SourceRandomDraws");
        SourceProcessSoundCall[] normalHelmet = Pop(false, GamePhase.LiveBall);
        Check(normalHelmet.Length == 2 && (bool)FlowGet(actors.GetValue(4)!, "SourceHelmetPopped") &&
            (long)FlowGet(model, "SourceRandomDraws") == beforeHelmet + 2,
            "Normal helmet callback preserves both original eligibility draws and its observable popped state.");
        Raw(normalHelmet[0], announcer, 166, 255, 127, 0xe281, 0);
        Raw(normalHelmet[1], "players", 94, 255, 127, 5, 0);
        Check(normalHelmet.Select(call => call.CalleeSourceLine).SequenceEqual(new[] { 9203, 9204 }) &&
            normalHelmet[0].OutputSequence + 1 == normalHelmet[1].OutputSequence &&
            normalHelmet.All(call => call.OriginalContactCue.Context.SourcePath == "player.c"),
            "The two normal helmet sounds retain original caller lines and canonical subcall ordering.");
        beforeHelmet = (long)FlowGet(model, "SourceRandomDraws");
        SourceProcessSoundCall[] fireHelmet = Pop(true, GamePhase.LiveBall);
        Check(fireHelmet.Length == 1 && (long)FlowGet(model, "SourceRandomDraws") == beforeHelmet + 1,
            "Team-fire helmet branch short-circuits the second eligibility draw and emits only its original announcer call.");
        Raw(fireHelmet[0], announcer, 165, 255, 127, 0xe281, 0);
        beforeHelmet = (long)FlowGet(model, "SourceRandomDraws");
        Check(Pop(true, GamePhase.DeadBall).Length == 0 &&
            (long)FlowGet(model, "SourceRandomDraws") == beforeHelmet + 1,
            "Original post-play helmet callback retains the initial draw but produces no sound or invented pop.");
#if BLITZ_NATIVE_AUDIO_API
        var stereo = new float[(tick * 31250 / 57 + 20000) * 2];
        native.ReadStereo(stereo);
        Check(native.NonzeroSampleFrames > 0 && native.PlayedCalls > 0 && stereo.Any(value => value != 0),
            "Actual collision/helmet/deferred source calls produce real stereo PCM through the native raw-output consumer.");
        Check(delivered.All(call => native.Receipts.Any(receipt => receipt.OutputSequence == call.OutputSequence &&
            receipt.Bank == call.Bank && receipt.PlaylistId == call.PlaylistId &&
            (receipt.Kind == "play" || receipt.Kind == "priority-rejected"))),
            "Every actual native source contact request reaches the renderer or an explicit original priority rejection.");
        Check(!native.NativeDspEquivalent, "Real portable native PCM must not claim arcade DSP equivalence.");
        Console.WriteLine("PASS native raw-source contact PCM: " + native.NonzeroSampleFrames +
            " nonzero stereo frames; Unity AudioSource API is compiled separately, no device audition claimed.");
        VerifyNativePcmBoundaries(data);
#endif
        random = new SourceInjectedRandom(site => audioRaw, random.Provenance);
        game.Restart(1);
        Check(!game.SourceOriginalBadLanguageEnabled, "An explicitly bound operator sound adjustment survives a same-game restart.");
        Console.WriteLine("PASS original contact audio: " + assertions + " assertions; actual collision/emitter/PCM commands.");
    }

#if BLITZ_NATIVE_AUDIO_API
    private static void VerifyNativePcmBoundaries(PlayerData data)
    {
        SourceDirectSoundCall Probe(long ordinal, string bank, int id, uint volume = 160, uint pan = 127, uint priority = 15) =>
            new(ordinal, 1, 1, "directed-native-mixer-contract", 0, 0, -1, TeamId.Home,
                "component-fixture", "native_mixer_contract", 1, bank, id, volume, pan, priority, 0, ordinal);
        SourceNativePcmMixer Mixer(uint reserved = 0) => new(data.AudioAdmission!.Banks,
            data.AudioAdmission.AuthorizationReference, new[] { "players", "main1", "cursors" }, reserved, 2);
        const int lead = 2 * 31250 / 57;
        SourceAudioPcm pcm = data.AudioAdmission!.RequireProgram("players", 35).Program.Pcm;
        int nonzero = Enumerable.Range(0, pcm.SampleCount).First(index => pcm[index] != 0);
        var left = Mixer();
        left.Consume(Probe(1, "players", 35, pan: 0));
        var one = new float[(lead + pcm.SampleCount + 4) * 2];
        left.ReadStereo(one);
        float sample = pcm[nonzero] / 32768f * (160f / 255);
        Check(one.Take(lead * 2).All(value => value == 0) &&
            Math.Abs(one[(lead + nonzero) * 2] - sample) < .000001 &&
            one.Where((value, index) => index % 2 == 1).All(value => value == 0),
            "Native source-clock scheduling preserves exact decoded sample/gain, leading latency and original hard-left pan.");
        Check(left.Receipts.Single(receipt => receipt.Kind == "play").AtSample == lead,
            "Native play receipt retains its scheduled57Hz-to31250Hz sample boundary.");
        var overlap = Mixer();
        overlap.Consume(Probe(1, "players", 35, pan: 0));
        overlap.Consume(Probe(2, "players", 35, pan: 255));
        var stereo = new float[one.Length];
        overlap.ReadStereo(stereo);
        Check(overlap.PlayedCalls == 2 &&
            overlap.Receipts.Where(receipt => receipt.Kind == "play").Select(receipt => receipt.Track).SequenceEqual(new[] { 0, 1 }) &&
            Math.Abs(stereo[(lead + nonzero) * 2] - sample) < .000001 &&
            Math.Abs(stereo[(lead + nonzero) * 2 + 1] - sample) < .000001,
            "Native overlap uses two actual six-track voices and preserves independent left/right decoded PCM.");
        var priority = Mixer();
        priority.Consume(Probe(1, "players", 35, priority: 0xe281));
        priority.Consume(Probe(2, "players", 35, priority: 0xe28f));
        priority.Consume(Probe(3, "players", 35, priority: 0xe281));
        priority.Consume(Probe(4, "players", 35, priority: 0x828f));
        priority.ReadStereo(new float[(lead + 1) * 2]);
        Check(priority.PlayedCalls == 2 && priority.RejectedCalls == 2 &&
            priority.Receipts.Where(receipt => receipt.Kind == "priority-rejected").Select(receipt => receipt.OutputSequence)
                .SequenceEqual(new long[] { 2, 3 }) &&
            priority.Receipts.Last().OutputSequence == 4 && priority.Receipts.Last().Track == 5,
            "Native NOINT rejects weaker/equal calls while original SND_PRI_SET forcibly preempts the actual selected track.");
        var reserved = Mixer(63);
        reserved.Consume(Probe(1, "players", 35));
        reserved.ReadStereo(new float[(lead + 1) * 2]);
        Check(reserved.PlayedCalls == 0 && reserved.RejectedCalls == 1 &&
            reserved.Receipts.Single().Kind == "priority-rejected",
            "Native all-reserved dynamic allocation remains an explicit original rejection, not claimed playback.");
        var empty = Mixer();
        empty.Consume(Probe(1, "players", 10));
        empty.ReadStereo(new float[(lead + 1) * 2]);
        Check(empty.PlayedCalls == 0 && empty.RejectedCalls == 1 && empty.Receipts.Single().Kind == "unassigned-playlist",
            "Native original unassigned slots remain explicit source rejections, not invented PCM.");
        SourceAudioPlaylist music = data.AudioAdmission.RequireProgram("main1", 0).Program;
        Check(music.CompiledPlaybackQualified && music.LoopStartSample < music.LoopEndSample,
            "Native loop fixture uses the actual qualified original music programme.");
        var loop = Mixer();
        loop.Consume(Probe(1, "main1", 0, 255, 0, 0x8000));
        long toBoundary = lead + music.LoopEndSample - 2;
        var chunk = new float[8192];
        while (loop.RenderedSampleFrames < toBoundary)
        {
            long frames = Math.Min(4096, toBoundary - loop.RenderedSampleFrames);
            loop.ReadStereo(frames == 4096 ? chunk : new float[frames * 2]);
        }
        var edge = new float[8];
        loop.ReadStereo(edge);
        int[] indices = { music.LoopEndSample - 2, music.LoopEndSample - 1, music.LoopStartSample, music.LoopStartSample + 1 };
        Check(Enumerable.Range(0, 4).All(index => Math.Abs(edge[index * 2] - music.Pcm[indices[index]] / 32768f) < .000001),
            "Native actual compiled music wraps its verified loop region without replaying the attack or an isolated preview.");
        long held = loop.RenderedSampleFrames;
        loop.SetActive(false); loop.ReadStereo(edge);
        Check(loop.RenderedSampleFrames == held && edge.All(value => value == 0),
            "Explicit native suspension freezes the actual voice/sample frontier.");
        loop.SetActive(true); loop.ReadStereo(edge);
        Check(loop.RenderedSampleFrames == held + 4, "Native resume continues the retained voice/loop cursor.");

        var portableSink = new CompleteAudioSink();
        var portable = new SourceAudioPlayback(data.AudioAdmission, portableSink, 57);
        portable.SetActive(true); portable.LoadBank("main1"); portable.LoadBank("cursors");
        SourceDirectSoundCall tune = Probe(1, "main1", 0, 160, 0, 0x8000);
        SourceDirectSoundCall duck = Probe(2, "cursors", 80, 255, 255, 0xe281);
        portable.Consume(tune); portable.Consume(duck); portable.Flush();
        JsonElement[] envelope = portableSink.Commands.Last(command => command.GetProperty("kind").GetString() == "program-mix")
            .GetProperty("mixPoints").EnumerateArray().ToArray();
        var global = Mixer();
        global.Consume(tune); global.Consume(duck);
        long offset = 0;
        foreach (int relative in new[] { 6000, 318 * 240 + 1 })
        {
            long target = lead + relative;
            while (global.RenderedSampleFrames < target)
            {
                long frames = Math.Min(4096, target - global.RenderedSampleFrames);
                global.ReadStereo(frames == 4096 ? chunk : new float[frames * 2]);
            }
            global.ReadStereo(edge);
            long portableSample = 31250 / 57 + relative;
            double gain = envelope.Last(point => point.GetProperty("atSample").GetInt64() <= portableSample).GetProperty("gain").GetDouble();
            Check(Math.Abs(edge[0] - music.Pcm[relative] / 32768.0 * gain) < .000002,
                "Native actual PCM global duck/restore agrees with independently consumed original mixer-envelope commands: relative=" +
                relative + " actual=" + edge[0] + " expected=" + music.Pcm[relative] / 32768.0 * gain + " gain=" + gain);
            offset = target;
        }
        Check(offset > 318 * 240, "Native global envelope regression reaches the actual restore boundary.");
        bool duplicate = false;
        try { left.Consume(Probe(1, "players", 35)); }
        catch (InvalidOperationException) { duplicate = true; }
        Check(duplicate && left.Failure != null, "Native duplicate raw requests fault explicitly without reselecting or replaying.");
        left.Reset(1);
        Check(left.Failure == null, "Explicit native restart recovers the failed mixer, not a hidden retry.");
    }
#endif

    private static void VerifyAudioEnvelopes(PlayerData data, string output)
    {
        var sink = new CompleteAudioSink(Path.Combine(output, "envelopes"));
        var playback = new SourceAudioPlayback(data.AudioAdmission!, sink, 57);
        var service = new SourcePreGameSoundService(SourcePreGameSoundEntry.FromOriginalPermanentBanksAfterAcceptedStart(),
            SourcePreGameSoundService.CreateManagedPreviewRandom(1), new MixerPreludeDriver());
        service.CapturePhysical(new SourcePhysicalSwitchFrame(Enumerable.Range(0, 4).Select(id =>
            new SourceStationInput(id, 0, 0, id == 0 ? SourceButtons.C : SourceButtons.None,
                digitalCurrent: SourceDigitalDirections.None)).ToArray()));
        SourcePreGameSoundActivation? activation = null;
        for (int i = 0; i < 10 && activation == null; i++)
        {
            service.AdvanceHostTick();
            if (service.TryTakeEntryActivation(out var entry)) activation = entry;
        }
        Check(activation != null, "Envelope fixtures require an actual accepted cabinet-start activation.");
        playback.BeginPreGameEpoch(1, activation!);
        playback.LoadBank("main1");
        long ordinal = 0, direct = 0, tick = activation!.SourceTick + 1;
        long Sample(long k) => k * 31250 / 57;
        void Play(string bank, int id, int track, long at, uint volume = 115)
        {
            tick = at;
            playback.Consume(new SourceDirectSoundCall(++direct, at, 1, "directed-admitted-envelope-fixture",
                0, 0, 0, TeamId.Home, "nfl.c", "InitGameProc", 601, bank, id, volume, 127,
                0x8000u | (uint)track << 7, 0, ++ordinal));
            playback.Flush();
        }
        JsonElement[] Mixes() => sink.Commands.Where(c => c.GetProperty("kind").GetString() == "program-mix").ToArray();
        var music = data.AudioAdmission!.RequireProgram("main1", 0).Program;
        var duck = data.AudioAdmission.RequireProgram("cursors", 80).Program;
        var ordinary = data.AudioAdmission.RequireProgram("players", 13).Program;
        Check(music.LoopEndSample > 0 && music.ConstantOwnMixingLevel.HasValue &&
            ordinary.GlobalMixer == null && duck.GlobalMixer != null &&
            duck.GlobalMixer.GlobalLevelAt(0) == -640 && duck.GlobalMixer.GlobalLevelAt(318 * 240) == 0,
            "Use legitimate admitted loop/ordinary/13-14 duck programmes, not the rejected unpublished cursor fixture: " +
            JsonSerializer.Serialize(new { music.LoopEndSample, music.ConstantOwnMixingLevel,
                ordinaryGlobal = ordinary.GlobalMixer != null, global = duck.GlobalMixer?.GlobalMusic }));
        MethodInfo multiplier = typeof(SourceAudioPlayback).GetMethod("MixingMultiplier", BindingFlags.Static | BindingFlags.NonPublic)!;
        double NativeGain(int baseline, int contribution, uint volume)
        {
            int original = (int)multiplier.Invoke(null, new object[] { baseline, (int)volume })!;
            int mixed = (int)multiplier.Invoke(null, new object[] { baseline + contribution, (int)volume })!;
            return volume / 255.0 * (original == 0 ? 1 : (double)mixed / original);
        }
        double Gain(int contribution, uint volume) => NativeGain(music.ConstantOwnMixingLevel!.Value, contribution, volume);
        var columns = new Dictionary<int, long>();
        void Exact(uint volume = 115)
        {
            JsonElement mix = Mixes().Last();
            var actual = mix.GetProperty("mixPoints").EnumerateArray().ToArray();
            int total = columns.Values.Sum(start => duck.GlobalMixer!.GlobalLevelAt(Sample(tick) - start));
            var expected = new List<(long Sample, double Gain)> { (Sample(tick), Gain(total, volume)) };
            var deltas = new SortedDictionary<long, int>();
            foreach (long start in columns.Values)
            {
                int previous = 0;
                foreach (var point in duck.GlobalMixer!.GlobalMusic)
                {
                    long sample = start + point.Frame * 240L;
                    if (sample > Sample(tick))
                        deltas[sample] = deltas.GetValueOrDefault(sample) + point.Level - previous;
                    previous = point.Level;
                }
            }
            foreach (var change in deltas)
            {
                total += change.Value;
                double gain = Gain(total, volume);
                if (expected.Last().Gain != gain) expected.Add((change.Key, gain));
            }
            Check(actual.Length == expected.Count && actual.Zip(expected).All(pair =>
                pair.First.GetProperty("atSample").GetInt64() == pair.Second.Sample &&
                pair.First.GetProperty("gain").GetDouble() == pair.Second.Gain),
                "The entire six-column replacement must retain exact S(trigger)+240*f restore samples and gains.");
        }

        Play("main1", 0, 0, tick);
        Check(Mixes().Length == 0, "A new constant loop's play already supplies its entire gain envelope.");
        Play("players", 13, 1, tick + 1);
        Play("players", 13, 1, tick + 1);
        playback.StopTracks(2); playback.Flush();
        Check(Mixes().Length == 0, "Ordinary play, preemption and stop must emit no unchanged music replacement.");
        for (int track = 1; track < 6; track++)
        {
            Play("cursors", 80, track, tick + 1);
            columns[track] = Sample(tick);
            Check(Mixes().Length == track, "Each actual new global column requires exactly one replacement.");
            Exact();
        }
        double unchangedNow = Mixes().Last().GetProperty("mixPoints")[0].GetProperty("gain").GetDouble();
        int count = Mixes().Length;
        Play("cursors", 80, 5, tick + 1); columns[5] = Sample(tick);
        Check(Mixes().Length == count + 1 &&
            Mixes().Last().GetProperty("mixPoints")[0].GetProperty("gain").GetDouble() == unchangedNow,
            "Equal current gain is NOT equal future: same-column preemption must move its later restore, atomically.");
        Exact();
        count = Mixes().Length;
        Play("players", 13, 1, tick + 1); columns.Remove(1);
        Check(Mixes().Length == count + 1, "A non-global replacement must remove its old global contributor.");
        Exact();
        count = Mixes().Length;
        playback.StopTracks(2); playback.Flush();
        Check(Mixes().Length == count, "Stopping the now ordinary replacement changes no current/future music gain.");
        playback.StopTracks(12); playback.Flush(); columns.Remove(2); columns.Remove(3);
        Check(Mixes().Length == count + 1, "A multi-track stop replaces the final changed envelope once, not intermediate columns.");
        Exact();
        playback.SetTrackVolume(1, 80); playback.Flush(); Exact(80);
        count = Mixes().Length;
        playback.SetTrackPan(1, 255); playback.Flush();
        Check(Mixes().Length == count + 1, "The receiver's pan adjust also writes base gain; the real current duck must be restored.");
        Exact(80);
        playback.SetTrackVolume(1, 0); playback.Flush(); Exact(0);
        count = Mixes().Length;
        playback.SetTrackVolume(1, 0); playback.Flush();
        Check(Mixes().Length == count, "Repeated zero volume with no future gain change needs no cancellation.");
        playback.SetTrackVolume(1, 80); playback.Flush(); Exact(80);
        count = Mixes().Length;
        Play("main1", 0, 0, tick + 1, 80);
        Check(Mixes().Length == count + 1 && Mixes().Last().GetProperty("voiceId").GetInt64() ==
            sink.Commands.Last(c => c.GetProperty("kind").GetString() == "play").GetProperty("voiceId").GetInt64(),
            "Music replacement must bind the surviving columns to the NEW voice, not reuse the stopped voice's cache.");
        Exact(80);
        long end = columns.Values.Max() + duck.Pcm.SampleCount;
        count = Mixes().Length;
        playback.AdvanceTo((end * 57 + 31249) / 31250); playback.Flush(); tick = playback.SourceTick;
        Check(Mixes().Length == count && sink.Commands.Any(c => c.GetProperty("kind").GetString() == "stop" &&
            c.GetProperty("reason").GetString() == "completed") &&
            !sink.Commands.Any(c => c.GetProperty("kind").GetString() == "stop" &&
                c.GetProperty("track").GetInt32() == 0 && c.GetProperty("reason").GetString() == "completed"),
            "Natural global completion already has its exact reset scheduled; the actual infinite music loop must survive.");
        Play("players", 13, 1, tick + 1);
        Check(Mixes().Length == count, "Expired history must compare at NOW, without replaying old restores.");
        playback.StopAll(); playback.Flush();
        Play("main1", 0, 0, tick + 1);
        Check(Mixes().Length == count, "Stop-all/new music must not retain an old duck or cache.");

        Play("cursors", 80, 0, tick + 1);
        long primaryStart = Sample(tick);
        for (int track = 1; track < 6; track++) Play("cursors", 80, track, tick);
        var fullMatrix = new List<(long Sample, double Gain)>();
        foreach (long offset in duck.GlobalMixer!.PrimaryZeroLevels.Concat(duck.GlobalMixer.GlobalMusic)
            .Select(point => point.Frame * 240L).Append(0).Distinct().Order().Where(sample => sample < duck.Pcm.SampleCount))
        {
            double gain = NativeGain(duck.GlobalMixer.PrimaryZeroLevelAt(offset), 5 * duck.GlobalMixer.GlobalLevelAt(offset), 115);
            if (fullMatrix.Count == 0 || fullMatrix.Last().Gain != gain)
                fullMatrix.Add((primaryStart + offset, gain));
        }
        var matrixPoints = Mixes().Last().GetProperty("mixPoints").EnumerateArray().ToArray();
        Check(matrixPoints.Length == fullMatrix.Count && matrixPoints.Zip(fullMatrix).All(pair =>
            pair.First.GetProperty("atSample").GetInt64() == pair.Second.Sample &&
            pair.First.GetProperty("gain").GetDouble() == pair.Second.Gain),
            "All SIX actual13/14 columns, including the music programme's qualified primary-zero timeline, must retain their entire exact future.");
        count = Mixes().Length;
        playback.AdvanceTo(((primaryStart + duck.Pcm.SampleCount) * 57 + 31249) / 31250);
        playback.Flush(); tick = playback.SourceTick;
        Check(Mixes().Length == count && sink.Commands.Any(c => c.GetProperty("kind").GetString() == "stop" &&
            c.GetProperty("track").GetInt32() == 0 && c.GetProperty("reason").GetString() == "completed"),
            "Finite primary-zero music must complete naturally without replaying its cached future.");
        Play("main1", 0, 0, tick + 1);
        Check(Mixes().Length == count, "New music after natural completion must begin with a clean voice-bound cache.");

        // A separate production-command boundary fixture retains the actual 011 source coordinates.
        playback.AdvanceTo(6827); playback.Flush();
        int boundaryStart = sink.Batches.Count;
        Play("players", 13, 1, 6827);
        playback.SetActive(false); playback.Flush();
        playback.SetActive(true); playback.Flush();
        Play("cursors", 80, 2, 6828);
        File.WriteAllText(Path.Combine(output, "envelopes", "timing-fixture.json"), JsonSerializer.Serialize(new {
            schema = "source-player-audio-timing-fixture/v1", directedAdmittedProgrammes = true,
            nativeDspEquivalent = false, samples = sink.SampleInputs, batches = sink.Batches, boundaryStart,
            exactCases = new[] { "ordinary-noop", "six-columns", "primary-zero-six-global-columns", "equal-now-different-future", "old-global-preempted-by-ordinary",
                "multi-stop", "volume-zero-restore", "pan-preserves-duck", "music-replacement", "natural-end", "loop", "stop-all" }
        }));
        Console.WriteLine("PASS semantic current/full-future music envelopes: directed legitimate admitted programmes; " +
            "all six columns, atomic preemption/removal,240-sample restores, volume/pan, new music, completion and loops.");
    }

    private sealed class TimingHostSink : RecordingSink, ISourceAudioSink, ISourceAudioClockWitness
    {
        internal readonly CompleteAudioSink Audio;
        internal readonly List<double> BatchHostTimes = new();
        internal double HostTime, DiagnosticRenderElapsed;
        internal int WitnessReads;
        internal TimingHostSink(string output) { Audio = new CompleteAudioSink(output); }
        public double ReadSourceElapsed(long epoch) { WitnessReads++; return DiagnosticRenderElapsed; }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata) =>
            Audio.AudioSample(id, rate, channels, pcm, metadata);
        public void AudioCommands(string text)
        {
            Audio.AudioCommands(text);
            BatchHostTimes.Add(HostTime);
        }
    }

    private static void VerifyAudioHostTiming(PlayerData data, string output)
    {
        var sink = new TimingHostSink(Path.Combine(output, "host"));
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        RealtimeRandom? random = null;
        var host = new PlayerHost(data, sink, randomFactory: _ => random = new RealtimeRandom(), seed: 7, recordStore: store);
        void Raw(int button = 0, double x = 0) =>
            host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { button, 0, 0, 0 });
        void PumpMenu()
        {
            for (int i = 0; i < 70 && host.Session.UsingSourcePreGameInput; i++)
            { Raw(); host.Advance(1d / 57, 512, 384, 512, 384, 1); }
        }
        void Tap(int button = 0, double x = 0)
        {
            Raw(); Raw(button, x); Raw(); host.Advance(0, 512, 384, 512, 384, 1); PumpMenu();
        }
        host.Advance(0, 512, 384, 512, 384, 1);
        Tap(4); Tap(x: 1); Tap(1);
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams, "Public guest choice must reach the actual team menu, with no record/PIN fixture.");
        Tap(1); Tap(1); Tap(1);
        Check(host.Session.Playing && host.Game.SourceTick == 0, "Public team choices must construct the real fresh source-zero game.");
        Raw(); Raw(); host.Advance(0, 512, 384, 512, 384, 1);
        long epoch = host.AudioPlayback.Epoch;
        int gameStart = sink.Audio.Batches.FindIndex(text => {
            using var batch = JsonDocument.Parse(text);
            return batch.RootElement.GetProperty("epoch").GetInt64() == epoch &&
                batch.RootElement.GetProperty("commands")[0].GetProperty("kind").GetString() == "reset";
        });
        int Starts() => sink.Audio.Commands.Count(c => c.GetProperty("kind").GetString() == "clock-start");
        long origin = sink.Audio.Commands.Last(c => c.GetProperty("kind").GetString() == "clock-start")
            .GetProperty("sourceTick").GetInt64();
        int starts = Starts();
        double h = 0;
        void Frame(double actual, double? nominal = null, int button = 0)
        {
            h += actual; sink.HostTime = h;
            host.CaptureObserved(new double[8], new[] { button, 0, 0, 0 }, h);
            Check(host.AdvanceObserved(nominal ?? actual, h, 512, 384, 512, 384, 1) <= 6,
                "The actual Host must keep its six-step callback bound.");
        }
        double Pending() => sink.State.GetProperty("realtimeClock").GetProperty("PendingSeconds").GetDouble() +
            sink.State.GetProperty("realtimeClock").GetProperty("pauseCarrySeconds").GetDouble();
        Frame(0);
        for (int i = 0; i < 8000 && host.Game.SourceTick < 6827; i++) Frame(1d / 60);
        Check(host.Game.SourceTick == 6827 && random != null,
            "The exact 011 source6827 coordinate must be reached by real Host steps, never a source tick setter.");
        Frame(.006056666652);
        var pauses = new List<object>();
        foreach (double refill in new[] { .08533333333333333, .128, .08533333333333333 })
        {
            int batchStart = sink.Audio.Batches.Count;
            double pausedH = h;
            host.SetActive(false);
            long pausedHost = host.Game.State.Tick, pausedSource = host.Game.SourceTick;
            int draws = random!.Draws.Count;
            double carry = Pending();
            host.AdvanceObserved(10, h, 512, 384, 512, 384, 1);
            sink.DiagnosticRenderElapsed += refill;
            Check(host.Game.State.Tick == pausedHost && host.Game.SourceTick == pausedSource && random.Draws.Count == draws,
                "Paused wall time and independent render refill must consume no physics, input scan or RNG.");
            host.CaptureObserved(new double[8], new int[4], h);
            host.CaptureObserved(new double[8], new int[4], h);
            host.SetActive(true);
            Frame(.012620, 0);
            long expected = (long)Math.Floor((carry + .012620 + 1e-10) * 60);
            Check(host.Game.State.Tick == pausedHost + expected &&
                Math.Abs(Pending() - carry - .012620 + expected / 60d) < 1e-8,
                "Actual 12.620ms H, not85.333/128ms C refill or nominal0, must drive the ready resume.");
            Frame(.047380, 0);
            expected = (long)Math.Floor((carry + .060 + 1e-10) * 60);
            Check(host.Game.State.Tick == pausedHost + expected && Pending() < 1d / 60,
                "Repeated resumes must retain causal fractions without extra Bridge priming.");
            long before = host.Game.State.Tick, source = host.Game.SourceTick;
            draws = random.Draws.Count;
            sink.DiagnosticRenderElapsed += 2048d / 48000;
            Frame(0);
            Check(host.Game.State.Tick == before && host.Game.SourceTick == source && random.Draws.Count == draws &&
                host.AudioPlayback.Epoch == epoch && Starts() == starts &&
                sink.Audio.Commands.Last(c => c.GetProperty("kind").GetString() == "clock-start")
                    .GetProperty("sourceTick").GetInt64() == origin,
                "A C-only render batch must not consume source/input/RNG or change the epoch/origin, even after repeated pauses.");
            for (int i = 0; i < 6; i++) Frame(1d / 60);
            Check(host.Game.ConsumedSourceSwitches != null &&
                host.Game.ConsumedSourceSwitches.SourceTick == host.Game.SourceTick &&
                host.Game.ConsumedSourceSwitches.InputEpoch == host.Game.SourceInputEpoch &&
                host.Game.ConsumedSourceSwitches.PhysicalSwitches.IsNeutral,
                "Resume must consume the complete real neutral physical FIFO in the actual fresh input epoch.");
            pauses.Add(new { pausedSource, pausedHost, carry, pausedH, batchStart, batchEnd = sink.Audio.Batches.Count,
                hostResumeSeconds = .012620, renderRefillSeconds = refill,
                resumedSource = host.Game.SourceTick, inputEpoch = host.Game.SourceInputEpoch,
                epoch, origin, hostTime = h, sourceRandomDraws = random.Draws.Count });
        }
        Check(sink.WitnessReads == 0, "The diagnostic render witness must not become a simulation clock.");
        int plays = sink.Audio.Commands.Count(c => c.GetProperty("kind").GetString() == "play");
        long afterPauses = host.Game.SourceTick;
        for (int i = 0; i < 1600 && (host.Game.SourceTick - afterPauses < 240 ||
            sink.Audio.Commands.Count(c => c.GetProperty("kind").GetString() == "play") == plays); i++)
        {
            if (host.Game.State.Phase == GamePhase.DeadBall && !host.Game.C2State.ResultPending) host.Continue(0);
            Frame(1d / 60, button: host.Game.State.Phase == GamePhase.PlaySelect ? 2 : 0);
        }
        Check(host.Game.SourceTick - afterPauses >= 240 &&
            sink.Audio.Commands.Count(c => c.GetProperty("kind").GetString() == "play") > plays,
            "The actual post-resume Host must submit genuine new admitted programmes, not only no-op controls.");
        data.Catalog.RequireAllInputsConsumed();
        File.WriteAllText(Path.Combine(output, "host", "timing-fixture.json"), JsonSerializer.Serialize(new {
            schema = "source-player-host-audio-timing-fixture/v1", publicGuestControlsOnly = true,
            privateClockOrPhaseMutation = false, nominalZeroResume = true, witnessReads = sink.WitnessReads,
            gameStart, samples = sink.Audio.SampleInputs, batches = sink.Audio.Batches,
            batchHostTimes = sink.BatchHostTimes, pauses, finalSourceTick = host.Game.SourceTick,
            scope = "Actual managed Host/production playback, not browser, named match, native DSP or ready-game acceptance."
        }));
        Console.WriteLine("PASS actual Host6827 repeated causal resume: independent85.333/128ms C witnesses ignored; " +
            "real12.620ms H, neutral physical FIFO/input epochs/RNG, fractions, six-step bound and fixed C6 origin.");
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--source-realtime-clock-only")
            {
                var simulation = new GameSimulation(7);
                var bridge = new FixedStepBridge(simulation);
                var clockDriver = new SourceRealtimeFrameClock();
                double[] delayed = { .008, .141, .032, .157, .01, .097, .127 };
                int callbacks = 0;
                int Advance(double delta)
                {
                    Check(delta >= 0 && delta <= FixedStepBridge.MaximumAccumulatedSeconds,
                        "The runtime may carry elapsed time, never enlarge the bridge's per-callback bound.");
                    callbacks++;
                    int count = bridge.Advance(delta, default, false);
                    Check(count <= FixedStepBridge.MaximumStepsPerFrame, "Catch-up must keep the actual bridge six-step budget.");
                    return count;
                }
                foreach (double delta in delayed) clockDriver.Advance(delta, Advance);
                Check(clockDriver.PendingSeconds > FixedStepBridge.StepSeconds,
                    "Real long frames must retain elapsed debt, not silently lose source/audio clock time.");
                for (int i = 0; i < 120; i++) clockDriver.Advance(FixedStepBridge.StepSeconds, Advance);
                int expectedTicks = (int)Math.Floor((delayed.Sum() + 120 * FixedStepBridge.StepSeconds) /
                    FixedStepBridge.StepSeconds + 1e-8);
                Check(simulation.State.Tick == expectedTicks && clockDriver.HostSteps == expectedTicks &&
                    clockDriver.PendingSeconds < FixedStepBridge.StepSeconds,
                    "Partitioned real elapsed time must eventually produce every actual host step without a source-clock setter.");
                var referenceSimulation = new GameSimulation(7);
                var referenceBridge = new FixedStepBridge(referenceSimulation);
                for (int i = 0; i < expectedTicks; i++) referenceBridge.Advance(FixedStepBridge.StepSeconds, default, false);
                Check(referenceSimulation.State.Tick == simulation.State.Tick &&
                    referenceSimulation.State.Phase == simulation.State.Phase,
                    "Delayed and evenly partitioned actual bridge calls must preserve the same observed simulation time/state.");
                bridge.SetActive(false); clockDriver.Clear();
                long paused = simulation.State.Tick;
                bridge.SetActive(true);
                Check(bridge.Advance(0, default, false) == 0,
                    "An actual zero-time resume/ready callback clears transient input without an invented host/source wake.");
                clockDriver.Advance(FixedStepBridge.StepSeconds, Advance);
                Check(simulation.State.Tick == paused + 1 && clockDriver.HostSteps == 1 &&
                    Math.Abs(clockDriver.ObservedSeconds - FixedStepBridge.StepSeconds) < 1e-12,
                    "Paused wall time and cleared old debt must not leak into the resumed source domain.");
                Console.WriteLine("PASS bounded realtime clock: " + assertions + " assertions, " + callbacks +
                    " actual bridge callbacks; delayed/even partitions, retained debt, unchanged six-step budget and pause exclusion.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--native-mixer-reference-only")
            {
                MethodInfo multiply = typeof(SourceAudioPlayback).GetMethod("MixingMultiplier",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (string line in File.ReadLines(args[1]))
                {
                    using var native = JsonDocument.Parse(line);
                    JsonElement value = native.RootElement;
                    if (!value.TryGetProperty("actualUnsigned15Multipliers", out var expected)) continue;
                    int[] cells = value.GetProperty("cells").EnumerateArray().Select(e => e.GetInt32()).ToArray();
                    int[] levels = value.GetProperty("levels").EnumerateArray().Select(e => e.GetInt32()).ToArray();
                    for (int channel = 0; channel < 6; channel++)
                    {
                        int sum = cells.Skip(channel * 6).Take(6).Sum();
                        Check(sum >= short.MinValue && sum <= short.MaxValue,
                            "Independent native fixture must stay within its explicitly qualified16-bit sum domain.");
                        int actual = (int)multiply.Invoke(null, new object[] { sum, levels[channel] })!;
                        Check(actual == expected[channel].GetInt32(),
                            "C6 unsigned1.15 transfer differs from independently executed original2ECC firmware.");
                    }
                }
                Check(assertions >= 24, "Independent native mixer comparison did not consume its actual ISA cases.");
                Console.WriteLine("PASS independent native mixer operator: " + assertions +
                    " assertions; signed sums/clamps/channel levels and unsigned1.15 coefficients. Not outer/master PCM equivalence.");
                return 0;
            }
            if (args.Length == 3 && args[1] == "--complete-original-animation-only")
            {
                string libraryRoot = Path.GetFullPath(args[0]), originalRoot = Path.GetFullPath(args[2]);
                byte[] inventory = File.ReadAllBytes(Path.Combine(libraryRoot, "export-inventory.json"));
                SourceAnimationLibrary library = SourceAnimationLibrary.Read(inventory,
                    path => File.ReadAllBytes(Path.Combine(libraryRoot, path)));
                SourceTeamResourceProfile.CompleteOriginal303.RequireLibrary(library);
                var legacy = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(originalRoot,
                    "animation/export-inventory-presnap-switch.json")));
                foreach (JsonElement entry in legacy.RootElement.GetProperty("clips").EnumerateArray())
                {
                    string name = entry.GetProperty("runtime_name").GetString()!;
                    Check(library.GetClip(name).DecodedSha256 == entry.GetProperty("sha256").GetString(),
                        "CompleteOriginal303 must preserve inherited clip bytes, not relabel old282.");
                }
                string[] extras = ("ENDZ_06 ENDZ_08 ENDZ_20 INZONE1 INZONE2 INZONE3 M_TAUNBC M_EZCTHR " +
                    "M_EZCGUN M_CELCHE M_EZCWHA M_EZWAV1 M_EZCSPN M_EZCHRS M_EZPUSH M_EZJASN M_EZDNCE " +
                    "M_EZJON1 M_EZJON2 R_SLO_D1 R_SLO_D2").Split(' ');
                foreach (string name in extras)
                    Check(library.GetClip(name).SelectedSourceFrames.Count > 0,
                        "Actual full selected original TD/reaction sequence is unreadable: " + name);
                SourceAudioFunctionData hit = SourceAudioFunctionData.Read(File.ReadAllBytes(Path.Combine(originalRoot,
                    "kernel/m-qbfite-hit-extension.safn")), SourceInterfacePackagePlan.C2HitSha256);
                SourceC2CallbackData callback = SourceC2CallbackData.Read(File.ReadAllBytes(Path.Combine(libraryRoot,
                    "c2-complete-original-callback-data.json")), SourceTeamResourceProfile.CompleteOriginalCallbackSha256, hit);
                Check(callback.Profile == SourceC2CallbackData.CompleteOriginalProfile &&
                    callback.AnimationManifestSha256 == library.InventorySha256 && callback.Bindings.Count == 15,
                    "The separately authenticated303 callback generation must bind this SAME new inventory.");
                SourceTeamResourceProfile.CompleteOriginal303.RequireImpact(SourceCinematicLibrary.Read(
                    File.ReadAllBytes(Path.Combine(libraryRoot, "impact-complete-original-presentation.json")),
                    SourceTeamResourceProfile.CompleteOriginalImpactSha256));
                bool oldRejected = false;
                try { SourceTeamResourceProfile.Contact282C2M1DefaultAudibles.RequireLibrary(library); }
                catch (SourceInterfaceException) { oldRejected = true; }
                Check(oldRejected, "The complete303 library must never pass the old282 capability gate.");
                Console.WriteLine("PASS complete-original animation: " + assertions +
                    " assertions;303 unique clips/30777 selected frames, exact21 additions and preserved282 inputs.");
                return 0;
            }
            if (args.Contains("--source-render-observation-only"))
            {
                if (args.Length != 1)
                {
                    Console.Error.WriteLine("Usage: SourcePlayer.Checks --source-render-observation-only");
                    return 2;
                }
                return AffinePacketChecks.RunRenderObservation();
            }
            if (args.Contains("--source-affine-packet-only"))
            {
                if (args.Length != 1)
                {
                    Console.Error.WriteLine("Usage: SourcePlayer.Checks --source-affine-packet-only");
                    return 2;
                }
                AffinePacketChecks.Run();
                return 0;
            }
            if (args.Contains("--source-callback-bootstrap-only"))
            {
                if (args.Length != 1)
                {
                    Console.Error.WriteLine("Usage: SourcePlayer.Checks --source-callback-bootstrap-only");
                    return 2;
                }
                return VerifyCallbackBootstrapSelection();
            }
            if (args.Length == 1 && args[0] == "--free-ball-synthetic") { FreeBallChecks.Synthetic(); return 0; }
            if (args.Length < 1) throw new ArgumentException("Explicit prepared private data root required.");
            string root = Path.GetFullPath(args[0]);
            byte[] manifest = File.ReadAllBytes(Path.Combine(root, "manifest.json"));
            byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, path));
            if (args.Contains("--default-audibles-adapters-only"))
            {
                int index = Array.IndexOf(args, "--proof-output");
                DefaultAudiblesChecks.Run(manifest, Read, index >= 0 ? args[index + 1] : null);
                return 0;
            }
            if (args.Contains("--loader-contract-only"))
            {
                int index = Array.IndexOf(args, "--proof-output");
                VerifyLoaderContract(manifest, Read, index >= 0 ? args[index + 1] : null);
                return 0;
            }
            var catalog = new DataCatalog(manifest, Read);
            var data = new PlayerData(catalog);
            if (args.Contains("--two-station-join-revision-only"))
            {
                VerifyTwoStationJoinRevision(data);
                Console.WriteLine("PASS independent TwoStation join revision: " + assertions);
                return 0;
            }
            if (args.Contains("--a1-v1-runtime-inventory"))
            {
                int output = Array.IndexOf(args, "--proof-output");
                if (output < 0) throw new ArgumentException("Runtime inventory needs an explicit private --proof-output.");
                WriteAvRuntimeInventory(data, args[output + 1]); return 0;
            }
            if (args.Contains("--cabinet-participation-only"))
            { VerifyCabinetParticipation(data, manifest, Read); return 0; }
            if (args.Contains("--gc-skill-binding-only"))
            { VerifyGcSkillBinding(data); return 0; }
            if (args.Contains("--complete-av306-admission-only"))
            { VerifyCompleteAv306Admission(data, manifest, Read); return 0; }
            if (args.Contains("--two-station-records-only"))
            { VerifyTwoStationRecords(data, manifest, Read); return 0; }
            if (args.Contains("--original-team-selection-output"))
            { VerifyOriginalTeamSelection(data, args[Array.IndexOf(args, "--original-team-selection-output") + 1]); return 0; }
            if (args.Contains("--audio-timing-only"))
            {
                int index = Array.IndexOf(args, "--proof-output");
                Check(index >= 0 && index + 1 < args.Length, "Timing tests require a new owned proof output.");
                VerifyAudioEnvelopes(data, Path.GetFullPath(args[index + 1]));
                VerifyAudioHostTiming(data, Path.GetFullPath(args[index + 1]));
                return 0;
            }
            if (args.Contains("--source-realtime-actual"))
            {
                VerifyRealtimeSource(data);
                if (data.Catalog.Profile.StableId == SourceTeamResourceProfile.CompleteOriginalPrematchStableId)
                    VerifyRealtimeRenderAdmission(data);
                return 0;
            }
            if (args.Contains("--prematch-inputs-only"))
            {
                Check(catalog.Profile.IsCompleteOriginalPrematch303 && data.OriginalPresentation!.HasPrematchArt &&
                    data.AudioAdmission!.Programs.Count == 1081, "The new family must actually load all original303 images and1081 programmes.");
                _ = new RenderAssets(data, new RecordingSink());
                catalog.RequireAllInputsConsumed();
                var mixerSink = new CompleteAudioSink();
                var playback = new SourceAudioPlayback(data.AudioAdmission!, mixerSink, 57);
                var driver = new MixerPreludeDriver();
                var service = new SourcePreGameSoundService(SourcePreGameSoundEntry.FromOriginalPermanentBanksAfterAcceptedStart(),
                    SourcePreGameSoundService.CreateManagedPreviewRandom(1), driver);
                service.CapturePhysical(new SourcePhysicalSwitchFrame(Enumerable.Range(0, 4).Select(id =>
                    new SourceStationInput(id, 0, 0, id == 0 ? SourceButtons.C : SourceButtons.None,
                        digitalCurrent: SourceDigitalDirections.None)).ToArray()));
                var order = new SourceSoundOutputOrder();
                for (int wakeIndex = 0; wakeIndex < 180; wakeIndex++)
                {
                    service.AdvanceHostTick();
                    if (service.TryTakeEntryActivation(out SourcePreGameSoundActivation activation))
                        playback.BeginPreGameEpoch(playback.Epoch + 1, activation);
                    if (!service.OriginalEntryActivated) continue;
                    playback.AdvanceTo(service.SourceTick);
                    var calls = service.TakeSourceProcessSoundCalls();
                    var controls = service.TakeSourceSoundControls();
                    foreach (var item in order.Begin(Array.Empty<long>(), Array.Empty<long>(),
                        calls.Select(call => call.OutputSequence).ToArray(), controls.Select(call => call.OutputSequence).ToArray()))
                    {
                        if (item.Kind == SourceSoundOutputKind.Process) playback.Consume(calls[item.Index]);
                        else playback.Consume(controls[item.Index]);
                        order.Complete(item.OutputSequence);
                    }
                    playback.Flush();
                }
                Check(mixerSink.Commands.Any(c => c.GetProperty("kind").GetString() == "program-mix" &&
                    c.GetProperty("mixPoints").EnumerateArray().Any(point => point.GetProperty("gain").GetDouble() < 1)),
                    "The actual welcome child must execute its global0 duck on the real intro voice, not just play isolated PCM.");
                var programme = data.AudioAdmission!.RequireProgram("cursors", 80).Program;
                Check(programme.GlobalMixer != null && programme.Pcm.Sha256 != programme.GlobalMixer.PrimaryZeroPcm.Sha256 &&
                    programme.GlobalMixer.GlobalLevelAt(0) == -640 &&
                    programme.GlobalMixer.GlobalLevelAt(318 * 240) == 0,
                    "The authentic global0 and programme-local stream contexts must remain distinct and reach their actual00 reset.");
                byte[] actualBank = catalog.Read("audio/cursors.saud");
                byte[] nativePin = Convert.FromHexString(SourceAudioMixerProgram.NativeReferenceSha256);
                int pinOffset = Enumerable.Range(0, actualBank.Length - nativePin.Length)
                    .First(index => actualBank.AsSpan(index, nativePin.Length).SequenceEqual(nativePin));
                foreach (int changed in new[] { pinOffset, pinOffset + nativePin.Length + 6 })
                {
                    byte[] mutation = (byte[])actualBank.Clone();
                    mutation[changed] = 0;
                    if (changed != pinOffset) mutation[changed + 1] = 0;
                    bool rejected = false;
                    try { SourceAudioBankReader.Read(mutation, DataCatalog.Hash(mutation)); }
                    catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "Changing the real native-operator reference or removing its global duck must fail even with a recomputed outer package hash.");
                }
                Console.WriteLine("PASS original pregame inputs/global mixer: " + assertions +
                    " assertions;963 exact inputs,303 images,1081 programmes; actual source pregame/welcome process outputs and live music duck. Directed component fixture, not browser/natural-match acceptance.");
                return 0;
            }
            if (args.Contains("--original-hut-sounds-only")) { VerifyOriginalHutSounds(data); return 0; }
            if (args.Contains("--original-contact-sounds-only")) { VerifyOriginalContactSounds(data); return 0; }
            if (args.Contains("--original-period-audio-only")) { VerifyOriginalPeriodAudio(data); return 0; }
            if (args.Contains("--original-animation-core-output"))
            {
                ExportOriginalAnimationCore(data, args[Array.IndexOf(args, "--original-animation-core-output") + 1]);
                return 0;
            }
            if (args.Contains("--original-animation-callers-output"))
            {
                VerifyOriginalAnimationCallers(data, args[Array.IndexOf(args, "--original-animation-callers-output") + 1]);
                return 0;
            }
            if (args.Contains("--original-gameplay-sounds-only"))
            {
                int turn = Array.IndexOf(args, "--turn-oracle-output");
                int coverage = Array.IndexOf(args, "--av-coverage-output");
                VerifyOriginalGameplaySounds(data, turn < 0 ? null : args[turn + 1],
                    coverage < 0 ? null : args[coverage + 1]);
                return 0;
            }
            if (args.Contains("--complete-original-inputs-only"))
            {
                Check(catalog.Profile.IsCompleteOriginal303 && data.OriginalPresentation != null && data.AudioAdmission != null,
                    "The actual complete profile must bind original artwork and actual decoded playback, not dormant recordings.");
                if (catalog.Profile.IsCompleteOriginalFinal303)
                {
                    Check(data.Impact.AnimationCues.Count == 508 && data.Impact.CameraOperandBindings.Count == 36,
                        "Final TD admission must retain the real eleven additional camera operands and fifteen source sound sites.");
                    foreach (var binding in data.Impact.CameraOperandBindings.Where(site => new[] {
                        7849, 7981, 8026, 15723, 15763, 17279, 17331, 17494, 17567, 17666, 17889
                    }.Contains(site.OperandLine)))
                    {
                        Check(data.Impact.RequireExecutedOpcode(binding.Script, binding.OpcodeLine, "camera").Arguments[0] == binding.Table &&
                            data.Impact.RequireExecutedOpcode(binding.Script, binding.OperandLine, "camera").Arguments[0] == binding.Table,
                            "Actual original TD byte and WORD sites must both resolve their same authentic source camera table.");
                    }

                    bool guessedSiteRejected = false;
                    try { data.Impact.RequireExecutedOpcode("end_zone10_anim", 17668, "camera"); }
                    catch (InvalidOperationException) { guessedSiteRejected = true; }
                    Check(guessedSiteRejected, "TD source-identity qualification must not generalize to a guessed neighboring instruction.");
                }
                var graphics = new RecordingSink();
                _ = new RenderAssets(data, graphics);
                foreach (SourceOriginalRaster shadow in data.OriginalPresentation!.Shadows)
                {
                    byte[] pixels = shadow.CopyRgba();
                    Check(graphics.Textures.ContainsKey(shadow.TextureId) &&
                        Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] > 100 && pixels[i * 4 + 3] < 255),
                        "Actual fractional-alpha source shadow pixels must reach the same renderer resource sink.");
                }
                catalog.RequireAllInputsConsumed();
                int proofIndex = Array.IndexOf(args, "--proof-output");
                var audioSink = new CompleteAudioSink(proofIndex < 0 ? null : args[proofIndex + 1]);
                var playback = new SourceAudioPlayback(data.AudioAdmission!, audioSink, 57);
                playback.LoadBank("main1");
                playback.Flush();
                Check(audioSink.Samples.Count == 0,
                    "Complete original admission must not eagerly upload109MB of unused original PCM to WebAudio.");
                var original = data.AudioAdmission!.RequireProgram("main1", 0).Program;
                SourceAudioBank playersBank = data.AudioAdmission.Banks.Single(bank => bank.BankName == "players");
                Check(SourceAudioSparseSlots.IsUnassigned(playersBank, 10) &&
                    !SourceAudioSparseSlots.IsUnassigned(playersBank, 192),
                    "The real original unallocated players10 slot must remain distinct from assigned-but-unqualified opcode13 player192.");
                Check(!original.IsolatedPlaybackQualified && original.CompiledPlaybackQualified &&
                    original.LoopStartSample < original.LoopEndSample,
                    "Real multi-stream original music requires the executed playlist/loop profile, never an isolatedQualified flip.");
                byte[] malformed = (byte[])catalog.Read("audio/main1.saud").Clone();
                int entryOffset = 9 + malformed[8] + 52 + 4;
                int programBytes = malformed[entryOffset + 6] | malformed[entryOffset + 7] << 8;
                int loopOffset = entryOffset + 8 + programBytes + 4;
                malformed[loopOffset]++;
                bool malformedRejected = false;
                try { SourceAudioBankReader.Read(malformed, DataCatalog.Hash(malformed)); }
                catch (InvalidDataException) { malformedRejected = true; }
                Check(malformedRejected,
                    "Mutating the real original music package loop off its DSP-frame boundary must fail even with a recomputed outer package hash.");
                playback.Consume(new SourceDirectSoundCall(1, 1, 1, "directed-playlist-component-fixture",
                    0, 0, 0, TeamId.Home, "nfl.c", "InitGameProc", 601, "main1", 0, 115, 127, 0x8000, 0, 1));
                playback.Flush();
                JsonElement play = audioSink.Commands.Single(c => c.GetProperty("kind").GetString() == "play");
                Check(play.GetProperty("loop").GetBoolean() &&
                    play.GetProperty("loopStartSample").GetInt32() == original.LoopStartSample &&
                    play.GetProperty("loopEndSample").GetInt32() == original.LoopEndSample &&
                    audioSink.Samples.Single().Value == original.Pcm.SampleCount * 2,
                    "Actual original playlist PCM and independently verified loop region must reach the consumer.");
                playback.AdvanceTo(5700); playback.Flush();
                Check(!audioSink.Commands.Any(c => c.GetProperty("kind").GetString() == "stop"),
                    "Real original infinite music must survive beyond an isolated sample duration.");
                playback.StopTracks(1); playback.DeleteBank("main1"); playback.Flush();
                Check(audioSink.Commands.Last().GetProperty("kind").GetString() == "bank-delete" &&
                    audioSink.Commands.Any(c => c.GetProperty("kind").GetString() == "stop" &&
                        c.GetProperty("reason").GetString() == "source-stop-track"),
                    "Source stop and bank deletion must be distinct actual receiver commands.");
                playback.Reset(1); playback.Flush(); audioSink.Save();
                Console.WriteLine("PASS complete-original inputs: " + assertions +
                    " assertions;" + catalog.FileCount + " inputs consumed, real shadows uploaded, real compiled music/loop/stop delivered. Directed component fixture, not a natural game.");
                return 0;
            }
            if (args.Contains("--complete-original-host-only"))
            {
                var avSink = new CompleteAvSink();
                var avHost = new PlayerHost(data, avSink,
                    options: PreviewOptions.FromUrl("?source-records=disabled", catalog.Profile.StableId));
                CabinetRender(avHost);
                CabinetStart(avHost);
                for (int step = 0; step < 6000; step++)
                {
                    int buttons = avHost.Game.State.Phase == GamePhase.PlaySelect ? 2 : 0;
                    CabinetRaw(avHost, new[] { buttons, 0, 0, 0 });
                    CabinetRender(avHost, 1);
                }
                catalog.RequireAllInputsConsumed();
                Check(avHost.Game.SourceTick > 300 && avHost.AudioPlayback.ScheduledCalls > 2 &&
                    avHost.BootSoundOutputsDelivered > 0 && avHost.World.Packet.Links.Count > 0,
                    "The actual original-source composition must advance moving football, real source0 audio and articulated shadows.");
                Console.WriteLine("PASS complete-original actual host: " + assertions + " assertions, sourceTick" +
                    avHost.Game.SourceTick + ", bootOutputs" + avHost.BootSoundOutputsDelivered +
                    ", originalAudioCalls" + avHost.AudioPlayback.ScheduledCalls + ". Public cabinet/input only.");
                return 0;
            }
            if (args.Contains("--source-world-only"))
            {
                int index = Array.IndexOf(args, "--proof-output");
                VerifySourceWorld(data, index >= 0 ? args[index + 1] : null);
                return 0;
            }
            if (args.Contains("--portable-instructions-only")) { VerifyPortableInstructions(data, manifest, Read); return 0; }
            if (args.Contains("--portable-instructions-natural")) { VerifyNaturalPortableInstructions(data); return 0; }
            if (args.Contains("--cabinet-session-only")) { VerifyCabinetSession(data); return 0; }
            if (args.Contains("--cabinet-full-match")) { VerifyNaturalCabinetMatch(data); return 0; }
            if (args.Contains("--source-gameflow-only")) { VerifySourceCabinetFlow(data); return 0; }
            if (args.Contains("--arm-rendering-only"))
            {
                int index = Array.IndexOf(args, "--proof-output");
                ArmRenderingChecks.Run(data, index >= 0 ? args[index + 1] : null);
                return 0;
            }
            if (args.Contains("--defensive-presnap-switch-only"))
            {
                int index = Array.IndexOf(args, "--proof-output");
                PreSnapSwitchChecks.Run(data, index >= 0 ? args[index + 1] : null);
                return 0;
            }
            if (args.Contains("--random-selection-only")) { VerifyRandomSelection(data); return 0; }
            if (args.Contains("--loose-helmet-only")) { HelmetAdapterChecks.Run(data); return 0; }
            if (args.Contains("--dust-only")) { DustAdapterChecks.Run(data); return 0; }
            if (args.Contains("--ball-trails-only")) { BallTrailChecks.Run(data); return 0; }
            if (args.Contains("--fire-adapter-only")) { FeatureChecks.Run(data); return 0; }
            if (args.Contains("--free-ball-only")) { FreeBallChecks.Run(data); return 0; }
            if (args.Length == 3 && args[1] == "--free-ball-reference") { FreeBallChecks.VerifyReference(data, args[2]); return 0; }
            Check(data.Animations.SequenceNames.Count == catalog.Profile.ClipCount &&
                data.Animations.FrameCount == catalog.Profile.FrameCount,
                "The actual exact-profile library must feed gameplay and pose sampling.");
            VerifyCallbackBootstrap(catalog.Profile, data.Callbacks.Profile, data.Callbacks.Bindings.Count);
            int inventoryPredicateAssertions = VerifyUploadedInventoryPredicate();
            var sink = new RecordingSink();
            var host = new PlayerHost(data, sink);
            host.Advance(0, 1024, 768, 1024, 768, 1);
            Check(sink.State.GetProperty("actorCount").GetInt32() == 14 &&
                sink.State.GetProperty("jointsPerActor").GetInt32() == 15 && sink.Joints.Length == 14 * 15 * 16,
                "Actual production fourteen-actor/fifteen-joint render data is required, not static inspection.");
            Check(HasCompleteUploadedInventory(sink.Models.Count, sink.WorldDraws, data.Teams.Textures.Count,
                sink.Textures, data.Catalog.Profile.IsContact282C2M1DefaultAudibles),
                "The actual complete original model selector, world and texture inventories must be uploaded.");
            Check(sink.HasOriginalBall, "The original medball2/HEDSHU free-ball mesh must be bound, never a hand extraction or sphere.");
            var actors = sink.State.GetProperty("actors").EnumerateArray().ToArray();
            Check(actors.Count(a => a.GetProperty("team").GetString() == "Home") == 7 &&
                actors.Count(a => a.GetProperty("team").GetString() == "Away") == 7,
                "Both source teams need their seven real original actor/roster identities.");
            var uniforms = actors.Select(a => a.GetProperty("uniformHash").GetString()).Distinct().ToArray();
            Check(uniforms.Length == 2 && actors.All(a => a.GetProperty("tint").GetString() == "none"),
                "Different original team texture bindings, not shared RGB meshes/tints, are mandatory.");
            Check(sink.State.GetProperty("hud").GetProperty("vertices").GetInt32() > 0 &&
                sink.HudTextures.All(t => t.StartsWith("hud/") && sink.Textures.ContainsKey(t)),
                "Actual original HUD atlas/font/draw commands must be consumed.");
            VerifyJointMath(data, host);
            var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
            var input = new SourceCabinetInputBuffer(data.Configuration.Match);
            var configured = data.Configuration.Match.Stations;
            Check(configured.Count > 0, "Public cabinet checks require an explicitly configured human station.");
            int firstStation = configured[0].StationId;
            SourceCabinetSample[] CabinetSamples(SourceButtons held) => configured.Select(station =>
                new SourceCabinetSample(station.StationId, quantizer.SevenLevel(0, 0),
                    station.StationId == firstStation ? held : SourceButtons.None)).ToArray();
            input.Capture(CabinetSamples(SourceButtons.B));
            input.Capture(CabinetSamples(SourceButtons.None));
            SourceInputFrame pressed = input.NextHostFrame(), released = input.NextHostFrame();
            Check(pressed.Stations.Count == configured.Count && released.Stations.Count == configured.Count &&
                pressed.Stations.Single(s => s.StationId == firstStation).Pressed == SourceButtons.B &&
                released.Stations.Single(s => s.StationId == firstStation).Released == SourceButtons.B,
                "Real cabinet buffer preserves press/release pairs even between two source wakes.");
            bool missingStationRejected = false;
            try { input.Capture(CabinetSamples(SourceButtons.None).Take(configured.Count - 1).ToArray()); }
            catch (ArgumentException error) { missingStationRejected = error.Message.Contains("all configured source stations"); }
            Check(missingStationRejected, "Actual cabinet checks retain configured-station completeness, not a single-station shortcut.");
            var beforePose = actors.Select(a => a.GetProperty("poseDigest").GetString()).ToArray();
            int uploadsBefore = sink.Meshes.Count;
            for (int i = 0; i < 90; i++)
                host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            Check(host.Game.State.Tick == 90 && host.Game.SourceTick == 85 && host.SourceWakes == 85,
                "Actual 60Hz host/57Hz source skip/hold scheduling must run, with every source wake observed.");
            Check(host.Camera.LastSourceTick == host.Game.SourceTick &&
                host.Game.SourceCameraHeadingSampleTick == host.Game.SourceTick,
                "The actual current pre-player camera frame must feed M1, not an inspection/orbit camera.");
            var nextPose = sink.State.GetProperty("actors").EnumerateArray()
                .Select(a => a.GetProperty("poseDigest").GetString()).ToArray();
            Check(!beforePose.SequenceEqual(nextPose), "Real production animation must advance; static source meshes cannot pass.");
            Check(sink.Meshes.Count == uploadsBefore, "Per-frame poses must update matrices without re-uploading static geometry.");
            long tick = host.Game.SourceTick;
            host.SetActive(false);
            host.Advance(2, 1024, 768, 1024, 768, 1);
            Check(host.Game.SourceTick == tick, "Focus pause must not advance the source clock or catch up hidden time.");
            host.SetActive(true);
            host.Capture(new double[8], new int[4]);
            host.Advance(0, 360, 270, 360, 270, 1);
            Check(sink.State.GetProperty("hud").GetProperty("vertices").GetInt32() > 0,
                "Original HUD composition must survive actual portrait CSS metrics.");
            RejectMissingAndChanged(manifest, Read, catalog.Entry("model"));
            VerifyCausalInput(data);
            VerifyDeferredOriginalHud(data);
            FreeBallChecks.Run(data);
            if (data.Fire != null) FeatureChecks.Run(data);
            if (data.PoppedHead != null) PoppedHeadChecks.Run(data);
            if (args.Contains("--require-portable"))
                Check(KernelBinding.IsPortable(host.Game) && host.Game.Compatibility == SourceGameplayCompatibility.Portable,
                    "A player-ready test must reject an unbound portable factory even when strict rendering works.");
            Directory.CreateDirectory("tools/source-player/artifacts");
            File.WriteAllText("tools/source-player/artifacts/native-frame.json", host.LastStatus);
            Console.WriteLine(JsonSerializer.Serialize(new {
                status = "passed", assertions, factory = KernelBinding.FactoryFor(catalog.Profile), policy = host.Game.Compatibility.ToString(),
                portableBound = KernelBinding.IsPortable(host.Game), sourceTick = host.Game.SourceTick,
                originalModels = sink.Models.Count, originalMeshes = sink.Meshes.Count,
                originalTextures = sink.Textures.Count, worldDraws = sink.WorldDraws,
                inventoryPredicateAssertions,
                boundary = "Actual production loader/pose/input/57Hz host checks; not a browser, Unity build, M1 parity trace or native pixel qualification."
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static bool HasCompleteUploadedInventory(int models, int worldDraws, int teamTextures,
        IReadOnlyDictionary<string, string> textures, bool defaultAudibles)
    {
        return models == 32 && worldDraws > 0 &&
            textures.Count == teamTextures + 28 + (defaultAudibles ? 1 : 0) &&
            textures.ContainsKey("hud/interface-white") &&
            textures.ContainsKey("hud/interface-chroma") == defaultAudibles;
    }

    private static int VerifyUploadedInventoryPredicate()
    {
        int before = assertions;
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < 29; i++) textures.Add("synthetic/" + i, "synthetic-inventory-control");
        textures.Add("hud/interface-white", "synthetic-inventory-control");
        Check(HasCompleteUploadedInventory(32, 1, 2, textures, false), "Inventory control: exact historical inventory.");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: default audibles cannot use the historical count/masks.");
        textures.Add("hud/interface-chroma", "synthetic-inventory-control");
        Check(HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: exactly one default-audibles chroma mask.");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, false), "Inventory control: historical inventory rejects an extra chroma mask.");

        textures.Remove("hud/interface-chroma");
        textures.Remove("hud/interface-white");
        textures.Add("hud/interface-white-incorrect", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, false), "Inventory control: plausible historical count cannot replace mandatory white.");
        textures.Add("hud/interface-chroma", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: plausible default-audibles count cannot replace mandatory white.");
        textures.Remove("hud/interface-chroma");
        textures.Remove("hud/interface-white-incorrect");
        textures.Add("hud/interface-white", "synthetic-inventory-control");

        textures.Remove("synthetic/0");
        textures.Add("hud/interface-chroma", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, false), "Inventory control: chroma is forbidden historically even at the exact old count.");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: the required chroma does not waive the exact total.");
        textures.Add("synthetic/0", "synthetic-inventory-control");
        Check(HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: restored exact default-audibles inventory.");
        textures.Remove("hud/interface-chroma");
        textures.Add("hud/interface-Chroma", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: wrong-case chroma identity at a plausible count.");
        textures.Remove("hud/interface-Chroma");
        textures.Add("synthetic/replacement", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: an ordinary texture cannot stand in for chroma.");
        textures.Remove("synthetic/replacement");
        textures.Add("hud/interface-chroma", "synthetic-inventory-control");

        textures.Add("synthetic/extra", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: an extra ordinary texture fails the exact default count.");
        textures.Remove("synthetic/extra");
        textures.Remove("synthetic/0");
        Check(!HasCompleteUploadedInventory(32, 1, 2, textures, true), "Inventory control: a missing ordinary texture fails the exact default count.");
        textures.Add("synthetic/0", "synthetic-inventory-control");
        Check(!HasCompleteUploadedInventory(31, 1, 2, textures, true), "Inventory control: all32 model selectors are mandatory.");
        Check(!HasCompleteUploadedInventory(33, 1, 2, textures, true), "Inventory control: extra model selectors are rejected.");
        Check(!HasCompleteUploadedInventory(32, 0, 2, textures, true), "Inventory control: actual world draws remain mandatory.");
        Check(!HasCompleteUploadedInventory(32, -1, 2, textures, true), "Inventory control: negative world draw counts are rejected.");
        Check(!HasCompleteUploadedInventory(32, 1, 3, textures, true), "Inventory control: a changed team-texture count cannot be hidden.");
        textures.Remove("hud/interface-chroma");
        Check(HasCompleteUploadedInventory(32, 1, 2, textures, false), "Inventory control: historical count and mask behavior remain exact.");
        Check(!HasCompleteUploadedInventory(32, 1, 1, textures, false), "Inventory control: a lower team-texture count cannot be hidden.");
        return assertions - before;
    }

    private static void VerifyCallbackBootstrap(SourceTeamResourceProfile profile, string callbackProfile, int bindingCount)
    {
        bool preSnapSwitch = profile.UsesPreSnapSwitchPayload;
        Check(callbackProfile == (preSnapSwitch ? SourceC2CallbackData.PreSnapSwitchProfile : SourceC2CallbackData.M1Profile) &&
            bindingCount == (preSnapSwitch ? 15 : 14),
            "The real exact-profile callback bootstrap is required.");
    }

    private static int VerifyCallbackBootstrapSelection()
    {
        var profiles = new[] {
            (Profile: SourceTeamResourceProfile.Contact281C2M1, CallbackProfile: SourceC2CallbackData.M1Profile, BindingCount: 14),
            (Profile: SourceTeamResourceProfile.Contact282C2M1PreSnapSwitch, CallbackProfile: SourceC2CallbackData.PreSnapSwitchProfile, BindingCount: 15),
            (Profile: SourceTeamResourceProfile.Contact282C2M1DefaultAudibles, CallbackProfile: SourceC2CallbackData.PreSnapSwitchProfile, BindingCount: 15)
        };
        var cases = new List<object>();
        foreach (var sample in profiles)
            foreach (string callbackProfile in new[] { SourceC2CallbackData.M1Profile, SourceC2CallbackData.PreSnapSwitchProfile })
                foreach (int bindingCount in new[] { 14, 15 })
                {
                    bool expectedMatch = callbackProfile == sample.CallbackProfile && bindingCount == sample.BindingCount;
                    bool accepted;
                    try
                    {
                        VerifyCallbackBootstrap(sample.Profile, callbackProfile, bindingCount);
                        accepted = true;
                    }
                    catch (InvalidOperationException error) when (error.Message == "The real exact-profile callback bootstrap is required.")
                    {
                        accepted = false;
                    }
                    Check(accepted == expectedMatch,
                        $"Callback bootstrap selection mismatch for {sample.Profile.StableId}: callbackProfile={callbackProfile}, bindingCount={bindingCount}, expected={expectedMatch}, actual={accepted}.");
                    cases.Add(new { resourceProfile = sample.Profile.StableId, callbackProfile, bindingCount, expectedMatch, accepted });
                }
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "source-normal-callback-bootstrap-checks/v1",
            status = "passed-normal-callback-bootstrap-only", assertions, cases,
            boundary = "Data-free shared normal callback validation only; no loader, PlayerData, Game or PlayerHost construction."
        }));
        return 0;
    }

    private static void VerifyJointMath(PlayerData data, PlayerHost host)
    {
        foreach (PlayerView actor in host.Game.State.Players)
        {
            SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(data.Animations, data.ReferencePivots, actor, actor, 1);
            var matrix = new double[16];
            var draw = SourceInterfacePresentation.DrawState(actor, host.Game.State);
            var match = ((ISourceMatchState)host.Game.State).Match;
            var appearance = SourceTeamPresentationModel.Select(data.Rosters, match.Configuration.HomeTeam.TeamIndex,
                match.Configuration.AwayTeam.TeamIndex, (int)data.Configuration.Presentation.DesignatedHomeSide,
                actor, host.Game.State.Ball.CarrierId, draw);
            var mesh = SourceTeamGeneratedModels.BuildModel(
                SourceTeamGeneratedModels.ReadModel(data.Rosters, appearance.Model.Index, data.Header));
            for (int joint = 0; joint < 15; joint++)
            {
                var p = mesh[joint].Positions[0];
                SourceVector3 root = host.Game.SourceActorWorldPosition(actor.Id);
                FrameMath.Joint(matrix, 0, pose.Joints[joint], root, pose.ModelScale);
                SourceVector3 expected = SourceAnimationMatrices.TransformVertex(pose.Joints[joint],
                    new SourceVector3(p.X, p.Y, p.Z), pose.ModelScale);
                double x = matrix[0] * p.X + matrix[4] * p.Y - matrix[8] * p.Z + matrix[12];
                double y = matrix[1] * p.X + matrix[5] * p.Y - matrix[9] * p.Z + matrix[13];
                double z = matrix[2] * p.X + matrix[6] * p.Y - matrix[10] * p.Z + matrix[14];
                Check(Math.Abs(x - root.X - expected.X) < 0.0002 &&
                    Math.Abs(y - root.Y - expected.Y) < 0.0002 &&
                    Math.Abs(z - root.Z + expected.Z) < 0.0002,
                    "Browser joint matrix must match unchanged actual production pose transformation for actor " + actor.Id + " joint " + joint);
            }
        }
    }

    private static void VerifyCausalInput(PlayerData data)
    {
        var neutral = new PlayerHost(data, new RecordingSink());
        var controlled = new PlayerHost(data, new RecordingSink());
        bool diverged = false;
        for (int i = 0; i < 600 && !diverged; i++)
        {
            bool humanControl = neutral.Game.State.Phase == GamePhase.LiveBall && neutral.Game.State.Ball.CarrierId >= 0;
            neutral.Capture(new double[8], new int[4]);
            controlled.Capture(humanControl ? new double[] { 0, -1, 0, 0, 0, 0, 0, 0 } : new double[8],
                humanControl ? new[] { 4, 0, 0, 0 } : new int[4]);
            neutral.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            controlled.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            Check(neutral.Game.SourceTick == controlled.Game.SourceTick,
                "Input-functional counterparts must use the same real production source scheduler.");
            foreach (PlayerView actor in controlled.Game.State.Players)
                if (actor.Source.ControllerStationId >= 0)
                {
                    PlayerView baseline = neutral.Game.State.Players.First(p => p.Id == actor.Id);
                    diverged |= Math.Abs(actor.Position.X - baseline.Position.X) > .01 ||
                        Math.Abs(actor.Position.Z - baseline.Position.Z) > .01;
                }
        }
        Check(diverged, "Actual production controlled actor must causally differ with cabinet stick/turbo, not merely play a canned animation.");
    }

    private static void VerifyDeferredOriginalHud(PlayerData data)
    {
        var host = new PlayerHost(data, new RecordingSink());
        bool pending = false;
        for (int i = 0; i < 60 * 20; i++)
        {
            host.Capture(new double[8], new int[4]);
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            if (host.Game.C2State.ResultPending)
            {
                pending = true;
                Check(host.HudProcess.ResultPending && host.HudProcess.CommittedSourceTick == host.Game.SourceTick - 1,
                    "Actual C2 pending-result wake must hold the last committed original HUD, not fabricate a result or stop the kernel.");
            }
            else if (pending)
            {
                Check(((ISourceMatchState)host.Game.State).Match.Result.IsSpecified &&
                    host.HudProcess.CommittedSourceTick == host.Game.SourceTick &&
                    host.HudProcess.PendingResultWakes == 1,
                    "The actual original game_proc result must replace the held authored HUD on its next source wake.");
                return;
            }
        }
        Check(false, "The ordinary original kickoff never exercised its real C2 result/HUD commit boundary.");
    }

    private static SourceCabinetFlowView CabinetFlow(PlayerHost host) =>
        ((ISourceCabinetFlowState)host.Game.State).CabinetFlow;

    private static T FlowMethod<T>(GameSimulation game, string name) where T : Delegate =>
        (T)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke")!.GetParameters().Select(p => p.ParameterType).ToArray(), null)!
            .CreateDelegate(typeof(T), game);

    private static object FlowModel(GameSimulation game) =>
        typeof(GameSimulation).GetProperty("Model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;

    private static void FlowSet(object target, string field, object? value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static object FlowGet(object target, string field) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static SourcePortableProcessHost FlowProcesses(PlayerHost host) =>
        (SourcePortableProcessHost)FlowGet(FlowModel(host.Game), "SourceProcesses");

    private static void FlowHostStep(PlayerHost host, SourceButtons held = SourceButtons.None,
        SourceCommand command = default)
    {
        host.Bridge.AdvanceSource(FixedStepBridge.StepSeconds, () => {
            if (!host.Options.InstructionsEnabled)
                return new SourceInputFrame(host.Data.Configuration.Match.Stations.Select((s, i) =>
                    new SourceStationInput(s.StationId, held: held, command: i == 0 ? command : default)).ToArray());
            // Declared Core boundary fixtures still supply a complete explicit
            // physical sample once the real Host has bound that input domain.
            var configured = host.MatchConfiguration.Stations;
            var frame = configured.Select((s, i) => new SourceStationInput(s.StationId, held: held,
                command: i == 0 ? command : default, digitalCurrent: SourceDigitalDirections.None)).ToArray();
            var physical = Enumerable.Range(0, 4).Select(id => new SourceStationInput(id,
                held: configured.Any(s => s.StationId == id) ? held : SourceButtons.None,
                digitalCurrent: SourceDigitalDirections.None)).ToArray();
            return new SourceInputFrame(frame, new SourcePhysicalSwitchFrame(physical));
        });
    }

    private static void FlowWake(PlayerHost host, SourceButtons held = SourceButtons.None,
        SourceCommand command = default)
    {
        long tick = host.Game.SourceTick;
        for (int i = 0; i < 3 && host.Game.SourceTick == tick; i++)
        {
            FlowHostStep(host, held, command);
            command = default;
        }
        Check(host.Game.SourceTick == tick + 1, "Cabinet checks must resume exactly one actual source wake.");
    }

    private static void FlowWakes(PlayerHost host, int count, SourceButtons held = SourceButtons.None)
    {
        for (int i = 0; i < count; i++) FlowWake(host, held);
    }

    private static void FlowTo(PlayerHost host, SourceCabinetFlowStage stage,
        SourceButtons held = SourceButtons.None, int maximum = 2200)
    {
        for (int i = 0; i < maximum && CabinetFlow(host).Stage != stage; i++) FlowWake(host, held);
        Check(CabinetFlow(host).Stage == stage,
            "Production flow did not reach " + stage + "; actual " + CabinetFlow(host).Stage + "/" + host.Game.State.Phase);
    }

    private static PlayerHost FlowHost(PlayerData data,
        SourceCabinetContinuationPolicy policy = SourceCabinetContinuationPolicy.KeepParticipatingStations,
        bool fire = false)
    {
        var host = new PlayerHost(data, new RecordingSink(), options: PreviewOptions.FromUrl(
            fire ? "?source-fire=portable" : "", data.Catalog.Profile.StableId));
        int participants = data.Configuration.Match.Stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId);
        host.Game.BindSourceCabinetFlow(new SourceCabinetFlowConfiguration(participants,
            new SourceCabinetButtonMasks(0xf0, 0x70, 0xf0f0f0f0u), policy,
            SourceCabinetWaitInitialization.ZeroCountersOnEntry));
        Check(CabinetFlow(host).Stage == SourceCabinetFlowStage.Inactive &&
            CabinetFlow(host).Configuration.Contract == "source-gameflow/v1" &&
            !CabinetFlow(host).Configuration.NativeScratchContentsKnown,
            "The named portable policy must be explicitly published before the first source dispatch.");
        return host;
    }

    // Declared boundary fixtures, as in match-lifecycle, followed by the REAL C2
    // queue/next-wake commit, real scheduler and real cabinet continuations.
    private static void FlowEnd(PlayerHost host, int quarter, int home, int away, int clock = 0x10000,
        int side = 0, int down = 2, SourcePlayType kind = SourcePlayType.Normal,
        SourcePlayEndCause cause = SourcePlayEndCause.CarrierTackledInBounds,
        int? initialSide = null, SourceGameFlags flags = SourceGameFlags.None, float? endX = null)
    {
        Check(CabinetFlow(host).Stage == SourceCabinetFlowStage.Inactive, "Boundary fixtures cannot skip an active continuation.");
        if (host.Game.SourceTick == 0) FlowWakes(host, 2);
        FlowMethod<Action>(host.Game, "SourceFireSelectionExit")();
        object model = FlowModel(host.Game);
        FlowSet(model, "Quarter", quarter + 1);
        FlowSet(model, "SourcePeriodView", new SourcePeriodView(quarter));
        FlowSet(model, "HomeScore", home); FlowSet(model, "AwayScore", away);
        FlowSet(model, "SourceRules", new SourceRulesState(side, initialSide ?? side, down, 50, 80, kind));
        FlowSet(model, "Possession", (TeamId)side);
        FlowSet(model, "SourceLastPossession", side);
        FlowSet(model, "SourceOffensePlay", host.Data.Playbook.GetPlay("play_zigzag"));
        FlowSet(model, "SourceLastResult", SourcePlayResult.Down);
        FlowSet(model, "SourceFlags", flags);
        FlowSet(model, "SourcePlayEndX", endX ?? (cause == SourcePlayEndCause.CarrierTouchdown ? (side == 0 ? 391f : -391f) : 0f));
        FlowSet(model, "SourcePlayEndZ", 0f); FlowSet(model, "SourceFumbleX", 0f);
        FlowSet(model, "SourceForwardCatcher", -1);
        FlowSet(model, "BallKind", SourceBallKind.Pass);
        FlowSet(model, "BallMode", BallMode.Held);
        FlowSet(model, "CarrierId", side * 7 + 3);
        FlowSet(model, "SourceClock", SourceClock.Create(clock, firstWakeTick: host.Game.SourceTick + 1));
        FlowSet(model, "ClockActive", true);
        FlowSet(model, "Phase", GamePhase.LiveBall);
        FlowSet(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        var scores = (int[,])FlowGet(model, "SourceQuarterScores");
        Array.Clear(scores);
        scores[quarter, 0] = home; scores[quarter, 1] = away;
        foreach (object actor in (Array)FlowGet(model, "Players"))
            FlowSet(actor, "SourceMode", SourceActorMode.Dead);
        if (clock == 0)
            Check(!FlowMethod<Func<bool>>(host.Game, "AdvanceSourcePeriod")() &&
                CabinetFlow(host).Stage == SourceCabinetFlowStage.Inactive,
                "Even at zero, a live final play must finish before the source period gate.");
        FlowMethod<Action<SourcePlayEndCause, int, FieldVector, int, SourceVector3?>>(host.Game, "EndSourcePlay")(
            cause, side * 7 + 3, new FieldVector(50, 0), -1, null);
        FlowMethod<Action>(host.Game, "Publish")();
        Check(host.Game.C2State.ResultPending && CabinetFlow(host).Stage == SourceCabinetFlowStage.Inactive,
            "A declared real C2 pending result cannot start the portable result wait.");
        host.HudProcess.Observe(host.Game);
        FlowWake(host);
        Check(!host.Game.C2State.ResultPending && CabinetFlow(host).Stage == SourceCabinetFlowStage.ResultWait &&
            CabinetFlow(host).ElapsedSourceWakes == 0 && host.HudProcess.CommittedSourceTick == host.Game.SourceTick,
            "Only the next source game_proc commit starts the result wait, with no sleep already consumed.");
    }

    private static void VerifySourceCabinetFlow(PlayerData data)
    {
        int before = assertions;
        const SourceButtons abc = SourceButtons.A | SourceButtons.B | SourceButtons.C;
        var opening = FlowHost(data);
        var configured = CabinetFlow(opening).Configuration;
        opening.Game.BindSourceCabinetFlow(configured);
        bool rejected = false;
        try { new GameSimulation().BindSourceCabinetFlow(configured); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "The portable binding cannot silently enable legacy gameplay.");
        var strict = KernelBinding.Create(data, PreviewOptions.FromUrl("?source-compatibility=strict", data.Catalog.Profile.StableId));
        rejected = false;
        try { strict.BindSourceCabinetFlow(configured); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && ((ISourceCabinetFlowState)strict.State).CabinetFlow == null &&
            strict.Compatibility == SourceGameplayCompatibility.StrictSource,
            "Strict factory defaults reject the portable scratch policy and remain unbound.");
        var unbound = new PlayerHost(data, new RecordingSink());
        Check(((ISourceCabinetFlowState)unbound.Game.State).CabinetFlow == null, "Old portable hosts remain explicitly UNBOUND.");
        for (int i = 0; i < 2200 && !opening.Game.C2State.ResultPending; i++) FlowWake(opening);
        Check(opening.Game.C2State.ResultPending && CabinetFlow(opening).Stage == SourceCabinetFlowStage.Inactive &&
            opening.HudProcess.ResultPending && opening.HudProcess.CommittedSourceTick == opening.Game.SourceTick - 1,
            "The ordinary production kickoff still earns exactly the original one-wake pending HUD hold.");
        FlowWake(opening);
        Check(!opening.Game.C2State.ResultPending && opening.HudProcess.PendingResultWakes == 1 &&
            CabinetFlow(opening).Stage == SourceCabinetFlowStage.ResultWait &&
            CabinetFlow(opening).ElapsedSourceWakes == 0, "The real kickoff commits before the wait starts.");
        rejected = false;
        try { opening.Game.BindSourceCabinetFlow(configured); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "A portable binding cannot be installed or rebound after the first source wake.");
        SourceCabinetFlowView initial = CabinetFlow(opening);
        bool ordinary = new[] { SourceResultKind.Down, SourceResultKind.Incomplete,
            SourceResultKind.FumbledOutOfBounds, SourceResultKind.NewPossession }
            .Contains(((ISourceMatchState)opening.Game.State).Match.Result.Kind);
        Check(initial.MinimumTicks == (ordinary ? 30 : 70) && initial.MaximumTicks == (ordinary ? 205 : 265),
            "The ACTUAL result_box return branch, not points or a graphic heuristic, selects its wait arguments.");
        opening.Bridge.QueueAction(InputAction.Continue);
        FlowWakes(opening, initial.MinimumTicks - 1, abc);
        Check(CabinetFlow(opening).Stage == SourceCabinetFlowStage.ResultWait &&
            CabinetFlow(opening).ConsecutiveButtonTicks.All(t => t == 0),
            "Administrative Continue and an early held chord cannot bypass the actual minimum sleep.");
        FlowWakes(opening, 7, abc);
        SourceCabinetFlowView seven = CabinetFlow(opening);
        Check(seven.ConsecutiveButtonTicks.Where((_, i) => (configured.ParticipatingStationMask & 1 << i) != 0).All(t => t == 7),
            "The all-buttons helper needs eight consecutive samples; any-button/edge logic is not interchangeable.");
        FlowWake(opening, SourceButtons.A | SourceButtons.B);
        Check(CabinetFlow(opening).ConsecutiveButtonTicks.All(t => t == 0) &&
            seven.ConsecutiveButtonTicks.Any(t => t == 7), "Releasing one button resets the counter without mutating retained snapshots.");
        FlowWakes(opening, 3, abc);
        var paused = CabinetFlow(opening);
        var rng = (SourceInjectedRandom)opening.Game.SourceRandomService;
        int draws = rng.Trace.Count;
        FlowProcesses(opening).SuspendMultiple(0, 0x8000);
        FlowWakes(opening, 5, abc);
        Check(CabinetFlow(opening).ElapsedSourceWakes == paused.ElapsedSourceWakes &&
            CabinetFlow(opening).ConsecutiveButtonTicks.SequenceEqual(paused.ConsecutiveButtonTicks) && rng.Trace.Count == draws,
            "Suspended source processes cannot poll, count held buttons, or consume RNG.");
        FlowProcesses(opening).ResumeAll();
        int samples = 0;
        opening.Bridge.SetActive(false);
        opening.Bridge.AdvanceSource(2, () => { samples++; throw new Exception("paused input consumed"); });
        opening.Bridge.SetActive(true);
        opening.Bridge.AdvanceSource(FixedStepBridge.StepSeconds, () => { samples++; throw new Exception("resumed stale input consumed"); });
        opening.Bridge.AdvanceSource(0, () => { samples++; throw new Exception("redraw input consumed"); });
        Check(samples == 0 && CabinetFlow(opening).ElapsedSourceWakes == paused.ElapsedSourceWakes && rng.Trace.Count == draws,
            "Paused, discarded-resume and redraw frames consume neither a wait sample nor RNG.");
        while (opening.Game.State.Tick % 20 != 0) FlowHostStep(opening);
        long sourceTick = opening.Game.SourceTick;
        var skipped = CabinetFlow(opening);
        draws = rng.Trace.Count;
        FlowHostStep(opening, abc);
        Check(opening.Game.SourceTick == sourceTick && CabinetFlow(opening).ElapsedSourceWakes == skipped.ElapsedSourceWakes &&
            rng.Trace.Count == draws, "The real 60/57 host skip cannot age the cabinet continuation.");
        FlowWakes(opening, 8, abc);
        Check(opening.Game.State.Phase != GamePhase.DeadBall && CabinetFlow(opening).Stage == SourceCabinetFlowStage.Inactive,
            "Raw held cabinet buttons must actually continue the production kickoff without Enter.");

        for (int i = 0; i < 2200 && (unbound.Game.State.Phase != GamePhase.DeadBall || unbound.Game.C2State.ResultPending); i++)
            FlowWake(unbound);
        FlowWakes(unbound, 270, abc);
        Check(unbound.Game.State.Phase == GamePhase.DeadBall && ((ISourceCabinetFlowState)unbound.Game.State).CabinetFlow == null,
            "Old unbound portable gameplay retains its administrative DeadBall behavior.");
        FlowWake(unbound, command: new SourceCommand(SourceAction.Continue));
        Check(unbound.Game.State.Phase != GamePhase.DeadBall, "Unbound explicit Continue remains supported.");

        var timeout = FlowHost(data);
        FlowEnd(timeout, 0, 0, 0, side: 1, down: 4);
        var committed = ((ISourceMatchState)timeout.Game.State).Match;
        Check(committed.Result.TurnoverOnDowns && committed.Possession.Offense == TeamId.Home &&
            CabinetFlow(timeout).MinimumTicks == 30, "The real fourth-down resolver must feed the ordinary result wait.");
        FlowWakes(timeout, 204);
        Check(CabinetFlow(timeout).Stage == SourceCabinetFlowStage.ResultWait, "An ordinary result must remain through wake204.");
        FlowWake(timeout);
        Check(CabinetFlow(timeout).Stage == SourceCabinetFlowStage.Inactive &&
            ((ISourceMatchState)timeout.Game.State).Match.Possession.Captain == committed.Possession.Captain &&
            ((ISourceMatchState)timeout.Game.State).Match.PlayEndStats.HomeSacks == committed.PlayEndStats.HomeSacks &&
            ((ISourceMatchState)timeout.Game.State).Match.PlayEndStats.HomeSackYards == committed.PlayEndStats.HomeSackYards &&
            committed.Result.TurnoverOnDowns, "The exact maximum auto-continues without duplicate captain/stat/result commits.");

        var scoreTimeout = FlowHost(data);
        FlowEnd(scoreTimeout, 0, 0, 0, side: 1, initialSide: 0,
            cause: SourcePlayEndCause.CarrierDownInOwnEndZone, flags: SourceGameFlags.RecoveredInEndZone, endX: 391f);
        var scored = ((ISourceMatchState)scoreTimeout.Game.State).Match;
        Check(scored.Result.Kind == SourceResultKind.Touchback && CabinetFlow(scoreTimeout).MinimumTicks == 70,
            "The original TOUCHBACK result_box return is TRUE despite its non-scoring box graphic.");
        FlowWakes(scoreTimeout, 264);
        Check(CabinetFlow(scoreTimeout).Stage == SourceCabinetFlowStage.ResultWait &&
            scoreTimeout.Game.State.HomeScore == 0, "A committed TRUE result-box branch must remain through wake264.");
        FlowWake(scoreTimeout);
        Check(CabinetFlow(scoreTimeout).Stage == SourceCabinetFlowStage.Inactive &&
            ((ISourceMatchState)scoreTimeout.Game.State).Match.TouchdownStats.HomeTouchdowns == 0 &&
            scoreTimeout.Game.State.HomeScore == 0, "The score-plate maximum265 cannot invent points or touchdown statistics.");

        var unnecessaryPat = FlowHost(data);
        FlowEnd(unnecessaryPat, 3, 2, 7, clock: 0, cause: SourcePlayEndCause.CarrierTouchdown);
        FlowTo(unnecessaryPat, SourceCabinetFlowStage.EndOfQuarter, abc);
        Check(((ISourceMatchState)unnecessaryPat.Game.State).Match.PatStatus == SourcePatStatus.SkipUnnecessary &&
            unnecessaryPat.Game.State.HomeScore == 8 && unnecessaryPat.Game.State.Down == -1 &&
            ((ISourceMatchState)unnecessaryPat.Game.State).Match.PlayKind == SourcePlayKind.Normal,
            "The original unnecessary final PAT branch skips human selection automatically, without inventing a free point.");
        FlowTo(unnecessaryPat, SourceCabinetFlowStage.FinalStats, abc);
        Check(CabinetFlow(unnecessaryPat).FinalHomeScore == 8 && CabinetFlow(unnecessaryPat).FinalAwayScore == 7,
            "The skipped PAT reaches the real final observation with its unmodified scores.");

        var half = FlowHost(data, fire: data.Fire != null);
        FlowEnd(half, 1, 7, 3, clock: 0);
        SourceCaptainSlot halfCaptain = ((ISourceMatchState)half.Game.State).Match.Possession.Captain;
        FlowTo(half, SourceCabinetFlowStage.EndOfQuarter, abc);
        SourceProcessView child = ((ISourcePossessionState)half.Game.State).Processes.Single(p => p.Function == "show_end_quarter_proc");
        Check(child.BirthTick == half.Game.SourceTick && child.LastWakeTick == -1 && child.SleepRemaining == 1,
            "EOQ is a real newly scheduled child, not an inline fabricated screen transition.");
        FlowTo(half, SourceCabinetFlowStage.HalftimeStats, abc);
        Check(((ISourcePossessionState)half.Game.State).Processes.Where(p => p.Serial != 1 && (p.Id & 0x8000) == 0).All(p => p.Suspended),
            "eoq_handling suspends the actual destructible source processes while stats run.");
        FlowWakes(half, 56, abc);
        Check(CabinetFlow(half).Stage == SourceCabinetFlowStage.HalftimeStats,
            "The actual any-button stats helper still has its one-second minimum.");
        FlowWake(half, abc);
        Check(CabinetFlow(half).Stage == SourceCabinetFlowStage.ContinuationOffer, "Halftime statistics lead to the free-play cabinet offer.");
        FlowTo(half, SourceCabinetFlowStage.Inactive, abc);
        SourceMatchView resumed = ((ISourceMatchState)half.Game.State).Match;
        Check(half.Game.State.Quarter == 3 && resumed.ReceivingTeam == data.Configuration.Match.FirstPossession &&
            resumed.Possession.Captain != halfCaptain &&
            resumed.Period.Status == SourcePeriodStatus.HalftimeKickoff &&
            ((ISourceGameState)half.Game.State).PackedGameTime == SourceClock.QuarterTime,
            "Accepted free play reuses the actual halftime receiver and packed-clock restart.");

        var denied = FlowHost(data, SourceCabinetContinuationPolicy.RequireConfirmation);
        FlowEnd(denied, 0, 7, 3, clock: 0);
        FlowTo(denied, SourceCabinetFlowStage.ContinuationOffer, abc);
        var coachingDraws = ((SourceInjectedRandom)denied.Game.SourceRandomService).Trace.Where(
            d => d.Site.SourceIdentity.StartsWith("select.c:coaching_tip_scrn:", StringComparison.Ordinal)).ToArray();
        Check(coachingDraws.Length == 2 && CabinetFlow(denied).CoachingBackgroundOrdinal.HasValue &&
            CabinetFlow(denied).CoachingTipOrdinal.HasValue,
            "The original coaching helper's background then hint draws execute once on the source game process, not per redraw.");
        FlowTo(denied, SourceCabinetFlowStage.FinalStats, abc);
        Check(denied.Game.State.Phase == GamePhase.GameOver &&
            ((ISourceMatchState)denied.Game.State).Match.Period.Status == SourcePeriodStatus.CabinetContinuationDeclined,
            "A held-through chord cannot confirm an explicitly confirmation-required free-play offer.");
        FlowWakes(denied, 57 + 57 * 22 - 1);
        Check(CabinetFlow(denied).Stage == SourceCabinetFlowStage.FinalStats,
            "The original stats helper's polling budget is additional to its minimum, not a total timeout.");
        FlowWake(denied);
        Check(CabinetFlow(denied).Stage == SourceCabinetFlowStage.FinalConfirmation,
            "The original final stats timeout reaches an explicit final-confirmation boundary.");
        FlowWake(denied, command: new SourceCommand(SourceAction.Continue));
        Check(CabinetFlow(denied).NewMatchRequested && denied.Game.State.Phase == GamePhase.GameOver,
            "Terminal source Continue now exposes a request while preserving the final football observation.");
        denied.Bridge.Restart();
        FlowEnd(denied, 0, 7, 3, clock: 0);
        FlowTo(denied, SourceCabinetFlowStage.ContinuationOffer, abc);
        FlowWake(denied);
        FlowWake(denied, SourceButtons.B);
        FlowTo(denied, SourceCabinetFlowStage.Inactive, SourceButtons.B);
        Check(denied.Game.State.Quarter == 2 && denied.Game.State.Phase != GamePhase.GameOver,
            "A release followed by a fresh configured button really accepts a confirmation-required offer.");

        var overtime = FlowHost(data);
        var ends = new List<SourceGameEvent>();
        overtime.Bridge.SourceEventRaised += e => { if (e.Type == SourceEventType.MatchEnded) ends.Add(e); };
        int OtDraws() => ((SourceInjectedRandom)overtime.Game.SourceRandomService).Trace.Count(
            d => d.Site.SourceIdentity == "SourceMatchFlow.OvertimeKickoff");
        FlowEnd(overtime, 3, 0, 7, clock: 0, cause: SourcePlayEndCause.CarrierTouchdown);
        Check(CabinetFlow(overtime).MinimumTicks == 70 && CabinetFlow(overtime).MaximumTicks == 265,
            "Touchdown score plates retain the actual70/265 all-buttons wait.");
        FlowWakes(overtime, 76, abc);
        Check(CabinetFlow(overtime).Stage == SourceCabinetFlowStage.ResultWait &&
            CabinetFlow(overtime).ConsecutiveButtonTicks.Any(t => t == 7),
            "The scoring plate cannot acknowledge before seven held samples following its minimum70.");
        FlowWake(overtime, abc);
        Check(((ISourceMatchState)overtime.Game.State).Match.Phase == SourceMatchPhase.PatSelection && ends.Count == 0 &&
            ((ISourceMatchState)overtime.Game.State).Match.PatStatus == SourcePatStatus.AwaitHumanSelection &&
            ((ISourceMatchState)overtime.Game.State).Match.TouchdownStats.HomeTouchdowns == 1 &&
            overtime.Game.State.HomeScore == 6 && overtime.Game.State.Quarter == 4,
            "A necessary final PAT must run before any EOQ or terminal transition, without duplicate touchdown credit.");
        FlowWake(overtime, command: new SourceCommand(SourceAction.SelectPat, patChoice: SourcePatOption.FreeKick));
        Check(overtime.Game.State.HomeScore == 7 && overtime.Game.State.AwayScore == 7 &&
            CabinetFlow(overtime).Stage == SourceCabinetFlowStage.EndOfQuarter && ends.Count == 0 && OtDraws() == 0,
            "The actual PAT producer ties the score exactly once before the overtime gate.");
        FlowTo(overtime, SourceCabinetFlowStage.ContinuationOffer, abc);
        FlowWakes(overtime, 59, abc);
        Check(CabinetFlow(overtime).ContinuationSeconds == 9 && OtDraws() == 0,
            "Original OT timedown protects the initial9 for60 ticks; an early chord cannot draw the kickoff.");
        FlowTo(overtime, SourceCabinetFlowStage.Inactive, abc);
        Check(overtime.Game.State.Quarter == 5 && OtDraws() == 1 && ends.Count == 0,
            "Only accepted OT continuation consumes the single original first-kickoff draw.");
        TeamId firstOtKicker = ((ISourceMatchState)overtime.Game.State).Match.KickingTeam!.Value;
        FlowEnd(overtime, 4, 14, 7);
        FlowTo(overtime, SourceCabinetFlowStage.Inactive, abc);
        Check(overtime.Game.State.Phase != GamePhase.GameOver && overtime.Game.State.Quarter == 5 && ends.Count == 0,
            "Scoring or leading during live OT is not sudden death.");
        for (int q = 4; q <= 5; q++)
        {
            FlowEnd(overtime, q, 7, 7, clock: 0);
            FlowTo(overtime, SourceCabinetFlowStage.Inactive, abc);
            Check(overtime.Game.State.Quarter == q + 2 && OtDraws() == 1 && ends.Count == 0 &&
                ((ISourceMatchState)overtime.Game.State).Match.KickingTeam ==
                    (q == 4 ? (TeamId)(1 - (int)firstOtKicker) : firstOtKicker),
                "Later tied OT periods alternate the existing kickoff choice without a replacement RNG draw.");
        }
        FlowEnd(overtime, 6, 5, 7, clock: 0, kind: SourcePlayType.ExtraPoint, cause: SourcePlayEndCause.CarrierTouchdown);
        Check(overtime.Game.State.HomeScore == 7 && overtime.Game.State.AwayScore == 7 &&
            ((ISourceMatchState)overtime.Game.State).Match.Result.Kind == SourceResultKind.Conversion,
            "A final live two-point conversion must commit through C2 before the tied-third-OT terminal gate.");
        FlowTo(overtime, SourceCabinetFlowStage.FinalStats, abc);
        var final = CabinetFlow(overtime);
        Check(ends.Count == 1 && final.FinalMatch != null && final.FinalMatch.Phase == SourceMatchPhase.MatchEnded &&
            final.FinalMatch.Period.QuarterIndex == 6 && final.FinalHomeScore == 7 && final.FinalAwayScore == 7,
            "Tied third OT ends once and retains the actual immutable final match and scores.");
        FlowWakes(overtime, 57, abc);
        Check(CabinetFlow(overtime).Stage == SourceCabinetFlowStage.FinalConfirmation, "Final team stats use the original minimum wait.");
        FlowWakes(overtime, 3, abc);
        Check(!CabinetFlow(overtime).NewMatchRequested && CabinetFlow(overtime).WaitingForButtonRelease,
            "The button that dismissed final stats cannot also request another match.");
        FlowWake(overtime);
        FlowWake(overtime, SourceButtons.B);
        Check(CabinetFlow(overtime).NewMatchRequested && ReferenceEquals(final.FinalMatch, CabinetFlow(overtime).FinalMatch),
            "A fresh raw confirmation exposes a new-match REQUEST, never an implicit kernel restart.");
        var finalRng = (SourceInjectedRandom)overtime.Game.SourceRandomService;
        draws = finalRng.Trace.Count;
        FlowWake(overtime, command: new SourceCommand(SourceAction.Continue));
        FlowWakes(overtime, 20, abc);
        Check(ends.Count == 1 && finalRng.Trace.Count == draws && overtime.Game.State.HomeScore == 7 &&
            ReferenceEquals(final.FinalMatch, CabinetFlow(overtime).FinalMatch) &&
            ((ISourceMatchState)overtime.Game.State).Match.Possession.Captain == final.FinalMatch!.Possession.Captain &&
            ((ISourceMatchState)overtime.Game.State).Match.TouchdownStats.HomeTouchdowns == final.FinalMatch.TouchdownStats.HomeTouchdowns &&
            final.Stage == SourceCabinetFlowStage.FinalStats,
            "Idle and repeated terminal Continue cannot replay MatchEnded, statistics, captain changes or RNG.");
        overtime.Bridge.Restart();
        Check(CabinetFlow(overtime).Stage == SourceCabinetFlowStage.Inactive && CabinetFlow(overtime).FinalMatch == null &&
            overtime.Game.State.HomeScore == 0 && overtime.Game.State.AwayScore == 0 && final.FinalMatch!.QuarterScores.Count == 7,
            "Only explicit bridge Restart clears match-only flow, preserving previously retained final snapshots.");
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", sourceGameflowChecks = assertions - before,
            contract = configured.Contract, finalConfirmations = 1, matchEnded = ends.Count,
            boundary = "Actual source/FixedStepBridge/C2/HUD opening kickoff and declared period/PAT/OT fixtures; portable zeroed wait scratch, not native stack, whole-match or presentation qualification." }));
    }

    private static void VerifyRandomSelection(PlayerData data)
    {
        var sink = new RecordingSink();
        var host = new PlayerHost(data, sink);
        var selections = new List<SourceGameEvent>();
        host.Bridge.SourceEventRaised += value => { if (value.Type == SourceEventType.PlaySelected) selections.Add(value); };
        var axes = new double[8];
        var buttons = new int[4];
        int station = data.Configuration.Match.Stations[0].StationId;
        void Step()
        {
            host.Capture(axes, buttons);
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
        }
        for (int i = 0; i < 10000 && (host.Game.State.Phase != GamePhase.DeadBall || host.Game.C2State.ResultPending); i++) Step();
        Check(host.Game.State.Phase == GamePhase.DeadBall, "Actual PlayerHost public kickoff must reach its real result.");
        host.Continue(station);
        for (int i = 0; i < 10 && host.Game.State.Phase == GamePhase.DeadBall; i++) Step();
        Check(host.Game.State.Phase == GamePhase.PlaySelect, "Actual PlayerHost Continue must enter its real cabinet menu.");
        var match = ((ISourceMatchState)host.Game.State).Match;
        TeamId side = data.Configuration.Match.Stations[0].Team;
        station = SourceCabinetStations.Captain(match, side);
        long entry = host.Game.SourceTick;
        void To(long target, double x = 0, int held = 0)
        {
            axes[station * 2] = x; buttons[station] = held;
            while (host.Game.SourceTick < target) Step();
        }
        To(entry + 21); To(entry + 22, -1); To(entry + 23); To(entry + 24, -1, 4);
        To(entry + 69);
        SourceGameEvent choice = selections.Single(e => e.Team == side);
        Check(choice.SourceTick == entry + 69 && choice.Selection.IsSpecified &&
            choice.Selection.PageIndex >= 0 && choice.Selection.SlotIndex >= 0,
            "Actual PlayerHost gesture must commit the real source page/slot/play on its fifteenth paced iteration.");
        var random = (Blitz.Gameplay.SourceInjectedRandom)host.Game.SourceRandomService;
        Check(random.Trace.Count(d => d.Site.SourceIdentity.StartsWith("playsel.c:play_select_cntrl:", StringComparison.Ordinal)) == 30,
            "Actual PlayerHost gesture must use precisely thirty shared Core draws, never render/input RNG.");
        JsonElement menu = sink.State.GetProperty("menu").GetProperty(side == TeamId.Home ? "home" : "away");
        Check(menu.GetProperty("selected").GetBoolean() && menu.GetProperty("page").GetInt32() == choice.Selection.PageIndex &&
            menu.GetProperty("cursor").GetInt32() == choice.Selection.SlotIndex,
            "Actual PlayerHost/HUD selection reflection retains the chosen source page and slot.");
        JsonElement[] sounds = sink.State.GetProperty("events").EnumerateArray().Where(e =>
            e.TryGetProperty("kind", out var kind) && kind.GetString() == "cabinet-random-selection" &&
            e.GetProperty("playlist").ValueKind == JsonValueKind.Number).ToArray();
        Check(sounds.Length > 0 && sounds.All(e => e.GetProperty("bank").GetString() == "cursors" &&
            e.GetProperty("volume").GetInt32() == 235 && e.GetProperty("pan").GetInt32() == 127 &&
            !e.GetProperty("pcmOrDspPlayback").GetBoolean()) &&
            sounds.Any(e => e.GetProperty("stage").GetString() == "Selected"),
            "Actual PlayerHost forwards random menu source sound operands and final selection, without DSP claims.");
        int before = random.Trace.Count;
        for (int i = 0; i < 10; i++) host.Advance(0, 1024, 768, 1024, 768, 1);
        Check(random.Trace.Count == before && host.Game.SourceTick == entry + 69,
            "Actual PlayerHost redraws do not admit or replay random menu draws.");
        host.Restart();
        Check(host.Game.SourceTick == 0 && !((ISourceMatchState)host.Game.State).Match.HomeRandomSelection.IsSpecified,
            "Actual PlayerHost restart clears the real random menu epoch.");
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, randomDraws = 30,
            sourceTick = choice.SourceTick, page = choice.Selection.PageIndex, slot = choice.Selection.SlotIndex,
            boundary = "Real prepared PlayerHost/Core/cabinet/bridge/HUD, managed recording sink; not browser/Unity/DSP/native proof." }));
    }

    private static void VerifyLoaderContract(byte[] manifest, Func<string, byte[]> read, string? output)
    {
        (DataCatalog Catalog, PlayerData Data, PlayerHost Host, RecordingSink Sink, List<string> Reads)
            Load(byte[] raw, Func<string, byte[]> source)
        {
            var unread = JsonNode.Parse(raw)!["files"]!.AsArray()
                .Select(pin => pin!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var reads = new List<string>();
            byte[] ReadOnce(string path)
            {
                DataCatalog.Require(unread.Remove(path), "Missing/repeated actual JS readFile input: " + path);
                reads.Add(path);
                return source(path);
            }
            var catalog = new DataCatalog(raw, ReadOnce);
            var data = new PlayerData(catalog);
            var sink = new RecordingSink();
            var host = new PlayerHost(data, sink);
            catalog.RequireAllInputsConsumed();
            Check(unread.Count == 0, "Both the actual managed loader and JS-style one-shot input map must be empty.");
            return (catalog, data, host, sink, reads);
        }

        var loaded = Load(manifest, read);
        bool successor = loaded.Catalog.Profile.IsContact282C2M1PreSnapSwitch;
        string[] retainedPaths = successor ? new[] { "animation/export-inventory.json",
            "kernel/c2-callback-data.json", "kernel/impact-presentation.json", "settings.json" } : Array.Empty<string>();
        Check(loaded.Catalog.RetainedInputs.Select(pin => pin.Path).SequenceEqual(retainedPaths),
            "Only the four exact historical558 entries have sealed retained roles in the explicit successor.");
        Check(loaded.Reads.Distinct(StringComparer.Ordinal).Count() == loaded.Catalog.FileCount &&
            loaded.Catalog.ConsumedInputCount == loaded.Catalog.FileCount &&
            loaded.Catalog.VerifiedRetainedInputCount == retainedPaths.Length &&
            loaded.Reads.Count(path => !retainedPaths.Contains(path)) == loaded.Catalog.ActiveInputCount,
            "Every actual input is authenticated exactly once, with active and sealed retained accounting separated.");
        Check(loaded.Catalog.FileCount == (successor ? 563 : 558) &&
            loaded.Catalog.ActiveInputCount == (successor ? 559 : 558),
            "The real563 successor is559 active plus4 retained; the historical558 reader has no retained exemption.");
        var configured = loaded.Data.Configuration.Match;
        var declared = SourceInterfacePackagePlan.ReadConfiguration(read(loaded.Catalog.Entry("settings")),
            loaded.Catalog.EntryHash("settings")).Match;
        Check(configured.StationLayout == declared.StationLayout &&
            configured.Stations.SequenceEqual(declared.Stations) &&
            (!successor || configured.StationLayout == SourceStationLayout.FourStation &&
                configured.Stations.Count == 1 && configured.Stations[0].StationId == 0 &&
                configured.Stations[0].Team == TeamId.Home),
            "Only active declared settings apply: single-human station0/Home for new data; historical configuration stays intact.");
        loaded.Host.Advance(0, 512, 384, 512, 384, 1);
        for (int i = 0; i < 3; i++) loaded.Host.Advance(FixedStepBridge.StepSeconds, 512, 384, 512, 384, 1);
        Check(loaded.Host.Game.SourceDefensivePreSnapSwitchEnabled == successor &&
            loaded.Sink.WorldDraws > 0 && loaded.Sink.Models.Count == 32 &&
            loaded.Sink.State.GetProperty("actorCount").GetInt32() == 14 &&
            loaded.Host.Game.SourceTick >= 2,
            "The real loader feeds the actual profile factory/flag, original world and production actors.");

        JsonObject Document() => JsonNode.Parse(manifest)!.AsObject();
        JsonObject Pin(JsonObject document, string path) => document["files"]!.AsArray()
            .Select(node => node!.AsObject()).Single(pin => pin["path"]!.GetValue<string>() == path);
        var controls = new List<object>();
        void Reject(string name, JsonObject document, Func<string, byte[]> source, string expected)
        {
            Exception? failure = null;
            try { Load(JsonSerializer.SerializeToUtf8Bytes(document), source); }
            catch (Exception error) { failure = error; }
            Check(failure != null && failure.Message.Contains(expected, StringComparison.Ordinal),
                "Actual managed loader control must fail by exact identity: " + name + " / " + expected);
            controls.Add(new { name, expected, error = failure!.Message });
        }

        var wrongProfile = Document();
        wrongProfile["profile"] = "original-teams-future-not-authorized";
        Reject("unknown-profile", wrongProfile, read, "Only the explicitly identified");
        var extra = Document();
        byte[] unexpected = System.Text.Encoding.UTF8.GetBytes("Authored unexpected input, not original source data.");
        const string unexpectedPath = "private/unexpected-loader-input.bytes";
        extra["files"]!.AsArray().Add(new JsonObject {
            ["path"] = unexpectedPath, ["bytes"] = unexpected.Length, ["sha256"] = DataCatalog.Hash(unexpected)
        });
        Reject("unexpected-declared-input", extra, path => path == unexpectedPath ? unexpected : read(path),
            successor ? "exact563-file successor" : "unconsumed private input files: " + unexpectedPath);

        if (successor)
        {
            var historical = Document();
            historical["profile"] = SourceTeamResourceProfile.C2M1StableId;
            Reject("new-bundle-relabeled-historical", historical, read, "requires its exact 281-clip");
            foreach (string path in retainedPaths)
            {
                var renamed = Document();
                Pin(renamed, path)["path"] = "retained-substitute/" + path;
                Reject("retained-substituted-name:" + path, renamed, read, "sealed retained historical input: " + path);
                var changedPin = Document();
                Pin(changedPin, path)["sha256"] = new string('0', 64);
                Reject("retained-changed-manifest-hash:" + path, changedPin, read, "sealed retained historical input: " + path);
                var changedCount = Document();
                Pin(changedCount, path)["bytes"] = Pin(changedCount, path)["bytes"]!.GetValue<int>() + 1;
                Reject("retained-changed-byte-count:" + path, changedCount, read, "sealed retained historical input: " + path);
                var missing = Document();
                missing["files"]!.AsArray().Remove(Pin(missing, path));
                Reject("retained-missing-manifest-entry:" + path, missing, read, "exact563-file successor");
                Reject("retained-missing-bytes:" + path, Document(),
                    name => name == path ? Array.Empty<byte>() : read(name), "actual source asset: " + path);
                byte[] corrupt = read(path);
                corrupt[corrupt.Length / 2] ^= 1;
                Reject("retained-corrupt-bytes:" + path, Document(),
                    name => name == path ? corrupt : read(name), "actual source asset: " + path);
                var selfConsistent = Document();
                Pin(selfConsistent, path)["sha256"] = DataCatalog.Hash(corrupt);
                Reject("retained-self-consistent-substitution:" + path, selfConsistent,
                    name => name == path ? corrupt : read(name), "sealed retained historical input: " + path);
            }
            var oldSettings = Document();
            oldSettings["entrypoints"]!["settings"] = "settings.json";
            Reject("retained-settings-cannot-be-active", oldSettings, read, "explicit new configuration");
        }
        else
        {
            var relabeled = Document();
            relabeled["profile"] = SourceTeamResourceProfile.PreSnapSwitchStableId;
            Reject("historical-bundle-relabeled-successor", relabeled, read, "exact563-file successor");
        }
        RejectMissingAndChanged(manifest, read, loaded.Catalog.Entry("model"));
        var proof = new {
            schema = "source-player-private-input-accounting/v1", status = "passed", assertions,
            profile = loaded.Catalog.Profile.StableId, factory = KernelBinding.FactoryFor(loaded.Catalog.Profile),
            enabled = loaded.Host.Game.SourceDefensivePreSnapSwitchEnabled, loaded.Catalog.ManifestSha256,
            files = loaded.Catalog.FileCount, active = loaded.Catalog.ActiveInputCount,
            retained = loaded.Catalog.VerifiedRetainedInputCount, consumedPaths = loaded.Reads,
            retainedInputs = loaded.Catalog.RetainedInputs.Select(pin => new { path = pin.Path, bytes = pin.Bytes, sha256 = pin.Sha256 }),
            controls, worldDraws = loaded.Sink.WorldDraws, sourceTick = loaded.Host.Game.SourceTick,
            stationLayout = (int)loaded.Data.Configuration.Match.StationLayout,
            stations = loaded.Data.Configuration.Match.Stations.Count,
            boundary = "Actual managed PlayerData/PlayerHost loaders and one-shot private-input accounting; not a real browser/Unity/native/DSP qualification."
        };
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "loader-contract.json"), JsonSerializer.Serialize(proof));
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            proof.schema, proof.status, proof.assertions, proof.profile, proof.factory, proof.enabled,
            proof.files, proof.active, proof.retained, controls = controls.Count, proof.worldDraws, proof.sourceTick
        }));
    }

    private static void RejectMissingAndChanged(byte[] manifest, Func<string, byte[]> read, string modelPath)
    {
        foreach (bool missing in new[] { true, false })
        {
            bool rejected = false;
            try
            {
                var catalog = new DataCatalog(manifest, path => {
                    byte[] bytes = read(path);
                    if (path != modelPath) return bytes;
                    if (missing) return Array.Empty<byte>();
                    bytes[bytes.Length / 2] ^= 1;
                    return bytes;
                });
                catalog.Read(modelPath);
            }
            catch (InvalidDataException error)
            { rejected = error.Message.Contains(modelPath, StringComparison.Ordinal); }
            Check(rejected, "Mutation of the ACTUAL prepared original model must fail by named asset, not produce ready placeholders.");
        }
    }

    private class RecordingSink : IRenderSink, ISourceWorldRenderSink
    {
        internal readonly Dictionary<string, string> Textures = new();
        internal readonly HashSet<string> Meshes = new();
        internal readonly HashSet<int> Models = new();
        internal int WorldDraws;
        internal bool HasOriginalBall;
        internal double[] Joints = Array.Empty<double>();
        internal string[] HudTextures = Array.Empty<string>();
        internal double[] HudVertices = Array.Empty<double>(), FlashAlpha = Array.Empty<double>();
        internal int[] HudCounts = Array.Empty<int>(), HudColors = Array.Empty<int>(), EffectFlags = Array.Empty<int>();
        internal JsonElement State;
        private SourceWorldRenderPacket? pendingWorld;
        internal string WorldEffectsMetadata = "";
        public void WorldPresentation(SourceWorldRenderPacket packet)
        {
            Check(pendingWorld == null, "Each source world packet must reach Effects exactly once before the next packet.");
            pendingWorld = packet;
        }
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin)
        {
            Check(rgba.Length >= width * height * 4, "Original texture dimensions must match its real pixel bytes.");
            Textures.Add(id, sha256);
        }
        public void Mesh(string id, double[] vertices, int[] indices)
        {
            Check(vertices.Length % 5 == 0 && indices.Length % 3 == 0 && indices.All(i => i >= 0 && i < vertices.Length / 5),
                "Actual uploaded original mesh indices/UV vertex inventory must be valid: " + id);
            Check(Meshes.Add(id), "Static original geometry may only be registered once: " + id);
        }
        public void WorldDraw(string mesh, string texture, string state)
        {
            Check(Meshes.Contains(mesh) && Textures.ContainsKey(texture), "Actual world geometry/texture must be bound.");
            WorldDraws++;
        }
        public void Model(int id, string joints) { Check(Models.Add(id), "Duplicate original model selector."); }
        public void Appearance(int id, string metadata)
        {
            using var json = JsonDocument.Parse(metadata);
            Check(json.RootElement.GetProperty("tint").GetString() == "none", "Original uniforms cannot be replaced by tint.");
        }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) =>
            HasOriginalBall = Meshes.Contains(mesh) && Textures.ContainsKey(texture);
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags,
            double[] flashAlpha, string metadata)
        {
            EffectFlags = rawFlags; FlashAlpha = flashAlpha;
            if (pendingWorld != null) WorldEffectsMetadata = pendingWorld.AttachEffects(metadata);
            pendingWorld = null;
        }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            Joints = joints;
            HudTextures = hudTextures;
            HudVertices = hudVertices; HudCounts = hudCounts; HudColors = hudColors;
            State = JsonDocument.Parse(status).RootElement.Clone();
        }
    }
}
