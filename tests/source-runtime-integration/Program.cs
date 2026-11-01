using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal sealed class RuntimeAssertionFailure : Exception
{
    internal RuntimeAssertionFailure(string name) : base(name) { }
}

internal static class Program
{
    private const int Seed = 37;
    private const string RosterBoundary =
        "Source57 handicapping requires the existing strict SourceTeamRosterLibrary "
        + "with original teaminfo.drone_base and diff_settings; no scalar Difficulty fallback is used.";
    private const string MovementBoundary =
        "Original source M1 requires actual runtime-safe SourceMovementRuntimeData "
        + "and explicit field/powerup SourceMovementSettings. No tuned legacy movement fallback is used.";
    private const string SoundBoundary =
        "Bind the exact original SourceAudioFunctionData before the first source wake; no muted or post-host fallback exists.";
    private static readonly List<object> Fields = new List<object>();
    private static readonly List<object> Cases = new List<object>();
    private static int constructorAttempts;
    private static int constructedSourceInstances;
    private static int steps;

    private static void Equal<T>(string name, T expected, T actual)
    {
        bool equal = EqualityComparer<T>.Default.Equals(expected, actual);
        Fields.Add(new { name, expected, actual, status = equal ? "PASS" : "FAIL" });
        if (!equal) throw new RuntimeAssertionFailure(name);
    }

    private static void Reject<T>(string name, Action action, string message) where T : Exception
    {
        Exception observed = null;
        try { action(); }
        catch (Exception error) { observed = error; }
        Equal(name + ".exception_type", typeof(T).FullName,
            observed == null ? null : observed.GetType().FullName);
        Equal(name + ".message", message, observed.Message);
        Cases.Add(new { name, status = "PASS", scope = "public admission rejection, NOT admitted simulation execution" });
    }

    private static void Step(GameSimulation game, PlayerCommand command)
    {
        if (++steps > 20000) throw new RuntimeAssertionFailure("resource.maximum_simulation_steps");
        game.Step(command);
    }

