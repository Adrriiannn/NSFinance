import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const providerSource = readFileSync(
  join(
    resolve(dirname(fileURLToPath(import.meta.url))),
    "ThemeRuntimeProvider.tsx"
  ),
  "utf8"
);

test("theme changes remount the subtree atomically", () => {
  assert.match(
    providerSource,
    /<View key=\{resolvedThemeName\} style=\{styles\.container\}>/,
    "children must be keyed by the resolved theme so no consumer can keep stale styles"
  );

  const keyedIndex = providerSource.indexOf("key={resolvedThemeName}");
  const overlayIndex = providerSource.indexOf("<ThemeRevealOverlay");
  assert.ok(keyedIndex >= 0 && overlayIndex >= 0);
  assert.ok(
    overlayIndex > keyedIndex,
    "the reveal overlay must stay outside the keyed subtree so it can mask the remount"
  );
});

test("system-driven scheme changes get the same reveal transition as manual changes", () => {
  assert.match(
    providerSource,
    /previousResolvedThemeNameRef/,
    "the provider must track the previous resolved theme to detect system-driven changes"
  );
  assert.match(
    providerSource,
    /if \(previous === resolvedThemeName \|\| isTransitioningRef\.current\) \{/,
    "system transitions must not double-fire during manual transitions"
  );
});
