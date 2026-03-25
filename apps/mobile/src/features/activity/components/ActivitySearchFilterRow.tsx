import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, spacing, surfaces, typography } from "../../../theme/tokens";
import type { ActivitySearchFilterOption } from "../search/activitySearch.types";

type ActivitySearchFilterRowProps = {
  option: ActivitySearchFilterOption & { disabled?: boolean };
  onPress: (tokenType: ActivitySearchFilterOption["tokenType"]) => void;
};

const HINT_PREFIX_PATTERN = /(transaction:|merchant:|category:|currency:|amount:|date:)/gi;

function renderHintWithBoldPrefixes(hint: string) {
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
      <Text
        key={`${part}-${index}`}
        style={isPrefix ? styles.hintPrefix : styles.hintValue}
      >
        {part}
      </Text>
    );
  });
}

export function ActivitySearchFilterRow({ option, onPress }: ActivitySearchFilterRowProps) {
  return (
    <Pressable
      accessibilityRole="button"
      disabled={option.disabled}
      onPress={() => onPress(option.tokenType)}
      style={({ pressed }) => [
        styles.row,
        option.disabled ? styles.rowDisabled : null,
        pressed && !option.disabled ? styles.rowPressed : null
      ]}
    >
      <View style={styles.iconWrap}>
        <Ionicons name="options-outline" size={16} color={palette.textSecondary} />
      </View>
      <View style={styles.copyWrap}>
        <Text style={styles.title}>{option.title}</Text>
        <Text style={styles.hint}>{renderHintWithBoldPrefixes(option.hint)}</Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    minHeight: 52,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(21,21,21,0.95)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  rowDisabled: {
    opacity: 0.48
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
    color: palette.textSecondary,
    ...typography.caption
  },
  hintPrefix: {
    color: palette.accent,
    fontWeight: "500"
  },
  hintValue: {
    color: palette.textSecondary,
    opacity: 0.82
  }
});
