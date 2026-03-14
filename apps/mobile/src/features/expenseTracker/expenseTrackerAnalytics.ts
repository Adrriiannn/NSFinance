import type { ExpenseTrackerEntryDto } from "../../types/api";

export type ExpenseTrackerPeriodMode = "weekly" | "monthly";

export type ExpenseTrackerPeriodRange = {
  mode: ExpenseTrackerPeriodMode;
  start: Date;
  end: Date;
  comparisonStart: Date;
  comparisonEnd: Date;
  label: string;
  comparisonLabel: string;
};

export type ExpenseTrackerCategoryBreakdown = {
  category: string;
  total: number;
  count: number;
  percentage: number;
  entries: ExpenseTrackerEntryDto[];
};

export type ExpenseTrackerPeriodComparison = {
  currentTotal: number;
  previousTotal: number;
  currentCount: number;
  previousCount: number;
  difference: number;
  trendLabel: string;
};

export type ExpenseTrackerPeriodSummary = {
  completedTotal: number;
  plannedTotal: number;
  totalEntries: number;
};

function startOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function endOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}

function shiftDays(date: Date, days: number) {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function formatDate(date: Date) {
  return date.toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric"
  });
}

function formatRange(start: Date, end: Date) {
  const separator = "\u2013";
  const sameMonth = start.getMonth() === end.getMonth() && start.getFullYear() === end.getFullYear();
  const sameYear = start.getFullYear() === end.getFullYear();

  if (sameMonth) {
    return `${start.getDate()}${separator}${end.getDate()} ${end.toLocaleDateString("en-GB", { month: "short", year: "numeric" })}`;
  }

  if (sameYear) {
    return `${start.getDate()} ${start.toLocaleDateString("en-GB", { month: "short" })} ${separator} ${end.getDate()} ${end.toLocaleDateString("en-GB", { month: "short", year: "numeric" })}`;
  }

  return `${formatDate(start)} ${separator} ${formatDate(end)}`;
}

function sumCompleted(entries: ExpenseTrackerEntryDto[]) {
  return Number(
    entries
      .filter((entry) => entry.status === "completed")
      .reduce((sum, entry) => sum + entry.amount, 0)
      .toFixed(2)
  );
}

export function buildExpenseTrackerPeriodRange(
  mode: ExpenseTrackerPeriodMode,
  now: Date = new Date()
): ExpenseTrackerPeriodRange {
  const todayEnd = now;

  if (mode === "weekly") {
    const currentDay = now.getDay();
    const daysSinceMonday = (currentDay + 6) % 7;
    const start = startOfDay(shiftDays(todayEnd, -daysSinceMonday));
    const end = todayEnd;
    const comparisonStart = startOfDay(shiftDays(start, -7));
    const comparisonEnd = endOfDay(shiftDays(comparisonStart, 6));

    return {
      mode,
      start,
      end,
      comparisonStart,
      comparisonEnd,
      label: formatRange(start, end),
      comparisonLabel: formatRange(comparisonStart, comparisonEnd)
    };
  }

  const start = new Date(todayEnd.getFullYear(), todayEnd.getMonth(), 1);
  const end = todayEnd;
  const previousMonth = new Date(todayEnd.getFullYear(), todayEnd.getMonth() - 1, 1);
  const daySpan = todayEnd.getDate();
  const comparisonStart = new Date(previousMonth.getFullYear(), previousMonth.getMonth(), 1);
  const comparisonEnd = endOfDay(
    new Date(
      previousMonth.getFullYear(),
      previousMonth.getMonth(),
      Math.min(daySpan, new Date(previousMonth.getFullYear(), previousMonth.getMonth() + 1, 0).getDate())
    )
  );

  return {
    mode,
    start,
    end,
    comparisonStart,
    comparisonEnd,
    label: formatRange(start, end),
    comparisonLabel: formatRange(comparisonStart, comparisonEnd)
  };
}

