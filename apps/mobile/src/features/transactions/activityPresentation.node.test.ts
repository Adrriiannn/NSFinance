import assert from "node:assert/strict";
import test from "node:test";
import type { TransactionDto } from "../../types/api";
import { resolveTransactionDisplayLabel } from "./activityGrouping";
import {
  resolveTransactionLeadingVisual,
  shouldRenderSemanticHelperLine
} from "./activityPresentation";
import { resolveCanonicalTransactionSemantic } from "./semanticResolver";

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

test("internal transfer outflow uses standard expense directional icon styling", () => {
  const transaction = buildTransaction({
    deterministicClassificationStatus: "classified_matched_rule",
    deterministicRelationshipType: "internal_transfer",
    taxonomySubcategoryName: "Bank Account Transfer",
    direction: "Expense"
  });
  const semantic = resolveCanonicalTransactionSemantic(transaction);

  const visual = resolveTransactionLeadingVisual(transaction, semantic);

  assert.equal(visual.iconName, "arrow-down");
  assert.equal(visual.iconColor, "#FFFFFF");
  assert.equal(visual.backgroundColor, "rgba(226, 90, 90, 0.26)");
});

test("internal transfer inflow uses standard income directional icon styling", () => {
  const transaction = buildTransaction({
    deterministicClassificationStatus: "classified_matched_rule",
    deterministicRelationshipType: "internal_transfer",
    taxonomySubcategoryName: "Bank Account Transfer",
    direction: "Income"
  });
  const semantic = resolveCanonicalTransactionSemantic(transaction);

  const visual = resolveTransactionLeadingVisual(transaction, semantic);

  assert.equal(visual.iconName, "arrow-up");
  assert.equal(visual.iconColor, "#FFFFFF");
  assert.equal(visual.backgroundColor, "rgba(29, 186, 114, 0.22)");
});

test("opening balance uses neutral adjustment semantics and icon styling", () => {
  const transaction = buildTransaction({
    amount: 1_250,
    entryKind: "opening_balance_adjustment",
    analyticsTreatment: "balance_only",
    displaySemantic: "balance_adjustment",
    reportingBucket: "balance_only",
    isGloballyNeutralized: true,
    direction: "Adjustment"
  });
  const semantic = resolveCanonicalTransactionSemantic(transaction);

  const visual = resolveTransactionLeadingVisual(transaction, semantic);

  assert.equal(semantic.family, "balance_adjustment");
  assert.equal(semantic.variant, "opening_balance_adjustment");
  assert.equal(semantic.subtitle, "Starting balance");
  assert.equal(semantic.analyticsNeutralized, true);
  assert.equal(visual.iconName, "calculator-outline");
  assert.equal(visual.backgroundColor, "rgba(148, 163, 184, 0.2)");
});

test("bank account transfers do not render lower linked-transfer helper line", () => {
  const transaction = buildTransaction({
    deterministicClassificationStatus: "classified_matched_rule",
    deterministicRelationshipType: "internal_transfer",
    taxonomySubcategoryName: "Bank Account Transfer"
  });
  const semantic = resolveCanonicalTransactionSemantic(transaction);
  const labelResolution = resolveTransactionDisplayLabel(transaction, semantic.subtitle);

  const showHelperLine = shouldRenderSemanticHelperLine({
    metadataOverride: null,
    hasCanonicalLabel: labelResolution.hasCanonicalLabel,
    primaryLabel: labelResolution.displayLabel,
    semanticBadge: semantic.badgeText,
    semanticFamily: semantic.family
  });

  assert.equal(labelResolution.displayLabel, "Bank account transfer");
  assert.equal(showHelperLine, false);
});

test("semantic helper line can still appear for non-transfer fallback-only scenarios", () => {
  const showHelperLine = shouldRenderSemanticHelperLine({
    metadataOverride: null,
    hasCanonicalLabel: false,
    primaryLabel: "Uncategorized",
    semanticBadge: "Potential duplicate",
    semanticFamily: "none"
  });

  assert.equal(showHelperLine, true);
});

test("rejected transfer-family rows do not render transfer taxonomy labels as canonical subtitles", () => {
  const transaction = buildTransaction({
    deterministicClassificationStatus: "rejected_ambiguous_match",
    deterministicRelationshipType: null,
    taxonomyDomainId: 920,
    taxonomyDomainName: "Transfers",
    taxonomyCategoryName: "Internal Transfers",
    taxonomySubcategoryName: "Bank Account Transfer",
    transferKind: "linked_internal_transfer"
  });
  const semantic = resolveCanonicalTransactionSemantic(transaction);
  const labelResolution = resolveTransactionDisplayLabel(transaction, semantic.subtitle);

  assert.equal(semantic.family, "none");
  assert.equal(labelResolution.displayLabel, "Uncategorized");
  assert.equal(labelResolution.hasCanonicalLabel, false);
});
