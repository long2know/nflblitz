using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;

internal static class Program
{
    private static int assertions;
    private static int rejectedMutations;
    private static byte[] synthetic;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Assertion failed: " + message);
    }

    private static string Hash(byte[] data)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(data);
            var text = new StringBuilder(64);
            foreach (byte value in bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    private static void Rejected(Action action, string messagePart = null)
    {
        try { action(); }
        catch (SourceRosterException e)
        {
            Check(messagePart == null || e.Message.IndexOf(messagePart, StringComparison.OrdinalIgnoreCase) >= 0,
                "diagnostic names the violated contract: " + e.Message);
            rejectedMutations++;
            return;
        }
        throw new Exception("An invalid roster was accepted.");
    }

    private static void Immutable(Action mutation)
    {
        try { mutation(); }
        catch (NotSupportedException) { assertions++; return; }
        throw new Exception("A published roster collection was mutable.");
    }

    private static void Mutated(Action<JsonObject> mutation, string diagnostic = null)
    {
        JsonObject doc = JsonNode.Parse(synthetic).AsObject();
        mutation(doc);
        byte[] bytes = Encoding.UTF8.GetBytes(doc.ToJsonString());
        Rejected(() => SourceTeamRosterLibrary.Read(bytes, Hash(bytes)), diagnostic);
    }

    private static void SyntheticChecks(string path)
    {
        synthetic = File.ReadAllBytes(path);
        byte[] callerOwned = (byte[])synthetic.Clone();
        SourceTeamRosterLibrary library = SourceTeamRosterLibrary.Read(callerOwned, Hash(callerOwned));
        ISourceTeamRoster shared = library;
        Check(shared.SourceRevision == SourceTeamRosterLibrary.PinnedSourceRevision, "shared exact source revision");
        Check(shared.Teams.Count == 31 && library.PlayersBySourceId.Count == 496, "complete source cardinality");
        Check(library.GetPlayerData(new SourceRosterIdentity(30, 15)).SourceStaticId == 495, "team/static identity, not shuffled actor ID");
        string key = library.TeamData[0].Players[0].Profile.SourcePlayerId;
        SourceRosterPlayer player = library.GetPlayerData(key);
        Check(ReferenceEquals(shared.GetPlayer(key), player.Profile), "Lead contract projection matches full typed record");
        Check(ReferenceEquals(shared.GetTeam(0), library.TeamData[0].Roster), "Lead shared team projection");
        Check(player.Profile.PackedJerseyNumber == 0x54 && player.JerseyNumber == 54, "BCD is not decimal conversion of the hexadecimal integer");
        Check(player.JerseyDigitIndices[0] == 4 && player.JerseyDigitIndices[1] == 3, "number overlay digit atlas selectors");
        Check(player.Profile.ModelScale == 1.05f && player.Profile.ModelScale != 1f, "active source scale");
        Check(player.Ratings.Speed == 0 && player.Ratings.Power == 1 && player.Ratings.Defense == 5, "typed ratings preserve source fields");
        Check(player.HeightInches == null && player.WeightPounds == null && player.StaticHeadSelector == null &&
            player.StaticFlags == null && library.TeamData[0].CaptainSourcePlayerId == null && library.TeamData[0].RgbPalette == null,
            "absent source fields are not fabricated");
        Check(library.GetUniform(0, SourceUniformVariant.Home, SourceSkinSelector.White).DecalOffset == 0 &&
            library.GetUniform(0, SourceUniformVariant.Home, SourceSkinSelector.Black).DecalOffset == 1, "race is a skin-file selector");
        SourceRosterUniform home = library.GetUniform(0, SourceUniformVariant.Home, SourceSkinSelector.White);
        Check(home.Slots[2] == home.Slots[3] && home.Slots[2] == home.Slots[4] && home.Slots[2] == home.Slots[5],
            "same authored atlas is bound to all uniform and number slots");
        Check(home.Slots[0].RuntimeFilename == "ARMHND.WMS" && home.Slots[0].AvailableInPinnedTree == false,
            "missing original skin WMS is explicit, not recolored fallback");
        Check(library.GetUniform(0, SourceUniformVariant.Away, SourceSkinSelector.Black).Slots[2].Id != home.Slots[2].Id,
            "home/away atlas identity retained");
        Check(library.ActorModels.SelectModel(SourceBodySize.Normal).Index == 0, "normal low-count model");
        Check(library.ActorModels.SelectModel(SourceBodySize.Fat, true).Index == 3, "fat high-count bits");
        Check(library.ActorModels.SelectModel(SourceBodySize.Fat, true, SourceHeldBallModel.Both, true).Index == 31,
            "both-held-ball and jumbo source bits");
        Check(library.ActorModels.SelectJersey(player).Index == 5, "fat two-digit jersey selector");
        Check(library.ActorModels.NumberTextureSlot == 5 && library.ActorModels.NumberMatrixIndex == 1 &&
            library.ActorModels.HeadJointIndex == 8, "exact original number and head attachment selectors");
        SourceRosterUniformPair noClash = library.SelectMatchUniforms(0, 1, 0);
        Check(noClash.Side0 == SourceUniformVariant.Home && noClash.Side1 == SourceUniformVariant.Home,
            "both physical sides may wear home variants");
        SourceRosterUniformPair clash = library.SelectMatchUniforms(0, 0, 1);
        Check(clash.Side0 == SourceUniformVariant.Away && clash.Side1 == SourceUniformVariant.Home,
            "home side designation controls colliding uniform variants");
        Check(library.Lineups.Select(0, "play_fixture_normal", true).RosterIndices[0] == 0 &&
            library.Lineups.Select(0, "play_fixture_normal", false).RosterIndices[0] == 7, "source active actor slots not static uniform roles");
        Check(library.Lineups.Select(29, "play_kick_return_short", true).RosterIndices.Count == 7, "last in-bounds source returner selector");
        Rejected(() => library.Lineups.Select(30, "play_kick_return_short", true), "returners[30]");
        Check(library.Secrets.FindHeadIndex("HEAD  ", "0000") == 0 &&
            library.Secrets.FindHeadIndex("HEAD", "0000") == -1, "exact padded game head-selector comparison");
        Check(library.Secrets.FindHeadIndex("F00020", "0020") == 0, "distinct public game codes may alias one head");
        Check(library.Provenance.Inputs.Count == 22 && library.Profile.Platform == "SEATTLE", "complete pinned source/profile metadata");
        Check(library.Tuning.Constants["MOD_2DIGITS"].Value == 4 && library.Tuning.Direction49Table.Count == 49,
            "source model and motion tuning input surfaces");

        Array.Clear(callerOwned, 0, callerOwned.Length);
        Check(library.GetPlayer(key).LastName == "Fixture" && library.ContentSha256 == Hash(synthetic), "detached from caller byte-array mutation");
        Immutable(() => ((IList<SourceTeamRoster>)shared.Teams)[0] = shared.Teams[1]);
        Immutable(() => ((IList<SourceRosterProfile>)shared.GetTeam(0).Players)[0] = shared.GetTeam(0).Players[1]);
        Immutable(() => ((IList<SourceRosterPlayer>)library.TeamData[0].Players)[0] = library.TeamData[0].Players[1]);
        Immutable(() => ((IList<string>)player.RawFields)[0] = "100");
        Immutable(() => ((IList<int>)player.JerseyDigitIndices)[0] = 0);
        Immutable(() => ((IDictionary<string, SourceRosterPlayer>)library.PlayersBySourceId).Clear());
        Immutable(() => ((IDictionary<string, SourceRosterAsset>)library.Assets).Clear());
        Immutable(() => ((IList<SourceRosterAsset>)home.Slots)[2] = home.Slots[0]);
        Immutable(() => ((IList<int>)library.Lineups.Indices["std_offense"])[0] = 15);
        Immutable(() => ((IDictionary<string, string>)library.Lineups.OffenseSelectors).Add("play_new", "punt"));
        Immutable(() => ((IList<int>)library.AwayJersey[0])[0] = 0);
        Immutable(() => ((IList<int>)library.Tuning.TeamLadder[0])[0] = 30);
        Immutable(() => ((IDictionary<string, SourceRosterConstant>)library.Tuning.Constants).Clear());
        Immutable(() => ((IList<SourceRosterHeadCode>)library.Secrets.SelectionCodes).Clear());
        Immutable(() => ((IList<string>)library.Profile.Defines)[0] = "VEGAS");
        Immutable(() => ((IList<SourceRosterInputPin>)library.Provenance.Inputs).Clear());
        var records = new SourceRosterProfile[16];
        for (int i = 0; i < records.Length; i++) records[i] = shared.GetTeam(0).Players[i];
        var detachedRoster = new SourceTeamRoster(0, shared.GetTeam(0).SourceRosterSymbol, records);
        records[0] = null;
        Check(detachedRoster.Players[0] != null, "shared contract constructor detaches input array");

        Rejected(() => SourceTeamRosterLibrary.Read(synthetic, new string('0', 64)), "content pin");
        Rejected(() => SourceTeamRosterLibrary.Read(synthetic, null), "pin");
        Rejected(() => SourceTeamRosterLibrary.Read(callerOwned, Hash(synthetic)), "content pin");
        Rejected(() => library.GetPlayerData("not-an-actor-id"), "source-player");
        Rejected(() => library.GetTeam(31), "range");
        Rejected(() => library.ActorModels.SelectModel((SourceBodySize)3), "body");
        Rejected(() => library.ActorModels.SelectModel(SourceBodySize.Normal, false, (SourceHeldBallModel)1), "held-ball");
        Mutated(doc => doc["schema"] = "original-team-rosters/v99", "schema");
        Mutated(doc => doc["provenance"]["commit"] = new string('0', 40), "provenance");
        Mutated(doc => doc["provenance"]["content_pin_verified"] = false, "provenance");
        Mutated(doc => doc["provenance"]["manifest_sha256"] = new string('0', 64), "provenance");
        Mutated(doc => doc["provenance"]["inputs"][0]["git_blob_sha1"] = new string('0', 40), "manifest catalog");
        Mutated(doc => doc["profile"]["platform"] = "MODERN_NFL", "platform");
        Mutated(doc => doc["profile"]["maximum_humans"] = 5, "range");
        Mutated(doc => doc["profile"]["scale_macro"] = "(1.0f * x)", "scale");
        Mutated(doc => doc["teams"].AsArray().RemoveAt(30), "31");
        Mutated(doc => doc["teams"][0]["id"] = 1, "range");
        Mutated(doc => doc["teams"][0]["players"].AsArray().RemoveAt(15), "sixteen");
        Mutated(doc => doc["teams"][0]["players"][1]["id"] = doc["teams"][0]["players"][0]["id"].GetValue<string>(), "identity");
        Mutated(doc => doc["teams"][0]["players"][0]["index"] = 1, "range");
        Mutated(doc => doc["teams"][0]["players"][0]["size"]["symbol"] = "PS_GUESSED", "enum");
        Mutated(doc => doc["teams"][0]["players"][0]["size"]["raw"] = "PS_GUESSED", "alias");
        Mutated(doc => doc["teams"][0]["players"][0]["scale"] = 1, "scale");
        Mutated(doc => doc["teams"][0]["players"][0]["number_packed_bcd"] = 0x5a, "BCD");
        Mutated(doc => doc["teams"][0]["players"][0]["jersey_number"] = 84, "BCD");
        Mutated(doc => doc["teams"][0]["players"][0]["statistics"][0] = 10, "rating");
        Mutated(doc => doc["teams"][0]["players"][0]["height_inches"] = 0, "absent");
        Mutated(doc => doc["teams"][0]["players"][0]["new_stat"] = 0, "unknown");
        Mutated(doc => doc["uniforms"][0]["slot_asset_ids"][0] = "missing/binding", "uniform slots");
        Mutated(doc => doc["uniforms"][0]["decal_offset"] = 0, "decal");
        Mutated(doc => doc["assets"][0]["source_path"] = "../outside", "source-relative");
        Mutated(doc => doc["missing_asset_ids"].AsArray().RemoveAt(0), "missing-asset");
        Mutated(doc => doc["actor_model_schema"]["models"][0]["limb_asset_ids"][0] = "missing/limb", "Missing original asset");
        Mutated(doc => doc["lineups"]["std_offense"][0] = 1, "Duplicate");
        Mutated(doc => doc["lineups"]["offense_selector"][0]["lineup"] = "missing", "lineup reference");
        Mutated(doc => doc["selection"]["secret_heads"]["selection_codes"][0]["head_index"] = 20, "range");
        Mutated(doc => doc["tuning"]["constants"][1]["value"] = 99, "enum");
        Mutated(doc => doc["unexpected"] = true, "unknown");

        string text = Encoding.UTF8.GetString(synthetic);
        foreach (string invalid in new[] {
            "{\"schema\":\"original-team-rosters/v1\",\"schema\":\"duplicate\"}",
            text + "x", text.Replace("\"scale\":1.0499999523162842", "\"scale\":NaN"),
            text.Replace("\"scale\":1.0499999523162842", "\"scale\":1e999"),
            text.Replace("\"scale\":1.0499999523162842", "\"scale\":01"),
            text.Replace("\"schema\":\"original-team-rosters/v1\"", "\"schema\":\"\\ud800\"")
        })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(invalid);
            Rejected(() => SourceTeamRosterLibrary.Read(bytes, Hash(bytes)));
        }
    }

    private static void ActualChecks(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        SourceTeamRosterLibrary library = SourceTeamRosterLibrary.Read(bytes, Hash(bytes));
        JsonObject manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path),
            Path.GetFileNameWithoutExtension(path) + ".manifest.json"))).AsObject();
        Check(manifest["sha256"].GetValue<string>() == library.ContentSha256 && manifest["bytes"].GetValue<int>() == bytes.Length,
            "actual output manifest byte/hash pin matches production loader");
        Check(library.TeamData.Count == 31 && library.PlayersBySourceId.Count == 496 && library.Uniforms.Count == 124,
            "actual complete dataset cardinality");
        SourceRosterPlayer cade = library.GetPlayerData("spd_bea:03");
        Check(cade.Profile.Identity.TeamIndex == 5 && cade.Profile.Identity.RosterIndex == 3 &&
            cade.Profile.FirstName == "Cade" && cade.Profile.LastName == "McNown" &&
            cade.Skin == SourceSkinSelector.White && cade.Position == SourceRosterPosition.Quarterback &&
            cade.JerseyNumber == 8, "actual Bears/Cade McNown/PC_WHITE/#8 source record");
        SourceRosterUniform bears = library.GetUniform(5, SourceUniformVariant.Home, cade.Skin);
        Check(bears.Slots[0].RuntimeFilename == "ARMHND.WMS" && bears.Slots[1].RuntimeFilename == "HEDSHU.WMS" &&
            bears.Slots[2].SourcePath == "img3d/BEAHLOD.wms", "selected original preview material cross-check, not universal roster substitution");
        Check(library.ActorModels.SelectModel(cade.BodySize).LimbArray == "model266_limbs", "actual source-selected normal body");
        Check(library.ActorModels.SelectModel(SourceBodySize.Fat).LimbArray == "model266f_limbs", "actual fat body is different");
        Check(library.ActorModels.SelectModel(SourceBodySize.Normal, true).LimbArray == "model498_limbs", "actual high-count model selector");
        Check(library.ActorModels.SelectModel(SourceBodySize.Normal, false, SourceHeldBallModel.Left).LimbArray == "model266bl_limbs",
            "actual left-held-ball skeleton mapping");
        Check(library.ActorModels.SelectModel(SourceBodySize.Normal, false, SourceHeldBallModel.Both).LimbArray == "model266br_limbs",
            "source both-balls selector deliberately reuses right-held model");
        int fat = 0, white = 0;
        var identities = new HashSet<int>();
        foreach (SourceRosterTeam team in library.TeamData)
        {
            Check(team.Players.Count == 16 && team.Roster.TeamIndex >= 0 && team.Roster.TeamIndex < 31, "actual team records and IDs");
            foreach (SourceRosterPlayer player in team.Players)
            {
                Check(identities.Add(player.SourceStaticId), "unique actual stable static ID");
                Check(player.Profile.ModelScale == 1.05f && player.ScaleExpression == "SC(1.0f)", "actual source scale, not assumed 1.0");
                Check(player.Ratings.Speed == 0 && player.Ratings.Power == 0 && player.Ratings.Quarterback == 0 &&
                    player.Ratings.Receiver == 0 && player.Ratings.Offense == 0 && player.Ratings.Defense == 0,
                    "literal six-stat source zeros retained; team ratings are separate");
                if (player.BodySize == SourceBodySize.Fat) fat++;
                if (player.Skin == SourceSkinSelector.White) white++;
            }
        }
        Check(fat == 252 && white == 167, "actual original body/skin enum coverage");
        Check(library.TeamData[11].DroneBase == 19, "source Packers drone_base not clamped to conventional difficulty range");
        Check(library.TeamData[21].Home == "Oakland" && library.TeamData[27].Home == "St. Louis", "historical source teams, not modern data");
        Check(library.Secrets.Heads.Count == 20 && library.Secrets.SelectionCodes.Count == 21 &&
            library.Secrets.FindHeadIndex("MXV   ", "1014") == 12, "actual separate historical custom-head catalog and alias");
        Check(library.Secrets.Heads[0].Texture.RuntimeFilename == "hed_mark.wms",
            "original custom-head runtime filename case retained separately from stable asset ID");
        SourceRosterUniformPair forward = library.SelectMatchUniforms(0, 1, 0);
        SourceRosterUniformPair reverse = library.SelectMatchUniforms(1, 0, 0);
        Check(forward.Side0 == SourceUniformVariant.Home && forward.Side1 == SourceUniformVariant.Away &&
            reverse.Side0 == forward.Side0 && reverse.Side1 == forward.Side1,
            "actual upper-triangle collision lookup uses min/max IDs, never the lower-triangle zero placeholder");
        SourceRosterUniformPair dissimilar = library.SelectMatchUniforms(0, 8, 0);
        Check(dissimilar.Side0 == SourceUniformVariant.Home && dissimilar.Side1 == SourceUniformVariant.Home,
            "actual no-collision pairing keeps both home variants");
        Check(library.Lineups.Select(5, "play_field_goal", true).RosterIndices[6] == 14 &&
            library.Lineups.Select(5, "play_fake_punt", true).RosterIndices[3] == 3, "actual special-team and duplicate-punt-test semantics");
        Check(library.Lineups.Select(7, "play_kick_return_short", true).RosterIndices[3] == 6, "actual team-specific returner table entry");
        Rejected(() => library.Lineups.Select(30, "play_kickoff_313", false), "returners[30]");
        Check(library.Assets.Count == 180 && library.MissingAssets.Count == 31, "actual exact asset binding inventory including explicit missing entries");
        Console.WriteLine("Actual export SHA256: " + library.ContentSha256);
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2) throw new ArgumentException("Supply synthetic JSON and optionally actual ignored exported JSON.");
            SyntheticChecks(args[0]);
            if (args.Length == 2) ActualChecks(args[1]);
            Console.WriteLine("Roster checks passed: " + assertions + " assertions; " + rejectedMutations + " rejected mutations/invalid inputs.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
