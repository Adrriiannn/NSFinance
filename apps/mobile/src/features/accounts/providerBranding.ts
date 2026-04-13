import type { ImageSourcePropType } from "react-native";

type BankLogoRecord = {
  source: ImageSourcePropType;
  aliases: RegExp[];
  fallbackMonogram: string;
};

const BANK_LOGOS = {
  aib: {
    source: require("../../../assets/Banks/AIB/AIB.png"),
    aliases: [/\baib\b/i, /\ballied irish bank\b/i],
    fallbackMonogram: "AIB"
  },
  americanExpress: {
    source: require("../../../assets/Banks/AmericanExpress/AmericanExpress.png"),
    aliases: [/\bamerican express\b/i, /\bamex\b/i],
    fallbackMonogram: "AMEX"
  },
  bankOfScotland: {
    source: require("../../../assets/Banks/BankOfScotland/BankOfScotland.png"),
    aliases: [/\bbank of scotland\b/i],
    fallbackMonogram: "BOS"
  },
  barclaycard: {
    source: require("../../../assets/Banks/Barclaycard/Barclaycard.png"),
    aliases: [/\bbarclaycard\b/i],
    fallbackMonogram: "BAR"
  },
  barclays: {
    source: require("../../../assets/Banks/Barclays/Barclays.png"),
    aliases: [/\bbarclays\b/i],
    fallbackMonogram: "BAR"
  },
  boi: {
    source: require("../../../assets/Banks/BOI/BOI.png"),
    aliases: [/\bboi\b/i, /\bbank of ireland\b/i],
    fallbackMonogram: "BOI"
  },
  capitalOne: {
    source: require("../../../assets/Banks/CapitalOne/CapitalOne.png"),
    aliases: [/\bcapital one\b/i],
    fallbackMonogram: "CO"
  },
  chelseaBuildingSociety: {
    source: require("../../../assets/Banks/ChelseaBuildingSociety/ChelseaBuildingSociety.png"),
    aliases: [/\bchelsea building society\b/i],
    fallbackMonogram: "CBS"
  },
  danskeBank: {
    source: require("../../../assets/Banks/DanskeBank/DanskeBank.png"),
    aliases: [/\bdanske\b/i],
    fallbackMonogram: "DB"
  },
  firstDirect: {
    source: require("../../../assets/Banks/FirstDirect/FirstDirect.png"),
    aliases: [/\bfirst direct\b/i],
    fallbackMonogram: "FD"
  },
  halifax: {
    source: require("../../../assets/Banks/Halifax/Halifax.png"),
    aliases: [/\bhalifax\b/i],
    fallbackMonogram: "HX"
  },
  hsbc: {
    source: require("../../../assets/Banks/HSBC/HSBC.png"),
    aliases: [/\bhsbc\b/i],
    fallbackMonogram: "HSBC"
  },
  lloydsBank: {
    source: require("../../../assets/Banks/LloydsBank/LloydsBank.png"),
    aliases: [/\blloyds\b/i],
    fallbackMonogram: "LB"
  },
  mbna: {
    source: require("../../../assets/Banks/MBNA/MBNA.png"),
    aliases: [/\bmbna\b/i],
    fallbackMonogram: "MBNA"
  },
  mettle: {
    source: require("../../../assets/Banks/Mettle/Mettle.png"),
    aliases: [/\bmettle\b/i],
    fallbackMonogram: "MET"
  },
  monzo: {
    source: require("../../../assets/Banks/Monzo/Monzo.png"),
    aliases: [/\bmonzo\b/i],
    fallbackMonogram: "MZ"
  },
  msBank: {
    source: require("../../../assets/Banks/MSBank/MSBank.png"),
    aliases: [/\bms bank\b/i, /\bm&s bank\b/i, /\bmarks and spencer bank\b/i],
    fallbackMonogram: "MS"
  },
  nationwide: {
    source: require("../../../assets/Banks/Nationwide/Nationwide.png"),
    aliases: [/\bnationwide\b/i],
    fallbackMonogram: "NW"
  },
  natWest: {
    source: require("../../../assets/Banks/NatWest/Natwest.png"),
    aliases: [/\bnatwest\b/i],
    fallbackMonogram: "NW"
  },
  ptsb: {
    source: require("../../../assets/Banks/PTSB/PTSB.png"),
    aliases: [/\bptsb\b/i, /\bpermanent tsb\b/i],
    fallbackMonogram: "PTSB"
  },
  revolut: {
    source: require("../../../assets/Banks/Revolut/Revolut.png"),
    aliases: [/\brevolut\b/i],
    fallbackMonogram: "R"
  },
  santander: {
    source: require("../../../assets/Banks/Santander/Santander.png"),
    aliases: [/\bsantander\b/i],
    fallbackMonogram: "SAN"
  },
  starlingBank: {
    source: require("../../../assets/Banks/StarlingBank/StarlingBank.png"),
    aliases: [/\bstarling\b/i],
    fallbackMonogram: "SB"
  },
  tescoBank: {
    source: require("../../../assets/Banks/TescoBank/TescoBank.png"),
    aliases: [/\btesco bank\b/i],
    fallbackMonogram: "TB"
  },
  royalBankOfScotland: {
    source: require("../../../assets/Banks/TheRoyalBankOfScotland/RoyalBankOfScotland.png"),
    aliases: [/\broyal bank of scotland\b/i, /\brbs\b/i],
    fallbackMonogram: "RBS"
  },
  tide: {
    source: require("../../../assets/Banks/Tide/Tide.png"),
    aliases: [/\btide\b/i],
    fallbackMonogram: "TD"
  },
  ulsterBank: {
    source: require("../../../assets/Banks/UlsterBank/UlsterBank.png"),
    aliases: [/\bulster\b/i],
    fallbackMonogram: "UB"
  },
  virginMoney: {
    source: require("../../../assets/Banks/VirginMoney/VirginMoney.png"),
    aliases: [/\bvirgin money\b/i],
    fallbackMonogram: "VM"
  },
  wise: {
    source: require("../../../assets/Banks/WISE/WISE.png"),
    aliases: [/\bwise\b/i],
    fallbackMonogram: "W"
  },
  yorkshireBuildingSociety: {
    source: require("../../../assets/Banks/YorkshireBuildingSociety/YorkshireBuildingSociety.png"),
    aliases: [/\byorkshire building society\b/i],
    fallbackMonogram: "YBS"
  },
  zemplerBank: {
    source: require("../../../assets/Banks/ZemplerBank/ZemplerBank.png"),
    aliases: [/\bzempler\b/i],
    fallbackMonogram: "ZB"
  }
} satisfies Record<string, BankLogoRecord>;

