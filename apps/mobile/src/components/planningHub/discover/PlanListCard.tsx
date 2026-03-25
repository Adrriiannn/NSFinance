import { Ionicons } from "@expo/vector-icons";
import { Pressable, Text, View } from "react-native";
import type { ExpensePlanPublication } from "../../../features/expenseTracker/expensePlanningTypes";
import { palette, radius, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";

type PlanListCardVariant = "compact" | "full";

type PlanListCardProps = {
  publication: ExpensePlanPublication;
  onPress: () => void;
  variant?: PlanListCardVariant;
  showDate?: boolean;
};

function formatDate(value: string | null) {
  if (!value) {
    return "Draft";
  }

  return new Date(value).toLocaleDateString("en-GB", { day: "numeric", month: "short" });
}

export function PlanListCard({
  publication,
  onPress,
  variant = "compact",
  showDate = false
}: PlanListCardProps) {
  const isFull = variant === "full";
  const topTags = publication.tags.slice(0, 2);

  return (
    <Pressable style={styles.card} onPress={onPress}>
      <View style={styles.headerRow}>
        <View style={styles.copy}>
          <Text style={styles.title} numberOfLines={1}>{publication.publicTitle}</Text>
          <Text style={styles.creator} numberOfLines={1}>{publication.creatorName} {publication.creatorTag}</Text>
        </View>
        <Ionicons name="chevron-forward" size={18} color={palette.accent} />
      </View>

      {isFull ? (
        <Text style={styles.description} numberOfLines={2}>{publication.publicDescription}</Text>
      ) : null}

      <View style={styles.metricRow}>
        <Text style={styles.metric}>{publication.likeCount} likes</Text>
        <Text style={styles.metric}>{publication.downloadCount} uses</Text>
        {isFull ? <Text style={styles.metric}>{publication.planType}</Text> : null}
        {isFull && publication.isTemplate ? <Text style={styles.metric}>Template</Text> : null}
        {isFull && topTags.length > 0 ? <Text style={styles.metric}>{topTags.join(" | ")}</Text> : null}
        {showDate ? <Text style={styles.metric}>{formatDate(publication.publishedAtUtc)}</Text> : null}
      </View>
    </Pressable>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  card: {
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[14],
    paddingVertical: spacing[12],
    gap: spacing[8]
  },
  headerRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[8],
    alignItems: "center"
  },
  copy: {
    flex: 1,
    gap: 2
  },
  title: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  creator: {
    color: palette.textSecondary,
    ...typography.caption
  },
  description: {
    color: palette.textSecondary,
    ...typography.body2
  },
  metricRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  metric: {
    color: palette.textSecondary,
    ...typography.caption
  }
}));

