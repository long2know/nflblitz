Original source environment port (local art only)
================================================

Scope
-----
The active inputs are models/field2.sdl and models/stadpie.sdl at
historicalsource/nflblitz2000 commit
89f840d58d152a2a9f230cb8d0cdffbd554c8ded. Other stadium authoring files are
not substitutes. environment_inputs.json contains metadata-only Git blob pins,
not original art or reference vertices. Source art and every derivative remain
under ignored Assets/Imported, Build, or .squad/.scratch storage.

Offline conversion, from the project root
-----------------------------------------
Acquire the complete source-resolved venue closure into a NEW private cache:
python3 -B tests/parity/fetch_inputs.py --group venues \
  --output .squad/.scratch/my-new-venue-inputs \
  --cache <explicit-read-only-original-cache> --download \
  --confirm-local-use --authorization-reference owner-directed-local-use

This reuses the existing pinned original fetcher, not a clone or recursive
asset download. It reconstructs the complete Git tree, authenticates the nine
source authorities, and resolves only their required 188 texture paths for all
868 field/stadium/active-home identities. Cache misses are not archive absence.
Every reused/downloaded file is Git-blob/length checked; the private receipt
records SHA256 and all actual slot/path bindings. No new license is inferred.
Omit --download to require a complete authenticated local cache.

python3 -B tools/environments/prepare_environment.py \
  --source-root <explicit-local-original-source-root> \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --destination .squad/.scratch/my-new-environment-bundle \
  --field grass --stadium day --home-prefix BEA \
  --confirm-local-use --authorization-reference owner-directed-local-use

The output directory must be NEW. Nothing is overwritten or downloaded.
Supply the complete local tree.json metadata as well as the required original
files. The existing roster verifier reconstructs EVERY Git tree object against
the pinned sourceRootTree; JSON sha labels alone are not authentication.
Every required input is checked against its authenticated Git blob and length.
The manifest records SHA256 fingerprints, original notices and relative paths;
it never includes the supplied machine root. Missing/unsupported inputs fail
with exact identities; there are no replacement textures, palette guesses,
generic stadiums or generated artwork.

Field choices follow the original selectors: grass=0, old-grass=1 (same base
texture list), turf=2, dirt=3, snow=4, asphalt=5, mud=6. Stadium choices are
day=0, night=1, snow=2, none=3. Home prefixes are the 31 active teaminfo entries
in pinned teamdata.c, not the 32 authoring endzone sets (inactive OIL is not an
active team). All four bg/ez1/ez2/ez3 slots use the designated home team's
prefix, as field_setup does. All 7x4x31 selector identities are represented;
old-grass deliberately shares grass's art, not its selection identity.
An alternative selector still requires its exact authenticated local inputs.
Stadium3 loads no stadium textures and draws no stadium pieces; its 18 zone
volumes and inactive authoring geometry remain metadata, never replacement art.

For example, --field turf --stadium night --home-prefix EAG selects the exact
astro_turf_field_textures/stadium_textures/EAG endzone branch. It does NOT
recolor/copy grass or use Bears art if the requested original files are absent.
The exact missing path/unsupported format is an error before any output exists.

New outputs use blitz-source-environment/v2 with a separate selectionId,
homeTeamIndex and both original table identities. The production C#7.3 reader
checks all 27 original slot/sourceTexture/authoredName/selectedName/input
associations, including repeated slots and stadium3's explicit unbound slots.
Mesh, pixel, provenance and external manifest SHA256 guards remain required.
Historic v1 grass/day/BEA bundles and all sealed gameplay/profile hashes remain
unchanged; a relabeled legacy bundle is not a new selected venue.

The portable world packet carries this verified selection and each actual
uploaded mesh/material/texture binding. The renderer rejects a packet whose
field/stadium/team/hash differs from the uploaded world. V2 additionally checks
the actual movement bootstrap field and the live match's designated home-team
prefix before emitting a frame. Unprovided team changes fail visibly instead
of retaining Chicago art. Legacy packets explicitly retain their historical
unbound-team identity; they are not promoted to v2.
This adds no field/stadium/team menu, RNG outcome or new gameplay rules.

The existing source-player --world/--world-sha256 preparation path accepts an
explicitly selected bundle. Frozen complete-original AppBundles/manifests are
immutable: replacing their world payload is not admission. A new complete-game
generation must authenticate the selected payload separately; a newly built
AppBundle, not a running sealed candidate, is required for browser acceptance.

Actual cabinet team choices (one bundle, not a rebuild per team)
--------------------------------------------------------------
The current SourceCabinetSession really permits team changes at the Teams
stage, then creates a fresh configuration at the deliberate start boundary.
Rematch retains LastConfiguration. Its Versus settings do not choose a field
or stadium: Bootstrap.FieldIndex and the explicitly packaged stadium remain
immutable for that generation.