type BankLogoKey = keyof typeof BANK_LOGOS;

type KnownProviderIdentity = {
  shortCode: string | null;
  fullName: string;
};

const KNOWN_PROVIDER_IDENTITIES: Partial<Record<BankLogoKey, KnownProviderIdentity>> = {
  aib: { shortCode: "AIB", fullName: "Allied Irish Bank" },
  boi: { shortCode: "BOI", fullName: "Bank of Ireland" },
  ptsb: { shortCode: "PTSB", fullName: "Permanent TSB" },
  ulsterBank: { shortCode: "Ulster", fullName: "Ulster Bank" },
  revolut: { shortCode: "Revolut", fullName: "Revolut" },
  hsbc: { shortCode: "HSBC", fullName: "HSBC" },
  natWest: { shortCode: "NatWest", fullName: "NatWest" },
  bankOfScotland: { shortCode: "BOS", fullName: "Bank of Scotland" },
  royalBankOfScotland: { shortCode: "RBS", fullName: "Royal Bank of Scotland" },
  santander: { shortCode: "Santander", fullName: "Santander" },
  lloydsBank: { shortCode: "Lloyds", fullName: "Lloyds Bank" },
  nationwide: { shortCode: "Nationwide", fullName: "Nationwide Building Society" },
  danskeBank: { shortCode: "Danske", fullName: "Danske Bank" },
  monzo: { shortCode: "Monzo", fullName: "Monzo" },
  starlingBank: { shortCode: "Starling", fullName: "Starling Bank" },
  virginMoney: { shortCode: "Virgin", fullName: "Virgin Money" },
  wise: { shortCode: "Wise", fullName: "Wise" },
  americanExpress: { shortCode: "Amex", fullName: "American Express" },
  capitalOne: { shortCode: "Capital One", fullName: "Capital One" },
  barclays: { shortCode: "Barclays", fullName: "Barclays" }
};

export type ProviderBadgeInput = {
  providerId?: string | null;
  providerDisplayName?: string | null;
  providerIconUrl?: string | null;
  providerLogoUrl?: string | null;
};

