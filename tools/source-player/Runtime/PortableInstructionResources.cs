using System.Globalization;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

// These are managed source operations on the admitted authoring atlas, not an
// unmanaged Goose ABI, Texus/Composit metric reconstruction or native pixel claim.
public sealed class PortableInstructionResources : ISourceInstructionResources
{
    private readonly SourceInterfaceData hud;
    private readonly SourceInstructionRaster raster;
    private readonly SourceInstructionTextProgram pages;
    private readonly SourceKickoffInstructionHudBindings child;
    public ISourceKickoffInstructionResources Kickoff { get; }
    public const string SourceBlob = "e955e1c98a0fd0123ce0b865ddafdfd7c32c59d4";

    private static readonly string[][] PageLines = {
        new[] { "RULES:", "30 YARDS", "NEEDED FOR FIRST DOWN", "CLOCK STOPS AFTER EACH PLAY",
            "FIELD GOALS & PUNTING ALLOWED", "PASS INTERFERENCE IS OKAY", "NO PENALTIES" },
        new[] { "HOW TO PASS:", "PUSH STICK LEFT TO HIGHLIGHT", "LEFT MOST RECEIVER ON FIELD",
            "PUSH STICK RIGHT TO HIGHLIGHT", "RIGHT MOST RECEIVER ON FIELD",
            "PUSH STICK UP/DOWN TO HIGHLIGHT", "CENTER RECEIVER ON FIELD" },
        new[] { "HOW TO AUDIBLE:", "DOUBLE TAP THE TURBO", "BUTTON TO ACTIVATE",
            "PUSH STICK UP OR DOWN TO", "SELECT DIFFERENT PLAYS", "AUDIBLES CAN BE SET IN", "CREATE A PLAY" },
        new[] { "GENERAL HINTS:", "WAIT FOR A RECEIVER TO GET OPEN", "BEFORE PASSING TO HIM",
            "USE CHANGE PLAYER BUTTON, THEN", "PRESS THE TACKLE BUTTON",
            "KNOCK RECEIVER DOWN JUST AS HE", "CATCHES BALL TO CAUSE A BOBBLE" }
    };
    private static readonly int[][] PageSites = {
        new[] { 4943, 4951, 4954, 4957, 4960, 4963, 4966 },
        new[] { 4997, 5005, 5007, 5010, 5012, 5015, 5017 },
        new[] { 5044, 5052, 5054, 5057, 5059, 5062, 5064 },
        new[] { 5091, 5099, 5101, 5104, 5106, 5109, 5111 }
    };
    private static readonly string[][] ChildLines = {
        new[] { "PLAYER %d" }, new[] { "HAS BALL" },
        new[] { "USE TURBO", "TO RUN FASTER " },
        new[] { "DOUBLE TAP", "TURBO BUTTON", "TO SPIN." },
        new[] { "TURBO + BLOCK", "TO STIFF ARM" },
        new[] { "TURBO + JUMP", "TO HURDLE", "OPPONENT." },
        new[] { "TURBO + JUMP", "DIVES AT", "DEFENDERS. USE", "THIS TO HELP", "YOUR TEAMMATE", "GAIN EXTRA", "YARDS." },
        new[] { "TRY TO", "GET INTO", "POSITION FOR", "A LATERAL." },
        new[] { "PRESS RED", "BUTTON TO", "TAKE CONTROL", "OF THE CLOSET", "DEFENDER TO", "THE BALL", "CARRIER." },
        new[] { "TURBO + JUMP", "DIVE TACKLES", "OPPONENT." }
    };
    private static readonly int[][] ChildSites = {
        new[] { 4583 }, new[] { 4591 }, new[] { 4596, 4598 }, new[] { 4601, 4603, 4605 },
        new[] { 4608, 4610 }, new[] { 4613, 4615, 4617 },
        new[] { 4627, 4629, 4631, 4633, 4635, 4637, 4639 }, new[] { 4642, 4644, 4646, 4648 },
        new[] { 4656, 4658, 4660, 4662, 4664, 4666, 4668 }, new[] { 4671, 4673, 4675 }
    };
    private static readonly (int Site, string Format, string Text, SourceInstructionFont Font)[] IntroPrints = {
        (1075, "\u001b%dj\u001b%dcGENERAL INFO", "GENERAL INFO", SourceInstructionFont.Bast18),
        (1081, "\u001b%dj\u001b%dcHIDE CURSOR BY QUICKLY TAPPING", "HIDE CURSOR BY QUICKLY TAPPING", SourceInstructionFont.Bast10),
        (1083, "\u001b%dj\u001b%dcUP TWICE USING JOYSTICK", "UP TWICE USING JOYSTICK", SourceInstructionFont.Bast10),
        (1086, "\u001b%dj\u001b%dcBLUE BUTTON CHOOSES PLAY", "BLUE BUTTON CHOOSES PLAY", SourceInstructionFont.Bast10),
        (1088, "\u001b%dj\u001b%dcRED BUTTON FLIPS PLAY", "RED BUTTON FLIPS PLAY", SourceInstructionFont.Bast10),
        (1090, "\u001b%dj\u001b%dcWHITE BUTTON CHANGES PAGE", "WHITE BUTTON CHANGES PAGE", SourceInstructionFont.Bast10),
        (1099, "\u001b%dj\u001b%dcPAGE 1", "PAGE 1", SourceInstructionFont.Bast10),
        (1104, "\u001b%dj\u001b%dcPAGE 1", "PAGE 1", SourceInstructionFont.Bast10),
        (1124, "\u001b%djOFFENSE", "OFFENSE", SourceInstructionFont.Bast18),
        (1143, "\u001b%djDEFENSE", "DEFENSE", SourceInstructionFont.Bast18),
        (1162, "\u001b%djOFFENSE", "OFFENSE", SourceInstructionFont.Bast18),
        (1180, "\u001b%djDEFENSE", "DEFENSE", SourceInstructionFont.Bast18)
    };

