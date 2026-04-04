import type { TransactionDto } from "../../types/api";

export type CanonicalSemanticFamily = "none" | "internal_transfer" | "savings_transfer";
export type CanonicalSemanticVariant =
  | "none"
  | "internal_transfer"
  | "savings_roundup"
  | "savings_manual_move";
export type CanonicalSemanticStyleKind = "default" | "internal_transfer" | "savings_transfer";
export type CanonicalSemanticReasonSource = "deterministic" | "legacy_fallback" | "none";
export type CanonicalSemanticConfidenceState = "matched" | "deferred" | "ambiguous" | "uncategorized";
export type CanonicalSemanticIconKind = "income" | "expense" | "transfer" | "savings";

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

export function resolveCanonicalTransactionSemantic(transaction: TransactionDto): CanonicalTransactionSemantic {
  const defaultIconKind: CanonicalSemanticIconKind = transaction.direction === "Expense" ? "expense" : "income";
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
        transaction.deterministicClassificationReasonCode === "savings_context_nearby_spend"
        || transaction.displaySemantic === "savings_roundup";
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

  const allowLegacyFallback =
    transaction.deterministicClassificationStatus === "not_evaluated"
    || transaction.deterministicClassificationStatus === "evaluating"
    || transaction.deterministicClassificationStatus === "superseded_recompute_required";

  if (allowLegacyFallback && transaction.displaySemantic === "internal_transfer") {
    return {
      family: "internal_transfer",
      variant: "internal_transfer",
      subtitle: "Bank account transfer",
      badgeText: "Linked transfer",
      styleKind: "internal_transfer",
      iconKind: "transfer",
      analyticsNeutralized: true,
      reasonSource: "legacy_fallback",
      confidenceState: "matched",
      isTransferLike: true,
      isSavingsLike: false
    };
  }

  if (allowLegacyFallback && (transaction.displaySemantic === "savings_roundup" || transaction.displaySemantic === "savings_manual_move")) {
    return {
      family: "savings_transfer",
      variant: transaction.displaySemantic,
      subtitle: "Savings transfer",
      badgeText: "Savings transfer",
      styleKind: "savings_transfer",
      iconKind: "savings",
      analyticsNeutralized: true,
      reasonSource: "legacy_fallback",
      confidenceState: "matched",
      isTransferLike: true,
      isSavingsLike: true
    };
  }

  return defaultSemantic;
}
