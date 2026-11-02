### 2026-11-01: Defer presentation, not source/audio work, while retained host debt remains

**By:** Frontend independent revision author (`07ba1c25-4e85-4c71-b959-c63de83b5b30`)

**Status:** Author-verified proposal; independent actual-image full-match acceptance remains required.

**What:** Active prematch-profile callbacks defer presentation while at least one whole host step remains pending. Every callback still delivers its bounded source work and ordered audio commands. The existing six-step/0.1-second bridge budget, measured host elapsed debt, physical-input FIFO, RNG, sound selection, source ordinals and original data are unchanged. Paused/menu frames still render. No extra physics callback, automatic audio pause, larger lead, late acceptance, voice drop or clock reanchor was introduced.

**Why:** The rejected c6 command at source4734 was already132.725ms late with523.805ms retained active host debt, epoch2/segment0, before any gameplay pause. Paying for full presentation while whole-step debt remained was avoidable callback work. This is separate from the previous repeated-pause transport revision; `audio.js` and `SourceAudioPlayback.cs` remain byte-identical to their accepted versions. Arbitrary host stalls that exhaust the unchanged strict audio deadline still fail.

**Evidence:** Commit `c202cecc114c380ebe53fe98def59f2550461bde` is normally pushed to draft main-targeting PR45. The actual public-guest Host regression is red on original production code and green after the fix, using the recorded239.815ms callback spacing. Existing1466 realtime assertions,130 compiled audio assertions, semantic mixer/Host6827 pause fixtures,49 JS audio cases and16 trusted Chrome PCM/pause cycles pass. The distinct r2 image passes actual public moving play through source5516, signed custom audible/ORIGINAL restore, public pause/resume and240 subsequent source wakes with18 fresh original voices, zero console errors and zero HTTP failures. All1158 image-served hashes and isolation headers match; the963-file original payload is unchanged.

**Gate:** Image `sha256:6b65a498215d6f87ae21d8db3ac937b022a164d45abbd351212c6af3b640a682` is a candidate, not an accepted release. Receipt: session `files/frontend-audio-revision-20261101.json`. Independent reviewer `c2dc7267-a202-4678-9dd0-9a1dce01562c` must still verify the complete public four-quarter/save/recreation/rematch/reload path. No registry push or `latest` promotion occurred. Native/Unity/arcade parity remains unqualified. Locked-out authors supplied no revision advice or code.

### 2026-11-01: Independent R2 browser acceptance update

The original proposal and its pre-acceptance gate above are retained as history.
Reviewer `c2dc7267-a202-4678-9dd0-9a1dce01562c` subsequently passed the exact
immutable R2 image and build. One unmodified public-control campaign completed
four regulation periods, final source29335/score0:52, generation5 atomic save,
rematch, both same-origin container recreations and fresh career GAMES1 reload.
All1158 served hashes/headers and159 live/committed source hashes passed;3462
local browser requests had zero exceptions, console, HTTP, loading or nonlocal
failures.

The independent receipt is `frontend-behavior-image-acceptance-r2-20261101.json`,
SHA256 `b007138a60154602eb2bb909437253b06f3e426f6f6a8a8f6e8cce0141ac9d8c`;
the raw result SHA256 is
`31c8bb2dfc3479bd7d4202fc88ca2a9977e38022642402c9c244a929807f32b1`.
The author also read back these exact proofs without changing the frozen source,
image or prior failure evidence. Main merge and publication remain separately
gated. No additional full campaign is required. Native parity is still
`ExternalUnverified`; the bounded R2 prefix retest failed before startup because
of Docker VFS allocation, not an observed game or proxy failure.
