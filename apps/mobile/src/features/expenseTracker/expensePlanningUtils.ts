import type { ExpenseTaxonomyDomainDto, ExpenseTrackerEntryDto } from "../../types/api";
import { flattenVisibleExpenseTaxonomy } from "./expenseTrackerModels";
import type {
  ExpenseAnalyticsMode,
  ExpensePlan,
  ExpensePlanCategoryMetric,
  ExpensePlanComputed,
  ExpensePlanDraft,
  ExpensePlanLineItem,
  ExpensePlanStatus,
  ExpensePlanStatusMeta,
  ExpensePlanTaxonomyNode
} from "./expensePlanningTypes";

const DAY_MS = 24 * 60 * 60 * 1000;

function roundCurrency(value: number) {
  return Number(value.toFixed(2));
}

function startOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function endOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}

function dateString(date: Date) {
  return date.toISOString().slice(0, 10);
}

function shiftDays(date: Date, days: number) {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function monthBounds(date: Date) {
  return {
    start: new Date(date.getFullYear(), date.getMonth(), 1),
    end: new Date(date.getFullYear(), date.getMonth() + 1, 0)
  };
}

function toTimeRange(startDate: string, endDate: string) {
  return {
    start: startOfDay(new Date(startDate)),
    end: endOfDay(new Date(endDate))
  };
}

function formatDate(date: Date) {
  return date.toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short"
  });
}

export function formatExpensePlanPeriod(startDate: string, endDate: string) {
  const start = new Date(startDate);
  const end = new Date(endDate);
  const sameMonth = start.getMonth() === end.getMonth() && start.getFullYear() === end.getFullYear();

  if (sameMonth) {
    return `${start.getDate()}-${end.getDate()} ${end.toLocaleDateString("en-GB", { month: "short", year: "numeric" })}`;
  }

  return `${formatDate(start)} - ${formatDate(end)} ${end.getFullYear()}`;
}

export function buildExpensePlanCreatorTag(name: string, email?: string | null) {
  const fromName = name.trim().toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "");
  if (fromName) {
    return `@${fromName}`;
  }

  const emailLocal = (email ?? "you").split("@")[0]?.trim().toLowerCase().replace(/[^a-z0-9]+/g, "_");
  return `@${emailLocal || "you"}`;
}

