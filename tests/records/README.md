# Original logical records and local saves

```sh
python3 tests/records/run.py --mutations --data-root /path/to/existing/admitted/data \
  --original-root /path/to/pinned/nflblitz2000 \
  --output .squad/.scratch/records-statistics-20261110
```

The runner authenticates the unchanged original playbook, the five original
math/custom C inputs and the record/header/lifecycle source pins before compiling
only `convert_route`, its distance/angle helpers and
original `iasin`/`iacos`/trig data. It compares actual generated route words, source
compact-byte literals, validation/repair, station ownership, custom audibles,
source career updates and once-only completed-match receipts. Hosted `sqrtf` is
not the original MIPS `sqrt.s` execution or proof of native numerical equivalence.
`--mutations` compiles isolated copies with the cap angle sign reversed,
interceptions credited from the wrong side, and in-memory state committed before
async IO completes, plus incorrectly full-mask-filtered worlds and missing
version2 worlds accepted, recovery-only storage treated as empty, culture-aliased
career fields, collapsed extra career keys, and whitespace-aliased match receipts;
each must fail its actual behavioral assertion. Canonical production files are
never mutated.

The actual production contract/storage helpers compile as C# 7.3/netstandard2.1.
Only the portable runtime serializer/file/delegate adapters target .NET 10.
All generated executables, SDK scratch, synthetic saves and receipts stay under
the explicit `--output` root (the historical default remains
`.squad/.scratch/original-records-20261030/`). Choose a new dated root rather than
overwriting earlier evidence. No SDK, package, data or source is downloaded by
the runner. A restore runs only after a genuine missing-assets build failure.

## Persistence contract

`SourcePlayerRecordStore` requires a deliberate load/initialization and reports
not-found, corrupt, unsupported-version, stale-writer and IO failures separately.
It preserves the last valid in-memory records until an atomic write succeeds.
Recovery is explicit, retains corrupt/unknown primary bytes and never silently
creates new players. Export/import is the explicitly versioned portable format,
not a native CMOS image. The envelope hashes the version, source revision,
generation and complete payload; every source career field is required.
`NotFound` requires all three opaque values to be absent. A retained quarantine
without a primary or backup is `Corrupt` / `SOURCE_SAVE_PRIMARY_MISSING`, never
permission to initialize an empty cabinet. It has no authenticated backup to
recover; a deliberate failed-store replacement import retains that exact
quarantine while restoring the supplied logical records.

All supported versions require exactly seventeen literal, ordinal career
field names before and after deserialization. Integrity-valid Unicode/culture
aliases cannot replace a required field or collapse additional fields into it.
Match receipt keys require exactly thirty-two N-format hexadecimal characters.
Uppercase remains valid and normalizes to lowercase; surrounding whitespace
is corrupt, not a second unmatched receipt that could award a final twice.
The file adapter quarantines non-UTF8 corruption losslessly using its explicit
`NonUtf8RecoveryPrefix` plus base64, not replacement characters or a native image
conversion.
Further recoveries retain earlier quarantine strings in lossless nested JSON
pairs, rather than overwriting them or preventing recovery permanently.

Version2 adds exactly six nullable `SourceWorldRecord` entries, independently of
the opaque career `WorldRecords` bit mask. Entries carry source category, actual
single-match value and the private original name/PIN identity. Unset is null,
not an invented zero/default holder. Actual observed zero may establish a first
local record; no original factory CMOS/default leaderboard image is claimed.
The source writer visits physical stations in order, ignores career full-mask
eligibility for named world candidates, keeps the old holder on equal values,
and awards victory-margin entries only for a positive difference.

The original `nfl.c:2741-2751` guard runs **both** career and world writes only for
a winner after at least four completed periods, or the terminal seventh period.
Partial/declined games and tied periods4..6 never become synthetic four-period
results. Record commit numbers are a separate contiguous receipt sequence, not
the count of games started; skipped/failed matches do not consume them.

Validated version1 files retain all original player/custom/audible/commit data
and expose six unset worlds. Load does not write. `RequiresFormatUpgrade` remains
true until a successful mutation writes version2 with the valid version1 backup.
Legacy import preserves any already-known current worlds absent from that legacy
schema. Version2 missing its world field is corrupt; unknown newer versions stay
unsupported by the legacy/default policy. The IndexedDB database/wire remain
version1 and opaque.

### Opt-in custom-block history for the new named TwoStation workflow

Only the distinct `original-teams-complete-av306-two-station-records-v1`
workflow selects
`new SourcePlayerRecordStore(storage, SourceRecordSavePolicy.CustomBlockHistoryV3)`
**before load**. The unchanged default is `LegacyV2`: it still writes version2,
omits the new property, and rejects version3 rather than guessing a migration.
No new IndexedDB collection, original input bytes, native bank or CRC is added.

The version3 envelope retains the same schema identity, all counters, nine
nullable custom slots, audibles, six worlds and receipts. Every record additionally
requires the exact integer `customBlockState`: `LegacyUnknown=0`,
`NeverAllocated=1`, or `Allocated=2`. Missing/null/noninteger/aliased/out-of-range
fields are corrupt. `NeverAllocated` with an actual Exists play is contradictory,
not silently repaired. Invalid nonnull `Unused` play DTOs remain invalid.

