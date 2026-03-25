import { Pressable, Text, View } from "react-native";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";
import {
  getActivityCurrencyMetadata
} from "../search/activitySearch.currencies";

type CurrencySuggestionListProps = {
  currencies: string[];
  selectedCurrency: string;
  onSelect: (currencyCode: string) => void;
};

export function CurrencySuggestionList({
  currencies,
  selectedCurrency,
  onSelect
}: CurrencySuggestionListProps) {
  return (
    <View style={styles.list}>
      {currencies.map((code) => {
        const metadata = getActivityCurrencyMetadata(code);
        const selected = code.toUpperCase() === selectedCurrency.toUpperCase();
        return (
          <Pressable
            key={code}
            onPress={() => onSelect(code)}
            style={({ pressed }) => [
              styles.row,
              selected ? styles.rowSelected : null,
              pressed ? styles.rowPressed : null
            ]}
          >
            <Text style={styles.currencyCode}>{code.toUpperCase()}</Text>
            <Text style={styles.currencyMeta}>
              {metadata.placement === "prefix"
                ? `${metadata.symbol} amount`
                : `amount ${metadata.symbol}`}
            </Text>
            {selected ? <Text style={styles.selectedLabel}>Selected</Text> : null}
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  list: {
    gap: spacing[8]
  },
  row: {
    minHeight: 44,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  rowSelected: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.14)"
  },
  rowPressed: {
    opacity: 0.9
  },
  currencyCode: {
    color: palette.textPrimary,
    ...typography.body2
  },
  currencyMeta: {
    flex: 1,
    color: palette.textSecondary,
    ...typography.caption
  },
  selectedLabel: {
    color: palette.accent,
    ...typography.caption,
    fontWeight: "500"
  }
}));

