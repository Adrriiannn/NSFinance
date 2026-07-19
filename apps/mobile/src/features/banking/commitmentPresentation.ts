import type { FinancialCommitmentDto } from "../../types/api";

// Presentation mapping for canonical financial commitments. Certainty is part
// of the contract: estimated dates and amounts must read as estimates, and
// missing facts must say so instead of implying precision.

export type UpcomingCommitmentRow = {
  id: string;
  label: string;
  accountDisplayName: string;
  sourceLabel: string;
  amountText: string;
  whenText: string;
  isStale: boolean;
  nextDateUtc: string | null;
};

const KIND_SOURCE_LABELS: Record<string, string> = {
  direct_debit: "direct debit",
  standing_order: "standing order"
};

function resolveSourceLabel(commitment: FinancialCommitmentDto): string {
  const kindLabel = KIND_SOURCE_LABELS[commitment.kind];

  if (commitment.source === "provider" && kindLabel) {
    return kindLabel;
  }

  if (commitment.source === "provider") {
    return "from your bank";
  }

  if (commitment.source === "user") {
    return "added by you";
  }

  return "detected";
}

function formatAmount(amount: number, currency: string): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency,
    maximumFractionDigits: 2
  }).format(amount);
}

function resolveAmountText(commitment: FinancialCommitmentDto): string {
  const isExact = commitment.amountCertainty === "exact" && commitment.isVariableAmount !== true;

  if (commitment.nextAmount !== null && commitment.currency) {
    const formatted = formatAmount(commitment.nextAmount, commitment.currency);
    return isExact ? formatted : `~${formatted}`;
  }

  if (commitment.lastObservedAmount !== null && commitment.lastObservedCurrency) {
    return `~${formatAmount(commitment.lastObservedAmount, commitment.lastObservedCurrency)}`;
  }

  return "Amount pending";
}

const DAY_MS = 24 * 60 * 60 * 1000;

function resolveWhenText(commitment: FinancialCommitmentDto, nowUtcMs: number): string {
  if (!commitment.nextDateUtc) {
    return "date pending";
  }

  const dueMs = Date.parse(commitment.nextDateUtc);
  if (Number.isNaN(dueMs)) {
    return "date pending";
  }

  const dayLabel = new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "short",
    timeZone: "UTC"
  }).format(new Date(dueMs));

  const approximatePrefix = commitment.dateCertainty === "exact" ? "" : "~";
  const daysUntil = Math.ceil((dueMs - nowUtcMs) / DAY_MS);

  if (commitment.dateCertainty === "exact") {
    if (daysUntil <= 0) {
      return `due ${dayLabel}`;
    }

    if (daysUntil === 1) {
      return "due tomorrow";
    }

    if (daysUntil <= 7) {
      return `due in ${daysUntil} days`;
    }
  }

  return `${approximatePrefix}${dayLabel}`;
}

export function buildUpcomingCommitmentRows(
  commitments: readonly FinancialCommitmentDto[] | undefined,
  nowUtcMs: number
): UpcomingCommitmentRow[] {
  if (!commitments || commitments.length === 0) {
    return [];
  }

  return commitments
    .filter((commitment) => commitment.lifecycle === "active")
    .filter((commitment) => commitment.userDecision?.state !== "dismissed")
    .filter((commitment) => commitment.direction !== "incoming")
    .map((commitment) => ({
      id: commitment.id,
      label: commitment.label,
      accountDisplayName: commitment.accountDisplayName,
      sourceLabel: resolveSourceLabel(commitment),
      amountText: resolveAmountText(commitment),
      whenText: resolveWhenText(commitment, nowUtcMs),
      isStale: commitment.freshness === "stale",
      nextDateUtc: commitment.nextDateUtc
    }))
    .sort((left, right) => {
      if (left.nextDateUtc === right.nextDateUtc) {
        return left.label.localeCompare(right.label);
      }

      if (left.nextDateUtc === null) {
        return 1;
      }

      if (right.nextDateUtc === null) {
        return -1;
      }

      return Date.parse(left.nextDateUtc) - Date.parse(right.nextDateUtc);
    });
}
