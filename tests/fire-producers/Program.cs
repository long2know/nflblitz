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

internal static class Program
{
    private static FullKernelInputs real;
    private static SourceFireSpfxBundle assets;
    private static int assertions;
    private static readonly List<object> evidence = new List<object>();
    private static readonly List<object> nativeEmitterProbes = new List<object>();
    private static readonly List<object> ballBlinkProbes = new List<object>();
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new Exception(message); }
    private static T Bind<T>(GameSimulation game, string name)
    {
        var method = typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null);
        if (method == null) throw new MissingMethodException(name);
        return (T)(object)method.CreateDelegate(typeof(T), game);
    }
    private static GameSimulation Game(bool enabled, int raw = 0)
    {
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
        return real.Create(configuration: config,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            randomFactory: seed => new SourceInjectedRandom(site => raw,
                new SourceRandomProvenance("fire-producers/declared-raw", "Controlled component,not native PRNG",
                    "source-fire-producers", SourceRandomEvidence.ExternalUnverified)),
            compatibility: SourceGameplayCompatibility.Portable,
            fireEffects: enabled ? new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(assets), 0, SourceFireCull.Off) : null);
    }
    private static void Wake(GameSimulation game)
    {
        long before = game.SourceTick;
        while (game.SourceTick == before) game.Step(default(PlayerCommand));
    }
    private static void Normal(GameSimulation game)
    {
        SimulationState m = game.Model;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        m.Possession = TeamId.Home; m.Phase = GamePhase.PlaySelect; m.SourceBeginPlayInitialized = false;
        Bind<Action<PlayId, SourcePlaySelection, TeamId?>>(game, "SelectSourcePlay")(
            PlayId.Run, default(SourcePlaySelection), null);
    }
    private static void Park(GameSimulation game)
    {
        foreach (ActorState a in game.Model.Players)
        {
            a.SourceRawFlags |= 0x20;
            a.SourceControllerStation = -1;
            SourcePlayerDriver.SetSourcePosition(a, new SourceVector3(-300 + a.Id * 30, 0, 120));
        }
    }
    private static void PrepareQuarterback(GameSimulation game)
    {
        SimulationState m = game.Model;
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
        m.SourceFlags = SourceGameFlags.None; m.CarrierId = 3; m.ThrowerId = 3;
        m.BallKind = SourceBallKind.Pass; m.BallMode = BallMode.Held;
        m.SourceLastPossession = 0; m.CatchSourceTick = m.SourceTick;
        Park(game);
        ActorState a = m.Players[3];
        SourcePlayerDriver.SetSourcePosition(a, new SourceVector3(SourceMatchRules.WorldPosition(20, 0), 0, 0));
        a.SourceMode = SourceActorMode.Quarterback; a.SourceControllerStation = 0;
        a.SourceRawFlags &= ~0x20;
        a.SourceStick = 24; a.SourceNewButtons = SourceDroneButtons.None;
        a.AnimationState.Primary.SetMovementMode(SourceAnimationModes.Reacting | SourceAnimationModes.Uninterruptible);
        m.SourceStationPlayers[0] = 3;
    }
    private static void DefaultsAndEmpty()
    {
        Check(!SourceFireGameplayOptions.PortableRequested(null)
            && !SourceFireGameplayOptions.PortableRequested("?source-fire=disabled"), "FX disabled by default");
        Check(SourceFireGameplayOptions.PortableRequested("?source-fire=portable"), "explicit authored FX URL opt-in");
        foreach (string url in new[] { "?source-fire=guess", "?source-fire=portable&source-fire=disabled", "?source-fire" })
        {
            bool rejected = false;
            try { SourceFireGameplayOptions.PortableRequested(url); }
            catch (SourceFireBoundaryException) { rejected = true; }
            Check(rejected, "invalid FX option rejects");
        }
        GameSimulation a = Game(false), b = Game(true);
        Check(!a.SourceFireEnabled && a.SourceFireFrame == null && b.SourceFireEnabled, "actual factory disabled/enabled distinction");
        for (int i = 0; i < 6; i++) { Wake(a); Wake(b); }
        Check(JsonSerializer.Serialize(a.State) == JsonSerializer.Serialize(b.State), "empty FX preserves exact gameplay snapshot bytes");
        Check(JsonSerializer.Serialize(((SourceInjectedRandom)a.SourceRandomService).Trace)
            == JsonSerializer.Serialize(((SourceInjectedRandom)b.SourceRandomService).Trace), "empty FX and disabled use identical shared draw/site/order");
        Check(b.SourceFireFrame.SourceTick == b.SourceTick && b.Model.SourceCameraJointTick == b.SourceTick
            && b.SourceFireFrame.Basis.Camera.SourceTick == b.SourceTick, "real current15/cache camera publishes actual source frame");
        var consumer = new SourceFireSpfxFrameConsumer(assets);
        long draws = b.Model.SourceRandomDraws;
        consumer.Consume(b.SourceFireFrame); consumer.Consume(b.SourceFireFrame);
        Check(b.Model.SourceRandomDraws == draws && consumer.LastFrame == b.SourceFireFrame,
            "actual immutable frame consumer/snapshot reads consume no Core RNG");
        bool skipped = false;
        for (int host = 0; host < 25 && !skipped; host++)
        {
            long tick = b.SourceTick; SourceFireRenderFrame held = b.SourceFireFrame;
            b.Step(default(PlayerCommand));
            if (b.SourceTick == tick)
            {
                Check(ReferenceEquals(held, b.SourceFireFrame), "skipped host frame holds immutable FX without process aging");
                skipped = true;
            }
        }
        Check(skipped, "declared60/57 bridge exposes a skipped host frame");
    }
    private static void RecurrenceAndPairedCache()
    {
        GameSimulation game = Game(true); Wake(game); Normal(game); PrepareQuarterback(game);
        var emit = Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode");
        var cache = game.Model.SourceMovementCamera;
        SourceVector3 joint = game.Model.SourceCameraJoints[3, 4];
        int firstDraw = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        game.Model.SourceTeamFire[0] = true;
        emit(game.Model.Players[3], default(PlayerCommand));
        ActorState actual = game.Model.Players[3];
        nativeEmitterProbes.Add(new {
            input = new object[] { actual.Id, 1, actual.SourceControllerStation, GameSnapshot.SourceActorFlags(actual),
                actual.SourceVelocity.X, actual.SourceVelocity.Z,
                (actual.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0 ? 1 : 0,
                actual.SourceMode == SourceActorMode.Quarterback ? 1 : 0, 1, (int)game.SourceTick, 0 },
            requests = game.SourceFireRecords.Where(r => r.Kind == "CreateProcess" && r.Site.Function == "maybe_plyr_fire").Select(r => {
                SourceFireProcessView p = game.SourceFireProcesses.Single(x => x.Serial == r.ProcessSerial);
                return new { args = new[] { p.Args.A0, p.Args.A1, p.Args.A2, p.Args.A3 }, group = p.GroupId, line = r.Site.SourceLine };
            }).ToArray(),
            random = game.SourceFireRecords.Where(r => r.Kind == "Rng" && r.Site.Function == "maybe_plyr_fire")
                .Select(r => new { bound = 4, value = r.Value, line = r.Site.SourceLine }).ToArray()
        });
        Check(game.SourceFireSoundSubmissions.Count == 0, "recurring emitter is not inferred from sound");
        Wake(game);
        var particle = game.SourceFireFrame.Particles.First();
        var t = cache.Transform;
        var expected = new SourceVector3(joint.X * t[0] + joint.Y * t[4] + joint.Z * t[8] + cache.Position.X,
            joint.X * t[1] + joint.Y * t[5] + joint.Z * t[9] + cache.Position.Y,
            joint.X * t[2] + joint.Y * t[6] + joint.Z * t[10] + cache.Position.Z);
        Check(Math.Abs(particle.WorldPosition.X - expected.X) < .0001f
            && Math.Abs(particle.WorldPosition.Y - expected.Y) < .0001f
            && Math.Abs(particle.WorldPosition.Z - expected.Z) < .0001f,
            "first FX wake uses actual prior15/cache paired camera,not current camera or re-pose");
        int[] bounds = ((SourceInjectedRandom)game.SourceRandomService).Trace.Skip(firstDraw)
            .Where(x => x.Site.SourceIdentity.Contains("plyr_fire_trail_proc")).Take(4).Select(x => x.Site.BoundOrModulus).ToArray();
        Check(bounds.SequenceEqual(new[] { 5, 3, 10000, 4 }), "actual Core provider owns all ordered puff entry draws");
        for (int i = 0; i < 3; i++)
        {
            PrepareQuarterback(game); game.Model.SourceTeamFire[0] = true;
            emit(game.Model.Players[3], default(PlayerCommand));
            Wake(game);
        }
        var trace = ((SourceInjectedRandom)game.SourceRandomService).Trace;
        Check(trace.Count(x => x.Site.SourceIdentity.Contains("maybe_plyr_fire:4481")) >= 4,
            "actual QB caller recurs across source wakes,not only OnFire rising edge");
        Check(game.SourceFireFrame.Records.Any(r => r.Kind == "PlayerEntryCache" && r.Detail.Contains("cache-tick=")),
            "actual entry publishes paired producer-cache evidence");
        int beforeRead = trace.Count;
        var draws = new SourceFireSpfxFrameConsumer(assets).Consume(game.SourceFireFrame);
        Check(draws.Count == game.SourceFireFrame.Particles.Count(p => !p.Hidden)
            && trace.Count == beforeRead, "real nonempty process frame reaches approved textured CPU consumer without new RNG");
        evidence.Add(new { kind = "real-Core-recurring-QB-and-cache", game.SourceTick, particles = game.SourceFireFrame.Particles.Count });
    }
    private static void MissingCacheAndLevelZero()
    {
        GameSimulation missing = Game(true); Normal(missing); PrepareQuarterback(missing);
        missing.Model.SourceTeamFire[0] = true;
        Bind<Action<ActorState, string, int>>(missing, "MaybeSourcePlayerFire")(missing.Model.Players[3], "_mode_qb", 2088);
        bool rejected = false;
        try { Wake(missing); }
        catch (SourceFireBoundaryException e) { rejected = e.Message.Contains("paired actor15"); }
        Check(rejected, "enabled first-wake cache absence rejects,never fake bones or identity camera");

        GameSimulation game = Game(true); Wake(game); Normal(game); PrepareQuarterback(game);
        game.Model.SourceTeamFire[0] = true;
        Bind<Action<ActorState, string, int>>(game, "MaybeSourcePlayerFire")(game.Model.Players[3], "_mode_qb", 2088);
        Bind<Action>(game, "SpawnSourceC2Whistle")();
        int before = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        game.Model.SourceTeamFire[0] = false;
        Wake(game);
        var draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Skip(before).Take(5).ToArray();
        Check(draws.Select(d => d.Site.BoundOrModulus).SequenceEqual(new[] { 5, 3, 10000, 4, 100 })
            && draws[0].Site.Process == "plyr_fire_trail_proc" && draws[4].Site.Process == "whistle_proc",
            "authored actual level0 effect entries precede individual C2 whistle on same Core RNG");
        Check(game.C2Events.Any(e => e.Kind == SourceC2EventKind.WhistleFirstWake),
            "actual C2 continuation executes in its declared effect/host slice");
    }
    private static void PresnapCatchBallReset()
    {
        GameSimulation game = Game(true); Wake(game); Normal(game);
        SimulationState m = game.Model; ActorState receiver = m.Players[4];
        m.SourceLastReceiver[0] = 4; m.SourceConsecutiveCompletions[0] = 2;
        receiver.SourceMode = SourceActorMode.ReceiverPreSnap;
        receiver.AnimationState.Primary.SetMovementMode(0);
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(receiver, default(PlayerCommand));
        Check(game.SourceFireSoundSubmissions.Count(s => s.Call == 193) == 1, "real presnap tail produces almost-fire flash exactly once");
        Wake(game);
        Check((m.Players[4].SourceRawFlags & 0x400) != 0, "actual flash continuation writes real Core actor white bit");
        Check(new SourceFirePlayerFlashState(game.State.Players[4].Source.Presentation.RawFlags).White,
            "actual snapshot raw white bit reaches qualified player material-state consumer");
        m.SourceTeamFire[0] = false; m.Phase = GamePhase.LiveBall; m.SourceFlags = SourceGameFlags.None;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        m.Possession = TeamId.Home; m.CarrierId = -1; m.BallMode = BallMode.InFlight; m.BallKind = SourceBallKind.Pass;
        m.PassTicksElapsed = 40; m.TargetPlayerId = 4; m.ThrowerId = 3;
        receiver.NoCatchTicks = 0; receiver.SourceMode = SourceActorMode.Receiver;
        receiver.AnimationState.FullBody("run_anim", receiver, game.SourceTick, n => 0, AnimationSelectionEvidence.SourceVerified);
        SourcePlayerDriver.SetSourcePosition(receiver, new SourceVector3(SourceMatchRules.WorldPosition(40, 0), 0, 0));
        SourceBallDriver.SetPosition(m, new SourceVector3(receiver.SourcePosition.X, receiver.SourceRoot.Y, 0));
        int sounds = game.SourceFireSoundSubmissions.Count;
        Bind<Action<ActorState>>(game, "TrySourceCatch")(receiver);
        Check(m.CarrierId == 4 && game.SourceFireSoundSubmissions.Skip(sounds).Count(s => s.Call == 192) == 2,
            "actual third-potential catch submits two original192 calls,not an OnFire edge");
        m.CarrierId = -1; m.Phase = GamePhase.LiveBall; m.SourceFlags = SourceGameFlags.None;
        m.SourceBallFlags = SourceBallFlags.FirePass; m.BallKind = SourceBallKind.Pass;
        m.BallMode = BallMode.InFlight;
        m.PassTicksElapsed = 1;
        SourceBallDriver.SetPosition(m, new SourceVector3(-100, 60, 0)); m.SourceBallVelocity = new FieldVector(1, 0);
        m.SourceBallVerticalVelocity = 0;
        Bind<Action>(game, "AdvanceSourceBall")();
        var ballChildren = game.SourceFireProcesses.Where(p => p.Entry == SourceFireEntry.BallPuff).ToArray();
        Check(ballChildren.Length == 3 && ballChildren.Select(p => p.GroupId).All(g => g == SourceFireIds.Player)
            && ballChildren.Select(p => p.Args.A2).SequenceEqual(new[] { -2, 2, 0 }),
            "actual postincrement even ball caller queues ordered three PLAYER children,not preincrement");
        Wake(game);
        Check(game.SourceFireFrame.Records.Count(r => r.Kind == "Wake" && r.Detail == "ball_fire_trail_proc") == 3,
            "actual postincrement ball fire reaches all three process continuations");
        m.Players[7].SourceRawFlags |= 0x400; m.Players[0].SourceRawFlags |= 0x400;
        Bind<Action<int>>(game, "FireLineupScope")(0);
        Check((m.Players[0].SourceRawFlags & 0x400) == 0 && (m.Players[7].SourceRawFlags & 0x400) != 0,
            "Core typed side lineup preserves other-team white state");
        Normal(game);
        Check(m.Players.All(a => (a.SourceRawFlags & 0x400) == 0), "actual complete lineup owns exactly one explicit all-player clear");
        m.SourceLastReceiver[0] = 4; m.SourceConsecutiveCompletions[0] = 2;
        receiver.SourceMode = SourceActorMode.ReceiverPreSnap; receiver.AnimationState.Primary.SetMovementMode(0);
        sounds = game.SourceFireSoundSubmissions.Count;
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(receiver, default(PlayerCommand));
        Check(game.SourceFireSoundSubmissions.Count == sounds + 1, "real lineup resets shared presnap latches separately");
        game.Restart();
        Check(game.SourceFireEnabled && game.SourceFireFrame == null, "restart preserves explicit opt-in and resets effect epoch/frame");
    }
    private static void GameProcLossAndSelection()
    {
        GameSimulation game = Game(true); Wake(game); Normal(game); PrepareQuarterback(game);
        SimulationState m = game.Model;
        m.SourceConsecutiveLosses[1] = 1;
        m.SourcePlayEndX = SourceMatchRules.WorldPosition(20, 0); m.SourcePlayEndZ = 0;
        m.Players[3].SourceHitPartnerId = 7; m.Players[10].SourceHitPartnerId = 8;
        Bind<Action<int, SourceAnimationSignal>>(game, "SourceAnimationSideEffect")(3, SourceAnimationSignal.PlayerDown);
        m.Players[3].SourceMode = SourceActorMode.Quarterback;
        Check(!m.SourceTeamFire[1], "deferred actual loss FX waits for game_proc commit,not PlayerDown edge");
        Wake(game);
        Check(m.SourceTeamFire[1] && game.C2Events.Any(e => e.Kind == SourceC2EventKind.ResultCommitted),
            "actual level0 game_proc commit activates defensive fire");
        Check(game.SourceFireSoundSubmissions.Select(s => s.Call).SequenceEqual(new[] { 185, 192, 192 })
            && game.SourceFireProcesses.Any(p => p.Entry == SourceFireEntry.PlayerBurst
                && p.Args.A0 == 7 && p.Args.A1 == 6 && p.Args.A2 == 4),
            "second-loss source site submits185 then two192 and selected QB hit-partner burst");
        Bind<Action>(game, "ContinueSource")();
        Check(m.Phase == GamePhase.PlaySelect, "actual game_proc dismissal enters source selection");
        Wake(game);
        Check(game.SourceFireFrame.Bootstrap.Ordering == SourceFireOrdering.PortableDeclaredSlots
            && game.SourceFireFrame.Bootstrap.ClockOrigin == SourceFireClockOrigin.PortableLogicalEpoch,
            "selection preserves explicitly portable effect ordering,never native relabelling");
        long cameraTick = game.SourceFireFrame.Basis.Camera.SourceTick;
        Wake(game);
        Check(game.SourceFireFrame.Basis.Camera.SourceTick == cameraTick,
            "menu effect frame retains real suspended camera without inventing a fresh producer");
        Check(game.SourceFireFrame.Records.Any(r => r.Kind == "PlayerEntryCache" && r.Detail.Contains("retaining"))
            || game.SourceFireProcesses.Any(p => p.Entry == SourceFireEntry.PlayerPuff),
            "menu SPFX continuations remain independent of suspended actor producers");
        Normal(game);
        Check(game.SourceFireRecords.Any(r => r.Kind == "KillProcess" && r.Site.Function == "clobber_playsel"),
            "actual selection exit reaches original SPFX kill site");
        Check(game.SourceFireRecords.Where(r => r.Kind == "CreateProcess")
            .Select(r => new { r.Site.NativeProcessSerial, r.Site.WakeOrdinal, r.Site.WithinWakeCallOrdinal }).Distinct().Count()
            == game.SourceFireRecords.Count(r => r.Kind == "CreateProcess"),
            "producer call ordinals are ordered within the real host continuation");
    }
    private static void BallBlinkStep(GameSimulation game, string name, SourceBallKind kind, GamePhase phase,
        SourceBallFlags flags, int flight, bool white, int carrier = -1, bool teamFire = false)
    {
        SimulationState m = game.Model;
        m.Phase = phase; m.SourceMatchPhase = phase == GamePhase.LiveBall ? SourceMatchPhase.LiveBall : SourceMatchPhase.PreSnap;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100,
            kind == SourceBallKind.Kickoff ? SourcePlayType.Kickoff : SourcePlayType.Normal);
        m.Possession = TeamId.Home; m.SourceFlags = SourceGameFlags.None;
        m.CarrierId = carrier; m.ThrowerId = 3; m.TargetPlayerId = 4;
        m.BallKind = kind; m.BallMode = carrier == -1 ? BallMode.InFlight : BallMode.Held;
        m.SourceBallFlags = flags; m.PassTicksElapsed = flight;
        m.SourceBallBounces = 0; m.SourceBallBounceTick = 0;
        m.SourceTeamFire[0] = teamFire; m.SourceTeamFire[1] = false;
        SourceBallDriver.SetPosition(m, new SourceVector3(-100f, 50f, 0f));
        m.SourceBallVelocity = new FieldVector(1f, 0f); m.SourceBallVerticalVelocity = 0f;
        SourceBallDriver.SetTarget(m, 222f, 111f);
        int mode = (int)Bind<Func<SourceMovementPhase>>(game, "get_MovementPhase")();
        long draws = m.SourceRandomDraws;
        int trace = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        int processes = game.SourceFireEnabled ? game.SourceFireProcesses.Count : 0;
        int sounds = game.SourceFireSoundSubmissions.Count;
        bool free = carrier == -1 || (flags & SourceBallFlags.Phantom) != 0;
        Bind<Action>(game, "AdvanceSourceBall")();
        bool actualWhite = (m.SourceBallFlags & SourceBallFlags.White) != 0;
        string label = "original ball blink owner " + name;
        Check(actualWhite == white, label + " BF_WHITE");
        Check(m.PassTicksElapsed == flight + (free ? 1 : 0), label + " exact free/phantom postincrement");
        Check(m.SourceRandomDraws == draws && ((SourceInjectedRandom)game.SourceRandomService).Trace.Count == trace,
            label + " no added RNG draw/site/order");
        Check((m.SourceBallFlags & ~SourceBallFlags.White) == (flags & ~SourceBallFlags.White),
            label + " all other ball flags retained");
        Check((game.SourceFireEnabled ? game.SourceFireProcesses.Count : 0) == processes,
            label + " no invented particle process");
        Check(game.SourceFireSoundSubmissions.Count == sounds, label + " no invented sound submission");
        if (game.SourceFireEnabled)
            ballBlinkProbes.Add(new {
                name,
                input = new { kind = kind.ToString(), rawFlags = (int)flags, flight, mode, carrier,
                    teamFire = teamFire ? 1 : 0, offSide = 0 },
                actual = new { effect = game.SourceBallEffectSelection.ToString(),
                    white = actualWhite, flight = m.PassTicksElapsed }
            });
    }
    private static int BallBlinkOwner()
    {
        int before = assertions;
        GameSimulation game = Game(true); Wake(game);
        BallBlinkStep(game, "kickoff-nine-clears", SourceBallKind.Kickoff, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 8, false);
        BallBlinkStep(game, "kickoff-ten", SourceBallKind.Kickoff, GamePhase.LiveBall, SourceBallFlags.Bounce, 9, true);
        BallBlinkStep(game, "kickoff-eleven-restores", SourceBallKind.Kickoff, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 10, false);
        BallBlinkStep(game, "kickoff-twenty", SourceBallKind.Kickoff, GamePhase.LiveBall, SourceBallFlags.Bounce, 19, true);
        BallBlinkStep(game, "punt-ten", SourceBallKind.Punt, GamePhase.LiveBall, SourceBallFlags.Bounce, 9, true);
        BallBlinkStep(game, "punt-unbounced", SourceBallKind.Punt, GamePhase.LiveBall, SourceBallFlags.None, 9, false);
        BallBlinkStep(game, "field-goal-ten", SourceBallKind.FieldGoal, GamePhase.LiveBall, SourceBallFlags.Bounce, 9, true);
        BallBlinkStep(game, "field-goal-bounce-before-fire", SourceBallKind.FieldGoal, GamePhase.LiveBall,
            SourceBallFlags.Bounce, 9, true, teamFire: true);
        BallBlinkStep(game, "punt-play-over", SourceBallKind.Punt, GamePhase.DeadBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "field-goal-play-over", SourceBallKind.FieldGoal, GamePhase.DeadBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "kickoff-pre-kickoff", SourceBallKind.Kickoff, GamePhase.PreSnap,
            SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "field-goal-pre-snap", SourceBallKind.FieldGoal, GamePhase.PreSnap,
            SourceBallFlags.Bounce, 9, true);
        BallBlinkStep(game, "punt-primes-sticky-bobble", SourceBallKind.Punt, GamePhase.LiveBall,
            SourceBallFlags.Bounce, 8, false);
        BallBlinkStep(game, "bobble-sticky-ten", SourceBallKind.Bobble, GamePhase.LiveBall, SourceBallFlags.None, 9, true);
        BallBlinkStep(game, "bobble-sticky-eleven", SourceBallKind.Bobble, GamePhase.LiveBall, SourceBallFlags.White, 10, false);
        BallBlinkStep(game, "fumble-clears", SourceBallKind.Fumble, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "lateral-clears", SourceBallKind.Lateral, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "pass-does-not-blink", SourceBallKind.Pass, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "turbo-does-not-blink", SourceBallKind.Pass, GamePhase.LiveBall,
            SourceBallFlags.TurboPass | SourceBallFlags.White, 9, false);
        BallBlinkStep(game, "fire-does-not-blink", SourceBallKind.Pass, GamePhase.LiveBall,
            SourceBallFlags.FirePass | SourceBallFlags.White, 10, false);
        BallBlinkStep(game, "punt-primes-held", SourceBallKind.Punt, GamePhase.LiveBall, SourceBallFlags.Bounce, 8, false);
        BallBlinkStep(game, "held-dark-retained", SourceBallKind.Punt, GamePhase.LiveBall, SourceBallFlags.None, 10, false, 3);
        BallBlinkStep(game, "held-white-retained", SourceBallKind.Punt, GamePhase.LiveBall, SourceBallFlags.White, 10, true, 3);
        BallBlinkStep(game, "phantom-ten", SourceBallKind.Punt, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.Phantom, 9, true, 3);
        BallBlinkStep(game, "phantom-eleven-restores", SourceBallKind.Punt, GamePhase.LiveBall,
            SourceBallFlags.Bounce | SourceBallFlags.Phantom | SourceBallFlags.White, 10, false, 3);
        GameSimulation disabled = Game(false); Wake(disabled);
        Check(!disabled.SourceFireEnabled, "ball blink retains disabled-by-default factory");
        Check(disabled.SourceFireFrame == null, "disabled ball blink does not create an FX frame");
        foreach (SourceBallKind kind in new[] { SourceBallKind.Kickoff, SourceBallKind.Punt, SourceBallKind.FieldGoal })
            BallBlinkStep(disabled, "disabled-" + kind, kind, GamePhase.LiveBall,
                SourceBallFlags.Bounce | SourceBallFlags.White, 9, false);
        Check(ballBlinkProbes.Count == 25, "entire exact original ball blink owner case inventory");
        return assertions - before;
    }
    private static int Main(string[] args)
    {
        try
        {
            real = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
            assets = SourceFireSpfxReader.ReadAuthorized(args[3], "manifest.json", args[4]);
            DefaultsAndEmpty(); RecurrenceAndPairedCache(); MissingCacheAndLevelZero(); PresnapCatchBallReset(); GameProcLossAndSelection();
            int ballBlinkAssertions = BallBlinkOwner();
            var result = new { status = "passed", assertions, ballBlinkAssertions, ballBlinkProbes,
                evidence, nativeEmitterProbes, fullGameQualified = false, nativeQualified = false };
            File.WriteAllText(args[5], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FIRE-PRODUCER ASSERTION: " + error); return 1; }
    }
}
