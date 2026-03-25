import { useRouter } from "expo-router";
import { Pressable, Text, View } from "react-native";
import { PlanningHubScreen } from "../../../src/components/planningHub/PlanningHubScreen";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import { palette, radius, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

export default function ExpensePlanCommunityDashboardScreen() {
  const router = useRouter();
  const { getCreatorDashboard } = useExpensePlanning();
  const dashboard = getCreatorDashboard();

  return (
    <PlanningHubScreen title="My published plans">
      <View style={styles.metricGrid}>
        <GlassCard style={styles.metricCard}>
          <Text style={styles.metricValue}>{dashboard.publishedCount}</Text>
          <Text style={styles.metricLabel}>Published</Text>
        </GlassCard>
        <GlassCard style={styles.metricCard}>
          <Text style={styles.metricValue}>{dashboard.pendingReviewCount}</Text>
          <Text style={styles.metricLabel}>Pending review</Text>
        </GlassCard>
        <GlassCard style={styles.metricCard}>
          <Text style={styles.metricValue}>{dashboard.totalLikes}</Text>
          <Text style={styles.metricLabel}>Likes</Text>
        </GlassCard>
        <GlassCard style={styles.metricCard}>
          <Text style={styles.metricValue}>{dashboard.totalDownloads}</Text>
          <Text style={styles.metricLabel}>Uses</Text>
        </GlassCard>
      </View>

      {dashboard.plans.length === 0 ? (
        <EmptyState title="Nothing published yet" message="Publish a plan from the Plans tab or from a plan detail screen to start building your public library." />
      ) : (
        <View style={styles.planList}>
          {dashboard.plans.map((publication) => (
            <Pressable key={publication.id} style={styles.planCard} onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}>
              <View style={styles.planCardHeader}>
                <View style={styles.planCardCopy}>
                  <Text style={styles.planCardTitle}>{publication.publicTitle}</Text>
                  <Text style={styles.planCardMeta}>{publication.publicationStatus} • {publication.moderationStatus}</Text>
                </View>
                <Text style={styles.planCardDate}>{publication.publishedAtUtc ? new Date(publication.publishedAtUtc).toLocaleDateString("en-GB", { day: "numeric", month: "short" }) : "Not live"}</Text>
              </View>
              <View style={styles.badgeRow}>
                <Text style={styles.badge}>{publication.likeCount} likes</Text>
                <Text style={styles.badge}>{publication.downloadCount} uses</Text>
                <Text style={styles.badge}>{publication.reportCount} reports</Text>
              </View>
            </Pressable>
          ))}
        </View>
      )}
    </PlanningHubScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  metricGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[12]
  },
  metricCard: {
    width: "47%",
    gap: 4
  },
  metricValue: {
    color: palette.textPrimary,
    ...typography.title1
  },
  metricLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  planList: {
    gap: spacing[12]
  },
  planCard: {
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    padding: spacing[16],
    gap: spacing[8]
  },
  planCardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  planCardCopy: {
    flex: 1,
    gap: 4
  },
  planCardTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  planCardMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  planCardDate: {
    color: palette.textSecondary,
    ...typography.caption
  },
  badgeRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  badge: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  }
}));




