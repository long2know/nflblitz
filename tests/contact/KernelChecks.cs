using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static int canonicalResultTimingAssertions;
    private static readonly Dictionary<string, object> resultTimingEvidence = new Dictionary<string, object>();

    private static void C2ResultTimingCheck(bool condition, string identity)
    {
        canonicalResultTimingAssertions++;
        Check(condition, "C2 result timing: " + identity);
    }

    private static SourceC2PendingEnd VerifyQueuedPlayerDown(GameSimulation game, ActorState victim, long hit)
    {
        Check(game.Model.Phase == GamePhase.DeadBall && victim.Down && game.SourceTick > hit,
            "actual C2 H_FRONT1 ANI_CODE player_down queues the real result after impact/flight, not a fall timer");
        SourceC2PendingEnd pending = game.Model.SourceC2PendingResult;
        C2ResultTimingCheck(game.SourceTick == 135 && pending != null && pending.QueuedTick == 135 &&
            pending.Epoch == 1 && pending.PlayerId == 4 && pending.AttackerId == 11 &&
            pending.Input.Cause == SourcePlayEndCause.CarrierTackledInBounds,
            "actual source135 saves exactly epoch1/carrier4/attacker11/in-bounds result input");
        C2ResultTimingCheck(game.Model.SourceRules.Down == 1 && game.Model.SourceRules.LineOfScrimmage == 30 &&
            game.Model.SourceRules.FirstDown == 100 && !game.Model.SourceResultView.IsSpecified &&
            game.Model.SourceFlags == (SourceGameFlags)148480 && pending.Input.Flags == game.Model.SourceFlags &&
            JsonSerializer.Serialize(pending.Input.State) == JsonSerializer.Serialize(game.Model.SourceRules),
            "queued state preserves source flags0x24400 and pre-classification Down1/LOS30/FD100 without normalization");
        SourceC2Event[] events = game.C2Events.ToArray();
        C2ResultTimingCheck(events.Length == 2 && events[0].Kind == SourceC2EventKind.WhistleSpawned &&
            events[1].Kind == SourceC2EventKind.PlayOverQueued && events[0].Epoch == 0 && events[1].Epoch == 1 &&
            events.All(e => e.SourceTick == 135 && e.Process == "player_proc" && e.ProcessOrder == 6 && e.ActorId == 4),
            "original qcreate whistle precedes play-over assignment in actual player-proc/order6");
        SourceC2WhistleView whistle = game.C2State.WhistleProcesses.Single();
        C2ResultTimingCheck(whistle.Id == 1 && whistle.CreatedSourceTick == 135 &&
            whistle.FirstWakePending && whistle.SleepRemaining == 1 && game.C2State.ResultPending &&
            !game.SourceEvents.Any(e => e.Type == SourceEventType.PlayResolved) &&
            !events.Any(e => e.Kind == SourceC2EventKind.ResultCommitted),
            "creation does not run whistle or commit a result in the same actor wake");
        SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.ToArray();
        string rows = string.Join("\n", draws.Select(draw => string.Join("|", new[] {
            draw.Order.ToString(), draw.Raw.ToString(), draw.Value.ToString(), draw.Site.SourceTick.ToString(),
            draw.Site.Process, draw.Site.ActorId.ToString(), draw.Site.ProcessOrder.ToString(),
            draw.Site.CallIndex.ToString(), draw.Site.SourceIdentity, draw.Site.BoundOrModulus.ToString() })));
        C2ResultTimingCheck(draws.Length == 71 && game.Model.SourceRandomDraws == 71 &&
            Hash(Encoding.UTF8.GetBytes(rows)) == "a165e0239cb144673be7a9f7c9fc942852a6fb128f22d994b0a27ceb860e8730" &&
            !draws.Any(draw => draw.Site.SourceIdentity.StartsWith("player.c:whistle_proc:", StringComparison.Ordinal)),
            "every recorded71 raw/site/order prefix draw survives; qcreate consumes no chance or index draw");
        resultTimingEvidence["queued"] = new {
            tick = game.SourceTick, pending.QueuedTick, pending.Epoch, pending.PlayerId, pending.AttackerId, pending.Input,
            game.Model.SourceRules, game.Model.SourceFlags, game.Model.SourceResultView, game.C2State,
            events, queuedRawDraws = draws, rawPrefixSha256 = Hash(Encoding.UTF8.GetBytes(rows)),
            victim.Down, victim.SourceCelebrateTime, victim.SourceLateHittable,
            victim.AnimationState.Primary.ScriptSymbol, victim.AnimationState.Primary.SequenceName,
            victim.AnimationState.Primary.FrameOrdinal
        };
        return pending;
    }

    private static int VerifyNextSourceResult(GameSimulation game, SourceC2PendingEnd saved)
    {
        SourceRandomDraw[] newDraws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Skip(71).ToArray();
        C2ResultTimingCheck(game.SourceTick == 136 && newDraws.Length > 0 &&
            newDraws[0].Site.SourceTick == 136 && newDraws[0].Site.Process == "whistle_proc" &&
            newDraws[0].Site.ActorId == -1 && newDraws[0].Site.ProcessOrder == 0 &&
            newDraws[0].Site.CallIndex == 0 && newDraws[0].Site.BoundOrModulus == 100 &&
            newDraws[0].Site.SourceIdentity == "player.c:whistle_proc:6014",
            "first EXISTING next-source wake136 draws original whistle chance first at actor-1/order0/call0");
        SourceC2Event[] events = game.C2Events.ToArray();
        int first = Array.FindIndex(events, e => e.Kind == SourceC2EventKind.WhistleFirstWake);
        int committed = Array.FindIndex(events, e => e.Kind == SourceC2EventKind.ResultCommitted);
        C2ResultTimingCheck(first >= 0 && committed > first &&
            events.Count(e => e.Kind == SourceC2EventKind.WhistleFirstWake) == 1 &&
            events.Count(e => e.Kind == SourceC2EventKind.ResultCommitted) == 1 &&
            events[first].Process == "whistle_proc" && events[first].ProcessOrder == 0 && events[first].ActorId == -1 &&
            events[committed].SourceTick == 136 && events[committed].Process == "game_proc" &&
            events[committed].ProcessOrder == 1 && events[committed].Epoch == saved.Epoch &&
            events[committed].TauntedAfter == false,
            "head whistle runs BEFORE once-only game-proc/order1/epoch1 commit; observe latch on commit event");
        C2ResultTimingCheck(game.Model.SourceC2PendingResult == null && !game.C2State.ResultPending &&
            game.C2State.EndEpoch == 1 && game.Model.SourcePendingTackleAttacker == -1 &&
            game.Model.SourcePendingTackleVictim == -1,
            "commit clears exactly the saved pending epoch and resolution tackle markers");
        SourcePlayResolution expected = SourceMatchRules.Resolve(saved.Input);
        SourceMatchResult expectedView = new SourceMatchResult((SourcePlayKind)saved.Input.State.PlayType,
            (SourceResultKind)expected.Classification.Result, (SourceResultStatus)expected.Classification.Status,
            (SourcePlayEndKind)saved.Input.Cause, expected.Side0Points, expected.Side1Points,
            expected.JustGotFirstDown, expected.TurnoverOnDowns);
        SourceGameEvent[] resolved = game.SourceEvents.Where(e => e.Type == SourceEventType.PlayResolved).ToArray();
        C2ResultTimingCheck(resolved.Length == 1 && resolved[0].SourceTick == 136 &&
            resolved[0].ActorPlayerId == 4 && resolved[0].TargetPlayerId == 11 &&
            JsonSerializer.Serialize(resolved[0].Result) == JsonSerializer.Serialize(expectedView) &&
            JsonSerializer.Serialize(game.Model.SourceResultView) == JsonSerializer.Serialize(expectedView),
            "one external source136 PlayResolved uses actor4/target11 and exact saved-input classification");
        C2ResultTimingCheck(JsonSerializer.Serialize(game.Model.SourceRules) == JsonSerializer.Serialize(expected.State) &&
            game.Model.SourceFlags == expected.Flags && game.Model.SourceLastResult == expected.Classification.Result &&
            Bits(game.Model.SourcePlayEndX) == Bits(expected.PlayEndX) &&
            game.Model.SourceRules.Down == 2 && game.Model.SourceRules.LineOfScrimmage == 50 &&
            game.Model.SourceRules.FirstDown == 100 && expected.Classification.Result == SourcePlayResult.Down &&
            expected.Side0Points == 0 && expected.Side1Points == 0 && game.Model.HomeScore == 0 && game.Model.AwayScore == 0,
            "pinned Resolve(savedInput) commits exact Down/Down2/LOS50/FD100/no-points bookkeeping");
        int chance = newDraws[0].Value;
        SourceC2WhistleView[] remaining = game.C2State.WhistleProcesses.Where(p => p.Id == 1).ToArray();
        C2ResultTimingCheck(chance >= 25 ? remaining.Length == 0 :
            remaining.Length == 1 && !remaining[0].FirstWakePending && remaining[0].SleepRemaining == 30,
            "actual chance>=25 removes process; ONLY success installs original sleep30");
        C2ResultTimingCheck(!events.Any(e => e.Kind == SourceC2EventKind.WhistleEmission) &&
            newDraws.Count(draw => draw.Site.Process == "whistle_proc") == 1 &&
            !newDraws.Any(draw => draw.Site.Process == "whistle_proc" && draw.Site.BoundOrModulus == 2),
            "source136 has no immediate index2/audio; earliest successful emission is source166");
        resultTimingEvidence["nextSourceWake"] = new {
            tick = game.SourceTick, chance, earliestSuccessfulWhistleTick = 166, events, newDraws,
            saved.Input, expected, game.Model.SourceRules, game.Model.SourceFlags, game.Model.SourceResultView,
            game.C2State, resolved
        };
        return chance;
    }
    private static PlayerCommand RawPair(SourceButtons home = SourceButtons.None,
        SourceButtons away = SourceButtons.None, SourceButtons homePressed = SourceButtons.None,
        SourceButtons awayPressed = SourceButtons.None)
    {
        return new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(0, 0, 0, home, homePressed),
            new SourceStationInput(1, 0, 0, away, awayPressed) }));
    }

    private static void Stand(GameSimulation game, ActorState actor)
    {
        actor.SourceMode = SourceActorMode.Dead;
        actor.AnimationState.FullBody("qb_stance_anim", actor, game.SourceTick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
    }
    private static GameSimulation StandingPair(float distance = 9f, GamePhase phase = GamePhase.LiveBall,
        Func<SourceRandomSite, int> raw = null, bool baseOnly = false, int second = 11)
    {
        GameSimulation game = Create(raw, baseOnly: baseOnly);
        PairFixture(game, second: second, separation: distance, phase: phase);
        Stand(game, game.Model.Players[4]); Stand(game, game.Model.Players[second]);
        foreach (ActorState actor in new[] { game.Model.Players[4], game.Model.Players[second] })
        {
            ISourceAnimationClip clip = library.GetClip("QBSTANCE");
            SourceVector3 next = clip.GetFrame(clip.SelectedSourceFrames[1]).ScaledRoot;
            FieldVector drift = SourceAnimationLayerState.Rotate(next.X * actor.ModelScale - actor.SourceRoot.X,
                next.Z * actor.ModelScale - actor.SourceRoot.Z, actor.Facing, actor.SourceDirections);
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(
                (actor.Id == 4 ? 0f : distance) - drift.X, 0f, -drift.Z));
        }
        game.Model.CarrierId = -1; game.Model.BallMode = BallMode.InFlight; game.Model.BallKind = SourceBallKind.Fumble;
        game.Model.SourceBallFlags = SourceBallFlags.Freeze;
        SourceBallDriver.SetPosition(game.Model, new SourceVector3(200f, 90f, 0f));
        Cache(game);
        return game;
    }

    private static void Observe(string name, GameSimulation game, params int[] actors)
    {
        kernelEvidence.Add(new {
            name, source_tick = game.SourceTick, phase = game.Model.Phase.ToString(),
            carrier = game.Model.CarrierId, flags = (uint)game.Model.SourceFlags,
            hit_spot = new[] { game.Model.SourcePlayEndX, game.Model.SourcePlayEndZ },
            pending_attacker = game.Model.SourcePendingTackleAttacker,
            pending_victim = game.Model.SourcePendingTackleVictim,
            events = game.ContactEvents.Select(e => new {
                kind = e.Kind.ToString(), e.SourceTick, e.ProcessOrder, e.ActorPlayerId, e.OtherPlayerId,
                e.AttackMode, e.Direction4, e.Direction8, e.ScriptSymbol,
                world = new[] { e.WorldPosition.X, e.WorldPosition.Y, e.WorldPosition.Z } }).ToArray(),
            actors = actors.Select(id => game.Model.Players[id]).Select(a => new {
                a.Id, mode = a.SourceMode.ToString(), script = a.AnimationState.Primary.ScriptSymbol,
                sequence = a.AnimationState.Primary.SequenceName, frame = a.AnimationState.Primary.FrameOrdinal,
                modes = a.AnimationState.Primary.Mode, flags = GameSnapshot.SourceActorFlags(a),
                world = new[] { SourcePlayerDriver.WorldPosition(a).X, SourcePlayerDriver.WorldPosition(a).Y,
                    SourcePlayerDriver.WorldPosition(a).Z }, root = new[] { a.SourceRoot.X, a.SourceRoot.Y, a.SourceRoot.Z },
                velocity = new[] { a.SourceVelocity.X, a.SourceVerticalVelocity, a.SourceVelocity.Z },
                hit = a.SourceHitPartnerId, puppet = a.SourcePuppetLinkId, a.SourceBlockCount,
                a.SourceAttackMode, a.SourceAttackTime, a.Down }).ToArray(),
            random = ((SourceInjectedRandom)game.SourceRandomService).Trace.Where(r => r.Site.SourceTick == game.SourceTick)
                .Select(r => new { r.Order, r.Raw, r.Value, r.Site.Process, r.Site.ActorId,
                    r.Site.ProcessOrder, r.Site.CallIndex, r.Site.SourceIdentity, r.Site.BoundOrModulus }).ToArray()
        });
    }

    private static void KernelChecks()
    {
        VerifyPrograms();
        var newGame = Create();
        Check(newGame.ContactEvents.Count == 0, "original initialization has no detector wake");
        newGame.Step(default(PlayerCommand));
        Check(newGame.SourceTick == 0 && newGame.ContactEvents.Count == 0, "skipped host frame does not execute contact");
        BodyBoundaries();
        BodyTackleAndDown();
        RawSpinEvades();
        BlockAndPuppet();
        ContactBeforeBall();
        DeadBallPushAndGetUp();
        SpearHelmet();
        LateBodySelection();
        HelpUp();
        MissingAssetsAndUndefinedInputs();
    }

    private static void BodyBoundaries()
    {
        foreach (float distance in new[] { 9.999f, 10f, 10.001f })
        {
            GameSimulation game = StandingPair(distance);
            Wake(game, RawPair());
            bool collided = game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision);
            Check(collided == (distance < 10f), "actual kernel strict body10 boundary " + distance);
            Check((game.Model.Players[4].SourceHitPartnerId == 11) == collided,
                "actual kernel bilateral hit pointer only at reached do_collision " + distance);
            if (collided)
            {
                float gap = game.SourceActorWorldPosition(11).X - game.SourceActorWorldPosition(4).X;
                Check(Math.Abs(gap - 10f) < .00001f && game.Model.SourceDistances[4, 11] < 10f,
                    "actual body displacement uses frozen pre-response 3D cache and does not overwrite source velocities");
            }
            Observe("strict-body-" + distance, game, 4, 11);
        }
        foreach (string gate in new[] { "PF_NOCOLL", "MODE_NOCYL", "airborne3D" })
        {
            GameSimulation game = StandingPair(6f);
            if (gate == "PF_NOCOLL") game.Model.Players[4].SourceRawFlags |= 0x20;
            if (gate == "MODE_NOCYL") game.Model.Players[11].AnimationState.Primary.AddMode(0x200);
            if (gate == "airborne3D")
            {
                SourcePlayerDriver.SetSourcePosition(game.Model.Players[11], new SourceVector3(6f, 9f, 0f));
                game.Model.Players[11].SourceVerticalVelocity = .04f;
            }
            Wake(game, RawPair());
            Check(!game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision),
                "actual kernel no body collision at active source " + gate);
            Observe(gate, game, 4, 11);
        }
        GameSimulation same = StandingPair(9f, second: 5);
        same.Model.Players[5].SourceControllerStation = -1;
        same.Model.SourceStationPlayers[1] = -1;
        Wake(same, RawPair());
        Check(same.ContactEvents.Count(e => e.Kind == SourceContactEventKind.Collision) == 1
            && same.ContactEvents.All(e => e.Kind != SourceContactEventKind.AnimationChanged)
            && same.Model.Players[4].AnimationState.Primary.SequenceName == "QBSTANCE",
            "actual same-team body cylinders are not an opposing tackle or globally ignored");
        Observe("same-team-body", same, 4, 5);
        GameSimulation dead = StandingPair(9f, GamePhase.DeadBall);
        dead.Model.Players[11].AnimationState.Primary.AddMode(2u);
        Wake(dead, RawPair());
        Check(dead.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision)
            && dead.Model.Players[4].SourceHitPartnerId == 11,
            "actual dead-ball detector remains awake; UNINT tackler falls through to source cylinder response");
        Observe("deadball-unint-body", dead, 4, 11);
    }

    private static void BodyTackleAndDown()
    {
        var values = new Random(37);
        GameSimulation game = Create(site => site.SourceIdentity == "plyrseq.c:h_front1_anim" && site.BoundOrModulus == 100
            ? 0 : site.Process.StartsWith("detect", StringComparison.Ordinal)
                ? site.BoundOrModulus == 7 ? 2 : 99 : values.Next(0, 0x1000000));
        PairFixture(game);
        Stand(game, game.Model.Players[11]);
        Wake(game, RawPair());
        ActorState victim = game.Model.Players[4], attacker = game.Model.Players[11];
        Check(victim.AnimationState.Primary.ScriptSymbol == "h_front1_anim"
            && attacker.AnimationState.Primary.ScriptSymbol == "t_stshld_anim",
            "actual source body contact tackles a human WITHOUT A/legacy Tackle action using exact non-turbo table");
        Check(game.Model.Phase == GamePhase.LiveBall && (game.Model.SourceFlags & SourceGameFlags.PlayEnding) != 0
            && game.Model.SourcePendingTackleAttacker == 11 && game.Model.SourcePendingTackleVictim == 4
            && !victim.Down, "actual source impact stops progress but does NOT fabricate an immediate player_down");
        Check(victim.SourceHitPartnerId == 11 && attacker.SourceHitPartnerId == 4,
            "do_collision sets both real actor pointers before their immediate scripts");
        Check(victim.Facing == ((attacker.Facing + 512) & 1023) && victim.SourceVelocity.X < -1f,
            "actual rotate_anim and ANI_SET_FACE_REL_VEL use collided actor heading, never victim/target-radius facing");
        SourceCameraCue camera = game.PresentationCues.Cues.OfType<SourceCameraCue>()
            .Single(c => c.Kind == SourcePresentationCueKind.ContactCameraSelection);
        Check(camera.Context.SourcePath == "detect.c" && camera.Context.ExecutingSymbol == "evade_vs_tackle"
            && camera.Context.SourceLine == 2494 && camera.Selection.EntryCount == 8,
            "actual reached source contact csi_fs_endplay selection/caller is produced once");
        Observe("human-no-A-real-body-tackle", game, 4, 11);
        long hit = game.SourceTick;
        for (int i = 0; i < 120 && game.Model.Phase == GamePhase.LiveBall; i++) Wake(game, RawPair());
        SourceC2PendingEnd savedResult = null;
        if (!game.SourceC2Enabled)
        Check(game.Model.Phase == GamePhase.DeadBall && victim.Down && game.SourceTick > hit
            && game.SourceEvents.Any(e => e.Type == SourceEventType.PlayResolved),
            "actual H_FRONT1 ANI_CODE player_down resolves the real match after impact/flight, not a fall timer");
        else savedResult = VerifyQueuedPlayerDown(game, victim, hit);
        long downTick = game.SourceTick;
        Check(victim.AnimationState.Primary.ScriptSymbol == "m_wob_fu_anim" && !victim.SourceLateHittable,
            "actual post-player_down original plyr_have_ball enters real wobble interpolation BEFORE the late-hit flag");
        int commits = 0, resolutions = 0, whistleChance = -1;
        for (int i = 0; i < 10; i++)
        {
            Wake(game, RawPair());
            if (game.SourceC2Enabled && i == 0) whistleChance = VerifyNextSourceResult(game, savedResult);
            Check(victim.SourceLateHittable == (game.SourceTick - downTick >= 10),
                "actual ten-source-tick wobble ANI_INTTOSEQ precedes PF_LATEHITABLE " + i);
            if (game.SourceC2Enabled)
            {
                commits += game.C2Events.Count(e => e.Kind == SourceC2EventKind.ResultCommitted);
                resolutions += game.SourceEvents.Count(e => e.Type == SourceEventType.PlayResolved);
                C2ResultTimingCheck(commits == 1 && resolutions == 1 && game.C2State.EndEpoch == 1 &&
                    !game.C2State.ResultPending && game.SourceTick == downTick + i + 1 &&
                    !game.C2Events.Any(e => e.Kind == SourceC2EventKind.WhistleEmission),
                    "every existing wobble wake retains one commit/resolution and no premature whistle " + i);
            }
        }
        Check(game.State.Players[4].Source.Contact.HitPartnerPlayerId == 11
            && game.State.Players[4].Source.Contact.PuppetPlayerId == -1,
            "published actual source contact state is immutable and independently specified");
        Observe("real-player-down-and-wobble", game, 4, 11);
        if (game.SourceC2Enabled)
        {
            resultTimingEvidence["unchangedTenWakeWindow"] = new {
                downTick, lastTick = game.SourceTick, commits, resolutions, whistleChance, victim.SourceLateHittable,
                game.C2State, canonicalResultTimingAssertions, noAddedWakeOrInputChange = true
            };
            File.WriteAllText(Path.Combine(output, "c2-player-down-result-timing.json"),
                JsonSerializer.Serialize(resultTimingEvidence, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
    }

    private static void RawSpinEvades()
    {
        var random = new Random(551);
        GameSimulation game = Create(site => site.Process.StartsWith("detect", StringComparison.Ordinal) ? 0 : random.Next(0, 0x1000000));
        PairFixture(game, separation: 8f);
        Stand(game, game.Model.Players[11]);
        game.Model.Players[4].NonTurboTicks = 1;
        Wake(game, RawPair(SourceButtons.C, homePressed: SourceButtons.C));
        Check(game.Model.SourceSpinCount[0] == 1 && game.Model.Players[4].AnimationState.Primary.SequenceName == "SPIN",
            "raw human turbo edge causally produces the actual M1 spin before C1, not an injected evade label");
        Check(game.Model.Players[11].AnimationState.Primary.ScriptSymbol == "h_flailm_anim"
            && game.Model.SourcePendingTackleVictim == -1
            && (game.Model.SourceFlags & SourceGameFlags.PlayEnding) == 0,
            "actual source spin avoidance flails the tackler and leaves the carrier/play live");
        Observe("raw-C-spin-evades-contact", game, 4, 11);
    }

    private static void BlockAndPuppet()
    {
        foreach (bool humanRusher in new[] { true, false })
        {
            GameSimulation game = Create(site => site.Process.StartsWith("detect", StringComparison.Ordinal) ? 0 : 99);
            PairFixture(game, first: 1, second: 8);
            Stand(game, game.Model.Players[1]); Stand(game, game.Model.Players[8]);
            game.Model.CarrierId = 3; game.Model.Players[3].SourceMode = SourceActorMode.Quarterback;
            game.Model.Players[1].SourceJob = SourceActorJob.Block;
            game.Model.Players[1].SourceControllerStation = -1;
            game.Model.SourceStationPlayers[0] = -1;
            game.Model.Players[8].SourceControllerStation = humanRusher ? 1 : -1;
            game.Model.SourceStationPlayers[1] = humanRusher ? 8 : -1;
            Wake(game, RawPair());
            ActorState blocker = game.Model.Players[1], rusher = game.Model.Players[8];
            Check(rusher.SourceBlockCount == 1 && game.Model.SourcePendingTackleVictim == -1,
                "actual JOB_BLOCK caller increments the original rusher count and does not tackle the unrelated carrier");
            if (humanRusher)
            {
                Check(blocker.AnimationState.Primary.ScriptSymbol == "pushbl1_anim"
                    && rusher.AnimationState.Primary.ScriptSymbol == "pushbl1_anim"
                    && blocker.SourcePuppetLinkId == -1 && rusher.SourcePuppetLinkId == -1,
                    "actual human-rusher probability columns choose concrete non-puppet block animations");
            }
            else
            {
                Check(blocker.SourcePuppetLinkId == 8 && rusher.SourcePuppetLinkId == 1
                    && blocker.AnimationState.Primary.ScriptSymbol == "tie1c_v_anim"
                    && rusher.AnimationState.Primary.ScriptSymbol == "tie1c_a_anim",
                    "actual drone-rusher block table creates BOTH real puppet scripts/links");
                uint modes = blocker.AnimationState.Primary.Mode | rusher.AnimationState.Primary.Mode;
                Check((modes & 0x00c00000u) == 0x00c00000u,
                    "actual puppet/puppeteer modes come from original ANI programs, not animation labels");
                for (int i = 0; i < 12; i++) Wake(game, RawPair());
                Check(blocker.AnimationState.Primary.FrameOrdinal > 0 && rusher.AnimationState.Primary.FrameOrdinal > 0
                    && blocker.Facing == rusher.Facing && blocker.SourceHeight == rusher.SourceHeight,
                    "actual linked actors execute selected WAN frames and original set_puppet_pos facing/Y consequences");
                SourceMovementState before = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(blocker);
                Bind<Action<ActorState>>(game, "IntegrateSourceMovementPlayer")(blocker);
                SourceVector3 after = game.SourceActorWorldPosition(1);
                Check(Bits(before.X) == Bits(after.X) && Bits(before.Y) == Bits(after.Y)
                    && Bits(before.Z) == Bits(after.Z) && Bits(before.VelocityY) == Bits(blocker.SourceVerticalVelocity),
                    "puppet victim does not integrate gravity independently");
            }
            Observe(humanRusher ? "human-concrete-block" : "drone-concrete-puppet-block", game, 1, 8);
        }
    }

    private static void DeadBallPushAndGetUp()
    {
        GameSimulation game = StandingPair(9f, GamePhase.DeadBall, site => 99);
        game.Model.CarrierId = 3; game.Model.BallMode = BallMode.Dead;
        ActorState attacker = game.Model.Players[11], victim = game.Model.Players[4];
        attacker.Facing = attacker.TargetFacing = 768;
        attacker.AnimationState.FullBody("t_push_anim", attacker, game.SourceTick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        bool attacked = false, getUp = false, returned = false;
        for (int i = 0; i < 180; i++)
        {
            Wake(game, RawPair());
            attacked |= game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision && e.AttackMode == 1);
            getUp |= victim.Action == ActorAction.GetUp && victim.AnimationState.Primary.SequenceName == "GET_UP_B";
            if (attacked && getUp && victim.SourceImmediateGeneralRuns > 0) { returned = true; break; }
        }
        Check(attacked, "actual UNINT attack ANI_ATTACK_ON capsule executes during DEAD BALL with no carrier-only dispatcher");
        Check(getUp && returned && !victim.Down && victim.SourcePuppetLinkId == -1,
            "actual noncarrier push reaction reaches original get-up clip/callback and immediate GeneralRun");
        Check(game.Model.Phase == GamePhase.DeadBall && game.Model.SourcePendingTackleVictim == -1,
            "noncarrier/deadball attack changes actor state without fabricating a live-ball tackle");
        Observe("deadball-attack-reaction-real-getup", game, 4, 11);
    }

    private static void ContactBeforeBall()
    {
        GameSimulation game = StandingPair(9f, raw: site => 99);
        game.Model.Players[4].SourceJob = SourceActorJob.Block;
        game.Model.SourceBallFlags = SourceBallFlags.None;
        game.Model.SourceBallVerticalVelocity = -1f;
        SourceBallDriver.SetPosition(game.Model, new SourceVector3(200f, 1f, 0f));
        Wake(game, RawPair());
        SourceRandomDraw[] trace = ((SourceInjectedRandom)game.SourceRandomService).Trace
            .Where(r => r.Site.SourceTick == game.SourceTick).ToArray();
        SourceRandomDraw[] contact = trace.Where(r => r.Site.Process == "detect_proc").ToArray();
        SourceRandomDraw[] ball = trace.Where(r => r.Site.Process == "ball_proc").ToArray();
        Check(contact.Length >= 2 && ball.Length > 0 && contact.Max(r => r.Order) < ball.Min(r => r.Order)
            && game.Model.SourceBallBounces == 1,
            "actual RL_COLLIS wake/VM/selection completes BEFORE real ball_proc bounce RNG, not detect_after_ball");
        Check(game.Model.Players[4].SourceBlockCount + game.Model.Players[11].SourceBlockCount == 1,
            "the measured contact-before-ball row actually executes the original block result producer");
        Observe("real-collision-before-ball-bounce", game, 4, 11);
    }

    private static void SpearHelmet()
    {
        foreach (bool secretHead in new[] { false, true })
        {
            var random = new Random(552);
            GameSimulation game = Create(site => site.SourceIdentity.StartsWith("player.c:maybe_pop_helmet", StringComparison.Ordinal)
                ? 0 : site.Process.StartsWith("detect", StringComparison.Ordinal) ? 99 : random.Next(0, 0x1000000),
                presentation: new SourcePresentationSettings(TeamId.Home, new[] { secretHead ? 3 : -1, -1, -1, -1 }, 0));
            PairFixture(game, separation: 16f);
            Stand(game, game.Model.Players[4]); Stand(game, game.Model.Players[11]);
            game.Model.CarrierId = 4; game.Model.BallMode = BallMode.Held;
            ActorState attacker = game.Model.Players[11], victim = game.Model.Players[4];
            attacker.AnimationState.FullBody("t_sprhed_anim", attacker, game.SourceTick,
                Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
            bool hit = false;
            for (int i = 0; i < 25; i++)
            {
                Wake(game, RawPair());
                if (game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.Collision && e.AttackMode == 5)) { hit = true; break; }
            }
            Check(hit && attacker.SourceAttackMode == 0 && victim.AnimationState.Primary.ScriptSymbol != "qb_stance_anim",
                "actual source spear ANI_ATTACK_ON reaches camera-capsule dispatcher and concrete reaction");
            Check(victim.SourceHelmetPopped == !secretHead,
                "actual spear->maybe_pop_helmet preserves human secret-head guard " + secretHead);
            SourceRandomDraw[] helmet = ((SourceInjectedRandom)game.SourceRandomService).Trace
                .Where(r => r.Site.SourceIdentity.StartsWith("player.c:maybe_pop_helmet", StringComparison.Ordinal)).ToArray();
            Check(helmet.Length == (secretHead ? 1 : 2) && helmet.All(r => r.Site.BoundOrModulus == 100),
                "helmet always draws first, then short-circuits exact secret-head or second probability input");
            Observe(secretHead ? "actual-spear-secret-head-guard" : "actual-spear-helmet-pop", game, 4, 11);
        }
    }

    private static void PrepareWobbleContact(GameSimulation game, ActorState victim, ActorState other)
    {
        other.SourceRawFlags |= 0x20;
        victim.AnimationState.FullBody("m_wob_fu_anim", victim, game.SourceTick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        for (int i = 0; i < 10; i++) Wake(game, RawPair());
        Check(victim.SourceLateHittable, "actual source wobble interpolation produces the subsequent contact input flag");
        SourcePlayerDriver.SetSourcePosition(victim, new SourceVector3(0f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(other, new SourceVector3(8f, 0f, 0f));
        victim.SourceVelocity = other.SourceVelocity = default(FieldVector);
        other.SourceRawFlags &= ~0x20;
    }

    private static void HelpUp()
    {
        GameSimulation game = StandingPair(9f, GamePhase.DeadBall, site => 99, second: 5);
        ActorState down = game.Model.Players[4], helper = game.Model.Players[5];
        helper.SourceControllerStation = -1; game.Model.SourceStationPlayers[1] = -1;
        game.Model.CarrierId = 4; game.Model.BallMode = BallMode.Dead;
        PrepareWobbleContact(game, down, helper);
        Wake(game, RawPair());
        Check(down.SourcePuppetLinkId == 5 && helper.SourcePuppetLinkId == 4
            && down.AnimationState.Primary.ScriptSymbol == "p_hlpup_back_v_anim"
            && helper.AnimationState.Primary.ScriptSymbol == "p_hlpup_back_a_anim"
            && !down.SourceLateHittable,
            "actual same-team late-hittable source call creates help-up puppet and clears original victim flag");
        Observe("source-same-team-help-up", game, 4, 5);
        bool released = false;
        for (int i = 0; i < 150; i++)
        {
            Wake(game, RawPair());
            if (game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.PuppetReleased)) { released = true; break; }
        }

        Check(released && down.SourcePuppetLinkId == -1 && helper.SourcePuppetLinkId == -1
            && ((down.AnimationState.Primary.Mode | helper.AnimationState.Primary.Mode) & 0x00c00000u) == 0,
            "actual help-up ANI_CODE release_puppet clears bilateral links/modes at the real source frame");
        Observe("source-puppet-release", game, 4, 5);
    }

    private static void LateBodySelection()
    {
        GameSimulation game = StandingPair(9f, GamePhase.DeadBall, site => 2);
        ActorState victim = game.Model.Players[4], attacker = game.Model.Players[11];
        game.Model.CarrierId = 4; game.Model.BallMode = BallMode.Dead;
        PrepareWobbleContact(game, victim, attacker);
        Wake(game, RawPair());
        Check(victim.SourceLateHittable && attacker.AnimationState.Primary.ScriptSymbol == "m_nostomp",
            "real wobble flag and human late body contact select original m_nostomp table entry without action heuristics");
        Check(attacker.TargetFacing == game.Model.SourceAngles[11, 4] && attacker.SourceAttackMode == 0,
            "actual reachable late-hit face_carrier updates target facing, not an invented attack or facing snap");
        Observe("real-late-body-nostomp", game, 4, 11);
    }

    private static void MissingAssetsAndUndefinedInputs()
    {
        GameSimulation game = Create(site => 0, baseOnly: true);
        PairFixture(game, first: 1, second: 8);
        Stand(game, game.Model.Players[1]); Stand(game, game.Model.Players[8]);
        game.Model.CarrierId = 3; game.Model.Players[3].SourceMode = SourceActorMode.Quarterback;
        game.Model.Players[1].SourceJob = SourceActorJob.Block;
        game.Model.Players[1].SourceControllerStation = game.Model.Players[8].SourceControllerStation = -1;
        game.Model.SourceStationPlayers[0] = game.Model.SourceStationPlayers[1] = -1;
        bool failed = false;
        try { Wake(game, RawPair()); }
        catch (InvalidOperationException e) { failed = e.Message.Contains("missing clip TIE1C_A"); }
        Check(failed, "immutable157 cannot silently substitute FALL_04 for a reached required C1 TIE1C_A asset");
        Observe("visible-immutable157-asset-dependency", game, 1, 8);
        int draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        long failedTick = game.SourceTick;
        failed = false;
        try { game.Step(default(PlayerCommand)); }
        catch (InvalidOperationException e) { failed = e.Message.Contains("Restart is required"); }
        Check(failed && game.SourceTick == failedTick && ((SourceInjectedRandom)game.SourceRandomService).Trace.Count == draws,
            "partial C1 VM failure cannot retry, publish an old clip, or consume another RNG draw before Restart");
        game.Restart();
        Check(game.SourceTick == 0 && game.Model.SourceAnimationFailure == null && game.ContactEvents.Count == 0,
            "explicit Restart resets the failed C1 execution and transient events, never replays a failed contact");
        game = StandingPair(8f);
        ActorState attacker = game.Model.Players[11];
        attacker.SourceAttackMode = 1; attacker.SourceAttackJointA = attacker.SourceAttackJointB = 4;
        attacker.SourceAttackTime = 20;
        failed = false;
        try { Wake(game, RawPair()); }
        catch (NotSupportedException e) { failed = e.Message.Contains("spherical attack zone"); }
        Check(failed, "actual source spherical-attack lockup is visible unsupported, never a fabricated radius hit");
        Observe("visible-source-spherical-lockup", game, 4, 11);
    }
}
