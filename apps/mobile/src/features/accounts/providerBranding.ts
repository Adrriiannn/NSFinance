type ProviderFallback = {
  monogram: string;
};

type KnownProviderKey =
  | "aib"
  | "boi"
  | "revolut"
  | "n26"
  | "monzo"
  | "starling"
  | "wise"
  | "santander"
  | "bunq";

const KNOWN_PROVIDER_FALLBACKS: Record<KnownProviderKey, ProviderFallback> = {
  aib: { monogram: "AIB" },
  boi: { monogram: "BOI" },
  revolut: { monogram: "R" },
  n26: { monogram: "N26" },
  monzo: { monogram: "MZ" },
  starling: { monogram: "ST" },
  wise: { monogram: "W" },
  santander: { monogram: "SAN" },
  bunq: { monogram: "BQ" }
};

const PROVIDER_PATTERNS: {
  key: KnownProviderKey;
  patterns: RegExp[];
}[] = [
  {
    key: "aib",
    patterns: [/\baib\b/i, /\ballied irish bank\b/i]
  },
  {
    key: "boi",
    patterns: [/\bboi\b/i, /\bbank of ireland\b/i]
  },
  {
    key: "revolut",
    patterns: [/\brevolut\b/i]
  },
  {
    key: "n26",
    patterns: [/\bn26\b/i]
  },
  {
    key: "monzo",
    patterns: [/\bmonzo\b/i]
  },
  {
    key: "starling",
    patterns: [/\bstarling\b/i]
  },
  {
    key: "wise",
    patterns: [/\bwise\b/i]
  },
  {
    key: "santander",
    patterns: [/\bsantander\b/i]
  },
  {
    key: "bunq",
    patterns: [/\bbunq\b/i]
  }
];

export type ProviderBadgeInput = {
  providerId?: string | null;
  providerDisplayName?: string | null;
  providerIconUrl?: string | null;
  providerLogoUrl?: string | null;
};

export type ResolvedProviderBadge = {
  remoteIconUrls: string[];
  displayName: string | null;
  monogram: string | null;
  canonicalProviderKey: KnownProviderKey | null;
};

export function resolveProviderBadge(input: ProviderBadgeInput): ResolvedProviderBadge {
  const providerIdKey = normalizeProviderKey(input.providerId);
  const providerName = input.providerDisplayName?.trim() || null;
  const providerNameKey = normalizeProviderKey(providerName);
  const canonicalProviderKey = resolveCanonicalProviderKey(providerIdKey, providerNameKey);
  const fallback = canonicalProviderKey ? KNOWN_PROVIDER_FALLBACKS[canonicalProviderKey] : undefined;

  const remoteIconUrls = dedupeUrls([
    normalizeUrl(input.providerLogoUrl),
    normalizeUrl(input.providerIconUrl)
  ]);
  const monogram = fallback?.monogram ?? deriveMonogram(providerName);

  return {
    remoteIconUrls,
    displayName: providerName,
    monogram,
    canonicalProviderKey
  };
}

function resolveCanonicalProviderKey(
  providerIdKey: string | null,
  providerNameKey: string | null
): KnownProviderKey | null {
  const candidates = [providerIdKey, providerNameKey].filter((value): value is string => Boolean(value));
  if (candidates.length === 0) {
    return null;
  }

  for (const candidate of candidates) {
    const exact = PROVIDER_PATTERNS.find(({ key }) => key === candidate);
    if (exact) {
      return exact.key;
    }

    const fuzzy = PROVIDER_PATTERNS.find(({ patterns }) =>
      patterns.some((pattern) => pattern.test(candidate))
    );
    if (fuzzy) {
      return fuzzy.key;
    }
  }

  return null;
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

  if (/^https?:\/\//i.test(trimmed)) {
    return trimmed;
  }

  if (/^\/\//.test(trimmed)) {
    return `https:${trimmed}`;
  }

  if (/^data:image\//i.test(trimmed)) {
    return trimmed;
  }

  return null;
}

function dedupeUrls(urls: (string | null)[]): string[] {
  const seen = new Set<string>();
  const deduped: string[] = [];
  for (const candidate of urls) {
    if (!candidate || seen.has(candidate)) {
      continue;
    }

    seen.add(candidate);
    deduped.push(candidate);
  }

  return deduped;
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
