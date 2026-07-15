import assert from "node:assert/strict";
import test from "node:test";
import type {
  AccountBalanceDto,
  AccountDto,
  PortfolioBalanceDto
} from "../../types/api";
import {
  LEGACY_CURRENT_BALANCE_SOURCE,
  resolveAccountBalancePresentation,
  resolvePortfolioBalancePresentation
} from "./accountBalancePresentation";

type BalanceAwareAccountDto = AccountDto & {
  balance?: AccountBalanceDto | null;
};

function buildAccount(overrides: Partial<BalanceAwareAccountDto> = {}): BalanceAwareAccountDto {
  return {
    id: "account-1",
    name: "Current account",
    type: "Current",
    currency: "EUR",
    currentBalance: 999,
    transactionCount: 0,
    createdUtc: "2026-07-01T08:00:00Z",
    providerId: "provider-1",
    providerDisplayName: "Example Bank",
    providerIconUrl: null,
    providerLogoUrl: null,
    providerBrandBgColor: null,
    hasProviderBranding: true,
    source: "provider_projected",
    ...overrides
  };
}

function buildBalance(overrides: Partial<AccountBalanceDto> = {}): AccountBalanceDto {
  return {
    current: 120,
    available: 95,
    overdraft: 200,
    currency: "EUR",
    source: "provider_snapshot",
    asOfUtc: "2026-07-14T09:30:00Z",
    freshness: "fresh",
    exclusions: [],
    ...overrides
  };
}

test("structured unavailable balance never falls back to the legacy scalar", () => {
  const presentation = resolveAccountBalancePresentation(buildAccount({
    currentBalance: 999,
    balance: buildBalance({
      current: null,
      available: null,
      overdraft: null,
      source: "unavailable",
      asOfUtc: null,
      freshness: "unknown",
      exclusions: ["provider_snapshot_missing"]
    })
  }));

  assert.equal(presentation.current, null);
  assert.equal(presentation.source, "unavailable");
  assert.deepEqual(presentation.exclusions, ["provider_snapshot_missing"]);

  const nullPresentation = resolveAccountBalancePresentation(buildAccount({
    currentBalance: 999,
    balance: null
  }));
  assert.equal(nullPresentation.current, null);
  assert.equal(nullPresentation.source, "unavailable");
});

test("stale structured balance exposes its timestamp and freshness", () => {
  const presentation = resolveAccountBalancePresentation(buildAccount({
    balance: buildBalance({
      asOfUtc: "2026-07-11T09:30:00Z",
      freshness: "stale"
    })
  }));

  assert.equal(presentation.asOf, "2026-07-11T09:30:00Z");
  assert.equal(presentation.freshness, "stale");
  assert.equal(presentation.source, "provider_snapshot");
});

test("structured balance keeps current, available, and overdraft distinct", () => {
  const presentation = resolveAccountBalancePresentation(buildAccount({
    balance: buildBalance({
      current: 120,
      available: 95,
      overdraft: 200
    })
  }));

  assert.equal(presentation.current, 120);
  assert.equal(presentation.available, 95);
  assert.equal(presentation.overdraft, 200);
});

test("old API account falls back only when the structured field is absent", () => {
  const presentation = resolveAccountBalancePresentation(buildAccount({
    currentBalance: 73.5
  }));

  assert.deepEqual(presentation, {
    current: 73.5,
    available: null,
    overdraft: null,
    currency: "EUR",
    source: LEGACY_CURRENT_BALANCE_SOURCE,
    asOf: null,
    freshness: "unknown",
    exclusions: ["structured_balance_absent"]
  });
});

test("structured balance exposes all server exclusions without sharing the array", () => {
  const balance = buildBalance({
    exclusions: ["account_currency_mismatch", "available_balance_unavailable"]
  });
  const presentation = resolveAccountBalancePresentation(buildAccount({ balance }));

  assert.deepEqual(presentation.exclusions, [
    "account_currency_mismatch",
    "available_balance_unavailable"
  ]);
  assert.notEqual(presentation.exclusions, balance.exclusions);
});

test("portfolio presentation keeps mixed currencies in separate groups", () => {
  const portfolio: PortfolioBalanceDto = {
    byCurrency: [
      { currency: "EUR", amount: 350, accountCount: 2, basis: "current" },
      { currency: "USD", amount: 80, accountCount: 1, basis: "current" }
    ],
    includedAccountCount: 3,
    excludedAccountCount: 1,
    hasMultipleCurrencies: true
  };

  const presentation = resolvePortfolioBalancePresentation(portfolio);

  assert.ok(presentation);
  assert.deepEqual(presentation.currencyGroups, [
    { currency: "EUR", amount: 350, accountCount: 2, basis: "current" },
    { currency: "USD", amount: 80, accountCount: 1, basis: "current" }
  ]);
  assert.equal(presentation.includedAccountCount, 3);
  assert.equal(presentation.excludedAccountCount, 1);
  assert.equal(presentation.hasMultipleCurrencies, true);
  assert.equal("total" in presentation, false);
});
