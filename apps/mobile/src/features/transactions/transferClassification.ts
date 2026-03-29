import type { TransactionDto } from "../../types/api";

export const TRANSFER_DOMAIN_ID = 920;

const INTERNAL_TRANSFERS_CATEGORY_ID = 92010;
const LIABILITY_TRANSFERS_CATEGORY_ID = 92020;
const CASH_MOVEMENT_CATEGORY_ID = 92030;
const OTHER_TRANSFERS_CATEGORY_ID = 92040;

const BANK_ACCOUNT_TRANSFER_SUBCATEGORY_ID = 920101;
const SAVINGS_TRANSFER_SUBCATEGORY_ID = 920102;
const INVESTMENT_TRANSFER_SUBCATEGORY_ID = 920103;
const WALLET_TRANSFER_SUBCATEGORY_ID = 920104;

const CREDIT_CARD_PAYMENT_TRANSFER_SUBCATEGORY_ID = 920201;
const LOAN_ACCOUNT_TRANSFER_SUBCATEGORY_ID = 920202;
const DEBT_CONSOLIDATION_TRANSFER_SUBCATEGORY_ID = 920203;

const CASH_WITHDRAWAL_SUBCATEGORY_ID = 920301;
const CASH_DEPOSIT_SUBCATEGORY_ID = 920302;
const ATM_WITHDRAWAL_TRANSFER_SUBCATEGORY_ID = 920303;

const BROKERAGE_FUNDING_TRANSFER_SUBCATEGORY_ID = 920401;
const CURRENCY_TRANSFER_SUBCATEGORY_ID = 920402;
const OTHER_INTERNAL_MONEY_MOVEMENT_SUBCATEGORY_ID = 920403;

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

export type TransferPolicyInput = Pick<
  TransactionDto,
  | "amount"
  | "taxonomyDomainId"
  | "taxonomyCategoryId"
  | "taxonomySubcategoryId"
  | "transferKind"
  | "linkedTransferTransactionId"
  | "reportingBucket"
  | "isGloballyNeutralized"
  | "transferPolicyKind"
>;

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
  const policyKind = resolvePolicyKind(
    input.taxonomyDomainId,
    input.taxonomyCategoryId,
    input.taxonomySubcategoryId,
    input.transferKind ?? null
  );
  const isTransferTransaction = policyKind !== "none";
  const isManualUnverifiedTransfer = input.transferKind === "manual_transfer";
  const isVerifiedLinkedTransfer =
    input.transferKind === "linked_internal_transfer" && Boolean(input.linkedTransferTransactionId);
  const signedDirection = resolveSignedDirection(input.amount);

  if (!isTransferTransaction) {
    return {
      policyKind,
      reportingBucket: signedDirection === "outflow" ? "spending" : "income",
      isTransferTransaction: false,
      countsTowardExpense: signedDirection === "outflow",
      countsTowardIncome: signedDirection === "inflow",
      isGloballyNeutralized: false,
      isVerifiedLinkedTransfer: false,
      isManualUnverifiedTransfer: false,
      allowsManualNeutralization: false,
      requiresLinkedProofForNeutralization: false
    };
  }

  const allowsManualNeutralization = isManualNeutralizationAllowed(policyKind);
  const allowsVerifiedLinkedNeutralization = allowsVerifiedLinkedNeutralizationForKind(policyKind);
  const requiresLinkedProofForNeutralization = !allowsManualNeutralization && allowsVerifiedLinkedNeutralization;

  let isGloballyNeutralized = allowsVerifiedLinkedNeutralization && isVerifiedLinkedTransfer;
  if (!isGloballyNeutralized && allowsManualNeutralization && isManualUnverifiedTransfer) {
    isGloballyNeutralized = true;
  }

  if (!isGloballyNeutralized && input.isGloballyNeutralized === true) {
    isGloballyNeutralized = true;
  }

  if (isGloballyNeutralized) {
    return {
      policyKind,
      reportingBucket: resolveNeutralizedBucket(policyKind),
      isTransferTransaction: true,
      countsTowardExpense: false,
      countsTowardIncome: false,
      isGloballyNeutralized: true,
      isVerifiedLinkedTransfer,
      isManualUnverifiedTransfer,
      allowsManualNeutralization,
      requiresLinkedProofForNeutralization
    };
  }

  let countsTowardExpense = signedDirection === "outflow";
  let countsTowardIncome = signedDirection === "inflow";
  if (policyKind === "cash_deposit") {
    countsTowardIncome = false;
  }

  return {
    policyKind,
    reportingBucket: resolveNonNeutralBucket(policyKind, signedDirection, input.reportingBucket ?? null),
    isTransferTransaction: true,
    countsTowardExpense,
    countsTowardIncome,
    isGloballyNeutralized: false,
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
  if (!evaluation.isTransferTransaction) {
    return null;
  }

  if (evaluation.isGloballyNeutralized) {
    if (evaluation.isVerifiedLinkedTransfer) {
      return "Verified internal transfer: excluded from overall income and expense totals.";
    }

    if (evaluation.isManualUnverifiedTransfer) {
      return "Manual transfer override: excluded from overall totals. Use this only when money moved between your own accounts.";
    }

    return "This transfer is excluded from overall income and expense totals.";
  }

  if (evaluation.reportingBucket === "cash_out" || evaluation.reportingBucket === "cash_in") {
    return "Cash movements are not neutralized and remain reflected in your overall totals.";
  }

  if (evaluation.reportingBucket === "debt_payment") {
    return "Liability transfers are only neutralized when NSFinance verifies both linked sides.";
  }

  if (evaluation.reportingBucket === "investment_allocation") {
    return "Investment transfers remain in totals unless NSFinance verifies both linked sides.";
  }

  if (evaluation.requiresLinkedProofForNeutralization) {
    return "NSFinance cannot verify the linked destination/source, so this transfer remains in overall totals.";
  }

  return "Transfers remain visible in account history and may still affect overall totals depending on verification.";
}

