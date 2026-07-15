import assert from "node:assert/strict";
import test from "node:test";
import { maskAccountIdentifier } from "./accountPrivacy";

test("account identifiers expose only the final four characters", () => {
  assert.equal(maskAccountIdentifier("ie70 aibk 9323 5342 6970 26"), "Ending 7026");
  assert.equal(maskAccountIdentifier("1234 5678"), "Ending 5678");
  assert.equal(maskAccountIdentifier("123456-78"), "Ending 5678");
});

test("short and empty identifiers do not leak their full value", () => {
  assert.equal(maskAccountIdentifier("1234"), "Hidden");
  assert.equal(maskAccountIdentifier("  "), null);
  assert.equal(maskAccountIdentifier(null), null);
});