Fresh creation under the explicit policy starts `NeverAllocated`. Actual custom
save acquires the block; portable delete-last and audible/career/copy mutations
retain history. Actual CAP-slot B exit calls `CommitCustomBlockAsync(identity)`
even without editing. Only successful atomic persistence installs `Allocated`
and returns to Career through the normal queued source-menu continuation.
Failure remains in CAP with an explicit data-free storage error and the prior
acknowledged state. CAP's nested audible editor returns to CAP slots; an
independently opened audible editor keeps its old Career return behavior.
An unfinished new-policy CAP session cannot start a match or be bypassed by
administrative restart.

Opted-in v1/v2 reads migrate only a validated actual Exists play to `Allocated`.
Nine nulls remain `LegacyUnknown` regardless of games played or opaque DiskBank;
load does not write. An explicit successful mutation upgrades to version3 with
the exact old valid bytes as backup. Unknown unselected records may remain in
the document. The new-profile-only
`SourceCustomPlaySession.FromTwoStationSavedRecords(records)` rejects a selected
unknown identity with `SOURCE_CUSTOM_BLOCK_HISTORY_UNAVAILABLE`, uses
`NeverAllocated` as KnownNone, and `Allocated` as KnownSaved even when empty.
An actual selected-station C attempt with unknown history exposes that exact
machine code through the existing `StoreCode` and data-free `Describe().Code`
metadata while rendering only the font-safe human message:
`CUSTOM PLAY HISTORY UNAVAILABLE - ENTER CAP AND EXIT TO SAVE OR CHOOSE GUEST`.
`CanStartMatch` and match admission remain false; the legacy policy keeps its
original ready-only C condition. Failed IO/CAS keeps its real storage diagnostic
instead of being masked by the history code. Machine-code underscores must never
be sent through the original `bast8t` text renderer; its strict glyph guard stays
unchanged.
All legacy inventory constructors keep their Exists-only policy. An actual
successful CAP exit can resolve legacy-empty Unknown without fabricated native
allocation data.

Portable import remains an explicit **full-record replacement**, not a normal
career/edit copy: its existing confirmation says it replaces all local records
and custom plays. Incoming validated history replaces the prior record's history;
an imported legacy-empty record stays Unknown even if the same local identity
previously had Allocated history. No old history is silently merged or promoted.
The existing receipt/world-version safeguards remain unchanged. Import and
explicit backup recovery are management operations, not native CAP acquisition.

Run only the new storage/domain regressions and two isolated compiled semantic
controls, without original assets or the earlier component/accounting suites:

```sh
python3 tests/records/run.py --custom-block-history --mutations \
  --output .squad/.scratch/records-custom-block-20261110
```

These inspect required wire fields, fresh-store and actual storage-image JSON
round trips, empty acquisition, delete-last, career/world/receipt preservation,
v1/v2 migration and strict negatives, pending IO, CAS, lost acknowledgements and
explicit corruption recovery. The controls remove real empty acquisition and
the NeverAllocated/Exists contradiction guard; each must compile and fail the
named behavioral assertion, followed by a fresh canonical rebuild. This mode
does **not** claim public CAP input, forced-choice caller RNG, browser IndexedDB,
AOT trimming or native disk execution; those are separate new-profile acceptance.

The November10 focused run completed **47 assertions**, both clean-compiled
semantic controls failed their named behavioral assertions, and the final
forced canonical rebuild completed the same **47 assertions**. All compiled
input hashes remained unchanged; all19 exported log/receipt members were read
back. Persistent evidence:
`/home/long2know/.cache/nflblitz-quality-20261110-1440/backend-evidence-20261110/custom-block-v3-focused-20261110-01.tgz`,
SHA256 `1f948b3a76a607d544cbeff4d09f86ffb378a8fc4e8256de25456cc2de49283a`;
adjacent `custom-block-v3-focused-summary-20261110-01.json`,
SHA256 `09b786f624d61f60054b4044c9ea031a0dfd7e23b2f408e1fb1debf2af3267d7`.
Only the focused selector ran: none of the earlier321 component assertions,
sixteen accounting cases, original-C route campaign or browser suites was
repeated or promoted by this result.

An actual previously generated version1 fixture can also be checked without
rewriting that evidence:

```sh
dotnet .squad/.scratch/original-records-20261030/build/checks/bin/Release/net10.0/SourceRecords.Checks.dll \
  --legacy-v1 /path/to/owned/synthetic/version1/records.json
```

