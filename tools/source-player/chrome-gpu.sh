#!/usr/bin/env bash
set -euo pipefail
# These request a backend; acceptance records the actual WebGL renderer, not flags.
exec /opt/google/chrome/chrome --use-gl=angle --use-angle=vulkan \
  --enable-features=Vulkan --disable-vulkan-surface --ignore-gpu-blocklist \
  --in-process-gpu --disable-gpu-sandbox --disable-gpu-watchdog "$@"
