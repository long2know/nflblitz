using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public sealed class SourceCabinetMatchRecord
{
    public int MatchNumber { get; }
    public long FinalSourceTick { get; }
    public SourceCabinetStatistics Statistics { get; }
    internal SourceCabinetMatchRecord(int number, long tick, SourceCabinetFlowView flow)
    {
        MatchNumber = number; FinalSourceTick = tick;
        Statistics = new SourceCabinetStatistics(flow.FinalMatch, flow.FinalHomeScore, flow.FinalAwayScore,
            flow.CompletedQuarterIndex ?? flow.FinalMatch.Period.QuarterIndex);
    }
}

public sealed class SourceCabinetSession : ISourceInstructionAdmissionState, ISourcePreGameMenuDriver
{
    public const string Policy = "portable-free-play-new-player-session/v1";
    private readonly PlayerData data;
    private readonly SourceMatchConfiguration defaults;
    private readonly int[] teams = new int[2];
    private readonly int[][] records = { new int[7], new int[7] };
    private readonly int[] slots = new int[2], editingSide = { 0, 0, 1, 1 };
    private readonly int[] previousButtons = new int[4], previousDirections = new int[4];
    private readonly bool[] ready = new bool[2];
    private readonly List<SourceCabinetMatchRecord> results = new();
    private SourceMatchView recordedFinal;
    private SourceMatchConfiguration pending;
    private SourceWorldUserFogConfiguration pendingWorldFog;
    private readonly int[] gamesPlayed = new int[4];
    private int instructionFlag = 2;
    private int showPlaySelectionInstruction;
    private int selectedSetting, difficulty, timeAdjustment;
    private TeamId firstPossession;
    private SourceCaptainSlot captain;
    private SourceWorldUserFogSelection userFogSelection;
    private Action<SourcePhysicalSwitchFrame> sourceMenuCapture;
    private bool sourceMenuActive;
    public bool UsingSourcePreGameInput => sourceMenuActive;
    public bool WorldSettingsEnabled { get; }
    public SourceCabinetRecordsRuntime Records { get; }
    public SourceWorldUserFogConfiguration WorldUserFog { get; private set; }
    public SourceCabinetMenuStage Stage { get; private set; } = SourceCabinetMenuStage.Joining;
    public int ParticipatingStationMask { get; private set; }
    public bool WaitingForNeutral { get; private set; } = true;
    public bool Playing => Stage == SourceCabinetMenuStage.Playing;
    public bool HasMatchRequest => pending != null;
    public bool RematchRequested { get; private set; }
    public int MatchesStarted { get; private set; }
    public int MatchesCompleted { get; private set; }
    public SourceCabinetContinuationPolicy Continuation { get; private set; } =
        SourceCabinetContinuationPolicy.KeepParticipatingStations;
    public SourceMatchConfiguration LastConfiguration { get; private set; }
    public SourceCabinetMatchRecord LastResult => results.LastOrDefault();
    public IReadOnlyList<SourceCabinetMatchRecord> Results => results.AsReadOnly();
    public IReadOnlyList<int> GamesPlayed => Array.AsReadOnly((int[])gamesPlayed.Clone());