function resolvePolicyKind(
  taxonomyDomainId: number | null,
  taxonomyCategoryId: number | null,
  taxonomySubcategoryId: number | null,
  transferKind: TransactionDto["transferKind"] | null
): TransferPolicyKind {
  if (taxonomySubcategoryId !== null) {
    switch (taxonomySubcategoryId) {
      case BANK_ACCOUNT_TRANSFER_SUBCATEGORY_ID:
        return "bank_account_transfer";
      case SAVINGS_TRANSFER_SUBCATEGORY_ID:
        return "savings_transfer";
      case INVESTMENT_TRANSFER_SUBCATEGORY_ID:
        return "investment_transfer";
      case WALLET_TRANSFER_SUBCATEGORY_ID:
        return "wallet_transfer";
      case CREDIT_CARD_PAYMENT_TRANSFER_SUBCATEGORY_ID:
        return "credit_card_payment_transfer";
      case LOAN_ACCOUNT_TRANSFER_SUBCATEGORY_ID:
        return "loan_account_transfer";
      case DEBT_CONSOLIDATION_TRANSFER_SUBCATEGORY_ID:
        return "debt_consolidation_transfer";
      case CASH_WITHDRAWAL_SUBCATEGORY_ID:
        return "cash_withdrawal";
      case CASH_DEPOSIT_SUBCATEGORY_ID:
        return "cash_deposit";
      case ATM_WITHDRAWAL_TRANSFER_SUBCATEGORY_ID:
        return "atm_withdrawal_transfer";
      case BROKERAGE_FUNDING_TRANSFER_SUBCATEGORY_ID:
        return "brokerage_funding_transfer";
      case CURRENCY_TRANSFER_SUBCATEGORY_ID:
        return "currency_transfer";
      case OTHER_INTERNAL_MONEY_MOVEMENT_SUBCATEGORY_ID:
        return "other_internal_money_movement";
      default:
        break;
    }
  }

  if (taxonomyCategoryId !== null) {
    switch (taxonomyCategoryId) {
      case INTERNAL_TRANSFERS_CATEGORY_ID:
        return "internal_transfer_generic";
      case LIABILITY_TRANSFERS_CATEGORY_ID:
        return "liability_transfer_generic";
      case CASH_MOVEMENT_CATEGORY_ID:
        return "cash_movement_generic";
      case OTHER_TRANSFERS_CATEGORY_ID:
        return "other_transfer_generic";
      default:
        break;
    }
  }

  if (taxonomyDomainId === TRANSFER_DOMAIN_ID || transferKind !== null) {
    return "internal_transfer_generic";
  }

  return "none";
}

function isManualNeutralizationAllowed(policyKind: TransferPolicyKind) {
  return (
    policyKind === "internal_transfer_generic"
    || policyKind === "bank_account_transfer"
    || policyKind === "savings_transfer"
    || policyKind === "wallet_transfer"
    || policyKind === "currency_transfer"
  );
}

function allowsVerifiedLinkedNeutralizationForKind(policyKind: TransferPolicyKind) {
  return !(
    policyKind === "none"
    || policyKind === "cash_movement_generic"
    || policyKind === "cash_withdrawal"
    || policyKind === "cash_deposit"
    || policyKind === "atm_withdrawal_transfer"
    || policyKind === "loan_account_transfer"
    || policyKind === "debt_consolidation_transfer"
    || policyKind === "other_internal_money_movement"
    || policyKind === "other_transfer_generic"
  );
}

function resolveNeutralizedBucket(policyKind: TransferPolicyKind): TransferReportingBucket {
  switch (policyKind) {
    case "credit_card_payment_transfer":
      return "debt_payment";
    case "savings_transfer":
      return "savings_allocation";
    case "investment_transfer":
    case "brokerage_funding_transfer":
      return "investment_allocation";
    default:
      return "internal_transfer";
  }
}

function resolveNonNeutralBucket(
  policyKind: TransferPolicyKind,
  signedDirection: SignedDirection,
  backendBucket: string | null
): TransferReportingBucket {
  if (isReportingBucket(backendBucket)) {
    return backendBucket;
  }

  switch (policyKind) {
    case "cash_withdrawal":
    case "atm_withdrawal_transfer":
      return "cash_out";
    case "cash_deposit":
      return "cash_in";
    case "credit_card_payment_transfer":
    case "loan_account_transfer":
    case "debt_consolidation_transfer":
    case "liability_transfer_generic":
      return "debt_payment";
    case "savings_transfer":
      return "savings_allocation";
    case "investment_transfer":
    case "brokerage_funding_transfer":
      return "investment_allocation";
    case "currency_transfer":
    case "internal_transfer_generic":
    case "bank_account_transfer":
    case "wallet_transfer":
      return "internal_transfer";
    default:
      return signedDirection === "outflow" ? "spending" : "income";
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

function isReportingBucket(value: string | null): value is TransferReportingBucket {
  return (
    value === "spending"
    || value === "income"
    || value === "internal_transfer"
    || value === "savings_allocation"
    || value === "investment_allocation"
    || value === "debt_payment"
    || value === "cash_out"
    || value === "cash_in"
  );
}
