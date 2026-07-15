import assert from "node:assert/strict";
import test from "node:test";
import type { AccountDto } from "../../types/api";
import { isProviderProjectedAccount, resolveAccountSource } from "./accountProvenance";

type AccountFixture = Omit<AccountDto, "source"> & {
  source?: string | null;
};

function buildAccount(overrides: Partial<AccountFixture> = {}): AccountFixture {
  return {
    id: "account-1",
    name: "Current account",
    type: "Current",
    currency: "EUR",
    currentBalance: 100,
    transactionCount: 1,
    createdUtc: "2026-07-15T10:00:00Z",
    providerId: null,
    providerDisplayName: null,
    providerIconUrl: null,
    providerLogoUrl: null,
    providerBrandBgColor: null,
    hasProviderBranding: false,
    source: "manual",
    ...overrides
  };
}

test("explicit account source remains authoritative", () => {
  assert.equal(
    resolveAccountSource(buildAccount({ source: "manual", providerId: "stale-provider" })),
    "manual"
  );
  assert.equal(
    resolveAccountSource(buildAccount({ source: "provider_projected", providerId: null })),
    "provider_projected"
  );
});

test("provider evidence keeps old API accounts usable during additive rollout", () => {
  const oldApiAccount = buildAccount({
    source: undefined,
    providerId: "provider-account-1",
    providerDisplayName: "Example Bank",
    hasProviderBranding: true
  });

  assert.equal(resolveAccountSource(oldApiAccount), "provider_projected");
  assert.equal(isProviderProjectedAccount(oldApiAccount), true);
});

test("old API accounts without provider evidence remain read-only legacy accounts", () => {
  const oldApiAccount = buildAccount({ source: undefined });

  assert.equal(resolveAccountSource(oldApiAccount), "manual");
  assert.equal(isProviderProjectedAccount(oldApiAccount), false);
});

test("unknown future source values fail closed even when provider branding is present", () => {
  const forwardSkewedAccount = buildAccount({
    source: "future_provider_source",
    providerId: "provider-account-1",
    providerDisplayName: "Example Bank",
    hasProviderBranding: true
  });

  assert.equal(resolveAccountSource(forwardSkewedAccount), "manual");
  assert.equal(isProviderProjectedAccount(forwardSkewedAccount), false);
});

test("missing account data has no inferred source", () => {
  assert.equal(resolveAccountSource(undefined), null);
  assert.equal(isProviderProjectedAccount(undefined), false);
});