    public PortableInstructionResources(SourceInterfaceData hud, SourceInterfaceMovementBootstrap bootstrap)
    {
        this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
        ArgumentNullException.ThrowIfNull(bootstrap);
        DataCatalog.Require(bootstrap.HorizontalResolution == 512 && bootstrap.VerticalResolution == 384,
            "Instruction low-resolution FONTID_BAST23/FONTID_BAST10LOW resources are not in approved563; no font substitution.");
        raster = new SourceInstructionRaster(512, 384, 256, 192);
        foreach (string font in new[] { "bast18", "bast10", "bast8t" }) hud.Font(font);
        ResolvePlate("playresult", "SCR_PLATE_TID");
        var interpreter = new SourcePrintInterpreter();
        var formats = new List<SourceInstructionFormatSlot>();
        for (int page = 0; page < 4; page++)
        for (int line = 0; line < 7; line++)
        {
            var alignment = page == 0 && line == 1 ? SourceInstructionJustification.Left : SourceInstructionJustification.Center;
            var request = new SourceInstructionTextRequest((SourceInstructionPage)page, line, alignment);
            // Numeric SDK justification bits are deliberately not asserted. The
            // exact ESC %dj operation consumes the typed source symbols instead.
            formats.Add(new SourceInstructionFormatSlot(request.Page, line, PageSites[page][line],
                "\u001b%dj" + PageLines[page][line], request.NativeSecondArgumentSymbols, 0));
        }
        pages = new SourceInstructionTextProgram(formats, interpreter);
        var childFormats = new List<SourceKickoffInstructionFormatSlot>();
        for (int kind = 0; kind < ChildSites.Length; kind++)
        for (int ordinal = 0; ordinal < ChildSites[kind].Length; ordinal++)
            childFormats.Add(new SourceKickoffInstructionFormatSlot((SourceKickoffInstructionChildTextKind)kind,
                ordinal + 1, ChildSites[kind][ordinal], "\u001b%dj" + ChildLines[kind][ordinal],
                "HJUST_CENTER|VJUST_CENTER", 0));
        child = new SourceKickoffInstructionHudBindings(childFormats, interpreter,
            new Dictionary<SourceKickoffInstructionChildFont, SourceInstructionFontResource> {
                { SourceKickoffInstructionChildFont.Bast10, new SourceInstructionFontResource("bast10", "interface") },
                { SourceKickoffInstructionChildFont.Bast8T, new SourceInstructionFontResource("bast8t", "interface") }
            },
            new Dictionary<SourceKickoffInstructionChildColor, uint> {
                { SourceKickoffInstructionChildColor.NativeZero, 0 },
                { SourceKickoffInstructionChildColor.LightBlue, 0xff8080ff },
                { SourceKickoffInstructionChildColor.LightRed, 0xffff8080 },
                { SourceKickoffInstructionChildColor.LightGreen, 0xff80ff80 },
                { SourceKickoffInstructionChildColor.LightYellow, 0xffffff80 },
                { SourceKickoffInstructionChildColor.LightCyan, 0xff80ffff }
            }, new ChildPlateLayout(), 0xffffffff, 0x810);
        Kickoff = new ChildResources(this);
        // Validate every admitted print domain, not only the first reachable page.
        for (int page = 0; page < PageLines.Length; page++)
        for (int line = 0; line < PageLines[page].Length; line++)
            foreach (char character in PageLines[page][line].Where(c => c != ' '))
                hud.Font(line == 0 ? "bast18" : "bast10").Glyph(character);
        for (int kind = 0; kind < ChildLines.Length; kind++)
        foreach (string value in ChildLines[kind])
            foreach (char character in value.Replace("%d", "1234", StringComparison.Ordinal).Where(c => c != ' '))
                hud.Font(kind < 2 ? "bast10" : "bast8t").Glyph(character);
        foreach (var slot in IntroPrints)
            foreach (char character in slot.Text.Where(c => c != ' '))
                hud.Font(PageFont(slot.Font)).Glyph(character);
    }

