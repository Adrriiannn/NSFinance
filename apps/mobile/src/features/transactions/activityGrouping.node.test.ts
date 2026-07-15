import assert from "node:assert/strict";
import test from "node:test";
import type { TransactionDto } from "../../types/api";
import {
  areDisplayLabelsMeaningfullyDistinct,
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
