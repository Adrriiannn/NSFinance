import type { TransactionDto } from "../../types/api";
import {
  isReportableExpenseTransaction,
  isReportableIncomeTransaction
} from "../transactions/transferClassification";

const DAY_MS = 24 * 60 * 60 * 1000;

export type RecurringPaymentForecast = {
  id: string;
  label: string;
  amount: number;
  currency: string;
  nextDueUtc: string;
  daysUntilDue: number;
  cadenceLabel: string;
};

export type PlannerInsightBucket = "expense" | "income" | "net" | "event";

export type PlannerGraphModel = {
  currentMonthLabel: string;
  previousMonthLabel: string;
  displayCurrency: string;
  currentSeries: number[];
  previousSeries: number[];
  xCheckpoints: number[];
  yCheckpoints: number[];
  currentSpend: number;
  previousSpend: number;
  summaryText: string;
  bucket: PlannerInsightBucket;
  bucketTitle: string;
  bucketMessage: string;
};

export type PlannerComparisonPeriod = {
  year: number;
  month: number; // 0-indexed
};

function startOfMonth(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), 1, 0, 0, 0, 0);
}

function endOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}

function monthLabel(date: Date, includeYear = false) {
  return new Intl.DateTimeFormat("en-GB", {
    month: "long",
    ...(includeYear ? { year: "numeric" as const } : {})
  }).format(date);
}

