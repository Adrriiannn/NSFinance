import { Ionicons } from "@expo/vector-icons";
import { Pressable, Text, View } from "react-native";
import { GlassCard } from "../../ui/GlassCard";
import { palette, radius, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";

type DiscoverHeroCardProps = {
  livePlansCount: number;
  onPressPublishPlan: () => void;
  onPressMyPublishedPlans: () => void;
};

export function DiscoverHeroCard({
  livePlansCount,
  onPressPublishPlan,
  onPressMyPublishedPlans
}: DiscoverHeroCardProps) {
  return (
    <GlassCard style={styles.card}>
      <View style={styles.headerRow}>
        <View style={styles.copyBlock}>
          <Text style={styles.title}>Community library</Text>
          <Text numberOfLines={2} style={styles.copy}>
            Explore public plans from the community and save what fits your flow.
          </Text>
        </View>

        <View style={styles.metricWrap}>
          <Text style={styles.metricValue}>{livePlansCount}</Text>
          <Text style={styles.metricLabel}>Live plans</Text>
        </View>
      </View>

      <View style={styles.actionRow}>
        <Pressable style={styles.actionButton} onPress={onPressPublishPlan}>
          <Ionicons name="share-social-outline" size={16} color={palette.textPrimary} />
          <Text style={styles.actionLabel}>Publish a plan</Text>
        </Pressable>

        <Pressable style={styles.actionButton} onPress={onPressMyPublishedPlans}>
          <Ionicons name="briefcase-outline" size={16} color={palette.textPrimary} />
          <Text style={styles.actionLabel}>My published plans</Text>
        </Pressable>
      </View>
    </GlassCard>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  card: {
    gap: spacing[12],
    borderColor: palette.border,
    backgroundColor: surfaces.card
  },
  headerRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  copyBlock: {
    flex: 1,
    gap: spacing[4]
  },
  title: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  copy: {
    color: palette.textSecondary,
    ...typography.caption
  },
  metricWrap: {
    minWidth: 76,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[8],
    paddingVertical: spacing[6],
    gap: 2
  },
  metricValue: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  metricLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  actionRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  actionButton: {
    flex: 1,
    minHeight: 36,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  },
  actionLabel: {
    color: palette.textPrimary,
    ...typography.caption
  }
}));