export function createExpensePlanLineItem(subcategoryId: number | null = null): ExpensePlanLineItem {
  return {
    id: `line-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    subcategoryId,
    expectedAmount: 0,
    notes: ""
  };
}

export function createEmptyExpensePlanDraft(now: Date = new Date()): ExpensePlanDraft {
  const { start, end } = monthBounds(now);
  return {
    editingPlanId: null,
    title: "",
    periodType: "monthly",
    startDate: dateString(start),
    endDate: dateString(end),
    lineItems: [createExpensePlanLineItem()],
    isRecurring: false,
    recurrenceRule: null,
    isTemplate: false,
    isShared: false,
    sourcePlanId: null
  };
}

export function normalizeExpensePlanLineItems(lineItems: ExpensePlanLineItem[]): ExpensePlanLineItem[] {
  const merged = new Map<number, ExpensePlanLineItem>();
  const passthrough: ExpensePlanLineItem[] = [];

  lineItems.forEach((item) => {
    if (!item.subcategoryId) {
      if (item.expectedAmount > 0 || item.notes.trim()) {
        passthrough.push({ ...item, expectedAmount: roundCurrency(item.expectedAmount) });
      }
      return;
    }

    const existing = merged.get(item.subcategoryId);
    if (!existing) {
      merged.set(item.subcategoryId, { ...item, expectedAmount: roundCurrency(item.expectedAmount) });
      return;
    }

    existing.expectedAmount = roundCurrency(existing.expectedAmount + item.expectedAmount);
    if (item.notes.trim()) {
      existing.notes = [existing.notes, item.notes].filter(Boolean).join(" | ");
    }
  });

  return [...merged.values(), ...passthrough].filter((item) => item.subcategoryId || item.expectedAmount > 0 || item.notes.trim());
}

export function buildExpensePlanTaxonomyLookup(domains: ExpenseTaxonomyDomainDto[]) {
  return new Map<number, ExpensePlanTaxonomyNode>(
    flattenVisibleExpenseTaxonomy(domains).map((item) => [
      item.subcategory.id,
      {
        domainId: item.domain.id,
        domainName: item.domain.name,
        categoryId: item.category.id,
        categoryName: item.category.name,
        subcategoryId: item.subcategory.id,
        subcategoryName: item.subcategory.name
      }
    ])
  );
}

export function getExpensePlanStatusMeta(status: ExpensePlanStatus): ExpensePlanStatusMeta {
  switch (status) {
    case "active":
      return { label: "Active", color: "#1CC583", tint: "rgba(28,197,131,0.16)", icon: "pulse-outline" };
    case "drafted":
      return { label: "Drafted", color: "#FF9A66", tint: "rgba(255,154,102,0.16)", icon: "create-outline" };
    case "scheduled":
      return { label: "Scheduled", color: "#F6C75F", tint: "rgba(246,199,95,0.16)", icon: "time-outline" };
    case "completed":
      return { label: "Completed", color: "#6FA7FF", tint: "rgba(111,167,255,0.16)", icon: "checkmark-done-outline" };
    default:
      return { label: status, color: "#A7B6D1", tint: "rgba(167,182,209,0.16)", icon: "ellipse-outline" };
  }
}

export function buildExpensePlanningSeedPlans(input: {
  creatorId: string;
  creatorName: string;
  creatorTag: string;
  now?: Date;
}): ExpensePlan[] {
  const now = input.now ?? new Date();
  const currentMonth = monthBounds(now);
  const previousMonth = monthBounds(new Date(now.getFullYear(), now.getMonth() - 1, 12));
  const nextMonth = monthBounds(new Date(now.getFullYear(), now.getMonth() + 1, 12));
  const weeklyStart = startOfDay(shiftDays(now, -((now.getDay() + 6) % 7)));
  const weeklyEnd = endOfDay(shiftDays(weeklyStart, 6));
  const nowUtc = now.toISOString();

  const base = {
    creatorId: input.creatorId,
    creatorName: input.creatorName,
    creatorTag: input.creatorTag,
    createdAtUtc: nowUtc,
    updatedAtUtc: nowUtc
  };

  return [
    {
      ...base,
      id: "plan-active-monthly-household",
      title: "Monthly household runway",
      status: "active",
      periodType: "monthly",
      startDate: dateString(currentMonth.start),
      endDate: dateString(currentMonth.end),
      lineItems: normalizeExpensePlanLineItems([
        createExpensePlanLineItem(130111),
        { ...createExpensePlanLineItem(140101), expectedAmount: 120, subcategoryId: 140101 },
        { ...createExpensePlanLineItem(140201), expectedAmount: 85, subcategoryId: 140201 },
        { ...createExpensePlanLineItem(280101), expectedAmount: 17.99, subcategoryId: 280101 },
        { ...createExpensePlanLineItem(190401), expectedAmount: 49, subcategoryId: 190401 },
        { ...createExpensePlanLineItem(120201), expectedAmount: 180, subcategoryId: 120201 },
        { ...createExpensePlanLineItem(130111), expectedAmount: 420, subcategoryId: 130111 }
      ]),
      isRecurring: true,
      recurrenceRule: "Monthly",
      isTemplate: false,
      isShared: false,
      sharedIdentity: null,
      sourcePlanId: null,
      importedFromPublicPlanId: null,
      completedAtUtc: null
    },
    {
      ...base,
      id: "plan-active-commute-week",
      title: "Commute and quick meals",
      status: "active",
      periodType: "weekly",
      startDate: dateString(weeklyStart),
      endDate: dateString(weeklyEnd),
      lineItems: normalizeExpensePlanLineItems([
        { ...createExpensePlanLineItem(120108), expectedAmount: 70, subcategoryId: 120108 },
        { ...createExpensePlanLineItem(120106), expectedAmount: 35, subcategoryId: 120106 },
        { ...createExpensePlanLineItem(130301), expectedAmount: 24, subcategoryId: 130301 },
        { ...createExpensePlanLineItem(130204), expectedAmount: 48, subcategoryId: 130204 }
      ]),
      isRecurring: true,
      recurrenceRule: "Weekly",
      isTemplate: false,
      isShared: true,
      sharedIdentity: "share-commute-week",
      sourcePlanId: null,
      importedFromPublicPlanId: null,
      completedAtUtc: null
    },
    {
      ...base,
      id: "plan-draft-reset",
      title: "Spring spending reset",
      status: "drafted",
      periodType: "monthly",
      startDate: dateString(nextMonth.start),
      endDate: dateString(nextMonth.end),
      lineItems: normalizeExpensePlanLineItems([
        { ...createExpensePlanLineItem(130111), expectedAmount: 380, subcategoryId: 130111 },
        { ...createExpensePlanLineItem(190406), expectedAmount: 30, subcategoryId: 190406 },
        { ...createExpensePlanLineItem(230201), expectedAmount: 90, subcategoryId: 230201 }
      ]),
      isRecurring: false,
      recurrenceRule: null,
      isTemplate: false,
      isShared: false,
      sharedIdentity: null,
      sourcePlanId: null,
      importedFromPublicPlanId: null,
      completedAtUtc: null
    },
    {
      ...base,
      id: "plan-scheduled-family",
      title: "School term setup",
      status: "scheduled",
      periodType: "monthly",
      startDate: dateString(nextMonth.start),
      endDate: dateString(nextMonth.end),
      lineItems: normalizeExpensePlanLineItems([
        { ...createExpensePlanLineItem(200204), expectedAmount: 110, subcategoryId: 200204 },
        { ...createExpensePlanLineItem(200205), expectedAmount: 95, subcategoryId: 200205 },
        { ...createExpensePlanLineItem(230103), expectedAmount: 120, subcategoryId: 230103 },
        { ...createExpensePlanLineItem(260205), expectedAmount: 150, subcategoryId: 260205 }
      ]),
      isRecurring: false,
      recurrenceRule: null,
      isTemplate: false,
      isShared: true,
      sharedIdentity: "share-school-term",
      sourcePlanId: null,
      importedFromPublicPlanId: null,
      completedAtUtc: null
    },
    {
      ...base,
      id: "plan-completed-last-month",
      title: "Last month essentials",
      status: "completed",
      periodType: "monthly",
      startDate: dateString(previousMonth.start),
      endDate: dateString(previousMonth.end),
      lineItems: normalizeExpensePlanLineItems([
        { ...createExpensePlanLineItem(130111), expectedAmount: 400, subcategoryId: 130111 },
        { ...createExpensePlanLineItem(140101), expectedAmount: 115, subcategoryId: 140101 },
        { ...createExpensePlanLineItem(140201), expectedAmount: 90, subcategoryId: 140201 },
        { ...createExpensePlanLineItem(120304), expectedAmount: 28, subcategoryId: 120304 },
        { ...createExpensePlanLineItem(280101), expectedAmount: 17.99, subcategoryId: 280101 }
      ]),
      isRecurring: false,
      recurrenceRule: null,
      isTemplate: true,
      isShared: true,
      sharedIdentity: "share-last-month-essentials",
      sourcePlanId: null,
      importedFromPublicPlanId: null,
      completedAtUtc: previousMonth.end.toISOString()
    }
  ];
}

export function buildExpensePlanFromDraft(
  draft: ExpensePlanDraft,
  input: {
    creatorId: string;
    creatorName: string;
    creatorTag: string;
    status: ExpensePlanStatus;
    existingPlanId?: string | null;
    sharedIdentity?: string | null;
    completedAtUtc?: string | null;
  }
): ExpensePlan {
  const nowUtc = new Date().toISOString();
  return {
    id: input.existingPlanId ?? `plan-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    title: draft.title.trim() || "Untitled plan",
    status: input.status,
    periodType: draft.periodType,
    startDate: draft.startDate,
    endDate: draft.endDate,
    creatorId: input.creatorId,
    creatorName: input.creatorName,
    creatorTag: input.creatorTag,
    lineItems: normalizeExpensePlanLineItems(draft.lineItems),
    isRecurring: draft.isRecurring,
    recurrenceRule: draft.isRecurring ? draft.recurrenceRule ?? (draft.periodType === "weekly" ? "Weekly" : "Monthly") : null,
    isTemplate: draft.isTemplate,
    isShared: draft.isShared,
    sharedIdentity: draft.isShared ? input.sharedIdentity ?? draft.sourcePlanId ?? `share-${Date.now()}` : null,
    sourcePlanId: draft.sourcePlanId,
    importedFromPublicPlanId: null,
    createdAtUtc: nowUtc,
    updatedAtUtc: nowUtc,
    completedAtUtc: input.completedAtUtc ?? null
  };
}

