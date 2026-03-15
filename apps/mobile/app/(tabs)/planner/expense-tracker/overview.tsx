import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Easing, Pressable, ScrollView, StyleSheet, Text, View, useWindowDimensions } from "react-native";
import { ExpenseTrackerMiniAppScreen } from "../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { EmptyState } from "../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { useExpensePlanning } from "../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { useExpenseTrackerTaxonomyQuery, useExpenseTrackerEntriesQuery } from "../../../../src/features/expenseTracker/useExpenseTracker";
import {
  buildExpensePlanCategoryMetrics,
  buildExpensePlanComputed,
  buildExpensePlanTaxonomyLookup,
  filterExpensePlans,
  formatExpensePlanPeriod,
  getExpensePlanStatusMeta
} from "../../../../src/features/expenseTracker/expensePlanningUtils";
import type { ExpensePlanStatus } from "../../../../src/features/expenseTracker/expensePlanningTypes";
import { layout, palette, radius, spacing, typography } from "../../../../src/theme/tokens";

const quickActionConfig = [
  { key: "new", label: "New plan", icon: "add-circle-outline" },
  { key: "reuse", label: "Reuse plan", icon: "copy-outline" },
  { key: "completed", label: "Completed plans", icon: "archive-outline" },
  { key: "categories", label: "Categories", icon: "grid-outline" },
  { key: "templates", label: "Templates", icon: "layers-outline" },
  { key: "recurring", label: "Recurring plans", icon: "refresh-outline" },
  { key: "share", label: "Share plan", icon: "share-social-outline" }
] as const;

const statusActionConfig: Record<ExpensePlanStatus, { label: string; icon: keyof typeof Ionicons.glyphMap }> = {
  active: { label: "Active", icon: "play-circle-outline" },
  drafted: { label: "Drafted", icon: "create-outline" },
  scheduled: { label: "Scheduled", icon: "time-outline" },
  completed: { label: "Completed", icon: "checkmark-circle-outline" }
};

const statusOrder: ExpensePlanStatus[] = ["active", "drafted", "scheduled", "completed"];

type PlanListMode = ExpensePlanStatus | "recent";

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency,
    maximumFractionDigits: 2
  }).format(amount);
}

function formatPaceLabel(value: ReturnType<typeof buildExpensePlanComputed>["paceLabel"]) {
  if (value === "ahead") {
    return "Ahead of pace";
  }
  if (value === "over_pace") {
    return "Over pace";
  }
  return "On track";
}

