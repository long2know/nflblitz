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
