# Original pregame/runtime resource boundary

Unity Systems prepared the separate local-only profile
`original-teams-complete-original303-prematch`, manifest
`309ca9cf4f7f0d3b9f8b43427ab581fda4257d072ea00bfdb4ccc0003c2cba4f`.
Earlier profiles, routes, packages and receipts remain separate.

- Use the real pregame FIFO/process driver, then transfer its actual RNG,
  completed output order and typed current-cursor state to the real VS/Game.
  Adopt the new Game's cursor observation **after** validating the old
  pregame C6 state. Never emit backdrop sounds or reuse the ended pregame
  producer for game pause/focus.
- Original2.33 programme13/14 control global0 matrix contributions. SAUDv3
  preserves separately executed primary0/rebased1 PCM and live source-column
  envelopes; do not substitute a timer table, own-gain-only clip or ignored
  unsupported opcode. Native overflow, allocator/master/outer gain and
  whole-DSP equivalence remain separately qualified.
- Immutable PCM warmup precedes a once-only ready transport clock-start.
  Source origins, real process ordinals and later underrun/replay guards
  remain intact.
- Shadows use the four authenticated original alpha materials and actual
  joint raster packets. The normalized16-bit depth conversion is explicitly
  portable, not certified native floating-WBUFFER pixels.

The persistent technical/acceptance receipt is
`unity-systems-original-assets-20261030.json` in the session artifact root.
Original assets/derivatives stay ignored and local; no publication is approved.
