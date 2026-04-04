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

export type CanonicalTransactionSemantic =
  | "real_transaction"
  | "internal_transfer"
  | "savings_transfer";

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
  categoryOverride?: string | null
): string {
  if (!categoryOverride) {
    const semantic = resolveCanonicalTransactionSemantic(transaction);
    if (semantic === "internal_transfer") {
      return "Bank account transfer";
    }

    if (semantic === "savings_transfer") {
      return "Savings transfer";
    }
  }

  const category =
    categoryOverride ??
    transaction.taxonomySubcategoryName ??
    transaction.taxonomyCategoryName ??
    transaction.categoryName ??
    transaction.taxonomyDomainName ??
    "Uncategorized";
  return category;
}

export function buildTransactionDetailDate(transaction: TransactionDto): string {
  return `${formatLongDate(transaction.bookedAtUtc)} | ${formatTime(transaction.bookedAtUtc)}`;
}

export function resolveCanonicalTransactionSemantic(transaction: TransactionDto): CanonicalTransactionSemantic {
  if (transaction.deterministicClassificationStatus === "classified_matched_rule") {
    if (transaction.deterministicRelationshipType === "internal_transfer") {
      return "internal_transfer";
    }

    if (transaction.deterministicRelationshipType === "savings_transfer") {
      return "savings_transfer";
    }
  }

  if (transaction.displaySemantic === "internal_transfer") {
    return "internal_transfer";
  }

  if (transaction.displaySemantic === "savings_roundup" || transaction.displaySemantic === "savings_manual_move") {
    return "savings_transfer";
  }

  return "real_transaction";
}

export function resolveCanonicalRelationshipBadge(transaction: TransactionDto): string | null {
  const semantic = resolveCanonicalTransactionSemantic(transaction);
  if (semantic === "internal_transfer") {
    return "Linked transfer";
  }

  if (semantic === "savings_transfer") {
    return "Savings transfer";
  }

  return null;
}
