import type { ExpenseTaxonomyDomainDto } from "../../types/api";
import { getExpenseTaxonomyKeywordEntry } from "./expenseTaxonomyKeywordPack";

export type ExpenseTaxonomySearchIndexItem = {
  subcategoryId: number;
  categoryId: number;
  domainId: number;
  subcategoryName: string;
  categoryName: string;
  domainName: string;
  pathLabel: string;
  keywords: string[];
  normalizedName: string;
  normalizedNameJoined: string;
  normalizedKeywords: string[];
  normalizedKeywordJoined: string[];
  nameTokens: string[];
  keywordTokens: string[];
  pathTokens: string[];
};

export type ExpenseTaxonomySearchResult = {
  item: ExpenseTaxonomySearchIndexItem;
  score: number;
  reasons: string[];
};

const stopWords = new Set(["and", "the", "for", "to", "of", "a", "an", "with", "while"]);

function unique(values: string[]) {
  return Array.from(new Set(values.filter(Boolean)));
}

export function normalizeExpenseTaxonomySearchText(value: string) {
  return value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/&/g, " and ")
    .replace(/[+]/g, " plus ")
    .replace(/[\/\\]/g, " ")
    .replace(/-/g, " ")
    .replace(/[^a-z0-9\s]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function joinNormalized(value: string) {
  return value.replace(/\s+/g, "");
}

function tokenize(value: string) {
  return unique(
    normalizeExpenseTaxonomySearchText(value)
      .split(" ")
      .map((token) => token.trim())
      .filter((token) => token.length > 1 && !stopWords.has(token))
  );
}

function keywordVariants(values: string[]) {
  const expanded = values.flatMap((value) => {
    const normalized = normalizeExpenseTaxonomySearchText(value);
    const joined = joinNormalized(normalized);
    return joined && joined !== normalized ? [normalized, joined] : [normalized];
  });
  return unique(expanded);
}

function tokenSimilarity(left: string, right: string) {
  if (!left || !right) {
    return 0;
  }

  const distance = levenshteinDistance(left, right);
  const longest = Math.max(left.length, right.length);
  return longest === 0 ? 0 : 1 - distance / longest;
}

function levenshteinDistance(left: string, right: string) {
  if (left === right) {
    return 0;
  }

  const rows = left.length + 1;
  const cols = right.length + 1;
  const matrix = Array.from({ length: rows }, () => Array<number>(cols).fill(0));

  for (let row = 0; row < rows; row += 1) {
    matrix[row][0] = row;
  }
  for (let col = 0; col < cols; col += 1) {
    matrix[0][col] = col;
  }

  for (let row = 1; row < rows; row += 1) {
    for (let col = 1; col < cols; col += 1) {
      const substitutionCost = left[row - 1] === right[col - 1] ? 0 : 1;
      matrix[row][col] = Math.min(
        matrix[row - 1][col] + 1,
        matrix[row][col - 1] + 1,
        matrix[row - 1][col - 1] + substitutionCost
      );
    }
  }

  return matrix[left.length][right.length];
}

export function buildExpenseTaxonomySearchIndex(domains: ExpenseTaxonomyDomainDto[]): ExpenseTaxonomySearchIndexItem[] {
  return domains
    .filter((domain) => domain.isActive && domain.isUserSelectable && !domain.isSystemDomain)
    .flatMap((domain) =>
      domain.categories
        .filter((category) => category.isActive && category.isUserSelectable)
        .flatMap((category) =>
          category.subcategories
            .filter((subcategory) => subcategory.isActive && subcategory.isUserSelectable)
            .map((subcategory) => {
              const keywordEntry = getExpenseTaxonomyKeywordEntry(subcategory.id);
              const keywords = unique([
                ...(keywordEntry?.keywords ?? []),
                ...(keywordEntry?.aliases ?? []),
                ...(keywordEntry?.merchantHints ?? []),
                ...subcategory.keywords,
                ...subcategory.aliases,
                ...subcategory.merchantHints,
                subcategory.name,
                keywordEntry?.displayName ?? "",
                category.name,
                domain.name
              ]);
              const normalizedName = normalizeExpenseTaxonomySearchText(subcategory.name);
              const normalizedKeywords = keywordVariants(keywords);
              const pathLabel = `${category.name} • ${domain.name}`;

              return {
                subcategoryId: subcategory.id,
                categoryId: category.id,
                domainId: domain.id,
                subcategoryName: subcategory.name,
                categoryName: category.name,
                domainName: domain.name,
                pathLabel,
                keywords,
                normalizedName,
                normalizedNameJoined: joinNormalized(normalizedName),
                normalizedKeywords,
                normalizedKeywordJoined: normalizedKeywords.map(joinNormalized),
                nameTokens: tokenize(subcategory.name),
                keywordTokens: unique(keywords.flatMap(tokenize)),
                pathTokens: unique([...tokenize(category.name), ...tokenize(domain.name)])
              } satisfies ExpenseTaxonomySearchIndexItem;
            })
        )
    );
}

export function searchExpenseTaxonomy(
  index: ExpenseTaxonomySearchIndexItem[],
  rawQuery: string,
  limit = 24
): ExpenseTaxonomySearchResult[] {
  const query = normalizeExpenseTaxonomySearchText(rawQuery);
  if (!query) {
    return [];
  }

  const queryJoined = joinNormalized(query);
  const queryTokens = tokenize(rawQuery);

  return index
    .map((item) => {
      let score = 0;
      const reasons: string[] = [];
      let matchedName = false;
      let matchedKeyword = false;
      let matchedStrong = false;

      if (item.normalizedName === query || item.normalizedNameJoined === queryJoined) {
        score += 1400;
        matchedName = true;
        matchedStrong = true;
        reasons.push("exact-name");
      }

      if (item.normalizedKeywords.includes(query) || item.normalizedKeywordJoined.includes(queryJoined)) {
        score += 1200;
        matchedKeyword = true;
        matchedStrong = true;
        reasons.push("exact-keyword");
      }

      if (item.normalizedName.startsWith(query) || item.normalizedNameJoined.startsWith(queryJoined)) {
        score += 920;
        matchedName = true;
        matchedStrong = true;
        reasons.push("prefix-name");
      }

      if (item.normalizedKeywords.some((keyword) => keyword.startsWith(query)) || item.normalizedKeywordJoined.some((keyword) => keyword.startsWith(queryJoined))) {
        score += 760;
        matchedKeyword = true;
        matchedStrong = true;
        reasons.push("prefix-keyword");
      }

      queryTokens.forEach((token) => {
        item.nameTokens.forEach((nameToken, indexInName) => {
          if (nameToken === token) {
            score += 220 - Math.min(indexInName * 10, 40);
            matchedName = true;
            reasons.push(`token-name:${token}`);
          } else if (nameToken.startsWith(token)) {
            score += 165 - Math.min(indexInName * 8, 32);
            matchedName = true;
            reasons.push(`token-prefix-name:${token}`);
          } else if (nameToken.includes(token)) {
            score += 110;
            matchedName = true;
            reasons.push(`token-substring-name:${token}`);
          } else if (!matchedStrong && token.length >= 4) {
            const similarity = tokenSimilarity(token, nameToken);
            if (similarity >= 0.72) {
              score += Math.round(55 * similarity);
              matchedName = true;
              reasons.push(`token-fuzzy-name:${token}`);
            }
          }
        });

        item.keywordTokens.forEach((keywordToken) => {
          if (keywordToken === token) {
            score += 170;
            matchedKeyword = true;
            reasons.push(`token-keyword:${token}`);
          } else if (keywordToken.startsWith(token)) {
            score += 130;
            matchedKeyword = true;
            reasons.push(`token-prefix-keyword:${token}`);
          } else if (keywordToken.includes(token)) {
            score += 85;
            matchedKeyword = true;
            reasons.push(`token-substring-keyword:${token}`);
          } else if (!matchedStrong && token.length >= 4) {
            const similarity = tokenSimilarity(token, keywordToken);
            if (similarity >= 0.76) {
              score += Math.round(42 * similarity);
              matchedKeyword = true;
              reasons.push(`token-fuzzy-keyword:${token}`);
            }
          }
        });

        if (item.pathTokens.includes(token)) {
          score += 36;
          reasons.push(`token-path:${token}`);
        }
      });

      if (item.normalizedName.includes(query) || item.normalizedNameJoined.includes(queryJoined)) {
        const position = item.normalizedName.indexOf(query);
        score += 150 - Math.max(position, 0);
        matchedName = true;
        reasons.push("substring-name");
      }

      if (item.normalizedKeywords.some((keyword) => keyword.includes(query)) || item.normalizedKeywordJoined.some((keyword) => keyword.includes(queryJoined))) {
        score += 115;
        matchedKeyword = true;
        reasons.push("substring-keyword");
      }

      if (!matchedStrong && !matchedName && !matchedKeyword && query.length >= 4) {
        const similarity = Math.max(
          ...item.nameTokens.map((token) => tokenSimilarity(queryJoined, joinNormalized(token))),
          ...item.keywordTokens.map((token) => tokenSimilarity(queryJoined, joinNormalized(token))),
          0
        );

        if (similarity >= 0.74) {
          score += Math.round(70 * similarity);
          reasons.push("fallback-fuzzy");
        }
      }

      if (matchedName && matchedKeyword) {
        score += 90;
        reasons.push("name+keyword-boost");
      }

      if (score <= 0) {
        return null;
      }

      return {
        item,
        score,
        reasons: unique(reasons)
      } satisfies ExpenseTaxonomySearchResult;
    })
    .filter((result): result is ExpenseTaxonomySearchResult => Boolean(result))
    .sort((left, right) => {
      if (right.score !== left.score) {
        return right.score - left.score;
      }
      return left.item.subcategoryName.localeCompare(right.item.subcategoryName);
    })
    .slice(0, limit);
}


