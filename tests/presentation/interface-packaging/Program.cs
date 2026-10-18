using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Editor;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static class Program
{
    private static int assertions;
    private const string Configuration = "{\"schema\":\"original-source-match-package/v1\",\"profile\":1,\"station_layout\":4," +
        "\"home_team\":{\"team\":0,\"records\":[0,1,2,3,4,5,6]},\"away_team\":{\"team\":1,\"records\":[7,8,9,10,11,12,13]}," +
        "\"stations\":[{\"id\":0,\"team\":0,\"initial_player_id\":3,\"profile\":{\"team\":0,\"record\":3}}," +
        "{\"id\":1,\"team\":1,\"initial_player_id\":10,\"profile\":{\"team\":1,\"record\":10}}]," +
        "\"first_possession\":1,\"captain\":1,\"time_adjustment\":4,\"difficulty\":7,\"continue_after_cabinet_gate\":false," +
        "\"home_play\":{\"symbol\":\"fixture_original\",\"kind\":0,\"slot\":6,\"page\":2,\"pool\":3,\"pool_index\":28}," +
        "\"away_play\":null,\"presentation\":{\"designated_home_side\":1,\"station_heads\":[0,1,18,19],\"pup_bigball\":-7}," +
        "\"native_gamepads_enabled\":true}";

    private static int Main(string[] args)
    {
        try
        {
            CheckConfiguration();
            CheckPublicCabinetConfigurations();
            if (args.Length > 0) CheckActual(args[0], args[1], args[2], args[3]);
            Console.WriteLine("PASS original interface packaging / " + assertions + " production-preflight assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckPublicCabinetConfigurations()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "CabinetConfigurations");
        SourceInterfacePackageConfiguration baseline = Parse(File.ReadAllText(Path.Combine(directory, "match-settings.json")));
        Equal(baseline.Match.StationLayout, SourceStationLayout.TwoStation, "public default retains two-station cabinet");
        Equal(baseline.Match.Stations.Count, 1, "public default remains one human");
        Equal(baseline.Match.Stations[0].StationId, 0, "public default keeps station zero");
        Equal(baseline.Match.Stations[0].Team, TeamId.Home, "public default keeps Home human");
        foreach (int count in new[] { 2, 4 })
        {
            string name = count == 2 ? "match-settings.two-player.json" : "match-settings.four-player.json";
            SourceInterfacePackageConfiguration selected = Parse(File.ReadAllText(Path.Combine(directory, name)));
            var stations = Enumerable.Range(0, count)
                .Select(id => new SourceStationConfiguration(id, id % 2 == 0 ? TeamId.Home : TeamId.Away)).ToArray();
            var expected = new SourceMatchConfiguration(baseline.Match.Profile, (SourceStationLayout)count,
                baseline.Match.HomeTeam, baseline.Match.AwayTeam, stations, baseline.Match.FirstPossession,
                baseline.Match.Captain, baseline.Match.TimeAdjustment, baseline.Match.Difficulty,
                baseline.Match.ContinueAfterCabinetGate, baseline.Match.HomePlay, baseline.Match.AwayPlay);
            SourceTeamInterfaceChecks.ConfigurationEquals(selected, expected, baseline.Presentation, baseline.NativeGamepadsEnabled);
            assertions++;
            Equal(selected.Match.Stations.Count, count, "public cabinet example binds every human station");
            Check(selected.Match.Stations.Select(s => s.StationId).SequenceEqual(Enumerable.Range(0, count)),
                "public cabinet example preserves distinct physical station IDs");
            CheckCabinetBuffer(selected.Match);
        }
    }

    private static void CheckCabinetBuffer(SourceMatchConfiguration configuration)
    {
        var buffer = new SourceCabinetInputBuffer(configuration);
        var neutral = configuration.Stations.Select(station =>
            new SourceCabinetSample(station.StationId, new SourceCabinetStick(24, 0, 0), SourceButtons.None)).ToArray();
        int[] ordinals = { 0, 6, 42, 48 };
        SourceButtons[] buttons = { SourceButtons.A, SourceButtons.B, SourceButtons.C, SourceButtons.A | SourceButtons.B | SourceButtons.C };
        var held = configuration.Stations.Select((station, index) =>
            new SourceCabinetSample(station.StationId, new SourceCabinetStick(ordinals[index], 0, 0), buttons[index])).ToArray();
        buffer.Capture(neutral);
        buffer.Capture(held);
        SourceInputFrame pressed = buffer.NextHostFrame();
        Equal(pressed.Stations.Count, configuration.Stations.Count, "actual cabinet buffer retains all configured humans");
        for (int i = 0; i < held.Length; i++)
        {
            Equal(pressed.Stations[i].StationId, held[i].StationId, "actual cabinet station identity");
            Equal(pressed.Stations[i].StickX, held[i].Stick.StickX, "actual cabinet independent X");
            Equal(pressed.Stations[i].StickZ, held[i].Stick.StickZ, "actual cabinet independent Z");
            Equal(pressed.Stations[i].Held, buttons[i], "actual cabinet independent held A/B/C");
            Equal(pressed.Stations[i].Pressed, buttons[i], "actual cabinet independent pressed A/B/C");
        }
        Check(buffer.NextHostFrame().Stations.All(s => s.Pressed == SourceButtons.None), "held frames do not replay source edges");
        buffer.Clear();
        Check(buffer.WaitingForNeutral && buffer.NextHostFrame().Stations.All(s =>
            s.Held == SourceButtons.None && s.Pressed == SourceButtons.None), "focus clear neutralizes every human");
        buffer.Capture(held);
        Check(buffer.WaitingForNeutral && buffer.PendingFrames == 0, "held controllers cannot bypass the shared neutral gate");
        buffer.Capture(neutral);
        Check(!buffer.WaitingForNeutral, "all configured cabinets can release the neutral gate");
        buffer.Capture(held);
        Check(buffer.NextHostFrame().Stations.Select(s => s.Pressed).SequenceEqual(buttons.Take(held.Length)),
            "focus recovery produces fresh edges for every human");
        Reject(() => buffer.Capture(held.Take(held.Length - 1).ToArray()), "Sample all configured", "missing human cabinet sample");
        SourceCabinetSample[] duplicate = held.ToArray();
        duplicate[duplicate.Length - 1] = held[0];
        Reject(() => buffer.Capture(duplicate), "duplicate", "duplicate human cabinet sample");
    }

    private static void CheckConfiguration()
    {
        SourceInterfacePackageConfiguration result = Parse(Configuration);
        Equal(result.Match.Profile, SourceSimulationProfile.OriginalRelease57Hz, "explicit57 profile");
        Equal(result.Match.HomeTeam.TeamIndex, 0, "actual selected home source team");
        Equal(result.Match.AwayTeam.TeamIndex, 1, "actual selected away source team");
        Check(result.Match.AwayTeam.RosterIndices.SequenceEqual(new[] { 7, 8, 9, 10, 11, 12, 13 }), "actual seven actor/static-record bindings");
        Equal(result.Match.StationLayout, SourceStationLayout.FourStation, "explicit four-station layout");
        Equal(result.Match.Stations[1].InitialPlayerId, 10, "actual station/actor binding");
        Equal(result.Match.Stations[1].Profile.RosterIndex, 10, "actual station/static profile");
        Equal(result.Match.FirstPossession, TeamId.Away, "not inferred home possession");
        Equal(result.Match.Captain, SourceCaptainSlot.Second, "captain teammate offset, not TeamId");
        Equal(result.Match.TimeAdjustment, 4, "source time adjustment preserved");
        Equal(result.Match.Difficulty, 7, "source difficulty preserved");
        Check(!result.Match.ContinueAfterCabinetGate, "explicit false continuation is not a default");
        Equal(result.Match.HomePlay.PlaySymbol, "fixture_original", "original play identity");
        Equal(result.Match.HomePlay.PageIndex, 2, "original page");
        Equal(result.Match.HomePlay.SlotIndex, 6, "original slot");
        Equal(result.Match.HomePlay.PoolIndex, 28, "original duplicate weighted ordinal");
        Check(!result.Match.AwayPlay.IsSpecified, "explicit null play stays unknown, not legacy PlayId");
        Equal(result.Presentation.DesignatedHomeSide, TeamId.Away, "actual designated home side");
        Check(result.Presentation.StationHeads.SequenceEqual(new[] { 0, 1, 18, 19 }), "all actual source station heads");
        Equal(result.Presentation.PupBigBall, -7, "entire original pup int, not bool normalization");
        Check(result.NativeGamepadsEnabled, "explicit native gamepad choice");
        SourceTeamInterfaceChecks.ConfigurationEquals(result, result.Match, result.Presentation, true);
        Reject(() => SourceTeamInterfaceChecks.ConfigurationEquals(result, result.Match,
            new SourcePresentationSettings(TeamId.Home, result.Presentation.StationHeads, -7), true),
            "home/head/pup", "real default-home mutation");
        Reject(() => SourceTeamInterfaceChecks.ConfigurationEquals(result, result.Match,
            new SourcePresentationSettings(TeamId.Away, result.Presentation.StationHeads, 1), true),
            "home/head/pup", "whole pup int mutation");
        foreach (string changed in new[] {
            Configuration.Replace("\"station_layout\":4", "\"station_layout\":2"),
            Configuration.Replace("[0,1,2,3,4,5,6]", "[15,1,2,3,4,5,6]"),
            Configuration.Replace("[7,8,9,10,11,12,13]", "[7,8,9,10,11,12,14]"),
            Configuration.Replace("\"initial_player_id\":10", "\"initial_player_id\":11"),
            Configuration.Replace("\"record\":10", "\"record\":11"),
            Configuration.Replace("\"first_possession\":1", "\"first_possession\":0"),
            Configuration.Replace("\"captain\":1", "\"captain\":0"),
            Configuration.Replace("\"time_adjustment\":4", "\"time_adjustment\":2"),
            Configuration.Replace("\"difficulty\":7", "\"difficulty\":5"),
            Configuration.Replace("\"continue_after_cabinet_gate\":false", "\"continue_after_cabinet_gate\":true"),
            Configuration.Replace("fixture_original", "fixture_changed"),
            Configuration.Replace("\"kind\":0", "\"kind\":1"),
            Configuration.Replace("\"slot\":6", "\"slot\":5"),
            Configuration.Replace("\"page\":2", "\"page\":1"),
            Configuration.Replace("\"pool\":3", "\"pool\":4"),
            Configuration.Replace("\"pool_index\":28", "\"pool_index\":27"),
            Configuration.Replace("[0,1,18,19]", "[0,1,17,19]"),
            Configuration.Replace("\"native_gamepads_enabled\":true", "\"native_gamepads_enabled\":false")
        })
        {
            SourceInterfacePackageConfiguration saved = Parse(changed);
            Reject(() => SourceTeamInterfaceChecks.ConfigurationEquals(result, saved.Match,
                saved.Presentation, saved.NativeGamepadsEnabled), null, "every saved selected source setting compared");
        }
        var legacy = new SourceMatchConfiguration(SourceSimulationProfile.LegacyBaseline60Hz,
            result.Match.StationLayout, result.Match.HomeTeam, result.Match.AwayTeam, result.Match.Stations,
            result.Match.FirstPossession, result.Match.Captain, result.Match.TimeAdjustment, result.Match.Difficulty,
            result.Match.ContinueAfterCabinetGate, result.Match.HomePlay, result.Match.AwayPlay);
        Reject(() => SourceTeamInterfaceChecks.ConfigurationEquals(result, legacy, result.Presentation, true),
            "scalar", "saved source profile cannot become legacy");
        SourceInterfacePackageConfiguration absentHead = Parse(Configuration.Replace("[0,1,18,19]", "[-1,1,18,19]"));
        Equal(absentHead.Presentation.StationHeads[0], -1, "explicit original absent head is not default head zero");
        SourceInterfacePackageConfiguration unknownProfile = Parse(Configuration.Replace("\"profile\":{\"team\":1,\"record\":10}", "\"profile\":null"));
        Check(!unknownProfile.Match.Stations[1].Profile.IsSpecified, "explicit null station profile stays unknown");
        int invalidIndex = 0;
        foreach (string invalid in new[] {
            Configuration.Replace("\"pup_bigball\":-7", "\"pup_bigball\":-7,\"extra_original_setting\":0"),
            Configuration.Replace("\"pup_bigball\":-7", "\"pup_bigball\":-7,\"pup_bigball\":1"),
            Configuration.Replace("\"difficulty\":7,", ""),
            Configuration.Replace("\"profile\":1", "\"profile\":0"),
            Configuration.Replace("[0,1,18,19]", "[0,1,18]"),
            Configuration.Replace("\"team\":1,\"records\"", "\"team\":31,\"records\""),
            Configuration.Replace("\"station_layout\":4", "\"station_layout\":3"),
            Configuration.Replace("\"profile\":{\"team\":1,\"record\":10}", "\"profile\":{\"team\":0,\"record\":10}"),
            Configuration.Replace("\"pup_bigball\":-7", "\"pup_bigball\":null"),
            Configuration.Replace("\"pup_bigball\":-7", "\"pup_bigball\":true"),
            Configuration.Replace("\"difficulty\":7", "\"difficulty\":\"7\""),
            Configuration.Replace("\"difficulty\":7", "\"difficulty\":7.5"),
            Configuration.Replace("\"continue_after_cabinet_gate\":false", "\"continue_after_cabinet_gate\":null"),
            Configuration.Replace("\"continue_after_cabinet_gate\":false", "\"continue_after_cabinet_gate\":0"),
            Configuration.Replace("\"home_play\":{", "\"home_play\":{\"extra\":0,"),
            Configuration.Replace("\"home_play\":{", "\"home_play\":{\"pool\":null,"),
            Configuration.Replace("\"profile\":{\"team\":1,\"record\":10}", "\"profile\":{\"record\":10}"),
            Configuration.Replace("[0,1,18,19]", "[0,1,18,20]"),
            Configuration.Replace("[0,1,18,19]", "[0,1,18,-2]"),
            Configuration.Replace("\"stations\":[", "\"stations\":[null,"),
            Configuration + " {}", Configuration + ",{}", Configuration + "] {}", Configuration + "]",
            Configuration + "\0"
        }) Reject(() => Parse(invalid), null, "strict emitted configuration mutation " + invalidIndex++);
        byte[] bytes = Encoding.UTF8.GetBytes(Configuration);
        Reject(() => SourceInterfacePackagePlan.ReadConfiguration(bytes, new string('0', 64)),
            "content pin", "configuration content mutation");
        SourceInterfacePackagePlan.RequireOutputIdentity("Assets/Imported/fresh/Resources/" +
            SourceInterfacePackagePlan.HudResourceKey + ".asset", SourceInterfacePackagePlan.HudResourceKey);
        foreach (string path in new[] {
            "Assets/Resources/" + SourceInterfacePackagePlan.HudResourceKey + ".asset",
            "Assets/Imported/fresh/Resources/BlitzSourceInterface/old-subset.asset",
            "Assets/Imported/../Resources/" + SourceInterfacePackagePlan.HudResourceKey + ".asset"
        }) Reject(() => SourceInterfacePackagePlan.RequireOutputIdentity(path, SourceInterfacePackagePlan.HudResourceKey),
            null, "ignored exact Resources identity");
        Reject(() => SourceInterfacePackagePlan.RequireOutputIdentity(
            "Assets/Imported/fresh/Resources/BlitzSourceInterface/unknown.asset", "BlitzSourceInterface/unknown"),
            "exact Resources identity", "only the two owned exact resource keys are allowed");
    }

    private static void CheckActual(string hudRoot, string bookRoot, string artifacts, string uiRoot)
    {
        Directory.CreateDirectory(artifacts);
        SourceInterfacePackagePlan.RequireHudShaderSource(uiRoot);
        assertions++;
        string changedShaderRoot = Path.Combine(artifacts, "changed-shader");
        string changedShader = Path.Combine(changedShaderRoot, SourceInterfacePackagePlan.HudShaderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(changedShader));
        File.WriteAllBytes(changedShader, File.ReadAllBytes(Path.Combine(uiRoot, SourceInterfacePackagePlan.HudShaderPath))
            .Concat(new byte[] { 10 }).ToArray());
        Reject(() => SourceInterfacePackagePlan.RequireHudShaderSource(Path.GetFullPath(changedShaderRoot)),
            "shader source pin", "actual changed shader source is not a same-name fallback");
        string realConfig = Configuration.Replace("\"home_play\":{\"symbol\":\"fixture_original\",\"kind\":0,\"slot\":6,\"page\":2,\"pool\":3,\"pool_index\":28}",
            "\"home_play\":null");
        string configPath = Path.Combine(artifacts, "selected-explicit-config.json");
        File.WriteAllText(configPath, realConfig, new UTF8Encoding(false));
        string hash = Hash(File.ReadAllBytes(configPath));
        Func<SourceInterfacePackagePlan> load = () => SourceInterfacePackagePlan.Create(hudRoot, "interface.json",
            SourceInterfacePackagePlan.HudSha256, bookRoot, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            Path.GetFullPath(artifacts), "selected-explicit-config.json", hash);
        SourceInterfacePackagePlan plan = load();
        Equal(plan.Textures.Count, 3, "all actual original HUD/playsel/raw arrow textures");
        Equal(plan.Playbook.Plays.Count, 88, "actual P1 play inventory, not a synthetic playbook");
        foreach (SourceInterfacePackageTexture texture in plan.Textures)
        {
            byte[] original = File.ReadAllBytes(Path.Combine(hudRoot, texture.Definition.Path));
            Check(texture.CopyBytes().SequenceEqual(original), "actual emitted rawRGBA bytes " + texture.Definition.Id);
            Equal(original.Length, texture.Definition.Width * texture.Definition.Height * 4, "actual RGBA dimensions " + texture.Definition.Id);
            byte[] mutated = texture.CopyBytes(); mutated[mutated.Length / 2] ^= 1;
            Reject(() => plan.Hud.RequireTextureBytes(texture.Definition.Id, mutated), null, "real emitted atlas byte mutation");
            byte[] detached = texture.CopyBytes(); detached[0] ^= 1;
            Check(texture.CopyBytes().SequenceEqual(original), "package plan owns detached original bytes");
        }
        Equal(Hash(plan.CopyManifest()), SourceInterfacePackagePlan.HudSha256, "complete original wind/fire HUD pin");
        Equal(Hash(plan.CopyPlaybook()), SourceInterfacePackagePlan.PlaybookSha256, "actual original P1 pin");
        Equal(Hash(plan.CopyConfiguration()), hash, "actual explicit configuration copied verbatim");
        byte[] configCopy = plan.CopyConfiguration(); configCopy[0] ^= 1;
        Equal(Hash(plan.CopyConfiguration()), hash, "saved configuration bytes detached from callers");
        Equal(plan.Configuration.Presentation.PupBigBall, -7, "actual packaging preserves explicit selected pup");
        Check(!Directory.Exists(Path.Combine(artifacts, "Assets")), "pure preflight creates no fake Unity assets");
        Reject(() => SourceInterfacePackagePlan.Create(hudRoot, "../interface.json", SourceInterfacePackagePlan.HudSha256,
            bookRoot, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256, Path.GetFullPath(artifacts),
            "selected-explicit-config.json", hash), null, "actual preflight manifest traversal");
        Reject(() => SourceInterfacePackagePlan.Create(hudRoot, "interface.json", new string('0', 64),
            bookRoot, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256, Path.GetFullPath(artifacts),
            "selected-explicit-config.json", hash), "HUD manifest pin", "actual wrong-HUD pin rejects before output");
        Reject(() => SourceInterfacePackagePlan.Create(hudRoot, "interface.json", SourceInterfacePackagePlan.HudSha256,
            bookRoot, "catalog.tsv", new string('0', 64), Path.GetFullPath(artifacts),
            "selected-explicit-config.json", hash), "P1 catalog pin", "actual wrong-P1 pin rejects before output");
        string changedBook = Path.Combine(artifacts, "changed-book");
        Directory.CreateDirectory(changedBook);
        File.WriteAllBytes(Path.Combine(changedBook, "catalog.tsv"),
            File.ReadAllBytes(Path.Combine(bookRoot, "catalog.tsv")).Concat(new byte[] { 10 }).ToArray());
        using (var reader = new StringReader(File.ReadAllText(Path.Combine(changedBook, "catalog.tsv"))))
            Equal(SourcePlaybook.LoadConverted(reader).Plays.Count, 88, "changed P1 is still parseable; content pin is the protected gate");
        Reject(() => SourceInterfacePackagePlan.Create(hudRoot, "interface.json", SourceInterfacePackagePlan.HudSha256,
            Path.GetFullPath(changedBook), "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            Path.GetFullPath(artifacts), "selected-explicit-config.json", hash), "content pin", "real changed-P1 bytes reject");
        string changedHud = Path.Combine(artifacts, "changed-hud");
        Directory.CreateDirectory(changedHud);
        File.WriteAllBytes(Path.Combine(changedHud, "interface.json"), plan.CopyManifest());
        foreach (SourceInterfacePackageTexture texture in plan.Textures)
        {
            byte[] emitted = texture.CopyBytes();
            if (texture.Definition.Id == "interface") emitted[emitted.Length / 2] ^= 1;
            File.WriteAllBytes(Path.Combine(changedHud, texture.Definition.Path), emitted);
        }
        Reject(() => SourceInterfacePackagePlan.Create(Path.GetFullPath(changedHud), "interface.json",
            SourceInterfacePackagePlan.HudSha256, bookRoot, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            Path.GetFullPath(artifacts), "selected-explicit-config.json", hash), null, "actual changed atlas read through production Create");
        File.WriteAllBytes(Path.Combine(changedHud, "interface.json"),
            plan.CopyManifest().Concat(new byte[] { 32 }).ToArray());
        Reject(() => SourceInterfacePackagePlan.Create(Path.GetFullPath(changedHud), "interface.json",
            SourceInterfacePackagePlan.HudSha256, bookRoot, "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            Path.GetFullPath(artifacts), "selected-explicit-config.json", hash), null, "actual changed manifest read through production Create");
        SourcePlayDefinition originalPlay = plan.Playbook.Plays.Values.First(p => p.Side == SourcePlaySide.Offense);
        string selectedPlay = "\"home_play\":{\"symbol\":\"" + originalPlay.Identity +
            "\",\"kind\":0,\"slot\":-1,\"page\":-1,\"pool\":null,\"pool_index\":-1}";
        File.WriteAllText(configPath, realConfig.Replace("\"home_play\":null", selectedPlay), new UTF8Encoding(false));
        hash = Hash(File.ReadAllBytes(configPath));
        SourceInterfacePackagePlan selectedPlan = load();
        Equal(selectedPlan.Configuration.Match.HomePlay.PlaySymbol, originalPlay.Identity, "actual original selected P1 symbol packaged");
        Equal(selectedPlan.Configuration.Match.HomePlay.PageIndex, -1, "unknown original selected page not inferred");
        File.WriteAllText(configPath, Configuration, new UTF8Encoding(false));
        hash = Hash(File.ReadAllBytes(configPath));
        Reject(() => load(), "absent from actual P1", "non-original configured symbol rejected by real catalog");
        Check(!Directory.Exists(Path.Combine(artifacts, "Assets")), "all failing production preflights leave no Unity asset output");
        Console.WriteLine("ACTUAL package preflight: complete3 original atlases /88 P1 plays / explicit copied configuration; no Editor/assets/game claim");
    }

    private static SourceInterfacePackageConfiguration Parse(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return SourceInterfacePackagePlan.ReadConfiguration(bytes, Hash(bytes));
    }
    private static string Hash(byte[] bytes)
    { using (SHA256 sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))); }
    private static void Equal<T>(T actual, T expected, string label)
    { Check(EqualityComparer<T>.Default.Equals(actual, expected), label); }
    private static void Check(bool value, string label)
    { assertions++; if (!value) throw new InvalidOperationException("FAIL packaging: " + label); }
    private static void Reject(Action action, string expected, string label)
    {
        bool rejected = false;
        try { action(); } catch (Exception error) {
            rejected = expected == null || error.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        Check(rejected, label + " must reject " + expected);
    }
}