    private static void SourceRuntime(Fixture fixture, SourceStationLayout layout, TeamId first)
    {
        string name = layout == SourceStationLayout.TwoStation ? "base-two-station" : "base-four-station";
        SourceMatchConfiguration configuration = fixture.Configuration(layout, first);
        GameSimulation game = null;
        constructorAttempts++;
        if (constructorAttempts > 20) throw new RuntimeAssertionFailure("resource.maximum_constructor_attempts");
        try { game = fixture.Create(configuration); }
        catch (InvalidOperationException error)
        {
            Equal(name + ".admission.exception_type", "System.InvalidOperationException", error.GetType().FullName);
            Equal(name + ".admission.message", RosterBoundary, error.Message);
            Cases.Add(new {
                name, status = "BLOCKED_PUBLIC_ADMISSION",
                attemptedProfile = "OriginalRelease57Hz", compatibility = "StrictSource",
                layout = layout.ToString(), firstPossession = first.ToString(),
                constructorReturned = false, sourceInstancePublished = false,
                boundary = "GameSimulation.InitializeSource -> PrepareSourceKickoff -> LineUpSourceActors -> UpdateSourceHandicaps",
                exception = error.ToString()
            });
            return;
        }

        constructedSourceInstances++;
        Equal(name + ".profile", SimulationProfile.OriginalRelease57Hz, game.Profile);
        Equal(name + ".compatibility", SourceGameplayCompatibility.StrictSource, game.Compatibility);
        Equal(name + ".default_audibles", false, game.SourceDefaultAudiblesEnabled);
        IGameState initial = game.State;
        ISourceMatchState source = initial as ISourceMatchState;
        Equal(name + ".initial.source_state_present", true, source != null);
        Equal(name + ".initial.match_present", true, source.Match != null);
        Equal(name + ".initial.configuration_identity", true, ReferenceEquals(configuration, source.Match.Configuration));
        Equal(name + ".initial.tick", 0L, initial.Tick);
        Equal(name + ".initial.source_tick", 0L, source.SourceTick);
        Equal(name + ".initial.source_hz", 57, source.SourceTicksPerSecond);
        Equal(name + ".initial.ledger_present", true, source.Match.TimeOfPossession != null);
        Equal(name + ".initial.home_seconds", 0, source.Match.TimeOfPossession.HomeSeconds);
        Equal(name + ".initial.away_seconds", 0, source.Match.TimeOfPossession.AwaySeconds);
        Equal(name + ".initial.clock_active", false, source.ClockActive);
        Equal(name + ".initial.packed_time", 0x00020000u, source.PackedGameTime);
        Equal(name + ".initial.players", 14, initial.Players.Count);
        Equal(name + ".initial.players_readonly", true, ((IList<PlayerView>)initial.Players).IsReadOnly);

        int station = configuration.Stations[0].StationId;
        var input = new PlayerCommand(stationInputs: new SourceInputFrame(new[] {
            new SourceStationInput(station, 0, 0, SourceButtons.A, SourceButtons.A)
        }));
        Step(game, input);
        ISourceMatchState skipped = (ISourceMatchState)game.State;
        Equal(name + ".skipped.snapshot_replaced", false, ReferenceEquals(initial, game.State));
        Equal(name + ".skipped.tick", 1L, game.State.Tick);
        Equal(name + ".skipped.source_tick", 0L, skipped.SourceTick);
        Equal(name + ".skipped.home_seconds", 0, skipped.Match.TimeOfPossession.HomeSeconds);
        Equal(name + ".skipped.away_seconds", 0, skipped.Match.TimeOfPossession.AwaySeconds);
        Equal(name + ".retained.initial_tick", 0L, initial.Tick);
        Equal(name + ".retained.initial_source_tick", 0L, source.SourceTick);
        Equal(name + ".retained.initial_home_seconds", 0, source.Match.TimeOfPossession.HomeSeconds);
        Equal(name + ".retained.initial_away_seconds", 0, source.Match.TimeOfPossession.AwaySeconds);

        game.Restart(Seed);
        ISourceMatchState reset = (ISourceMatchState)game.State;
        Equal(name + ".restart.tick", 0L, game.State.Tick);
        Equal(name + ".restart.source_tick", 0L, reset.SourceTick);
        Equal(name + ".restart.home_seconds", 0, reset.Match.TimeOfPossession.HomeSeconds);
        Equal(name + ".restart.away_seconds", 0, reset.Match.TimeOfPossession.AwaySeconds);
        Equal(name + ".restart.new_snapshot", false, ReferenceEquals(skipped, game.State));
        Equal(name + ".restart.new_ledger", false, ReferenceEquals(source.Match.TimeOfPossession, reset.Match.TimeOfPossession));
        Equal(name + ".restart.retained_skipped_tick", 1L, ((IGameState)skipped).Tick);
        Cases.Add(new { name, status = "PASS", scope = "source construction, skipped host Step, detached snapshots and zero-ledger Restart only" });

        Step(game, default(PlayerCommand));
        Reject<InvalidOperationException>(name + ".first-source-wake", () => Step(game, input), SoundBoundary);
        Cases.Add(new { name = name + ".live-clock-credit", status = "UNRUN", reason = SoundBoundary });
    }

