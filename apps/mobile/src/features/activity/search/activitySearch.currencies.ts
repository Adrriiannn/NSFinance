import type { ActivityCurrencyMetadata } from "./activitySearch.types";

export const ACTIVITY_CURRENCY_METADATA: Record<string, ActivityCurrencyMetadata> = {
  EUR: {
    code: "EUR",
    symbol: "€",
    placement: "prefix"
  },
  GBP: {
    code: "GBP",
    symbol: "£",
    placement: "prefix"
  },
  USD: {
    code: "USD",
    symbol: "$",
    placement: "prefix"
  },
  RON: {
    code: "RON",
    symbol: "RON",
    placement: "suffix"
  }
};

const DEFAULT_CURRENCY = ACTIVITY_CURRENCY_METADATA.EUR;

export function getActivityCurrencyMetadata(code: string | null | undefined) {
  if (!code) {
    return DEFAULT_CURRENCY;
  }

  return ACTIVITY_CURRENCY_METADATA[code.toUpperCase()] ?? {
    code: code.toUpperCase(),
    symbol: code.toUpperCase(),
    placement: "suffix"
  };
}

export function formatActivityTokenAmount(amount: number, currencyCode: string) {
  const currency = getActivityCurrencyMetadata(currencyCode);
  const sign = amount < 0 ? "-" : "";
  const absolute = Math.abs(amount);
  const hasDecimals = Math.abs(absolute - Math.round(absolute)) > 0.0001;
  const numberLabel = absolute.toLocaleString("en-GB", {
    minimumFractionDigits: hasDecimals ? 2 : 0,
    maximumFractionDigits: 2
  });

  if (currency.placement === "prefix") {
    return `${sign}${currency.symbol}${numberLabel}`;
  }

  return `${sign}${numberLabel} ${currency.symbol}`;
}

export function parseActivityAmountInput(
  rawInput: string,
  selectedCurrencyCode: string
) {
  const normalizedInput = rawInput.trim().replace(/,/g, ".");
  if (!normalizedInput) {
    return null;
  }

  const selectedCurrency = getActivityCurrencyMetadata(selectedCurrencyCode);
  const withoutCurrencyHints = normalizedInput
    .replace(/[€£$]/g, "")
    .replace(/\b(?:eur|gbp|usd|ron|lei)\b/gi, "")
    .replace(new RegExp(selectedCurrency.symbol, "gi"), "")
    .replace(/\s+/g, "");

  const amountMatch = withoutCurrencyHints.match(/-?\d+(?:\.\d{0,2})?/);
  if (!amountMatch) {
    return null;
  }

  const parsed = Number(amountMatch[0]);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  return Number(parsed.toFixed(2));
}

export function dedupeCurrencyCodes(codes: string[]) {
  const seen = new Set<string>();
  const ordered: string[] = [];

  codes.forEach((code) => {
    const normalized = code.trim().toUpperCase();
    if (!normalized || seen.has(normalized)) {
      return;
    }

    seen.add(normalized);
    ordered.push(normalized);
  });

  return ordered;
}

