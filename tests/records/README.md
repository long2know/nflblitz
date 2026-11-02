# Original logical records and local saves

```sh
python3 tests/records/run.py --mutations --data-root /path/to/existing/admitted/data \
  --original-root /path/to/pinned/nflblitz2000
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
version2 worlds accepted; each must fail its actual behavioral assertion. Canonical
production files are never mutated.

The actual production contract/storage helpers compile as C# 7.3/netstandard2.1.
Only the portable runtime serializer/file/delegate adapters target .NET 10.
All generated executables, SDK scratch, synthetic saves and receipts stay under
`.squad/.scratch/original-records-20261030/`. No SDK, package, data or source is
downloaded. A restore runs only after a genuine missing-assets build failure.

## Persistence contract

`SourcePlayerRecordStore` requires a deliberate load/initialization and reports
not-found, corrupt, unsupported-version, stale-writer and IO failures separately.
It preserves the last valid in-memory records until an atomic write succeeds.
Recovery is explicit, retains corrupt/unknown primary bytes and never silently
creates new players. Export/import is the explicitly versioned portable format,
not a native CMOS image. The envelope hashes the version, source revision,
generation and complete payload; every source career field is required.
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
unsupported. The IndexedDB database/wire remain version1 and opaque.

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
