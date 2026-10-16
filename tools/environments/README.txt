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
python3 -B tools/environments/prepare_environment.py \
  --source-root <explicit-local-original-source-root> \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --destination .squad/.scratch/my-new-environment-bundle \
  --field grass --stadium day --home-prefix BEA \
  --confirm-local-use --authorization-reference owner-directed-local-use

The output directory must be NEW. Nothing is overwritten or downloaded.
Every required input is checked against its pinned Git blob and length.
The manifest records SHA256 fingerprints, original notices and relative paths;
it never includes the supplied machine root. Missing/unsupported inputs fail
with exact identities; there are no replacement textures, palette guesses,
generic stadiums or generated artwork.

Field choices follow the original selectors: grass=0, old-grass=1 (same base
texture list), turf=2, dirt=3, snow=4, asphalt=5, mud=6. Stadium choices are
day=0, night=1, snow=2, none=3. This recipe pins Bears endzones only. Other
prefixes are explicitly unsupported here, not alleged absent from the archive.
An alternative selector still requires its exact local pinned textures.

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

Validation
----------
Asset-free CI (Python stdlib and .NET10 SDK; no Unity or original asset bundle):
python3 -B tools/environments/test_prepare_environment.py -q
python3 -B tools/environments/test_publication_profiles.py -q
python3 -B tools/environments/run_reader_checks.py --synthetic-only

The synthetic reader profile compiles the actual production C#7.3 reader and
runs its 14 original toy-data assertions only. It reads no original bundle and
does not establish original geometry, native import, browser or game parity.
The 21 publication guard tests cover explicit profile routing and source/meta
copy guards; their process-routing checks are mocked, not original-data proof.
The SDL/TGA/WMS tests likewise use original synthetic fixtures only.

Local actual-data validation (NOT the asset-free CI profile):
python3 -B tools/environments/run_reader_checks.py --bundle-root <bundle>
python3 -B tools/environments/run_mutation_checks.py --bundle-root <bundle>
python3 -B tools/environments/verify_original_converter.py \
  --source-root <local-source-root> --tools-root <local-pinned-midwayvideo-root> \
  --destination .squad/.scratch/my-new-C-oracle-proof

Exactly one reader profile is required: --synthetic-only or --bundle-root.
The existing actual-bundle profile retains all 127 checks (including the toy
checks). Missing/corrupt originals fail; there is no synthetic fallback, skip
or claim that CI without original data ran those 127 actual-profile checks.
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
clipping is the adapter. Full gameplay, all home-team environments, original
HUD/audio/cinematics and N64 screenshot edition identity remain separate work.
User-directed local reuse is not a new license grant or art-publication approval.
