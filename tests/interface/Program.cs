using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class Program
{
    private static int assertions;
    private static int actualAssertions;
    private static void Check(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception(name);
    }
    private static void Reject<T>(Action operation, string name) where T : Exception
    {
        bool rejected = false;
        try { operation(); }
        catch (T) { rejected = true; }
        Check(rejected, name);
    }
    private static void Near(double value, double expected, string name)
    { Check(Math.Abs(value - expected) < 0.00001, name); }

    private static SourceMatchConfiguration Configuration(bool four = false)
    {
        var records = new[] { 0, 1, 2, 3, 4, 5, 6 };
        SourceStationConfiguration[] stations = four ? new[] {
            new SourceStationConfiguration(3, TeamId.Home, 3),
            new SourceStationConfiguration(0, TeamId.Away, 10),
            new SourceStationConfiguration(2, TeamId.Home, 4),
            new SourceStationConfiguration(1, TeamId.Away, 11)
        } : new[] { new SourceStationConfiguration(2, TeamId.Home, 3), new SourceStationConfiguration(0, TeamId.Away, 10) };
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            four ? SourceStationLayout.FourStation : SourceStationLayout.TwoStation,
            new SourceTeamSelection(5, records), new SourceTeamSelection(1, records), stations,
            TeamId.Home, SourceCaptainSlot.Second);
    }

    private static SourceMatchView Match(SourceMatchConfiguration config, SourceMatchPhase phase = SourceMatchPhase.PlaySelection,
        SourcePlayKind kind = SourcePlayKind.Normal, SourceMatchResult result = default(SourceMatchResult),
        int quarter = 0, SourcePeriodStatus period = SourcePeriodStatus.NotExpired,
        SourcePatStatus pat = SourcePatStatus.NotExtraPoint, SourceKickoffType? kick = null)
    {
        var stations = new List<SourceStationView>();
        foreach (SourceStationConfiguration station in config.Stations.Reverse())
            stations.Add(new SourceStationView(station.StationId, station.InitialPlayerId, (station.StationId + 1) / 4f));
        return new SourceMatchView(config, kind, phase, new SourcePeriodView(quarter, period),
            new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Away, TeamId.Home, config.Captain),
            new[] { new SourceQuarterScore(quarter, 7, 3) }, stations, result: result, selectionCountdown: 7,
            patStatus: pat, kickoff: kick, kickingTeam: kick.HasValue ? (TeamId?)TeamId.Home : null,
            receivingTeam: kick.HasValue ? (TeamId?)TeamId.Away : null);
    }

    private static void Layout()
    {
        foreach (int stations in new[] { 0, 1, 2, 3, 4 })
            foreach (double dpr in new[] { 1d, 1.25, 1.5, 2d, 3d })
                foreach (int[] size in new[] { new[] { 360, 740 }, new[] { 740, 360 }, new[] { 1280, 800 },
                    new[] { 1920, 1080 }, new[] { 2560, 1080 }, new[] { 800, 600 } })
                {
                    var view = new SourceHudViewport((int)(size[0] * dpr), (int)(size[1] * dpr), size[0], size[1], dpr);
                    var layout = new SourceHudLayout(view, stations);
                    Check(view.Content.Contains(layout.Score), "source score remains inside actual CSS content");
                    Check(view.Content.Contains(layout.Controls), "source controls remain inside actual CSS content");
                    Check(view.Content.Contains(layout.Menu), "source menu remains inside actual CSS content");
                    Check(!layout.Score.Overlaps(layout.Controls), "source score and controls never overlap");
                    Check(!layout.Menu.Overlaps(layout.Score), "source menu/title cannot overlap HOME score");
                    Check(!layout.Menu.Overlaps(layout.Controls), "source menu cannot overlap readable controls");
                    for (int i = 0; i < stations; i++)
                    {
                        SourceHudRect station = layout.Station(i, stations);
                        Check(layout.Controls.Contains(station), "station controls remain inside full content");
                        for (int j = i + 1; j < stations; j++)
                            Check(!station.Overlaps(layout.Station(j, stations)), "four-station portrait controls do not overlap");
                    }
                }
        var one = new SourceHudLayout(new SourceHudViewport(360, 740, 360, 740, 1), 4);
        var two = new SourceHudLayout(new SourceHudViewport(720, 1480, 360, 740, 2), 4);
        Check(two.Portrait && one.Portrait, "portrait actual DPR2 uses CSS metrics, not 720-buffer desktop branch");
        Near(two.Score.Width, one.Score.Width, "DPR does not change physical CSS HUD size");
        Near(two.Viewport.ToGui(two.Score).Width, two.Score.Width * 2, "DPR2 glyph geometry reaches actual doubled pixels");
        Reject<InvalidOperationException>(() => new SourceHudViewport(512, 384, 360, 740, 2),
            "fixed buffer stretched into portrait is rejected, not accepted as resize support");
        Reject<ArgumentOutOfRangeException>(() => new SourceHudViewport(0, 1, 360, 740, 2), "zero-sized buffer rejected");
        Reject<ArgumentOutOfRangeException>(() => new SourceHudViewport(720, 1480, double.NaN, 740, 2), "NaN CSS metrics rejected");
    }

    private static SourceInterfaceInputTables SyntheticTables()
    {
        var raw = new int[256]; var digital = new int[49]; var directions = new int[49];
        for (int i = 0; i < raw.Length; i++) raw[i] = 24;
        directions[24] = -1;
        return new SourceInterfaceInputTables(raw, digital, directions);
    }

    private static void SyntheticFont()
    {
        var image = new SourceInterfaceImage("synthetic_glyph", 0, 0, 5, 9, 1, -2, "synthetic", "");
        var glyphs = new Dictionary<char, SourceInterfaceImage> { { 'A', image } };
        var font = new SourceInterfaceFont("synthetic_font", 9, 4, 2, glyphs);
        glyphs.Clear();
        Check(font.Glyph('A') == image, "font glyph table is detached");
        Near(SourceHudText.Width(font, "A A"), 18, "font uses actual image width, source spacing and spaces");
        IReadOnlyList<SourceHudGlyph> laid = SourceHudText.Center(font, "A", 100, 100, 1);
        Near(laid[0].Rect.X, 96, "Goose centered text truncates half-width before anchor");
        Near(laid[0].Rect.Y, 98, "original glyph vertical anchor is not ignored");
        Reject<SourceInterfaceException>(() => font.Glyph('?'), "missing original glyph fails without a generic font fallback");
        Reject<SourceInterfaceException>(() => SourceHudText.Width(font, "A?"), "unsupported source text cannot silently use Arial");
        Reject<ArgumentOutOfRangeException>(() => SourceHudText.Center(font, "A", 0, 0, float.NaN), "nonfinite font scaling rejected");
    }

    private static void InputBuffer(SourceInterfaceInputTables tables)
    {
        SourceMatchConfiguration config = Configuration(true);
        var quantizer = new SourceCabinetQuantizer(tables);
        SourceCabinetStick center = quantizer.Raw49(0);
        Check(center.Ordinal == 24, "raw zero Hall byte uses original neutral deadzone");
        var buffer = new SourceCabinetInputBuffer(config);
        Func<SourceButtons, SourceButtons, SourceCabinetSample[]> samples = (first, second) => new[] {
            new SourceCabinetSample(1, center, SourceButtons.None),
            new SourceCabinetSample(3, center, first),
            new SourceCabinetSample(0, center, SourceButtons.None),
            new SourceCabinetSample(2, center, second)
        };
        buffer.Capture(samples(SourceButtons.A | SourceButtons.B | SourceButtons.C, SourceButtons.C));
        SourceInputFrame press = buffer.NextHostFrame();
        Check(press.Stations.Select(s => s.StationId).SequenceEqual(new[] { 1, 3, 0, 2 }), "raw station input preserves sampled order");
        Check(press.Stations[1].Held == (SourceButtons)7 && press.Stations[1].Pressed == (SourceButtons)7,
            "source simultaneous ABC press remains a raw combo, not a priority action");
        Check(!press.Stations[1].Command.IsSpecified, "raw cabinet producer does not guess gameplay actions");
        Check(buffer.NextHostFrame().Stations[1].Pressed == SourceButtons.None, "held source frame does not repeat action edges");
        buffer.Capture(samples(SourceButtons.None, SourceButtons.None));
        SourceInputFrame release = buffer.NextHostFrame();
        Check(release.Stations[1].Released == (SourceButtons)7 && release.Stations[1].Held == SourceButtons.None,
            "source release edges are preserved independently from held state");
        buffer.Capture(samples(SourceButtons.A, SourceButtons.None));
        buffer.Capture(samples(SourceButtons.None, SourceButtons.None));
        buffer.Capture(samples(SourceButtons.B, SourceButtons.None));
        Check(buffer.PendingFrames == 3, "press-release-press between source wakes retains all ordered samples");
        Check(buffer.NextHostFrame().Stations[1].Pressed == SourceButtons.A, "first queued edge order");
        Check(buffer.NextHostFrame().Stations[1].Released == SourceButtons.A, "second queued release order");
        Check(buffer.NextHostFrame().Stations[1].Pressed == SourceButtons.B, "third queued edge order");
        buffer.Clear();
        Check(buffer.WaitingForNeutral && buffer.PendingFrames == 0, "focus clear drains pending stations and awaits neutral");
        buffer.Capture(samples(SourceButtons.B, SourceButtons.None));
        Check(buffer.NextHostFrame().Stations.All(s => s.Held == SourceButtons.None && s.Pressed == SourceButtons.None),
            "focus wake cannot replay a still-held action");
        buffer.Capture(samples(SourceButtons.None, SourceButtons.None));
        Check(!buffer.WaitingForNeutral && buffer.PendingFrames == 0, "release rearms focus without a fake release action");
        buffer.Capture(samples(SourceButtons.B, SourceButtons.None));
        Check(buffer.NextHostFrame().Stations[1].Pressed == SourceButtons.B, "fresh post-focus press is accepted exactly once");
        Reject<ArgumentException>(() => buffer.Capture(new[] { new SourceCabinetSample(3, center, SourceButtons.None) }),
            "producer cannot omit configured stations");
        SourceMatchView match = Match(config);
        Check(SourceCabinetStations.Captain(match, TeamId.Home) == 2, "captain is teammate slot, not station ID or team side");
        Check(SourceCabinetStations.Captain(match, TeamId.Away) == 1, "away captain resolves actual configured station ownership");
        Check(SourceCabinetStations.View(match, 3).ControlledPlayerId == 3, "station view lookup does not equate collection index to station");
        Check(SourceCabinetStations.Team(match, 0) == TeamId.Away, "station zero need not be Home");
        Check(SourceCabinetStations.Captain(Match(Configuration()), TeamId.Home) == 2, "single human captain ignores absent teammate offset");
        Reject<SourceInterfaceException>(() => SourceCabinetStations.View(match, -1), "missing station view is explicit");
        Reject<ArgumentOutOfRangeException>(() => quantizer.Analog(float.NaN, 0), "NaN gamepad axis rejected");
        Reject<ArgumentOutOfRangeException>(() => quantizer.SevenLevel(4, 0), "seven-level bounds enforced");
        for (int station = 0; station < 4; station++)
            for (uint bits = 0; bits < 8; bits++)
                Check(SourceCabinetQuantizer.ButtonsFromSourceRegister(bits << (station * 8 + 4), station) == (SourceButtons)bits,
                    "source register A/B/C byte/bit layout");
        var scanner = new SourceCabinetSwitchScanner();
        uint abc = 7u << 4;
        Check(scanner.Scan(~abc).Closed == abc, "Goose close uses both previous open scans");
        Check(scanner.Scan(~abc).Closed == 0, "Goose held scan does not repeat close");
        Check(scanner.Scan(uint.MaxValue).Opened == abc, "Goose open uses both previous held scans");
        Check(scanner.Scan(~abc).Closed == 0, "Goose two-scan filter rejects immediate bounce");
        scanner.Clear();
        Check(scanner.Scan(uint.MaxValue).Closed == 0, "Goose scanner clear has no wake action");
    }

    private static void Bridge()
    {
        var simulation = new FixtureSimulation();
        var bridge = new FixedStepBridge(simulation);
        var trace = new List<string>();
        bridge.BeforeHostStep += state => trace.Add("before:" + state.Tick);
        bridge.SourceEventRaised += value => trace.Add("source:" + value.Tick + ":" + value.Value);
        bridge.SourcePresentationCueRaised += value => trace.Add("cue:" + value.Context.SourceTick + ":" + value.Context.ProcessOrder);
        bridge.EventRaised += value => trace.Add("legacy:" + value.Tick);
        bridge.AfterHostStep += state => trace.Add("after:" + state.Tick);
        int produced = 0;
        Func<SourceInputFrame> input = () => { produced++; return new SourceInputFrame(new[] { new SourceStationInput(2, held: SourceButtons.C) }); };
        Check(bridge.AdvanceSource(0.1, input) == 6 && produced == 6, "source bridge supplies one input per host Step without another 57-Hz clock");
        Check(simulation.Commands.Count == 6 && simulation.Commands.All(c => c.StationInputs.Stations[0].Held == SourceButtons.C),
            "actual bridge Step caller forwards raw source station frames");
        Check(trace.Count == 42, "every catch-up Step forwards all legacy and source events");
        for (int tick = 0; tick < 6; tick++)
            Check(trace.Skip(tick * 7).Take(7).SequenceEqual(new[] { "before:" + tick, "legacy:" + (tick + 1),
                "source:" + (tick + 1) + ":1", "source:" + (tick + 1) + ":2",
                "cue:" + (tick + 1) + ":4", "cue:" + (tick + 1) + ":5", "after:" + (tick + 1) }),
                "ordered per-step source events are neither dropped nor replayed between renders");
        var journal = new SourceHudEventJournal();
        foreach (SourceGameEvent value in simulation.SourceEvents) journal.Append(value);
        int delivered = 0;
        journal.Drain(value => delivered++);
        journal.Drain(value => delivered++);
        Check(delivered == 2 && journal.AcceptedCount == 2, "HUD source event queue drains every event exactly once");
        long host = bridge.State.Tick, sourceTick = ((ISourceGameState)bridge.State).SourceTick;
        bridge.SetActive(false);
        Check(simulation.Clears == 1, "focus clear calls typed IPendingSourceInput");
        Check(bridge.State.Tick == host && ((ISourceGameState)bridge.State).SourceTick == sourceTick,
            "input clear does not advance or reset match/source clock");
        Check(bridge.AdvanceSource(9, input) == 0 && produced == 6, "suspended host does not drain input");
        bridge.SetActive(true);
        Check(bridge.AdvanceSource(9, input) == 0 && produced == 6, "resume discards hidden-tab elapsed time");
        Check(bridge.AdvanceSource(1d / 60, input) == 1 && produced == 7, "resume takes exactly one fresh host Step");
        bridge.Restart();
        Check(bridge.State.Tick == 0 && simulation.Clears == 3, "restart clears pending typed input before explicit match reset");
        var legacy = new LegacySimulation();
        var legacyBridge = new FixedStepBridge(legacy);
        legacyBridge.QueueAction(InputAction.SelectPlay, PlayId.QuickPass);
        legacyBridge.Advance(1d / 60, new FieldVector(1, 0), true);
        Check(legacy.Last.Action == InputAction.SelectPlay && legacy.Last.Play == PlayId.QuickPass &&
            legacy.Last.Move.X == 1 && legacy.Last.Turbo, "legacy constructor prefix and controls remain intact");
        Reject<InvalidOperationException>(() => FixedStepBridge.BindSourceInputReset(legacy), "no private-buffer lifecycle reflection fallback");
    }

    private static void Presentation()
    {
        var state = new FixtureState { Match = Match(Configuration(true)),
            Presentation = new SourcePresentationSettings(TeamId.Away, new[] { -1, -1, 4, 11 }, -7),
            Ball = new BallView(BallMode.Held, default(FieldVector), 0, 3, -1,
                presentation: new SourceBallPresentationView(0x800)) };
        var actor = new PlayerView(3, TeamId.Home, default(FieldVector), default(FieldVector), 1,
            role: ActorRole.Receiver, source: new SourceActorView(3, new SourceRosterIdentity(5, 3),
                presentation: new SourceActorPresentationView(0x00800005, 5)));
        SourceTeamDrawState draw = SourceInterfacePresentation.DrawState(actor, state);
        Check(draw.IsSpecified && draw.HighCount && draw.QuarterbackMode && draw.Flip && draw.PhantomBall &&
            draw.JumboBall && draw.HelmetPopped, "raw draw flags/mode/phantom/bigball map from actual source metadata only");
        Check(draw.CustomHeadIndex == 11, "custom head uses actual controller station3, not Home/captain/player offsets");
        Check(SourceInterfacePresentation.Settings(state).DesignatedHomeSide == TeamId.Away &&
            SourceInterfacePresentation.Settings(state).PupBigBall == -7, "source home side and whole pup_bigball are not fabricated/normalized");
        var neutral = new PlayerView(3, TeamId.Home, default(FieldVector), new FieldVector(100, 100), 1,
            role: ActorRole.Quarterback, source: new SourceActorView(3, new SourceRosterIdentity(5, 3),
                presentation: new SourceActorPresentationView(0, 4)));
        Check(!SourceInterfacePresentation.DrawState(neutral, state).QuarterbackMode,
            "raw MODE_QBPS4 does not become MODE_QB5 from ActorRole/velocity");
        state.Presentation = null;
        Reject<SourceInterfaceException>(() => SourceInterfacePresentation.DrawState(actor, state),
            "unavailable source home/head settings fail rather than defaulting");
        state.Presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        state.Ball = default(BallView);
        Reject<SourceInterfaceException>(() => SourceInterfacePresentation.DrawState(actor, state), "missing raw ball phantom flags fail visibly");
    }

    private static void Actual(string directory, string oraclePath)
    {
        int first = assertions;
        byte[] manifest = File.ReadAllBytes(Path.Combine(directory, "interface.json"));
        string pin = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifest)).ToLowerInvariant();
        Check(pin == "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435", "actual interface prepared content identity");
        SourceInterfaceData data = SourceInterfaceData.Read(manifest, pin);
        Check(data.Images.Count == 381 && data.Planes.Count == 104 && data.Planes.Values.Sum(p => p.Count) == 5462,
            "actual original glyph/sprite/play-plane inventory");
        foreach (SourceInterfaceTexture texture in data.Textures.Values)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, texture.Path));
            Check(data.RequireTextureBytes(texture.Id, bytes).Length == texture.Width * texture.Height * 4,
                "actual source atlas pixels pass trusted content pin");
            bytes[0] ^= 1;
            Reject<SourceInterfaceException>(() => data.RequireTextureBytes(texture.Id, bytes), "changed original atlas pixel cannot silently load");
        }
        SourceInterfaceImage clock = data.Image("gameclock");
        Check(clock.Width == 104 && clock.Height == 48 && clock.AnchorX == 53 && clock.AnchorY == 24,
            "actual original gameclock dimensions/anchor");
        Check(data.Image("k_bar").Width == 151 && data.Image("k_marker").Width == 27 && data.Image("p_meter").Height == 209,
            "actual original kick/punt meter dimensions are data, not guessed accuracy constants");
        for (int frame = 1; frame <= 15; frame++)
            Check(data.Image("firbar" + frame.ToString("00")).Width > 0, "all original firebar frames remain available for actual source fire state");
        Check(data.Image("k_windtext").Width > 0 && data.Image("k_windarrow").Width == 92,
            "original kick wind art is not silently omitted");
        SourceInterfaceFont font = data.Font("bast8t");
        Check(font.Height == 9 && font.CharacterSpacing == 0 && font.SpaceWidth == 5,
            "actual source BAST8T font metrics rather than Arial");
        Check(font.Glyph('A').Width == 8 && font.Glyph('I').Width == 3 &&
            font.Glyph('a') == font.Glyph('A'), "actual variable glyph widths and source lower-case alias");
        Check(font.Glyph('1').Width == 4 && font.Glyph('0').Width == 7, "source clock/score digits retain original metrics");
        Near(SourceHudText.Width(font, "A I"), 16, "actual original bitmap measurement");
        var quantizer = new SourceCabinetQuantizer(data.Input);
        if (oraclePath != null)
        {
            using (JsonDocument oracle = JsonDocument.Parse(File.ReadAllBytes(oraclePath)))
            {
                JsonElement raw = oracle.RootElement.GetProperty("raw49");
                for (int i = 0; i < 256; i++)
                {
                    SourceCabinetStick stick = quantizer.Raw49((byte)i);
                    Check(stick.Ordinal == raw[i].GetInt32(), "Goose original raw49 byte " + i);
                    Check((3 - stick.StickZ) * 7 + 3 - stick.StickX == stick.Ordinal, "raw DTO signs reconstruct original 49-way ordinal");
                }
                JsonElement headings = oracle.RootElement.GetProperty("directions");
                JsonElement digital = oracle.RootElement.GetProperty("digital8");
                for (int i = 0; i < 49; i++)
                {
                    Check(data.Input.Directions[i] == headings[i].GetInt32(), "original C player 49-way direction " + i);
                    Check(data.Input.Digital8[i] == digital[i].GetInt32(), "original C Goose 49-to-digital " + i);
                }
                var scanner = new SourceCabinetSwitchScanner();
                foreach (JsonElement row in oracle.RootElement.GetProperty("scans").EnumerateArray())
                {
                    SourceCabinetSwitchEdges scan = scanner.Scan(row[0].GetUInt32());
                    Check(scan.Held == row[1].GetUInt32(), "original C active-low held register");
                    Check(scan.Closed == row[2].GetUInt32(), "original C two-history close transition");
                    Check(scan.Opened == row[3].GetUInt32(), "original C two-history open transition");
                }
            }
        }
        Check(quantizer.SevenLevel(-1, -1).Ordinal == 24 && quantizer.SevenLevel(1, 1).Ordinal == 24,
            "original central nine Hall positions collapse to neutral, not universal eight-way replacement");
        for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++)
            {
                SourceCabinetStick original = quantizer.SevenLevel(x, y);
                SourceCabinetStick analog = quantizer.Analog(x / 3f, y / 3f);
                Check(original.Ordinal == analog.Ordinal, "explicit seven-level analog centers retain raw Hall table output");
            }
        for (int axis = -1000; axis <= 1000; axis++)
        {
            SourceCabinetStick stick = quantizer.Analog(axis / 1000f, -axis / 1000f);
            Check(stick.StickX >= -3 && stick.StickX <= 3 && stick.StickZ >= -3 && stick.StickZ <= 3,
                "analog quantization bounds");
        }
        InputBuffer(data.Input);
        HudStates(data);
        Menus(data);
        Composition(data);
        actualAssertions += assertions - first;
    }

    private static void HudStates(SourceInterfaceData data)
    {
        var state = new FixtureState { Match = Match(Configuration()), PackedGameTime = 0x0001013b, QuarterTicksRemaining = 7200,
            HomeScore = 17, AwayScore = 9, ClockActive = false };
        SourceHudState hud = new SourceHudState(state, data);
        Check(hud.Clock == " 1:01" && !hud.ClockActive, "source HUD reads actual packed clock/active flag, not legacy ticks or text");
        Check(hud.HomeName == "CHI" && hud.AwayName == "ATL" && hud.HomeScore == 17 && hud.AwayScore == 9,
            "source HUD uses selected original team abbreviations and actual scores");
        state.Match = Match(Configuration(), kind: SourcePlayKind.Kickoff, phase: SourceMatchPhase.PreSnap, kick: SourceKickoffType.Onside);
        hud = new SourceHudState(state, data);
        Check(hud.HighlightedSide == TeamId.Away && hud.StateText == "ONSIDE KICK", "source kickoff state/highlight uses actual DTO values");
        state.Match = Match(Configuration(), phase: SourceMatchPhase.PatSelection, kind: SourcePlayKind.ExtraPoint, pat: SourcePatStatus.AwaitHumanSelection);
        Check(new SourceHudState(state, data).StateText == "FREE POINT OR TWO POINT PLAY", "actual source PAT selection is not generic next-play text");
        state.Match = Match(Configuration(), phase: SourceMatchPhase.FreePointResult, kind: SourcePlayKind.ExtraPoint,
            result: new SourceMatchResult(SourcePlayKind.ExtraPoint, SourceResultKind.ExtraPoint, homePoints: 1));
        Check(new SourceHudState(state, data).ResultGraphic == "msg_gd", "actual free PAT null classifier status remains representable");
        state.Match = Match(Configuration(), phase: SourceMatchPhase.PlayResult,
            result: new SourceMatchResult(SourcePlayKind.FieldGoal, SourceResultKind.FieldGoal, SourceResultStatus.IntentionalNoOp));
        Check(new SourceHudState(state, data).ResultGraphic == null && new SourceHudState(state, data).StateText.Contains("NO-OP"),
            "source intentional no-op must not fabricate an ITS GOOD field goal");
        state.Match = Match(Configuration(), phase: SourceMatchPhase.PeriodTransition, quarter: 6, period: SourcePeriodStatus.OvertimeKickoff);
        Check(new SourceHudState(state, data).Period == "OT3" && new SourceHudState(state, data).StateText == "OVERTIME",
            "source seven-period DTO is not clamped to four NFL quarters");
        state.Match = Match(Configuration(), phase: SourceMatchPhase.PlayResult, result: new SourceMatchResult(
            SourcePlayKind.Normal, SourceResultKind.Touchdown, SourceResultStatus.Classified, homePoints: 6));
        Check(new SourceHudState(state, data).ResultGraphic == "msg_tdn", "source classified touchdown selects actual original art");
        state.Match = null;
        Reject<SourceInterfaceException>(() => new SourceHudState(state, data), "legacy profile cannot masquerade as source HUD");
    }

    private static void Menus(SourceInterfaceData data)
    {
        var state = new FixtureState { Match = Match(Configuration(true)), LineOfScrimmage = 20 };
        var events = new List<SourceCabinetMenuEvent>();
        var selector = new SourceCabinetPlaySelection(data);
        selector.EventRaised += events.Add;
        Func<int, SourceButtons, SourceInputFrame> sample = (station, buttons) =>
            new SourceInputFrame(new[] { new SourceStationInput(station, pressed: buttons, held: buttons) });
        for (int tick = 1; tick <= 20; tick++)
        {
            state.SourceTick = tick - 1;
            Check(!selector.ApplyAtSourceTick(state, tick, sample(2, SourceButtons.B)).Stations[0].Command.IsSpecified,
                "original selection sleep20 does not leak early B presses");
        }
        state.SourceTick = 20;
        SourceInputFrame combo = selector.ApplyAtSourceTick(state, 21, sample(2, SourceButtons.A | SourceButtons.B));
        Check(!combo.Stations[0].Command.IsSpecified && !selector.Flipped && selector.Page(TeamId.Home) == 0,
            "shipping exact menu switch ignores simultaneous AB rather than prioritizing B");
        state.SourceTick = 21;
        SourceInputFrame teammate = selector.ApplyAtSourceTick(state, 22, sample(3, SourceButtons.B));
        Check(!teammate.Stations[0].Command.IsSpecified, "noncaptain teammate cannot silently pick captain play");
        state.SourceTick = 22;
        SourceInputFrame picked = selector.ApplyAtSourceTick(state, 23, sample(2, SourceButtons.B));
        SourcePlaySelection play = picked.Stations[0].Command.Play;
        Check(picked.Stations[0].Command.Action == SourceAction.SelectPlay && play.PlaySymbol == "play_ch_1" &&
            play.PageIndex == 0 && play.SlotIndex == 0 && play.Playbook == SourcePlaybookKind.Offense,
            "source selected team play retains original symbol/page/slot, not four legacy PlayIds");
        Check(events.Any(e => e.Kind == SourceCabinetMenuEventKind.Selected && e.StationId == 2), "menu emits source station event identity");
        Reject<SourceInterfaceException>(() => selector.ApplyAtSourceTick(state, 23, sample(2, SourceButtons.C)),
            "source menu cannot process a source tick twice");
        selector.Clear(); state.SourceTick = 0;
        selector.ApplyAtSourceTick(state, 1, sample(2, SourceButtons.None));
        SourceInputFrame flip = null;
        for (int tick = 2; tick <= 21; tick++)
        {
            state.SourceTick = tick - 1;
            flip = selector.ApplyAtSourceTick(state, tick, sample(2, tick == 21 ? SourceButtons.A : SourceButtons.None));
        }
        Check(selector.Flipped, "original offensive A toggles actual menu flip");
        Check(flip.Stations[0].Command.OffenseFlipped == true && flip.Stations[0].Command.Action == SourceAction.None,
            "A flip is transported explicitly even without selecting a play");
        state.SourceTick = 21;
        SourceInputFrame flipped = selector.ApplyAtSourceTick(state, 22, sample(2, SourceButtons.B));
        Check(flipped.Stations[0].Command.OffenseFlipped == true &&
            flipped.Stations[0].Command.Play.IsSpecified, "source selected flip reaches Core, not merely the visual diagram");
        state.Down = 4; state.LineOfScrimmage = 49;
        Check(selector.Slot(state, TeamId.Home, 0, 6).PlaySymbol == "play_punt", "source fourth-down punt page branch");
        Check(selector.Slot(state, TeamId.Away, 0, 7).PlaySymbol == "play_punt_return", "source corresponding return page branch");
        state.LineOfScrimmage = 50;
        Check(selector.Slot(state, TeamId.Home, 0, 6).PlaySymbol == "play_field_goal", "source field-goal range boundary50");
        state.Down = 1; state.Match = Match(Configuration(true), quarter: 1); state.PackedGameTime = 0x0f00;
        Check(selector.Slot(state, TeamId.Home, 0, 6).PlaySymbol == "play_field_goal", "source late-half under0x1000 FG page branch");
    }

    private static void Composition(SourceInterfaceData data)
    {
        var composer = new SourceHudComposer(data);
        var state = new FixtureState { Match = Match(Configuration(true), phase: SourceMatchPhase.LiveBall),
            PackedGameTime = 0x00013b18, HomeScore = 17, AwayScore = 9 };
        foreach (int[] size in new[] { new[] { 360, 740 }, new[] { 740, 360 }, new[] { 1280, 800 },
            new[] { 2560, 1080 }, new[] { 1920, 1080 } })
            foreach (double dpr in new[] { 1d, 2d, 3d })
            {
                var view = new SourceHudViewport((int)(size[0] * dpr), (int)(size[1] * dpr), size[0], size[1], dpr);
                IReadOnlyList<SourceHudPrimitive> primitives = composer.Compose(state, view, null, false, false);
                Check(primitives.Count > 60, "actual source HUD emits original bitmap glyph/texture geometry");
                foreach (SourceHudPrimitive primitive in primitives)
                    Check(view.Content.Contains(primitive.Bounds), "actual font/sprite layout fits true CSS/DPR content");
                var home = primitives.Where(p => p.Element == "score/home/name").ToArray();
                var clock = primitives.Where(p => p.Element == "score/clock").ToArray();
                foreach (SourceHudPrimitive name in home)
                    foreach (SourceHudPrimitive digit in clock)
                        Check(!name.Bounds.Overlaps(digit.Bounds), "actual original clock glyphs never overlap HOME/team name at portrait DPR2");
                state.Match = Match(Configuration(true));
                var menu = new SourceCabinetPlaySelection(data);
                IReadOnlyList<SourceHudPrimitive> pages = composer.Compose(state, view, menu, false, false);
                Check(pages.Any(p => p.Texture == "playsel8") && pages.Any(p => p.Element.Contains("/name/")),
                    "actual production HUD composes original play diagrams/bitmap names, not generic cards");
                foreach (SourceHudPrimitive primitive in pages)
                    Check(view.Content.Contains(primitive.Bounds), "actual original play plane/label fits full content");
                state.Match = Match(Configuration(true), phase: SourceMatchPhase.LiveBall);
            }
        var status = new SourceHudStatusAnimation();
        for (int i = 0; i < 8; i++) { status.Advance(i, true); Check(!status.RedClock, "source clock blink waits original post-decrement delay"); }
        status.Advance(8, true);
        Check(status.RedClock, "source clock warning toggles on ninth actual source wake");
        for (int frame = 0; frame < 100; frame++) status.Advance(8, true);
        Check(status.RedClock, "repeated renders/host-skip snapshots cannot advance original blink timer");
        Reject<SourceInterfaceException>(() => status.Advance(10, true), "HUD rejects dropped intermediate source status tick");
        status.Advance(9, false);
        Check(!status.RedClock, "source nonwarning branch clears red without a generic timer");
    }

    public static int Main(string[] args)
    {
        try
        {
            Layout(); SyntheticFont(); InputBuffer(SyntheticTables()); Bridge(); Presentation();
            MovementBootstrapChecks.Run(Check);
            if (args.Length > 0) Actual(args[0], args.Length > 1 && args[1] != "-" ? args[1] : null);
            if (args.Length > 2) Movement(args[2]);
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-interface-checks/v1", status = "passed",
                assertions = assertions, actual_assertions = actualAssertions, original_bundle_supplied = args.Length > 0,
                unity_browser_parity_claimed = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-interface-checks/v1", status = "failed",
                assertions = assertions, actual_assertions = actualAssertions, failed_assertion = error.Message }));
            return 1;
        }
    }

    private static void Movement(string path)
    {
        int first = assertions;
        byte[] bytes = File.ReadAllBytes(path);
        SourceInterfaceMovementData movement = SourceInterfaceMovementData.Read(bytes, SourceInterfaceMovementData.ContentSha256);
        using (JsonDocument original = JsonDocument.Parse(bytes))
        {
            JsonElement data = original.RootElement.GetProperty("data");
            foreach (JsonProperty property in data.GetProperty("Constants").EnumerateObject())
                Check(BitConverter.ToUInt32(BitConverter.GetBytes(movement.Constants[property.Name]), 0) == property.Value.GetUInt32(),
                    "runtime movement constant exact binary32 bits");
            for (int i = 0; i < 7; i++)
                Check(BitConverter.ToUInt32(BitConverter.GetBytes(movement.Friction[i]), 0) == data.GetProperty("Friction")[i].GetUInt32(),
                    "runtime movement friction retains exact field-indexed bits");
            for (int row = 0; row < 12; row++)
                for (int column = 0; column < 6; column++)
                {
                    Check(BitConverter.ToUInt32(BitConverter.GetBytes(movement.Defense[row].Line[column]), 0) ==
                        data.GetProperty("Defense")[row].GetProperty("Line")[column].GetUInt32(),
                        "runtime movement Line twelve-by-six order/bits");
                    Check(BitConverter.ToUInt32(BitConverter.GetBytes(movement.Defense[row].Back[column]), 0) ==
                        data.GetProperty("Defense")[row].GetProperty("Back")[column].GetUInt32(),
                        "runtime movement Back twelve-by-six order/bits");
                }
            foreach (JsonProperty property in data.GetProperty("Tables").EnumerateObject())
                Check(movement.Tables[property.Name].SequenceEqual(property.Value.EnumerateArray().Select(x => x.GetString())),
                    "runtime movement selector order/duplicates preserved");
        }
        Check(movement.Constants.Count == 23 && movement.Tables.Count == 13 && movement.Defense.Count == 12,
            "runtime-safe M1 inventory contains actual immutable constants/tables/both defense matrices");
        Reject<NotSupportedException>(() => ((IDictionary<string, float>)movement.Constants).Add("SYNTHETIC", 1),
            "runtime movement constants are immutable");
        Reject<NotSupportedException>(() => ((IList<float>)movement.Defense[0].Line)[0] = 10, "runtime defense rows are immutable");
        Reject<SourceInterfaceException>(() => SourceInterfaceMovementData.Read(bytes, new string('0', 64)),
            "runtime movement cannot trust a caller-supplied replacement pin");
        byte[] changed = (byte[])bytes.Clone(); changed[100] ^= 1;
        Reject<SourceInterfaceException>(() => SourceInterfaceMovementData.Read(changed, SourceInterfaceMovementData.ContentSha256),
            "runtime movement rejects changed/fixture payload, never falls back to Responses");
        actualAssertions += assertions - first;
    }
}

