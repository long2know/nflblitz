using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static class AnimationOpcodeChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int assertions;
    static readonly HashSet<string> clips = new(StringComparer.Ordinal);
    static readonly List<object> cases = new();
    static readonly List<object> policyChecks = new();
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); assertions++; }
    static object Get(object value, string name)
    {
        FieldInfo? field = value.GetType().GetField(name, Hidden | BindingFlags.Public);
        return field != null ? field.GetValue(value)! :
            value.GetType().GetProperty(name, Hidden | BindingFlags.Public)!.GetValue(value)!;
    }
    static void Set(object value, string name, object? field)
    {
        FieldInfo? found = value.GetType().GetField(name, Hidden | BindingFlags.Public)
            ?? value.GetType().GetField("<" + name + ">k__BackingField", Hidden);
        if (found != null) found.SetValue(value, field);
        else value.GetType().GetProperty(name, Hidden | BindingFlags.Public)!.SetValue(value, field);
    }
    static object? Call(object value, string name, params object?[] args)
    {
        MethodInfo[] methods = value.GetType().GetMethods(Hidden | BindingFlags.Public)
            .Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
        MethodInfo method = methods.Single(m => m.GetParameters().Zip(args).All(pair =>
            pair.Second == null || pair.First.ParameterType.IsInstanceOfType(pair.Second)));
        try { return method.Invoke(value, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { throw error.InnerException; }
    }
    static object Model(GameSimulation game) => Get(game, "Model");
    static object Actor(object model, int id) => ((Array)Get(model, "Players")).GetValue(id)!;
    static object Program(object model, string name)
    {
        Type type = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourceAnimationPrograms")!;
        return type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "Get" && m.GetParameters().Length == 2)
            .Invoke(null, new[] { name, Get(model, "SourceC2Programs") })!;
    }
    static object[] Instructions(object program) => ((Array)Get(program, "Instructions")).Cast<object>().ToArray();
    static float[][] Pose(SourceJointPose value) =>
        value.Rotations.Select(q => new[] { q.X, q.Y, q.Z, q.W }).ToArray();
    static object Snapshot(GameSimulation game, object actor, object layer, int? pc = null)
    {
        var root = (SourceVector3)Get(actor, "SourceRoot");
        var position = (FieldVector)Get(actor, "SourcePosition");
        var velocity = (FieldVector)Get(actor, "SourceVelocity");
        var radii = (SourceVector3)Get(actor, "SourceAttackRadii");
        object movement = Call(game, "CaptureSourceMovement", actor)!;
        return new {
            sequence = Get(layer, "SequenceName"), ordinal = Get(layer, "FrameOrdinal"),
            mode = Get(layer, "Mode"), hold = Get(layer, "hold"),
            vcount = Get(layer, "VariableCount"), vframe = Get(layer, "VariableFrame"),
            turn = Get(layer, "turnHack"), pc = pc ?? (int)Get(layer, "pc"),
            script = Get(layer, "ScriptSymbol"), codeEvidence = Get(layer, "CodeReturnEvidence").ToString(),
            root = new[] { root.X, root.Y, root.Z },
            position = new[] { position.X, (float)Get(actor, "SourceHeight"), position.Z },
            velocity = new[] { velocity.X, (float)Get(actor, "SourceVerticalVelocity"), velocity.Z },
            friction = Get(actor, "SourceFriction"), flags = (int)(SourceMovementFlags)Get(movement, "Flags"),
            facing = Get(actor, "Facing"), targetFacing = Get(actor, "TargetFacing"),
            upperFacing = Get(actor, "UpperFacing"), targetUpperFacing = Get(actor, "TargetUpperFacing"),
            attackMode = Get(actor, "SourceAttackMode"), attackTime = Get(actor, "SourceAttackTime"),
            attackJointA = Get(actor, "SourceAttackJointA"), attackJointB = Get(actor, "SourceAttackJointB"),
            attackRadii = new[] { radii.X, radii.Y, radii.Z },
            pose = Pose((SourceJointPose)Call(layer, "EvaluatePose")!)
        };
    }
    static (object Actor, object Layer, object Instruction, int Index, string Clip) Prime(
        PlayerData data, GameSimulation game, string script, string op, int occurrence = 0,
        string? text = null, int frame = 7, uint mode = 0x20040)
    {
        object model = Model(game), actor = Actor(model, 3), state = Get(actor, "AnimationState");
        object layer = Get(state, "Primary"), program = Program(model, script);
        object[] instructions = Instructions(program);
        int[] matching = instructions.Select((value, i) => (value, i))
            .Where(pair => Get(pair.value, "Op").ToString() == op &&
                (text == null || (string)Get(pair.value, "Text") == text))
            .Select(pair => pair.i).ToArray();
        Check(matching.Length > occurrence, "Declared opcode entry unavailable: " + script + "/" + op + "/" + text);
        int index = matching[occurrence];
        string sequence = instructions.Take(index + 1).Reverse()
            .Where(i => Get(i, "Op").ToString() is "NewSequence" or "Interpolate")
            .Select(i => (string)Get(i, "Text")).FirstOrDefault() ?? "RUN_WB";
        ISourceAnimationClip clip = data.Animations.GetClip(sequence);
        clips.Add(sequence);
        Check(frame >= 0 && frame < clip.SelectedSourceFrames.Count, "Declared real clip frame: " + sequence);
        Set(layer, "program", program); Set(layer, "clip", clip);
        Set(layer, "pc", index + 1); Set(layer, "FrameOrdinal", frame); Set(layer, "floatingFrame", (float)frame);
        Set(layer, "Mode", mode); Set(layer, "hold", 0); Set(layer, "transitionTicks", 0);
        Set(layer, "transitionFrom", null); Set(layer, "pendingTransitionOrigin", null);
        Set(layer, "lastAdvanceTick", -1L);
        Set(actor, "ModelScale", 1.5f); Set(actor, "Facing", 137); Set(actor, "TargetFacing", 137);
        Set(actor, "UpperFacing", 137); Set(actor, "TargetUpperFacing", 137);
        Set(actor, "SourceCoordinates", true); Set(actor, "SourcePosition", new FieldVector(4, -7));
        Set(actor, "SourceHeight", 1.5f); Set(actor, "SourceVelocity", new FieldVector(.3f, -.7f));
        Set(actor, "SourceVerticalVelocity", .2f); Set(actor, "SourceFriction", .125f);
        Set(actor, "SourceRawFlags", 0x400); Set(actor, "Flipped", false);
        Set(actor, "SourceAttackMode", 7); Set(actor, "SourceAttackTime", 19);
        Set(actor, "SourceAttackJointA", 2); Set(actor, "SourceAttackJointB", 9);
        Set(actor, "SourceAttackRadii", new SourceVector3(.1f, .2f, .3f));
        Set(actor, "SourceRoot", Call(layer, "SelectedRoot", actor));
        return (actor, layer, instructions[index], index, sequence);
    }
    static void Component(PlayerData data, string name, string native, string script, string op,
        int occurrence = 0, string? text = null, int frame = 7, uint mode = 0x20040,
        int randomValue = 0, Action<GameSimulation, object, object>? configure = null)
    {
        GameSimulation game = KernelBinding.Create(data);
        var p = Prime(data, game, script, op, occurrence, text, frame, mode);
        Set(Model(game), "Phase", GamePhase.LiveBall);
        Set(Model(game), "CarrierId", -1);
        Set(p.Actor, "PendingThrowTarget", -1);
        configure?.Invoke(game, p.Actor, p.Layer);
        var draws = new List<object>();
        Func<int, int> random = bound => { draws.Add(new { service = "randrng", bound, value = randomValue % bound }); return randomValue % bound; };
        Func<int, int> standard = bound => { draws.Add(new { service = "C-rand-modulo", bound, value = randomValue % bound }); return randomValue % bound; };
        Set(p.Layer, "CStandardRandom", standard);
        object before = Snapshot(game, p.Actor, p.Layer);
        Exception? failure = null;
        try { Call(p.Layer, "Execute", p.Instruction, p.Actor, random); }
        catch (Exception error) { failure = error; }
        object after = Snapshot(game, p.Actor, p.Layer);
        cases.Add(new {
            name, native, script, lowering = op, occurrence, sourceLine = Get(p.Instruction, "SourceLine"),
            clip = p.Clip, scale = 1.5f, before, after, draws, randomInput = randomValue, error = failure?.Message,
            input = new {
                gameMode = (GamePhase)Get(Model(game), "Phase") == GamePhase.LiveBall ? 2 : 4,
                carrier = Get(Model(game), "CarrierId"), actor = 3,
                helmet = Get(p.Actor, "SourceHelmetPopped"), pendingTarget = Get(p.Actor, "PendingThrowTarget"),
                ballDelta = new[] {
                    (((FieldVector)Get(p.Actor, "SourceBallTarget")).X - ((FieldVector)Get(p.Actor, "Position")).X)
                        * SourceTiming.SourceUnitsPerYard,
                    (((FieldVector)Get(p.Actor, "SourceBallTarget")).Z - ((FieldVector)Get(p.Actor, "Position")).Z)
                        * SourceTiming.SourceUnitsPerYard },
                hitFacing = (int?)Get(p.Actor, "SourceHitPartnerId") is int hit && hit >= 0
                    ? (int)Get(Actor(Model(game), hit), "Facing") : 0
            },
            boundary = "Actual compiled source instruction and real bound Core callback; declared instruction-entry component fixture, not natural selector coverage."
        });
    }
    static void Components(PlayerData data)
    {
        Component(data, "attack-on-all-operands", "ANI_ATTACK_ON", "qb_slide_anim", "AttackOn");
        Component(data, "attack-off-preserves-radii", "ANI_ATTACK_OFF", "qb_slide_anim", "AttackOff");
        Component(data, "set-flip-preserves-flash", "ANI_SETFLAG", "m_laterall_anim", "SetFlip");
        Component(data, "clear-flip-preserves-flash", "ANI_CLRFLAG", "m_laterall_anim", "SetFlip", occurrence: 1,
            configure: (_, a, _) => Set(a, "Flipped", true));
        Component(data, "code-known-int-nonzero", "ANI_CODE", "t_push_anim", "MovementCode", text: "face_throw_target");
        Component(data, "code-known-int-zero", "ANI_CODE", "t_push_anim", "MovementCode", text: "face_throw_target",
            configure: (g, a, _) => {
                Set(a, "PendingThrowTarget", 4);
                ((int[,])Get(Model(g), "SourceAngles"))[3, 4] = 137;
            });
        foreach (int value in new[] { 0, 1 })
            Component(data, "code-bnz-c-rand-" + value, "ANI_CODE_BNZ", "qbhut_knee_anim", "RandomBranch",
                randomValue: value);
        foreach (int value in new[] { 84, 85 })
            Component(data, "code-bnz-randrng-threshold-" + value, "ANI_CODE_BNZ", "qbset_anim", "RandomBranch",
                randomValue: value);
        foreach (bool helmet in new[] { false, true })
            Component(data, "code-bz-real-ball-predicate-" + helmet, "ANI_CODE_BZ", "r_diveoh_anim", "MovementBranchZero",
                mode: helmet ? 0x20040u : 0x20000u, configure: (g, a, _) => {
                    Set(Model(g), "Phase", helmet ? GamePhase.DeadBall : GamePhase.LiveBall);
                    Set(a, "SourceHelmetPopped", helmet);
                });
        foreach (bool helmet in new[] { false, true })
            Component(data, "code-bz-celebration-real-predicate-" + helmet, "ANI_CODE_BZ", "endz_08", "BranchIfNotHasBall",
                mode: helmet ? 0x20040u : 0x20000u, configure: (g, a, _) => {
                    Set(Model(g), "Phase", helmet ? GamePhase.DeadBall : GamePhase.LiveBall);
                    Set(a, "SourceHelmetPopped", helmet);
                });
        Component(data, "set-celebrate-preserves-flash", "ANI_SETFLAG", "endz_08", "SetCelebrate");
        Component(data, "clear-celebrate-preserves-flash", "ANI_CLRFLAG", "endz_08", "SetCelebrate", occurrence: 1,
            configure: (_, a, _) => Set(a, "SourceRawFlags", 0x500));
        Component(data, "end-retains-pose", "ANI_END", "qb_slide_anim", "End");
        Component(data, "prepare-transition-explicit-frame", "ANI_PREP_XITION", "punt_ball_anim", "PrepareTransition");
        foreach (int frame in new[] { 30, 31, 32 })
            Component(data, "run-to-frame-boundary-" + frame, "ANI_RUNTOFRAME", "qbhut_knee_anim", "RunToFrame", frame: frame);
        Component(data, "friction-exact-float", "ANI_SETFRICTION", "qb_slide_anim", "Friction");
        foreach (int heading in new[] { 0, 137, 511, -513 })
            Component(data, "velocity-relative-" + heading, "ANI_SETVELS", "punt_ball_anim", "SetVelocity",
                configure: (_, a, _) => Set(a, "Facing", heading));
        Component(data, "set-y-retains-planar", "ANI_SETYVEL", "qb_slide_anim", "SetYVelocity");
        foreach (int heading in new[] { 0, 333, 768 })
            Component(data, "hit-facing-not-own-" + heading, "ANI_SET_FACE_REL_VEL", "h_flailm_anim", "HitFacingVelocity",
                configure: (g, a, _) => { Set(a, "SourceHitPartnerId", 8); Set(Actor(Model(g), 8), "Facing", heading); });
        foreach (string script in new[] { "get_up_c2_hurt_anim", "get_up_c3_hurt_anim" })
            Component(data, "turnhack-signed-" + script, "ANI_TURNHACK", script, "TurnHack");
        foreach (float distance in new[] { .5f, 25f, 60f, 75f, 75.01f })
            Component(data, "leap-target-distance-" + distance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "ANI_LEAPAT_TGTX", "c_dive_for_ball_anim", "LeapTarget", configure: (_, a, _) => {
                    Set(a, "Position", new FieldVector(50, 0));
                    Set(a, "SourceBallTarget", new FieldVector(50 + distance / SourceTiming.SourceUnitsPerYard, 0));
                });
    }

    static void Policies(PlayerData data)
    {
        foreach (string op in new[] { "MovementCode", "RandomBranch", "BranchIfNotHasBall" })
        {
            GameSimulation game = KernelBinding.Create(data);
            string script = op == "MovementCode" ? "t_push_anim" : op == "RandomBranch" ? "qbhut_knee_anim" : "endz_08";
            var p = Prime(data, game, script, op, text: op == "MovementCode" ? "face_throw_target" : null);
            Set(p.Layer, "OriginalChangeSemantics", false);
            Set(Model(game), "Phase", GamePhase.LiveBall);
            Set(Model(game), "CarrierId", -1);
            Set(p.Actor, "PendingThrowTarget", -1);
            Func<int, int> random = _ => 1;
            Set(p.Layer, "CStandardRandom", random);
            uint mode = (uint)Get(p.Layer, "Mode");
            object evidence = Get(p.Layer, "CodeReturnEvidence");
            Call(p.Layer, "Execute", p.Instruction, p.Actor, random);
            Check((uint)Get(p.Layer, "Mode") == mode && Get(p.Layer, "CodeReturnEvidence").Equals(evidence),
                "The old numerical policy must retain its old mode/evidence behavior: " + op);
            policyChecks.Add(new { name = "legacy-policy-component-" + op, beforeMode = mode,
                afterMode = Get(p.Layer, "Mode"), profileRelabeled = false });
        }
        {
            GameSimulation game = KernelBinding.Create(data);
            var p = Prime(data, game, "t_push_anim", "MovementCode", text: "face_throw_target");
            Type instruction = p.Instruction.GetType();
            ConstructorInfo constructor = instruction.GetConstructors(Hidden).Single();
            object?[] values = constructor.GetParameters().Select(parameter =>
                parameter.Name == "op" ? Enum.Parse(parameter.ParameterType, "MovementBranchZero") :
                parameter.Name == "text" ? (object)"maybe_taunt" : parameter.DefaultValue).ToArray();
            object invalidConditional = constructor.Invoke(values);
            uint mode = (uint)Get(p.Layer, "Mode");
            string? error = null;
            try { Call(p.Layer, "Execute", invalidConditional, p.Actor, (Func<int, int>)(_ => throw new Exception("Unexpected RNG"))); }
            catch (InvalidOperationException failure) { error = failure.Message; }
            Check(error?.Contains("conditional ANI_CODE queries the opaque return of original void maybe_taunt") == true &&
                (uint)Get(p.Layer, "Mode") == mode,
                "An invalid conditional on a real original void callback remains rejected without fabricating its return.");
            policyChecks.Add(new { name = "opaque-void-invalid-conditional-negative", error,
                reachableOriginalSiteClaimed = false, callbackExecuted = false, rngCalls = 0 });
        }
    }

    sealed class Sink : IRenderSink, ISourceAudioSink, ISourceWorldRenderSink
    {
        internal long Presents;
        internal double[] Joints = Array.Empty<double>();
        internal readonly List<JsonElement> Audio = new();
        internal int Samples;
        internal SourceWorldRenderPacket? World;
        public void WorldPresentation(SourceWorldRenderPacket packet) { World = packet; }
        public void Texture(string id, int w, int h, byte[] rgba, int mips, bool repeat, bool linear, string sha, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternate, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] alpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hud, int[] colors, string[] textures, int[] counts, string status)
        {
            Check(models.Length == 14 && appearances.Length == 14 && joints.Length == 14 * 15 * 16,
                "Actual opcode Host must submit the fourteen-actor pose/matrix packet.");
            Presents++; Joints = joints.ToArray();
        }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata)
        {
            Check(id == "pcm16-" + DataCatalog.Hash(pcm) && rate == 31250 && channels == 1,
                "Any actual scheduled callback PCM retains its immutable sample identity.");
            Samples++;
        }
        public void AudioCommands(string text)
        {
            using var document = JsonDocument.Parse(text);
            Audio.AddRange(document.RootElement.GetProperty("commands").EnumerateArray().Select(value => value.Clone()));
        }
    }

    static void Render(PlayerHost host, int steps = 0) =>
        host.Advance(FixedStepBridge.StepSeconds * steps, 512, 384, 512, 384, 1);
    static void StartGuest(PlayerHost host)
    {
        void Raw(int buttons = 0, double x = 0) =>
            host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { buttons, 0, 0, 0 });
        void Tap(int buttons = 0, double x = 0)
        {
            Raw(); Raw(buttons, x); Raw(); Render(host);
            for (int tick = 0; tick < 70 && host.Session.UsingSourcePreGameInput; tick++)
            { Raw(); host.Advance(1d / 57, 512, 384, 512, 384, 1); }
        }
        Render(host); Tap(4);
        if (host.Session.Records?.Active == true) { Tap(x: 1); Tap(1); }
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams, "Public opcode fixture reaches actual Teams.");
        Tap(1); Tap(1); Tap(1);
        Check(host.Session.Playing && host.Game.SourceTick == 0, "Public opcode fixture starts a new actual source-zero game.");
        Raw(); Raw(); Render(host);
    }
    static void Step(PlayerHost host)
    {
        long before = host.Game.SourceTick;
        for (int attempt = 0; attempt < 3 && host.Game.SourceTick == before; attempt++)
        { host.Capture(new double[8], new int[4]); Render(host, 1); }
        Check(host.Game.SourceTick == before + 1, "Exactly one actual public source wake, without a clock setter.");
        Check(host.LatestRenderObservation?.CurrentSourceTick == host.Game.SourceTick,
            "The same actual wake reaches the production pose/matrix consumer.");
    }
    static (PlayerHost Host, Sink Sink) Host(PlayerData data)
    {
        var sink = new Sink();
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        var host = new PlayerHost(data, sink, observeRendering: true,
            options: PreviewOptions.FromUrl("", data.Catalog.Profile.StableId), recordStore: store);
        StartGuest(host); Step(host);
        object model = Model(host.Game);
        Set(model, "SourceRules", new SourceRulesState(0, 0, 2, 40, 70, SourcePlayType.Normal));
        Set(model, "SourceOffensePlay", data.Playbook.GetPlay("play_spider"));
        Set(model, "SourceDefensePlay", data.Playbook.GetPlay("play_safe_cover"));
        Set(model, "SourceFlags", SourceGameFlags.None);
        Set(model, "SourceKickApproach", false);
        Set(model, "Phase", GamePhase.LiveBall); Set(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        Set(model, "ClockActive", false); Set(model, "BallKind", SourceBallKind.Pass);
        Set(model, "BallMode", BallMode.Held); Set(model, "CarrierId", 3);
        foreach (object actor in (Array)Get(model, "Players"))
        {
            Set(actor, "SourceMode", SourceActorMode.Dead); Set(actor, "NoCatchTicks", 200);
            Set(actor, "SourceJob", Enum.ToObject(Get(actor, "SourceJob").GetType(), 0));
        }
        return (host, sink);
    }

    sealed class ScheduledProbe
    {
        internal PlayerHost Host = null!;
        internal object Actor = null!, Layer = null!;
        internal Delegate Original = null!;
        internal string Script = "";
        internal int Index;
        internal object Instruction = null!;
        internal object? Before, After, Leap;
        internal long Tick;
        internal int ShakerElapsed, CameraMode;
        internal void Observe<T>(T actor)
        {
            Original.DynamicInvoke(actor);
            string script = (string)Get(Layer, "ScriptSymbol");
            int current = (int)Get(Layer, "pc") - 1;
            if (Before == null && script == Script && current == Index)
            {
                Tick = Host.Game.SourceTick;
                Before = Snapshot(Host.Game, Actor, Layer);
                ShakerElapsed = (int)Get(Get(Host.Camera.Playback, "shaker"), "ElapsedTicks");
                CameraMode = Host.Camera.Playback.RawMode;
                object model = Model(Host.Game), camera = Get(model, "SourceMovementCamera");
                var transform = ((IEnumerable<float>)Get(camera, "Transform")).ToArray();
                var position = (SourceVector3)Get(camera, "Position");
                int target = Get(Instruction, "Op").ToString() == "LeapAttack"
                    ? SourceEvasionLeap.ResolveVictim((int)Get(Instruction, "A"), (int)Get(model, "CarrierId"),
                        (int)Get(Actor, "PendingThrowTarget"), (int)(TeamId)Get(model, "Possession"))
                    : Math.Max(0, (int)Get(model, "CarrierId"));
                object victim = AnimationOpcodeChecks.Actor(model, target);
                var velocity = (FieldVector)Get(victim, "SourceVelocity");
                var joints = (SourceVector3[,])Get(model, "SourceCameraJoints");
                Leap = new {
                    transform, position = new[] { position.X, position.Y, position.Z }, target,
                    velocity = new[] { velocity.X, (float)Get(victim, "SourceVerticalVelocity"), velocity.Z },
                    joints = Enumerable.Range(0, 15).Select(j => new[] { joints[target,j].X, joints[target,j].Y, joints[target,j].Z }).ToArray(),
                    cacheTick = Get(model, "SourceCameraJointTick"), cameraTick = Get(model, "SourceMovementCameraTick")
                };
            }
            else if (Before != null && After == null)
                After = Snapshot(Host.Game, Actor, Layer, current);
        }
    }

    static void Scheduled(PlayerData data, PlayerHost host, Sink sink, string name, string native,
        string script, string op, int carrier = 3, bool fullProgram = false)
    {
        object model = Model(host.Game);
        Set(model, "CarrierId", carrier); Set(model, "BallMode", BallMode.Held);
        var p = Prime(data, host.Game, script, op, mode: 0x20002);
        Set(p.Actor, "PendingThrowTarget", 4); Set(p.Actor, "SourceVelocity", default(FieldVector));
        Set(p.Actor, "SourceVerticalVelocity", 0f);
        Set(p.Layer, "pc", fullProgram ? 0 : p.Index);
        Set(p.Layer, "hold", 1);
        var probe = new ScheduledProbe { Host = host, Actor = p.Actor, Layer = p.Layer, Script = script,
            Index = p.Index, Instruction = p.Instruction };
        FieldInfo before = p.Layer.GetType().GetField("BeforeInstruction", Hidden)!;
        probe.Original = (Delegate)before.GetValue(p.Layer)!;
        before.SetValue(p.Layer, typeof(ScheduledProbe).GetMethod("Observe", Hidden)!
            .MakeGenericMethod(p.Actor.GetType()).CreateDelegate(before.FieldType, probe));
        object shaker = Get(host.Camera.Playback, "shaker");
        probe.ShakerElapsed = (int)Get(shaker, "ElapsedTicks");
        int audioBefore = sink.Audio.Count, samplesBefore = sink.Samples;
        try
        {
            for (int tick = 0; tick < 60 && probe.After == null; tick++) Step(host);
        }
        finally { before.SetValue(p.Layer, probe.Original); }
        Check(probe.Before != null && probe.After != null, "Actual scheduled opcode entry and continuation: " + name +
            ";before=" + (probe.Before != null) + ";tick=" + host.Game.SourceTick + ";script=" + Get(p.Layer, "ScriptSymbol") +
            ";pc=" + Get(p.Layer, "pc") + ";hold=" + Get(p.Layer, "hold") + ";failure=" + Get(model, "SourceAnimationFailure"));
        SourcePresentationCue[] cues = host.Game.PresentationCues.Cues
            .Where(c => c.Context.ExecutingSymbol == script).ToArray();
        SourceCameraCue? cameraCue = cues.OfType<SourceCameraCue>().SingleOrDefault();
        var after = fullProgram ? probe.Before! : probe.After!;
        cases.Add(new {
            name, native, script, lowering = op, occurrence = 0, sourceLine = Get(p.Instruction, "SourceLine"),
            clip = p.Clip, scale = 1.5f, before = probe.Before, after,
            draws = cameraCue == null ? Array.Empty<object>() : new object[] {
                new { service = "randrng", bound = cameraCue.Selection.EntryCount, value = cameraCue.Selection.SelectedOrdinal }
            },
            randomInput = cameraCue?.Selection.SelectedOrdinal ?? 0, error = (string?)null,
            input = new {
                gameMode = 2, carrier, actor = 3, helmet = false, pendingTarget = 4,
                ballDelta = new[] { 0f, 0f }, hitFacing = 0, leap = probe.Leap,
                shakerElapsed = probe.ShakerElapsed, beforeCameraMode = probe.CameraMode
            },
            consumer = new {
                sourceTick = probe.Tick, currentTick = host.Game.SourceTick,
                renderTick = host.LatestRenderObservation?.CurrentSourceTick, presents = sink.Presents,
                matrixValues = sink.Joints.Length, cameraCue, cameraMode = host.Camera.Playback.RawMode,
                cameraTarget = host.Camera.Playback.TargetActorId, cameraTick = host.Camera.Playback.LastCameraTick,
                shakerAmplitude = Get(shaker, "Amplitude"), shakerTotal = Get(shaker, "TotalTicks"),
                shakerElapsed = Get(shaker, "ElapsedTicks"),
                nativeLeapBytes = Get(p.Actor, "SourceLastLeapBytesConsumed"),
                nativeLeapCache = Get(p.Actor, "SourceLastLeapCacheTick"),
                samples = sink.Samples - samplesBefore, commands = sink.Audio.Skip(audioBefore).ToArray(),
                sourceSoundDeliveries = host.ProcessSoundDeliveries.Where(c => c.SourceTick == probe.Tick).ToArray(),
                clearedFlipAfterRelease = !(bool)Get(p.Actor, "Flipped")
            },
            expectedEffectBeforeCallback = fullProgram,
            boundary = "Declared source-row/actor placement fixture; actual public Capture/Advance/source player wake, real callbacks, Host camera/audio and fourteen-actor matrix consumer."
        });
    }
    static void ScheduledCases(PlayerData data)
    {
        var camera = Host(data);
        Scheduled(data, camera.Host, camera.Sink, "camera-original-table-to-host", "ANI_SETCAMBOT",
            "r_diveoh_anim", "CameraCue");
        var conditional = Host(data);
        Scheduled(data, conditional.Host, conditional.Sink, "code-bz-live-blocks-csi-production", "ANI_CODE_BZ",
            "r_diveoh_anim", "MovementBranchZero");
        var shaker = Host(data);
        Scheduled(data, shaker.Host, shaker.Sink, "shaker-carrier-reset", "ANI_SHAKER",
            "r_diveoh_anim", "ShakerCue");
        Scheduled(data, shaker.Host, shaker.Sink, "shaker-noncarrier-retains-elapsed", "ANI_SHAKER",
            "r_diveoh_anim", "ShakerCue", carrier: 8);
        var leap = Host(data);
        Scheduled(data, leap.Host, leap.Sink, "leap-actual-previous-camera-joint-cache", "ANI_LEAPATT",
            "elbow_drp_anim", "LeapAttack", carrier: 8);
        var target = Host(data);
        Scheduled(data, target.Host, target.Sink, "leap-actual-throw-target-camera-cache", "ANI_LEAPATT",
            "t_sprhed_anim", "LeapAttack", carrier: 8);
    }
    static void LeftReleaseCases(PlayerData data)
    {
        foreach (string script in new[] { "qb_rthrow_sl_anim", "qb_throw_sl_anim", "qb_throw_ll_anim" })
        {
            var throwing = Host(data);
            Scheduled(data, throwing.Host, throwing.Sink, "release-flip-real-callback-" + script,
                "ANI_SETFLAG", script, "Callback", fullProgram: true);
        }
    }

    private static int VerifyRawThrowSelectors(PlayerData data, string output)
    {
        var results = new List<object>();
        var lastTrace = new List<object>();
        var assets = new RenderAssets(data, new Sink());
        float[] Vector(SourceVector3 value) => new[] { value.X, value.Y, value.Z };
        SourceVector3 World(object actor) => (SourceVector3)typeof(GameSimulation).Assembly
            .GetType("Blitz.Gameplay.SourcePlayerDriver")!
            .GetMethod("WorldPosition", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { actor })!;
        void Place(object actor, SourceVector3 position) => typeof(GameSimulation).Assembly
            .GetType("Blitz.Gameplay.SourcePlayerDriver")!
            .GetMethod("SetSourcePosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { actor, position });
        PlayerCommand Raw(int x = 0, int z = 0, SourceButtons held = SourceButtons.None,
            SourceButtons pressed = SourceButtons.None, SourceButtons released = SourceButtons.None) =>
            new(stationInputs: new SourceInputFrame(new[] { new SourceStationInput(0, x, z, held, pressed, released) }));
        object Frame(GameSimulation game)
        {
            object model = Model(game), qb = Actor(model, 3), state = Get(qb, "AnimationState");
            return new {
                host = game.State.Tick, source = game.SourceTick, phase = game.State.Phase.ToString(),
                match = ((ISourceMatchState)game.State).Match.Phase.ToString(), carrier = Get(model, "CarrierId"),
                qbMode = Get(qb, "SourceMode").ToString(), flipped = Get(qb, "Flipped"),
                controller = Get(qb, "SourceControllerStation"), stick = Get(qb, "SourceStick"),
                facing = Get(qb, "Facing"), cameraHeading = Get(model, "SourceCameraHeading"),
                pendingTarget = Get(qb, "PendingThrowTarget"),
                primary = Get(Get(state, "Primary"), "ScriptSymbol"),
                secondary = Get(Get(state, "Secondary"), "ScriptSymbol"),
                events = game.SourceEvents.Select(e => new { type = e.Type.ToString(), e.SourceTick }).ToArray()
            };
        }
        try
        {
            foreach (var row in new (float Distance, bool Left, bool Moving)[] {
                (10f, true, true), (25f, true, false), (45f, true, false),
                (10f, false, true), (25f, false, false), (45f, false, false) })
            {
                var defaults = data.Configuration.Match;
                var configuration = new SourceMatchConfiguration(defaults.Profile, defaults.StationLayout,
                    defaults.HomeTeam, defaults.AwayTeam, defaults.Stations, TeamId.Away, defaults.Captain);
                GameSimulation game = KernelBinding.Create(data, matchConfiguration: configuration, newPlayerSession: true);
                var camera = new PreviewCamera(data, assets, game);
                object model = Model(game);
                var trace = new List<object>();
                lastTrace = trace;
                var inputs = new List<object>();
                var events = new List<SourceGameEvent>();
                bool recordRaw = false;
                void StepRaw(PlayerCommand command)
                {
                    long tick = game.SourceTick;
                    if (recordRaw)
                        inputs.Add(new { hostBefore = game.State.Tick, sourceBefore = tick, command.StationInputs });
                    game.Step(command);
                    if (game.SourceTick == tick) return;
                    events.AddRange(game.SourceEvents);
                    foreach (SourcePresentationCue cue in game.PresentationCues.Cues)
                        if (cue is SourceCameraCue or SourceShakerCue) camera.Cue(cue);
                    camera.ObserveHostStep(game.State);
                }
                void Wake(PlayerCommand command)
                {
                    long tick = game.SourceTick;
                    for (int host = 0; host < 3 && game.SourceTick == tick; host++) StepRaw(command);
                    Check(game.SourceTick == tick + 1, "Raw selector advances exactly one actual source wake.");
                }
                for (int host = 0; host < 5000 && !(game.State.Phase == GamePhase.PlaySelect &&
                    game.State.Possession == TeamId.Home); host++)
                {
                    if (host % 120 == 0) trace.Add(Frame(game));
                    StepRaw(game.State.Phase == GamePhase.DeadBall ? new PlayerCommand(action: InputAction.Continue) :
                        new PlayerCommand(new FieldVector(1f, 0f), true));
                }
                Check(game.State.Phase == GamePhase.PlaySelect && game.State.Possession == TeamId.Home,
                    "Public away kickoff/home return reaches actual home offensive selection.");
                for (int host = 0; host < 90 && game.State.Phase == GamePhase.PlaySelect; host++)
                    StepRaw(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                        new SourcePlaySelection("play_spider", SourcePlaybookKind.Offense))));
                Check(game.State.Phase == GamePhase.PreSnap, "Typed public play setup reaches actual pre-snap.");
                recordRaw = true;
                while ((int)Get(model, "PreSnapTicksRemaining") >=
                    SourceClock.DefaultAutoHikeTicks - SourceClock.SourceTicksPerSecond) Wake(Raw());
                Check((int)Get(model, "PreSnapTicksRemaining") > SourceClock.SourceTicksPerSecond,
                    "Actual raw snap is outside grace and still before the automatic hike deadline.");
                long snapPress = game.SourceTick;
                Wake(Raw(held: SourceButtons.B, pressed: SourceButtons.B));
                long snapRequest = game.SourceTick;
                Check((string)Get(Get(Get(Actor(model, 1), "AnimationState"), "Primary"), "ScriptSymbol") == "center_snap_anim",
                    "The raw B request selects the real centre animation, not an assigned programme.");
                Wake(Raw(released: SourceButtons.B));
                for (int host = 0; host < 120 && ((int)Get(model, "CarrierId") != 3 ||
                    game.State.Phase != GamePhase.LiveBall); host++) StepRaw(Raw());
                Check(game.State.Phase == GamePhase.LiveBall && (int)Get(model, "CarrierId") == 3,
                    "Raw snap reaches actual centre hike and QB catch.");
                SourceGameEvent[] snaps = events.Where(e => e.Type == SourceEventType.Snapped).ToArray();
                Check(snaps.Length == 1 && snaps[0].SourceTick > snapRequest && snaps[0].SourceTick <= game.SourceTick,
                    "Exactly one actual centre-produced snap precedes the real QB catch.");
                long caughtTick = game.SourceTick;
                object quarterback = Actor(model, 3);
                Check((int)Get(quarterback, "SourceControllerStation") == 0 &&
                    Get(quarterback, "SourceMode").ToString() == "Quarterback",
                    "The ordinary human station, not an injected drone/animation actor, owns the QB.");
                SourceVector3 qb = World(quarterback);
                for (int id = 7; id < 14; id++)
                {
                    Place(Actor(model, id), new SourceVector3(qb.X + 300f, 0f, (id - 10) * 35f));
                    Set(Actor(model, id), "SourceVelocity", default(FieldVector));
                }
                void PlaceReceivers()
                {
                    SourceVector3 from = World(quarterback);
                    for (int id = 4; id < 7; id++)
                    {
                        Place(Actor(model, id), new SourceVector3(from.X + row.Distance * SourceTiming.SourceUnitsPerYard,
                            0f, from.Z + (id - 5) * 20f));
                        Set(Actor(model, id), "SourceVelocity", default(FieldVector));
                    }
                }
                PlayerCommand Motion(SourceButtons held = SourceButtons.None, SourceButtons pressed = SourceButtons.None)
                {
                    int relative = ((row.Left ? 512 : 0) - (int)Get(model, "SourceCameraHeading")) & 1023;
                    int stick = data.Playbook.Directions.DirectionToStick(relative);
                    return Raw(3 - stick % 7, 3 - stick / 7, held, pressed);
                }
                PlaceReceivers();
                for (int i = 0; i < 6; i++) { Wake(Motion()); trace.Add(Frame(game)); }
                Check((bool)Get(quarterback, "Flipped") == row.Left,
                    "Ordinary raw QB movement computes the hand state without a PF-bit assignment.");
                if (!row.Moving)
                    for (int i = 0; i < 6; i++) { Wake(Raw()); trace.Add(Frame(game)); }
                PlaceReceivers();
                var declaredPlacement = Enumerable.Range(0, 14)
                    .Select(id => new { id, position = Vector(World(Actor(model, id))) }).ToArray();
                object state = Get(quarterback, "AnimationState");
                string? selectedScript = null, selectedPart = null;
                object? beforeRelease = null, afterRelease = null;
                var instructions = new List<object>();
                var restorations = new List<(FieldInfo Field, object Layer, Delegate Original)>();
                foreach (string part in new[] { "Primary", "Secondary" })
                {
                    object layer = Get(state, part);
                    FieldInfo before = layer.GetType().GetField("BeforeInstruction", Hidden)!;
                    Delegate original = (Delegate)before.GetValue(layer)!;
                    bool lastWasRelease = false;
                    Action<object> observe = actor =>
                    {
                        original.DynamicInvoke(actor);
                        int pc = (int)Get(layer, "pc") - 1;
                        object instruction = ((Array)Get(Get(layer, "program"), "Instructions")).GetValue(pc)!;
                        string script = (string)Get(layer, "ScriptSymbol");
                        string op = Get(instruction, "Op").ToString()!;
                        if (lastWasRelease)
                        {
                            var velocity = (FieldVector)Get(model, "SourceBallVelocity");
                            afterRelease = new {
                                sourceTick = game.SourceTick, script, part, carrier = Get(model, "CarrierId"),
                                origin = Vector((SourceVector3)Get(model, "SourceBallOrigin")),
                                velocity = new[] { velocity.X, (float)Get(model, "SourceBallVerticalVelocity"), velocity.Z },
                                target = Get(model, "TargetPlayerId"),
                                ballPosition = Vector((SourceVector3)Get(model, "SourceBallPosition"))
                            };
                            lastWasRelease = false;
                        }
                        if (op == "Callback" && (int)Get(instruction, "A") == 1)
                        {
                            selectedScript = script; selectedPart = part; lastWasRelease = true;
                            beforeRelease = new {
                                sourceTick = game.SourceTick, script, part, pc, sourceFrame = Get(layer, "FrameOrdinal"),
                                flipped = Get(quarterback, "Flipped"), carrier = Get(model, "CarrierId"),
                                pendingTarget = Get(quarterback, "PendingThrowTarget"), position = Vector(World(quarterback)),
                                randomDraws = Get(model, "SourceRandomDraws")
                            };
                        }
                        if (instructions.Count < 1000)
                            instructions.Add(new { sourceTick = game.SourceTick, script, part, pc, op });
                    };
                    var parameter = System.Linq.Expressions.Expression.Parameter(quarterback.GetType(), "actor");
                    var body = System.Linq.Expressions.Expression.Invoke(
                        System.Linq.Expressions.Expression.Constant(observe),
                        System.Linq.Expressions.Expression.Convert(parameter, typeof(object)));
                    before.SetValue(layer, System.Linq.Expressions.Expression.Lambda(before.FieldType, body, parameter).Compile());
                    restorations.Add((before, layer, original));
                }
                long throwPress = game.SourceTick;
                try
                {
                    Wake(row.Moving ? Motion(SourceButtons.B, SourceButtons.B) :
                        Raw(held: SourceButtons.B, pressed: SourceButtons.B));
                    Check((int)Get(quarterback, "PendingThrowTarget") >= 0,
                        "The raw B press reaches the ordinary human target/throw selector.");
                    Wake(row.Moving ? Motion() : Raw(released: SourceButtons.B));
                    for (int host = 0; host < 120 && afterRelease == null; host++)
                    {
                        StepRaw(row.Moving ? Motion() : Raw());
                        trace.Add(Frame(game));
                    }
                }
                finally
                {
                    foreach (var restore in restorations) restore.Field.SetValue(restore.Layer, restore.Original);
                }
                Check(beforeRelease != null && afterRelease != null,
                    "The raw human throw selector reaches its real animation callback and ball release.");
                string expected = row.Moving ? row.Left ? "qb_rthrow_sl_anim" : "qb_rthrow_sr_anim" :
                    row.Distance >= 35 ? row.Left ? "qb_throw_ll_anim" : "qb_throw_lr_anim" :
                    row.Left ? "qb_throw_sl_anim" : "qb_throw_sr_anim";
                Check(selectedScript == expected, "The observed programme must match original distance/hand selection.");
                Check(selectedPart == (row.Moving ? "Secondary" : "Primary"),
                    "Actual running throws use the upper layer; planted throws use the full-body layer.");
                Check(events.Any(e => e.Type == SourceEventType.PassReleased && e.SourceTick > throwPress),
                    "The actual public pass-release event follows the raw throw press.");
                results.Add(new {
                    distance = row.Distance, left = row.Left, moving = row.Moving, expected,
                    actualScript = selectedScript, actualLayer = selectedPart,
                    snapPress, snapRequest, actualSnap = snaps[0].SourceTick, caughtTick, throwPress,
                    beforeRelease, afterRelease, declaredPlacement, trace, actualInputs = inputs,
                    actualEvents = events.Select(e => new { type = e.Type.ToString(), e.SourceTick }).ToArray(),
                    instructionTrace = instructions,
                    setup = "Public away kickoff/home return; typed public play choice is not raw menu proof. Declared post-catch receiver/defender positions; actual raw QB motion computes PF state. No programme/PF/release-tick assignment."
                });
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                schema = "actual-raw-six-qb-selector-observations-20261111/v1",
                data.Catalog.ManifestSha256, profile = data.Catalog.Profile.StableId, assertions, cases = results
            }));
            Console.WriteLine("PASS actual raw QB selector cases=" + results.Count + "; assertions=" + assertions);
            return 0;
        }
        catch (Exception error)
        {
            if (!File.Exists(output))
                File.WriteAllText(output, JsonSerializer.Serialize(new {
                    schema = "actual-raw-six-qb-selector-diagnostic-20261111/v1",
                    status = "failed", assertions, cases = results, lastTrace, error = error.ToString()
                }));
            throw;
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length is 2 or 3, "Usage: DATA1153 NEW-OBSERVATIONS.json [--left-release-diagnostic]");
            string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            Check(!File.Exists(output), "Choose a create-new Nov11 observation file.");
            if (args.Length == 3 && args[2] == "--left-releases")
                return LeftThrowChecks.Run(new[] { root, output });
            if (args.Length == 3 && args[2] == "--camera-cache")
                return CameraReleaseChecks.Run(new[] { root, output });
            var catalog = new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
                path => File.ReadAllBytes(Path.Combine(root, path)));
            var data = new PlayerData(catalog);
            if (args.Length == 3 && args[2] == "--raw-throw-selectors")
                return VerifyRawThrowSelectors(data, output);
            if (args.Length == 3)
            {
                Check(args[2] == "--left-release-diagnostic", "Unknown opcode selector.");
                LeftReleaseCases(data);
            }
            else { Components(data); Policies(data); ScheduledCases(data); }
            object directions = Get(Actor(Model(KernelBinding.Create(data)), 3), "SourceDirections");
            float[] sine = Enumerable.Range(0, 257).Select(n => (float)Call(directions, "Sin", n)!).ToArray();
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                schema = "actual-original-animation-opcode-observations/v1",
                manifestSha256 = data.Catalog.ManifestSha256, profile = data.Catalog.Profile.StableId,
                assertions, quarterSine = sine, cases, policyChecks,
                clips = clips.Order().Select(name => {
                    ISourceAnimationClip clip = data.Animations.GetClip(name);
                    return new { name, flags = clip.SourceFlags, clip.SourcePath, clip.SourceGitBlobSha1,
                        clip.DecodedSha256, selectedSourceFrames = clip.SelectedSourceFrames,
                        frames = clip.SelectedSourceFrames.Select(frame => {
                            SourceAnimationFrame value = clip.GetFrame(frame);
                            return new { root = new[] { value.ScaledRoot.X, value.ScaledRoot.Y, value.ScaledRoot.Z }, pose = Pose(value.Pose) };
                        })
                    };
                })
            }));
            Console.WriteLine("EXPORTED finite opcode components=" + cases.Count + "; original-C comparison still required.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

static class LeftThrowChecks
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static int assertions;
    static readonly List<object> failures = new(), cases = new();
    static object? A1(string method, params object?[] args) =>
        typeof(AnimationOpcodeChecks).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
    static object Get(object value, string name) => A1("Get", value, name)!;
    static void Set(object value, string name, object? field) => A1("Set", value, name, field);
    static object? Call(object value, string method, params object?[] args) => A1("Call", value, method, args);
    static object Model(GameSimulation game) => A1("Model", game)!;
    static object Actor(object model, int id) => A1("Actor", model, id)!;
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static void Check(bool condition, string name, object? expected = null, object? actual = null)
    {
        assertions++;
        if (!condition) failures.Add(new { name, expected, actual });
    }
    static float[] V(SourceVector3 value) => new[] { value.X, value.Y, value.Z };
    static object Ball(object model)
    {
        var velocity = (FieldVector)Get(model, "SourceBallVelocity");
        return new {
            origin = V((SourceVector3)Get(model, "SourceBallOrigin")),
            position = V((SourceVector3)Get(model, "SourceBallPosition")),
            velocity = new[] { velocity.X, (float)Get(model, "SourceBallVerticalVelocity"), velocity.Z },
            target = V((SourceVector3)Get(model, "SourceBallTarget")),
            elapsed = Get(model, "PassTicksElapsed"), total = Get(model, "PassTicksTotal"),
            carrier = Get(model, "CarrierId"), thrower = Get(model, "ThrowerId"),
            receiver = Get(model, "TargetPlayerId"), flags = Convert.ToInt32(Get(model, "SourceBallFlags"))
        };
    }
    static int Flags(GameSimulation game, object actor) =>
        (int)(SourceMovementFlags)Get(Call(game, "CaptureSourceMovement", actor)!, "Flags");
    static float[][] Pose(object layer) => ((SourceJointPose)Call(layer, "EvaluatePose")!).Rotations
        .Select(q => new[] { q.X, q.Y, q.Z, q.W }).ToArray();
    static SourceVector3 WorldJoint(GameSimulation game, object actor, int joint) =>
        (SourceVector3)Call(game, "SourceWorldJoint", actor, joint)!;
    static object World(object actor)
    {
        Type type = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!;
        return V((SourceVector3)type.GetMethod("WorldPosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new[] { actor })!);
    }

    sealed class Sink : IRenderSink, ISourceAudioSink, ISourceWorldRenderSink
    {
        internal double[] Joints = Array.Empty<double>(), BallPacket = Array.Empty<double>();
        internal long Presents;
        internal readonly Dictionary<string, object> Samples = new();
        internal readonly List<JsonElement> Commands = new();
        public void WorldPresentation(SourceWorldRenderPacket packet) { }
        public void Texture(string id, int w, int h, byte[] rgba, int mips, bool repeat, bool linear, string sha, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternate, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] alpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hud, int[] colors, string[] textures, int[] counts, string status)
        {
            Require(joints.Length == 3360 && models.Length == 14 && appearances.Length == 14 && ball.Length == 20,
                "The real programme consumer must submit fourteen original poses and the original free-ball matrix.");
            Joints = joints.ToArray(); BallPacket = ball.ToArray(); Presents++;
        }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata)
        {
            Require(id == "pcm16-" + DataCatalog.Hash(pcm) && rate == 31250 && channels == 1,
                "Actual release sound must retain genuine PCM bytes, never a placeholder.");
            Samples[id] = new { id, rate, channels, bytes = pcm.Length, sha256 = DataCatalog.Hash(pcm) };
        }
        public void AudioCommands(string text)
        {
            using var json = JsonDocument.Parse(text);
            Commands.AddRange(json.RootElement.GetProperty("commands").EnumerateArray().Select(c => c.Clone()));
        }
    }
    sealed class Probe
    {
        internal PlayerHost Host = null!;
        internal object Actor = null!, Layer = null!;
        internal Delegate Original = null!;
        internal string Script = "";
        internal int ReleaseIndex;
        internal bool SawRelease, SawClear, LastWasRelease, LastWasClear;
        internal object? BeforeRelease, AfterRelease, AfterClear, BeforeSet;
        internal readonly List<object> Order = new();
        internal long ReleaseTick;
        internal void Observe<T>(T actor)
        {
            Original.DynamicInvoke(actor);
            if ((string)Get(Layer, "ScriptSymbol") != Script) return;
            object model = Model(Host.Game);
            int pc = (int)Get(Layer, "pc") - 1;
            object instruction = ((Array)Get(Get(Layer, "program"), "Instructions")).GetValue(pc)!;
            string op = Get(instruction, "Op").ToString()!;
            var pose = Pose(Layer);
            object State(string phase) => new {
                phase, sourceTick = Host.Game.SourceTick, pc, flags = Flags(Host.Game, Actor),
                sourceFrame = Get(Layer, "FrameOrdinal"), pose, ball = Ball(model),
                randomDraws = Get(model, "SourceRandomDraws")
            };
            if (LastWasRelease)
            {
                AfterRelease = State("after-real-release-before-next-opcode");
                Order.Add(AfterRelease); LastWasRelease = false;
            }
            if (LastWasClear)
            {
                AfterClear = State("after-original-clear");
                Order.Add(AfterClear); LastWasClear = false;
            }
            if (op == "SetFlip" && (int)Get(instruction, "A") == 1)
            {
                BeforeSet = State("before-original-set");
                Order.Add(BeforeSet);
            }
            if (pc == ReleaseIndex)
            {
                Require(op == "Callback" && (int)Get(instruction, "A") == 1,
                    "Only the actual compiled qb_release_ball callback is observed.");
                SawRelease = LastWasRelease = true; ReleaseTick = Host.Game.SourceTick;
                object receiver = LeftThrowChecks.Actor(model, 4);
                var velocity = (FieldVector)Get(receiver, "SourceVelocity");
                BeforeRelease = new {
                    phase = "before-real-release", sourceTick = ReleaseTick, pc,
                    flags = Flags(Host.Game, Actor), sourceFrame = Get(Layer, "FrameOrdinal"), pose,
                    leftWrist = V(WorldJoint(Host.Game, Actor, 7)),
                    rightWrist = V(WorldJoint(Host.Game, Actor, 4)),
                    priorCacheTick = Get(model, "SourceCameraJointTick"),
                    currentCameraTick = Get(model, "SourceMovementCameraTick"),
                    cameraPosition = V(((SourceMovementCameraFrame)Get(model, "SourceMovementCamera")).Position),
                    cameraMatrix = ((SourceMovementCameraFrame)Get(model, "SourceMovementCamera")).Transform.ToArray(),
                    cameraJoints = Enumerable.Range(0, 15).Select(j =>
                        V(((SourceVector3[,])Get(model, "SourceCameraJoints"))[(int)Get(Actor, "Id"), j])).ToArray(),
                    receiver = new { position = World(receiver),
                        velocity = new[] { velocity.X, (float)Get(receiver, "SourceVerticalVelocity"), velocity.Z },
                        station = Get(receiver, "SourceControllerStation") },
                    ball = Ball(model), randomDraws = Get(model, "SourceRandomDraws")
                };
                Order.Add(BeforeRelease);
            }
            if (SawRelease && op == "SetFlip" && (int)Get(instruction, "A") == 0)
            {
                SawClear = LastWasClear = true;
                Order.Add(State("before-original-clear"));
            }
        }
    }

    static void Case(PlayerData data, string script, bool left, int expectedTick)
    {
        var sink = new Sink();
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        var host = new PlayerHost(data, sink, observeRendering: true,
            options: PreviewOptions.FromUrl("", data.Catalog.Profile.StableId), recordStore: store);
        A1("StartGuest", host); A1("Step", host);
        object model = Model(host.Game);
        Set(model, "SourceRules", new SourceRulesState(0, 0, 2, 40, 70, SourcePlayType.Normal));
        Set(model, "SourceOffensePlay", data.Playbook.GetPlay("play_spider"));
        Set(model, "SourceDefensePlay", data.Playbook.GetPlay("play_safe_cover"));
        Set(model, "SourceFlags", SourceGameFlags.None); Set(model, "SourceKickApproach", false);
        Set(model, "Phase", GamePhase.LiveBall); Set(model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        Set(model, "ClockActive", false); Set(model, "BallKind", SourceBallKind.Pass);
        Set(model, "BallMode", BallMode.Held); Set(model, "CarrierId", 3);
        Set(model, "SourceBallFlags", Enum.ToObject(Get(model, "SourceBallFlags").GetType(), 0));
        foreach (object actor in (Array)Get(model, "Players"))
        {
            Set(actor, "SourceMode", SourceActorMode.Dead); Set(actor, "NoCatchTicks", 200);
            Set(actor, "SourceJob", Enum.ToObject(Get(actor, "SourceJob").GetType(), 0));
        }
        var primed = (ITuple)A1("Prime", data, host.Game, script, "Callback", 0, null, 7, 0x20002u)!;
        object selected = primed[0]!, layer = primed[1]!;
        Set(selected, "PendingThrowTarget", 4); Set(selected, "SourceVelocity", default(FieldVector));
        Set(selected, "SourceVerticalVelocity", 0f);
        Set(layer, "pc", 0); Set(layer, "hold", 1);
        var probe = new Probe { Host = host, Actor = selected, Layer = layer, Script = script,
            ReleaseIndex = (int)primed[3]! };
        FieldInfo before = layer.GetType().GetField("BeforeInstruction", Hidden)!;
        probe.Original = (Delegate)before.GetValue(layer)!;
        before.SetValue(layer, typeof(Probe).GetMethod("Observe", Hidden)!.MakeGenericMethod(selected.GetType())
            .CreateDelegate(before.FieldType, probe));
        int commandsBefore = sink.Commands.Count;
        try
        {
            for (int tick = 0; tick < 60 && probe.AfterRelease == null; tick++) A1("Step", host);
        }
        finally { before.SetValue(layer, probe.Original); }
        Require(probe.BeforeRelease != null && probe.AfterRelease != null,
            "The complete actual programme must reach its actual public release callback: " + script);
        Check(probe.ReleaseTick == expectedTick, script + "/source-release-tick", expectedTick, probe.ReleaseTick);
        var trajectory = new List<object>();
        void Frame()
        {
            var ball = (SourceVector3)Get(model, "SourceBallPosition");
            Check(sink.BallPacket[1] == host.Game.SourceTick && sink.BallPacket[0] == 1,
                script + "/actual-free-ball-consumer-current", host.Game.SourceTick, sink.BallPacket[1]);
            Check(sink.BallPacket[16] == ball.X && sink.BallPacket[17] == ball.Y && sink.BallPacket[18] == ball.Z,
                script + "/actual-free-ball-matrix-position", V(ball), sink.BallPacket.Skip(16).Take(3).ToArray());
            Check(host.LatestRenderObservation?.CurrentSourceTick == host.Game.SourceTick,
                script + "/actual-pose-render-current");
            trajectory.Add(new { sourceTick = host.Game.SourceTick, ball = Ball(model),
                freeBallPacket = sink.BallPacket.ToArray(), poseMatrices = sink.Joints.ToArray() });
        }
        Frame();
        for (int i = 0; i < 4; i++) { A1("Step", host); Frame(); }
        var plays = sink.Commands.Skip(commandsBefore).Where(command =>
            command.GetProperty("kind").GetString() == "play").ToArray();
        var delivered = host.ProcessSoundDeliveries.Where(call => call.SourceTick == probe.ReleaseTick).ToArray();
        Check(plays.Length > 0 && plays.All(command => sink.Samples.ContainsKey(command.GetProperty("sampleId").GetString()!)),
            script + "/actual-release-to-original-pcm");
        Check(delivered.Length == 1 && plays.Any(command =>
            command.GetProperty("outputSequence").GetInt64() == delivered[0].OutputSequence),
            script + "/same-actual-release-output-to-pcm-transport");
        Check(!left || probe.SawClear && probe.AfterClear != null,
            script + "/actual-source-clear-continuation", true, probe.SawClear);
        Check(!(bool)Get(selected, "Flipped"), script + "/restored-post-release-flag", false, Get(selected, "Flipped"));
        cases.Add(new {
            script, left, expectedTick, probe.ReleaseTick, probe.BeforeSet, probe.BeforeRelease,
            probe.AfterRelease, probe.AfterClear, probe.SawClear, order = probe.Order,
            trajectory, plays, samples = sink.Samples, sink.Presents,
            processSounds = delivered,
            scope = "Complete original programme-entry and actor-placement fixture, actual public source steps/callback/ball/pose/audio consumers; not a natural physical throw-selector claim."
        });
    }
    public static int Run(string[] args)
    {
        try
        {
            Require(args.Length == 2 && !File.Exists(args[1]), "Usage: DATA1153 NEW-LEFT-THROW-OBSERVATIONS.json");
            string root = Path.GetFullPath(args[0]);
            var catalog = new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
                path => File.ReadAllBytes(Path.Combine(root, path)));
            var data = new PlayerData(catalog);
            foreach (var item in new[] {
                ("qb_rthrow_sl_anim", true, 9), ("qb_throw_sl_anim", true, 12), ("qb_throw_ll_anim", true, 16),
                ("qb_rthrow_sr_anim", false, 9), ("qb_throw_sr_anim", false, 12), ("qb_throw_lr_anim", false, 16) })
                Case(data, item.Item1, item.Item2, item.Item3);
            object directions = Get(Actor(Model(KernelBinding.Create(data)), 3), "SourceDirections");
            float[] quarterSine = Enumerable.Range(0, 257).Select(n => (float)Call(directions, "Sin", n)!).ToArray();
            File.WriteAllText(args[1], JsonSerializer.Serialize(new {
                schema = "actual-public-left-throw-release-observations-20261111/v1",
                data.Catalog.ManifestSha256, profile = data.Catalog.Profile.StableId, assertions, failures, cases, quarterSine
            }));
            Console.WriteLine("EXPORTED actual left/right release cases=" + cases.Count + " assertions=" + assertions +
                " failures=" + failures.Count + "; original-C comparison still required.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

static class CameraReleaseChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int assertions;
    static readonly List<object> cases = new();
    static readonly List<object> negativeChecks = new(), legacyChecks = new();
    static object? A1(string method, params object?[] args)
    {
        try { return typeof(AnimationOpcodeChecks).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args); }
        catch(TargetInvocationException e) when(e.InnerException!=null) { throw e.InnerException; }
    }
    static object Get(object value, string name) => A1("Get", value, name)!;
    static object? Call(object value, string name, params object?[] args) => A1("Call", value, name, args);
    static void Set(object value, string name, object? field) => A1("Set", value, name, field);
    static object Model(GameSimulation game) => A1("Model", game)!;
    static object Actor(object model, int id) => A1("Actor", model, id)!;
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); assertions++; }
    static float[] V(SourceVector3 value) => new[] { value.X, value.Y, value.Z };
    static float[][] Q(object layer) => ((SourceJointPose)Call(layer, "EvaluatePose")!).Rotations
        .Select(q => new[] { q.X, q.Y, q.Z, q.W }).ToArray();
    static object Ball(object model)
    {
        var velocity = (FieldVector)Get(model, "SourceBallVelocity");
        return new {
            origin = V((SourceVector3)Get(model, "SourceBallOrigin")),
            position = V((SourceVector3)Get(model, "SourceBallPosition")),
            velocity = new[] { velocity.X, (float)Get(model, "SourceBallVerticalVelocity"), velocity.Z },
            target = V((SourceVector3)Get(model, "SourceBallTarget")),
            elapsed = Get(model, "PassTicksElapsed"), total = Get(model, "PassTicksTotal"),
            flags = Convert.ToInt32(Get(model, "SourceBallFlags")), carrier = Get(model, "CarrierId")
        };
    }
    static object Camera(GameSimulation game, PreviewCamera camera)
    {
        object model = Model(game), current = Get(model, "SourceMovementCamera");
        SourceCinematicSeed seed = camera.Playback.SourceState;
        return new {
            tick = game.SourceTick, cameraTick = Get(model, "SourceMovementCameraTick"),
            eye = V(seed.Eye), focus = V(seed.Focus), theta = seed.Theta, phi = seed.Phi,
            mode = camera.Playback.RawMode, table = camera.Playback.Frame.Table,
            position = V((SourceVector3)Get(current, "Position")),
            matrix = ((IEnumerable<float>)Get(current, "Transform")).ToArray()
        };
    }
    sealed class OtherLibrary : ISourceAnimationLibrary
    {
        readonly ISourceAnimationLibrary actual;
        internal OtherLibrary(ISourceAnimationLibrary value){actual=value;}
        public ISourceAnimationClip GetClip(string name)=>actual.GetClip(name);
    }
    static PlayerView View(GameSimulation game,object actor)
    {
        Type type=typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.GameSnapshot")!;
        return (PlayerView)type.GetMethods(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
            .Single(m=>m.Name=="SourcePlayerView"&&m.GetParameters().Length==3).Invoke(null,new object[]{actor,game.SourceTick,true})!;
    }
    static SourceVector3 LegacyWorldJoint(ISourceAnimationLibrary library,
        IReadOnlyList<SourceVector3> pivots, PlayerView actor, int joint)
    {
        SourceVector3 point = SourceActorPoseSampler.Evaluate(library, pivots, actor, actor, 1f).Joints[joint].Position;
        return new SourceVector3(SourceMatchRules.SourceWorldFromAbsoluteYards(actor.Position.X) + point.X,
            actor.Height * SourceTiming.SourceUnitsPerYard + point.Y,
            actor.Position.Z * SourceTiming.SourceUnitsPerYard - point.Z);
    }

    // Independent reference for the unchanged pre-cache-fix fallback arithmetic.
    static IReadOnlyList<SourceVector3> LegacyCameraJoints(ISourceAnimationLibrary library,
        IReadOnlyList<SourceVector3> pivots, PlayerView actor, SourceVector3 root, SourceMovementCameraFrame camera)
    {
        SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(library, pivots, actor, actor, 1f);
        var m = camera.Transform;
        float rx = root.X - camera.Position.X, ry = root.Y - camera.Position.Y, rz = root.Z - camera.Position.Z;
        float x = m[0] * rx + m[1] * ry + m[2] * rz + m[3];
        float y = m[4] * rx + m[5] * ry + m[6] * rz + m[7];
        float z = m[8] * rx + m[9] * ry + m[10] * rz + m[11];
        var joints = new SourceVector3[SourceTiming.JointCount];
        for (int i = 0; i < joints.Length; i++)
        {
            SourceVector3 local = pose.Joints[i].Position;
            joints[i] = new SourceVector3(
                m[0] * local.X + m[1] * local.Y - m[2] * local.Z + x,
                m[4] * local.X + m[5] * local.Y - m[6] * local.Z + y,
                m[8] * local.X + m[9] * local.Y - m[10] * local.Z + z);
        }
        return joints;
    }

    static void Legacy(PlayerData data,GameSimulation game,int id)
    {
        object model=Model(game),actor=Actor(model,id);PlayerView view=View(game,actor);
        SourceVector3 root=game.SourceActorWorldPosition(id);
        SourceMovementCameraFrame frame=(SourceMovementCameraFrame)Get(model,"SourceMovementCamera");
        var current=new SourceCatchGeometry(data.Animations,data.ReferencePivots);
        for(int j=0;j<15;j++)Check(V(LegacyWorldJoint(data.Animations,data.ReferencePivots,view,j)).Select(BitConverter.SingleToInt32Bits)
            .SequenceEqual(V(current.WorldJoint(view,j)).Select(BitConverter.SingleToInt32Bits)),"World-joint method remains unchanged.");
        var proxy=new OtherLibrary(data.Animations);
        var legacyAnimation=new ActorAnimationView(game.SourceTick,ActorAction.Idle,AnimationSelectionEvidence.SourceVerified,
            new SourceAnimationLayer("legacy-normalized-component",1,view.Animation.Primary.Sample),view.Animation.RootOffsetSourceUnits);
        var legacyView=new PlayerView(view.Id,view.Team,view.Position,view.Velocity,view.Turbo01,view.Height,
            view.FacingDirection,view.Role,legacyAnimation,view.ModelScale);
        foreach(var pair in new[]{
            (Name:"LegacyNormalized",Library:(ISourceAnimationLibrary)data.Animations,Actor:legacyView),
            (Name:"other-library-fallback",Library:(ISourceAnimationLibrary)proxy,Actor:view)})
        {
            var before=LegacyCameraJoints(pair.Library,data.ReferencePivots,pair.Actor,root,frame);
            var after=new SourceCatchGeometry(pair.Library,data.ReferencePivots).CameraJoints(pair.Actor,root,frame);
            for(int j=0;j<15;j++)Check(V(before[j]).Select(BitConverter.SingleToInt32Bits)
                .SequenceEqual(V(after[j]).Select(BitConverter.SingleToInt32Bits)),"Old camera geometry policy remains bit-identical: "+pair.Name);
            legacyChecks.Add(new{kind=pair.Name,actor=id,sourceTick=game.SourceTick,joints=15,bitExact=true,profileRelabeled=false});
        }
    }
    static object CacheInput(GameSimulation game, PreviewCamera camera, int id)
    {
        object model = Model(game), actor = Actor(model, id), state = Get(actor, "AnimationState");
        object primary = Get(state, "Primary"), secondary = Get(state, "Secondary");
        var geometry = (ISourceActorCameraGeometry)Get(game, "sourceGeometry");
        Type snapshots = typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.GameSnapshot")!;
        PlayerView view = (PlayerView)snapshots.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(m => m.Name == "SourcePlayerView" && m.GetParameters().Length == 3)
            .Invoke(null, new object[] { actor, game.SourceTick, true })!;
        SourceVector3 root = game.SourceActorWorldPosition(id);
        SourceMovementCameraFrame frame = (SourceMovementCameraFrame)Get(model, "SourceMovementCamera");
        IReadOnlyList<SourceVector3> reconstructed = geometry.CameraJoints(view, root, frame);
        var cache = (SourceVector3[,])Get(model, "SourceCameraJoints");
        for (int j = 0; j < 15; j++)
            Check(BitConverter.SingleToInt32Bits(reconstructed[j].X) == BitConverter.SingleToInt32Bits(cache[id,j].X) &&
                BitConverter.SingleToInt32Bits(reconstructed[j].Y) == BitConverter.SingleToInt32Bits(cache[id,j].Y) &&
                BitConverter.SingleToInt32Bits(reconstructed[j].Z) == BitConverter.SingleToInt32Bits(cache[id,j].Z),
                "End-of-wake primitive pose reconstruction must exactly reproduce the actual stored camera cache.");
        return new {
            tick = game.SourceTick, cacheTick = Get(model, "SourceCameraJointTick"), camera = Camera(game, camera),
            actor = new {
                id, root = V(root), animationRoot = V((SourceVector3)Get(actor, "SourceRoot")),
                scale = Get(actor, "ModelScale"), facing = Get(actor, "Facing"), upperFacing = Get(actor, "UpperFacing"),
                primaryMode = Get(primary, "Mode"), secondaryMode = Get(secondary, "Mode"), twoPart = Get(state, "TwoPart"),
                primary = Q(primary), secondary = (bool)Get(state, "TwoPart") ? Q(secondary) : null,
                sequence = Get(primary, "SequenceName"), frame = Get(primary, "FrameOrdinal")
            },
            actualCameraJoints = Enumerable.Range(0,15).Select(j => V(cache[id,j])).ToArray(),
            exactActualProducerInputReconstruction = true
        };
    }
    sealed class Probe
    {
        internal GameSimulation Game = null!;
        internal PreviewCamera Camera = null!;
        internal object Actor = null!, Layer = null!;
        internal Delegate Original = null!;
        internal Dictionary<long, object> CacheFrames = null!;
        internal List<SourceProcessSoundCall> Sounds = null!;
        internal object? BeforeRelease, AfterRelease;
        internal bool LastRelease;
        internal void Before<T>(T actor)
        {
            Original.DynamicInvoke(actor);
            object model = Model(Game);
            if (LastRelease)
            {
                AfterRelease = new { tick = Game.SourceTick, ball = Ball(model),
                    randomDraws = Get(model,"SourceRandomDraws"), pose = Q(Layer), sounds = Sounds.Count };
                LastRelease = false;
            }
            int pc = (int)Get(Layer, "pc") - 1;
            object instruction = ((Array)Get(Get(Layer, "program"), "Instructions")).GetValue(pc)!;
            if (Get(instruction, "Op").ToString() != "Callback" || (int)Get(instruction, "A") != 1) return;
            int id = (int)Get(Actor, "Id"), target = (int)Get(Actor, "PendingThrowTarget");
            object receiver = CameraReleaseChecks.Actor(model, target);
            var velocity = (FieldVector)Get(receiver, "SourceVelocity");
            var cache = (SourceVector3[,])Get(model, "SourceCameraJoints");
            long cacheTick = (long)Get(model, "SourceCameraJointTick");
            Check(cacheTick == Game.SourceTick - 1 && CacheFrames.ContainsKey(cacheTick),
                "Actual release must retain the preceding post-player cache, not a fresh world-wrist substitute.");
            int wrist=(bool)Get(Actor,"Flipped")?7:4;
            object savedCamera=Get(model,"SourceMovementCamera");
            long savedCameraTick=(long)Get(model,"SourceMovementCameraTick");
            SourceVector3 savedPoint=cache[id,wrist];
            string beforeBall=JsonSerializer.Serialize(Ball(model));long beforeRandom=(long)Get(model,"SourceRandomDraws");
            int beforeSounds=Sounds.Count;
            foreach(string kind in new[]{"missing-cache","stale-cache","future-cache","missing-camera","stale-camera","nan-wrist","infinite-wrist"})
            {
                string? error=null;
                try
                {
                    if(kind=="missing-cache")Set(model,"SourceCameraJointTick",-1L);
                    if(kind=="stale-cache")Set(model,"SourceCameraJointTick",cacheTick-1);
                    if(kind=="future-cache")Set(model,"SourceCameraJointTick",cacheTick+1);
                    if(kind=="missing-camera")Set(model,"SourceMovementCamera",null);
                    if(kind=="stale-camera")Set(model,"SourceMovementCameraTick",savedCameraTick-1);
                    if(kind=="nan-wrist")cache[id,wrist]=new SourceVector3(float.NaN,savedPoint.Y,savedPoint.Z);
                    if(kind=="infinite-wrist")cache[id,wrist]=new SourceVector3(savedPoint.X,float.PositiveInfinity,savedPoint.Z);
                    try{Call(Game,"ReleaseSourcePass",id);}
                    catch(InvalidOperationException failure){error=failure.Message;}
                }
                finally
                {
                    Set(model,"SourceCameraJointTick",cacheTick);Set(model,"SourceMovementCamera",savedCamera);
                    Set(model,"SourceMovementCameraTick",savedCameraTick);cache[id,wrist]=savedPoint;
                }
                Check(error!=null&&error.StartsWith("Original pass release requires",StringComparison.Ordinal),"Actual cache guard must reject "+kind);
                Check(JsonSerializer.Serialize(Ball(model))==beforeBall&&(long)Get(model,"SourceRandomDraws")==beforeRandom&&Sounds.Count==beforeSounds,
                    "Rejected cache cannot mutate the ball, emit sound or draw RNG: "+kind);
                negativeChecks.Add(new{kind,actor=id,tick=Game.SourceTick,error,ballUnchanged=true,rngDraws=0,soundCalls=0});
            }
            BeforeRelease = new {
                tick = Game.SourceTick, process = Get(model, "SourceRandomProcess"),
                processOrder = Get(model,"SourceRandomProcessOrder"), processActor = Get(model,"SourceRandomActor"),
                randomCallIndex = Get(model,"SourceRandomCallIndex"), randomDraws = Get(model,"SourceRandomDraws"), actor = id,
                script = Get(Layer, "ScriptSymbol"), frame = Get(Layer, "FrameOrdinal"),
                currentPose = Q(Layer),
                flipped = Get(Actor, "Flipped"), currentCamera = CameraReleaseChecks.Camera(Game, Camera),
                previousCache = CacheFrames[cacheTick],
                actualStoredCameraJoints = Enumerable.Range(0,15).Select(j => V(cache[id,j])).ToArray(),
                receiver = new { target, position = V(Game.SourceActorWorldPosition(target)),
                    velocity = new[] { velocity.X, (float)Get(receiver, "SourceVerticalVelocity"), velocity.Z },
                    station = Get(receiver, "SourceControllerStation") },
                managedFreshWorldWristDIAGNOSTICONLY = V((SourceVector3)Call(Game, "SourceWorldJoint", Actor,
                    (bool)Get(Actor, "Flipped") ? 7 : 4)!)
            };
            LastRelease = true;
        }
    }
    static void Case(PlayerData data, RenderAssets assets, TeamId side)
    {
        int offset = 7 * (int)side, qbId = offset + 3;
        int qbStation = side == TeamId.Home ? 0 : 3, receiverStation = side == TeamId.Home ? 1 : 2;
        var d = data.Configuration.Match;
        var configuration = new SourceMatchConfiguration(d.Profile, d.StationLayout, d.HomeTeam, d.AwayTeam,
            new[] { new SourceStationConfiguration(qbStation, side), new SourceStationConfiguration(receiverStation, side) },
            side == TeamId.Home ? TeamId.Away : TeamId.Home, d.Captain);
        GameSimulation game = KernelBinding.Create(data, matchConfiguration: configuration, newPlayerSession: true);
        var camera = new PreviewCamera(data, assets, game);
        object model = Model(game);
        var sounds=new List<SourceProcessSoundCall>();game.SourceProcessSoundRaised+=sounds.Add;
        var cacheFrames = new Dictionary<long, object>();
        var transitions = new List<object>();
        object? previousCamera = null;
        int previousMode = int.MinValue;
        bool capture = false;
        PlayerCommand Raw(int x = 0, int z = 0, SourceButtons held = SourceButtons.None,
            SourceButtons pressed = SourceButtons.None, SourceButtons released = SourceButtons.None,
            SourceButtons partner = SourceButtons.None) =>
            new(stationInputs: new SourceInputFrame(new[] {
                new SourceStationInput(qbStation,x,z,held,pressed,released), new SourceStationInput(receiverStation,0,0,partner,partner)
            }));
        void Step(PlayerCommand command)
        {
            long old = game.SourceTick; game.Step(command);
            if (game.SourceTick == old) return;
            foreach (SourcePresentationCue cue in game.PresentationCues.Cues)
                if (cue is SourceCameraCue or SourceShakerCue) camera.Cue(cue);
            camera.ObserveHostStep(game.State);
            if ((long)Get(model, "SourceMovementCameraTick") == game.SourceTick)
            {
                object current = Camera(game, camera);
                if (camera.Playback.RawMode != previousMode)
                {
                    if (previousCamera != null) transitions.Add(previousCamera);
                    transitions.Add(current); previousMode = camera.Playback.RawMode;
                }
                previousCamera = current;
                if (capture) cacheFrames[game.SourceTick] = CacheInput(game, camera, qbId);
            }
        }
        void Wake(PlayerCommand command)
        {
            long old = game.SourceTick;
            for (int h = 0; h < 3 && game.SourceTick == old; h++) Step(command);
            Check(game.SourceTick == old + 1, "The public cache fixture advances exactly one real wake.");
        }
        for (int h = 0; h < 6000 && !(game.State.Phase == GamePhase.PlaySelect && game.State.Possession == side); h++)
            Step(game.State.Phase == GamePhase.DeadBall ? Raw(held:SourceButtons.B,pressed:SourceButtons.B,partner:SourceButtons.B) :
                new PlayerCommand(new FieldVector(side == TeamId.Home ? 1f : -1f,0),true));
        Check(game.State.Phase == GamePhase.PlaySelect && game.State.Possession == side, "Actual kickoff/return reaches the controlled offense.");
        for (int h=0;h<90&&game.State.Phase==GamePhase.PlaySelect;h++)
            Step(new PlayerCommand(source:new SourceCommand(SourceAction.SelectPlay,new SourcePlaySelection("play_spider",SourcePlaybookKind.Offense))));
        Check(game.State.Phase==GamePhase.PreSnap,"Public play setup reaches pre-snap.");
        int actualQbStation=(int)Get(Actor(model,qbId),"SourceControllerStation");
        Check(configuration.Stations.Any(s=>s.StationId==actualQbStation),"The actual quarterback must belong to a real configured physical human.");
        qbStation=actualQbStation;receiverStation=configuration.Stations.Single(s=>s.StationId!=qbStation).StationId;
        while((int)Get(model,"PreSnapTicksRemaining")>=SourceClock.DefaultAutoHikeTicks-SourceClock.SourceTicksPerSecond)Wake(Raw());
        Wake(Raw(held:SourceButtons.B,pressed:SourceButtons.B));Wake(Raw(released:SourceButtons.B));
        for(int h=0;h<120&&((int)Get(model,"CarrierId")!=qbId||game.State.Phase!=GamePhase.LiveBall);h++)Step(Raw());
        Check((int)Get(model,"CarrierId")==qbId&&game.State.Phase==GamePhase.LiveBall,
            "Actual snap/center/QB catch must precede the camera release probe;side="+side+";tick="+game.SourceTick+
            ";phase="+game.State.Phase+";carrier="+Get(model,"CarrierId")+";qbStation="+qbStation+
            ";flags="+Get(model,"SourceFlags")+";script="+Get(Get(Get(Actor(model,qbId),"AnimationState"),"Primary"),"ScriptSymbol"));
        object quarterback=Actor(model,qbId);SourceVector3 origin=game.SourceActorWorldPosition(qbId);
        Legacy(data,game,qbId);
        Type driver=typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay.SourcePlayerDriver")!;
        void Place(int id,SourceVector3 p)=>driver.GetMethod("SetSourcePosition",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{Actor(model,id),p});
        for(int id=7*(1-(int)side);id<7*(1-(int)side)+7;id++)
            Place(id,new SourceVector3(origin.X+(side==TeamId.Home?300:-300),0,(id%7-3)*35));
        void Receivers()
        {
            SourceVector3 from=game.SourceActorWorldPosition(qbId);float direction=side==TeamId.Home?1:-1;
            Place(offset+4,new SourceVector3(from.X+direction*25*SourceTiming.SourceUnitsPerYard,0,from.Z));
            Place(offset+5,new SourceVector3(from.X-direction*30*SourceTiming.SourceUnitsPerYard,0,from.Z-80));
            Place(offset+6,new SourceVector3(from.X-direction*30*SourceTiming.SourceUnitsPerYard,0,from.Z+80));
        }
        capture=true;Receivers();
        for(int i=0;i<6;i++)
        {
            int world=side==TeamId.Home?512:0;
            int stick=data.Playbook.Directions.DirectionToStick((world-(int)Get(model,"SourceCameraHeading"))&1023);
            Wake(Raw(3-stick%7,3-stick/7));
        }
        for(int i=0;i<6;i++)Wake(Raw());
        Receivers();
        var hooks=new List<Probe>();object state=Get(quarterback,"AnimationState");
        foreach(string part in new[]{"Primary","Secondary"})
        {
            object layer=Get(state,part);FieldInfo before=layer.GetType().GetField("BeforeInstruction",Hidden)!;
            var hook=new Probe{Game=game,Camera=camera,Actor=quarterback,Layer=layer,CacheFrames=cacheFrames,Sounds=sounds,Original=(Delegate)before.GetValue(layer)!};
            before.SetValue(layer,typeof(Probe).GetMethod("Before",Hidden)!.MakeGenericMethod(quarterback.GetType()).CreateDelegate(before.FieldType,hook));hooks.Add(hook);
        }
        Wake(Raw(held:SourceButtons.A|SourceButtons.B,pressed:SourceButtons.A|SourceButtons.B));
        Wake(Raw(held:SourceButtons.A|SourceButtons.B));
        Wake(Raw(released:SourceButtons.A|SourceButtons.B));
        for(int h=0;h<120&&!hooks.Any(p=>p.AfterRelease!=null);h++)Step(Raw());
        foreach(Probe p in hooks)p.Layer.GetType().GetField("BeforeInstruction",Hidden)!.SetValue(p.Layer,p.Original);
        Probe? released=hooks.FirstOrDefault(p=>p.AfterRelease!=null);
        Check(released!=null,"The actual raw human throw must reach its real release.");
        cases.Add(new{side=side.ToString(),qbId,released!.BeforeRelease,released.AfterRelease,transitions,
            cacheFrames=cacheFrames.Values.ToArray(),sourceSoundCalls=sounds.ToArray(),
            boundary="Public config/typed-play setup and raw snap/QB motion/AB human-target throw; declared actor positions. No managed world-wrist is supplied to the native oracle."});
    }
    public static int Run(string[]args)
    {
        try
        {
            Check(args.Length==2&&!File.Exists(args[1]),"Usage: DATA1153 NEW-CAMERA-BOUNDARY.json");
            string root=Path.GetFullPath(args[0]);
            var data=new PlayerData(new DataCatalog(File.ReadAllBytes(Path.Combine(root,"manifest.json")),p=>File.ReadAllBytes(Path.Combine(root,p))));
            Type sinkType=typeof(AnimationOpcodeChecks).GetNestedType("Sink",BindingFlags.NonPublic)!;
            var assets=new RenderAssets(data,(IRenderSink)Activator.CreateInstance(sinkType,true)!);
            Case(data,assets,TeamId.Home);Case(data,assets,TeamId.Away);
            object directions=Get(Actor(Model(KernelBinding.Create(data)),3),"SourceDirections");
            float[] sine=Enumerable.Range(0,257).Select(n=>(float)Call(directions,"Sin",n)!).ToArray();
            File.WriteAllText(args[1],JsonSerializer.Serialize(new{schema="actual-nonidentity-camera-cache-release-observations-20261111/v1",
                data.Catalog.ManifestSha256,assertions,cases,negativeChecks,legacyChecks,quarterSine=sine,
                originalSkeletonBase64=Convert.ToBase64String(data.Catalog.EntryBytes("skeleton"))}));
            Console.WriteLine("EXPORTED actual nonidentity release cases="+cases.Count+"; original-C producer comparison required.");
            return 0;
        }
        catch(Exception e)
        {
            if(args.Length==2&&!File.Exists(args[1]))File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="failed",assertions,cases,error=e.ToString()}));
            Console.Error.WriteLine(e);return 1;
        }
    }
}
