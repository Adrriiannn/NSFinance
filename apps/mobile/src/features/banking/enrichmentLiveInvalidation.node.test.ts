import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  enrichmentCompletionInvalidationRoots,
  shouldRunEnrichmentCompletionInvalidation
} from "./enrichmentLiveInvalidation";

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

function readMobileSource(...segments: string[]) {
  return readFileSync(join(mobileRoot, ...segments), "utf8");
}

test("completion invalidation fires only on the active-to-idle transition", () => {
  assert.equal(shouldRunEnrichmentCompletionInvalidation(true, false), true);
  assert.equal(shouldRunEnrichmentCompletionInvalidation(true, true), false);
  assert.equal(shouldRunEnrichmentCompletionInvalidation(false, true), false);
  assert.equal(shouldRunEnrichmentCompletionInvalidation(false, false), false);
});

test("completion converges every root the interval loop refreshes", () => {
  // The interval loop touches transactions, accounts, and dashboard; a root
  // it refreshes but completion does not would inherit the last-tick
  // staleness bug on that surface (account lists, the Home recent strip).
  const roots = enrichmentCompletionInvalidationRoots.map((root) => root[0]);
  assert.deepEqual([...roots].sort(), ["accounts", "dashboard", "transactions"]);
});

test("the enrichment dial converges the shared roots when work completes", () => {
  const dialSource = readMobileSource(
    "src",
    "components",
    "feedback",
    "GlobalEnrichmentProgressDial.tsx"
  );

  assert.match(dialSource, /shouldRunEnrichmentCompletionInvalidation\(/);
  assert.match(
    dialSource,
    /enrichmentCompletionInvalidationRoots\.map\(\(root\) =>\s*queryClient\.invalidateQueries\(\{ queryKey: root \}\)/
  );
});

test("the completion invalidation is not gated on activity feed interaction", () => {
  const dialSource = readMobileSource(
    "src",
    "components",
    "feedback",
    "GlobalEnrichmentProgressDial.tsx"
  );

  // Capture from the transition check to the end of its effect. Skipping the
  // final pull while the user scrolls would recreate the stale-filter bug the
  // transition exists to close; the feed's own commit gate defers rendering.
  const completionEffect = dialSource.match(
    /shouldRunEnrichmentCompletionInvalidation\([\s\S]*?\}, \[[^\]]*\]\);/
  )?.[0] ?? "";

  assert.ok(completionEffect.length > 0, "completion invalidation effect not found");
  assert.doesNotMatch(completionEffect, /getActivityFeedInteracting/);
});
