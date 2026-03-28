import type { TransactionPlannerAnnotation } from "../../../providers/PlannerProvider";
import type { TransactionDto } from "../../../types/api";
import { formatShortDate } from "../../../lib/format";
import {
  formatActivityDateDisplay,
  toActivityIsoDate
} from "./activitySearch.parsers";
import { formatActivityTokenAmount } from "./activitySearch.currencies";
import type {
  ActivityCategoryTokenValue,
  ActivitySearchFilterInput,
  ActivitySearchToken,
  ActivityTaxonomySearchEntry
} from "./activitySearch.types";

export function normalizeActivitySearchText(value: string) {
  return value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-zA-Z0-9\s.:-]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();
}

export function resolveTransactionMerchant(
  transaction: TransactionDto,
  annotations: Record<string, TransactionPlannerAnnotation>
) {
  const annotationMerchant = annotations[transaction.id]?.merchant?.trim();
  if (annotationMerchant) {
    return annotationMerchant;
  }

  return transaction.description.trim();
}

export function resolveTransactionCategory(
  transaction: TransactionDto,
  annotations: Record<string, TransactionPlannerAnnotation>
) {
  return (
    annotations[transaction.id]?.category?.trim() ??
    transaction.taxonomySubcategoryName?.trim() ??
    transaction.taxonomyCategoryName?.trim() ??
    transaction.categoryName?.trim() ??
    "Uncategorized"
  );
}

function buildCategoryCandidates(
  value: ActivityCategoryTokenValue,
  taxonomyBySubcategoryId: Map<number, ActivityTaxonomySearchEntry>
) {
  const candidates = new Set<string>();
  const excludedCategoryIds = new Set(value.excludedCategoryIds ?? []);
  const excludedSubcategoryIds = new Set(value.excludedSubcategoryIds ?? []);
  const add = (label: string | null | undefined) => {
    const normalized = normalizeActivitySearchText(label ?? "");
    if (!normalized) {
      return;
    }

    candidates.add(normalized);
  };

  if (value.scope === "subcategory") {
    add(value.subcategoryName);
    const mapped = value.subcategoryId
      ? taxonomyBySubcategoryId.get(value.subcategoryId)
      : null;
    add(mapped?.subcategoryName);
  } else if (value.scope === "category") {
    let hasIncludedEntries = false;
    taxonomyBySubcategoryId.forEach((entry) => {
      if (value.categoryId !== null && entry.categoryId === value.categoryId) {
        if (excludedSubcategoryIds.has(entry.subcategoryId)) {
          return;
        }
        hasIncludedEntries = true;
        add(entry.categoryName);
        add(entry.subcategoryName);
      }
    });
    if (!hasIncludedEntries) {
      add(value.categoryName);
    }
  } else {
    let hasIncludedEntries = false;
    taxonomyBySubcategoryId.forEach((entry) => {
      if (value.domainId !== null && entry.domainId === value.domainId) {
        if (excludedCategoryIds.has(entry.categoryId)) {
          return;
        }
        if (excludedSubcategoryIds.has(entry.subcategoryId)) {
          return;
        }
        hasIncludedEntries = true;
        add(entry.categoryName);
        add(entry.subcategoryName);
      }
    });
    if (!hasIncludedEntries) {
      add(value.domainName);
    }
  }

  if (candidates.size === 0) {
    add(value.subcategoryName);
    add(value.categoryName);
    add(value.domainName);
  }

  return [...candidates];
}

function matchesCategoryToken(
  transaction: TransactionDto,
  annotations: Record<string, TransactionPlannerAnnotation>,
  token: ActivitySearchToken,
  taxonomyBySubcategoryId: Map<number, ActivityTaxonomySearchEntry>
) {
  const value = token.value as ActivityCategoryTokenValue;
  if (!value) {
    return true;
  }

  const transactionCategory = normalizeActivitySearchText(
    resolveTransactionCategory(transaction, annotations)
  );
  if (!transactionCategory) {
    return false;
  }

  const candidates = buildCategoryCandidates(value, taxonomyBySubcategoryId);
  return candidates.some(
    (candidate) =>
      transactionCategory === candidate ||
      transactionCategory.includes(candidate) ||
      candidate.includes(transactionCategory)
  );
}

