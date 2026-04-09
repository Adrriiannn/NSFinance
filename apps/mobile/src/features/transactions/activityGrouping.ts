import type { TransactionDto } from "../../types/api";
import { formatLongDate, formatTime } from "../../lib/format";

export type ActivityFilter =
  | "All"
  | "Income"
  | "Expense"
  | "Online"
  | "In person";

export type GroupedActivity = {
  title: string;
  items: TransactionDto[];
};

export type CanonicalTransactionLabels = {
  categoryLabel: string | null;
  subcategoryLabel: string | null;
};

export type TransactionDisplayLabelResolution = CanonicalTransactionLabels & {
  displayLabel: string;
  hasCanonicalLabel: boolean;
};

export function getTransactionChannelLabel(transaction: TransactionDto): "Online" | "In person" {
  const source = `${transaction.description} ${transaction.categoryName ?? ""}`.toLowerCase();
  if (/\bonline\b|\bweb\b|\bapp\b|\bsubscription\b/.test(source)) {
    return "Online";
  }

  return "In person";
}

export function applyActivityFilter(
  transactions: TransactionDto[],
  filter: ActivityFilter
): TransactionDto[] {
  if (filter === "All") {
    return transactions;
  }

  if (filter === "Income" || filter === "Expense") {
    return transactions.filter((item) => item.direction === filter);
  }

  return transactions.filter((item) => getTransactionChannelLabel(item) === filter);
}

function bucketLabel(date: Date): string {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const txDay = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const diffDays = Math.floor((today.getTime() - txDay.getTime()) / (1000 * 60 * 60 * 24));

  if (diffDays === 0) {
    return "Today";
  }

  if (diffDays === 1) {
    return "Yesterday";
  }

  if (diffDays < 7) {
    return "This week";
  }

  if (today.getFullYear() === txDay.getFullYear() && today.getMonth() === txDay.getMonth()) {
    return "This month";
  }

  const lastMonth = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  if (lastMonth.getFullYear() === txDay.getFullYear() && lastMonth.getMonth() === txDay.getMonth()) {
    return "Last month";
  }

  if (today.getFullYear() === txDay.getFullYear()) {
    return new Intl.DateTimeFormat("en-IE", { month: "long" }).format(txDay);
  }

  return new Intl.DateTimeFormat("en-IE", { month: "long", year: "numeric" }).format(txDay);
}

const bucketOrder = [
  "Today",
  "Yesterday",
  "This week",
  "This month",
  "Last month"
];

function bucketSort(a: string, b: string): number {
  const aIndex = bucketOrder.indexOf(a);
  const bIndex = bucketOrder.indexOf(b);

  if (aIndex >= 0 && bIndex >= 0) {
    return aIndex - bIndex;
  }

  if (aIndex >= 0) {
    return -1;
  }

  if (bIndex >= 0) {
    return 1;
  }

  const parse = (label: string) => {
    const parts = label.split(" ");
    if (parts.length === 2) {
      return new Date(`${parts[0]} 1 ${parts[1]}`);
    }

    return new Date(`${label} 1 ${new Date().getFullYear()}`);
  };

  return parse(b).getTime() - parse(a).getTime();
}

export function groupTransactionsByTimeBucket(transactions: TransactionDto[]): GroupedActivity[] {
  const map = new Map<string, TransactionDto[]>();

  transactions.forEach((item) => {
    const label = bucketLabel(new Date(item.bookedAtUtc));
    const current = map.get(label) ?? [];
    current.push(item);
    map.set(label, current);
  });

  return [...map.entries()]
    .sort(([a], [b]) => bucketSort(a, b))
    .map(([title, items]) => ({
      title,
      items: [...items].sort(
        (left, right) => new Date(right.bookedAtUtc).getTime() - new Date(left.bookedAtUtc).getTime()
      )
    }));
}

export function buildTransactionMetaLine(
  transaction: TransactionDto,
  semanticFallbackLabel?: string | null
): string {
  return resolveTransactionDisplayLabel(transaction, semanticFallbackLabel).displayLabel;
}

export function buildTransactionDetailDate(transaction: TransactionDto): string {
  return `${formatLongDate(transaction.bookedAtUtc)} | ${formatTime(transaction.bookedAtUtc)}`;
}

export function resolveCanonicalTransactionLabels(transaction: TransactionDto): CanonicalTransactionLabels {
  const subcategoryLabel = normalizeLabel(transaction.taxonomySubcategoryName);
  const categoryLabel =
    normalizeLabel(transaction.taxonomyCategoryName)
    ?? normalizeLabel(transaction.categoryName);

  return {
    categoryLabel,
    subcategoryLabel
  };
}

export function resolveTransactionDisplayLabel(
  transaction: TransactionDto,
  semanticFallbackLabel?: string | null
): TransactionDisplayLabelResolution {
  const { categoryLabel, subcategoryLabel } = resolveCanonicalTransactionLabels(transaction);
  const semanticLabel = normalizeLabel(semanticFallbackLabel);
  const domainLabel = normalizeLabel(transaction.taxonomyDomainName);
  const displayLabel = subcategoryLabel
    ?? categoryLabel
    ?? semanticLabel
    ?? domainLabel
    ?? "Uncategorized";

  return {
    categoryLabel,
    subcategoryLabel,
    displayLabel,
    hasCanonicalLabel: Boolean(subcategoryLabel ?? categoryLabel)
  };
}

export function areDisplayLabelsMeaningfullyDistinct(
  primaryLabel: string | null | undefined,
  secondaryLabel: string | null | undefined
): boolean {
  const primaryComparable = toComparableLabel(primaryLabel);
  const secondaryComparable = toComparableLabel(secondaryLabel);
  if (!primaryComparable || !secondaryComparable) {
    return false;
  }

  if (primaryComparable === secondaryComparable) {
    return false;
  }

  if (primaryComparable.includes(secondaryComparable) || secondaryComparable.includes(primaryComparable)) {
    return false;
  }

  return true;
}

function normalizeLabel(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function toComparableLabel(value: string | null | undefined): string {
  return (value ?? "")
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
}
