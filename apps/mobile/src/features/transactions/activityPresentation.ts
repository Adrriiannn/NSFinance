import type { TransactionDto } from "../../types/api";
import { areDisplayLabelsMeaningfullyDistinct } from "./activityGrouping";
import type { CanonicalSemanticFamily, CanonicalTransactionSemantic } from "./semanticResolver";

export type TransactionLeadingVisual = {
  iconName: "swap-horizontal" | "wallet-outline" | "arrow-down" | "arrow-up" | "calculator-outline";
  backgroundColor: string;
  iconColor: string;
};

export type SemanticHelperLineInput = {
  metadataOverride?: string | null;
  hasCanonicalLabel: boolean;
  primaryLabel: string | null;
  semanticBadge: string | null;
  semanticFamily: CanonicalSemanticFamily;
};

const ICON_COLOR_WHITE = "#FFFFFF";
const ICON_BACKGROUND_EXPENSE = "rgba(226, 90, 90, 0.26)";
const ICON_BACKGROUND_INCOME = "rgba(29, 186, 114, 0.22)";
const ICON_BACKGROUND_SAVINGS = "rgba(90, 186, 226, 0.18)";
const ICON_BACKGROUND_ADJUSTMENT = "rgba(148, 163, 184, 0.2)";

export function resolveTransactionLeadingVisual(
  transaction: TransactionDto,
  semantic: CanonicalTransactionSemantic
): TransactionLeadingVisual {
  if (semantic.styleKind === "savings_transfer") {
    return {
      iconName: "wallet-outline",
      backgroundColor: ICON_BACKGROUND_SAVINGS,
      iconColor: ICON_COLOR_WHITE
    };
  }

  if (semantic.styleKind === "balance_adjustment") {
    return {
      iconName: "calculator-outline",
      backgroundColor: ICON_BACKGROUND_ADJUSTMENT,
      iconColor: ICON_COLOR_WHITE
    };
  }

  // Internal transfers intentionally follow normal directional styling.
  if (semantic.family === "internal_transfer") {
    return resolveDirectionalVisual(transaction.direction);
  }

  switch (semantic.iconKind) {
    case "expense":
      return resolveDirectionalVisual("Expense");
    case "income":
      return resolveDirectionalVisual("Income");
    case "savings":
      return {
        iconName: "wallet-outline",
        backgroundColor: ICON_BACKGROUND_SAVINGS,
        iconColor: ICON_COLOR_WHITE
      };
    case "transfer":
      return {
        iconName: "swap-horizontal",
        backgroundColor: ICON_BACKGROUND_INCOME,
        iconColor: ICON_COLOR_WHITE
      };
    case "adjustment":
      return {
        iconName: "calculator-outline",
        backgroundColor: ICON_BACKGROUND_ADJUSTMENT,
        iconColor: ICON_COLOR_WHITE
      };
    default:
      return resolveDirectionalVisual(transaction.direction);
  }
}

export function shouldRenderSemanticHelperLine({
  metadataOverride,
  hasCanonicalLabel,
  primaryLabel,
  semanticBadge,
  semanticFamily
}: SemanticHelperLineInput): boolean {
  if (metadataOverride) {
    return false;
  }

  if (!semanticBadge?.trim()) {
    return false;
  }

  if (hasCanonicalLabel) {
    return false;
  }

  // Hide transfer-family helper lines like "Linked transfer" / "Savings transfer".
  if (semanticFamily === "internal_transfer" || semanticFamily === "savings_transfer") {
    return false;
  }

  return areDisplayLabelsMeaningfullyDistinct(primaryLabel, semanticBadge);
}

function resolveDirectionalVisual(
  direction: TransactionDto["direction"]
): TransactionLeadingVisual {
  if (direction === "Expense") {
    return {
      iconName: "arrow-down",
      backgroundColor: ICON_BACKGROUND_EXPENSE,
      iconColor: ICON_COLOR_WHITE
    };
  }

  if (direction === "Adjustment") {
    return {
      iconName: "calculator-outline",
      backgroundColor: ICON_BACKGROUND_ADJUSTMENT,
      iconColor: ICON_COLOR_WHITE
    };
  }

  return {
    iconName: "arrow-up",
    backgroundColor: ICON_BACKGROUND_INCOME,
    iconColor: ICON_COLOR_WHITE
  };
}