Package every active home choice for those fixed operands:
python3 -B tools/environments/prepare_environment.py \
  --source-root <complete-authenticated-venue-cache> \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --destination .squad/.scratch/my-new-runtime-venue-catalog \
  --field grass --stadium day --home-prefix CAR --runtime-catalog \
  --confirm-local-use --authorization-reference owner-directed-local-use

catalog.json pins all31 independently verified v2 manifests and a closed
files/path/bytes/SHA256 inventory (catalog.json itself is externally pinned).
The grass/day example has275 files:99 shared meshes,144 original pixel files,
31 manifests and the catalog. environment.json is the explicit default; the
other30 manifests are selections/<PREFIX>.json. Shared pixel filenames follow
their original source basename, so this default hash differs from a standalone
single-world conversion. It is not a relabeled legacy v1 generation.

SourceEnvironmentCatalog reads each physical file once, checks every manifest,
slot, team index and shared geometry, and rejects absent selections. The new
complete profile must explicitly admit its worldCatalog entrypoint; old
963-file/v1 packages do not acquire a catalog implicitly. PlayerData.RequireWorld
receives the actual bootstrap field and the actual live match's designated
home prefix. SourceWorldPresentation selects that authenticated bundle each
frame, while RenderAssets registers immutable resources/draw groups once.
The browser selects only a registered matching manifest/selection group at a
new-match epoch. Team changes/restarts need no replacement texture upload.

This supports the existing runtime team choices, not a new field/stadium menu
or a claim that arbitrary field changes exist in the current game. An alternate
immutable bootstrap needs its own explicitly admitted fixed-field/stadium
31-team catalog, produced by the same command with different selectors.
Missing/corrupt originals, missing catalog entries, wrong actual field/home
operands and mismatched uploaded groups fail rather than reuse loaded art.

Geometry and materials
----------------------
This is the original Alias SDL 8.2 cv/st/polygon schema, not OBJ, modern SDL or
the player skeleton parser. The active cvtmod arguments are -f 65.0 -fz 57.5.
Binary32 operations and generated-C six-decimal rounding are retained.
MODEL groups/transforms, texture rotation and authored sidedness are not
applied by the original converter; positions are already baked. Original
texture ordering, repeated filename slots, shader sort and UV repeat/offset
operations are retained. TRI/255 state compression is expanded, not interpreted
as strips. No strip option is present in these active recipes.

Meshes retain scaled original source units at centered origin (0,0,0).
The (+X,+Y,-Z) reflection and original Renderer321 order together preserve
declared triangle winding. The runtime root alone scales by 1/7.8 into the
same gameplay yard space used by actors. Visibility boxes are not stadium art:
99 polysets include 18 zone volumes and three inactive authoring field pieces;
only the original 78 selected draw pieces render in the day/base configuration.

Important sampler interaction: pipe3D.c load_textures creates WRAP/WRAP
textures. guTexSource restores those texture attributes after per-limb CLAMP
state. Both states are retained separately in metadata. Native textures use
Repeat; clamping authored negative/multitile UVs smears texture edges across
the field and crowd. The browser image gate catches that actual regression.

Exact original true-color TGA pixels are decoded without recoloring.
TGA origins, channels, alpha and optional standard footer are validated.
WMS supports verified 40-byte-header RGB565, ARGB1555 and ARGB4444, including
version/filter/trilinear/aspect/LOD checks and every supplied mip. Unverified
palette/NCC/other formats fail explicitly. TGA authoring inputs have one level;
the tool does not invent the absent texus-generated mip/quantization pipeline.

The bundle contains environment.json, original-derived RGBA pixel files and
meshbin files. BZENM001 meshbin layout is little-endian: eight magic bytes,
uint32 vertex/submesh counts, interleaved XYZ/UV float32 values per split
vertex, then material-slot/count uint32 pairs and uint32 triangle indices.
All paths, counts, bounds, indices, material slots and hashes are checked by
the pure production C# reader before native asset creation.
The reader/catalog and isolated runtime/Editor callers also compile as C#7.3
against the selected Unity6000.3.24f1 reference assemblies. This is API/language
verification only, not an Editor launch, import, native player or pixel proof.

Native import and runtime
-------------------------
The explicit Unity command method is
Blitz.Editor.SourceEnvironmentImporter.ImportFromCommandLine, with:
  -environmentBundleRoot <absolute-converted-bundle-root>
  -environmentManifestSha256 <SHA256-of-environment.json>

It reserves NEW Assets/Imported/source-environment output, creates native
meshes/textures/materials and a self-contained Resources prefab at:
  BlitzSourceEnvironment/source-field2-stadpie/world

No source filesystem access is required by the browser player. ?environment=source
requires that verified prefab and fails if it is missing/incomplete.
?environment=graybox is explicit procedural presentation. With no parameter,
an installed source world is preferred; otherwise graybox is clearly logged.
The source path does not create the old procedural field or its invented labels.
Existing gameplay/control line overlays remain separate presentation.
SourceEnvironmentProvider.Selection exposes the verified typed selection;
RequireSelection(requested) rejects an installed native prefab mismatch.
Native gameplay adapters must pass their actual field/designated-home binding;
import/inspection of a selected prefab alone is not proof of that callsite.