    private static int Main(string[] args)
    {
        string status = "ERROR";
        string failure = null;
        Exception unexpected = null;
        Fixture fixture = null;
        int exit = 2;
        try
        {
            Equal("runner.no_arguments", 0, args.Length);
            fixture = new Fixture();
            SourceRuntime(fixture, SourceStationLayout.TwoStation, TeamId.Home);
            SourceRuntime(fixture, SourceStationLayout.FourStation, TeamId.Away);

            constructorAttempts++;
            Reject<InvalidOperationException>("animation-only-overload",
                () => new GameSimulation(fixture.Animations, Seed), MovementBoundary);
            constructorAttempts++;
            Reject<NotSupportedException>("portable-without-m1-bootstrap",
                () => fixture.Create(fixture.Configuration(SourceStationLayout.TwoStation, TeamId.Home),
                    SourceGameplayCompatibility.Portable),
                "Portable gameplay requires the explicit actual M1 source bootstrap.");
            constructorAttempts++;
            Reject<ArgumentException>("legacy-configuration-not-source-fallback",
                () => fixture.Create(fixture.Configuration(SourceStationLayout.TwoStation, TeamId.Home,
                    SourceSimulationProfile.LegacyBaseline60Hz)),
                "The source constructor requires the explicit 57 Hz profile. (Parameter 'configuration')");

            byte[] syntheticRoster = Encoding.UTF8.GetBytes(Fixture.RejectedRosterJson);
            Reject<SourceRosterException>("honest-synthetic-roster-provenance",
                () => SourceTeamRosterLibrary.Read(syntheticRoster, Fixture.Hash(syntheticRoster)),
                "Roster provenance must identify the known, content-verified original source pin.");
            status = constructedSourceInstances == 0 ? "BLOCKED_PUBLIC_ADMISSION" : "BLOCKED_FIRST_SOURCE_WAKE";
            exit = 3;
        }
        catch (RuntimeAssertionFailure error) { status = "FAILED"; failure = error.Message; exit = 1; }
        catch (Exception error) { unexpected = error; status = "ERROR"; exit = 2; }

        Console.WriteLine(JsonSerializer.Serialize(new {
            schema = "source-public-runtime-integration/v1", status,
            failed_assertion = failure, exception = unexpected == null ? null : unexpected.ToString(),
            expected_source_profile = "OriginalRelease57Hz", source_compatibility = "StrictSource",
            constructor_attempts = constructorAttempts, constructed_source_instances = constructedSourceInstances,
            simulation_steps = steps, fields = Fields, cases = Cases,
            fixture = fixture == null ? null : fixture.Observation(),
            unrun = new[] {
                "admitted-source-initial-zero-ledger", "source-snapshot-identity-and-immutability",
                "ordinary-source-Step-input-consumption", "source-Restart-reset",
                "active-clock-current-offense-credit", "credited-snapshot-retention",
                "result-captain-onside-runtime", "advanced-default-audibles", "native-whole-game"
            },
            controls = new[] {
                new { name = "model-ledger-initializer-nonzero", status = "UNRUN", reason = "No matching admitted clean source runtime scenario passed." },
                new { name = "snapshot-ledger-argument-dropped", status = "UNRUN", reason = "No matching admitted clean source runtime scenario passed." },
                new { name = "clock-consumer-credit-omitted", status = "UNRUN", reason = "No live clock consumer execution occurred." }
            },
            claims = new {
                admitted_source_execution = false, source_helpers_counted_as_runtime = false,
                non_source_fallback_counted = false, original_asset_authenticity = false,
                native_fidelity = false, whole_game_qualification = false, material_gate_performed = false
            }
        }));
        return exit;
    }
}

internal sealed class Fixture
{
    internal const string Origin = "explicitly-synthetic-public-DTO-fixture/v1";
    internal const string RejectedRosterJson =
        "{\"schema\":\"original-team-rosters/v1\",\"provenance\":{"
        + "\"repository\":\"synthetic/not-original\",\"commit\":\"synthetic-not-a-source-revision\","
        + "\"tree\":\"synthetic-not-a-source-tree\",\"manifest_sha256\":\"synthetic-not-an-original-manifest\","
        + "\"content_pin_verified\":false}}";
    internal readonly SyntheticAnimations Animations = new SyntheticAnimations();
    private readonly SyntheticRosters rosters = new SyntheticRosters();
    private readonly SourceCatchGeometry geometry;
    private readonly SourcePlaybook playbook;
    private readonly SourceMovementRuntimeData movement;
    private readonly SourceMovementSettings settings = new SourceMovementSettings(0, 0, false, 0, 0, false);

    internal Fixture()
    {
        geometry = new SourceCatchGeometry(Animations, new SourceVector3[15]);
        var sines = new float[257];
        for (int i = 0; i < sines.Length; i++) sines[i] = i / 256f;
        int[] directions = {
            128,96,52,0,972,928,896,160,128,76,0,948,896,864,204,180,128,0,896,844,820,
            256,256,256,-1,768,768,768,308,332,384,512,640,692,716,352,384,436,512,588,
            640,672,384,416,460,512,564,608,640
        };
        var math = new SourceDirectionMath(sines, directions, Origin, "port-source-49-way-direction-literals");
        var route = new SourceRouteProgram("synthetic_wait_for_snap", Origin, 1, new[] { 1, 0 });
        var routes = new SourceRouteProgram[7];
        var slots = new SourceFormationSlot[7];
        for (int i = 0; i < 7; i++)
        {
            routes[i] = route;
            slots[i] = new SourceFormationSlot(new SourcePlanar(-10f, (i - 3) * 12f),
                "stance_anim", i == 0 ? SourceActorMode.Kicker : SourceActorMode.ReceiverPreSnap,
                i == 0 ? SourceFormationControl.Captain : SourceFormationControl.None);
        }
        var formation = new SourceFormation("synthetic_kickoff_shape", Origin, 1, slots);
        var plays = new[] {
            new SourcePlayDefinition("play_kickoff_313", "Synthetic home kickoff", Origin, 1,
                SourcePlaySide.Kickoff, SourcePlayFlags.None, formation, routes),
            new SourcePlayDefinition("play_kickoff2_313", "Synthetic away kickoff", Origin, 1,
                SourcePlaySide.Kickoff, SourcePlayFlags.None, formation, routes),
            new SourcePlayDefinition("play_kick_return", "Synthetic return", Origin, 1,
                SourcePlaySide.KickReturn, SourcePlayFlags.None, formation, routes)
        };
        playbook = new SourcePlaybook(Origin, Origin, plays, new[] { route },
            new SourceWeightedPlayPool[0], math);
        var defense = new SourceMovementDefenseRow[12];
        for (int i = 0; i < defense.Length; i++)
            defense[i] = new SourceMovementDefenseRow(new float[6], new float[6], 0, 0, 0);
        movement = new SourceMovementRuntimeData(
            new SourceMovementData(new Dictionary<string, float>(), new Dictionary<string, IReadOnlyList<string>>()),
            new float[7], defense);
    }

