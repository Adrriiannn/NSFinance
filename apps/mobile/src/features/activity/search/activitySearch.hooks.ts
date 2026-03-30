import { useCallback, useMemo, useState } from "react";
import type { TransactionPlannerAnnotation } from "../../../providers/PlannerProvider";
import type { TransactionDto } from "../../../types/api";
import {
  ACTIVITY_SEARCH_FILTER_OPTIONS
} from "./activitySearch.constants";
import {
  dedupeCurrencyCodes,
  formatActivityTokenAmount,
  getActivityCurrencyMetadata,
  parseActivityAmountInput
} from "./activitySearch.currencies";
import {
  buildActivityDateSuggestions,
  parseActivityDateInput,
  toActivityIsoDate
} from "./activitySearch.parsers";
import {
  getMerchantSuggestions
} from "./activitySearch.merchants";
import {
  createActivityTokenId,
  getTokenByType,
  removeTokenById,
  updateTokenById,
  upsertUniqueToken
} from "./activitySearch.tokens";
import type {
  ActivityCategoryTokenValue,
  ActivityDateTokenValue,
  ActivitySearchSnapshot,
  ActivitySearchDropdownMode,
  ActivitySearchToken,
  ActivitySearchTokenType,
  ActivityTaxonomySearchEntry
} from "./activitySearch.types";
import { filterTransactionsByActivitySearch } from "./activitySearch.utils";

type UseActivitySearchParams = {
  transactions: TransactionDto[];
  annotations: Record<string, TransactionPlannerAnnotation>;
  preferredCurrency?: string | null;
  accountCurrencies?: string[];
  taxonomyEntries: ActivityTaxonomySearchEntry[];
  onRequestCategoryPicker: (snapshot: ActivitySearchSnapshot) => void;
  initialSnapshot?: ActivitySearchSnapshot | null;
};

function buildCategoryDisplayValue(value: ActivityCategoryTokenValue) {
  if (!value.domainName.trim()) {
    return "Select categories";
  }

  return value.domainName;
}

function normalizeCategoryTokenDisplay(token: ActivitySearchToken) {
  if (token.type !== "category") {
    return token;
  }

  const current = token.value as ActivityCategoryTokenValue;
  const normalizedValue: ActivityCategoryTokenValue = {
    ...current,
    excludedCategoryIds: current.excludedCategoryIds ?? [],
    excludedSubcategoryIds: current.excludedSubcategoryIds ?? []
  };
  const display = buildCategoryDisplayValue(normalizedValue);

  return {
    ...token,
    value: normalizedValue,
    displayValue: display,
    rawValue: display
  };
}

function normalizeSnapshotTokens(tokens: ActivitySearchToken[]) {
  return tokens.map((token) => normalizeCategoryTokenDisplay(token));
}

function buildTokenLabel(type: ActivitySearchTokenType) {
  return type;
}

function createCategoryToken(): ActivitySearchToken {
  const value: ActivityCategoryTokenValue = {
    subcategoryId: null,
    domainId: null,
    categoryId: null,
    domainName: "",
    categoryName: "",
    subcategoryName: "",
    scope: "domain",
    excludedCategoryIds: [],
    excludedSubcategoryIds: []
  };

  return {
    id: createActivityTokenId("category"),
    type: "category",
    label: "category",
    displayValue: "Select categories",
    rawValue: "",
    value,
    isDraft: false
  };
}

function buildCurrencyToken(currencyCode: string): ActivitySearchToken {
  return {
    id: createActivityTokenId("currency"),
    type: "currency",
    label: "currency",
    displayValue: currencyCode,
    rawValue: currencyCode,
    value: currencyCode,
    isDraft: false
  };
}

function buildAmountDraftToken(): ActivitySearchToken {
  return {
    id: createActivityTokenId("amount"),
    type: "amount",
    label: "amount",
    displayValue: "",
    rawValue: "",
    value: {
      amount: null,
      rawAmount: ""
    },
    isDraft: true
  };
}

