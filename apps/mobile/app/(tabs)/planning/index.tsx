import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Easing, Pressable, ScrollView, Text, View, useWindowDimensions } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { PlanningHubShell } from "../../../src/components/planningHub/PlanningHubShell";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import {
  PLANNING_HUB_CONTENT_PADDING_X,
  PLANNING_HUB_CONTENT_TOP_GAP,
  getPlanningHubContentBottomInset
} from "../../../src/components/planningHub/planningHubLayout";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import { useExpenseTrackerTaxonomyQuery, useExpenseTrackerEntriesQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  buildExpensePlanCategoryMetrics,
  buildExpensePlanComputed,
  buildExpensePlanTaxonomyLookup,
  filterExpensePlans,
  formatExpensePlanPeriod,
  getExpensePlanStatusMeta
} from "../../../src/features/expenseTracker/expensePlanningUtils";
import type { ExpensePlanStatus } from "../../../src/features/expenseTracker/expensePlanningTypes";
import { layout, palette, radius, spacing, typography, createRuntimeStyleSheet, useThemeTokens } from "../../../src/theme/tokens";

const quickActionConfig = [
  { key: "new", label: "New plan", icon: "add-circle-outline" },
  { key: "discover", label: "Discover", icon: "compass-outline" },
  { key: "published", label: "My published plans", icon: "briefcase-outline" }
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

export default function PlanningHubOverviewScreen() {
  useThemeTokens();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const { width: windowWidth } = useWindowDimensions();
  const { plans, createNewPlanDraft } = useExpensePlanning();
  const [listMode, setListMode] = useState<PlanListMode>("recent");
  const [heroRailWidth, setHeroRailWidth] = useState<number | null>(null);
  const quickActionsHint = useRef(new Animated.Value(0)).current;
  const entries = useMemo(() => entriesQuery.data ?? [], [entriesQuery.data]);

  const currency = entries[0]?.currency ?? "EUR";
  const taxonomyLookup = useMemo(
    () => buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []),
    [taxonomyQuery.data?.domains]
  );
  const activePlans = useMemo(() => plans.filter((plan) => plan.status === "active"), [plans]);
  const listPlans = useMemo(() => filterExpensePlans(plans, listMode), [plans, listMode]);
  const planComputedById = useMemo(() => {
    const computedById = new Map<string, ReturnType<typeof buildExpensePlanComputed>>();
    plans.forEach((plan) => {
      computedById.set(plan.id, buildExpensePlanComputed(plan, entries, taxonomyLookup));
    });
    return computedById;
  }, [entries, plans, taxonomyLookup]);
  const plannedCategoryCountByPlanId = useMemo(() => {
    const countByPlanId = new Map<string, number>();
    listPlans.forEach((plan) => {
      countByPlanId.set(
        plan.id,
        buildExpensePlanCategoryMetrics(plan, entries, taxonomyLookup, "planned").length
      );
    });
    return countByPlanId;
  }, [entries, listPlans, taxonomyLookup]);
  const heroCardWidth = Math.max(heroRailWidth ?? (windowWidth - PLANNING_HUB_CONTENT_PADDING_X * 2), 280);
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
    if (quickActionConfig.length <= 4) {
      quickActionsHint.setValue(0);
      return;
    }

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
      router.push("/(tabs)/planning/builder" as never);
      return;
    }

    if (key === "discover") {
      router.push("/(tabs)/planning/browse" as never);
      return;
    }

    if (key === "published") {
      router.push("/(tabs)/planning/my-published" as never);
    }
  };

  return (
    <PlanningHubShell>
      <View style={styles.screen}>
      <HeaderShell
        preset="primaryDefault"
        title="Your plans"
        includeTopInset
        bleedHorizontal={PLANNING_HUB_CONTENT_PADDING_X}
      />
      
      <ScrollView
        contentContainerStyle={[
          styles.scrollContent,
          {
            paddingTop: PLANNING_HUB_CONTENT_TOP_GAP,
            paddingBottom: getPlanningHubContentBottomInset(insets.bottom)
          }
        ]}
        showsVerticalScrollIndicator={false}
      >
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
          <View
            style={styles.heroRailViewport}
            onLayout={(event) => {
              const nextWidth = event.nativeEvent.layout.width;
              if (nextWidth > 0 && nextWidth !== heroRailWidth) {
                setHeroRailWidth(nextWidth);
              }
            }}
          >
            <ScrollView
              horizontal
              pagingEnabled
              snapToAlignment="start"
              snapToInterval={heroCardWidth}
              disableIntervalMomentum
              decelerationRate="fast"
              bounces={false}
              overScrollMode="never"
              showsHorizontalScrollIndicator={false}
              contentContainerStyle={styles.heroRail}
            >
              {activePlans.map((plan) => {
                const computed = planComputedById.get(plan.id);
                if (!computed) {
                  return null;
                }
                const meta = getExpensePlanStatusMeta(plan.status);
                return (
                  <Pressable
                    key={plan.id}
                    style={[styles.heroCardPressable, { width: heroCardWidth }]}
                    onPress={() => router.push(`/(tabs)/planning/${plan.id}` as never)}
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
          </View>
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
              router.push("/(tabs)/planning/builder" as never);
            }}
          />
        ) : (
          <View style={styles.planList}>
            {listPlans.map((plan) => {
              const computed = planComputedById.get(plan.id);
              if (!computed) {
                return null;
              }
              const meta = getExpensePlanStatusMeta(plan.status);
              const categoryCount = plannedCategoryCountByPlanId.get(plan.id) ?? 0;
              return (
                <Pressable key={plan.id} onPress={() => router.push(`/(tabs)/planning/${plan.id}` as never)}>
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
      </ScrollView>
      </View>
    </PlanningHubShell>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  screen: {
    flex: 1
  },
  scrollContent: {
    gap: layout.sectionGap
  },
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
    fontWeight: "600",
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
    backgroundColor: "rgba(242,140,40,0.24)",
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.34)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  },
  heroActionLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  heroRail: {
    gap: 0,
    paddingRight: 0
  },
  heroRailViewport: {
    width: "100%",
    overflow: "hidden"
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
    borderRadius: 6,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center"
  },
  statusPillLabel: {
    ...typography.caption,
    fontWeight: "600"
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
    fontWeight: "600"
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
    fontWeight: "600"
  },
  progressTrack: {
    height: 10,
    borderRadius: 6,
    backgroundColor: "rgba(226,236,255,0.08)",
    overflow: "hidden"
  },
  progressFill: {
    height: "100%",
    backgroundColor: palette.primary,
    borderRadius: 6
  },
  paceSignal: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  quickActionRail: {
    gap: 0,
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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(242,140,40,0.2)",
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.26)"
  },
  quickActionLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600",
    textAlign: "center"
  },
  quickActionHint: {
    position: "absolute",
    right: 0,
    top: 12,
    width: 30,
    height: 30,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(17,17,17,0.82)",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.1)"
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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center"
  },
  statusCountText: {
    color: "#041120",
    ...typography.caption,
    fontWeight: "600"
  },
  statusIconWrap: {
    width: 48,
    height: 48,
    borderRadius: 6,
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
    fontWeight: "600",
    textAlign: "center"
  },
  statusCardLabelSelected: {
    color: palette.textPrimary
  },
  clearFilterButton: {
    minHeight: 34,
    paddingHorizontal: spacing[12],
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(21,21,21,0.72)",
    borderWidth: 1,
    borderColor: palette.border
  },
  clearFilterLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
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
    fontWeight: "600"
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
    fontWeight: "600"
  }
}));



