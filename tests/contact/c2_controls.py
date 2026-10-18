"""The frozen25 C2 causal controls and nine explicitly disclosed portability controls."""
G = "Assets/Scripts/Gameplay/"
P = "Assets/Scripts/Presentation/"
RNG = "complete native callback ordered guards and exact RNG count"
LATCH = "native latch AFTER taunt and BEFORE mad synchronous change_anim"


def m1_specifications():
    data = "Assets/Scripts/AssetImport/SourceC2CallbackData.cs"
    sound = P + "SourceC2SoundFunctions.cs"
    vm = G + "SourceSoundVm.cs"
    real = "M1 running-taunt real ANI_CODE resolves exact registered tuple"
    chance = "M1 running-taunt native chance9vs10 zero-output no-eager-index contract"
    bounds = "M1 running-taunt native ordered100 then conditional3 bounds"
    output = "M1 running-taunt original bank table priority and callee519or524 attribution"
    return [
        ("m1-drop-binding", data,
         "bindings.Add(new SourceC2VmBinding(bindingScripts[n], function, kind, lines[n], arguments[n]));",
         "if (lines[n] != 6401) bindings.Add(new SourceC2VmBinding(bindingScripts[n], function, kind, lines[n], arguments[n]));",
         "M1 running-taunt exact14 bindings and unchanged9 extension programs"),
        ("m1-miskey-script", data, "ScriptSymbol = script; FunctionSymbol = function;",
         'ScriptSymbol = script + "_wrong"; FunctionSymbol = function;', real),
        ("m1-miskey-line", data, "FunctionKind = kind; SourceLine = line; Argument0 = argument;",
         "FunctionKind = kind; SourceLine = line + 1; Argument0 = argument;", real),
        ("m1-miskey-args", data, "FunctionKind = kind; SourceLine = line; Argument0 = argument;",
         "FunctionKind = kind; SourceLine = line; Argument0 = argument + 1;", real),
        ("m1-old-profile-promotion", "Assets/Scripts/Runtime/SourceC2Composition.cs",
         "return CreateChecked(SourceTeamResourceProfile.Contact281C2M1, animations",
         "return CreateChecked(SourceTeamResourceProfile.Contact281C2, animations",
         "M1 running-taunt successor factory rejects old-nine bootstrap"),
        ("m1-chance10-inclusive", sound, 'sp == -4 && Draw(100, "taunt_speech", 518) >= 10',
         'sp == -4 && Draw(100, "taunt_speech", 518) > 10', chance),
        ("m1-ez-chance10-inclusive", sound, 'sp == -5 && Draw(100, "taunt_speech", 523) >= 10',
         'sp == -5 && Draw(100, "taunt_speech", 523) > 10', chance),
        ("m1-eager-index", sound, "int sp = context.Argument0;",
         'int sp = context.Argument0; if (sp == -4) Draw(3, "taunt_speech", 519);', chance),
        ("m1-index-before-chance", sound, 'if (sp == -4 && Draw(100, "taunt_speech", 518) >= 10 ||',
         'if (sp == -4 && (Draw(3, "taunt_speech", 519) + Draw(100, "taunt_speech", 518)) >= 10 ||', chance),
        ("m1-wrong-bound", sound, 'Draw(data.SpeechTable(table).Count,\n                    "taunt_speech", line)',
         'Draw(data.SpeechTable(table).Count + 1,\n                    "taunt_speech", line)', bounds),
        ("m1-wrong-table", sound, 'sp == -4 ? "histep_taunt_sp"', 'sp == -4 ? "short_taunt_sp"', bounds),
        ("m1-private-RNG", sound, "recorder = new Recorder(random);",
         "recorder = new Recorder(new SourceInjectedRandom(site => 0, random.Provenance));",
         "M1 running-taunt actual shared RNG process actor cursor no private/reordered stream"),
        ("m1-wrong-bank", sound, 'Bank(state.CurrentTauntBank, "taunt_bnk_str")',
         'Bank(state.CurrentPlayerBank, "taunt_bnk_str")', output),
        ("m1-wrong-priority", data, "Priority = (uint)priority;", "Priority = (uint)(priority + 1);", output),
        ("m1-wrong-callee", sound,
         "new SourceAudioResolvedOriginalSoundCall(context, call.Site, calls.Count, bank, id,",
         "new SourceAudioResolvedOriginalSoundCall(context, new SourceAudioCalleeSite(call.Site.SourcePath, call.Site.SourceGitBlob, call.Site.FunctionSymbol, call.Site.SourceLine + 1), calls.Count, bank, id,",
         output),
        ("m1-reversed-FIFO", vm,
         "sourceSoundVmPending.Add(new SourceSoundVmDispatch(context, execution, state, actorState,\n                    SourceSoundVmStatus.Resolved, result, null));",
         "sourceSoundVmPending.Insert(0, new SourceSoundVmDispatch(context, execution, state, actorState,\n                    SourceSoundVmStatus.Resolved, result, null));",
         "M1 running-taunt actual6401then6508 FIFO and synchronous shared call cursor"),
        ("m1-zero-percent-muted", sound, "int sp = context.Argument0;",
         "int sp = context.Argument0; if (context.Argument1 == 0) return;", chance),
        ("m1-unknown-bank-defaulted", sound, 'Bank(state.CurrentTauntBank, "taunt_bnk_str")',
         'Bank(state.CurrentTauntBank ?? "taunts3", "taunt_bnk_str")',
         "M1 running-taunt unknown bank stays typed failure not invented default"),
        ("m1-partial-cursor-dropped", vm, "AdvanceSourceSoundRandom(execution, error.PartialResult);",
         "/* compiled causal control: omit partial source cursor */",
         "M1 running-taunt partial missing-bank chance+index cursor retained"),
    ]


