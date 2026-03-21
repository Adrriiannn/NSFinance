import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../../theme/tokens";
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

const styles = StyleSheet.create({
  list: {
    gap: spacing[8]
  },
  row: {
    minHeight: 44,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(17,35,58,0.96)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  rowSelected: {
    borderColor: "rgba(127,174,255,0.58)",
    backgroundColor: "rgba(36,58,89,0.94)"
  },
  rowPressed: {
    opacity: 0.9
  },
  currencyCode: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  currencyMeta: {
    flex: 1,
    color: palette.textSecondary,
    ...typography.caption
  },
  selectedLabel: {
    color: palette.primaryGlow,
    ...typography.caption,
    fontWeight: "700"
  }
});

