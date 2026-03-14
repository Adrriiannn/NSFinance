import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { GlassCard } from "../ui/GlassCard";
import { palette, spacing, typography } from "../../theme/tokens";
import type { ExpenseTrackerEntryDto } from "../../types/api";

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

function formatOccurredAt(occurredAtUtc: string) {
  return new Date(occurredAtUtc).toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit"
  });
}

type ExpenseTrackerEntryCardProps = {
  entry: ExpenseTrackerEntryDto;
  onPress: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
  onToggleStatus: () => void;
};

export function ExpenseTrackerEntryCard({
  entry,
  onPress,
  onDuplicate,
  onDelete,
  onToggleStatus
}: ExpenseTrackerEntryCardProps) {
  const isPlanned = entry.status === "planned";

  return (
    <GlassCard style={styles.card}>
      <Pressable onPress={onPress} style={({ pressed }) => [styles.body, pressed ? styles.pressed : null]}>
        <View style={styles.topRow}>
          <View style={styles.titleWrap}>
            <Text style={styles.title} numberOfLines={1}>
              {entry.title}
            </Text>
            <View style={[styles.statusPill, isPlanned ? styles.statusPlanned : styles.statusCompleted]}>
              <Text style={styles.statusLabel}>{isPlanned ? "Planned" : "Completed"}</Text>
            </View>
          </View>
          <Text style={[styles.amount, isPlanned ? styles.amountPlanned : null]}>
            {formatAmount(entry.amount, entry.currency)}
          </Text>
        </View>

        <View style={styles.metaRow}>
          <Text style={styles.metaText}>{entry.category}</Text>
          <Text style={styles.metaDot}>?</Text>
          <Text style={styles.metaText}>{entry.paymentSource}</Text>
          <Text style={styles.metaDot}>?</Text>
          <Text style={styles.metaText}>{formatOccurredAt(entry.occurredAtUtc)}</Text>
        </View>

        {entry.merchant ? (
          <Text style={styles.supportingText} numberOfLines={1}>
            Merchant: {entry.merchant}
          </Text>
        ) : null}

        {entry.notes ? (
          <Text style={styles.supportingText} numberOfLines={2}>
            {entry.notes}
          </Text>
        ) : null}

        {entry.tags.length > 0 ? (
          <View style={styles.tagsRow}>
            {entry.tags.slice(0, 3).map((tag) => (
              <View key={tag} style={styles.tagPill}>
                <Text style={styles.tagLabel}>#{tag}</Text>
              </View>
            ))}
          </View>
        ) : null}
      </Pressable>

      <View style={styles.actionsRow}>
        <Pressable onPress={onToggleStatus} style={({ pressed }) => [styles.actionButton, pressed ? styles.actionPressed : null]}>
          <Ionicons name={isPlanned ? "checkmark-circle-outline" : "time-outline"} size={16} color={palette.textPrimary} />
          <Text style={styles.actionLabel}>{isPlanned ? "Mark completed" : "Mark planned"}</Text>
        </Pressable>
        <Pressable onPress={onDuplicate} style={({ pressed }) => [styles.iconAction, pressed ? styles.actionPressed : null]}>
          <Ionicons name="copy-outline" size={18} color={palette.textSecondary} />
        </Pressable>
        <Pressable onPress={onDelete} style={({ pressed }) => [styles.iconAction, pressed ? styles.deletePressed : null]}>
          <Ionicons name="trash-outline" size={18} color={palette.negative} />
        </Pressable>
      </View>
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    gap: spacing[12]
  },
  body: {
    gap: spacing[8]
  },
  pressed: {
    opacity: 0.95
  },
  topRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  titleWrap: {
    flex: 1,
    gap: spacing[8]
  },
  title: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "700"
  },
  amount: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  amountPlanned: {
    color: "#F6C75F"
  },
  statusPill: {
    alignSelf: "flex-start",
    borderRadius: 999,
    paddingHorizontal: spacing[8],
    paddingVertical: 4,
    borderWidth: 1
  },
  statusCompleted: {
    borderColor: "rgba(104,215,169,0.4)",
    backgroundColor: "rgba(104,215,169,0.12)"
  },
  statusPlanned: {
    borderColor: "rgba(246,199,95,0.4)",
    backgroundColor: "rgba(246,199,95,0.12)"
  },
  statusLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  metaRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "center",
    gap: 6
  },
  metaText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  metaDot: {
    color: palette.textSecondary,
    ...typography.caption
  },
  supportingText: {
    color: palette.textSecondary,
    ...typography.body2
  },
  tagsRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  tagPill: {
    borderRadius: 999,
    backgroundColor: "rgba(91,157,255,0.14)",
    borderWidth: 1,
    borderColor: "rgba(91,157,255,0.18)",
    paddingHorizontal: spacing[8],
    paddingVertical: 4
  },
  tagLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  actionsRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  actionButton: {
    flex: 1,
    minHeight: 36,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8],
    paddingHorizontal: spacing[12]
  },
  actionLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  iconAction: {
    width: 38,
    height: 38,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center"
  },
  actionPressed: {
    opacity: 0.9
  },
  deletePressed: {
    opacity: 0.9,
    backgroundColor: "rgba(244,91,105,0.12)"
  }
});
