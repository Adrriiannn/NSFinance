import assert from "node:assert/strict";
import test from "node:test";
import { getTransferPolicyEvaluation } from "./transferClassification";

test("stale linked transfer taxonomy is not treated as transfer policy when deterministic status rejected", () => {
  const evaluation = getTransferPolicyEvaluation({
    amount: -120,
    taxonomyDomainId: 920,
    taxonomyCategoryId: 92010,
    taxonomySubcategoryId: 920101,
    transferKind: "linked_internal_transfer",
    linkedTransferTransactionId: null,
    deterministicClassificationStatus: "evaluated_no_matching_rule",
    deterministicRelationshipType: null,
    transferPolicyKind: "bank_account_transfer",
    reportingBucket: "internal_transfer",
    isGloballyNeutralized: false
  });

  assert.equal(evaluation.policyKind, "none");
  assert.equal(evaluation.isTransferTransaction, false);
  assert.equal(evaluation.countsTowardExpense, true);
});

test("manual transfer policy remains intact when deterministic transfer matching is rejected", () => {
  const evaluation = getTransferPolicyEvaluation({
    amount: -40,
    taxonomyDomainId: 920,
    taxonomyCategoryId: 92010,
    taxonomySubcategoryId: 920101,
    transferKind: "manual_transfer",
    linkedTransferTransactionId: null,
    deterministicClassificationStatus: "evaluated_no_matching_rule",
    deterministicRelationshipType: null,
    transferPolicyKind: "bank_account_transfer",
    reportingBucket: "internal_transfer",
    isGloballyNeutralized: true
  });

  assert.equal(evaluation.policyKind, "bank_account_transfer");
  assert.equal(evaluation.isTransferTransaction, true);
});
