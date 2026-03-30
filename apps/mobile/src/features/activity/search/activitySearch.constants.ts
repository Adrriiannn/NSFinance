import type {
  ActivitySearchFilterOption,
  ActivitySearchTokenType
} from "./activitySearch.types";

export const ACTIVITY_SEARCH_TOKEN_LABELS: Record<ActivitySearchTokenType, string> = {
  transaction: "transaction",
  category: "category",
  merchant: "merchant",
  account: "account",
  currency: "currency",
  amount: "amount",
  date: "date"
};

export const ACTIVITY_SEARCH_UNIQUE_TOKEN_TYPES: ActivitySearchTokenType[] = [
  "transaction",
  "category",
  "merchant",
  "account",
  "currency",
  "amount",
  "date"
];

export const ACTIVITY_SEARCH_FILTER_OPTIONS: ActivitySearchFilterOption[] = [
  {
    key: "transaction",
    tokenType: "transaction",
    title: "Includes a certain transaction name",
    hint: "transaction: transaction's name"
  },
  {
    key: "category",
    tokenType: "category",
    title: "Includes a certain category",
    hint: "category: transaction's category"
  },
  {
    key: "merchant",
    tokenType: "merchant",
    title: "Made to a specific merchant",
    hint: "merchant: transaction's merchant"
  },
  {
    key: "account",
    tokenType: "account",
    title: "From a specific bank account",
    hint: "account: a bank account"
  },
  {
    key: "amount",
    tokenType: "amount",
    title: "Contains an exact amount",
    hint: "currency: EUR / amount: transaction's amount"
  },
  {
    key: "date",
    tokenType: "date",
    title: "Made on a specific date",
    hint: "date: transaction's date"
  }
];

export const ACTIVITY_SEARCH_DEBOUNCE_MS = 120;
export const ACTIVITY_SEARCH_MAX_MERCHANT_SUGGESTIONS = 7;
export const ACTIVITY_SEARCH_MAX_DATE_SUGGESTIONS = 8;
