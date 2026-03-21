import { ACTIVITY_SEARCH_TOKEN_LABELS } from "./activitySearch.constants";
import type {
  ActivitySearchToken,
  ActivitySearchTokenType
} from "./activitySearch.types";

const TOKEN_ID_PREFIX = "activity-token";

export function createActivityTokenId(type: ActivitySearchTokenType) {
  return `${TOKEN_ID_PREFIX}-${type}-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`;
}

export function createEmptySearchToken(type: ActivitySearchTokenType): ActivitySearchToken {
  return {
    id: createActivityTokenId(type),
    type,
    label: ACTIVITY_SEARCH_TOKEN_LABELS[type],
    displayValue: "",
    rawValue: "",
    value: "",
    isDraft: true
  };
}

export function upsertUniqueToken(
  tokens: ActivitySearchToken[],
  nextToken: ActivitySearchToken
) {
  const withoutType = tokens.filter((item) => item.type !== nextToken.type);
  return [...withoutType, nextToken];
}

export function removeTokenById(tokens: ActivitySearchToken[], tokenId: string) {
  return tokens.filter((item) => item.id !== tokenId);
}

export function getTokenByType(
  tokens: ActivitySearchToken[],
  type: ActivitySearchTokenType
) {
  return tokens.find((item) => item.type === type) ?? null;
}

export function updateTokenById(
  tokens: ActivitySearchToken[],
  tokenId: string,
  updater: (token: ActivitySearchToken) => ActivitySearchToken
) {
  return tokens.map((item) => (item.id === tokenId ? updater(item) : item));
}

