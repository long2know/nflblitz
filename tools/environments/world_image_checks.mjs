import assert from 'node:assert/strict';
import { decodePng } from '../../tests/browser/lib.mjs';

export function sourceFieldRegion(png) {
  const image = decodePng(png);
  assert.equal(image.width, 1280);
  assert.equal(image.height, 720);
  let green = 0, samples = 0;
  for (let y = 350; y < 620; y++) {
    for (let x = 280; x < 1050; x++) {
      const pixel = (y * image.width + x) * image.channels;
      const [r, g, b] = image.pixels.subarray(pixel, pixel + 3);
      if (g > 40 && g > r * 1.4 && g > b * 1.4) green++;
      samples++;
    }
  }
  return { rectangle: { x: 280, y: 350, width: 770, height: 270 }, greenFraction: green / samples, samples };
}

export function requireSourceFieldRegion(png) {
  const region = sourceFieldRegion(png);
  assert.ok(region.greenFraction > 0.5,
    `Original sideline field is edge-smeared/blank/not visible: actual turf fraction ${region.greenFraction}.`);
  return region;
}
