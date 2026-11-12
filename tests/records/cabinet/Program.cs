using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;
using Blitz.Storage;

internal static class Program
{
    private static int assertions;
    private static bool terminalOnly;
    private static bool naturalFullMatch;
    private static bool observedResumeOnly;
    private static string? accountingCase;
    private const string Name = "QAZUI ", Pin = "8642";
    private static readonly SourceRecordIdentity Identity = new(Name, Pin);

    private sealed class OwnedStorage : ISourcePlayerRecordStorage
    {
        internal SourceRecordStorageImage Image = new(null);
        internal int Writes;
        internal bool FailNext;
        internal TaskCompletionSource? Hold;
        public Task<SourceRecordStorageImage> ReadAsync() => Task.FromResult(Image);
        public async Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement)
        {
            Writes++;
            if (Hold != null) await Hold.Task;
            if (FailNext) { FailNext = false; throw new SourceRecordStorageException(); }
            if (Image.Primary != expectedPrimary) return false;
            Image = replacement;
            return true;
        }
    }

    private sealed class Sink : IRenderSink, ISourceWorldRenderSink, ISourceAudioSink, ISourceAudioClockWitness
    {
        internal readonly Dictionary<string, string> Textures = new();
        internal string[] HudTextures = Array.Empty<string>();
        internal int[] HudCounts = Array.Empty<int>();
        internal JsonElement State;
        internal int AudioResets, AudioClockStarts, Player13Plays;
        internal readonly List<int> PlayerProgrammes = new();
        internal long AudioClockEpoch, AudioClockSourceTick;
        internal double DiagnosticRenderElapsed;
        public double ReadSourceElapsed(long epoch) => AudioClockStarts > 0 && epoch == AudioClockEpoch ? DiagnosticRenderElapsed : -1;
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin)
            => Textures.Add(id, sha256);
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void WorldPresentation(SourceWorldRenderPacket packet) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags, double[] alpha, string metadata) { }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata) { }
        public void AudioCommands(string text)
        {
            using var batch = JsonDocument.Parse(text);
            foreach (JsonElement command in batch.RootElement.GetProperty("commands").EnumerateArray())
            {
                string kind = command.GetProperty("kind").GetString() ?? throw new InvalidDataException("A real audio command needs its kind.");
                if (kind == "reset") AudioResets++;
                if (kind == "clock-start")
                {
                    AudioClockStarts++;
                    AudioClockEpoch = command.GetProperty("epoch").GetInt64();
                    AudioClockSourceTick = command.GetProperty("sourceTick").GetInt64();
                }
                if (kind == "play" && command.GetProperty("bank").GetString() == "players" &&
                    command.GetProperty("playlistId").GetInt32() == 13) Player13Plays++;
                if (kind == "play" && command.GetProperty("bank").GetString() == "players")
                    PlayerProgrammes.Add(command.GetProperty("playlistId").GetInt32());
            }
        }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            HudTextures = hudTextures; HudCounts = hudCounts;
            State = JsonDocument.Parse(status).RootElement.Clone();
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); assertions++; }
    private static SourceCabinetRecordsRuntime Records(PlayerHost host) => host.Session.Records ??
        throw new InvalidOperationException("The actual opted Host did not own its real record runtime.");
    private static SourceMatchView Match(PlayerHost host) => ((ISourceMatchState)host.Game.State).Match;
    private static SourceCabinetFlowView Flow(PlayerHost host) => ((ISourceCabinetFlowState)host.Game.State).CabinetFlow;
    private static void Raw(PlayerHost host, int button = 0, int station = 0, int x = 0, int y = 0)
    {
        var axes = new double[8]; var buttons = new int[4];
        axes[station * 2] = x; axes[station * 2 + 1] = y; buttons[station] = button;
        host.Capture(axes, buttons);
    }
    private static void Render(PlayerHost host, double elapsed = 0) => host.Advance(elapsed, 512, 384, 512, 384, 1);
    private static void PumpSourceMenu(PlayerHost host, int count = 70)
    {
        for (int i = 0; i < count && host.Session.UsingSourcePreGameInput; i++)
        { Raw(host); Render(host, 1.0 / 57); Thread.Yield(); }
    }
    private static void Tap(PlayerHost host, int button, int station = 0)
    { Raw(host); Raw(host, button, station); Raw(host); Render(host); PumpSourceMenu(host); }
    private static void Move(PlayerHost host, int x, int y, int station = 0)
    { Raw(host); Raw(host, station: station, x: x, y: y); Raw(host); Render(host); PumpSourceMenu(host); }
    private static void Down(PlayerHost host, int count)
    { for (int i = 0; i < count; i++) Move(host, 0, 1); }
    private static void Wakes(PlayerHost host, int count, int held = 0)
    {
        for (int i = 0; i < count; i++)
        {
            try { Raw(host, held); Render(host, 1.0 / 57); }
            catch (InvalidDataException error) when (accountingCase != null)
            {
                object model = Model(host);
                object? pending = model.GetType().GetField("SourceC2PendingResult", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(model);
                var gameProc = ((ISourcePossessionState)host.Game.State).Processes
                    .FirstOrDefault(process => process.Function == "game_proc");
                Console.WriteLine(JsonSerializer.Serialize(new { accountingCallerFailure = accountingCase,
                    error = error.Message, sourceTick = host.Game.SourceTick,
                    queuedTick = pending == null ? (long?)null : (long)Get(pending, "QueuedTick"),
                    gameProc, cameraContinuation = Get(model, "SourceCameraPossessionContinuation"),
                    phase = Match(host).Phase.ToString(), resultPending = host.Game.C2State.ResultPending }));
                throw;
            }
        }
    }
    private static async Task Settled(PlayerHost host)
    {
        for (int i = 0; i < 5000 && !Records(host).PendingOperation.IsCompleted; i++)
        { PumpSourceMenu(host, 1); await Task.Yield(); }
        Check(Records(host).PendingOperation.IsCompleted, "The real source menu must resume queued storage completion.");
        await Records(host).PendingOperation; Raw(host); Render(host);
    }

    private static async Task<(PlayerHost Host, Sink Sink, SourcePlayerRecordStore Store)> Host(PlayerData data, OwnedStorage storage,
        bool kickMeters = false)
    {
        var store = new SourcePlayerRecordStore(storage);
        await store.LoadAsync();
        var sink = new Sink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl(
            kickMeters ? "?source-kick-meters=portable-authored" : "", data.Catalog.Profile.StableId), recordStore: store);
        data.Catalog.RequireAllInputsConsumed();
        Render(host);
        return (host, sink, store);
    }

    private static void BeginNames(PlayerHost host, bool joinSecond = false)
    {
        Raw(host);
        if (joinSecond) Tap(host, 1, 1);
        Tap(host, 4);
        Check(host.Session.Stage == SourceCabinetMenuStage.Records &&
            Records(host).View().Kind == SourceRecordScreenKind.Options,
            "The public cabinet C must enter the actual original options screen before teams.");
        Tap(host, 1);
    }

    private static void Key(PlayerHost host, int target, int station, bool pin = false, bool playName = false)
    {
        int Cursor() => playName ? Records(host).View().InputCursor :
            Records(host).View().Stations.Single(item => item.StationId == station).Cursor;
        int columns = pin ? 3 : 4;
        while (Cursor() / columns > target / columns) Move(host, 0, -1, station);
        while (Cursor() / columns < target / columns) Move(host, 0, 1, station);
        while (Cursor() > target) Move(host, -1, 0, station);
        while (Cursor() < target) Move(host, 1, 0, station);
        Tap(host, 1, station);
    }

    private static void Enter(PlayerHost host, string name, string pin, int station = 0)
    {
        Move(host, 0, -1, station); Tap(host, 1, station);
        foreach (char character in name) Key(host, "ABCDEFGHIJKLMNOPQRSTUVWXYZ-*! ".IndexOf(character), station);
        foreach (char character in pin) Key(host, "123456789 0".IndexOf(character), station, pin: true);
    }

    private static void TeamsToGame(PlayerHost host, bool receiveHome = false)
    {
        Tap(host, 4);
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams, "All explicit record choices must lead to the real team chooser.");
        Tap(host, 1); Tap(host, 1);
        if (receiveHome && host.Session.CreateConfiguration().FirstPossession == TeamId.Home) Move(host, 1, 0);
        Tap(host, 1);
        Check(host.Session.Playing && host.Game.SourceTick == 0 &&
            host.Game.SourceCustomPlaySession?.SavedRecords != null,
            "The real fresh Game must bind the SAME saved-record session before source wake1.");
        Raw(host);
    }

    private static async Task VerifyEditorAndFinal(PlayerData data)
    {
        var storage = new OwnedStorage();
        var (host, sink, store) = await Host(data, storage);
        BeginNames(host);
        Enter(host, Name, Pin);
        Check(store.LoadState == SourceRecordStoreStatus.NotFound && storage.Writes == 0 &&
            Records(host).View().Stations.Single().Step == "Missing",
            "Typing a missing six-position identity must not silently initialize or create a record.");
        Tap(host, 1); await Settled(host);
        Check(store.Current.Find(Identity)?.Career.GamesPlayed == 0 && storage.Writes == 2 &&
            Records(host).View().Stations.Single().Step == "Selected",
            "Only explicit Create may await cabinet initialization plus actual durable identity creation.");
        string identityDiagnostics = sink.State.GetProperty("records").GetRawText() +
            sink.State.GetProperty("cabinetSession").GetRawText() + sink.State.GetProperty("hud").GetRawText();
        Check(!identityDiagnostics.Contains(Pin, StringComparison.Ordinal) && !identityDiagnostics.Contains(Name.Trim(), StringComparison.Ordinal),
            "The actual record/session/HUD diagnostic fields must not contain identity names or unmasked PINs.");
        Check(sink.HudTextures.All(sink.Textures.ContainsKey),
            "The real source-HUD must render only actually uploaded admitted original resources.");

        Tap(host, 1); Tap(host, 1); Tap(host, 1);
        Check(Records(host).View().Kind == SourceRecordScreenKind.Editor &&
            Records(host).View().Draft.Point(0, 0).X == 9 && Records(host).View().Draft.Point(1, 0).Y == 5,
            "The actual editor must start from Backend's original source formation, not a handwritten football/PAT play.");
        Down(host, 1); Tap(host, 1);
        Tap(host, 1);
        for (int i = 0; i < 6; i++) Move(host, 0, -1);
        Tap(host, 1); Down(host, 3); Tap(host, 1);
        for (int i = 0; i < 6; i++) Move(host, 1, 0);
        Tap(host, 1); Down(host, 5); Tap(host, 1);
        Down(host, 4); Tap(host, 1);
        Down(host, 1); Tap(host, 1);
        Down(host, 3); Tap(host, 1);
        foreach (char c in "ZIG") Key(host, c - 'A', 0, playName: true);
        Key(host, 31, 0, playName: true);
        foreach (char c in "RUN") Key(host, c - 'A', 0, playName: true);
        Key(host, 31, 0, playName: true);
        Down(host, 10); Tap(host, 1); await Settled(host);
        SourceCustomPlay play = store.Current.Find(Identity).CustomPlays[0] ??
            throw new InvalidOperationException("The actual editor save produced no source custom play.");
        Check(play.Point(0, 1).X == 9 && play.Point(0, 1).Y == 16 &&
            (play.Point(0, 1).Flags & SourceCustomPointFlags.Turbo) != 0 &&
            play.Point(0, 2).X == 15 && play.Point(0, 2).Y == 18 && play.Name1 == "ZIG" && play.Name2 == "RUN" &&
            (play.Flags & (SourceCustomPlayFlags.PrimaryReceiver1 | SourceCustomPlayFlags.QuarterbackFar)) ==
                (SourceCustomPlayFlags.PrimaryReceiver1 | SourceCustomPlayFlags.QuarterbackFar),
            "Ordinary editor controls must save the actual routed grid, special point, two source name fields, primary and QB flags.");
        Check(sink.HudTextures.Contains("hud/record-solid") && sink.HudTextures.Contains("hud/original/mp_plate"),
            "Actual compact source-route geometry and original custom-play cards must reach the real Host HUD packet.");

        if (!observedResumeOnly)
        {
        SourcePlayerRecords beforeEditorFailure = store.Current;
        Tap(host, 1); Down(host, 5); Tap(host, 1);
        SourceCustomPlay unsaved = Records(host).View().Draft;
        storage.FailNext = true;
        Down(host, 5); Tap(host, 1); await Settled(host);
        Check(ReferenceEquals(store.Current, beforeEditorFailure) && !store.IsWritable &&
            Records(host).View().Kind == SourceRecordScreenKind.Editor && ReferenceEquals(Records(host).View().Draft, unsaved),
            "A failed actual editor save must retain the old Current AND the user's unsaved draft/editor, not close or claim Saved.");
        Down(host, 1); Tap(host, 1); Tap(host, 1); await Settled(host);
        Tap(host, 2); Move(host, 0, -1); Tap(host, 1); await Settled(host);
        play = store.Current.Find(Identity).CustomPlays[0];
        Check(Records(host).View().Kind == SourceRecordScreenKind.CustomSlots &&
            (play.Flags & SourceCustomPlayFlags.QuarterbackFar) == 0,
            "The original editor must reload/retry the retained draft through the same actual store without clearing custom routes.");
        }

        Tap(host, 4);
        Move(host, -1, 0); Tap(host, 4);
        Down(host, 6); Tap(host, 1); await Settled(host);
        SourceRecordAudibles audibles = store.Current.Find(Identity).Audibles;
        Check(audibles.Offense.SequenceEqual(new short[] { -37, 2, 3 }) && audibles.Defense.All(id => id == 0),
            "The actual menu must persist signed custom audible37 while leaving an untouched allzero defense row as original defaults.");
        Down(host, 4); Tap(host, 1); TeamsToGame(host);
        Check(ReferenceEquals(host.Game.SourceCustomPlaySession.SavedRecords.Stations.Single().Record, store.Current.Find(Identity)) &&
            host.Session.ReadGamesPlayed(0) == 0,
            "Game binding and tutorial experience must consume the actual selected immutable saved record.");
        int resources = sink.Textures.Count;
        if (!terminalOnly) await VerifyNaturalCustomSelection(host, sink, play);
        if (observedResumeOnly)
        {
            VerifyObservedResume();
            return;
        }
        if (naturalFullMatch)
        {
            await VerifyNaturalNamedFinal(host, sink, store, storage, play);
            return;
        }

        void VerifyObservedResume()
        {
            double witness = 0;
            var axes = new double[8];
            void Frame(double actualElapsed, double nominalElapsed, int button = 0)
            {
                witness += actualElapsed;
                host.CaptureObserved(axes, new[] { button, 0, 0, 0 }, witness);
                host.AdvanceObserved(nominalElapsed, witness, 512, 384, 512, 384, 1);
            }
            JsonElement Clock() => sink.State.GetProperty("realtimeClock");
            double Pending() => Clock().GetProperty("PendingSeconds").GetDouble() +
                Clock().GetProperty("pauseCarrySeconds").GetDouble();
            Check(Match(host).Phase == SourceMatchPhase.PreSnap && Match(host).PlayKind == SourcePlayKind.Normal,
                "The resume regression must begin at genuine selected normal PreSnap, not a phase/clock fixture.");
            Frame(0, 0);
            Frame(0.167623, 0.167623);
            long beforePauseDrain = host.Game.State.Tick;
            double beforePauseCarry = Pending();
            long audioEpoch = host.AudioPlayback.Epoch, originEpoch = sink.AudioClockEpoch, originTick = sink.AudioClockSourceTick;
            int resets = sink.AudioResets, starts = sink.AudioClockStarts;
            Check(beforePauseCarry >= 0.06 && beforePauseCarry < 0.1, "The actual bounded Host must retain, not discard, real pre-pause active elapsed debt.");
            host.SetActive(false);
            long pausedHost = host.Game.State.Tick, pausedSource = host.Game.SourceTick;
            host.AdvanceObserved(0, witness, 512, 384, 512, 384, 1);
            double carry = Pending();
            long drained = (long)Math.Floor((beforePauseCarry + 1e-10) * 60);
            Check(drained <= 6 && pausedHost == beforePauseDrain + drained &&
                Math.Abs(beforePauseCarry - carry - drained / 60.0) < 1e-8 && carry < 1.0 / 60,
                "Public pause must drain the bounded REAL host debt before freezing and retain its fractional remainder.");
            host.AdvanceObserved(10, witness, 512, 384, 512, 384, 1);
            Check(host.Game.State.Tick == pausedHost && host.Game.SourceTick == pausedSource,
                "A real paused wall-time gap must advance neither host nor original source.");
            host.CaptureObserved(axes, new int[4], witness);
            host.CaptureObserved(axes, new int[4], witness);
            host.SetActive(true);
            sink.DiagnosticRenderElapsed += 0.085333;
            Frame(0.012620, 0);
            long firstSteps = (long)Math.Floor((carry + 0.012620 + 1e-10) * 60);
            Check(host.Game.State.Tick == pausedHost + firstSteps &&
                Math.Abs(Pending() - carry - 0.012620 + firstSteps / 60.0) < 1e-8,
                "First ready resume must count only causal12.620ms host elapsed despite nominal0, never advance physics from render-clock batching.");
            Frame(0.047380, 0);
            long expectedSteps = (long)Math.Floor((carry + 0.060000 + 1e-10) * 60);
            Check(host.Game.State.Tick == pausedHost + expectedSteps &&
                Pending() < 1.0 / 60,
                "The next real host interval must survive the existing resume Bridge discard boundary and preserve its time fraction.");
            for (int i = 0; i < 6; i++) Frame(1.0 / 60, 1.0 / 60);
            SourceConsumedPhysicalSwitchScan scan = host.Game.ConsumedSourceSwitches;
            Check(scan != null && scan.SourceTick == host.Game.SourceTick &&
                scan.InputEpoch == host.Game.SourceInputEpoch && scan.PhysicalSwitches.IsNeutral &&
                !sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean(),
                "Repeated actual neutral captures must populate the consumed fresh source epoch, not a fabricated fallback scan.");
            Check(host.AudioPlayback.Epoch == audioEpoch && sink.AudioResets == resets &&
                sink.AudioClockStarts == starts && sink.AudioClockEpoch == originEpoch && sink.AudioClockSourceTick == originTick,
                "Public pause/resume must retain the SAME genuine C6 epoch/source clock origin, not reanchor or reset it.");
            bool Ready()
            {
                var audible = Match(host).Audibles;
                var qb = host.Game.State.Players.Single(player => player.Id == 7 * (int)audible.Offense.Team + 3);
                return host.Game.State.PreSnapTicksRemaining < 228 && qb.Source.ControllerStationId == 0 &&
                    qb.Source.Presentation.RawPlayerMode == 4 && (qb.Animation.Primary.SourceModeBits & 2u) == 0;
            }
            for (int i = 0; i < 600 && !Ready(); i++) Frame(1.0 / 60, 1.0 / 60);
            Check(Ready(), "The fresh default QB must reach the ACTUAL native snap counter/mode/interruptibility gate.");
            int plays = sink.PlayerProgrammes.Count;
            for (int i = 0; i < 4; i++) Frame(1.0 / 60, 1.0 / 60, 1);
            JsonElement pressedPhysical = JsonSerializer.SerializeToElement(host.Game.ConsumedSourceSwitches?.PhysicalSwitches);
            for (int i = 0; i < 120 && (Match(host).Phase != SourceMatchPhase.LiveBall || sink.PlayerProgrammes.Count == plays); i++)
                Frame(1.0 / 60, 1.0 / 60);
            Check(Match(host).Phase == SourceMatchPhase.LiveBall && sink.PlayerProgrammes.Count > plays,
                "The real eligible post-resume A press must reach normal live play and emit an actual admitted player programme. Actual phase=" +
                Match(host).Phase + " source=" + host.Game.SourceTick + " actualPhysical=" + pressedPhysical.GetRawText() +
                " clock=" + Clock().GetRawText() + " homeAction=" + Match(host).Audibles.HomeTeam.Action +
                " awayAction=" + Match(host).Audibles.AwayTeam.Action + " caller=" + Match(host).Audibles.Offense.CallerStationId);
            Console.WriteLine(JsonSerializer.Serialize(new { actualHostObservedResume = "passed",
                pausedHost, pausedSource, carriedSeconds = carry, observedHostResumeSeconds = 0.012620,
                renderClockNeverPhysicsElapsed = true, actualResumedHost = host.Game.State.Tick,
                sourceTick = host.Game.SourceTick, consumedEpoch = host.Game.SourceInputEpoch,
                audioEpoch, sourceClockOrigin = originTick, actualPlayerProgrammes = sink.PlayerProgrammes.Skip(plays).ToArray(),
                manualBridgePriming = false, privateClockScoreOrPhaseMutation = false }));
        }

        FinishActualKickoff(host);
        TerminalFixture(host, 0, 0, 0);
        ToFlow(host, SourceCabinetFlowStage.CoachingTip);
        ToFlow(host, SourceCabinetFlowStage.Inactive);
        FinishActualKickoff(host);

        // Explicit terminal/statistics fixture, not natural-match evidence. The
        // actual game_proc still creates its final context and owns the IO wait.
        storage.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SourcePlayerRecords previous = store.Current;
        long generation = store.Generation;
        TerminalFixture(host, 3, 21, 7);
        ToFlow(host, SourceCabinetFlowStage.RecordCommitWait);
        int attempted = storage.Writes;
        SourceMatchView final = Flow(host).FinalMatch;
        Wakes(host, 8);
        for (int i = 0; i < 4; i++) Render(host);
        Check(ReferenceEquals(store.Current, previous) && store.Generation == generation &&
            Records(host).BlocksGameplay && Flow(host).Stage == SourceCabinetFlowStage.RecordCommitWait &&
            storage.Writes == attempted && ReferenceEquals(final, Flow(host).FinalMatch),
            "Pending actual CAS must retain old Current and the SAME immutable game_proc final; redraw/wakes cannot commit twice or advance final stats.");
        storage.FailNext = true; storage.Hold.SetResult(); await Settled(host); storage.Hold = null;
        Check(store.Notification.Status == SourceRecordStoreStatus.IoFailure && !store.IsWritable &&
            ReferenceEquals(store.Current, previous) && Records(host).View().Kind == SourceRecordScreenKind.Final,
            "A genuine asynchronous storage rejection must leave the previous career intact and show failed/retry UI, not Saved.");
        Tap(host, 1); await Settled(host); Wakes(host, 3);
        SourcePlayerRecord completed = store.Current.Find(Identity);
        Check(completed.Career.GamesPlayed == 1 && completed.Career.GamesWon == 1 &&
            completed.Career.PointsScored == 21 && completed.Career.PointsAllowed == 7 &&
            completed.Career.Intercepted == final.Interceptions.AwayInterceptionsMade &&
            store.Current.CommittedMatches.Values.Single() == 1 &&
            store.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Value == 21,
            "Awaited retry must atomically save career, opposite interceptions, actual worlds and a single receipt from the original final.");
        int committedWrites = storage.Writes;
        Wakes(host, 6);
        Check(storage.Writes == committedWrites && host.Session.MatchesCompleted == 1,
            "Repeated final observations cannot write, count a second match or reapply the immutable final.");
        ToFlow(host, SourceCabinetFlowStage.FinalConfirmation);
        Raw(host); Wakes(host, 3); host.Continue(0); Wakes(host, 4);
        Check(host.Session.Stage == SourceCabinetMenuStage.NextMatch && Records(host).Active,
            "Actual final confirmation must reach retained career/next-match UI only after the save barrier.");
        Tap(host, 2); Raw(host);
        GameSimulation old = host.Game;
        Tap(host, 1);
        Check(!ReferenceEquals(old, host.Game) && host.Game.SourceTick == 0 && sink.Textures.Count == resources &&
            ReferenceEquals(host.Data, data) && store.Current.Find(Identity).Career.GamesPlayed == 1 &&
            host.Game.SourceCustomPlaySession.SavedRecords.Stations.Single().Record.Career.GamesPlayed == 1 &&
            host.Session.ReadGamesPlayed(0) == 1,
            "A deliberate named rematch must refresh only the Game/immutable binding and retain the SAME store, original render resources and earned career experience.");

        var freshStore = new SourcePlayerRecordStore(storage);
        await freshStore.LoadAsync();
        SourcePlayerRecord reloaded = freshStore.Current.Find(Identity);
        Check(reloaded.Career.GamesPlayed == 1 && reloaded.Audibles.Offense[0] == -37 &&
            SourceCustomPlayCodec.EncodeCompact(reloaded.CustomPlays[0]).SequenceEqual(SourceCustomPlayCodec.EncodeCompact(play)),
            "A new actual store lifetime must reload career, exact compact route bytes and signed audibles together.");
    }

    private static async Task VerifyNaturalCustomSelection(PlayerHost host, Sink sink, SourceCustomPlay play)
    {
        for (int wake = 0; wake < 6000; wake++)
        {
            SourceMatchView match = Match(host);
            if (match.Phase == SourceMatchPhase.PlaySelection && match.Possession.Offense == TeamId.Home) break;
            int held = host.Instructions?.Frame().Count > 0 || Flow(host).Stage != SourceCabinetFlowStage.Inactive ? 7 : 0;
            if (match.Phase == SourceMatchPhase.PlaySelection) held = 0;
            Wakes(host, 1, held);
        }
        Check(Match(host).Phase == SourceMatchPhase.PlaySelection && Match(host).Possession.Offense == TeamId.Home,
            "Ordinary source progression must reach the named station's actual offensive play selector. Actual: source=" +
            host.Game.SourceTick + " phase=" + Match(host).Phase + " play=" + Match(host).PlayKind +
            " offense=" + Match(host).Possession.Offense + " flow=" + Flow(host).Stage + " menu=" + host.Session.Stage);
        for (int wake = 0; wake < 1000 && host.Session.ReadPlaySelectionInstructionGate() >= 10; wake++) Wakes(host, 1);
        Wakes(host, 40);
        if (observedResumeOnly)
        {
            Tap(host, 2);
            for (int wake = 0; wake < 120 && Match(host).Phase != SourceMatchPhase.PreSnap; wake++) Wakes(host, 1);
            Check(Match(host).Phase == SourceMatchPhase.PreSnap && Match(host).PlayKind == SourcePlayKind.Normal,
                "The isolated resume check must select a fresh DEFAULT normal play through actual public controls.");
            Wakes(host, 8);
            return;
        }
        for (int page = 0; page < 4; page++) { Tap(host, 4); Wakes(host, 4); }
        Check(sink.State.GetProperty("menu").GetProperty("home").GetProperty("page").GetInt32() == 4 &&
            sink.State.GetProperty("hud").GetProperty("elements").EnumerateArray().Any(item =>
                item.GetString()!.Contains("/custom/0/route/0/", StringComparison.Ordinal)),
            "The real last offense page must render that record's actual source route diagram, not a static placeholder.");
        Tap(host, 2);
        for (int wake = 0; wake < 120 && Match(host).Phase != SourceMatchPhase.PreSnap; wake++) Wakes(host, 1);
        SourcePlayDefinition definition = (SourcePlayDefinition)Get(Model(host), "SourceOffensePlay");
        SourcePlayDefinition expected = SourceCustomPlayCodec.ConvertToPlay(play, 0, 0, host.Data.Playbook);
        Check(definition.Identity == expected.Identity && definition.Formation.Slots[3].Offset.X == expected.Formation.Slots[3].Offset.X &&
            definition.Routes[4].Words.SequenceEqual(expected.Routes[4].Words),
            "Actual public play selection must install the saved dynamic formation and converted receiver route, not a catalog/PAT alias.");
        Wakes(host, 8);
        Tap(host, 4); Wakes(host, 3); Tap(host, 4); Wakes(host, 3);
        Check(Match(host).Audibles.HomeTeam.Action == 1,
            "The named station's ordinary double-C must open the real source audible menu.");
        JsonElement audibleJson = sink.State.GetProperty("audibles").GetProperty("core");
        Check(audibleJson.TryGetProperty("HomeTeam", out JsonElement homeJson) &&
            homeJson.TryGetProperty("Action", out JsonElement actionJson) &&
            actionJson.ValueKind == JsonValueKind.Number &&
            actionJson.GetInt32() == Match(host).Audibles.HomeTeam.Action,
            "The public Host audibles.core must expose the SAME consumed audible action. Actual public keys: " +
            string.Join(",", audibleJson.EnumerateObject().Select(item => item.Name)));
        JsonElement offenseJson = audibleJson.GetProperty("Offense");
        Check(offenseJson.GetProperty("Choice").GetInt32() == Match(host).Audibles.Offense.Choice &&
            offenseJson.GetProperty("CallerStationId").GetInt32() == 0 &&
            Match(host).Audibles.Offense.CallerStationId == 0,
            "The public opened audible must expose the SAME actual choice and named human caller.");
        Move(host, 0, 1); Wakes(host, 10);
        SourcePlayDefinition audible = (SourcePlayDefinition)Get(Model(host), "SourceOffensePlay");
        Check(audible.Identity == expected.Identity && Match(host).OffenseFlipped == true &&
            Match(host).HomePlay.SavedOrigin.SlotIndex == 0,
            "The actual signed -37 audible must restart the source saved formation/routes with its recorded flip.");
        for (int choice = 0; choice < 3; choice++) { Move(host, 0, 1); Wakes(host, 10); }
        Check(ReferenceEquals(Get(Model(host), "SourceOffensePlay"), definition) &&
            Match(host).OffenseFlipped == false && Match(host).Audibles.Offense.Choice == 0,
            "Cycling ORIGINAL through ordinary controls must restore the SAME saved definition and original flip, not a static catalog lookup.");
        await Task.CompletedTask;
    }

    private static async Task VerifyGuestSpacesDuplicatesAndRecovery(PlayerData data)
    {
        var guestStorage = new OwnedStorage();
        var (guest, _, guestStore) = await Host(data, guestStorage);
        Raw(guest); Tap(guest, 4); Move(guest, 1, 0); Tap(guest, 1);
        Check(guest.Session.Stage == SourceCabinetMenuStage.Teams && guestStorage.Writes == 0 && guestStore.Current == null,
            "Deliberate SELECT TEAM guest must not initialize an empty cabinet or claim a saved identity.");

        var (cap, capSink, capStore) = await Host(data, new OwnedStorage());
        Raw(cap); Tap(cap, 4); Move(cap, -1, 0); Tap(cap, 1);
        Check(Records(cap).View().Kind == SourceRecordScreenKind.Confirmation,
            "The original CREATE option must ask for its source confirmation rather than silently enter the editor.");
        Move(cap, 0, 1); Tap(cap, 1);
        Check(Records(cap).View().CapIdentityEntry && capSink.HudTextures.Contains("hud/original/letters") &&
            capSink.HudTextures.Contains("hud/original/lback"),
            "CREATE must use the single actual CAP get_name/get_pin art and flow, not a normal four-station name plaque.");
        foreach (char character in "BBBBBB") Key(cap, character - 'A', 0);
        foreach (char character in "2222") Key(cap, character - '1', 0, pin: true);
        Tap(cap, 1); await Settled(cap);
        Check(capStore.Current.Find(new SourceRecordIdentity("BBBBBB", "2222")) != null &&
            Records(cap).View().Kind == SourceRecordScreenKind.CustomSlots,
            "The CAP identity must await explicit durable creation before the actual source custom slots.");

        var storage = new OwnedStorage();
        var (spaces, _, store) = await Host(data, storage);
        BeginNames(spaces, joinSecond: true);
        Enter(spaces, "      ", "0000");
        Tap(spaces, 1); await Settled(spaces);
        var blank = new SourceRecordIdentity("      ", "0000");
        Check(store.Current.Find(blank) != null && Records(spaces).View().Stations.Single(item => item.StationId == 0).Step == "Selected",
            "Six actual source spaces plus a PIN must be a named record, never inferred Guest from whitespace.");
        Enter(spaces, "      ", "0000", 1);
        Check(Records(spaces).View().Stations.Single(item => item.StationId == 1).Step == "Duplicate",
            "A second physical station must explicitly resolve duplicate name/PIN selection rather than share mutable career ownership.");
        Tap(spaces, 2, 1);
        Check(Records(spaces).CanStartMatch && store.Current.Records.Count == 1,
            "The duplicate station's deliberate Guest choice must preserve the first station's named space identity.");

        string corrupt = "owned-synthetic-corrupt-record-ui";
        var brokenStorage = new OwnedStorage { Image = new SourceRecordStorageImage(corrupt, storage.Image.Primary) };
        var (broken, _, brokenStore) = await Host(data, brokenStorage);
        BeginNames(broken); Enter(broken, "      ", "0000");
        Check(brokenStore.LoadState == SourceRecordStoreStatus.Corrupt && brokenStorage.Writes == 0 &&
            Records(broken).View().Stations.Single().Step == "Unavailable" && !Records(broken).CanStartMatch,
            "An actually corrupt primary must never become NotFound/new Guest or a silent initialized record.");
        Tap(broken, 4); Down(broken, 1); Tap(broken, 1);
        Tap(broken, 1);
        Check(brokenStorage.Writes == 0 && brokenStorage.Image.Primary == corrupt,
            "Destructive recovery must default to NO and preserve the original corrupt bytes without consent.");
        Tap(broken, 1); Move(broken, 0, 1); Tap(broken, 1); await Settled(broken);
        Check(brokenStore.IsWritable && brokenStore.Current.Find(blank) != null && brokenStorage.Image.Recovery == corrupt,
            "Explicit normal-control recovery must restore the real authenticated backup and preserve corrupt primary quarantine.");
        Down(broken, 1); Tap(broken, 1);
        Check(Records(broken).CanImport, "The original-HUD management action must explicitly arm a local import gesture.");
        var exported = await store.ExportAsync();
        Records(broken).StagePortableImport(exported.Envelope);
        Check(Records(broken).View().Kind == SourceRecordScreenKind.Confirmation &&
            !JsonSerializer.Serialize(Records(broken).Describe()).Contains("0000", StringComparison.Ordinal),
            "Staged portable import must remain private and require a second C# replacement confirmation.");
        Tap(broken, 2);
        Check(brokenStore.Current.Find(blank) != null, "Declining portable import must preserve current actual records.");
        long importedGeneration = brokenStore.Generation;
        Tap(broken, 1);
        Records(broken).StagePortableImport(exported.Envelope);
        Move(broken, 0, 1); Tap(broken, 1); await Settled(broken);
        Check(brokenStore.Generation == importedGeneration + 1 && brokenStore.Current.Find(blank) != null &&
            brokenStorage.Image.Recovery == corrupt,
            "Confirmed actual local import must use the real atomic replacement and preserve prior corrupt quarantine.");
        Down(broken, 1); Tap(broken, 1);
        Check(Records(broken).CanExport, "The original-HUD management action must explicitly arm a local export gesture.");
        Task<string> exportTask = Records(broken).ExportPortableAsync();
        for (int tick = 0; tick < 5000 && !exportTask.IsCompleted; tick++) { PumpSourceMenu(broken, 1); await Task.Yield(); }
        string portable = await exportTask;
        Check(!string.IsNullOrEmpty(portable) && !Records(broken).CanExport &&
            !JsonSerializer.Serialize(Records(broken).Describe()).Contains(portable, StringComparison.Ordinal),
            "Actual portable export must return only to the armed local transfer, never a public debug snapshot.");
        Tap(broken, 2); Tap(broken, 1); Down(broken, 6); Tap(broken, 1);
        Move(broken, 0, 1); Tap(broken, 1); await Settled(broken);
        var afterDelete = new SourcePlayerRecordStore(brokenStorage);
        await afterDelete.LoadAsync();
        Check(afterDelete.Current.Find(blank) == null && afterDelete.Current.Records.Count == 0,
            "Explicit confirmed Delete must durably remove the selected identity and its custom/audible data across a new load.");
    }

    private static async Task VerifyNaturalNamedFinal(PlayerHost host, Sink sink, SourcePlayerRecordStore store,
        OwnedStorage storage, SourceCustomPlay play)
    {
        var stages = new HashSet<SourceCabinetFlowStage>();
        int pulse = 0, captures = 0;
        for (int frame = 0; frame < 45000 && host.Session.MatchesCompleted == 0; frame++)
        {
            SourceMatchView match = Match(host);
            SourceCabinetFlowStage stage = Flow(host).Stage;
            stages.Add(stage);
            int button = host.Game.State.Phase == GamePhase.PlaySelect ? pulse++ % 2 == 0 ? 2 : 0 :
                host.Game.State.Phase == GamePhase.PreSnap && match.PlayKind != SourcePlayKind.Kickoff ? pulse++ % 2 == 0 ? 1 : 0 :
                stage is SourceCabinetFlowStage.ResultWait or SourceCabinetFlowStage.EndOfQuarter ||
                    host.Instructions?.Frame().Count > 0 ? 7 : 0;
            Raw(host, button); captures++;
            Render(host, FixedStepBridge.StepSeconds * 8);
        }
        await Records(host).PendingOperation;
        Check(host.Session.MatchesCompleted == 1 && host.Session.LastResult != null &&
            stages.Contains(SourceCabinetFlowStage.EndOfQuarter) && stages.Contains(SourceCabinetFlowStage.HalftimeStats),
            "The named actual source match must finish real regulation/intermissions using public controls only.");
        SourceCabinetStatistics final = (host.Session.LastResult ??
            throw new InvalidOperationException("The natural final was not retained.")).Statistics;
        SourcePlayerRecord record = store.Current.Find(Identity);
        Check(record.Career.GamesPlayed == 1 && record.Career.PointsScored == final.HomeScore &&
            record.Career.PointsAllowed == final.AwayScore && record.Career.GamesWon == (final.HomeScore > final.AwayScore ? 1 : 0) &&
            record.Career.Tackles == final.Match.PlayActivityStats.HomeTackles &&
            record.Career.Intercepted == final.Match.Interceptions.AwayInterceptionsMade &&
            store.Current.CommittedMatches.Values.Single() == 1,
            "The actual natural final must atomically credit precisely the source team totals/opposite interceptions and a single real receipt.");
        var recovered = new SourcePlayerRecordStore(storage);
        await recovered.LoadAsync();
        SourcePlayerRecord loaded = recovered.Current.Find(Identity);
        Check(loaded.Career.GamesPlayed == 1 && loaded.Career.WinStreak == record.Career.WinStreak &&
            loaded.Career.TeamsDefeated == record.Career.TeamsDefeated &&
            loaded.Audibles.Offense[0] == -37 &&
            SourceCustomPlayCodec.EncodeCompact(loaded.CustomPlays[0]).SequenceEqual(SourceCustomPlayCodec.EncodeCompact(play)),
            "A new durable consumer must recover the real completed career, defeated bits, custom bytes and signed audibles together.");
        Console.WriteLine(JsonSerializer.Serialize(new { naturalNamedMatch = "passed", privateGameOrClockMutation = false,
            actualPublicCaptures = captures, sourceTick = host.Game.SourceTick,
            completedPeriodCount = final.CompletedQuarterIndex + 1, final.HomeScore, final.AwayScore,
            sourceCareerAndWorldCommit = store.Notification.Code, generation = store.Generation,
            loadedCareerGames = loaded.Career.GamesPlayed, sourceStages = stages.Select(stage => stage.ToString()).ToArray(),
            exactCustomAndSignedAudiblesReloaded = true }));
    }

    private static object Model(PlayerHost host) => typeof(GameSimulation).GetProperty("Model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host.Game)!;
    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static T Method<T>(PlayerHost host, string name) where T : Delegate =>
        (T)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(T), host.Game);

    private static void TerminalFixture(PlayerHost host, int quarter, int home, int away)
    {
        if (host.Game.SourceTick == 0) Wakes(host, 2);
        Method<Action>(host, "SourceFireSelectionExit")();
        object model = Model(host);
        Set(model, "Quarter", quarter + 1); Set(model, "SourcePeriodView", new SourcePeriodView(quarter));
        Set(model, "HomeScore", home); Set(model, "AwayScore", away);
        Set(model, "SourceRules", new SourceRulesState(0, 0, 2, 50, 80, SourcePlayType.Normal));
        Set(model, "Possession", TeamId.Home); Set(model, "SourceLastPossession", 0);
        Set(model, "SourceLastResult", SourcePlayResult.Down); Set(model, "SourceFlags", SourceGameFlags.None);
        Set(model, "SourcePlayEndX", 0f); Set(model, "SourcePlayEndZ", 0f); Set(model, "SourceFumbleX", 0f);
        Set(model, "SourceForwardCatcher", -1); Set(model, "BallKind", SourceBallKind.Pass);
        Set(model, "BallMode", BallMode.Held); Set(model, "CarrierId", 3);
        Set(model, "SourceClock", SourceClock.Create(0, firstWakeTick: host.Game.SourceTick + 1));
        Set(model, "ClockActive", true); Set(model, "Phase", GamePhase.LiveBall); Set(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        var scores = (int[,])Get(model, "SourceQuarterScores");
        Array.Clear(scores); scores[quarter, 0] = home; scores[quarter, 1] = away;
        foreach (object actor in (Array)Get(model, "Players")) Set(actor, "SourceMode", SourceActorMode.Dead);
        Method<Action<SourcePlayEndCause, int, FieldVector, int, SourceVector3?>>(host, "EndSourcePlay")(
            SourcePlayEndCause.CarrierTackledInBounds, 3, new FieldVector(50, 0), -1, null);
        Method<Action>(host, "Publish")();
        host.HudProcess.Observe(host.Game); Wakes(host, 2);
    }

    private static void ToFlow(PlayerHost host, SourceCabinetFlowStage stage)
    {
        for (int wake = 0; wake < 5000 && Flow(host).Stage != stage; wake++) Wakes(host, 1, 7);
        Check(Flow(host).Stage == stage, "Actual source final continuation did not reach " + stage + ".");
    }

    private static void FinishActualKickoff(PlayerHost host)
    {
        for (int wake = 0; wake < 5000 && Match(host).PlayKind == SourcePlayKind.Kickoff; wake++) Wakes(host, 1);
        Check(Match(host).PlayKind != SourcePlayKind.Kickoff, "The fixture must let the actual original kickoff/CAMBOT opening finish before declaring a normal terminal play.");
        Wakes(host, 2);
    }

    private sealed class AccountingCase
    {
        internal string Id = "", Producer = "";
        internal SourcePlayResult Result = SourcePlayResult.Down;
        internal SourcePlayType Type = SourcePlayType.Normal;
        internal SourcePlayEndCause Cause = SourcePlayEndCause.CarrierTackledInBounds;
        internal int Initial, Side, Los = 40, End = 60, Carrier = 4, Quarter = 3, Home = 3, Away;
        internal int HomeAdded, AwayAdded;
        internal bool FreePat;
        internal readonly Dictionary<string, int> Delta = new(StringComparer.Ordinal);
    }

    private static Dictionary<string, int> Accounting(SourceMatchView match)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        void Pair(string key, int home, int away) { result["home." + key] = home; result["away." + key] = away; }
        Pair("passes", match.PlayActivityStats.HomePassAttempts, match.PlayActivityStats.AwayPassAttempts);
        Pair("completions", match.PassCompletions.HomeCompletions, match.PassCompletions.AwayCompletions);
        Pair("passing", match.CompletedPassStats.HomePassingYards, match.CompletedPassStats.AwayPassingYards);
        Pair("passTD", match.CompletedPassStats.HomePassTouchdowns, match.CompletedPassStats.AwayPassTouchdowns);
        Pair("rushing", match.PlayEndStats.HomeRushingYards, match.PlayEndStats.AwayRushingYards);
        Pair("rushes", match.PlayEndStats.HomeRushes, match.PlayEndStats.AwayRushes);
        Pair("sacks", match.PlayEndStats.HomeSacks, match.PlayEndStats.AwaySacks);
        Pair("sackYards", match.PlayEndStats.HomeSackYards, match.PlayEndStats.AwaySackYards);
        Pair("returns", match.PlayEndStats.HomeReturnYards, match.PlayEndStats.AwayReturnYards);
        Pair("kickReturns", match.PlayEndStats.HomeKickReturns, match.PlayEndStats.AwayKickReturns);
        Pair("total", match.CompletedPassStats.HomeTotalYards, match.CompletedPassStats.AwayTotalYards);
        Pair("TD", match.TouchdownStats.HomeTouchdowns, match.TouchdownStats.AwayTouchdowns);
        Pair("safeties", match.ScoringStats.HomeSafeties, match.ScoringStats.AwaySafeties);
        Pair("twoAttempts", match.ScoringStats.HomeTwoPointAttempts, match.ScoringStats.AwayTwoPointAttempts);
        Pair("twoMade", match.ScoringStats.HomeTwoPointConversions, match.ScoringStats.AwayTwoPointConversions);
        Pair("interceptions", match.Interceptions.HomeInterceptionsMade, match.Interceptions.AwayInterceptionsMade);
        Pair("fumbles", match.PlayActivityStats.HomeFumbles, match.PlayActivityStats.AwayFumbles);
        Pair("lostFumbles", match.PlayActivityStats.HomeLostFumbles, match.PlayActivityStats.AwayLostFumbles);
        Pair("tackles", match.PlayActivityStats.HomeTackles, match.PlayActivityStats.AwayTackles);
        return result;
    }

    private static object? InvokeGame(PlayerHost host, string method, params object?[] arguments)
        => typeof(GameSimulation).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host.Game, arguments);

    private static void ActorSourcePosition(object actor, int field, int side)
    {
        Type driver = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!;
        driver.GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new[] { actor, (object)new SourceVector3(SourceMatchRules.WorldPosition(field, side), 0, 0) });
        Set(actor, "SourceVelocity", default(FieldVector));
        Set(actor, "SourceVerticalVelocity", 0f);
    }

    private static void AccountingSourceSample(PlayerHost host, int station = -1, int button = 0, int x = 0, int y = 0)
    {
        long tick = host.Game.SourceTick;
        var axes = new double[8]; var buttons = new int[4];
        if (station >= 0) { axes[station * 2] = x; axes[station * 2 + 1] = y; buttons[station] = button; }
        do { host.Capture(axes, buttons); Render(host, 1d / 60); } while (host.Game.SourceTick == tick);
        Check(host.Game.SourceTick == tick + 1, "The accounting starter must execute exactly one source wake.");
    }

    private static void AccountingActorSample(PlayerHost host, int station, int button = 0, int x = 0, int y = 0)
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            AccountingSourceSample(host, station, button, x, y);
            int actor = Match(host).Stations.Single(value => value.StationId == station).ControlledPlayerId;
            var process = ((ISourcePossessionState)host.Game.State).Processes.Single(
                value => value.Function == "player_proc" && value.RunLevel == 0x300 + actor);
            if (process.LastWakeTick == host.Game.SourceTick) return;
        }
        throw new InvalidOperationException("The real accounting starter's controlled player_proc never woke.");
    }

    private static void StartAccountingLivePlay(PlayerHost host, bool punt)
    {
        var selection = (SourceCabinetPlaySelection)typeof(PlayerHost)
            .GetField("selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        bool selectedPunt = false;
        for (int tick = 0; tick < 3000 && !(host.Game.State.Phase == GamePhase.PreSnap &&
            Match(host).PlayKind == SourcePlayKind.Normal); tick++)
        {
            int captain = SourceCabinetStations.Captain(Match(host), host.Game.State.Possession);
            if (punt && host.Game.State.Phase == GamePhase.PlaySelect && captain >= 0 && !selectedPunt)
            {
                for (int wait = 0; wait < 24; wait++) AccountingSourceSample(host);
                for (int wait = 0; wait < 600 &&
                    ((int)Get(selection, "menuSleepRemaining") > 0 || host.Session.ReadPlaySelectionInstructionGate() >= 10); wait++)
                    AccountingSourceSample(host);
                int page = -1, slot = -1;
                for (int p = 0; p < 4; p++) for (int s = 0; s < 9; s++)
                    if (selection.Slot(host.Game.State, host.Game.State.Possession, p, s).PlaySymbol == "play_punt" && page < 0)
                    { page = p; slot = s; }
                Check(page >= 0, "The actual source selector must expose canonical punt.");
                var previous = host.Game.SourceStationInputTransform!;
                bool sent = false;
                host.Game.SourceStationInputTransform = (state, sourceTick, frame) =>
                {
                    if (!sent && !frame.PlaySelectionContinuationBlocked)
                    {
                        var inputs = frame.Stations.Select(input => input.StationId != captain ? input :
                            new SourceStationInput(input.StationId, input.StickX, input.StickZ, input.Held,
                                input.Pressed, input.Released, new SourceCommand(SourceAction.SelectPlay,
                                    new SourcePlaySelection("play_punt", SourcePlaybookKind.Offense, slot, page)),
                                input.DigitalCurrent)).ToArray();
                        var typed = new SourceInputFrame(inputs, frame.PhysicalSwitches);
                        typed = (SourceInputFrame)typeof(SourceInputFrame).GetMethod("WithSelectionSoundProducer",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(typed,
                                new object?[] { frame.SelectionSoundProducer })!;
                        frame = typed; sent = true;
                    }
                    return previous(state, sourceTick, frame);
                };
                try
                {
                    for (int inputWake = 0; inputWake < 80 && host.Game.State.Phase == GamePhase.PlaySelect; inputWake++)
                        AccountingSourceSample(host);
                }
                finally { host.Game.SourceStationInputTransform = previous; }
                Check(sent, "The public typed source punt selection must enter an actual game_proc input continuation.");
                selectedPunt = true;
                continue;
            }
            int[] buttons = new int[4];
            if (host.Game.State.Phase == GamePhase.PlaySelect && tick % 8 < 3)
                foreach (TeamId team in new[] { TeamId.Home, TeamId.Away })
                {
                    int station = SourceCabinetStations.Captain(Match(host), team);
                    if (station >= 0) buttons[station] = 2;
                }
            host.Capture(new double[8], buttons); Render(host, 1d / 60);
        }
        Check(host.Game.State.Phase == GamePhase.PreSnap, "Authentic normal selection must precede the accounting play.");
        int qb = 7 * (int)host.Game.State.Possession + 3;
        int owner = SourceCabinetStations.Captain(Match(host), host.Game.State.Possession);
        Check(owner >= 0 && Match(host).Stations.Single(value => value.StationId == owner).ControlledPlayerId == qb,
            "Use the actual captain/controlled QB, never a hardcoded physical station.");
        while (((ISourceGameState)host.Game.State).PreSnapTicksRemaining >= 228) AccountingSourceSample(host);
        AccountingActorSample(host, owner, 1);
        for (int tick = 0; tick < 180 && host.Game.State.Ball.CarrierId != qb; tick++) AccountingSourceSample(host);
        Check(host.Game.State.Phase == GamePhase.LiveBall && host.Game.State.Ball.CarrierId == qb,
            "The original center ANI callback and actual QB catch must establish the live accounting starter.");
        AccountingActorSample(host, owner);
        if (punt)
        {
            for (int tick = 0; tick < 180 && (Get(Model(host), "SourceFlags") is not SourceGameFlags flags ||
                (flags & SourceGameFlags.BallPunted) == 0); tick++) AccountingSourceSample(host);
            Check(((SourceGameFlags)Get(Model(host), "SourceFlags") & SourceGameFlags.BallPunted) != 0,
                "The actual punt ANI frame callback, not a direct observer call, must release the ball.");
        }
    }

    private static void AccountingInput(PlayerHost host, AccountingCase item)
    {
        object model = Model(host);
        bool pass = item.Producer is "pass" or "interception" or "pat-interception";
        bool punt = item.Producer == "punt";
        int down = item.Producer == "punt" ? 4 : item.Type == SourcePlayType.ExtraPoint ? -1 : 2;
        Method<Action<SourceRulesState>>(host, "SetSourceRules")(
            new SourceRulesState(item.Initial, item.Initial, down,
                item.Los, Math.Min(100, item.Los + 30), item.Type));
        Set(model, "SourceLastPossession", item.Initial);
        Set(model, "SourceLastResult", SourcePlayResult.Down);
        if (!punt) Set(model, "SourceFlags", SourceGameFlags.None);
        Set(model, "SourceForwardCatcher", -1);
        Set(model, "SourceClock", SourceClock.Create(0, firstWakeTick: host.Game.SourceTick + 1));
        Set(model, "QuarterEndEmitted", false);
        Set(model, "ClockActive", true); Set(model, "Phase", GamePhase.LiveBall);
        Set(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        if (!punt)
        {
            Set(model, "BallKind", SourceBallKind.Pass); Set(model, "BallMode", BallMode.Held);
            Set(model, "CarrierId", item.Initial * 7 + 3);
        }
        var actors = (Array)Get(model, "Players");
        if (pass)
        {
            int qb = item.Initial * 7 + 3, receiver = item.Initial * 7 + 4;
            object thrower = actors.GetValue(qb)!, target = actors.GetValue(receiver)!;
            for (int id = item.Initial * 7 + 4; id < item.Initial * 7 + 7; id++)
                ActorSourcePosition(actors.GetValue(id)!, item.Producer == "pass" ? item.End : item.Los + 10, item.Initial);
            int owner = SourceCabinetStations.Captain(Match(host), (TeamId)item.Initial);
            for (int wake = 0; wake < 80 &&
                ((SourceGameFlags)Get(model, "SourceFlags") & SourceGameFlags.PassThrown) == 0; wake++)
                AccountingActorSample(host, owner, wake % 3 == 0 ? 2 : 0, y: -1);
            Check(((SourceGameFlags)Get(model, "SourceFlags") & SourceGameFlags.PassThrown) != 0,
                item.Id + ": raw B must reach the genuine actor VM pass-release callback. Actual=" +
                JsonSerializer.Serialize(new {
                    sourceTick = host.Game.SourceTick, phase = Match(host).Phase.ToString(),
                    flags = Get(model, "SourceFlags").ToString(), carrier = Get(model, "CarrierId"),
                    mode = Get(thrower, "SourceMode").ToString(), role = Get(thrower, "Role").ToString(),
                    controller = Get(thrower, "SourceControllerStation"), bound = ((int[])Get(model, "SourceStationPlayers"))[0],
                    buttons = Get(thrower, "SourceButtons").ToString(), pressed = Get(thrower, "SourceNewButtons").ToString(),
                    pendingTarget = Get(thrower, "PendingThrowTarget"),
                    script = host.Game.State.Players.Single(player => player.Id == qb).Animation.Primary.ScriptSymbol
                }));
            for (int wake = 0; wake < 20 &&
                ((SourceGameFlags)Get(model, "SourceFlags") & SourceGameFlags.ForwardPass) == 0; wake++)
                AccountingSourceSample(host);
            Check(((SourceGameFlags)Get(model, "SourceFlags") & SourceGameFlags.ForwardPass) != 0,
                item.Id + ": the actual ball process must establish the forward-pass flag.");
        }
        else if (punt)
        {
            Check(((SourceGameFlags)Get(model, "SourceFlags") & SourceGameFlags.BallPunted) != 0,
                item.Id + ": the actual punt release must establish BallPunted.");
        }
        else if (item.Producer == "fumble")
        {
            object victim = actors.GetValue(item.Initial * 7 + 4)!;
            Set(model, "CarrierId", item.Initial * 7 + 4);
            ActorSourcePosition(victim, item.Los, item.Initial);
            Func<int, int> random = (Func<int, int>)InvokeGame(host, "SourceDrawFor", "player.c:fumble_ball")!;
            typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourceBallDriver")!
                .GetMethod("Fumble", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { model, victim,
                    new SourceVector3(SourceMatchRules.WorldPosition(item.Los, item.Initial), 15, 0), 0f, 0f, random });
        }
        foreach (object actor in actors) Set(actor, "SourceMode", SourceActorMode.Dead);
        Method<Action<SourceRulesState>>(host, "SetSourceRules")(
            new SourceRulesState(item.Side, item.Initial, down,
                item.Los, Math.Min(100, item.Los + 30), item.Type));
        Set(model, "CarrierId", item.Carrier);
        Set(model, "BallMode", item.Carrier >= 0 ? BallMode.Held : BallMode.InFlight);
        if (item.Carrier >= 0)
        {
            object carrier = actors.GetValue(item.Carrier)!;
            ActorSourcePosition(carrier, item.End, item.Side);
            Set(model, "SourceCatchX", SourceMatchRules.WorldPosition(20, item.Side));
            if (item.Producer == "punt")
                model.GetType().GetMethod("CaptureSourceKickReturnCatch", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(model, new object[] { false });
            Set(carrier, "SourceMode", SourceActorMode.Receiver);
            Set(model, "CatchSourceTick", host.Game.SourceTick - 17);
            Wakes(host, 2);
            if (item.Side != item.Initial)
            {
                for (int wait = 0; wait < 180; wait++)
                {
                    var gameProc = ((ISourcePossessionState)host.Game.State).Processes
                        .First(process => process.Function == "game_proc");
                    if (!(bool)Get(model, "SourceCameraPossessionContinuation") && !gameProc.Suspended &&
                        gameProc.LastWakeTick == host.Game.SourceTick) break;
                    AccountingSourceSample(host);
                }
                var resumed = ((ISourcePossessionState)host.Game.State).Processes
                    .First(process => process.Function == "game_proc");
                Check(!resumed.Suspended && !(bool)Get(model, "SourceCameraPossessionContinuation"),
                    item.Id + ": source cambot suspension must finish naturally before a later result can be queued.");
            }
            if (item.Producer == "tackle")
            {
                // Authored mirrored BodyTackleAndDown pair; keep the real Host clock,
                // camera, process/VM owners and native detector instead of calling Stop.
                Set(model, "SourceNoFumbles", true);
                object attacker = actors.GetValue(4)!;
                foreach (object other in actors)
                {
                    Set(other, "SourceRawFlags", (int)Get(other, "SourceRawFlags") | 0x20);
                    Set(other, "SourceVelocity", default(FieldVector));
                }
                foreach (object participant in new[] { carrier, attacker })
                {
                    Set(participant, "SourceRawFlags", (int)Get(participant, "SourceRawFlags") & ~0x20);
                    Set(participant, "Down", false); Set(participant, "SourcePuppetLinkId", -1);
                    Set(participant, "SourceHitPartnerId", null!);
                }
                Set(carrier, "Role", ActorRole.Receiver); Set(carrier, "SourceMode", SourceActorMode.Receiver);
                Set(attacker, "Role", ActorRole.Defender); Set(attacker, "SourceMode", SourceActorMode.DefensiveBack);
                foreach (string field in new[] { "Facing", "TargetFacing", "UpperFacing", "TargetUpperFacing" })
                { Set(carrier, field, 768); Set(attacker, field, 256); }
                ((int[])Get(model, "SourceStationPlayers"))[0] = 4;
                Method<Action>(host, "RecomputeSourceMovementStations")();
                Type driver = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!;
                MethodInfo position = driver.GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!;
                position.Invoke(null, new object[] { carrier, new SourceVector3(120, 0, 0) });
                position.Invoke(null, new object[] { attacker, new SourceVector3(111, 0, 0) });
                Set(model, "SourceBallPosition", new SourceVector3(120, 14, 0));
                Set(model, "SourceBallTarget", new SourceVector3(120, 14, 0));
                Set(model, "CatchSourceTick", host.Game.SourceTick - 30);
                for (int wake = 0; wake < 180 && host.Game.State.Phase == GamePhase.LiveBall; wake++)
                    AccountingSourceSample(host);
                Check(host.Game.State.Phase == GamePhase.DeadBall,
                    item.Id + ": actual detector/contact ANI player_down must queue the result without direct Stop/End.");
                return;
            }
        }
        Set(model, "SourcePlayEndX", SourceMatchRules.WorldPosition(item.End, item.Side));
        Set(model, "SourcePlayEndZ", 0f);
        Set(model, "SourceClock", SourceClock.Create(0, firstWakeTick: host.Game.SourceTick + 1));
        Set(model, "ClockActive", true);
        Console.WriteLine(JsonSerializer.Serialize(new { statisticsBeforeEnd = item.Id,
            sourceTick = host.Game.SourceTick,
            gameProc = ((ISourcePossessionState)host.Game.State).Processes.First(process => process.Function == "game_proc"),
            cameraContinuation = Get(model, "SourceCameraPossessionContinuation"),
            processOperations = ((SourcePortableProcessHost)Get(model, "SourceProcesses")).Records }));
        Method<Action<SourcePlayEndCause, int, FieldVector, int, SourceVector3?>>(host, "EndSourcePlay")(
            item.Cause, item.Carrier, new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(item.End, item.Side)), 0), -1, null);
        object? pending = model.GetType().GetField("SourceC2PendingResult", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model);
        if (pending != null)
        {
            var input = (SourcePlayEndInput)Get(pending, "Input");
            Console.WriteLine(JsonSerializer.Serialize(new { statisticsEndingInput = item.Id,
                sourceTick = host.Game.SourceTick, queuedTick = Get(pending, "QueuedTick"),
                rawEndX = input.PlayEndX, rawEndXBits = BitConverter.SingleToInt32Bits(input.PlayEndX).ToString("x8"),
                los = input.State.LineOfScrimmage, offense = input.State.OffenseSide,
                initial = input.State.InitialOffenseSide, flags = input.Flags.ToString(),
                cause = input.Cause.ToString(), carrier = Get(model, "CarrierId") }));
        }
        Method<Action>(host, "Publish")();
        host.HudProcess.Observe(host.Game);
    }

    private static AccountingCase[] AccountingCases()
    {
        return new[] {
            new AccountingCase { Id = "completed-pass", Producer = "pass",
                // Original WORLDX(60) bits429bfffc rounds to FIELDX59, not60.
                Delta = { ["home.passes"] = 1, ["home.completions"] = 1, ["home.passing"] = 19, ["home.total"] = 19 } },
            new AccountingCase { Id = "pass-touchdown", Producer = "pass", Los = 90, End = 100, Home = 0,
                Cause = SourcePlayEndCause.CarrierTouchdown, Result = SourcePlayResult.Touchdown, HomeAdded = 6,
                Delta = { ["home.passes"] = 1, ["home.completions"] = 1, ["home.passing"] = 10,
                    ["home.total"] = 10, ["home.passTD"] = 1, ["home.TD"] = 1 } },
            new AccountingCase { Id = "rush-gain", End = 55,
                // Original WORLDX(55) bits421bfff8 rounds to FIELDX54.
                Delta = { ["home.rushing"] = 14, ["home.rushes"] = 1, ["home.total"] = 14 } },
            new AccountingCase { Id = "sack", Carrier = 3, End = 35,
                Delta = { ["away.sacks"] = 1, ["away.sackYards"] = 5 } },
            new AccountingCase { Id = "punt-return", Producer = "punt", Side = 1, Carrier = 11, End = 35,
                Result = SourcePlayResult.NewPossession,
                // Original WORLDX(side1,35) bits42e9fffc rounds to FIELDX36.
                Delta = { ["away.returns"] = 16, ["away.kickReturns"] = 1, ["away.total"] = 16 } },
            new AccountingCase { Id = "punt-oob", Producer = "punt", Carrier = -1,
                Cause = SourcePlayEndCause.BallOutOfBounds, Result = SourcePlayResult.PuntOutOfBounds },
            new AccountingCase { Id = "punt-return-touchdown", Producer = "punt", Side = 1, Carrier = 11,
                End = 100, Home = 0, AwayAdded = 6, Cause = SourcePlayEndCause.CarrierTouchdown,
                Result = SourcePlayResult.Touchdown, Delta = { ["away.TD"] = 1 } },
            new AccountingCase { Id = "interception", Producer = "interception", Side = 1, Carrier = 11, End = 35,
                Result = SourcePlayResult.NewPossession, Delta = { ["home.passes"] = 1, ["away.interceptions"] = 1 } },
            new AccountingCase { Id = "lost-fumble", Producer = "fumble", Side = 1, Carrier = 11, End = 35,
                Result = SourcePlayResult.NewPossession, Delta = { ["home.fumbles"] = 1, ["home.lostFumbles"] = 1 } },
            new AccountingCase { Id = "human-tackle", Producer = "tackle", Initial = 1, Side = 1, Carrier = 11, End = 35,
                Delta = { ["home.tackles"] = 1, ["away.rushing"] = -5, ["away.rushes"] = 1, ["away.total"] = -5 } },
            new AccountingCase { Id = "two-point-pass", Producer = "pass", Type = SourcePlayType.ExtraPoint,
                Los = 90, End = 100, Home = 6, Away = 7, HomeAdded = 2,
                Cause = SourcePlayEndCause.CarrierTouchdown, Result = SourcePlayResult.Conversion,
                Delta = { ["home.passes"] = 1, ["home.completions"] = 1, ["home.passing"] = 10, ["home.total"] = 10,
                    ["home.twoAttempts"] = 1, ["home.twoMade"] = 1 } },
            new AccountingCase { Id = "failed-two-point-rush", Type = SourcePlayType.ExtraPoint,
                Los = 90, End = 95, Home = 7, Away = 6, Result = SourcePlayResult.FailedConversion,
                Delta = { ["home.rushing"] = 5, ["home.rushes"] = 1, ["home.total"] = 5, ["home.twoAttempts"] = 1 } },
            new AccountingCase { Id = "safety", End = 0, AwayAdded = 2,
                Cause = SourcePlayEndCause.CarrierDownInOwnEndZone, Result = SourcePlayResult.Safety,
                Delta = { ["home.rushing"] = -39, ["home.rushes"] = 1, ["home.total"] = -39, ["home.safeties"] = 1 } },
            new AccountingCase { Id = "defensive-pat-safety", Producer = "pat-interception", Type = SourcePlayType.ExtraPoint,
                Side = 1, Carrier = 11, Los = 90, End = 0, Home = 7, Away = 6, HomeAdded = 1,
                Cause = SourcePlayEndCause.CarrierDownInOwnEndZone, Result = SourcePlayResult.PatSafety,
                Delta = { ["home.passes"] = 1, ["away.interceptions"] = 1, ["home.safeties"] = 1 } },
            new AccountingCase { Id = "touchdown-free-pat", Producer = "pass", Los = 90, End = 100, Home = 1, Away = 7,
                Cause = SourcePlayEndCause.CarrierTouchdown, Result = SourcePlayResult.Touchdown, HomeAdded = 7, FreePat = true,
                Delta = { ["home.passes"] = 1, ["home.completions"] = 1, ["home.passing"] = 10,
                    ["home.total"] = 10, ["home.passTD"] = 1, ["home.TD"] = 1 } },
            new AccountingCase { Id = "terminal-third-ot-tie", Quarter = 6, Home = 3, Away = 3, End = 40,
                Delta = { ["home.rushes"] = 1 } },
        };
    }

    private static async Task VerifyStatisticsToCareer(PlayerData data)
    {
        string? selected = Environment.GetEnvironmentVariable("SOURCE_RECORD_STAT_CASE");
        var cases = AccountingCases().Where(item => selected == null || item.Id == selected).ToArray();
        Check(cases.Length > 0, "A statistics selector must execute a real named case.");
        foreach (AccountingCase item in cases)
        {
            accountingCase = item.Id;
            var storage = new OwnedStorage();
            var seed = new SourcePlayerRecordStore(storage);
            await seed.LoadAsync(); await seed.InitializeNewAsync(); await seed.CreateAsync(Identity);
            var (host, _, store) = await Host(data, storage, kickMeters: item.Producer == "punt");
            BeginNames(host); Enter(host, Name, Pin);
            TeamsToGame(host, receiveHome: true); FinishActualKickoff(host);
            for (int wake = 0; wake < 6000 && host.Game.State.Phase != GamePhase.PlaySelect; wake++)
                Wakes(host, 1, 7);
            Check(host.Game.State.Phase == GamePhase.PlaySelect, "The public first kickoff must reach actual normal selection.");
            object model = Model(host);
            Set(model, "Quarter", item.Quarter + 1); Set(model, "SourcePeriodView", new SourcePeriodView(item.Quarter));
            Set(model, "HomeScore", item.Home); Set(model, "AwayScore", item.Away);
            var scores = (int[,])Get(model, "SourceQuarterScores");
            Array.Clear(scores); scores[item.Quarter, 0] = item.Home; scores[item.Quarter, 1] = item.Away;
            Method<Action<SourceRulesState>>(host, "SetSourceRules")(
                new SourceRulesState(0, 0, item.Producer == "punt" ? 4 : 2, item.Los, Math.Min(100, item.Los + 30),
                    SourcePlayType.Normal));
            Set(model, "SourceClock", SourceClock.Create(0x00020000, firstWakeTick: host.Game.SourceTick + 1));
            Method<Action>(host, "Publish")();
            StartAccountingLivePlay(host, item.Producer == "punt");
            var before = Accounting(Match(host));
            int writes = storage.Writes;
            storage.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actualEvents = new List<SourceGameEvent>();
            host.Bridge.SourceEventRaised += source => actualEvents.Add(source);
            AccountingInput(host, item);
            var events = new List<string>();
            for (int wake = 0; wake < 6000 && Flow(host).Stage != SourceCabinetFlowStage.RecordCommitWait; wake++)
            {
                foreach (SourceGameEvent source in host.Game.SourceEvents)
                    if (events.Count == 0 || events[^1] != source.SourceTick + ":" + source.Type)
                        events.Add(source.SourceTick + ":" + source.Type);
                if (item.FreePat && Match(host).Phase == SourceMatchPhase.PatSelection)
                    AccountingSourceSample(host, SourceCabinetStations.Captain(Match(host), host.Game.State.Possession),
                        wake % 4 == 0 ? 2 : 0);
                else Wakes(host, 1, 7);
            }
            Check(Flow(host).Stage == SourceCabinetFlowStage.RecordCommitWait,
                item.Id + ": the real game_proc must reach its final record barrier, actual=" + Flow(host).Stage +
                "/" + Match(host).Phase + "/" + Match(host).Period.QuarterIndex);
            SourceMatchView final = Flow(host).FinalMatch;
            var observed = Accounting(final);
            var expected = before.ToDictionary(pair => pair.Key,
                pair => pair.Value + (item.Delta.TryGetValue(pair.Key, out int delta) ? delta : 0), StringComparer.Ordinal);
            Console.WriteLine(JsonSerializer.Serialize(new { statisticsFinalObservation = item.Id,
                expected, observed, result = final.Result.Kind.ToString(), sourceTick = host.Game.SourceTick }));
            foreach (var pair in expected)
                Check(observed[pair.Key] == pair.Value, item.Id + ": " + pair.Key +
                    " expected=" + pair.Value + " actual=" + observed[pair.Key]);
            Check(actualEvents.Any(source => source.Type == SourceEventType.PlayResolved &&
                source.Result.IsSpecified && source.Result.Kind == (SourceResultKind)item.Result),
                item.Id + ": the actual committed play event must carry the classified result before PAT/period cleanup.");
            Check(Flow(host).FinalHomeScore == item.Home + item.HomeAdded &&
                Flow(host).FinalAwayScore == item.Away + item.AwayAdded &&
                final.QuarterScores.Count == item.Quarter + 1 &&
                store.Current.Find(Identity).Career.GamesPlayed == 0 && storage.Writes == writes + 1,
                item.Id + ": points/period and pending no-speculative-career boundary.");
            storage.Hold.SetResult(); await Settled(host); storage.Hold = null; Wakes(host, 2);
            SourceRecordCareer career = store.Current.Find(Identity).Career;
            Check(career.Intercepted == expected["away.interceptions"],
                item.Id + ": career.Intercepted expected=" + expected["away.interceptions"] + " actual=" + career.Intercepted);
            Check(career.GamesPlayed == 1 && career.PointsScored == item.Home + item.HomeAdded &&
                career.PointsAllowed == item.Away + item.AwayAdded &&
                career.GamesWon == (item.Home + item.HomeAdded > item.Away + item.AwayAdded ? 1 : 0) &&
                career.PassingYards == expected["home.passing"] && career.RushingYards == expected["home.rushing"] &&
                career.PassAttempts == expected["home.passes"] && career.PassCompletions == expected["home.completions"] &&
                career.PassTouchdowns == expected["home.passTD"] && career.Intercepted == expected["away.interceptions"] &&
                career.Sacks == expected["home.sacks"] && career.Tackles == expected["home.tackles"] &&
                store.Current.CommittedMatches.Values.Single() == 1 && storage.Writes == writes + 1,
                item.Id + ": actual final/CAS must credit exact source counters once, including opposite interceptions.");
            int margin = item.Home + item.HomeAdded - item.Away - item.AwayAdded;
            int[] worldValues = { expected["home.sacks"], expected["home.TD"], expected["home.tackles"],
                expected["home.interceptions"], item.Home + item.HomeAdded, margin };
            for (int category = 0; category < worldValues.Length; category++)
            {
                SourceWorldRecord world = store.Current.WorldRecords[category];
                Check(category == (int)SourceWorldRecordCategory.VictoryMargin && margin <= 0 ? world == null :
                    world != null && world.Value == worldValues[category] && world.Holder.Equals(Identity),
                    item.Id + ": actual source world category " + category + " must commit with the same final.");
            }
            var reload = new SourcePlayerRecordStore(storage); await reload.LoadAsync();
            SourceRecordCareer loaded = reload.Current.Find(Identity).Career;
            Check(loaded.PassingYards == career.PassingYards && loaded.RushingYards == career.RushingYards &&
                loaded.PassAttempts == career.PassAttempts && loaded.PassCompletions == career.PassCompletions &&
                loaded.PassTouchdowns == career.PassTouchdowns && loaded.Sacks == career.Sacks &&
                loaded.Tackles == career.Tackles && loaded.Intercepted == career.Intercepted &&
                loaded.PointsScored == career.PointsScored && loaded.PointsAllowed == career.PointsAllowed &&
                loaded.GamesPlayed == 1 && loaded.GamesWon == career.GamesWon &&
                reload.Current.WorldRecords[(int)SourceWorldRecordCategory.Touchdowns].Value == expected["home.TD"] &&
                reload.Current.CommittedMatches.Values.Single() == 1,
                item.Id + ": actual serialized career and receipt must reload together.");
            Console.WriteLine(JsonSerializer.Serialize(new { statisticsToCareerCase = item.Id, status = "passed",
                directedStateFixture = true, privateStatisticCounterAssignment = false,
                producer = item.Producer, sourceTick = host.Game.SourceTick, expected, observed, events,
                finalHome = Flow(host).FinalHomeScore, finalAway = Flow(host).FinalAwayScore,
                periods = final.QuarterScores.Count, realGameProcCommit = store.Notification.Code }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { statisticsToCareer = "passed", cases = cases.Length, assertions }));
    }

    private static void VerifyOriginalHighScoreProjection()
    {
        var a = new SourcePlayerRecord(0, new SourceRecordIdentity("ALPHA", "1234"),
            new SourceRecordCareer(gamesPlayed: 4, gamesWon: 1, pointsAllowed: 4, passAttempts: 1,
                passCompletions: 10, passingYards: 500, passTouchdowns: 10));
        var b = new SourcePlayerRecord(1, new SourceRecordIdentity("BETA", "5678"),
            new SourceRecordCareer(gamesPlayed: 4, gamesWon: 3, pointsAllowed: 4));
        Check(SourceRecordHighScores.Compare(a, b, SourceRecordHighScoreCategory.PointsAllowed) < 0 &&
            Math.Abs(SourceRecordHighScores.PassRating(a.Career) - 6252.0835f) < 0.01f,
            "The actual source score projection must retain ascending defensive win tie-breaks and unbounded PMISC QB arithmetic.");
        var inexperienced = new SourcePlayerRecord(2, new SourceRecordIdentity("GAMMA", "9012"),
            new SourceRecordCareer(gamesPlayed: 3, gamesWon: 3));
        var fresh = new SourcePlayerRecord(3, new SourceRecordIdentity("DELTA", "3456"), new SourceRecordCareer());
        Check(SourceRecordHighScores.Compare(inexperienced, fresh, SourceRecordHighScoreCategory.GamesWon) == 0 &&
            SourceRecordHighScores.Compare(inexperienced, a, SourceRecordHighScoreCategory.GamesWon) > 0,
            "The native four-game qualification must precede score metrics and leave exact unqualified ties unordered.");
    }

    private static async Task VerifyOwnedSourceMenuDriver(PlayerData data)
    {
        var storage = new OwnedStorage();
        var store = new SourcePlayerRecordStore(storage);
        await store.LoadAsync();
        var session = new SourceCabinetSession(data, recordStore: store);
        var random = SourcePreGameSoundService.CreateManagedPreviewRandom();
        var source = new SourcePreGameSoundService(SourcePreGameSoundEntry.FromOriginalPermanentBanksAfterAcceptedStart(), random, session);
        session.BindPreGameInput(source.CapturePhysical);
        var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
        var sounds = new List<SourceProcessSoundCall>();
        var controls = new List<SourceProcessSoundControl>();
        int button = 0, x = 0, y = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                session.Capture(new[] { (double)x, y, 0, 0, 0, 0, 0, 0 }, new[] { button, 0, 0, 0 }, quantizer);
                source.AdvanceHostTick();
                source.TryTakeEntryActivation(out _);
                if (source.OriginalEntryActivated)
                {
                    sounds.AddRange(source.TakeSourceProcessSoundCalls());
                    controls.AddRange(source.TakeSourceSoundControls());
                }
                Thread.Yield();
            }
        }
        void Press(int a = 0, int horizontal = 0, int vertical = 0)
        {
            button = x = y = 0; Tick(3);
            button = a; x = horizontal; y = vertical; Tick(1);
            button = x = y = 0; Tick(65);
        }
        void Select(int target, bool pin = false)
        {
            int Cursor() => session.Records.View().Stations.Single().Cursor;
            int columns = pin ? 3 : 4;
            while (Cursor() / columns > target / columns) Press(vertical: -1);
            while (Cursor() / columns < target / columns) Press(vertical: 1);
            while (Cursor() > target) Press(horizontal: -1);
            while (Cursor() < target) Press(horizontal: 1);
            Press(1);
        }
        Tick(3); Press(4); Press(1); Press(vertical: -1); Press(1);
        foreach (char c in "AAAAAA") Select(c - 'A');
        foreach (char c in "1111") Select(c - '1', true);
        Check(session.Records.View().Stations.Single().Step == "Missing" && storage.Writes == 0,
            "The genuine57Hz source driver must finish PIN flash/lookup before showing explicit missing-record creation.");
        Press(1);
        for (int tick = 0; tick < 1000 && !session.Records.PendingOperation.IsCompleted; tick++) { Tick(1); await Task.Yield(); }
        await session.Records.PendingOperation;
        Check(session.Records.View().Stations.Single().Step == "Selected" && storage.Writes == 2 &&
            sounds.All(sound => sound.SourceTick > 0 && sound.ProcessSerial > 0) &&
            sounds.Any(sound => sound.PlaylistId == 28 && sound.Operand.Contains("4042", StringComparison.Ordinal)) &&
            sounds.All(sound => sound.PlaylistId != 80),
            "Actual original player_cursor/YES helper output must own positive source/process lineage; new creation must never fake RecordFound80.");
        Press(1); Press(1); Press(1); Press(vertical: 1); Press(1); Press(1);
        Press(vertical: -1); Press(1); Press(vertical: 1); Press(vertical: 1); Press(vertical: 1); Press(1);
        Check(session.Records.View().Kind == SourceRecordScreenKind.Route &&
            sounds.Any(sound => sound.PlaylistId == 95 && sound.Operand.Contains("lcb_create_edit", StringComparison.Ordinal)),
            "The same genuine menu clock must reach real CAP helpers. Actual screen=" + session.Records.View().Kind +
            " phase=" + source.ActivePhase + " calls=" + string.Join(",", sounds.TakeLast(8).Select(sound => sound.Operand + "/" + sound.PlaylistId)));
        Console.WriteLine(JsonSerializer.Serialize(new { ownedSourceMenuDriver = "passed", source.SourceTick,
            hostTicks = source.HostTicks, calls = sounds.Count, controls = controls.Count, privateDataLogged = false }));
    }

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2 || args.Length == 2 && args[1] != "--terminal-only" &&
                args[1] != "--natural-full-match" && args[1] != "--source-menu-driver" && args[1] != "--observed-resume" &&
                args[1] != "--statistics-to-career")
                throw new ArgumentException("Supply the explicit data root and optional focused selector.");
            terminalOnly = args.Length == 2 && args[1] == "--terminal-only";
            naturalFullMatch = args.Length == 2 && args[1] == "--natural-full-match";
            observedResumeOnly = args.Length == 2 && args[1] == "--observed-resume";
            string root = Path.GetFullPath(args[0]);
            var catalog = new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
                path => File.ReadAllBytes(Path.Combine(root, path)));
            var data = new PlayerData(catalog);
            if (args.Length == 2 && args[1] == "--statistics-to-career")
            { await VerifyStatisticsToCareer(data); return 0; }
            if (!observedResumeOnly) VerifyOriginalHighScoreProjection();
            if (args.Length == 2 && args[1] == "--source-menu-driver")
            { await VerifyOwnedSourceMenuDriver(data); return 0; }
            if (!observedResumeOnly) await VerifyGuestSpacesDuplicatesAndRecovery(data);
            await VerifyEditorAndFinal(data);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions,
                profile = data.Catalog.Profile.StableId,
                actualHost = true, sourceHud = true, actualBackendStore = true,
                selectionProof = observedResumeOnly ? "genuine public named menu and fresh DEFAULT normal play; native ready QB before physical snap" :
                    terminalOnly ? "not run in terminal-only fixture subset" : "ordinary public cabinet controls to saved dynamic formation/routes",
                finalProof = observedResumeOnly ? "not run in focused actual Host causal pause/resume selector" :
                    naturalFullMatch ? "real named regulation/final commit and new-store reload, public input only" :
                    "explicitly labelled terminal fixture; real game_proc I/O barrier, CAS failure/retry, final once/rematch",
                storage = "owned synthetic memory CAS; real IndexedDB browser proof is separate" }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
