using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static partial class Program
{
    private static string compatibilityOldInputs;
    private static string compatibilitySoundFunctions;

    private static void CompatibilityReject(Action action, string identity)
    {
        bool rejected = false;
        try { action(); }
        catch (NotSupportedException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, identity);
    }

    private static GameSimulation CompatibilityGame(SourceGameplayCompatibility mode)
    {
        return Create(random: seed => new SourceInjectedRandom(site => 99,
            new SourceRandomProvenance("portable-component/raw99", "Declared component, not target RNG",
                "compatibility", SourceRandomEvidence.ExternalUnverified)), compatibility: mode);
    }

    private static void CompatibilitySelect(GameSimulation game, int repeats)
    {
        SimulationState m = game.Model;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        m.Possession = TeamId.Home;
        m.Phase = GamePhase.PlaySelect;
        m.SourceBeginPlayInitialized = false;
        SourcePlayDefinition play = playbook.GetPlay("play_zigzag");
        for (int i = 0; i < 5; i++) m.SourceHandicapPlayHistory[i] = i < repeats ? play : null;
        Bind<Action<PlayId, SourcePlaySelection, TeamId?>>(game, "SelectSourcePlay")(
            PlayId.Run, default(SourcePlaySelection), null);
    }

    private sealed class CompatibilityBoundary : ISourceContactBoundary
    {
        internal readonly List<string> Calls = new List<string>();
        public int Draw(int bound, string function, int line)
        { Calls.Add("draw:" + bound + ":" + function + ":" + line); return 0; }
        public void Collision(ActorState a, ActorState b, int mode, int d4, int d8)
        { Calls.Add("collision:" + a.Id + ":" + b.Id); }
        public void Change(ActorState actor, string script) { Calls.Add("change:" + actor.Id + ":" + script); }
        public void Link(ActorState a, ActorState b) { Calls.Add("link:" + a.Id + ":" + b.Id); }
        public void StopCarrier(ActorState a, ActorState b, int bobble, int fumble, string function, int line, bool camera)
        { Calls.Add("stop:" + a.Id); }
        public void PopHelmet(ActorState actor) { Calls.Add("helmet:" + actor.Id); }
        public void Sound(ActorState actor, string function, int line, params string[] args)
        { Calls.Add("sound:" + function); }
    }

    private static CompatibilityBoundary CompatibilityBlock(GameSimulation game, int rawCount)
    {
        SimulationState m = game.Model;
        m.SourceTick = 80; m.SourceCameraJointTick = 80;
        m.Phase = GamePhase.LiveBall; m.SourceFlags = SourceGameFlags.None;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        m.Possession = TeamId.Home; m.CarrierId = 3;
        foreach (ActorState actor in m.Players)
        {
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(2000 + actor.Id * 100, 0, 0));
            actor.SourceRawFlags = 0; actor.SourceAttackMode = 0;
            actor.SourceJob = SourceActorJob.None; actor.SourceControllerStation = -1;
            actor.SourceVelocity = default(FieldVector); actor.SourceRoot = default(SourceVector3);
            actor.AnimationState.Primary.SetMovementMode(0); actor.AnimationState.Secondary.ClearLowModes();
        }
        ActorState blocker = m.Players[1], rusher = m.Players[8];
        SourcePlayerDriver.SetSourcePosition(blocker, new SourceVector3(0, 0, 0));
        SourcePlayerDriver.SetSourcePosition(rusher, new SourceVector3(0, 0, 8));
        blocker.SourceJob = SourceActorJob.Block; rusher.SourceBlockCount = rawCount;
        m.SourceTeamFire[0] = m.SourceTeamFire[1] = false;
        for (int id = 0; id < 14; id++)
            for (int j = 0; j < 15; j++) m.SourceCameraJoints[id, j] = SourcePlayerDriver.WorldPosition(m.Players[id]);
        var boundary = new CompatibilityBoundary();
        SourceContactDriver.Detect(m, boundary);
        return boundary;
    }

    private static GameSimulation CompatibilityRunningPushGame(SourceGameplayCompatibility mode, int stick)
    {
        GameSimulation game = CompatibilityGame(mode);
        SimulationState model = game.Model;
        model.SourceTick = 300; model.Phase = GamePhase.LiveBall; model.SourceFlags = SourceGameFlags.None;
        model.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        model.Possession = TeamId.Home; model.CarrierId = -1; model.TargetPlayerId = 5;
        model.BallKind = SourceBallKind.Pass;
        ActorState actor = model.Players[13];
        actor.SourceMode = SourceActorMode.DefensiveBack; actor.SourceJob = SourceActorJob.Rush;
        actor.SourceControllerStation = -1; actor.SourceStick = stick; actor.SourceTarget = -1;
        actor.AnimationState.Secondary.AddMode(0x1000u);
        SourceRouteState route = SourceRouteState.Begin(playbook.GetRoute("db_man"));
        route.Stick = route.OldStick = stick; route.ThinkTicks = -1;
        model.SourceDrones[13] = route;
        Bind<Action>(game, "CacheSourceGeometry")();
        model.SourceAiFrame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
        model.SourceRandomActor = 13; model.SourceRandomProcess = "player_proc";
        model.SourceRandomProcessOrder = 14; model.SourceRandomCallIndex = 0;
        return game;
    }

    private static SourceRouteStep CompatibilityRunningPushStep(GameSimulation game,
        SourceGameplayCompatibility mode, out string missing)
    {
        missing = null;
        try
        {
            return SourceDroneAI.DecideDefensiveBack(game.Model.SourceDrones[13],
                new SourceRouteInput(game.Model.SourceAiFrame, 13, game.Model.SourceAiParameters,
                    new SourceRandomContext(300, 13, "player_proc", 14, game.SourceRandomService.Provenance.Profile),
                    compatibility: mode),
                game.SourceRandomService);
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith(
            "Required source branch p2p_angles target -1", StringComparison.Ordinal))
        { missing = error.Message; return null; }
    }

    private static void CompatibilityRunningPushChecks()
    {
        for (int stick = 0; stick <= 48; stick++)
        {
            GameSimulation game = CompatibilityRunningPushGame(SourceGameplayCompatibility.Portable, stick);
            var random = (SourceInjectedRandom)game.SourceRandomService;
            int before = random.Trace.Count;
            string missing;
            SourceRouteStep strict = CompatibilityRunningPushStep(game, SourceGameplayCompatibility.StrictSource, out missing);
            Check(strict == null && missing.Contains("actor 13 sourceTick 300"),
                "Strict running-push missing target remains a visible exact source failure");
            SourceRouteStep portable = CompatibilityRunningPushStep(game, SourceGameplayCompatibility.Portable, out missing);
            Check(portable != null && missing == null,
                "portable targetless running push continues its real route without an angle read");
            Check(portable.RetainedTargetlessRunningPush && portable.State.Target == -1
                && portable.State.Stick == stick && portable.State.OldStick == stick
                && portable.State.Buttons == (SourceDroneButtons.B | SourceDroneButtons.C)
                && portable.State.LastSourceTick == 300,
                "portable targetless running push retains all49 valid sticks and source buttons without retargeting");
            Check(portable.RandomCalls == 0 && random.Trace.Count == before
                && portable.SourceBranch == "portable:drone.c:_drone_db:DB_H:retained-stick",
                "portable targetless running push has zero RNG and explicit source provenance");
            game.Model.SourceDrones[13] = game.Model.SourceDrones[13].WithTarget(3);
            SourceRouteStep validStrict = CompatibilityRunningPushStep(game, SourceGameplayCompatibility.StrictSource, out missing);
            SourceRouteStep validPortable = CompatibilityRunningPushStep(game, SourceGameplayCompatibility.Portable, out missing);
            Check(validStrict != null && validPortable != null && !validStrict.RetainedTargetlessRunningPush
                && !validPortable.RetainedTargetlessRunningPush && validStrict.State.Stick == validPortable.State.Stick
                && validStrict.State.Buttons == validPortable.State.Buttons
                && validStrict.RandomCalls == validPortable.RandomCalls,
                "defined running-push target behavior remains identical in both modes");
        }
        GameSimulation core = CompatibilityRunningPushGame(SourceGameplayCompatibility.Portable, 12);
        var coreRandom = (SourceInjectedRandom)core.SourceRandomService;
        int prior = coreRandom.Trace.Count;
        bool moved = false;
        try
        {
            Bind<Func<ActorState, PlayerCommand, FieldVector>>(core, "SourceDroneMove")(
                core.Model.Players[13], default(PlayerCommand));
            moved = true;
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith(
            "Required source branch p2p_angles target -1", StringComparison.Ordinal)) { }
        Check(moved, "actual M1 caller forwards Portable compatibility into the running-push route");
            Check(core.Model.SourceCompatibilityRepairs.Count == 1,
                "actual running-push repair is never silent or duplicated");
            SourceCompatibilityRepair repair = core.Model.SourceCompatibilityRepairs.Single();
        Check(repair.Kind == SourceCompatibilityRepairKind.TargetlessRunningPush && repair.SubjectId == 13
            && repair.RawIndex == -1 && repair.EffectiveIndex == -1 && repair.RetainedStick == 12
            && repair.SourceTick == 300 && repair.SourceContext.Contains("DB_H")
            && core.Model.SourceDrones[13].Target == -1 && coreRandom.Trace.Count == prior,
            "actual running-push repair reports retained control without target or RNG mutation");
        SourceMatchView snapshot = ((ISourceMatchState)new GameSnapshot(core.Model)).Match;
        Check(snapshot.CompatibilityRepairCount == 1 && snapshot.CompatibilityRepairs.Single().RetainedStick == 12,
            "published running-push repair is typed and visible");
        CompatibilityReject(() => new SourceRouteInput(core.Model.SourceAiFrame, 13, core.Model.SourceAiParameters,
            new SourceRandomContext(300, 13, "player_proc", 14, core.SourceRandomService.Provenance.Profile),
            compatibility: (SourceGameplayCompatibility)99), "invalid route compatibility cannot enable a repair");
        core.Restart();
        Check(core.Compatibility == SourceGameplayCompatibility.Portable && core.Model.SourceCompatibilityRepairCount == 0,
            "running-push repair resets with restart without mode promotion");
    }

    private static GameSimulation CarrierlessSwitchGame(SourceGameplayCompatibility mode, int station,
        SourceBallKind kind, bool turbo = false, bool moving = false)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away),
                new SourceStationConfiguration(2, TeamId.Home), new SourceStationConfiguration(3, TeamId.Away) },
            TeamId.Away, SourceCaptainSlot.Second);
        activeGame = real.Create(configuration: configuration, compatibility: mode,
            randomFactory: seed => new SourceInjectedRandom(site => 99,
                new SourceRandomProvenance("carrierless-switch/raw99", "Declared caller inputs, not target RNG",
                    "four-station", SourceRandomEvidence.ExternalUnverified)),
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0));
        GameSimulation game = activeGame;
        SimulationState m = game.Model;
        m.SourceTick = 8269; m.Phase = GamePhase.LiveBall; m.SourceFlags = SourceGameFlags.None;
        m.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        m.CarrierId = -1; m.TargetPlayerId = -1; m.BallKind = kind;
        m.SourceBallPosition = new SourceVector3(0, 16, 0); m.SourceBallTarget = default(SourceVector3);
        m.SourceOffensePlay = playbook.GetPlay("play_spider");
        m.SourceDefensePlay = playbook.GetPlay("play_safe_cover");
        foreach (ActorState player in m.Players)
        {
            player.SourceControllerStation = -1;
            player.SourceMode = SourceActorMode.DefensiveBack; player.SourceRawFlags = 0;
            player.SourceTarget = -1; player.SourceStick = player.SourcePreviousStick = 24;
            player.SourceButtons = player.SourceOldButtons = player.SourceNewButtons = SourceDroneButtons.None;
            player.SourceVelocity = default(FieldVector); player.SourceRoot = default(SourceVector3);
            player.AnimationState.Primary.SetMovementMode(0);
            player.AnimationState.Secondary.SetMovementMode(0);
            SourcePlayerDriver.SetSourcePosition(player, new SourceVector3(100 * (player.Id % 7 + 1), 0, 0));
        }
        int[] owners = { 4, 11, 5, 12 };
        for (int i = 0; i < owners.Length; i++)
        {
            m.SourceStationPlayers[i] = owners[i];
            m.Players[owners[i]].SourceControllerStation = i;
        }
        m.ControlledPlayerId = owners[0];
        ActorState actor = m.Players[owners[station]];
        m.Possession = actor.Team == TeamId.Home ? TeamId.Away : TeamId.Home;
        actor.SourceButtons = SourceDroneButtons.B | (turbo ? SourceDroneButtons.C : SourceDroneButtons.None);
        actor.SourceNewButtons = SourceDroneButtons.B;
        actor.SourceVelocity = new FieldVector(moving ? 2f : 0f, 0f);
        Bind<Action>(game, "CacheSourceGeometry")();
        m.SourceRandomActor = actor.Id; m.SourceRandomProcess = "player_proc";
        m.SourceRandomProcessOrder = actor.Id + 1; m.SourceRandomCallIndex = 0;
        return game;
    }

    private static string CarrierlessSwitchState(GameSimulation game)
    {
        SimulationState m = game.Model;
        return System.Text.Json.JsonSerializer.Serialize(new {
            m.CarrierId, m.TargetPlayerId, m.SourceStationPlayers, m.ControlledPlayerId,
            ball = new[] { m.SourceBallPosition.X, m.SourceBallPosition.Y, m.SourceBallPosition.Z,
                m.SourceBallTarget.X, m.SourceBallTarget.Y, m.SourceBallTarget.Z },
            players = m.Players.Select(a => new {
                a.Id, a.SourceControllerStation, a.SourceTarget, a.SourceStick, a.SourcePreviousStick,
                a.SourceButtons, a.SourceOldButtons, a.SourceNewButtons,
                position = new[] { SourcePlayerDriver.WorldPosition(a).X, SourcePlayerDriver.WorldPosition(a).Y,
                    SourcePlayerDriver.WorldPosition(a).Z },
                velocity = new[] { a.SourceVelocity.X, a.SourceVelocity.Z }, a.Facing, a.TargetFacing,
                lower = new { a.AnimationState.Primary.ScriptSymbol, a.AnimationState.Primary.Mode,
                    a.AnimationState.Primary.FrameOrdinal },
                upper = new { a.AnimationState.Secondary.ScriptSymbol, a.AnimationState.Secondary.Mode,
                    a.AnimationState.Secondary.FrameOrdinal } }),
            m.SourceRandomDraws, m.SourceRandomCallIndex,
            trace = ((SourceInjectedRandom)game.SourceRandomService).Trace
        });
    }

    private static bool CarrierlessSwitchFault(GameSimulation game, ActorState actor)
    {
        try { Bind<Action<ActorState>>(game, "SourceDefensiveMovementButtons")(actor); return false; }
        catch (NotSupportedException error)
        {
            Check(error.Message == "player.c:db_change_player seeking-player branch indexes "
                + "p2p_angles[game_info.ball_carrier==-1]; original undefined C is not replaced with a target.",
                "strict carrierless switch retains its exact existing source fault");
            return true;
        }
    }

    private static void CarrierlessSwitchChecks()
    {
        Check(real.IsC2M1Profile, "carrierless switching uses the actual M1 factory");
        foreach (int station in new[] { 0, 1, 2, 3 })
        foreach (SourceBallKind kind in new[] { SourceBallKind.Pass, SourceBallKind.FieldGoal, SourceBallKind.Kickoff })
        foreach (bool turbo in new[] { false, true })
        foreach (bool moving in new[] { false, true })
        {
            GameSimulation strict = CarrierlessSwitchGame(SourceGameplayCompatibility.StrictSource, station, kind, turbo, moving);
            ActorState actor = strict.Model.Players[strict.Model.SourceStationPlayers[station]];
            string before = CarrierlessSwitchState(strict);
            Check(CarrierlessSwitchFault(strict, actor), "strict carrierless B and B+C both fail at the original boundary");
            Check(before == CarrierlessSwitchState(strict) && strict.Model.SourceCompatibilityRepairCount == 0,
                "strict carrierless failure never transfers a station, starts a push or consumes RNG");
            GameSimulation portable = CarrierlessSwitchGame(SourceGameplayCompatibility.Portable, station, kind, turbo, moving);
            actor = portable.Model.Players[portable.Model.SourceStationPlayers[station]];
            before = CarrierlessSwitchState(portable);
            Check(!CarrierlessSwitchFault(portable, actor),
                "portable actual four-station defensive caller defers carrierless switching without a source fault");
            Check(before == CarrierlessSwitchState(portable),
                "portable carrierless B and B+C retain every station, actor, ball, animation and RNG value");
            Check(portable.Model.SourceCompatibilityRepairCount == 1 && portable.Model.SourceCompatibilityRepairs.Count == 1,
                "portable carrierless switch records exactly one repair per attempted source call");
            SourceCompatibilityRepair repair = portable.Model.SourceCompatibilityRepairs.Single();
            Check(repair.Kind == SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch && repair.SubjectId == actor.Id
                && repair.SourceTick == 8269 && repair.RawIndex == -1 && repair.EffectiveIndex == -1
                && !repair.RetainedStick.HasValue && repair.SourceContext.Contains("player.c:db_change_player")
                && repair.SourceContext.Contains("deferred"),
                "carrierless switch metadata identifies the retained actor and no invented carrier or target");
            var published = ((ISourceMatchState)new GameSnapshot(portable.Model)).Match;
            Check(published.Compatibility == SourceGameplayCompatibility.Portable
                && published.CompatibilityRepairCount == 1
                && published.CompatibilityRepairs.Single().Kind == SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch,
                "carrierless deferral is visible in the typed immutable public snapshot");
        }
        foreach (SourceGameplayCompatibility mode in new[] { SourceGameplayCompatibility.StrictSource, SourceGameplayCompatibility.Portable })
        {
            foreach (int exit in new[] { 0, 1, 2, 3 })
            {
                GameSimulation game = CarrierlessSwitchGame(mode, 0, SourceBallKind.Kickoff);
                ActorState actor = game.Model.Players[4];
                if (exit == 0) actor.SourceControllerStation = -1;
                if (exit == 1) { game.Model.Phase = GamePhase.DeadBall; actor.SourceRawFlags = 0x2; }
                if (exit == 2) actor.SourceNewButtons = SourceDroneButtons.None;
                if (exit == 3) actor.AnimationState.Secondary.AddMode(0x1000u);
                string before = CarrierlessSwitchState(game);
                Check(!CarrierlessSwitchFault(game, actor) && before == CarrierlessSwitchState(game)
                    && game.Model.SourceCompatibilityRepairCount == 0,
                    "existing drone, play-over onscreen, no-new-B and running-push exits precede any repair");
            }
            foreach (bool moving in new[] { false, true })
            {
                GameSimulation game = CarrierlessSwitchGame(mode, 0, SourceBallKind.Pass, true, moving);
                ActorState actor = game.Model.Players[4];
                SourcePlayerDriver.SetSourcePosition(actor, default(SourceVector3));
                game.Model.SourceRules = new SourceRulesState(0, 0, 1, 50, 100, SourcePlayType.Normal);
                int draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
                Check(!CarrierlessSwitchFault(game, actor) && game.Model.SourceCompatibilityRepairCount == 0,
                    "defined pushHere short circuit never calls the carrierless switch or records a repair");
                Check(moving ? (actor.AnimationState.Secondary.Mode & 0x1000u) != 0
                        && ((SourceInjectedRandom)game.SourceRandomService).Trace.Count == draws
                    : actor.AnimationState.Primary.ScriptSymbol == movement.Movement.Script("push_anim_tbl", 99 % movement.Movement.Count("push_anim_tbl"))
                        && ((SourceInjectedRandom)game.SourceRandomService).Trace.Count > draws,
                    "defined pushHere retains both actual source animation and running-push RNG behavior");
            }
        }
        var definedCases = new List<object>();
        foreach (int station in new[] { 0, 1, 2, 3 })
        foreach (SourceBallKind kind in new[] { SourceBallKind.Pass, SourceBallKind.FieldGoal, SourceBallKind.Kickoff,
            SourceBallKind.Punt, SourceBallKind.Fumble, SourceBallKind.Bobble, SourceBallKind.Lateral })
        foreach (bool held in new[] { false, true })
        foreach (bool alreadyClosest in new[] { false, true })
        {
            if (!held && (kind == SourceBallKind.Pass || kind == SourceBallKind.FieldGoal || kind == SourceBallKind.Kickoff)) continue;
            string previous = null;
            foreach (SourceGameplayCompatibility mode in new[] { SourceGameplayCompatibility.StrictSource, SourceGameplayCompatibility.Portable })
            {
                GameSimulation game = CarrierlessSwitchGame(mode, station, kind);
                SimulationState m = game.Model;
                ActorState actor = m.Players[m.SourceStationPlayers[station]];
                int expected = actor.Team == TeamId.Home ? 0 : 7;
                int otherHuman = actor.Team == TeamId.Home ? 5 : 12;
                if (m.Players[otherHuman].Id != actor.Id)
                    SourcePlayerDriver.SetSourcePosition(m.Players[otherHuman], new SourceVector3(1, 0, 0));
                if (held)
                {
                    m.CarrierId = actor.Team == TeamId.Home ? 10 : 3;
                    SourcePlayerDriver.SetSourcePosition(m.Players[m.CarrierId], default(SourceVector3));
                }
                if (alreadyClosest)
                {
                    SourcePlayerDriver.SetSourcePosition(actor, default(SourceVector3));
                    expected = actor.Id;
                }
                Bind<Action>(game, "CacheSourceGeometry")();
                var input = new {
                    actor = actor.Id, team = (int)actor.Team, station, carrier = m.CarrierId,
                    ballKind = kind.ToString(), offenseSide = (int)m.Possession, playOver = false, onScreen = false,
                    intendedReceiver = m.TargetPlayerId, tx = m.SourceBallTarget.X, tz = m.SourceBallTarget.Z,
                    players = m.Players.Select(a => new { x = SourcePlayerDriver.WorldPosition(a).X,
                        z = SourcePlayerDriver.WorldPosition(a).Z, station = a.SourceControllerStation, unint = false }).ToArray(),
                    angles = Enumerable.Range(0, 14).SelectMany(a => Enumerable.Range(0, 14).Select(b => m.SourceAngles[a, b])).ToArray()
                };
                int preview = Bind<Func<ActorState, bool, int>>(game, "ChangeSourceDefensePlayer")(actor, false);
                Check(preview == (alreadyClosest ? -1 : 0) && m.SourceStationPlayers[station] == actor.Id,
                    "defined switch preview retains original closest-player results without transferring control");
                Check(!CarrierlessSwitchFault(game, actor) && m.SourceStationPlayers[station] == expected
                    && actor.SourceControllerStation == (alreadyClosest ? station : -1)
                    && m.Players[expected].SourceControllerStation == station,
                    "defined carrier and nonseeking free-ball switches retain the original selected teammate");
                Check(m.SourceCompatibilityRepairCount == 0, "defined switching never emits a compatibility repair");
                string after = CarrierlessSwitchState(game);
                if (previous != null) Check(after == previous,
                    "defined strict and portable switching have identical station, actor, ball, animation and RNG state");
                previous = after;
                if (mode == SourceGameplayCompatibility.StrictSource)
                    definedCases.Add(new { input, selected = m.SourceStationPlayers[station] });
            }
        }
        Save("carrierless-defined.json", System.Text.Json.JsonSerializer.Serialize(definedCases));
        foreach (int station in new[] { 0, 1, 2, 3 })
        foreach (bool turbo in new[] { false, true })
        {
            GameSimulation game = CarrierlessSwitchGame(SourceGameplayCompatibility.Portable, station,
                SourceBallKind.Kickoff, turbo);
            ActorState actor = game.Model.Players[game.Model.SourceStationPlayers[station]];
            int[] owners = game.Model.SourceStationPlayers.ToArray();
            bool fault = false;
            try
            {
                Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(actor, default(PlayerCommand));
            }
            catch (NotSupportedException error) when (error.Message.StartsWith(
                "player.c:db_change_player seeking-player branch indexes", StringComparison.Ordinal)) { fault = true; }
            Check(!fault, "actual M1 ExecuteSourceMovementMode reaches Portable carrierless continuity for all four stations");
            Check(game.Model.SourceCompatibilityRepairCount == 1
                && game.Model.SourceStationPlayers.SequenceEqual(owners)
                && game.Model.Players[owners[station]].SourceControllerStation == station,
                "actual M1 execution retains the station and records exactly one defensive-switch deferral");
        }
        foreach (bool hiking in new[] { false, true })
        foreach (int receiver in new[] { -1, 3 })
        {
            GameSimulation game = CarrierlessSwitchGame(SourceGameplayCompatibility.Portable, 0, SourceBallKind.Pass, true);
            game.Model.SourceFlags = hiking ? SourceGameFlags.HikingBall : SourceGameFlags.None;
            game.Model.TargetPlayerId = receiver;
            string before = CarrierlessSwitchState(game);
            Check(!CarrierlessSwitchFault(game, game.Model.Players[4])
                && before == CarrierlessSwitchState(game) && game.Model.SourceCompatibilityRepairCount == 1,
                "carrierless B+C defers with either hiking flag and receiver value without guessing an intended target");
        }
        GameSimulation noAngles = CarrierlessSwitchGame(SourceGameplayCompatibility.Portable, 0, SourceBallKind.Kickoff);
        noAngles.Model.SourceAngles = null;
        Check(Bind<Func<ActorState, bool, int>>(noAngles, "ChangeSourceDefensePlayer")(noAngles.Model.Players[4], false) == -2
            && noAngles.Model.SourceCompatibilityRepairCount == 1,
            "carrierless deferral precedes any angle-cache read rather than supplying a replacement row");
        GameSimulation invalid = CarrierlessSwitchGame(SourceGameplayCompatibility.Portable, 0, SourceBallKind.Kickoff);
        invalid.Model.CarrierId = -2;
        Check(CarrierlessSwitchFault(invalid, invalid.Model.Players[4])
            && invalid.Model.SourceCompatibilityRepairCount == 0,
            "carrierless deferral does not normalize any unsupported negative carrier index");
        GameSimulation retained = CarrierlessSwitchGame(SourceGameplayCompatibility.Portable, 0, SourceBallKind.Kickoff, true);
        ActorState retainedActor = retained.Model.Players[4];
        string retainedBefore = CarrierlessSwitchState(retained);
        for (int i = 0; i < 100; i++)
            Check(Bind<Func<ActorState, bool, int>>(retained, "ChangeSourceDefensePlayer")(retainedActor, false) == -2,
                "portable deferred result is distinct from closest-player minus-one and ordinary zero");
        SourceMatchView snapshot = ((ISourceMatchState)new GameSnapshot(retained.Model)).Match;
        Check(retainedBefore == CarrierlessSwitchState(retained) && snapshot.CompatibilityRepairCount == 100
            && snapshot.CompatibilityRepairs.Count == 64 && snapshot.CompatibilityRepairs[0].Sequence == 37,
            "carrierless repair retains the bounded chronological tail and lifetime count without control mutation");
        retained.Restart();
        Check(retained.Compatibility == SourceGameplayCompatibility.Portable
            && retained.Model.SourceCompatibilityRepairCount == 0
            && snapshot.CompatibilityRepairCount == 100 && snapshot.CompatibilityRepairs.Count == 64,
            "carrierless repairs restart cleanly without changing policy or previously published snapshots");
    }

    private static void CompatibilityChecks()
    {
        Check(real.IsC2M1Profile, "compatibility suite requires actual M1 inputs");
        Check(new GameSimulation().Compatibility == SourceGameplayCompatibility.StrictSource, "legacy stays strict/default");
        Check(SourceGameplayCompatibilityOptions.FromUrl(null) == SourceGameplayCompatibility.StrictSource
            && SourceGameplayCompatibilityOptions.FromUrl("https://game/?other=1") == SourceGameplayCompatibility.StrictSource,
            "no silent mode promotion");
        Check(SourceGameplayCompatibilityOptions.FromUrl("https://game/?source-compatibility=portable")
            == SourceGameplayCompatibility.Portable, "actual host portable query parser");
        foreach (string url in new[] { "?source-compatibility=typo", "?source-compatibility",
            "?source-compatibility=portable&source-compatibility=strict" })
            CompatibilityReject(() => SourceGameplayCompatibilityOptions.FromUrl(url), "unsupported or duplicate compatibility flag rejected");
        GameSimulation strict = CompatibilityGame(SourceGameplayCompatibility.StrictSource);
        GameSimulation portable = CompatibilityGame(SourceGameplayCompatibility.Portable);
        Check(real.Create(configuration: ((ISourceMatchState)strict.State).Match.Configuration,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0))
            .Compatibility == SourceGameplayCompatibility.StrictSource, "actual M1 omitted option stays strict");
        CompatibilitySelect(strict, 0); CompatibilitySelect(portable, 0);
        Check(strict.Compatibility == SourceGameplayCompatibility.StrictSource
            && portable.Compatibility == SourceGameplayCompatibility.Portable, "actual M1 factory explicit immutable modes");
        Check(strict.Model.SourceTeamHandicap.SequenceEqual(portable.Model.SourceTeamHandicap)
            && strict.Model.SourceEffectiveTeamHandicap.SequenceEqual(portable.Model.SourceEffectiveTeamHandicap)
            && strict.Model.SourceCompatibilityRepairCount == 0 && portable.Model.SourceCompatibilityRepairCount == 0,
            "in-domain raw/effective state and no repairs identical");
        var sr = (SourceInjectedRandom)strict.SourceRandomService;
        var pr = (SourceInjectedRandom)portable.SourceRandomService;
        Check(System.Text.Json.JsonSerializer.Serialize(sr.Trace) == System.Text.Json.JsonSerializer.Serialize(pr.Trace)
            && sr.Trace.Count == pr.Trace.Count, "in-domain RNG identical with no compatibility draw");
        CompatibilityReject(() => CompatibilitySelect(strict, 1), "Strict actual lineup rejects raw handicap12");
        Check(strict.Model.SourceTeamHandicap[1] == 12, "Strict rejection retains raw12");
        foreach (int repeats in new[] { 1, 2, 5 })
        {
            portable = CompatibilityGame(SourceGameplayCompatibility.Portable);
            CompatibilitySelect(portable, repeats);
            SimulationState m = portable.Model;
            int raw = 11 + (repeats + 1) / 2;
            Check(m.SourceTeamHandicap[1] == raw && m.SourceEffectiveTeamHandicap[1] == 11,
                "portable raw arithmetic preserved; consumer index saturates11");
            Check(m.SourceAiParameters.Level(1) == 11 && m.SourceHandicapRepetitionBonus == repeats,
                "actual AI consumes effective index without raw/history mutation");
            Check(m.SourceCompatibilityRepairCount == 1 && m.SourceCompatibilityRepairs.Count == 1,
                "portable repair is not silent");
            SourceCompatibilityRepair repair = m.SourceCompatibilityRepairs[0];
            Check(repair.Kind == SourceCompatibilityRepairKind.HandicapConsumerIndex && repair.RawIndex == raw
                && repair.EffectiveIndex == 11 && repair.SubjectId == 1 && repair.SourceTick == m.SourceTick
                && repair.SourceContext.Contains("handicap.c"), "handicap repair exact typed context");
            foreach (ActorState actor in m.Players.Where(a => a.Team == TeamId.Away))
                Check(actor.SourceHandicap == 11 && actor.SourceRawHandicap == raw, "all actor consumers receive effective handicap");
            var snapshot = (ISourceMatchState)new GameSnapshot(m);
            Check(snapshot.Match.AwayGameplay.Handicap == raw && snapshot.Match.AwayGameplay.EffectiveHandicap == 11
                && snapshot.Match.Compatibility == SourceGameplayCompatibility.Portable
                && snapshot.Match.CompatibilityRepairCount == 1, "published raw/effective policy and repair metadata");
            Check(snapshot.Players[8].Source.Handicap == 11 && snapshot.Players[8].Source.RawHandicap == raw,
                "published actor raw/effective values");
        }
        portable = CompatibilityGame(SourceGameplayCompatibility.Portable);
        portable.Model.HomeScore = 100;
        CompatibilitySelect(portable, 2);
        Check(portable.Model.SourceTeamHandicap[0] == -1 && portable.Model.SourceEffectiveTeamHandicap[0] == 0
            && portable.Model.SourceCompatibilityRepairs.Any(r => r.RawIndex == -1 && r.EffectiveIndex == 0),
            "portable lower consumer bound preserves negative raw human handicap");
        CompatibilityReject(() => real.Create(configuration: ((ISourceMatchState)strict.State).Match.Configuration,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            handicapSettings: new SourceHandicapSettings(userBaseAdjustment: -99),
            compatibility: SourceGameplayCompatibility.Portable), "portable still rejects undefined earlier producer row");
        CompatibilityReject(() => CompatibilityGame((SourceGameplayCompatibility)2), "unknown compatibility enum rejected");
        CompatibilityReject(() => CompatibilityBlock(CompatibilityGame(SourceGameplayCompatibility.StrictSource), 6),
            "Strict actual detect caller rejects bc6");
        CompatibilityReject(() => CompatibilityBlock(CompatibilityGame(SourceGameplayCompatibility.Portable), -1),
            "negative block count rejected even portable");
        var last = CompatibilityBlock(CompatibilityGame(SourceGameplayCompatibility.StrictSource), 5);
        foreach (int count in new[] { 6, 7, 50 })
        {
            portable = CompatibilityGame(SourceGameplayCompatibility.Portable);
            var observed = CompatibilityBlock(portable, count);
            Check(last.Calls.SequenceEqual(observed.Calls), "portable bc consumer uses last existing row with identical draws/reactions");
            Check(portable.Model.Players[8].SourceBlockCount == count + 1, "actual raw block count increments without saturation");
            SourceCompatibilityRepair repair = portable.Model.SourceCompatibilityRepairs.Single();
            Check(repair.Kind == SourceCompatibilityRepairKind.BlockCountConsumerIndex && repair.SubjectId == 8
                && repair.RawIndex == count && repair.EffectiveIndex == 5 && repair.SourceTick == 80,
                "bc repair exact raw/effective index and actor/tick");
        }
        portable = CompatibilityGame(SourceGameplayCompatibility.Portable);
        for (int i = 0; i < 100; i++) portable.Model.ResolveCompatibilityIndex(
            SourceCompatibilityRepairKind.HandicapConsumerIndex, 1, 12, 12, "declared-retention");
        var retained = ((ISourceMatchState)new GameSnapshot(portable.Model)).Match;
        Check(retained.CompatibilityRepairCount == 100 && retained.CompatibilityRepairs.Count == 64,
            "bounded repair tail retains lifetime count without notification flood");
        portable.Model.ResolveCompatibilityIndex(SourceCompatibilityRepairKind.HandicapConsumerIndex, 1, 13, 12, "later");
        Check(retained.CompatibilityRepairCount == 100 && retained.CompatibilityRepairs[0].Sequence == 37,
            "published repair snapshots remain immutable after retention updates");
        portable.Restart();
        Check(portable.Compatibility == SourceGameplayCompatibility.Portable && portable.Model.SourceCompatibilityRepairCount == 0,
            "restart preserves opt-in but resets repair state");
        var config = ((ISourceMatchState)portable.State).Match.Configuration;
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        var settings = new SourceMovementSettings(0, 0, false, 0, 0, false);
        FullKernelInputs c1 = FullKernelInputs.Load(Path.Combine(fixtureRoot, "fixture.json"),
            soundFunctionsPath: compatibilitySoundFunctions);
        var c1game = SourceContactComposition.CreateSimulation(c1.Animations, c1.ReferencePivots, c1.Playbook,
            config, c1.Rosters, presentation, c1.MovementRuntime, settings, c1.SoundFunctions);
        Check(c1game.Compatibility == SourceGameplayCompatibility.StrictSource, "actual C1 factory omitted option stays strict");
        CompatibilityReject(() => SourceContactComposition.CreateSimulation(c1.Animations, c1.ReferencePivots,
            c1.Playbook, config, c1.Rosters, presentation, c1.MovementRuntime, settings, c1.SoundFunctions,
            compatibility: SourceGameplayCompatibility.Portable), "actual C1 portable request rejected explicitly");
        Check(!string.IsNullOrEmpty(compatibilityOldInputs), "explicit old-nine inputs required");
        FullKernelInputs old = FullKernelInputs.Load(Path.Combine(fixtureRoot, "fixture.json"),
            soundFunctionsPath: compatibilitySoundFunctions, c2InputsPath: compatibilityOldInputs);
        var oldGame = old.Create(configuration: config, presentation: presentation);
        Check(oldGame.Compatibility == SourceGameplayCompatibility.StrictSource, "actual old-nine default stays strict");
        CompatibilityReject(() => old.Create(configuration: config, presentation: presentation,
            compatibility: SourceGameplayCompatibility.Portable), "actual old-nine portable request rejected explicitly");
        CompatibilityRunningPushChecks();
    }
}
