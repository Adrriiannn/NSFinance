import type { AccountDto, AccountSource } from "../../types/api";

type AccountSourceEvidence = Pick<
  AccountDto,
  | "providerId"
  | "providerDisplayName"
  | "providerIconUrl"
  | "providerLogoUrl"
  | "hasProviderBranding"
> & {
  source?: string | null;
};

function hasText(value?: string | null) {
  return Boolean(value?.trim());
}

export function resolveAccountSource(
  account?: AccountSourceEvidence | null
): AccountSource | null {
  if (!account) {
    return null;
  }

  if (account.source !== undefined && account.source !== null) {
    return account.source === "provider_projected" ? "provider_projected" : "manual";
  }

  const hasProviderEvidence =
    hasText(account.providerId) ||
    hasText(account.providerDisplayName) ||
    hasText(account.providerIconUrl) ||
    hasText(account.providerLogoUrl) ||
    account.hasProviderBranding;

  return hasProviderEvidence ? "provider_projected" : "manual";
}

export function isProviderProjectedAccount(account?: AccountSourceEvidence | null) {
  return resolveAccountSource(account) === "provider_projected";
}