export function filterEntriesForPeriod(
  entries: ExpenseTrackerEntryDto[],
  period: ExpenseTrackerPeriodRange
) {
  const start = period.start.getTime();
  const end = period.end.getTime();

  return entries.filter((entry) => {
    const occurredAt = new Date(entry.occurredAtUtc).getTime();
    return occurredAt >= start && occurredAt <= end;
  });
}

export function filterComparisonEntriesForPeriod(
  entries: ExpenseTrackerEntryDto[],
  period: ExpenseTrackerPeriodRange
) {
  const start = period.comparisonStart.getTime();
  const end = period.comparisonEnd.getTime();

  return entries.filter((entry) => {
    const occurredAt = new Date(entry.occurredAtUtc).getTime();
    return occurredAt >= start && occurredAt <= end;
  });
}

export function buildExpenseTrackerPeriodComparison(
  entries: ExpenseTrackerEntryDto[],
  period: ExpenseTrackerPeriodRange
): ExpenseTrackerPeriodComparison {
  const currentEntries = filterEntriesForPeriod(entries, period).filter((entry) => entry.status === "completed");
  const previousEntries = filterComparisonEntriesForPeriod(entries, period).filter((entry) => entry.status === "completed");
  const currentTotal = sumCompleted(currentEntries);
  const previousTotal = sumCompleted(previousEntries);
  const difference = Number((currentTotal - previousTotal).toFixed(2));

  let trendLabel = "Holding steady";
  if (difference < 0) {
    trendLabel = `${Math.abs(difference).toFixed(2)} less than the previous period`;
  } else if (difference > 0) {
    trendLabel = `${difference.toFixed(2)} more than the previous period`;
  }

  return {
    currentTotal,
    previousTotal,
    currentCount: currentEntries.length,
    previousCount: previousEntries.length,
    difference,
    trendLabel
  };
}

export function buildExpenseTrackerPeriodSummary(
  entries: ExpenseTrackerEntryDto[],
  period: ExpenseTrackerPeriodRange
): ExpenseTrackerPeriodSummary {
  const currentEntries = filterEntriesForPeriod(entries, period);

  return currentEntries.reduce<ExpenseTrackerPeriodSummary>(
    (summary, entry) => {
      if (entry.status === "completed") {
        summary.completedTotal = Number((summary.completedTotal + entry.amount).toFixed(2));
      } else {
        summary.plannedTotal = Number((summary.plannedTotal + entry.amount).toFixed(2));
      }
      summary.totalEntries += 1;
      return summary;
    },
    {
      completedTotal: 0,
      plannedTotal: 0,
      totalEntries: 0
    }
  );
}

export function buildExpenseTrackerCategoryBreakdown(
  entries: ExpenseTrackerEntryDto[],
  period: ExpenseTrackerPeriodRange
): ExpenseTrackerCategoryBreakdown[] {
  const currentEntries = filterEntriesForPeriod(entries, period).filter((entry) => entry.status === "completed");
  const total = sumCompleted(currentEntries);
  const grouped = new Map<string, ExpenseTrackerEntryDto[]>();

  currentEntries.forEach((entry) => {
    const bucket = grouped.get(entry.category) ?? [];
    bucket.push(entry);
    grouped.set(entry.category, bucket);
  });

  return Array.from(grouped.entries())
    .map(([category, categoryEntries]) => {
      const categoryTotal = sumCompleted(categoryEntries);
      return {
        category,
        total: categoryTotal,
        count: categoryEntries.length,
        percentage: total > 0 ? Number(((categoryTotal / total) * 100).toFixed(1)) : 0,
        entries: categoryEntries.sort(
          (left, right) => new Date(right.occurredAtUtc).getTime() - new Date(left.occurredAtUtc).getTime()
        )
      };
    })
    .filter((item) => item.total > 0)
    .sort((left, right) => right.total - left.total);
}
