import { Ionicons } from "@expo/vector-icons";
import { Pressable, Text, View } from "react-native";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";
import type { ActivityDateSuggestion } from "../search/activitySearch.types";

type DateSuggestionListProps = {
  suggestions: ActivityDateSuggestion[];
  onSelect: (selection: ActivityDateSuggestion) => void;
};

const HINT_PREFIX_PATTERN = /(transaction:|merchant:|category:|currency:|amount:|date:)/gi;

function renderHintWithPrefix(hint: string) {
  const parts = hint.split(HINT_PREFIX_PATTERN);

  return parts.map((part, index) => {
    if (!part) {
      return null;
    }

    const normalized = part.toLowerCase();
    const isPrefix =
      normalized === "transaction:" ||
      normalized === "merchant:" ||
      normalized === "category:" ||
      normalized === "currency:" ||
      normalized === "amount:" ||
      normalized === "date:";

    return (
      <Text key={`${part}-${index}`} style={isPrefix ? styles.hintPrefix : styles.hintValue}>
        {part}
      </Text>
    );
  });
}

export function DateSuggestionList({ suggestions, onSelect }: DateSuggestionListProps) {
  if (suggestions.length === 0) {
    return (
      <View style={styles.emptyWrap}>
        <Text style={styles.emptyTitle}>Type a date expression</Text>
        <Text style={styles.emptyText}>
          Try: yesterday, monday, 12 apr, april 12, 12.04, a week ago
        </Text>
      </View>
    );
  }

  return (
    <View style={styles.list}>
      {suggestions.map((item) => (
        <Pressable
          key={item.id}
          onPress={() => onSelect(item)}
          style={({ pressed }) => [styles.row, pressed ? styles.rowPressed : null]}
        >
          <View style={styles.iconWrap}>
            <Ionicons name="calendar-outline" size={16} color={palette.textSecondary} />
          </View>
          <View style={styles.copyWrap}>
            <Text style={styles.title}>{item.label}</Text>
            <Text style={styles.hint}>{renderHintWithPrefix(item.hintLabel ?? `date: ${item.mode === "weekday" ? "weekday" : "exact date"}`)}</Text>
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

