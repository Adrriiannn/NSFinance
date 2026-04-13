import type { TransactionDto } from "../../types/api";
import { resolveCanonicalTransactionSemantic } from "./semanticResolver";

export type TransferPolicyKind =
  | "none"
  | "internal_transfer_generic"
  | "bank_account_transfer"
  | "savings_transfer"
  | "investment_transfer"
  | "wallet_transfer"
  | "credit_card_payment_transfer"
  | "loan_account_transfer"
  | "debt_consolidation_transfer"
  | "cash_movement_generic"
  | "cash_withdrawal"
  | "cash_deposit"
  | "atm_withdrawal_transfer"
  | "liability_transfer_generic"
  | "brokerage_funding_transfer"
  | "currency_transfer"
  | "other_internal_money_movement"
  | "other_transfer_generic";

export type TransferReportingBucket =
  | "spending"
  | "income"
  | "internal_transfer"
  | "savings_allocation"
  | "investment_allocation"
  | "debt_payment"
  | "cash_out"
  | "cash_in";

export const TRANSFER_DOMAIN_ID = 920;
const TRANSFER_CATEGORY_IDS = new Set([92010, 92020, 92030, 92040]);
const TRANSFER_SUBCATEGORY_IDS = new Set([
  920101,
  920102,
  920103,
  920104,
  920201,
  920202,
  920203,
  920301,
  920302,
  920303,
  920401,
  920402,
  920403
]);
const TRANSFER_SEMANTIC_MISMATCH_STATUSES = new Set([
  "evaluated_no_matching_rule",
  "deferred_waiting_for_counterparty",
  "deferred_waiting_for_more_context",
  "rejected_ambiguous_match",
  "superseded_recompute_required"
]);

export type TransferPolicyInput = Pick<
  TransactionDto,
  | "amount"
  | "taxonomyDomainId"
  | "taxonomyCategoryId"
  | "taxonomySubcategoryId"
  | "transferKind"
  | "linkedTransferTransactionId"
  | "transferPolicyKind"
  | "reportingBucket"
  | "isGloballyNeutralized"
> & Partial<Pick<TransactionDto, "deterministicClassificationStatus" | "deterministicRelationshipType">>;

export type TransferPolicyEvaluation = {
  policyKind: TransferPolicyKind;
  reportingBucket: TransferReportingBucket;
  isTransferTransaction: boolean;
  countsTowardExpense: boolean;
  countsTowardIncome: boolean;
  isGloballyNeutralized: boolean;
  isVerifiedLinkedTransfer: boolean;
  isManualUnverifiedTransfer: boolean;
  allowsManualNeutralization: boolean;
  requiresLinkedProofForNeutralization: boolean;
};

type SignedDirection = "none" | "outflow" | "inflow";

export function getTransferPolicyEvaluation(input: TransferPolicyInput): TransferPolicyEvaluation {
  const policyKind = shouldSuppressStaleTransferPolicy(input)
    ? "none"
    : normalizePolicyKind(input.transferPolicyKind);
  const signedDirection = resolveSignedDirection(input.amount);
  const reportingBucket: TransferReportingBucket = policyKind === "none"
    ? (signedDirection === "inflow" ? "income" : "spending")
    : normalizeReportingBucket(input.reportingBucket);
  const isTransferTransaction = policyKind !== "none";
  const isVerifiedLinkedTransfer =
    input.deterministicClassificationStatus === "classified_matched_rule"
    && input.deterministicRelationshipType === "internal_transfer";
  const isManualUnverifiedTransfer =
    input.transferKind === "manual_transfer" && !isVerifiedLinkedTransfer;
  const allowsManualNeutralization = false;
  const requiresLinkedProofForNeutralization = false;
  const isGloballyNeutralized =
    input.isGloballyNeutralized === true
    || (reportingBucket === "internal_transfer"
      || reportingBucket === "savings_allocation"
      || reportingBucket === "investment_allocation")
      && isTransferTransaction;

  const countsTowardExpense =
    !isGloballyNeutralized
    && (reportingBucket === "spending" || reportingBucket === "debt_payment" || reportingBucket === "cash_out"
      || (reportingBucket === "income" && signedDirection === "outflow"));
  const countsTowardIncome =
    !isGloballyNeutralized
    && (reportingBucket === "income" || reportingBucket === "cash_in"
      || (reportingBucket === "spending" && signedDirection === "inflow"));

  return {
    policyKind,
    reportingBucket,
    isTransferTransaction,
    countsTowardExpense,
    countsTowardIncome,
    isGloballyNeutralized,
    isVerifiedLinkedTransfer,
    isManualUnverifiedTransfer,
    allowsManualNeutralization,
    requiresLinkedProofForNeutralization
  };
}

