import assert from "node:assert/strict";
import test from "node:test";
import type { TransactionDto } from "../../types/api";
import {
  areDisplayLabelsMeaningfullyDistinct,
  buildTransactionDetailDate,
  buildTransactionMetaLine,
  resolveTransactionDisplayLabel
} from "./activityGrouping";

function buildTransaction(overrides: Partial<TransactionDto>): TransactionDto {
  return {
    id: "tx-1",
    accountId: "acc-1",
    accountName: "Current account",
    description: "Sample transaction",
    amount: -1,
    currency: "EUR",
    entryKind: "ordinary",
    analyticsTreatment: "ordinary",
    categoryId: null,
    categoryName: null,
    taxonomyDomainId: null,
    taxonomyDomainName: null,
    taxonomyCategoryId: null,
    taxonomyCategoryName: null,
    taxonomySubcategoryId: null,
    taxonomySubcategoryName: null,
    transferKind: null,
    linkedTransferTransactionId: null,
    deterministicClassificationStatus: "not_evaluated",
    deterministicClassificationTerminal: false,
    deterministicClassificationVersion: null,
    deterministicClassificationRuleKey: null,
    deterministicClassificationReasonCode: null,
    deterministicClassificationEvidenceJson: null,
    deterministicDeferredRetryEligible: false,
    deterministicLinkedTransactionId: null,
    deterministicRelationshipType: null,
    deterministicRelationshipGroupId: null,
    relationshipType: null,
    relationshipStatus: null,
    relationshipDirection: null,
    relationshipConfidenceScore: null,
    relationshipConfidenceTier: null,
    relationshipAnalyticsTreatment: null,
    relationshipVirtualDestinationLabel: null,
    relationshipCounterpartyTransactionId: null,
    displaySemantic: null,
    transferPolicyKind: null,
    reportingBucket: null,
    isGloballyNeutralized: null,
    reason: null,
    notes: null,
    bookedAtUtc: "2026-04-09T10:30:00Z",
    createdUtc: "2026-04-09T10:30:00Z",
    metadataUpdatedUtc: null,
    direction: "Expense",
    accountSource: "manual",
    accountCurrency: "EUR",
    effectiveTime: {
      precision: "instant",
      date: null,
      instantUtc: "2026-04-09T10:30:00Z"
    },
    statementImport: null,
    ...overrides
  };
}

test("subcategory label has highest precedence over semantic fallback", () => {
  const transaction = buildTransaction({
    taxonomyCategoryName: "Cash Savings",
    taxonomySubcategoryName: "General Savings Transfer"
  });

  const resolution = resolveTransactionDisplayLabel(transaction, "Savings transfer");

  assert.equal(resolution.displayLabel, "General Savings Transfer");
  assert.equal(resolution.hasCanonicalLabel, true);
  assert.equal(buildTransactionMetaLine(transaction, "Savings transfer"), "General Savings Transfer");
});

test("category label is used when subcategory is absent", () => {
  const transaction = buildTransaction({
    taxonomyCategoryName: "Cash Savings",
    taxonomySubcategoryName: null
  });

  const resolution = resolveTransactionDisplayLabel(transaction, "Savings transfer");

  assert.equal(resolution.displayLabel, "Cash Savings");
  assert.equal(resolution.hasCanonicalLabel, true);
});

test("uncategorized rows keep fallback behavior", () => {
  const transaction = buildTransaction({});

  assert.equal(buildTransactionMetaLine(transaction, null), "Uncategorized");
});

test("semantic fallback is used only when canonical labels are missing", () => {
  const transaction = buildTransaction({
    taxonomyCategoryName: null,
    taxonomySubcategoryName: null
  });

  const resolution = resolveTransactionDisplayLabel(transaction, "Bank account transfer");

  assert.equal(resolution.displayLabel, "Bank account transfer");
  assert.equal(resolution.hasCanonicalLabel, false);
});

test("stale transfer taxonomy is suppressed when deterministic classification rejected transfer matching", () => {
  const transaction = buildTransaction({
    taxonomyDomainId: 920,
    taxonomyDomainName: "Transfers",
    taxonomyCategoryName: "Internal Transfers",
    taxonomySubcategoryName: "Bank Account Transfer",
    transferKind: "linked_internal_transfer",
    deterministicClassificationStatus: "evaluated_no_matching_rule",
    deterministicRelationshipType: null
  });

  const resolution = resolveTransactionDisplayLabel(transaction, null);

  assert.equal(resolution.displayLabel, "Uncategorized");
  assert.equal(resolution.hasCanonicalLabel, false);
});

test("manual transfer labeling is preserved when user selected manual transfer category", () => {
  const transaction = buildTransaction({
    taxonomyDomainId: 920,
    taxonomyDomainName: "Transfers",
    taxonomyCategoryName: "Internal Transfers",
    taxonomySubcategoryName: "Bank Account Transfer",
    transferKind: "manual_transfer",
    deterministicClassificationStatus: "evaluated_no_matching_rule",
    deterministicRelationshipType: null
  });

  const resolution = resolveTransactionDisplayLabel(transaction, null);

  assert.equal(resolution.displayLabel, "Bank Account Transfer");
  assert.equal(resolution.hasCanonicalLabel, true);
});

test("duplicate descriptor prevention treats near-identical labels as duplicates", () => {
  assert.equal(
    areDisplayLabelsMeaningfullyDistinct("General Savings Transfer", "Savings transfer"),
    false
  );
  assert.equal(
    areDisplayLabelsMeaningfullyDistinct("Savings transfer", "Savings transfer"),
    false
  );
  assert.equal(
    areDisplayLabelsMeaningfullyDistinct("Bank account transfer", "Linked transfer"),
    true
  );
});

test("date-precision transactions render the provider calendar day without a fabricated time", () => {
  const transaction = buildTransaction({
    bookedAtUtc: "2026-07-16T23:00:00Z",
    effectiveTime: {
      precision: "date",
      date: "2026-07-17",
      instantUtc: null
    }
  });

  const line = buildTransactionDetailDate(transaction);

  assert.equal(line.includes("|"), false, "date-precision rows must not render a time separator");
  assert.equal(line.includes("00:00"), false, "date-precision rows must not invent midnight");
  assert.equal(line.includes("17"), true, "the provider-authoritative calendar day must render");
  assert.equal(line.toLowerCase().includes("july"), true);
});

test("instant-precision transactions keep their real time display", () => {
  const transaction = buildTransaction({
    bookedAtUtc: "2026-07-17T14:23:00Z",
    effectiveTime: {
      precision: "instant",
      date: null,
      instantUtc: "2026-07-17T14:23:00Z"
    }
  });

  const line = buildTransactionDetailDate(transaction);

  assert.equal(line.includes("|"), true, "instant rows keep the date | time layout");
});

test("missing effectiveTime falls back to legacy rendering without crashing", () => {
  const transaction = buildTransaction({
    bookedAtUtc: "2026-07-17T14:23:00Z",
    effectiveTime: undefined as unknown as TransactionDto["effectiveTime"]
  });

  const line = buildTransactionDetailDate(transaction);

  assert.equal(typeof line, "string");
  assert.equal(line.includes("|"), true);
});
