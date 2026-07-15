import type { TransactionDto } from "../../types/api";

export type CanonicalSemanticFamily = "none" | "internal_transfer" | "savings_transfer" | "balance_adjustment";
export type CanonicalSemanticVariant =
  | "none"
  | "internal_transfer"
  | "savings_roundup"
  | "savings_manual_move"
  | "opening_balance_adjustment"
  | "balance_adjustment";
export type CanonicalSemanticStyleKind = "default" | "internal_transfer" | "savings_transfer" | "balance_adjustment";
export type CanonicalSemanticReasonSource = "deterministic" | "legacy_fallback" | "provenance" | "none";
export type CanonicalSemanticConfidenceState = "matched" | "deferred" | "ambiguous" | "uncategorized";
export type CanonicalSemanticIconKind = "income" | "expense" | "transfer" | "savings" | "adjustment";
export type CanonicalPresentationStyleSource = "deterministic_semantic" | "taxonomy_fallback";

export type CanonicalTransactionSemantic = {
  family: CanonicalSemanticFamily;
  variant: CanonicalSemanticVariant;
  subtitle: string | null;
  badgeText: string | null;
  styleKind: CanonicalSemanticStyleKind;
  iconKind: CanonicalSemanticIconKind;
  analyticsNeutralized: boolean;
  reasonSource: CanonicalSemanticReasonSource;
  confidenceState: CanonicalSemanticConfidenceState;
  isTransferLike: boolean;
  isSavingsLike: boolean;
};

export type CanonicalPresentationDiagnostics = {
  deterministicSemanticFamily: CanonicalSemanticFamily;
  taxonomyCategory: string | null;
  taxonomySubcategory: string | null;
  stylingSource: CanonicalPresentationStyleSource;
  taxonomyFallbackUsed: boolean;
};

const TRANSFER_DOMAIN_ID = 920;
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

export function resolveCanonicalTransactionSemantic(transaction: TransactionDto): CanonicalTransactionSemantic {
  const balanceAdjustment = transaction.analyticsTreatment === "balance_only"
    || transaction.displaySemantic === "balance_adjustment";
  if (balanceAdjustment) {
    const openingBalance = transaction.entryKind === "opening_balance_adjustment";
    return {
      family: "balance_adjustment",
      variant: openingBalance ? "opening_balance_adjustment" : "balance_adjustment",
      subtitle: openingBalance ? "Starting balance" : "Balance adjustment",
      badgeText: "Balance only",
      styleKind: "balance_adjustment",
      iconKind: "adjustment",
      analyticsNeutralized: true,
      reasonSource: "provenance",
      confidenceState: "matched",
      isTransferLike: false,
      isSavingsLike: false
    };
  }

  const defaultIconKind: CanonicalSemanticIconKind = transaction.direction === "Expense"
    ? "expense"
    : transaction.direction === "Income"
      ? "income"
      : "adjustment";
  const defaultSemantic: CanonicalTransactionSemantic = {
    family: "none",
    variant: "none",
    subtitle: null,
    badgeText: null,
    styleKind: "default",
    iconKind: defaultIconKind,
    analyticsNeutralized: false,
    reasonSource: "none",
    confidenceState: "uncategorized",
    isTransferLike: false,
    isSavingsLike: false
  };

  if (transaction.deterministicClassificationStatus === "classified_matched_rule") {
    if (transaction.deterministicRelationshipType === "internal_transfer") {
      return {
        family: "internal_transfer",
        variant: "internal_transfer",
        subtitle: "Bank account transfer",
        badgeText: "Linked transfer",
        styleKind: "internal_transfer",
        iconKind: "transfer",
        analyticsNeutralized: true,
        reasonSource: "deterministic",
        confidenceState: "matched",
        isTransferLike: true,
        isSavingsLike: false
      };
    }

    if (transaction.deterministicRelationshipType === "savings_transfer") {
      const isRoundup =
        transaction.deterministicClassificationReasonCode === "savings_context_nearby_spend";
      return {
        family: "savings_transfer",
        variant: isRoundup ? "savings_roundup" : "savings_manual_move",
        subtitle: "Savings transfer",
        badgeText: "Savings transfer",
        styleKind: "savings_transfer",
        iconKind: "savings",
        analyticsNeutralized: true,
        reasonSource: "deterministic",
        confidenceState: "matched",
        isTransferLike: true,
        isSavingsLike: true
      };
    }

    return {
      ...defaultSemantic,
      reasonSource: "deterministic",
      confidenceState: "matched"
    };
  }

  if (transaction.deterministicClassificationStatus === "deferred_waiting_for_counterparty"
    || transaction.deterministicClassificationStatus === "deferred_waiting_for_more_context") {
    return {
      ...defaultSemantic,
      reasonSource: "deterministic",
      confidenceState: "deferred"
    };
  }

  if (transaction.deterministicClassificationStatus === "rejected_ambiguous_match") {
    return {
      ...defaultSemantic,
      reasonSource: "deterministic",
      confidenceState: "ambiguous"
    };
  }

  return defaultSemantic;
}

export function shouldSuppressTransferLikeTaxonomyFallback(transaction: TransactionDto): boolean {
  if (!TRANSFER_SEMANTIC_MISMATCH_STATUSES.has(transaction.deterministicClassificationStatus)) {
    return false;
  }

  if (transaction.transferKind === "manual_transfer") {
    return false;
  }

  if (transaction.deterministicClassificationStatus === "classified_matched_rule") {
    return false;
  }

  if (isTransferLikeTaxonomy(transaction)) {
    return true;
  }

  return transaction.transferKind === "linked_internal_transfer"
    || transaction.transferKind === "savings_roundup"
    || transaction.transferKind === "savings_manual_deposit"
    || transaction.transferKind === "savings_manual_withdrawal";
}

export function isTransferLikeLabel(label: string | null | undefined): boolean {
  const normalized = normalizeLabel(label);
  if (!normalized) {
    return false;
  }

  return normalized.includes("transfer")
    || normalized.includes("round up")
    || normalized.includes("roundup")
    || normalized.includes("internal move")
    || normalized.includes("money movement");
}

export function resolveCanonicalPresentationDiagnostics(
  transaction: TransactionDto,
  semantic: CanonicalTransactionSemantic = resolveCanonicalTransactionSemantic(transaction)
): CanonicalPresentationDiagnostics {
  const stylingSource: CanonicalPresentationStyleSource =
    semantic.family === "none" ? "taxonomy_fallback" : "deterministic_semantic";

  return {
    deterministicSemanticFamily: semantic.family,
    taxonomyCategory: transaction.taxonomyCategoryName ?? null,
    taxonomySubcategory: transaction.taxonomySubcategoryName ?? null,
    stylingSource,
    taxonomyFallbackUsed: stylingSource === "taxonomy_fallback"
  };
}

function isTransferLikeTaxonomy(transaction: TransactionDto): boolean {
  if (transaction.taxonomyDomainId === TRANSFER_DOMAIN_ID) {
    return true;
  }

  if (transaction.taxonomyCategoryId !== null && TRANSFER_CATEGORY_IDS.has(transaction.taxonomyCategoryId)) {
    return true;
  }

  return transaction.taxonomySubcategoryId !== null && TRANSFER_SUBCATEGORY_IDS.has(transaction.taxonomySubcategoryId);
}

function normalizeLabel(value: string | null | undefined): string {
  return value?.trim().toLowerCase() ?? "";
}
