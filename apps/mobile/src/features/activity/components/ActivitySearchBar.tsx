import { Ionicons } from "@expo/vector-icons";
import { useEffect, useRef } from "react";
import { Pressable, ScrollView, StyleSheet, TextInput, View } from "react-native";
import { HEADER_CONSTANTS } from "../../../layout/header/header.constants";
import { palette, radius, spacing, surfaces, typography } from "../../../theme/tokens";
import { ActivitySearchDropdown } from "./ActivitySearchDropdown";
import { ActivitySearchToken } from "./ActivitySearchToken";
import type {
  ActivityDateSuggestion,
  ActivitySearchDropdownMode,
  ActivitySearchFilterOption,
  ActivitySearchToken as ActivitySearchTokenModel
} from "../search/activitySearch.types";

type ActivitySearchBarProps = {
  tokens: ActivitySearchTokenModel[];
  rawSearchText: string;
  activeTokenId: string | null;
  activeTokenType: ActivitySearchTokenModel["type"] | null;
  activeTokenDraft: string;
  dropdownOpen: boolean;
  dropdownMode: ActivitySearchDropdownMode;
  filterOptions: (ActivitySearchFilterOption & { disabled?: boolean })[];
  merchantSuggestions: { displayName: string; normalizedName: string; score: number }[];
  dateSuggestions: ActivityDateSuggestion[];
  currencies: string[];
  selectedCurrency: string;
  onFocusSearch: () => void;
  onSetRawSearchText: (value: string) => void;
  onSetActiveDraft: (value: string) => void;
  onConfirmActiveDraft: (options?: { reopenDropdown?: boolean }) => void;
  onSelectFilter: (tokenType: ActivitySearchFilterOption["tokenType"]) => void;
  onSelectMerchantSuggestion: (value: string) => void;
  onSelectDateSuggestion: (selection: ActivityDateSuggestion) => void;
  onSelectCurrency: (currencyCode: string) => void;
  onEditToken: (tokenId: string) => void;
  onRemoveToken: (tokenId: string) => void;
  onOpenCategoryPicker: () => void;
  onClearSearch: () => void;
};

function isInlineEditingTokenType(type: ActivitySearchTokenModel["type"] | null) {
  return (
    type === "transaction" ||
    type === "merchant" ||
    type === "date" ||
    type === "amount"
  );
}