def specifications(root):
    specs = [
        ("carrier-probability45", G + "SourceC2Callbacks.cs", "? 70 : 45", "? 70 : 44", RNG),
        ("fire-probability70", G + "SourceC2Callbacks.cs", "? 70 : 45", "? 69 : 45", RNG),
        ("receiver-probability60", G + "SourceC2Callbacks.cs",
         'SourceDraw(100, "player.c:maybe_taunt:5864") < 60',
         'SourceDraw(100, "player.c:maybe_taunt:5864") < 59', RNG),
        ("qb-probability40", G + "SourceC2Callbacks.cs",
         'SourceDraw(100, "player.c:maybe_qb_mad:10689") >= 40',
         'SourceDraw(100, "player.c:maybe_qb_mad:10689") >= 39', RNG),
        ("receiver-mad-probability50", G + "SourceC2Callbacks.cs",
         'SourceDraw(100, "player.c:maybe_receiver_mad:10707") >= 50',
         'SourceDraw(100, "player.c:maybe_receiver_mad:10707") >= 49', RNG),
        ("qb-weighted-duplicate", G + "SourceC2Callbacks.cs",
         '"m_qbwhat_anim", "m_qbwhat_anim", "m_qbfite_anim"',
         '"m_qbwhat_anim", "m_qbfite_anim", "m_qbfite_anim"',
         "native selected exact script executes synchronously to first frame break"),
        ("taunt-latch-too-early", G + "SourceC2Callbacks.cs",
         "ChangeContactAnimation(actor, script);\n            Model.SourceTaunted = true;",
         "Model.SourceTaunted = true;\n            ChangeContactAnimation(actor, script);", LATCH),
        ("first-down-suppression", G + "SourceC2Callbacks.cs", "if (C2OverFirstDown(hit)) return;",
         "if (C2OverFirstDown(hit) && hit.Id == -1) return;", RNG),
        ("same-wake-result-commit", G + "SourceC2Callbacks.cs",
         "pending.QueuedTick >= Model.SourceTick", "pending.QueuedTick > Model.SourceTick",
         "result is not committed in same actor wake"),
        ("quarter-bank-event-disconnected", G + "GameSimulation.Source.cs", "                ReloadSourceC2Banks(false);",
         "                /* compiled control: omit real quarter producer */",
         "actual quarter event reloads both banks before clock restart without extra latch reset"),
        ("whistle-qcreate-not-front", G + "SourceC2Callbacks.cs",
         "Model.SourceC2Whistles.Insert(0, process);", "Model.SourceC2Whistles.Add(process);",
         "qcreate head/LIFO/default sleep1, no synchronous actor chance"),
        ("whistle-chance-inclusive25", G + "SourceC2Callbacks.cs",
         "if (chance >= 25) { Model.SourceC2Whistles.RemoveAt(index); continue; }",
         "if (chance > 25) { Model.SourceC2Whistles.RemoveAt(index); continue; }",
         "native chance25 branch removes/ends typed processes exactly once"),
        ("whistle-sleep29", G + "SourceC2Callbacks.cs",
         "process.Started = true;\n                    process.SleepRemaining = 30;",
         "process.Started = true;\n                    process.SleepRemaining = 29;",
         "native sleep30 resumes after30 subsequent dispatch decrements"),
        ("opaque-void-fake-int", G + "SourceAnimationState.cs",
         "C2VoidCode(actor, i.Text, i.A, i.B, i.C);\n                        CodeReturnEvidence = SourceAnimationCodeReturnEvidence.VoidOpaque;",
         "C2VoidCode(actor, i.Text, i.A, i.B, i.C);\n                        CodeReturnEvidence = SourceAnimationCodeReturnEvidence.KnownInt;",
         "actual void callback retains opaque MODE_ZERO evidence, never manufactured scalar zero"),
        ("old-script-continuation", G + "SourceAnimationState.cs",
         "C2VoidCode(actor, i.Text, i.A, i.B, i.C);\n                        CodeReturnEvidence",
         "SourceAnimationProgram oldC2Program = program; int oldC2Pc = pc;\n                        C2VoidCode(actor, i.Text, i.A, i.B, i.C);\n                        program = oldC2Program; pc = oldC2Pc;\n                        CodeReturnEvidence",
         "actual get-up caller rebinds to selected script and stops at its first frame break"),
        ("unknown-bank-default", P + "SourceC2SoundFunctions.cs",
         'Bank(state.CurrentTauntBank, "taunt_bnk_str")', 'Bank(state.CurrentTauntBank ?? "taunts3", "taunt_bnk_str")',
         "unknown source taunt bank stays visible after actual partial helper draw"),
        ("partial-draw-cursor-dropped", G + "SourceSoundVm.cs", "AdvanceSourceSoundRandom(execution, error.PartialResult);",
         "/* compiled control: omit actual partial cursor advancement */", "partial missing-bank RNG advances shared cursor exactly once"),
        ("native-context-fake-actor0", G + "SourceSoundVm.cs",
         "public SourcePresentationContext OriginalCueContext { get { return OriginalCue.Context; } }",
         "public SourcePresentationContext OriginalCueContext { get { return HasOriginalAnimationCue ? OriginalCue.Context : default(SourcePresentationContext); } }",
         "native dispatch cannot expose fake actor0 context"),
        ("restart-leaves-undrained-fifo", G + "GameSimulation.cs", "            sourceSoundVmPending.Clear();",
         "            /* compiled control: retain undrained shared FIFO */",
         "restart drains actual undrained old/new shared FIFO rather than a previously empty batch"),
        ("player-down-sentinel-omitted", G + "GameSimulation.Source.cs", "Math.Abs(Model.SourcePlayEndX) > 600f",
         "Math.Abs(Model.SourcePlayEndX) > 600f && !SourceC2Enabled",
         "original player_down exact spot/sentinel/cause before deferred result"),
        ("signed-bearing-absolute-fold", G + "SourceC2Callbacks.cs", "if (bearing > 256) return;",
         "if (Math.Abs(bearing) > 256) return;", RNG),
        ("literal-speech-invented-rng", P + "SourceC2SoundFunctions.cs", "int sp = context.Argument0;",
         'int sp = context.Argument0; if (sp >= 0) Draw(100, "taunt_speech", 527);', RNG),
        ("unknown-latch-guessed-false", G + "SourceC2Callbacks.cs",
         'if (!Model.SourceTaunted.HasValue) throw new SourceC2MissingStateException("player.c:293 global taunted");',
         "if (!Model.SourceTaunted.HasValue) return false;", "unknown global latch is never guessed false"),
    ]
    for symbol, array, bound, line in (
        ("qb", '"m_qbwhat_anim", "m_qbwhat_anim", "m_qbfite_anim"', 3, 10692),
        ("receiver", '"m_mscat1_anim", "m_mscat2_anim"', 2, 10710),
    ):
        before = """            Model.SourceTaunted = true;
            string[] mad = { %s };
            int ordinal = SourceDraw(%d, "player.c:maybe_%s_mad:%d");
            string script = mad[ordinal];
            ChangeContactAnimation(actor, script);""" % (array, bound, symbol, line)
        after = before.replace("            Model.SourceTaunted = true;\n", "") + "\n            Model.SourceTaunted = true;"
        specs.append((symbol + "-latch-too-late", G + "SourceC2Callbacks.cs", before, after, LATCH))
    relative = G + "SourceC2Callbacks.cs"
    current = (root / relative).read_text()
    start = current.index("            if (him == -1 && hit.Id != Model.CarrierId")
    end = current.index("            if (him < 0", start)
    repair = current[start:end]
    diagnostic = repair[repair.index("                C2Record("):repair.index("                return;")]
    specs += [
        ("repair-restores-throw", relative, repair, "", "portability repair returns VOID without unsafe original bearing load"),
        ("repair-universal-noop", relative, "private void SourceMaybeTaunt(ActorState actor)\n        {",
         "private void SourceMaybeTaunt(ActorState actor)\n        {\n            if (Model.Players.Length > 0) return;", RNG),
        ("repair-wrong-eligible-guard", relative,
         "if (him == -1 && hit.Id != Model.CarrierId && hit.Id != Model.TargetPlayerId\n                && Model.TargetPlayerId >= -1 && Model.TargetPlayerId < Model.Players.Length)",
         "if (Model.CarrierId == -1 && hit.Id != Model.CarrierId)", RNG),
        ("repair-fabricates-receiver-angle", relative, repair,
         "            if (him == -1 && hit.Id != Model.CarrierId && hit.Id != Model.TargetPlayerId) him = Model.TargetPlayerId;\n",
         "portability repair never fabricates a target or angle-cache read for a noncandidate"),
        ("repair-missing-diagnostic", relative, diagnostic, "                /* compiled control: missing portability disclosure */\n",
         "portability repair emits exactly one honest policy/source-site diagnostic per invocation"),
        ("repair-invented-rng", relative, diagnostic,
         '                SourceDraw(100, "portability-control-invented-rng");\n' + diagnostic,
         "portability repair consumes ZERO callback RNG or index/table draws"),
        ("repair-clears-source-flags", relative, diagnostic,
         "                Model.SourceFlags &= ~SourceGameFlags.PlayEnding;\n" + diagnostic,
         "portability repair changes no flags hit carrier receiver animation latch or sound"),
        ("repair-hides-unknown-hit", relative,
         'if (!actor.SourceHitPartnerId.HasValue) throw new SourceC2MissingStateException("pobj_hit");',
         "if (!actor.SourceHitPartnerId.HasValue) return;", "portability repair never guesses unknown hit state"),
        ("repair-hides-invalid-receiver", relative,
         "&& Model.TargetPlayerId >= -1 && Model.TargetPlayerId < Model.Players.Length", "&& Model.Players.Length > 0",
         "invalid receiver indices remain visible outside the bounded portability repair"),
    ]
    if len(specs) != 34 or len({row[0] for row in specs}) != 34:
        raise ValueError("C2 requires all original25 and nine repair controls.")
    for identity, path, old, new, expected in specs:
        if not old or (root / path).read_bytes().count(old.encode()) != 1 or not expected or old == new:
            raise ValueError("C2 control must change one actual unique production anchor: " + identity)
    return tuple(specs)