function normalizeMerchantKey(raw: string) {
  return raw
    .toLowerCase()
    .replace(/\d+/g, "")
    .replace(/[^a-z\s]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function mean(values: number[]) {
  if (values.length === 0) {
    return 0;
  }

  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

function stdDeviation(values: number[]) {
  if (values.length <= 1) {
    return 0;
  }

  const avg = mean(values);
  const variance = mean(values.map((value) => (value - avg) ** 2));
  return Math.sqrt(variance);
}

function cadenceLabelFromDays(days: number) {
  if (days <= 8) {
    return "weekly";
  }

  if (days <= 16) {
    return "bi-weekly";
  }

  return "monthly";
}

function toNiceCheckpoint(value: number) {
  if (value <= 0) {
    return 0;
  }

  if (value < 50) {
    return Math.ceil(value / 10) * 10;
  }

  if (value < 250) {
    return Math.ceil(value / 25) * 25;
  }

  return Math.ceil(value / 50) * 50;
}

function buildCumulativeSpendSeries(transactions: TransactionDto[], monthStart: Date, days: number) {
  const totals = Array.from({ length: days }, () => 0);
  transactions.forEach((transaction) => {
    if (!isReportableExpenseTransaction(transaction)) {
      return;
    }

    const bookedAt = new Date(transaction.bookedAtUtc);
    if (bookedAt < monthStart) {
      return;
    }

    const dayIndex = bookedAt.getDate() - 1;
    if (dayIndex < 0 || dayIndex >= days) {
      return;
    }

    totals[dayIndex] += Math.abs(transaction.amount);
  });

  let running = 0;
  return totals.map((value) => {
    running += value;
    return Number(running.toFixed(2));
  });
}

function buildIncomeSummary(transactions: TransactionDto[], previousPeriod: TransactionDto[]) {
  const recurringKeywords = ["salary", "payroll", "wage", "pension", "benefit"];
  const previousIncomes = previousPeriod.filter((transaction) => isReportableIncomeTransaction(transaction));

  let recurringTotal = 0;
  let nonRecurringTotal = 0;

  transactions.forEach((transaction) => {
    if (transaction.amount <= 0) {
      return;
    }

    const normalizedDescription = transaction.description.toLowerCase();
    const directRecurring = recurringKeywords.some((keyword) =>
      normalizedDescription.includes(keyword)
    );
    const matchingHistorical = previousIncomes.some((previousIncome) => {
      if (
        normalizeMerchantKey(previousIncome.description) !==
        normalizeMerchantKey(transaction.description)
      ) {
        return false;
      }

      const amountGap = Math.abs(previousIncome.amount - transaction.amount);
      return amountGap <= Math.max(10, Math.abs(transaction.amount) * 0.15);
    });

    if (directRecurring || matchingHistorical) {
      recurringTotal += transaction.amount;
      return;
    }

    nonRecurringTotal += transaction.amount;
  });

  return {
    recurringTotal: Number(recurringTotal.toFixed(2)),
    nonRecurringTotal: Number(nonRecurringTotal.toFixed(2))
  };
}

export function buildRecurringPaymentForecast(
  transactions: TransactionDto[],
  now = new Date()
) {
  const currentMonthStart = startOfMonth(now);
  const nextMonthStart = startOfMonth(new Date(now.getFullYear(), now.getMonth() + 1, 1));

  const grouped = new Map<string, TransactionDto[]>();
  transactions
    .filter((transaction) => isReportableExpenseTransaction(transaction))
    .forEach((transaction) => {
      const key = normalizeMerchantKey(transaction.description);
      if (!key) {
        return;
      }

      const list = grouped.get(key) ?? [];
      list.push(transaction);
      grouped.set(key, list);
    });

  const forecasts: RecurringPaymentForecast[] = [];
  grouped.forEach((items, key) => {
    if (items.length < 2) {
      return;
    }

    const ordered = [...items].sort(
      (left, right) => new Date(left.bookedAtUtc).getTime() - new Date(right.bookedAtUtc).getTime()
    );

    const intervals: number[] = [];
    for (let index = 1; index < ordered.length; index += 1) {
      const previous = new Date(ordered[index - 1].bookedAtUtc);
      const current = new Date(ordered[index].bookedAtUtc);
      const diffDays = Math.round((current.getTime() - previous.getTime()) / DAY_MS);
      if (diffDays > 0 && diffDays <= 45) {
        intervals.push(diffDays);
      }
    }

    if (intervals.length === 0) {
      return;
    }

    const avgInterval = mean(intervals);
    const intervalStd = stdDeviation(intervals);
    const amounts = ordered.map((item) => Math.abs(item.amount));
    const amountStdRatio = stdDeviation(amounts) / Math.max(mean(amounts), 1);
    const supportedCadence =
      (avgInterval >= 6 && avgInterval <= 8) ||
      (avgInterval >= 13 && avgInterval <= 16) ||
      (avgInterval >= 25 && avgInterval <= 35);

    if (!supportedCadence || intervalStd > 6 || amountStdRatio > 0.24) {
      return;
    }

    const latest = ordered[ordered.length - 1];
    const nextDueDate = new Date(new Date(latest.bookedAtUtc).getTime() + Math.round(avgInterval) * DAY_MS);
    if (nextDueDate < currentMonthStart || nextDueDate >= nextMonthStart) {
      return;
    }

    const daysUntilDue = Math.ceil((endOfDay(nextDueDate).getTime() - now.getTime()) / DAY_MS);
    if (daysUntilDue < 0) {
      return;
    }

    forecasts.push({
      id: `${key}-${nextDueDate.toISOString()}`,
      label: latest.description,
      amount: Number(Math.abs(latest.amount).toFixed(2)),
      currency: latest.currency,
      nextDueUtc: nextDueDate.toISOString(),
      daysUntilDue,
      cadenceLabel: cadenceLabelFromDays(avgInterval)
    });
  });

  const orderedForecasts = forecasts.sort(
    (left, right) => new Date(left.nextDueUtc).getTime() - new Date(right.nextDueUtc).getTime()
  );

  return {
    next7Days: orderedForecasts.filter((item) => item.daysUntilDue <= 7),
    restOfMonth: orderedForecasts
  };
}

type PlannerGraphModelOptions = {
  currentPeriod: PlannerComparisonPeriod;
  previousPeriod: PlannerComparisonPeriod;
  now?: Date;
};

export function buildPlannerGraphModel(
  transactions: TransactionDto[],
  options: PlannerGraphModelOptions
): PlannerGraphModel {
  const now = options.now ?? new Date();
  const currentMonthAnchor = new Date(options.currentPeriod.year, options.currentPeriod.month, 1);
  const previousMonthAnchor = new Date(options.previousPeriod.year, options.previousPeriod.month, 1);
  const currentMonthStart = startOfMonth(currentMonthAnchor);
  const previousMonthStart = startOfMonth(previousMonthAnchor);
  const currentMonthDays = new Date(
    currentMonthAnchor.getFullYear(),
    currentMonthAnchor.getMonth() + 1,
    0
  ).getDate();
  const previousMonthDays = new Date(previousMonthAnchor.getFullYear(), previousMonthAnchor.getMonth() + 1, 0).getDate();
  const isCurrentPeriodThisMonth =
    currentMonthAnchor.getFullYear() === now.getFullYear() &&
    currentMonthAnchor.getMonth() === now.getMonth();
  const elapsedDay = isCurrentPeriodThisMonth ? now.getDate() : currentMonthDays;
  const comparableDays = Math.max(1, Math.min(elapsedDay, currentMonthDays, previousMonthDays));
  const comparableEndCurrent = endOfDay(
    new Date(currentMonthAnchor.getFullYear(), currentMonthAnchor.getMonth(), comparableDays)
  );
  const comparableEndPrevious = endOfDay(
    new Date(previousMonthAnchor.getFullYear(), previousMonthAnchor.getMonth(), comparableDays)
  );

  const currentComparable = transactions.filter((transaction) => {
    const bookedAt = new Date(transaction.bookedAtUtc);
    return bookedAt >= currentMonthStart && bookedAt <= comparableEndCurrent;
  });
  const previousComparable = transactions.filter((transaction) => {
    const bookedAt = new Date(transaction.bookedAtUtc);
    return bookedAt >= previousMonthStart && bookedAt <= comparableEndPrevious;
  });

  const currentSeries = buildCumulativeSpendSeries(currentComparable, currentMonthStart, comparableDays);
  const previousSeries = buildCumulativeSpendSeries(previousComparable, previousMonthStart, comparableDays);
  const currencyFrequency = new Map<string, number>();
  [...currentComparable, ...previousComparable].forEach((transaction) => {
    currencyFrequency.set(transaction.currency, (currencyFrequency.get(transaction.currency) ?? 0) + 1);
  });
  const displayCurrency =
    Array.from(currencyFrequency.entries()).sort((left, right) => right[1] - left[1])[0]?.[0] ?? "GBP";

  const currentSpend = Number(currentComparable.filter((transaction) => isReportableExpenseTransaction(transaction)).reduce((sum, transaction) => sum + Math.abs(transaction.amount), 0).toFixed(2));
  const previousSpend = Number(previousComparable.filter((transaction) => isReportableExpenseTransaction(transaction)).reduce((sum, transaction) => sum + Math.abs(transaction.amount), 0).toFixed(2));
  const currentIncome = Number(currentComparable.filter((transaction) => isReportableIncomeTransaction(transaction)).reduce((sum, transaction) => sum + transaction.amount, 0).toFixed(2));
  const previousIncome = Number(previousComparable.filter((transaction) => isReportableIncomeTransaction(transaction)).reduce((sum, transaction) => sum + transaction.amount, 0).toFixed(2));
  const currentNet = Number((currentIncome - currentSpend).toFixed(2));
  const previousNet = Number((previousIncome - previousSpend).toFixed(2));

  const spendDelta = Number((currentSpend - previousSpend).toFixed(2));
  const incomeDelta = Number((currentIncome - previousIncome).toFixed(2));
  const netDelta = Number((currentNet - previousNet).toFixed(2));
  const percentDelta =
    previousSpend > 0 ? Math.abs((spendDelta / previousSpend) * 100) : 0;

  const significantCurrent = currentComparable.reduce((largest, transaction) => {
    const candidate = Math.abs(transaction.amount);
    return candidate > largest ? candidate : largest;
  }, 0);

  const referenceDelta = Math.max(Math.abs(spendDelta), Math.abs(incomeDelta), Math.abs(netDelta), 1);
  const isEventDriven = significantCurrent >= referenceDelta * 0.75 && significantCurrent >= 120;
  const dominantBucket: PlannerInsightBucket = isEventDriven
    ? "event"
    : Math.abs(spendDelta) >= Math.abs(incomeDelta) && Math.abs(spendDelta) >= Math.abs(netDelta)
      ? "expense"
      : Math.abs(incomeDelta) >= Math.abs(spendDelta) && Math.abs(incomeDelta) >= Math.abs(netDelta)
        ? "income"
        : "net";

  const incomeSummary = buildIncomeSummary(currentComparable, previousComparable);

  let bucketTitle = "Net insight";
  let bucketMessage = "Net movement is currently the strongest driver versus last month.";
  if (dominantBucket === "expense") {
    bucketTitle = "Expense insight";
    bucketMessage =
      spendDelta > 0
        ? "Spending categories are the main reason this period is higher than last month."
        : "Lower spending categories are the biggest reason performance improved this month.";
  } else if (dominantBucket === "income") {
    bucketTitle = "Income insight";
    bucketMessage =
      incomeSummary.nonRecurringTotal > incomeSummary.recurringTotal
        ? "Income change is driven more by non-recurring inflows than regular salary-like income."
        : "Income change appears mostly linked to recurring salary-like inflows.";
  } else if (dominantBucket === "event") {
    bucketTitle = "Event-driven insight";
    bucketMessage = "A single large transaction is likely driving most of this month-on-month change.";
  }

  const maxYAxis = Math.max(...currentSeries, ...previousSeries, 1);
  const yCheckpoints = [toNiceCheckpoint(maxYAxis / 3), toNiceCheckpoint((maxYAxis * 2) / 3), toNiceCheckpoint(maxYAxis)]
    .filter((value, index, list) => value > 0 && list.indexOf(value) === index)
    .sort((left, right) => left - right);
  const xCheckpoints = [7, 14, 21, 28].filter((day) => day <= comparableDays);

  const summaryText =
    previousSpend > 0
      ? `So far this month you've spent ${new Intl.NumberFormat("en-GB", {
          style: "currency",
          currency: displayCurrency
        }).format(currentSpend)}, ${percentDelta.toFixed(0)}% ${spendDelta <= 0 ? "less" : "more"} than the same period last month.`
      : `So far this month you've spent ${new Intl.NumberFormat("en-GB", {
          style: "currency",
          currency: displayCurrency
        }).format(currentSpend)}. No comparable prior-period spend was found.`;

  return {
    currentMonthLabel: monthLabel(
      currentMonthAnchor,
      currentMonthAnchor.getFullYear() !== previousMonthAnchor.getFullYear()
    ),
    previousMonthLabel: monthLabel(
      previousMonthAnchor,
      previousMonthAnchor.getFullYear() !== currentMonthAnchor.getFullYear()
    ),
    displayCurrency,
    currentSeries,
    previousSeries,
    xCheckpoints,
    yCheckpoints,
    currentSpend,
    previousSpend,
    summaryText,
    bucket: dominantBucket,
    bucketTitle,
    bucketMessage
  };
}
