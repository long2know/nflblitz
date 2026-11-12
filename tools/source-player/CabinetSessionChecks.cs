using System.Text.Json;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;
using Blitz.Storage;

static partial class Checks
{
    private static void VerifyCabinetParticipation(PlayerData data, byte[] manifest, Func<string, byte[]> read)
    {
        int before = assertions;
        SourceMatchConfiguration original = data.Configuration.Match;
        string originalJson = JsonSerializer.Serialize(data.Configuration);
        var two = new SourceMatchConfiguration(original.Profile, SourceStationLayout.TwoStation,
            original.HomeTeam, original.AwayTeam, new[] {
                new SourceStationConfiguration(0, TeamId.Home),
                new SourceStationConfiguration(1, TeamId.Away)
            }, original.FirstPossession, original.Captain, original.TimeAdjustment, original.Difficulty,
            original.ContinueAfterCabinetGate, original.HomePlay, original.AwayPlay);
        data.ValidateCabinetConfiguration(two);
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        SourceMatchConfiguration WithStations(SourceStationLayout layout, params SourceStationConfiguration[] stations) =>
            new SourceMatchConfiguration(original.Profile, layout, original.HomeTeam, original.AwayTeam, stations,
                original.FirstPossession, original.Captain, original.TimeAdjustment, original.Difficulty,
                original.ContinueAfterCabinetGate, original.HomePlay, original.AwayPlay);
        Reject(() => data.ValidateCabinetConfiguration(WithStations(SourceStationLayout.TwoStation,
            new SourceStationConfiguration(0, TeamId.Home))), "An incomplete TwoStation must retain the undefined-control guard.");
        Reject(() => data.ValidateCabinetConfiguration(WithStations(SourceStationLayout.TwoStation,
            new SourceStationConfiguration(0, TeamId.Away), new SourceStationConfiguration(1, TeamId.Home))),
            "Two physical stations cannot be relabeled as opposite source sides.");
        foreach (string query in new[] {
            "?source-cabinet=two-station&source-stations=0", "?source-cabinet=two-station&source-stations=0,2",
            "?source-cabinet=four-station&source-stations=0,0", "?source-stations=0,1",
            "?source-cabinet=four-station&source-cabinet=two-station", "?source-cabinet=4",
            "?source-cabinet=four-station&source-stations=0,1,2,3=extra"
        })
            Reject(() => PreviewOptions.FromUrl(query, data.Catalog.Profile.StableId),
                "Malformed cabinet admission must not guess participation: " + query);
        Check(PreviewOptions.FromUrl("", data.Catalog.Profile.StableId).CabinetLayout == null &&
            PreviewOptions.FromUrl("#?source-cabinet=two-station", data.Catalog.Profile.StableId).CabinetLayout == null,
            "Omitted/fragment-only cabinet options must leave authenticated defaults unchanged.");
        var receipts = new List<object>();
        foreach (var scenario in new[] {
            (Name: "two-opponents", Query: "?source-cabinet=two-station&source-stations=0,1", Mask: 3, Count: 2),
            (Name: "four-humans", Query: "?source-cabinet=four-station&source-stations=3,1,2,0", Mask: 15, Count: 4),
            (Name: "same-side-coop", Query: "?source-cabinet=four-station&source-stations=0,1", Mask: 3, Count: 2)
        })
        {
            string label = scenario.Name + ": ";
            var options = PreviewOptions.FromUrl(scenario.Query, data.Catalog.Profile.StableId);
            var configured = new PlayerData(new DataCatalog(manifest, read), cabinetOptions: options);
            int[] joined = Enumerable.Range(0, 4).Where(id => (scenario.Mask & 1 << id) != 0).ToArray();
            Check(configured.Configuration.Match.Stations.Select(s => s.StationId).SequenceEqual(joined),
                label + "explicit physical IDs must enter in original station order, even when URL order differs.");
            var sink = new CompleteAvSink();
            SourcePlayerRecordStore? store = options.RecordsEnabled ? new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
                () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false))) : null;
            store?.LoadAsync().GetAwaiter().GetResult();
            var host = new PlayerHost(configured, sink, options: options, recordStore: store);
            if (options.CabinetLayout == SourceStationLayout.TwoStation)
            {
                Reject(() => host.Capture(new double[8], new[] { 0, 0, 1, 0 }),
                    label + "absent physical P3 input cannot be silently discarded by Capture.");
                Reject(() => host.CaptureObserved(new double[8], new[] { 0, 0, 0, 1 }, 0),
                    label + "absent physical P4 input cannot be silently discarded by the actual observed browser entry.");
            }
            var events = new List<SourceGameEvent>();
            void ObserveEvents() => host.Bridge.SourceEventRaised += value => events.Add(value);
            void Raw(int[]? buttons = null, double[]? axes = null) =>
                host.Capture(axes ?? new double[8], buttons ?? new int[4]);
            void Frame() => host.Advance(1d / 60, 512, 384, 512, 384, 1);
            void Tap(int button, int[] stations, double x = 0, double y = 0)
            {
                Raw(); Raw();
                var buttons = new int[4]; var axes = new double[8];
                foreach (int id in stations) { buttons[id] = button; axes[id * 2] = x; axes[id * 2 + 1] = y; }
                Raw(buttons, axes); Raw();
                host.Advance(0, 512, 384, 512, 384, 1);
                for (int i = 0; i < 70 && host.Session.UsingSourcePreGameInput; i++) { Raw(); Frame(); }
            }
            SourceMatchView Match() => ((ISourceMatchState)host.Game.State).Match;
            int Controlled(int station) => Match().Stations.Single(value => value.StationId == station).ControlledPlayerId;
            void SourceSample(int[]? buttons = null, double[]? axes = null)
            {
                long tick = host.Game.SourceTick;
                do { Raw(buttons, axes); Frame(); } while (host.Game.SourceTick == tick);
                Check(host.Game.SourceTick == tick + 1, label + "one measured host step may not skip a source wake.");
            }
            int suspendedWakes = 0;
            void ActorSample(int[]? buttons = null, double[]? axes = null)
            {
                for (int attempt = 0; attempt < 600; attempt++)
                {
                    var previous = host.Game.State.Players.Select(value => value.Source.Input).ToArray();
                    SourceSample(buttons, axes);
                    var processes = ((ISourcePossessionState)host.Game.State).Processes;
                    bool awake = true;
                    foreach (int station in joined)
                    {
                        int actor = Controlled(station);
                        var process = processes.Single(value => value.Function == "player_proc" && value.RunLevel == 0x300 + actor);
                        if (process.LastWakeTick == host.Game.SourceTick) continue;
                        awake = false;
                        var current = host.Game.State.Players[actor].Source.Input;
                        Check(current.NonTurboTicks == previous[actor].NonTurboTicks &&
                            current.NonATicks == previous[actor].NonATicks && current.NonBTicks == previous[actor].NonBTicks,
                            label + "a suspended player_proc must not advance its double-tap counters.");
                    }
                    if (awake) return;
                    suspendedWakes++;
                }
                throw new InvalidOperationException(label + "physical inputs never reached all actual participating player_proc wakes.");
            }
            host.Advance(0, 512, 384, 512, 384, 1);
            Check(host.CabinetInputStations.SequenceEqual(Enumerable.Range(0, (int)options.CabinetLayout!.Value)),
                label + "browser input admission must distinguish physical cabinet slots from participating humans.");
            Tap(4, new[] { 0 });
            if (options.RecordsEnabled)
            {
                Tap(0, joined, x: 1);
                Tap(1, joined);
            }
            Check(host.Session.Stage == SourceCabinetMenuStage.Teams, label + "every participating human must complete the real public guest menu.");
            int[] captains = options.CabinetLayout == SourceStationLayout.TwoStation ? new[] { 0, 1 } :
                scenario.Mask == 15 ? new[] { 0, 2 } : new[] { 0 };
            Tap(1, captains); Tap(1, captains);
            Check(host.Session.Stage == SourceCabinetMenuStage.Versus, label + "actual side captains must reach versus, not a phase setter.");
            if (scenario.Name == "same-side-coop" && original.FirstPossession == TeamId.Home)
                Tap(0, new[] { 0 }, x: 1);
            Tap(0, new[] { 0 }, y: 1); Tap(0, new[] { 0 }, x: 1);
            Tap(1, new[] { 0 });
            Check(host.Session.Playing && host.Game.SourceTick == 0 &&
                host.MatchConfiguration.Stations.Count == scenario.Count &&
                host.MatchConfiguration.StationLayout == options.CabinetLayout &&
                host.MatchConfiguration.Captain == SourceCaptainSlot.Second &&
                host.Session.ParticipatingStationMask == scenario.Mask,
                label + "public choices must construct the actual fresh two/four-station participating match.");
            ObserveEvents();
            Raw(); Raw(); host.Advance(0, 512, 384, 512, 384, 1);
            bool forcedSelection = false;
            for (int i = 0; i < 3000 && !(host.Game.State.Phase == GamePhase.PreSnap &&
                Match().PlayKind == SourcePlayKind.Normal); i++)
            {
                var buttons = new int[4];
                forcedSelection |= Match().SelectionTimer?.ForceSelection == true;
                if (host.Game.State.Phase == GamePhase.PlaySelect && i % 8 < 3)
                    foreach (TeamId team in new[] { TeamId.Home, TeamId.Away })
                    {
                        int station = SourceCabinetStations.Captain(Match(), team);
                        if (station >= 0) buttons[station] = 2;
                    }
                Raw(buttons); Frame();
            }
            Check(host.Game.State.Phase == GamePhase.PreSnap && Match().PlayKind == SourcePlayKind.Normal && !forcedSelection,
                label + "actual captains' B edges must select normal football, never a timer-forced substitute for the wrong controller.");
            int homeCaptain = options.CabinetLayout == SourceStationLayout.TwoStation ? 0 : 1;
            int awayCaptain = options.CabinetLayout == SourceStationLayout.TwoStation ? 1 : scenario.Mask == 15 ? 3 : -1;
            Check(SourceCabinetStations.Captain(Match(), TeamId.Home) == homeCaptain &&
                SourceCabinetStations.Captain(Match(), TeamId.Away) == awayCaptain,
                label + "Second means physical teammate slot, not actor ID, possession or URL order.");
            int[] actors = joined.Select(Controlled).ToArray();
            Check(actors.All(id => id >= 0) && actors.Distinct().Count() == joined.Length,
                label + "every participating human must own a distinct actual Core actor.");
            for (int i = 0; i < joined.Length; i++)
                Check(host.Game.State.Players[actors[i]].Source.ControllerStationId == joined[i] &&
                    host.Game.State.Players[actors[i]].Team == host.MatchConfiguration.Stations[i].Team,
                    label + "station ownership must round-trip through the actual fourteen-player snapshot.");
            int quarterback = (int)host.Game.State.Possession * 7 + 3;
            int quarterbackStation = host.Game.State.Possession == TeamId.Home ? homeCaptain : awayCaptain;
            Check(quarterbackStation >= 0 && Controlled(quarterbackStation) == quarterback &&
                host.Game.State.Ball.CarrierId != quarterback,
                label + "the captain controls the quarterback while the presnap center still possesses the ball.");
            ActorSample();
            int[] abc = Enumerable.Range(0, 4).Select(id => joined.Contains(id) ? 7 : 0).ToArray();
            ActorSample(abc);
            foreach (int actor in actors)
                Check((Match().Audibles.ActorButtons[actor] & 7) == 7 &&
                    host.Game.State.Players[actor].Source.Input.Pressed == 7 &&
                    host.Game.State.Players[actor].Source.Input.Released == 0,
                    label + "simultaneous ABC must reach each owned player_proc, not merely a physical UI slot: " +
                    JsonSerializer.Serialize(new { actor, sourceTick = host.Game.SourceTick,
                        preSnap = ((ISourceGameState)host.Game.State).PreSnapTicksRemaining,
                        input = host.Game.State.Players[actor].Source.Input,
                        controller = host.Game.State.Players[actor].Source.ControllerStationId,
                        mode = host.Game.State.Players[actor].Source.Presentation.RawPlayerMode,
                        stations = Match().Stations,
                        processes = ((ISourcePossessionState)host.Game.State).Processes
                            .Where(value => value.Function == "player_proc" && value.RunLevel == 0x300 + actor).ToArray(),
                        audibleRestart = Match().Audibles.RestartStage.ToString(),
                        reinitializations = ((ISourceAudibleObservationState)host.Game.State).AudibleReinitializations
                            .Select(value => value.Phase.ToString()).ToArray(),
                        ball = host.Game.State.Ball,
                        actorButtons = Match().Audibles.ActorButtons[actor],
                        physical = host.Game.ConsumedSourceSwitches!.PhysicalSwitches.Stations.Select(s => (int)s.Held).ToArray() }));
            ActorSample(abc);
            foreach (int actor in actors)
                Check(host.Game.State.Players[actor].Source.Input.Held == 7 &&
                    host.Game.State.Players[actor].Source.Input.Pressed == 0,
                    label + "held ABC must not repeat the original edge command.");
            ActorSample();
            foreach (int actor in actors)
                Check((Match().Audibles.ActorButtons[actor] & 7) == 0 &&
                    host.Game.State.Players[actor].Source.Input.Released == 7 &&
                    host.Game.State.Players[actor].Source.Input.NonTurboTicks == 1 &&
                    host.Game.State.Players[actor].Source.Input.NonATicks == 1 &&
                    host.Game.State.Players[actor].Source.Input.NonBTicks == 1,
                    label + "actual controlled actor buttons must release independently of possession.");
            ActorSample(abc);
            var doubleTap = actors.Select(actor => host.Game.State.Players[actor].Source.Input).ToArray();
            Check(doubleTap.All(value => value.Pressed == 7 && value.NonTurboTicks == -1 &&
                value.NonATicks == -1 && value.NonBTicks == -1),
                label + "all participating actors must execute source A/B/C double taps, not only raw packet edges.");
            ActorSample();
            int defenderStation = host.Game.State.Possession == TeamId.Home ? awayCaptain : homeCaptain;
            if (defenderStation >= 0)
            {
                while (((ISourceGameState)host.Game.State).PreSnapTicksRemaining >= 270) SourceSample();
                int old = Controlled(defenderStation), expected = old;
                do { expected = expected / 7 * 7 + (expected + 1) % 7; }
                while (host.Game.State.Players[expected].Source.ControllerStationId >= 0 ||
                    host.Game.State.Players[expected].Role != ActorRole.Defender);
                int startEvents = events.Count;
                var buttons = new int[4]; buttons[defenderStation] = 2;
                ActorSample(buttons);
                Check(Controlled(defenderStation) == expected &&
                    host.Game.State.Players[old].Source.ControllerStationId == -1 &&
                    host.Game.State.Players[expected].Source.ControllerStationId == defenderStation &&
                    events.Skip(startEvents).Count(value => value.Type == SourceEventType.ControllerChanged &&
                        value.StationId == defenderStation) == 1,
                    label + "B must switch once to the next unowned defender, preserving the teammate's actor.");
            }
            while (((ISourceGameState)host.Game.State).PreSnapTicksRemaining >= 228) SourceSample();
            var snap = new int[4]; snap[quarterbackStation] = 1;
            SourceAudibleTeamView OffensiveAudible() => host.Game.State.Possession == TeamId.Home ?
                Match().Audibles.HomeTeam : Match().Audibles.AwayTeam;
            if (OffensiveAudible().Action == 1)
            {
                while (OffensiveAudible().Timer <= 3) SourceSample();
                ActorSample(snap);
                Check(OffensiveAudible().Action == 0 && !OffensiveAudible().BackgroundSpriteObjectSerial.HasValue &&
                    OffensiveAudible().Shown && host.Game.State.Phase == GamePhase.PreSnap &&
                    !events.Any(value => value.Type == SourceEventType.Snapped),
                    label + "an A edge closes the actual audible before hike; it cannot bypass the source re-armed snap delay.");
                ActorSample();
                while (((ISourceGameState)host.Game.State).PreSnapTicksRemaining >= 228) SourceSample();
            }
            ActorSample(snap);
            long snapIntentTick = host.Game.SourceTick;
            int center = (int)host.Game.State.Possession * 7 + 1;
            Check(((ISourceGameState)host.Game.State).PreSnapTicksRemaining == 0 &&
                host.Game.State.Players[center].Animation.Primary.ScriptSymbol == "center_snap_anim" &&
                host.Game.State.Ball.CarrierId == center,
                label + "the quarterback's A edge must start the actual center animation, not teleport possession or invent a release.");
            for (int i = 0; i < 80 && host.Game.State.Ball.CarrierId != quarterback; i++) SourceSample();
            Check(host.Game.State.Phase == GamePhase.LiveBall && host.Game.State.Ball.CarrierId == quarterback &&
                host.Game.State.Ball.Control.ControllerStationId == quarterbackStation,
                label + "control ownership and ball possession must converge only at the actual hike catch.");
            var actualHike = events.Single(value => value.Type == SourceEventType.Snapped);
            Check(actualHike.SourceTick > snapIntentTick && actualHike.ActorPlayerId == quarterback &&
                actualHike.TargetPlayerId == center,
                label + "the actual center callback must release after the input intent, preserving source actor/target order.");
            var diagonal = new[] { 1d, -1d, -1d, -1d, -1d, 1d, 1d, 1d };
            for (int id = 0; id < 4; id++) if (!joined.Contains(id)) { diagonal[id * 2] = 0; diagonal[id * 2 + 1] = 0; }
            ActorSample(axes: diagonal);
            var physical = host.Game.ConsumedSourceSwitches!.PhysicalSwitches;
            int[] expectedX = { -3, 3, 3, -3 }, expectedZ = { 3, 3, -3, -3 };
            int[] expectedStick = { 6, 0, 42, 48 };
            foreach (int station in joined)
                Check(physical.Station(station).StickX == expectedX[station] &&
                    physical.Station(station).StickZ == expectedZ[station] &&
                    host.Game.State.Players[Controlled(station)].Source.Input.IsSpecified &&
                    host.Game.State.Players[Controlled(station)].Source.Input.StickOrdinal == expectedStick[station],
                    label + "independent live raw diagonal must reach both the actual source scan and owned P" + (station + 1) + " actor.");
            var turboBefore = Match().Stations.ToDictionary(value => value.StationId, value => value.Turbo01);
            var turbo = Enumerable.Range(0, 4).Select(id => joined.Contains(id) ? 4 : 0).ToArray();
            ActorSample(turbo, diagonal);
            foreach (int station in joined)
                Check(Math.Abs(turboBefore[station] - Match().Stations.Single(value => value.StationId == station).Turbo01 - .0035f) < .000001f,
                    label + "each participating human's actual station resource must spend its own source turbo amount.");
            ActorSample();
            int partner = host.MatchConfiguration.Stations.Where(value => value.Team == host.Game.State.Possession &&
                value.StationId != quarterbackStation).Select(value => value.StationId).FirstOrDefault(-1);
            if (partner >= 0)
            {
                int partnerActor = Controlled(partner), eventStart = events.Count;
                var button = new int[4]; button[partner] = 2;
                ActorSample(button);
                Check(Controlled(partner) == partnerActor && Controlled(quarterbackStation) == quarterback &&
                    host.Game.State.Ball.CarrierId == quarterback &&
                    !events.Skip(eventStart).Any(value => value.Type is SourceEventType.ControllerChanged or SourceEventType.PassReleased),
                    label + "the receiver's B must honor the original quarterback-carrier switch guard, not steal control or throw for its captain.");
                ActorSample();
            }
            int releaseStart = events.Count;
            var pass = new int[4]; pass[quarterbackStation] = 2;
            var aim = new double[8]; aim[quarterbackStation * 2 + 1] = -1;
            for (int attempt = 0; attempt < 80 && !events.Skip(releaseStart).Any(value =>
                value.Type is SourceEventType.PassReleased or SourceEventType.LateralReleased); attempt++)
                ActorSample(attempt % 3 == 0 ? pass : null, aim);
            var release = events.Skip(releaseStart).Single(value =>
                value.Type is SourceEventType.PassReleased or SourceEventType.LateralReleased);
            Check(release.ActorPlayerId == quarterback && release.TargetPlayerId / 7 == (int)host.Game.State.Possession &&
                release.TargetPlayerId % 7 is >= 4 and <= 6 &&
                host.Game.State.Ball.TargetPlayerId == release.TargetPlayerId &&
                host.Game.State.Ball.CarrierId == -1 && Controlled(quarterbackStation) == quarterback,
                label + "raw quarterback stick/B must select and actually release to an eligible receiver without injecting a target or moving captain control.");
            var proofStations = Match().Stations.Select(value => new { value.StationId, value.ControlledPlayerId }).ToArray();
            long liveSourceTick = host.Game.SourceTick, inputEpoch = host.Game.SourceInputEpoch, audioEpoch = host.AudioPlayback!.Epoch;
            var instructionProof = JsonSerializer.SerializeToElement(host.Instructions?.Describe());
            host.SetActive(false);
            Check(host.Advance(.1, 512, 384, 512, 384, 1) == 0 && host.Game.SourceTick == liveSourceTick,
                label + "actual suspension must not spend elapsed time on player wakes.");
            host.CaptureObserved(diagonal, turbo, 0); host.CaptureObserved(diagonal, turbo, 0);
            host.SetActive(true);
            Check(host.Advance(0, 512, 384, 512, 384, 1) == 0 && host.Game.SourceTick == liveSourceTick &&
                host.AudioPlayback.Epoch == audioEpoch && host.Game.SourceInputEpoch > inputEpoch,
                label + "actual resume must preserve audio epoch and source time while resetting only transient input.");
            host.CaptureObserved(new double[8], new int[4], 0);
            host.CaptureObserved(new double[8], new int[4], 0);
            double observed = 0;
            for (int i = 0; i < 4 && host.Game.SourceTick == liveSourceTick; i++)
            {
                observed += 1d / 60;
                host.CaptureObserved(new double[8], new int[4], observed);
                host.AdvanceObserved(1d / 60, observed, 512, 384, 512, 384, 1);
            }
            Check(host.Game.SourceTick > liveSourceTick && joined.All(station =>
                (host.Game.State.Players[Controlled(station)].Source.Input.Held & 7) == 0),
                label + "two actual neutral observations must resume without replaying any participating human's held controls.");
            SourceMatchConfiguration restartedConfiguration = host.MatchConfiguration;
            host.Restart();
            Check(host.Game.SourceTick == 0 && host.Game.State.Tick == 0 &&
                ReferenceEquals(host.MatchConfiguration, restartedConfiguration) &&
                host.MatchConfiguration.Stations.Count == scenario.Count &&
                joined.All(station => (host.Game.State.Players[Controlled(station)].Source.Input.Held & 7) == 0),
                label + "public restart must retain the actual human configuration and discard old control edges.");
            receipts.Add(new { scenario.Name, scenario.Query, participatingMask = scenario.Mask,
                layout = host.MatchConfiguration.StationLayout.ToString(), sourceTick = liveSourceTick,
                stations = proofStations, quarterback, quarterbackStation,
                snapIntentTick, actualHikeTick = actualHike.SourceTick, doubleTap, suspendedWakes,
                receiver = release.TargetPlayerId, releaseKind = release.Type.ToString(), releaseTick = release.SourceTick,
                forcedSelection,
                selectionOrder = events.Where(value => value.Type == SourceEventType.PlaySelected)
                    .Select(value => new { value.SourceTick, team = value.Team?.ToString(), value.StationId }).ToArray(),
                inputEpochBeforePause = inputEpoch, audioEpochPreservedOnResume = true, restartSourceTick = host.Game.SourceTick,
                instructions = instructionProof, events = events.Select(value => value.Type.ToString()).ToArray() });
            host.SetActive(false);
        }
        Check(JsonSerializer.Serialize(data.Configuration) == originalJson,
            "New runtime cabinet choices must not change the authenticated default settings or legacy flight.");
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "actual-participating-source-cabinet-20261110/v1", assertions = assertions - before,
            dataManifestSha256 = data.Catalog.ManifestSha256, publicRawInputOnly = true, privateStateMutation = false,
            scenarios = receipts
        }));
    }

    private static void VerifyCompleteAv306Admission(PlayerData data, byte[] manifest, Func<string, byte[]> read)
    {
        int before = assertions;
        var catalog = data.Catalog;
        Check(catalog.Profile.IsCompleteAv306 && !catalog.Profile.IsCompleteOriginal303 &&
            catalog.ManifestSha256 == SourceTeamResourceProfile.CompleteAv306ManifestSha256 &&
            catalog.FeatureContract == SourceTeamResourceProfile.CompleteAv306FeatureContract &&
            catalog.Factory == SourceTeamResourceProfile.CompleteAv306Factory,
            "The actual306 root must have its distinct v3 identity/factory, never a relabeled303 profile.");
        Check(catalog.FileCount == 1153 && catalog.ByteCount == 300324289 && catalog.RetainedInputs.Count == 42,
            "The final306/audio successor must retain every original1114 input and all42 inactive historical metadata/bank inputs.");
        Check(data.Animations.SequenceNames.Count == 306 && data.Animations.FrameCount == 31051,
            "Actual decoded306 clips/31051 selected frames must load, not only manifest declarations.");
        foreach (var clip in new[] { (Name: "RUSH_I", Frames: 120), (Name: "QB_RDY", Frames: 77), (Name: "QB_RDYF", Frames: 77) })
            Check(data.Animations.GetClip(clip.Name).SelectedSourceFrames.Count == clip.Frames,
                "The newly reachable original stance must retain its actual selected frames: " + clip.Name);
        Check(data.CourseCamera != null && ReferenceEquals(data.CourseCamera, data.Impact.CourseCamera) &&
            data.CourseCamera.ContentSha256 == SourceTeamResourceProfile.CompleteAv306CourseSha256 &&
            data.CourseCamera.Entries.Count == 7 && data.CourseCamera.Entries[0].IsNoSelection &&
            data.CourseCamera.Entries.Skip(1).All(entry => entry.RawMode == 17 && entry.ControlPoints.Count == 301 &&
                entry.ControlPoints.Last().X == 0 && entry.ControlPoints.Last().Y == 0 && entry.ControlPoints.Last().Z == 0),
            "Actual csi_cd_endplay must bind the SAME authenticated seven-entry course with six301-point CAM_CO_C streams.");
        var old = SourceAnimationLibrary.Read(read("animation/export-inventory-complete-original.json"),
            path => read("animation/" + path));
        SourceTeamResourceProfile.CompleteOriginalSelectedWorld303.RequireLibrary(old);
        Check(old.SequenceNames.Count == 303 && !old.SequenceNames.Contains("RUSH_I") &&
            !old.SequenceNames.Contains("QB_RDY") && !old.SequenceNames.Contains("QB_RDYF"),
            "Inherited303 inventory and clip membership must remain its exact historical generation.");
        bool oldRejected = false, newRejected = false;
        try { SourceTeamResourceProfile.CompleteOriginalSelectedWorld303.RequireLibrary(data.Animations); }
        catch (SourceInterfaceException) { oldRejected = true; }
        try { SourceTeamResourceProfile.CompleteAv306.RequireLibrary(old); }
        catch (SourceInterfaceException) { newRejected = true; }
        Check(oldRejected && newRejected, "Typed old/new library capabilities must reject cross-generation aliases.");
        void Invalid(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        foreach (var mutation in new[] {
            (Key: "profile", Value: SourceTeamResourceProfile.CompleteOriginalSelectedWorldStableId),
            (Key: "schema", Value: DataCatalog.SelectedWorldSchema),
            (Key: "ancestorManifestSha256", Value: new string('0', 64)),
            (Key: "factory", Value: SourceTeamResourceProfile.CompleteOriginalPrematchFactory)
        })
        {
            var changed = System.Text.Json.Nodes.JsonNode.Parse(manifest)!;
            changed[mutation.Key] = mutation.Value;
            Invalid(() => new DataCatalog(JsonSerializer.SerializeToUtf8Bytes(changed), read),
                "Changing the actual306 metadata must fail, not select a historical/guessed composition: " + mutation.Key);
        }
        byte[] CorruptInherited(string path)
        {
            byte[] raw = read(path);
            if (path == "settings-presnap-switch.json") { raw = (byte[])raw.Clone(); raw[0] ^= 1; }
            return raw;
        }
        Invalid(() => new PlayerData(new DataCatalog(manifest, CorruptInherited)),
            "Changing an actual inherited1114 setting must fail its original byte pin, even in the successor.");
        var options = PreviewOptions.FromUrl("", catalog.Profile.StableId);
        Check(options.GameflowEnabled && options.InstructionsEnabled && options.WorldEnabled &&
            options.RecordsEnabled && options.PreGameSoundEnabled && !options.FireEnabled &&
            options.KickMeters == SourceKickMeterMode.Disabled && options.GcSkillIndex == 1 && options.UserBaseAdjustment == 0,
            "The306 complete-source defaults must bind actual original services while preserving diagnostic opt-in defaults.");
        foreach (string profile in new[] { SourceTeamResourceProfile.C2M1StableId, SourceTeamResourceProfile.PreSnapSwitchStableId,
            SourceTeamResourceProfile.DefaultAudiblesStableId })
        {
            var historical = PreviewOptions.FromUrl("", profile);
            Check(!historical.GameflowEnabled && !historical.InstructionsEnabled && !historical.WorldEnabled &&
                !historical.RecordsEnabled && !historical.PreGameSoundEnabled && historical.UserBaseAdjustment == 0,
                "Omitted options must not promote a historical flight into the new services or skill producer.");
        }
        foreach (string query in new[] { "?source-records=disabled", "?source-world=disabled",
            "?source-instructions=disabled", "?source-compatibility=strict" })
            Invalid(() => PreviewOptions.FromUrl(query, catalog.Profile.StableId),
                "The new exact feature contract cannot silently disable or replace its required service: " + query);
        for (int index = 0; index < 3; index++)
        {
            var skill = PreviewOptions.FromUrl("?source-gc-skill=" + index, catalog.Profile.StableId);
            Check(skill.GcSkillIndex == index && skill.UserBaseAdjustment == new[] { -3, 0, 3 }[index],
                "Public GC_SKILL must map the actual original index into user_base_adjust, not Difficulty.");
        }
        foreach (string query in new[] { "?source-gc-skill=-1", "?source-gc-skill=3", "?source-gc-skill=01",
            "?source-gc-skill=5", "?source-gc-skill=1&source-gc-skill=1", "?source-gc-skill=1=extra" })
            Invalid(() => PreviewOptions.FromUrl(query, catalog.Profile.StableId),
                "Unknown/duplicate GC_SKILL indices must reject without clamping or operator-Difficulty inference.");
        Check(PreviewOptions.FromUrl("#?source-gc-skill=0", catalog.Profile.StableId).UserBaseAdjustment == 0,
            "Fragment text must not change actual per-game user configuration.");
        _ = new RenderAssets(data, new RecordingSink());
        catalog.RequireAllInputsConsumed();
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "complete-av306-actual-loader-admission-20261110/v1", assertions = assertions - before,
            profile = catalog.Profile.StableId, catalog.ManifestSha256, files = catalog.FileCount,
            retained = catalog.RetainedInputs.Count, consumed = catalog.ConsumedInputCount,
            clips = data.Animations.SequenceNames.Count, frames = data.Animations.FrameCount,
            courseEntries = data.CourseCamera!.Entries.Count, sameImpactCourseReference = true,
            inherited303Unchanged = true, gcSkillIndexToAdjustment = new[] { -3, 0, 3 },
            naturalMatchOrBrowserClaimed = false
        }));
    }

    private static void VerifyGcSkillBinding(PlayerData data)
    {
        int before = assertions;
        string profile = data.Catalog.Profile.StableId;
        SourceMatchConfiguration original = data.Configuration.Match;
        SourceMatchConfiguration Configuration(TeamId human, int cpuTeam, int difficulty = 5) =>
            new SourceMatchConfiguration(original.Profile, SourceStationLayout.FourStation,
                new SourceTeamSelection(human == TeamId.Home ? original.HomeTeam.TeamIndex : cpuTeam, original.HomeTeam.RosterIndices),
                new SourceTeamSelection(human == TeamId.Away ? original.AwayTeam.TeamIndex : cpuTeam, original.AwayTeam.RosterIndices),
                new[] { new SourceStationConfiguration((int)human * 2, human) }, human, SourceCaptainSlot.First,
                original.TimeAdjustment, difficulty, original.ContinueAfterCabinetGate, original.HomePlay, original.AwayPlay);
        SourceTeamGameplayView Gameplay(GameSimulation game, TeamId team)
        {
            SourceMatchView match = ((ISourceMatchState)game.State).Match;
            return team == TeamId.Home ? match.HomeGameplay : match.AwayGameplay;
        }
        var defaults = PreviewOptions.FromUrl("", profile);
        Check(defaults.GcSkillIndex == 1 && !defaults.GcSkillSpecified && defaults.UserBaseAdjustment == 0 &&
            PreviewOptions.FromUrl("#?source-gc-skill=0", profile).UserBaseAdjustment == 0,
            "Omission/fragment retain clear_user_config's zero adjustment, independently of operator Difficulty.");
        int cpu = Enumerable.Range(0, 31).First(team => data.Rosters.GetTeamData(team).DroneBase == 6);
        var rows = new List<object>();
        foreach (TeamId human in new[] { TeamId.Home, TeamId.Away })
            for (int index = 0; index < 3; index++)
            {
                var options = PreviewOptions.FromUrl("?source-gc-skill=" + index, profile);
                var config = Configuration(human, cpu);
                GameSimulation game = KernelBinding.Create(data, options, matchConfiguration: config);
                int expectedAdjustment = new[] { -3, 0, 3 }[index];
                Check(options.GcSkillSpecified && options.GcSkillIndex == index &&
                    options.UserBaseAdjustment == expectedAdjustment,
                    "The public original GC_SKILL index must select the exact source adjustment.");
                Check(game.SourceTick == 0 && game.State.Tick == 0 &&
                    ((ISourceMatchState)game.State).Match.Configuration.Difficulty == 5,
                    "User configuration must reach the actual constructor before wake1 without remapping operator Difficulty.");
                Check(Gameplay(game, human).RawHandicap == 5 &&
                    Gameplay(game, (TeamId)(1 - (int)human)).RawHandicap == 6 + expectedAdjustment,
                    "Actual set_handicaps constructor output must consume GC_SKILL, not silently use its zero default.");
                rows.Add(new { human = human.ToString(), gcSkillIndex = index, userBaseAdjustment = expectedAdjustment,
                    cpuTeam = cpu, sourceDroneBase = 6, actualDroneHandicap = Gameplay(game, (TeamId)(1 - (int)human)).RawHandicap,
                    sourceTick = game.SourceTick });
            }
        foreach (int difficulty in new[] { 0, 10 })
        {
            GameSimulation game = KernelBinding.Create(data, defaults,
                matchConfiguration: Configuration(TeamId.Home, cpu, difficulty));
            Check(Gameplay(game, TeamId.Away).RawHandicap == 6 &&
                ((ISourceMatchState)game.State).Match.Configuration.Difficulty == difficulty,
                "Operator Difficulty must not be invented as the public GC_SKILL producer.");
        }
        var supplied = new SourceHandicapSettings(2, 4, true, 1, 2, 3, true, true);
        Check(ReferenceEquals(KernelBinding.ResolveHandicapSettings(defaults, supplied), supplied),
            "Omitted public GC_SKILL must preserve the caller's existing immutable typed settings.");
        var selected = PreviewOptions.FromUrl("?source-gc-skill=2", profile);
        var resolved = KernelBinding.ResolveHandicapSettings(selected, supplied);
        Check(resolved.UserBaseAdjustment == 3 && resolved.HumanWinStreak == 4 && resolved.RandomDrones &&
            resolved.OffenseTeamMask == 1 && resolved.DefenseTeamMask == 2 && resolved.DronesTeamMask == 3 &&
            resolved.NoAssist && resolved.SuperDrones,
            "Composing an explicit public skill may replace only user_base_adjust, retaining every other actual setting.");
        foreach (string value in new[] { "", "-1", "3", "5", "01", "1.0", "1=extra", "1&source-gc-skill=2" })
        {
            bool rejected = false;
            try { PreviewOptions.FromUrl("?source-gc-skill=" + value, profile); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid public GC_SKILL must reject without defaulting or clamping: " + value);
        }
        bool lowerGuard = false;
        try
        {
            KernelBinding.Create(data, defaults, matchConfiguration: Configuration(TeamId.Home, cpu),
                handicapSettings: new SourceHandicapSettings(userBaseAdjustment: -4));
        }
        catch (NotSupportedException error)
        { lowerGuard = error.Message.Contains("base=2") && error.Message.Contains("No invented lower clamp"); }
        Check(lowerGuard, "The original diff_settings[base-3] undefined branch must stay visible after public binding.");
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "actual-public-gc-skill-binding-20261110/v1", assertions = assertions - before,
            profile, dataManifestSha256 = data.Catalog.ManifestSha256, originalGcSkillId = 5,
            sourceMapping = new[] { -3, 0, 3 }, defaultsUnchanged = true,
            operatorDifficultyRemapped = false, originalLowerGuardRetained = true,
            privateStateMutation = false, beforeFirstSourceWake = true, cases = rows
        }));
    }

    private sealed class TwoStationRecordsStorage : ISourcePlayerRecordStorage
    {
        internal SourceRecordStorageImage Image = new(null);
        internal int Writes;
        public Task<SourceRecordStorageImage> ReadAsync() => Task.FromResult(Image);
        public Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement)
        {
            Writes++;
            if (Image.Primary != expectedPrimary) return Task.FromResult(false);
            Image = replacement;
            return Task.FromResult(true);
        }
    }

    private static void VerifyTwoStationRecords(PlayerData data, byte[] manifest, Func<string, byte[]> read)
    {
        int before = assertions;
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "named-two-station-loaded-assembly-identity/v1",
            coreSha256 = DataCatalog.Hash(File.ReadAllBytes(typeof(GameSimulation).Assembly.Location)),
            coreMvid = typeof(GameSimulation).Module.ModuleVersionId,
            checksSha256 = DataCatalog.Hash(File.ReadAllBytes(typeof(Checks).Assembly.Location)),
            checksMvid = typeof(Checks).Module.ModuleVersionId
        }));
        var catalog = data.Catalog;
        Check(catalog.Profile.IsCompleteAv306TwoStationRecords && !catalog.Profile.IsCompleteAv306 &&
            !catalog.Profile.IsCompleteOriginal303 && catalog.Profile.UsesCompleteAv306Assets &&
            catalog.ManifestSha256 == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsManifestSha256 &&
            catalog.Factory == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsFactory,
            "Named TwoStation must use its real new typed profile/factory, never a renamed legacy306/303 flight.");
        Check(catalog.FileCount == 1154 && catalog.ByteCount == 300324845 && catalog.RetainedInputs.Count == 43 &&
            catalog.RetainedInputs.Any(pin => pin.Path == "settings-presnap-switch.json") &&
            catalog.Entry("settings") == DataCatalog.CompleteAv306TwoStationRecordsSettingsPath &&
            data.Configuration.Match.StationLayout == SourceStationLayout.TwoStation &&
            data.Configuration.Match.Stations.Select(station => (station.StationId, station.Team))
                .SequenceEqual(new[] { (0, TeamId.Home), (1, TeamId.Away) }),
            "All1153 old pins must remain unchanged, with inactive FourStation settings and actual new mapped TwoStation settings.");
        var options = PreviewOptions.FromUrl("", catalog.Profile.StableId);
        Check(options.CabinetLayout == SourceStationLayout.TwoStation && options.CabinetStationsMask == 3 &&
            options.RecordsEnabled && options.PreGameSoundEnabled && options.GameflowEnabled &&
            !options.FireEnabled && options.KickMeters == SourceKickMeterMode.Disabled,
            "The public new profile must default to both actual opposing stations without changing diagnostic opt-ins.");
        void Invalid<T>(Action action, string message) where T : Exception
        {
            bool rejected = false;
            try { action(); } catch (T) { rejected = true; }
            Check(rejected, message);
        }
        foreach (string query in new[] { "?source-cabinet=four-station", "?source-cabinet=two-station&source-stations=0",
            "?source-stations=0,1,2,3", "?source-records=disabled", "?source-compatibility=strict" })
            Invalid<InvalidDataException>(() => PreviewOptions.FromUrl(query, catalog.Profile.StableId),
                "The new record feature cannot alias another layout, participation or disabled record service: " + query);
        foreach (string key in new[] { "profile", "factory", "ancestorManifestSha256" })
        {
            var changed = System.Text.Json.Nodes.JsonNode.Parse(manifest)!;
            changed[key] = "not-the-authenticated-two-station-feature";
            Invalid<InvalidDataException>(() => new DataCatalog(JsonSerializer.SerializeToUtf8Bytes(changed), read),
                "The actual new named profile tuple must reject changed metadata: " + key);
        }
        var storage = new TwoStationRecordsStorage();
        var randomOrigins = new Dictionary<PlayerHost, long>();
        var identities = new[] { new SourceRecordIdentity("AAAAAA", "1111"), new SourceRecordIdentity("BBBBBB", "2222") };
        (PlayerHost Host, CompleteAvSink Sink, SourcePlayerRecordStore Store, SourceInjectedRandom Random) Open(
            TwoStationRecordsStorage? backing = null)
        {
            var store = new SourcePlayerRecordStore(backing ?? storage, SourceRecordSavePolicy.CustomBlockHistoryV3);
            store.LoadAsync().GetAwaiter().GetResult();
            var sink = new CompleteAvSink();
            SourceInjectedRandom ownedRandom = null!;
            var host = new PlayerHost(data, sink, options: options, recordStore: store,
                randomFactory: seed => ownedRandom = (SourceInjectedRandom)SourcePreGameSoundService.CreatePortableRandom(seed));
            host.Advance(0, 512, 384, 512, 384, 1);
            catalog.RequireAllInputsConsumed();
            return (host, sink, store, ownedRandom);
        }
        SourceCabinetRecordsRuntime Records(PlayerHost host) => host.Session.Records ??
            throw new InvalidOperationException("The actual opted Host must own its record runtime.");
        void Raw(PlayerHost host, int button = 0, int station = 0, int x = 0, int y = 0)
        {
            var axes = new double[8]; var buttons = new int[4];
            axes[station * 2] = x; axes[station * 2 + 1] = y; buttons[station] = button;
            host.Capture(axes, buttons);
        }
        void MenuPump(PlayerHost host, int count = 70)
        {
            for (int i = 0; i < count && host.Session.UsingSourcePreGameInput; i++)
            { Raw(host); host.Advance(1d / 57, 512, 384, 512, 384, 1); Thread.Yield(); }
        }
        void Tap(PlayerHost host, int button, int station = 0, int x = 0, int y = 0)
        {
            Raw(host); Raw(host, button, station, x, y); Raw(host);
            host.Advance(0, 512, 384, 512, 384, 1); MenuPump(host);
        }
        void Move(PlayerHost host, int x, int y, int station = 0) => Tap(host, 0, station, x, y);
        void Settle(PlayerHost host)
        {
            for (int i = 0; i < 5000 && !Records(host).PendingOperation.IsCompleted; i++)
            { MenuPump(host, 1); Thread.Yield(); }
            Check(Records(host).PendingOperation.IsCompleted,
                "The actual source menu must consume the queued durable storage completion.");
            Records(host).PendingOperation.GetAwaiter().GetResult();
            Raw(host); host.Advance(0, 512, 384, 512, 384, 1);
        }
        void BeginNames(PlayerHost host)
        {
            Raw(host); Tap(host, 4); Tap(host, 1);
            Check(host.Session.Stage == SourceCabinetMenuStage.Records &&
                Records(host).View().Kind == SourceRecordScreenKind.Selection &&
                Records(host).View().Stations.Select(row => row.StationId).SequenceEqual(new[] { 0, 1 }),
                "Both original TwoStation physical name/PIN cursors must be reached through public C/options/A.");
        }
        void Key(PlayerHost host, int target, int station, bool pin = false, bool playName = false)
        {
            int Cursor() => playName ? Records(host).View().InputCursor :
                Records(host).View().Stations.Single(row => row.StationId == station).Cursor;
            int columns = pin ? 3 : 4;
            while (Cursor() / columns > target / columns) Move(host, 0, -1, station);
            while (Cursor() / columns < target / columns) Move(host, 0, 1, station);
            while (Cursor() > target) Move(host, -1, 0, station);
            while (Cursor() < target) Move(host, 1, 0, station);
            Tap(host, 1, station);
        }
        void Enter(PlayerHost host, int station)
        {
            Move(host, 0, -1, station); Tap(host, 1, station);
            foreach (char c in identities[station].Name) Key(host, "ABCDEFGHIJKLMNOPQRSTUVWXYZ-*! ".IndexOf(c), station);
            foreach (char c in identities[station].CopyPinForLocalPersistence())
                Key(host, "123456789 0".IndexOf(c), station, pin: true);
        }
        void ActionRow(PlayerHost host, int station, string label)
        {
            int target = Records(host).View().Rows.ToList().FindIndex(row => row.Text.StartsWith(label, StringComparison.Ordinal));
            Check(target >= 0, "The real public record action must exist: " + label);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int current = Records(host).View().Rows.ToList().FindIndex(row => row.Selected);
                if (current == target) { Tap(host, 1, station); return; }
                Move(host, 0, current < target ? 1 : -1, station);
            }
            throw new InvalidOperationException("The public action cursor failed to reach " + label);
        }
        void StartMatch(PlayerHost host)
        {
            Tap(host, 4);
            Check(host.Session.Stage == SourceCabinetMenuStage.Teams, "Both deliberate named choices must reach the actual team screen.");
            foreach (int station in new[] { 0, 1 }) Tap(host, 1, station);
            foreach (int station in new[] { 0, 1 }) Tap(host, 1, station);
            Check(host.Session.Stage == SourceCabinetMenuStage.Versus,
                "Both original side captains, not FourStation P2-as-teammate, must confirm teams and rosters.");
            Move(host, 0, 1); Move(host, 1, 0);
            Tap(host, 1);
            randomOrigins.Add(host, host.PreGameSounds.SourceTick);
            Check(host.Session.Playing && host.Game.SourceTick == 0 &&
                ReferenceEquals(host.Game.SourceTwoStationRecordProfile, SourceTeamResourceProfile.CompleteAv306TwoStationRecords) &&
                ReferenceEquals(host.Game.SourceCustomPlaySession.Configuration, host.MatchConfiguration) &&
                ReferenceEquals(host.Game.SourceCustomPlaySession.SavedRecords.Configuration, host.MatchConfiguration) &&
                ReferenceEquals(host.Session.LastConfiguration, host.MatchConfiguration) &&
                host.MatchConfiguration.Captain == SourceCaptainSlot.Second,
                "The actual public start must construct the new-capability Game and bind its SAME records/configuration once before wake1.");
        }
        SourceMatchView Match(PlayerHost host) => ((ISourceMatchState)host.Game.State).Match;
        void SourceWake(PlayerHost host, int button = 0, int station = 0)
        {
            long tick = host.Game.SourceTick;
            do { Raw(host, button, station); host.Advance(1d / 60, 512, 384, 512, 384, 1); }
            while (host.Game.SourceTick == tick);
            Check(host.Game.SourceTick == tick + 1, "The actual public host must dispatch every original source wake in order.");
        }
        void GameTap(PlayerHost host, int button, int station)
        {
            SourceWake(host); SourceWake(host);
            SourceWake(host, button, station); SourceWake(host, button, station);
            SourceWake(host); SourceWake(host); SourceWake(host);
        }
        void ReachSelection(PlayerHost host)
        {
            for (int i = 0; i < 4000 && !(Match(host).Phase == SourceMatchPhase.PlaySelection &&
                Match(host).PlayKind == SourcePlayKind.Normal); i++) SourceWake(host);
            Check(Match(host).Phase == SourceMatchPhase.PlaySelection && Match(host).PlayKind == SourcePlayKind.Normal &&
                Match(host).Possession.Offense == TeamId.Away,
                "Unmodified source kickoff/instructions must reach physical station1's actual offensive play selector.");
            for (int i = 0; i < 1000 && host.Session.ReadPlaySelectionInstructionGate() >= 10; i++) SourceWake(host);
            for (int i = 0; i < 40; i++) SourceWake(host);
        }
        int Page(CompleteAvSink sink) => sink.State.GetProperty("menu").GetProperty("away").GetProperty("page").GetInt32();
        void CustomPage(PlayerHost host, CompleteAvSink sink)
        {
            for (int page = 1; page <= 4; page++)
            {
                GameTap(host, 4, 1);
                Check(Page(sink) == page, "Physical station1's real C must advance the actual offensive page, including its allocated custom page.");
            }
        }
        var firstNamed = Open(new TwoStationRecordsStorage());
        BeginNames(firstNamed.Host); Enter(firstNamed.Host, 0);
        Tap(firstNamed.Host, 1); Settle(firstNamed.Host); Tap(firstNamed.Host, 1, 1);
        StartMatch(firstNamed.Host);
        var firstRecords = firstNamed.Host.Game.SourceCustomPlaySession.SavedRecords;
        Check(firstRecords.Station(0).State == SourceRecordSelectionState.KnownNew &&
            ReferenceEquals(firstRecords.Station(0).Record, firstNamed.Store.Current.Find(identities[0])) &&
            firstRecords.Station(0).Record.CustomBlockState == SourceCustomBlockState.NeverAllocated &&
            firstRecords.Station(1).State == SourceRecordSelectionState.Guest &&
            firstRecords.Station(1).Identity == null && firstRecords.Station(1).Record == null,
            "An actual just-created named human plus deliberately selected opponent Guest must bind as KnownNew/Guest before any CAP refresh.");
        SourceWake(firstNamed.Host);
        Check(firstNamed.Host.Game.SourceTick == 1, "The genuinely new named/Guest match must enter the actual production source step.");
        firstNamed.Host.SetActive(false);
        var created = Open();
        BeginNames(created.Host);
        for (int station = 0; station < 2; station++)
        {
            Enter(created.Host, station);
            Check(Records(created.Host).View().Stations.Single(row => row.StationId == station).Step == "Missing" &&
                created.Store.Current?.Find(identities[station]) == null,
                "A missing identity is not silently created or replaced with a guest.");
            Tap(created.Host, 1, station); Settle(created.Host);
            SourcePlayerRecord record = created.Store.Current!.Find(identities[station])!;
            Check(record.Career.GamesPlayed == 0 && record.CustomBlockState == SourceCustomBlockState.NeverAllocated &&
                record.CustomPlays.All(play => play == null),
                "Each actual durable new named record must retain known-never-allocated logical history.");
        }
        for (int station = 0; station < 2; station++)
        {
            Tap(created.Host, 1, station); Tap(created.Host, 1, station);
            Check(Records(created.Host).View().Kind == SourceRecordScreenKind.CustomSlots,
                "Each real selected identity must enter the actual source CAP slots before an empty exit.");
            int writes = storage.Writes;
            Tap(created.Host, 2, station); Settle(created.Host);
            SourcePlayerRecord record = created.Store.Current.Find(identities[station])!;
            Check(record.CustomBlockState == SourceCustomBlockState.Allocated && record.CustomPlays.All(play => play == null) &&
                storage.Writes == writes + 1,
                "Original CAP exit WITHOUT editing must durably acquire an empty logical block, not infer None from nine nulls.");
            Tap(created.Host, 2, station);
        }
        var empty = Open();
        BeginNames(empty.Host); Enter(empty.Host, 0); Enter(empty.Host, 1);
        StartMatch(empty.Host);
        SourceSavedRecordSession emptyRecords = empty.Host.Game.SourceCustomPlaySession.SavedRecords;
        foreach (int station in new[] { 0, 1 })
            Check(ReferenceEquals(emptyRecords.Station(station).Record, empty.Store.Current.Find(identities[station])) &&
                emptyRecords.Station(station).State == SourceRecordSelectionState.KnownSaved &&
                empty.Host.Game.SourceCustomPlaySession.InventoryFor(station) == SourceCustomPlayInventoryState.KnownSaved &&
                empty.Host.Game.IsSourceCustomPageAvailable(station) &&
                new SourceCustomPlaySession(emptyRecords).InventoryFor(station) == SourceCustomPlayInventoryState.KnownNone,
                "Fresh saved-name reload must preserve acquired-empty versus legacy Exists-only inventory without changing the legacy constructor.");
        ReachSelection(empty.Host); CustomPage(empty.Host, empty.Sink);
        var random = empty.Random;
        int fallbackBefore = random.Trace.Count(draw => draw.Site.SourceIdentity == "playsel.c:PlaySel__MakeChoice:2847");
        GameTap(empty.Host, 2, 1);
        Check(Match(empty.Host).Phase == SourceMatchPhase.PlaySelection && !Match(empty.Host).SelectionTimer.Selected(TeamId.Away) &&
            random.Trace.Count(draw => draw.Site.SourceIdentity == "playsel.c:PlaySel__MakeChoice:2847") == fallbackBefore,
            "An actual manual B on an acquired-empty custom slot must make no selection and consume no fallback draw.");
        long forceTick = -1;
        for (int i = 0; i < 4000 && Match(empty.Host).Phase == SourceMatchPhase.PlaySelection; i++)
        {
            if (Match(empty.Host).SelectionTimer.ForceSelection) forceTick = Match(empty.Host).SelectionTimer.ForceTick;
            SourceWake(empty.Host);
        }
        SourceRandomDraw[] fallback = random.Trace.Where(draw => draw.Site.SourceIdentity == "playsel.c:PlaySel__MakeChoice:2847").ToArray();
        Check(fallback.Length == fallbackBefore + 1 && forceTick >= 0 && fallback.Last().Site.BoundOrModulus == 9 &&
            fallback.Last().Site.Process == "game_proc" && fallback.Last().Site.ActorId == -1 &&
            fallback.Last().Site.SourceTick > randomOrigins[empty.Host] + forceTick && Match(empty.Host).AwayPlay.PageIndex == 0 &&
            Match(empty.Host).AwayPlay.SlotIndex == fallback.Last().Value && Match(empty.Host).AwayPlay.SavedOrigin == null,
            "Only the genuine expired source timer/game_proc may draw one original first-page randrng(9) for acquired-empty inventory.");
        empty.Host.SetActive(false);
        Tap(created.Host, 1, 1); Tap(created.Host, 1, 1); Tap(created.Host, 1, 1);
        Check(Records(created.Host).View().Kind == SourceRecordScreenKind.Editor,
            "The actual saved station1 must open its own source custom editor.");
        ActionRow(created.Host, 1, "EDIT RECEIVER 1"); Tap(created.Host, 1, 1);
        for (int i = 0; i < 6; i++) Move(created.Host, 0, -1, 1);
        Tap(created.Host, 1, 1);
        for (int i = 0; i < 3; i++) Move(created.Host, 0, 1, 1);
        Tap(created.Host, 1, 1);
        for (int i = 0; i < 6; i++) Move(created.Host, 1, 0, 1);
        Tap(created.Host, 1, 1);
        for (int i = 0; i < 5; i++) Move(created.Host, 0, 1, 1);
        Tap(created.Host, 1, 1);
        ActionRow(created.Host, 1, "PRIMARY:"); ActionRow(created.Host, 1, "QB:");
        ActionRow(created.Host, 1, "NAME PLAY");
        foreach (char c in "AWAY") Key(created.Host, c - 'A', 1, playName: true);
        Key(created.Host, 31, 1, playName: true);
        foreach (char c in "BOOK") Key(created.Host, c - 'A', 1, playName: true);
        Key(created.Host, 31, 1, playName: true);
        ActionRow(created.Host, 1, "SAVE PLAY"); Settle(created.Host);
        SourceCustomPlay saved = created.Store.Current.Find(identities[1])!.CustomPlays[0]!;
        Check(saved.Exists && saved.Name1 == "AWAY" && saved.Name2 == "BOOK" &&
            saved.Point(0, 1).X == 9 && saved.Point(0, 1).Y == 16 &&
            (saved.Point(0, 1).Flags & SourceCustomPointFlags.Turbo) != 0 &&
            saved.Point(0, 2).X == 15 && saved.Point(0, 2).Y == 18 &&
            (saved.Flags & (SourceCustomPlayFlags.PrimaryReceiver1 | SourceCustomPlayFlags.QuarterbackFar)) ==
                (SourceCustomPlayFlags.PrimaryReceiver1 | SourceCustomPlayFlags.QuarterbackFar) &&
            created.Store.Current.Find(identities[0])!.CustomPlays.All(play => play == null),
            "Public P2 editor controls must save its actual route/names/flags without writing physical P1's record.");
        Tap(created.Host, 4, 1); Move(created.Host, -1, 0, 1); Tap(created.Host, 4, 1);
        for (int i = 0; i < 6; i++) Move(created.Host, 0, 1, 1);
        Tap(created.Host, 1, 1); Settle(created.Host);
        Check(created.Store.Current.Find(identities[1])!.Audibles.Offense.SequenceEqual(new short[] { -37, 2, 3 }) &&
            created.Store.Current.Find(identities[0])!.Audibles.Offense.All(id => id == 0),
            "The actual signed custom audible must persist for original TwoStation Away/P2 only.");
        Tap(created.Host, 2, 1); Settle(created.Host); Tap(created.Host, 2, 1);
        StartMatch(created.Host);
        Console.WriteLine(JsonSerializer.Serialize(new {
            checkpoint = "named-two-created-after-cap-save",
            sameConfiguration = ReferenceEquals(created.Host.Game.SourceCustomPlaySession.Configuration,
                created.Host.Game.SourceCustomPlaySession.SavedRecords.Configuration),
            rows = created.Host.Game.SourceCustomPlaySession.SavedRecords.Stations.Select(row => new {
                station = row.StationId, state = row.State.ToString(), recordPresent = row.Record != null,
                sameStoredRecord = ReferenceEquals(row.Record, created.Store.Current.Find(identities[row.StationId])),
                gp0 = row.Record != null && row.Record.Career.GamesPlayed == 0
            })
        }));
        Check(created.Host.Game.SourceCustomPlaySession.SavedRecords.Stations.All(row =>
                row.State == SourceRecordSelectionState.KnownSaved && row.Record != null &&
                ReferenceEquals(row.Record, created.Store.Current.Find(identities[row.StationId]))),
            "Successful CAP-exit refresh must bind both actual GP0 named records as KnownSaved without inventing a prior played career.");
        SourceWake(created.Host);
        Check(created.Host.Game.SourceTick == 1, "The genuinely created named match must reach the actual production source step.");
        created.Host.SetActive(false);
        var loaded = Open();
        BeginNames(loaded.Host); Enter(loaded.Host, 0); Enter(loaded.Host, 1); StartMatch(loaded.Host);
        var bound = loaded.Host.Game.SourceCustomPlaySession.SavedRecords;
        Check(bound.Stations.All(row => row.State == SourceRecordSelectionState.KnownSaved && row.Record.Career.GamesPlayed == 0) &&
            ReferenceEquals(bound.Station(1).Record, loaded.Store.Current.Find(identities[1])) &&
            bound.Station(1).Record.CustomPlays[0].Points.SequenceEqual(saved.Points) &&
            bound.Station(1).Record.Audibles.Offense[0] == -37 &&
            bound.Station(0).Record.CustomBlockState == SourceCustomBlockState.Allocated,
            "Fresh actual storage/name/PIN reload must recover the correct unplayed careers, block history, routes and signed audibles together.");
        Invalid<NotSupportedException>(() => loaded.Host.Game.BindSourceCustomPlaySession(new SourceCustomPlaySession(bound), data.Hud),
            "The unchanged legacy binder must still reject named TwoStation rather than gaining the new capability.");
        Invalid<InvalidOperationException>(() => loaded.Host.Game.BindSourceTwoStationSavedRecords(bound, data.Hud),
            "The actual new saved session may be bound only once.");
        var bootstrap = SourceC2Bootstrap.FromCompleteAv306VersusSoundBanks(data.Callbacks,
            Enumerable.Range(0, 31).Select(data.Rosters.GetSourceSoundBankName).ToArray());
        GameSimulation oldFactory = SourceC2Composition.CreateCompleteAv306Simulation(data.Animations, data.ReferencePivots,
            data.Playbook, loaded.Host.MatchConfiguration, data.Rosters, data.Configuration.Presentation, data.Movement,
            data.MovementSettings, data.Sound, bootstrap, compatibility: SourceGameplayCompatibility.Portable,
            defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact, courseCamera: data.CourseCamera);
        Check(oldFactory.SourceTwoStationRecordProfile == null, "Legacy AV306 factory creation must retain a null named-Two capability.");
        Invalid<NotSupportedException>(() => oldFactory.BindSourceTwoStationSavedRecords(bound, data.Hud),
            "Calling the new public binder cannot enable a legacy factory.");
        ReachSelection(loaded.Host); CustomPage(loaded.Host, loaded.Sink);
        Check(loaded.Sink.State.GetProperty("hud").GetProperty("elements").EnumerateArray().Any(row =>
                row.GetString() == "selection/Away/custom/0/route/0/1"),
            "The actual public custom page must render physical station1's saved route, not a catalog placeholder.");
        GameTap(loaded.Host, 2, 0); GameTap(loaded.Host, 2, 1);
        for (int i = 0; i < 120 && Match(loaded.Host).Phase != SourceMatchPhase.PreSnap; i++) SourceWake(loaded.Host);
        SourcePlaySelection chosen = Match(loaded.Host).AwayPlay;
        Check(Match(loaded.Host).Phase == SourceMatchPhase.PreSnap && chosen.SavedOrigin != null &&
            chosen.SavedOrigin.StationId == 1 && chosen.SavedOrigin.SlotIndex == 0 &&
            ReferenceEquals(loaded.Host.Game.SourceCustomPlaySession.SavedRecords, bound) &&
            chosen.PlaySymbol == SourceCustomPlayCodec.ConvertToPlay(saved, 1, 0, data.Playbook).Identity &&
            Match(loaded.Host).SelectionTimer?.ForceSelection != true,
            "The actual public P2 B must install that SAME saved offensive definition in Core without forced selection or a FourStation teammate alias.");
        foreach (SourceStationView station in Match(loaded.Host).Stations)
        {
            TeamId side = loaded.Host.MatchConfiguration.Stations.Single(row => row.StationId == station.StationId).Team;
            Check(station.ControlledPlayerId >= 0 && station.ControlledPlayerId / 7 == (int)side &&
                loaded.Host.Game.State.Players[station.ControlledPlayerId].Source.ControllerStationId == station.StationId &&
                loaded.Host.Game.State.Players[station.ControlledPlayerId].Team == side && station.StationId == (int)side,
                "Both named TwoStation humans must control the correct actual Core side/player, not merely appear on the menu.");
        }
        using var description = JsonDocument.Parse(JsonSerializer.Serialize(KernelBinding.Describe(loaded.Host.Game)));
        Check(description.RootElement.GetProperty("resourceProfile").GetString() == catalog.Profile.StableId &&
            description.RootElement.GetProperty("factory").GetString() == catalog.Factory,
            "Public diagnostics must use the actual readonly creation capability, not infer a legacy306 identity from shared callback hashes.");
        loaded.Host.SetActive(false);
        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "actual-public-named-two-station-records-20261110/v1", assertions = assertions - before,
            catalog.ManifestSha256, profile = catalog.Profile.StableId, catalog.Factory,
            files = catalog.FileCount, retained = catalog.RetainedInputs.Count, consumed = catalog.ConsumedInputCount,
            publicRawInputOnly = true, privateStateMutation = false, nativeDiskCrcClaimed = false,
            capEmptyExitReload = true, manualMissingDraws = 0, forcedFallbackDraws = 1,
            forceTick, randomOrigin = randomOrigins[empty.Host],
            fallbackTick = fallback.Last().Site.SourceTick - randomOrigins[empty.Host], fallbackSlot = fallback.Last().Value,
            savedOriginStation = chosen.SavedOrigin!.StationId, savedOriginSlot = chosen.SavedOrigin.SlotIndex,
            sourceTick = loaded.Host.Game.SourceTick, savedCareerGamesPlayed = 0, naturalCompletedCareerClaimed = false
        }));
    }

    private static void CabinetGuestToTeams(PlayerHost host)
    {
        Check(host.Session.UsingSourcePreGameInput && host.Session.Records != null &&
            host.Session.Stage == SourceCabinetMenuStage.Joining,
            "The shared guest helper requires the real fresh source-clocked cabinet join screen.");
        void Raw(int button = 0, int x = 0) =>
            host.Capture(new double[] { x, 0, 0, 0, 0, 0, 0, 0 }, new[] { button, 0, 0, 0 });
        void Action(int button = 0, int x = 0)
        {
            Raw(); Raw(button, x); Raw();
            host.Advance(0, 512, 384, 512, 384, 1);
            for (int i = 0; i < 70 && host.Session.Stage != SourceCabinetMenuStage.Teams; i++)
            { Raw(); host.Advance(1d / 57, 512, 384, 512, 384, 1); Thread.Yield(); }
        }
        Action(4); Action(x: 1); Action(1);
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams && host.Game.SourceTick == 0,
            "Actual C/options/RIGHT/Guest/A must reach the first team-screen source wake without advancing the game backdrop.");
    }

    private static PlayerHost CabinetHost(PlayerData data, out RecordingSink sink)
    {
        sink = new RecordingSink();
        return new PlayerHost(data, sink, options: PreviewOptions.FromUrl("?source-gameflow=portable", data.Catalog.Profile.StableId));
    }

    private static void CabinetRender(PlayerHost host, int steps = 0) =>
        host.Advance(FixedStepBridge.StepSeconds * steps, 512, 384, 512, 384, 1);

    private static void CabinetRaw(PlayerHost host, int[]? buttons = null, double[]? axes = null) =>
        host.Capture(axes ?? new double[8], buttons ?? new int[4]);

    private static void CabinetTap(PlayerHost host, int button, params int[] stations)
    {
        CabinetRaw(host);
        var buttons = new int[4];
        foreach (int station in stations) buttons[station] = button;
        CabinetRaw(host, buttons);
        CabinetRaw(host);
        CabinetRender(host);
    }

    private static void CabinetMove(PlayerHost host, int station, int x, int y)
    {
        CabinetRaw(host);
        var axes = new double[8]; axes[station * 2] = x; axes[station * 2 + 1] = y;
        CabinetRaw(host, axes: axes);
        CabinetRaw(host);
        CabinetRender(host);
    }

    private static void CabinetStart(PlayerHost host, params int[] extraStations)
    {
        if (extraStations.Length != 0) CabinetTap(host, 1, extraStations);
        CabinetTap(host, 4, 0);
        int[] captains = extraStations.Any(id => id >= 2) ? new[] { 0, 2 } : new[] { 0 };
        CabinetTap(host, 1, captains);
        CabinetTap(host, 1, captains);
        CabinetTap(host, 1, 0);
        Check(host.Session.Playing && host.Session.MatchesStarted > 0 && host.Game.SourceTick == 0,
            "Public cabinet choices must construct the actual chosen match before its first source wake.");
        CabinetRaw(host);
    }

    private static string CabinetText(RecordingSink sink, string element) =>
        sink.State.GetProperty("hud").GetProperty("text").EnumerateArray()
            .Where(item => item.GetProperty("Element").GetString() == element)
            .Select(item => item.GetProperty("text").GetString()!).Single();

    private static void CabinetToStage(PlayerHost host, SourceCabinetFlowStage stage, int held = 7, int maximum = 2200)
    {
        for (int wake = 0; wake < maximum && CabinetFlow(host).Stage != stage; wake++)
        {
            CabinetRaw(host, new[] { held, 0, 0, 0 });
            CabinetRender(host, 1);
        }
        Check(CabinetFlow(host).Stage == stage,
            "Actual cabinet host did not reach " + stage + "; got " + CabinetFlow(host).Stage);
        CabinetRender(host);
    }

    private static void CabinetNextOptions(PlayerHost host)
    {
        CabinetToStage(host, SourceCabinetFlowStage.FinalConfirmation);
        CabinetRaw(host);
        CabinetRender(host, 3);
        host.Continue(0);
        CabinetRaw(host);
        for (int tick = 0; tick < 4 && host.Session.Playing; tick++) CabinetRender(host, 1);
        Check(host.Session.Stage == SourceCabinetMenuStage.NextMatch && host.Game.State.Phase == GamePhase.GameOver,
            "Public terminal Continue must reach next-match choices without erasing the retained final football state.");
    }

    private static void VerifyCabinetSession(PlayerData data)
    {
        int before = assertions;
        string profile = data.Catalog.Profile.StableId;
        foreach (SourceRosterTeam team in data.Rosters.TeamData)
        {
            Check(SourceHudText.Width(data.Hud.Font("bast18"), team.Name.ToUpperInvariant()) > 0 &&
                SourceHudText.Width(data.Hud.Font("bast13"), team.Home.ToUpperInvariant()) > 0,
                "Every actual selectable team needs original-font glyphs, not an unvisited menu-time fallback.");
            foreach (SourceRosterPlayer player in team.Players)
                Check(SourceHudText.Width(data.Hud.Font("bast8t"),
                    player.Profile.FirstName.ToUpperInvariant() + " " + player.Profile.LastName.ToUpperInvariant()) > 0,
                    "Every selectable original roster record needs its genuine original-font name glyphs.");
        }
        for (int tip = 0; tip < 30; tip++)
        {
            var lines = SourceCabinetCoachingText.Lines(tip);
            Check(SourceHudText.Width(data.Hud.Font("bast18"), lines[0]) > 0 &&
                lines.Skip(1).All(line => SourceHudText.Width(data.Hud.Font("bast10"), line) > 0),
                "All source-selected coaching ordinals must fit the admitted original glyph domain.");
        }
        Check(!PreviewOptions.FromUrl("", profile).GameflowEnabled &&
            !PreviewOptions.FromUrl("#?source-gameflow=portable", profile).GameflowEnabled,
            "Old and fragment-only routes must not inherit the portable cabinet flight.");
        foreach (string url in new[] { "?source-gameflow=", "?source-gameflow=native", "?source-gameflow=portable&source-gameflow=portable",
            "?source-gameflow=portable&source-compatibility=strict", "?source-gameflow=portable=extra" })
        {
            bool rejected = false;
            try { PreviewOptions.FromUrl(url, profile); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed/strict cabinet options must reject instead of enabling a different composition: " + url);
        }
        var host = CabinetHost(data, out var sink);
        SourceMatchConfiguration approved = data.Configuration.Match;
        GameSimulation backdrop = host.Game;
        CabinetRender(host);
        Check(host.CabinetInputStations.SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            host.Session.Stage == SourceCabinetMenuStage.Joining && host.Game.SourceTick == 0 &&
            host.Game.SourceCustomPlaySession == null &&
            CabinetFlow(host).Configuration.ParticipatingStationMask == 1 &&
            CabinetText(sink, "cabinet/title") == "FREE PLAY - JOIN CABINET",
            "The opted-in actual World must present its original-atlas cabinet menu and expose four physical join inputs before wake1.");
        CabinetRender(host, 60);
        Check(host.Game.SourceTick == 0 && host.SourceWakes == 0,
            "Menu wall time cannot silently play the default match behind the choices.");
        CabinetRaw(host, new[] { 0, 1, 1, 1 });
        CabinetRaw(host);
        Check(host.Session.ParticipatingStationMask == 15, "Simultaneous public P2/P3/P4 joins must all survive the same sample.");
        CabinetTap(host, 2, 1);
        Check(host.Session.ParticipatingStationMask == 13, "B leaves exactly its physical cabinet station.");
        CabinetTap(host, 1, 1);
        CabinetRaw(host, new[] { 4, 0, 0, 0 });
        CabinetRaw(host, new[] { 1, 0, 1, 0 });
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams && host.Session.WaitingForNeutral,
            "Held-through transition buttons must not also confirm the next menu.");
        CabinetRaw(host);
        CabinetMove(host, 0, -1, 0);
        CabinetMove(host, 2, 1, 0);
        Check(((JsonElement)JsonSerializer.SerializeToElement(host.Session.Describe())).GetProperty("homeTeam").GetInt32() == 30,
            "Team navigation must wrap across all31 actual teams, not the two original defaults.");
        CabinetTap(host, 1, 0, 2);
        Check(host.Session.Stage == SourceCabinetMenuStage.Rosters, "Both source-side captains confirm the team choices once.");
        CabinetMove(host, 0, -1, 0);
        CabinetMove(host, 2, 1, 0);
        CabinetTap(host, 1, 0, 2);
        Check(host.Session.Stage == SourceCabinetMenuStage.Versus, "Seven-slot roster choices must lead to the explicit versus configuration.");
        CabinetMove(host, 0, 1, 0);
        CabinetMove(host, 0, 0, 1);
        CabinetMove(host, 0, 1, 0);
        CabinetTap(host, 1, 0);
        Check(!ReferenceEquals(backdrop, host.Game) && host.Session.MatchesStarted == 1 && host.Epoch == 1 &&
            host.MatchConfiguration.HomeTeam.TeamIndex == 30 && host.MatchConfiguration.AwayTeam.TeamIndex == 2 &&
            host.MatchConfiguration.HomeTeam.RosterIndices[0] == 15 && host.MatchConfiguration.AwayTeam.RosterIndices[0] == 1 &&
            host.MatchConfiguration.FirstPossession == TeamId.Away && host.MatchConfiguration.Captain == SourceCaptainSlot.Second &&
            host.MatchConfiguration.Stations.Select(s => s.StationId).SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            host.MatchConfiguration.Stations.All(s => s.Team == (TeamId)(s.StationId / 2)) &&
            ReferenceEquals(approved, data.Configuration.Match) && approved.HomeTeam.TeamIndex == 0 &&
            approved.Stations.Count == 1 && approved.HomeTeam.RosterIndices[0] == 0,
            "Only a new immutable factory configuration may change teams/records/humans; authenticated settings and physical sides remain unchanged.");
        var customSession = host.Game.SourceCustomPlaySession ??
            throw new InvalidOperationException("The selected new-player factory did not bind its explicit known-none inventory.");
        Check(ReferenceEquals(customSession.Configuration, host.MatchConfiguration) &&
            host.MatchConfiguration.Stations.All(s => customSession.InventoryFor(s.StationId) == SourceCustomPlayInventoryState.KnownNone),
            "Deliberate new free-play players bind explicit known-none inventories before wake1; absent saved data is not guessed empty on the backdrop/base route.");
        CabinetRender(host);
        var actors = sink.State.GetProperty("actors").EnumerateArray().ToArray();
        Check(actors[0].GetProperty("rosterTeam").GetInt32() == 30 && actors[0].GetProperty("rosterRecord").GetInt32() == 15 &&
            actors[7].GetProperty("rosterTeam").GetInt32() == 2 && actors[7].GetProperty("rosterRecord").GetInt32() == 1 &&
            actors.All(actor => actor.GetProperty("tint").GetString() == "none") &&
            actors[0].GetProperty("uniformHash").GetString() != actors[7].GetProperty("uniformHash").GetString(),
            "Actual selected original roster/uniform bindings must reach the fourteen-actor render packet without tint substitutes.");
        CabinetRaw(host, new[] { 1, 2, 4, 3 });
        CabinetRender(host, 3);
        Check(sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean(),
            "Held menu buttons cannot leak through the fresh-match neutral latch.");
        CabinetRaw(host);
        var observed = new List<SourceInputFrame>();
        long observedTick = host.Game.SourceTick;
        host.Bridge.AfterSimulationStep += () => {
            if (host.Game.SourceTick == observedTick) return;
            observedTick = host.Game.SourceTick;
            var actual = (PlayerCommand)FlowGet(FlowModel(host.Game), "SourceCurrentInput");
            if (actual.StationInputs != null) observed.Add(actual.StationInputs);
        };
        CabinetRaw(host, new[] { 1, 2, 4, 3 }, new[] { 1d, 0d, -1d, 0d, 0d, 1d, 0d, -1d });
        CabinetRaw(host);
        CabinetRender(host, 4);
        Check(observed.Any(frame => frame.Stations.Count == 4 && frame.Stations.Select(s => (int)s.Pressed).SequenceEqual(new[] { 1, 2, 4, 3 })) &&
            observed.Any(frame => frame.Stations.Count == 4 && frame.Stations.Select(s => (int)s.Released).SequenceEqual(new[] { 1, 2, 4, 3 })),
            "The real source consumer must see simultaneous four-station press AND release edges captured between rendered frames.");
        host.SetActive(false);
        CabinetRaw(host, new[] { 7, 7, 7, 7 });
        long paused = host.Game.SourceTick;
        CabinetRender(host, 8);
        host.SetActive(true);
        CabinetRaw(host, new[] { 7, 7, 7, 7 });
        CabinetRender(host, 3);
        Check(host.Game.SourceTick <= paused + 2 && sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean(),
            "Focus freeze/discard and a truly released raw sample are required before resumed held controls can participate.");

        VerifyCabinetScreensAndRematch(data);
        VerifyCarrierlessCabinetCaller(data);
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions = assertions - before,
            contract = "source-gameflow/v1 + portable-free-play-new-player-session/v1",
            boundary = "Actual PlayerHost/public raw input and rendered original-atlas packets; declared terminal/clock/statistics fixtures are edge tests, not natural full-match proof." }));
    }

    private static void VerifyCabinetScreensAndRematch(PlayerData data)
    {
        var host = CabinetHost(data, out var sink);
        CabinetStart(host);
        FlowWakes(host, 2);
        object model = FlowModel(host.Game);
        void Counters(string field, int home, int away)
        {
            var values = (int[])FlowGet(model, field); values[0] = home; values[1] = away;
        }
        Counters("SourceInterceptions", 2, 5);
        Counters("SourcePassCompletions", 3, 7);
        Counters("SourcePassingYards", 42, 84);
        Counters("SourceSacks", 1, 2);
        Counters("SourcePassAttempts", 13, 29);
        Counters("SourceFumbles", 7, 11);
        Counters("SourceLostFumbles", 2, 3);
        Counters("SourceTackles", 0, 5);
        FlowSet(model, "SourceTimeOfPossession", new SourceTimeOfPossessionView(67, 123));
        FlowEnd(host, 3, 4, 17, clock: 0);
        CabinetRender(host);
        Check(CabinetText(sink, "cabinet/title") == "PLAY RESULT" &&
            sink.State.GetProperty("hud").GetProperty("elements").EnumerateArray().Any(e => e.GetString() == "cabinet/result/plate"),
            "A real committed C2 result must compose the original result plate, not a generic phase label.");
        long tick = host.Game.SourceTick;
        int elapsed = CabinetFlow(host).ElapsedSourceWakes, random = ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count;
        for (int redraw = 0; redraw < 12; redraw++) CabinetRender(host);
        Check(host.Game.SourceTick == tick && CabinetFlow(host).ElapsedSourceWakes == elapsed &&
            ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count == random,
            "Repeated actual render packets must not advance waits, inputs, RNG or statistics.");
        CabinetToStage(host, SourceCabinetFlowStage.EndOfQuarter);
        Check(CabinetText(sink, "cabinet/winner/team") == data.Hud.TeamAbbreviations[1],
            "The actual EOQ winner is Away from scores4:17, not the zero MatchEnded event value.");
        CabinetToStage(host, SourceCabinetFlowStage.FinalStats);
        Check(CabinetText(sink, "cabinet/title") == "FINAL STATISTICS" &&
            CabinetText(sink, "cabinet/stats/home/score") == "4" && CabinetText(sink, "cabinet/stats/away/score") == "17" &&
            CabinetText(sink, "cabinet/stats/outcome") == data.Hud.TeamAbbreviations[1] + " WINS!" &&
            CabinetText(sink, "cabinet/stats/interceptions/home") == "2" && CabinetText(sink, "cabinet/stats/interceptions/away") == "5" &&
            CabinetText(sink, "cabinet/stats/passing/home") == "42/14" && CabinetText(sink, "cabinet/stats/passing/away") == "84/12" &&
            CabinetText(sink, "cabinet/stats/field-goals/home") == "0/0" &&
            CabinetText(sink, "cabinet/stats/quarter/0/home") == "0" && CabinetText(sink, "cabinet/stats/quarter/3/away") == "17" &&
            CabinetText(sink, "cabinet/stats/quarter/4/away") == "--",
            "Actual final HUD glyph packets must be side-correct, preserve producer values/averages, completed quarter zeroes, and unavailable versus zero.");
        Check(CabinetText(sink, "cabinet/stats/passes/home") == "13/3" &&
            CabinetText(sink, "cabinet/stats/passes/away") == "29/7" &&
            CabinetText(sink, "cabinet/stats/fumbles/home") == "7/2" &&
            CabinetText(sink, "cabinet/stats/fumbles/away") == "11/3" &&
            CabinetText(sink, "cabinet/stats/turnovers/home") == "5/2" &&
            CabinetText(sink, "cabinet/stats/turnovers/away") == "2/3" &&
            CabinetText(sink, "cabinet/stats/tackles-sacks/home") == "0/1" &&
            CabinetText(sink, "cabinet/stats/tackles-sacks/away") == "5/3",
            "Available activity producers must render asymmetric attempts/completions, fumbles/lost, opposite interceptions/own lost fumbles, and human-only tackles independently of source sacks.");
        var composer = new SourceHudComposer(data.Hud);
        var glyphs = composer.Compose(host.HudProcess.CommittedState, new SourceHudViewport(512, 384, 512, 384, 1), null!, false, false);
        Check(glyphs.Where(p => p.Element == "cabinet/stats/interceptions/home").Single().SourceImageSymbol ==
            data.Hud.Font("bast8t").Glyph('2').Id &&
            glyphs.Where(p => p.Element == "cabinet/stats/interceptions/away").Single().SourceImageSymbol ==
            data.Hud.Font("bast8t").Glyph('5').Id &&
            sink.HudTextures.All(t => t.StartsWith("hud/") && sink.Textures.ContainsKey(t)),
            "The side-correct count must select the actual original atlas glyph, not only be reported in metadata.");
        host.Advance(0, 360, 270, 360, 270, 1);
        SourceCabinetMatchRecord final = host.Session.LastResult;
        Check(final.Statistics.HomeScore == 4 && final.Statistics.AwayScore == 17 && host.Session.MatchesCompleted == 1,
            "Session records retain the real immutable final statistics exactly once.");
        SourceMatchView finalMatch = final.Statistics.Match;
        var legacyMatch = new SourceMatchView(finalMatch.TouchdownStats, finalMatch.FourthDownAttempts,
            finalMatch.ResultProgressionStats, finalMatch.PlaceKickStats, finalMatch.PlayEndStats,
            finalMatch.CompletedPassStats, finalMatch.PassCompletions, finalMatch.Interceptions,
            finalMatch.TimeOfPossession, finalMatch.Configuration, finalMatch.PlayKind, finalMatch.Phase,
            finalMatch.Period, finalMatch.Possession, finalMatch.QuarterScores, finalMatch.Stations, finalMatch.Result);
        var legacyProjection = new SourceCabinetStatistics(legacyMatch, 4, 17, 3);
        Check(legacyProjection.Rows.Single(row => row.Key == "passes").Home == "--/3" &&
            legacyProjection.Rows.Single(row => row.Key == "passes").Away == "--/7" &&
            legacyProjection.Rows.Single(row => row.Key == "fumbles").Home == "--/--" &&
            legacyProjection.Rows.Single(row => row.Key == "fumbles").Away == "--/--" &&
            legacyProjection.Rows.Single(row => row.Key == "turnovers").Home == "5/--" &&
            legacyProjection.Rows.Single(row => row.Key == "turnovers").Away == "2/--" &&
            legacyProjection.Rows.Single(row => row.Key == "tackles-sacks").Home == "--/1" &&
            legacyProjection.Rows.Single(row => row.Key == "tackles-sacks").Away == "--/3",
            "Old-constructor absent activity groups must stay unavailable while independently present completions, opposite interceptions and sacks still display.");
        CabinetNextOptions(host);
        long epoch = host.Epoch;
        GameSimulation game = host.Game;
        CabinetRaw(host, new[] { 1, 0, 0, 0 });
        CabinetRender(host, 8);
        Check(host.Epoch == epoch && host.Session.Stage == SourceCabinetMenuStage.NextMatch,
            "Held-through terminal controls cannot accidentally select a rematch.");
        CabinetTap(host, 1, 0);
        CabinetRender(host);
        CabinetRender(host);
        Check(ReferenceEquals(game, host.Game) && host.Epoch == epoch + 1 && host.Game.SourceTick == 0 &&
            host.Game.State.HomeScore == 0 && host.Game.State.AwayScore == 0 && host.SourceWakes == 0 &&
            host.Camera.CueCount == 0 && host.HudProcess.PendingResultWakes == 0 &&
            sink.State.GetProperty("events").GetArrayLength() == 0 &&
            sink.State.GetProperty("input").GetProperty("pending").GetInt32() == 0 &&
            sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean() &&
            ReferenceEquals(final, host.Session.LastResult) && final.Statistics.AwayScore == 17 && host.Session.MatchesCompleted == 1,
            "A deliberate public rematch must execute the existing once-only reset and retain only deliberate choices/final records.");
        var freshProjection = new SourceCabinetStatistics(((ISourceMatchState)host.Game.State).Match, 0, 0, 0);
        Check(new[] { "passes", "fumbles", "turnovers", "tackles-sacks" }.All(key =>
                freshProjection.Rows.Single(row => row.Key == key).Home == "0/0" &&
                freshProjection.Rows.Single(row => row.Key == key).Away == "0/0") &&
            final.Statistics.Match.PlayActivityStats.HomePassAttempts == 13 &&
            final.Statistics.Match.PlayActivityStats.AwayFumbles == 11 &&
            final.Statistics.Match.PlayActivityStats.HomeTackles == 0 &&
            final.Statistics.Rows.Single(row => row.Key == "passes").Home == "13/3" &&
            final.Statistics.Rows.Single(row => row.Key == "turnovers").Home == "5/2",
            "Fresh actual groups display observed zeroes after rematch, while the retained immutable final projection keeps its old activity values.");
        CabinetRaw(host);
        FlowEnd(host, 3, 21, 9, clock: 0);
        CabinetNextOptions(host);
        CabinetTap(host, 2, 0);
        Check(host.Session.Stage == SourceCabinetMenuStage.Joining, "B deliberately returns to public participation/team choices, not a reload.");
        CabinetTap(host, 1, 2);
        CabinetTap(host, 4, 0);
        CabinetMove(host, 0, 1, 0);
        CabinetTap(host, 1, 0, 2);
        CabinetTap(host, 1, 0, 2);
        CabinetTap(host, 1, 0);
        Check(!ReferenceEquals(game, host.Game) && host.MatchConfiguration.HomeTeam.TeamIndex == 1 &&
            host.MatchConfiguration.Stations.Select(s => s.StationId).SequenceEqual(new[] { 0, 2 }) &&
            host.Epoch == epoch + 2 && host.Game.SourceTick == 0 &&
            host.Session.MatchesStarted == 3 && host.Session.MatchesCompleted == 2 &&
            final.Statistics.HomeScore == 4 && final.Statistics.AwayScore == 17,
            "Changed teams/humans require a fresh actual simulation and epoch, not mutable authenticated data or reuse of old source queues.");

        var half = CabinetHost(data, out var halfSink);
        CabinetStart(half);
        FlowEnd(half, 1, 3, 7, clock: 0);
        CabinetToStage(half, SourceCabinetFlowStage.EndOfQuarter);
        Check(halfSink.State.GetProperty("hud").GetProperty("elements").EnumerateArray().Any(e => e.GetString() == "cabinet/quarter/end"),
            "The second-quarter announcement must use admitted original msg_end/quarter imagery.");
        CabinetToStage(half, SourceCabinetFlowStage.HalftimeStats);
        Check(CabinetText(halfSink, "cabinet/title") == "HALFTIME STATISTICS" &&
            CabinetText(halfSink, "cabinet/stats/quarter/1/away") == "7" &&
            CabinetText(halfSink, "cabinet/stats/quarter/2/away") == "--" && half.Session.MatchesCompleted == 0,
            "Halftime must show actual completed quarter stats without recording a final match or inventing future quarter zeroes.");
        CabinetToStage(half, SourceCabinetFlowStage.ContinuationOffer, held: 0);
        Check(CabinetText(halfSink, "cabinet/title") == "CONTINUE GAME" &&
            CabinetText(halfSink, "cabinet/continuation/policy") == "FREE PLAY - SAME HUMAN STATIONS",
            "The actual source continuation offer needs its own rendered free-play screen, not an unvisited DTO.");
        CabinetToStage(half, SourceCabinetFlowStage.Inactive);
        Check(((ISourceMatchState)half.Game.State).Match.Period.QuarterIndex == 2 &&
            half.Game.State.Phase == GamePhase.PreSnap,
            "The rendered halftime offer must resume the actual third-quarter kickoff once.");
        var coach = CabinetHost(data, out var coachSink);
        CabinetStart(coach);
        FlowEnd(coach, 0, 0, 0, clock: 0);
        CabinetToStage(coach, SourceCabinetFlowStage.CoachingTip);
        SourceCabinetFlowView flow = CabinetFlow(coach);
        Check(CabinetText(coachSink, "cabinet/coaching/title") == SourceCabinetCoachingText.Lines(flow.CoachingTipOrdinal!.Value)[0],
            "The coaching screen must render the already-drawn source ordinal's actual hint text.");
        random = ((SourceInjectedRandom)coach.Game.SourceRandomService).Trace.Count;
        for (int redraw = 0; redraw < 8; redraw++) CabinetRender(coach);
        Check(((SourceInjectedRandom)coach.Game.SourceRandomService).Trace.Count == random &&
            CabinetFlow(coach).CoachingTipOrdinal == flow.CoachingTipOrdinal,
            "Coaching redraw cannot reselect either original random ordinal.");
        var tie = CabinetHost(data, out var tieSink);
        CabinetStart(tie);
        FlowEnd(tie, 6, 0, 0, clock: 0);
        CabinetToStage(tie, SourceCabinetFlowStage.FinalStats);
        Check(CabinetText(tieSink, "cabinet/stats/outcome") == "TIE GAME!" &&
            CabinetText(tieSink, "cabinet/stats/quarter/6/home") == "0" &&
            CabinetText(tieSink, "cabinet/stats/quarter/6/away") == "0",
            "The declared tied third-OT edge must render a tie and all seven actual period columns, not award Home from an event value.");
        SourceMatchView actual = tie.HudProcess.CommittedHud.Statistics.Match;
        var absent = new SourceMatchView(actual.Configuration, actual.PlayKind, actual.Phase, actual.Period,
            actual.Possession, actual.QuarterScores, actual.Stations, actual.Result);
        var unavailable = new SourceCabinetStatistics(absent, 0, 0, 6);
        Check(unavailable.Rows.Single(row => row.Key == "possession").Home == "--" &&
            unavailable.Rows.Single(row => row.Key == "passes").Away == "--/--" &&
            unavailable.Rows.Single(row => row.Key == "field-goals").Home == "--/--",
            "An actually omitted optional statistical producer must remain unavailable, never be projected as a fabricated zero.");
    }

    private static void VerifyNaturalCabinetMatch(PlayerData data)
    {
        var host = CabinetHost(data, out var sink);
        CabinetStart(host);
        var stages = new HashSet<string>();
        var repairs = new HashSet<string>();
        bool observedUnfilteredWaitChord = false;
        host.Bridge.AfterHostStep += state => {
            stages.Add(((ISourceCabinetFlowState)state).CabinetFlow.Stage.ToString());
            var match = ((ISourceMatchState)state).Match;
            foreach (SourceCompatibilityRepair repair in match.CompatibilityRepairs)
            {
                repairs.Add(repair.Kind.ToString());
                if (repair.Kind != SourceCompatibilityRepairKind.CarrierlessDeadBallLateHit ||
                    repair.SourceTick != host.Game.SourceTick) continue;
                var input = (PlayerCommand)FlowGet(FlowModel(host.Game), "SourceCurrentInput");
                observedUnfilteredWaitChord |= input.StationInputs != null &&
                    input.StationInputs.Stations.Any(s => s.StationId == 0 &&
                        s.Held == (SourceButtons.A | SourceButtons.B | SourceButtons.C));
            }
        };
        int publicCaptures = 0;
        long lastPlay = -1;
        int pulse = 0;
        void DrivePublicPolicy()
        {
            SourceMatchView match = ((ISourceMatchState)host.Game.State).Match;
            var buttons = new int[4];
            if (host.Game.State.Phase == GamePhase.PlaySelect)
                buttons[0] = pulse++ % 2 == 0 ? 2 : 0;
            else if (host.Game.State.Phase == GamePhase.PreSnap && match.PlayKind != SourcePlayKind.Kickoff)
                buttons[0] = pulse++ % 2 == 0 ? 1 : 0;
            else if (CabinetFlow(host).Stage == SourceCabinetFlowStage.ResultWait ||
                CabinetFlow(host).Stage == SourceCabinetFlowStage.EndOfQuarter)
                buttons[0] = 7;
            CabinetRaw(host, buttons); publicCaptures++;
            CabinetRender(host, 8);
        }
        try
        {
            for (int frame = 0; frame < 45000 && host.Session.MatchesCompleted == 0; frame++)
            {
                DrivePublicPolicy();
                if (lastPlay != host.Game.State.PlayNumber)
                {
                    lastPlay = host.Game.State.PlayNumber;
                    Console.WriteLine("cabinet natural: " + JsonSerializer.Serialize(new {
                        hostTick = host.Game.State.Tick, sourceTick = host.Game.SourceTick, play = lastPlay,
                        quarter = ((ISourceMatchState)host.Game.State).Match.Period.QuarterIndex,
                        home = host.Game.State.HomeScore, away = host.Game.State.AwayScore
                    }));
                }
            }
            Check(host.Session.MatchesCompleted == 1 && host.Session.LastResult != null &&
                stages.Contains("EndOfQuarter") && stages.Contains("HalftimeStats"),
                "Natural public-host inputs must complete regulation/intermissions and retain real final team statistics.");
            Check(repairs.Contains(nameof(SourceCompatibilityRepairKind.CarrierlessDeadBallLateHit)) &&
                observedUnfilteredWaitChord,
                "The unchanged formerly-failing natural policy must keep its actual raw ABC chord and report the bound portable undefined-target repair.");
            SourceCabinetMatchRecord result = host.Session.LastResult ??
                throw new InvalidOperationException("The natural host did not retain its actual final statistics.");
            Check(CabinetText(sink, "cabinet/title") == "FINAL STATISTICS" &&
                CabinetText(sink, "cabinet/stats/home/score") == result.Statistics.HomeScore.ToString() &&
                CabinetText(sink, "cabinet/stats/away/score") == result.Statistics.AwayScore.ToString() &&
                result.Statistics.Match.QuarterScores.Sum(score => score.HomeScore) == result.Statistics.HomeScore &&
                result.Statistics.Match.QuarterScores.Sum(score => score.AwayScore) == result.Statistics.AwayScore,
                "The natural actual final packet must render real immutable scores and matching quarter totals, not merely emit MatchEnded.");
            CabinetNextOptions(host);
            CabinetTap(host, 1, 0);
            CabinetRaw(host);
            long secondNormalSnap = -1;
            for (int frame = 0; frame < 2000 && (secondNormalSnap < 0 || host.Game.SourceTick < secondNormalSnap + 30); frame++)
            {
                DrivePublicPolicy();
                if (secondNormalSnap < 0 && host.Game.State.PlayNumber >= 2 && host.Game.State.Phase == GamePhase.LiveBall &&
                    ((ISourceMatchState)host.Game.State).Match.PlayKind == SourcePlayKind.Normal)
                    secondNormalSnap = host.Game.SourceTick;
            }
            Check(host.Session.MatchesStarted == 2 && secondNormalSnap >= 0 &&
                host.Game.SourceTick >= secondNormalSnap + 30 && host.Game.State.Phase == GamePhase.LiveBall &&
                ReferenceEquals(result, host.Session.LastResult),
                "The same natural public policy must finish the second kickoff, select/snap a normal play and continue live gameplay without reload/private setters.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", natural = true, publicCaptures, stages,
                repairs, observedUnfilteredWaitChord,
                finalHome = result.Statistics.HomeScore, finalAway = result.Statistics.AwayScore,
                completedQuarter = result.Statistics.CompletedQuarterIndex, finalSourceTick = result.FinalSourceTick,
                secondSourceTick = host.Game.SourceTick, secondNormalSnap,
                secondPlay = host.Game.State.PlayNumber, secondPhase = host.Game.State.Phase.ToString(),
                matchesStarted = host.Session.MatchesStarted,
                boundary = "Actual PlayerHost/raw cabinet controls only; no clock/score/seed/gameplay-state mutation." }));
        }
        catch
        {
            Console.Error.WriteLine("cabinet natural fault: " + JsonSerializer.Serialize(new {
                hostTick = host.Game.State.Tick, sourceTick = host.Game.SourceTick,
                phase = host.Game.State.Phase.ToString(), play = host.Game.State.PlayNumber,
                quarter = ((ISourceMatchState)host.Game.State).Match.Period.QuarterIndex,
                publicCaptures, stages
            }));
            throw;
        }
    }

    // Declared caller-boundary fixtures. The separate natural policy above exercises
    // the same caller through public Capture -> source wakes without state mutation.
    private static void VerifyCarrierlessCabinetCaller(PlayerData data)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Member(object instance, string name) =>
            instance.GetType().GetProperty(name, flags)?.GetValue(instance) ??
            instance.GetType().GetField(name, flags)!.GetValue(instance)!;
        void Mode(object actor, string layer, uint value)
        {
            object state = Member(Member(actor, "AnimationState"), layer);
            state.GetType().GetMethod("SetMovementMode", flags)!.Invoke(state, new object[] { value });
        }
        object Prepare(PlayerHost host, int buttons, GamePhase phase = GamePhase.DeadBall,
            int carrier = -1, bool runningPush = false, bool controlDisabled = false)
        {
            object model = FlowModel(host.Game);
            object actor = ((Array)FlowGet(model, "Players")).GetValue(0)!;
            FlowSet(model, "Phase", phase);
            FlowSet(model, "SourceMatchPhase", phase == GamePhase.LiveBall ? SourceMatchPhase.LiveBall : SourceMatchPhase.PlayResult);
            FlowSet(model, "SourceResultView", new SourceMatchResult(SourcePlayKind.Normal, SourceResultKind.Down, SourceResultStatus.Classified));
            FlowSet(model, "SourceRules", new SourceRulesState(1, 1, 2, 50, 80, SourcePlayType.Normal));
            FlowSet(model, "Possession", TeamId.Away);
            FlowSet(model, "SourceOffensePlay", data.Playbook.GetPlay("play_zigzag"));
            FlowSet(model, "SourceDefensePlay", data.Playbook.GetPlay(data.Hud.PlaySlots["def_slots"][0].PlaySymbol));
            FlowSet(model, "SourceFlags", SourceGameFlags.None);
            FlowSet(model, "CarrierId", carrier);
            FlowSet(model, "BallKind", phase == GamePhase.LiveBall ? SourceBallKind.Fumble : SourceBallKind.Pass);
            FlowSet(model, "SourceBallTarget", new SourceVector3(SourceMatchRules.WorldPosition(75, 1), 90, 90));
            FlowSet(actor, "SourceMode", SourceActorMode.DefensiveBack);
            FlowSet(actor, "SourceControllerStation", 0);
            FlowSet(actor, "SourceRawFlags", 0);
            FlowSet(actor, "SourceStick", 24);
            FlowSet(actor, "SourcePreviousStick", 24);
            FlowSet(actor, "SourceButtons", (SourceDroneButtons)buttons);
            FlowSet(actor, "SourceOldButtons", SourceDroneButtons.None);
            FlowSet(actor, "SourceNewButtons", (SourceDroneButtons)buttons);
            FlowSet(actor, "NoCatchTicks", 100);
            Mode(actor, "Primary", controlDisabled ? (uint)SourceMovementModes.Uninterruptible : 0u);
            Mode(actor, "Secondary", runningPush ? (uint)SourceMovementModes.RunningPush : 0u);
            return actor;
        }
        void Invoke(PlayerHost host, object actor)
        {
            typeof(GameSimulation).GetMethod("ExecuteSourceMovementMode", flags)!
                .Invoke(host.Game, new[] { actor, (object)default(PlayerCommand) });
            FlowMethod<Action>(host.Game, "Publish")();
        }
        PlayerHost Bound()
        {
            PlayerHost host = CabinetHost(data, out _);
            CabinetStart(host);
            CabinetRaw(host);
            CabinetRender(host, 3);
            return host;
        }
        SourceCompatibilityRepair[] NewRepairs(PlayerHost host) =>
            ((ISourceMatchState)host.Game.State).Match.CompatibilityRepairs.Where(r =>
                r.Kind == SourceCompatibilityRepairKind.CarrierlessDeadBallLateHit).ToArray();
        var abc = Bound();
        object actorAbc = Prepare(abc, 7);
        int beforeAbc = ((SourceInjectedRandom)abc.Game.SourceRandomService).Trace.Count;
        Invoke(abc, actorAbc);
        var deferred = NewRepairs(abc);
        Check(deferred.Length == 1 && deferred[0].SubjectId == 0 && deferred[0].RawIndex == -1 &&
            deferred[0].EffectiveIndex == -1 && deferred[0].RetainedStick == (int)FlowGet(actorAbc, "SourceStick") &&
            ((ISourceMatchState)abc.Game.State).Match.CompatibilityRepairs.Any(r =>
                r.Kind == SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch),
            "The actual bound caller must defer only undefined A, report retained steering, and STILL execute the independent B/C carrierless switch stage.");
        Check((int)(SourceDroneButtons)FlowGet(actorAbc, "SourceButtons") == 7 &&
            (int)(SourceDroneButtons)FlowGet(actorAbc, "SourceNewButtons") == 7,
            "The portable late-hit boundary cannot filter the actual held/new ABC history.");
        var bc = Bound();
        object actorBc = Prepare(bc, 6);
        int beforeBc = ((SourceInjectedRandom)bc.Game.SourceRandomService).Trace.Count;
        Invoke(bc, actorBc);
        Check(NewRepairs(bc).Length == 0 &&
            ((SourceInjectedRandom)abc.Game.SourceRandomService).Trace.Count - beforeAbc ==
                ((SourceInjectedRandom)bc.Game.SourceRandomService).Trace.Count - beforeBc &&
            abc.Game.State.Players[0].Animation.Primary.ScriptSymbol == bc.Game.State.Players[0].Animation.Primary.ScriptSymbol &&
            ((FieldVector)FlowGet(actorAbc, "SourceVelocity")).Equals((FieldVector)FlowGet(actorBc, "SourceVelocity")),
            "Deferring A adds no animation/steering/target/RNG choice compared with the same actual standard-movement and independent B/C caller.");
        foreach (bool strict in new[] { false, true })
        {
            var host = new PlayerHost(data, new RecordingSink(), options: PreviewOptions.FromUrl(
                strict ? "?source-compatibility=strict" : "", data.Catalog.Profile.StableId));
            CabinetRender(host, 3);
            object actor = Prepare(host, 7);
            bool rejected = false;
            try { Invoke(host, actor); }
            catch (TargetInvocationException error)
            { rejected = error.InnerException is InvalidOperationException && error.InnerException.Message.Contains("player_blocks[-1]"); }
            Check(rejected && ((ISourceCabinetFlowState)host.Game.State).CabinetFlow == null &&
                NewRepairs(host).Length == 0,
                "Strict and unbound Portable retain the original named undefined-target guard; the cabinet repair is not a default policy.");
        }
        foreach (var gate in new[] {
            (Phase: GamePhase.LiveBall, Carrier: -1, Push: false, Disabled: false),
            (Phase: GamePhase.DeadBall, Carrier: 7, Push: false, Disabled: false),
            (Phase: GamePhase.DeadBall, Carrier: -1, Push: true, Disabled: false),
            (Phase: GamePhase.DeadBall, Carrier: -1, Push: false, Disabled: true) })
        {
            object actor = Prepare(bc, 1, gate.Phase, gate.Carrier, gate.Push, gate.Disabled);
            Invoke(bc, actor);
            Check(NewRepairs(bc).Length == 0,
                "Live loose-ball, carrier-present, RunningPush and control-disabled A calls must not inherit the new late-hit repair.");
        }
    }
}