Browser glue supplies `ISourcePlayerRecordStorage` through
`SourceRecordDelegateStorage`: read opaque primary/backup/recovery strings, then
compare the expected primary and replace all three in one IndexedDB transaction.
`SourceRecordStorageImage.DecodeBrowserJson`/`EncodeBrowserJson` use exactly
`{"primary":string-or-null,"backup":string-or-null,"recovery":string-or-null}`;
missing/wrong-case/wrong-type fields fail instead of becoming an empty store.
The native object store is also restricted to those three exact string keys.
Every transaction validates its complete cursor before returning an image or
performing compare/exchange writes. Unknown, wrong-case or numeric persisted
keys produce a data-free schema failure without deleting or masking their data.
Resolve writes only on transaction completion; abort/quota/unavailable failures
become `SourceRecordStorageException`, without including browser exception text.
Do not use the WASM virtual filesystem as browser persistence.

Completed matches use one session GUID and increasing match numbers. Their
persistent high-water receipts and career updates share the same transaction.
Rematch/new-game resets simulation counters, not the independent store. Current
fixed pre-match participation can use `FromFixedParticipation`; other policies
must supply genuine four-station quarter masks. The original writer credits team
totals to each eligible teammate and counts opposite-side interceptions as that
record's interceptions thrown. Null producer groups are unavailable, never zero.

The retained production flow is typed source counters → immutable `GameSnapshot`
match → the executing `game_proc` final context → runtime record session →
career/worlds/receipt in one transaction → final-stats continuation. The source
`tdata_t` is per side; eligible human records receive that side's totals, not
invented per-roster-player careers. Existing TD/pass/rush/return/sack/turnover
producers remain the accounting authority. The new corruption/matching checks
do not add counters, native CMOS assumptions or a service. Their earlier
version1/version2 schema behavior remains separate from the explicitly opted-in
custom-block-history workflow above.

For the focused integrity-valid import regressions alone, run the built
`SourceRecords.Checks.dll --strict-imports`. These cases exercise both version1
and version2 through the actual production store, including positive normalized
receipt round trips and duplicate-final suppression. The separate cabinet
`--terminal-only` selector executes the actual final-save barrier/failure/retry
caller using a clearly labelled terminal fixture; it is not natural-match or
native IndexedDB evidence. Browser `page.mjs`/`native.mjs` additionally exercise
unexpected persisted keys in real IndexedDB when run after the source freeze.
`SOURCE_RECORDS_BROWSER_ROOT` selects an explicit dated proof root beneath this
worktree's `.squad/.scratch`; its evidence and optional fresh managed AppBundle
must remain inside that root. The historical default root is unchanged.

## November 10, 2026 bounded result

The final component run completed **321 assertions**, a **five-assertion fresh
process/file reload**, and **nine compiled semantic controls**, all passing or
rejected at their named behavioral assertion as appropriate. The production-only
helpers compiled under C#7.3/netstandard2.1; both final builds had zero warnings/errors.
The existing actual cabinet `--terminal-only` consumer separately passed
**61 assertions** against frozen Core SHA256
`506ee98400eeea819df76cb399fef97b061d84cb2a3d56b439eaabc9303fcb80`,
checked before compilation and after execution.

The literal typed final-stat fixture retained the source writer's asymmetric
home career: points21/14, passing−23, rushing101, attempts13/completions3/passTD1,
tackles7/sacks2 and interceptions thrown6 (the other side's interceptions made).
The same-side teammate receives team totals; partial participation does not
grant full-game career credit; a terminal seventh-period tie is a non-win.
These are record-adapter cases, not a claim that the fixture played those events.
The actual cabinet fixture proves the real game process waits for IO, retains
the same final snapshot during failure/retry, and commits once before rematch.

Recovery-only classification and culture-aliased imports failed their new
behavioral assertions before their fixes. Removing each new matching guard
again fails its compiled control. The JS unknown-key case also failed before
the full-cursor fix; the pure protocol run now passes five tests. Its one native
browser test was explicitly skipped pending coordinator source freeze; the new
real IndexedDB cases remain a Frontend handoff, not claimed browser evidence.

The persistent, data-free receipt/log archive is
`/home/long2know/.cache/nflblitz-quality-20261110-1440/backend-evidence-20261110/records-components-20261110.tgz`,
SHA256 `c780c48f87639a72e388eba991cf2dc4cbce46f765db5e0f5660f74ab6748c92`.
Adjacent `summary.json` records exact commands, source pins, observed results and
the remaining browser condition. All 31 exported receipt/log members were read
back and hash-verified. Raw saves, original source, SDK files and asset payloads
are excluded. The private generated build/input trees expired with their mount
namespace; only the verified export and source/test/document changes persist.

## Exact source boundaries

The source `cap_play_t` is a compact **101-byte grid**, not a bit-packed stream.
`convert_play` expands it into original formations and drone opcodes. Names have
two eight-character content fields; nine slots retain physical-station ownership.
Only source callers perform forced-choice RNG; manual missing/custom audible
resolution does not. Native `cap_block_t`/CMOS padding, endianness and CRC-container
provenance are not inferred from a host struct overlay.

The first 350 original record slots are supported. Full-store least-used
replacement requires explicit consent; source tie ordering is preserved.
Original fixed-width career overflow fails explicitly rather than claiming
implementation-defined native wrapping. PINs distinguish local game identities,
are not server authentication and must never enter public snapshots or logs.
