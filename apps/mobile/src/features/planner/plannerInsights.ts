import type { DashboardSummaryDto, TransactionDto } from "../../types/api";
import type { TransactionPlannerAnnotation } from "../../providers/PlannerProvider";
import {
  isReportableExpenseTransaction,
  isTransferTransaction
} from "../transactions/transferClassification";

export type MonthComparison = {
  thisMonthSpend: number;
  lastMonthSpend: number;
  delta: number;
  trend: "improved" | "worse" | "flat";
};

export type PlannerSuggestion = {
  id: string;
  title: string;
  message: string;
};

export type HomeInsight = {
  id: string;
  title: string;
  message: string;
};

function getMostCommonCurrency(transactions: TransactionDto[]) {
  const frequency = new Map<string, number>();
  transactions.forEach((transaction) => {
    frequency.set(transaction.currency, (frequency.get(transaction.currency) ?? 0) + 1);
  });

  return (
    Array.from(frequency.entries()).sort((left, right) => right[1] - left[1])[0]?.[0] ?? "GBP"
  );
}

function formatCurrencyAmount(value: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(value);
}

export function getMonthComparison(transactions: TransactionDto[]): MonthComparison {
  const now = new Date();
  const startOfThisMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
  const startOfLastMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1));
  const startOfNextMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1));

  const thisMonthSpend = Math.abs(
    transactions
      .filter((tx) => {
        const date = new Date(tx.bookedAtUtc);
        return date >= startOfThisMonth && date < startOfNextMonth && isReportableExpenseTransaction(tx);
      })
      .reduce((sum, tx) => sum + tx.amount, 0)
  );

  const lastMonthSpend = Math.abs(
    transactions
      .filter((tx) => {
        const date = new Date(tx.bookedAtUtc);
        return date >= startOfLastMonth && date < startOfThisMonth && isReportableExpenseTransaction(tx);
      })
      .reduce((sum, tx) => sum + tx.amount, 0)
  );

  const delta = thisMonthSpend - lastMonthSpend;
  const trend: MonthComparison["trend"] =
    Math.abs(delta) < 0.01 ? "flat" : delta < 0 ? "improved" : "worse";

  return {
    thisMonthSpend,
    lastMonthSpend,
    delta,
    trend
  };
}

export function buildPlannerSuggestions(input: {
  dashboard: DashboardSummaryDto | undefined;
  transactions: TransactionDto[];
  annotations: Record<string, TransactionPlannerAnnotation>;
}): PlannerSuggestion[] {
  const suggestions: PlannerSuggestion[] = [];
  const monthComparison = getMonthComparison(input.transactions);
  const displayCurrency = getMostCommonCurrency(input.transactions);

  const unclassifiedCount = input.transactions.filter(
    (tx) => !input.annotations[tx.id]?.category
  ).length;

  if (unclassifiedCount > 0) {
    suggestions.push({
      id: "unclassified",
      title: `${unclassifiedCount} transactions still need a category`,
      message: "A few expenses are uncategorized. Review them so your planner stays accurate."
    });
  }

  const subscriptionsSpend = input.transactions
    .filter((tx) => isReportableExpenseTransaction(tx))
    .filter((tx) => {
      const category = (
        input.annotations[tx.id]?.category ??
        tx.categoryName ??
        tx.description
      ).toLowerCase();
      return category.includes("subscription");
    })
    .reduce((sum, tx) => sum + Math.abs(tx.amount), 0);
  const totalOutflow = input.transactions
    .filter((tx) => isReportableExpenseTransaction(tx))
    .reduce((sum, tx) => sum + Math.abs(tx.amount), 0);

  if (subscriptionsSpend > 0 && totalOutflow > 0) {
    suggestions.push({
      id: "subscriptions-share",
      title: "Subscriptions are taking a noticeable share",
      message: `Subscriptions now account for ${((subscriptionsSpend / totalOutflow) * 100).toFixed(0)}% of your tracked spending.`
    });
  }

  const currentMonthDining = input.transactions
    .filter((tx) => isReportableExpenseTransaction(tx))
    .filter((tx) => {
      const date = new Date(tx.bookedAtUtc);
      const now = new Date();
      return (
        date.getUTCFullYear() === now.getUTCFullYear() &&
        date.getUTCMonth() === now.getUTCMonth()
      );
    })
    .filter((tx) => {
      const category = (
        input.annotations[tx.id]?.category ??
        tx.categoryName ??
        tx.description
      ).toLowerCase();
      return category.includes("dining") || category.includes("restaurant");
    })
    .reduce((sum, tx) => sum + Math.abs(tx.amount), 0);

  if (monthComparison.trend === "worse" && currentMonthDining > 0) {
    suggestions.push({
      id: "dining-watch",
      title: "Dining out is pushing spend higher",
      message: `You have spent ${formatCurrencyAmount(currentMonthDining, displayCurrency)} on dining this month, which is likely pushing spending above last month.`
    });
  }

  if (suggestions.length === 0 && (input.dashboard?.recentOutflow ?? 0) > 0) {
    suggestions.push({
      id: "outflow",
      title: "Spending check-in",
      message: `Recent spending is ${formatCurrencyAmount(input.dashboard?.recentOutflow ?? 0, displayCurrency)}. Pick one high-spend category to review next.`
    });
  }

  return suggestions.slice(0, 4);
}

export function buildHomeInsights(input: {
  dashboard: DashboardSummaryDto | undefined;
  transactions: TransactionDto[];
  annotations: Record<string, TransactionPlannerAnnotation>;
}): HomeInsight[] {
  const insights: HomeInsight[] = [];
  const monthComparison = getMonthComparison(input.transactions);
  const balance = input.dashboard?.totalBalance ?? 0;

  if (monthComparison.trend === "worse") {
    insights.push({
      id: "spend-pace-up",
      title: "Spending pace is up",
      message:
        "This month is trending above last month. A small pullback on variable spending can protect your buffer."
    });
  } else if (monthComparison.trend === "improved") {
    insights.push({
      id: "spend-pace-down",
      title: "Spending pace improved",
      message:
        "You are running below last month so far. Keep this rhythm to create extra room for priorities."
    });
  }

  const discretionaryTags = ["coffee", "dining", "restaurant", "takeaway", "bar", "subscription"];
  const discretionaryCount = input.transactions.filter((tx) => {
    if (!isReportableExpenseTransaction(tx)) {
      return false;
    }
    if (isTransferTransaction(tx)) {
      return false;
    }

    const source = `${tx.description} ${tx.categoryName ?? input.annotations[tx.id]?.category ?? ""}`.toLowerCase();
    return discretionaryTags.some((tag) => source.includes(tag));
  }).length;

  if (discretionaryCount >= 4) {
    insights.push({
      id: "lifestyle-tuning",
      title: "Lifestyle tuning chance",
      message:
        "You have several variable spend events this cycle. Trimming one recurring habit can free up monthly savings."
    });
  }

  if (insights.length === 0) {
    insights.push({
      id: "decision-prompt",
      title: "Decision prompt",
      message:
        balance >= 0
          ? "Pick one money decision for today: reduce one variable expense or move a small amount into savings."
          : "Today focus on recovery: delay one variable expense and review upcoming commitments."
    });
  }

  return insights.slice(0, 3);
}
