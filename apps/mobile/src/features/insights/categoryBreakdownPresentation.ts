import type { InsightCategoryBreakdownDto, InsightCategoryPeriodDto } from "../../types/api";

// Pure presentation model for the register's category-bars block: pick the
// selected month out of the server series and shape the top categories into
// proportional bars, with the uncategorized remainder and coverage stated
// honestly rather than hidden.

export type CategoryBarModel = {
  key: string;
  label: string;
  amountText: string;
  share: number;
  transactionCount: number;
};

export type CategoryBreakdownBlockModel = {
  monthLabel: string;
  totalText: string;
  bars: CategoryBarModel[];
  remainingCategoryCount: number;
  remainingSpendText: string | null;
  uncategorized: { amountText: string; count: number; share: number } | null;
  coveragePercent: number;
  isPartial: boolean;
};

const TOP_BAR_COUNT = 5;

export function buildCategoryBreakdownBlock(
  breakdown: InsightCategoryBreakdownDto | undefined,
  currency: string,
  year: number,
  monthOneBased: number
): CategoryBreakdownBlockModel | null {
  const group = breakdown?.currencyGroups.find((candidate) => candidate.currency === currency)
    ?? breakdown?.currencyGroups[0];
  const period = group?.periods.find(
    (candidate) => candidate.year === year && candidate.month === monthOneBased
  );

  if (!group || !period || period.totalSpend <= 0) {
    return null;
  }

  return buildFromPeriod(period, group.currency);
}

function buildFromPeriod(
  period: InsightCategoryPeriodDto,
  currency: string
): CategoryBreakdownBlockModel {
  const formatAmount = (value: number) =>
    new Intl.NumberFormat("en-GB", { style: "currency", currency }).format(value);

  const top = period.categories.slice(0, TOP_BAR_COUNT);
  const remaining = period.categories.slice(TOP_BAR_COUNT);
  const remainingSpend = remaining.reduce((sum, category) => sum + category.spend, 0);

  const bars: CategoryBarModel[] = top.map((category) => ({
    key: `category-${category.taxonomyCategoryId}`,
    label: category.categoryName,
    amountText: formatAmount(category.spend),
    share: period.totalSpend > 0 ? category.spend / period.totalSpend : 0,
    transactionCount: category.transactionCount
  }));

  const monthLabel = new Date(Date.UTC(period.year, period.month - 1, 1)).toLocaleString("en-IE", {
    month: "long",
    timeZone: "UTC"
  });

  return {
    monthLabel,
    totalText: formatAmount(period.totalSpend),
    bars,
    remainingCategoryCount: remaining.length,
    remainingSpendText: remaining.length > 0 ? formatAmount(remainingSpend) : null,
    uncategorized:
      period.uncategorizedSpend > 0
        ? {
            amountText: formatAmount(period.uncategorizedSpend),
            count: period.uncategorizedTransactionCount,
            share: period.totalSpend > 0 ? period.uncategorizedSpend / period.totalSpend : 0
          }
        : null,
    coveragePercent:
      period.totalSpend > 0
        ? Math.round((period.categorizedSpend / period.totalSpend) * 100)
        : 0,
    isPartial: period.isPartial
  };
}