    public SourceInstructionRaster ReadRaster() => raster;
    // Portable ARGB palette for source LT_* symbols; not a numeric SDK-color witness.
    public uint ResolveColor(SourceInstructionColor color) => color switch {
        SourceInstructionColor.White => 0xffffffff,
        SourceInstructionColor.LightRed => 0xffff8080,
        SourceInstructionColor.LightYellow => 0xffffff80,
        _ => throw Missing("Unbound source instruction color " + color)
    };
    public SourceInstructionImage ResolvePlate(string imageSymbol, string textureSymbol)
    {
        if (imageSymbol != "playresult" || textureSymbol != "SCR_PLATE_TID")
            throw Missing("Unbound source plate " + imageSymbol + "/" + textureSymbol);
        SourceInterfaceImage image = hud.Image(imageSymbol);
        SourceInterfaceTexture atlas = hud.Textures["interface"];
        return new SourceInstructionImage(image.Id, "interface", "interface-white", image.Width, image.Height,
            image.AnchorX, image.AnchorY, image.X / (double)atlas.Width, image.Y / (double)atlas.Height,
            (image.X + image.Width) / (double)atlas.Width, (image.Y + image.Height) / (double)atlas.Height);
    }
    public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
        SourceInstructionRaster sourceRaster, SourceInstructionPoint point, float widthScale, float heightScale)
    {
        RequireRaster(sourceRaster);
        return new[] { Quad(image, point.Depth, 0xffffffff,
            point.X - image.AnchorX * widthScale, sourceRaster.Height - point.Y - image.AnchorY * heightScale,
            image.Width * widthScale, image.Height * heightScale) };
    }
    public SourceInstructionTextLayout FormatText(SourceInstructionTextRequest request, SourceInstructionFont font,
        uint color, SourceInstructionRaster sourceRaster, SourceInstructionPoint point)
    {
        RequireRaster(sourceRaster);
        SourceInstructionFormattedText result = pages.Evaluate(request, font, color, sourceRaster, point);
        var graphics = new List<SourceInstructionGraphic>();
        foreach (SourceInstructionTextRun run in result.Runs)
            Append(graphics, PageFont(run.Font), run.Text, run.Color, point, run.OffsetX, run.OffsetY, run.Justification);
        return new SourceInstructionTextLayout(result.StateColor, graphics);
    }
    public SourceInstructionTextLayout FormatPlaySelectionIntroduction(int originalPrintLine, string originalFormat,
        SourceInstructionFont font, uint evaluatedSourceColor, SourceInstructionPoint point)
    {
        var matches = IntroPrints.Where(slot => slot.Site == originalPrintLine).ToArray();
        if (matches.Length != 1 || matches[0].Format != originalFormat || matches[0].Font != font)
            throw Missing("Unbound original playsel.c introduction print/format/font at " + originalPrintLine + ".");
        // Only these twelve literal source calls are bound. The source caller
        // supplies its evaluated j/c operands; this is not a general printf or
        // an inferred numeric SDK justification/color interpreter.
        var graphics = new List<SourceInstructionGraphic>();
        Append(graphics, PageFont(font), matches[0].Text, evaluatedSourceColor, point, 0, 0,
            SourceInstructionJustification.Center);
        return new SourceInstructionTextLayout(evaluatedSourceColor, graphics);
    }
    private static string PageFont(SourceInstructionFont font) => font switch {
        SourceInstructionFont.Bast18 => "bast18", SourceInstructionFont.Bast10 => "bast10",
        _ => throw Missing("Missing actual low-resolution font " + font)
    };
    private void RequireRaster(SourceInstructionRaster value)
    {
        if (value.Width != raster.Width || value.Height != raster.Height ||
            value.HalfWidth != raster.HalfWidth || value.HalfHeight != raster.HalfHeight)
            throw Missing("Instruction geometry must retain its admitted source raster, not browser dimensions.");
    }
    private void Append(List<SourceInstructionGraphic> output, string fontId, string text, uint color,
        SourceInstructionPoint point, float dx, float dy, SourceInstructionJustification alignment)
    {
        SourceInterfaceFont font = hud.Font(fontId);
        SourceInterfaceTexture atlas = hud.Textures["interface"];
        double leftShift = alignment == SourceInstructionJustification.Left ? Math.Truncate(SourceHudText.Width(font, text) / 2) : 0;
        foreach (SourceHudGlyph glyph in SourceHudText.Center(font, text, point.X + dx, raster.Height - point.Y - dy, 1))
        {
            SourceInterfaceImage image = glyph.Image;
            var binding = new SourceInstructionImage(image.Id, "interface", "interface-white", image.Width, image.Height,
                image.AnchorX, image.AnchorY, image.X / (double)atlas.Width, image.Y / (double)atlas.Height,
                (image.X + image.Width) / (double)atlas.Width, (image.Y + image.Height) / (double)atlas.Height);
            output.Add(Quad(binding, point.Depth, color, glyph.Rect.X + leftShift, glyph.Rect.Y, glyph.Rect.Width, glyph.Rect.Height));
        }
    }
    private static SourceInstructionGraphic Quad(SourceInstructionImage image, float depth, uint color,
        double x, double y, double width, double height) =>
        new(image.Texture, image.WhiteTexture, image.Symbol, color, depth, new[] {
            new SourceInstructionVertex(x, y, image.U0, image.V1),
            new SourceInstructionVertex(x + width, y, image.U1, image.V1),
            new SourceInstructionVertex(x + width, y + height, image.U1, image.V0),
            new SourceInstructionVertex(x, y, image.U0, image.V1),
            new SourceInstructionVertex(x + width, y + height, image.U1, image.V0),
            new SourceInstructionVertex(x, y + height, image.U0, image.V0) });
    private sealed class ChildResources : ISourceKickoffInstructionResources
    {
        private readonly PortableInstructionResources owner;
        internal ChildResources(PortableInstructionResources owner) { this.owner = owner; }
        public SourceInstructionRaster ReadRaster() => owner.raster;
        public uint ResolveColor(SourceKickoffInstructionChildColor color) => owner.child.ResolveColor(color);
        public uint ResolveObjectId(string symbol, int slot) => owner.child.ResolveObjectId(symbol, slot);
        public SourceInstructionImage ResolvePlate(string image, string texture) => owner.ResolvePlate(image, texture);
        public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
            SourceInstructionRaster raster, SourceInstructionPoint point, float angle, float heightScale)
        {
            owner.RequireRaster(raster);
            return new[] { new SourceInstructionGraphic(image.Texture, image.WhiteTexture, image.Symbol,
                owner.child.PlateColor, point.Depth, owner.child.PlateLayout.Layout(image, raster, point, angle, heightScale)) };
        }
        public SourceInstructionTextLayout FormatText(SourceKickoffInstructionChildTextRequest request,
            SourceKickoffInstructionChildFont font, uint color, SourceInstructionRaster raster,
            SourceInstructionPoint point, float increment)
        {
            owner.RequireRaster(raster);
            SourceKickoffInstructionFormattedText text = owner.child.Evaluate(request, font, color, raster, point, increment);
            var graphics = new List<SourceInstructionGraphic>();
            foreach (SourceKickoffInstructionTextRun run in text.Runs)
                owner.Append(graphics, owner.child.ResolveFont(run.Font).Font, run.Text, run.Color,
                    point, run.OffsetX, run.OffsetY, run.Justification);
            return new SourceInstructionTextLayout(text.StateColor, graphics);
        }
    }
    private sealed class SourcePrintInterpreter : ISourceInstructionNativeTextFormatter, ISourceKickoffInstructionNativeTextFormatter
    {
        private static string Justify(string format, string symbols)
        {
            if (!format.StartsWith("\u001b%dj", StringComparison.Ordinal) ||
                symbols != "HJUST_CENTER|VJUST_CENTER" && symbols != "HJUST_LEFT|VJUST_CENTER")
                throw Missing("Unimplemented original print operation; expected the source ESC %dj justification.");
            return format.Substring(4);
        }
        public SourceInstructionFormattedText Evaluate(SourceInstructionFormatCall call)
        {
            string text = Justify(call.Slot.OriginalFormat, call.Slot.NativeSecondArgumentSymbols);
            if (text.Contains('%') || text.Any(char.IsControl)) throw Missing("Unbound page print operand.");
            return new SourceInstructionFormattedText(call.Color, new[] { new SourceInstructionTextRun(
                text, call.Font, call.Color, 0, 0, call.Request.Justification) });
        }
        public SourceKickoffInstructionFormattedText Evaluate(SourceKickoffInstructionFormatCall call)
        {
            if (call.TextZIncrement != 0) throw Missing("Unbound child text-depth operation.");
            string text = Justify(call.Slot.OriginalFormat, call.Slot.NativeSecondArgumentSymbols);
            if (call.Request.Kind == SourceKickoffInstructionChildTextKind.Header)
            {
                if (text != "PLAYER %d" || !call.Request.DisplayPlayerNumber.HasValue)
                    throw Missing("Unbound source PLAYER decimal operand.");
                text = "PLAYER " + call.Request.DisplayPlayerNumber.Value.ToString(CultureInfo.InvariantCulture);
            }
            if (text.Contains('%') || text.Any(char.IsControl)) throw Missing("Unbound child print operand.");
            return new SourceKickoffInstructionFormattedText(call.Color, new[] { new SourceKickoffInstructionTextRun(
                text, call.Font, call.Color, 0, 0, SourceInstructionJustification.Center) });
        }
    }
    private sealed class ChildPlateLayout : ISourceKickoffInstructionNativePlateLayout
    {
        public IReadOnlyList<SourceInstructionVertex> Layout(SourceInstructionImage image, SourceInstructionRaster raster,
            SourceInstructionPoint point, float angle, float heightScale)
        {
            if (angle != 256.04f || heightScale != 1.45f)
                throw Missing("Unbound kickoff sprite transform; retain the actual source z_angle/h_scale.");
            // beginobj's unit width and the child's radian rotation are separate
            // from h_scale. Use admitted authoring anchors/dimensions, not 212x79.
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            SourceInstructionVertex Vertex(double x, double y, double u, double v) =>
                new(point.X + cos * x - sin * y, raster.Height - (point.Y + sin * x + cos * y), u, v);
            double left = -image.AnchorX, right = image.Width - image.AnchorX;
            double top = image.AnchorY * heightScale, bottom = (image.AnchorY - image.Height) * heightScale;
            var a = Vertex(left, top, image.U0, image.V1);
            var b = Vertex(right, top, image.U1, image.V1);
            var c = Vertex(right, bottom, image.U1, image.V0);
            var d = Vertex(left, bottom, image.U0, image.V0);
            return new[] { a, b, c, a, c, d };
        }
    }
    internal static SourceInstructionPresentationException Missing(string message) =>
        new(SourceInstructionPresentationError.MissingResource, message);
}

