using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourceKickoffRuntimeIntegration
{
    internal static class Program
    {
        private static readonly SourceInstructionProcessIds Ids = new SourceInstructionProcessIds(0x71, 0x72, 0x73, 0x74);
        private const int ChildId = 0x6b42, ParentA = 0x1000, ParentB = 0x2000;
        private const uint A = 0x80, B = 0x20, C = 0x02, White = 0x400;
        private static int assertions;
        private static void Check(bool value, string why)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(why);
        }
        private static void Equal<T>(T expected, T actual, string why)
        { Check(EqualityComparer<T>.Default.Equals(expected, actual), why + ": expected " + expected + ", got " + actual); }
        private static T Throws<T>(Action action, string why) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T error) { return error; }
            throw new InvalidOperationException(why + ": expected " + typeof(T).Name);
        }

        // Explicit synthetic primitives/effects, not Game, Unity, native values or resource substitutes in production.
        private sealed class Plate : ISourceInstructionPlate { }
        private sealed class Services : ISourceInstructionAdmissionState, ISourceInstructionInputs,
            ISourceKickoffInstructionChildInputs, ISourceKickoffInstructionPlayerButtons,
            ISourceKickoffInstructionChildEffects, ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects,
            ISourceKickoffPlayerObject, ISourceKickoffInstructionChildReturnBinding,
            ISourceKickoffInstructionFlashReturnBinding, ISourceInstructionResources,
            ISourceInstructionObjectFlashBinding, ISourceInstructionSoundPlayer
        {
            internal int Shown = -99, Status = 1;
            internal uint Flags = 8;
            internal readonly List<int> FlagWrites = new List<int>();
            internal readonly List<SourceKickoffInstructionChildTextRequest> ChildText =
                new List<SourceKickoffInstructionChildTextRequest>();
            internal readonly List<SourceInstructionTextRequest> PageText = new List<SourceInstructionTextRequest>();
            internal readonly List<string> Suffix = new List<string>();
            internal readonly List<SourceInstructionSoundContext> Sounds = new List<SourceInstructionSoundContext>();
            internal readonly int[] Current = new int[4], Old = new int[4];
            internal int Captures, ObjectWrites, SwitchReads, ChildPlates, ChildReturns, FlashReturns, RasterReads;
            internal int StatusReads;
            internal Exception FlagWriteFailure;
            internal readonly SourceKickoffInstructionChildOperands Operands =
                new SourceKickoffInstructionChildOperands(false, 101, 203, 0, 1);
            internal SourceKickoffInstructionOwnedHelpers Helpers()
            { return new SourceKickoffInstructionOwnedHelpers(A, B, C, White, this, this, this); }
            public int ReadIdiotBoxesShown() { return Shown; }
            public void WriteIdiotBoxesShown(int value)
            {
                FlagWrites.Add(value); Shown = value;
                if (FlagWriteFailure != null) throw FlagWriteFailure;
            }
            public int ReadPlayerStatus() { StatusReads++; return Status; }
            public int ReadGamesPlayed(int slot) { return 0; }
            public int ReadPlayerSwitches() { return 4; }
            public int ReadShowPlyselIdiotBox() { return 0; }
            public int ReadTsec() { return 7; }
            public int ReadIsLowResolution() { return 0; }
            public int ReadFourPlayerVersion() { return 0; }
            public int ReadPlayerControl(int slot) { return 3 - slot; }
            public int ReadBallCarrier() { return 3; }
            public int ReadPlayerTeam(int nativeControlIndex) { return 0; }
            public int ReadOffensiveSide() { return 0; }
            public int ReadCurrentButtons(int player) { return Current[player]; }
            public void WriteCurrentButtons(int player, int value) { Current[player] = value; }
            public int ReadOldButtons(int player) { return Old[player]; }
            public void WriteOldButtons(int player, int value) { Old[player] = value; }
            public uint ReadCurrentSwitchWord() { SwitchReads++; return A; }
            public ISourceKickoffPlayerObject ReadPlayerBlock(int originalPlayerNumber)
            { Equal(0, originalPlayerNumber, "captured original slot"); Captures++; return this; }
            public uint ReadFlags() { return Flags; }
            public void WriteFlags(uint value) { Flags = value; ObjectWrites++; }
            public void SetTextZIncrement(float value) { }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint point)
            { ChildPlates++; return new Plate(); }
            public void SetPlateZAngle(ISourceInstructionPlate plate, float value) { }
            public void SetPlateHScale(ISourceInstructionPlate plate, float value) { }
            public void SetPlateObjectId(ISourceInstructionPlate plate, string symbol, int slot) { }
            public void GeneratePlateVertices(ISourceInstructionPlate plate) { }
            public void SetTextFont(SourceKickoffInstructionChildFont font) { }
            public void SetStringId(int id) { }
            public void SetTextColor(SourceKickoffInstructionChildColor color) { }
            public void SetTextPosition(SourceInstructionPoint point) { }
            public void Print(SourceKickoffInstructionChildTextRequest request) { ChildText.Add(request); }
            public void PlaySound(SourceKickoffInstructionChildSound sound)
            {
                Check(ReferenceEquals(SourceKickoffInstructionChildSound.Ricochet, sound), "owned child sound");
                Suffix.Add("sound");
            }
            public void DeleteStrings(int id, uint mask)
            {
                Equal(0x34, id, "child string ID"); Equal(uint.MaxValue, mask, "child string mask");
                Suffix.Add("strings");
            }
            public void DeletePlateClass(string symbol, int slot, uint mask)
            {
                Equal("OID_IDIOTBOX", symbol, "child plate class"); Equal(0, slot, "child cleanup slot");
                Equal(uint.MaxValue, mask, "child plate mask"); Suffix.Add("plate");
            }
            public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(
                SourceKickoffInstructionChildInvocation invocation, SourceKickoffInstructionChildReturnReason reason)
            {
                Equal(SourceKickoffInstructionChildReturnReason.NormalBodyEnd, reason, "explicit child body end");
                ChildReturns++; Suffix.Add("return");
                return SourceKickoffInstructionChildReturnAction.ExternallyRetire;
            }
            public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(
                SourceKickoffInstructionFlashInvocation invocation)
            { FlashReturns++; return SourceKickoffInstructionFlashReturnAction.ExternallyRetire; }
            public SourceInstructionRaster ReadRaster()
            { RasterReads++; return new SourceInstructionRaster(513, 259, 256.75f, 128.75f); }
            public uint ResolveColor(SourceInstructionColor color) { return (uint)color + 1; }
            public SourceInstructionImage ResolvePlate(string imageSymbol, string textureSymbol)
            { return new SourceInstructionImage(imageSymbol, "fixture/normal", "fixture/white", 212, 79, 106, 39, 0, 0, 1, 1); }
            public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
                SourceInstructionRaster raster, SourceInstructionPoint position, float widthScale, float heightScale)
            { return Graphics("plate", position, 1); }
            public SourceInstructionTextLayout FormatText(SourceInstructionTextRequest request,
                SourceInstructionFont font, uint color, SourceInstructionRaster raster, SourceInstructionPoint position)
            {
                PageText.Add(request);
                return new SourceInstructionTextLayout(color, Graphics(request.Page + "/" + request.Line, position, color));
            }
            private static SourceInstructionGraphic[] Graphics(string symbol, SourceInstructionPoint point, uint color)
            {
                return new[] { new SourceInstructionGraphic("fixture/normal", "fixture/white", symbol, color,
                    point.Depth, new[] { new SourceInstructionVertex(point.X, point.Y, 0, 0),
                        new SourceInstructionVertex(point.X + 1, point.Y, 1, 0),
                        new SourceInstructionVertex(point.X, point.Y + 1, 0, 1) }) };
            }
            public ISourceInstructionObjectFlashProgram Create(ISourceInstructionPlate plate,
                int flashes, int delay, int fourthArgument)
            { return new ExternalPlateFlash(); }
            private sealed class ExternalPlateFlash : ISourceInstructionObjectFlashProgram
            {
                public SourceInstructionPlateFlashStep Advance()
                { return new SourceInstructionPlateFlashStep(false, false, true, 0); }
            }
            public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context) { Sounds.Add(context); }
        }

        // This is the real portable instruction-program adapter, never an IGameSimulation stand-in.
        private sealed class ProgramHost : ISourceInstructionAdmissionProgramBinding
        {
            internal SourcePortableProcessHost Host;
            internal SourceInstructionDispatcher Dispatcher;
            internal int ManualInvocations, Initializations;
            internal readonly List<SourceInstructionAdmissionBinding> BindingAttempts =
                new List<SourceInstructionAdmissionBinding>();
            internal SourceInstructionAdmissionBinding Admission { get; private set; }
            private long next;
            internal ProgramHost() { Reset(); }
            internal void Reset()
            {
                Host = new SourcePortableProcessHost(); Dispatcher = new SourceInstructionDispatcher(Host); next = 100;
                Host.Create(1, Ids.PostPlayer, "last_player_proc", 0);
                Host.Create(2, Ids.Camera, "cambot_proc", 0);
                Host.Create(3, 0x75, "game_proc", 0);
                Host.Create(4, 0x76, "player_proc", 0);
                if (Admission != null) Initialize(Admission);
            }
            private void Initialize(SourceInstructionAdmissionBinding binding)
            {
                Initializations++;
                Dispatcher.InitializeAdmission(binding, () => checked(next + 1), value => next = value);
            }
            public void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding)
            {
                BindingAttempts.Add(binding);
                Initialize(binding);
                Admission = binding;
            }
            public SourceInstructionSnapshot Current { get { return Dispatcher.LastSnapshot; } }
            public IReadOnlyList<SourcePossessionProcessRecord> Records { get { return Host.Records; } }
            public long Invoke(SourceInstructionInvocation invocation)
            {
                ManualInvocations++;
                long serial = checked(next + 1); Dispatcher.Admit(serial, invocation); next = serial; return serial;
            }
            internal long Admit(SourceInstructionAdmissionBinding binding, bool kickoff, SourceInstructionConsumer consumer)
            {
                Host.BeginTick(Host.SourceTick + 1);
                long caller = kickoff ? 4 : 3;
                Check(Host.Visit(caller), "actual producer wakes");
                long? admitted;
                try
                {
                    admitted = kickoff
                        ? Dispatcher.AdmitKickoffFromCatch(binding, true, caller, () => checked(next + 1))
                        : Dispatcher.AdmitAtPlaySelectionReturn(binding, true, caller, () => checked(next + 1));
                    Check(admitted.HasValue, "actual initialized producer admits a process");
                    next = admitted.Value;
                }
                finally { Host.Complete(); }
                consumer.AfterSourceStep(Host.SourceTick);
                return admitted.Value;
            }
            internal void Step(SourceInstructionConsumer consumer)
            {
                Host.BeginTick(Host.SourceTick + 1);
                foreach (SourceProcessView process in Host.Processes)
                {
                    if (Dispatcher.Dispatch(process.Serial, name => { })) continue;
                    if (!Host.Visit(process.Serial)) continue;
                    try { Host.Sleep(1); }
                    finally { Host.Complete(); }
                }
                consumer.AfterSourceStep(Host.SourceTick);
            }
        }

        private static void Bind(SourceInstructionConsumer consumer,
            Services services, SourceKickoffInstructionOwnedHelpers helpers)
        {
            consumer.BindSourceKickoffInstructionOwnedHelpers(4, Ids, services, services, ChildId, 4,
                ParentA, ParentB, services, services, services.Operands, services, helpers, services);
        }
        private static void Until(ProgramHost host, SourceInstructionConsumer consumer, Func<bool> reached, int limit)
        {
            for (int i = 0; i < limit && !reached(); i++) host.Step(consumer);
            Check(reached(), "bounded runtime lifecycle reached its expected state");
        }

        private static void OwnedAdmissionLifecycle()
        {
            var services = new Services();
            var host = new ProgramHost();
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                SourceKickoffInstructionOwnedHelpers helpers = services.Helpers();
                Equal(0, services.FlagWrites.Count, "construction cannot initialize or dispatch");
                Check(!consumer.HasSourceAdmission && host.Admission == null, "admission remains explicit");
                Bind(consumer, services, helpers);
                SourceInstructionAdmissionBinding binding = host.Admission;
                Check(ReferenceEquals(binding.Invocation.Effects, binding.State), "invocation and admission have one owner");
                Check(ReferenceEquals(helpers, binding.KickoffCoordinator.SourceChild.OwnedHelpers), "actual explicit helper inputs retained");
                Check(consumer.HasSourceAdmission && !consumer.HasInvocation, "binding is not manual page invocation");
                Equal<int?>(0, consumer.IdiotBoxesShown, "real initialized admission flag");
                Check(consumer.Presentation.IdiotBoxesShown == null, "initialization is not a page write");
                Throws<SourceInstructionPresentationException>(() => Bind(consumer, services, helpers), "no hot rebind");
                Throws<SourceInstructionPresentationException>(() => consumer.Invoke(4, Ids, services), "no manual/automatic mixing");
                Equal(1, services.FlagWrites.Count, "rejected integrations do not reinitialize the owner");
                Equal(1, host.BindingAttempts.Count, "consumer forwards one initial coherent binding");
                Equal(1, host.Initializations, "actual dispatcher initializes once");
                long parent = host.Admit(binding, true, consumer);
                Equal(0, services.Shown, "creation does not inline kickoff entry");
                host.Step(consumer);
                Equal(1, services.Shown, "actual kickoff entry writes the same flag owner");
                Check(host.Dispatcher.OwnsKickoff(parent) && !consumer.HasInvocation, "kickoff is not the old four pages");
                Equal(0, services.ChildPlates, "children remain deferred");
                host.Step(consumer);
                Equal(1, services.ChildPlates, "source child owns the supplied child effects");
                Check(services.ChildText.Count > 0
                    && services.ChildText[0].Kind == SourceKickoffInstructionChildTextKind.Header, "source child text reaches actual effects");
                Equal(0, services.Captures, "flash remains deferred");
                Equal(0, services.PageText.Count, "kickoff does not substitute manual pages");
                host.Step(consumer);
                Equal(1, services.Captures, "real deferred flash captures the supplied object");
                Equal(White | 8u, services.Flags, "owned flash writes supplied live flags");
                services.Flags |= 0x10000;
                Until(host, consumer, () => services.ChildReturns == 1, 60);
                Equal(40, services.SwitchReads, "child actually ran the owned held-current/minimum wait");
                Equal(1, services.FlashReturns, "explicit flash return policy ran");
                Equal(2, services.ObjectWrites, "owned flash completed one full cycle");
                Equal(0x10008u, services.Flags, "live unrelated flags survived the owned clear");
                Equal("sound,strings,plate,return", string.Join(",", services.Suffix), "child suffix order");
                Equal(ParentA | ParentB, services.Current[3], "coordinator completed real current-button suffix");
                Equal(ParentA | ParentB, services.Old[3], "coordinator completed real old-button suffix");
                Check(!host.Host.Exists(ChildId, -1), "explicit child return retired actual children");
                long serial = host.Admit(binding, false, consumer);
                Check(consumer.HasInvocation && consumer.Program.Process.Serial == serial, "AfterSourceStep adopts actual automatic serial");
                Equal(0, host.ManualInvocations, "automatic producer never calls the manual wrapper");
                host.Step(consumer);
                Equal(-1, services.Shown, "instruction entry writes the original admission owner");
                Equal<int?>(-1, consumer.Presentation.IdiotBoxesShown, "entry reaches the existing presentation");
                Until(host, consumer, () => consumer.Program.Stage == SourceInstructionStage.PageDelay, 4);
                Equal(8, consumer.Presentation.Frame().Count, "automatic invocation produces the existing drawable page");
                Until(host, consumer, () => consumer.Program.Stage == SourceInstructionStage.Fallthrough, 100);
                Equal(28, services.PageText.Count, "all original page requests still run after selection return");
                for (int i = 0; i < services.PageText.Count; i++)
                {
                    Equal((SourceInstructionPage)(i / 7), services.PageText[i].Page, "page identity preserved");
                    Equal(i % 7, services.PageText[i].Line, "page line preserved");
                }
                Equal(2, services.Shown, "final flag uses that same live owner");
                Equal("0,1,-1,2", string.Join(",", services.FlagWrites), "one coherent admission/presentation flag sequence");
                Equal(0, consumer.Presentation.Frame().Count, "actual source cleanup reaches the frame");
                foreach (SourceInstructionSoundContext context in services.Sounds)
                    Equal(serial, context.InstructionSerial, "automatic sound retains admitted source serial");
                Check(services.Sounds.Count > 0, "automatic sound service is used");
                Throws<SourceInstructionPresentationException>(() => consumer.OnSourceProgramReset(), "no fake reset of a live source epoch");
                consumer.Presentation.Abort(new InvalidOperationException("explicit runtime failure"));
                Throws<SourceInstructionPresentationException>(() => consumer.AfterSourceStep(host.Host.SourceTick), "faults need an actual reset");
                services.Status = 0;
                host.Reset();
                Equal(2, host.Initializations, "established program initializes its retained binding before notification");
                consumer.OnSourceProgramReset();
                Equal(2, host.Initializations, "consumer does not double initialize an established reset");
                Equal(1, host.BindingAttempts.Count, "consumer does not rebind an established reset");
                Check(consumer.HasSourceAdmission && !consumer.HasInvocation && consumer.Program == null, "real restart retains binding, clears epoch");
                Equal<int?>(2, consumer.IdiotBoxesShown, "restart initializes the caller's inactive participation");
                Check(consumer.Presentation.Failure == null && consumer.Presentation.IdiotBoxesShown == null,
                    "restart initialization is not a stale presentation effect");
                services.Status = 1;
                host.Reset(); consumer.OnSourceProgramReset();
                Equal(3, host.Initializations, "each established fresh program initializes exactly once");
                Equal(1, host.BindingAttempts.Count, "established binding is not resubmitted");
                Equal("0,1,-1,2,2,0", string.Join(",", services.FlagWrites), "reset does not replay initialization writes");
                Equal<int?>(0, consumer.IdiotBoxesShown, "same binding reads live restarted participation");
                host.Admit(binding, true, consumer); host.Step(consumer);
                Equal(1, services.Shown, "retained admission is callable after restart");
                Equal(0, host.ManualInvocations, "restart does not switch to manual dispatch");
            }
        }

        private static void PendingAdmissionRecoversOnlyOnFreshReset()
        {
            var firstFailure = new InvalidOperationException("synthetic initial admission write failure");
            var repeatedFailure = new InvalidOperationException("synthetic restarted admission write failure");
            var services = new Services { FlagWriteFailure = firstFailure };
            var host = new ProgramHost();
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                SourceKickoffInstructionOwnedHelpers helpers = services.Helpers();
                Check(ReferenceEquals(firstFailure, Throws<InvalidOperationException>(
                    () => Bind(consumer, services, helpers), "actual dispatcher initialization fails")),
                    "actual dispatcher initialization surfaces the original synthetic provider failure");
                Equal(1, host.BindingAttempts.Count, "failed initial admission reached the real program once");
                Equal(1, host.Initializations, "actual dispatcher attempted initialization");
                SourceInstructionAdmissionBinding binding = host.BindingAttempts[0];
                Check(ReferenceEquals(binding.Invocation.Effects, binding.State), "pending admission retains one coherent owner");
                Check(ReferenceEquals(helpers, binding.KickoffCoordinator.SourceChild.OwnedHelpers),
                    "pending admission retains the explicit owned helpers");
                Check(host.Admission == null, "failed initial admission was not retained by the program");
                Check(!consumer.HasSourceAdmission && !consumer.HasInvocation, "failed pending admission is never ready");
                Exception firstPresentationFailure = consumer.Presentation.Failure;
                Check(firstPresentationFailure != null, "initial failure remains observable");
                Equal("0", string.Join(",", services.FlagWrites), "real initialization failed after its live state write");
                Equal(1, services.StatusReads, "real dispatcher read the supplied provider");
                Check(consumer.Presentation.IdiotBoxesShown == null, "partial initialization is not a page effect");
                Throws<SourceInstructionPresentationException>(() => host.Step(consumer), "ordinary stepping blocks pending dispatch");
                Throws<InvalidOperationException>(() => host.Admit(binding, true, consumer),
                    "actual poisoned dispatcher rejects the producer");
                Throws<SourceInstructionPresentationException>(() => consumer.Invoke(4, Ids, services),
                    "failed pending admission cannot fall back to manual invocation");
                Throws<SourceInstructionPresentationException>(() => Bind(consumer, services, helpers),
                    "failed pending admission cannot hot rebind");
                Equal(1, host.Initializations, "ordinary stepping cannot retry the poisoned program");
                Equal(1, services.FlagWrites.Count, "blocked paths do not replay partial initialization writes");
                Equal(0L, host.Dispatcher.LastKickoffSerial, "failed dispatcher created no kickoff process");
                Equal(0L, host.Dispatcher.LastSerial, "failed dispatcher created no page process");
                Equal(0, services.ChildPlates, "failed pending admission dispatched no child effects");
                Equal(0, services.RasterReads, "failed pending admission consumed no page resources");

                SourcePortableProcessHost oldHost = host.Host;
                SourceInstructionDispatcher oldDispatcher = host.Dispatcher;
                services.Status = 0;
                services.FlagWriteFailure = repeatedFailure;
                host.Reset();
                Check(!ReferenceEquals(oldHost, host.Host) && !ReferenceEquals(oldDispatcher, host.Dispatcher),
                    "restart creates the actual fresh portable host and dispatcher");
                Equal(1, host.Initializations, "fresh program cannot automatically retain a failed initial bind");
                Check(!consumer.HasSourceAdmission, "fresh program alone cannot mark pending admission ready");
                Check(ReferenceEquals(repeatedFailure, Throws<InvalidOperationException>(
                    () => consumer.OnSourceProgramReset(), "actual reset notification retries pending initialization")),
                    "actual reset notification surfaces the original repeated provider failure");
                Equal(2, host.BindingAttempts.Count, "only actual reset retries the pending bind");
                Check(ReferenceEquals(binding, host.BindingAttempts[1]), "retry uses the same coherent binding");
                Check(!consumer.HasSourceAdmission && host.Admission == null, "repeated failure remains pending, not established");
                Exception repeatedPresentationFailure = consumer.Presentation.Failure;
                Check(repeatedPresentationFailure != null && !ReferenceEquals(firstPresentationFailure, repeatedPresentationFailure),
                    "repeated initialization failure replaces stale failure");
                Equal("0,2", string.Join(",", services.FlagWrites), "fresh failed retry reads live restarted participation once");
                services.FlagWriteFailure = null;
                Throws<SourceInstructionPresentationException>(() => host.Step(consumer),
                    "fixing the provider cannot enable in-place dispatch or retry");
                Equal(2, host.Initializations, "poisoned restarted program is not retried by stepping");
                Equal(2, services.StatusReads, "ordinary stepping does not reread the repaired provider");
                Equal(2, services.FlagWrites.Count, "ordinary stepping does not replay side effects");
                Check(ReferenceEquals(repeatedPresentationFailure, consumer.Presentation.Failure), "failure remains visible until another actual reset");

                oldDispatcher = host.Dispatcher;
                services.Status = 1;
                host.Reset();
                Check(!ReferenceEquals(oldDispatcher, host.Dispatcher), "successful retry also requires a fresh actual program");
                Equal(2, host.Initializations, "still-pending binding is not initialized before its reset notification");
                Check(!consumer.HasSourceAdmission, "pending binding stays non-ready before successful initialization");
                consumer.OnSourceProgramReset();
                Equal(3, host.Initializations, "successful fresh retry initializes once");
                Equal(3, host.BindingAttempts.Count, "pending binding was retried once per actual reset");
                Check(ReferenceEquals(binding, host.BindingAttempts[2]) && ReferenceEquals(binding, host.Admission),
                    "successful retry establishes that same binding on the new program");
                Check(consumer.HasSourceAdmission && !consumer.HasInvocation && consumer.Program == null,
                    "only successful initialization marks admission ready without manually invoking pages");
                Check(consumer.Presentation.Failure == null && consumer.Presentation.IdiotBoxesShown == null,
                    "successful actual reset clears the presentation failure without page writes");
                Equal<int?>(0, consumer.IdiotBoxesShown, "successful retry reads the live owner against the fresh program");
                Equal("0,2,0", string.Join(",", services.FlagWrites), "one initialization write per fresh admission attempt");
                Equal(0, services.ChildPlates, "recovery itself does not replay child effects");
                Equal(0, services.RasterReads, "recovery itself does not replay page effects");
                Equal(0, host.ManualInvocations, "failed or recovered admission never used the manual path");
                long parent = host.Admit(binding, true, consumer);
                host.Step(consumer);
                Check(host.Dispatcher.OwnsKickoff(parent), "recovered binding admits on the actual fresh dispatcher");
                Equal(1, services.Shown, "recovered producer writes the original coherent owner");
                host.Step(consumer);
                Equal(1, services.ChildPlates, "recovered producer reaches the actual owned child");

                int writes = services.FlagWrites.Count;
                services.Status = 0;
                host.Reset();
                Equal(4, host.Initializations, "recovered established binding is retained and initialized by its program");
                consumer.OnSourceProgramReset();
                Equal(4, host.Initializations, "consumer does not initialize a recovered established binding twice");
                Equal(3, host.BindingAttempts.Count, "recovered established binding is not rebound");
                Equal(writes + 1, services.FlagWrites.Count, "established reset writes initialization exactly once");
                Equal<int?>(2, consumer.IdiotBoxesShown, "established reset still uses live participation");
            }
        }

        private static void DefaultAndManualRemainExplicit()
        {
            var services = new Services();
            var host = new ProgramHost();
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                for (int i = 0; i < 10; i++) host.Step(consumer);
                Check(!consumer.HasSourceAdmission && !consumer.HasInvocation, "default remains unbound");
                Equal(0, services.RasterReads, "default consumes no instruction resources");
                Equal(0, services.FlagWrites.Count, "default does not initialize admission");
                host.Reset(); consumer.OnSourceProgramReset();
                Equal(0, host.Initializations, "default restart does not activate admission");
                consumer.Invoke(4, Ids, services);
                Throws<SourceInstructionPresentationException>(() => Bind(consumer, services, services.Helpers()), "manual cannot hot-switch to owned");
                Until(host, consumer, () => consumer.Program.Stage == SourceInstructionStage.Fallthrough, 100);
                Equal(1, host.ManualInvocations, "existing manual call still uses the existing program binding");
                Equal(28, services.PageText.Count, "existing manual four-page behavior retained");
                Equal<int?>(2, consumer.IdiotBoxesShown, "manual flag remains presentation-owned");
                Equal(0, services.FlagWrites.Count, "manual did not silently adopt the external admission state");
                Equal(0, services.ChildPlates, "manual did not enable kickoff children");
                Equal(0, services.SwitchReads, "manual did not enable owned waits");
                Equal(0, services.ObjectWrites, "manual did not enable owned flashes");
                host.Reset(); consumer.OnSourceProgramReset();
                Check(!consumer.HasSourceAdmission && !consumer.HasInvocation, "manual reset clears only the manual epoch");
                Equal(0, host.Initializations, "manual reset cannot initialize owned admission");
                Equal(0, host.BindingAttempts.Count, "default and manual paths never request the admission capability");
            }
        }

        private static void MissingBindingsDoNotActivate()
        {
            var services = new Services();
            var host = new ProgramHost();
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                Throws<ArgumentNullException>(() => Bind(consumer, services, null), "helpers are mandatory");
                Check(!consumer.HasSourceAdmission && consumer.Presentation.Failure == null, "invalid inputs are not a success-shaped binding");
                Equal(0, services.FlagWrites.Count, "invalid inputs did not change live state");
                Equal(0, host.BindingAttempts.Count, "invalid arguments fail before preparing a program bind");
                host.Reset(); consumer.OnSourceProgramReset();
                Equal(0, host.Initializations, "invalid arguments leave no pending admission to recover");
                consumer.Invoke(4, Ids, services);
                host.Step(consumer);
                Equal<int?>(-1, consumer.IdiotBoxesShown, "validation failure leaves the untouched manual path usable");
            }
        }

        private static int Main()
        {
            try
            {
                OwnedAdmissionLifecycle(); PendingAdmissionRecoversOnlyOnFreshReset();
                DefaultAndManualRemainExplicit(); MissingBindingsDoNotActivate();
                Console.WriteLine("PASS runtime-owned-admission-lifecycle assertions=" + assertions);
                Console.WriteLine("SUMMARY component_cases=1 assertions=" + assertions + " failures=0 Game=0 Unity=0 native=0");
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL runtime-owned-admission-lifecycle: " + error.Message);
                return 1;
            }
        }
    }
}
