import assert from "node:assert/strict";
import test from "node:test";
import type { FinancialCommitmentDto } from "../../types/api";
import { buildUpcomingCommitmentRows } from "./commitmentPresentation";

const NOW_UTC_MS = Date.parse("2026-07-19T12:00:00Z");

function buildCommitment(overrides: Partial<FinancialCommitmentDto>): FinancialCommitmentDto {
  return {
    id: "commitment-1",
    kind: "direct_debit",
    lifecycle: "active",
    source: "provider",
    confidence: "high",
    confidenceScore: null,
    direction: "outgoing",
    accountId: "acc-1",
    linkedBankAccountId: null,
    accountDisplayName: "AIB **7026",
    label: "Electric Ireland",
    cadence: "monthly",
    startsAtUtc: null,
    endsAtUtc: null,
    lastObservedDateUtc: null,
    lastObservedAmount: null,
    lastObservedCurrency: null,
    nextDateUtc: "2026-07-25T00:00:00Z",
    dateCertainty: "exact",
    nextAmount: 84.5,
    currency: "EUR",
    amountCertainty: "exact",
    isVariableAmount: false,
    sourceUpdatedUtc: "2026-07-19T10:00:00Z",
    freshness: "fresh",
    analyticsNeutral: false,
    providerStatus: null,
    exclusions: [],
    evidence: [],
    userDecision: null,
    ...overrides
  };
}

test("exact provider commitments render precise amount and countdown", () => {
  const rows = buildUpcomingCommitmentRows([buildCommitment({})], NOW_UTC_MS);

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.sourceLabel, "direct debit");
  assert.equal(rows[0]?.amountText.includes("~"), false);
  assert.equal(rows[0]?.amountText.includes("84.50"), true);
  assert.equal(rows[0]?.whenText, "due in 6 days");
});

test("estimated dates and variable amounts read as estimates", () => {
  const rows = buildUpcomingCommitmentRows(
    [
      buildCommitment({
        id: "commitment-2",
        source: "inferred",
        kind: "recurring_pattern",
        dateCertainty: "estimated",
        amountCertainty: "estimated",
        isVariableAmount: true,
        nextDateUtc: "2026-08-17T00:00:00Z",
        nextAmount: 12.99,
        label: "Streaming service"
      })
    ],
    NOW_UTC_MS
  );

  assert.equal(rows[0]?.sourceLabel, "detected");
  assert.equal(rows[0]?.amountText.startsWith("~"), true);
  assert.equal(rows[0]?.whenText.startsWith("~"), true);
});

test("missing facts say pending instead of implying precision", () => {
  const rows = buildUpcomingCommitmentRows(
    [
      buildCommitment({
        id: "commitment-3",
        nextDateUtc: null,
        nextAmount: null,
        currency: null,
        lastObservedAmount: null,
        lastObservedCurrency: null
      })
    ],
    NOW_UTC_MS
  );

  assert.equal(rows[0]?.amountText, "Amount pending");
  assert.equal(rows[0]?.whenText, "date pending");
});

test("dismissed, inactive, and incoming commitments are excluded", () => {
  const rows = buildUpcomingCommitmentRows(
    [
      buildCommitment({
        id: "dismissed",
        userDecision: {
          state: "dismissed",
          decisionMode: "manual",
          lastAction: "dismiss",
          revision: 1,
          updatedUtc: "2026-07-18T00:00:00Z"
        }
      }),
      buildCommitment({ id: "ended", lifecycle: "ended" }),
      buildCommitment({ id: "income", direction: "incoming" }),
      buildCommitment({ id: "kept" })
    ],
    NOW_UTC_MS
  );

  assert.deepEqual(
    rows.map((row) => row.id),
    ["kept"]
  );
});

test("rows sort by next date with unknown dates last", () => {
  const rows = buildUpcomingCommitmentRows(
    [
      buildCommitment({ id: "later", nextDateUtc: "2026-08-01T00:00:00Z", label: "Later" }),
      buildCommitment({ id: "unknown", nextDateUtc: null, label: "Unknown" }),
      buildCommitment({ id: "sooner", nextDateUtc: "2026-07-21T00:00:00Z", label: "Sooner" })
    ],
    NOW_UTC_MS
  );

  assert.deepEqual(
    rows.map((row) => row.id),
    ["sooner", "later", "unknown"]
  );
});

test("last observed amount backs an estimate when next amount is unknown", () => {
  const rows = buildUpcomingCommitmentRows(
    [
      buildCommitment({
        id: "observed",
        nextAmount: null,
        currency: null,
        lastObservedAmount: 55.2,
        lastObservedCurrency: "EUR"
      })
    ],
    NOW_UTC_MS
  );

  assert.equal(rows[0]?.amountText.startsWith("~"), true);
  assert.equal(rows[0]?.amountText.includes("55.20"), true);
});
