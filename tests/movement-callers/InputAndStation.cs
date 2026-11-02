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
    private static void InputAndStationCallers()
    {
        GameSimulation game = Create();
        int calls = 0;
        SourceStationInput observed = default(SourceStationInput);
        game.SourceStationInputTransform = (snapshot, tick, frame) =>
        {
            calls++;
            Check(((ISourceMatchState)snapshot).SourceTick == tick, "M1 retains actual executing-tick input transform");
            observed = frame.Stations[0];
            return frame;
        };
        game.Step(Raw(SourceButtons.A | SourceButtons.B | SourceButtons.C, SourceButtons.A | SourceButtons.B | SourceButtons.C));
        Check(calls == 0, "M1 skipped host wake does not consume B1 queued raw edges");
        game.Step(Raw(SourceButtons.None, released: SourceButtons.A | SourceButtons.B | SourceButtons.C));
        Check(calls == 1 && observed.Held == SourceButtons.None
            && observed.Pressed == (SourceButtons.A | SourceButtons.B | SourceButtons.C)
            && observed.Released == observed.Pressed, "M1 retains complete queued tap/ABC combination masks without priority collapse");
        game.Step(Raw(SourceButtons.None));
        Check(calls == 2 && observed.Pressed == SourceButtons.None && observed.Released == SourceButtons.None,
            "M1 raw edges execute once, not per render or repeated source wake");
        RetainExecutionEpoch(game);
        game.Restart();
        executionInitialTicks[game] = game.Model.Tick;
        game.Step(Raw(SourceButtons.B, SourceButtons.B));
        ((IPendingSourceInput)game).ClearPendingInput();
        game.Step(default(PlayerCommand));
        Check(observed.Pressed == SourceButtons.None && observed.Held == SourceButtons.None,
            "M1 strong focus clear still flushes pending B1 edges/held sample");

        game = Create(settings: new SourceMovementSettings(0, 1, true, 1, 2, false));
        SourceMovementContext home = Bind<Func<ActorState, SourceMovementContext>>(game, "MovementContext")(game.Model.Players[0]);
        SourceMovementContext away = Bind<Func<ActorState, SourceMovementContext>>(game, "MovementContext")(game.Model.Players[7]);
        Check(home.SpeedPowerup && away.SpeedPowerup, "actual pup_speed remains GLOBAL, not a team mask");
        Check(home.FastTurboPowerup && !away.FastTurboPowerup, "actual pup_fastturborun selects executing TEAM bit");
        Check(!home.RunOutOfBoundsPowerup && away.RunOutOfBoundsPowerup, "actual pup_runob selects executing TEAM bit");
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.SourceStationTurbo[0] = 57f;
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.C));
        Check(B(game.Model.SourceStationTurbo[0]) == B(57f),
            "actual station infinite-turbo branch freezes resource, does not invent refill");
        game = Create();
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.SourceTeamFire[0] = true;
        game.Model.SourceStationTurbo[0] = 57f;
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.C));
        Check(B(game.Model.SourceStationTurbo[0]) == B(57f), "actual station fire branch freezes resource");
        game.Model.SourceTeamFire[0] = false;
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.C));
        Check(B(game.Model.SourceStationTurbo[0]) == B(57f - .35f),
            "actual shared station resource depletes once, not once per fourteen actors");
        game.Model.Possession = TeamId.Away;
        float before = game.Model.SourceStationTurbo[0];
        float expected = Math.Min(100f, before + .35f);
        expected = Math.Min(100f, expected + .15f);
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.None));
        Check(B(game.Model.SourceStationTurbo[0]) == B(expected),
            "actual defending station recharge preserves separately clamped original additions");
    }

    private const string OpenSpaceName = "trips-rollout-clearance";
    private const string OpenSpaceVersion = "source-trips-rollout-public-controller/v1";
    private const string OpenSpaceGeneration = "source-public-open-space/v7";
    private const string OpenSpaceCase = "trips-rollout-clearance-presnap-switch-v7-12000";

    private readonly struct OpenSpacePlayer
    {
        public int Id { get; }
        public TeamId Team { get; }
        public FieldVector Position { get; }
        public FieldVector Velocity { get; }
        public ActorRole Role { get; }
        public float Turbo01 { get; }
        internal OpenSpacePlayer(int id, TeamId team, FieldVector position, ActorRole role,
            float turbo = 1f, FieldVector velocity = default(FieldVector))
        { Id = id; Team = team; Position = position; Velocity = velocity; Role = role; Turbo01 = turbo; }
    }

    private sealed class OpenSpaceVisible
    {
        public GamePhase Phase { get; }
        public TeamId Possession { get; }
        public int ControlledId { get; }
        public int CarrierId { get; }
        public FieldVector BallPosition { get; }
        public float LineOfScrimmage { get; }
        public SourcePlayKind PlayKind { get; }
        public IReadOnlyList<OpenSpacePlayer> Players { get; }

        internal OpenSpaceVisible(GamePhase phase, TeamId possession, int controlled, int carrier,
            IEnumerable<OpenSpacePlayer> players, float line = 25f,
            FieldVector ball = default(FieldVector), SourcePlayKind kind = SourcePlayKind.Normal)
        {
            Phase = phase; Possession = possession; ControlledId = controlled; CarrierId = carrier;
            BallPosition = ball; LineOfScrimmage = line; PlayKind = kind;
            Players = Array.AsReadOnly(players.ToArray());
        }

        internal static OpenSpaceVisible Project(IGameState state)
        {
            return new OpenSpaceVisible(state.Phase, state.Possession, state.ControlledPlayerId,
                state.Ball.CarrierId, state.Players.Select(p => new OpenSpacePlayer(
                    p.Id, p.Team, p.Position, p.Role, p.Turbo01, p.Velocity)),
                state.LineOfScrimmage, state.Ball.Position, ((ISourceMatchState)state).Match.PlayKind);
        }
    }

    private sealed class OpenSpaceController
    {
        internal const int MenuWait = 24, SnapWait = 75, ContinueWait = 30, ReadWait = 12, PassRepeat = 30;
        internal const int TurboReleaseWait = 12;
        internal const float Approach = 75f, Clear = 8f, CoastCorridor = 6f, LaneLength = 12f;
        private GamePhase previousPhase = (GamePhase)(-1);
        private TeamId previousPossession = (TeamId)(-1);
        private int phaseHosts, previousCarrier = -1, carrierHosts, passWait, releasedHosts = TurboReleaseWait;
        private bool menuSent, snapSent, continued, turboHeld;

        private static float Distance(FieldVector a, FieldVector b)
        {
            float x = a.X - b.X, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        private static float Nearest(OpenSpaceVisible view, TeamId team, FieldVector point, float seconds = 0f)
        {
            float nearest = float.PositiveInfinity;
            foreach (OpenSpacePlayer other in view.Players)
                if (other.Team != team)
                    nearest = Math.Min(nearest, Distance(point, new FieldVector(
                        other.Position.X + seconds * other.Velocity.X,
                        other.Position.Z + seconds * other.Velocity.Z)));
            return nearest;
        }

        internal static FieldVector Route(OpenSpaceVisible view, OpenSpacePlayer carrier, out float clearance)
        {
            float direction = carrier.Team == TeamId.Home ? 1f : -1f;
            FieldVector best = new FieldVector(direction, 0f);
            float bestScore = float.NegativeInfinity;
            clearance = 0f;
            foreach (float lane in new[] { 0f, -6f, 6f, -12f, 12f, -18f, 18f })
            {
                float dz = lane - carrier.Position.Z;
                float length = (float)Math.Sqrt(LaneLength * LaneLength + dz * dz);
                var heading = new FieldVector(direction * LaneLength / length, dz / length);
                float gap = float.PositiveInfinity;
                for (int step = 1; step <= 4; step++)
                {
                    float distance = step * 3f;
                    var point = new FieldVector(carrier.Position.X + heading.X * distance,
                        carrier.Position.Z + heading.Z * distance);
                    gap = Math.Min(gap, Nearest(view, carrier.Team, point, step * .125f));
                }
                float score = 3f * Math.Min(gap, 10f) + 3f * direction * heading.X - .03f * Math.Abs(lane);
                if (score <= bestScore) continue;
                bestScore = score; best = heading; clearance = gap;
            }
            return best;
        }

        private PortableIntent Carry(OpenSpaceVisible view, OpenSpacePlayer carrier)
        {
            float clearance;
            FieldVector heading = Route(view, carrier, out clearance);
            float progress = carrier.Team == TeamId.Home ? carrier.Position.X : 100f - carrier.Position.X;
            bool coast = progress >= Approach && Nearest(view, carrier.Team, carrier.Position) >= Clear &&
                clearance >= CoastCorridor;
            if (coast || carrier.Turbo01 <= .05f) turboHeld = false;
            else if (!turboHeld && carrier.Turbo01 >= .25f && releasedHosts >= TurboReleaseWait) turboHeld = true;
            releasedHosts = turboHeld ? 0 : releasedHosts + 1;
            return PortablePublicController.World(heading.X, heading.Z, turboHeld ? SourceButtons.C : SourceButtons.None,
                coast ? "carry-clear-red-zone-approach" : turboHeld ? "carry-open-lane-burst" : "carry-open-lane-recharge");
        }

        internal PortableIntent Choose(OpenSpaceVisible view)
        {
            if (view.Phase != previousPhase || view.Possession != previousPossession)
            {
                previousPhase = view.Phase; previousPossession = view.Possession; phaseHosts = 0;
                menuSent = snapSent = continued = turboHeld = false;
                previousCarrier = -1; carrierHosts = passWait = 0; releasedHosts = TurboReleaseWait;
            }
            phaseHosts++;
            if (view.Phase == GamePhase.PlaySelect && phaseHosts >= MenuWait && !menuSent)
            {
                menuSent = true;
                bool offense = view.Possession == TeamId.Home;
                return new PortableIntent(menu: new SourceCommand(SourceAction.SelectPlay,
                    new SourcePlaySelection(offense ? "play_da_bomb" : "play_3_man_cover",
                        offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense)),
                    decision: offense ? "menu-trips-da-bomb" : "menu-three-man-cover");
            }
            if (view.Phase == GamePhase.PreSnap && phaseHosts >= SnapWait && !snapSent)
            {
                snapSent = true;
                return new PortableIntent(held: SourceButtons.B, decision: "fresh-cabinet-B-after-public-presnap-wait");
            }
            if (view.Phase == GamePhase.DeadBall && phaseHosts >= ContinueWait && !continued)
            {
                continued = true;
                return new PortableIntent(menu: new SourceCommand(SourceAction.Continue), decision: "menu-continue");
            }
            if (view.Phase != GamePhase.LiveBall) return new PortableIntent();
            OpenSpacePlayer[] humans = view.Players.Where(p => p.Id == view.ControlledId && p.Team == TeamId.Home).ToArray();
            if (humans.Length != 1) return new PortableIntent();
            OpenSpacePlayer human = humans[0];
            OpenSpacePlayer[] carriers = view.Players.Where(p => p.Id == view.CarrierId).ToArray();
            if (carriers.Length != 1)
                return PortablePublicController.World(view.BallPosition.X - human.Position.X,
                    view.BallPosition.Z - human.Position.Z, human.Turbo01 > .1f ? SourceButtons.C : SourceButtons.None,
                    "pursue-public-ball");
            OpenSpacePlayer carrier = carriers[0];
            if (carrier.Team != human.Team)
            {
                SourceButtons held = human.Turbo01 > .1f ? SourceButtons.C : SourceButtons.None;
                if (Distance(human.Position, carrier.Position) <= 3f) held |= SourceButtons.A;
                return PortablePublicController.World(carrier.Position.X + .3f * carrier.Velocity.X - human.Position.X,
                    Math.Max(-20f, Math.Min(20f, carrier.Position.Z + .3f * carrier.Velocity.Z)) - human.Position.Z,
                    held, "defense-pursuit-and-close-tackle");
            }
            if (human.Id != carrier.Id)
                return PortablePublicController.World(carrier.Position.X - human.Position.X,
                    carrier.Position.Z - human.Position.Z, SourceButtons.None, "escort-real-carrier");
            if (previousCarrier != carrier.Id)
            {
                previousCarrier = carrier.Id; carrierHosts = passWait = 0;
                turboHeld = false; releasedHosts = TurboReleaseWait;
            }
            carrierHosts++;
            if (passWait > 0) passWait--;
            if (carrier.Role == ActorRole.Quarterback && view.PlayKind != SourcePlayKind.Kickoff &&
                carrierHosts >= ReadWait && passWait == 0)
            {
                OpenSpacePlayer[] receivers = view.Players.Where(p => p.Team == carrier.Team &&
                    p.Id != carrier.Id && p.Id % 7 >= 3 && p.Role == ActorRole.Receiver &&
                    p.Position.X >= carrier.Position.X).OrderBy(p => p.Position.Z).ThenBy(p => p.Id).ToArray();
                float best = float.NegativeInfinity;
                int chosen = -1;
                for (int side = 0; side < 2 && receivers.Length != 0; side++)
                {
                    OpenSpacePlayer receiver = receivers[side == 0 ? 0 : receivers.Length - 1];
                    float ahead = receiver.Position.X - carrier.Position.X;
                    var lead = new FieldVector(receiver.Position.X + .5f * receiver.Velocity.X,
                        receiver.Position.Z + .5f * receiver.Velocity.Z);
                    float gap = Math.Min(Nearest(view, carrier.Team, receiver.Position),
                        Nearest(view, carrier.Team, lead, .5f));
                    if (receiver.Position.X < view.LineOfScrimmage + 5f || ahead < 4f || ahead > 40f ||
                        Math.Abs(lead.Z) > 20f || gap < Clear) continue;
                    float score = 2f * gap + .15f * ahead;
                    if (score > best) { best = score; chosen = side; }
                }
                if (chosen >= 0)
                {
                    passWait = PassRepeat;
                    return new PortableIntent(chosen == 0 ? 3 : -3, 3,
                        SourceButtons.B | (carrier.Turbo01 > .1f ? SourceButtons.C : SourceButtons.None),
                        decision: chosen == 0 ? "rollout-pass-open-left" : "rollout-pass-open-right");
                }
            }
            return Carry(view, carrier);
        }
    }

    private static int OpenSpacePolicyChecks()
    {
        var controller = new OpenSpaceController();
        Func<GamePhase, TeamId, OpenSpaceVisible> menu = (phase, side) =>
            new OpenSpaceVisible(phase, side, 3, -1, new OpenSpacePlayer[0]);
        PortableIntent intent = default(PortableIntent);
        for (int host = 0; host < OpenSpaceController.MenuWait; host++)
            intent = controller.Choose(menu(GamePhase.PlaySelect, TeamId.Home));
        Check(intent.Menu.Play.PlaySymbol == "play_da_bomb" && intent.Menu.Play.Playbook == SourcePlaybookKind.Offense,
            "open-space Home offense selects the actual original trips play");
        Check(!controller.Choose(menu(GamePhase.PlaySelect, TeamId.Home)).Menu.IsSpecified,
            "open-space menu emits no duplicate queued selection");
        for (int host = 0; host < OpenSpaceController.MenuWait; host++)
            intent = controller.Choose(menu(GamePhase.PlaySelect, TeamId.Away));
        Check(intent.Menu.Play.PlaySymbol == "play_3_man_cover" && intent.Menu.Play.Playbook == SourcePlaybookKind.Defense,
            "open-space same-phase possession change selects only the current Home defensive book");
        for (int host = 1; host < OpenSpaceController.SnapWait; host++)
            Check(controller.Choose(menu(GamePhase.PreSnap, TeamId.Away)).Held == SourceButtons.None,
                "open-space fresh B respects the original public pre-snap waiting interval");
        var packets = new PortableCabinetPackets();
        PlayerCommand pressed = packets.Encode(controller.Choose(menu(GamePhase.PreSnap, TeamId.Away)));
        PlayerCommand released = packets.Encode(controller.Choose(menu(GamePhase.PreSnap, TeamId.Away)));
        Check(pressed.StationInputs.Stations[0].Pressed == SourceButtons.B &&
            released.StationInputs.Stations[0].Released == SourceButtons.B,
            "open-space defending B is a genuine fresh pressed/released cabinet edge");
        var runner = new OpenSpacePlayer(4, TeamId.Home, new FieldVector(82f, 0f), ActorRole.Receiver);
        var trailing = new OpenSpacePlayer(10, TeamId.Away, new FieldVector(60f, 0f), ActorRole.Defender);
        var clear = new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Home, 4, 4, new[] { runner, trailing });
        intent = controller.Choose(clear);
        PortableIntent forward = PortablePublicController.World(1f, 0f, SourceButtons.None, "synthetic");
        Check(intent.Held == SourceButtons.None && intent.X == forward.X && intent.Z == forward.Z &&
            intent.Decision == "carry-clear-red-zone-approach",
            "open-space enemy twenty with all opponents clear keeps moving and releases plow/stiff-arm buttons");
        var closeEnemy = new OpenSpacePlayer(10, TeamId.Away, new FieldVector(86f, 0f), ActorRole.Defender);
        float clearance;
        FieldVector route = OpenSpaceController.Route(
            new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Home, 4, 4, new[] { runner, closeEnemy }), runner, out clearance);
        Check(route.X > 0f && Math.Abs(route.Z) > .1f,
            "open-space planner takes a forward escape lane instead of running into a six-yard override opponent");
        var before = new OpenSpacePlayer(4, TeamId.Home, new FieldVector(60f, 0f), ActorRole.Receiver);
        var danger = new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Home, 4, 4, new[] { before,
            new OpenSpacePlayer(10, TeamId.Away, new FieldVector(64f, 0f), ActorRole.Defender) });
        var resources = new OpenSpaceController();
        Check(resources.Choose(danger).Held == SourceButtons.C, "open-space runner uses legal turbo to escape pressure");
        Check(resources.Choose(clear).Held == SourceButtons.None, "open-space clear approach releases turbo before the trigger");
        for (int host = 1; host < OpenSpaceController.TurboReleaseWait; host++)
            Check(resources.Choose(danger).Held == SourceButtons.None,
                "open-space controller does not accidentally double-C spin on an oscillating clearance boundary");
        Check(resources.Choose(danger).Held == SourceButtons.C, "open-space turbo can resume after the declared release interval");
        var qb = new OpenSpacePlayer(3, TeamId.Home, new FieldVector(20f, 0f), ActorRole.Quarterback);
        var left = new OpenSpacePlayer(4, TeamId.Home, new FieldVector(40f, -12f), ActorRole.Receiver);
        var right = new OpenSpacePlayer(6, TeamId.Home, new FieldVector(34f, 10f), ActorRole.Receiver);
        var cover = new OpenSpacePlayer(10, TeamId.Away, new FieldVector(34f, 11f), ActorRole.Defender);
        var pocket = new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Home, 3, 3, new[] { qb, left, right, cover });
        var reader = new OpenSpaceController();
        for (int host = 0; host < OpenSpaceController.ReadWait; host++) intent = reader.Choose(pocket);
        Check((intent.Held & SourceButtons.B) != 0 && intent.X == 3 && intent.Z == 3,
            "open-space quarterback uses source raw left column only for a publicly clear downfield outlet");
        Check((reader.Choose(pocket).Held & SourceButtons.B) == 0, "open-space pass releases B on the following host");
        var covered = new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Home, 3, 3, new[] { qb, left, right, cover,
            new OpenSpacePlayer(11, TeamId.Away, new FieldVector(40f, -11f), ActorRole.Defender) });
        reader = new OpenSpaceController();
        for (int host = 0; host < 2 * OpenSpaceController.PassRepeat; host++)
        {
            intent = reader.Choose(covered);
            Check((intent.Held & SourceButtons.B) == 0 && (intent.X != 0 || intent.Z != 0),
                "open-space covered pocket rolls out without a deadline-forced covered pass");
        }
        var defense = new OpenSpaceVisible(GamePhase.LiveBall, TeamId.Away, 3, 10, new[] { qb,
            new OpenSpacePlayer(10, TeamId.Away, new FieldVector(50f, 0f), ActorRole.Receiver) });
        intent = controller.Choose(defense);
        Check(intent.Decision == "defense-pursuit-and-close-tackle" && intent.X == forward.X && intent.Z == forward.Z,
            "open-space defense pursues the actual carrier rather than deliberately vacating an opposing runway");
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-open-space-policy-checks/v1",
            status = "passed", assertions, actualGameConstructions = 0, actualGameSteps = 0 }));
        return 0;
    }

    private static int AuthorOpenSpacePublicPolicy(string policyPath, IDictionary<string, object> summary)
    {
        Check(real.IsPreSnapSwitchProfile && !real.IsC2M1Profile &&
            gameplayCompatibility == SourceGameplayCompatibility.Portable,
            "open-space authoring uses only the unchanged actual enabled282 Portable factory");
        byte[] policyBytes = File.ReadAllBytes(policyPath);
        string policySha = CanonicalHash(policyBytes);
        int[] rawVector;
        using (JsonDocument document = JsonDocument.Parse(policyBytes))
        {
            JsonElement policy = document.RootElement;
            string root = policy.GetProperty("worktree").GetString();
            Check(policy.GetProperty("name").GetString() == OpenSpaceName &&
                policy.GetProperty("version").GetString() == OpenSpaceVersion &&
                policy.GetProperty("case").GetString() == OpenSpaceCase &&
                policy.GetProperty("recordingGeneration").GetString() == OpenSpaceGeneration &&
                policy.GetProperty("maxHosts").GetInt32() == 12000 &&
                policy.GetProperty("maximumInitialWindows").GetInt32() == 1 &&
                policy.GetProperty("seed").GetInt32() == 1 &&
                policy.GetProperty("providerProfile").GetString() == "source-sound-smoke/raw-vector-v1" &&
                policy.GetProperty("sourceManifestSha256").GetString() ==
                    "a061fa40aa67ce792672a13ade4aef7ad1eda0fc141d1694e1c0e9b4ec52c507" &&
                policy.GetProperty("configurationSha256").GetString() ==
                    "75b13a866bcbbe9a7ad23bdff425153b985c41141c3dd42868bbeb533e25837d" &&
                Path.GetFullPath(policyPath) == Path.Combine(root,
                    ".squad/.scratch/unity-rskip-scenario-20261101/authority/prospective-policy.json"),
                "open-space authoring requires its distinct prospectively frozen one-window authority");
            foreach (JsonProperty pin in policy.GetProperty("frozenCodeSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(Path.Combine(root, pin.Name))) == pin.Value.GetString(),
                    "open-space authoring source freeze changed: " + pin.Name);
            foreach (JsonProperty pin in policy.GetProperty("frozenInputSha256").EnumerateObject())
                Check(CanonicalHash(File.ReadAllBytes(pin.Name)) == pin.Value.GetString(),
                    "open-space authoring input freeze changed: " + pin.Name);
            Check(CanonicalHash(File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location)) ==
                policy.GetProperty("entrypointSha256").GetString(),
                "open-space authoring executes the sealed normal Main PE, not a helper");
            rawVector = policy.GetProperty("rawVector").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        }
        Check(rawVector.Length == 15 && CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(rawVector)) ==
            "bbf469b9c4de0ce8227ea126fa33510d927f815d879281dbcefd26dabc897827",
            "open-space authoring retains the same seed1 unfiltered global raw vector");
        using (var marker = new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(policyPath)),
            "only-public-window-started.json"), FileMode.CreateNew))
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { policySha, maximumInitialWindows = 1,
                maxHosts = 12000, recordingGeneration = OpenSpaceGeneration });
            marker.Write(bytes, 0, bytes.Length);
        }
        int rawCursor = 0;
        SourceInjectedRandom shared = null;
        GameSimulation game = Create(camera: false, random: seed => shared = new SourceInjectedRandom(
            site => rawVector[rawCursor++ % rawVector.Length], new SourceRandomProvenance(
                "source-sound-smoke/raw-vector-v1", "Same unfiltered raw vector shared by ALL sites, not native PRNG",
                "unchanged enabled282 composition; one human; original seed1", SourceRandomEvidence.ExternalUnverified)),
            compatibility: SourceGameplayCompatibility.Portable);
        Check(game.SourceDefensivePreSnapSwitchEnabled && shared.Trace[0].Raw == 0 && shared.Trace[1].Raw == 1 &&
            shared.Trace[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            shared.Trace[1].Site.SourceIdentity.Contains("InitGameProc:560"),
            "open-space authoring retains both original bootstrap draws before AI");
        var cache = new CanonicalWitness(game, false);
        game.SourceCameraHeadingProvider = (state, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, state, tick);
            cache.Camera(tick); return heading;
        };
        var controller = new OpenSpaceController();
        var packets = new PortableCabinetPackets();
        var options = new JsonSerializerOptions { IncludeFields = true };
        var first = new Dictionary<string, Tuple<long, int, string>>();
        var poses = new List<object>();
        var switches = new List<object>();
        var rawDefendingB = new List<object>();
        var faults = new List<object>();
        PreSnapPublicEdge pendingPublicB = null;
        int attempted = 0, completed = 0, rSkipFrames = 0;
        object positive = null;
        string inputPath = Path.Combine(outputRoot, "captured-public-commands.jsonl");
        using (var commands = new StreamWriter(inputPath, false, new UTF8Encoding(false)))
        using (var observations = new StreamWriter(Path.Combine(outputRoot, "public-window-observations.jsonl"),
            false, new UTF8Encoding(false)))
        {
            for (int host = 0; host < 12000; host++)
            {
                OpenSpaceVisible visible = OpenSpaceVisible.Project(game.State);
                PortableIntent intent = controller.Choose(visible);
                PlayerCommand command = packets.Encode(intent);
                PreSnapPublicEdge captured = CapturePublicB(host, game, command, pendingPublicB);
                if (!ReferenceEquals(captured, pendingPublicB) && captured.Visible.Phase == GamePhase.PreSnap &&
                    captured.Visible.Possession == TeamId.Away)
                    rawDefendingB.Add(new { host, before = game.SourceTick, visible, intent, command });
                pendingPublicB = captured;
                commands.WriteLine(JsonSerializer.Serialize(new { host, command }, options)); commands.Flush(); attempted++;
                long before = game.SourceTick;
                SourceSoundVmDispatch[] sounds = new SourceSoundVmDispatch[0];
                try
                {
                    game.Step(command); cache.Complete(before); completed++;
                    sounds = game.TakeSourceSoundVmDispatches().Dispatches.ToArray();
                    foreach (SourceSoundVmDispatch sound in sounds.Where(s => s.C2Context != null &&
                        s.C2Context.Kind == SourceC2SoundFunctionKind.AttackSoundFixed))
                    {
                        object actual = RequirePublicSwitchWitness(host, game, sound, pendingPublicB);
                        if (pendingPublicB.Visible.Phase == GamePhase.PreSnap &&
                            pendingPublicB.Visible.Possession == TeamId.Away) switches.Add(actual);
                    }
                    if (game.SourceTick != before)
                        foreach (ActorState actor in game.Model.Players)
                        {
                            SourceAnimationLayerState layer = actor.AnimationState.Primary;
                            if (layer.SequenceName != "R_SKIP") continue;
                            rSkipFrames++;
                            if (game.SourceTick <= 2299 || !layer.IsSpecified || layer.SourceTick != game.SourceTick) continue;
                            ISourceAnimationClip clip = library.GetClip("R_SKIP");
                            Check(layer.FrameOrdinal >= 0 && layer.FrameOrdinal < clip.SelectedSourceFrames.Count,
                                "open-space R_SKIP is an actual selected original ordinal after2299");
                            PlayerView view = GameSnapshot.SourcePlayerView(actor, game.SourceTick, true);
                            SourceEvaluatedPose local = SourceActorPoseSampler.Evaluate(library, real.ReferencePivots, view, view, 1f);
                            SourceVector3[] joints = Enumerable.Range(0, 15).Select(j => game.Model.SourceCameraJoints[actor.Id, j]).ToArray();
                            Check(game.Model.SourceCameraJointTick == game.SourceTick && local.Joints.Count == 15 &&
                                joints.All(j => !float.IsNaN(j.X) && !float.IsInfinity(j.X) && !float.IsNaN(j.Y) &&
                                    !float.IsInfinity(j.Y) && !float.IsNaN(j.Z) && !float.IsInfinity(j.Z)),
                                "open-space R_SKIP retains all15 actual cached and local pose joints");
                            string pose = JsonSerializer.Serialize(local.Joints.Select(j => j.Position).ToArray(), options);
                            poses.Add(new { host, game.SourceTick, actor = actor.Id, layer.InstanceId, ordinal = layer.FrameOrdinal,
                                real15Joints = joints, real15LocalPose = local.Joints.Select(j => j.Position).ToArray(), view });
                            string key = actor.Id + "/" + layer.InstanceId;
                            Tuple<long, int, string> earlier;
                            if (!first.TryGetValue(key, out earlier)) first[key] = Tuple.Create(game.SourceTick, layer.FrameOrdinal, pose);
                            else if (positive == null && game.SourceTick > earlier.Item1 &&
                                layer.FrameOrdinal != earlier.Item2 && pose != earlier.Item3)
                                positive = new { actor = actor.Id, layer.InstanceId, firstTick = earlier.Item1,
                                    secondTick = game.SourceTick, firstOrdinal = earlier.Item2, secondOrdinal = layer.FrameOrdinal };
                        }
                }
                catch (Exception error)
                {
                    object fault = new { host, before, game.SourceTick, command, error = error.ToString(),
                        actual = CanonicalObservation(game, host, rawCursor, null), allSharedRawDraws = shared.Trace };
                    faults.Add(fault);
                    File.WriteAllText(Path.Combine(outputRoot, "first-source-fault.json"), JsonSerializer.Serialize(fault, options));
                }
                if (game.SourceTick != before) pendingPublicB = null;
                observations.WriteLine(JsonSerializer.Serialize(new { host, before, game.SourceTick, visible, intent,
                    game.State.Phase, game.State.PlayNumber, game.State.Ball, game.SourceEvents, rawCursor,
                    stateEventCacheSha256 = CanonicalHash(JsonSerializer.SerializeToUtf8Bytes(
                        CanonicalObservation(game, host, rawCursor, null), options)) }, options));
                if (faults.Count != 0) break;
            }
        }
        Check(shared.Trace.Count == rawCursor, "open-space recording retains ALL unfiltered shared raw draws");
        object receipt = new { schema = "source-open-space-public-policy-first-window/v1", name = OpenSpaceName,
            version = OpenSpaceVersion, recordingGeneration = OpenSpaceGeneration, policySha256 = policySha,
            maximumInitialWindows = 1, initialExecutions = 1, attemptedHosts = attempted, completedHosts = completed,
            game.SourceTick, rawCursor, rawVector, rSkipFrames, positive, failure = faults.FirstOrDefault(), faults,
            preSnapSwitchWitnesses = switches.Count, rawDefendingBInputs = rawDefendingB.Count,
            profile = SourceTeamResourceProfile.PreSnapSwitchStableId, sourceData = real.C2Data.PayloadSha256,
            factory = "REAL SourceC2Composition.CreatePreSnapSwitchSimulation", gameplayCompatibility = game.Compatibility.ToString(),
            publicInputSha256 = CanonicalHash(File.ReadAllBytes(inputPath)), cache = cache.Summary(),
            fullM1OrCIOrNativeOrWholeGameQualified = false };
        File.WriteAllText(Path.Combine(outputRoot, "public-window-result.json"), JsonSerializer.Serialize(receipt, options));
        File.WriteAllText(Path.Combine(outputRoot, "all-shared-raw-draws.json"), JsonSerializer.Serialize(shared.Trace, options));
        File.WriteAllText(Path.Combine(outputRoot, "R_SKIP-real-poses.json"), JsonSerializer.Serialize(poses, options));
        File.WriteAllText(Path.Combine(outputRoot, "pre-snap-switch-witnesses.json"), JsonSerializer.Serialize(switches, options));
        File.WriteAllText(Path.Combine(outputRoot, "raw-defending-B-inputs.json"), JsonSerializer.Serialize(rawDefendingB, options));
        summary["status"] = faults.Count != 0 ? "STOP-new-source-fault" :
            completed != 12000 || game.SourceTick != 11400 ? "STOP-incomplete-window" :
            positive == null ? "UNMET-public-R_SKIP" : switches.Count == 0 ? "UNMET-fresh-B-switch" :
            "recorded-new-window-full-qualification-required";
        summary["public_policy_receipt"] = receipt; summary["assertions"] = assertions;
        Console.WriteLine(JsonSerializer.Serialize(summary, options));
        return faults.Count != 0 || completed != 12000 || game.SourceTick != 11400 ? 2 :
            positive == null || switches.Count == 0 ? 1 : 0;
    }
}