internal sealed class FixtureState : ISourcePresentationState
{
    public long Tick { get; set; }
    public long SourceTick { get; set; }
    public int SourceTicksPerSecond { get { return 57; } }
    public uint PackedGameTime { get; set; }
    public bool ClockActive { get; set; }
    public int PlayNumber { get { return 1; } }
    public GamePhase Phase { get; set; }
    public TeamId Possession { get { return TeamId.Home; } }
    public int OffenseDirection { get { return 1; } }
    public int Quarter { get { return 1; } }
    public int QuarterTicksRemaining { get; set; }
    public int PreSnapTicksRemaining { get { return 0; } }
    public int Down { get; set; } = 1;
    public float LineOfScrimmage { get; set; }
    public float FirstDownLine { get { return 50; } }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int ControlledPlayerId { get { return 3; } }
    public PlayId HomePlay { get { return PlayId.Run; } }
    public PlayId AwayPlay { get { return PlayId.Cover; } }
    public PlayOutcome LastOutcome { get { return PlayOutcome.None; } }
    public BallView Ball { get; set; }
    public IReadOnlyList<PlayerView> Players { get { return Array.AsReadOnly(new PlayerView[0]); } }
    public SourceMatchView Match { get; set; }
    public SourcePresentationSettings Presentation { get; set; }
}

