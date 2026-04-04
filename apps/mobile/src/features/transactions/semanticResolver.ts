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

export type CanonicalTransactionSemantic = {
  family: CanonicalSemanticFamily;
  variant: CanonicalSemanticVariant;
  displaySubtitle: string | null;
  badgeText: string | null;
  styleKind: CanonicalSemanticStyleKind;
  analyticsNeutralized: boolean;
  reasonSource: CanonicalSemanticReasonSource;
  confidenceState: CanonicalSemanticConfidenceState;
};

const DEFAULT_SEMANTIC: CanonicalTransactionSemantic = {
  family: "none",
  variant: "none",
  displaySubtitle: null,
  badgeText: null,
  styleKind: "default",
  analyticsNeutralized: false,
  reasonSource: "none",
  confidenceState: "uncategorized"
};

export function resolveCanonicalTransactionSemantic(transaction: TransactionDto): CanonicalTransactionSemantic {
  if (transaction.deterministicClassificationStatus === "classified_matched_rule") {
    if (transaction.deterministicRelationshipType === "internal_transfer") {
      return {
        family: "internal_transfer",
        variant: "internal_transfer",
        displaySubtitle: "Bank account transfer",
        badgeText: "Linked transfer",
        styleKind: "internal_transfer",
        analyticsNeutralized: true,
        reasonSource: "deterministic",
        confidenceState: "matched"
      };
    }

    if (transaction.deterministicRelationshipType === "savings_transfer") {
      const isRoundup =
        transaction.deterministicClassificationReasonCode === "savings_context_nearby_spend"
        || transaction.displaySemantic === "savings_roundup";
      return {
        family: "savings_transfer",
        variant: isRoundup ? "savings_roundup" : "savings_manual_move",
        displaySubtitle: "Savings transfer",
        badgeText: "Savings transfer",
        styleKind: "savings_transfer",
        analyticsNeutralized: true,
        reasonSource: "deterministic",
        confidenceState: "matched"
      };
    }
  }

  if (transaction.displaySemantic === "internal_transfer") {
    return {
      family: "internal_transfer",
      variant: "internal_transfer",
      displaySubtitle: "Bank account transfer",
      badgeText: "Linked transfer",
      styleKind: "internal_transfer",
      analyticsNeutralized: true,
      reasonSource: "legacy_fallback",
      confidenceState: "matched"
    };
  }

  if (transaction.displaySemantic === "savings_roundup" || transaction.displaySemantic === "savings_manual_move") {
    return {
      family: "savings_transfer",
      variant: transaction.displaySemantic,
      displaySubtitle: "Savings transfer",
      badgeText: "Savings transfer",
      styleKind: "savings_transfer",
      analyticsNeutralized: true,
      reasonSource: "legacy_fallback",
      confidenceState: "matched"
    };
  }

  if (transaction.deterministicClassificationStatus === "deferred_waiting_for_counterparty"
    || transaction.deterministicClassificationStatus === "deferred_waiting_for_more_context") {
    return {
      ...DEFAULT_SEMANTIC,
      confidenceState: "deferred"
    };
  }

  if (transaction.deterministicClassificationStatus === "rejected_ambiguous_match") {
    return {
      ...DEFAULT_SEMANTIC,
      confidenceState: "ambiguous"
    };
  }

  return DEFAULT_SEMANTIC;
}