export function buildExpensePlanDraftFromPlan(plan: ExpensePlan): ExpensePlanDraft {
  return {
    editingPlanId: plan.id,
    title: plan.title,
    periodType: plan.periodType,
    startDate: plan.startDate,
    endDate: plan.endDate,
    lineItems: plan.lineItems.map((item) => ({ ...item })),
    isRecurring: plan.isRecurring,
    recurrenceRule: plan.recurrenceRule,
    isTemplate: plan.isTemplate,
    isShared: plan.isShared,
    sourcePlanId: plan.sourcePlanId ?? plan.id
  };
}

function getEntriesForPlan(entries: ExpenseTrackerEntryDto[], plan: ExpensePlan) {
  const range = toTimeRange(plan.startDate, plan.endDate);
  return entries.filter((entry) => {
    if (entry.status !== "completed") {
      return false;
    }

    const occurredAt = new Date(entry.occurredAtUtc).getTime();
    return occurredAt >= range.start.getTime() && occurredAt <= range.end.getTime();
  });
}

export function buildExpensePlanComputed(
  plan: ExpensePlan,
  entries: ExpenseTrackerEntryDto[],
  taxonomyLookup: Map<number, ExpensePlanTaxonomyNode>
): ExpensePlanComputed {
  const planEntries = getEntriesForPlan(entries, plan);
  const actualBySubcategory = new Map<number, { total: number; count: number }>();

  planEntries.forEach((entry) => {
    if (!entry.subcategoryId) {
      return;
    }

    const current = actualBySubcategory.get(entry.subcategoryId) ?? { total: 0, count: 0 };
    current.total = roundCurrency(current.total + entry.amount);
    current.count += 1;
    actualBySubcategory.set(entry.subcategoryId, current);
  });

  const computedLineItems = plan.lineItems.map((line) => {
    const taxonomy = line.subcategoryId ? taxonomyLookup.get(line.subcategoryId) : null;
    const actual = line.subcategoryId ? actualBySubcategory.get(line.subcategoryId) : null;
    return {
      id: line.id,
      subcategoryId: line.subcategoryId,
      subcategoryName: taxonomy?.subcategoryName ?? "Select category",
      categoryId: taxonomy?.categoryId ?? null,
      categoryName: taxonomy?.categoryName ?? "Unassigned",
      domainId: taxonomy?.domainId ?? null,
      domainName: taxonomy?.domainName ?? "Taxonomy",
      expectedAmount: roundCurrency(line.expectedAmount),
      actualAmount: roundCurrency(actual?.total ?? 0),
      varianceAmount: roundCurrency((actual?.total ?? 0) - line.expectedAmount),
      entryCount: actual?.count ?? 0
    };
  });

  const plannedSubcategoryIds = new Set(plan.lineItems.map((item) => item.subcategoryId).filter((value): value is number => Boolean(value)));
  const unexpectedMap = new Map<string, ExpensePlanComputed["unexpectedCategories"][number]>();
  planEntries.forEach((entry) => {
    if (entry.subcategoryId && plannedSubcategoryIds.has(entry.subcategoryId)) {
      return;
    }

    const key = `${entry.subcategoryId ?? "none"}|${entry.subcategoryName ?? entry.legacyCategoryLabel ?? "Unplanned"}`;
    const existing = unexpectedMap.get(key) ?? {
      subcategoryId: entry.subcategoryId,
      subcategoryName: entry.subcategoryName ?? entry.legacyCategoryLabel ?? "Unplanned",
      categoryId: entry.categoryId,
      categoryName: entry.categoryName ?? "Unplanned",
      domainId: entry.domainId,
      domainName: entry.domainName ?? "Unplanned",
      totalAmount: 0,
      entryCount: 0,
      entryIds: []
    };

    existing.totalAmount = roundCurrency(existing.totalAmount + entry.amount);
    existing.entryCount += 1;
    existing.entryIds.push(entry.id);
    unexpectedMap.set(key, existing);
  });

  const expectedTotal = roundCurrency(computedLineItems.reduce((sum, item) => sum + item.expectedAmount, 0));
  const actualTotal = roundCurrency(planEntries.reduce((sum, entry) => sum + entry.amount, 0));
  const remainingAmount = roundCurrency(expectedTotal - actualTotal);
  const varianceAmount = roundCurrency(actualTotal - expectedTotal);
  const progressRatio = expectedTotal > 0 ? Math.min(actualTotal / expectedTotal, 1.25) : 0;

  const range = toTimeRange(plan.startDate, plan.endDate);
  const now = Date.now();
  const elapsed = Math.min(Math.max(now - range.start.getTime(), 0), Math.max(range.end.getTime() - range.start.getTime(), DAY_MS));
  const duration = Math.max(range.end.getTime() - range.start.getTime(), DAY_MS);
  const expectedByNow = expectedTotal * (elapsed / duration);

  let paceLabel: ExpensePlanComputed["paceLabel"] = "on_track";
  if (actualTotal > expectedByNow * 1.08) {
    paceLabel = "over_pace";
  } else if (actualTotal < expectedByNow * 0.82) {
    paceLabel = "ahead";
  }

  return {
    expectedTotal,
    actualTotal,
    remainingAmount,
    varianceAmount,
    progressRatio,
    paceLabel,
    lineItems: computedLineItems,
    unexpectedCategories: Array.from(unexpectedMap.values()).sort((left, right) => right.totalAmount - left.totalAmount),
    transactionCount: planEntries.length
  };
}