internal sealed class FixtureSimulation : ISourcePresentationSimulation
{
    private FixtureState state;
    public readonly List<PlayerCommand> Commands = new List<PlayerCommand>();
    public int Clears;
    public IGameState State { get { return state; } }
    public IReadOnlyList<GameEvent> Events { get; private set; } = new GameEvent[0];
    public IReadOnlyList<SourceGameEvent> SourceEvents { get; private set; } = new SourceGameEvent[0];
    public SourcePresentationCueBatch PresentationCues { get; private set; } = new SourcePresentationCueBatch(new SourcePresentationCue[0]);
    public FixtureSimulation() { Restart(); }
    public void Step(PlayerCommand command)
    {
        Commands.Add(command);
        state = new FixtureState { Tick = state.Tick + 1, SourceTick = state.SourceTick + 1, Match = state.Match };
        Events = new[] { new GameEvent(state.Tick, GameEventType.Snapped, TeamId.Home) };
        SourceEvents = new[] { new SourceGameEvent(state.Tick, state.SourceTick, SourceEventType.Snapped, value: 1),
            new SourceGameEvent(state.Tick, state.SourceTick, SourceEventType.ControllerChanged, value: 2) };
        PresentationCues = new SourcePresentationCueBatch(new SourcePresentationCue[] {
            new SourceShakerCue(new SourcePresentationContext(state.SourceTick, 4, 3, "synthetic.c", "synthetic_script", 1), 2, 10, 3),
            new SourceCallbackCue(new SourcePresentationContext(state.SourceTick, 5, 3, "synthetic.c", "synthetic_script", 2),
                SourcePresentationCueKind.SoundCallback, "synthetic_sound", -1, 0, -1, 3)
        });
    }
    public void ClearPendingInput() { Clears++; }
    public void Restart(int seed = 1)
    {
        var team = new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 });
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, team, team, new[] { new SourceStationConfiguration(2, TeamId.Home, 3) },
            TeamId.Home, SourceCaptainSlot.First);
        state = new FixtureState { Match = new SourceMatchView(configuration, SourcePlayKind.Normal, SourceMatchPhase.LiveBall,
            new SourcePeriodView(0), new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home,
                SourceCaptainSlot.First), new SourceQuarterScore[0], new[] { new SourceStationView(2, 3, 1) }) };
        Commands.Clear(); Events = new GameEvent[0]; SourceEvents = new SourceGameEvent[0];
        PresentationCues = new SourcePresentationCueBatch(new SourcePresentationCue[0]);
    }
}

internal sealed class LegacySimulation : IGameSimulation
{
    public PlayerCommand Last;
    public IGameState State { get { return new FixtureState(); } }
    public IReadOnlyList<GameEvent> Events { get { return new GameEvent[0]; } }
    public void Step(PlayerCommand command) { Last = command; }
    public void Restart(int seed = 1) { }
}
