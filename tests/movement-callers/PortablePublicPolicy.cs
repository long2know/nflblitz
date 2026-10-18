using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private const string PortablePolicyName = "portable-cabinet-wide-pass-clearance";
    private const string PortablePolicyVersion = "approved-portable-public-policy/v1";

    private readonly struct PortableVisiblePlayer
    {
        public int Id { get; }
        public TeamId Team { get; }
        public FieldVector Position { get; }
        public ActorRole Role { get; }
        public float Turbo01 { get; }
        internal PortableVisiblePlayer(int id, TeamId team, FieldVector position, ActorRole role, float turbo)
        { Id = id; Team = team; Position = position; Role = role; Turbo01 = turbo; }
    }

    private sealed class PortableVisible
    {
        public GamePhase Phase { get; }
        public TeamId Possession { get; }
        public int ControlledId { get; }
        public int CarrierId { get; }
        public SourcePlayKind PlayKind { get; }
        public bool HomeFire { get; }
        public bool AwayFire { get; }
        public int LastReceiver { get; }
        public int Completions { get; }
        public IReadOnlyList<PortableVisiblePlayer> Players { get; }

        internal PortableVisible(GamePhase phase, TeamId possession, int controlled, int carrier,
            IEnumerable<PortableVisiblePlayer> players, SourcePlayKind kind = SourcePlayKind.Normal,
            bool homeFire = false, bool awayFire = false, int lastReceiver = -1, int completions = 0)
        {
            Phase = phase; Possession = possession; ControlledId = controlled; CarrierId = carrier;
            Players = Array.AsReadOnly(players.ToArray()); PlayKind = kind;
            HomeFire = homeFire; AwayFire = awayFire; LastReceiver = lastReceiver; Completions = completions;
        }

        internal static PortableVisible Project(IGameState state)
        {
            SourceMatchView match = ((ISourceMatchState)state).Match;
            return new PortableVisible(state.Phase, state.Possession, state.ControlledPlayerId, state.Ball.CarrierId,
                state.Players.Select(p => new PortableVisiblePlayer(p.Id, p.Team, p.Position, p.Role, p.Turbo01)),
                match.PlayKind, match.HomeGameplay.OnFire, match.AwayGameplay.OnFire,
                match.HomeGameplay.LastReceiverPlayerId, match.HomeGameplay.ConsecutiveCompletions);
        }
    }

    private readonly struct PortableIntent
    {
        public int X { get; }
        public int Z { get; }
        public SourceButtons Held { get; }
        public SourceCommand Menu { get; }
        public string Decision { get; }
        internal PortableIntent(int x = 0, int z = 0, SourceButtons held = SourceButtons.None,
            SourceCommand menu = default(SourceCommand), string decision = "neutral")
        { X = x; Z = z; Held = held; Menu = menu; Decision = decision; }
    }

    private sealed class PortableCabinetPackets
    {
        private SourceButtons previous;
        internal PlayerCommand Encode(PortableIntent intent)
        {
            SourceButtons pressed = intent.Held & ~previous, released = previous & ~intent.Held;
            previous = intent.Held;
            return new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
                new SourceStationInput(0, intent.X, intent.Z, intent.Held, pressed, released, intent.Menu)
            }));
        }
    }

    private sealed class PortablePublicController
    {
        internal const int CameraHeading = 203, MenuWait = 24, SnapWait = 75, ContinueWait = 30;
        internal const int PassEarliest = 18, PassDeadline = 60, PassRepeat = 30;
        internal const float BurstDistance = 5f, ClearDistance = 7f, MinTurbo = .08f;
        private GamePhase previousPhase = (GamePhase)(-1);
        private int phaseHosts, previousCarrier = -1, carrierHosts, lastPass = -1000, escapeSide;
        private bool menuSent, snapSent, continued, turboHeld;

        internal static PortableIntent World(float x, float z, SourceButtons held, string decision)
        {
            double length = Math.Sqrt(x * x + z * z);
            if (length == 0d) return new PortableIntent(held: held, decision: decision);
            double theta = CameraHeading * (2d * Math.PI / 1024d);
            int sx = (int)Math.Round((x * Math.Cos(theta) - z * Math.Sin(theta)) / length * 3d,
                MidpointRounding.AwayFromZero);
            int sz = (int)Math.Round((z * Math.Cos(theta) + x * Math.Sin(theta)) / length * 3d,
                MidpointRounding.AwayFromZero);
            return new PortableIntent(sx, sz, held, decision: decision);
        }

        private static float Nearest(PortableVisible view, PortableVisiblePlayer player)
        {
            float squared = float.PositiveInfinity;
            foreach (PortableVisiblePlayer other in view.Players)
            {
                if (other.Team == player.Team) continue;
                float x = player.Position.X - other.Position.X, z = player.Position.Z - other.Position.Z;
                squared = Math.Min(squared, x * x + z * z);
            }
            return (float)Math.Sqrt(squared);
        }

        internal PortableIntent Choose(PortableVisible view)
        {
            if (view.Phase != previousPhase)
            {
                previousPhase = view.Phase; phaseHosts = 0;
                menuSent = snapSent = continued = turboHeld = false;
                previousCarrier = -1; carrierHosts = 0; lastPass = -1000;
            }
            phaseHosts++;
            if (view.Phase == GamePhase.PlaySelect && phaseHosts >= MenuWait && !menuSent)
            {
                menuSent = true;
                bool offense = view.Possession == TeamId.Home;
                return new PortableIntent(menu: new SourceCommand(SourceAction.SelectPlay,
                    new SourcePlaySelection(offense ? "play_hail_mary" : "play_suicide_blitz",
                        offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense)),
                    decision: offense ? "menu-hail-mary" : "menu-suicide-blitz");
            }
            if (view.Phase == GamePhase.PreSnap && phaseHosts >= SnapWait && !snapSent)
            {
                snapSent = true;
                return new PortableIntent(held: SourceButtons.B, decision: "cabinet-B-snap-after-public-wait");
            }
            if (view.Phase == GamePhase.DeadBall && phaseHosts >= ContinueWait && !continued)
            {
                continued = true;
                return new PortableIntent(menu: new SourceCommand(SourceAction.Continue), decision: "menu-continue");
            }
            if (view.Phase != GamePhase.LiveBall) return new PortableIntent();
            PortableVisiblePlayer[] controlled = view.Players.Where(p => p.Id == view.ControlledId).ToArray();
            PortableVisiblePlayer[] carriers = view.Players.Where(p => p.Id == view.CarrierId).ToArray();
            if (controlled.Length != 1 || carriers.Length != 1) return new PortableIntent();
            PortableVisiblePlayer human = controlled[0], carrier = carriers[0];
            if (human.Team != TeamId.Home) return new PortableIntent();
            if (carrier.Team != human.Team)
            {
                // A declared wide safety drill, not pursuit that guarantees the six-yard sprint override.
                float targetZ = carrier.Position.Z >= 0f ? -20f : 20f;
                return World(1f, Math.Max(-1f, Math.Min(1f, (targetZ - human.Position.Z) * .2f)),
                    SourceButtons.C, "defense-wide-away-from-carrier");
            }
            if (human.Id != carrier.Id) return new PortableIntent(decision: "wait-real-control-transfer");
            if (previousCarrier != carrier.Id)
            {
                previousCarrier = carrier.Id; carrierHosts = 0; lastPass = -1000; turboHeld = false;
                escapeSide = carrier.Position.Z <= 0f ? -1 : 1;
            }
            carrierHosts++;
            if (carrier.Role == ActorRole.Quarterback && view.PlayKind != SourcePlayKind.Kickoff)
            {
                PortableVisiblePlayer[] receivers = view.Players.Where(p => p.Team == carrier.Team &&
                    p.Id != carrier.Id && p.Id % 7 >= 3 && p.Role == ActorRole.Receiver)
                    .OrderBy(p => p.Position.Z).ThenBy(p => p.Id).ToArray();
                if (receivers.Length == 0) return new PortableIntent(decision: "pocket-no-visible-outlet");
                PortableVisiblePlayer left = receivers[0], right = receivers[receivers.Length - 1];
                Func<PortableVisiblePlayer, float> score = p => Nearest(view, p) +
                    .15f * Math.Max(0f, p.Position.X - carrier.Position.X) +
                    (view.Completions > 0 && p.Id == view.LastReceiver ? 6f : 0f);
                bool aimLeft = score(left) >= score(right);
                PortableVisiblePlayer chosen = aimLeft ? left : right;
                bool open = chosen.Position.X - carrier.Position.X >= 4f && Nearest(view, chosen) >= 3f;
                if (carrierHosts >= PassEarliest && (open || carrierHosts >= PassDeadline) &&
                    carrierHosts - lastPass >= PassRepeat)
                {
                    lastPass = carrierHosts;
                    // Original pick_receiver uses raw columns, not the world-steering rotation.
                    return new PortableIntent(aimLeft ? 3 : -3, 3, SourceButtons.B,
                        decision: aimLeft ? "cabinet-B-left-outlet" : "cabinet-B-right-outlet");
                }
                return new PortableIntent(decision: "pocket-wait-visible-outlet");
            }
            float nearest = Nearest(view, carrier);
            if (nearest <= BurstDistance && carrier.Turbo01 > MinTurbo) turboHeld = true;
            if (nearest >= ClearDistance || carrier.Turbo01 <= MinTurbo) turboHeld = false;
            float lateral = (escapeSide * 14f - carrier.Position.Z) * .18f;
            foreach (PortableVisiblePlayer other in view.Players)
            {
                if (other.Team == carrier.Team) continue;
                float ahead = other.Position.X - carrier.Position.X;
                float dz = carrier.Position.Z - other.Position.Z;
                if (ahead > -1f && ahead < 6f && Math.Abs(dz) < 5f)
                    lateral += (dz == 0f ? escapeSide : Math.Sign(dz)) * (6f - ahead) * .25f;
            }
            lateral = Math.Max(-1f, Math.Min(1f, lateral));
            if (carrier.Position.Z < -19f) lateral = 1f;
            if (carrier.Position.Z > 19f) lateral = -1f;
            return World(1f, lateral, turboHeld ? SourceButtons.C : SourceButtons.None,
                turboHeld ? "carrier-clearance-burst" : "carrier-clearance-run");
        }
    }

    private static int PortablePolicyChecks()
    {
        var policy = new PortablePublicController();
        var packets = new PortableCabinetPackets();
        Func<GamePhase, PortableVisible> phase = p => new PortableVisible(p, TeamId.Home, 3, -1,
            new PortableVisiblePlayer[0]);
        for (int host = 1; host < PortablePublicController.MenuWait; host++)
            Check(!policy.Choose(phase(GamePhase.PlaySelect)).Menu.IsSpecified, "portable public policy keeps cabinet menu intro");
        Check(policy.Choose(phase(GamePhase.PlaySelect)).Menu.Play.PlaySymbol == "play_hail_mary",
            "portable public policy selects one original Hail Mary option");
        Check(!policy.Choose(phase(GamePhase.PlaySelect)).Menu.IsSpecified, "portable public menu does not duplicate FIFO command");
        for (int host = 1; host < PortablePublicController.SnapWait; host++)
            Check(policy.Choose(phase(GamePhase.PreSnap)).Held == SourceButtons.None, "portable public B waits beyond source auto-hike guard");
        Check(policy.Choose(phase(GamePhase.PreSnap)).Held == SourceButtons.B, "portable public snap is actual cabinet B");
        Check(policy.Choose(phase(GamePhase.PreSnap)).Held == SourceButtons.None, "portable public B pulse is not held/repeated");
        var qb = new PortableVisiblePlayer(3, TeamId.Home, new FieldVector(25f, 0f), ActorRole.Quarterback, 1f);
        var left = new PortableVisiblePlayer(4, TeamId.Home, new FieldVector(35f, -10f), ActorRole.Receiver, 1f);
        var right = new PortableVisiblePlayer(6, TeamId.Home, new FieldVector(35f, 10f), ActorRole.Receiver, 1f);
        var defender = new PortableVisiblePlayer(10, TeamId.Away, new FieldVector(35f, 10.5f), ActorRole.Defender, 1f);
        var live = new PortableVisible(GamePhase.LiveBall, TeamId.Home, 3, 3, new[] { qb, left, right, defender });
        PortableIntent pass = default(PortableIntent);
        for (int host = 0; host < PortablePublicController.PassEarliest; host++) pass = policy.Choose(live);
        Check(pass.Held == SourceButtons.B && pass.X == 3 && pass.Z == 3,
            "portable public pass aims at visible open left group using source raw columns");
        Check(policy.Choose(live).Held == SourceButtons.None, "portable public pass edge releases without extra wake");
        var repeated = new PortablePublicController();
        var priorReceiver = new PortableVisible(GamePhase.LiveBall, TeamId.Home, 3, 3, new[] { qb, left, right,
            new PortableVisiblePlayer(10, TeamId.Away, new FieldVector(50f, 0f), ActorRole.Defender, 1f) },
            lastReceiver: 6, completions: 2);
        for (int host = 0; host < PortablePublicController.PassEarliest; host++) pass = repeated.Choose(priorReceiver);
        Check(pass.X == -3 && pass.Held == SourceButtons.B, "portable public repeat preference reads actual visible completion counter only");
        var runner = new PortableVisiblePlayer(4, TeamId.Home, new FieldVector(50f, -12f), ActorRole.Receiver, 1f);
        var close = new PortableVisible(GamePhase.LiveBall, TeamId.Home, 4, 4, new[] { runner,
            new PortableVisiblePlayer(10, TeamId.Away, new FieldVector(54f, -12f), ActorRole.Defender, 1f) });
        Check(policy.Choose(close).Held == SourceButtons.C, "portable public runner uses a legal C burst when close");
        var clear = new PortableVisible(GamePhase.LiveBall, TeamId.Home, 4, 4, new[] { runner,
            new PortableVisiblePlayer(10, TeamId.Away, new FieldVector(57f, -12f), ActorRole.Defender, 1f) });
        Check(policy.Choose(clear).Held == SourceButtons.None,
            "portable public C releases at actual seven-yard clearance BEFORE enemy twenty; no old82-yard prerequisite");
        Check(policy.Choose(new PortableVisible(GamePhase.LiveBall, TeamId.Away, 3, 10, new[] { qb, defender }))
            .Decision == "defense-wide-away-from-carrier", "portable public drill does not chase every opposing taunt candidate");
        PlayerCommand pressed = packets.Encode(new PortableIntent(3, 3, SourceButtons.B));
        PlayerCommand released = packets.Encode(new PortableIntent());
        Check(pressed.StationInputs.Stations[0].Pressed == SourceButtons.B &&
            released.StationInputs.Stations[0].Released == SourceButtons.B,
            "portable public packets retain actual pressed/released B edges through production constructors");
        for (int x = -10; x <= 10; x++)
        for (int z = -10; z <= 10; z++)
        {
            PortableIntent intent = PortablePublicController.World(x, z, SourceButtons.C, "synthetic-bound");
            SourceStationInput actual = packets.Encode(intent).StationInputs.Stations[0];
            Check(actual.StickX >= -3 && actual.StickX <= 3 && actual.StickZ >= -3 && actual.StickZ <= 3 &&
                ((int)actual.Held & ~7) == 0 && !actual.Command.IsSpecified,
                "portable public declared synthetic world steering stays in actual legal cabinet axes/ABC");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "portable-public-policy-checks/v1",
            status = "passed", assertions, actualGameConstructions = 0, actualGameSteps = 0 }));
        return 0;
    }

    private static int AuthorPortablePublicPolicy(string policyPath, IDictionary<string, object> summary)
    {
        Check(real.IsC2M1Profile && gameplayCompatibility == SourceGameplayCompatibility.Portable,
            "portable public policy requires explicit actual M1 Portable factory, not default promotion");
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(policyPath)))
        {
            JsonElement p = document.RootElement;
            Check(p.GetProperty("name").GetString() == PortablePolicyName &&
                p.GetProperty("version").GetString() == PortablePolicyVersion &&
                p.GetProperty("maxHosts").GetInt32() == 12000 &&
                p.GetProperty("maximumInitialWindows").GetInt32() == 1,
                "portable public policy is one frozen declared new-generation scenario");
            foreach (JsonProperty item in p.GetProperty("frozenCodeSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(Path.Combine(p.GetProperty("worktree").GetString(), item.Name))) ==
                    item.Value.GetString(), "portable public policy preexecution hash matches: " + item.Name);
        }
        string policySha = CanonicalHash(File.ReadAllBytes(policyPath));
        using (var marker = new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(policyPath)),
            "only-public-window-started.json"), FileMode.CreateNew))
        {
            byte[] value = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { policySha, maximumInitialWindows = 1, maxHosts = 12000 }));
            marker.Write(value, 0, value.Length);
        }
        int[] rawVector = { 0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
            2147483645, 2147483646, 2147483647 };
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera: false, random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length],
            new SourceRandomProvenance("source-sound-smoke/raw-vector-v1",
                "Explicit repeated raw vector shared by ALL sites, not target libc PRNG",
                "canonical281 actual composition entry", SourceRandomEvidence.ExternalUnverified)),
            compatibility: SourceGameplayCompatibility.Portable);
        Check(shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560"),
            "portable public policy retains same global vector and BOTH original bootstrap draws");
        var cache = new CanonicalWitness(game, false);
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
            cache.Camera(tick); return heading;
        };
        var policy = new PortablePublicController();
        var packets = new PortableCabinetPackets();
        var options = new JsonSerializerOptions { IncludeFields = true };
        var first = new Dictionary<string, Tuple<long, int, string>>();
        var poses = new List<object>();
        var phases = new Dictionary<string, int>();
        var plays = new Dictionary<string, int>();
        var actions = new Dictionary<string, int>();
        var routes = new Dictionary<string, int>();
        var routePrograms = new Dictionary<string, int>();
        var fire = new Dictionary<string, int>();
        var turbo = new Dictionary<string, int>();
        var events = new Dictionary<string, int>();
        int attempted = 0, completed = 0, rSkipFrames = 0;
        object positive = null, failure = null;
        Action<Dictionary<string, int>, string> count = (map, key) => {
            int previous; map.TryGetValue(key, out previous); map[key] = previous + 1;
        };
        string inputPath = Path.Combine(outputRoot, "captured-public-commands.jsonl");
        using (var commands = new StreamWriter(inputPath, false, new UTF8Encoding(false)))
        using (var observations = new StreamWriter(Path.Combine(outputRoot, "public-window-observations.jsonl"), false, new UTF8Encoding(false)))
        {
            for (int host = 0; host < 12000; host++)
            {
                PortableVisible visible = PortableVisible.Project(game.State);
                PortableIntent intent = policy.Choose(visible);
                PlayerCommand command = packets.Encode(intent);
                commands.WriteLine(JsonSerializer.Serialize(new { host, command }, options)); commands.Flush();
                attempted++;
                count(phases, visible.Phase.ToString()); count(actions, intent.Decision ?? "neutral");
                count(fire, "Home/" + visible.HomeFire); count(fire, "Away/" + visible.AwayFire);
                count(turbo, "heldC/" + ((intent.Held & SourceButtons.C) != 0));
                long before = game.SourceTick;
                try { game.Step(command); cache.Complete(before); completed++; }
                catch (Exception error)
                {
                    failure = new { host, before, game.SourceTick, command, error = error.ToString(),
                        actual = CanonicalObservation(game, host, rawCursor, null), allSharedRawDraws = shared.Trace };
                    File.WriteAllText(Path.Combine(outputRoot, "first-source-fault.json"), JsonSerializer.Serialize(failure, options));
                }
                if (failure != null) break;
                SourceSoundVmDispatch[] sounds = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
                foreach (SourceGameEvent item in game.SourceEvents) count(events, item.Type.ToString());
                if (game.SourceTick != before)
                {
                    count(plays, (game.Model.SourceOffensePlay == null ? "<unselected>" : game.Model.SourceOffensePlay.Identity) +
                        "/" + (game.Model.SourceDefensePlay == null ? "<unselected>" : game.Model.SourceDefensePlay.Identity));
                    foreach (SourceRouteState route in game.Model.SourceDrones)
                        count(routePrograms, (route.Program == null ? "<unselected>" : route.Program.Identity) + "/" + route.WordIndex);
                    foreach (ActorState actor in game.Model.Players)
                    {
                        count(routes, actor.Id + "/" + actor.SourceJob);
                        SourceAnimationLayerState layer = actor.AnimationState.Primary;
                        if (layer.SequenceName != "R_SKIP") continue;
                        rSkipFrames++;
                        if (game.SourceTick <= 2299 || !layer.IsSpecified || layer.SourceTick != game.SourceTick) continue;
                        ISourceAnimationClip clip = library.GetClip("R_SKIP");
                        Check(layer.FrameOrdinal >= 0 && layer.FrameOrdinal < clip.SelectedSourceFrames.Count,
                            "portable public R_SKIP uses a valid actual selected ordinal after2299");
                        SourceVector3[] joints = Enumerable.Range(0, 15).Select(j => game.Model.SourceCameraJoints[actor.Id, j]).ToArray();
                        Check(game.Model.SourceCameraJointTick == game.SourceTick &&
                            joints.All(j => !float.IsNaN(j.X) && !float.IsInfinity(j.X) &&
                                !float.IsNaN(j.Y) && !float.IsInfinity(j.Y) && !float.IsNaN(j.Z) && !float.IsInfinity(j.Z)),
                            "portable public R_SKIP current15 cached joints are real and finite");
                        PlayerView view = GameSnapshot.SourcePlayerView(actor, game.SourceTick, true);
                        SourceEvaluatedPose local = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots, view, view, 1f);
                        string pose = JsonSerializer.Serialize(local.Joints.Select(j => j.Position).ToArray(), options);
                        poses.Add(new { host, game.SourceTick, actor = actor.Id, layer.InstanceId,
                            ordinal = layer.FrameOrdinal, selected = clip.SelectedSourceFrames[layer.FrameOrdinal],
                            real15Joints = joints, real15LocalPose = local.Joints.Select(j => j.Position).ToArray(), view });
                        string key = actor.Id + "/" + layer.InstanceId;
                        Tuple<long, int, string> earlier;
                        if (!first.TryGetValue(key, out earlier))
                            first[key] = Tuple.Create(game.SourceTick, layer.FrameOrdinal, pose);
                        else if (positive == null && game.SourceTick > earlier.Item1 &&
                            layer.FrameOrdinal != earlier.Item2 && pose != earlier.Item3)
                            positive = new { actor = actor.Id, layer.InstanceId, firstTick = earlier.Item1,
                                secondTick = game.SourceTick, firstOrdinal = earlier.Item2, secondOrdinal = layer.FrameOrdinal };
                    }
                }
                SourceMatchView match = ((ISourceMatchState)game.State).Match;
                observations.WriteLine(JsonSerializer.Serialize(new { host, beforeSourceTick = before, game.SourceTick,
                    visible, intent, command, game.State.Phase, game.State.PlayNumber, game.State.Ball,
                    match.HomeGameplay, match.AwayGameplay, match.CompatibilityRepairCount,
                    game.SourceEvents, rawCursor, plays = new { offense = game.Model.SourceOffensePlay == null ?
                            null : game.Model.SourceOffensePlay.Identity,
                        defense = game.Model.SourceDefensePlay == null ? null : game.Model.SourceDefensePlay.Identity },
                    routeStates = game.Model.SourceDrones.Select((r, id) => new { actor = id,
                        program = r.Program == null ? null : r.Program.Identity,
                        r.WordIndex, r.Job, r.Flags, r.WaitTicks, r.Target }),
                    actors = game.Model.Players.Select(a => new { a.Id, a.Position, a.SourceMode, a.SourceJob,
                        a.SourceRawFlags, a.SourceButtons, a.SourceControllerStation,
                        sourceRoot = SourcePlayerDriver.WorldPosition(a),
                        lowerMode = a.AnimationState.Primary.Mode, upperMode = a.AnimationState.Secondary.Mode,
                        upperSequence = a.AnimationState.Secondary.SequenceName,
                        a.AnimationState.Primary.InstanceId, a.AnimationState.Primary.ScriptSymbol,
                        a.AnimationState.Primary.SequenceName, a.AnimationState.Primary.FrameOrdinal }),
                    runningSounds = sounds.Where(s => s.HasOriginalAnimationCue &&
                        new[] { 6373, 6401, 6428, 6480, 6508 }.Contains(s.OriginalCue.Context.SourceLine))
                        .Select(s => new { s.OriginalCue, s.Status }),
                    stateEventCacheSha256 = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                        CanonicalObservation(game, host, rawCursor, null), options)) }, options));
            }
        }
        Check(shared.Trace.Count == rawCursor, "portable public policy retains ALL unfiltered global raw draws");
        if (failure == null)
        {
            Check(ReadPublicReplay(inputPath).Length == 12000 && completed == 12000 && game.SourceTick == 11400,
                "portable public policy retains hard12000 ordinary hosts and11400 source bridge ticks");
        }
        SourceMatchView final = ((ISourceMatchState)game.State).Match;
        object receipt = new { schema = "portable-public-policy-first-window/v1", name = PortablePolicyName,
            version = PortablePolicyVersion, policySha256 = policySha, maximumInitialWindows = 1, initialExecutions = 1,
            attemptedHosts = attempted, completedHosts = completed, game.SourceTick, rawCursor, rawVector,
            rSkipFrames, positive, failure, profile = SourceTeamResourceProfile.C2M1StableId,
            sourceData = real.C2Data.PayloadSha256, factory = "REAL SourceC2Composition.CreateM1Simulation",
            gameplayCompatibility = game.Compatibility.ToString(), final.CompatibilityRepairCount,
            final.CompatibilityRepairs, final.HomeGameplay, final.AwayGameplay,
            phaseCounts = phases, playSourceWakeCounts = plays, policyActionCounts = actions,
            routeActorJobSourceWakeCounts = routes, routeProgramWordSourceWakeCounts = routePrograms,
            publicFireHostCounts = fire, turboHostCounts = turbo,
            actualSourceEventCounts = events, cache = cache.Summary(),
            publicInputSha256 = CanonicalHash(File.ReadAllBytes(inputPath)),
            fullM1OrCIOrNativeOrWholeGameQualified = false, previousFourPoliciesRemainUnqualified = true };
        File.WriteAllText(Path.Combine(outputRoot, "public-window-result.json"), JsonSerializer.Serialize(receipt, options));
        File.WriteAllText(Path.Combine(outputRoot, "all-shared-raw-draws.json"), JsonSerializer.Serialize(shared.Trace, options));
        File.WriteAllText(Path.Combine(outputRoot, "R_SKIP-real-poses.json"), JsonSerializer.Serialize(poses, options));
        summary["status"] = failure != null ? "STOP-new-source-fault" :
            positive == null ? "UNMET-portable-public-R_SKIP" : "portable-public-witness-parent-qualification-required";
        summary["public_policy_receipt"] = receipt; summary["assertions"] = assertions;
        Console.WriteLine(JsonSerializer.Serialize(summary, options));
        return failure != null ? 2 : positive == null ? 1 : 0;
    }
}
