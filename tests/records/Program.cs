using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.SourcePlayer;
using Blitz.Storage;

internal static class Program
{
    private static int assertions;
    private const string SessionId = "11111111111111111111111111111111";
    private static readonly SourceRecordIdentity First = new("SYNAAA", "1234");
    private static readonly SourceRecordIdentity Second = new("SYNBBB", "2345");
    private static readonly SourceRecordIdentity Third = new("SYNCCC", "3456");

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--custom-block-history")
            {
                await CustomBlockHistory();
                Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", customBlockHistory = true, assertions }));
                return 0;
            }
            if (args.Length == 1 && args[0] == "--strict-imports")
            {
                await StrictImports();
                Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", strictImports = true, assertions }));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--legacy-v1")
            {
                byte[] original = File.ReadAllBytes(args[1]);
                string text = Encoding.UTF8.GetString(original);
                Check(JsonNode.Parse(text)["version"].GetValue<int>() == 1, "The retained legacy fixture must really be version1.");
                var legacyStorage = new AtomicStorage { Image = new SourceRecordStorageImage(text) };
                var legacyStore = new SourcePlayerRecordStore(legacyStorage);
                Check((await legacyStore.LoadAsync()).Succeeded && legacyStore.RequiresFormatUpgrade &&
                    legacyStore.Current.WorldRecords.All(world => world == null),
                    "The actual prior v1 file must load with explicitly unobserved worlds.");
                VerifySavedRecord(legacyStore.Current);
                Check(legacyStore.Current.CommittedMatches[SessionId] == 1 && legacyStorage.Image.Primary == text,
                    "Actual prior receipt/data remain intact and load does not rewrite the legacy image.");
                await legacyStore.SaveAudiblesAsync(First, legacyStore.Current.Find(First).Audibles);
                Check(JsonNode.Parse(legacyStorage.Image.Primary)["version"].GetValue<int>() == 2 &&
                    legacyStorage.Image.Backup == text && !legacyStore.RequiresFormatUpgrade,
                    "A real legacy image upgrades only on a successful mutation, retaining its exact valid original backup.");
                Check(original.SequenceEqual(File.ReadAllBytes(args[1])), "Historical v1 fixture evidence must remain byte-identical.");
                Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", legacyVersion = 1, upgradedVersion = 2, assertions }));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--fresh-process")
            {
                var store = new SourcePlayerRecordStore(new SourceRecordFileStorage(args[1]));
                Check((await store.LoadAsync()).Succeeded, "A fresh process must load the actual durable file.");
                VerifySavedRecord(store.Current);
                Check(store.Current.WorldRecords[(int)SourceWorldRecordCategory.Sacks].Value == 2 &&
                    store.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Holder.Equals(First),
                    "Global world records and private holder identity must survive a fresh process.");
                Check(store.Current.CommittedMatches[SessionId] == 1, "The match receipt must survive process exit.");
                Check((await store.CommitMatchAsync(Match(store.Current, 1))).Status == SourceRecordStoreStatus.AlreadyCommitted,
                    "A fresh process must not apply the same completed match twice.");
                Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", freshProcess = true, assertions }));
                return 0;
            }
            if (args.Length != 3) throw new ArgumentException("Expected actual playbook TSV, owned output directory and original-C oracle.");
            byte[] catalog = File.ReadAllBytes(args[0]);
            Check(Convert.ToHexString(SHA256.HashData(catalog)).ToLowerInvariant() ==
                "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3",
                "Conversion tests require the unchanged authenticated original catalog.");
            using var reader = new StringReader(Encoding.UTF8.GetString(catalog));
            SourcePlaybook playbook = SourcePlaybook.LoadConverted(reader);
            Codec(playbook, args[2]);
            Records();
            Worlds();
            await Store();
            await WorldStore();
            await StrictImports();
            await FileStore(Path.Combine(args[1], "file-store"));
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions,
                originalC = "exact cap route helpers and original trig/iasin; hosted float arithmetic, not target execution",
                durableFile = Path.Combine(args[1], "file-store") }));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("SOURCE_RECORD_CHECK_FAILED: " + e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }

    private static SourceCustomPlay Play()
    {
        return SourceCustomPlayCodec.ReplaceRoute(SourceCustomPlayCodec.CreateFromFormation(0, "GRID", "TEST"), 0,
            new[] { new SourceCustomPoint(9, 8, 0), new SourceCustomPoint(9, 18, SourceCustomPointFlags.Turbo),
                new SourceCustomPoint(20, 18, SourceCustomPointFlags.Juke), new SourceCustomPoint(20, 29, SourceCustomPointFlags.Spin),
                new SourceCustomPoint(30, 40, SourceCustomPointFlags.Last) });
    }
    private static void Codec(SourcePlaybook book, string oracle)
    {
        SourceCustomPlay play = Play();
        byte[] compact = SourceCustomPlayCodec.EncodeCompact(play);
        const string literal = "4752494400000000005445535400000000000101090800091204141201141D021E2808" +
            "0000000000000000000000000C0508000000000000000000000000000000000000000000000000" +
            "1C0808000000000000000000000000000000000000000000000000";
        Check(Convert.ToHexString(compact) == literal, "Compact bytes must match the source field-order literal, not a serializer layout.");
        SourceCustomPlayValidation decoded = SourceCustomPlayCodec.DecodeCompact(compact);
        Check(decoded.Valid && !decoded.Repaired &&
            SourceCustomPlayCodec.EncodeCompact(decoded.Play).SequenceEqual(compact), "All compact point/flag/name bytes must round trip.");
        Throws<ArgumentException>(() => SourceCustomPlayCodec.DecodeCompact(compact[..^1]), "Truncation must never become an empty play.");
        byte[] repaired = (byte[])compact.Clone();
        repaired[22] = 7; repaired[20 + 8 * 3] = 25; repaired[21 + 8 * 3] = 35;
        var salvage = SourceCustomPlayCodec.DecodeCompact(repaired);
        Check(salvage.Valid && salvage.Repaired && salvage.Play.Point(0, 0).Flags == 0 &&
            salvage.Play.Point(0, 8).X == 0, "Source validation removes base specials and unreachable nodes explicitly.");
        byte[] invalidMotion = (byte[])compact.Clone(); invalidMotion[18] = 3;
        Check(SourceCustomPlayCodec.DecodeCompact(invalidMotion).Error == SourceCustomPlayValidationError.Motion,
            "Original motion value3 is error1, not a fourth receiver.");
        byte[] overlapping = (byte[])compact.Clone(); overlapping[20 + 9 * 3] = 9; overlapping[21 + 9 * 3] = 8;
        Check(SourceCustomPlayCodec.DecodeCompact(overlapping).Error == SourceCustomPlayValidationError.BasePosition,
            "Motion receiver cannot start on the LOS (source error2 precedes overlap).");
        SourcePlayDefinition converted = SourceCustomPlayCodec.ConvertToPlay(play, 1, 2, book);
        Check(converted.Identity == "custom_p1_s2" && (converted.Flags & SourcePlayFlags.Custom) != 0 &&
            converted.Formation.Slots[0].Offset.X == -12f && converted.Formation.Slots[3].Offset.X == -18f &&
            converted.Formation.Slots[4].Offset.Z == -11f * 7.09f &&
            converted.Formation.Slots[5].Control == (SourceFormationControl.Center | SourceFormationControl.Motion | SourceFormationControl.SecondPlayer),
            "A converted custom play retains exact source positions, mode/control, station and slot identity.");
        Check(ReferenceEquals(converted.Routes[0], book.GetRoute("ds_none")) &&
            ReferenceEquals(converted.Routes[3], book.GetRoute("pr_QB_block")) &&
            converted.Routes[4].Words.Count > 10 && converted.Routes[4].Words.Last() == 5,
            "Custom receiver programs must be actual conversion output, never a static PAT placeholder.");
        CompareC(play, book, oracle);
        var random = new Random(7341);
        for (int sample = 0; sample < 48; sample++)
        {
            SourceCustomPlay candidate = SourceCustomPlayCodec.CreateFromFormation(sample % 5, "SYN", "ROUTE");
            for (int receiver = 0; receiver < 3; receiver++)
            {
                SourceCustomPoint root = candidate.Point(receiver, 0);
                var route = new[] { root, new SourceCustomPoint(2 + random.Next(36), 13 + random.Next(8),
                    (SourceCustomPointFlags)(1 << receiver)), new SourceCustomPoint(2 + random.Next(36), 25 + random.Next(7), 0),
                    new SourceCustomPoint(2 + random.Next(36), 38 + random.Next(5), SourceCustomPointFlags.Last) };
                candidate = SourceCustomPlayCodec.ReplaceRoute(candidate, receiver, route);
            }
            CompareC(candidate, book, oracle);
        }
        SourceCustomPlay straight = SourceCustomPlayCodec.ReplaceRoute(SourceCustomPlayCodec.CreateFromFormation(0, "SYN", "AXIS"), 0,
            new[] { new SourceCustomPoint(9, 8, 0), new SourceCustomPoint(9, 19, SourceCustomPointFlags.Last) });
        Check(SourceCustomPlayCodec.ConvertRoute(straight, 0, book.Directions).SequenceEqual(new[] { 1, 2, 0, 4, 10, 5 }),
            "Original axis/1.1 distance quantization has a literal drone-word vector.");
        SourceCustomPlay right = SourceCustomPlayCodec.ReplaceRoute(straight, 0,
            new[] { new SourceCustomPoint(9, 8, 0), new SourceCustomPoint(20, 8, SourceCustomPointFlags.Last) });
        Check(SourceCustomPlayCodec.ConvertRoute(right, 0, book.Directions).SequenceEqual(new[] { 1, 2, 256, 4, 10, 5 }),
            "Cap angle sign and left-root flip must not accidentally use ptangle's unmodified dx sign.");
        CompareC(straight, book, oracle); CompareC(right, book, oracle);
        Throws<ArgumentException>(() => SourceCustomPlayCodec.ReplaceRoute(play, 0,
            new[] { play.Point(0, 0), new SourceCustomPoint(40, 20, SourceCustomPointFlags.Last) }),
            "Grid control X40 is invalid, not silently clamped.");
        Throws<ArgumentException>(() => SourceCustomPlayCodec.ConvertRoute(
            new SourceCustomPlay("", "", 0, SourceCustomPlayFlags.Unused, new SourceCustomPoint[27]), 0, book.Directions),
            "An unused source slot cannot be converted into a fabricated route.");
    }
    private static void CompareC(SourceCustomPlay play, SourcePlaybook book, string oracle)
    {
        var start = new ProcessStartInfo(oracle) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Convert.ToHexString(SourceCustomPlayCodec.EncodeCompact(play)));
        using Process process = Process.Start(start);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0 && error.Length == 0, "The exact original-C helper must execute successfully.");
        int[][] expected = JsonSerializer.Deserialize<int[][]>(output);
        for (int receiver = 0; receiver < 3; receiver++)
            Check(SourceCustomPlayCodec.ConvertRoute(play, receiver, book.Directions).SequenceEqual(expected[receiver]),
                "Managed route words must equal original C (all turns/specials/turbo budgets/terminal words).");
    }
    private static void Records()
    {
        Throws<ArgumentException>(() => new SourceRecordIdentity("SYN", "123"), "PIN truncation must fail.");
        Throws<ArgumentException>(() => new SourceRecordIdentity("-GUEST", "1234"), "Guest marker cannot become a saved identity.");
        Check(new SourceRecordIdentity("SYN", "1234").Equals(new SourceRecordIdentity("SYN   ", "1234")) &&
            !new SourceRecordIdentity("SYN", "1234").Equals(new SourceRecordIdentity("SYN", "2345")),
            "Identity compares all six padded name and four PIN bytes.");
        Check(new SourceRecordIdentity(" ", "    ").Name == "      ",
            "Source-enterable space characters remain an exact named identity; display whitespace is not guest state.");
        string identityJson = JsonSerializer.Serialize(First);
        Check(!identityJson.Contains("1234") && !identityJson.Contains("Pin", StringComparison.OrdinalIgnoreCase),
            "Public identity serialization must not reveal the arcade PIN.");
        var records = new SourcePlayerRecords().Create(First).Create(Second).Create(Third).SaveCustomPlay(First, 2, Play());
        var otherPlay = SourceCustomPlayCodec.CreateFromFormation(2, "SYN", "OTHER");
        records = records.SaveCustomPlay(Second, 0, otherPlay);
        var config = Configuration(1, 0, 1, 2, 3);
        var session = new SourceSavedRecordSession(config, new[] {
            new SourceStationRecordSelection(0, SourceRecordSelectionState.KnownSaved, record: records.Find(First)),
            new SourceStationRecordSelection(1, SourceRecordSelectionState.KnownSaved, record: records.Find(Second)),
            new SourceStationRecordSelection(2, SourceRecordSelectionState.Guest),
            new SourceStationRecordSelection(3, SourceRecordSelectionState.Unavailable) });
        Check(session.ResolveCustom(0, 1, false).Kind == SourceSavedCustomChoiceKind.Missing &&
            session.ResolveCustom(0, 1, true).SlotIndex == 2 &&
            ReferenceEquals(session.ResolveCustom(1, 0, false).Play, records.Find(Second).CustomPlays[0]) &&
            session.ResolveCustom(1, 0, false).Play.Name2 == "OTHER",
            "Manual NULL, forced first-existing scan and each station's actual ownership must be preserved.");
        Check(session.ResolveCustom(2, 0, true).Kind == SourceSavedCustomChoiceKind.SharedFirstPageDrawRequired &&
            session.ResolveCustom(3, 0, true).Kind == SourceSavedCustomChoiceKind.Unavailable,
            "Guest known-empty and unavailable storage are not interchangeable.");
        Throws<ArgumentException>(() => new SourceRecordAudibles(new short[] { 39, 0, 39 }, new short[3]),
            "Original audible rows cannot mix zero and nonzero IDs.");
        records = records.SaveAudibles(First, new SourceRecordAudibles(new short[] { -39, 39, -39 }, new short[] { 1, 2, 3 }));
        Check(records.Find(First).Audibles.Offense[0] == -39, "Custom audible flip retains signed source ID.");
        records = records.SaveCustomPlay(First, 2, null);
        Check(records.Find(First).Audibles.Offense.All(id => id == 0),
            "Deleting a custom target clears the invalid offense row, as original read validation does.");
        records = records.SaveCustomPlay(First, 2, Play());
        SourcePlayerRecords withoutTeammate = records.Delete(Second);
        var completed = Match(withoutTeammate, 1);
        Throws<ArgumentException>(() => new SourceCompletedRecordMatch(completed.Id, completed.Session,
            completed.FinalMatch, 0, 14, 4, completed.QuarterParticipation),
            "Record score inputs must match the genuine immutable period totals, not caller-made zeros.");
        SourceRecordMatchUpdate updated = withoutTeammate.CommitMatch(completed);
        VerifySavedRecord(updated.Records);
        SourcePlayerRecord teammate = updated.Records.Find(Second);
        Check(teammate.Career.PassingYards == -23 && teammate.Career.Tackles == 7 &&
            teammate.Career.Intercepted == 6 && teammate.Career.LastUsed == 1,
            "Original writer awards same actual team totals to new full-game teammate, with last_used1.");
        Check(updated.Records.Find(Third).Career.GamesWon == 0 && updated.Records.Find(Third).Career.Intercepted == 1,
            "Away losses and interceptions thrown use the independently correct side.");
        var duplicate = updated.Records.CommitMatch(completed);
        Check(duplicate.AlreadyCommitted && ReferenceEquals(duplicate.Records, updated.Records),
            "Re-observing final/confirmation/rematch does not update career twice.");
        Throws<InvalidOperationException>(() => updated.Records.CommitMatch(Match(updated.Records, 3)),
            "An out-of-order match cannot silently consume a journal gap.");
        var rematch = updated.Records.CommitMatch(Match(updated.Records, 2));
        Check(rematch.Records.Find(First).Career.GamesPlayed == 2 && rematch.Records.Find(First).CustomPlays[2] != null,
            "New-match statistics add once without clearing career/custom data.");
        var partial = records.CommitMatch(Match(records, 1, sessionId: "22222222222222222222222222222222",
            participation: new[] { 15, 14, 15, 0 }));
        Check(partial.Records.Find(Second).Career.GamesPlayed == 0, "Partial-quarter participants are ineligible.");
        var human = records.CommitMatch(Match(records, 1, opponent: 9, sessionId: "33333333333333333333333333333333"));
        Check(human.Records.Find(First).Career.TeamsDefeated == 0, "A human tier7 opponent does not award the defeated bit.");
        var cpu = records.CommitMatch(Match(records, 1, opponent: 9, cpu: true,
            sessionId: "44444444444444444444444444444444"));
        Check(cpu.Records.Find(First).Career.TeamsDefeated == 1 << 9, "CPU win awards the source opponent bit even in tier7.");
        var unavailable = Match(records, 1, missingStats: true);
        Throws<InvalidOperationException>(() => records.CommitMatch(unavailable), "Unavailable producer groups must not manufacture zeros.");
        var capacity = new List<SourcePlayerRecord>();
        for (int i = 0; i < 350; i++)
        {
            string name = "SYN" + (char)('A' + i / (26 * 26)) + (char)('A' + i / 26 % 26) + (char)('A' + i % 26);
            capacity.Add(new SourcePlayerRecord(i, new SourceRecordIdentity(name, "0000"),
                new SourceRecordCareer(lastUsed: i == 12 || i == 349 ? 20 : 2)));
        }
        var full = new SourcePlayerRecords(capacity);
        Throws<InvalidOperationException>(() => full.Create(Third), "A full cabinet requires explicit destructive-replacement consent.");
        Check(full.Create(Third, true).Find(Third).RecordNumber == 349,
            "The original >= last_used scan resolves equal maxima to the last record.");
        Check(full.Delete(capacity[12].Identity).Create(Third).Find(Third).RecordNumber == 12,
            "Allocation chooses the first actual free original record slot.");
        var experienced = new SourcePlayerRecords(new[] {
            new SourcePlayerRecord(0, First, new SourceRecordCareer(gamesPlayed: 4, gamesWon: 4,
                worldRecords: 0x22, triviaPoints: 17), diskBank: 4) });
        var fifthWin = experienced.CommitMatch(Match(experienced, 1, cpu: true)).Records;
        Check(fifthWin.Find(First).PlaysAllowed == 3 && SourcePlayerRecords.UsableSlots(fifthWin.Find(First)) == 4 &&
            fifthWin.Find(First).Career.WorldRecords == 0x22 && fifthWin.Find(First).Career.TriviaPoints == 17 &&
            fifthWin.Find(First).DiskBank == 4 &&
            fifthWin.SaveCustomPlay(First, 3, Play()).Find(First).PlaysAllowed == 4,
            "Fifth original win unlocks slot4 only in the editor; match updates retain world/trivia/bank metadata instead of inventing zeros.");
        Throws<ArgumentOutOfRangeException>(() => fifthWin.SaveCustomPlay(First, 4, Play()),
            "The next locked custom slot remains unavailable before its actual win threshold.");
        var bounded = new SourcePlayerRecords(new[] {
            new SourcePlayerRecord(0, First, new SourceRecordCareer(gamesPlayed: 127, gamesWon: 127, winStreak: 127)) });
        Throws<ArgumentOutOfRangeException>(() => bounded.CommitMatch(Match(bounded, 1, cpu: true)),
            "Portable record overflow must fail explicitly, not claim native signed-char wrapping.");
        Check(bounded.Find(First).Career.WinStreak == 127 && bounded.CommittedMatches.Count == 0,
            "A capacity failure cannot partially commit records or exactly-once receipts.");
    }
    private static void Worlds()
    {
        var records = new SourcePlayerRecords().Create(First).Create(Second).Create(Third);
        Check(records.WorldRecords.Count == 6 && records.WorldRecords.All(world => world == null),
            "An explicit new portable cabinet has six unset worlds, not invented zeros/default holders.");
        SourceCompletedRecordMatch match = Match(records, 1);
        SourceRecordMatchUpdate result = records.CommitMatch(match);
        int[] expected = { 5, 3, 11, 6, 21, 7 };
        SourceRecordIdentity[] holders = { Third, First, Third, Third, First, First };
        Check(result.UpdatedWorldRecords == 6 && result.Records.WorldRecords.Select(world => world.Value).SequenceEqual(expected) &&
            result.Records.WorldRecords.Select(world => world.Holder).SequenceEqual(holders),
            "Original world categories use actual own sacks/TDs/human tackles/interceptions MADE/scores/positive margin.");
        Check(result.Records.Find(First).Career.WorldRecords == 0,
            "The opaque career.world_records mask must not be manufactured from global bests.");
        var tie = result.Records.CommitMatch(Match(result.Records, 2));
        Check(tie.UpdatedWorldRecords == 0 && result.Records.WorldRecords.Zip(tie.Records.WorldRecords,
            (old, current) => ReferenceEquals(old, current)).All(equal => equal),
            "Strict greater comparison preserves all original world holders on equal values.");
        var reversed = new SourceSavedRecordSession(match.Session.Configuration, match.Session.Stations.Reverse().ToArray());
        var reversedMatch = new SourceCompletedRecordMatch(match.Id, reversed, match.FinalMatch, 21, 14, 4,
            match.QuarterParticipation);
        Check(records.CommitMatch(reversedMatch).Records.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Holder.Equals(First),
            "Equal teammate candidates retain first PHYSICAL station, never selection array ordering.");
        var partialSession = new SourceSavedRecordSession(match.Session.Configuration, new[] {
            new SourceStationRecordSelection(0, SourceRecordSelectionState.Guest),
            new SourceStationRecordSelection(1, SourceRecordSelectionState.KnownSaved, record: records.Find(Second)),
            new SourceStationRecordSelection(2, SourceRecordSelectionState.Guest) });
        var partial = records.CommitMatch(new SourceCompletedRecordMatch(match.Id, partialSession, match.FinalMatch,
            21, 14, 4, new[] { 15, 14, 15, 0 }));
        Check(partial.UpdatedStations == 0 && partial.UpdatedWorldRecords == 6 &&
            partial.Records.Find(Second).Career.GamesPlayed == 0 &&
            partial.Records.WorldRecords.All(world => world.Holder.Equals(Second)),
            "World helper credits an actually named partial station without falsely granting full-game career credit.");
        Check(!SourceCompletedRecordMatch.IsSourceSaveEligible(3, 21, 14) &&
            !SourceCompletedRecordMatch.IsSourceSaveEligible(4, 14, 14) &&
            !SourceCompletedRecordMatch.IsSourceSaveEligible(6, 14, 14) &&
            SourceCompletedRecordMatch.IsSourceSaveEligible(7, 14, 14),
            "Original nfl game_over guard excludes partial/early tied finals and allows the terminal seventh-period tie.");
        var config = Configuration(1, 0, 2);
        var selected = new SourceSavedRecordSession(config, new[] {
            new SourceStationRecordSelection(0, SourceRecordSelectionState.KnownSaved, record: records.Find(First)),
            new SourceStationRecordSelection(2, SourceRecordSelectionState.KnownSaved, record: records.Find(Third)) });
        Throws<ArgumentException>(() => SourceCompletedRecordMatch.FromFixedParticipation(match.Id, selected,
            Final(config, false, 4, 14, 14), 14, 14, 4), "An early tied terminal view cannot bypass original save eligibility.");
        var terminalTie = records.CommitMatch(SourceCompletedRecordMatch.FromFixedParticipation(match.Id, selected,
            Final(config, false, 7, 14, 14), 14, 14, 7));
        Check(terminalTie.Records.Find(First).Career.GamesPlayed == 1 && terminalTie.Records.Find(First).Career.GamesWon == 0 &&
            terminalTie.Records.WorldRecords[(int)SourceWorldRecordCategory.VictoryMargin] == null,
            "Seventh-period draw counts as a non-win but never creates a zero victory-margin holder.");
        var observedZero = records.CommitMatch(SourceCompletedRecordMatch.FromFixedParticipation(match.Id, selected,
            Final(config, false, 4, 3, 0, true), 3, 0, 4));
        Check(observedZero.Records.WorldRecords[(int)SourceWorldRecordCategory.Sacks].Value == 0 &&
            observedZero.Records.WorldRecords[(int)SourceWorldRecordCategory.Sacks].Holder.Equals(First),
            "A first real observed zero is a measured world entry, distinct from an unset slot.");
        Check(result.Records.Delete(Third).WorldRecords[(int)SourceWorldRecordCategory.Sacks].Holder.Equals(Third),
            "Deleting/recycling a player slot must not erase the independent historical world holder.");
        Check(!JsonSerializer.Serialize(result.Records.WorldRecords[(int)SourceWorldRecordCategory.PointsScored]).Contains("1234"),
            "Public world projections must not expose the holder PIN.");
    }

    private static async Task WorldStore()
    {
        var memory = new AtomicStorage();
        var store = new SourcePlayerRecordStore(memory);
        await store.LoadAsync(); await store.InitializeNewAsync(); await store.CreateAsync(First);
        await store.SaveCustomPlayAsync(First, 2, Play());
        await store.SaveAudiblesAsync(First, new SourceRecordAudibles(new short[] { -39, 39, -39 }, new short[] { 1, 2, 3 }));
        SourcePlayerRecords before = store.Current;
        string previous = memory.Image.Primary;
        SourceCompletedRecordMatch final = Match(before, 1, cpu: true);
        memory.FailBeforeCommit = true;
        Check((await store.CommitMatchAsync(final)).Status == SourceRecordStoreStatus.IoFailure &&
            ReferenceEquals(store.Current, before) && store.Current.WorldRecords.All(world => world == null) &&
            store.Current.CommittedMatches.Count == 0 && memory.Image.Primary == previous,
            "Career, new worlds and receipt must all remain uncommitted after atomic write failure.");
        await store.LoadAsync();
        Check((await store.CommitMatchAsync(final)).Succeeded &&
            (await store.CommitMatchAsync(final)).Status == SourceRecordStoreStatus.AlreadyCommitted,
            "Retry commits career+worlds+receipt once, using the same final identity.");
        SourceRecordExport exported = await store.ExportAsync();
        Check(JsonNode.Parse(exported.Envelope)["version"].GetValue<int>() == 2 &&
            store.Current.WorldRecords.All(world => world != null),
            "Portable version2 explicitly carries all six nullable world slots.");
        var fresh = new SourcePlayerRecordStore(memory);
        await fresh.LoadAsync();
        Check(fresh.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Value == 21 &&
            fresh.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Holder.Equals(First) &&
            fresh.Current.Find(First).CustomPlays[2] != null && fresh.Current.Find(First).Audibles.Offense[0] == -39,
            "World value/private identity, original custom bytes and signed audibles survive serialization/reload together.");
        JsonNode header = JsonNode.Parse(exported.Envelope);
        JsonNode payload = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header["payload"].GetValue<string>())));
        payload.AsObject().Remove("worldRecords");
        header["version"] = 1;
        string legacy = ReplacePayload(header.ToJsonString(), payload.ToJsonString());
        var oldMemory = new AtomicStorage { Image = new SourceRecordStorageImage(legacy) };
        var migrated = new SourcePlayerRecordStore(oldMemory);
        Check((await migrated.LoadAsync()).Succeeded && migrated.RequiresFormatUpgrade &&
            migrated.Current.WorldRecords.All(world => world == null) && oldMemory.Image.Primary == legacy &&
            migrated.Current.Find(First).Career.GamesPlayed == 1 && migrated.Current.Find(First).CustomPlays[2] != null &&
            migrated.Current.CommittedMatches[SessionId] == 1,
            "Validated v1 migration preserves player/custom/audible/receipt data; worlds remain explicitly unavailable and load never writes.");
        SourceRecordExport migratedExport = await migrated.ExportAsync();
        Check(JsonNode.Parse(migratedExport.Envelope)["version"].GetValue<int>() == 2 && migrated.RequiresFormatUpgrade &&
            oldMemory.Image.Primary == legacy,
            "Export may carry the new explicit schema without silently upgrading or rewriting loaded legacy storage.");
        await migrated.SaveAudiblesAsync(First, migrated.Current.Find(First).Audibles);
        Check(!migrated.RequiresFormatUpgrade && JsonNode.Parse(oldMemory.Image.Primary)["version"].GetValue<int>() == 2 &&
            oldMemory.Image.Backup == legacy && migrated.Current.WorldRecords.All(world => world == null),
            "Only a successful subsequent mutation upgrades v1 on disk, preserving the original valid legacy backup.");
        Check((await store.ImportAsync(legacy)).Succeeded &&
            store.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Value == 21,
            "Legacy import cannot silently erase known v2 global data absent from that legacy schema.");
        string missingWorld = ReplacePayload(exported.Envelope, payload.ToJsonString());
        Check((await store.ImportAsync(missingWorld)).Status == SourceRecordStoreStatus.Corrupt,
            "A version2 envelope missing its world field is corrupt, never a v1 fallback or fake zero table.");
        JsonNode invalidPayload = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            JsonNode.Parse(exported.Envelope)["payload"].GetValue<string>())));
        invalidPayload["worldRecords"][0]["category"] = 5;
        Check((await store.ImportAsync(ReplacePayload(exported.Envelope, invalidPayload.ToJsonString()))).Status == SourceRecordStoreStatus.Corrupt,
            "Wrong global category identity is rejected without changing valid player/world data.");
        await store.SaveCustomPlayAsync(First, 0, SourceCustomPlayCodec.CreateFromFormation(1, "SYN", "WORLD"));
        Check(store.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Value == 21,
            "Saving custom plays after a final must retain the independent global worlds.");
        memory.Image = new SourceRecordStorageImage("well-formed-corrupt-world-save", memory.Image.Backup, memory.Image.Recovery);
        await store.LoadAsync();
        Check((await store.RecoverAsync()).Status == SourceRecordStoreStatus.Recovered &&
            store.Current.WorldRecords[(int)SourceWorldRecordCategory.Sacks].Value == 2 &&
            store.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Holder.Equals(First),
            "Explicit recovery restores actual global values/holders with the same previous valid player/receipt transaction.");
    }

    private static async Task Store()
    {
        SourceRecordStorageImage bridge = SourceRecordStorageImage.DecodeBrowserJson(
            new SourceRecordStorageImage("opaque-primary", "opaque-backup", "opaque-quarantine").EncodeBrowserJson());
        Check(bridge.Primary == "opaque-primary" && bridge.Backup == "opaque-backup" && bridge.Recovery == "opaque-quarantine",
            "The actual browser JSON bridge must preserve all three exact opaque values.");
        Throws<SourceRecordStorageException>(() => SourceRecordStorageImage.DecodeBrowserJson("{\"primary\":null}"),
            "Missing browser IO fields are a protocol failure, never an empty/not-found store.");
        var memory = new AtomicStorage();
        var store = new SourcePlayerRecordStore(memory);
        Check((await store.LoadAsync()).Status == SourceRecordStoreStatus.NotFound &&
            !store.IsWritable && store.Current == null, "Missing store is NotFound, not a fabricated loaded empty cabinet.");
        Check((await store.InitializeNewAsync()).Succeeded && (await store.CreateAsync(First)).Succeeded,
            "Only deliberate initialization/creation persist new data.");
        var foreignFailure = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromException<SourceRecordStorageImage>(new InvalidOperationException("synthetic adapter details")),
            (_, _) => Task.FromResult(false)));
        Check((await foreignFailure.LoadAsync()).Status == SourceRecordStoreStatus.IoFailure &&
            !JsonSerializer.Serialize(foreignFailure.Notification).Contains("synthetic adapter details"),
            "The actual delegate boundary sanitizes unexpected async browser exceptions into typed IO failures.");
        var foreignWriteFailure = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => memory.ReadAsync(),
            (_, _) => Task.FromException<bool>(new InvalidOperationException("synthetic write details"))));
        await foreignWriteFailure.LoadAsync();
        Check((await foreignWriteFailure.SaveCustomPlayAsync(First, 2, Play())).Status == SourceRecordStoreStatus.IoFailure &&
            foreignWriteFailure.Current.Find(First).CustomPlays[2] == null,
            "The actual async browser write adapter must retain old state and sanitize a foreign exception.");
        memory.HoldNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SourceRecordStoreResult> pending = store.SaveCustomPlayAsync(First, 2, Play());
        await memory.WriteStarted.Task;
        Check(!pending.IsCompleted && store.Current.Find(First).CustomPlays[2] == null,
            "An asynchronous pending write must not install speculative state.");
        memory.HoldNext.SetResult();
        Check((await pending).Succeeded, "Committed async write installs the real custom definition.");
        Check((await store.SaveAudiblesAsync(First, new SourceRecordAudibles(new short[] { -39, 39, -39 },
            new short[] { 1, 2, 3 }))).Succeeded, "Real custom audible IDs persist.");
        var reloaded = new SourcePlayerRecordStore(memory);
        Check((await reloaded.LoadAsync()).Succeeded && reloaded.Current.Find(First).Audibles.Offense[0] == -39 &&
            SourceCustomPlayCodec.EncodeCompact(reloaded.Current.Find(First).CustomPlays[2]).SequenceEqual(SourceCustomPlayCodec.EncodeCompact(Play())),
            "A new store instance restores real compact custom routes and signed audibles.");
        SourceRecordExport exported = await store.ExportAsync();
        JsonNode metadataDocument = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            JsonNode.Parse(exported.Envelope)["payload"].GetValue<string>())));
        metadataDocument["records"][0]["career"]["world_records"] = 0x22;
        metadataDocument["records"][0]["career"]["trivia_pts"] = 17;
        metadataDocument["records"][0]["diskBank"] = 4;
        var metadataMemory = new AtomicStorage();
        var metadata = new SourcePlayerRecordStore(metadataMemory);
        await metadata.LoadAsync();
        Check((await metadata.ImportAsync(ReplacePayload(exported.Envelope, metadataDocument.ToJsonString()))).Succeeded,
            "Portable logical import accepts explicitly supplied synthetic original metadata, not a native ABI overlay.");
        await metadata.CommitMatchAsync(Match(metadata.Current, 1, cpu: true));
        var metadataReload = new SourcePlayerRecordStore(metadataMemory);
        await metadataReload.LoadAsync();
        Check(metadataReload.Current.Find(First).Career.WorldRecords == 0x22 &&
            metadataReload.Current.Find(First).Career.TriviaPoints == 17 && metadataReload.Current.Find(First).DiskBank == 4 &&
            metadataReload.Current.Find(First).Career.GamesPlayed == 1,
            "Serialization, real logical match updates and reload must preserve nonzero opaque source metadata.");
        var noBackupMemory = new AtomicStorage { Image = memory.Image };
        var noBackup = new SourcePlayerRecordStore(noBackupMemory);
        await noBackup.LoadAsync();
        SourcePlayerRecords noBackupRetained = noBackup.Current;
        noBackupMemory.Image = new SourceRecordStorageImage("truncated", null);
        Check((await noBackup.RecoverAsync()).Status == SourceRecordStoreStatus.Corrupt &&
            ReferenceEquals(noBackup.Current, noBackupRetained) && !noBackup.IsWritable &&
            (await noBackup.CreateAsync(Second)).Status == SourceRecordStoreStatus.Corrupt,
            "Direct recovery with no valid backup must revoke writes even without a preceding failed load.");
        const string orphanedRecovery = "owned-synthetic-quarantine-with-missing-primary";
        var recoveryOnlyMemory = new AtomicStorage {
            Image = new SourceRecordStorageImage(null, null, orphanedRecovery) };
        var recoveryOnly = new SourcePlayerRecordStore(recoveryOnlyMemory);
        SourceRecordStoreResult recoveryOnlyLoad = await recoveryOnly.LoadAsync();
        Check(recoveryOnlyLoad.Status == SourceRecordStoreStatus.Corrupt &&
            recoveryOnlyLoad.Code == "SOURCE_SAVE_PRIMARY_MISSING" &&
            recoveryOnly.Current == null && !recoveryOnly.IsWritable,
            "Recovery-only storage is evidence of a lost primary, never an uninitialized empty cabinet.");
        Check((await recoveryOnly.InitializeNewAsync()).Status == SourceRecordStoreStatus.InvalidData &&
            (await recoveryOnly.ImportAsync(exported.Envelope)).Status == SourceRecordStoreStatus.Corrupt &&
            recoveryOnlyMemory.Image.Primary == null && recoveryOnlyMemory.Image.Backup == null &&
            recoveryOnlyMemory.Image.Recovery == orphanedRecovery,
            "A recovery-only image cannot initialize or replace missing career data without explicit failed-store replacement consent.");
        Check((await recoveryOnly.RecoverAsync()).Status == SourceRecordStoreStatus.Corrupt &&
            recoveryOnly.Notification.Code == "SOURCE_SAVE_NO_VALID_BACKUP" &&
            recoveryOnlyMemory.Image.Recovery == orphanedRecovery && !recoveryOnly.IsWritable,
            "Quarantined corruption is not an authenticated backup and must never be decoded as one.");
        Check((await recoveryOnly.ImportAsync(exported.Envelope, replaceFailedStore: true)).Succeeded &&
            recoveryOnlyMemory.Image.Recovery == orphanedRecovery &&
            recoveryOnly.Current.Find(First).Audibles.Offense[0] == -39 &&
            SourceCustomPlayCodec.EncodeCompact(recoveryOnly.Current.Find(First).CustomPlays[2])
                .SequenceEqual(SourceCustomPlayCodec.EncodeCompact(Play())),
            "Explicit recovery-only replacement restores actual imported records and retains the exact earlier quarantine.");
        SourcePlayerRecords retainedRecoveryOnly = recoveryOnly.Current;
        long retainedRecoveryOnlyGeneration = recoveryOnly.Generation;
        recoveryOnlyMemory.Image = new SourceRecordStorageImage(null, null, orphanedRecovery);
        Check((await recoveryOnly.LoadAsync()).Status == SourceRecordStoreStatus.Corrupt &&
            ReferenceEquals(recoveryOnly.Current, retainedRecoveryOnly) &&
            recoveryOnly.Generation == retainedRecoveryOnlyGeneration && !recoveryOnly.IsWritable,
            "Losing primary and backup after a valid load retains the last valid record set without reopening writes.");
        string damaged = exported.Envelope[..^11];
        memory.Image = new SourceRecordStorageImage(damaged, memory.Image.Backup);
        SourcePlayerRecords retained = store.Current;
        Check((await store.LoadAsync()).Status == SourceRecordStoreStatus.Corrupt &&
            ReferenceEquals(store.Current, retained) && !store.IsWritable,
            "Corrupt/truncated load preserves the last valid in-memory state and disables writes.");
        Check((await store.CreateAsync(Second)).Status == SourceRecordStoreStatus.Corrupt &&
            (await store.InitializeNewAsync()).Status == SourceRecordStoreStatus.InvalidData && memory.Image.Primary == damaged,
            "Corruption cannot silently initialize/overwrite user data.");
        Check((await store.RecoverAsync()).Status == SourceRecordStoreStatus.Recovered && memory.Image.Recovery == damaged &&
            store.Current.Find(First).CustomPlays[2] != null, "Explicit backup recovery preserves corrupt bytes and real definitions.");
        memory.Image = new SourceRecordStorageImage("second-truncated", memory.Image.Backup, memory.Image.Recovery);
        await store.LoadAsync();
        Check((await store.RecoverAsync()).Status == SourceRecordStoreStatus.Recovered,
            "A second corruption must not permanently block recovery because an older quarantine already exists.");
        string[] archived = JsonSerializer.Deserialize<string[]>(memory.Image.Recovery);
        Check(archived.SequenceEqual(new[] { damaged, "second-truncated" }),
            "Repeated recovery preserves both exact opaque corrupt primaries without overwriting earlier evidence.");
        int futureVersion = SourcePlayerRecordStore.Version + 1;
        var futureMemory = new AtomicStorage {
            Image = new SourceRecordStorageImage("{\"schema\":\"nflblitz-portable-local-records\",\"version\":" +
                futureVersion + "}", exported.Envelope) };
        var future = new SourcePlayerRecordStore(futureMemory);
        Check((await future.LoadAsync()).Status == SourceRecordStoreStatus.UnsupportedVersion &&
            (await future.RecoverAsync()).Status == SourceRecordStoreStatus.UnsupportedVersion &&
            futureMemory.Image.Recovery == null, "Unknown versions remain untouched without explicit replacement consent.");
        Check((await future.RecoverAsync(true)).Status == SourceRecordStoreStatus.Recovered &&
            futureMemory.Image.Recovery.Contains("\"version\":" + futureVersion), "Explicit version rollback retains the unknown original bytes.");
        string missingField = RemoveCareerField(exported.Envelope, "pass_attempts");
        retained = store.Current;
        Check((await store.ImportAsync(missingField)).Status == SourceRecordStoreStatus.Corrupt &&
            ReferenceEquals(retained, store.Current), "Even integrity-valid imports must reject missing source counters, not fill zero.");
        Check((await store.ImportAsync(ReplacePayload(exported.Envelope, "null"))).Status == SourceRecordStoreStatus.Corrupt &&
            ReferenceEquals(retained, store.Current),
            "An integrity-valid null payload is corrupt, not a null dereference or fabricated empty store.");
        string alteredGeneration = exported.Envelope.Replace("\"generation\":4", "\"generation\":5", StringComparison.Ordinal);
        Check((await store.ImportAsync(alteredGeneration)).Status == SourceRecordStoreStatus.Corrupt,
            "Integrity covers generation/header metadata as well as payload.");
        var a = new SourcePlayerRecordStore(memory); var b = new SourcePlayerRecordStore(memory);
        await a.LoadAsync(); await b.LoadAsync();
        Check((await a.CreateAsync(Second)).Succeeded && (await b.CreateAsync(Third)).Status == SourceRecordStoreStatus.Conflict &&
            b.Current.Find(Second) == null && b.Current.Find(Third) == null,
            "Two stale writers cannot overwrite each other's valid records.");
        await b.LoadAsync();
        Check((await b.CreateAsync(Third)).Succeeded && b.Current.Find(Second) != null,
            "Deliberate reload/retry retains the competing writer's records.");
        memory.FailBeforeCommit = true;
        retained = b.Current; string primary = memory.Image.Primary;
        Check((await b.SaveCustomPlayAsync(Third, 0, Play())).Status == SourceRecordStoreStatus.IoFailure &&
            ReferenceEquals(b.Current, retained) && memory.Image.Primary == primary && !b.IsWritable,
            "Failed atomic write cannot lose/change the previous valid state.");
        await b.LoadAsync();
        memory.FailAfterCommit = true;
        Check((await b.CommitMatchAsync(Match(b.Current, 1))).Status == SourceRecordStoreStatus.IoFailure,
            "Lost commit acknowledgement is an explicit IO failure.");
        await b.LoadAsync();
        Check((await b.CommitMatchAsync(Match(b.Current, 1))).Status == SourceRecordStoreStatus.AlreadyCommitted &&
            b.Current.Find(First).Career.GamesPlayed == 1, "Receipt persisted with the stats prevents double update after lost acknowledgement.");
        Check((await b.ImportAsync(exported.Envelope)).Succeeded &&
            (await b.CommitMatchAsync(Match(b.Current, 1))).Status == SourceRecordStoreStatus.AlreadyCommitted,
            "Importing an older logical export does not rewind the exactly-once session journal.");
        await b.CreateAsync(Third);
        Check((await b.DeleteAsync(Third)).Succeeded && b.Current.Find(Third) == null, "Delete persists only the selected logical identity.");
        string notice = JsonSerializer.Serialize(b.Notification);
        Check(!notice.Contains("1234") && !notice.Contains("SYNAAA"), "Storage notifications must never leak identity or PIN content.");
    }
    private static async Task StrictImports()
    {
        var storage = new AtomicStorage();
        var store = new SourcePlayerRecordStore(storage);
        await store.LoadAsync(); await store.InitializeNewAsync(); await store.CreateAsync(First);
        await store.SaveCustomPlayAsync(First, 2, Play());
        await store.SaveAudiblesAsync(First, new SourceRecordAudibles(new short[] { -39, 39, -39 }, new short[] { 1, 2, 3 }));
        Check((await store.CommitMatchAsync(Match(store.Current, 1, cpu: true))).Succeeded,
            "Strict-import fixtures require a genuinely committed synthetic career and receipt.");
        string currentEnvelope = (await store.ExportAsync()).Envelope;
        JsonNode legacyHeader = JsonNode.Parse(currentEnvelope);
        JsonNode legacyBody = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            legacyHeader["payload"].GetValue<string>())));
        legacyBody.AsObject().Remove("worldRecords");
        legacyHeader["version"] = 1;
        string legacyEnvelope = ReplacePayload(legacyHeader.ToJsonString(), legacyBody.ToJsonString());
        foreach (string envelope in new[] { legacyEnvelope, currentEnvelope })
        {
            JsonNode Body() => JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
                JsonNode.Parse(envelope)["payload"].GetValue<string>())));
            SourcePlayerRecords retained = store.Current;
            string primary = storage.Image.Primary;
            JsonNode aliased = Body();
            JsonObject career = aliased["records"][0]["career"].AsObject();
            int games = career["games_played"].GetValue<int>();
            career.Remove("games_played");
            career["games_played\0"] = games;
            Check((await store.ImportAsync(ReplacePayload(envelope, aliased.ToJsonString()))).Status == SourceRecordStoreStatus.Corrupt &&
                ReferenceEquals(store.Current, retained) && storage.Image.Primary == primary,
                "Integrity-valid culture aliases cannot replace exact source career keys.");
            JsonNode additionalAlias = Body();
            additionalAlias["records"][0]["career"]["games_played\0"] = games + 17;
            Check((await store.ImportAsync(ReplacePayload(envelope, additionalAlias.ToJsonString()))).Status == SourceRecordStoreStatus.Corrupt &&
                ReferenceEquals(store.Current, retained) && storage.Image.Primary == primary,
                "Integrity-valid extra career aliases must not collapse into valid fields.");
            JsonNode receiptAlias = Body();
            receiptAlias["committedMatches"].AsObject().Remove(SessionId);
            receiptAlias["committedMatches"][" " + SessionId + " "] = 1;
            Check((await store.ImportAsync(ReplacePayload(envelope, receiptAlias.ToJsonString()))).Status == SourceRecordStoreStatus.Corrupt &&
                ReferenceEquals(store.Current, retained) && storage.Image.Primary == primary,
                "A whitespace-aliased receipt must not import as an unmatched ID that permits double career credit.");
            const string canonical = "abcdefabcdefabcdefabcdefabcdefab";
            JsonNode uppercaseReceipt = Body();
            uppercaseReceipt["committedMatches"].AsObject().Remove(SessionId);
            uppercaseReceipt["committedMatches"][canonical.ToUpperInvariant()] = 1;
            var positive = new SourcePlayerRecordStore(new AtomicStorage());
            await positive.LoadAsync();
            Check((await positive.ImportAsync(ReplacePayload(envelope, uppercaseReceipt.ToJsonString()))).Succeeded &&
                positive.Current.CommittedMatches.ContainsKey(canonical) &&
                (await positive.CommitMatchAsync(Match(positive.Current, 1, cpu: true, sessionId: canonical)))
                    .Status == SourceRecordStoreStatus.AlreadyCommitted &&
                positive.Current.Find(First).Career.GamesPlayed == 1 &&
                positive.Current.Find(First).CustomPlays[2] != null &&
                positive.Current.Find(First).Audibles.Offense[0] == -39,
                "Canonical uppercase N-format receipts still normalize, round trip and suppress the same real final without losing saved configuration.");
        }
    }
    private static async Task CustomBlockHistory()
    {
        SourcePlayerRecordStore History(AtomicStorage storage) =>
            new(storage, SourceRecordSavePolicy.CustomBlockHistoryV3);
        JsonNode Body(string envelope) => JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            JsonNode.Parse(envelope)["payload"].GetValue<string>())));
        var storage = new AtomicStorage();
        var store = History(storage);
        await store.LoadAsync(); await store.InitializeNewAsync();
        Check((await store.CreateAsync(First)).Succeeded &&
            store.Current.Find(First).CustomBlockState == SourceCustomBlockState.NeverAllocated &&
            store.Current.Find(First).DiskBank == -1 && store.Current.Find(First).CustomPlays.All(play => play == null),
            "An explicitly new v3 identity starts NeverAllocated without an invented native disk block.");
        await store.SaveAudiblesAsync(First, new SourceRecordAudibles(new short[] { 1, 2, 3 }, new short[] { 1, 8, 7 }));
        Check(store.Current.Find(First).CustomBlockState == SourceCustomBlockState.NeverAllocated,
            "An independent audible save must not pretend that CAP has acquired a block.");
        SourcePlayerRecords before = store.Current;
        string primary = storage.Image.Primary;
        storage.HoldNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SourceRecordStoreResult> pending = store.CommitCustomBlockAsync(First);
        await storage.WriteStarted.Task;
        Check(!pending.IsCompleted && ReferenceEquals(store.Current, before) && storage.Image.Primary == primary &&
            store.Current.Find(First).CustomBlockState == SourceCustomBlockState.NeverAllocated,
            "An empty-block CAP write cannot advance history before the actual transaction acknowledgement.");
        storage.HoldNext.SetResult();
        Check((await pending).Succeeded && store.Current.Find(First).CustomBlockState == SourceCustomBlockState.Allocated &&
            store.Current.Find(First).CustomPlays.All(play => play == null) && storage.Image.Backup == primary,
            "Successful CAP empty-block commit records acquisition while retaining all nine empty slot identities and its backup.");
        var fresh = History(storage);
        Check((await fresh.LoadAsync()).Succeeded && fresh.Current.Find(First).CustomBlockState == SourceCustomBlockState.Allocated &&
            fresh.Current.Find(First).CustomPlays.All(play => play == null) && !fresh.RequiresFormatUpgrade,
            "Allocated-empty is a durable logical state, distinct from never allocated after a fresh store load.");
        Check((await fresh.CommitMatchAsync(Match(fresh.Current, 1, cpu: true))).Succeeded &&
            fresh.Current.Find(First).CustomBlockState == SourceCustomBlockState.Allocated &&
            fresh.Current.Find(First).Career.GamesPlayed == 1 &&
            fresh.Current.WorldRecords[(int)SourceWorldRecordCategory.PointsScored].Value == 21,
            "A career/worlds/receipt rewrite must preserve an allocated-empty block instead of reconstructing it from Exists.");
        await fresh.CreateAsync(Second);
        await fresh.SaveCustomPlayAsync(Second, 2, Play());
        await fresh.SaveAudiblesAsync(Second, new SourceRecordAudibles(new short[] { -39, 39, -39 }, new short[] { 1, 8, 7 }));
        Check(fresh.Current.Find(Second).CustomBlockState == SourceCustomBlockState.Allocated,
            "A successfully saved actual custom definition acquires the logical block.");
        await fresh.SaveCustomPlayAsync(Second, 2, null);
        await fresh.SaveAudiblesAsync(Second, SourceRecordAudibles.Defaults);
        Check(fresh.Current.Find(Second).CustomBlockState == SourceCustomBlockState.Allocated &&
            fresh.Current.Find(Second).CustomPlays.All(play => play == null) &&
            fresh.Current.Find(Second).Audibles.Offense.All(id => id == 0),
            "Portable delete-last and audible-row repair retain acquisition; this action does not claim a native free-bank operation.");
        await fresh.CreateAsync(Third);
        var bridge = SourceRecordStorageImage.DecodeBrowserJson(storage.Image.EncodeBrowserJson());
        var bridgeReload = History(new AtomicStorage { Image = bridge });
        Check((await bridgeReload.LoadAsync()).Succeeded &&
            bridgeReload.Current.Find(First).CustomBlockState == SourceCustomBlockState.Allocated &&
            bridgeReload.Current.Find(Second).CustomBlockState == SourceCustomBlockState.Allocated &&
            bridgeReload.Current.Find(Third).CustomBlockState == SourceCustomBlockState.NeverAllocated &&
            bridgeReload.Current.CommittedMatches[SessionId] == 1,
            "The production storage-image JSON bridge round trips both explicit history states alongside career receipts.");
        string v3 = (await bridgeReload.ExportAsync()).Envelope;
        JsonNode v3Body = Body(v3);
        Check(JsonNode.Parse(v3)["version"].GetValue<int>() == 3 &&
            v3Body["records"][0]["customBlockState"].GetValue<int>() == 2 &&
            v3Body["records"][2]["customBlockState"].GetValue<int>() == 1,
            "The opted-in envelope carries version3 and a mandatory integer history field on every actual record.");
        var legacyReader = new SourcePlayerRecordStore(new AtomicStorage { Image = bridge });
        Check((await legacyReader.LoadAsync()).Status == SourceRecordStoreStatus.UnsupportedVersion &&
            legacyReader.Current == null && !legacyReader.IsWritable,
            "A legacy/default store must not implicitly admit or reinterpret version3.");

        foreach (string value in new[] { null, "null", "-1", "3", "2147483648", "1.5", "\"2\"", "true" })
        {
            JsonNode body = Body(v3);
            if (value == null) body["records"][0].AsObject().Remove("customBlockState");
            else body["records"][0]["customBlockState"] = JsonNode.Parse(value);
            string malformed = ReplacePayload(v3, body.ToJsonString());
            before = bridgeReload.Current;
            Check((await bridgeReload.ImportAsync(malformed)).Status == SourceRecordStoreStatus.Corrupt &&
                ReferenceEquals(bridgeReload.Current, before),
                "V3 missing/null/noninteger/unknown history is rejected without installing speculative imported records.");
            var malformedLoad = History(new AtomicStorage { Image = new SourceRecordStorageImage(malformed) });
            Check((await malformedLoad.LoadAsync()).Status == SourceRecordStoreStatus.Corrupt && !malformedLoad.IsWritable,
                "The same malformed history must fail the primary-load caller, not only import.");
        }
        foreach (string alias in new[] { "CustomBlockState", "customBlockState\0" })
        {
            JsonNode body = Body(v3);
            body["records"][0][alias] = body["records"][0]["customBlockState"].DeepClone();
            body["records"][0].AsObject().Remove("customBlockState");
            Check((await bridgeReload.ImportAsync(ReplacePayload(v3, body.ToJsonString()))).Status == SourceRecordStoreStatus.Corrupt,
                "A history-field case/cultural alias is not a replacement for the required ordinal JSON name.");
        }
        JsonNode contradictory = Body(v3);
        contradictory["records"][2]["customPlays"][0] = Convert.ToBase64String(SourceCustomPlayCodec.EncodeCompact(Play()));
        Check((await bridgeReload.ImportAsync(ReplacePayload(v3, contradictory.ToJsonString()))).Status == SourceRecordStoreStatus.Corrupt,
            "NeverAllocated with an actual Exists slot is contradictory, not an automatic state repair.");

        var failedStorage = new AtomicStorage();
        var failed = History(failedStorage);
        await failed.LoadAsync(); await failed.InitializeNewAsync(); await failed.CreateAsync(First);
        before = failed.Current; primary = failedStorage.Image.Primary;
        failedStorage.FailBeforeCommit = true;
        Check((await failed.CommitCustomBlockAsync(First)).Status == SourceRecordStoreStatus.IoFailure &&
            ReferenceEquals(failed.Current, before) && failedStorage.Image.Primary == primary &&
            failed.Current.Find(First).CustomBlockState == SourceCustomBlockState.NeverAllocated,
            "A rejected CAP block write leaves both the durable record and acknowledged history unchanged.");
        await failed.LoadAsync();
        var competitor = History(failedStorage);
        await competitor.LoadAsync(); await competitor.CreateAsync(Second);
        Check((await failed.CommitCustomBlockAsync(First)).Status == SourceRecordStoreStatus.Conflict &&
            failed.Current.Find(First).CustomBlockState == SourceCustomBlockState.NeverAllocated &&
            failed.Current.Find(Second) == null,
            "A stale CAP writer reports CAS conflict without acquiring a block or copying a competing generation.");
        await failed.LoadAsync();
        Check((await failed.CommitCustomBlockAsync(First)).Succeeded && failed.Current.Find(Second) != null,
            "Explicit reload and CAP retry retain the competing record and durably acquire the selected block.");
        await failed.CreateAsync(Third);
        before = failed.Current;
        failedStorage.FailAfterCommit = true;
        Check((await failed.CommitCustomBlockAsync(Third)).Status == SourceRecordStoreStatus.IoFailure &&
            ReferenceEquals(failed.Current, before) &&
            failed.Current.Find(Third).CustomBlockState == SourceCustomBlockState.NeverAllocated,
            "A lost acknowledgement surfaces failure and never claims speculative in-memory acquisition.");
        var acknowledged = History(failedStorage);
        Check((await acknowledged.LoadAsync()).Succeeded &&
            acknowledged.Current.Find(Third).CustomBlockState == SourceCustomBlockState.Allocated,
            "An explicit fresh reload, not a fallback, resolves a lost acknowledgement from actual committed bytes.");
        string damaged = failedStorage.Image.Primary[..^11];
        failedStorage.Image = new SourceRecordStorageImage(damaged, failedStorage.Image.Backup);
        await acknowledged.LoadAsync();
        Check((await acknowledged.RecoverAsync()).Succeeded && failedStorage.Image.Recovery == damaged &&
            acknowledged.Current.Find(First).CustomBlockState == SourceCustomBlockState.Allocated &&
            acknowledged.Current.Find(Third).CustomBlockState == SourceCustomBlockState.NeverAllocated,
            "Explicit backup recovery restores the exact earlier history generation and quarantines the corrupt primary.");

        var oldStorage = new AtomicStorage();
        var old = new SourcePlayerRecordStore(oldStorage);
        await old.LoadAsync(); await old.InitializeNewAsync(); await old.CreateAsync(First); await old.CreateAsync(Second);
        await old.CreateAsync(Third); await old.SaveCustomPlayAsync(Second, 2, Play());
        await old.CommitMatchAsync(Match(old.Current, 1, cpu: true));
        string oldEnvelope = (await old.ExportAsync()).Envelope;
        JsonNode oldBody = Body(oldEnvelope);
        Check(JsonNode.Parse(oldEnvelope)["version"].GetValue<int>() == 2 &&
            oldBody["records"].AsArray().All(row => !row.AsObject().ContainsKey("customBlockState")) &&
            oldEnvelope == oldStorage.Image.Primary,
            "The legacy policy still emits the exact version2 field set and export is byte-identical to its committed primary.");
        primary = oldStorage.Image.Primary; before = old.Current;
        Check((await old.CommitCustomBlockAsync(First)).Status == SourceRecordStoreStatus.InvalidData &&
            old.Notification.Code == "SOURCE_CUSTOM_BLOCK_HISTORY_POLICY_REQUIRED" &&
            oldStorage.Image.Primary == primary && ReferenceEquals(old.Current, before),
            "The new CAP acquisition operation cannot silently promote a legacy/default store.");
        oldBody["records"][0]["diskBank"] = 4;
        oldEnvelope = ReplacePayload(oldEnvelope, oldBody.ToJsonString());
        foreach (int version in new[] { 1, 2 })
        {
            JsonNode header = JsonNode.Parse(oldEnvelope), body = Body(oldEnvelope);
            header["version"] = version;
            if (version == 1) body.AsObject().Remove("worldRecords");
            string legacy = ReplacePayload(header.ToJsonString(), body.ToJsonString());
            var migrationStorage = new AtomicStorage { Image = new SourceRecordStorageImage(legacy) };
            var migration = History(migrationStorage);
            Check((await migration.LoadAsync()).Succeeded && migration.RequiresFormatUpgrade &&
                migrationStorage.Image.Primary == legacy && migrationStorage.Image.Backup == null &&
                migration.Current.Find(First).CustomBlockState == SourceCustomBlockState.LegacyUnknown &&
                migration.Current.Find(First).Career.GamesPlayed == 1 && migration.Current.Find(First).DiskBank == 4 &&
                migration.Current.Find(Second).CustomBlockState == SourceCustomBlockState.Allocated &&
                migration.Current.Find(Third).CustomBlockState == SourceCustomBlockState.LegacyUnknown,
                "Opt-in v1/v2 read migration uses actual validated Exists only: all-empty remains Unknown regardless of bank or games, without writing.");
            await migration.SaveAudiblesAsync(First, migration.Current.Find(First).Audibles);
            Check(!migration.RequiresFormatUpgrade && migrationStorage.Image.Backup == legacy &&
                JsonNode.Parse(migrationStorage.Image.Primary)["version"].GetValue<int>() == 3 &&
                migration.Current.Find(First).CustomBlockState == SourceCustomBlockState.LegacyUnknown &&
                migration.Current.CommittedMatches[SessionId] == 1 &&
                migration.Current.Find(Second).CustomPlays[2]?.Exists == true,
                "Only a successful explicit mutation writes v3, retaining legacy bytes, unresolved history, real slots and the existing receipt.");
            Check((await migration.CommitCustomBlockAsync(First)).Succeeded &&
                migration.Current.Find(First).CustomBlockState == SourceCustomBlockState.Allocated,
                "An explicit successful CAP exit can resolve legacy-empty Unknown without guessing a native bank.");
            body["records"][0]["customBlockState"] = 0;
            var wrongLegacySchema = new SourcePlayerRecordStore(new AtomicStorage {
                Image = new SourceRecordStorageImage(ReplacePayload(header.ToJsonString(), body.ToJsonString())) });
            Check((await wrongLegacySchema.LoadAsync()).Status == SourceRecordStoreStatus.Corrupt,
                "V1/v2 cannot smuggle a new history field under an unchanged schema version.");
        }
        string notification = JsonSerializer.Serialize(acknowledged.Notification);
        Check(!notification.Contains("SYNAAA") && !notification.Contains("1234"),
            "History failure and recovery notifications remain free of private identity/PIN data.");
    }

    private static async Task FileStore(string directory)
    {
        Directory.CreateDirectory(directory);
        var store = new SourcePlayerRecordStore(new SourceRecordFileStorage(directory));
        Check((await store.LoadAsync()).Status == SourceRecordStoreStatus.NotFound, "Fresh owned durable path must actually be missing.");
        await store.InitializeNewAsync(); await store.CreateAsync(First); await store.SaveCustomPlayAsync(First, 2, Play());
        Check((await store.CommitMatchAsync(Match(store.Current, 1, cpu: true))).Succeeded,
            "Actual disk adapter commits the logical record and receipt.");
        byte[] before = File.ReadAllBytes(Path.Combine(directory, "records.json"));
        Directory.CreateDirectory(Path.Combine(directory, "records.json.pending"));
        Check((await store.CreateAsync(Second)).Status == SourceRecordStoreStatus.IoFailure &&
            before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "records.json"))),
            "An actual staged-file write failure must preserve the previous committed file bytes.");
        Directory.Delete(Path.Combine(directory, "records.json.pending"));
        var fresh = new SourcePlayerRecordStore(new SourceRecordFileStorage(directory));
        Check((await fresh.LoadAsync()).Succeeded, "A different adapter instance can recover the committed file.");
        VerifySavedRecord(fresh.Current);
        string corruptDirectory = Path.Combine(directory, "binary-recovery");
        var binaryBackend = new SourceRecordFileStorage(corruptDirectory);
        var binary = new SourcePlayerRecordStore(binaryBackend);
        await binary.LoadAsync(); await binary.InitializeNewAsync(); await binary.CreateAsync(First);
        await binary.SaveCustomPlayAsync(First, 2, Play());
        byte[] corrupt = { 0xff, 0x00, 0xfe, 0x81 };
        File.WriteAllBytes(Path.Combine(corruptDirectory, "records.json"), corrupt);
        Check((await binary.LoadAsync()).Status == SourceRecordStoreStatus.Corrupt &&
            (await binary.RecoverAsync()).Status == SourceRecordStoreStatus.Recovered,
            "Even non-UTF8 primary corruption must permit deliberate recovery from a valid actual file backup.");
        string quarantine = (await binaryBackend.ReadAsync()).Recovery;
        Check(quarantine.StartsWith(SourceRecordFileStorage.NonUtf8RecoveryPrefix, StringComparison.Ordinal) &&
            Convert.FromBase64String(quarantine[SourceRecordFileStorage.NonUtf8RecoveryPrefix.Length..]).SequenceEqual(corrupt),
            "Recovery must preserve the exact corrupt file bytes, not lossy replacement text.");
    }
    private static void VerifySavedRecord(SourcePlayerRecords records)
    {
        SourcePlayerRecord saved = records.Find(First);
        Check(saved != null && saved.Career.GamesPlayed == 1 && saved.Career.GamesWon == 1 &&
            saved.Career.WinStreak == 1 && saved.Career.LastUsed == 0 && saved.Career.PointsScored == 21 &&
            saved.Career.PointsAllowed == 14 && saved.Career.PassingYards == -23 && saved.Career.RushingYards == 101 &&
            saved.Career.PassAttempts == 13 && saved.Career.PassCompletions == 3 && saved.Career.PassTouchdowns == 1 &&
            saved.Career.Tackles == 7 && saved.Career.Sacks == 2 && saved.Career.Intercepted == 6 &&
            saved.Career.TeamsDefeated == 2 && saved.DiskBank == -1 && saved.CustomPlays[2] != null,
            "Source record writer must preserve exact asymmetric original counters, opposite interceptions, defaults and actual custom play.");
    }
    private static SourceCompletedRecordMatch Match(SourcePlayerRecords records, long ordinal, int opponent = 1,
        bool cpu = false, string sessionId = SessionId, int[] participation = null, bool missingStats = false)
    {
        SourceMatchConfiguration config = Configuration(opponent, cpu ? new[] { 0 } : new[] { 0, 1, 2 });
        var selected = new List<SourceStationRecordSelection> {
            new(0, SourceRecordSelectionState.KnownSaved, record: records.Find(First)) };
        if (!cpu)
        {
            selected.Add(records.Find(Second) == null ?
                new SourceStationRecordSelection(1, SourceRecordSelectionState.KnownNew, Second) :
                new SourceStationRecordSelection(1, SourceRecordSelectionState.KnownSaved, record: records.Find(Second)));
            selected.Add(records.Find(Third) == null ?
                new SourceStationRecordSelection(2, SourceRecordSelectionState.Guest) :
                new SourceStationRecordSelection(2, SourceRecordSelectionState.KnownSaved, record: records.Find(Third)));
        }
        SourceMatchView final = Final(config, missingStats);
        var session = new SourceSavedRecordSession(config, selected);
        return participation == null ? SourceCompletedRecordMatch.FromFixedParticipation(new SourceRecordMatchId(sessionId, ordinal),
            session, final, 21, 14, 4) : new SourceCompletedRecordMatch(new SourceRecordMatchId(sessionId, ordinal),
                session, final, 21, 14, 4, participation);
    }
    private static SourceMatchConfiguration Configuration(int opponent, params int[] stationIds)
    {
        var roster = Enumerable.Range(0, 7).ToArray();
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.FourStation,
            new SourceTeamSelection(0, roster), new SourceTeamSelection(opponent, roster),
            stationIds.Select(id => new SourceStationConfiguration(id, (TeamId)(id / 2))).ToArray(),
            TeamId.Home, SourceCaptainSlot.First);
    }
    private static SourceMatchView Final(SourceMatchConfiguration config, bool missingStats,
        int periods = 4, int homeScore = 21, int awayScore = 14, bool zeroTotals = false)
    {
        var period = new SourcePeriodView(periods - 1, SourcePeriodStatus.MatchEnded, periods - 1);
        var possession = new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home, SourceCaptainSlot.First);
        var quarters = Enumerable.Range(0, periods).Select(q => new SourceQuarterScore(q,
            q == 0 ? homeScore : 0, q == 0 ? awayScore : 0)).ToArray();
        var stations = config.Stations.Select(s => new SourceStationView(s.StationId, -1, 1f)).ToArray();
        if (missingStats) return new SourceMatchView(config, SourcePlayKind.Normal, SourceMatchPhase.MatchEnded, period, possession, quarters, stations);
        return new SourceMatchView(new SourcePlayActivityStatsView(11, 17, zeroTotals ? 0 : 13, zeroTotals ? 0 : 19,
                zeroTotals ? 0 : 7, zeroTotals ? 0 : 11, 0, 0, 0, 0, 0, 0, 0, 0),
            new SourceScoringStatsView(0, 0, 0, 0, 0, 0), new SourceTouchdownStatsView(zeroTotals ? 0 : 3, zeroTotals ? 0 : 2),
            new SourceFourthDownAttemptsView(0, 0), new SourceResultProgressionStatsView(0, 0, 0, 0),
            new SourcePlaceKickStatsView(0, 0, 0, 0), new SourcePlayEndStatsView(0, 0, 0, 0,
                zeroTotals ? 0 : 101, zeroTotals ? 0 : -4, zeroTotals ? 0 : 3, zeroTotals ? 0 : 4,
                zeroTotals ? 0 : 5, zeroTotals ? 0 : 6, zeroTotals ? 0 : 2, zeroTotals ? 0 : 5),
            new SourceCompletedPassStatsView(zeroTotals ? 0 : -23, zeroTotals ? 0 : 317,
                zeroTotals ? 0 : 78, zeroTotals ? 0 : 313, zeroTotals ? 0 : 1, zeroTotals ? 0 : 4),
            new SourcePassCompletionsView(zeroTotals ? 0 : 3, zeroTotals ? 0 : 8),
            new SourceInterceptionsView(zeroTotals ? 0 : 1, zeroTotals ? 0 : 6), new SourceTimeOfPossessionView(100, 200),
            config, SourcePlayKind.Normal, SourceMatchPhase.MatchEnded, period, possession, quarters, stations);
    }
    private static string RemoveCareerField(string envelope, string field)
    {
        JsonNode header = JsonNode.Parse(envelope);
        JsonNode body = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header["payload"].GetValue<string>())));
        body["records"][0]["career"].AsObject().Remove(field);
        return ReplacePayload(envelope, body.ToJsonString());
    }
    private static string ReplacePayload(string envelope, string body)
    {
        JsonNode header = JsonNode.Parse(envelope);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        header["payload"] = payload;
        string preimage = SourcePlayerRecordStore.Schema + "\n" + header["version"].GetValue<int>() + "\n" +
            SourceSavedRecordLimits.OriginalRevision + "\n" +
            header["generation"].GetValue<long>() + "\n" + payload;
        header["sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();
        return header.ToJsonString();
    }
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        assertions++;
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException(message);
    }
    private sealed class AtomicStorage : ISourcePlayerRecordStorage
    {
        public SourceRecordStorageImage Image = new(null);
        public bool FailBeforeCommit, FailAfterCommit;
        public TaskCompletionSource HoldNext;
        public TaskCompletionSource WriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SourceRecordStorageImage> ReadAsync() => Task.FromResult(Image);
        public async Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement)
        {
            TaskCompletionSource hold = HoldNext;
            if (hold != null) { WriteStarted.TrySetResult(); await hold.Task; HoldNext = null; }
            if (Image.Primary != expectedPrimary) return false;
            if (FailBeforeCommit) { FailBeforeCommit = false; throw new SourceRecordStorageException(); }
            Image = replacement;
            if (FailAfterCommit) { FailAfterCommit = false; throw new SourceRecordStorageException(); }
            return true;
        }
    }
}
