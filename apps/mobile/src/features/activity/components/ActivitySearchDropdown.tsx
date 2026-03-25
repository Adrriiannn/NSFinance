import { ScrollView, Text, View, useWindowDimensions } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { HEADER_CONSTANTS } from "../../../layout/header/header.constants";
import { getFloatingTabBarContentInset } from "../../../theme/insets";
import { zIndex, palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";
import { ActivitySearchFilterRow } from "./ActivitySearchFilterRow";
import { CurrencySuggestionList } from "./CurrencySuggestionList";
import { DateSuggestionList } from "./DateSuggestionList";
import { MerchantSuggestionList } from "./MerchantSuggestionList";
import type {
  ActivityDateSuggestion,
  ActivityMerchantSuggestion,
  ActivitySearchDropdownMode,
  ActivitySearchFilterOption
} from "../search/activitySearch.types";

type ActivitySearchDropdownProps = {
  visible: boolean;
  mode: ActivitySearchDropdownMode;
  filterOptions: (ActivitySearchFilterOption & { disabled?: boolean })[];
  merchantSuggestions: ActivityMerchantSuggestion[];
  dateSuggestions: ActivityDateSuggestion[];
  currencyOptions: string[];
  selectedCurrency: string;
  onSelectFilter: (tokenType: ActivitySearchFilterOption["tokenType"]) => void;
  onSelectMerchant: (value: string) => void;
  onSelectDateSuggestion: (selection: ActivityDateSuggestion) => void;
  onSelectCurrency: (currencyCode: string) => void;
};

export function ActivitySearchDropdown({
  visible,
  mode,
  filterOptions,
  merchantSuggestions,
  dateSuggestions,
  currencyOptions,
  selectedCurrency,
  onSelectFilter,
  onSelectMerchant,
  onSelectDateSuggestion,
  onSelectCurrency
}: ActivitySearchDropdownProps) {
  const insets = useSafeAreaInsets();
  const { height: windowHeight } = useWindowDimensions();

  if (!visible || mode === "hidden") {
    return null;
  }

  const preferredPanelHeight =
    mode === "filters"
      ? 460
      : mode === "dateSuggestions"
        ? 460
        : mode === "merchantSuggestions"
          ? 420
          : 380;

  const estimatedPanelTop =
    insets.top + HEADER_CONSTANTS.rowHeight + HEADER_CONSTANTS.secondRowHeight + spacing[8];
  const bottomClearance = getFloatingTabBarContentInset(insets.bottom, spacing[8]);
  const availablePanelHeight = Math.max(
    180,
    windowHeight - estimatedPanelTop - bottomClearance
  );
  const maxPanelHeight = Math.min(preferredPanelHeight, availablePanelHeight);

  return (
    <View style={[styles.panel, { maxHeight: maxPanelHeight }]}>
      <ScrollView
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {mode === "filters" ? (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Filters</Text>
            {filterOptions.map((option) => (
              <ActivitySearchFilterRow
                key={option.key}
                option={option}
                onPress={onSelectFilter}
              />
            ))}
          </View>
        ) : null}

        {mode === "merchantSuggestions" ? (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Merchant suggestions</Text>
            <MerchantSuggestionList
              suggestions={merchantSuggestions}
              onSelect={onSelectMerchant}
            />
          </View>
        ) : null}

        {mode === "dateSuggestions" ? (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Date suggestions</Text>
            <DateSuggestionList
              suggestions={dateSuggestions}
              onSelect={onSelectDateSuggestion}
            />
          </View>
        ) : null}

        {mode === "currencySuggestions" ? (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Currencies</Text>
            <CurrencySuggestionList
              currencies={currencyOptions}
              selectedCurrency={selectedCurrency}
              onSelect={onSelectCurrency}
            />
          </View>
        ) : null}
      </ScrollView>
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  panel: {
    position: "absolute",
    top: 44,
    left: 0,
    right: 0,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    zIndex: zIndex.dropdown + 6
  },
  content: {
    padding: spacing[10],
    gap: spacing[8]
  },
  section: {
    gap: spacing[8]
  },
  sectionTitle: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "500",
    textTransform: "uppercase",
    letterSpacing: 0.6
  }
}));

