using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.AssetImport
{
    public sealed partial class SourceInterfaceData
    {
        private static readonly ConditionalWeakTable<SourcePlaybook, object> audibleCatalogs =
            new ConditionalWeakTable<SourcePlaybook, object>();
        private static readonly ConditionalWeakTable<SourceDefaultAudibleData, SourcePlaybook> audibleAdmissions =
            new ConditionalWeakTable<SourceDefaultAudibleData, SourcePlaybook>();

        public static SourcePlaybook ReadDefaultAudiblePlaybook(byte[] bytes)
        {
            SourceInterfaceInputTables.Require(bytes != null && bytes.Length > 0 && bytes.Length <= 4 * 1024 * 1024,
                "Default audibles require the actual authenticated existing playbook bytes.");
            byte[] authenticated = (byte[])bytes.Clone();
            SourceInterfaceInputTables.Require(RosterGuard.Sha256(authenticated) == SourceDefaultAudibleData.RequiredPlaybookSha256,
                "Default audibles require the actual authenticated existing playbook bytes.");
            SourcePlaybook book;
            using (var reader = new StringReader(AuthorizedAssetRoot.DecodeUtf8(authenticated, "default-audibles/playbook")))
                book = SourcePlaybook.LoadConverted(reader);
            audibleCatalogs.Add(book, new object());
            return book;
        }

        public SourceDefaultAudibleData CreateDefaultAudibleData(SourcePlaybook playbook)
        {
            object authenticated;
            SourceInterfaceInputTables.Require(ContentSha256 == SourceDefaultAudibleData.RequiredHudSha256 &&
                playbook != null && audibleCatalogs.TryGetValue(playbook, out authenticated),
                "Default audibles require the SAME catalog loaded from authenticated bytes and the exact actual HUD.");
            var offense = new IReadOnlyList<SourcePlayDefinition>[4];
            var defense = new IReadOnlyList<SourcePlayDefinition>[2];
            for (int page = 0; page < offense.Length; page++)
                offense[page] = AudiblePage(playbook, SourcePlaybookKind.Offense, page);
            for (int page = 0; page < defense.Length; page++)
                defense[page] = AudiblePage(playbook, SourcePlaybookKind.Defense, page);
            var specials = new IReadOnlyList<SourcePlayDefinition>[31];
            for (int team = 0; team < specials.Length; team++)
            {
                var row = new SourcePlayDefinition[3];
                for (int slot = 0; slot < row.Length; slot++)
                    row[slot] = playbook.GetPlay(PageSlot(SourcePlaybookKind.Offense, 0, slot, team).PlaySymbol);
                specials[team] = row;
            }
            var contexts = new SourceDefaultAudibleSpecialPlays(
                playbook.GetPlay(PlaySlots["def_slots"][21].PlaySymbol),
                playbook.GetPlay(PlaySlots["off_slots"][38].PlaySymbol),
                playbook.GetPlay(PlaySlots["def_slots"][19].PlaySymbol),
                playbook.GetPlay(PlaySlots["off_slots"][39].PlaySymbol),
                playbook.GetPlay(PlaySlots["off_slots"][36].PlaySymbol),
                playbook.GetPlay(PlaySlots["off_slots"][37].PlaySymbol),
                playbook.GetPlay(PlaySlots["def_slots"][20].PlaySymbol),
                playbook.GetPlay(PlaySlots["off_slots"][40].PlaySymbol));
            var result = new SourceDefaultAudibleData(SourceDefaultAudibleData.StableProfileId,
                SourceDefaultAudibleData.RequiredFeatureContract, ContentSha256,
                SourceDefaultAudibleData.RequiredPlaybookSha256, playbook, Input.Digital8, offense, defense, specials, contexts);
            audibleAdmissions.Add(result, playbook);
            return result;
        }

        private SourcePlayDefinition[] AudiblePage(SourcePlaybook book, SourcePlaybookKind kind, int page)
        {
            var values = new SourcePlayDefinition[9];
            for (int slot = 0; slot < values.Length; slot++)
                values[slot] = book.GetPlay(PageSlot(kind, page, slot, 0).PlaySymbol);
            return values;
        }

        public static void RequireDefaultAudibleAdmission(SourceDefaultAudibleData data, SourcePlaybook book)
        {
            SourcePlaybook authenticated;
            SourceInterfaceInputTables.Require(data != null && book != null &&
                audibleAdmissions.TryGetValue(data, out authenticated) && ReferenceEquals(authenticated, book) &&
                ReferenceEquals(data.Playbook, book),
                "Default audibles require typed data produced from the SAME authenticated HUD/catalog; declared hashes alone do not authenticate supplied arrays.");
        }
    }
}
