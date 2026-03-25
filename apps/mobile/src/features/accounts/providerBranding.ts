type ProviderFallback = {
  monogram: string;
};

const PROVIDER_ID_FALLBACKS: Record<string, ProviderFallback> = {
  aib: { monogram: "AIB" },
  "allied irish bank": { monogram: "AIB" },
  boi: { monogram: "BOI" },
  "bank of ireland": { monogram: "BOI" },
  revolut: { monogram: "R" },
  n26: { monogram: "N26" },
  monzo: { monogram: "MZ" },
  starling: { monogram: "ST" },
  wise: { monogram: "W" },
  santander: { monogram: "SAN" },
  bunq: { monogram: "BQ" }
};

const PROVIDER_NAME_FALLBACKS: Record<string, ProviderFallback> = {
  "allied irish bank": { monogram: "AIB" },
  aib: { monogram: "AIB" },
  "bank of ireland": { monogram: "BOI" },
  "revolut bank": { monogram: "R" },
  revolut: { monogram: "R" },
  n26: { monogram: "N26" },
  monzo: { monogram: "MZ" },
  starling: { monogram: "ST" },
  wise: { monogram: "W" },
  santander: { monogram: "SAN" },
  bunq: { monogram: "BQ" }
};

export type ProviderBadgeInput = {
  providerId?: string | null;
  providerDisplayName?: string | null;
  providerIconUrl?: string | null;
  providerLogoUrl?: string | null;
};

export type ResolvedProviderBadge = {
  remoteIconUrl: string | null;
  displayName: string | null;
  monogram: string | null;
};

export function resolveProviderBadge(input: ProviderBadgeInput): ResolvedProviderBadge {
  const providerIdKey = normalizeProviderKey(input.providerId);
  const providerName = input.providerDisplayName?.trim() || null;
  const providerNameKey = normalizeProviderKey(providerName);
  const fallback =
    (providerIdKey ? PROVIDER_ID_FALLBACKS[providerIdKey] : undefined)
    ?? (providerNameKey ? PROVIDER_NAME_FALLBACKS[providerNameKey] : undefined);

  const remoteIconUrl = normalizeUrl(input.providerIconUrl) ?? normalizeUrl(input.providerLogoUrl);
  const monogram = fallback?.monogram ?? deriveMonogram(providerName);

  return {
    remoteIconUrl,
    displayName: providerName,
    monogram
  };
}

function normalizeProviderKey(value: string | null | undefined): string | null {
  if (!value) {
    return null;
  }

  const normalized = value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

  return normalized.length > 0 ? normalized : null;
}

function normalizeUrl(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  if (!trimmed) {
    return null;
  }

  return /^https?:\/\//i.test(trimmed) ? trimmed : null;
}

function deriveMonogram(providerName: string | null): string | null {
  if (!providerName) {
    return null;
  }

  const words = providerName
    .split(/[\s&/-]+/)
    .map((segment) => segment.trim())
    .filter(Boolean);

  if (words.length === 0) {
    return null;
  }

  if (words.length === 1) {
    const cleaned = words[0].replace(/[^a-z0-9]/gi, "");
    if (cleaned.length <= 3) {
      return cleaned.toUpperCase();
    }

    return cleaned.slice(0, 2).toUpperCase();
  }

  return `${words[0][0] ?? ""}${words[1][0] ?? ""}`.toUpperCase();
}
