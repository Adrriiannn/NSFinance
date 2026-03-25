import { Ionicons } from "@expo/vector-icons";
import { Animated, Pressable, Text, View } from "react-native";
import { useEntranceAnimation } from "../../hooks/useEntranceAnimation";
import {
  getExpenseTrackerEntryCategoryLabel,
  getExpenseTrackerEntrySubcategoryLabel,
  getExpenseTrackerVisual
} from "../../features/expenseTracker/expenseTrackerModels";
import { palette, radius, spacing, typography, createRuntimeStyleSheet } from "../../theme/tokens";
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
  const visuals = getExpenseTrackerVisual({ domainId: entry.domainId, categoryId: entry.categoryId });
  const animationStyle = useEntranceAnimation();
  const isPlanned = entry.status === "planned";

  return (
    <Animated.View style={animationStyle}>
      <Pressable onPress={onPress} style={({ pressed }) => [styles.card, pressed ? styles.cardPressed : null]}>
        <View style={[styles.leftAccent, { backgroundColor: visuals.color }]} />
        <View style={[styles.iconWrap, { backgroundColor: `${visuals.color}22` }]}> 
          <Ionicons
            name={visuals.icon as keyof typeof Ionicons.glyphMap}
            size={18}
            color={visuals.color}
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
              <Text style={styles.categoryLabel}>{getExpenseTrackerEntrySubcategoryLabel(entry)}</Text>
            </View>
            <Text style={styles.statusLabel}>{isPlanned ? getExpenseTrackerEntryCategoryLabel(entry) : getExpenseTrackerEntryCategoryLabel(entry)}</Text>
          </View>
        </View>
      </Pressable>
    </Animated.View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  card: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(21,21,21,0.82)",
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
    borderRadius: 6,
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
    fontWeight: "600"
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.caption
  },
  amount: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
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
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.16)",
    backgroundColor: "rgba(242,140,40,0.1)",
    paddingHorizontal: spacing[8],
    paddingVertical: 4,
    flexShrink: 1
  },
  categoryLabel: {
    color: palette.textPrimary,
    ...typography.caption
  },
  statusLabel: {
    color: palette.textSecondary,
    ...typography.caption
  }
}));