function matchesDateToken(transaction: TransactionDto, token: ActivitySearchToken) {
  const value = token.value as
    | {
        mode: "exact";
        isoDate: string;
      }
    | {
        mode: "weekday";
        weekday: number;
      };
  const transactionDate = new Date(transaction.bookedAtUtc);

  if (value.mode === "exact") {
    return toActivityIsoDate(transactionDate) === value.isoDate;
  }

  return transactionDate.getDay() === value.weekday;
}

function matchesToken(
  transaction: TransactionDto,
  annotations: Record<string, TransactionPlannerAnnotation>,
  token: ActivitySearchToken,
  taxonomyBySubcategoryId: Map<number, ActivityTaxonomySearchEntry>
) {
  if (token.isDraft) {
    return true;
  }

  switch (token.type) {
    case "transaction": {
      const query = normalizeActivitySearchText(token.rawValue);
      if (!query) {
        return true;
      }

      return normalizeActivitySearchText(transaction.description).includes(query);
    }
    case "merchant": {
      const query = normalizeActivitySearchText(token.rawValue);
      if (!query) {
        return true;
      }

      const merchant = normalizeActivitySearchText(resolveTransactionMerchant(transaction, annotations));
      return merchant.includes(query) || query.includes(merchant);
    }
    case "category":
      return matchesCategoryToken(transaction, annotations, token, taxonomyBySubcategoryId);
    case "currency": {
      const code = String(token.value ?? "").toUpperCase();
      return code.length === 0 ? true : transaction.currency.toUpperCase() === code;
    }
    case "amount": {
      const amountValue = (token.value as { amount: number | null }).amount;
      if (amountValue === null || Number.isNaN(amountValue)) {
        return true;
      }

      return Number(transaction.amount.toFixed(2)) === Number(amountValue.toFixed(2));
    }
    case "date":
      return matchesDateToken(transaction, token);
    default:
      return true;
  }
}

function buildFreeSearchHaystack(
  transaction: TransactionDto,
  annotations: Record<string, TransactionPlannerAnnotation>
) {
  const merchant = resolveTransactionMerchant(transaction, annotations);
  const category = resolveTransactionCategory(transaction, annotations);
  const bookedDate = new Date(transaction.bookedAtUtc);
  const amount = formatActivityTokenAmount(transaction.amount, transaction.currency);
  const displayDate = formatActivityDateDisplay(bookedDate);

  return normalizeActivitySearchText(
    [
      transaction.description,
      merchant,
      category,
      transaction.accountName,
      transaction.currency,
      amount,
      displayDate,
      formatShortDate(transaction.bookedAtUtc),
      toActivityIsoDate(bookedDate)
    ].join(" ")
  );
}

function splitFreeTerms(freeText: string) {
  return normalizeActivitySearchText(freeText)
    .split(" ")
    .filter((item) => item.length > 0);
}

export function filterTransactionsByActivitySearch(input: ActivitySearchFilterInput) {
  const freeTerms = splitFreeTerms(input.freeText);
  if (input.tokens.length === 0 && freeTerms.length === 0) {
    return input.transactions;
  }

  return input.transactions.filter((transaction) => {
    const tokensMatch = input.tokens.every((token) =>
      matchesToken(transaction, input.annotations, token, input.taxonomyBySubcategoryId)
    );
    if (!tokensMatch) {
      return false;
    }

    if (freeTerms.length === 0) {
      return true;
    }

    const haystack = buildFreeSearchHaystack(transaction, input.annotations);
    return freeTerms.every((term) => haystack.includes(term));
  });
}
