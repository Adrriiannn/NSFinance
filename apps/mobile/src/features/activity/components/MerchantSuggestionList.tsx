import { Ionicons } from "@expo/vector-icons";
import { Pressable, Text, View } from "react-native";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";
import type { ActivityMerchantSuggestion } from "../search/activitySearch.types";

type MerchantSuggestionListProps = {
  suggestions: ActivityMerchantSuggestion[];
  onSelect: (value: string) => void;
};

export function MerchantSuggestionList({
  suggestions,
  onSelect
}: MerchantSuggestionListProps) {
  if (suggestions.length === 0) {
    return (
      <View style={styles.emptyWrap}>
        <Text style={styles.emptyTitle}>No merchant suggestions yet</Text>
        <Text style={styles.emptyText}>Keep typing to see fuzzy matches.</Text>
      </View>
    );
  }

  return (
    <View style={styles.list}>
      {suggestions.map((item) => (
        <Pressable
          key={item.normalizedName}
          onPress={() => onSelect(item.displayName)}
          style={({ pressed }) => [styles.row, pressed ? styles.rowPressed : null]}
        >
          <View style={styles.iconWrap}>
            <Ionicons name="storefront-outline" size={16} color={palette.textSecondary} />
          </View>
          <View style={styles.copyWrap}>
            <Text style={styles.title}>{item.displayName}</Text>
            <Text style={styles.hint}>
              <Text style={styles.hintPrefix}>merchant:</Text>{" "}
              <Text style={styles.hintValue}>{item.displayName}</Text>
            </Text>
          </View>
        </Pressable>
      ))}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  list: {
    gap: spacing[8]
  },
  row: {
    minHeight: 48,
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
  rowPressed: {
    opacity: 0.9
  },
  iconWrap: {
    width: 26,
    height: 26,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.fieldStrong
  },
  copyWrap: {
    flex: 1,
    gap: 2
  },
  title: {
    color: palette.textPrimary,
    ...typography.body2
  },
  hint: {
    ...typography.caption
  },
  hintPrefix: {
    color: palette.accent,
    fontWeight: "500"
  },
  hintValue: {
    color: palette.textSecondary
  },
  emptyWrap: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: 2
  },
  emptyTitle: {
    color: palette.textPrimary,
    ...typography.body2
  },
  emptyText: {
    color: palette.textSecondary,
    ...typography.caption
  }
}));