export default function ExpenseTrackerOverviewScreen() {
  const router = useRouter();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const { width: windowWidth } = useWindowDimensions();
  const { plans, createNewPlanDraft, startDuplicatePlan, updateBuilderDraft } = useExpensePlanning();
  const [listMode, setListMode] = useState<PlanListMode>("recent");
  const quickActionsHint = useRef(new Animated.Value(0)).current;

  const currency = entriesQuery.data?.[0]?.currency ?? "EUR";
  const taxonomyLookup = useMemo(
    () => buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []),
    [taxonomyQuery.data?.domains]
  );
  const activePlans = useMemo(() => plans.filter((plan) => plan.status === "active"), [plans]);
  const listPlans = useMemo(() => filterExpensePlans(plans, listMode), [plans, listMode]);
  const heroCardWidth = Math.max(windowWidth - layout.screenHorizontalPadding * 2, 280);
  const statusCounts = useMemo(
    () => ({
      active: plans.filter((plan) => plan.status === "active").length,
      drafted: plans.filter((plan) => plan.status === "drafted").length,
      scheduled: plans.filter((plan) => plan.status === "scheduled").length,
      completed: plans.filter((plan) => plan.status === "completed").length
    }),
    [plans]
  );

  useEffect(() => {
    const animation = Animated.loop(
      Animated.sequence([
        Animated.delay(900),
        Animated.timing(quickActionsHint, {
          toValue: 1,
          duration: 650,
          easing: Easing.out(Easing.quad),
          useNativeDriver: true
        }),
        Animated.timing(quickActionsHint, {
          toValue: 0,
          duration: 650,
          easing: Easing.inOut(Easing.quad),
          useNativeDriver: true
        }),
        Animated.delay(1400)
      ])
    );

    animation.start();

    return () => {
      animation.stop();
    };
  }, [quickActionsHint]);

  const handleQuickAction = async (key: typeof quickActionConfig[number]["key"]) => {
    if (key === "new") {
      createNewPlanDraft();
      router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
      return;
    }

    if (key === "reuse") {
      const candidate = plans.find((plan) => plan.status === "completed") ?? plans[0];
      if (!candidate) {
        return;
      }
      startDuplicatePlan(candidate.id);
      router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
      return;
    }

    if (key === "completed") {
      setListMode("completed");
      return;
    }

    if (key === "categories") {
      router.push("/(tabs)/planner/expense-tracker/add" as never);
      return;
    }

    if (key === "templates") {
      const template = plans.find((plan) => plan.isTemplate) ?? plans.find((plan) => plan.status === "completed") ?? plans[0];
      if (!template) {
        return;
      }
      startDuplicatePlan(template.id);
      router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
      return;
    }

    if (key === "recurring") {
      createNewPlanDraft();
      updateBuilderDraft({ isRecurring: true, recurrenceRule: "Monthly" });
      router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
      return;
    }

    if (key === "share") {
      const shareTarget = activePlans[0] ?? plans[0];
      if (shareTarget) {
        router.push({
          pathname: '/(tabs)/planner/expense-tracker/community/publish',
          params: { planId: shareTarget.id }
        } as never);
      }
    }
  };

  return (
    <ExpenseTrackerMiniAppScreen title="Plans">
      {entriesQuery.isError ? (
        <ErrorState
          title="Could not load plans"
          message={entriesQuery.error.message}
          onRetry={() => {
            void entriesQuery.refetch();
          }}
        />
      ) : null}

      {taxonomyQuery.isError ? (
        <ErrorState
          title="Could not load categories"
          message={taxonomyQuery.error.message}
          onRetry={() => {
            void taxonomyQuery.refetch();
          }}
        />
      ) : null}

      <View style={styles.sectionWrap}>
        <Text style={styles.sectionEyebrow}>Live plans</Text>

        {activePlans.length === 0 ? (
          <GlassCard style={styles.emptyHeroCard}>
            <Text style={styles.emptyHeroTitle}>No active plans yet</Text>
            <Text style={styles.emptyHeroBody}>Start a plan to track expected spend against your live transactions.</Text>
            <Pressable style={styles.heroActionButton} onPress={() => handleQuickAction("new")}>
              <Ionicons name="add-circle-outline" size={18} color={palette.textPrimary} />
              <Text style={styles.heroActionLabel}>Create your first plan</Text>
            </Pressable>
          </GlassCard>
        ) : (
          <ScrollView
            horizontal
            pagingEnabled
            snapToAlignment="start"
            disableIntervalMomentum
            decelerationRate="fast"
            bounces={false}
            overScrollMode="never"
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.heroRail}
          >
            {activePlans.map((plan) => {
              const computed = buildExpensePlanComputed(plan, entriesQuery.data ?? [], taxonomyLookup);
              const meta = getExpensePlanStatusMeta(plan.status);
              return (
                <Pressable
                  key={plan.id}
                  style={[styles.heroCardPressable, { width: heroCardWidth }]}
                  onPress={() => router.push(`/(tabs)/planner/expense-tracker/plan/${plan.id}` as never)}
                >
                  <GlassCard style={styles.heroCard}>
                    <View style={styles.heroHeaderRow}>
                      <View style={styles.heroTextColumn}>
                        <Text style={styles.heroPlanTitle}>{plan.title}</Text>
                        <Text style={styles.heroPeriod}>{formatExpensePlanPeriod(plan.startDate, plan.endDate)}</Text>
                      </View>
                      <View style={[styles.statusPill, { backgroundColor: meta.tint, borderColor: `${meta.color}55` }]}>
                        <Text style={[styles.statusPillLabel, { color: meta.color }]}>{meta.label}</Text>
                      </View>
                    </View>

                    <View style={styles.heroMetricsGrid}>
                      <View style={styles.metricTile}>
                        <Text style={styles.metricLabel}>Expected</Text>
                        <Text style={styles.metricValue}>{formatAmount(computed.expectedTotal, currency)}</Text>
                      </View>
                      <View style={styles.metricTile}>
                        <Text style={styles.metricLabel}>Actual</Text>
                        <Text style={styles.metricValue}>{formatAmount(computed.actualTotal, currency)}</Text>
                      </View>
                      <View style={styles.metricTile}>
                        <Text style={styles.metricLabel}>Remaining</Text>
                        <Text style={[styles.metricValue, computed.remainingAmount < 0 ? styles.metricNegative : null]}>
                          {formatAmount(Math.abs(computed.remainingAmount), currency)}
                        </Text>
                      </View>
                      <View style={styles.metricTile}>
                        <Text style={styles.metricLabel}>Variance</Text>
                        <Text style={[styles.metricValue, computed.varianceAmount > 0 ? styles.metricNegative : styles.metricPositive]}>
                          {computed.varianceAmount >= 0 ? "+" : "-"}{formatAmount(Math.abs(computed.varianceAmount), currency)}
                        </Text>
                      </View>
                    </View>

                    <View style={styles.progressBlock}>
                      <View style={styles.progressHeaderRow}>
                        <Text style={styles.progressLabel}>Spend progress</Text>
                        <Text style={styles.progressLabel}>{Math.round(computed.progressRatio * 100)}%</Text>
                      </View>
                      <View style={styles.progressTrack}>
                        <View style={[styles.progressFill, { width: `${Math.min(computed.progressRatio * 100, 100)}%` }]} />
                      </View>
                      <Text style={styles.paceSignal}>{formatPaceLabel(computed.paceLabel)}</Text>
                    </View>
                  </GlassCard>
                </Pressable>
              );
            })}
          </ScrollView>
        )}
      </View>

      <View style={styles.sectionWrap}>
        <View style={styles.quickActionRailWrap}>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.quickActionRail}>
            {quickActionConfig.map((action) => (
              <Pressable key={action.key} style={styles.quickActionCard} onPress={() => { void handleQuickAction(action.key); }}>
                <View style={styles.quickActionIconWrap}>
                  <Ionicons name={action.icon as keyof typeof Ionicons.glyphMap} size={18} color={palette.textPrimary} />
                </View>
                <Text style={styles.quickActionLabel}>{action.label}</Text>
              </Pressable>
            ))}
          </ScrollView>

          {quickActionConfig.length > 4 ? (
            <Animated.View
              pointerEvents="none"
              style={[
                styles.quickActionHint,
                {
                  opacity: quickActionsHint.interpolate({
                    inputRange: [0, 1],
                    outputRange: [0.46, 0.94]
                  }),
                  transform: [
                    {
                      translateX: quickActionsHint.interpolate({
                        inputRange: [0, 1],
                        outputRange: [0, 7]
                      })
                    }
                  ]
                }
              ]}
            >
              <Ionicons name="arrow-forward" size={16} color={palette.textPrimary} />
            </Animated.View>
          ) : null}
        </View>
      </View>

      <View style={styles.sectionWrap}>
        <Text style={styles.sectionEyebrow}>Community</Text>
        <View style={styles.communityGrid}>
          <Pressable style={styles.communityCard} onPress={() => router.push("/(tabs)/planner/expense-tracker/community" as never)}>
            <View style={[styles.communityIconWrap, styles.communityIconBrowse]}>
              <Ionicons name="storefront-outline" size={24} color={palette.textPrimary} />
            </View>
            <Text style={styles.communityCardTitle}>Browse plans</Text>
          </Pressable>
          <Pressable style={styles.communityCard} onPress={() => router.push("/(tabs)/planner/expense-tracker/community/dashboard" as never)}>
            <View style={[styles.communityIconWrap, styles.communityIconMine]}>
              <Ionicons name="briefcase-outline" size={24} color={palette.textPrimary} />
            </View>
            <Text style={styles.communityCardTitle}>My plans</Text>
          </Pressable>
        </View>
      </View>

      <View style={styles.sectionWrap}>
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionEyebrow}>
            {listMode === "recent" ? "Recent plans" : `${getExpensePlanStatusMeta(listMode).label} plans`}
          </Text>
          {listMode !== "recent" ? (
            <Pressable style={styles.clearFilterButton} onPress={() => setListMode("recent")}>
              <Text style={styles.clearFilterLabel}>Show recent</Text>
            </Pressable>
          ) : null}
        </View>

        <View style={styles.statusGrid}>
          {statusOrder.map((status) => {
            const meta = getExpensePlanStatusMeta(status);
            const isSelected = listMode === status;
            const statusConfig = statusActionConfig[status];
            return (
              <Pressable
                key={status}
                style={styles.statusCard}
                onPress={() => setListMode(status)}
              >
                <View style={styles.statusBadgeWrap}>
                  <View style={[styles.statusCountBadge, { backgroundColor: meta.color }]}>
                    <Text style={styles.statusCountText}>{statusCounts[status]}</Text>
                  </View>
                </View>
                <View
                  style={[
                    styles.statusIconWrap,
                    isSelected ? styles.statusIconWrapSelected : null,
                    {
                      backgroundColor: isSelected ? meta.tint : `${meta.color}18`,
                      borderColor: isSelected ? `${meta.color}66` : `${meta.color}2E`
                    }
                  ]}
                >
                  <Ionicons name={statusConfig.icon} size={20} color={meta.color} />
                </View>
                <Text style={[styles.statusCardLabel, isSelected ? styles.statusCardLabelSelected : null]}>
                  {statusConfig.label}
                </Text>
              </Pressable>
            );
          })}
        </View>

        {listPlans.length === 0 ? (
          <EmptyState
            title="No plans in this view"
            message="Try another status filter or create a new plan from the quick actions above."
            actionLabel="New plan"
            onActionPress={() => {
              createNewPlanDraft();
              router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
            }}
          />
        ) : (
          <View style={styles.planList}>
            {listPlans.map((plan) => {
              const computed = buildExpensePlanComputed(plan, entriesQuery.data ?? [], taxonomyLookup);
              const meta = getExpensePlanStatusMeta(plan.status);
              const categoryCount = buildExpensePlanCategoryMetrics(plan, entriesQuery.data ?? [], taxonomyLookup, "planned").length;
              return (
                <Pressable key={plan.id} onPress={() => router.push(`/(tabs)/planner/expense-tracker/plan/${plan.id}` as never)}>
                  <GlassCard style={styles.planCard}>
                    <View style={styles.planCardTopRow}>
                      <View style={styles.planCardTitleWrap}>
                        <Text style={styles.planCardTitle}>{plan.title}</Text>
                        <Text style={styles.planCardMeta}>{formatExpensePlanPeriod(plan.startDate, plan.endDate)} • {plan.creatorTag}</Text>
                      </View>
                      <View style={[styles.statusPill, { backgroundColor: meta.tint, borderColor: `${meta.color}55` }]}>
                        <Text style={[styles.statusPillLabel, { color: meta.color }]}>{meta.label}</Text>
                      </View>
                    </View>

                    <View style={styles.planCardSummaryRow}>
                      <Text style={styles.planCardSummaryText}>Expected {formatAmount(computed.expectedTotal, currency)}</Text>
                      <Text style={styles.planCardSummaryText}>Actual {formatAmount(computed.actualTotal, currency)}</Text>
                    </View>

                    <View style={styles.planCardIndicatorRow}>
                      {plan.isRecurring ? <Text style={styles.planIndicator}>Recurring</Text> : null}
                      {plan.isTemplate ? <Text style={styles.planIndicator}>Template</Text> : null}
                      {plan.isShared ? <Text style={styles.planIndicator}>Shared</Text> : null}
                      <Text style={styles.planIndicator}>{categoryCount} categories</Text>
                    </View>
                  </GlassCard>
                </Pressable>
              );
            })}
          </View>
        )}
      </View>
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
  sectionWrap: {
    gap: spacing[12]
  },
  sectionHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  sectionEyebrow: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "800",
    letterSpacing: 1.1,
    textTransform: "uppercase"
  },
  emptyHeroCard: {
    gap: spacing[16]
  },
  emptyHeroTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  emptyHeroBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  heroActionButton: {
    minHeight: 48,
    borderRadius: radius.medium,
    backgroundColor: "rgba(47,107,255,0.24)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.34)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  },
  heroActionLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  heroRail: {
    gap: 0,
    paddingRight: 0
  },
  heroCardPressable: {
    width: "100%"
  },
  heroCard: {
    gap: spacing[16]
  },
  heroHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  heroTextColumn: {
    flex: 1,
    gap: 4
  },
  heroPlanTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  heroPeriod: {
    color: palette.textSecondary,
    ...typography.body2
  },
  statusPill: {
    minHeight: 30,
    paddingHorizontal: spacing[12],
    borderRadius: 999,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center"
  },
  statusPillLabel: {
    ...typography.caption,
    fontWeight: "700"
  },
  heroMetricsGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[12]
  },
  metricTile: {
    width: "47%",
    gap: 4
  },
  metricLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  metricValue: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  metricPositive: {
    color: palette.success
  },
  metricNegative: {
    color: palette.negative
  },
  progressBlock: {
    gap: spacing[8]
  },
  progressHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  progressLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  progressTrack: {
    height: 10,
    borderRadius: 999,
    backgroundColor: "rgba(226,236,255,0.08)",
    overflow: "hidden"
  },
  progressFill: {
    height: "100%",
    backgroundColor: palette.primary,
    borderRadius: 999
  },
  paceSignal: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  quickActionRail: {
    gap: spacing[8],
    paddingRight: spacing[8]
  },
  quickActionRailWrap: {
    position: "relative"
  },
  quickActionCard: {
    width: 104,
    gap: spacing[8],
    alignItems: "center"
  },
  quickActionIconWrap: {
    width: 48,
    height: 48,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(47,107,255,0.2)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.26)"
  },
  quickActionLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700",
    textAlign: "center"
  },
  quickActionHint: {
    position: "absolute",
    right: 0,
    top: 12,
    width: 30,
    height: 30,
    borderRadius: 15,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(12,25,43,0.82)",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.1)"
  },
  communityGrid: {
    flexDirection: "row",
    gap: spacing[12]
  },
  communityCard: {
    flex: 1,
    minHeight: 108,
    paddingVertical: spacing[8],
    paddingHorizontal: spacing[12],
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[12]
  },
  communityIconWrap: {
    width: 56,
    height: 56,
    borderRadius: 18,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1
  },
  communityIconBrowse: {
    backgroundColor: "rgba(47,107,255,0.18)",
    borderColor: "rgba(127,174,255,0.28)"
  },
  communityIconMine: {
    backgroundColor: "rgba(67,188,155,0.16)",
    borderColor: "rgba(120,220,191,0.28)"
  },
  communityCardTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700",
    textAlign: "center"
  },
  statusGrid: {
    flexDirection: "row",
    gap: spacing[8],
    justifyContent: "space-between"
  },
  statusCard: {
    width: "23%",
    minHeight: 94,
    paddingTop: spacing[8],
    paddingHorizontal: spacing[4],
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8],
    position: "relative"
  },
  statusBadgeWrap: {
    position: "absolute",
    top: 0,
    right: 2,
    zIndex: 1
  },
  statusCountBadge: {
    minWidth: 24,
    height: 24,
    paddingHorizontal: 6,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center"
  },
  statusCountText: {
    color: "#041120",
    ...typography.caption,
    fontWeight: "800"
  },
  statusIconWrap: {
    width: 48,
    height: 48,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1
  },
  statusIconWrapSelected: {
    transform: [{ scale: 1.02 }]
  },
  statusCardLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700",
    textAlign: "center"
  },
  statusCardLabelSelected: {
    color: palette.textPrimary
  },
  clearFilterButton: {
    minHeight: 34,
    paddingHorizontal: spacing[12],
    borderRadius: 999,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(18,36,58,0.72)",
    borderWidth: 1,
    borderColor: palette.border
  },
  clearFilterLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  planList: {
    gap: spacing[12]
  },
  planCard: {
    gap: spacing[12]
  },
  planCardTopRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  planCardTitleWrap: {
    flex: 1,
    gap: 4
  },
  planCardTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  planCardMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  planCardSummaryRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  planCardSummaryText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  planCardIndicatorRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  planIndicator: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  }
});