Validation
----------
Asset-free CI (Python stdlib and .NET10 SDK; no Unity or original asset bundle):
python3 -B tools/environments/test_prepare_environment.py -q
python3 -B tools/environments/test_publication_profiles.py -q
python3 -B tools/environments/run_reader_checks.py --synthetic-only

The synthetic reader profile compiles the actual production C#7.3 reader and
runs its original toy-data assertions plus focused selection/mismatch cases.
It reads no original bundle and
does not establish original geometry, native import, browser or game parity.
The publication guard tests cover explicit profile routing and source/meta
copy guards; their process-routing checks are mocked, not original-data proof.
The SDL/TGA/WMS tests likewise use original synthetic fixtures only.

Local actual-data validation (NOT the asset-free CI profile):
python3 -B tools/environments/run_reader_checks.py --bundle-root <bundle>
python3 -B tools/environments/run_reader_checks.py --catalog-root <runtime-catalog>
python3 -B tools/environments/run_mutation_checks.py --bundle-root <bundle>
python3 -B tools/environments/run_mutation_checks.py --catalog-root <runtime-catalog>
python3 -B tools/environments/verify_original_converter.py \
  --source-root <local-source-root> --tools-root <local-pinned-midwayvideo-root> \
  --destination .squad/.scratch/my-new-C-oracle-proof

Exactly one reader profile is required: --synthetic-only, --bundle-root or
--catalog-root. Catalog checks cover actual31-team resolution, shared original
pixel buffers, absent field/stadium and forged index/hash/payload pins. Its
focused mutation profile proves default-fallback and payload-pin removals red.
The actual-bundle profile retains the original geometry/pixel checks and adds
selection regressions; it accepts either the explicit selected inventory or
the unchanged historic baseline. Missing/corrupt originals fail; there is no
synthetic fallback or claim that CI ran original-data checks without assets.
The oracle compiles blob-verified original CVTMOD.C locally with standard
headers and pow2(0)=ldexp(1,0)=1; no historical binary is executed. It compares
every vertex, expanded UV/material/index and oriented triangle.
Synthetic tests use original toy data only, never upstream art/public vertices.

Clean-checkout source-only preview preparation, from the repository root:
python3 -B tools/environments/create_preview_project.py

This creates NEW Build/world-preview/native-project without starting Unity or
reading activation/original art. The permanent metadata-only preview_sources.json
pins exactly four World sources and two frozen generic import types, plus all
six existing metas. Every source and copied byte is verified before/after.
The v3 selection snapshot preserves both earlier v1/v2 metadata identities.
No ignored session snapshot, FieldScene, hot gameplay/animation source or art is
required/copied. Changed source or metadata fails rather than silently refreshing
the pins; intentional source changes require a reviewed snapshot update.
An existing destination is never overwritten.

An explicit custom snapshot remains supported:
python3 -B tools/environments/create_preview_project.py \
  --source-hashes <explicit-custom-snapshot.json>

Custom snapshots must include all six source/meta pairs. Legacy snapshots may
retain extra source-hash guards, which are checked but never added to the six
copied sources. Neither preparation profile supplies original environment art.
A coordinated isolated Editor lease and an explicitly authorized converted
bundle are still required for the native steps. Run the import method above, then
Blitz.Editor.SourceEnvironmentChecks.VerifyFromCommandLine with the same
arguments. Use -environmentCheckReceipt Build/<new-receipt>.json for repeat
proofs. BuildIsolatedPreview is restricted to that isolated project.

capture_world.mjs requires an explicit unused loopback8186 URL, manifest hash,
isolated player root and NEW ignored output directory. It captures real Chrome
WebGL, verifies every served player payload, source-provider readiness and
actual visible field pixels, and closes only its owned Chrome process.
Stop the temporary 8186 server afterwards; never promote or replace live8080.
On a shared filesystem that discards chmod, use an isolated user/mount namespace
and a private0700 tmpfs mounted at project Build/world-chrome, setting
CHROME_RUNTIME_ROOT to that path. No /tmp or external profile directory is
needed; no global mount changes or unrelated browser profiles are used.

Remaining qualifications
------------------------
The inspection cameras are not gameplay camera parity. No gameplay camera
pose/FOV/viewport is changed by this port. Original hardware W-buffer bias,
Glide combiner/raster equivalence, generated mip filtering, fog/weather,
camera/kickoff-dependent goalpost fading and hardware zone screening are
not established. Zone IDs/volume meshes are retained; Unity per-piece frustum
clipping is the adapter. The authenticated venue acquisition now supplies the
required original variants, but input/conversion/catalog checks alone do not
establish every venue's rendered pixels. Full gameplay,
all home-team environments' browser acceptance, original
HUD/audio/cinematics and N64 screenshot edition identity remain separate work.
User-directed local reuse is not a new license grant or art-publication approval.