export function isTransferTransaction(transaction: TransactionDto) {
  return getTransferPolicyEvaluation(transaction).isTransferTransaction;
}

export function isReportableExpenseTransaction(transaction: TransactionDto) {
  return getTransferPolicyEvaluation(transaction).countsTowardExpense;
}

export function isReportableIncomeTransaction(transaction: TransactionDto) {
  return getTransferPolicyEvaluation(transaction).countsTowardIncome;
}

export function getTransferPolicyWarning(evaluation: TransferPolicyEvaluation): string | null {
  return getTransferPolicyWarningForSemantic(evaluation, null);
}

export function getTransferPolicyWarningForTransaction(transaction: TransactionDto): string | null {
  const evaluation = getTransferPolicyEvaluation(transaction);
  const semantic = resolveCanonicalTransactionSemantic(transaction);
  return getTransferPolicyWarningForSemantic(evaluation, semantic.family);
}

function getTransferPolicyWarningForSemantic(
  evaluation: TransferPolicyEvaluation,
  semanticFamily: "none" | "internal_transfer" | "savings_transfer" | null): string | null {
  if (!evaluation.isTransferTransaction) {
    return null;
  }

  if (semanticFamily === "savings_transfer") {
    return "Savings movement: visible in activity, excluded from overall income and expense totals.";
  }

  if (semanticFamily === "internal_transfer") {
    return "Verified internal transfer: excluded from overall income and expense totals.";
  }

  if (evaluation.isGloballyNeutralized) {
    if (evaluation.reportingBucket === "savings_allocation") {
      return "Savings movement: visible in activity, excluded from overall income and expense totals.";
    }

    if (evaluation.isVerifiedLinkedTransfer) {
      return "Verified internal transfer: excluded from overall income and expense totals.";
    }

    return "This transfer is excluded from overall income and expense totals.";
  }

  if (evaluation.reportingBucket === "cash_out" || evaluation.reportingBucket === "cash_in") {
    return "Cash movements remain reflected in your overall totals.";
  }

  return "Transfer impact is determined by persisted categorization state.";
}

function normalizePolicyKind(value: string | null | undefined): TransferPolicyKind {
  if (!value) {
    return "none";
  }

  switch (value) {
    case "internal_transfer_generic":
    case "bank_account_transfer":
    case "savings_transfer":
    case "investment_transfer":
    case "wallet_transfer":
    case "credit_card_payment_transfer":
    case "loan_account_transfer":
    case "debt_consolidation_transfer":
    case "cash_movement_generic":
    case "cash_withdrawal":
    case "cash_deposit":
    case "atm_withdrawal_transfer":
    case "liability_transfer_generic":
    case "brokerage_funding_transfer":
    case "currency_transfer":
    case "other_internal_money_movement":
    case "other_transfer_generic":
    case "none":
      return value;
    default:
      return "none";
  }
}

function shouldSuppressStaleTransferPolicy(input: TransferPolicyInput): boolean {
  if (!TRANSFER_SEMANTIC_MISMATCH_STATUSES.has(input.deterministicClassificationStatus ?? "")) {
    return false;
  }

  if (input.transferKind === "manual_transfer") {
    return false;
  }

  if (input.deterministicClassificationStatus === "classified_matched_rule") {
    return false;
  }

  if (input.transferKind === "linked_internal_transfer"
    || input.transferKind === "savings_roundup"
    || input.transferKind === "savings_manual_deposit"
    || input.transferKind === "savings_manual_withdrawal") {
    return true;
  }

  if (input.taxonomyDomainId === TRANSFER_DOMAIN_ID) {
    return true;
  }

  if (input.taxonomyCategoryId !== null && input.taxonomyCategoryId !== undefined
    && TRANSFER_CATEGORY_IDS.has(input.taxonomyCategoryId)) {
    return true;
  }

  return input.taxonomySubcategoryId !== null
    && input.taxonomySubcategoryId !== undefined
    && TRANSFER_SUBCATEGORY_IDS.has(input.taxonomySubcategoryId);
}

function normalizeReportingBucket(value: string | null | undefined): TransferReportingBucket {
  switch (value) {
    case "internal_transfer":
    case "savings_allocation":
    case "investment_allocation":
    case "debt_payment":
    case "cash_out":
    case "cash_in":
    case "spending":
    case "income":
      return value;
    default:
      return "spending";
  }
}

function resolveSignedDirection(amount: number): SignedDirection {
  if (amount < 0) {
    return "outflow";
  }

  if (amount > 0) {
    return "inflow";
  }

  return "none";
}
