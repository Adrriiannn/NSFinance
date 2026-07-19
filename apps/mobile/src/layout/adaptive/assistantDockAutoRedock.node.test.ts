import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const dockSource = readFileSync(
  join(
    resolve(dirname(fileURLToPath(import.meta.url))),
    "FloatingAssistantDock.tsx"
  ),
  "utf8"
);

test("the expanded dock auto-redocks after an idle period", () => {
  const idleMatch = dockSource.match(/AUTO_REDOCK_IDLE_MS = (\d+)/);
  assert.ok(idleMatch, "AUTO_REDOCK_IDLE_MS constant must exist");

  const idleMs = Number(idleMatch?.[1]);
  assert.ok(
    idleMs >= 4000 && idleMs <= 15000,
    "idle delay should be long enough to tap but short enough to clear content"
  );

  assert.match(
    dockSource,
    /setTimeout\(\(\) => \{\s*snapDock\(dockSide, topFromRatio\(verticalRatio\)\);\s*\}, AUTO_REDOCK_IDLE_MS\)/,
    "idle timer must snap back to the docked handle"
  );
  assert.match(
    dockSource,
    /return \(\) => clearTimeout\(timer\);/,
    "the idle timer must be cleared on interaction or unmount"
  );
  assert.match(
    dockSource,
    /if \(!isExpanded \|\| isDraggingDock \|\| hidden \|\| !hasHydratedDockState\) \{/,
    "auto-redock must not run while docked, dragging, hidden, or before hydration"
  );
});
