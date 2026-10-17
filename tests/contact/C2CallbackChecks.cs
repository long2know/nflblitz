using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class Program
{
    private static SourceC2CallbackData c2Data;
    private static SourceAnimationLibrary c2Library;
    private static SourceCatchGeometry c2Geometry;
    private static string c2Root;
    private static string c2NativeRoot;
    private static readonly Dictionary<string, object> c2Evidence = new Dictionary<string, object>();
    private static readonly string[] C2Scripts = { "taunt_1_anim", "taunt_2_anim", "taunt_3_anim",
        "taunt_4_anim", "m_laugh1_anim", "m_qbwhat_anim", "m_qbfite_anim", "m_mscat1_anim", "m_mscat2_anim" };
    private static readonly string[] C2Clips = { "TAUNT_1", "TAUNT_2", "TAUNT_3", "TAUNT_4", "M_LAUGH1",
        "M_QBWHAT", "M_QBFITE", "M_MSCAT1", "M_MSCAT2" };

    private static void C2Check(bool condition, string identity) { Check(condition, "C2 " + identity); }
    private static void C2Throws<T>(Action action, string identity) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        C2Check(rejected, identity);
    }
    private static int C2Int(JsonElement row, string key) { return row.GetProperty(key).GetInt32(); }

    private static GameSimulation CreateC2(Func<SourceRandomSite, int> raw = null,
        SourceC2Bootstrap bootstrap = null, ISourceActorGeometry actualGeometry = null)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
        Func<int, ISourceRandom> factory = seed =>
        {
            var fallback = new Random(37);
            return new SourceInjectedRandom(raw ?? (site => fallback.Next(0, 0x1000000)),
                new SourceRandomProvenance(SourceC2CallbackData.EntryProfile,
                    "Explicit source-input oracle provider; NOT target libc PRNG", "C2 bounded contract inputs",
                    SourceRandomEvidence.ExternalUnverified));
        };
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        var movementSettings = new SourceMovementSettings(0, 0, false, 0, 0, false);
        SourceC2Bootstrap entry = bootstrap ?? SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(c2Data);
        GameSimulation game;
        if (actualGeometry == null)
            game = SourceC2Composition.CreateSimulation(c2Library, real.ReferencePivots, real.Playbook,
                configuration, real.Rosters, presentation, real.MovementRuntime, movementSettings,
                real.SoundFunctions, entry, factory, 1);
        else
        {
            // Preserved historical proxy trace, never the canonical real-composition qualification.
            game = new GameSimulation(c2Library, actualGeometry, real.Playbook, configuration, factory, 1,
                real.Rosters, presentation, real.MovementRuntime, movementSettings, entry);
            game.BindSourceSoundVmData(real.SoundFunctions);
        }
        game.SourceCameraHeadingProvider = (snapshot, tick) => FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
        lastGame = game;
        return game;
    }

    private static void C2CallbackChecks(string originalRoot, string inputs, string nativeRootPath, bool naturalOnly)
    {
        c2Root = Path.GetFullPath(inputs);
        byte[] dataBytes = File.ReadAllBytes(Path.Combine(c2Root, "c2-callback-data.json"));
        string dataHash = File.ReadAllText(Path.Combine(c2Root, "c2-callback-data.json.sha256")).Trim();
        byte[] hitBytes = File.ReadAllBytes(Path.Combine(c2Root, "m-qbfite-hit-extension.safn"));
        string hitHash = File.ReadAllText(Path.Combine(c2Root, "m-qbfite-hit-extension.safn.sha256")).Trim();
        c2Data = SourceC2CallbackData.Read(dataBytes, dataHash, SourceAudioFunctionData.Read(hitBytes, hitHash));
        string animation = Path.Combine(c2Root, "animation281");
        c2Library = SourceAnimationLibrary.Read(File.ReadAllBytes(Path.Combine(animation, "export-inventory.json")),
            relative => File.ReadAllBytes(Path.Combine(animation, relative)));
        c2Geometry = new SourceCatchGeometry(c2Library, real.ReferencePivots);
        C2Check(c2Library.InventorySha256 == c2Data.AnimationManifestSha256 &&
            c2Library.SequenceNames.Count == 281 && c2Library.FrameCount == 26230,
            "real281 distinct union contains exactly26230 selected original frames");
        C2Check(c2Data.Bindings.Count == 9 && c2Data.HitExtensionData.VmBindings.Count == 1 &&
            c2Data.CallTemplates.Count == 9 && real.SoundFunctions.VmBindings.Count == 6 &&
            real.SoundFunctions.PayloadSha256 == GameSimulation.SourceSoundVmDataSha256,
            "new9 typed tuples and single7538 SAFN do not replace old6 frozen payload");
        if (naturalOnly) { C2NaturalTrace(); return; }
        c2NativeRoot = Path.GetFullPath(nativeRootPath);
        using (JsonDocument receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c2NativeRoot, "receipt.json"))))
        {
            C2Check(receipt.RootElement.GetProperty("proper_original_void_functions_no_undefined_scalar_returns").GetBoolean(),
                "native C observes proper original void effects, not undefined int return values");
            C2Check(receipt.RootElement.GetProperty("cases_sha256").GetString() ==
                Hash(File.ReadAllBytes(Path.Combine(c2NativeRoot, "cases.json"))), "native original-C detached cases pin");
        }
        using (JsonDocument cases = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c2NativeRoot, "cases.json"))))
        using (JsonDocument sourcePrograms = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c2NativeRoot, "source-programs.json"))))
        {
            C2NativeCallbacks(cases.RootElement.GetProperty("callbacks"));
            C2Speech(cases.RootElement.GetProperty("speech"));
            C2Programs(sourcePrograms.RootElement);
            C2VmCallers();
            C2BanksAndEpoch(cases.RootElement.GetProperty("banks"));
            C2NativeEndEpochs(cases.RootElement.GetProperty("end_epochs"));
            C2Whistles(cases.RootElement.GetProperty("whistles"));
            C2ImmediateWhistles(cases.RootElement.GetProperty("immediate_whistles"));
            C2PlayerDown(cases.RootElement.GetProperty("player_down"));
        }
        C2MissingInputsAndFifo();
        C2NegativeTargetPortabilityRepair();
        C2GlobalLatchAndIntEvidence();
        C2ActualPlayerDownWake();
        C2InputGuards(dataBytes, dataHash, hitBytes);
        File.WriteAllText(Path.Combine(output, "c2-focused-evidence.json"),
            JsonSerializer.Serialize(c2Evidence, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }

    private static void C2Input(GameSimulation game, JsonElement row)
    {
        SimulationState model = game.Model;
        int actorId = C2Int(row, "actor"), hit = C2Int(row, "hit"), offense = C2Int(row, "off");
        model.SourceTick++;
        model.Possession = (TeamId)offense;
        model.SourceLastPossession = offense;
        model.SourceRules = new SourceRulesState(offense, offense, C2Int(row, "down"), 30,
            C2Int(row, "first"), (SourcePlayType)C2Int(row, "type"));
        model.SourceFlags = (SourceGameFlags)C2Int(row, "flags");
        model.SourceLastResult = (SourcePlayResult)C2Int(row, "result");
        model.CarrierId = C2Int(row, "carrier"); model.TargetPlayerId = C2Int(row, "receiver");
        model.Phase = C2Int(row, "gm") == 2 ? GamePhase.LiveBall : C2Int(row, "gm") == 3 ? GamePhase.DeadBall : GamePhase.PreSnap;
        model.BallKind = (SourceBallKind)(C2Int(row, "ball") + 1);
        model.SourceTaunted = C2Int(row, "latch") != 0;
        model.SourceTeamFire[0] = model.SourceTeamFire[1] = false;
        model.SourceTeamFire[1 - offense] = C2Int(row, "fire") != 0;
        if (model.SourceAngles == null) model.SourceAngles = new int[14, 14];
        if (model.SourceDistances == null) model.SourceDistances = new float[14, 14];
        for (int n = 0; n < 14; n++) model.SourceAngles[actorId, n] = C2Int(row, "angle");
        ActorState actor = model.Players[actorId];
        actor.SourceMode = (SourceActorMode)C2Int(row, "mode");
        actor.Facing = actor.TargetFacing = C2Int(row, "facing"); actor.SourceHitPartnerId = hit;
        actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0;
        if (hit >= 0) SourcePlayerDriver.SetSourcePosition(model.Players[hit],
            new SourceVector3(row.GetProperty("x").GetSingle(), 0, row.GetProperty("z").GetSingle()));
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", actorId, actorId);
    }

    private static void C2NativeCallbacks(JsonElement rows)
    {
        JsonElement active = default(JsonElement);
        bool running = false;
        GameSimulation game = CreateC2(site => !running ? 99 :
            site.BoundOrModulus == 100 ? C2Int(active, "chance") : C2Int(active, "choice"));
        var counts = new int[9]; int compared = 0;
        Action<ActorState, string, int, int, int> execute =
            Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void");
        foreach (JsonElement item in rows.EnumerateArray())
        {
            active = item.GetProperty("input"); JsonElement expected = item.GetProperty("expected");
            running = false; C2Input(game, active);
            ActorState actor = game.Model.Players[C2Int(active, "actor")];
            actor.AnimationState.FullBody("breath_anim", actor, game.SourceTick, n => 0, AnimationSelectionEvidence.SourceVerified);
            int beforeEvents = game.C2Events.Count, during = -1;
            Action<ActorState> old = actor.AnimationState.Primary.BeforeInstruction;
            actor.AnimationState.Primary.BeforeInstruction = a =>
            {
                if (old != null) old(a);
                if (during < 0 && C2Scripts.Contains(a.AnimationState.Primary.ScriptSymbol))
                    during = game.Model.SourceTaunted.Value ? 1 : 0;
            };
            running = true;
            string function = C2Int(active, "kind") == 0 ? "maybe_taunt" : C2Int(active, "kind") == 1 ? "maybe_qb_mad" : "maybe_receiver_mad";
            execute(actor, function, 0, 0, 0);
            actor.AnimationState.Primary.BeforeInstruction = old;
            running = false;
            int chosen = C2Int(expected, "selected");
            SourceC2Event[] selected = game.C2Events.Skip(beforeEvents).Where(e => e.SelectedOrdinal.HasValue).ToArray();
            C2Check(game.Model.SourceRandomCallIndex == C2Int(expected, "draws"),
                "complete native callback ordered guards and exact RNG count");
            var trace = (SourceInjectedRandom)typeof(GameSimulation).GetField("sourceRandom",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            C2Check(trace.Trace.Where(d => d.Site.SourceTick == game.SourceTick &&
                d.Site.SourceIdentity.StartsWith("player.c:maybe_", StringComparison.Ordinal))
                .Select(d => d.Site.BoundOrModulus).SequenceEqual(expected.GetProperty("bounds").EnumerateArray().Select(v => v.GetInt32())),
                "complete native callback exact RNG bounds/order");
            C2Check(game.Model.SourceTaunted == (C2Int(expected, "taunted") != 0), "complete native callback shared latch result");
            C2Check((chosen >= 0) == (selected.Length == 1), "complete native callback selection/no-selection effect");
            if (chosen >= 0)
            {
                C2Check(actor.AnimationState.Primary.ScriptSymbol == C2Scripts[chosen] &&
                    actor.AnimationState.Primary.SequenceName == C2Clips[chosen] && actor.AnimationState.Primary.IsSpecified &&
                    actor.AnimationState.Primary.IsUninterruptible, "native selected exact script executes synchronously to first frame break");
                C2Check(during == C2Int(expected, "before"), "native latch AFTER taunt and BEFORE mad synchronous change_anim");
                C2Check(selected[0].SelectedOrdinal == C2Int(active, "choice") %
                    (function == "maybe_taunt" ? 5 : function == "maybe_qb_mad" ? 3 : 2),
                    "native weighted selection retains actual ordinal including QB duplicate");
                counts[chosen]++;
            }
            compared++;
        }
        C2Check(counts.All(n => n > 0), "all five taunts and four distinct mad programs selected by real native branch inputs");
        c2Evidence["native_callback_cases"] = compared; c2Evidence["selected_nine_counts"] = counts;
    }

    private static SourceCallbackCue C2FunctionInputCue(int sp, int percent = 999)
    {
        return new SourceCallbackCue(new SourcePresentationContext(42, 0, 0, "sounds.c", "taunt_speech", 507,
            opcodeSymbol: "source-function-oracle-input"), SourcePresentationCueKind.SoundCallback,
            "taunt_speech", sp, percent, 0);
    }

    private static void C2Speech(JsonElement rows)
    {
        int compared = 0, zeroIds = 0;
        foreach (JsonElement item in rows.EnumerateArray())
        {
            JsonElement row = item.GetProperty("input"), expected = item.GetProperty("expected");
            int sp = C2Int(row, "sp"), raw = 0;
            var random = new SourceInjectedRandom(site => raw++ == 0 ? C2Int(row, "chance") : C2Int(row, "index"),
                new SourceRandomProvenance("C2/speech", "Explicit pinned-C function inputs", "all signed sp branches", SourceRandomEvidence.ExternalUnverified));
            SourceCallbackCue cue = C2FunctionInputCue(sp);
            var context = new SourceC2SoundContext(SourceC2SoundFunctionKind.TauntSpeech, 42, "player_proc", 7, 0,
                "sounds.c", "taunt_speech", 507, sp, 999, 0, cue);
            SourceAudioFunctionResult actual = SourceC2SoundFunctions.Select(c2Data, context.Kind, context,
                new SourceC2SoundState(C2Int(row, "bank") == 0 ? "taunts3" : "taunts4", "players"), random,
                new SourceRandomContext(42, 0, "player_proc", 7, "C2/speech"));
            C2Check(actual.RandomCalls == C2Int(expected, "draws") && actual.Calls.Count == C2Int(expected, "calls"),
                "full native taunt_speech branch/probability/RNG contract");
            C2Check(actual.RandomDraws.Select(d => d.Site.BoundOrModulus).SequenceEqual(
                expected.GetProperty("bounds").EnumerateArray().Select(v => v.GetInt32())),
                "full native taunt_speech exact helper bounds/order");
            if (actual.Calls.Count > 0)
            {
                SourceAudioResolvedOriginalSoundCall call = actual.Calls[0];
                C2Check(call.PlaylistId == C2Int(expected, "id") && call.Bank == expected.GetProperty("bank").GetString() &&
                    call.Volume == C2Int(expected, "volume") && call.PriorityWord == C2Int(expected, "priority") &&
                    call.Pan == 127 && call.HasOriginalAnimationCue && call.C2Context == context,
                    "full native taunt_speech actual bank/id/volume/priority/caller");
                if (call.PlaylistId == 0) zeroIds++;
            }
            compared++;
        }
        C2Check(zeroIds > 0, "legitimate zero taunt IDs survive without weakening old SAFN positive-only tables");
        c2Evidence["native_speech_cases"] = compared; c2Evidence["legitimate_zero_ids"] = zeroIds;
    }

    private static void C2Programs(JsonElement originals)
    {
        IReadOnlyDictionary<string, SourceAnimationProgram> extension = SourceAnimationPrograms.CreateC2Extension();
        C2Check(extension.Count == 9, "per-instance extension has exactly nine scripts, not a mutable global catalog");
        int instructions = 0;
        foreach (string symbol in C2Scripts)
        {
            JsonElement[] expected = originals.GetProperty(symbol).EnumerateArray().ToArray();
            SourceAnimationInstruction[] actual = extension[symbol].Instructions;
            C2Check(actual.Length == expected.Length, "nine program native source instruction count " + symbol);
            for (int n = 0; n < actual.Length; n++)
            {
                C2Check(actual[n].Op.ToString() == expected[n].GetProperty("op").GetString(), "nine program original ordered opcode " + symbol);
                foreach (JsonProperty member in expected[n].EnumerateObject())
                {
                    if (member.Name == "op") continue;
                    string field = char.ToUpperInvariant(member.Name[0]) + member.Name.Substring(1);
                    object value = typeof(SourceAnimationInstruction).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(actual[n]);
                    C2Check(member.Value.ValueKind == JsonValueKind.String ? (string)value == member.Value.GetString()
                        : value is float ? (float)value == member.Value.GetSingle() : (int)value == member.Value.GetInt32(),
                        "nine program exact original operand/source line " + symbol + "/" + field);
                }
                instructions++;
            }
        }
        C2Throws<NotSupportedException>(() => SourceAnimationPrograms.Get("taunt_1_anim"),
            "legacy131 contact group is not silently extended through global mutable lookup");
        var programObject = (IDictionary<string, SourceAnimationProgram>)extension;
        C2Throws<NotSupportedException>(() => programObject.Add("alias", extension["taunt_1_anim"]), "C2 per-instance program inventory is immutable");
        c2Evidence["exact_nine_source_instructions"] = instructions;
        int speechSites = 0, hitSites = 0, clampObservations = 0;
        for (int script = 0; script < C2Scripts.Length; script++)
        {
            GameSimulation game = CreateC2(site => 99);
            ActorState actor = game.Model.Players[0];
            game.Model.Phase = GamePhase.DeadBall; game.Model.CarrierId = 1; game.Model.TargetPlayerId = 1;
            game.Model.SourceAngles = new int[14, 14]; actor.SourceHitPartnerId = 1;
            actor.SourceMode = SourceActorMode.Quarterback; actor.SourceRawFlags |= 2;
            actor.Facing = actor.TargetFacing = 0;
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", actor.Id, 0);
            actor.AnimationState.FullBody(C2Scripts[script], actor, game.SourceTick, n => 0, AnimationSelectionEvidence.SourceVerified);
            C2Check(actor.AnimationState.Primary.ScriptSymbol == C2Scripts[script] &&
                actor.AnimationState.Primary.SequenceName == C2Clips[script] && actor.AnimationState.Primary.IsSpecified,
                "real nine program first frame break " + C2Scripts[script]);
            int max = 0, ticks = 0;
            while (actor.AnimationState.Primary.ScriptSymbol == C2Scripts[script] && ticks++ < 1000)
            {
                game.Model.SourceTick++;
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", actor.Id, 0);
                actor.AnimationState.Advance(actor, game.SourceTick, n => 0);
                max = Math.Max(max, actor.AnimationState.Primary.FrameOrdinal);
                if (script == 6 && actor.AnimationState.Primary.FrameOrdinal == 164) clampObservations++;
            }
            C2Check(ticks < 1000 && (actor.AnimationState.Primary.ScriptSymbol == "breath_anim" ||
                actor.AnimationState.Primary.ScriptSymbol == "stance_anim_int"),
                "real nine program exact existing BREATH/RUSH_C continuation " + C2Scripts[script]);
            SourceSoundVmDispatch[] sounds = game.TakeSourceSoundVmDispatches().Dispatches.Where(d => d.C2Context != null).ToArray();
            C2Check(sounds.Length == 1 && sounds[0].Status == SourceSoundVmStatus.Resolved,
                "real nine program reaches its actual later wake sound tuple " + C2Scripts[script]);
            SourceSoundVmDispatch sound = sounds[0];
            C2Check(sound.C2Context.ActorId == 0 && sound.RandomContext.Process == "player_proc" &&
                sound.RandomContext.SourceTick == sound.C2Context.SourceTick, "later wake sound retains actor/process/current RNG context");
            if (script == 6)
            {
                hitSites++;
                C2Check(sound.C2Context.SourceLine == 7538 && sound.C2Context.Argument0 == 70 &&
                    sound.Result.Calls.Any(c => c.PlaylistId == 70), "actual m_qbfite get_hit_snd70 uses unchanged old selector");
            }
            else
            {
                speechSites++;
                C2Check(sound.Result.Calls.Single().Bank == game.C2State.TauntBank, "later speech uses source boot-selected taunt bank");
                if (script >= 5) C2Check(sound.Result.RandomCalls == 0, "literal16/47 has no inside helper RNG");
                if (script < 5) C2Check(sound.RandomContext.SourceTick > 0, "initial five speech executes after interpolation on a later source wake");
            }
        }
        C2Check(speechSites == 8 && hitSites == 1 && clampObservations > 0,
            "all8 speech sites and hit7538 plus original release clamp, no padded M_QBFITE poses");
        c2Evidence["real_later_speech_sites"] = speechSites; c2Evidence["real_hit_sites"] = hitSites;
        c2Evidence["m_qbfite_release_clamp_observations"] = clampObservations;
    }

    private static JsonElement C2BaseInput(int kind, int choice = 0)
    {
        using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(new {
            kind, actor = kind == 0 ? 7 : 0, hit = kind == 0 ? 0 : 1, latch = 0, flags = 0x400,
            off = 0, down = 2, first = 60, type = 0, result = 0, carrier = kind == 2 ? -1 : 0,
            receiver = 0, facing = 0, angle = 0, mode = kind == 0 ? 3 : kind == 1 ? 5 : 7,
            gm = 3, ball = 0, fire = 0, chance = 0, choice, x = -156, z = 0 })))
            return doc.RootElement.Clone();
    }

    private static void C2VmCallers()
    {
        int cases = 0;
        foreach (int kind in new[] { 0, 1, 2 })
        for (int ordinal = 0; ordinal < (kind == 0 ? 5 : kind == 1 ? 3 : 2); ordinal++)
        {
            int choice = ordinal;
            GameSimulation game = CreateC2(site => site.SourceIdentity.Contains("player.c:maybe_")
                ? site.BoundOrModulus == 100 ? 0 : choice : 99);
            C2Input(game, C2BaseInput(kind, choice));
            ActorState actor = game.Model.Players[kind == 0 ? 7 : 0];
            actor.AnimationState.FullBody("get_up_b_anim", actor, game.SourceTick, n => 99, AnimationSelectionEvidence.SourceVerified);
            int wakes = 0;
            Exception continuationFailure = null;
            while (!game.C2Events.Any(e => e.SelectedOrdinal.HasValue) && wakes++ < 100)
            {
                game.Model.SourceTick++;
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", actor.Id, actor.Id);
                try { actor.AnimationState.Advance(actor, game.SourceTick, n => 99); }
                catch (InvalidOperationException error) { continuationFailure = error; break; }
                catch (NotSupportedException error) { continuationFailure = error; break; }
            }
            string script = kind == 0 ? C2Scripts[choice] : kind == 1 ? C2Scripts[choice == 2 ? 6 : 5] : C2Scripts[7 + choice];
            C2Check(continuationFailure == null && wakes < 100 && actor.AnimationState.Primary.ScriptSymbol == script &&
                actor.AnimationState.Primary.IsSpecified && actor.AnimationState.Primary.IsUninterruptible,
                "actual get-up caller rebinds to selected script and stops at its first frame break" +
                (continuationFailure == null ? "" : "; unexpected old continuation=" + continuationFailure.Message));
            C2Check(actor.AnimationState.Primary.CodeReturnEvidence == SourceAnimationCodeReturnEvidence.VoidOpaque,
                "actual void callback retains opaque MODE_ZERO evidence, never manufactured scalar zero");
            cases++;
        }
        GameSimulation conditional = CreateC2(site => 99);
        ActorState target = conditional.Model.Players[0];
        var invalid = new Dictionary<string, SourceAnimationProgram> {
            { "conditional_void", new SourceAnimationProgram("conditional_void",
                new SourceAnimationInstruction(SourceAnimationOp.MovementBranchZero, text: "maybe_qb_mad", branchPc: 0)) } };
        var layer = new SourceAnimationLayerState(c2Library, 0, false, invalid);
        bool invoked = false;
        layer.C2VoidCode = (a, f, x, y, z) => invoked = true;
        C2Throws<InvalidOperationException>(() => layer.Change("conditional_void", target, 1, n => 0),
            "conditional query rejects opaque VOID before invoking effects/RNG");
        C2Check(!invoked, "opaque VOID conditional rejection occurs before any original action");
        c2Evidence["actual_get_up_caller_rebindings"] = cases;
    }

    private static void C2BanksAndEpoch(JsonElement rows)
    {
        foreach (JsonElement item in rows.EnumerateArray())
        {
            JsonElement input = item.GetProperty("input"), expected = item.GetProperty("expected");
            int cursor = 0;
            GameSimulation game = CreateC2(site =>
            {
                int at = cursor++;
                return at == 0 ? C2Int(input, "first") : at == 1 ? C2Int(input, "second") : 99;
            });
            C2Check(game.C2State.AnnouncerBank == expected.GetProperty("announcer").GetString() &&
                game.C2State.TauntBank == expected.GetProperty("taunt").GetString() && game.C2State.BankEpoch == 1,
                "actual source boot selects announcer2 THEN taunt2 before initial AI RNG");
            C2Check(cursor > 2 && game.C2Events.First().Kind == SourceC2EventKind.BootBanksSelected,
                "new entry has two genuine boot draws, not old1144/1164 forced goldens");
            var trace = (SourceInjectedRandom)typeof(GameSimulation).GetField("sourceRandom",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            C2Check(trace.Trace.Take(2).Select(d => d.Site.BoundOrModulus).SequenceEqual(new[] { 2, 2 }) &&
                trace.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
                trace.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560") &&
                trace.Trace.Take(2).All(d => d.Site.SourceTick == 0 && d.Site.ActorId == -1),
                "actual bank-entry RNG identities/order precede initial AI");
        }
        GameSimulation epoch = CreateC2(site => 99);
        C2Input(epoch, C2BaseInput(0));
        epoch.Model.Phase = GamePhase.LiveBall; epoch.Model.SourceTaunted = true;
        epoch.Model.SourcePlayEndX = -156; epoch.Model.SourcePlayEndZ = 0;
        SourceRulesState before = epoch.Model.SourceRules;
        Bind<Action<SourcePlayEndCause, int, FieldVector, int>>(epoch, "QueueSourceC2PlayEnd")(
            SourcePlayEndCause.CarrierTackledInBounds, 0, new FieldVector(30, 0), 7);
        C2Check(epoch.Model.Phase == GamePhase.DeadBall && epoch.C2State.ResultPending &&
            epoch.C2State.Taunted == true && epoch.Model.SourceRules.Down == before.Down,
            "play-over exposes mode without early rules/result/latch rewrite");
        Bind<Action>(epoch, "CommitSourceC2PlayEnd")();
        C2Check(epoch.C2State.ResultPending && epoch.C2State.Taunted == true, "result is not committed in same actor wake");
        epoch.Model.SourceTick++;
        Bind<Action<string, int, int>>(epoch, "BeginSourceProcess")("game_proc", -1, 0);
        Bind<Action>(epoch, "CommitSourceC2PlayEnd")();
        C2Check(!epoch.C2State.ResultPending && epoch.C2State.Taunted == false &&
            epoch.Model.SourceRules.Down == before.Down + 1 && epoch.C2State.EndEpoch == 1 &&
            epoch.C2Events.Last().Process == "game_proc", "next actual game_proc boundary commits once and resets global latch");
        epoch.Model.SourceTaunted = true;
        Bind<Action>(epoch, "CommitSourceC2PlayEnd")();
        C2Check(epoch.C2State.Taunted == true, "latch is not reset again on generic dead-ball wake");
        epoch.Model.SourceClock = SourceClock.Create(0, firstWakeTick: epoch.SourceTick + 1);
        epoch.Model.Quarter = 1;
        epoch.Model.SourceRules = new SourceRulesState(0, 0, 2, 30, 60);
        long bankEpoch = epoch.C2State.BankEpoch;
        epoch.Step(new PlayerCommand(action: InputAction.Continue));
        epoch.Step(new PlayerCommand(action: InputAction.Continue));
        C2Check(epoch.Model.Quarter == 2 && epoch.C2State.BankEpoch == bankEpoch + 1 &&
            epoch.C2Events.Any(e => e.Kind == SourceC2EventKind.QuarterBanksSelected) &&
            epoch.C2State.Taunted == true, "actual quarter event reloads both banks before clock restart without extra latch reset");
        c2Evidence["native_bank_cases"] = rows.GetArrayLength(); c2Evidence["actual_quarter_reload"] = true;
    }

    private static void C2NativeEndEpochs(JsonElement rows)
    {
        foreach (JsonElement item in rows.EnumerateArray())
        {
            JsonElement input = item.GetProperty("input"), expected = item.GetProperty("expected");
            GameSimulation game = CreateC2(site => 99);
            C2Input(game, C2BaseInput(0)); game.Model.Phase = GamePhase.LiveBall;
            game.Model.SourceTaunted = true; game.Model.SourcePlayEndX = -156;
            long origin = game.SourceTick;
            for (int frame = 1; frame <= C2Int(input, "over_after") + 1; frame++)
            {
                game.Model.SourceTick = origin + frame;
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("game_proc", -1, 0);
                Bind<Action>(game, "CommitSourceC2PlayEnd")();
                if (frame <= C2Int(input, "over_after"))
                    C2Check(game.C2State.Taunted == (C2Int(expected, "before") != 0),
                        "native game_proc waits while only GF_PLAY_ENDING, never resets in live/ending wake");
                if (frame == C2Int(input, "over_after"))
                    Bind<Action<SourcePlayEndCause, int, FieldVector, int>>(game, "QueueSourceC2PlayEnd")(
                        SourcePlayEndCause.CarrierTackledInBounds, 0, new FieldVector(30, 0), 7);
            }
            C2Check(game.C2State.Taunted == (C2Int(expected, "after") != 0) && !game.Model.ClockActive &&
                !game.C2State.ResultPending && C2Int(expected, "finished") == 1 &&
                game.C2Events.Count(e => e.Kind == SourceC2EventKind.ResultCommitted) == 1,
                "native original wait/reset window commits at first next source wake exactly once");
        }
        c2Evidence["native_game_proc_wait_reset_cases"] = rows.GetArrayLength();
    }

    private static void C2GlobalLatchAndIntEvidence()
    {
        GameSimulation game = CreateC2(site => 0);
        C2Input(game, C2BaseInput(0));
        ActorState actor = game.Model.Players[7];
        Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(actor, "maybe_taunt", 0, 0, 0);
        C2Check(game.C2State.Taunted == true, "one source global taunt latch is set by defensive actor");
        long draws = game.Model.SourceRandomDraws;
        for (int id = 0; id < 14; id++)
        {
            ActorState other = game.Model.Players[id]; other.SourceMode = SourceActorMode.Quarterback;
            other.SourceHitPartnerId = id == 0 ? 1 : 0;
            game.Model.CarrierId = id; game.Model.TargetPlayerId = id;
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", id, id);
            Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(other, "maybe_taunt", 0, 0, 0);
            Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(other, "maybe_qb_mad", 0, 0, 0);
            game.Model.CarrierId = -1;
            Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(other, "maybe_receiver_mad", 0, 0, 0);
        }
        C2Check(game.Model.SourceRandomDraws == draws && game.C2State.Taunted == true,
            "same global latch blocks all14 actors/both teams/all3 callbacks without extra RNG");
        Bind<Action>(game, "BeginSourcePlay")();
        C2Check(game.C2State.Taunted == true, "source begin-play does not reset retained global latch");
        foreach (int angle in new[] { 0, 512 })
        {
            game = CreateC2(site => 99); actor = game.Model.Players[0];
            game.Model.CarrierId = 1; actor.SourceHitPartnerId = 1;
            game.Model.SourceAngles = new int[14, 14]; game.Model.SourceAngles[0, 1] = angle;
            actor.Facing = actor.TargetFacing = 0;
            actor.AnimationState.FullBody("taunt_1_anim", actor, game.SourceTick, n => 0, AnimationSelectionEvidence.SourceVerified);
            C2Check(actor.AnimationState.Primary.CodeReturnEvidence == SourceAnimationCodeReturnEvidence.KnownInt &&
                ((actor.AnimationState.Primary.Mode & SourceAnimationModes.Zero) != 0) == (angle == 0),
                "source-declared int face_carrier keeps proper MODE_ZERO set/clear semantics");
        }
        game = CreateC2(site => 99); C2Input(game, C2BaseInput(0));
        game.Model.CarrierId = -1; game.Model.TargetPlayerId = 1; actor = game.Model.Players[7]; actor.SourceHitPartnerId = 0;
        draws = game.Model.SourceRandomDraws;
        Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(actor, "maybe_taunt", 0, 0, 0);
        C2Check(game.Model.SourceRandomDraws == draws && game.C2Events.Count(C2IsPortabilityDiagnostic) == 1,
            "noneligible negative target uses disclosed portability repair without chance/table draws");
        c2Evidence["global_latch_all14_and_original_int_evidence"] = true;
    }

    private static bool C2IsPortabilityDiagnostic(SourceC2Event item)
    {
        return item.Detail != null && item.Detail.StartsWith("PORTABILITY_REPAIR:NO_ELIGIBLE_CANDIDATE;",
            StringComparison.Ordinal);
    }

    private static JsonElement C2RecordedNegativeTargetInput()
    {
        using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(new {
            kind = 0, actor = 4, hit = 7, latch = 0, flags = 0x480, off = 1, down = 3, first = 100,
            type = 0, result = 5, carrier = -1, receiver = 11, facing = 228, angle = 0, mode = 9,
            gm = 3, ball = 0, fire = 0, chance = 0, choice = 0, x = -169.93988f, z = 28.273537f })))
            return doc.RootElement.Clone();
    }

    private static void C2NegativeTargetPortabilityRepair()
    {
        GameSimulation game = CreateC2(site => 99);
        C2Input(game, C2RecordedNegativeTargetInput());
        game.Model.SourceTick = 2543;
        ActorState actor = game.Model.Players[4];
        Action<ActorState, string, int, int, int> execute =
            Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void");
        game.TakeSourceSoundVmDispatches();
        long draws = game.Model.SourceRandomDraws;
        int callIndex = game.Model.SourceRandomCallIndex;
        Func<string> state = () => JsonSerializer.Serialize(new {
            game.Model.SourceFlags, game.Model.SourceRules, game.Model.SourceTaunted,
            game.Model.CarrierId, game.Model.TargetPlayerId, game.Model.SourceLastResult,
            actor.Facing, actor.TargetFacing, actor.SourceHitPartnerId, actor.SourceRawFlags,
            actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
            actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId,
            actor.AnimationState.Primary.Mode, position = SourcePlayerDriver.WorldPosition(actor)
        }, new JsonSerializerOptions { IncludeFields = true });
        string before = state();
        Exception error = null;
        try { execute(actor, "maybe_taunt", 0, 0, 0); } catch (Exception e) { error = e; }
        C2Check(error == null, "portability repair returns VOID without unsafe original bearing load" +
            (error == null ? "" : ";error=" + error.Message));
        C2Check(game.Model.SourceRandomDraws == draws && game.Model.SourceRandomCallIndex == callIndex,
            "portability repair consumes ZERO callback RNG or index/table draws");
        C2Check(state() == before && game.TakeSourceSoundVmDispatches().Dispatches.Count == 0,
            "portability repair changes no flags hit carrier receiver animation latch or sound");
        SourceC2Event[] diagnostics = game.C2Events.Where(C2IsPortabilityDiagnostic).ToArray();
        // This dependency is deliberately unavailable: this noneligible boundary must never read it.
        game.Model.SourceAngles = null;
        error = null;
        try { execute(actor, "maybe_taunt", 0, 0, 0); } catch (Exception e) { error = e; }
        C2Check(error == null,
            "portability repair never fabricates a target or angle-cache read for a noncandidate");
        game.Model.SourceAngles = new int[14, 14];
        C2Check(diagnostics.Length == 1 && game.C2Events.Count(C2IsPortabilityDiagnostic) == 2 &&
            diagnostics[0].ActorId == 4 && diagnostics[0].SourceTick == 2543 &&
            diagnostics[0].SourcePath == "player.c" && diagnostics[0].FunctionSymbol == "maybe_taunt" &&
            diagnostics[0].SourceLine == 5843 && diagnostics[0].Detail.Contains("original-bearing(4,-1)=SKIPPED") &&
            diagnostics[0].Detail.Contains("policy=Parent-autonomous-source-semantic") &&
            diagnostics[0].Detail.Contains("user-approval=NOT_CAPTURED"),
            "portability repair emits exactly one honest policy/source-site diagnostic per invocation");
        foreach (int invalid in new[] { -2, 14 })
        {
            game.Model.CarrierId = invalid;
            C2Throws<InvalidOperationException>(() => execute(actor, "maybe_taunt", 0, 0, 0),
                "other invalid carrier indices remain visible, never fabricated target0 or receiver");
        }
        game.Model.CarrierId = -1;
        foreach (int invalid in new[] { -2, 14 })
        {
            game.Model.TargetPlayerId = invalid;
            C2Throws<InvalidOperationException>(() => execute(actor, "maybe_taunt", 0, 0, 0),
                "invalid receiver indices remain visible outside the bounded portability repair");
        }
        game.Model.TargetPlayerId = 11;
        actor.SourceHitPartnerId = null;
        C2Throws<SourceC2MissingStateException>(() => execute(actor, "maybe_taunt", 0, 0, 0),
            "portability repair never guesses unknown hit state");
        actor.SourceHitPartnerId = -2;
        C2Throws<InvalidOperationException>(() => execute(actor, "maybe_taunt", 0, 0, 0),
            "portability repair never broadens invalid hit indices");
        actor.SourceHitPartnerId = 7;
        int oldDiagnostics = game.C2Events.Count(C2IsPortabilityDiagnostic);
        game.Model.SourceTaunted = true;
        execute(actor, "maybe_taunt", 0, 0, 0);
        game.Model.SourceTaunted = false;
        game.Model.SourceFlags = SourceGameFlags.PassThrown;
        execute(actor, "maybe_taunt", 0, 0, 0);
        C2Check(game.C2Events.Count(C2IsPortabilityDiagnostic) == oldDiagnostics,
            "all earlier original latch and ending guards precede portability repair");
        game.Model.SourceFlags = SourceGameFlags.PlayEnding | SourceGameFlags.PassThrown;
        var probe = new Dictionary<string, SourceAnimationProgram> {
            { "portability_void_boundary_probe", new SourceAnimationProgram("portability_void_boundary_probe",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 64),
                new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "BREATH"),
                new SourceAnimationInstruction(SourceAnimationOp.MovementCode, text: "maybe_taunt", sourceLine: 1897),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, a: 1)) } };
        var layer = new SourceAnimationLayerState(c2Library, actor.Id, false, probe);
        layer.C2VoidCode = execute;
        layer.Change("portability_void_boundary_probe", actor, game.SourceTick, n => 0);
        C2Check(layer.CodeReturnEvidence == SourceAnimationCodeReturnEvidence.VoidOpaque &&
            (layer.Mode & SourceAnimationModes.Zero) != 0,
            "portability repair stays VOID opaque, not manufactured int0 or certified MODE_ZERO");
        string trap = Path.Combine(c2NativeRoot, "negative-target", "trap-receipt.json");
        using (JsonDocument original = JsonDocument.Parse(File.ReadAllBytes(trap)))
        {
            JsonElement value = original.RootElement;
            C2Check(value.GetProperty("bearingObservedMe").GetInt32() == 4 &&
                value.GetProperty("bearingObservedHim").GetInt32() == -1 &&
                value.GetProperty("stoppedBeforeAnyBearingRead").GetBoolean() &&
                value.GetProperty("postBearingNoncandidateCases").GetInt32() > 0 &&
                value.GetProperty("postBearingChanceDraws").GetInt32() == 0 &&
                value.GetProperty("postBearingAnimationChanges").GetInt32() == 0,
                "original native bearing trap observes4,-1 without host UB or a synthetic return value");
        }
        c2Evidence["portability_repair_policy"] = new {
            policy = "Parent-autonomous no-eligible-candidate source-semantic portability repair",
            explicitUserApprovalCaptured = false, originalTargetReadOrABIQualified = false,
            originalBearingReadSkipped = true, noActorAngleRngLatchAnimationSoundSubstitution = true
        };
    }

    private static void C2ActualPlayerDownWake()
    {
        GameSimulation game = CreateC2(site => 99);
        C2Input(game, C2BaseInput(0)); game.Model.Phase = GamePhase.LiveBall;
        game.Model.CarrierId = 0; game.Model.SourcePlayEndX = -156; game.Model.SourceTaunted = true;
        SourcePlayerDriver.SetSourcePosition(game.Model.Players[0], new SourceVector3(-156, 0, 0));
        Bind<Action<int>>(game, "SourcePlayerDown")(0);
        C2Check(game.C2State.ResultPending && game.C2State.Taunted == true &&
            game.C2State.WhistleProcesses.Single().FirstWakePending && game.Model.Players[0].SourceCelebrateTime == 60,
            "actual player_down producer queues result and qcreate before next source dispatch");
        game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand));
        C2Check(!game.C2State.ResultPending && game.C2State.Taunted == false &&
            game.C2Events.Any(e => e.Kind == SourceC2EventKind.WhistleFirstWake && e.ProcessOrder == 0) &&
            game.C2Events.Any(e => e.Kind == SourceC2EventKind.ResultCommitted && e.Process == "game_proc" && e.ProcessOrder == 1),
            "real SourceStep dispatches head whistle BEFORE next game_proc result/latch commit");
        c2Evidence["actual_SourceStep_whistle_before_game_proc"] = true;
    }

    private static void C2Whistles(JsonElement rows)
    {
        int compared = 0;
        foreach (JsonElement item in rows.EnumerateArray())
        {
            JsonElement input = item.GetProperty("input"), expected = item.GetProperty("expected");
            GameSimulation game = CreateC2(site => site.Process != "whistle_proc" ? 99 :
                site.BoundOrModulus == 100 ? C2Int(input, "chance") : C2Int(input, "index"));
            Action spawn = Bind<Action>(game, "SpawnSourceC2Whistle"), dispatch = Bind<Action>(game, "DispatchSourceC2Whistles");
            for (int n = 0; n < C2Int(input, "count"); n++) spawn();
            C2Check(game.C2State.WhistleProcesses.First().Id == C2Int(expected, "firstId") &&
                game.C2State.WhistleProcesses.All(p => p.FirstWakePending && p.SleepRemaining == 1),
                "qcreate head/LIFO/default sleep1, no synchronous actor chance");
            var emissions = new List<Tuple<long, SourceSoundVmDispatch>>();
            for (int tick = 1; tick <= 32; tick++)
            {
                game.Model.SourceTick = tick; game.Model.SourceDispatchOrdinal = 0;
                dispatch();
                Bind<Action<string, int, int>>(game, "BeginSourceProcess")("game_proc", -1, 0);
                if (tick == 1) C2Check(game.Model.SourceRandomProcessOrder == C2Int(input, "count"),
                    "head-inserted whistle first wake precedes existing game_proc");
                foreach (SourceSoundVmDispatch d in game.TakeSourceSoundVmDispatches().Dispatches)
                    emissions.Add(Tuple.Create((long)tick, d));
            }
            C2Check(game.C2Events.Count(e => e.Kind == SourceC2EventKind.WhistleFirstWake) == C2Int(input, "count") &&
                game.C2Events.Where(e => e.Kind == SourceC2EventKind.WhistleFirstWake).All(e => e.SourceTick == 1),
                "native whistle chance executes once on first dispatch, not after fake delay");
            C2Check(emissions.Count == C2Int(expected, "calls") && game.C2State.DeferredWhistles == 0,
                "native chance25 branch removes/ends typed processes exactly once");
            foreach (Tuple<long, SourceSoundVmDispatch> emission in emissions)
            {
                SourceSoundVmDispatch d = emission.Item2; SourceAudioResolvedOriginalSoundCall call = d.Result.Calls.Single();
                C2Check(emission.Item1 == C2Int(expected, "emitFrame") && emission.Item1 == 31,
                    "native sleep30 resumes after30 subsequent dispatch decrements");
                C2Check(d.Status == SourceSoundVmStatus.Resolved && d.C2Context.ActorId == -1 &&
                    !d.HasOriginalAnimationCue && d.RandomContext.ActorId == -1 && d.RandomContext.Process == "whistle_proc" &&
                    call.CalleeSourceSite.SourcePath == "player.c" && call.CalleeSourceSite.SourceLine == 6018 &&
                    call.PlaylistId == C2Int(expected, "id") && call.Volume == C2Int(expected, "volume") &&
                    call.PriorityWord == C2Int(expected, "priority"), "native actorless whistle actual source/id/bank/volume/priority/context");
                C2Throws<InvalidOperationException>(() => GC.KeepAlive(d.OriginalCueContext), "native dispatch cannot expose fake actor0 context");
                C2Throws<InvalidOperationException>(() => GC.KeepAlive(d.StateAtCall), "native dispatch cannot expose default actor function state");
                C2Throws<InvalidOperationException>(() => GC.KeepAlive(call.OriginalCueContext), "native resolved call cannot expose fake actor0 context");
            }
            compared++;
        }
        c2Evidence["native_whistle_scheduler_cases"] = compared;
    }

    private static void C2PlayerDown(JsonElement rows)
    {
        foreach (JsonElement item in rows.EnumerateArray())
        {
            JsonElement input = item.GetProperty("input"), expected = item.GetProperty("expected");
            GameSimulation game = CreateC2(site => 99);
            game.Model.Phase = C2Int(input, "gm") == 2 ? GamePhase.LiveBall : GamePhase.DeadBall;
            game.Model.CarrierId = C2Int(input, "carrier");
            game.Model.Possession = TeamId.Home; game.Model.SourceLastPossession = 0;
            game.Model.SourceRules = new SourceRulesState(0, 0, 2, 30, 60);
            game.Model.SourceHasContactSpot = true;
            game.Model.SourcePlayEndX = input.GetProperty("spot").GetSingle();
            game.Model.SourceContactSourcePosition = new SourceVector3(game.Model.SourcePlayEndX, 0, 0);
            ActorState actor = game.Model.Players[0]; actor.SourceCelebrateTime = 0; actor.SourceRawFlags &= ~0x100;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(input.GetProperty("x").GetSingle(), 0, 0));
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 0, 0);
            Bind<Action<int>>(game, "SourcePlayerDown")(0);
            C2Check(game.C2State.DeferredWhistles == C2Int(expected, "created") &&
                actor.SourceCelebrateTime == C2Int(expected, "celebrate") &&
                (actor.SourceRawFlags & 0x100) == (C2Int(expected, "flags") & 0x100),
                "original player_down qcreate/celebrate60 actor consequences");
            if (C2Int(expected, "created") != 0)
                C2Check(Bits(game.Model.SourcePlayEndX) == Bits(expected.GetProperty("spot").GetSingle()) &&
                    (int)game.Model.SourceC2PendingResult.Input.Cause == C2Int(expected, "cause") &&
                    game.C2State.ResultPending, "original player_down exact spot/sentinel/cause before deferred result " +
                    input.GetRawText() + " native=" + expected.GetRawText() + " actualSpot=" + game.Model.SourcePlayEndX +
                    " actualCause=" + (int)game.Model.SourceC2PendingResult.Input.Cause);
        }
        c2Evidence["native_player_down_cases"] = rows.GetArrayLength();
    }

    private static void C2ImmediateWhistles(JsonElement rows)
    {
        foreach (JsonElement item in rows.EnumerateArray())
        {
            JsonElement input = item.GetProperty("input"), expected = item.GetProperty("expected");
            GameSimulation game = CreateC2(site => site.BoundOrModulus == 2 ? C2Int(input, "index") : 99);
            C2Input(game, C2BaseInput(0)); ActorState actor = game.Model.Players[0];
            actor.SourceMode = (SourceActorMode)C2Int(input, "mode");
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 0, 0);
            Bind<Action<ActorState>>(game, "SourceC2ImmediateWhistle")(actor);
            SourceSoundVmDispatch sound = game.TakeSourceSoundVmDispatches().Dispatches.Single();
            SourceAudioResolvedOriginalSoundCall call = sound.Result.Calls.Single();
            C2Check(call.PlaylistId == C2Int(expected, "id") && call.Volume == C2Int(expected, "volume") &&
                call.PriorityWord == C2Int(expected, "priority") && sound.Result.RandomCalls == C2Int(expected, "draws") &&
                call.Bank == expected.GetProperty("bank").GetString() && call.CalleeSourceSite.SourcePath == "player.c" &&
                call.CalleeSourceSite.SourceLine == (C2Int(input, "mode") == 5 ? 1839 : 2434),
                "native immediate QB/receiver whistle uses exact index/235/priority0 source call");
            C2Check(game.Model.Phase == GamePhase.DeadBall && game.C2State.DeferredWhistles == 0 &&
                !sound.HasOriginalAnimationCue && sound.C2Context.ActorId == 0,
                "immediate whistle preserves actual actor identity without fake ANI cue/deferred duplicate");
        }
        c2Evidence["native_immediate_whistle_cases"] = rows.GetArrayLength();
    }

    private static void C2MissingInputsAndFifo()
    {
        GameSimulation game = CreateC2(site => 0);
        C2Input(game, C2BaseInput(0));
        ActorState actor = game.Model.Players[7];
        game.Model.SourceTauntBank = null;
        var goodCue = new SourceCallbackCue(new SourcePresentationContext(game.SourceTick, 0, 7, "plyrseq.c",
            "t_dshoul_anim", 2448), SourcePresentationCueKind.SoundCallback, "get_hit_snd", 57, 0, 0);
        Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, goodCue);
        int prefix = game.Model.SourceRandomCallIndex;
        var missingCue = new SourceCallbackCue(new SourcePresentationContext(game.SourceTick, 1, 7, "plyrseq.c",
            "taunt_1_anim", 3144), SourcePresentationCueKind.SoundCallback, "taunt_speech", -2, 0, 0);
        C2Throws<SourceAudioMissingInputException>(() =>
            Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, missingCue),
            "unknown source taunt bank stays visible after actual partial helper draw");
        C2Check(game.Model.SourceRandomCallIndex == prefix + 1, "partial missing-bank RNG advances shared cursor exactly once");
        SourceSoundVmDispatch[] pending = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
        C2Check(pending.Length == 2 && pending[0].OriginalCue == goodCue && pending[0].Status == SourceSoundVmStatus.Resolved &&
            pending[1].OriginalCue == missingCue && pending[1].Status == SourceSoundVmStatus.MissingInput &&
            pending[1].Result.RandomCalls == 1 && pending[1].Result.Calls.Count == 0,
            "single ordered FIFO preserves old call then partial C2 failure without fake sound");
        C2Check(game.TakeSourceSoundVmDispatches().Dispatches.Count == 0, "single FIFO Take drains once");
        C2Throws<InvalidOperationException>(() =>
            Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, goodCue), "missing source bank requires restart, never retry/rewind");
        game.Restart();
        C2Check(game.TakeSourceSoundVmDispatches().Dispatches.Count == 0 && game.C2State.Taunted == false &&
            game.C2State.TauntBank != null, "explicit fresh original-program restart drains FIFO and recreates real boot state");
        actor = game.Model.Players[7];
        var afterRestart = new SourceCallbackCue(new SourcePresentationContext(game.SourceTick, 0, 7, "plyrseq.c",
            "t_dshoul_anim", 2448), SourcePresentationCueKind.SoundCallback, "get_hit_snd", 57, 0, 0);
        Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, afterRestart);
        game.Restart();
        C2Check(game.TakeSourceSoundVmDispatches().Dispatches.Count == 0,
            "restart drains actual undrained old/new shared FIFO rather than a previously empty batch");
        game.Model.SourceCurrentPlayerBank = null;
        game.Model.SourceTick = 1;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("whistle_proc", -1, 0);
        var native = new SourceC2SoundContext(SourceC2SoundFunctionKind.WhistleProc, 1, "whistle_proc",
            game.Model.SourceRandomProcessOrder, -1, "player.c", "whistle_proc", 6018,
            firstCallIndex: game.Model.SourceRandomCallIndex);
        C2Throws<SourceAudioMissingInputException>(() => Bind<Action<SourceC2SoundContext>>(game,
            "ExecuteSourceC2NativeSound")(native), "native unknown player bank reports partial index2 draw, never players default");
        SourceSoundVmDispatch partial = game.TakeSourceSoundVmDispatches().Dispatches.Single();
        C2Check(partial.Status == SourceSoundVmStatus.MissingInput && partial.Result.RandomCalls == 1 &&
            partial.Result.RandomDraws.Single().Site.BoundOrModulus == 2 && !partial.HasOriginalAnimationCue &&
            partial.C2Context.ActorId == -1 && game.Model.SourceRandomCallIndex == 1,
            "native partial FIFO retains actorless context and consumed index2 draw exactly once");
        game.Restart();
        game.Model.SourceTaunted = null; actor = game.Model.Players[7]; actor.SourceHitPartnerId = 0;
        C2Throws<SourceC2MissingStateException>(() =>
            Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(actor, "maybe_taunt", 0, 0, 0),
            "unknown global latch is never guessed false");
        actor.SourceHitPartnerId = -1;
        Bind<Action<ActorState, string, int, int, int>>(game, "ExecuteSourceC2Void")(actor, "maybe_taunt", 0, 0, 0);
        C2Check(!game.C2State.Taunted.HasValue, "original NULL hit returns BEFORE unknown latch read");
        c2Evidence["partial_fifo_restart_unknown_contracts"] = true;
    }

    private static void C2InputGuards(byte[] dataBytes, string dataHash, byte[] hitBytes)
    {
        C2Throws<Exception>(() => SourceC2CallbackData.Read(dataBytes, new string('0', 64), c2Data.HitExtensionData),
            "reader requires external trusted C2 payload pin");
        var changed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dataBytes);
        byte[] invalid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(dataBytes)
            .Replace("\"clip_count\": 281", "\"clip_count\": 280"));
        C2Throws<Exception>(() => SourceC2CallbackData.Read(invalid, Hash(invalid), c2Data.HitExtensionData),
            "reader strictly rejects semantic profile/count mutation even with matching supplied payload hash");
        C2Throws<Exception>(() => SourceC2CallbackData.Read(dataBytes, dataHash, real.SoundFunctions),
            "old6 SAFN cannot masquerade as one new7538 extension");
        C2Throws<ArgumentException>(() => new SourceAudioCalleeSite("../player.c", c2Data.PlayerGitBlob, "whistle_proc", 6018),
            "native callee DTO rejects noncanonical source path");
        C2Throws<ArgumentException>(() => new SourceC2SoundContext(SourceC2SoundFunctionKind.WhistleProc, 0,
            "whistle_proc", 0, 0, "player.c", "whistle_proc", 6018), "native actorless process cannot be fabricated actor0");
        C2Throws<ArgumentException>(() => new SourcePresentationContext(0, 0, -1, "player.c", "whistle_proc", 6018),
            "old presentation context actor guard remains unchanged");
        C2Throws<ArgumentException>(() => SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(c2Data).RequireLibrary(real.Animations),
            "new C2 bootstrap cannot silently alias legacy272 resources");
        GC.KeepAlive(changed); GC.KeepAlive(hitBytes);
        c2Evidence["strict_data_guards"] = true;
    }

    private static void C2NaturalTrace()
    {
        int[] rawVector = { 0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
            2147483645, 2147483646, 2147483647 };
        int rawCursor = 0;
        var cache = new C2CacheObservation(c2Geometry);
        GameSimulation game = CreateC2(site => rawVector[rawCursor++ % rawVector.Length], actualGeometry: cache);
        cache.Game = game;
        var shared = (SourceInjectedRandom)typeof(GameSimulation).GetField("sourceRandom",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
        C2Check(shared.Trace.Count >= 2 && shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.BoundOrModulus == 2 && shared.Trace[1].Site.BoundOrModulus == 2 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560") &&
            shared.Trace[0].Site.ActorId == -1 && shared.Trace[1].Site.ActorId == -1 &&
            game.C2State.AnnouncerBank == "tkbank3" && game.C2State.TauntBank == "taunts4",
            "natural new entry visibly consumes two real bank draws before AI, without old-profile vector surgery");
        var lifecycle = new List<object>(); var sounds = new List<object>(); var commands = new List<object>();
        object firstCallback = null, firstEnd = null, firstWhistle = null, failure = null;
        int deadWait = 0, completedHosts = 0;
        const int maximumHosts = 12000;
        for (int host = 0; host < maximumHosts; host++)
        {
            GamePhase beforePhase = game.State.Phase;
            long beforeTick = game.SourceTick;
            PlayerCommand command = default(PlayerCommand);
            if (game.State.Phase == GamePhase.PlaySelect)
                command = new PlayerCommand(action: InputAction.SelectPlay,
                    play: game.State.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Cover);
            else if (game.State.Phase == GamePhase.PreSnap)
                command = new PlayerCommand(action: InputAction.Snap);
            else if (game.State.Phase == GamePhase.DeadBall)
                command = ++deadWait > 30 ? new PlayerCommand(action: InputAction.Continue) : default(PlayerCommand);
            else if (game.State.Phase == GamePhase.LiveBall)
                command = new PlayerCommand(new FieldVector(0f, 1f), true,
                    game.State.Possession == TeamId.Home ? InputAction.None : InputAction.Tackle);
            if (game.State.Phase != GamePhase.DeadBall) deadWait = 0;
            commands.Add(new { host, sourceTick = beforeTick, beforePhase, command });
            bool failedThisHost = false;
            try
            {
                game.Step(command);
                if (game.SourceTick != beforeTick) cache.Complete(game.Model.Phase != GamePhase.PlaySelect);
                completedHosts++;
            }
            catch (Exception error)
            {
                failure = new {
                    host, maximumHosts, sourceTick = game.SourceTick, beforeTick, beforePhase,
                    phase = game.Model.Phase.ToString(), exception = error.ToString(), command, rawCursor,
                    game.Model.SourceRandomProcess, game.Model.SourceRandomActor,
                    game.Model.SourceRandomProcessOrder, game.Model.SourceRandomCallIndex,
                    c2State = game.C2State,
                    actualGuardInputs = new {
                        game.Model.SourceRules, game.Model.SourceFlags, game.Model.SourceLastResult, game.Model.SourceResultView,
                        game.Model.CarrierId, game.Model.TargetPlayerId, game.Model.SourceTeamFire,
                        executing = game.Model.SourceRandomActor < 0 ? null : (object)new {
                            actor = game.Model.SourceRandomActor,
                            game.Model.Players[game.Model.SourceRandomActor].Facing,
                            game.Model.Players[game.Model.SourceRandomActor].SourceHitPartnerId,
                            hitWorldPosition = game.Model.Players[game.Model.SourceRandomActor].SourceHitPartnerId.HasValue &&
                                game.Model.Players[game.Model.SourceRandomActor].SourceHitPartnerId.Value >= 0
                                ? (SourceVector3?)SourcePlayerDriver.WorldPosition(game.Model.Players[
                                    game.Model.Players[game.Model.SourceRandomActor].SourceHitPartnerId.Value]) : null
                        }
                    },
                    actors = game.Model.Players.Select(actor => new {
                        actor.Id, actor.Facing, actor.TargetFacing, actor.SourceMode, actor.SourceJob,
                        actor.SourceControllerStation, actor.SourceTarget, actor.SourceHitPartnerId, actor.SourcePuppetLinkId,
                        actor.AnimationState.Primary.ScriptSymbol, actor.AnimationState.Primary.SequenceName,
                        actor.AnimationState.Primary.FrameOrdinal, actor.AnimationState.Primary.InstanceId
                    }).ToArray()
                };
                failedThisHost = true;
            }
            foreach (SourceC2Event e in game.C2Events)
            {
                object observation = new { host, e.SourceTick, e.Process, e.ProcessOrder, e.ActorId, e.Kind, e.Detail,
                    e.Epoch, e.TauntedBefore, e.TauntedAfter, e.SourcePath, e.FunctionSymbol, e.SourceLine, e.SelectedOrdinal,
                    rawCursor, state = game.C2State };
                lifecycle.Add(observation);
                if (firstCallback == null && e.Kind == SourceC2EventKind.CallbackEntered) firstCallback = observation;
                if (firstEnd == null && e.Kind == SourceC2EventKind.PlayOverQueued) firstEnd = observation;
                if (firstWhistle == null && (e.Kind == SourceC2EventKind.WhistleSpawned ||
                    e.Kind == SourceC2EventKind.WhistleFirstWake || e.Kind == SourceC2EventKind.WhistleEmission))
                    firstWhistle = observation;
            }
            foreach (SourceSoundVmDispatch dispatch in game.TakeSourceSoundVmDispatches().Dispatches)
                sounds.Add(new {
                    tick = dispatch.RandomContext.SourceTick, dispatch.Status, dispatch.Detail,
                    dispatch.HasOriginalAnimationCue, c2Context = C2ContextRecord(dispatch.C2Context), dispatch.RandomContext,
                    animation = dispatch.HasOriginalAnimationCue ? dispatch.OriginalCue : null,
                    draws = dispatch.Result == null ? 0 : dispatch.Result.RandomCalls,
                    calls = dispatch.Result == null ? null : dispatch.Result.Calls.Select(call => new {
                        call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord,
                        call.CalleeSourceSite, call.HasOriginalAnimationCue, c2Context = C2ContextRecord(call.C2Context)
                    }).ToArray()
                });
            if (failedThisHost) break;
        }
        object result = new {
            schema = "source-contact-c2-natural-new-entry/v1",
            status = failure == null ? "bounded-input-window-completed-not-full-M1" : "stopped-first-software-or-source-domain-boundary",
            profile = SourceC2CallbackData.EntryProfile, library = c2Library.InventorySha256,
            data = c2Data.PayloadSha256, old272Fixture = real.ManifestSha256,
            sameFrozenVectorCommandsConfigurationAndCamera = true,
            onlyEntryDifferences = "Actual281 library, complete C2 source path and two real InitGameProc bank draws",
            portabilityDecision = new {
                policy = "Parent-autonomous no-eligible-candidate source-semantic portability repair",
                explicitUserApprovalCaptured = false, originalNegativeTargetMemoryOrABIQualified = false
            },
            noStateMutation = true, noSiteFiltering = true, noSkippedDraws = true,
            firstCallback, firstEnd, firstWhistle, failure, completedHosts, sourceTick = game.SourceTick,
            rawVector, rawCursor, allSharedRawDraws = shared.Trace, lifecycle, sounds, commands,
            cache = new { cache.CompletedFrames, cache.CurrentRows, cache.PreviousRows, cache.Samples, cache.FrameHashes },
            fullM1OrNativeOrBrowserOrDSPAcceptance = false
        };
        File.WriteAllText(Path.Combine(output, "natural-new-entry.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        C2VerifyPortabilityPrefix(Path.Combine(output, "natural-new-entry.json"));
        C2Check(shared.Trace.Count == rawCursor && cache.CompletedFrames > 0 && cache.CurrentRows == cache.CompletedFrames * 14 * 15,
            "natural unfiltered source keeps every shared raw draw and14x15 cache row for every completed wake");
        c2Evidence["natural_status"] = failure == null ? "bounded-window-only" : "blocked-first-next-software-or-source-domain-boundary";
        c2Evidence["natural_source_tick"] = game.SourceTick;
        c2Evidence["natural_raw_draws"] = rawCursor;
        c2Evidence["natural_first_callback_recorded"] = firstCallback != null;
        c2Evidence["natural_first_end_recorded"] = firstEnd != null;
        c2Evidence["natural_first_whistle_recorded"] = firstWhistle != null;
        c2Evidence["natural_completed_cache_frames"] = cache.CompletedFrames;
        c2Evidence["natural_current_cache_rows"] = cache.CurrentRows;
    }

    private static void C2VerifyPortabilityPrefix(string currentPath)
    {
        const string originalPath = ".squad/.scratch/c2-natural-final/natural-new-entry.json";
        byte[] originalBytes = File.ReadAllBytes(originalPath);
        C2Check(Hash(originalBytes) == "7d2c6229072172a40965c869e3e7884ab6a5e15ab29843402fc91b1f1800435b",
            "portability repair retains exact frozen7d2 pre-repair witness");
        using (JsonDocument oldDocument = JsonDocument.Parse(originalBytes))
        using (JsonDocument newDocument = JsonDocument.Parse(File.ReadAllBytes(currentPath)))
        {
            JsonElement old = oldDocument.RootElement, current = newDocument.RootElement;
            JsonElement[] oldDraws = old.GetProperty("allSharedRawDraws").EnumerateArray().ToArray();
            JsonElement[] draws = current.GetProperty("allSharedRawDraws").EnumerateArray().ToArray();
            C2Check(oldDraws.Length == 3829 && draws.Length >= oldDraws.Length,
                "portability repair compares ALL3829 raw prefix draws, not filtered sites");
            for (int n = 0; n < oldDraws.Length; n++)
                C2Check(oldDraws[n].GetRawText() == draws[n].GetRawText(),
                    "portability repair byte-identical raw draw/site/order/process/index prefix " + n);
            JsonElement[] oldFrames = old.GetProperty("cache").GetProperty("FrameHashes").EnumerateArray().ToArray();
            JsonElement[] frames = current.GetProperty("cache").GetProperty("FrameHashes").EnumerateArray().ToArray();
            C2Check(frames.Length >= oldFrames.Length, "portability repair retains every prior cache frame");
            for (int n = 0; n < oldFrames.Length; n++)
                C2Check(oldFrames[n].GetRawText() == frames[n].GetRawText(),
                    "portability repair byte-identical14x15 cache frame hash prefix " + n);
            JsonElement[] oldCommands = old.GetProperty("commands").EnumerateArray().ToArray();
            JsonElement[] commands = current.GetProperty("commands").EnumerateArray().ToArray();
            C2Check(commands.Length >= oldCommands.Length, "portability repair retains every pre-boundary host command");
            for (int n = 0; n < oldCommands.Length; n++)
                C2Check(oldCommands[n].GetRawText() == commands[n].GetRawText(),
                    "portability repair byte-identical command/config prefix " + n);
            JsonElement[] oldEvents = old.GetProperty("lifecycle").EnumerateArray().ToArray();
            JsonElement[] events = current.GetProperty("lifecycle").EnumerateArray().ToArray();
            JsonElement[] prior = oldEvents.Where(e => e.GetProperty("SourceTick").GetInt64() < 2543).ToArray();
            JsonElement[] actualPrior = events.Where(e => e.GetProperty("SourceTick").GetInt64() < 2543).ToArray();
            C2Check(prior.Length == actualPrior.Length, "portability repair no pre2543 lifecycle additions/removals");
            for (int n = 0; n < prior.Length; n++)
                C2Check(prior[n].GetRawText() == actualPrior[n].GetRawText(),
                    "portability repair byte-identical earlier end/latch/whistle epochs " + n);
            string[] immutableFields = { "SourceTick", "Process", "ProcessOrder", "ActorId", "Kind", "Detail", "Epoch",
                "TauntedBefore", "TauntedAfter", "SourcePath", "FunctionSymbol", "SourceLine", "SelectedOrdinal" };
            foreach (JsonElement item in oldEvents.Where(e => e.GetProperty("SourceTick").GetInt64() == 2543))
            {
                JsonElement[] found = events.Where(e => e.GetProperty("SourceTick").GetInt64() == 2543 &&
                    e.GetProperty("Kind").GetInt32() == item.GetProperty("Kind").GetInt32() &&
                    e.GetProperty("ActorId").GetInt32() == item.GetProperty("ActorId").GetInt32() &&
                    e.GetProperty("Detail").GetString() == item.GetProperty("Detail").GetString()).ToArray();
                C2Check(found.Length == 1 && immutableFields.All(key =>
                    found[0].GetProperty(key).GetRawText() == item.GetProperty(key).GetRawText()),
                    "portability repair preserves immutable epoch7/actor4 events BEFORE the repair boundary");
            }
            JsonElement[] repairs = events.Where(e => e.GetProperty("Detail").GetString()
                .StartsWith("PORTABILITY_REPAIR:NO_ELIGIBLE_CANDIDATE;", StringComparison.Ordinal)).ToArray();
            JsonElement first = repairs.FirstOrDefault();
            C2Check(repairs.Length > 0 && first.GetProperty("SourceTick").GetInt64() == 2543 &&
                first.GetProperty("ActorId").GetInt32() == 4 && first.GetProperty("ProcessOrder").GetInt32() == 6 &&
                first.GetProperty("Detail").GetString().Contains("draws=3829;call=0") &&
                first.GetProperty("Detail").GetString().Contains("original-bearing(4,-1)=SKIPPED"),
                "actual2543 repair diagnostic is emitted once at original actor4/order6/call0/raw3829 boundary");
            C2Check(current.GetProperty("sourceTick").GetInt64() > 2543,
                "same natural NEW281 stream progresses genuinely beyond2543 without input filtering");
            object proof = new {
                schema = "source-c2-portability-prefix-proof/v1", rawPrefix = oldDraws.Length,
                cacheFramePrefix = oldFrames.Length, hostCommandPrefix = oldCommands.Length,
                earlierCompleteLifecycleRows = prior.Length, epoch7ImmutableEvents = oldEvents.Count(e =>
                    e.GetProperty("SourceTick").GetInt64() == 2543),
                firstRepair = first.Clone(), repairInvocationsObserved = repairs.Length,
                nextSourceTick = current.GetProperty("sourceTick").GetInt64(),
                oldWitnessSha256 = Hash(originalBytes), newTraceSha256 = Hash(File.ReadAllBytes(currentPath)),
                allRawRowsCacheHashesCommandsAndEarlierEpochsByteIdentical = true,
                epoch7PostHostObservationNotMisrepresentedAsPreRepairState = true
            };
            File.WriteAllText(Path.Combine(output, "portability-prefix-proof.json"),
                JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
            c2Evidence["portability_repair_prefix"] = proof;
        }
    }

    private static object C2ContextRecord(SourceC2SoundContext context)
    {
        if (context == null) return null;
        return new { context.Kind, context.SourceTick, context.Process, context.ProcessOrder, context.FirstCallIndex, context.ActorId,
            context.SourcePath, context.FunctionSymbol, context.SourceLine, context.Argument0, context.Argument1,
            context.Argument2, context.InvocationId, context.HasOriginalAnimationCue };
    }

    private sealed class C2CacheObservation : ISourceActorGeometry, ISourceActorCameraGeometry
    {
        private readonly SourceCatchGeometry inner;
        private SourceVector3[][] current = new SourceVector3[14][];
        private SourceVector3[][] previous = new SourceVector3[14][];
        private long collectingTick = -1;
        private int nextActor;
        internal GameSimulation Game;
        internal int CompletedFrames, CurrentRows, PreviousRows, Samples;
        internal readonly List<object> FrameHashes = new List<object>();
        internal C2CacheObservation(SourceCatchGeometry actual) { inner = actual; }
        public SourceVector3 WorldJoint(PlayerView actor, int joint) { return inner.WorldJoint(actor, joint); }
        private static bool Same(SourceVector3 a, SourceVector3 b)
        { return Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z); }
        public IReadOnlyList<SourceVector3> CameraJoints(PlayerView actor, SourceVector3 root, SourceMovementCameraFrame camera)
        {
            C2Check(Game != null && Game.Model.SourceRandomProcess == "player_proc" &&
                Game.Model.SourceCameraJointTick < Game.SourceTick && ReferenceEquals(camera, Game.Model.SourceMovementCamera) &&
                Game.Model.SourceMovementCameraTick == Game.SourceTick,
                "strict source-frame cache sampled post-player/pre-contact with current executing camera");
            if (collectingTick != Game.SourceTick)
            {
                collectingTick = Game.SourceTick; nextActor = 0;
                previous = current; current = new SourceVector3[14][];
            }
            C2Check(actor.Id == nextActor++, "strict source-frame cache includes all14 actors in source order");
            C2Check(Same(root, SourcePlayerDriver.WorldPosition(Game.Model.Players[actor.Id])),
                "strict source-frame sample captures actual root BEFORE contact displacement");
            if (previous[actor.Id] != null)
                for (int joint = 0; joint < 15; joint++)
                {
                    C2Check(Same(Game.Model.SourceCameraJoints[actor.Id, joint], previous[actor.Id][joint]),
                        "strict previous source-frame cache survives until this actor is sampled");
                    PreviousRows++;
                }
            SourceVector3[] actual = inner.CameraJoints(actor, root, camera).ToArray();
            C2Check(actual.Length == 15, "strict actual original fifteen-bone geometry");
            current[actor.Id] = actual; Samples++;
            return actual;
        }
        internal void Complete(bool expectedCache)
        {
            if (!expectedCache)
            {
                C2Check(collectingTick != Game.SourceTick, "play-selection suspension does not fabricate a cache wake");
                return;
            }
            C2Check(collectingTick == Game.SourceTick && nextActor == 14 &&
                Game.Model.SourceCameraJointTick == Game.SourceTick, "every completed source wake commits all14x15 cache joints");
            var bits = new List<byte>();
            for (int actor = 0; actor < 14; actor++)
            for (int joint = 0; joint < 15; joint++)
            {
                SourceVector3 actual = Game.Model.SourceCameraJoints[actor, joint];
                C2Check(Same(actual, current[actor][joint]), "current source-frame cache retains captured PRE-contact geometry bits");
                bits.AddRange(BitConverter.GetBytes(actual.X)); bits.AddRange(BitConverter.GetBytes(actual.Y)); bits.AddRange(BitConverter.GetBytes(actual.Z));
                CurrentRows++;
            }
            FrameHashes.Add(new { tick = Game.SourceTick, sha256 = Hash(bits.ToArray()) }); CompletedFrames++;
        }
    }

    private static void AddC2Result(Dictionary<string, object> result)
    {
        result["production_profile"] = "original-teams-contact281-c2";
        result["entry_profile"] = SourceC2CallbackData.EntryProfile;
        result["actual_animation_clips"] = c2Library.SequenceNames.Count;
        result["actual_animation_frames"] = c2Library.FrameCount;
        result["c2_animation_sha256"] = c2Library.InventorySha256;
        result["c2_callback_data_sha256"] = c2Data.PayloadSha256;
        result["c2_hit_extension_sha256"] = c2Data.HitExtensionData.PayloadSha256;
        result["c2_bounded_evidence"] = c2Evidence;
    }
}