    public int ReadPlayerStatus()
    {
        DataCatalog.Require(LastConfiguration != null,
            "Instruction participation belongs to an actually admitted match, not its menu backdrop.");
        return LastConfiguration.Stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId);
    }
    public int ReadGamesPlayed(int slot)
    {
        SourceCabinetQuantizer.ValidateStationId(slot);
        if (Records != null) return Records.ReadGamesPlayed(slot);
        return gamesPlayed[slot];
    }
    public int ReadIdiotBoxesShown() => instructionFlag;
    public int ReadPlaySelectionInstructionGate() => showPlaySelectionInstruction;
    public void WriteIdiotBoxesShown(int value)
    {
        DataCatalog.Require(value is >= -1 and <= 2, "Unknown source instruction admission flag.");
        instructionFlag = value;
        if (value == 0) showPlaySelectionInstruction = 2;
    }
    internal void WritePlaySelectionInstructionGate(int value)
    {
        DataCatalog.Require(value is 0 or 1 or 2 or 11 or 12,
            "Unknown source play-selection introductory gate.");
        showPlaySelectionInstruction = value;
    }

    public SourceCabinetSession(PlayerData data, bool worldSettingsEnabled = false, SourcePlayerRecordStore recordStore = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        WorldSettingsEnabled = worldSettingsEnabled;
        Records = recordStore == null ? null : new SourceCabinetRecordsRuntime(recordStore, data);
        defaults = data.Configuration.Match;
        data.ValidateCabinetConfiguration(defaults);
        teams[0] = defaults.HomeTeam.TeamIndex; teams[1] = defaults.AwayTeam.TeamIndex;
        defaults.HomeTeam.RosterIndices.CopyTo(records[0], 0);
        defaults.AwayTeam.RosterIndices.CopyTo(records[1], 0);
        ParticipatingStationMask = defaults.Stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId);
        firstPossession = defaults.FirstPossession; captain = defaults.Captain;
        difficulty = defaults.Difficulty; timeAdjustment = defaults.TimeAdjustment;
    }

    public SourceMatchConfiguration CreateConfiguration()
    {
        var stations = Enumerable.Range(0, 4).Where(Joined).Select(id =>
            new SourceStationConfiguration(id, (TeamId)(id / 2))).ToArray();
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(teams[0], records[0]),
            new SourceTeamSelection(teams[1], records[1]), stations, firstPossession, captain,
            timeAdjustment, difficulty, true, defaults.HomePlay, defaults.AwayPlay);
        data.ValidateCabinetConfiguration(configuration);
        return configuration;
    }

    public void BindPreGameInput(Action<SourcePhysicalSwitchFrame> capturePhysical)
    {
        DataCatalog.Require(Records != null && !Playing && !HasMatchRequest,
            "SOURCE_PREGAME_DRIVER_BOUNDARY: bind the genuine menu input owner before an actual new match.");
        sourceMenuCapture = capturePhysical ?? throw new ArgumentNullException(nameof(capturePhysical));
        sourceMenuActive = true;
        Records.BindSourceMenu();
        ResetControls();
    }

    public SourcePreGameMenuRoute ReadRoute()
    {
        DataCatalog.Require(Records != null, "SOURCE_PREGAME_RECORD_DRIVER_REQUIRED");
        var screens = new SourcePreGameMenuScreen[4];
        if (HasMatchRequest)
            return new SourcePreGameMenuRoute(SourcePreGameMenuPhase.MatchRequested,
                ParticipatingStationMask, Leader(), screens);
        if (Records.Active) return Records.ReadSourceRoute();
        SourcePreGameMenuPhase phase = Stage switch
        {
            SourceCabinetMenuStage.Joining => SourcePreGameMenuPhase.Joining,
            SourceCabinetMenuStage.Teams => SourcePreGameMenuPhase.Teams,
            SourceCabinetMenuStage.Rosters => SourcePreGameMenuPhase.Rosters,
            SourceCabinetMenuStage.Versus => SourcePreGameMenuPhase.Versus,
            _ => SourcePreGameMenuPhase.PortableManagement
        };
        return new SourcePreGameMenuRoute(phase, ParticipatingStationMask, Leader(), screens);
    }

    public void OnSourceMenuWake(SourcePreGameMenuWake wake)
    {
        ArgumentNullException.ThrowIfNull(wake);
        DataCatalog.Require(sourceMenuActive && !Playing,
            "SOURCE_PREGAME_DRIVER_CLOCK: UI execution requires the actual active menu service.");
        Records.WithSourceMenuWake(wake, () =>
        {
            if (WaitingForNeutral)
            {
                if (wake.Scan.PhysicalSwitches.IsNeutral) WaitingForNeutral = false;
                return;
            }
            var pressed = new int[4];
            var moved = new int[4];
            for (int station = 0; station < 4; station++)
            {
                if (wake.StationId >= 0 && wake.Route.Phase != SourcePreGameMenuPhase.Options && station != wake.StationId) continue;
                pressed[station] = (int)wake.ClosedButtons(station);
                moved[station] = (int)wake.ClosedDirections(station);
            }
            ApplyMenuEdges(pressed, moved);
        });
    }

    public void Capture(double[] axes, int[] buttons, SourceCabinetQuantizer quantizer)
    {
        if (Playing && Records?.Active != true || HasMatchRequest && !sourceMenuActive) return;
        var directions = new int[4];
        var physical = sourceMenuActive && !Playing ? new SourceStationInput[4] : null;
        bool neutral = true;
        for (int station = 0; station < 4; station++)
        {
            var stick = quantizer.Analog((float)axes[station * 2], (float)axes[station * 2 + 1]);
            directions[station] = stick.DigitalDirections;
            if (physical != null)
                physical[station] = new SourceStationInput(station, stick.StickX, stick.StickZ, (SourceButtons)buttons[station],
                    digitalCurrent: (SourceDigitalDirections)stick.DigitalDirections);
            neutral &= directions[station] == 0 && buttons[station] == 0;
        }
        if (physical != null) { sourceMenuCapture(new SourcePhysicalSwitchFrame(physical)); return; }
        if (WaitingForNeutral)
        {
            if (neutral) { WaitingForNeutral = false; Array.Clear(previousButtons); Array.Clear(previousDirections); }
            return;
        }
        var pressed = new int[4];
        var moved = new int[4];
        for (int station = 0; station < 4; station++)
        {
            pressed[station] = buttons[station] & ~previousButtons[station];
            moved[station] = directions[station] == previousDirections[station] ? 0 : directions[station];
            previousButtons[station] = buttons[station]; previousDirections[station] = directions[station];
        }
        ApplyMenuEdges(pressed, moved);
    }

    private void ApplyMenuEdges(int[] pressed, int[] moved)
    {
        SourceCabinetMenuStage entry = Stage;
        if (Records?.Active == true)
        {
            Records.Capture(pressed, moved);
            if (Stage == SourceCabinetMenuStage.Records && Records.BackRequested) ShowJoining();
            else if (!Playing && Records.SelectionComplete) EnterChoices(SourceCabinetMenuStage.Teams);
            return;
        }
        if (entry == SourceCabinetMenuStage.Joining)
        {
            // Apply all joins/leaves in the SAME raw sample before a stage decision.
            for (int station = 0; station < 4; station++)
                if (pressed[station] == 1) ParticipatingStationMask |= 1 << station;
                else if (pressed[station] == 2) ParticipatingStationMask &= ~(1 << station);
            if (ParticipatingStationMask != 0 && Enumerable.Range(0, 4).Any(id => Joined(id) && pressed[id] == 4))
                EnterRecordChoices();
            return;
        }
        for (int station = 0; station < 4 && Stage == entry && !HasMatchRequest; station++)
        {
            if (!Joined(station)) continue;
            int button = pressed[station], direction = moved[station];
            if (entry == SourceCabinetMenuStage.NextMatch)
            {
                if (button == 1) RequestMatch(true);
                else if (button == 2) ShowJoining();
                continue;
            }
            if (entry == SourceCabinetMenuStage.Versus)
            {
                if (station != Leader()) continue;
                if (button == 2) { EnterChoices(SourceCabinetMenuStage.Rosters); continue; }
                if (button == 1) { RequestMatch(false); continue; }
                int vertical = Vertical(direction), horizontal = Horizontal(direction);
                if (vertical != 0) selectedSetting = Wrap(selectedSetting + vertical, WorldSettingsEnabled ? 6 : 5);
                if (horizontal != 0) ChangeSetting(horizontal);
                continue;
            }
            int ownSide = station / 2;
            if (station != Captain(ownSide)) continue;
            if (button == 2)
            {
                if (entry == SourceCabinetMenuStage.Teams) ShowJoining();
                else EnterChoices(SourceCabinetMenuStage.Teams);
                continue;
            }
            if (button == 4 && Captain(1 - ownSide) < 0)
                editingSide[station] = 1 - editingSide[station];
            int side = editingSide[station];
            int dx = Horizontal(direction), dy = Vertical(direction);
            if (entry == SourceCabinetMenuStage.Teams && (dx != 0 || dy != 0))
            {
                teams[side] = Wrap(teams[side] + dx + dy * 5, SourceMatchLimits.TeamCount);
                for (int slot = 0; slot < 7; slot++) records[side][slot] = slot;
                ready[side] = false;
            }
            else if (entry == SourceCabinetMenuStage.Rosters)
            {
                if (dy != 0) slots[side] = Wrap(slots[side] + dy, 7);
                if (dx != 0) { records[side][slots[side]] = Wrap(records[side][slots[side]] + dx, 16); ready[side] = false; }
            }
            if (button == 1) ready[side] = true;
        }
        if (Stage == entry && (entry == SourceCabinetMenuStage.Teams || entry == SourceCabinetMenuStage.Rosters) &&
            ready.All(value => value))
            EnterChoices(entry == SourceCabinetMenuStage.Teams ? SourceCabinetMenuStage.Rosters : SourceCabinetMenuStage.Versus);
    }

    public void Continue(int station)
    {
        SourceCabinetQuantizer.ValidateStationId(station);
        if (sourceMenuActive) return;
        if (WaitingForNeutral || HasMatchRequest) return;
        if (Records?.Active == true)
        {
            Records.Continue(station);
            if (!Playing && Records.SelectionComplete)
                EnterChoices(SourceCabinetMenuStage.Teams);
            return;
        }
        if (Playing) return;
        if (Stage == SourceCabinetMenuStage.Joining)
        {
            if (!Joined(station)) { ParticipatingStationMask |= 1 << station; ResetControls(); }
            else EnterRecordChoices();
        }
        else if (Joined(station) && Stage == SourceCabinetMenuStage.NextMatch)
            RequestMatch(true);
        else if (Joined(station) && Stage == SourceCabinetMenuStage.Versus && station == Leader())
            RequestMatch(false);
    }

    private void RequestMatch(bool rematch)
    {
        if (Records != null && !Records.CanStartMatch)
        { Records.ShowPostMatch(); ResetControls(); return; }
        pending = rematch ? LastConfiguration : CreateConfiguration();
        pendingWorldFog = rematch ? WorldUserFog : WorldSettingsEnabled ?
            SourceWorldUserFogConfiguration.ApplyVersus(userFogSelection) : null;
        RematchRequested = rematch;
    }

    internal SourceMatchConfiguration TakeMatchRequest()
    {
        SourceMatchConfiguration configuration = pending ?? throw new InvalidOperationException("No deliberate cabinet match request.");
        pending = null; LastConfiguration = configuration; recordedFinal = null;
        sourceMenuActive = false;
        WorldUserFog = pendingWorldFog; pendingWorldFog = null;
        MatchesStarted++; Stage = SourceCabinetMenuStage.Playing; ResetControls();
        return configuration;
    }

    internal bool Observe(IGameState state)
    {
        var flow = (state as ISourceCabinetFlowState)?.CabinetFlow;
        if (!Playing || flow == null) return false;
        if (flow.FinalMatch != null && !ReferenceEquals(recordedFinal, flow.FinalMatch))
        {
            DataCatalog.Require(recordedFinal == null, "A session cannot record two different final observations for one match.");
            recordedFinal = flow.FinalMatch; MatchesCompleted++;
            Records?.ObserveFinal(flow);
            foreach (SourceStationConfiguration station in LastConfiguration.Stations)
                gamesPlayed[station.StationId] = checked(gamesPlayed[station.StationId] + 1);
            results.Add(new SourceCabinetMatchRecord(MatchesStarted, ((ISourceGameState)state).SourceTick, flow));
            if (results.Count > 16) results.RemoveAt(0);
        }
        if (!flow.NewMatchRequested) return false;
        if (Records?.BlocksGameplay == true) return false;
        DataCatalog.Require(LastResult != null, "A new-match request needs its actual retained final match statistics.");
        Stage = SourceCabinetMenuStage.NextMatch; ResetControls();
        Records?.ShowPostMatch();
        return true;
    }

    public void ResetControls()
    {
        WaitingForNeutral = true; Array.Clear(previousButtons); Array.Clear(previousDirections);
    }

    internal void AdministrativeRestart()
    {
        recordedFinal = null;
        if (!Playing) ShowJoining();
        ResetControls();
    }

    private void ShowJoining()
    {
        pending = null; pendingWorldFog = null; RematchRequested = false; Stage = SourceCabinetMenuStage.Joining;
        ResetControls();
    }

    private void EnterChoices(SourceCabinetMenuStage stage)
    {
        Stage = stage;
        for (int side = 0; side < 2; side++) ready[side] = Captain(side) < 0;
        for (int station = 0; station < 4; station++) editingSide[station] = station / 2;
        ResetControls();
    }

    private void EnterRecordChoices()
    {
        if (Records == null) { EnterChoices(SourceCabinetMenuStage.Teams); return; }
        Records.BeginSelection(ParticipatingStationMask);
        Stage = SourceCabinetMenuStage.Records;
        ResetControls();
    }

    private bool Joined(int id) => (ParticipatingStationMask & 1 << id) != 0;
    private int Leader() => Enumerable.Range(0, 4).FirstOrDefault(Joined, -1);
    private int Captain(int side)
    {
        int first = side * 2, second = first + 1;
        if (!Joined(first)) return Joined(second) ? second : -1;
        return captain == SourceCaptainSlot.Second && Joined(second) ? second : first;
    }
    private static int Wrap(int value, int count) => (value % count + count) % count;
    private static int Horizontal(int direction) => ((direction & 8) != 0 ? 1 : 0) - ((direction & 4) != 0 ? 1 : 0);
    private static int Vertical(int direction) => ((direction & 2) != 0 ? 1 : 0) - ((direction & 1) != 0 ? 1 : 0);

    private void ChangeSetting(int direction)
    {
        switch (selectedSetting)
        {
            case 0: firstPossession = firstPossession == TeamId.Home ? TeamId.Away : TeamId.Home; break;
            case 1: captain = captain == SourceCaptainSlot.First ? SourceCaptainSlot.Second : SourceCaptainSlot.First; break;
            case 2: difficulty = Wrap(difficulty + direction, 11); break;
            case 3: timeAdjustment = Wrap(timeAdjustment + direction, 5); break;
            case 4: Continuation = Continuation == SourceCabinetContinuationPolicy.KeepParticipatingStations ?
                SourceCabinetContinuationPolicy.RequireConfirmation : SourceCabinetContinuationPolicy.KeepParticipatingStations; break;
            case 5:
                DataCatalog.Require(WorldSettingsEnabled, "SOURCE_WORLD_SETTING: user fog requires source-world=portable.");
                userFogSelection = (SourceWorldUserFogSelection)Wrap((int)userFogSelection + direction, 5); break;
        }
    }

    public SourceCabinetMenuView View()
    {
        SourceCabinetMenuSide Side(int side)
        {
            SourceRosterTeam team = data.Rosters.GetTeamData(teams[side]);
            string[] roster = records[side].Select((index, slot) => {
                SourceRosterPlayer player = team.Players[index];
                return (slot + 1) + "  #" + player.JerseyNumber + " " +
                    player.Profile.FirstName.ToUpperInvariant() + " " + player.Profile.LastName.ToUpperInvariant() +
                    "  R" + (index + 1);
            }).ToArray();
            int leader = Captain(side);
            string stations = string.Join(" + ", Enumerable.Range(side * 2, 2).Where(Joined).Select(id => "P" + (id + 1)));
            return new SourceCabinetMenuSide(team.Name.ToUpperInvariant(), team.Home.ToUpperInvariant(),
                stations.Length == 0 ? "CPU" : stations + "  CAPTAIN P" + (leader + 1),
                roster, slots[side], ready[side],
                Enumerable.Range(0, 4).Any(id => Joined(id) && id == Captain(id / 2) && editingSide[id] == side));
        }
        var settings = new List<string> { "FIRST POSSESSION: " + firstPossession.ToString().ToUpperInvariant(),
                "COOPERATIVE CAPTAIN: " + captain.ToString().ToUpperInvariant(),
                "SOURCE DIFFICULTY: " + difficulty, "SOURCE TIME ADJUSTMENT: " + timeAdjustment,
                "QUARTER CONTINUE: " + (Continuation == SourceCabinetContinuationPolicy.KeepParticipatingStations ? "FREE PLAY" : "CONFIRM") };
        if (WorldSettingsEnabled) settings.Add("SOURCE USER FOG: " + userFogSelection.ToString().ToUpperInvariant());
        return new SourceCabinetMenuView(Stage, ParticipatingStationMask, WaitingForNeutral, Side(0), Side(1), settings.ToArray(),
            selectedSetting, data.Catalog.Profile.UsesDefaultAudibles, LastResult?.Statistics);
    }

    public object Describe() => new {
        policy = Records == null ? Policy : "source-local-cabinet-records/v1",
        stage = Stage.ToString(), participatingStationMask = ParticipatingStationMask,
        waitingForNeutral = WaitingForNeutral, matchesStarted = MatchesStarted, matchesCompleted = MatchesCompleted,
        homeTeam = teams[0], awayTeam = teams[1], homeRoster = records[0].ToArray(), awayRoster = records[1].ToArray(),
        stations = Enumerable.Range(0, 4).Where(Joined).ToArray(),
        continuationPolicy = Continuation.ToString(),
        savedRecords = Records == null ?
            "explicit known-new stations; completed admitted matches count only in this in-memory session; no native saves/custom plays/coin/audit hardware" :
            "explicit original six-name/four-PIN local record selections; awaited portable atomic career/custom/audible commits; no native CMOS import",
        localRecords = Records?.Describe(),
        gamesPlayed = Records == null ? GamesPlayed : null,
        venue = "approved field0/stadium0/BEA only; team choice does not select alternate home-field art",
        defaultAudibles = data.Catalog.Profile.UsesDefaultAudibles ? "admitted source-default-audibles/v1" : "unavailable in this composition",
        records = results.Select(result => new {
            result.MatchNumber, result.FinalSourceTick, result.Statistics.HomeScore, result.Statistics.AwayScore,
            homeTeam = result.Statistics.Match.Configuration.HomeTeam.TeamIndex,
            awayTeam = result.Statistics.Match.Configuration.AwayTeam.TeamIndex,
            winner = result.Statistics.Winner?.ToString() ?? "Tie"
        }).ToArray()
    };
}
