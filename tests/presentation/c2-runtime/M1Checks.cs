using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class Program
{
    private static void M1Registry()
    {
        SourceTeamResourceProfile profile = SourceTeamResourceProfile.Contact281C2M1;
        Equal(profile.StableId, "original-teams-contact281-c2-m1", "M1 successor explicit stable identity");
        Check(profile.UsesC2Data && !profile.IsContact281C2 && profile.IsContact281C2M1, "old-nine and M1 remain distinct profiles");
        Equal(profile.AnimationSha256, Animation, "M1 shares only identical canonical281 animation bytes");
        Equal(SourceTeamResourceProfile.C2M1ImpactSha256, "2fee43a7a20dad06d8846b1d558a69d8648041368091ab12677c941e19b66cd1", "M1 exact482 catalog pin");
        Equal(SourceInterfacePackagePlan.C2M1CallbackSha256, "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a", "M1 exact14 callback generation");
        Equal(SourceInterfacePackagePlan.C2M1CallbackResourceKey, "SourceData/source-c2-m1-callbacks-b0c02e55", "M1 callback resource not old-nine alias");
        Equal(SourceInterfacePackagePlan.C2M1ServicesResourceKey, "BlitzSourceInterface/original-contact281-c2-m1-match-services", "M1 services not old-nine alias");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile(null), SourceTeamResourceProfile.Contact272, "M1 must not change default C1 entry");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile("?sourceprofile=original-teams-contact281-c2-m1"),
            profile, "explicit M1 URL admits only the new successor generation");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile(null, profile.StableId), profile, "explicit M1 build selector");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.SelectRuntimeProfile(
            "?sourceprofile=original-teams-contact281-c2-m1", SourceTeamResourceProfile.C2StableId), "M1 URL cannot promote a pinned old-nine build");
    }

    private static GameSimulation M1Game(SourceC2CallbackData callbacks, Func<SourceRandomSite, int> raw = null,
        bool useOldFactory = false)
    {
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }), new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
        Func<int, ISourceRandom> random = seed => {
            int draw = 0;
            return new SourceInjectedRandom(site => draw++ == 0 ? 0 : draw == 2 ? 1 : raw == null ? 0 : raw(site),
                new SourceRandomProvenance("M1-resource-controlled-input", "Bounded source binding fixture, NOT public vector replay",
                    "no target PRNG claim", SourceRandomEvidence.ExternalUnverified));
        };
        var bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(callbacks);
        var settings = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        if (useOldFactory)
            return SourceC2Composition.CreateSimulation(library, Pivots(), real.Playbook, config, real.Rosters, settings,
                real.MovementRuntime, new SourceMovementSettings(0, 0, false, 0, 0, false), real.SoundFunctions, bootstrap, random);
        try
        {
            return SourceC2Composition.CreateM1Simulation(library, Pivots(), real.Playbook, config, real.Rosters, settings,
                real.MovementRuntime, new SourceMovementSettings(0, 0, false, 0, 0, false), real.SoundFunctions, bootstrap, random);
        }
        catch (Exception e) { throw new InvalidOperationException("M1 actual factory accepts only14 bootstrap before initial AI", e); }
    }

    private static void M1Profile(string[] args)
    {
        string inputs = args[7], catalog = args[8];
        byte[] json = File.ReadAllBytes(Path.Combine(inputs, "c2-callback-data.json"));
        byte[] hit = File.ReadAllBytes(Path.Combine(inputs, "m-qbfite-hit-extension.safn"));
        SourceC2CallbackData callbacks;
        try { callbacks = SourceInterfacePackagePlan.ReadC2M1Callbacks(json, hit); }
        catch (Exception e) { throw new InvalidOperationException("M1 canonical14 schema and reader must load", e); }
        Equal(callbacks.Profile, SourceC2CallbackData.M1Profile, "M1 payload has explicit successor profile");
        Equal(callbacks.Bindings.Count, 14, "M1 exact14 tuples, not old-nine promotion");
        Equal(callbacks.HitExtensionData.PayloadSha256, HitHash, "M1 shares only unchanged ONE7538");
        Reject<Exception>(() => SourceInterfacePackagePlan.ReadC2Callbacks(json, hit), "old-nine reader rejects M1 payload");
        Reject<Exception>(() => SourceInterfacePackagePlan.ReadC2M1Callbacks(
            File.ReadAllBytes(Path.Combine(args[0], "c2-callback-data.json")), hit), "M1 reader rejects old-nine payload");
        Reject<SourceInterfaceException>(() => M1Game(callbacks, useOldFactory: true), "old factory rejects new14 bootstrap");
        Reject<InvalidOperationException>(() => M1Game(data), "new factory rejects old-nine bootstrap");
        SourceCinematicLibrary m1Impact;
        try { m1Impact = SourceCinematicLibrary.Read(File.ReadAllBytes(catalog), SourceTeamResourceProfile.C2M1ImpactSha256); }
        catch (Exception e) { throw new InvalidOperationException("M1 canonical482 catalog must load", e); }
        SourceTeamResourceProfile.Contact281C2M1.RequireImpact(m1Impact);
        Equal(m1Impact.AnimationCues.Count, 482, "M1 exact482 source sites");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact281C2M1.RequireImpact(impact), "old477 catalog cannot be M1");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact281C2.RequireImpact(m1Impact), "new482 catalog cannot be old-nine");
        foreach (SourceCinematicCueDefinition cue in impact.AnimationCues)
            Check(m1Impact.RequireExecutedOpcode(cue.Script, cue.Line, cue.Kind).Arguments.SequenceEqual(cue.Arguments), "all477 old sites preserved unchanged");
        foreach (SourceC2VmBinding binding in callbacks.Bindings)
        {
            try
            {
                SourceCinematicCueDefinition cue = m1Impact.RequireExecutedOpcode(binding.ScriptSymbol, binding.SourceLine, "callback");
                Check(cue.Arguments.SequenceEqual(new[] { binding.FunctionSymbol, binding.Argument0.ToString(), "0", "0" }), "all14 actual M1 tuples match482 source catalog");
            }
            catch (Exception e) { throw new InvalidOperationException("M1 exact14 catalog caller tuples including6373 must exist", e); }
        }
        GameSimulation boot = M1Game(callbacks);
        Check(ReferenceEquals(Field(boot, "sourceAnimations"), library) &&
            ReferenceEquals(Field(Field(boot, "sourceGeometry"), "library"), library), "M1 SAME actual281 Core and geometry instance");
        Check(ReferenceEquals(Field(boot, "sourceSoundVmData"), real.SoundFunctions), "M1 old-six SAFN before first wake");
        SourceRandomDraw[] initial = ((SourceInjectedRandom)boot.SourceRandomService).Trace.ToArray();
        Check(initial.Length > 2 && initial[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            initial[1].Site.SourceIdentity.Contains("InitGameProc:560"), "M1 bootstrap real2 bank draws precede initial AI");
        foreach (SourceC2VmBinding binding in callbacks.Bindings.Skip(9))
        foreach (int chance in new[] { 9, 10 })
        {
            GameSimulation game = M1Game(callbacks, site => site.BoundOrModulus == 100 ? chance : 2);
            ActorState actor = game.Model.Players[4];
            actor.SourceMode = SourceActorMode.Receiver; actor.SourceHitPartnerId = -1;
            game.Model.SourceTick = 20;
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 4, 0);
            int prefix = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
            actor.AnimationState.FullBody(binding.ScriptSymbol, actor, 20, Bind<Func<int, int>>(game, "SourceDraw"),
                AnimationSelectionEvidence.SourceVerified);
            var pending = new List<SourceSoundVmDispatch>(game.TakeSourceSoundVmDispatches().Dispatches);
            for (int tick = 21; pending.Count == 0 && tick < 45; tick++)
            {
                game.Model.SourceTick = tick;
                actor.AnimationState.Primary.Advance(actor, tick, Bind<Func<int, int>>(game, "SourceDraw"));
                pending.AddRange(game.TakeSourceSoundVmDispatches().Dispatches);
            }
            SourceSoundVmDispatch d = pending.Single();
            Check(d.HasOriginalAnimationCue && d.OriginalCueContext.SourceLine == binding.SourceLine &&
                d.C2Context.Kind == SourceC2SoundFunctionKind.TauntSpeech &&
                d.OriginalCue.Argument0 == binding.Argument0 && d.C2Context.ActorId == 4 &&
                d.C2Context.Process == "player_proc", "M1 new-five real ANI caller/root process/actor tuple");
            SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Skip(prefix).ToArray();
            int expectedDraws = chance == 10 || binding.Argument0 == -5 ? 1 : 2;
            Equal(draws.Length, expectedDraws, "M1 chance first and only conditional index consumes actual shared RNG");
            Equal(draws[0].Site.BoundOrModulus, 100, "M1 actual root chance100");
            Check(draws[0].Site.SourceIdentity.EndsWith(binding.Argument0 == -5 ? ":523" : ":518", StringComparison.Ordinal),
                "M1 caller/callee RNG attribution is actual original root");
            if (chance == 10) Equal(d.Result.Calls.Count, 0, "M1 chance10 no output/no eager index");
            else
            {
                SourceAudioResolvedOriginalSoundCall call = d.Result.Calls.Single();
                Equal(call.PlaylistId, binding.Argument0 == -5 ? 11 : callbacks.SpeechTable("histep_taunt_sp")[2], "M1 real original playlist");
                Equal(call.CalleeSourceSite.SourceLine, binding.Argument0 == -5 ? 524 : 519, "M1 native callee519/524");
                Equal(call.Volume, 235u, "M1 full raw volume");
                Equal(call.Pan, 127u, "M1 full raw pan, not Unity gain-pan");
                Equal(call.PriorityWord, binding.Argument0 == -5 ? 5u : 1u, "M1 raw original priority");
                Equal(call.Bank, game.C2State.TauntBank, "M1 actual boot-selected bank");
            }
            var fifo = new SourceSoundVmTransport(); var sink = new RawRecorder(); fifo.BindOutput(sink);
            fifo.Drain(new SourceSoundVmBatch(pending));
            Check(ReferenceEquals(fifo.TakeForCue(d.OriginalCue), d), "M1 same FIFO pairs original cue once");
            var playback = new SourceCinematicPlayback(m1Impact, new SourceCinematicSeed(new SourceVector3(-120, 90, -20),
                new SourceVector3(0, 15, 0), 150, 1, -.4f));
            playback.ConsumeResolvedSourceSound(d, fifo.DeliverResolved); fifo.CompletePresentationBatch();
            Equal(sink.Values.Count, chance == 10 ? 0 : 1, "M1 raw recording not audible DSP and no duplicate");
        }
        string output = Path.Combine(args[6], "m1"); Directory.CreateDirectory(output);
        SourceInterfacePackagePlan m1 = SourceInterfacePackagePlan.CreateMovement157(args[5], "interface.json", SourceInterfacePackagePlan.HudSha256,
            Path.Combine(Path.GetDirectoryName(args[2]), "playbook"), "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            args[6], "configuration.json", Hash(Encoding.UTF8.GetBytes(Config)), Path.GetDirectoryName(args[2]), "movement-runtime.json",
            SourceInterfaceMovementData.ContentSha256, args[6], "bootstrap.json", Hash(Encoding.UTF8.GetBytes(Bootstrap)));
        SourceInterfacePackagePlan oldSound = SourceInterfacePackagePlan.WithSourceSoundVm(m1,
            Path.Combine(Path.GetDirectoryName(args[2]), "audio-functions"), "sound-functions.safn", SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        SourceInterfacePackagePlan c1 = SourceInterfacePackagePlan.WithContact272(oldSound);
        SourceInterfacePackagePlan successor = SourceInterfacePackagePlan.WithContact281C2M1(c1, inputs, "c2-callback-data.json", "m-qbfite-hit-extension.safn");
        Equal(successor.SelectedServicesResourceKey, SourceInterfacePackagePlan.C2M1ServicesResourceKey, "M1 actual package selects new services key");
        Equal(successor.C2Data.Bindings.Count, 14, "M1 actual package keeps14 binding data");
        Check(successor.CopyManifest().SequenceEqual(c1.CopyManifest()) && successor.CopyConfiguration().SequenceEqual(c1.CopyConfiguration()) &&
            successor.CopyMovement().SequenceEqual(c1.CopyMovement()) && successor.CopySoundVmFunctions().SequenceEqual(c1.CopySoundVmFunctions()),
            "M1 HUD/config/source-DIP/movement/old-six payload unchanged");
        Check(ReferenceEquals(SourceTeamResourceProfile.FromStableId(successor.Profile.StableId), SourceTeamResourceProfile.Contact281C2M1),
            "M1 resource registry is explicit without forcing deferred URL visibility");
        evidence.Add("M1 bounded14 data /482 sites /five running-taunt VM tuples /chance9vs10; no public M1 input, seed or vector replay.");
    }
}
