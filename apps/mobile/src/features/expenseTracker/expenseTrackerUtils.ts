import type { ExpenseTrackerEntryDto, ExpenseTrackerEntryStatus } from "../../types/api";
import type { ExpenseTrackerQuickRange, ExpenseTrackerSortOrder } from "./expenseTrackerModels";

export type ExpenseTrackerFilters = {
  search: string;
  quickRange: ExpenseTrackerQuickRange;
  category: string | null;
  paymentSource: string | null;
  status: ExpenseTrackerEntryStatus | "all";
  sortOrder: ExpenseTrackerSortOrder;
};

export type ExpenseTrackerSummary = {
  todayTotal: number;
  weekTotal: number;
  monthTotal: number;
  plannedTotal: number;
  completedTotal: number;
  entryCount: number;
};

export type ExpenseTrackerSection = {
  title: string;
  total: number;
  data: ExpenseTrackerEntryDto[];
};

function startOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function startOfWeek(date: Date) {
  const day = date.getDay();
  const diff = (day + 6) % 7;
  const next = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  next.setDate(next.getDate() - diff);
  return startOfDay(next);
}

function startOfMonth(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function isSameDay(left: Date, right: Date) {
  return left.getFullYear() === right.getFullYear() && left.getMonth() === right.getMonth() && left.getDate() === right.getDate();
}

function currencyAmount(entry: ExpenseTrackerEntryDto) {
  return entry.status === "completed" ? entry.amount : 0;
}

export function buildExpenseTrackerSummary(
  entries: ExpenseTrackerEntryDto[],
  now: Date = new Date()
): ExpenseTrackerSummary {
  const dayStart = startOfDay(now).getTime();
  const weekStart = startOfWeek(now).getTime();
  const monthStart = startOfMonth(now).getTime();

  return entries.reduce<ExpenseTrackerSummary>(
    (summary, entry) => {
      const occurredAt = new Date(entry.occurredAtUtc).getTime();
      if (entry.status === "completed") {
        summary.completedTotal = Number((summary.completedTotal + entry.amount).toFixed(2));
        if (occurredAt >= dayStart) {
          summary.todayTotal = Number((summary.todayTotal + entry.amount).toFixed(2));
        }
        if (occurredAt >= weekStart) {
          summary.weekTotal = Number((summary.weekTotal + entry.amount).toFixed(2));
        }
        if (occurredAt >= monthStart) {
          summary.monthTotal = Number((summary.monthTotal + entry.amount).toFixed(2));
        }
      }

      if (entry.status === "planned") {
        summary.plannedTotal = Number((summary.plannedTotal + entry.amount).toFixed(2));
      }

      summary.entryCount += 1;
      return summary;
    },
    {
      todayTotal: 0,
      weekTotal: 0,
      monthTotal: 0,
      plannedTotal: 0,
      completedTotal: 0,
      entryCount: 0
    }
  );
}

export function filterExpenseTrackerEntries(
  entries: ExpenseTrackerEntryDto[],
  filters: ExpenseTrackerFilters,
  now: Date = new Date()
) {
  const search = filters.search.trim().toLowerCase();
  const dayStart = startOfDay(now).getTime();
  const weekStart = startOfWeek(now).getTime();
  const monthStart = startOfMonth(now).getTime();

  return [...entries]
    .filter((entry) => {
      const occurredAt = new Date(entry.occurredAtUtc).getTime();
      if (filters.quickRange === "today" && occurredAt < dayStart) {
        return false;
      }
      if (filters.quickRange === "week" && occurredAt < weekStart) {
        return false;
      }
      if (filters.quickRange === "month" && occurredAt < monthStart) {
        return false;
      }
      if (filters.category && entry.category !== filters.category) {
        return false;
      }
      if (filters.paymentSource && entry.paymentSource !== filters.paymentSource) {
        return false;
      }
      if (filters.status !== "all" && entry.status !== filters.status) {
        return false;
      }
      if (!search) {
        return true;
      }

      const haystack = [entry.title, entry.notes ?? "", entry.merchant ?? "", entry.category, entry.paymentSource, ...entry.tags]
        .join(" ")
        .toLowerCase();
      return haystack.includes(search);
    })
    .sort((left, right) => {
      if (filters.sortOrder === "highest") {
        return right.amount - left.amount;
      }
      if (filters.sortOrder === "lowest") {
        return left.amount - right.amount;
      }
      if (filters.sortOrder === "oldest") {
        return new Date(left.occurredAtUtc).getTime() - new Date(right.occurredAtUtc).getTime();
      }
      return new Date(right.occurredAtUtc).getTime() - new Date(left.occurredAtUtc).getTime();
    });
}

export function groupExpenseTrackerEntries(
  entries: ExpenseTrackerEntryDto[],
  now: Date = new Date()
): ExpenseTrackerSection[] {
  const sections = new Map<string, ExpenseTrackerEntryDto[]>();

  entries.forEach((entry) => {
    const occurredAt = new Date(entry.occurredAtUtc);
    const key = occurredAt.toDateString();
    const bucket = sections.get(key) ?? [];
    bucket.push(entry);
    sections.set(key, bucket);
  });

  return Array.from(sections.entries()).map(([dateKey, items]) => {
    const date = new Date(dateKey);
    const today = startOfDay(now);
    const yesterday = new Date(today);
    yesterday.setDate(yesterday.getDate() - 1);
    const sameWeek = date >= startOfWeek(now);

    let title = date.toLocaleDateString("en-GB", {
      day: "numeric",
      month: "short",
      year: "numeric"
    });

    if (isSameDay(date, today)) {
      title = "Today";
    } else if (isSameDay(date, yesterday)) {
      title = "Yesterday";
    } else if (sameWeek) {
      title = date.toLocaleDateString("en-GB", { weekday: "long" });
    }

    const total = items.reduce((sum, item) => sum + currencyAmount(item), 0);

    return {
      title,
      total: Number(total.toFixed(2)),
      data: items.sort(
        (left, right) => new Date(right.occurredAtUtc).getTime() - new Date(left.occurredAtUtc).getTime()
      )
    };
  }).sort((left, right) => new Date(right.data[0].occurredAtUtc).getTime() - new Date(left.data[0].occurredAtUtc).getTime());
}
