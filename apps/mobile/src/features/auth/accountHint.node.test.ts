import assert from "node:assert/strict";
import test from "node:test";
import { maskAccountEmail } from "./accountHint";

test("maskAccountEmail reveals only the first three local-part characters and the domain", () => {
  assert.equal(maskAccountEmail("adrian@example.com"), "adr****@example.com");
});

test("maskAccountEmail handles short local parts without exposing more information", () => {
  assert.equal(maskAccountEmail("a@example.com"), "a****@example.com");
  assert.equal(maskAccountEmail("ab@example.com"), "ab****@example.com");
});

test("maskAccountEmail rejects missing and malformed addresses", () => {
  assert.equal(maskAccountEmail(null), null);
  assert.equal(maskAccountEmail(""), null);
  assert.equal(maskAccountEmail("not-an-email"), null);
  assert.equal(maskAccountEmail("@example.com"), null);
});
