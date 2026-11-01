using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourcePreGameSoundActivation
    {
        public SourcePreGameSoundEntry Entry { get; }
        public long SourceTick { get; }
        public long InputEpoch { get; }
        public int ParticipatingStationMask { get; }
        internal SourcePreGameSoundActivation(SourcePreGameSoundEntry entry, long tick, long epoch, int mask)
        { Entry = entry; SourceTick = tick; InputEpoch = epoch; ParticipatingStationMask = mask; }
    }

    public sealed class SourcePreGameSoundHandoff
    {
        private readonly Func<int, ISourceRandom> factory;
        private readonly Action<ISourceRandom, long> observeClock;
        private bool factoryTaken;
        private ISourceRandom currentRandom;
        private GameSimulation currentGame;
        private long observedGameTick = -1;
        public long SourceTick { get; }
        public long LastOutputSequence { get; }
        public ReadOnlyCollection<string> LoadedBanks { get; }
        public uint ReservedTracks { get; }
        public SourceVersusSoundBankState BankState { get; private set; }
        public string CurrentCursorBank { get { return BankState.CurrentCursorBank; } }
        public string Boundary { get { return "vs_screen:1127 sleep returned, before original sound-load block1140"; } }

        internal SourcePreGameSoundHandoff(long tick, long last, ReadOnlyCollection<string> banks,
            uint reserved, Func<int, ISourceRandom> factory, Action<ISourceRandom, long> observeClock)
        {
            SourceTick = tick; LastOutputSequence = last; LoadedBanks = banks; ReservedTracks = reserved;
            this.factory = factory; this.observeClock = observeClock;
            BankState = new SourceVersusSoundBankState("cursors", tick);
        }

        public Func<int, ISourceRandom> CreateGameRandomFactory()
        {
            if (factoryTaken) throw new InvalidOperationException("The pregame RNG handoff has already been claimed.");
            factoryTaken = true;
            return seed => {
                if (currentGame != null) ObserveGameClock(currentGame);
                currentRandom = factory(seed); currentGame = null; observedGameTick = -1;
                return currentRandom;
            };
        }

        public void ObserveGameClock(GameSimulation game)
        {
            if (game == null || currentRandom == null || !game.OwnsPreGameRandom(currentRandom)
                || currentGame != null && !ReferenceEquals(currentGame, game))
                throw new InvalidOperationException("Observe the actual Game using this exact handed-off random domain.");
            if (game.SourceTick < observedGameTick)
                throw new InvalidOperationException("Prepare the actual game reset before replacing its source clock.");
            currentGame = game; observedGameTick = game.SourceTick;
            observeClock(currentRandom, game.SourceTick);
            BankState = new SourceVersusSoundBankState(game.SourceCursorSoundBank, game.SourceTick);
        }

        public void PrepareGameReset(GameSimulation game)
        {
            ObserveGameClock(game);
            currentGame = null;
        }
    }

    // Audio/input continuations of the pinned record/CAP helpers. Rendering and
    // record storage stay in the driver; its private data never enters this host.
    public sealed class SourcePreGameSoundService
    {
        private enum Kind { Parent, Options, Cursor, Cap, CapExit, Welcome, Timer }
        private sealed class Process
        {
            internal long Serial;
            internal Kind Kind;
            internal int Station, Stage, Wakes, Seconds = 9, TimerStage, TimerTicks, OldMask;
            internal bool WasBusy, FoundEmitted;
            internal long YesNoEntry = -1;
        }
        private readonly struct Capture
        {
            internal readonly SourcePhysicalSwitchFrame Frame;
            internal readonly long Sequence;
            internal Capture(SourcePhysicalSwitchFrame frame, long sequence) { Frame = frame; Sequence = sequence; }
        }

        private sealed class RandomOwner
        {
            private readonly ISourceRandom source;
            private long lastTick = -1;
            internal RandomOwner(ISourceRandom source) { this.source = source; }
            internal SourceRandomProvenance Provenance { get { return source.Provenance; } }
            internal SourceRandomDraw Draw(SourceRandomSite site)
            {
                SourceRandomDraw draw = source.Draw(site);
                lastTick = Math.Max(lastTick, site.SourceTick);
                return draw;
            }
            internal ISourceRandom BeginGameDomain(long pregameTick)
            {
                return new GameRandom(this, checked(Math.Max(lastTick, pregameTick) + 1));
            }
            internal void ObserveGameClock(ISourceRandom domain, long tick)
            {
                GameRandom game = domain as GameRandom;
                if (game == null || !ReferenceEquals(game.owner, this) || tick < 0)
                    throw new InvalidOperationException("Only the actual handed-off game can advance its random trace clock.");
                lastTick = Math.Max(lastTick, checked(game.origin + tick));
            }
            private sealed class GameRandom : ISourceRandom
            {
                internal readonly RandomOwner owner;
                internal readonly long origin;
                public SourceRandomProvenance Provenance { get { return owner.Provenance; } }
                internal GameRandom(RandomOwner owner, long origin) { this.owner = owner; this.origin = origin; }
                public SourceRandomDraw Draw(SourceRandomSite site)
                {
                    var absolute = new SourceRandomSite(site.Kind, site.BoundOrModulus, checked(origin + site.SourceTick),
                        site.ActorId, site.Process, site.ProcessOrder, site.CallIndex, site.Profile, site.SourceIdentity);
                    SourceRandomDraw actual = owner.Draw(absolute);
                    return new SourceRandomDraw(site, actual.Raw, actual.Order, actual.Provenance);
                }
            }
        }

        private readonly SourcePreGameSoundEntry entry;
        private readonly ISourcePreGameMenuDriver driver;
        private readonly RandomOwner random;
        private readonly SourcePortableProcessHost processes = new SourcePortableProcessHost();
        private readonly Dictionary<long, Process> bodies = new Dictionary<long, Process>();
        private readonly Queue<Capture> physical = new Queue<Capture>();
        private readonly List<SourceProcessSoundCall> calls = new List<SourceProcessSoundCall>();
        private readonly List<SourceProcessSoundControl> controls = new List<SourceProcessSoundControl>();
        private SourcePhysicalSwitchFrame captured = SourcePhysicalSwitchFrame.Neutral();
        private SourcePhysicalSwitchFrame held = SourcePhysicalSwitchFrame.Neutral();
        private SourcePhysicalSwitchFrame prior1 = SourcePhysicalSwitchFrame.Neutral();
        private SourcePhysicalSwitchFrame prior2 = SourcePhysicalSwitchFrame.Neutral();
        private SourceConsumedPhysicalSwitchScan scan;
        private ReadOnlyCollection<SourceDigitalSwitchScan> directions;
        private SourcePreGameMenuWake executing;
        private SourcePreGameMenuPhase phase = SourcePreGameMenuPhase.Joining;
        private SourcePreGameSoundActivation activation;
        private Process caller;
        private long nextSerial, nextCapture, heldCapture, nextOutput;
        private long parent, option, timer, cap, capExit, transitionReadyTick = -1;
        private int remainder, dispatchOrder, currentOrder, subcall, delay;
        private bool sampled, insideTick, activationTaken, handoffTaken, firstIntro;
        private bool forceSelection, capExitRequested;
        public long SourceTick { get; private set; }
        public long HostTicks { get; private set; }
        public long InputEpoch { get; private set; } = 1;
        public bool Active { get; private set; } = true;
        public string Failure { get; private set; }
        public int PendingPhysicalSamples { get { return physical.Count; } }
        public SourcePreGameMenuPhase ActivePhase { get { return phase; } }
        public bool OriginalEntryActivated { get { return activation != null; } }
        public bool ReadyForGame { get { return transitionReadyTick >= 0 && SourceTick >= transitionReadyTick; } }
        public SourceConsumedPhysicalSwitchScan ConsumedSwitches { get { return scan; } }
        public ReadOnlyCollection<SourceProcessView> Processes { get { return processes.Processes; } }
        public ReadOnlyCollection<SourcePossessionProcessRecord> ProcessRecords { get { return processes.Records; } }
        public SourceRandomProvenance RandomProvenance { get { return random.Provenance; } }

        public SourcePreGameSoundService(SourcePreGameSoundEntry entry, ISourceRandom sourceRandom,
            ISourcePreGameMenuDriver driver)
        {
            this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
            random = new RandomOwner(sourceRandom ?? throw new ArgumentNullException(nameof(sourceRandom)));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public static ISourceRandom CreateManagedPreviewRandom(int seed = 1)
        {
            uint state = unchecked((uint)seed) ^ 0x9e3779b9u;
            return new SourceInjectedRandom(site => {
                state = unchecked(state * 1664525u + 1013904223u);
                return (int)(state >> 8);
            }, new SourceRandomProvenance("OriginalRelease57Hz/preview",
                "Explicit legacy LCG preview provider; NOT the historical platform random()/rand() body",
                "One portable cabinet stream initialized at pregame entry(" + seed + "); never reseeded at VS",
                SourceRandomEvidence.ExternalUnverified));
        }

        public static ISourceRandom CreatePortableRandom(int seed = 1)
        {
            return SourceRandom.CreatePortableLibrary(seed,
                "One portable cabinet runtime initialized at pregame entry(" + seed + "); retained across VS and rematches");
        }

        public void CapturePhysical(SourcePhysicalSwitchFrame frame)
        {
            Healthy();
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (insideTick) throw new InvalidOperationException("Capture cannot reenter an executing source menu wake.");
            if (sampled && captured.SameCurrent(frame)) return;
            if (physical.Count >= 1024) throw new InvalidOperationException("Source pregame physical FIFO overflow; no edge was discarded.");
            sampled = true; captured = frame;
            physical.Enqueue(new Capture(frame, checked(++nextCapture)));
        }

        public void SetActive(bool active)
        {
            Healthy();
            if (insideTick) throw new InvalidOperationException("Pause cannot reenter a source menu wake.");
            Active = active;
        }

        public void ClearPendingInput()
        {
            Healthy();
            if (insideTick) throw new InvalidOperationException("Input clearing cannot reenter a source menu wake.");
            physical.Clear();
            captured = held = prior1 = prior2 = SourcePhysicalSwitchFrame.Neutral();
            sampled = false; heldCapture = 0; scan = null;
            InputEpoch = checked(InputEpoch + 1);
        }

        public int AdvanceHostTick()
        {
            Healthy();
            if (insideTick) throw new InvalidOperationException("A source menu process cannot advance its own clock.");
            if (!Active || ReadyForGame) return 0;
            HostTicks = checked(HostTicks + 1);
            remainder += SourceTiming.DefaultTicksPerSecond;
            if (remainder < 60) return 0;
            if (!sampled) throw new InvalidOperationException("Source pregame needs its actual four physical inputs, not assumed neutral.");
            remainder -= 60;
            insideTick = true;
            try { Tick(); }
            catch { Failure = "Source pregame continuation failed; no replay or ordinal/RNG rewind is permitted."; throw; }
            finally { executing = null; caller = null; insideTick = false; }
            return 1;
        }

        public bool TryTakeEntryActivation(out SourcePreGameSoundActivation value)
        {
            if (insideTick) throw new InvalidOperationException("The source menu consumer cannot steal its entry activation.");
            if (activation == null || activationTaken) { value = null; return false; }
            activationTaken = true; value = activation; return true;
        }

        public ReadOnlyCollection<SourceProcessSoundCall> TakeSourceProcessSoundCalls()
        {
            RequireActivationDrain();
            var result = Array.AsReadOnly(calls.ToArray()); calls.Clear(); return result;
        }
        public ReadOnlyCollection<SourceProcessSoundControl> TakeSourceSoundControls()
        {
            RequireActivationDrain();
            var result = Array.AsReadOnly(controls.ToArray()); controls.Clear(); return result;
        }
        public bool TryTakeGameHandoff(out SourcePreGameSoundHandoff value)
        {
            Healthy();
            if (!ReadyForGame) { value = null; return false; }
            if (insideTick || calls.Count != 0 || controls.Count != 0 || !activationTaken)
                throw new InvalidOperationException("Deliver actual entry/canonical sound output before the VS handoff.");
            handoffTaken = true;
            value = new SourcePreGameSoundHandoff(SourceTick, nextOutput, entry.LoadedBanks, entry.ReservedTracks,
                seed => random.BeginGameDomain(SourceTick), random.ObserveGameClock);
            return true;
        }

        private void Tick()
        {
            SourceTick = checked(SourceTick + 1);
            processes.BeginTick(SourceTick);
            if (physical.Count != 0)
            {
                Capture sample = physical.Dequeue(); held = sample.Frame; heldCapture = sample.Sequence;
            }
            scan = new SourceConsumedPhysicalSwitchScan(SourceTick, InputEpoch, held, prior1, prior2);
            var digital = new SourceDigitalSwitchScan[4];
            for (int i = 0; i < 4; i++)
                digital[i] = new SourceDigitalSwitchScan(i, SourceTick, held.Station(i).DigitalCurrent.Value,
                    prior1.Station(i).DigitalCurrent.Value, prior2.Station(i).DigitalCurrent.Value);
            directions = Array.AsReadOnly(digital);
            prior2 = prior1; prior1 = held; dispatchOrder = 0;
            SourcePreGameMenuRoute route = ReadRoute();
            if (transitionReadyTick >= 0) return;
            if (activation == null && IsNative(route.Phase)) Activate(route);
            if (route.Phase == SourcePreGameMenuPhase.PortableManagement)
            {
                DispatchPortable(route);
                return;
            }
            if (activation == null || route.Phase == SourcePreGameMenuPhase.PortableManagement
                || route.Phase == SourcePreGameMenuPhase.Joining)
            {
                if (cap != 0) capExitRequested = true;
                if (cap == 0)
                {
                    DispatchPortable(route);
                    SourcePreGameMenuPhase after = ReadRoute().Phase;
                    if (after == SourcePreGameMenuPhase.PortableManagement || after == SourcePreGameMenuPhase.Joining) return;
                }
            }
            foreach (SourceProcessView process in processes.Processes)
            {
                Process body;
                if (!bodies.TryGetValue(process.Serial, out body) || !processes.Visit(process.Serial)) continue;
                caller = body; currentOrder = dispatchOrder++; subcall = delay = 0;
                try { Dispatch(body); }
                finally { executing = null; caller = null; processes.Complete(); }
            }

        }

        private SourcePreGameMenuRoute ReadRoute()
        { return driver.ReadRoute() ?? throw new InvalidOperationException("The source menu driver returned no actual state."); }
        private static bool IsNative(SourcePreGameMenuPhase value)
        { return value != SourcePreGameMenuPhase.Joining && value != SourcePreGameMenuPhase.PortableManagement; }
        private void Activate(SourcePreGameMenuRoute route)
        {
            if (route.Phase != SourcePreGameMenuPhase.Options)
                throw new InvalidOperationException("The original pregame entry starts with its actual option screen.");
            activation = new SourcePreGameSoundActivation(entry, SourceTick, InputEpoch, route.ParticipatingStationMask);
            parent = Create(Kind.Parent, 0x0c00, "pre_game_scrns", route.FocusStationId).Serial;
            phase = SourcePreGameMenuPhase.Options;
        }
        private Process Create(Kind kind, int id, string function, int station)
        {
            var body = new Process { Serial = checked(++nextSerial), Kind = kind, Station = station };
            bodies.Add(body.Serial, body);
            processes.Create(body.Serial, id, function, 0);
            return body;
        }
        private void Kill(long serial)
        { if (serial != 0) { processes.Kill(serial); bodies.Remove(serial); } }
        private void KillTimer() { Kill(timer); timer = 0; }

        private void Dispatch(Process body)
        {
            if (ReadRoute().Phase == SourcePreGameMenuPhase.PortableManagement) return;
            switch (body.Kind)
            {
                case Kind.Parent: Parent(body); break;
                case Kind.Options: Options(body); break;
                case Kind.Cursor: Cursor(body); break;
                case Kind.Cap: Cap(body); break;
                case Kind.CapExit: CapExit(body); break;
                case Kind.Welcome: Welcome(body); break;
                case Kind.Timer: Timer(body); break;
                default: throw new InvalidOperationException("Unknown source menu continuation.");
            }
        }

        private void Parent(Process body)
        {
            SourcePreGameMenuRoute route = ReadRoute();
            if (!firstIntro)
            {
                Call("lib/select/select.c", "pre_game_scrns", 1147, "intro", 9, 255, 0x8000);
                firstIntro = true;
                Create(Kind.Welcome, 0, "welcome_proc", -1);
                timer = Create(Kind.Timer, 601, "timedown", -1).Serial;
                option = Create(Kind.Options, 0, "plyr_select_option_proc", route.FocusStationId).Serial;
                return;
            }
            if (phase == SourcePreGameMenuPhase.Options || phase == SourcePreGameMenuPhase.CreatePlay) return;
            if (phase == SourcePreGameMenuPhase.PlayerEntry)
            {
                if (route.Phase != SourcePreGameMenuPhase.PlayerEntry)
                {
                    for (int i = 0; i < 4; i++)
                        foreach (Process child in new List<Process>(bodies.Values))
                            if (child.Kind == Kind.Cursor && child.Station == i) Kill(child.Serial);
                    KillTimer();
                    if (route.Phase == SourcePreGameMenuPhase.CreatePlay) { BeginPhase(route); return; }
                    phase = route.Phase;
                }
                else return;
            }
            if (route.Phase == SourcePreGameMenuPhase.MatchRequested)
            {
                if (phase != SourcePreGameMenuPhase.Versus)
                    throw new InvalidOperationException("VS handoff requires the real preceding versus menu.");
                transitionReadyTick = checked(SourceTick + 1);
                processes.Record("VersusSleep", body.Serial, 0x0c00, 1);
                processes.Sleep(1);
                return;
            }
            if (route.Phase == SourcePreGameMenuPhase.Teams || route.Phase == SourcePreGameMenuPhase.Rosters
                || route.Phase == SourcePreGameMenuPhase.Versus)
            {
                phase = route.Phase;
                DispatchDriver(body, route, -1, SourcePreGameMenuScreen.None, false);
            }
        }

        private void Options(Process body)
        {
            if (body.Stage == 1)
            {
                Call("lib/select/select.c", "plyr_select_option_proc", 2281, "cursors", 20, 255, 1);
                body.Stage = 2; processes.Sleep(48); return;
            }
            if (body.Stage == 2)
            {
                Kill(body.Serial); option = 0; KillTimer(); forceSelection = false;
                BeginPhase(ReadRoute()); return;
            }
            SourcePreGameMenuRoute route = ReadRoute();
            if (route.Phase == SourcePreGameMenuPhase.PortableManagement)
            { DispatchPortable(route); return; }
            if (route.Phase != SourcePreGameMenuPhase.Options)
            { body.Stage = 1; processes.Sleep(1); return; }
            body.Wakes++;
            DispatchDriver(body, route, route.FocusStationId, route.ScreenFor(route.FocusStationId), forceSelection);
            SourcePreGameMenuRoute after = ReadRoute();
            if (after.Phase != SourcePreGameMenuPhase.Options && after.Phase != SourcePreGameMenuPhase.PortableManagement)
            { body.Stage = 1; processes.Sleep(1); }
        }

        private void BeginPhase(SourcePreGameMenuRoute route)
        {
            phase = route.Phase;
            if (phase == SourcePreGameMenuPhase.PlayerEntry)
            {
                // select.c2423-2433 qcreates at the head; P4 is the first next-wake child.
                timer = Create(Kind.Timer, 601, "timedown", -1).Serial;
                bodies[timer].TimerTicks = 40 * SourceTiming.DefaultTicksPerSecond;
                for (int i = 0; i < 4; i++) Create(Kind.Cursor, 200 + 2 * i, "player_cursor", i);
            }
            else if (phase == SourcePreGameMenuPhase.CreatePlay)
            {
                foreach (Process child in new List<Process>(bodies.Values))
                    if (child.Kind == Kind.Welcome) Kill(child.Serial);
                Kill(parent); parent = 0;
                cap = Create(Kind.Cap, 0x1000, "create_play_proc", route.FocusStationId).Serial;
            }
            else if (phase != SourcePreGameMenuPhase.Teams && phase != SourcePreGameMenuPhase.Rosters
                && phase != SourcePreGameMenuPhase.Versus && phase != SourcePreGameMenuPhase.Options)
                throw new InvalidOperationException("Unknown original option return.");
        }

        private void Cursor(Process body)
        {
            SourcePreGameMenuRoute route = ReadRoute();
            if (phase != SourcePreGameMenuPhase.PlayerEntry || route.Phase != SourcePreGameMenuPhase.PlayerEntry) return;
            if ((route.ParticipatingStationMask & (1 << body.Station)) == 0) return;
            if (body.Stage == 0) { body.Stage = 1; processes.Sleep(2); return; }
            SourcePreGameMenuScreen screen = route.ScreenFor(body.Station);
            if (body.FoundEmitted && (screen == SourcePreGameMenuScreen.Name || screen == SourcePreGameMenuScreen.YesNo))
            {
                Kill(body.Serial);
                Create(Kind.Cursor, 200 + body.Station * 2, "player_cursor", body.Station);
                return;
            }
            if (screen == SourcePreGameMenuScreen.Ready || screen == SourcePreGameMenuScreen.None) return;
            if (screen == SourcePreGameMenuScreen.YesNo && body.YesNoEntry < 0) body.YesNoEntry = SourceTick;
            bool forced = forceSelection || screen == SourcePreGameMenuScreen.YesNo
                && SourceTick - body.YesNoEntry >= 7 * SourceTiming.DefaultTicksPerSecond - 1;
            DispatchDriver(body, route, body.Station, screen, forced);
            if (delay > 0) processes.Sleep(delay);
        }

        private void Cap(Process body)
        {
            SourcePreGameMenuRoute route = ReadRoute();
            if (route.Phase != SourcePreGameMenuPhase.CreatePlay) { capExitRequested = true; return; }
            if (body.Stage == 0) { body.Stage = 1; processes.Sleep(1); return; }
            if (body.Stage == 1)
            {
                Control(SourceProcessSoundControlKind.StopTracks, "cap.c", "create_play_proc", 909, 1);
                Control(SourceProcessSoundControlKind.ReservedTracks, "cap.c", "create_play_proc", 910, 35);
                Call("cap.c", "create_play_proc", 911, "intro", 10, 235, 0x8000);
                capExit = Create(Kind.CapExit, 0x1000, "create_play_exit_proc", body.Station).Serial;
                body.Stage = 2; processes.Sleep(10); return;
            }
            if (body.Stage == 2)
            {
                Call("cap.c", "create_play_proc", 1026, "intro", 12, 205, 0x8280);
                body.Stage = 3;
            }
            DispatchDriver(body, route, body.Station, route.ScreenFor(body.Station), false);
            if (delay > 0) processes.Sleep(delay);
            SourcePreGameMenuPhase after = ReadRoute().Phase;
            if (after != SourcePreGameMenuPhase.CreatePlay && after != SourcePreGameMenuPhase.PortableManagement)
                capExitRequested = true;
        }

        private void CapExit(Process body)
        {
            if (!capExitRequested) return;
            Control(SourceProcessSoundControlKind.StopTracks, "cap.c", "create_play_exit_proc", 856, 1);
            Control(SourceProcessSoundControlKind.ReservedTracks, "cap.c", "create_play_exit_proc", 857, 35);
            Kill(cap); Kill(body.Serial); cap = capExit = 0; capExitRequested = false;
            // The cabinet's editor-return policy is explicit; it does not pretend
            // to restore the native attract/coin RAM or reuse the dead CAP process.
            SourcePreGameMenuRoute route = ReadRoute();
            if (IsNative(route.Phase))
            {
                parent = Create(Kind.Parent, 0x0c00, "pre_game_scrns", route.FocusStationId).Serial;
                phase = route.Phase;
                if (phase == SourcePreGameMenuPhase.PlayerEntry) BeginPhase(route);
            }
            else phase = route.Phase;
        }

        private void Welcome(Process body)
        {
            if (body.Stage == 0) { body.Stage = 1; processes.Sleep(50); return; }
            var site = new SourceRandomSite(SourceRandomKind.RandRange, 3, SourceTick, -1,
                "welcome_proc", currentOrder, 0, random.Provenance.Profile, "select.c:welcome_proc:1893");
            SourceRandomDraw draw = random.Draw(site);
            int[] sounds = { 11, 6, 7 };
            Call("lib/select/select.c", "welcome_proc", 1894, "intro", sounds[draw.Value], 235, 0x8280, 1);
            Kill(body.Serial);
        }

        private void Timer(Process body)
        {
            SourcePreGameMenuRoute route = ReadRoute();
            if (body.Stage == 0)
            {
                body.Stage = 1; body.OldMask = route.ParticipatingStationMask;
                if (body.TimerTicks == 0) body.TimerTicks = SourceTiming.DefaultTicksPerSecond;
                processes.Sleep(body.TimerTicks); return;
            }
            if (body.OldMask != route.ParticipatingStationMask)
            {
                body.Stage = 0; body.Seconds = 9;
                body.TimerTicks = phase == SourcePreGameMenuPhase.PlayerEntry
                    ? 40 * SourceTiming.DefaultTicksPerSecond : SourceTiming.DefaultTicksPerSecond;
                processes.Sleep(1); return;
            }
            if (body.TimerStage == 0)
            {
                Call("lib/select/select.c", "timedown", 6403, "cursors", 36, 235, 5);
                body.TimerStage = 1; processes.Sleep(SourceTiming.DefaultTicksPerSecond);
            }
            else if (body.TimerStage == 1)
            {
                Call("lib/select/select.c", "timedown", 6463, "cursors", 36, 160, 5);
                body.TimerStage = 2; processes.Sleep(25);
            }
            else
            {
                body.Seconds--; body.TimerStage = 0;
                if (body.Seconds < 0) { forceSelection = true; Kill(body.Serial); timer = 0; }
                else Timer(body);
            }
        }

        private void DispatchPortable(SourcePreGameMenuRoute route)
        {
            var wake = new SourcePreGameMenuWake(route, scan, null, -1, SourcePreGameMenuScreen.Portable,
                false, heldCapture, Emit) { Directions = directions };
            executing = wake;
            try { driver.OnSourceMenuWake(wake); }
            finally { executing = null; }
            SourcePreGameMenuRoute after = ReadRoute();
            if (activation == null && IsNative(after.Phase)) Activate(after);
        }

        private void DispatchDriver(Process body, SourcePreGameMenuRoute route, int station,
            SourcePreGameMenuScreen screen, bool forced)
        {
            if (route.StorageBusy) { body.WasBusy = true; processes.Sleep(1); return; }
            bool resume = body.WasBusy; body.WasBusy = false;
            var wake = new SourcePreGameMenuWake(route, scan, processes.Require(body.Serial), station,
                screen, resume, heldCapture, Emit) { Directions = directions, ForceSelection = forced,
                ButtonsEnabled = body.Kind != Kind.Options || body.Wakes > 30 };
            executing = wake;
            try { driver.OnSourceMenuWake(wake); }
            finally { executing = null; }
        }

        private void Emit(SourcePreGameMenuWake wake, SourcePreGameMenuSound sound)
        {
            if (!insideTick || !ReferenceEquals(wake, executing) || caller == null || !wake.Process.HasValue
                || processes.Caller != caller.Serial || wake.Process.Value.LastWakeTick != SourceTick
                || wake.Scan.SourceTick != SourceTick || wake.Scan.InputEpoch != InputEpoch)
                throw new InvalidOperationException("Menu sound requires its current actual source process callback, never a retained UI event.");
            SourceContractGuard.Range((int)sound, 0, 19, nameof(sound));
            SourcePreGameSoundPrograms.Emit(this, wake, sound);
        }

        internal void OriginalMenuCall(SourcePreGameMenuWake wake, SourcePreGameMenuSound sound,
            string path, string function, int line, int playlist, uint volume, uint priority, int sleep = 0)
        {
            bool move = sound == SourcePreGameMenuSound.NameCursor || sound == SourcePreGameMenuSound.YesNoCursor
                || sound == SourcePreGameMenuSound.CapCursor || sound == SourcePreGameMenuSound.FormationCursor
                || sound == SourcePreGameMenuSound.CapLetterCursor || sound == SourcePreGameMenuSound.OptionCursor
                || sound == SourcePreGameMenuSound.PlayCursor || sound == SourcePreGameMenuSound.RouteMenuCursor
                || sound == SourcePreGameMenuSound.CapYesNoCursor;
            bool found = sound == SourcePreGameMenuSound.RecordFound;
            bool edge = false;
            for (int i = 0; i < 4; i++)
                if ((wake.Route.ParticipatingStationMask & (1 << i)) != 0
                    && (wake.StationId < 0 || wake.StationId == i || caller.Kind == Kind.Options))
                    edge |= move ? wake.ClosedDirections(i) != 0 : wake.ClosedButtons(i) != 0;
            if (!found && !edge && !wake.ForceSelection)
                throw new InvalidOperationException("An original menu cue requires this process's actual accepted physical edge.");
            if (found)
            {
                if (caller.FoundEmitted) throw new InvalidOperationException("This source record lookup has already emitted its found sound.");
                caller.FoundEmitted = true;
            }
            Call(path, function, line, "cursors", playlist, volume, priority);
            delay = Math.Max(delay, sleep);
        }

        internal void RequireScreen(SourcePreGameMenuWake wake, params SourcePreGameMenuScreen[] allowed)
        {
            foreach (SourcePreGameMenuScreen screen in allowed) if (wake.Screen == screen) return;
            throw new InvalidOperationException("The original sound operand does not belong to this actual source menu helper.");
        }

        internal bool CurrentYesAnswer(int station)
        {
            if (caller == null || caller.Kind != Kind.Cursor || caller.Station != station)
                throw new InvalidOperationException("YES/NO confirmation belongs to its actual player cursor.");
            return ReadRoute().YesAnswers[station];
        }

        internal int CurrentOptionIndex()
        {
            if (caller == null || caller.Kind != Kind.Options)
                throw new InvalidOperationException("Option confirmation belongs to the actual option controller.");
            return ReadRoute().OptionIndex;
        }

        private void Call(string path, string function, int line, string bank, int playlist,
            uint volume, uint priority, int randomCalls = 0)
        {
            SourceProcessView process = RequireCaller();
            calls.Add(new SourceProcessSoundCall(SourceProcessSoundKind.OriginalGameplay, checked(++nextOutput),
                SourceTick, process.Serial, process.Function, currentOrder, subcall++,
                path + ":" + function + ":" + line, bank, playlist, volume, 127, priority, randomCalls));
        }
        private void Control(SourceProcessSoundControlKind kind, string path, string function, int line, uint mask)
        {
            SourceProcessView process = RequireCaller();
            controls.Add(new SourceProcessSoundControl(kind, checked(++nextOutput), SourceTick, process.Serial,
                process.Function, currentOrder, subcall++, path, function, line, null, mask, 0));
        }
        private SourceProcessView RequireCaller()
        {
            if (!insideTick || caller == null || processes.Caller != caller.Serial)
                throw new InvalidOperationException("Canonical pregame output requires the real executing source continuation.");
            SourceProcessView process = processes.Require(caller.Serial);
            if (process.LastWakeTick != SourceTick || SourceTick <= 0)
                throw new InvalidOperationException("A pregame output cannot invent a source process wake.");
            return process;
        }
        private void RequireActivationDrain()
        {
            if (insideTick) throw new InvalidOperationException("Do not drain sound while its source producer is executing.");
            if (activation != null && !activationTaken && (calls.Count != 0 || controls.Count != 0))
                throw new InvalidOperationException("Accept the typed original entry/clock origin before consuming pregame output.");
        }
        private void Healthy()
        {
            if (Failure != null) throw new InvalidOperationException(Failure);
            if (handoffTaken) throw new InvalidOperationException("The pregame producer has handed ownership to the actual VS/game entry.");
        }
    }

    public sealed partial class GameSimulation
    {
        internal bool OwnsPreGameRandom(ISourceRandom value) { return ReferenceEquals(sourceRandom, value); }
    }
}
