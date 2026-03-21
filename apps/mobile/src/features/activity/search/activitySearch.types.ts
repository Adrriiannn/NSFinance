import type { TransactionDto } from "../../../types/api";
import type { TransactionPlannerAnnotation } from "../../../providers/PlannerProvider";

export type ActivitySearchTokenType =
  | "transaction"
  | "category"
  | "merchant"
  | "currency"
  | "amount"
  | "date";

export type ActivityFilterOptionKey =
  | "transaction"
  | "category"
  | "merchant"
  | "amount"
  | "date";

export type ActivityCategoryScope = "domain" | "category" | "subcategory";

export type ActivityCategoryTokenValue = {
  subcategoryId: number | null;
  domainId: number | null;
  categoryId: number | null;
  domainName: string;
  categoryName: string;
  subcategoryName: string;
  scope: ActivityCategoryScope;
  excludedCategoryIds: number[];
  excludedSubcategoryIds: number[];
};

export type ActivityAmountTokenValue = {
  amount: number | null;
  rawAmount: string;
};

export type ActivityDateTokenValue =
  | {
      mode: "exact";
      isoDate: string;
      displayLabel: string;
    }
  | {
      mode: "weekday";
      weekday: number;
      displayLabel: string;
    };

export type ActivitySearchToken = {
  id: string;
  type: ActivitySearchTokenType;
  label: string;
  displayValue: string;
  rawValue: string;
  value:
    | string
    | ActivityCategoryTokenValue
    | ActivityAmountTokenValue
    | ActivityDateTokenValue;
  isDraft?: boolean;
};

export type ActivitySearchFilterOption = {
  key: ActivityFilterOptionKey;
  tokenType: ActivitySearchTokenType;
  title: string;
  hint: string;
};

export type ActivityMerchantDictionaryItem = {
  id: string;
  displayName: string;
  normalizedName: string;
  aliases: string[];
};

export type ActivityMerchantSuggestion = {
  displayName: string;
  normalizedName: string;
  score: number;
};

export type ActivityCurrencyPlacement = "prefix" | "suffix";

export type ActivityCurrencyMetadata = {
  code: string;
  symbol: string;
  placement: ActivityCurrencyPlacement;
};

export type ActivityDateSuggestion = {
  id: string;
  label: string;
  hintLabel?: string;
  mode: "exact" | "weekday";
  isoDate?: string;
  weekday?: number;
};

export type ActivityDateParseResult =
  | {
      kind: "exact";
      date: Date;
      displayLabel: string;
    }
  | {
      kind: "weekday";
      weekday: number;
      displayLabel: string;
    }
  | {
      kind: "none";
    };

export type ActivityDateSuggestionResult = {
  suggestions: ActivityDateSuggestion[];
  parseResult: ActivityDateParseResult;
};

export type ActivityTaxonomySearchEntry = {
  domainId: number;
  domainName: string;
  categoryId: number;
  categoryName: string;
  subcategoryId: number;
  subcategoryName: string;
};

export type ActivitySearchSnapshot = {
  tokens: ActivitySearchToken[];
  rawSearchText: string;
};

export type ActivitySearchFilterInput = {
  transactions: TransactionDto[];
  annotations: Record<string, TransactionPlannerAnnotation>;
  tokens: ActivitySearchToken[];
  freeText: string;
  taxonomyBySubcategoryId: Map<number, ActivityTaxonomySearchEntry>;
};

export type ActivitySearchDropdownMode =
  | "filters"
  | "merchantSuggestions"
  | "dateSuggestions"
  | "currencySuggestions"
  | "hidden";