export function buildExpensePlanCategoryMetrics(
  plan: ExpensePlan,
  entries: ExpenseTrackerEntryDto[],
  taxonomyLookup: Map<number, ExpensePlanTaxonomyNode>,
  mode: ExpenseAnalyticsMode
): ExpensePlanCategoryMetric[] {
  const planEntries = getEntriesForPlan(entries, plan);
  const plannedByCategory = new Map<string, ExpensePlanCategoryMetric>();
  const actualByCategory = new Map<string, ExpensePlanCategoryMetric>();

  const touchBucket = (
    target: Map<string, ExpensePlanCategoryMetric>,
    input: {
      categoryId: number | null;
      categoryName: string;
      domainId: number | null;
      domainName: string;
      subcategoryId: number | null;
      subcategoryName: string;
      amount: number;
      entryId?: string;
    }
  ) => {
    const key = `${input.categoryId ?? input.categoryName}`;
    const bucket = target.get(key) ?? {
      key,
      categoryId: input.categoryId,
      categoryName: input.categoryName,
      domainId: input.domainId,
      domainName: input.domainName,
      amount: 0,
      percentage: 0,
      transactionCount: 0,
      subcategories: [],
      entryIds: []
    };

    bucket.amount = roundCurrency(bucket.amount + input.amount);
    if (input.entryId) {
      bucket.transactionCount += 1;
      bucket.entryIds.push(input.entryId);
    }

    const existingSubcategory = bucket.subcategories.find((item) => item.subcategoryId === input.subcategoryId && item.subcategoryName === input.subcategoryName);
    if (existingSubcategory) {
      existingSubcategory.amount = roundCurrency(existingSubcategory.amount + input.amount);
      if (input.entryId) {
        existingSubcategory.transactionCount += 1;
      }
    } else {
      bucket.subcategories.push({
        subcategoryId: input.subcategoryId,
        subcategoryName: input.subcategoryName,
        amount: roundCurrency(input.amount),
        percentage: 0,
        transactionCount: input.entryId ? 1 : 0
      });
    }

    target.set(key, bucket);
  };

  plan.lineItems.forEach((line) => {
    const taxonomy = line.subcategoryId ? taxonomyLookup.get(line.subcategoryId) : null;
    touchBucket(plannedByCategory, {
      categoryId: taxonomy?.categoryId ?? null,
      categoryName: taxonomy?.categoryName ?? "Unassigned",
      domainId: taxonomy?.domainId ?? null,
      domainName: taxonomy?.domainName ?? "Taxonomy",
      subcategoryId: line.subcategoryId,
      subcategoryName: taxonomy?.subcategoryName ?? "Select category",
      amount: line.expectedAmount
    });
  });

  planEntries.forEach((entry) => {
    touchBucket(actualByCategory, {
      categoryId: entry.categoryId,
      categoryName: entry.categoryName ?? entry.legacyCategoryLabel ?? "Unplanned",
      domainId: entry.domainId,
      domainName: entry.domainName ?? "Unplanned",
      subcategoryId: entry.subcategoryId,
      subcategoryName: entry.subcategoryName ?? entry.legacyCategoryLabel ?? "Unplanned",
      amount: entry.amount,
      entryId: entry.id
    });
  });

  const buckets = new Map<string, ExpensePlanCategoryMetric>();

  if (mode === "planned") {
    plannedByCategory.forEach((bucket, key) => buckets.set(key, bucket));
  } else if (mode === "actual") {
    actualByCategory.forEach((bucket, key) => buckets.set(key, bucket));
  } else {
    const keys = new Set([...plannedByCategory.keys(), ...actualByCategory.keys()]);
    keys.forEach((key) => {
      const planned = plannedByCategory.get(key);
      const actual = actualByCategory.get(key);
      const subcategoryMap = new Map<string, ExpensePlanCategoryMetric["subcategories"][number]>();

      [...(planned?.subcategories ?? []), ...(actual?.subcategories ?? [])].forEach((subcategory) => {
        const subKey = `${subcategory.subcategoryId ?? subcategory.subcategoryName}`;
        const next = subcategoryMap.get(subKey) ?? {
          subcategoryId: subcategory.subcategoryId,
          subcategoryName: subcategory.subcategoryName,
          amount: 0,
          percentage: 0,
          transactionCount: 0
        };
        subcategoryMap.set(subKey, next);
      });

      subcategoryMap.forEach((subcategory, subKey) => {
        const plannedSub = planned?.subcategories.find((item) => `${item.subcategoryId ?? item.subcategoryName}` === subKey);
        const actualSub = actual?.subcategories.find((item) => `${item.subcategoryId ?? item.subcategoryName}` === subKey);
        subcategory.amount = roundCurrency((actualSub?.amount ?? 0) - (plannedSub?.amount ?? 0));
        subcategory.transactionCount = actualSub?.transactionCount ?? 0;
      });

      buckets.set(key, {
        key,
        categoryId: actual?.categoryId ?? planned?.categoryId ?? null,
        categoryName: actual?.categoryName ?? planned?.categoryName ?? "Unassigned",
        domainId: actual?.domainId ?? planned?.domainId ?? null,
        domainName: actual?.domainName ?? planned?.domainName ?? "Taxonomy",
        amount: roundCurrency((actual?.amount ?? 0) - (planned?.amount ?? 0)),
        percentage: 0,
        transactionCount: actual?.transactionCount ?? 0,
        subcategories: Array.from(subcategoryMap.values()),
        entryIds: actual?.entryIds ?? []
      });
    });
  }

  const totalsBase = Array.from(buckets.values());
  const denominator = totalsBase.reduce((sum, bucket) => sum + Math.abs(bucket.amount), 0) || 1;

  return totalsBase
    .map((bucket) => {
      const subTotal = bucket.subcategories.reduce((sum, item) => sum + Math.abs(item.amount), 0) || 1;
      return {
        ...bucket,
        percentage: Number(((Math.abs(bucket.amount) / denominator) * 100).toFixed(1)),
        subcategories: bucket.subcategories
          .map((item) => ({
            ...item,
            percentage: Number(((Math.abs(item.amount) / subTotal) * 100).toFixed(1))
          }))
          .sort((left, right) => Math.abs(right.amount) - Math.abs(left.amount))
      };
    })
    .filter((bucket) => Math.abs(bucket.amount) > 0)
    .sort((left, right) => Math.abs(right.amount) - Math.abs(left.amount));
}

export function filterExpensePlans(plans: ExpensePlan[], status: ExpensePlanStatus | "recent") {
  if (status === "recent") {
    return [...plans].sort((left, right) => new Date(right.updatedAtUtc).getTime() - new Date(left.updatedAtUtc).getTime());
  }

  return plans
    .filter((plan) => plan.status === status)
    .sort((left, right) => new Date(right.updatedAtUtc).getTime() - new Date(left.updatedAtUtc).getTime());
}

export function duplicateExpensePlan(plan: ExpensePlan): ExpensePlanDraft {
  return {
    editingPlanId: null,
    title: `${plan.title} copy`,
    periodType: plan.periodType,
    startDate: plan.startDate,
    endDate: plan.endDate,
    lineItems: plan.lineItems.map((item) => ({ ...item, id: createExpensePlanLineItem(item.subcategoryId).id })),
    isRecurring: plan.isRecurring,
    recurrenceRule: plan.recurrenceRule,
    isTemplate: plan.isTemplate,
    isShared: plan.isShared,
    sourcePlanId: plan.id
  };
}
