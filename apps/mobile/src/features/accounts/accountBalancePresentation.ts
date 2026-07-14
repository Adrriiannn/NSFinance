import type {
  AccountBalanceDto,
  AccountDto,
  CurrencyBalanceTotalDto,
  PortfolioBalanceDto
} from "../../types/api";

export const LEGACY_CURRENT_BALANCE_SOURCE = "legacy_current_balance" as const;

type BalanceAwareAccountDto = AccountDto & {
  balance?: AccountBalanceDto | null;
};

export type AccountBalancePresentation = {
  current: number | null;
  available: number | null;
  overdraft: number | null;
  currency: string;
  source: AccountBalanceDto["source"] | typeof LEGACY_CURRENT_BALANCE_SOURCE;
  asOf: string | null;
  freshness: AccountBalanceDto["freshness"];
  exclusions: readonly string[];
};

export type PortfolioCurrencyGroupPresentation = {
  currency: CurrencyBalanceTotalDto["currency"];
  amount: CurrencyBalanceTotalDto["amount"];
  accountCount: CurrencyBalanceTotalDto["accountCount"];
  basis: CurrencyBalanceTotalDto["basis"];
};

export type PortfolioBalancePresentation = {
  currencyGroups: readonly PortfolioCurrencyGroupPresentation[];
  includedAccountCount: number;
  excludedAccountCount: number;
  hasMultipleCurrencies: boolean;
};

export function resolveAccountBalancePresentation(
  account: BalanceAwareAccountDto
): AccountBalancePresentation {
  if (!Object.prototype.hasOwnProperty.call(account, "balance")) {
    return {
      current: account.currentBalance,
      available: null,
      overdraft: null,
      currency: account.currency,
      source: LEGACY_CURRENT_BALANCE_SOURCE,
      asOf: null,
      freshness: "unknown",
      exclusions: ["structured_balance_absent"]
    };
  }

  const balance = account.balance;
  if (!balance) {
    return {
      current: null,
      available: null,
      overdraft: null,
      currency: account.currency,
      source: "unavailable",
      asOf: null,
      freshness: "unknown",
      exclusions: ["structured_balance_unavailable"]
    };
  }

  return {
    current: balance.current,
    available: balance.available,
    overdraft: balance.overdraft,
    currency: balance.currency,
    source: balance.source,
    asOf: balance.asOfUtc,
    freshness: balance.freshness,
    exclusions: [...balance.exclusions]
  };
}

export function resolvePortfolioBalancePresentation(
  portfolioBalance: PortfolioBalanceDto | null | undefined
): PortfolioBalancePresentation | null {
  if (!portfolioBalance) {
    return null;
  }

  return {
    currencyGroups: portfolioBalance.byCurrency.map((group) => ({
      currency: group.currency,
      amount: group.amount,
      accountCount: group.accountCount,
      basis: group.basis
    })),
    includedAccountCount: portfolioBalance.includedAccountCount,
    excludedAccountCount: portfolioBalance.excludedAccountCount,
    hasMultipleCurrencies: portfolioBalance.hasMultipleCurrencies
  };
}
