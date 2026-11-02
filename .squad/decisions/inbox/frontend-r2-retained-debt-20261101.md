### 2026-11-01: Defer presentation, not source/audio work, while retained host debt remains

**By:** Frontend independent revision author (`07ba1c25-4e85-4c71-b959-c63de83b5b30`)

**Status:** Author-verified proposal; independent actual-image full-match acceptance remains required.

**What:** Active prematch-profile callbacks defer presentation while at least one whole host step remains pending. Every callback still delivers its bounded source work and ordered audio commands. The existing six-step/0.1-second bridge budget, measured host elapsed debt, physical-input FIFO, RNG, sound selection, source ordinals and original data are unchanged. Paused/menu frames still render. No extra physics callback, automatic audio pause, larger lead, late acceptance, voice drop or clock reanchor was introduced.

**Why:** The rejected c6 command at source4734 was already132.725ms late with523.805ms retained active host debt, epoch2/segment0, before any gameplay pause. Paying for full presentation while whole-step debt remained was avoidable callback work. This is separate from the previous repeated-pause transport revision; `audio.js` and `SourceAudioPlayback.cs` remain byte-identical to their accepted versions. Arbitrary host stalls that exhaust the unchanged strict audio deadline still fail.

**Evidence:** Commit `c202cecc114c380ebe53fe98def59f2550461bde` is normally pushed to draft main-targeting PR45. The actual public-guest Host regression is red on original production code and green after the fix, using the recorded239.815ms callback spacing. Existing1466 realtime assertions,130 compiled audio assertions, semantic mixer/Host6827 pause fixtures,49 JS audio cases and16 trusted Chrome PCM/pause cycles pass. The distinct r2 image passes actual public moving play through source5516, signed custom audible/ORIGINAL restore, public pause/resume and240 subsequent source wakes with18 fresh original voices, zero console errors and zero HTTP failures. All1158 image-served hashes and isolation headers match; the963-file original payload is unchanged.

**Gate:** Image `sha256:6b65a498215d6f87ae21d8db3ac937b022a164d45abbd351212c6af3b640a682` is a candidate, not an accepted release. Receipt: session `files/frontend-audio-revision-20261101.json`. Independent reviewer `c2dc7267-a202-4678-9dd0-9a1dce01562c` must still verify the complete public four-quarter/save/recreation/rematch/reload path. No registry push or `latest` promotion occurred. Native/Unity/arcade parity remains unqualified. Locked-out authors supplied no revision advice or code.
