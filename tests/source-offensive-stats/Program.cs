using System;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class Program
{
    private sealed class NamedAssertion : Exception
    {
        internal readonly string Field;
        internal NamedAssertion(string field) { Field = field; }
    }

    private sealed class Scenario
    {
        internal readonly string Name;
        internal readonly int Credit;
        internal readonly SourcePlayResult Result;
        internal readonly int End;
        internal readonly bool Changes;
        internal Scenario(string name, int credit, SourcePlayResult result, int end = 50, bool changes = false)
        { Name = name; Credit = credit; Result = result; End = end; Changes = changes; }
    }

    private static readonly Scenario[] Scenarios =
    {
        new Scenario("completion.gain", 1, SourcePlayResult.Down),
        new Scenario("completion.loss", 1, SourcePlayResult.Down),
        new Scenario("completion.zero-gain", 1, SourcePlayResult.Down),
        new Scenario("completion.before-next-possession", 1, SourcePlayResult.Down, 50, true),
        new Scenario("completion.touchdown", 1, SourcePlayResult.Touchdown, 100),
        new Scenario("completion.conversion", 1, SourcePlayResult.Conversion, 100),
        new Scenario("guard.pass-thrown-is-not-forward", 0, SourcePlayResult.Down),
        new Scenario("guard.no-forward-flag", 0, SourcePlayResult.Down),
        new Scenario("guard.incomplete", 0, SourcePlayResult.Incomplete),
        new Scenario("guard.possession-change", 0, SourcePlayResult.NewPossession),
        new Scenario("guard.established-side-not-initial", 0, SourcePlayResult.NewPossession, 50, true),
        new Scenario("guard.dead-ball-independent", 0, SourcePlayResult.Down),
        new Scenario("guard.quick-return-new-possession", 0, SourcePlayResult.NewPossession),
        new Scenario("guard.quick-return-touchdown", 0, SourcePlayResult.Touchdown, 100),
        new Scenario("completion.quick-bit-not-blanket-veto", 1, SourcePlayResult.Down),
        new Scenario("completion.kick-bits-not-blanket-veto", 1, SourcePlayResult.Down)
    };

    private static readonly List<object> Fields = new List<object>();
    private static readonly List<string> Cases = new List<string>();
    private static string currentCase;
    private static string selectedCase;

    private const string SyntheticClipKey = "SYNTHETIC_COMPLETION_SNAPSHOT_FRAME";
    private const string SyntheticProgramKey = "synthetic_completion_snapshot_frame_only";
    private static int syntheticActors, syntheticLayers, syntheticClipRequests, syntheticFrameRequests, syntheticRandomRequests;

    private sealed class SyntheticSnapshotClip : ISourceAnimationClip
    {
        private readonly IReadOnlyList<int> selected = Array.AsReadOnly(new[] { 0 });
        private readonly SourceAnimationFrame frame;
        public string SequenceName { get { return SyntheticClipKey; } }
        public string SourceRevision { get { return "SYNTHETIC_NON_ORIGINAL_COMPONENT_VALUES"; } }
        public string SourcePath { get { return "synthetic-component-values/not-a-WAN"; } }
        public string SourceGitBlobSha1 { get { return null; } }
        public string SourceSha256 { get { return null; } }
        public string DecodedSha256 { get { return null; } }
        public uint SourceFlags { get { return 0; } }
        public int SourceFrameCount { get { return 1; } }
        public IReadOnlyList<int> SelectedSourceFrames { get { return selected; } }

        internal SyntheticSnapshotClip()
        {
            var rotations = new SourceRotation[15];
            for (int joint = 0; joint < rotations.Length; joint++)
                rotations[joint] = new SourceRotation(0f, 0f, 0f, 1f);
            frame = new SourceAnimationFrame(0, new SourceVector3(0f, 0f, 0f),
                new SourceVector3(0f, 0f, 0f), new SourceJointPose(rotations));
        }

        public SourceAnimationFrame GetFrame(int sourceFrame)
        {
            syntheticFrameRequests++;
            if (sourceFrame != 0)
                throw new KeyNotFoundException("Synthetic completion snapshot fixture declares only frame0.");
            return frame;
        }
    }

    private sealed class SyntheticSnapshotValues : ISourceAnimationLibrary
    {
        private readonly ISourceAnimationClip clip = new SyntheticSnapshotClip();
        public ISourceAnimationClip GetClip(string sequenceName)
        {
            syntheticClipRequests++;
            if (sequenceName != SyntheticClipKey)
                throw new KeyNotFoundException("Undeclared synthetic completion snapshot clip: " + sequenceName);
            return clip;
        }
    }

    private static int UnexpectedFixtureRandom(int bound)
    {
        syntheticRandomRequests++;
        throw new InvalidOperationException("Frame-only synthetic snapshot initialization must not request RNG.");
    }

    internal static object FixtureDiagnostics()
    {
        return new
        {
            origin = "SYNTHETIC/NON-ORIGINAL; no original animation or source gameplay ticks",
            clipKey = SyntheticClipKey, programKey = SyntheticProgramKey,
            sourceFrames = 1, selectedFrame = 0, joints = 15,
            actorsInitialized = syntheticActors, layersInitialized = syntheticLayers,
            initializationStepsPerLayer = 1, program = "NewSequence,NextFrame(1),End",
            clipRequests = syntheticClipRequests, frameRequests = syntheticFrameRequests,
            randomRequests = syntheticRandomRequests, originalProvenanceSupplied = false
        };
    }

    private static void Check(string suffix, int actual, int expected)
    {
        string field = currentCase + "." + suffix;
        Fields.Add(new { @case = currentCase, field, expected, actual });
        if (actual != expected) throw new NamedAssertion(field);
    }

    private static int Bit(bool value) { return value ? 1 : 0; }

    private static void Case(string name, Action action)
    {
        if (selectedCase != null && selectedCase != name) return;
        currentCase = name;
        action();
        Cases.Add(name);
    }

    private static SourcePlayEndInput Input(string name, int side)
    {
        int los = 30, down = 1, last = side;
        SourcePlayType play = SourcePlayType.Normal;
        SourcePlayEndCause cause = SourcePlayEndCause.CarrierTackledInBounds;
        SourceGameFlags flags = SourceGameFlags.ForwardPass | SourceGameFlags.PassThrown;
        switch (name)
        {
            case "completion.loss": los = 60; break;
            case "completion.zero-gain": los = 50; break;
            case "completion.before-next-possession": los = 60; down = 4; break;
            case "completion.touchdown": cause = SourcePlayEndCause.CarrierTouchdown; break;
            case "completion.conversion": play = SourcePlayType.ExtraPoint; los = 90;
                cause = SourcePlayEndCause.CarrierTouchdown; break;
            case "guard.pass-thrown-is-not-forward": flags = SourceGameFlags.PassThrown; break;
            case "guard.no-forward-flag": flags = SourceGameFlags.None; break;
            case "guard.incomplete": cause = SourcePlayEndCause.BallOutOfBounds; break;
            case "guard.possession-change": flags |= SourceGameFlags.PossessionChange; break;
            case "guard.established-side-not-initial": last = 1 - side; break;
            case "guard.dead-ball-independent": play = SourcePlayType.FieldGoal;
                cause = SourcePlayEndCause.DeadBallInBounds; break;
            case "guard.quick-return-new-possession": play = SourcePlayType.Kickoff;
                cause = SourcePlayEndCause.CarrierOutOfBounds; flags |= SourceGameFlags.QuickKick; break;
            case "guard.quick-return-touchdown": play = SourcePlayType.Kickoff;
                cause = SourcePlayEndCause.CarrierTouchdown; flags |= SourceGameFlags.QuickKick; break;
            case "completion.quick-bit-not-blanket-veto": flags |= SourceGameFlags.QuickKick; break;
            case "completion.kick-bits-not-blanket-veto":
                flags |= SourceGameFlags.BallKicked | SourceGameFlags.BallPunted; break;
        }
        return new SourcePlayEndInput(new SourceRulesState(side, side, down, los, 90, play),
            cause, SourceBallType.Pass, flags, last, 0f, previousResult: SourcePlayResult.Down);
    }

    private static void TableCase(Scenario scenario, int side)
    {
        var model = new SimulationState();
        model.SourcePassCompletions[0] = 2;
        model.SourcePassCompletions[1] = 5;
        model.SourceOffensivePlays[0] = 8;
        model.SourceOffensivePlays[1] = 13;
        model.SourceConsecutiveCompletions[0] = 1;
        model.SourceConsecutiveCompletions[1] = 4;
        model.SourceInterceptions[0] = 7;
        model.SourceInterceptions[1] = 9;
        model.SourceTimeOfPossession = new SourceTimeOfPossessionView(11, 17);
        model.SourceTeamFire[0] = true;
        model.SourceTeamFire[1] = false;
        SourcePlayEndInput input = Input(scenario.Name, side);
        SourcePlayResolution resolution = SourceMatchRules.Resolve(input);
        if (!resolution.TransitionApplied) throw new InvalidOperationException("Component input did not resolve.");
        model.AccumulateSourcePassCompletion(input, resolution, side == 0 ? 4 : 11);
        Check("counter.home", model.SourcePassCompletions[0], scenario.Credit == 1 && side == 0 ? 3 : 2);
        Check("counter.away", model.SourcePassCompletions[1], scenario.Credit == 1 && side == 1 ? 6 : 5);
        int owner = scenario.Name == "guard.established-side-not-initial" ? 1 - side : side;
        Check("classification.owner", resolution.Classification.OffenseSide, owner);
        Check("classification.result", (int)resolution.Classification.Result, (int)scenario.Result);
        Check("next-owner", resolution.State.OffenseSide, scenario.Changes ? 1 - side : side);
        Check("field-end", resolution.FieldEnd, scenario.End);
        Check("preserve.offensive-plays.home", model.SourceOffensivePlays[0], 8);
        Check("preserve.offensive-plays.away", model.SourceOffensivePlays[1], 13);
        Check("preserve.consecutive.home", model.SourceConsecutiveCompletions[0], 1);
        Check("preserve.consecutive.away", model.SourceConsecutiveCompletions[1], 4);
        Check("preserve.interceptions.home", model.SourceInterceptions[0], 7);
        Check("preserve.interceptions.away", model.SourceInterceptions[1], 9);
        Check("preserve.time.home", model.SourceTimeOfPossession.HomeSeconds, 11);
        Check("preserve.time.away", model.SourceTimeOfPossession.AwaySeconds, 17);
        Check("preserve.fire.home", Bit(model.SourceTeamFire[0]), 1);
        Check("preserve.fire.away", Bit(model.SourceTeamFire[1]), 0);
    }

    internal static SourceMatchConfiguration Configuration()
    {
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceStationConfiguration[0], TeamId.Home, SourceCaptainSlot.First);
    }

    internal static SimulationState ComponentState()
    {
        var model = new SimulationState
        {
            SourceMode = true,
            SourceConfiguration = Configuration(),
            SourceRules = new SourceRulesState(0, 0, 1, 30, 90),
            SourcePeriodView = new SourcePeriodView(0),
            SourceProcesses = new SourcePortableProcessHost(),
            SourceMatchPhase = SourceMatchPhase.PlayResult,
            Phase = GamePhase.DeadBall
        };
        var values = new SyntheticSnapshotValues();
        var programs = new System.Collections.ObjectModel.ReadOnlyDictionary<string, SourceAnimationProgram>(
            new Dictionary<string, SourceAnimationProgram>
            {
                { SyntheticProgramKey, new SourceAnimationProgram(SyntheticProgramKey,
                    new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: SyntheticClipKey),
                    new SourceAnimationInstruction(SourceAnimationOp.NextFrame, a: 1),
                    new SourceAnimationInstruction(SourceAnimationOp.End)) }
            });
        foreach (ActorState actor in model.Players)
        {
            actor.AnimationState = new SourceAnimationState(values, actor.Id, programs);
            actor.AnimationState.Primary.Change(SyntheticProgramKey, actor, 0, UnexpectedFixtureRandom);
            actor.AnimationState.Secondary.Change(SyntheticProgramKey, actor, 0, UnexpectedFixtureRandom);
            if (!actor.AnimationState.Primary.IsSpecified || !actor.AnimationState.Secondary.IsSpecified
                || actor.AnimationState.Primary.FrameOrdinal != 0 || actor.AnimationState.Secondary.FrameOrdinal != 0
                || actor.AnimationState.Primary.ConsumeSignals() != SourceAnimationSignal.None
                || actor.AnimationState.Secondary.ConsumeSignals() != SourceAnimationSignal.None)
                throw new InvalidOperationException("Synthetic snapshot actor must have both specified frame-only layers.");
            syntheticActors++;
            syntheticLayers += 2;
        }
        return model;
    }

    private static void Accumulate(SimulationState model, string scenario, int side)
    {
        SourcePlayEndInput input = Input(scenario, side);
        model.AccumulateSourcePassCompletion(input, SourceMatchRules.Resolve(input), side == 0 ? 4 : 11);
    }

    private static void Lifecycle()
    {
        SimulationState model = ComponentState();
        Check("fresh.home", model.SourcePassCompletions[0], 0);
        Check("fresh.away", model.SourcePassCompletions[1], 0);
        GameSnapshot s0 = new GameSnapshot(model);
        Check("snapshot-s0.home", s0.Match.PassCompletions.HomeCompletions, 0);
        Check("snapshot-s0.away", s0.Match.PassCompletions.AwayCompletions, 0);
        Accumulate(model, "completion.gain", 0);
        Check("first-home-credit", model.SourcePassCompletions[0], 1);
        Check("first-away-zero", model.SourcePassCompletions[1], 0);
        GameSnapshot s1 = new GameSnapshot(model);
        Check("snapshot-s1.home", s1.Match.PassCompletions.HomeCompletions, 1);
        Check("snapshot-s1.away", s1.Match.PassCompletions.AwayCompletions, 0);
        Accumulate(model, "completion.loss", 0);
        Check("second-home-credit", model.SourcePassCompletions[0], 2);
        Check("second-away-zero", model.SourcePassCompletions[1], 0);
        Check("retained-s0.home", s0.Match.PassCompletions.HomeCompletions, 0);
        Check("retained-s0.away", s0.Match.PassCompletions.AwayCompletions, 0);
        Check("retained-s1.home", s1.Match.PassCompletions.HomeCompletions, 1);
        Check("retained-s1.away", s1.Match.PassCompletions.AwayCompletions, 0);
        Accumulate(model, "completion.touchdown", 1);
        Check("away-credit.home", model.SourcePassCompletions[0], 2);
        Check("away-credit.away", model.SourcePassCompletions[1], 1);
        GameSnapshot s2 = new GameSnapshot(model);
        Check("snapshot-s2.home", s2.Match.PassCompletions.HomeCompletions, 2);
        Check("snapshot-s2.away", s2.Match.PassCompletions.AwayCompletions, 1);
        model.Phase = GamePhase.PlaySelect;
        new GameSnapshot(model);
        model.Phase = GamePhase.DeadBall;
        new GameSnapshot(model);
        Check("capture-not-producer.home", model.SourcePassCompletions[0], 2);
        Check("capture-not-producer.away", model.SourcePassCompletions[1], 1);
        SimulationState fresh = new SimulationState();
        Check("fresh-state-reset.home", fresh.SourcePassCompletions[0], 0);
        Check("fresh-state-reset.away", fresh.SourcePassCompletions[1], 0);
        Check("old-state-retained.home", model.SourcePassCompletions[0], 2);
        Check("old-state-retained.away", model.SourcePassCompletions[1], 1);
        Check("retained-after-reset.home", s1.Match.PassCompletions.HomeCompletions, 1);
        Check("retained-after-reset.away", s1.Match.PassCompletions.AwayCompletions, 0);
    }

    private static void LegacyNeutral()
    {
        var model = new SimulationState();
        Check("match-null", Bit(new GameSnapshot(model).Match == null), 1);
        Check("counter.home", model.SourcePassCompletions[0], 0);
        Check("counter.away", model.SourcePassCompletions[1], 0);
    }

    private static void AdditiveApi()
    {
        var view = new SourcePassCompletionsView(2, 5);
        Check("dto.home", view.HomeCompletions, 2);
        Check("dto.away", view.AwayCompletions, 5);
        bool negativeHome = false, negativeAway = false, nullView = false;
        try { new SourcePassCompletionsView(-1, 0); }
        catch (ArgumentOutOfRangeException) { negativeHome = true; }
        try { new SourcePassCompletionsView(0, -1); }
        catch (ArgumentOutOfRangeException) { negativeAway = true; }
        Check("negative-home-rejected", Bit(negativeHome), 1);
        Check("negative-away-rejected", Bit(negativeAway), 1);
        SourceMatchConfiguration configuration = Configuration();
        var period = new SourcePeriodView(0);
        var possession = new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Home,
            TeamId.Home, SourceCaptainSlot.First);
        var scores = new[] { new SourceQuarterScore(0, 0, 0) };
        var stations = new SourceStationView[0];
        var time = new SourceTimeOfPossessionView(11, 17);
        var interceptions = new SourceInterceptionsView(7, 9);
        var oldBase = new SourceMatchView(configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
            period, possession, scores, stations);
        var oldTime = new SourceMatchView(time, configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
            period, possession, scores, stations);
        var oldInterceptions = new SourceMatchView(interceptions, time, configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PlayResult, period, possession, scores, stations);
        Check("old-base.unobserved", Bit(oldBase.PassCompletions == null), 1);
        Check("old-time.unobserved", Bit(oldTime.PassCompletions == null), 1);
        Check("old-interceptions.unobserved", Bit(oldInterceptions.PassCompletions == null), 1);
        var current = new SourceMatchView(view, interceptions, time, configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PlayResult, period, possession, scores, stations);
        Check("new-view-retained", Bit(ReferenceEquals(current.PassCompletions, view)), 1);
        Check("new-interceptions-retained", Bit(ReferenceEquals(current.Interceptions, interceptions)), 1);
        Check("new-time-retained", Bit(ReferenceEquals(current.TimeOfPossession, time)), 1);
        try
        {
            new SourceMatchView(null, interceptions, time, configuration, SourcePlayKind.Normal,
                SourceMatchPhase.PlayResult, period, possession, scores, stations);
        }
        catch (ArgumentNullException) { nullView = true; }
        Check("null-new-view-rejected", Bit(nullView), 1);
        Check("old-base.time-null", Bit(oldBase.TimeOfPossession == null), 1);
        Check("old-base.interceptions-null", Bit(oldBase.Interceptions == null), 1);
        Check("old-time.time-retained", Bit(ReferenceEquals(oldTime.TimeOfPossession, time)), 1);
        Check("old-time.interceptions-null", Bit(oldTime.Interceptions == null), 1);
        Check("old-interceptions.time-retained", Bit(ReferenceEquals(oldInterceptions.TimeOfPossession, time)), 1);
        Check("old-interceptions.interceptions-retained", Bit(ReferenceEquals(oldInterceptions.Interceptions, interceptions)), 1);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--case") selectedCase = args[1];
            else if (args.Length != 0) throw new ArgumentException("Only --case <declared-name> is supported.");
            foreach (Scenario scenario in Scenarios)
            {
                for (int side = 0; side < 2; side++)
                {
                    Scenario captured = scenario;
                    int team = side;
                    Case(scenario.Name + (side == 0 ? ".home" : ".away"), () => TableCase(captured, team));
                }
            }
            Case("completion.lifecycle", Lifecycle);
            Case("completion.legacy-neutral", LegacyNeutral);
            Case("completion.additive-api", AdditiveApi);
            if (Cases.Count != (selectedCase == null ? 35 : 1))
                throw new InvalidOperationException("Exact declared case selection required.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "passed", selectedCase, cases = Cases, assertions = Fields.Count, fields = Fields,
                fixture = FixtureDiagnostics()
            }));
            return 0;
        }
        catch (NamedAssertion failure)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "assertion-failed", selectedCase, failedAssertion = failure.Field,
                cases = Cases, assertions = Fields.Count, fields = Fields, fixture = FixtureDiagnostics()
            }));
            return 2;
        }
        catch (Exception failure)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "unexpected-error", selectedCase, errorType = failure.GetType().FullName,
                error = failure.Message, cases = Cases, assertions = Fields.Count, fields = Fields,
                fixture = FixtureDiagnostics()
            }));
            return 3;
        }
    }
}