function buildTextDraftToken(type: "transaction" | "merchant" | "date"): ActivitySearchToken {
  return {
    id: createActivityTokenId(type),
    type,
    label: buildTokenLabel(type),
    displayValue: "",
    rawValue: "",
    value: "",
    isDraft: true
  };
}

export function useActivitySearch({
  transactions,
  annotations,
  preferredCurrency,
  accountCurrencies,
  taxonomyEntries,
  onRequestCategoryPicker,
  initialSnapshot
}: UseActivitySearchParams) {
  const [tokens, setTokens] = useState<ActivitySearchToken[]>(
    normalizeSnapshotTokens(initialSnapshot?.tokens ?? [])
  );
  const [rawSearchText, setRawSearchText] = useState(initialSnapshot?.rawSearchText ?? "");
  const [searchFocused, setSearchFocused] = useState(false);
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const [activeTokenId, setActiveTokenId] = useState<string | null>(null);
  const [activeTokenType, setActiveTokenType] = useState<ActivitySearchTokenType | null>(null);
  const [activeTokenDraft, setActiveTokenDraft] = useState("");

  const taxonomyBySubcategoryId = useMemo(
    () =>
      new Map(
        taxonomyEntries.map((entry) => [entry.subcategoryId, entry] as const)
      ),
    [taxonomyEntries]
  );

  const observedMerchants = useMemo(() => {
    const values = new Set<string>();
    transactions.forEach((item) => {
      const annotated = annotations[item.id]?.merchant?.trim();
      if (annotated) {
        values.add(annotated);
      } else if (item.description.trim()) {
        values.add(item.description.trim());
      }
    });
    return [...values];
  }, [annotations, transactions]);

  const resolvedPreferredCurrency = useMemo(() => {
    const currencies = dedupeCurrencyCodes([
      preferredCurrency ?? "",
      ...(accountCurrencies ?? []),
      ...transactions.map((item) => item.currency),
      "EUR"
    ]);
    return currencies[0] ?? "EUR";
  }, [accountCurrencies, preferredCurrency, transactions]);

  const availableCurrencies = useMemo(
    () =>
      dedupeCurrencyCodes([
        resolvedPreferredCurrency,
        ...(accountCurrencies ?? []),
        ...transactions.map((item) => item.currency)
      ]),
    [accountCurrencies, resolvedPreferredCurrency, transactions]
  );

  const activeToken = useMemo(
    () => tokens.find((token) => token.id === activeTokenId) ?? null,
    [activeTokenId, tokens]
  );

  const merchantSuggestions = useMemo(() => {
    if (activeTokenType !== "merchant") {
      return [];
    }
    return getMerchantSuggestions(activeTokenDraft, {
      additionalMerchants: observedMerchants
    });
  }, [activeTokenDraft, activeTokenType, observedMerchants]);

  const dateSuggestionResult = useMemo(() => {
    if (activeTokenType !== "date") {
      return {
        parseResult: { kind: "none" } as const,
        suggestions: []
      };
    }

    return buildActivityDateSuggestions(activeTokenDraft, new Date());
  }, [activeTokenDraft, activeTokenType]);

  const filterOptionAvailability = useMemo(() => {
    const preferredCurrencyCode = (resolvedPreferredCurrency || "EUR").toUpperCase();
    return ACTIVITY_SEARCH_FILTER_OPTIONS.map((option) => {
      if (option.tokenType === "amount") {
        const hasAmount = Boolean(getTokenByType(tokens, "amount"));
        const hasCurrency = Boolean(getTokenByType(tokens, "currency"));
        return {
          ...option,
          hint: `currency: ${preferredCurrencyCode} / amount: transaction's amount`,
          disabled: hasAmount || hasCurrency
        };
      }

      return {
        ...option,
        disabled: Boolean(getTokenByType(tokens, option.tokenType))
      };
    });
  }, [resolvedPreferredCurrency, tokens]);

  const dropdownMode: ActivitySearchDropdownMode = useMemo(() => {
    if (!dropdownOpen || !searchFocused) {
      return "hidden";
    }

    if (activeTokenType === "merchant") {
      return "merchantSuggestions";
    }

    if (activeTokenType === "date") {
      return "dateSuggestions";
    }

    if (activeTokenType === "currency") {
      return "currencySuggestions";
    }

    return "filters";
  }, [activeTokenType, dropdownOpen, searchFocused]);

  const filteredTransactions = useMemo(
    () =>
      filterTransactionsByActivitySearch({
        transactions,
        annotations,
        tokens,
        freeText: rawSearchText,
        taxonomyBySubcategoryId
      }),
    [annotations, rawSearchText, taxonomyBySubcategoryId, tokens, transactions]
  );

  const dismissDropdown = useCallback(() => {
    setDropdownOpen(false);
    setSearchFocused(false);
    setActiveTokenType(null);
    setActiveTokenId(null);
    setActiveTokenDraft("");
  }, []);

  const focusSearch = useCallback(() => {
    setSearchFocused(true);
    if (!activeTokenType) {
      setActiveTokenId(null);
    }
  }, [activeTokenType]);

  const openFilters = useCallback(() => {
    setSearchFocused(true);
    setDropdownOpen(true);
    setActiveTokenType(null);
    setActiveTokenId(null);
    setActiveTokenDraft("");
  }, []);

  const beginEditingToken = useCallback(
    (tokenId: string) => {
      const token = tokens.find((item) => item.id === tokenId);
      if (!token) {
        return;
      }

      if (token.type === "category") {
        onRequestCategoryPicker({
          tokens,
          rawSearchText
        });
        return;
      }

      if (token.type === "currency") {
        setActiveTokenId(token.id);
        setActiveTokenType("currency");
        setDropdownOpen(true);
        setSearchFocused(true);
        return;
      }

      setActiveTokenId(token.id);
      setActiveTokenType(token.type);
      setActiveTokenDraft(token.rawValue);
      setDropdownOpen(token.type === "date" || token.type === "merchant");
      setSearchFocused(true);
    },
    [onRequestCategoryPicker, rawSearchText, tokens]
  );

  const ensureTokenDraft = useCallback(
    (type: "transaction" | "merchant" | "date") => {
      const existing = getTokenByType(tokens, type);
      const token = existing ?? buildTextDraftToken(type);
      const nextTokens = existing ? tokens : [...tokens, token];
      const withUnique = upsertUniqueToken(nextTokens, token);
      setTokens(withUnique);
      setActiveTokenId(token.id);
      setActiveTokenType(type);
      setActiveTokenDraft(existing?.rawValue ?? "");
      setDropdownOpen(type === "merchant" || type === "date");
      setSearchFocused(true);
    },
    [tokens]
  );

  const ensureAmountFilter = useCallback(() => {
    const existingCurrency = getTokenByType(tokens, "currency");
    const existingAmount = getTokenByType(tokens, "amount");
    const currencyCode = String(existingCurrency?.value ?? resolvedPreferredCurrency);
    const nextCurrency = existingCurrency ?? buildCurrencyToken(currencyCode);
    const nextAmount = existingAmount ?? buildAmountDraftToken();

    let nextTokens = upsertUniqueToken(tokens, nextCurrency);
    nextTokens = upsertUniqueToken(nextTokens, nextAmount);
    setTokens(nextTokens);
    setActiveTokenId(nextAmount.id);
    setActiveTokenType("amount");
    setActiveTokenDraft(existingAmount?.rawValue ?? "");
    setDropdownOpen(false);
    setSearchFocused(true);
  }, [resolvedPreferredCurrency, tokens]);

  const selectFilterOption = useCallback(
    (type: ActivitySearchTokenType) => {
      if (type === "category") {
        const existing = getTokenByType(tokens, "category");
        const nextCategoryToken = existing ?? createCategoryToken();
        const nextTokens = existing ? tokens : [...tokens, nextCategoryToken];
        const withCategory = upsertUniqueToken(nextTokens, nextCategoryToken);
        setTokens(withCategory);
        setDropdownOpen(false);
        setSearchFocused(true);
        onRequestCategoryPicker({
          tokens: withCategory,
          rawSearchText
        });
        return;
      }

      if (type === "amount") {
        ensureAmountFilter();
        return;
      }

      if (type === "transaction" || type === "merchant" || type === "date") {
        ensureTokenDraft(type);
      }
    },
    [ensureAmountFilter, ensureTokenDraft, onRequestCategoryPicker, rawSearchText, tokens]
  );

  const removeToken = useCallback(
    (tokenId: string) => {
      const token = tokens.find((item) => item.id === tokenId);
      if (!token) {
        return;
      }

      let nextTokens = removeTokenById(tokens, tokenId);
      if (token.type === "amount") {
        const currencyToken = getTokenByType(nextTokens, "currency");
        if (currencyToken) {
          nextTokens = removeTokenById(nextTokens, currencyToken.id);
        }
      }

      if (token.type === "currency") {
        const amountToken = getTokenByType(nextTokens, "amount");
        if (amountToken) {
          nextTokens = removeTokenById(nextTokens, amountToken.id);
        }
      }

      setTokens(nextTokens);
      if (activeTokenId === tokenId) {
        setActiveTokenId(null);
        setActiveTokenType(null);
        setActiveTokenDraft("");
      }
    },
    [activeTokenId, tokens]
  );

  const confirmTokenDraft = useCallback((options?: { reopenDropdown?: boolean }) => {
    const reopenDropdown = options?.reopenDropdown ?? true;
    if (!activeToken || !activeTokenType) {
      return;
    }

    if (activeTokenType === "transaction" || activeTokenType === "merchant") {
      const nextRaw = activeTokenDraft.trim();
      if (!nextRaw) {
        removeToken(activeToken.id);
        setDropdownOpen(reopenDropdown);
        return;
      }

      setTokens((current) =>
        updateTokenById(current, activeToken.id, (token) => ({
          ...token,
          rawValue: nextRaw,
          displayValue: nextRaw,
          value: nextRaw,
          isDraft: false
        }))
      );
      setActiveTokenType(null);
      setActiveTokenId(null);
      setActiveTokenDraft("");
      setDropdownOpen(reopenDropdown);
      return;
    }

    if (activeTokenType === "amount") {
      const currencyToken = getTokenByType(tokens, "currency");
      const currencyCode = String(currencyToken?.value ?? resolvedPreferredCurrency);
      const parsedAmount = parseActivityAmountInput(activeTokenDraft, currencyCode);
      if (parsedAmount === null) {
        return;
      }

      setTokens((current) =>
        updateTokenById(current, activeToken.id, (token) => ({
          ...token,
          rawValue: activeTokenDraft.trim(),
          displayValue: formatActivityTokenAmount(parsedAmount, currencyCode),
          value: {
            amount: parsedAmount,
            rawAmount: activeTokenDraft.trim()
          },
          isDraft: false
        }))
      );
      setActiveTokenType(null);
      setActiveTokenId(null);
      setActiveTokenDraft("");
      setDropdownOpen(reopenDropdown);
      return;
    }

    if (activeTokenType === "date") {
      const parseResult = parseActivityDateInput(activeTokenDraft, new Date());
      if (parseResult.kind === "none") {
        return;
      }

      const value: ActivityDateTokenValue =
        parseResult.kind === "exact"
          ? {
              mode: "exact",
              isoDate: toActivityIsoDate(parseResult.date),
              displayLabel: parseResult.displayLabel
            }
          : {
              mode: "weekday",
              weekday: parseResult.weekday,
              displayLabel: parseResult.displayLabel
            };

      setTokens((current) =>
        updateTokenById(current, activeToken.id, (token) => ({
          ...token,
          rawValue: activeTokenDraft.trim(),
          displayValue: parseResult.displayLabel,
          value,
          isDraft: false
        }))
      );
      setActiveTokenType(null);
      setActiveTokenId(null);
      setActiveTokenDraft("");
      setDropdownOpen(reopenDropdown);
    }
  }, [
    activeToken,
    activeTokenDraft,
    activeTokenType,
    removeToken,
    resolvedPreferredCurrency,
    tokens
  ]);

  const selectMerchantSuggestion = useCallback(
    (value: string) => {
      if (!activeToken || activeToken.type !== "merchant") {
        return;
      }

      setTokens((current) =>
        updateTokenById(current, activeToken.id, (token) => ({
          ...token,
          rawValue: value,
          displayValue: value,
          value,
          isDraft: false
        }))
      );
      setActiveTokenType(null);
      setActiveTokenId(null);
      setActiveTokenDraft("");
      setDropdownOpen(false);
    },
    [activeToken]
  );

  const selectDateSuggestion = useCallback(
    (selection: {
      mode: "exact" | "weekday";
      label: string;
      isoDate?: string;
      weekday?: number;
    }) => {
      if (!activeToken || activeToken.type !== "date") {
        return;
      }

      const value: ActivityDateTokenValue =
        selection.mode === "exact"
          ? {
              mode: "exact",
              isoDate: selection.isoDate ?? toActivityIsoDate(new Date()),
              displayLabel: selection.label
            }
          : {
              mode: "weekday",
              weekday: selection.weekday ?? new Date().getDay(),
              displayLabel: selection.label
            };

      setTokens((current) =>
        updateTokenById(current, activeToken.id, (token) => ({
          ...token,
          rawValue: selection.label,
          displayValue: selection.label,
          value,
          isDraft: false
        }))
      );
      setActiveTokenType(null);
      setActiveTokenId(null);
      setActiveTokenDraft("");
      setDropdownOpen(true);
    },
    [activeToken]
  );

  const selectCurrency = useCallback(
    (code: string) => {
      const normalized = code.toUpperCase();
      const amountToken = getTokenByType(tokens, "amount");

      setTokens((current) => {
        let next = current;
        const currentAmountToken = getTokenByType(next, "amount");
        const currentAmountValue = currentAmountToken?.value as
          | { amount: number | null }
          | undefined;
        const resolvedAmount = currentAmountValue?.amount ?? null;
        const existingCurrency = getTokenByType(next, "currency");

        if (existingCurrency) {
          next = updateTokenById(next, existingCurrency.id, (token) => ({
            ...token,
            rawValue: normalized,
            displayValue: normalized,
            value: normalized
          }));
        } else {
          const currencyToken = {
            ...buildCurrencyToken(normalized),
            rawValue: normalized,
            displayValue: normalized,
            value: normalized
          };

          if (currentAmountToken) {
            const amountIndex = next.findIndex((token) => token.id === currentAmountToken.id);
            if (amountIndex >= 0) {
              next = [
                ...next.slice(0, amountIndex),
                currencyToken,
                ...next.slice(amountIndex)
              ];
            } else {
              next = [...next, currencyToken];
            }
          } else {
            next = [...next, currencyToken];
          }
        }

        if (currentAmountToken && resolvedAmount !== null) {
          next = updateTokenById(next, currentAmountToken.id, (token) => ({
            ...token,
            displayValue: formatActivityTokenAmount(resolvedAmount, normalized)
          }));
        }

        return next;
      });

      if (amountToken) {
        setActiveTokenType("amount");
        setActiveTokenId(amountToken.id);
        setActiveTokenDraft(amountToken.rawValue);
        setDropdownOpen(false);
      } else {
        setActiveTokenType(null);
        setActiveTokenId(null);
        setActiveTokenDraft("");
        setDropdownOpen(true);
      }
    },
    [tokens]
  );

  const applyPickedCategory = useCallback(
    (selection: {
      scope: "domain" | "category" | "subcategory";
      domainId: number | null;
      domainName: string;
      categoryId: number | null;
      categoryName: string;
      subcategoryId: number | null;
      subcategoryName: string;
      excludedCategoryIds: number[];
      excludedSubcategoryIds: number[];
    } | null) => {
      if (!selection) {
        return;
      }

      const mappedSubcategory = selection.subcategoryId
        ? taxonomyBySubcategoryId.get(selection.subcategoryId)
        : null;

      setTokens((current) => {
        const existing = getTokenByType(current, "category");
        const nextValue: ActivityCategoryTokenValue = {
          subcategoryId: selection.subcategoryId ?? mappedSubcategory?.subcategoryId ?? null,
          domainId: selection.domainId ?? mappedSubcategory?.domainId ?? null,
          categoryId: selection.categoryId ?? mappedSubcategory?.categoryId ?? null,
          domainName: selection.domainName || mappedSubcategory?.domainName || "",
          categoryName: selection.categoryName || mappedSubcategory?.categoryName || "",
          subcategoryName: selection.subcategoryName || mappedSubcategory?.subcategoryName || "",
          scope: selection.scope,
          excludedCategoryIds: selection.excludedCategoryIds ?? [],
          excludedSubcategoryIds: selection.excludedSubcategoryIds ?? []
        };
        const displayValue = buildCategoryDisplayValue(nextValue);

        const categoryToken: ActivitySearchToken = {
          id: existing?.id ?? createActivityTokenId("category"),
          type: "category",
          label: "category",
          displayValue,
          rawValue: displayValue,
          value: nextValue,
          isDraft: false
        };

        return upsertUniqueToken(existing ? current : [...current, categoryToken], categoryToken);
      });

      setSearchFocused(true);
      setDropdownOpen(true);
    },
    [taxonomyBySubcategoryId]
  );

  const clearSearch = useCallback(() => {
    setTokens([]);
    setRawSearchText("");
    setActiveTokenId(null);
    setActiveTokenType(null);
    setActiveTokenDraft("");
    setDropdownOpen(false);
  }, []);

  const getSnapshot = useCallback(
    (): ActivitySearchSnapshot => ({
      rawSearchText,
      tokens
    }),
    [rawSearchText, tokens]
  );

  const setSnapshot = useCallback((snapshot: ActivitySearchSnapshot | null) => {
    if (!snapshot) {
      return;
    }

    setTokens(normalizeSnapshotTokens(snapshot.tokens ?? []));
    setRawSearchText(snapshot.rawSearchText ?? "");
  }, []);

  const activeCurrencyCode = String(
    getTokenByType(tokens, "currency")?.value ?? resolvedPreferredCurrency
  ).toUpperCase();
  const activeCurrencyMetadata = getActivityCurrencyMetadata(activeCurrencyCode);

  return {
    tokens,
    rawSearchText,
    setRawSearchText,
    activeTokenId,
    activeTokenType,
    activeTokenDraft,
    setActiveTokenDraft,
    searchFocused,
    dropdownOpen,
    dropdownMode,
    filterOptionAvailability,
    merchantSuggestions,
    dateSuggestionResult,
    availableCurrencies,
    activeCurrencyCode,
    activeCurrencyMetadata,
    filteredTransactions,
    focusSearch,
    openFilters,
    dismissDropdown,
    beginEditingToken,
    selectFilterOption,
    removeToken,
    confirmTokenDraft,
    selectMerchantSuggestion,
    selectDateSuggestion,
    selectCurrency,
    applyPickedCategory,
    clearSearch,
    getSnapshot,
    setSnapshot
  };
}
