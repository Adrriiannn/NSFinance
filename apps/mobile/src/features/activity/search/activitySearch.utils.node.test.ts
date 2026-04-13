import assert from "node:assert/strict";
import test from "node:test";
import { resolveTransactionCategory } from "./activitySearch.utils";
import type { TransactionPlannerAnnotation } from "../../../providers/PlannerProvider";
import type { TransactionDto } from "../../../types/api";

function buildTransaction(overrides: Partial<TransactionDto>): TransactionDto {
  return {
    id: "tx-1",
    accountId: "acc-1",
    accountName: "Current account",
    description: "Sample transaction",
    amount: -1,
    currency: "EUR",
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

test("search category resolver suppresses stale transfer taxonomy labels for rejected transfer-family rows", () => {
  const transaction = buildTransaction({
    taxonomyDomainId: 920,
    taxonomyDomainName: "Transfers",
    taxonomyCategoryName: "Internal Transfers",
    taxonomySubcategoryName: "Bank Account Transfer",
    transferKind: "linked_internal_transfer",
    deterministicClassificationStatus: "rejected_ambiguous_match",
    deterministicRelationshipType: null
  });

  const category = resolveTransactionCategory(transaction, {} as Record<string, TransactionPlannerAnnotation>);

  assert.equal(category, "Uncategorized");
});

test("search category resolver keeps manual transfer categories", () => {
  const transaction = buildTransaction({
    taxonomyDomainId: 920,
    taxonomyCategoryName: "Internal Transfers",
    taxonomySubcategoryName: "Bank Account Transfer",
    transferKind: "manual_transfer",
    deterministicClassificationStatus: "evaluated_no_matching_rule",
    deterministicRelationshipType: null
  });

  const category = resolveTransactionCategory(transaction, {} as Record<string, TransactionPlannerAnnotation>);

  assert.equal(category, "Bank Account Transfer");
});
