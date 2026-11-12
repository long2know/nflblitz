using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.Encodings.Web;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static partial class Checks
{
    private static PlayerHost InstructionHost(PlayerData data, out RecordingSink sink)
    {
        sink = new RecordingSink();
        return new PlayerHost(data, sink, options: PreviewOptions.FromUrl(
            "?source-gameflow=portable&source-instructions=portable", data.Catalog.Profile.StableId));
    }

    private static bool HasSourceProcess(PlayerHost host, string function) =>
        ((ISourcePossessionState)host.Game.State).Processes.Any(p => p.Function == function);

    private static void InstructionUntil(PlayerHost host, Func<bool> reached, int maximum = 1600,
        bool select = false, int held = 0)
    {
        for (int step = 0; step < maximum && !reached(); step++)
        {
            int button = held;
            if (CabinetFlow(host).Stage is SourceCabinetFlowStage.ResultWait or SourceCabinetFlowStage.EndOfQuarter)
                button = 7;
            else if (select && host.Game.State.Phase == GamePhase.PlaySelect)
                button = step % 2 == 0 ? 2 : 0;
            CabinetRaw(host, new[] { button, 0, 0, 0 });
            CabinetRender(host, 1);
        }
        Check(reached(), "Actual instruction source route exceeded its bound: " + JsonSerializer.Serialize(new {
            host.Game.SourceTick, phase = host.Game.State.Phase.ToString(), flag = host.Session.ReadIdiotBoxesShown(),
            program = host.Game.SourceInstructionProgram, instructions = host.Instructions?.Describe()
        }));
        CabinetRender(host);
    }

    private static void VerifyInstructionPacket(PlayerHost host, RecordingSink sink)
    {
        var viewport = new SourceHudViewport(512, 384, 512, 384, 1);
        var expected = new SourceHudComposer(host.Data.Hud).Instructions(host.Instructions.Frame(),
            host.Instructions.Resources.ReadRaster(), viewport);
        Check(expected.Count > 10, "A tutorial needs actual original glyph/image triangles, not a component-only invocation.");
        int first = sink.HudCounts.Length - expected.Count;
        int vertex = sink.HudCounts.Take(first).Sum() * 4;
        for (int primitive = 0; primitive < expected.Count; primitive++)
        {
            SourceHudPrimitive draw = expected[primitive];
            Check(sink.HudTextures[first + primitive] == "hud/" + draw.Texture &&
                sink.Textures.ContainsKey(sink.HudTextures[first + primitive]) &&
                sink.HudColors[first + primitive] == unchecked((int)draw.Color) &&
                sink.HudCounts[first + primitive] == draw.Vertices.Count,
                "Actual PlayerHost Present must carry each original instruction atlas/color/triangle count.");
            foreach (SourceHudVertex value in draw.Vertices)
            {
                Check(sink.HudVertices[vertex++] == value.X && sink.HudVertices[vertex++] == value.Y &&
                    sink.HudVertices[vertex++] == value.S && sink.HudVertices[vertex++] == value.T,
                    "The actual browser HUD packet must contain the source instruction glyph positions/UVs.");
            }
        }
        Check(vertex == sink.HudVertices.Length, "Instruction output must use the sole actual HUD packet, not a private renderer setter.");
    }

    private static void VerifyInstructionText(PlayerHost host, SourceInstructionPage page)
    {
        string[] titles = { "RULES:", "HOW TO PASS:", "HOW TO AUDIBLE:", "GENERAL HINTS:" };
        string[] last = { "NO PENALTIES", "CENTER RECEIVER ON FIELD", "CREATE A PLAY", "CATCHES BALL TO CAUSE A BOBBLE" };
        var texts = host.Instructions.Consumer.Presentation.Objects.OfType<SourceInstructionTextState>()
            .Where(text => !text.IsDeleted).ToArray();
        Check(texts.Length == 7 && texts.All(text => text.Request.Page == page && text.StringId == 0x34),
            "Each actual source page must own its seven original print returns and source string ID.");
        foreach (var expected in new[] { (Line: 0, Value: titles[(int)page], Font: "bast18"),
            (Line: 6, Value: last[(int)page], Font: "bast10") })
        {
            SourceInstructionTextState text = texts.Single(value => value.Request.Line == expected.Line);
            string[] glyphs = expected.Value.Where(c => c != ' ').Select(c => host.Data.Hud.Font(expected.Font).Glyph(c).Id).ToArray();
            Check(text.Graphics.Select(value => value.ImageSymbol).SequenceEqual(glyphs),
                "Actual source page " + page + "/" + expected.Line + " must select its original font glyphs, not generic paragraphs.");
        }
        if (page == SourceInstructionPage.Rules)
        {
            SourceInstructionTextState detail = texts.Single(value => value.Request.Line == 1);
            Check(detail.Request.Justification == SourceInstructionJustification.Left &&
                detail.Position.X == 128 && detail.Position.Y == 239,
                "The source's left-justified 30 YARDS print must retain its distinct ESC justification and source position.");
        }
    }

    private static void VerifyNaturalPortableInstructions(PlayerData data)
    {
        int before = assertions;
        PlayerHost host = InstructionHost(data, out RecordingSink sink);
        Check(host.Instructions == null && host.Game.SourceTick == 0,
            "The cabinet menu backdrop is not silently declared an experienced/new participating match.");
        GameSimulation backdrop = host.Game;
        CabinetStart(host);
        Check(!ReferenceEquals(backdrop, host.Game) && host.Instructions != null &&
            host.Instructions.Consumer.HasSourceAdmission && host.Session.ReadIdiotBoxesShown() == 0 &&
            host.Game.SourceTick == 0 && host.Session.ReadPlayerStatus() == 1,
            "The actual deliberate fresh session must bind the real instruction dispatcher before source wake1.");
        long kickoffBirth = -1;
        InstructionUntil(host, () => HasSourceProcess(host, SourceInstructionDispatcher.KickoffFunction));
        var producer = ((ISourcePossessionState)host.Game.State).Processes.Single(p => p.Function == SourceInstructionDispatcher.KickoffFunction);
        kickoffBirth = producer.BirthTick;
        Check(kickoffBirth == host.Game.SourceTick && host.Session.ReadIdiotBoxesShown() == 0 &&
            host.Game.State.Ball.CarrierId >= 0 && host.Game.State.Ball.Kind == SourceBallKind.Kickoff,
            "A real original kickoff catch must create the deferred producer without writing flag1 inline.");
        InstructionUntil(host, () => host.Instructions!.KickoffPresentation.Frame().Count > 0);
        long kickoffPanel = host.Game.SourceTick;
        Check(host.Session.ReadIdiotBoxesShown() == 1 && host.Game.SourceInstructionProgram == null &&
            ((ISourcePossessionState)host.Game.State).Processes.Any(p => p.Function == "game_proc" && p.Suspended),
            "Actual kickoff parent/children must publish1 and suspend football, independently of the four-page program.");
        VerifyInstructionPacket(host, sink);
        Check(host.Instructions!.KickoffPresentation.Frame().Any(g => g.ImageSymbol == data.Hud.Font("bast10").Glyph('P').Id) &&
            host.Instructions.KickoffPresentation.Frame().Any(g => g.ImageSymbol == "playresult"),
            "The actual kickoff route must render the original PLAYER header and admitted plate.");
        var plate = host.Instructions.KickoffPresentation.Frame().Single(g => g.ImageSymbol == "playresult");
        double width = plate.Vertices.Max(v => v.X) - plate.Vertices.Min(v => v.X);
        double height = plate.Vertices.Max(v => v.Y) - plate.Vertices.Min(v => v.Y);
        Check(width > 120 && width < 121 && height > 219 && height < 221,
            "Kickoff source z_angle/h_scale must rotate the real220x83 authoring plate, not invent212x79 or ignore rotation.");
        bool sawWhite = false, sawHostSkip = false;
        for (int wake = 0; wake < 20; wake++)
        {
            long tick = host.Game.SourceTick, observed = host.Instructions.SourceStepsObserved;
            CabinetRaw(host); CabinetRender(host, 1);
            sawWhite |= sink.EffectFlags[0] == unchecked((int)host.Game.State.Players[0].Source.Presentation.RawFlags) &&
                (sink.EffectFlags[0] & SourceFireIds.FlashWhite) != 0 && sink.FlashAlpha[0] > 0;
            if (host.Game.SourceTick == tick)
            {
                sawHostSkip = true;
                Check(host.Instructions.SourceStepsObserved == observed,
                    "A real57/60 source-host skip cannot advance instruction waits or flash state.");
            }
        }
        Check(sawWhite && sawHostSkip, "Actual source player flash flags and source-host skips must reach the unchanged effects/HUD route.");
        long pausedTick = host.Game.SourceTick, pausedObserved = host.Instructions.SourceStepsObserved;
        int pausedGraphics = host.Instructions.Frame().Count;
        host.SetActive(false);
        for (int redraw = 0; redraw < 5; redraw++) CabinetRender(host, 6);
        Check(host.Game.SourceTick == pausedTick && host.Instructions.SourceStepsObserved == pausedObserved &&
            host.Instructions.Frame().Count == pausedGraphics,
            "Focus/pause must freeze the actual source continuation/resources, not dispatch a UI wait or destroy live handles.");
        host.SetActive(true); CabinetRaw(host); CabinetRender(host);
        InstructionUntil(host, () => host.Instructions.KickoffPresentation.Frame().Count == 0, held: 1);
        Check(host.Game.SourceTick >= kickoffPanel + 40 && host.Instructions.ChildReturns == 1,
            "Held A dismisses only a participating new player's source child after its40-wake minimum and once-only body return.");
        InstructionUntil(host, () => !((ISourcePossessionState)host.Game.State).Processes
            .Any(p => p.Function == "game_proc" && p.Suspended), held: 1);
        Check(!HasSourceProcess(host, SourceInstructionDispatcher.KickoffChildFunction) &&
            host.Instructions.FlashReturns == 1 && host.Session.ReadIdiotBoxesShown() == 1,
            "Actual child retirement must let the parent resume and preserve the source P2_ID22 flash-return policy.");
        InstructionUntil(host, () => host.Game.SourceInstructionProgram != null, select: true);
        Check(host.Game.SourceInstructionProgram!.Stage == SourceInstructionStage.PendingEntry &&
            host.Session.ReadIdiotBoxesShown() == 1 &&
            ((ISourceMatchState)host.Game.State).Match.PlayKind == SourcePlayKind.Normal,
            "Only actual eligible NORMAL selection-return creates the deferred four-page process.");
        InstructionUntil(host, () => host.Game.SourceInstructionProgram?.Stage == SourceInstructionStage.EntryDelay);
        Check(host.Session.ReadIdiotBoxesShown() == -1 && host.Session.ReadPlaySelectionInstructionGate() == 1,
            "The real instruction entry publishes -1; actual first normal-selection return leaves source tick-sound gate1.");
        var pages = new List<string>();
        bool sawPlateWhite = false, sawTitleWhite = false;
        for (int page = 0; page < 4; page++)
        {
            var expected = (SourceInstructionPage)page;
            InstructionUntil(host, () => host.Game.SourceInstructionProgram?.Page == expected &&
                host.Game.SourceInstructionProgram.Stage == SourceInstructionStage.Waiting);
            VerifyInstructionText(host, expected);
            VerifyInstructionPacket(host, sink);
            pages.Add(expected.ToString());
            Check(((ISourcePossessionState)host.Game.State).Processes.Any(p => p.Function == "game_proc" && p.Suspended) &&
                ((ISourcePossessionState)host.Game.State).Processes.Any(p => p.Function == "last_player_proc" && !p.Suspended) &&
                ((ISourcePossessionState)host.Game.State).Processes.Any(p => p.Function == "cambot_proc" && !p.Suspended),
                "Instruction suspension must resume exactly its source post-player/camera bindings, not the football caller.");
            if (page == 0)
            {
                long tick = host.Game.SourceTick, wakes = host.SourceWakes, sounds = host.SoundSelections;
                int draws = ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count;
                SourceMatchView match = ((ISourceMatchState)host.Game.State).Match;
                for (int redraw = 0; redraw < 5; redraw++) CabinetRender(host);
                Check(host.Game.SourceTick == tick && host.SourceWakes == wakes && host.SoundSelections == sounds &&
                    ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count == draws &&
                    ReferenceEquals(match, ((ISourceMatchState)host.Game.State).Match) && host.Session.ReadGamesPlayed(0) == 0,
                    "Redrawing actual source glyphs must not poll/advance waits, consume RNG, replay sound/input or accumulate statistics/experience.");
                for (int wait = 0; wait < 20; wait++)
                {
                    CabinetRaw(host); CabinetRender(host, 1);
                    sawPlateWhite |= sink.HudTextures.Contains("hud/interface-white");
                    sawTitleWhite |= host.Instructions.Consumer.Presentation.Objects.OfType<SourceInstructionTextState>()
                        .Any(text => !text.IsDeleted && text.Request.Line == 0 && text.CharacterColorOverride == 0xffffffffu);
                }
                Check(sawPlateWhite && sawTitleWhite, "Real flash_obj_white2x3 and flash_text_proc7-wake scheduling must alter actual outgoing atlas/color packets.");
            }
            int expectedResults = page + 1;
            // Original any-button wait accepts unjoined physical stations too;
            // kickoff's active-player helper above does not.
            for (int wake = 0; wake < 35 && host.Game.SourceInstructionProgram.WaitResults.Count < expectedResults; wake++)
            {
                CabinetRaw(host, new[] { 0, 0, 0, 1 }); CabinetRender(host, 1);
            }
            Check(host.Game.SourceInstructionProgram.WaitResults.Count == expectedResults &&
                host.Game.SourceInstructionProgram.WaitResults[page] == 0x10000000,
                "Held raw P4 A must cross each real minimum/poll wait with the exact native-byte sample, not a guessed p_status filter or fresh-edge rule.");
            CabinetRaw(host);
        }
        Check(host.Game.SourceInstructionProgram.Stage == SourceInstructionStage.FinalDelay &&
            host.Session.ReadIdiotBoxesShown() == -1 && host.Instructions.Frame().Count == 0,
            "Page4 cleanup must resume football and delete every live plate/string before the real10-wake final delay.");
        long finalDelay = host.Game.SourceTick;
        InstructionUntil(host, () => host.Game.SourceInstructionProgram?.Stage == SourceInstructionStage.Fallthrough);
        Check(host.Game.SourceTick == finalDelay + 10 && host.Session.ReadIdiotBoxesShown() == 2 &&
            host.Instructions.Consumer.Presentation.Objects.All(value => value.IsDeleted) &&
            host.Instructions.Consumer.Presentation.Flashers.All(value => value.IsKilled),
            "Only the completed source final delay publishes2; all original resource handles and flashers must be retired once.");
        InstructionUntil(host, () => host.Game.State.Phase == GamePhase.LiveBall, held: 1);
        long normalSnap = host.Game.SourceTick;
        InstructionUntil(host, () => host.Game.SourceTick >= normalSnap + 20);
        Check(host.Game.State.PlayNumber >= 2 && host.Instructions.Frame().Count == 0 &&
            host.Game.SourceInstructionProgram.WaitResults.Count == 4 &&
            sink.State.GetProperty("events").EnumerateArray().Count(value =>
                value.TryGetProperty("kind", out var kind) && kind.GetString() == "source-instruction-sound") == 6 &&
            host.ProcessSoundDeliveries.Count(call => call.Kind == SourceProcessSoundKind.PlaySelectionIntroduction) == 1 &&
            host.SourceProcessSoundCallsTaken == host.SourceProcessSoundCallsDelivered && host.AudioPlayback == null,
            "The real tutorial must return to normal football with exactly1 child+1 source intro+4 page recording-only ricochets, each genuine queue record delivered once.");
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", natural = true,
            assertions = assertions - before, kickoffBirth, kickoffPanel, pages, finalDelay,
            normalSnap, finalSourceTick = host.Game.SourceTick, sourceWakes = host.SourceWakes,
            route = "Actual PlayerHost/public Capture through real original kickoff catch and normal selection; no gameplay/clock/score/seed setters",
            audio = "source operands recorded only; no original samples/hearing claimed" }));
    }

    private static void VerifyPortableInstructions(PlayerData data, byte[] manifest, Func<string, byte[]> read)
    {
        int before = assertions;
        string profile = data.Catalog.Profile.StableId;
        Check(!PreviewOptions.FromUrl("", profile).InstructionsEnabled &&
            !PreviewOptions.FromUrl("?source-instructions=disabled", profile).InstructionsEnabled,
            "Instructions must remain an explicit independent default-OFF feature.");
        foreach (string url in new[] { "?source-instructions=portable", "?source-gameflow=portable&source-instructions=",
            "?source-gameflow=portable&source-instructions=native", "?source-gameflow=portable&source-instructions=portable&source-instructions=portable",
            "?source-gameflow=portable&source-instructions=portable&source-compatibility=strict" })
        {
            bool rejected = false;
            try { PreviewOptions.FromUrl(url, profile); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed/conflicting instruction dependencies must reject: " + url);
        }
        Check(PreviewOptions.FromUrl("?source-%69nstructions=%70ortable&source-gameflow=portable#source-instructions=disabled", profile).InstructionsEnabled,
            "The actual host option must parse exact decoded keys/values and ignore fragments.");
        VerifyNaturalPortableInstructions(data);
        VerifyInstructionPatAndTick(data);
        VerifyInstructionSessionBoundaries(data);
        VerifyInstructionBindingRecovery(data);
        VerifyHumanIntroduction(data);
        VerifyInstructionDefaultFifo(data, manifest, read);
        VerifyThreeWaySoundOrder();
        Console.WriteLine("PASS " + (assertions - before) + " portable instruction assertions; natural entry and labelled terminal/binding edge fixtures.");
    }

    private static void StartHumanReceivingMatch(PlayerHost host)
    {
        CabinetTap(host, 4, 0);
        CabinetTap(host, 1, 0);
        CabinetTap(host, 1, 0);
        CabinetMove(host, 0, 1, 0);
        CabinetTap(host, 1, 0);
        Check(host.Session.Playing && host.MatchConfiguration.FirstPossession == TeamId.Away &&
            host.Game.SourceTick == 0 && host.Instructions.Consumer.HasSourceAdmission,
            "The public Versus first-possession choice must create the actual human receiving match before wake1.");
        CabinetRaw(host);
    }

    private static void ToHumanIntroduction(PlayerHost host)
    {
        CabinetRaw(host);
        InstructionUntil(host, () => host.Instructions.KickoffPresentation.Frame().Count > 0);
        InstructionUntil(host, () => host.Instructions.ChildReturns == 1, held: 1);
        InstructionUntil(host, () => !((ISourcePossessionState)host.Game.State).Processes
            .Any(p => p.Function == "game_proc" && p.Suspended));
        InstructionUntil(host, () => host.Game.SourcePlaySelectionIntroduction?.Active == true, maximum: 2600);
        Check(host.Game.SourcePlaySelectionIntroduction.OffensiveSide == 0 &&
            host.Game.SourcePlaySelectionIntroduction.Stage == SourcePlaySelectionIntroductionStage.WaitingActiveTeam,
            "Real catch/result/public continuation must reach the human-offense introductory helper, not a synthetic card setter.");
    }

    private static void VerifyHumanIntroduction(PlayerData data)
    {
        PlayerHost host = InstructionHost(data, out RecordingSink sink);
        StartHumanReceivingMatch(host);
        ToHumanIntroduction(host);
        long entry = host.Game.SourcePlaySelectionIntroduction.EntrySourceTick;
        int number = ((ISourceMatchState)host.Game.State).Match.SelectionTimer.Number;
        var frame = host.Instructions.IntroductionFrame;
        Check(frame.Count(value => value.ImageSymbol == "playresult") == 3 &&
            frame.Skip(1).Take("GENERALINFO".Length).Select(value => value.ImageSymbol).SequenceEqual(
                "GENERALINFO".Select(c => data.Hud.Font("bast18").Glyph(c).Id)),
            "The actual original introduction must draw its three admitted plates and exact GENERAL INFO glyphs.");
        VerifyInstructionPacket(host, sink);
        bool flashed = false;
        while (host.Game.SourceTick < entry + 35)
        {
            CabinetRaw(host, new[] { 0, 0, 7, 7 }); CabinetRender(host, 1);
            flashed |= host.Instructions.IntroductionFrame.Any(value =>
                value.ImageSymbol == data.Hud.Font("bast18").Glyph('O').Id && value.Color == 0xffffffffu);
        }
        Check(flashed && host.Game.SourcePlaySelectionIntroduction.Active &&
            host.Session.ReadPlaySelectionInstructionGate() == 12 &&
            ((ISourceMatchState)host.Game.State).Match.SelectionTimer.Number == number &&
            !((ISourceMatchState)host.Game.State).Match.SelectionTimer.ForceSelection,
            "Actual pflash source colors must change while +10 freezes the existing timer; other-team/unjoined held ABC cannot dismiss the intro.");
        InstructionUntil(host, () => !host.Game.SourcePlaySelectionIntroduction.Active, maximum: 20, held: 1);
        Check(host.Game.SourceTick == entry + 40 && host.Game.SourcePlaySelectionIntroduction.WaitTicks == 40 &&
            host.Session.ReadPlaySelectionInstructionGate() == 1 && host.Instructions.IntroductionFrame.Count == 0 &&
            host.ProcessSoundDeliveries.Count(value => value.Kind == SourceProcessSoundKind.PlaySelectionIntroduction) == 1 &&
            host.ProcessSoundDeliveries.All(value => value.OutputSequence > 0),
            "Held own-team A must complete exactly the source40-wake helper and sound/cleanup/-11 once through the common queue.");
        host.Restart();
        ToHumanIntroduction(host);
        entry = host.Game.SourcePlaySelectionIntroduction.EntrySourceTick;
        while (host.Game.SourcePlaySelectionIntroduction.Active && host.Game.SourceTick < entry + 630)
        {
            CabinetRaw(host, new[] { 0, 0, 7, 7 }); CabinetRender(host, 1);
        }
        Check(host.Game.SourceTick == entry + 11 * 57 &&
            host.Game.SourcePlaySelectionIntroduction.WaitTicks == 11 * 57 &&
            host.Session.ReadPlaySelectionInstructionGate() == 1 && host.Instructions.IntroductionFrame.Count == 0,
            "The actual active-team timeout is total627 source wakes, not40+627 or an unjoined-button shortcut.");
        Console.WriteLine("PASS actual public human-offense intro glyphs/flasher/team minimum and timeout.");
    }

    private static PlayerData InstructionSuccessor(PlayerData data, byte[] manifest, Func<string, byte[]> read)
    {
        if (data.Catalog.Profile.IsContact282C2M1DefaultAudibles) return data;
        Check(DataCatalog.Hash(manifest) == "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6",
            "The metadata-only instruction successor starts from the actual approved563 manifest.");
        JsonObject document = JsonNode.Parse(manifest)!.AsObject();
        document["profile"] = SourceTeamResourceProfile.DefaultAudiblesStableId;
        document["featureContract"] = "source-default-audibles/v1";
        document["factory"] = KernelBinding.DefaultAudiblesFactory;
        var compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        string Canonical(JsonNode? value, int depth)
        {
            string indent = new(' ', depth * 2), next = new(' ', (depth + 1) * 2);
            if (value is JsonObject obj)
                return obj.Count == 0 ? "{}" : "{\n" + string.Join(",\n", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => next + JsonSerializer.Serialize(p.Key, compact) + ": " + Canonical(p.Value, depth + 1))) + "\n" + indent + "}";
            if (value is JsonArray array)
                return array.Count == 0 ? "[]" : "[\n" + string.Join(",\n", array.Select(v => next + Canonical(v, depth + 1))) + "\n" + indent + "]";
            return value?.ToJsonString(compact) ?? "null";
        }
        byte[] descriptor = Encoding.UTF8.GetBytes(Canonical(document, 0) + "\n");
        Check(DataCatalog.Hash(descriptor) == "885c9e04cee21a2bb15d40b243744d3b1bfa6f4846ddf2ea9837c7fd3bec5548",
            "The default-audibles descriptor must match its exact explicit derivative pin, never relabel arbitrary profiles.");
        return new PlayerData(new DataCatalog(descriptor, read));
    }

    private static void VerifyInstructionDefaultFifo(PlayerData data, byte[] manifest, Func<string, byte[]> read)
    {
        PlayerData successor = InstructionSuccessor(data, manifest, read);
        PlayerHost host = InstructionHost(successor, out RecordingSink sink);
        CabinetStart(host);
        Check(host.Game.SourceDefaultAudiblesEnabled && host.Instructions.Consumer.HasSourceAdmission,
            "Genuine separately admitted default audibles must bind tutorials through the real source scan, not alias the base profile.");
        InstructionUntil(host, () => host.Instructions.KickoffPresentation.Frame().Count > 0);
        var seen = new List<SourceStationInput>();
        long tick = host.Game.SourceTick;
        void Observe(IGameState state)
        {
            if (host.Game.SourceTick == tick) return;
            tick = host.Game.SourceTick;
            var scan = host.Game.ConsumedSourceSwitches;
            Check(scan != null && scan.SourceTick == tick && scan.InputEpoch == host.Game.SourceInputEpoch,
                "The actual tutorial must read its current source-tick/input-epoch consumed FIFO scan.");
            seen.Add(scan!.PhysicalSwitches.Station(3));
        }
        host.Bridge.AfterHostStep += Observe;
        var axes = new double[8]; axes[7] = -1;
        CabinetRaw(host, new[] { 0, 0, 0, 7 }, axes);
        CabinetRaw(host);
        for (int step = 0; step < 5 && seen.Count < 2; step++) CabinetRender(host, 1);
        host.Bridge.AfterHostStep -= Observe;
        Check(seen.Count >= 2 && seen[0].Held == (SourceButtons)7 && seen[0].DigitalCurrent == SourceDigitalDirections.Up &&
            seen[1].Held == SourceButtons.None && seen[1].DigitalCurrent == SourceDigitalDirections.None &&
            ((ISourcePossessionState)host.Game.State).Processes.Any(p => p.Function == "game_proc" && p.Suspended),
            "Actual default-audibles FIFO must retain unjoined P4 press/digital/release in order while game_proc is suspended; latest-host mirroring fails this.");
        long epoch = host.Game.SourceInputEpoch;
        host.SetActive(false);
        Check(host.Game.SourceInputEpoch > epoch && host.Game.ConsumedSourceSwitches == null,
            "Real pause clears the consumed input epoch, never leaves an apparently current stale physical sample.");
        host.SetActive(true); CabinetRaw(host); CabinetRender(host);
        InstructionUntil(host, () => host.Instructions.ChildReturns == 1, held: 1);
        InstructionUntil(host, () => host.Game.SourceInstructionProgram?.Stage == SourceInstructionStage.Waiting, select: true);
        for (int step = 0; step < 150 && host.Game.SourceInstructionProgram.Stage != SourceInstructionStage.Fallthrough; step++)
        {
            CabinetRaw(host, new[] { 0, 0, 0, 1 }); CabinetRender(host, 1);
        }
        Check(host.Game.SourceInstructionProgram.Stage == SourceInstructionStage.Fallthrough &&
            host.Session.ReadIdiotBoxesShown() == 2 && host.SourceProcessSoundCallsTaken == host.SourceProcessSoundCallsDelivered &&
            sink.State.GetProperty("kernel").GetProperty("factory").GetString() == KernelBinding.DefaultAudiblesFactory &&
            host.ProcessSoundDeliveries.Select(value => value.OutputSequence).SequenceEqual(
                host.ProcessSoundDeliveries.Select(value => value.OutputSequence).Order()),
            "Actual default-audibles catch/intro/four pages must complete with one ordered source output per canonical record.");
        Console.WriteLine("PASS actual885c default-audibles physical FIFO, pause epoch and completed tutorial.");
    }

    private static void VerifyThreeWaySoundOrder()
    {
        var order = new SourceSoundOutputOrder();
        var merged = order.Begin(new long[] { 10, 40 }, new long[] { 20 }, new long[] { 30, 50 });
        Check(merged.Select(value => value.OutputSequence).SequenceEqual(new long[] { 10, 20, 30, 40, 50 }) &&
            merged[2].Kind == SourceSoundOutputKind.Process && merged[3].Kind == SourceSoundOutputKind.Direct,
            "The real Host order gate must merge direct/VM/actorless queues by their genuine source ordinals, not observation order.");
        foreach (var value in merged) order.Complete(value.OutputSequence);
        bool duplicate = false;
        try { order.Begin(new long[] { 60 }, new long[] { 70 }, new long[] { 60 }); }
        catch (InvalidDataException) { duplicate = true; }
        Check(duplicate, "A shared direct/process ordinal must be rejected before either receiver can be delivered twice.");
    }

    private static void VerifyInstructionPatAndTick(PlayerData data)
    {
        // Declared touchdown boundary fixture, then real public PAT selection,
        // next kickoff/catch and normal selection. Not a natural scoring proof.
        PlayerHost host = InstructionHost(data, out RecordingSink sink);
        CabinetStart(host);
        InstructionUntil(host, () => host.Instructions.KickoffPresentation.Frame().Count > 0);
        InstructionUntil(host, () => host.Instructions.ChildReturns == 1, held: 1);
        InstructionUntil(host, () => !((ISourcePossessionState)host.Game.State).Processes
            .Any(p => p.Function == "game_proc" && p.Suspended));
        Check(host.Session.ReadIdiotBoxesShown() == 1, "The PAT fixture starts only after a real source catch/coordinator.");
        CabinetRaw(host);
        FlowEnd(host, 0, 0, 0, cause: SourcePlayEndCause.CarrierTouchdown);
        InstructionUntil(host, () => ((ISourceMatchState)host.Game.State).Match.Phase == SourceMatchPhase.PatSelection);
        Check(host.Game.SourceInstructionProgram == null && host.Session.ReadPlaySelectionInstructionGate() == 12 &&
            host.Game.SourcePlaySelectionIntroduction.Active,
            "An actual PAT intro must raise its real +10 gate before selection without admitting the NORMAL four-page program.");
        InstructionUntil(host, () => host.Game.State.Phase != GamePhase.PlaySelect, select: true);
        Check(host.Game.SourceInstructionProgram == null && host.Session.ReadIdiotBoxesShown() == 1 &&
            host.Session.ReadPlaySelectionInstructionGate() == 1,
            "PAT play_select return advances its own source display count, but never admits instructions_proc.");
        InstructionUntil(host, () => host.Game.SourceInstructionProgram?.Stage == SourceInstructionStage.Waiting, select: true);
        Check(host.Session.ReadPlaySelectionInstructionGate() == 0 && host.Session.ReadIdiotBoxesShown() == -1 &&
            ((ISourcePossessionState)host.Game.State).Processes.Count(p => p.Function == SourceInstructionDispatcher.KickoffFunction) == 1,
            "A later kickoff cannot re-admit the flag1 producer; the following NORMAL selector alone admits pages with source gate0.");
        int nextResult = host.Game.SourceInstructionProgram!.WaitResults.Count + 1;
        for (int step = 0; step < 30 && host.Game.SourceInstructionProgram.WaitResults.Count < nextResult; step++)
        {
            CabinetRaw(host, new[] { 1, 0, 0, 0 }); CabinetRender(host, 1);
        }
        var calls = sink.State.GetProperty("events").EnumerateArray().Where(value =>
            value.TryGetProperty("kind", out var kind) && kind.GetString() == "source-instruction-sound").ToArray();
        var tick = calls.Single(value => value.GetProperty("Operand").GetString() == "tick_sndn");
        Check(tick.GetProperty("Bank").GetString() == "cursors" && tick.GetProperty("Playlist").GetInt32() == 17 &&
            tick.GetProperty("Volume").GetInt32() == 115 && tick.GetProperty("Pan").GetInt32() == 127 &&
            tick.GetProperty("Priority").GetInt32() == 1 && tick.GetProperty("Direct").GetBoolean() &&
            !tick.GetProperty("PcmOrDspPlayback").GetBoolean() &&
            calls.Last().GetProperty("Operand").GetString() == "RICOCHET_SND" &&
            host.Game.SourceInstructionProgram.WaitResults.Count == nextResult,
            "The real zero-gate any-button wait must record the exact direct tick17 before page ricochet148, without inventing a bank address, RNG or PCM.");
    }

    private static void VerifyInstructionSessionBoundaries(PlayerData data)
    {
        PlayerHost host = InstructionHost(data, out RecordingSink sink);
        CabinetStart(host);
        InstructionUntil(host, () => host.Instructions.KickoffPresentation.Frame().Count > 0);
        PortableInstructionRuntime runtime = host.Instructions;
        int textures = sink.Textures.Count;
        host.Restart();
        Check(ReferenceEquals(runtime, host.Instructions) && runtime.Consumer.HasSourceAdmission &&
            host.Game.SourceTick == 0 && host.Session.ReadIdiotBoxesShown() == 0 &&
            runtime.Frame().Count == 0 && runtime.ChildReturns == 0 && runtime.SourceStepsObserved == 0 &&
            !HasSourceProcess(host, SourceInstructionDispatcher.KickoffChildFunction) && sink.Textures.Count == textures,
            "Real administrative restart must cancel actual children/resources and retain/reinitialize one successful binding without texture reupload.");
        CabinetRaw(host);
        InstructionUntil(host, () => host.Game.SourceInstructionProgram?.Stage == SourceInstructionStage.Waiting, select: true, held: 1);
        SourceAnyButtonWaitSnapshot suspendedWait = host.Game.SourceInstructionProgram.Wait;
        long suspendedWake = host.Game.SourceInstructionProgram.Process.LastWakeTick;
        FlowProcesses(host).SuspendMultiple(0xf9b, -1);
        for (int step = 0; step < 5; step++) { CabinetRaw(host); CabinetRender(host, 1); }
        Check(host.Game.SourceInstructionProgram.Process.Suspended &&
            host.Game.SourceInstructionProgram.Process.LastWakeTick == suspendedWake &&
            host.Game.SourceInstructionProgram.Wait.DelayRemaining == suspendedWait.DelayRemaining &&
            host.Game.SourceInstructionProgram.Wait.PollsRemaining == suspendedWait.PollsRemaining,
            "Declared actual-host process-suspension fixture: source wake/render progress cannot advance a suspended instructions_proc wait.");
        FlowProcesses(host).ResumeMultiple(0xf9b, -1);
        var oldObjects = runtime.Consumer.Presentation.Objects.ToArray();
        host.Restart();
        Check(oldObjects.All(value => value.IsDeleted) && runtime.Frame().Count == 0 &&
            runtime.Consumer.Program == null && runtime.Consumer.HasSourceAdmission &&
            host.Session.ReadIdiotBoxesShown() == 0 && host.Session.ReadGamesPlayed(0) == 0,
            "Restart during an actual four-page wait must release old handles exactly once without inventing a completed game/experience.");
        // Explicit terminal clock fixtures exercise lifecycle/experience only.
        // They are not natural regulation or browser acceptance.
        for (int completed = 1; completed <= 3; completed++)
        {
            CabinetRaw(host);
            FlowEnd(host, 3, 4, 17, clock: 0);
            Check(host.Session.ReadGamesPlayed(0) == completed - 1,
                "A queued result/result wait is not yet a completed admitted match.");
            CabinetToStage(host, SourceCabinetFlowStage.FinalStats);
            Check(host.Session.ReadGamesPlayed(0) == completed && host.Session.GamesPlayed.Skip(1).All(value => value == 0),
                "Only one actual admitted final observation may increment participating stations' in-memory games_played.");
            CabinetRender(host, 2);
            Check(host.Session.ReadGamesPlayed(0) == completed, "Final redraws must not duplicate games_played or statistical accumulation.");
            CabinetNextOptions(host);
            GameSimulation sameGame = host.Game;
            long epoch = host.Epoch;
            CabinetTap(host, 1, 0);
            Check(ReferenceEquals(sameGame, host.Game) && ReferenceEquals(runtime, host.Instructions) &&
                host.Epoch == epoch + 1 && host.Game.SourceTick == 0 &&
                runtime.Frame().Count == 0 && runtime.Consumer.HasSourceAdmission && host.Session.ReadIdiotBoxesShown() == 0 &&
                host.SoundSelections == 0 && host.SourceWakes == 0 && host.Session.ReadGamesPlayed(0) == completed,
                "Every actual public rematch must reset/recover the retained source binding once without duplicate input/audio/stats or lost experience.");
            CabinetRaw(host);
            if (completed == 2)
            {
                InstructionUntil(host, () => runtime.KickoffPresentation.Frame().Count > 0);
                Check(host.Session.ReadGamesPlayed(0) == 2 && host.Session.ReadIdiotBoxesShown() == 1,
                    "Exactly two completed games remain source-inexperienced and must still render the actual kickoff child.");
                host.Restart(); CabinetRaw(host);
            }
        }
        bool renderedExperienced = false;
        host.Bridge.AfterHostStep += _ => renderedExperienced |= host.Instructions.Frame().Count != 0;
        InstructionUntil(host, () => host.Game.State.PlayNumber >= 2 && host.Game.State.Phase == GamePhase.PreSnap, select: true);
        Check(!renderedExperienced && runtime.ChildReturns == 0 && runtime.Consumer.Program == null &&
            host.Session.ReadIdiotBoxesShown() == 1 && host.Session.ReadGamesPlayed(0) == 3,
            "At source experience boundary3 all actual kickoff children die and normal selection skips pages; flag1 is not falsely rewritten2.");
        FlowEnd(host, 3, 7, 0, clock: 0);
        CabinetNextOptions(host);
        CabinetTap(host, 2, 0);
        CabinetTap(host, 1, 2);
        CabinetTap(host, 4, 0);
        CabinetTap(host, 1, 0, 2);
        CabinetTap(host, 1, 0, 2);
        long oldEpoch = host.Epoch;
        GameSimulation oldGame = host.Game;
        CabinetTap(host, 1, 0);
        Check(!ReferenceEquals(oldGame, host.Game) && !ReferenceEquals(runtime, host.Instructions) &&
            host.Epoch == oldEpoch + 1 && host.Instructions.Consumer.HasSourceAdmission && host.Game.SourceTick == 0 &&
            host.Session.ReadPlayerStatus() == 5 && host.Session.ReadGamesPlayed(0) == 4 &&
            host.Session.ReadGamesPlayed(2) == 0 && host.Instructions.Frame().Count == 0 && sink.Textures.Count == textures,
            "A changed-human fresh factory must dispose the old epoch and bind BEFORE wake1 using the same real session experience owner.");
        CabinetRaw(host);
        InstructionUntil(host, () => host.Instructions.KickoffPresentation.Frame().Count > 0);
        var headers = host.Instructions.KickoffPresentation.Frame();
        Check(headers.Count(value => value.ImageSymbol == "playresult") == 1 && host.Session.ReadIdiotBoxesShown() == 1,
            "The newly joined known-new station receives its own real child while the experienced station is skipped.");
        VerifyInstructionPacket(host, sink);
    }

    private static void VerifyInstructionBindingRecovery(PlayerData data)
    {
        // An actual deliberate host provides the immutable admitted configuration.
        PlayerHost host = InstructionHost(data, out _);
        CabinetStart(host);
        var game = KernelBinding.Create(data, host.Options, matchConfiguration: host.MatchConfiguration, newPlayerSession: true);
        var binding = new InstructionRecoveryBinding(game);
        var state = new InstructionFailOnceState(host.Session);
        var forbidden = new InstructionUnexecutedServices();
        using var consumer = new SourceInstructionConsumer(binding, host.Instructions.Resources, forbidden, forbidden);
        bool failed = false;
        try
        {
            consumer.BindSourceKickoffInstructionOwnedHelpers(unchecked((int)0xf0f0f0f0u),
                new SourceInstructionProcessIds(0xf9b, 0x502, 0x1400, 0xf10), forbidden, state,
                0xf9a, 4, 1, 2, forbidden, forbidden, new SourceKickoffInstructionChildOperands(false, 0, 0, 0, 1),
                host.Instructions.KickoffPresentation,
                new SourceKickoffInstructionOwnedHelpers(0x10, 0x20, 0x40, SourceFireIds.FlashWhite, forbidden, forbidden, forbidden), forbidden);
        }
        catch (InvalidOperationException error) { failed = error.Message.Contains("declared first-bind failure", StringComparison.Ordinal); }
        Check(failed && !consumer.HasSourceAdmission && consumer.Presentation.Failure != null && game.SourceTick == 0,
            "A real source admission write failure must fault the consumer and the actual fresh Game binding, not count as success.");
        game.Restart(1);
        consumer.OnSourceProgramReset();
        Check(consumer.HasSourceAdmission && consumer.Presentation.Failure == null && state.Writes == 2 &&
            game.SourceInstructionProgram == null && consumer.Program == null,
            "Only a real simulation reset may retry the same prepared effects/state owner and recover an initially failed bind.");
        game.Restart(1); consumer.OnSourceProgramReset();
        Check(consumer.HasSourceAdmission && state.Writes == 3,
            "A later successful reset is initialized by Game once, not double-bound by Consumer.");
    }

    private sealed class InstructionRecoveryBinding : ISourceInstructionAdmissionProgramBinding
    {
        private readonly GameSimulation game;
        internal InstructionRecoveryBinding(GameSimulation game) { this.game = game; }
        public SourceInstructionSnapshot Current => game.SourceInstructionProgram;
        public IReadOnlyList<SourcePossessionProcessRecord> Records => game.SourcePossessionProcessRecords;
        public long Invoke(SourceInstructionInvocation invocation) => throw new InvalidOperationException("No manual tutorial invocation in admission recovery.");
        public void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding) => game.BindSourceInstructionAdmission(binding);
    }
    private sealed class InstructionFailOnceState : ISourceInstructionAdmissionState
    {
        private readonly SourceCabinetSession state;
        internal int Writes;
        internal InstructionFailOnceState(SourceCabinetSession state) { this.state = state; }
        public int ReadPlayerStatus() => state.ReadPlayerStatus();
        public int ReadGamesPlayed(int slot) => state.ReadGamesPlayed(slot);
        public int ReadIdiotBoxesShown() => state.ReadIdiotBoxesShown();
        public void WriteIdiotBoxesShown(int value)
        {
            if (++Writes == 1) throw new InvalidOperationException("declared first-bind failure");
            state.WriteIdiotBoxesShown(value);
        }
    }
    private sealed class InstructionUnexecutedServices : ISourceInstructionInputs, ISourceKickoffInstructionChildInputs,
        ISourceKickoffInstructionPlayerButtons, ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects,
        ISourceKickoffInstructionChildReturnBinding, ISourceKickoffInstructionFlashReturnBinding,
        ISourceInstructionObjectFlashBinding, ISourceInstructionSoundPlayer
    {
        private static Exception Unexecuted() => new InvalidOperationException("Binding-recovery fixture must not execute a child or replace real host progression.");
        public int ReadPlayerSwitches() => throw Unexecuted();
        public int ReadShowPlyselIdiotBox() => throw Unexecuted();
        public int ReadTsec() => throw Unexecuted();
        public int ReadIsLowResolution() => throw Unexecuted();
        public int ReadFourPlayerVersion() => throw Unexecuted();
        public int ReadPlayerControl(int slot) => throw Unexecuted();
        public int ReadBallCarrier() => throw Unexecuted();
        public int ReadPlayerTeam(int player) => throw Unexecuted();
        public int ReadOffensiveSide() => throw Unexecuted();
        public int ReadCurrentButtons(int player) => throw Unexecuted();
        public void WriteCurrentButtons(int player, int value) => throw Unexecuted();
        public int ReadOldButtons(int player) => throw Unexecuted();
        public void WriteOldButtons(int player, int value) => throw Unexecuted();
        public uint ReadCurrentSwitchWord() => throw Unexecuted();
        public ISourceKickoffPlayerObject ReadPlayerBlock(int player) => throw Unexecuted();
        public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(SourceKickoffInstructionChildInvocation invocation,
            SourceKickoffInstructionChildReturnReason reason) => throw Unexecuted();
        public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(SourceKickoffInstructionFlashInvocation invocation) => throw Unexecuted();
        public ISourceInstructionObjectFlashProgram Create(ISourceInstructionPlate plate, int flashes, int delay, int fourth) => throw Unexecuted();
        public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context) => throw Unexecuted();
    }
}