    internal SourceMatchConfiguration Configuration(SourceStationLayout layout, TeamId first,
        SourceSimulationProfile profile = SourceSimulationProfile.OriginalRelease57Hz)
    {
        return new SourceMatchConfiguration(profile, layout,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home),
                new SourceStationConfiguration(layout == SourceStationLayout.TwoStation ? 1 : 2, TeamId.Away) },
            first, SourceCaptainSlot.First);
    }

    internal GameSimulation Create(SourceMatchConfiguration configuration,
        SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource)
    {
        return new GameSimulation(Animations, geometry, playbook, configuration,
            seed => new SourceInjectedRandom(site => 0, new SourceRandomProvenance(
                Origin, "constant-zero synthetic raw draws; NOT original RNG", "synthetic seed " + seed,
                SourceRandomEvidence.ExternalUnverified)),
            seed: 37, rosters: rosters,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            movementData: movement, movementSettings: settings, compatibility: compatibility);
    }

    internal object Observation()
    {
        return new {
            origin = Origin, original_provenance_claimed = false,
            roster_teams = rosters.Teams.Count, records_per_team = rosters.Teams[0].Players.Count,
            animation_requests = Animations.Requests,
            actual_geometry_type = "Blitz.Gameplay.SourceCatchGeometry",
            source_profile_substituted = false,
            movement = "Public DTOs satisfy dimensions; empty selector/constant maps are deliberately NOT a complete live M1 fixture.",
            rejected_roster_sha256 = Hash(Encoding.UTF8.GetBytes(RejectedRosterJson))
        };
    }

    internal static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
        {
            var text = new StringBuilder();
            foreach (byte value in hash.ComputeHash(bytes)) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}

internal sealed class SyntheticRosters : ISourceTeamRoster
{
    private readonly ReadOnlyCollection<SourceTeamRoster> teams;
    private readonly Dictionary<string, SourceRosterProfile> players = new Dictionary<string, SourceRosterProfile>();
    public string SourceRevision { get { return Fixture.Origin; } }
    public IReadOnlyList<SourceTeamRoster> Teams { get { return teams; } }

    internal SyntheticRosters()
    {
        var records = new SourceTeamRoster[31];
        for (int team = 0; team < records.Length; team++)
        {
            var roster = new SourceRosterProfile[16];
            for (int index = 0; index < roster.Length; index++)
            {
                string id = "synthetic_team_" + team + "_record_" + index;
                roster[index] = new SourceRosterProfile(new SourceRosterIdentity(team, index), id, index, 1f);
                players.Add(id, roster[index]);
            }
            records[team] = new SourceTeamRoster(team, "synthetic_roster_" + team, roster);
        }
        teams = Array.AsReadOnly(records);
    }

    public SourceTeamRoster GetTeam(int sourceTeamId) { return teams[sourceTeamId]; }
    public SourceRosterProfile GetPlayer(string sourcePlayerId) { return players[sourcePlayerId]; }
}

internal sealed class SyntheticAnimations : ISourceAnimationLibrary
{
    internal readonly List<string> Requests = new List<string>();
    public ISourceAnimationClip GetClip(string name)
    {
        Requests.Add(name);
        throw new InvalidOperationException("Synthetic boundary fixture has no original animation assets: " + name);
    }
}