export function ActivitySearchBar({
  tokens,
  rawSearchText,
  activeTokenId,
  activeTokenType,
  activeTokenDraft,
  dropdownOpen,
  dropdownMode,
  filterOptions,
  merchantSuggestions,
  dateSuggestions,
  currencies,
  selectedCurrency,
  onFocusSearch,
  onSetRawSearchText,
  onSetActiveDraft,
  onConfirmActiveDraft,
  onSelectFilter,
  onSelectMerchantSuggestion,
  onSelectDateSuggestion,
  onSelectCurrency,
  onEditToken,
  onRemoveToken,
  onOpenCategoryPicker,
  onClearSearch
}: ActivitySearchBarProps) {
  const mainInputRef = useRef<TextInput | null>(null);
  const tokenRailRef = useRef<ScrollView | null>(null);
  const suppressMainInputOpenUntilRef = useRef(0);
  const previousActiveTokenTypeRef = useRef<ActivitySearchTokenModel["type"] | null>(
    activeTokenType
  );
  const hasSearchContent = tokens.length > 0 || rawSearchText.trim().length > 0;
  const hasCurrencyToken = tokens.some((token) => token.type === "currency");
  const hasAmountToken = tokens.some((token) => token.type === "amount");
  const shouldAnchorSingleToken = tokens.length === 1 && rawSearchText.trim().length === 0;
  const shouldAnchorAmountPair =
    activeTokenType === "amount" &&
    hasCurrencyToken &&
    hasAmountToken &&
    rawSearchText.trim().length === 0;
  const shouldAnchorFromStart = shouldAnchorSingleToken || shouldAnchorAmountPair;

  useEffect(() => {
    if (shouldAnchorFromStart) {
      tokenRailRef.current?.scrollTo({ x: 0, y: 0, animated: true });
      return;
    }

    tokenRailRef.current?.scrollToEnd({ animated: true });
  }, [tokens.length, rawSearchText, activeTokenType, shouldAnchorFromStart]);

  useEffect(() => {
    if (!shouldAnchorFromStart) {
      return;
    }

    const pinStart = () => {
      tokenRailRef.current?.scrollTo({ x: 0, y: 0, animated: false });
    };

    pinStart();
    const handle = setTimeout(pinStart, 24);
    return () => {
      clearTimeout(handle);
    };
  }, [activeTokenDraft, activeTokenType, shouldAnchorFromStart]);

  useEffect(() => {
    const previousTokenType = previousActiveTokenTypeRef.current;
    const wasInlineEditing = isInlineEditingTokenType(previousTokenType);
    const isInlineEditing = isInlineEditingTokenType(activeTokenType);
    previousActiveTokenTypeRef.current = activeTokenType;

    if (wasInlineEditing && !isInlineEditing) {
      suppressMainInputOpenUntilRef.current = Date.now() + 450;
      requestAnimationFrame(() => {
        mainInputRef.current?.focus();
      });
    }
  }, [activeTokenType]);

  const handleMainInputFocus = () => {
    if (Date.now() < suppressMainInputOpenUntilRef.current) {
      return;
    }

    onFocusSearch();
  };

  return (
    <View style={styles.wrap}>
      <Pressable style={styles.inputShell} onPress={onFocusSearch}>
        <Ionicons name="search-outline" size={18} color={palette.textSecondary} />

        <ScrollView
          ref={tokenRailRef}
          horizontal
          keyboardShouldPersistTaps="handled"
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={styles.tokenRailContent}
          style={styles.tokenRail}
        >
          {tokens.map((token) => (
            <ActivitySearchToken
              key={token.id}
              token={token}
              isActive={token.id === activeTokenId && isInlineEditingTokenType(activeTokenType)}
              activeDraft={activeTokenDraft}
              onDraftChange={onSetActiveDraft}
              onSubmitDraft={onConfirmActiveDraft}
              onRemove={onRemoveToken}
              onPressToken={onEditToken}
              onOpenCategoryPicker={onOpenCategoryPicker}
            />
          ))}

          <TextInput
            ref={mainInputRef}
            value={rawSearchText}
            onFocus={handleMainInputFocus}
            onChangeText={onSetRawSearchText}
            placeholder={
              tokens.length > 0
                ? "Add free text..."
                : "Search transactions, merchants, or dates"
            }
            placeholderTextColor={palette.textSecondary}
            style={[styles.mainInput, tokens.length > 0 ? styles.mainInputWithTokens : null]}
            editable={!isInlineEditingTokenType(activeTokenType)}
            returnKeyType="search"
            onSubmitEditing={() => onConfirmActiveDraft({ reopenDropdown: false })}
          />
        </ScrollView>

        {hasSearchContent ? (
          <Pressable onPress={onClearSearch} style={styles.clearButton}>
            <Ionicons name="close" size={16} color={palette.textSecondary} />
          </Pressable>
        ) : null}
      </Pressable>

      <ActivitySearchDropdown
        visible={dropdownOpen}
        mode={dropdownMode}
        filterOptions={filterOptions}
        merchantSuggestions={merchantSuggestions}
        dateSuggestions={dateSuggestions}
        currencyOptions={currencies}
        selectedCurrency={selectedCurrency}
        onSelectFilter={onSelectFilter}
        onSelectMerchant={onSelectMerchantSuggestion}
        onSelectDateSuggestion={onSelectDateSuggestion}
        onSelectCurrency={onSelectCurrency}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    width: "100%",
    position: "relative",
    zIndex: 60
  },
  inputShell: {
    minHeight: HEADER_CONSTANTS.searchHeight,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[10],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  tokenRail: {
    flex: 1,
    minWidth: 0
  },
  tokenRailContent: {
    alignItems: "center",
    gap: spacing[6],
    paddingLeft: spacing[8],
    paddingRight: spacing[8]
  },
  mainInput: {
    minWidth: 120,
    color: palette.textPrimary,
    ...typography.caption,
    paddingVertical: 0
  },
  mainInputWithTokens: {
    minWidth: 86
  },
  clearButton: {
    width: 24,
    height: 24,
    alignItems: "center",
    justifyContent: "center"
  }
});
