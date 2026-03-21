import {
  ACTIVITY_SEARCH_MAX_MERCHANT_SUGGESTIONS
} from "./activitySearch.constants";
import type {
  ActivityMerchantDictionaryItem,
  ActivityMerchantSuggestion
} from "./activitySearch.types";

export const ACTIVITY_MERCHANT_DICTIONARY: ActivityMerchantDictionaryItem[] = [
  {
    id: "tesco",
    displayName: "Tesco",
    normalizedName: "tesco",
    aliases: ["tecso", "tesco plc", "tesc", "tesco store"]
  },
  {
    id: "supervalu",
    displayName: "SuperValu",
    normalizedName: "supervalu",
    aliases: ["supervalue", "super valu", "super value"]
  },
  {
    id: "lidl",
    displayName: "Lidl",
    normalizedName: "lidl",
    aliases: []
  },
  {
    id: "aldi",
    displayName: "Aldi",
    normalizedName: "aldi",
    aliases: []
  },
  {
    id: "dunnes-stores",
    displayName: "Dunnes Stores",
    normalizedName: "dunnes stores",
    aliases: ["dunnes", "dunne stores", "dunnes stores"]
  },
  {
    id: "amazon",
    displayName: "Amazon",
    normalizedName: "amazon",
    aliases: ["amzn", "amazon eu", "amazon marketplace"]
  },
  {
    id: "apple",
    displayName: "Apple",
    normalizedName: "apple",
    aliases: ["apple.com", "itunes", "app store"]
  },
  {
    id: "spotify",
    displayName: "Spotify",
    normalizedName: "spotify",
    aliases: ["spotify ltd", "spotify premium"]
  },
  {
    id: "netflix",
    displayName: "Netflix",
    normalizedName: "netflix",
    aliases: ["netflix.com"]
  },
  {
    id: "aa-membership",
    displayName: "AA Membership",
    normalizedName: "aa membership",
    aliases: ["the aa", "aa renewal", "aa insurance"]
  },
  {
    id: "revolut",
    displayName: "Revolut",
    normalizedName: "revolut",
    aliases: ["revoult", "revolt", "revolut card"]
  },
  {
    id: "ryanair",
    displayName: "Ryanair",
    normalizedName: "ryanair",
    aliases: []
  },
  {
    id: "airbnb",
    displayName: "Airbnb",
    normalizedName: "airbnb",
    aliases: ["air bnb"]
  },
  {
    id: "deliveroo",
    displayName: "Deliveroo",
    normalizedName: "deliveroo",
    aliases: ["deliver oo"]
  },
  {
    id: "ubereats",
    displayName: "Uber Eats",
    normalizedName: "uber eats",
    aliases: ["uber eat", "uber-eats"]
  }
];

export function normalizeMerchantText(value: string) {
  return value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-zA-Z0-9\s]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();
}

function levenshtein(left: string, right: string) {
  const rows = left.length + 1;
  const cols = right.length + 1;
  const matrix = Array.from({ length: rows }, () => Array<number>(cols).fill(0));

  for (let i = 0; i < rows; i += 1) {
    matrix[i][0] = i;
  }

  for (let j = 0; j < cols; j += 1) {
    matrix[0][j] = j;
  }

  for (let i = 1; i < rows; i += 1) {
    for (let j = 1; j < cols; j += 1) {
      const cost = left[i - 1] === right[j - 1] ? 0 : 1;
      matrix[i][j] = Math.min(
        matrix[i - 1][j] + 1,
        matrix[i][j - 1] + 1,
        matrix[i - 1][j - 1] + cost
      );
    }
  }

  return matrix[rows - 1][cols - 1];
}

function scoreCandidate(query: string, candidate: string) {
  if (!query || !candidate) {
    return 0;
  }

  if (candidate === query) {
    return 1000;
  }

  if (candidate.startsWith(query)) {
    return 900 - Math.max(candidate.length - query.length, 0);
  }

  if (candidate.includes(query)) {
    return 760 - Math.max(candidate.length - query.length, 0);
  }

  const queryTokens = query.split(" ");
  const candidateTokens = candidate.split(" ");
  const tokenHits = queryTokens.filter((token) =>
    candidateTokens.some((candidateToken) => candidateToken.startsWith(token))
  ).length;
  if (tokenHits > 0) {
    return 620 + tokenHits * 24;
  }

  const distance = levenshtein(query, candidate);
  if (distance <= 2) {
    return 560 - distance * 60;
  }

  if (query.length >= 4 && distance <= 3) {
    return 430 - distance * 48;
  }

  return 0;
}

export function getMerchantSuggestions(
  query: string,
  options?: {
    additionalMerchants?: string[];
    limit?: number;
  }
): ActivityMerchantSuggestion[] {
  const normalizedQuery = normalizeMerchantText(query);
  if (!normalizedQuery) {
    return [];
  }

  const dynamicEntries: ActivityMerchantDictionaryItem[] = (options?.additionalMerchants ?? [])
    .map((displayName) => ({
      id: `dynamic-${normalizeMerchantText(displayName).replace(/\s+/g, "-")}`,
      displayName: displayName.trim(),
      normalizedName: normalizeMerchantText(displayName),
      aliases: []
    }))
    .filter((item) => item.normalizedName.length > 0);

  const deduped = new Map<string, ActivityMerchantDictionaryItem>();
  [...ACTIVITY_MERCHANT_DICTIONARY, ...dynamicEntries].forEach((item) => {
    if (!deduped.has(item.normalizedName)) {
      deduped.set(item.normalizedName, item);
    }
  });

  const scored = [...deduped.values()]
    .map((item) => {
      const candidates = [item.normalizedName, ...item.aliases.map(normalizeMerchantText)];
      const scores = candidates.map((candidate) => scoreCandidate(normalizedQuery, candidate));
      const bestScore = Math.max(...scores, 0);
      return {
        displayName: item.displayName,
        normalizedName: item.normalizedName,
        score: bestScore
      };
    })
    .filter((item) => item.score > 0)
    .sort((left, right) => {
      if (right.score !== left.score) {
        return right.score - left.score;
      }

      return left.displayName.localeCompare(right.displayName);
    });

  return scored.slice(0, options?.limit ?? ACTIVITY_SEARCH_MAX_MERCHANT_SUGGESTIONS);
}

