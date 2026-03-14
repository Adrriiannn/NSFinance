import { Ionicons } from "@expo/vector-icons";
import { Animated, Pressable, StyleSheet, Text, View } from "react-native";
import { useEntranceAnimation } from "../../hooks/useEntranceAnimation";
import { expenseTrackerCategoryOptions } from "../../features/expenseTracker/expenseTrackerModels";
import { palette, radius, spacing, typography } from "../../theme/tokens";
import type { ExpenseTrackerEntryDto } from "../../types/api";

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

function formatWhen(occurredAtUtc: string) {
  return new Date(occurredAtUtc).toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit"
  });
}

type ExpenseTrackerJournalItemProps = {
  entry: ExpenseTrackerEntryDto;
  onPress: () => void;
};

export function ExpenseTrackerJournalItem({ entry, onPress }: ExpenseTrackerJournalItemProps) {
  const categoryOption = expenseTrackerCategoryOptions.find((option) => option.value === entry.category);
  const animationStyle = useEntranceAnimation();
  const isPlanned = entry.status === "planned";

  return (
    <Animated.View style={animationStyle}>
      <Pressable onPress={onPress} style={({ pressed }) => [styles.card, pressed ? styles.cardPressed : null]}>
        <View style={[styles.leftAccent, { backgroundColor: categoryOption?.color ?? palette.primaryGlow }]} />
        <View style={[styles.iconWrap, { backgroundColor: `${categoryOption?.color ?? palette.primaryGlow}22` }]}> 
          <Ionicons
            name={(categoryOption?.icon ?? "ellipse-outline") as keyof typeof Ionicons.glyphMap}
            size={18}
            color={categoryOption?.color ?? palette.primaryGlow}
          />
        </View>
        <View style={styles.contentWrap}>
          <View style={styles.topRow}>
            <View style={styles.titleWrap}>
              <Text style={styles.title} numberOfLines={1}>{entry.title}</Text>
              <Text style={styles.subtitle} numberOfLines={1}>
                {entry.merchant ?? entry.paymentSource} | {formatWhen(entry.occurredAtUtc)}
              </Text>
            </View>
            <Text style={[styles.amount, isPlanned ? styles.amountPlanned : null]}>
              {formatAmount(entry.amount, entry.currency)}
            </Text>
          </View>
          <View style={styles.bottomRow}>
            <View style={styles.categoryPill}>
              <Text style={styles.categoryLabel}>{entry.category}</Text>
            </View>
            <Text style={styles.statusLabel}>{isPlanned ? "Planned" : "Completed"}</Text>
          </View>
        </View>
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.82)",
    paddingVertical: 14,
    paddingHorizontal: spacing[16],
    overflow: "hidden"
  },
  cardPressed: {
    opacity: 0.96,
    transform: [{ scale: 0.995 }]
  },
  leftAccent: {
    position: "absolute",
    left: 0,
    top: 0,
    bottom: 0,
    width: 4
  },
  iconWrap: {
    width: 42,
    height: 42,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center"
  },
  contentWrap: {
    flex: 1,
    gap: spacing[8]
  },
  topRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  titleWrap: {
    flex: 1,
    gap: 2
  },
  title: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.caption
  },
  amount: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  amountPlanned: {
    color: "#F6D27D"
  },
  bottomRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  categoryPill: {
    borderRadius: 999,
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.16)",
    backgroundColor: "rgba(127,174,255,0.1)",
    paddingHorizontal: spacing[8],
    paddingVertical: 4
  },
  categoryLabel: {
    color: palette.textPrimary,
    ...typography.caption
  },
  statusLabel: {
    color: palette.textSecondary,
    ...typography.caption
  }
});