export type ResolvedProviderBadge = {
  logoSource: ImageSourcePropType | null;
  displayName: string | null;
  monogram: string | null;
  bankLogoKey: BankLogoKey | null;
};

export type ResolvedConnectedBankIdentity = {
  title: string;
  shortCode: string | null;
  fullName: string | null;
  friendlyName: string;
  bankLogoKey: BankLogoKey | null;
};

export function resolveProviderBadge(input: ProviderBadgeInput): ResolvedProviderBadge {
  const providerName = normalizeLabel(input.providerDisplayName);
  const providerId = normalizeLabel(input.providerId);
  const bankLogoKey = resolveBankLogoKey(providerId, providerName);
  const bankLogo = bankLogoKey ? BANK_LOGOS[bankLogoKey] : null;

  return {
    logoSource: bankLogo?.source ?? null,
    displayName: providerName,
    monogram: bankLogo?.fallbackMonogram ?? deriveMonogram(providerName ?? providerId),
    bankLogoKey
  };
}

export function resolveConnectedBankIdentity(input: ProviderBadgeInput): ResolvedConnectedBankIdentity {
  const badge = resolveProviderBadge(input);
  const knownIdentity = badge.bankLogoKey ? KNOWN_PROVIDER_IDENTITIES[badge.bankLogoKey] ?? null : null;
  if (knownIdentity) {
    const fullName = knownIdentity.fullName.trim();
    const shortCode = knownIdentity.shortCode?.trim() || null;
    const title = shortCode && !equalsCaseInsensitive(shortCode, fullName)
      ? `${shortCode} - ${fullName}`
      : fullName;

    return {
      title,
      shortCode,
      fullName,
      friendlyName: fullName,
      bankLogoKey: badge.bankLogoKey
    };
  }

  const friendlyName = toFriendlyProviderName(input.providerDisplayName ?? input.providerId) ?? "Connected bank";
  return {
    title: friendlyName,
    shortCode: null,
    fullName: friendlyName,
    friendlyName,
    bankLogoKey: badge.bankLogoKey
  };
}

function resolveBankLogoKey(
  normalizedProviderId: string | null,
  normalizedProviderDisplayName: string | null
): BankLogoKey | null {
  const candidates = [normalizedProviderId, normalizedProviderDisplayName].filter(
    (value): value is string => Boolean(value)
  );

  for (const candidate of candidates) {
    for (const [key, value] of Object.entries(BANK_LOGOS) as [BankLogoKey, BankLogoRecord][]) {
      if (value.aliases.some((alias) => alias.test(candidate))) {
        return key;
      }
    }
  }

  return null;
}

function normalizeLabel(value: string | null | undefined): string | null {
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

function toFriendlyProviderName(value: string | null | undefined): string | null {
  if (!value) {
    return null;
  }

  const compact = value
    .trim()
    .replace(/^ob[-\s_]+/i, "")
    .replace(/[-_\s]+(ie|uk|gb|eu)$/i, "")
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  if (!compact) {
    return null;
  }

  const upper = compact.toUpperCase();
  const knownAcronyms = new Set(["AIB", "BOI", "PTSB", "TSB", "HSBC", "MBNA", "RBS", "IBAN"]);
  if (knownAcronyms.has(upper)) {
    return upper;
  }

  if (upper === "REVOLUT") {
    return "Revolut";
  }

  return compact
    .split(" ")
    .map((word) => (word.length === 0 ? word : `${word[0].toUpperCase()}${word.slice(1).toLowerCase()}`))
    .join(" ");
}

function equalsCaseInsensitive(left: string, right: string): boolean {
  return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0
    || left.toLowerCase() === right.toLowerCase();
}

function deriveMonogram(value: string | null): string | null {
  if (!value) {
    return null;
  }

  const words = value
    .split(/[\s&/-]+/)
    .map((segment) => segment.trim())
    .filter(Boolean);

  if (words.length === 0) {
    return null;
  }

  if (words.length === 1) {
    const cleaned = words[0].replace(/[^a-z0-9]/gi, "");
    if (cleaned.length <= 4) {
      return cleaned.toUpperCase();
    }

    return cleaned.slice(0, 2).toUpperCase();
  }

  return `${words[0][0] ?? ""}${words[1][0] ?? ""}`.toUpperCase();
}
