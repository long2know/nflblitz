# Full browser-content viewport regression

The host fills the browser's **content viewport**, not the browser chrome or
operating-system screen. It neither requests fullscreen nor caps the game at
1280×720. Unity's default `matchWebGLToCanvasSize` remains enabled; the host does
not stretch a fixed-resolution render or crop the camera/HUD.

Run the CSS, real-config mutation, HTTP, and hardened-container contracts:

```sh
BUILDX_BUILDER=default python3 scripts/test-webgl-tooling.py --docker
node --test tests/viewport/gpu.test.mjs
```

Against an already-running host of the current, continuous-animation source
player, run the actual installed Chrome/CDP check:

```sh
bash tests/browser/with-posix-runtime.sh node tests/viewport/fill-window.mjs \
  --url http://127.0.0.1:8080/ \
  --output .squad/.scratch/fill-viewport-browser
```

This reuses the read-only browser helpers, installed Node 24, Tesseract and
ImageMagick (`convert`, only for pixel-only portrait-footer OCR magnification);
no npm install is required. The wrapper gives Chrome a unique, private,
project-local POSIX runtime and cleans it up. Missing Chrome, runtime isolation,
actual game artifacts, readable HUD, or WebGL buffers **fail**, never skip.

The driver verifies the current build receipt/proof, all four raw payload hashes
and MIME types, and actual Chrome payload requests. It observes the real WebGL
context and the existing read-only `LogAnimationEvidence` diagnostic. It waits
for the fresh opening kickoff to settle without gameplay input, then checks
1920×1080, 2560×1080, 1280×800, 360×740, repeated resizes, DPR 2, and an explicitly
test-requested canvas fullscreen/exit. Every canvas must start at (0,0), exactly
fill `innerWidth`/`innerHeight` (±1 pixel), have no border/focus outline or page
scrollbars, and use a correspondingly resized real drawing buffer/GL viewport.

Uncropped desktop/portrait PNGs must contain the field, title, scores, clock,
phase and controls, with the same fresh-match HUD after resize only. Finally,
ordinary next-play/select/snap and W keydown/keyup exercise focus/input. JSON
evidence and PNGs stay in the ignored output directory. These bounded checks do
not certify full-match completion or whole-game parity. HUD/OCR gates apply to
the DPR 1 desktop/portrait pair; DPR 2 checks qualify buffer/viewport geometry,
not the existing engine-side HUD scaling.

The report also records the actual game's `WEBGL_debug_renderer_info` renderer,
Chrome `SystemInfo.getInfo` GPU feature status, and five-second idle/action
RAF/GL-drawn-frame cadence samples (median/p95 frame times and FPS). Real GL draw
calls distinguish drawn game frames from otherwise empty RAF callbacks. These
are submission-cadence measurements, not GPU-completion or display-refresh
counters. The headless renderer may be SwiftShader/software even on a machine
with a physical GPU; this does not qualify a separate visible browser as
hardware accelerated or establish a measured software-to-hardware speedup.
