using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.SourcePlayer;

internal static class AdmittedActionTailChecks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private sealed class ActionFixture
    {
        internal readonly GameSimulation Game;
        internal readonly PlayerData Data;
        internal ActionFixture(PlayerHost actual) { Game = actual.Game; Data = actual.Data; }
        internal ActionFixture(PlayerData data, GameSimulation game)
        {
            Data = data; Game = game;
            var camera = new PreviewCamera(data, null, game);
            camera.Playback.OriginalNormal.StartedOpeningKickoff();
        }
    }
    private static ActionFixture host;
    private static SourceInjectedRandom random;
    private static int raw = 99;
    private static int assertions;
    private static Func<SourceRandomSite, int> originalInputs;
    private static object Model { get { return Get(host.Game, "Model"); } }
    private static object Actor { get { return ((Array)Get(Model, "Players")).GetValue(4); } }
    private static object Animation { get { return Get(Actor, "AnimationState"); } }
    private static object Lower { get { return Get(Animation, "Primary"); } }
    private static long Tick { get { return (long)Get(Model, "SourceTick"); } }
    private static string Script { get { return (string)Get(Lower, "ScriptSymbol"); } }
    private static Func<int, int> Draw { get { return bound => (int)Call(host.Game, "SourceDraw", bound); } }

    private static object Get(object value, string name)
    {
        FieldInfo field = value.GetType().GetField(name, Instance);
        return field != null ? field.GetValue(value) : value.GetType().GetProperty(name, Instance).GetValue(value);
    }

    private static void Set(object value, string name, object field)
    {
        FieldInfo target = value.GetType().GetField(name, Instance);
        if (target == null) throw new MissingFieldException(value.GetType().FullName, name);
        target.SetValue(value, field);
    }

    private static object Invoke(MethodInfo method, object target, object[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static object Call(object target, string method, params object[] arguments)
    {
        return Invoke(target.GetType().GetMethods(Instance)
            .Single(m => m.Name == method && m.GetParameters().Length == arguments.Length), target, arguments);
    }

    private static object Static(string type, string method, params object[] arguments)
    {
        return Invoke(typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay." + type)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == method && m.GetParameters().Length == arguments.Length), null, arguments);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Position(object actor, float x, float z)
    {
        Static("SourcePlayerDriver", "SetSourcePosition", actor, new SourceVector3(x, 0f, z));
    }

    private static void Begin(long tick)
    {
        Set(Model, "SourceTick", tick);
        Call(host.Game, "BeginSourceProcess", "player_proc", 4, 5);
    }

    private static void FullBody(string script)
    {
        Call(Animation, "FullBody", script, Actor, Tick, Draw, AnimationSelectionEvidence.SourceVerified);
    }

    // Explicit component fixtures on the actual admitted PlayerHost Game/VM, not natural-match inputs.
    private static void Prepare(GamePhase phase, bool carrier, SourceActorMode mode = SourceActorMode.Receiver)
    {
        raw = 99;
        host.Game.Restart();
        Set(Model, "Phase", phase);
        Set(Model, "SourceMatchPhase", phase == GamePhase.LiveBall
            ? SourceMatchPhase.LiveBall : SourceMatchPhase.PlayResult);
        Set(Model, "SourceRules", new SourceRulesState(0, 0, 1, 50, 90, SourcePlayType.Normal));
        Set(Model, "Possession", TeamId.Home);
        Set(Model, "SourceLastPossession", 0);
        Set(Model, "SourceOffensePlay", host.Data.Playbook.GetPlay("play_zigzag"));
        Set(Model, "SourceFlags", SourceGameFlags.BallCrossedLine | SourceGameFlags.OverLineSpeedsSet);
        Set(Model, "CarrierId", carrier ? 4 : 3);
        Set(Model, "TargetPlayerId", -1);
        Set(Model, "BallKind", SourceBallKind.Pass);
        Set(Model, "BallMode", BallMode.Held);
        Set(Model, "CatchSourceTick", 70L);
        Set(Model, "SourceNoFumbles", true);
        Set(Model, "SourceBreathControl", 0u);
        Set(Model, "ControlledPlayerId", 4);
        ((int[])Get(Model, "SourceStationPlayers"))[0] = 4;
        foreach (object actor in (Array)Get(Model, "Players"))
        {
            Set(actor, "SourceMode", SourceActorMode.Dead);
            Set(actor, "SourceControllerStation", -1);
            Set(actor, "SourceVelocity", default(FieldVector));
            Set(actor, "SourceVerticalVelocity", 0f);
            Set(actor, "SourceRawFlags", 0);
            Set(actor, "SourceLateHittable", false);
            Set(actor, "SourceRequestedScript", null);
            Set(actor, "SourceButtons", SourceDroneButtons.None);
            Set(actor, "SourceNewButtons", SourceDroneButtons.None);
            Set(actor, "SourceImmediateGeneralRuns", 0);
            Position(actor, 0f, 100f);
        }
        Set(Actor, "SourceMode", mode);
        Set(Actor, "SourceControllerStation", 0);
        Set(Actor, "SourceStick", 24);
        Set(Actor, "SourcePreviousStick", 21);
        Set(Actor, "Turbo", false);
        Position(Actor, SourceMatchRules.WorldPosition(81, 0), 0f);
        Begin(100);
        FullBody(carrier ? "run_wb_anim" : "run_anim");
        Call(Get(Animation, "Secondary"), "SetMovementMode", 0u);
        Call(host.Game, "CacheSourceGeometry");
    }

    private static void Execute()
    {
        Call(host.Game, "ExecuteSourceMovementMode", Actor, default(PlayerCommand));
    }

    private static void Advance(long tick)
    {
        Begin(tick);
        Call(Animation, "Advance", Actor, tick, Draw);
    }

    private static int Draws(string identity, int after)
    {
        return random.Trace.Skip(after).Count(d => d.Site.SourceIdentity == identity);
    }

    private static void SameTickCannotRepeat()
    {
        long serial = (long)Get(Lower, "InstanceId");
        int ordinal = (int)Get(Lower, "FrameOrdinal");
        int count = random.Trace.Count;
        Call(Animation, "Advance", Actor, Tick, Draw);
        Check((long)Get(Lower, "InstanceId") == serial && (int)Get(Lower, "FrameOrdinal") == ordinal
            && random.Trace.Count == count, "A repeated render/sample cannot advance a source frame or callback/RNG.");
    }

    private static void DispatchUsesM1()
    {
        Module module = typeof(GameSimulation).Module;
        int legacy = module.Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")
            .GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic).MetadataToken;
        int current = typeof(GameSimulation).GetMethod("UpdateSourceMovementPlayers", Instance).MetadataToken;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(op => op.Value);
        var calls = new Dictionary<int, int> { { legacy, 0 }, { current, 0 } };
        foreach (Type type in module.Assembly.GetTypes())
        foreach (MethodBase method in type.GetMethods(Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>().Concat(type.GetConstructors(Instance | BindingFlags.Static)))
        {
            MethodBody body = method.GetMethodBody();
            if (body == null) continue;
            byte[] il = body.GetILAsByteArray();
            for (int at = 0; at < il.Length;)
            {
                short value = il[at++];
                if (value == 0xfe) value = unchecked((short)(0xfe00 | il[at++]));
                OpCode op = opcodes[value];
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, at); break;
                    default: size = 4; break;
                }
                if (op.OperandType == OperandType.InlineMethod)
                {
                    int token = BitConverter.ToInt32(il, at);
                    if (calls.ContainsKey(token)) calls[token]++;
                }
                at += size;
            }
        }
        Check(calls[legacy] == 0, "The compiled production Game has no caller/delegate to the approximate legacy Update driver.");
        Check(calls[current] > 0, "The compiled production source scheduler actually references the M1 player dispatcher.");
    }

    private static void DeadBallMilling()
    {
        // Original plyrseq.c INTTOSEQ + INTSTREAM tick operands, in player.c breath_seq_tbl order.
        int[] delays = { 239, 490, 154, 192, 141, 290, 298, 226, 264, 310, 294 };
        for (int selected = 0; selected < 14; selected++)
        {
            Prepare(GamePhase.DeadBall, false);
            raw = selected;
            Set(Actor, "SourceVelocity", new FieldVector(1f, 2f));
            Set(Actor, "SourceRawFlags", 0x40000);
            Set(Actor, "SourceDroneFace", 321);
            int before = random.Trace.Count;
            Execute();
            string expected = host.Data.Movement.Movement.Script("breath_seq_tbl", selected);
            Check(Script == expected && (uint)Get(Model, "SourceBreathControl") == 1u << selected,
                "The actual dead-ball mode uses unique_random/breath_seq_tbl, not the legacy generic breath fallback: " + expected);
            FieldVector velocity = (FieldVector)Get(Actor, "SourceVelocity");
            Check(velocity.X == 0f && velocity.Z == 0f && (int)Get(Actor, "TargetFacing") == 321
                && ((int)Get(Actor, "SourceRawFlags") & 0x40000) == 0,
                "Original neutral movement stops XZ and consumes PF_DRONEFACE once.");
            Check((AnimationSelectionEvidence)Get(Animation, "Evidence") == AnimationSelectionEvidence.SourceVerified
                && Draws("player.c:unique_random", before) == 1,
                "M1 selection has its actual source evidence and one ordered fourteen-entry draw.");
            SameTickCannotRepeat();
            long initial = Tick;
            int delay = selected < delays.Length ? delays[selected] : 80;
            for (int elapsed = 1; elapsed < delay; elapsed++)
            {
                Advance(initial + elapsed);
                Check(Script == expected && Draws("player.c:unique_random", before) == 1,
                    "Milling/breathing cannot reselect before its original source-tick delay: " + expected);
            }
            Advance(initial + delay);
            if (selected < delays.Length)
            {
                int next = selected == 0 ? 1 : 0;
                Check(Script == host.Data.Movement.Movement.Script("breath_seq_tbl", next)
                    && (uint)Get(Model, "SourceBreathControl") == ((1u << selected) | (1u << next)),
                    "The VM tail immediately executes pick_random_mill and retains the shared uniqueness mask: " + expected);
                Check(Draws("player.c:unique_random", before) == 30
                    && Draws("player.c:unique_random:fallback", before) == 0,
                    "Repeated indices retry exactly 2*14+1 times, then select the first unused entry without an extra draw.");
            }
            else
                Check(Script == expected && Draws("player.c:unique_random", before) == 1,
                    "The three original breathing programs loop rather than inventing a milling callback.");
            SameTickCannotRepeat();
        }
    }

    private static void DeadBallGetUpCallback()
    {
        Prepare(GamePhase.DeadBall, false, SourceActorMode.Quarterback);
        raw = 0;
        Set(Actor, "SourceLateHittable", true);
        Set(Actor, "SourceRawFlags", 0x1000);
        FullBody("get_up_b_anim");
        Check(!(bool)Get(Actor, "SourceLateHittable") && ((int)Get(Actor, "SourceRawFlags") & 0x1000) == 0,
            "The real get-up VM clears PF_LATEHITABLE before its display/stream waits.");
        long initial = Tick;
        for (int elapsed = 1; elapsed < 35; elapsed++)
        {
            Advance(initial + elapsed);
            Check((int)Get(Actor, "SourceImmediateGeneralRuns") == 0,
                "get_up_b cannot call general_run before INTTOSEQ5 + INTSTREAM56/30 completes.");
        }
        Advance(initial + 35);
        Check((int)Get(Actor, "SourceImmediateGeneralRuns") == 1 && Script == "fb_mill1",
            "Dead-ball QB general_run dispatches standard_run immediately at the VM callback, not qb_run or legacy SelectIdle.");
        Check(((uint)Get(Lower, "Mode") & 2u) == 0, "Original general_run clears lower MODE_UNINT on the neutral tail.");
        SameTickCannotRepeat();
        Advance(initial + 36);
        Check((int)Get(Actor, "SourceImmediateGeneralRuns") == 1,
            "The replaced get-up program cannot replay general_run on its following source wake.");
    }

    private static void LateRun()
    {
        string[] scripts = { "r_histep_anim", "r_skip_anim", "r_hihand_anim", "r_bktau2_anim" };
        for (int selected = 0; selected < scripts.Length; selected++)
        for (int near = 0; near < 2; near++)
        {
            Prepare(GamePhase.LiveBall, true);
            raw = selected;
            Set(Actor, "SourceStick", 21);
            Position(((Array)Get(Model, "Players")).GetValue(7),
                SourceMatchRules.WorldPosition(81, 0), near == 0 ? 100f : 46f);
            Call(host.Game, "CacheSourceGeometry");
            int before = random.Trace.Count;
            Execute();
            string expected = near == 0 ? scripts[selected] : "r_ezsprint_anim";
            Check(Script == expected && Draws("player.c:standard_run:taunt", before) == 1,
                "Original past-80 carrier selects its actual taunt before the ordered strictly-under-six-yard sprint override: " + expected);
            Check(random.Trace[before].Site.SourceIdentity == "player.c:standard_run:taunt"
                && random.Trace[before].Site.BoundOrModulus == 4,
                "The four-entry shared draw precedes animation/speech callbacks, including overwritten taunts.");
            Check((AnimationSelectionEvidence)Get(Animation, "Evidence") == AnimationSelectionEvidence.SourceVerified,
                "Actual M1 late-run programs are source backed; legacy approximation evidence is not silently renamed.");
            long serial = (long)Get(Lower, "InstanceId");
            SameTickCannotRepeat();
            Begin(Tick + 1);
            Execute();
            Check(Script == expected && (long)Get(Lower, "InstanceId") == serial
                && Draws("player.c:standard_run:taunt", before) == 1,
                "An already active late-run/sprint sequence cannot reselect or redraw every player tick.");
        }
        foreach (float yards in new[] { 80f, 80.5f })
        {
            Prepare(GamePhase.LiveBall, true);
            Set(Actor, "SourceStick", 21);
            Position(Actor, SourceMatchRules.WorldPosition(80, 0) + (yards - 80f) * SourceTiming.SourceUnitsPerYard, 0f);
            Call(host.Game, "CacheSourceGeometry");
            int before = random.Trace.Count;
            Execute();
            Check(Script == "run_wb_anim" && Draws("player.c:standard_run:taunt", before) == 0,
                "The original integer FIELDX>80 guard must not become a continuous-yard or generic-victory trigger.");
        }
    }

    private static void UnsupportedTouchdowns()
    {
        string[] scripts = { "endz_06", "endz_08", "endz_20", "end_zone1_anim", "end_zone5_anim",
            "end_zone6_anim", "end_zone9_anim", "end_zone3_anim", "end_zone2_anim", "end_zone4_anim",
            "end_zone7_anim", "end_zone8_anim", "end_zone10_anim", "end_zone11_anim", "end_zone13_anim",
            "end_zone14_anim", "end_zone15_anim", "end_zone18_anim", "end_zone19_anim",
            "disapointed1_anim", "disapointed2_anim" };
        string[] clips = { "ENDZ_06", "ENDZ_08", "ENDZ_20", "INZONE1", "M_TAUNBC", "M_EZCTHR", "M_EZCGUN",
            "INZONE3", "INZONE2", "M_CELCHE", "M_EZCWHA", "M_EZWAV1", "M_EZCSPN", "M_EZCHRS", "M_EZPUSH",
            "M_EZJASN", "M_EZDNCE", "M_EZJON1", "M_EZJON2", "R_SLO_D1", "R_SLO_D2" };
        for (int i = 0; i < scripts.Length; i++)
        {
            bool rejected = false;
            try { Static("SourceAnimationPrograms", "Get", scripts[i]); }
            catch (NotSupportedException error) { rejected = error.Message == "Unported source animation script: " + scripts[i]; }
            Check(rejected && !host.Data.Animations.SequenceNames.Contains(clips[i]),
                "The precise unsupported TD/reaction script and required original clip stay visible: " + scripts[i] + "/" + clips[i]);
        }
    }

    private static object DefensiveSwitchFixture(string script, SourceActorMode mode, bool linked = true)
    {
        Prepare(GamePhase.LiveBall, false);
        Array players = (Array)Get(Model, "Players");
        object previous = players.GetValue(7), next = players.GetValue(8), other = players.GetValue(0);
        Set(Model, "CarrierId", -1);
        Set(Model, "BallKind", SourceBallKind.Fumble);
        Set(Model, "SourceBallTarget", new SourceVector3(0f, 0f, 0f));
        Set(Model, "ControlledPlayerId", 7);
        ((int[])Get(Model, "SourceStationPlayers"))[0] = 7;
        foreach (object player in players)
        {
            Set(player, "SourceControllerStation", -1);
            Set(player, "SourcePuppetLinkId", -1);
            Set(player, "SourceHitPartnerId", 0);
            Set(player, "SourceStick", 24);
            Set(player, "SourcePreviousStick", 24);
            Position(player, 1000f, 0f);
        }
        Set(previous, "SourceControllerStation", 0);
        Set(previous, "SourceMode", SourceActorMode.DefensiveBack);
        Position(previous, 100f, 0f);
        Set(next, "SourceMode", mode);
        Set(next, "SourceHitPartnerId", 0);
        Set(other, "SourceHitPartnerId", 8);
        Set(next, "SourcePuppetLinkId", linked ? 0 : -1);
        Set(other, "SourcePuppetLinkId", linked ? 8 : -1);
        Position(next, 1f, 0f);
        Call(host.Game, "CacheSourceGeometry");
        Call(Get(next, "AnimationState"), "FullBody", script, next, Tick, Draw,
            AnimationSelectionEvidence.SourceVerified);
        Position(next, 1f, 0f);
        Call(host.Game, "CacheSourceGeometry");
        Call(Get(Get(other, "AnimationState"), "Primary"), "AddMode", 0x00c00000u);
        Call(host.Game, "BeginSourceProcess", "player_proc", 7, 5);
        return next;
    }

    private static void DefensiveBlockSwitch()
    {
        string[] scripts = { "spin_a_anim", "spin_v_anim", "tie3c_a_anim", "tie3c_v_anim",
            "pushlg1a_anim", "pushlg1v_anim", "tie1c_a_anim", "tie1c_v_anim",
            "tie2c_a_anim", "tie2c_v_anim", "pushbl3_anim" };
        foreach (string script in scripts)
        {
            object next = DefensiveSwitchFixture(script, SourceActorMode.DefensiveBack);
            object previous = ((Array)Get(Model, "Players")).GetValue(7);
            object other = ((Array)Get(Model, "Players")).GetValue(0);
            object lower = Get(Get(next, "AnimationState"), "Primary");
            int before = random.Trace.Count;
            long instance = (long)Get(lower, "InstanceId");
            Check((int)Call(host.Game, "ChangeSourceDefensePlayer", previous, false) == 0,
                "Uninterruptible source block must be admitted by nearest-defender query: " + script);
            Check(random.Trace.Count == before && (long)Get(lower, "InstanceId") == instance
                && (int)Get(next, "SourcePuppetLinkId") == 0
                && (int)Get(previous, "SourceControllerStation") == 0,
                "A do_change=false query must not draw, release, animate, or transfer control: " + script);
            Check((int)Call(host.Game, "ChangeSourceDefensePlayer", previous, true) == 0
                && (int)Get(next, "SourceControllerStation") == 0
                && (int)Get(previous, "SourceControllerStation") == -1
                && ((int[])Get(Model, "SourceStationPlayers"))[0] == 8
                && ((SourceDroneButtons)Get(next, "SourceButtons") & SourceDroneButtons.B) != 0,
                "Block breakout transfers the actual station and then fakes the new defender's B: " + script);
            Check((int)Get(next, "SourcePuppetLinkId") == -1 && (int)Get(other, "SourcePuppetLinkId") == -1
                && ((uint)Get(lower, "Mode") & 0x00c00000u) == 0
                && ((uint)Get(Get(Get(other, "AnimationState"), "Primary"), "Mode") & 0x00c00000u) == 0
                && (string)Get(Get(Get(other, "AnimationState"), "Primary"), "ScriptSymbol") == "h_flailm_anim"
                && (string)Get(lower, "ScriptSymbol") == "breath2_anim",
                "Bilateral puppet release and original H_FLAILM precede standard_run before station transfer: " + script);
            Check(Draws("player.c:db_change_player:6360", before) == 0,
                "Non-line defenders short-circuit breakout RNG: " + script);
        }

        foreach (string script in new[] { "run_anim", "breath2_anim" })
        {
            object next = DefensiveSwitchFixture(script, SourceActorMode.DefensiveBack, false);
            Call(Get(Get(next, "AnimationState"), "Primary"), "AddMode", (uint)SourceMovementModes.Uninterruptible);
            object previous = ((Array)Get(Model, "Players")).GetValue(7);
            int before = random.Trace.Count;
            Check((int)Call(host.Game, "ChangeSourceDefensePlayer", previous, true) == -1
                && (int)Get(previous, "SourceControllerStation") == 0 && random.Trace.Count == before,
                "Uninterruptible nonblock sequences outside both enum boundaries stay excluded: " + script);
        }

        int[] chances = { 80, 75, 70, 60, 50, 40, 30, 20, 10, 5, 0, 0 };
        for (int handicap = 0; handicap < chances.Length; handicap++)
        for (int edge = 0; edge < 2; edge++)
        {
            object next = DefensiveSwitchFixture("tie1c_v_anim", SourceActorMode.Line);
            object previous = ((Array)Get(Model, "Players")).GetValue(7);
            object other = ((Array)Get(Model, "Players")).GetValue(0);
            ((int[])Get(Model, "SourceEffectiveTeamHandicap"))[0] = handicap;
            ((int[])Get(Model, "SourceEffectiveTeamHandicap"))[1] = 11 - handicap;
            raw = Math.Max(0, chances[handicap] - edge);
            int before = random.Trace.Count;
            object lower = Get(Get(next, "AnimationState"), "Primary");
            long instance = (long)Get(lower, "InstanceId");
            Check((int)Call(host.Game, "ChangeSourceDefensePlayer", previous, false) == 0
                && random.Trace.Count == before && (int)Get(next, "SourceControllerStation") == -1,
                "Line eligibility queries cannot consume the later breakout decision or transfer control.");
            Call(host.Game, "ChangeSourceDefensePlayer", previous, true);
            bool released = raw < chances[handicap];
            Check(Draws("player.c:db_change_player:6360", before) == 1
                && random.Trace[before].Site.SourceIdentity == "player.c:db_change_player:6360"
                && random.Trace[before].Site.BoundOrModulus == 100
                && ((int)Get(next, "SourcePuppetLinkId") == -1) == released
                && ((int)Get(other, "SourcePuppetLinkId") == -1) == released
                && (int)Get(next, "SourceControllerStation") == 0,
                "Line breakout uses exactly one first draw and strict OFFH threshold; even failed breakout transfers control: "
                + handicap + "/" + raw);
            Check(released
                ? (string)Get(lower, "ScriptSymbol") == "breath2_anim"
                : (long)Get(lower, "InstanceId") == instance
                    && (string)Get(lower, "ScriptSymbol") == "tie1c_v_anim"
                    && ((uint)Get(lower, "Mode") & (uint)SourceMovementModes.Puppet) != 0,
                "A failed breakout retains the actual locked VM; success runs before the defender becomes human.");
        }

        object unlinked = DefensiveSwitchFixture("pushbl3_anim", SourceActorMode.DefensiveBack, false);
        Call(host.Game, "ChangeSourceDefensePlayer", ((Array)Get(Model, "Players")).GetValue(7), true);
        Check((int)Get(unlinked, "SourcePuppetLinkId") == -1
            && (string)Get(Get(Get(unlinked, "AnimationState"), "Primary"), "ScriptSymbol") == "breath2_anim",
            "An original block without a puppet link still executes standard_run and transfers control.");

        object callerTarget = DefensiveSwitchFixture("spin_v_anim", SourceActorMode.DefensiveBack);
        object caller = ((Array)Get(Model, "Players")).GetValue(7);
        Set(caller, "SourceNewButtons", SourceDroneButtons.B);
        Set(caller, "SourceButtons", SourceDroneButtons.B);
        Call(Get(Get(caller, "AnimationState"), "Secondary"), "SetMovementMode", 0u);
        Call(host.Game, "SourceDefensiveMovementButtons", caller);
        Check((int)Get(callerTarget, "SourceControllerStation") == 0
            && (int)Get(callerTarget, "SourcePuppetLinkId") == -1
            && (int)Get(caller, "SourceControllerStation") == -1,
            "The actual fresh-B defensive movement caller reaches block admission, breakout, and control transfer.");
    }

    private static void BeginOriginalWake(long tick, long serial, string function, int actor)
    {
        var processes = (SourcePortableProcessHost)Get(Model, "SourceProcesses");
        while (processes.SourceTick < tick) processes.BeginTick(processes.SourceTick + 1);
        Set(Model, "SourceTick", tick);
        Check(processes.Visit(serial), "The bounded fixture uses the actual registered source process wake.");
        Call(host.Game, "BeginSourceProcess", function, actor, 0);
    }

    private static void EndOriginalWake() { Call(host.Game, "CompleteSourceProcess"); }

    private static void CompleteOriginalPrograms()
    {
        string[] scripts = { "endz_06", "endz_08", "endz_20", "end_zone1_anim", "end_zone5_anim",
            "end_zone6_anim", "end_zone9_anim", "end_zone3_anim", "end_zone2_anim", "end_zone4_anim",
            "end_zone7_anim", "end_zone8_anim", "end_zone10_anim", "end_zone11_anim", "end_zone13_anim",
            "end_zone14_anim", "end_zone15_anim", "end_zone18_anim", "end_zone19_anim",
            "disapointed1_anim", "disapointed2_anim" };
        string[] clips = { "ENDZ_06", "ENDZ_08", "ENDZ_20", "INZONE1", "M_TAUNBC", "M_EZCTHR", "M_EZCGUN",
            "INZONE3", "INZONE2", "M_CELCHE", "M_EZCWHA", "M_EZWAV1", "M_EZCSPN", "M_EZCHRS", "M_EZPUSH",
            "M_EZJASN", "M_EZDNCE", "M_EZJON1", "M_EZJON2", "R_SLO_D1", "R_SLO_D2" };
        for (int i = 0; i < scripts.Length; i++)
        {
            Prepare(GamePhase.DeadBall, true);
            host.Game.TakeSourceSoundVmDispatches();
            BeginOriginalWake(100, 6, "player_proc", 4);
            FullBody(scripts[i]);
            Check((string)Get(Lower, "SequenceName") == clips[i] && (bool)Get(Lower, "IsUninterruptible"),
                "The complete-only VM starts the actual distinct clip " + scripts[i] + "/" + clips[i]);
            Check(i >= 19 || ((int)Get(Actor, "SourceRawFlags") & 0x100) != 0,
                "The dance's actual PF_CELEBRATE flag is set at script entry.");
            EndOriginalWake();
            int maximumFrame = (int)Get(Lower, "FrameOrdinal");
            long terminal = -1;
            for (long tick = 101; tick < 1000; tick++)
            {
                BeginOriginalWake(tick, 6, "player_proc", 4);
                Call(Animation, "Advance", Actor, tick, Draw);
                maximumFrame = Math.Max(maximumFrame, (int)Get(Lower, "FrameOrdinal"));
                bool returned = Script == "breath_anim";
                EndOriginalWake();
                if (returned) { terminal = tick; break; }
            }
            Check(terminal > 100 && maximumFrame > 4 && Script == "breath_anim"
                && ((int)Get(Actor, "SourceRawFlags") & 0x100) == 0,
                "Actual selected frames progress, cleanup clears PF_CELEBRATE and the original breath tail returns: " + scripts[i]);
            if (i == 0 || i == 1 || i == 2 || i == 14 || i == 16 || i == 17 || i == 18)
            {
                int expected = i == 0 ? 270 : i == 1 ? 132 : i == 2 ? 250 : i == 16 ? 426 : 346;
                Check(terminal - 100 == expected,
                    "Original transition plus INTSTREAM durations are retained for " + scripts[i] +
                    ": expected " + expected + ", actual " + (terminal - 100));
            }
            SourceSoundVmDispatch[] outputs = host.Game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
            Check(outputs.All(output => output.Status == SourceSoundVmStatus.Resolved
                && output.HasOriginalAnimationCue && output.OriginalCue.Context.ExecutingSymbol == scripts[i]),
                "Each real animation sound callback is paired once and resolved through its exact closed original tuple: " +
                scripts[i] + "/" + string.Join(";", outputs.Select(output => output.Status + ":" +
                    (output.HasOriginalAnimationCue ? output.OriginalCue.Context.ExecutingSymbol + ":" +
                        output.OriginalCue.Context.SourceLine + ":" + output.OriginalCue.FunctionSymbol : "native"))));
            if (i == 6)
                Check(outputs.Length == 9 && outputs.SelectMany(output => output.Result.Calls).Select(call => call.PlaylistId)
                    .SequenceEqual(new[] { 147, 147, 147, 147, 147, 182, 182, 181, 183 }),
                    "The gun dance executes all nine ordered original fixed attack calls, not one generic pose/sound.");
            SameTickCannotRepeat();
        }
    }

    private static void CompleteOriginalTouchdownCaller()
    {
        foreach (bool puppet in new[] { false, true })
        {
            Prepare(GamePhase.LiveBall, true);
            Position(Actor, SourceMatchRules.WorldPosition(102, 0), 0);
            Set(Model, "SourceLastPlayEndCause", null);
            Array actors = (Array)Get(Model, "Players");
            object busy = actors.GetValue(7), eligible = actors.GetValue(8), later = actors.GetValue(9);
            Position(busy, SourceMatchRules.WorldPosition(102, 0) - 10, 0);
            Position(eligible, SourceMatchRules.WorldPosition(102, 0) - 50, 0);
            Position(later, SourceMatchRules.WorldPosition(102, 0) - 1, 0);
            Call(Get(Get(busy, "AnimationState"), "Primary"), "AddMode", 2u);
            if (puppet)
            {
                Set(Actor, "SourceRawFlags", 0x100000);
                Set(Actor, "SourcePuppetLinkId", 7);
                Set(busy, "SourcePuppetLinkId", 4);
                Set(Actor, "SourceHitPartnerId", (int?)7);
                Set(busy, "SourceHitPartnerId", (int?)4);
                Call(Lower, "AddMode", 0x00400080u);
                Call(Get(Get(busy, "AnimationState"), "Primary"), "AddMode", 0x00800080u);
            }
            Call(host.Game, "CacheSourceGeometry");
            originalInputs = site => site.SourceIdentity == "player.c:_mode_wpn:2389" ? 39
                : site.SourceIdentity == "player.c:_mode_wpn:2390" ? 2 : 0;
            int before = random.Trace.Count;
            BeginOriginalWake(100, 6, "player_proc", 4);
            Execute();
            EndOriginalWake();
            Check(Script == "endz_20" && (SourcePlayEndCause?)Get(Model, "SourceLastPlayEndCause")
                == SourcePlayEndCause.CarrierTouchdown && Get(Model, "SourceC2PendingResult") != null,
                "Actual _mode_wpn/td_check source caller queues the touchdown and starts its selected new dance in that actor wake.");
            int[] choiceBounds = random.Trace.Skip(before).Where(draw => draw.Site.SourceIdentity.StartsWith(
                "player.c:_mode_wpn:", StringComparison.Ordinal)).Select(draw => draw.Site.BoundOrModulus).ToArray();
            Check(choiceBounds.SequenceEqual(new[] { 100, 3, 2 }) &&
                (string)Get(Get(Get(eligible, "AnimationState"), "Primary"), "ScriptSymbol") == "disapointed1_anim" &&
                (string)Get(Get(Get(later, "AnimationState"), "Primary"), "ScriptSymbol") != "disapointed1_anim",
                "Family/new/reaction bounds/order and the FIRST eligible opposing actor within50, not the closest, are exact.");
            SourceProcessSoundCall speech = host.Game.TakeSourceProcessSoundCalls().Single(call =>
                call.Operand == "sounds.c:disappointed_speech:502");
            Check(speech.PlaylistId == 45 && speech.Bank == host.Game.C2State.TauntBank && speech.RandomCalls == 1
                && speech.ProcessSerial == 6 && speech.SourceTick == 100,
                "The actual nearby reaction emits exactly one ordered original45/46 selection in its real carrier process.");
            if (puppet)
                Check((int)Get(Actor, "SourcePuppetLinkId") == -1 && (int)Get(busy, "SourcePuppetLinkId") == -1 &&
                    (long)Get(Actor, "SourcePuppetReleaseTick") == 100 &&
                    ((int)Get(Actor, "SourceRawFlags") & 0x100000) == 0 &&
                    (string)Get(Get(Get(busy, "AnimationState"), "Primary"), "ScriptSymbol") == "h_bsider_anim",
                    "Puppet release timestamp/modes/drag flag precede the real H_BSIDER program and bilateral unlink.");
            int scoreBefore = (int)Get(Model, "HomeScore");
            for (long tick = 101; tick <= 103; tick++)
            {
                BeginOriginalWake(tick, 1, "game_proc", -1);
                Call(host.Game, "SourceGameProc", default(PlayerCommand));
                EndOriginalWake();
            }
            Check((int)Get(Model, "HomeScore") == scoreBefore + 6 && Get(Model, "SourceC2PendingResult") == null,
                "Actual next-wake game_proc commits six points once; repeated source wakes cannot rescore the animation/reaction.");
            Call(host.Game, "Publish");
            Check(((ISourceMatchState)host.Game.State).Match.TouchdownStats.HomeTouchdowns == 1,
                "The original once-only touchdown statistical producer remains attached to the real pending result.");
        }

        foreach (bool spin in new[] { false, true })
        {
            Prepare(GamePhase.DeadBall, true);
            Set(Model, "SourceLastPlayEndCause", SourcePlayEndCause.CarrierTouchdown);
            if (spin) FullBody("spin_anim");
            else Call(Lower, "AddMode", 2u);
            originalInputs = site => site.SourceIdentity == "player.c:_mode_wpn:2389" ? 40 : 0;
            BeginOriginalWake(100, 6, "player_proc", 4);
            int before = random.Trace.Count;
            Execute();
            EndOriginalWake();
            Check(spin ? Script == "end_zone10_anim" && random.Trace.Skip(before).Count(draw =>
                draw.Site.SourceIdentity.StartsWith("player.c:_mode_wpn:", StringComparison.Ordinal)) == 2
                : random.Trace.Skip(before).All(draw => !draw.Site.SourceIdentity.StartsWith(
                    "player.c:_mode_wpn:", StringComparison.Ordinal)),
                "The lower-uninterruptible guard retains the exact SPIN exception and its extra conditional bound100.");
        }
        foreach (int guard in new[] { 0, 1, 2 })
        {
            Prepare(guard == 2 ? GamePhase.GameOver : GamePhase.DeadBall, true);
            Set(Model, "SourceLastPlayEndCause", guard == 1
                ? SourcePlayEndCause.CarrierTackledInBounds : SourcePlayEndCause.CarrierTouchdown);
            if (guard == 0) Static("SourcePlayerDriver", "SetSourcePosition", Actor,
                new SourceVector3(SourceMatchRules.WorldPosition(102, 0), .01f, 0));
            int before = random.Trace.Count;
            BeginOriginalWake(100, 6, "player_proc", 4); Execute(); EndOriginalWake();
            Check(random.Trace.Skip(before).All(draw => !draw.Site.SourceIdentity.StartsWith(
                "player.c:_mode_wpn:", StringComparison.Ordinal)),
                "Airborne, non-TD and preserved GameOver observations cannot select a generic dance or consume TD RNG.");
        }
    }

    private static void CompleteOriginalSoundLifecycle()
    {
        host.Game.Restart();
        SourceBootSoundOutput[] boot = host.Game.TakeSourceBootSoundOutputs().ToArray();
        Check(boot.Length == 17 && boot.Select(output => output.OutputSequence).SequenceEqual(
            Enumerable.Range(0, boot.Length).Select(i => boot[0].OutputSequence + i)) &&
            boot.All(output => output.SourceTick == 0 && output.ProcessSerial == 0 &&
                output.EntryKind == SourceC2EntryKind.CompleteOriginalVersusSoundBanks) &&
            host.Game.TakeSourceBootSoundOutputs().Count == 0,
            "The named actual constructor entry has one immutable ordered17-output boot batch, never fake positive process wakes.");
        Check(boot[4].ControlKind == SourceProcessSoundControlKind.DeleteBank && boot[4].Bank == "cursors" &&
            boot[5].ControlKind == SourceProcessSoundControlKind.LoadBank && boot[5].Bank == "cursors2" &&
            boot[5].SourceLine == 1154 && host.Game.SourceCursorSoundBank == "cursors2" &&
            boot[15].PlaylistId == 0 && boot[15].PriorityWord == 0x8000 &&
            boot[16].Bank == "generic" && boot[16].PlaylistId == 1 && boot[16].PriorityWord == 0x8080,
            "Actual VS delete/strcpy/load changes the cursor bank and starts the original selected tune/crowd tracks, not an alias.");
        Check(random.Trace.Take(3).Select(draw => draw.Site.BoundOrModulus).SequenceEqual(new[] { 5, 2, 2 }) &&
            random.Trace[0].Site.SourceIdentity == "lib/select/vsscreen.c:vs_screen:1159",
            "The real tune bound5 precedes both original InitGameProc bound2 bank selections.");
        long last = boot[boot.Length - 1].OutputSequence;
        host.Game.Restart();
        Check(host.Game.TakeSourceBootSoundOutputs().First().OutputSequence > last,
            "A real same-Game reset retains the common source output counter.");

        foreach (bool quarterback in new[] { false, true })
        {
            Prepare(GamePhase.DeadBall, true, quarterback ? SourceActorMode.Quarterback : SourceActorMode.Receiver);
            Set(Actor, "SourceHitPartnerId", (int?)7);
            Array actors = (Array)Get(Model, "Players");
            Set(actors.GetValue(7), "SourceHitPartnerId", (int?)4);
            BeginOriginalWake(100, 6, "player_proc", 4); FullBody("h_bsider_anim"); EndOriginalWake();
            var processes = (SourcePortableProcessHost)Get(Model, "SourceProcesses");
            SourceProcessView child = default(SourceProcessView);
            long born = 0;
            for (long tick = 101; tick < 200; tick++)
            {
                BeginOriginalWake(tick, 6, "player_proc", 4);
                Call(Animation, "Advance", Actor, tick, Draw);
                EndOriginalWake();
                child = processes.Processes.FirstOrDefault(process => process.Function ==
                    (quarterback ? "sacked_sp_proc" : "tackled2_sp_proc"));
                if (child.Serial != 0) { born = tick; break; }
            }
            Check(born > 100 && child.BirthTick == born && child.Id == 0,
                "Actual H_BSIDER ANI_CODE creates the original native speech child, not a sound observer/reselection.");
            host.Game.TakeSourceSoundVmDispatches();
            host.Game.TakeSourceBootSoundOutputs();
            int delay = quarterback ? 5 : 10;
            for (long tick = born + 1; tick <= born + delay + 1; tick++)
            {
                processes.BeginTick(tick); Set(Model, "SourceTick", tick);
                Check((bool)Call(host.Game, "DispatchSourceOriginalSpeechChild", child.Serial),
                    "The registered child executes through its real source dispatcher.");
                SourceSoundVmDispatch[] outputs = host.Game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
                if (tick <= born + delay)
                    Check(outputs.Length == 0 && processes.Contains(child.Serial),
                        "qcreate's next wake plus exact5/10 sleep cannot emit early; draining boot output cannot remove children.");
                else
                    Check(outputs.Length == 1 && outputs[0].Status == SourceSoundVmStatus.Resolved &&
                        !outputs[0].HasOriginalAnimationCue && outputs[0].C2Context.ActorId == -1 &&
                        outputs[0].C2Context.FunctionSymbol == child.Function &&
                        outputs[0].C2Context.SourceLine == (quarterback ? 1407 : 1463) &&
                        outputs[0].Result.RandomCalls == 1 && !processes.Contains(child.Serial),
                        "The native child emits exactly once from the actual original table/site after its real delay, then dies.");
            }
        }
    }

    private static void ContactProductionChildCase(PlayerData data, bool fire, bool caught, bool language,
        GamePhase? suppressLate, GamePhase initialPhase = GamePhase.DeadBall)
    {
        host = new ActionFixture(data, KernelBinding.Create(data, PreviewOptions.FromUrl(
            "?source-gameflow=portable" + (fire ? "&source-fire=portable" : ""),
            data.Catalog.Profile.StableId), randomFactory: seed => random = new SourceInjectedRandom(site =>
                site.SourceIdentity == "sounds.c:hit_intended_receiver_sp_proc:750" ? 1
                : site.SourceIdentity == "sounds.c:head_plow_speech:873" ? 3 : 0,
                new SourceRandomProvenance("independent-production-contact-children",
                    "Declared source boundary inputs, not native RNG", "receiver1/plow3/other0",
                    SourceRandomEvidence.ExternalUnverified))));
        Check(host.Game.SourceCompleteOriginalEnabled && host.Game.SourceFireEnabled == fire,
            "The genuine original composition selects the actual fire or ordinary production path.");
        host.Game.BindSourceOriginalBadLanguageAdjustment(language);
        Set(Model, "Phase", initialPhase);
        Set(Model, "SourceMatchPhase", initialPhase == GamePhase.LiveBall
            ? SourceMatchPhase.LiveBall : SourceMatchPhase.PlayResult);
        Set(Model, "SourceRules", new SourceRulesState(0, 0, 1, 50, 90, SourcePlayType.Normal));
        Set(Model, "CarrierId", caught ? 4 : -1);
        Set(Model, "TargetPlayerId", 4);
        Set(Model, "BallKind", SourceBallKind.Fumble);
        Set(Model, "BallMode", BallMode.Dead);
        Set(Model, "SourceBallFlags", Enum.Parse(Get(Model, "SourceBallFlags").GetType(), "Freeze"));
        Set(Model, "SourceFlags", SourceGameFlags.None);
        Set(host.Game, "sourceOriginalLateHitSpeechIndex", 2);
        Array players = (Array)Get(Model, "Players");
        foreach (object actor in players)
        {
            Set(actor, "SourceMode", SourceActorMode.Dead);
            Set(actor, "SourceRawFlags", 0);
            Set(actor, "SourceButtons", SourceDroneButtons.None);
            Set(actor, "SourceNewButtons", SourceDroneButtons.None);
        }
        var camera = host.Game.SourceCameraHeadingProvider;
        var children = new Dictionary<string, long>();
        var processes = (SourcePortableProcessHost)Get(Model, "SourceProcesses");
        const long unrelated = 900000;
        processes.Create(unrelated, 0, "hit_intended_receiver_sp_proc_extra", 0);
        // Producer fixtures execute in a real wake; child dispatch occurs ONLY
        // through public Step and its ordinary/fire production message admission.
        host.Game.SourceCameraHeadingProvider = (state, tick) => {
            int heading = camera(state, tick);
            if (tick == 1)
                Call(host.Game, "SourceContactSound", players.GetValue(0), "qcreate", "att_head", 2125,
                    new[] { "plow", "0", "delay_plow_proc", "0", "0", "0", "0" });
            if (tick == 31)
            {
                Call(host.Game, "SourceContactSound", players.GetValue(4), "late_hit_speech", "att_late_hit", 1771,
                    new string[0]);
                Call(host.Game, "SourceContactSound", players.GetValue(4), "late_hit_speech", "att_late_hit", 1771,
                    new string[0]);
            }
            if (tick == 32)
                Call(host.Game, "SourceContactSound", players.GetValue(4), "hit_intended_receiver_sp",
                    "att_spear_head", 1976, new string[0]);
            foreach (SourceProcessView process in processes.Processes)
                if (process.Function == "delay_plow_proc" || process.Function == "late_hit_speech_proc"
                    || process.Function == "hit_intended_receiver_sp_proc")
                    children[process.Function] = process.Serial;
            return heading;
        };
        var sounds = new List<SourceProcessSoundCall>();
        long previous = 0;
        while (host.Game.SourceTick < 55)
        {
            if (suppressLate.HasValue && host.Game.SourceTick == 51)
            {
                Set(Model, "Phase", suppressLate.Value);
                Set(Model, "SourceMatchPhase", suppressLate.Value == GamePhase.PlaySelect
                    ? SourceMatchPhase.PlaySelection : SourceMatchPhase.PreSnap);
            }
            host.Game.Step(default(PlayerCommand));
            if (host.Game.SourceTick == previous) continue;
            previous = host.Game.SourceTick;
            var outputs = host.Game.TakeSourceProcessSoundCalls()
                .Where(sound => sound.Kind == SourceProcessSoundKind.OriginalContact).ToArray();
            sounds.AddRange(outputs);
            if (host.Game.SourceTick < 52)
                Check(outputs.Length == 0 && !random.Trace.Any(draw =>
                    draw.Site.SourceIdentity == "sounds.c:hit_intended_receiver_sp_proc:750"
                    || draw.Site.SourceIdentity == "sounds.c:head_plow_speech:873"),
                    "Production sleeping children must not speak or consume delayed RNG early.");
            if (host.Game.SourceTick == 2)
            {
                long serial;
                Check(children.TryGetValue("delay_plow_proc", out serial)
                    && processes.Require(serial).LastWakeTick == 2
                    && processes.Require(serial).SleepRemaining == 50,
                    "Production message-slot filter must wake queued delay_plow_proc on the next source tick.");
            }
        }
        Check(children.Count == 3 && children.Values.Distinct().Count() == 3,
            "All three actual producers create children; duplicate late-hit requests respect the native latch.");
        Check(children.Values.All(serial => !processes.Contains(serial))
            && ((System.Collections.ICollection)Get(host.Game, "sourceOriginalSpeechChildren")).Count == 0,
            "All children retire through actual production source steps, including silent native branches.");
        Check(processes.Contains(unrelated) && processes.Require(unrelated).LastWakeTick == -1,
            "Only exact admitted names run; a matching prefix stays excluded.");
        string[] expected = (caught ? new string[0] : new[] { "hit_intended_receiver_sp_proc" })
            .Concat(suppressLate.HasValue ? new string[0] : new[] { "late_hit_speech_proc" })
            .Concat(new[] { "delay_plow_proc" }).ToArray();
        Check(sounds.Select(sound => sound.Process).SequenceEqual(expected)
            && sounds.All(sound => sound.SourceTick == 52)
            && sounds.Zip(sounds.Skip(1), (a, b) => a.OutputSequence < b.OutputSequence).All(value => value),
            "Shared-wake output order is newest child first, retaining common producer ordinals.");
        var draws = random.Trace.Where(draw =>
            draw.Site.SourceIdentity == "sounds.c:hit_intended_receiver_sp_proc:750"
            || draw.Site.SourceIdentity == "sounds.c:head_plow_speech:873").ToArray();
        Check(draws.Length == (caught ? 1 : 2)
            && draws.All(draw => draw.Site.SourceTick == 52 && draw.Site.ActorId == -1
                && draw.Site.Kind == SourceRandomKind.RandRange)
            && draws.Select(draw => draw.Site.BoundOrModulus).SequenceEqual(caught ? new[] { 4 } : new[] { 2, 4 })
            && draws.Select(draw => draw.Site.Process).SequenceEqual(caught ? new[] { "delay_plow_proc" }
                : new[] { "hit_intended_receiver_sp_proc", "delay_plow_proc" })
            && draws.Select(draw => draw.Raw.Value).SequenceEqual(caught ? new[] { 3 } : new[] { 1, 3 }),
            "Real shared RNG draws receiver2 before plow4; caught-receiver and sequential late speech consume none.");
        foreach (SourceProcessSoundCall sound in sounds)
        {
            Check(sound.OriginalContactCue != null && sound.CalleeSourcePath == "sounds.c"
                && sound.ProcessSerial == children[sound.Process]
                && sound.Bank == (string)Get(Model, "SourceAnnouncerSwappedBank") && sound.Pan == 127,
                "Delayed production calls retain actual cues, announcer bank and native callee identity.");
            if (sound.Process == "hit_intended_receiver_sp_proc")
                Check(sound.PlaylistId == 134 && sound.Volume == 255 && sound.PriorityWord == 0xe281
                    && sound.CalleeSourceLine == 750 && sound.RandomCalls == 1,
                    "Original receiver table index1 and sound tuple are exact.");
            else if (sound.Process == "delay_plow_proc")
                Check(sound.PlaylistId == 153 && sound.Volume == 225 && sound.PriorityWord == 0xe28a
                    && sound.CalleeSourceLine == 873 && sound.RandomCalls == 1,
                    "Original four-entry plow table index3 and sound tuple are exact.");
            else
                Check(sound.PlaylistId == (language ? 142 : 149) && sound.Volume == 225
                    && sound.PriorityWord == 0xe285 && sound.CalleeSourceLine == (language ? 917 : 919)
                    && sound.RandomCalls == 0,
                    "Language-selected late-hit counter index2 advances sequentially without RNG.");
        }
        Check((int)Get(host.Game, "sourceOriginalLateHitSpeechIndex") == (suppressLate.HasValue ? 2 : 3),
            "The original sequential counter advances once only for a spoken late-hit branch.");
    }

    private static void ContactProductionScheduler(string root)
    {
        var data = new PlayerData(new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            path => File.ReadAllBytes(Path.Combine(root, path))));
        foreach (bool fire in new[] { false, true })
        {
            ContactProductionChildCase(data, fire, false, true, null, GamePhase.LiveBall);
            ContactProductionChildCase(data, fire, false, false, null);
            ContactProductionChildCase(data, fire, true, true, null);
            ContactProductionChildCase(data, fire, false, true, GamePhase.PreSnap);
            ContactProductionChildCase(data, fire, false, true, GamePhase.PlaySelect);
        }
        Console.WriteLine("PASS " + assertions + " independent actual-Step contact scheduler assertions; fire ON/OFF, no direct child dispatcher.");
    }

    private static void CompleteOriginal(string root)
    {
        var data = new PlayerData(new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            path => File.ReadAllBytes(Path.Combine(root, path))));
        Check(data.Catalog.Profile.IsCompleteOriginal303 && data.Animations.SequenceNames.Count == 303
            && data.Animations.FrameCount == 30777, "Only the genuine distinct complete-original303 data enables these programs.");
        host = new ActionFixture(data, KernelBinding.Create(data, PreviewOptions.FromUrl(
            "?source-gameflow=portable", data.Catalog.Profile.StableId),
            randomFactory: seed => random = new SourceInjectedRandom(site => originalInputs == null ? 0 : originalInputs(site),
                new SourceRandomProvenance("complete-original303/component", "Explicit source function inputs, not native RNG",
                    "bounded actual source caller/VM fixtures", SourceRandomEvidence.ExternalUnverified))));
        Check(host.Game.SourceCompleteOriginalEnabled && host.Game.SourceDefensivePreSnapSwitchEnabled
            && host.Game.SourceDefaultAudiblesEnabled, "Exact new callback/library admission retains the genuine source services.");
        CompleteOriginalPrograms();
        CompleteOriginalTouchdownCaller();
        CompleteOriginalSoundLifecycle();
        Console.WriteLine("Complete-original303: all21 programs and bounded actual touchdown/RNG/puppet/reaction/result callers passed; not a natural drive/native/browser claim.");
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--contact-production-scheduler")
            {
                ContactProductionScheduler(Path.GetFullPath(args[1]));
                return 0;
            }
            if (args.Length < 1 || args.Length > 2)
                throw new ArgumentException("Supply unchanged approved563 data, and optionally the separately authenticated complete-original303 root.");
            string root = Path.GetFullPath(args[0]);
            byte[] manifest = File.ReadAllBytes(Path.Combine(root, "manifest.json"));
            using (SHA256 sha = SHA256.Create())
                Check(BitConverter.ToString(sha.ComputeHash(manifest)).Replace("-", "").ToLowerInvariant()
                    == "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6",
                    "These cases require unchanged approved563 data; no new animation/profile admission.");
            var data = new PlayerData(new DataCatalog(manifest,
                path => File.ReadAllBytes(Path.Combine(root, path))));
            host = new ActionFixture(new PlayerHost(data, new NullSink(), options: PreviewOptions.FromUrl(
                "?source-gameflow=portable", data.Catalog.Profile.StableId),
                randomFactory: seed => random = new SourceInjectedRandom(site => raw,
                    new SourceRandomProvenance("admitted-action-tail/component", "Declared raw inputs, not native RNG",
                        "fixed-per-component-case", SourceRandomEvidence.ExternalUnverified))));
            Check(host.Game.Compatibility == SourceGameplayCompatibility.Portable,
                "These component cases retain the actual Portable production profile.");
            DefensiveBlockSwitch();
            DispatchUsesM1();
            DeadBallMilling();
            DeadBallGetUpCallback();
            LateRun();
            UnsupportedTouchdowns();
            if (args.Length == 2) CompleteOriginal(Path.GetFullPath(args[1]));
            Console.WriteLine("PASS " + assertions + " admitted action-tail assertions; actual production components, not natural-match/native/browser proof.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class NullSink : IRenderSink
    {
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags, double[] flashAlpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status) { }
    }
}