public sealed class PortableInstructionSound
{
    public SourceProcessSoundCall Output { get; }
    public long OutputSequence => Output.OutputSequence;
    public long SourceTick => Output.SourceTick;
    public long ProcessSerial => Output.ProcessSerial;
    public string Process => Output.Process;
    public int DispatchOrder => Output.DispatchOrder;
    public int SubcallOrder => Output.SubcallOrder;
    public string Operand => Output.Operand;
    public string Bank => Output.Bank;
    public int? Playlist => Output.PlaylistId;
    public int Volume => checked((int)Output.Volume);
    public int Pan => checked((int)Output.Pan);
    public int Priority => checked((int)Output.PriorityWord);
    public bool Direct => Output.Operand == "tick_sndn";
    public bool PcmOrDspPlayback => false;
    internal PortableInstructionSound(SourceProcessSoundCall output)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }
}

public sealed class PortableInstructionRuntime : IDisposable
{
    public const string Policy = "portable-source-instructions-authoring-atlas/v1";
    private readonly GameSimulation game;
    private readonly SourceCabinetSession session;
    private readonly LiveInputs inputs;
    private readonly Action<PortableInstructionSound> recordSound;
    private bool disposed;
    private bool introductionBound;
    private readonly IntroductionPresentation introduction;
    public PortableInstructionResources Resources { get; }
    public SourceInstructionConsumer Consumer { get; }
    public SourceKickoffInstructionPresentation KickoffPresentation { get; }
    public IReadOnlyList<SourceInstructionGraphic> IntroductionFrame => introduction.Frame();
    public int ChildReturns => inputs.ChildReturns;
    public int FlashReturns => inputs.FlashReturns;
    public uint? CurrentSwitchWord => inputs.CurrentSwitchWord;
    public long SourceStepsObserved { get; private set; }
    private long observedTick;
    public PortableInstructionRuntime(GameSimulation game, PlayerData data, SourceCabinetSession session,
        Action<PortableInstructionSound> recordSound)
    {
        this.game = game ?? throw new ArgumentNullException(nameof(game));
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.recordSound = recordSound ?? throw new ArgumentNullException(nameof(recordSound));
        ArgumentNullException.ThrowIfNull(data);
        DataCatalog.Require(game.Compatibility == SourceGameplayCompatibility.Portable &&
            data.Catalog.Profile.UsesPreSnapSwitchServices && game.SourceDefensivePreSnapSwitchEnabled &&
            game.SourceTick == 0 && game.SourceCustomPlaySession != null &&
            ReferenceEquals(session.LastConfiguration, ((ISourceMatchState)game.State).Match.Configuration) &&
            (session.LastConfiguration.StationLayout == SourceStationLayout.FourStation ||
             session.LastConfiguration.StationLayout == SourceStationLayout.TwoStation),
            "Portable instructions require an actual fresh deliberate known-new source cabinet match, not an unbound backdrop.");
        Resources = new PortableInstructionResources(data.Hud, data.Bootstrap);
        inputs = new LiveInputs(game, session, Resources);
        var output = new SoundOutput(this);
        Consumer = new SourceInstructionConsumer(new ProgramBinding(game), Resources, new PlateFlashBinding(), output);
        KickoffPresentation = new SourceKickoffInstructionPresentation(Resources.Kickoff, output);
        introduction = new IntroductionPresentation(this);
    }
    public void Bind()
    {
        Consumer.BindSourceKickoffInstructionOwnedHelpers(unchecked((int)0xf0f0f0f0u),
            new SourceInstructionProcessIds(0x0f9b, 0x0502, 0x1400, 0x0f10), inputs, session,
            0x0f9a, 4, 1, 2, inputs, inputs,
            new SourceKickoffInstructionChildOperands(false, 0, 0, 0, 1), KickoffPresentation,
            new SourceKickoffInstructionOwnedHelpers(0x10, 0x20, 0x40, SourceFireIds.FlashWhite, inputs, inputs, inputs), inputs);
        BindIntroduction();
    }
    private void BindIntroduction()
    {
        game.BindSourcePlaySelectionIntroduction(inputs, introduction);
        introductionBound = true;
    }
    public void AfterSourceStep()
    {
        if (game.SourceTick == observedTick) return;
        DataCatalog.Require(game.SourceTick == observedTick + 1, "Instruction presentation missed a real source wake.");
        introduction.Synchronize(game.SourceTick, true);
        Consumer.AfterSourceStep(game.SourceTick);
        observedTick = game.SourceTick; SourceStepsObserved++;
    }
    public IReadOnlyList<SourceInstructionGraphic> Frame()
    {
        if (Consumer.Presentation.Failure != null) throw Consumer.Presentation.Failure;
        if (KickoffPresentation.Failure != null) throw KickoffPresentation.Failure;
        return KickoffPresentation.Frame().Concat(introduction.Frame()).Concat(Consumer.Presentation.Frame()).ToArray();
    }
    public void OnSourceProgramReset()
    {
        KickoffPresentation.Reset();
        introduction.Reset();
        inputs.Reset();
        observedTick = SourceStepsObserved = 0;
        Consumer.OnSourceProgramReset();
        if (!introductionBound) BindIntroduction();
    }
    public object Describe() => new {
        policy = Policy, bound = Consumer.HasSourceAdmission, idiotBoxesShown = Consumer.IdiotBoxesShown,
        participatingStationMask = session.ReadPlayerStatus(),
        fourPlayerVersion = inputs.ReadFourPlayerVersion(),
        gamesPlayed = session.Records == null ? session.GamesPlayed : null,
        experienceSource = session.Records == null ? "portable-in-memory-session" : "actual-selected-record",
        showPlaySelectionInstruction = session.ReadPlaySelectionInstructionGate(),
        sourceStepsObserved = SourceStepsObserved, currentSwitchWord = CurrentSwitchWord,
        physicalSourceTick = game.ConsumedSourceSwitches?.SourceTick,
        physicalInputEpoch = game.ConsumedSourceSwitches?.InputEpoch, sourceInputEpoch = game.SourceInputEpoch,
        program = Consumer.Program, childReturns = ChildReturns, flashReturns = FlashReturns,
        introduction = game.SourcePlaySelectionIntroduction, introductionGraphics = introduction.Frame().Count,
        kickoffGraphics = KickoffPresentation.Frame().Count,
        pageGraphics = Consumer.Presentation.Frame().Count,
        resourceOrigin = "approved563 playresult + bast18/bast10/bast8t; portable authored metrics/palette/radian transform",
        sound = "same actual source-stamped process calls, merged once at Host output boundary; no bank samples admitted",
        nativeNumericOrPixelEquivalent = false
    };
    public void Dispose()
    {
        if (disposed) return;
        Consumer.Dispose(); KickoffPresentation.Dispose(); introduction.Dispose(); inputs.Reset(); disposed = true;
    }
    private sealed class ProgramBinding : ISourceInstructionAdmissionProgramBinding
    {
        private readonly GameSimulation game;
        internal ProgramBinding(GameSimulation game) { this.game = game; }
        public SourceInstructionSnapshot Current => game.SourceInstructionProgram;
        public IReadOnlyList<SourcePossessionProcessRecord> Records => game.SourcePossessionProcessRecords;
        public long Invoke(SourceInstructionInvocation invocation) => game.InvokeSourceInstructions(invocation);
        public void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding) => game.BindSourceInstructionAdmission(binding);
    }
    private sealed class SoundOutput : ISourceInstructionSoundPlayer, ISourceKickoffInstructionSoundPlayer
    {
        private readonly PortableInstructionRuntime owner;
        internal SoundOutput(PortableInstructionRuntime owner) { this.owner = owner; }
        public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context)
        {
            owner.recordSound(new PortableInstructionSound(
                owner.game.TakeExecutingSourceInstructionSound(sound, context)));
        }
        public void Play(SourceKickoffInstructionChildSound sound)
        {
            owner.recordSound(new PortableInstructionSound(owner.game.TakeExecutingSourceKickoffInstructionSound(sound)));
        }
    }
    private sealed class PlateFlashBinding : ISourceInstructionObjectFlashBinding
    {
        public ISourceInstructionObjectFlashProgram Create(ISourceInstructionPlate plate, int flashes, int delay, int fourth)
        {
            if (plate is not SourceInstructionPlateState || flashes != 2 || delay != 3 || fourth != 0)
                throw PortableInstructionResources.Missing("Unbound source flash_obj_white operands.");
            return new PlateFlash(flashes, delay);
        }
        // select.c:3755..3818: white/chroma sleep, restore/disable sleep, repeat,
        // then restore and return. The presentation host supplies birth/suspension.
        private sealed class PlateFlash : ISourceInstructionObjectFlashProgram
        {
            private readonly int count, delay;
            private int step;
            internal PlateFlash(int count, int delay) { this.count = count; this.delay = delay; }
            public SourceInstructionPlateFlashStep Advance()
            {
                if (step == count * 2) return new SourceInstructionPlateFlashStep(false, false, true, 0);
                bool white = step++ % 2 == 0;
                return new SourceInstructionPlateFlashStep(white, white, false, delay);
            }
        }
    }
    private sealed class IntroductionPresentation : ISourcePlaySelectionIntroductionEffects, IDisposable
    {
        private abstract class Owned
        {
            internal bool Deleted;
            internal long Generation;
            internal IntroductionPresentation Owner;
            internal IReadOnlyList<SourceInstructionGraphic> Graphics;
        }
        private sealed class Plate : Owned, ISourceInstructionPlate
        {
            internal SourceInstructionImage Image;
            internal SourceInstructionPoint Point;
            internal float? Width, Height;
            internal uint? Id;
        }
        private sealed class Text : Owned, ISourceInstructionText
        {
            internal int Id;
            internal uint Color;
            internal uint? Override;
        }
        private sealed class Flasher : ISourceInstructionFlasher
        {
            internal IntroductionPresentation Owner;
            internal long Serial, Generation;
            internal bool Killed, Entered, WhiteNext = true;
            internal Text First, Second;
            internal uint FirstColor, SecondColor;
        }
        private readonly PortableInstructionRuntime owner;
        private readonly List<Owned> objects = new();
        private readonly List<Flasher> flashers = new();
        private SourcePortableProcessHost host = new();
        private SourceInstructionFont? font;
        private SourcePlaySelectionIntroductionColor? color;
        private SourceInstructionPoint? point;
        private int? stringId;
        private long generation, serial, prepared = -1, completed = -1, recordTick = -1;
        private int cursor;
        private bool disposed;
        private Exception failure;
        internal IntroductionPresentation(PortableInstructionRuntime owner) { this.owner = owner; }
        private static uint Color(SourcePlaySelectionIntroductionColor value) => value switch {
            SourcePlaySelectionIntroductionColor.LightYellow => 0xffffff80,
            SourcePlaySelectionIntroductionColor.LightBlue => 0xff8080ff,
            SourcePlaySelectionIntroductionColor.LightRed => 0xffff8080,
            SourcePlaySelectionIntroductionColor.LightGreen => 0xff80ff80,
            SourcePlaySelectionIntroductionColor.White => 0xffffffff,
            _ => throw PortableInstructionResources.Missing("Unknown source introduction color.")
        };
        internal void Synchronize(long tick, bool finish)
        {
            Alive();
            if (prepared < 0)
            {
                if (finish) return;
                host.BeginTick(1); prepared = tick; completed = tick - 1;
            }
            if (tick < prepared) throw PortableInstructionResources.Missing("Introduction source epoch changed without actual reset.");
            while (prepared < tick)
            {
                Finish();
                prepared++; host.BeginTick(host.SourceTick + 1);
            }
            var records = owner.game.SourcePossessionProcessRecords;
            if (recordTick != tick) { recordTick = tick; cursor = 0; }
            while (cursor < records.Count && records[cursor].SourceTick <= tick)
            {
                SourcePossessionProcessRecord record = records[cursor++];
                if (record.Operation == "SuspendMask") host.SuspendMultiple(record.Id, record.Value);
                else if (record.Operation == "ResumeMask") host.ResumeMultiple(record.Id, record.Value);
                else if (record.Operation == "ResumeAllCall") host.ResumeAll();
            }
            if (finish) Finish();
        }
        private void Finish()
        {
            if (completed == prepared) return;
            foreach (Flasher value in flashers)
            {
                if (value.Killed || !host.Visit(value.Serial)) continue;
                try
                {
                    Require<Text>(value.First); Require<Text>(value.Second);
                    if (!value.Entered)
                    {
                        value.FirstColor = value.First.Color;
                        value.SecondColor = value.Second.Color; value.Entered = true;
                    }
                    else
                    {
                        value.First.Color = value.WhiteNext ? 0xffffffff : value.FirstColor;
                        value.Second.Color = value.WhiteNext ? 0xffffffff : value.SecondColor;
                        value.First.Override = value.First.Color; value.Second.Override = value.Second.Color;
                        value.WhiteNext = !value.WhiteNext;
                    }
                    host.Sleep(7);
                }
                finally { host.Complete(); }
            }
            completed = prepared;
        }
        private void Before()
        {
            Alive();
            SourcePortableProcessHost source = owner.game.Model.SourceProcesses;
            if (source.Caller == 0 || source.Require(source.Caller).Function != "game_proc" ||
                source.Require(source.Caller).LastWakeTick != owner.game.SourceTick)
                throw PortableInstructionResources.Missing("Intro effects require the actual inline game_proc continuation.");
            Synchronize(owner.game.SourceTick, false);
        }
        private void Run(Action operation) => Run<object>(() => { operation(); return null; });
        private T Run<T>(Func<T> operation)
        {
            try { Before(); return operation(); }
            catch (Exception error) { failure = error; Cleanup(); throw; }
        }
        public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint position) =>
            Run<ISourceInstructionPlate>(() => {
                var value = new Plate { Owner = this, Generation = generation,
                    Image = owner.Resources.ResolvePlate(image, texture), Point = position };
                objects.Add(value); return value;
            });
        public void SetPlateWidthScale(ISourceInstructionPlate plate, float scale) =>
            Run(() => { Scale(scale); Require<Plate>(plate).Width = scale; });
        public void SetPlateHeightScale(ISourceInstructionPlate plate, float scale) =>
            Run(() => { Scale(scale); Require<Plate>(plate).Height = scale; });
        public void SetPlateId(ISourceInstructionPlate plate, string symbol) => Run(() => {
            if (symbol != "OID_MESSAGE") throw PortableInstructionResources.Missing("Unbound introduction object ID.");
            Require<Plate>(plate).Id = 0x800;
        });
        public void GeneratePlateVertices(ISourceInstructionPlate plate) => Run(() => {
            Plate value = Require<Plate>(plate);
            if (!value.Width.HasValue || !value.Height.HasValue || !value.Id.HasValue)
                throw PortableInstructionResources.Missing("Introduction plate scale/ID must precede vertex generation.");
            value.Graphics = owner.Resources.GeneratePlate(value.Image, owner.Resources.ReadRaster(),
                value.Point, value.Width.Value, value.Height.Value);
        });
        public void SetTextFont(SourceInstructionFont value) => Run(() => { font = value; });
        public void SetStringId(int value) => Run(() => { stringId = value; });
        public void SetTextColor(SourcePlaySelectionIntroductionColor value) => Run(() => { Color(value); color = value; });
        public void SetTextPosition(SourceInstructionPoint value) => Run(() => { point = value; });
        public void SetTextZIncrement(float increment) => Run(() => {
            if (increment != 0) throw PortableInstructionResources.Missing("Only the source intro's explicit zero text increment is bound.");
        });
        public ISourceInstructionText Print(SourcePlaySelectionIntroductionPrint request) =>
            Run<ISourceInstructionText>(() => {
                if (!font.HasValue || !stringId.HasValue || !point.HasValue ||
                    request.Justification != SourceInstructionJustification.Center ||
                    request.NativeSecondArgumentSymbols != "HJUST_CENTER|VJUST_CENTER" ||
                    request.ColorOperand.HasValue != request.OriginalFormat.StartsWith("\u001b%dj\u001b%dc", StringComparison.Ordinal))
                    throw PortableInstructionResources.Missing("Incomplete source introduction print state/operands: " +
                        request.NativePrintLine + "/" + request.OriginalFormat + "; font=" + font +
                        "; id=" + stringId + "; position=" + point.HasValue + "; colorOperand=" + request.ColorOperand +
                        "; justification=" + request.NativeSecondArgumentSymbols + ".");
                var selected = request.ColorOperand ?? color ??
                    throw PortableInstructionResources.Missing("A source j print needs its selected text color.");
                SourceInstructionTextLayout layout = owner.Resources.FormatPlaySelectionIntroduction(
                    request.NativePrintLine, request.OriginalFormat, font.Value, Color(selected), point.Value);
                var value = new Text { Owner = this, Generation = generation, Id = stringId.Value,
                    Color = layout.StateColor, Graphics = layout.Graphics };
                objects.Add(value); return value;
            });
        public ISourceInstructionFlasher CreateTextFlasher(string name, int processId, ISourceInstructionText first,
            ISourceInstructionText second, int third, int fourth) => Run<ISourceInstructionFlasher>(() => {
                if (name != "pflash" || processId != 0 || third != 0 || fourth != 0)
                    throw PortableInstructionResources.Missing("Unknown source introduction flasher operands.");
                var value = new Flasher { Owner = this, Generation = generation, Serial = ++serial,
                    First = Require<Text>(first), Second = Require<Text>(second) };
                host.Create(value.Serial, processId, "flash_text_proc", 0);
                flashers.Add(value); return value;
            });
        public void KillTextFlasher(ISourceInstructionFlasher handle, int signal) => Run(() => {
            if (handle is not Flasher value || value.Owner != this || value.Generation != generation || value.Killed || signal != 0)
                throw PortableInstructionResources.Missing("A live source introduction flasher return and signal0 are required.");
            Kill(value);
        });
        public void DeleteStrings(int id, uint mask) => Run(() => {
            foreach (Text value in objects.OfType<Text>())
                if (((uint)value.Id & mask) == ((uint)id & mask)) value.Deleted = true;
        });
        public void DeletePlateClass(string symbol, uint mask) => Run(() => {
            if (symbol != "OID_MESSAGE") throw PortableInstructionResources.Missing("Unbound introduction plate class.");
            foreach (Plate value in objects.OfType<Plate>())
                if (value.Id.HasValue && (value.Id.Value & mask) == (0x800u & mask)) value.Deleted = true;
        });
        public void PlaySound(SourcePlaySelectionIntroductionSound sound) => Run(() =>
            owner.recordSound(new PortableInstructionSound(owner.game.TakeExecutingSourcePlaySelectionIntroductionSound(sound))));
        internal IReadOnlyList<SourceInstructionGraphic> Frame()
        {
            Alive();
            return objects.Where(value => !value.Deleted && value.Graphics != null).SelectMany(value =>
                value.Graphics.Select(graphic => graphic.With(graphic.Texture,
                    value is Text text && text.Override.HasValue ? text.Override.Value : graphic.Color))).ToArray();
        }
        private T Require<T>(object handle) where T : Owned
        {
            if (handle is not T value || value.Owner != this || value.Generation != generation || value.Deleted)
                throw PortableInstructionResources.Missing("A live returned introduction object in this epoch is required.");
            return value;
        }
        private static void Scale(float value)
        {
            if (!float.IsFinite(value) || value <= 0) throw PortableInstructionResources.Missing("Invalid source introduction plate scale.");
        }
        private void Kill(Flasher value) { host.Kill(value.Serial); value.Killed = true; }
        private void Cleanup()
        {
            foreach (Flasher value in flashers) if (!value.Killed) Kill(value);
            foreach (Owned value in objects) value.Deleted = true;
        }
        internal void Reset()
        {
            if (disposed) throw new ObjectDisposedException(nameof(IntroductionPresentation));
            Cleanup(); objects.Clear(); flashers.Clear(); host = new SourcePortableProcessHost(); generation++;
            serial = 0; prepared = completed = recordTick = -1; cursor = 0;
            font = null; color = null; point = null; stringId = null; failure = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            Cleanup(); disposed = true;
        }
        private void Alive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(IntroductionPresentation));
            if (failure != null) throw new InvalidOperationException("Failed source introduction presentation requires actual reset.", failure);
        }
    }
    private sealed class LiveInputs : ISourcePlaySelectionIntroductionInputs, ISourceKickoffInstructionChildInputs,
        ISourceKickoffInstructionPlayerButtons, ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects,
        ISourceKickoffInstructionChildReturnBinding, ISourceKickoffInstructionFlashReturnBinding
    {
        private readonly GameSimulation game;
        private readonly SourceCabinetSession session;
        private readonly PortableInstructionResources resources;
        private readonly List<PlayerObject> captured = new();
        private readonly HashSet<long> returned = new();
        internal int ChildReturns, FlashReturns;
        internal LiveInputs(GameSimulation game, SourceCabinetSession session, PortableInstructionResources resources)
        { this.game = game; this.session = session; this.resources = resources; }
        internal uint? CurrentSwitchWord
        {
            get
            {
                SourceConsumedPhysicalSwitchScan scan = game.ConsumedSourceSwitches;
                return scan == null || scan.SourceTick != game.SourceTick || scan.InputEpoch != game.SourceInputEpoch
                    ? null : Pack(scan);
            }
        }
        private uint ReadActualSwitches() => CurrentSwitchWord ??
            throw PortableInstructionResources.Missing("Instruction input requires this actual consumed source FIFO tick/epoch; no latest-host or zero fallback.");
        private static uint Pack(SourceConsumedPhysicalSwitchScan scan)
        {
            uint value = 0;
            foreach (SourceStationInput station in scan.PhysicalSwitches.Stations)
            {
                if (!station.DigitalCurrent.HasValue)
                    throw PortableInstructionResources.Missing("Consumed physical switch sample lacks authenticated decoded directions.");
                value |= ((uint)station.DigitalCurrent.Value | (uint)station.Held << 4) << (station.StationId * 8);
            }
            return value;
        }
        public int ReadPlayerSwitches() => unchecked((int)ReadActualSwitches());
        public int ReadShowPlyselIdiotBox() => session.ReadPlaySelectionInstructionGate();
        public void WriteShowPlyselIdiotBox(int value) => session.WritePlaySelectionInstructionGate(value);
        public SourceInstructionRaster ReadInstructionRaster() => resources.ReadRaster();
        public int ReadTsec() => ((ISourceGameState)game.State).SourceTicksPerSecond;
        public int ReadIsLowResolution() => 0;
        public int ReadFourPlayerVersion() =>
            session.LastConfiguration.StationLayout == SourceStationLayout.FourStation ? 1 : 0;
        public int ReadPlayerControl(int slot) => game.Model.SourceStationPlayers[slot];
        public int ReadBallCarrier() => game.Model.CarrierId;
        public int ReadPlayerTeam(int player) => (int)Actor(player).Team;
        public int ReadOffensiveSide() => (int)game.Model.Possession;
        public int ReadCurrentButtons(int player) => (int)Actor(player).SourceButtons;
        public void WriteCurrentButtons(int player, int value) { Actor(player).SourceButtons = (SourceDroneButtons)value; }
        public int ReadOldButtons(int player) => (int)Actor(player).SourceOldButtons;
        public void WriteOldButtons(int player, int value) { Actor(player).SourceOldButtons = (SourceDroneButtons)value; }
        public uint ReadCurrentSwitchWord() => ReadActualSwitches();
        public ISourceKickoffPlayerObject ReadPlayerBlock(int player)
        {
            var value = new PlayerObject(Actor(player)); captured.Add(value); return value;
        }
        public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(SourceKickoffInstructionChildInvocation invocation,
            SourceKickoffInstructionChildReturnReason reason)
        {
            DataCatalog.Require(reason == SourceKickoffInstructionChildReturnReason.NormalBodyEnd &&
                game.Model.SourceProcesses.Caller == invocation.Serial && returned.Add(invocation.Serial),
                "Only the actual once-only source child body return may retire its process.");
            ChildReturns++;
            return SourceKickoffInstructionChildReturnAction.ExternallyRetire;
        }
        public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(SourceKickoffInstructionFlashInvocation invocation)
        {
            DataCatalog.Require(game.Model.SourceProcesses.Caller == invocation.Serial && returned.Add(invocation.Serial),
                "Only the actual once-only source flash body return may retire its process.");
            FlashReturns++;
            return SourceKickoffInstructionFlashReturnAction.ExternallyRetire;
        }
        private ActorState Actor(int id)
        {
            DataCatalog.Require(id >= 0 && id < game.Model.Players.Length,
                "Original kickoff tutorial referenced an unavailable player_blocks[" + id + "]; no control fallback.");
            return game.Model.Players[id];
        }
        internal void Reset()
        {
            foreach (PlayerObject value in captured) value.ClearWhite();
            captured.Clear(); returned.Clear(); ChildReturns = FlashReturns = 0;
        }
        private sealed class PlayerObject : ISourceKickoffPlayerObject
        {
            private readonly ActorState actor;
            internal PlayerObject(ActorState actor) { this.actor = actor; }
            public uint ReadFlags() => unchecked((uint)actor.SourceRawFlags);
            public void WriteFlags(uint value)
            {
                DataCatalog.Require(((ReadFlags() ^ value) & ~(uint)SourceFireIds.FlashWhite) == 0,
                    "Instruction player flash cannot rewrite unrelated actual player flags.");
                actor.SourceRawFlags = unchecked((int)value);
            }
            internal void ClearWhite() => actor.SourceRawFlags &= ~SourceFireIds.FlashWhite;
        }
    }
}
