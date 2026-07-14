import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const componentSource = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "PlaceResultCard.tsx"),
  "utf8"
);

test("the photo viewer fills SystemModal's safe viewport instead of the full window", () => {
  const viewerStart = componentSource.indexOf("function PlacePhotoViewer");
  const viewerEnd = componentSource.indexOf("\nfunction getTouchDistance", viewerStart);

  assert.notEqual(viewerStart, -1);
  assert.notEqual(viewerEnd, -1);

  const viewerSource = componentSource.slice(viewerStart, viewerEnd);

  assert.match(viewerSource, /<SystemModal/);
  assert.match(viewerSource, /style=\{styles\.viewerContent\}/);
  assert.doesNotMatch(viewerSource, /useWindowDimensions|\{\s*width,\s*height\s*\}/);
  assert.match(
    componentSource,
    /viewerContent:\s*\{[\s\S]*?flex:\s*1,[\s\S]*?alignSelf:\s*"stretch"/
  );
});
