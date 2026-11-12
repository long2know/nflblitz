using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Presentation
{
    public abstract class SourceInstructionOwnedObject
    {
        internal SourceInstructionPresentation Owner { get; }
        internal long Generation { get; }
        public long CreationOrder { get; }
        public bool IsDeleted { get; internal set; }
        internal SourceInstructionOwnedObject(SourceInstructionPresentation owner, long generation, long order)
        { Owner = owner; Generation = generation; CreationOrder = order; }
    }

    public sealed class SourceInstructionPlateState : SourceInstructionOwnedObject, ISourceInstructionPlate
    {
        public SourceInstructionImage Image { get; }
        public string TextureSymbol { get; }
        public SourceInstructionRaster Raster { get; }
        public SourceInstructionPoint Position { get; }
        public float? WidthScale { get; internal set; }
        public float? HeightScale { get; internal set; }
        public string ObjectIdSymbol { get; internal set; }
        public bool WhiteFlash { get; internal set; }
        public bool ChromaKey { get; internal set; }
        public IReadOnlyList<SourceInstructionGraphic> Graphics { get; internal set; }
        internal SourceInstructionPlateState(SourceInstructionPresentation owner, long generation, long order,
            SourceInstructionImage image, string textureSymbol, SourceInstructionRaster raster, SourceInstructionPoint point)
            : base(owner, generation, order)
        { Image = image; TextureSymbol = textureSymbol; Raster = raster; Position = point; }
    }

    public sealed class SourceInstructionTextState : SourceInstructionOwnedObject, ISourceInstructionText
    {
        public int StringId { get; }
        public SourceInstructionTextRequest Request { get; }
        public SourceInstructionFont Font { get; }
        public SourceInstructionColor SourceColor { get; }
        public uint StateColor { get; internal set; }
        public uint? CharacterColorOverride { get; internal set; }
        public SourceInstructionPoint Position { get; }
        public IReadOnlyList<SourceInstructionGraphic> Graphics { get; }
        internal SourceInstructionTextState(SourceInstructionPresentation owner, long generation, long order,
            int id, SourceInstructionTextRequest request, SourceInstructionFont font, SourceInstructionColor sourceColor,
            uint color, SourceInstructionPoint point, IReadOnlyList<SourceInstructionGraphic> graphics)
            : base(owner, generation, order)
        {
            StringId = id; Request = request; Font = font; SourceColor = sourceColor;
            StateColor = color; Position = point; Graphics = graphics;
        }
    }

    public sealed class SourceInstructionFlasherState : ISourceInstructionFlasher
    {
        internal SourceInstructionPresentation Owner;
        internal long Generation;
        internal bool Entered, WhiteNext = true;
        internal uint TitleOriginal, SecondOriginal;
        internal int Flashes, Delay, Fourth;
        internal ISourceInstructionObjectFlashProgram Program;
        public long Serial { get; internal set; }
        public int ProcessId { get; internal set; }
        public string Name { get; internal set; }
        public bool IsKilled { get; internal set; }
        public SourceInstructionTextState Title { get; internal set; }
        public SourceInstructionTextState Second { get; internal set; }
        public SourceInstructionPlateState Plate { get; internal set; }
    }

    public sealed class SourceInstructionPresentation : ISourceInstructionEffects, IDisposable
    {
        private readonly ISourceInstructionResources resources;
        private readonly ISourceInstructionObjectFlashBinding objectFlash;
        private readonly ISourceInstructionSoundPlayer sounds;
        private readonly Action beforeEffect;
        private readonly List<SourceInstructionOwnedObject> objects = new List<SourceInstructionOwnedObject>();
        private readonly List<SourceInstructionFlasherState> flashers = new List<SourceInstructionFlasherState>();
        private readonly SourceInstructionTextState[] messages = new SourceInstructionTextState[4];
        private SourcePortableProcessHost host = new SourcePortableProcessHost();
        private long generation, creationOrder, nextFlasher, instructionSerial;
        private long preparedTick = -1, completedTick = -1, recordTick = -1, tickOffset;
        private int recordCursor, soundOrdinal;
        private SourceInstructionFont? font;
        private SourceInstructionColor? color;
        private int? stringId;
        private SourceInstructionRaster? textRaster;
        private SourceInstructionPoint? textPosition;
        private bool disposed;

        public int? IdiotBoxesShown { get; private set; }
        public SourceInstructionPresentationException Failure { get; private set; }
        public IReadOnlyList<SourceInstructionOwnedObject> Objects { get { return objects.AsReadOnly(); } }
        public IReadOnlyList<SourceInstructionTextState> Messages { get { return Array.AsReadOnly(messages); } }
        public IReadOnlyList<SourceInstructionFlasherState> Flashers { get { return flashers.AsReadOnly(); } }
        public ReadOnlyCollection<SourceProcessView> Processes
        {
            get
            {
                var views = new List<SourceProcessView>();
                foreach (SourceProcessView process in host.Processes)
                    views.Add(new SourceProcessView(process.Serial, process.Id, process.Function, process.RunLevel,
                        process.BirthTick + tickOffset, process.SleepWord,
                        process.LastWakeTick < 0 ? -1 : process.LastWakeTick + tickOffset));
                return views.AsReadOnly();
            }
        }
        public ReadOnlyCollection<SourcePossessionProcessRecord> ProcessRecords
        {
            get
            {
                var records = new List<SourcePossessionProcessRecord>();
                foreach (SourcePossessionProcessRecord record in host.Records)
                    records.Add(new SourcePossessionProcessRecord(record.SourceTick + tickOffset,
                        record.Operation, record.Serial, record.Id, record.Value));
                return records.AsReadOnly();
            }
        }

        public SourceInstructionPresentation(ISourceInstructionResources resources,
            ISourceInstructionObjectFlashBinding objectFlash, ISourceInstructionSoundPlayer sounds, Action beforeEffect)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.objectFlash = objectFlash ?? throw new ArgumentNullException(nameof(objectFlash));
            this.sounds = sounds ?? throw new ArgumentNullException(nameof(sounds));
            this.beforeEffect = beforeEffect ?? throw new ArgumentNullException(nameof(beforeEffect));
        }

        public void BeginInvocation(long serial)
        {
            RequireAlive();
            if (serial <= 0) throw new ArgumentOutOfRangeException(nameof(serial));
            Reset();
            instructionSerial = serial;
        }

        public void Synchronize(long tick, IReadOnlyList<SourcePossessionProcessRecord> records, bool finishTick)
        {
            Run(() =>
            {
                if (records == null) throw new ArgumentNullException(nameof(records));
                if (tick < 0 || tick < preparedTick)
                    throw Error(SourceInstructionPresentationError.InvalidLifecycle, "Source ticks must be supplied monotonically, not reconstructed from Unity time.");
                if (preparedTick < 0)
                {
                    tickOffset = tick - 1;
                    host.BeginTick(1); preparedTick = tick; completedTick = tick - 1;
                }
                PrepareTick(tick);
                if (recordTick != tick) { recordTick = tick; recordCursor = 0; }
                if (recordCursor > records.Count)
                    throw Error(SourceInstructionPresentationError.InvalidLifecycle, "A source process epoch changed without reset.");
                while (recordCursor < records.Count && records[recordCursor].SourceTick <= tick)
                {
                    SourcePossessionProcessRecord record = records[recordCursor++];
                    if (record.Operation != "SuspendMask" && record.Operation != "ResumeMask"
                        && record.Operation != "ResumeAllCall") continue;
                    if (record.SourceTick < preparedTick)
                        throw Error(SourceInstructionPresentationError.InvalidLifecycle, "Late source mask operation cannot be reordered.");
                    if (record.Operation == "SuspendMask") host.SuspendMultiple(record.Id, record.Value);
                    else if (record.Operation == "ResumeMask") host.ResumeMultiple(record.Id, record.Value);
                    else if (record.Operation == "ResumeAllCall") host.ResumeAll();
                }
                if (finishTick && completedTick != tick) DispatchPreparedTick();
            }, false);
        }

        private void PrepareTick(long tick)
        {
            while (preparedTick < tick)
            {
                if (completedTick != preparedTick) DispatchPreparedTick();
                preparedTick++;
                host.BeginTick(host.SourceTick + 1);
            }
        }

        private void DispatchPreparedTick()
        {
            foreach (SourceInstructionFlasherState flasher in flashers)
            {
                if (flasher.IsKilled || !host.Visit(flasher.Serial)) continue;
                try
                {
                    if (flasher.Plate == null) AdvanceText(flasher);
                    else AdvancePlate(flasher);
                }
                finally { host.Complete(); }
            }
            completedTick = preparedTick;
        }

        private void AdvanceText(SourceInstructionFlasherState flasher)
        {
            RequireText(flasher.Title);
            if (flasher.Second != null) RequireText(flasher.Second);
            if (!flasher.Entered)
            {
                flasher.TitleOriginal = flasher.Title.StateColor;
                if (flasher.Second != null) flasher.SecondOriginal = flasher.Second.StateColor;
                flasher.Entered = true;
            }
            else
            {
                uint white = resources.ResolveColor(SourceInstructionColor.White);
                WriteCharacters(flasher.Title, flasher.WhiteNext ? white : flasher.TitleOriginal);
                if (flasher.Second != null)
                    WriteCharacters(flasher.Second, flasher.WhiteNext ? white : flasher.SecondOriginal);
                flasher.WhiteNext = !flasher.WhiteNext;
            }
            host.Sleep(7);
        }

        private static void WriteCharacters(SourceInstructionTextState text, uint value)
        { text.StateColor = value; text.CharacterColorOverride = value; }

        private void AdvancePlate(SourceInstructionFlasherState flasher)
        {
            RequirePlate(flasher.Plate);
            if (flasher.Program == null)
            {
                flasher.Program = objectFlash.Create(flasher.Plate, flasher.Flashes, flasher.Delay, flasher.Fourth);
                if (flasher.Program == null) throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "An admitted flash_obj_white implementation is required; no SDK timing is guessed.");
            }
            SourceInstructionPlateFlashStep step = flasher.Program.Advance();
            if (!step.Completed && (step.SleepTicks <= 0 || step.SleepTicks > 0x7fff))
                throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "The object flasher returned no native continuation.");
            flasher.Plate.WhiteFlash = step.White; flasher.Plate.ChromaKey = step.ChromaKey;
            if (step.Completed) Kill(flasher);
            else host.Sleep(step.SleepTicks);
        }

        public void WriteIdiotBoxesShown(int value)
        {
            Run(() =>
            {
                if (value != -1 && value != 2) throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "Unknown instruction flag write.");
                IdiotBoxesShown = value;
            });
        }

        public ISourceInstructionPlate BeginPlate(string imageSymbol, string textureSymbol, SourceInstructionPosition position)
        {
            return Run<ISourceInstructionPlate>(() =>
            {
                SourceInstructionImage image = resources.ResolvePlate(imageSymbol, textureSymbol);
                if (image == null) throw Error(SourceInstructionPresentationError.MissingResource, "beginobj requires an actual image.");
                SourceInstructionRaster raster = ReadRaster();
                var plate = new SourceInstructionPlateState(this, generation, ++creationOrder, image, textureSymbol,
                    raster, Resolve(position, raster));
                objects.Add(plate);
                return plate;
            });
        }

        public void SetPlateWidthScale(ISourceInstructionPlate plate, float scale)
        { Run(() => { RequireScale(scale); RequirePlate(plate).WidthScale = scale; }); }
        public void SetPlateHeightScale(ISourceInstructionPlate plate, float scale)
        { Run(() => { RequireScale(scale); RequirePlate(plate).HeightScale = scale; }); }
        public void SetPlateId(ISourceInstructionPlate plate, string objectIdSymbol)
        {
            Run(() =>
            {
                if (string.IsNullOrEmpty(objectIdSymbol)) throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "A native object symbol is required.");
                RequirePlate(plate).ObjectIdSymbol = objectIdSymbol;
            });
        }
        public void GeneratePlateVertices(ISourceInstructionPlate plate)
        {
            Run(() =>
            {
                SourceInstructionPlateState state = RequirePlate(plate);
                if (!state.WidthScale.HasValue || !state.HeightScale.HasValue || state.ObjectIdSymbol == null)
                    throw Error(SourceInstructionPresentationError.InvalidLifecycle, "Source scale/id writes must precede vertex generation.");
                state.Graphics = Graphics(resources.GeneratePlate(state.Image, state.Raster, state.Position,
                    state.WidthScale.Value, state.HeightScale.Value));
            });
        }

        public void SetTextFont(SourceInstructionFont value) { Run(() => { font = value; }); }
        public void SetStringId(int id) { Run(() => { stringId = id; }); }
        public void SetTextColor(SourceInstructionColor value) { Run(() => { resources.ResolveColor(value); color = value; }); }
        public void SetTextPosition(SourceInstructionPosition position)
        {
            Run(() =>
            {
                SourceInstructionRaster raster = ReadRaster();
                textRaster = raster; textPosition = Resolve(position, raster);
            });
        }
        public ISourceInstructionText Print(SourceInstructionTextRequest request)
        {
            return Run<ISourceInstructionText>(() =>
            {
                if (!font.HasValue || !color.HasValue || !stringId.HasValue || !textRaster.HasValue || !textPosition.HasValue)
                    throw Error(SourceInstructionPresentationError.InvalidLifecycle, "All source text-state writes must precede oprintf.");
                uint selectedColor = resources.ResolveColor(color.Value);
                SourceInstructionTextLayout layout = resources.FormatText(request, font.Value, selectedColor,
                    textRaster.Value, textPosition.Value);
                if (layout == null) throw Error(SourceInstructionPresentationError.MissingResource,
                    "oprintf requires actual returned string state and glyph geometry.");
                var text = new SourceInstructionTextState(this, generation, ++creationOrder, stringId.Value, request,
                    font.Value, color.Value, layout.StateColor, textPosition.Value, Graphics(layout.Graphics));
                objects.Add(text);
                return text;
            });
        }
        public void StoreMessage(int index, ISourceInstructionText text)
        {
            Run(() =>
            {
                if (index < 0 || index >= messages.Length) throw new ArgumentOutOfRangeException(nameof(index));
                messages[index] = RequireText(text);
            });
        }

        public ISourceInstructionFlasher CreateTextFlasher(string name, int processId,
            ISourceInstructionText title, ISourceInstructionText second, int thirdArgument, int fourthArgument)
        {
            return Run<ISourceInstructionFlasher>(() =>
            {
                if (thirdArgument != 0 || fourthArgument != 0)
                    throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "Only the supplied two text pointers and native null arguments are bound.");
                SourceInstructionFlasherState flasher = CreateFlasher(name, processId, "flash_text_proc");
                flasher.Title = RequireText(title); flasher.Second = second == null ? null : RequireText(second);
                return flasher;
            });
        }
        public void CreatePlateFlasher(string name, int processId, ISourceInstructionPlate plate,
            int flashes, int delay, int fourthArgument)
        {
            Run(() =>
            {
                SourceInstructionPlateState state = RequirePlate(plate);
                SourceInstructionFlasherState flasher = CreateFlasher(name, processId, "flash_obj_proc");
                flasher.Plate = state; flasher.Flashes = flashes; flasher.Delay = delay; flasher.Fourth = fourthArgument;
            });
        }
        private SourceInstructionFlasherState CreateFlasher(string name, int processId, string function)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A source process name is required.", nameof(name));
            var flasher = new SourceInstructionFlasherState { Owner = this, Generation = generation,
                Serial = ++nextFlasher, ProcessId = processId, Name = name };
            host.Create(flasher.Serial, processId, function, 0);
            flashers.Add(flasher);
            return flasher;
        }
        public void PlaySound(SourceInstructionSound sound)
        {
            Run(() =>
            {
                if (sound == null) throw new ArgumentNullException(nameof(sound));
                sounds.Play(sound, new SourceInstructionSoundContext(preparedTick, instructionSerial, soundOrdinal++));
            });
        }
        public void DeleteStrings(int id, int mask)
        {
            Run(() =>
            {
                foreach (SourceInstructionOwnedObject value in objects)
                {
                    var text = value as SourceInstructionTextState;
                    if (text != null && (text.StringId & mask) == (id & mask)) text.IsDeleted = true;
                }
            });
        }
        public void KillTextFlasher(ISourceInstructionFlasher flasher, int signal)
        {
            Run(() =>
            {
                var state = flasher as SourceInstructionFlasherState;
                if (state == null || state.Owner != this || state.Generation != generation || state.IsKilled || state.Plate != null)
                    throw Error(SourceInstructionPresentationError.InvalidHandle, "A live owned text-flasher return is required.");
                if (signal != 0) throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "Only the actual zero kill signal is bound.");
                Kill(state);
            });
        }
        public void DeletePlate(ISourceInstructionPlate plate)
        {
            Run(() =>
            {
                SourceInstructionPlateState state = RequirePlate(plate);
                state.IsDeleted = true;
                foreach (SourceInstructionFlasherState flasher in flashers)
                    if (flasher.Plate == state && !flasher.IsKilled) Kill(flasher);
            });
        }

        public IReadOnlyList<SourceInstructionGraphic> Frame()
        {
            RequireAlive();
            if (Failure != null) return new List<SourceInstructionGraphic>().AsReadOnly();
            return Run<IReadOnlyList<SourceInstructionGraphic>>(BuildFrame, false);
        }

        private IReadOnlyList<SourceInstructionGraphic> BuildFrame()
        {
            var frame = new List<SourceInstructionGraphic>();
            foreach (SourceInstructionOwnedObject value in objects)
            {
                if (value.IsDeleted) continue;
                var plate = value as SourceInstructionPlateState;
                var text = value as SourceInstructionTextState;
                IReadOnlyList<SourceInstructionGraphic> graphics = plate == null ? text.Graphics : plate.Graphics;
                if (graphics == null) continue;
                foreach (SourceInstructionGraphic graphic in graphics)
                {
                    string texture = graphic.Texture;
                    uint tint = graphic.Color;
                    if (plate != null && plate.WhiteFlash && plate.ChromaKey)
                    {
                        if (string.IsNullOrEmpty(graphic.WhiteTexture))
                            throw Error(SourceInstructionPresentationError.MissingResource, "The actual white/chroma texture is unavailable.");
                        texture = graphic.WhiteTexture;
                    }
                    else if (plate != null && plate.WhiteFlash)
                        throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding,
                            "White-object output without the actual chroma binding is unsupported.");
                    if (text != null && text.CharacterColorOverride.HasValue) tint = text.CharacterColorOverride.Value;
                    frame.Add(graphic.With(texture, tint));
                }
            }
            return frame.AsReadOnly();
        }

        private SourceInstructionRaster ReadRaster()
        {
            SourceInstructionRaster raster = resources.ReadRaster();
            if (raster.Width <= 0 || raster.Height <= 0)
                throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "Actual native raster values are required.");
            return raster;
        }
        private static SourceInstructionPoint Resolve(SourceInstructionPosition value, SourceInstructionRaster raster)
        {
            float x = raster.HalfWidth + value.HorizontalOffset, y = raster.HalfHeight + value.VerticalOffset;
            if (value.TruncateFloatHalfAfterOffset)
            {
                x = checked((int)x); y = checked((int)y);
            }
            return new SourceInstructionPoint(x, y, value.Depth);
        }
        private SourceInstructionPlateState RequirePlate(ISourceInstructionPlate value)
        {
            var state = value as SourceInstructionPlateState;
            RequireOwned(state);
            return state;
        }
        private SourceInstructionTextState RequireText(ISourceInstructionText value)
        {
            var state = value as SourceInstructionTextState;
            RequireOwned(state);
            return state;
        }
        private void RequireOwned(SourceInstructionOwnedObject state)
        {
            if (state == null || state.Owner != this || state.Generation != generation || state.IsDeleted)
                throw Error(SourceInstructionPresentationError.InvalidHandle, "A live returned object owned by this instruction invocation is required.");
        }
        private static void RequireScale(float value)
        {
            if (value <= 0 || float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
        private static IReadOnlyList<SourceInstructionGraphic> Graphics(IReadOnlyList<SourceInstructionGraphic> values)
        {
            if (values == null || values.Count == 0)
                throw Error(SourceInstructionPresentationError.MissingResource, "A real resource/layout binding must return drawable geometry.");
            var copy = new List<SourceInstructionGraphic>(values);
            foreach (SourceInstructionGraphic value in copy)
                if (value == null) throw Error(SourceInstructionPresentationError.InvalidGeometry, "Null graphic.");
            return copy.AsReadOnly();
        }
        private void Kill(SourceInstructionFlasherState state)
        {
            if (host.Contains(state.Serial)) host.Kill(state.Serial);
            state.IsKilled = true;
        }
        private void Cleanup()
        {
            foreach (SourceInstructionFlasherState state in flashers) if (!state.IsKilled) Kill(state);
            foreach (SourceInstructionOwnedObject state in objects) state.IsDeleted = true;
        }
        public void Reset()
        {
            RequireAlive();
            Cleanup();
            objects.Clear(); flashers.Clear(); Array.Clear(messages, 0, messages.Length);
            host = new SourcePortableProcessHost();
            generation++; creationOrder = 0; nextFlasher = 0; instructionSerial = 0;
            preparedTick = -1; completedTick = -1; recordTick = -1; tickOffset = 0; recordCursor = 0; soundOrdinal = 0;
            font = null; color = null; stringId = null; textRaster = null; textPosition = null;
            IdiotBoxesShown = null; Failure = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            Cleanup(); Array.Clear(messages, 0, messages.Length); disposed = true;
        }
        public void Abort(Exception error)
        {
            RequireAlive();
            Failure = error as SourceInstructionPresentationException
                ?? new SourceInstructionPresentationException(SourceInstructionPresentationError.InvalidLifecycle,
                    "The source instruction epoch stopped before successful completion.", error);
            Cleanup();
        }
        private void RequireAlive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SourceInstructionPresentation));
        }
        private void Run(Action action, bool synchronize = true)
        { Run<object>(() => { action(); return null; }, synchronize); }
        private T Run<T>(Func<T> action, bool synchronize = true)
        {
            RequireAlive();
            if (Failure != null) throw Error(SourceInstructionPresentationError.InvalidLifecycle, "Reset the failed instruction epoch before reuse.");
            try
            {
                if (synchronize) beforeEffect();
                return action();
            }
            catch (Exception error)
            {
                Failure = error as SourceInstructionPresentationException
                    ?? new SourceInstructionPresentationException(SourceInstructionPresentationError.UnsupportedNativeBinding,
                        "Instruction presentation binding failed; no substitute was created.", error);
                Cleanup();
                throw Failure;
            }
        }
        private static SourceInstructionPresentationException Error(SourceInstructionPresentationError code, string message)
        { return new SourceInstructionPresentationException(code, message); }
    }
}
