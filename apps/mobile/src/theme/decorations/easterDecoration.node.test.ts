import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { mulberry32, pickSeeded, seededInRange } from "./decorativeRandom";

const layerSource = readFileSync(
  join(resolve(dirname(fileURLToPath(import.meta.url))), "EasterDecorativeLayer.tsx"),
  "utf8"
);

test("seeded randomness is deterministic and in range", () => {
  const first = mulberry32(1234);
  const second = mulberry32(1234);

  for (let index = 0; index < 32; index += 1) {
    const value = first();
    assert.equal(value, second(), "same seed must produce the same sequence");
    assert.ok(value >= 0 && value < 1);
  }

  const random = mulberry32(99);
  for (let index = 0; index < 16; index += 1) {
    const ranged = seededInRange(random, 26, 44);
    assert.ok(ranged >= 26 && ranged <= 44);
  }
});

test("seeded egg styles cover multiple variants without repetition lockstep", () => {
  const styles = ["stripes", "dots", "chevron", "gradient"] as const;
  const chosen = new Set<string>();

  for (let index = 0; index < 8; index += 1) {
    const random = mulberry32(0x5ea50000 + index * 97);
    chosen.add(pickSeeded(random, styles));
  }

  assert.ok(
    chosen.size >= 3,
    `egg zones must draw a varied mix of styles, got ${[...chosen].join(", ")}`
  );
});

test("the decorative layer honors the non-obscuring contract", () => {
  assert.match(
    layerSource,
    /EASTER_DECORATION_OPACITY = 0\.1\b/,
    "layer opacity must stay decorative"
  );
  assert.match(
    layerSource,
    /pointerEvents="none"/,
    "decorations must never intercept touches"
  );
  assert.doesNotMatch(
    layerSource,
    /Math\.random\(/,
    "variation must be seeded, not nondeterministic"
  );
});
