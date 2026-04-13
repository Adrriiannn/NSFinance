import assert from "node:assert/strict";
import test from "node:test";
import { resolveConnectedBankIdentity } from "./providerBranding";

test("connected bank identity formats AIB with shortcode and full name", () => {
  const identity = resolveConnectedBankIdentity({
    providerId: "ob-aib-ie",
    providerDisplayName: "AIB"
  });

  assert.equal(identity.title, "AIB - Allied Irish Bank");
  assert.equal(identity.shortCode, "AIB");
  assert.equal(identity.fullName, "Allied Irish Bank");
});

test("connected bank identity keeps clean fallback for unknown providers", () => {
  const identity = resolveConnectedBankIdentity({
    providerId: "ob-demo-bank-ie",
    providerDisplayName: null
  });

  assert.equal(identity.title, "Demo Bank");
  assert.equal(identity.shortCode, null);
});
