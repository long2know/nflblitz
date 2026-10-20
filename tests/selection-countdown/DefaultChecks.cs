using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class DefaultCabinetChecks
{
    private static string Digest(object value)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))))
                .Replace("-", "").ToLowerInvariant();
    }

    private static int Main(string[] args)
    {
        try
        {
            FullKernelInputs inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
            var impact = SourceCinematicLibrary.Read(File.ReadAllBytes(args[3]), SourceTeamResourceProfile.C2M1ImpactSha256);
            var normal = SourceNormalCameraData.Read(File.ReadAllBytes(args[4]));
            var speech = SourcePossessionSoundData.Read(File.ReadAllBytes(args[5]));
            var data = SourceInterfaceData.Read(File.ReadAllBytes(args[6]), SourceInterfacePackagePlan.HudSha256);
            var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation,
                new SourceTeamSelection(4, Enumerable.Range(0, 7).ToArray()),
                new SourceTeamSelection(8, Enumerable.Range(0, 7).ToArray()),
                new[] { new SourceStationConfiguration(0, TeamId.Home, 3), new SourceStationConfiguration(1, TeamId.Away, 10) },
                TeamId.Away, SourceCaptainSlot.First);
            GameSimulation game = inputs.Create(configuration: configuration,
                presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                compatibility: SourceGameplayCompatibility.Portable, camera: false);
            game.BindSourcePossessionSoundData(speech);
            var selector = new SourceCabinetPlaySelection(data);
            game.SourceStationInputTransform = selector.ApplyAtSourceTick;
            var camera = new SourceCinematicPlayback(impact, normal, ((ISourceCameraState)game.State).CameraBoundary);
            game.SourceCameraHeadingProvider = (state, tick) => {
                camera.ObserveCameraProcessSuspension(false);
                camera.CameraSourceTick(((ISourceCameraState)state).CameraBoundary, 512, 384,
                    game.DrawSourceCameraRandom, game.SourceCameraProcesses);
                game.ApplySourceCameraFeedback(camera.CameraFeedback);
                if (camera.CameraFeedback.CameraCompleted)
                {
                    SourceCinematicTransform output = camera.RequireSourceCameraAtTick(tick);
                    game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(output.Position, output.Transform, 512, 384), tick);
                }
                else camera.ObserveCameraProcessSuspension(true);
                return camera.SourceHeading1024;
            };
            var buffer = new SourceCabinetInputBuffer(configuration);
            var quantizer = new SourceCabinetQuantizer(data.Input);
            var bridge = new FixedStepBridge(game);
            bridge.AfterHostStep += state => camera.ObserveCameraProcessSuspension(
                ((ISourcePossessionState)state).Processes.Single(p => p.Function == "cambot_proc").Suspended);
            var composer = new SourceHudComposer(data);
            var menuEvents = new List<object>();
            var selections = new List<SourceGameEvent>();
            selector.EventRaised += e => menuEvents.Add(new {
                e.SourceTick, e.StationId, e.Side, e.Kind, e.SoundBank, e.SoundSymbol, e.SoundCall });
            bridge.SourceEventRaised += e => { if (e.Type == SourceEventType.PlaySelected) selections.Add(e); };
            long menuEntry = -1;
            string hudDigest = null;
            bool live = false;
            using (var writer = new StreamWriter(Path.Combine(args[7], "default-host.jsonl"), false, new UTF8Encoding(false)))
                for (int host = 0; host < 1200; host++)
                {
                    IGameState state = game.State;
                    bool menu = state.Phase == GamePhase.PlaySelect;
                    if (!menu) menuEntry = -1;
                    else if (menuEntry < 0) menuEntry = game.SourceTick;
                    long age = menuEntry < 0 ? -1 : game.SourceTick + 1 - menuEntry;
                    SourceButtons home = menu ? age == 22 ? SourceButtons.A :
                        age == 24 ? SourceButtons.C : age == 32 ? SourceButtons.B : SourceButtons.None : SourceButtons.None;
                    SourceButtons away = menu && age == 32 ? SourceButtons.B : SourceButtons.None;
                    SourceCommand command = state.Phase == GamePhase.DeadBall && !game.C2State.ResultPending
                        ? new SourceCommand(SourceAction.Continue) : default(SourceCommand);
                    buffer.Capture(new[] {
                        new SourceCabinetSample(0, quantizer.SevenLevel(menu && age == 26 ? 3 : 0,
                            menu && age == 28 ? 3 : 0), home, command),
                        new SourceCabinetSample(1, quantizer.SevenLevel(0, 0), away) });
                    bridge.AdvanceSource(FixedStepBridge.StepSeconds, buffer.NextHostFrame);
                    state = game.State;
                    live |= state.Phase == GamePhase.LiveBall;
                    if (!game.C2State.ResultPending)
                        hudDigest = Digest(composer.Compose(state, new SourceHudViewport(1024, 768, 1024, 768, 1),
                            selector, false, false));
                    SourceMatchView match = ((ISourceMatchState)state).Match;
                    writer.WriteLine(JsonSerializer.Serialize(new {
                        state.Tick, game.SourceTick, state.Phase, state.PlayNumber, state.Possession, state.Down,
                        state.LineOfScrimmage, state.FirstDownLine, state.HomeScore, state.AwayScore,
                        state.Ball, state.Players, clock = ((ISourceGameState)state).PackedGameTime,
                        matchPhase = match.Phase, match.HomePlay, match.AwayPlay, match.OffenseFlipped, match.SelectionTimer,
                        processes = ((ISourcePossessionState)state).Processes,
                        records = game.SourcePossessionProcessRecords, game.SourceEvents, hudDigest,
                        homePage = selector.Page(TeamId.Home), homeSlot = selector.Cursor(TeamId.Home),
                        awayPage = selector.Page(TeamId.Away), awaySlot = selector.Cursor(TeamId.Away)
                    }));
                }
            var random = (SourceInjectedRandom)game.SourceRandomService;
            if (!live || selections.Count < 2 || random.Trace.Any(d =>
                d.Site.SourceIdentity.StartsWith("playsel.c:play_select_cntrl:", StringComparison.Ordinal)))
                throw new InvalidOperationException("Default compatibility requires actual kickoff, manual source selections/live play and no random gesture draws.");
            File.WriteAllText(Path.Combine(args[7], "default-rng.json"), JsonSerializer.Serialize(random.Trace));
            File.WriteAllText(Path.Combine(args[7], "default-menu.json"), JsonSerializer.Serialize(menuEvents));
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", hosts = 1200, game.SourceTick,
                rawCalls = random.Trace.Count, selections = selections.Count, defaultSeed = 1,
                rawVectorFiltered = false, originalTargetRandomQualified = false }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
