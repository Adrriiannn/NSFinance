import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { LayoutAnimation, PanResponder, Platform, Pressable, ScrollView, StyleSheet, Text, UIManager, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ExpenseTrackerCategoryRadialChart } from "../../../../../src/components/expenseTracker/ExpenseTrackerCategoryRadialChart";
import { ExpenseTrackerSegmentedControl } from "../../../../../src/components/expenseTracker/ExpenseTrackerSegmentedControl";
import { EmptyState } from "../../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../../src/components/ui/GlassCard";
import {
  EXPENSE_HUB_CONTENT_PADDING_X,
  EXPENSE_HUB_CONTENT_TOP_GAP,
  getExpenseHubContentBottomInset
} from "../../../../../src/components/expenseTracker/expenseHubLayout";
import { useExpensePlanning } from "../../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { useExpenseTrackerEntriesQuery, useExpenseTrackerTaxonomyQuery } from "../../../../../src/features/expenseTracker/useExpenseTracker";
import {
  buildExpensePlanCategoryMetrics,
  buildExpensePlanComputed,
  buildExpensePlanTaxonomyLookup,
  formatExpensePlanPeriod
} from "../../../../../src/features/expenseTracker/expensePlanningUtils";
import type { ExpenseAnalyticsMode } from "../../../../../src/features/expenseTracker/expensePlanningTypes";
import { getExpenseTrackerVisual } from "../../../../../src/features/expenseTracker/expenseTrackerModels";
import { HeaderDropdownSlot, HeaderShell } from "../../../../../src/layout/appHeader";
import { palette, spacing, typography } from "../../../../../src/theme/tokens";

const analyticsModes = [
  { label: "Actual", value: "actual" },
  { label: "Planned", value: "planned" },
  { label: "Variance", value: "variance" }
] as const;

const analyticsModeOrder: ExpenseAnalyticsMode[] = ["actual", "planned", "variance"];

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

function getAnalyticsModeDescription(mode: ExpenseAnalyticsMode) {
  if (mode === "planned") {
    return "Your planned expenses.";
  }

  if (mode === "variance") {
    return "The gap between planned and actual spending.";
  }

  return "Your actual recorded expenses.";
}

export default function ExpenseTrackerGraphsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const { plans } = useExpensePlanning();
  const [mode, setMode] = useState<ExpenseAnalyticsMode>("actual");
  const [selectedPlanId, setSelectedPlanId] = useState<string | null>(null);
  const [expandedCategory, setExpandedCategory] = useState<string | null>(null);
  const modeRef = useRef<ExpenseAnalyticsMode>("actual");

  useEffect(() => {
    if (Platform.OS === "android" && UIManager.setLayoutAnimationEnabledExperimental) {
      UIManager.setLayoutAnimationEnabledExperimental(true);
    }
  }, []);

  useEffect(() => {
    modeRef.current = mode;
  }, [mode]);

  useEffect(() => {
    if (selectedPlanId) {
      return;
    }

    setSelectedPlanId(plans.find((plan) => plan.status === "active")?.id ?? plans[0]?.id ?? null);
  }, [plans, selectedPlanId]);

  const selectedPlan = plans.find((plan) => plan.id === selectedPlanId) ?? plans[0] ?? null;
  const currency = entriesQuery.data?.[0]?.currency ?? "EUR";
  const taxonomyLookup = useMemo(
    () => buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []),
    [taxonomyQuery.data?.domains]
  );
  const computed = selectedPlan ? buildExpensePlanComputed(selectedPlan, entriesQuery.data ?? [], taxonomyLookup) : null;
  const categoryMetrics = useMemo(
    () => selectedPlan ? buildExpensePlanCategoryMetrics(selectedPlan, entriesQuery.data ?? [], taxonomyLookup, mode) : [],
    [entriesQuery.data, mode, selectedPlan, taxonomyLookup]
  );
  const topLegendItems = categoryMetrics.slice(0, 3);
  const totalLabel = mode === "planned"
    ? computed?.expectedTotal ?? 0
    : mode === "actual"
      ? computed?.actualTotal ?? 0
      : computed?.varianceAmount ?? 0;

  const chartPanResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gestureState) =>
          Math.abs(gestureState.dx) > 16 && Math.abs(gestureState.dx) > Math.abs(gestureState.dy) * 1.3,
        onPanResponderRelease: (_event, gestureState) => {
          if (Math.abs(gestureState.dx) < 42 || Math.abs(gestureState.dx) <= Math.abs(gestureState.dy) * 1.3) {
            return;
          }

          const currentIndex = analyticsModeOrder.indexOf(modeRef.current);
          if (currentIndex < 0) {
            return;
          }

          // Swiping left advances the analytics mode; swiping right goes back.
          if (gestureState.dx < 0 && currentIndex < analyticsModeOrder.length - 1) {
            setMode(analyticsModeOrder[currentIndex + 1]);
            return;
          }

          if (gestureState.dx > 0 && currentIndex > 0) {
            setMode(analyticsModeOrder[currentIndex - 1]);
          }
        }
      }),
    []
  );

  return (
    <View style={styles.screen}>
      <HeaderShell
        preset="primaryTwoRowSelector"
        includeTopInset
        bleedHorizontal={EXPENSE_HUB_CONTENT_PADDING_X}
        title="Analytics"
        secondRow={
          <HeaderDropdownSlot
            title="Current plan"
            value={selectedPlan?.title ?? null}
            placeholder="Select plan"
            containerStyle={styles.planHeaderDropdown}
            options={plans.map((plan) => ({
              label: `${plan.title} • ${formatExpensePlanPeriod(plan.startDate, plan.endDate)}`,
              value: plan.id
            }))}
            onChange={(value) => setSelectedPlanId(value)}
          />
        }
      />
      <ScrollView
        contentContainerStyle={[
          styles.scrollContent,
          {
            paddingTop: EXPENSE_HUB_CONTENT_TOP_GAP,
            paddingBottom: getExpenseHubContentBottomInset(insets.bottom)
          }
        ]}
        showsVerticalScrollIndicator={false}
      >
      {!selectedPlan ? (
        <EmptyState
          title="No plans to analyse yet"
          message="Create a plan first, then this screen will compare actual, planned, and variance views." 
          actionLabel="Create plan"
          onActionPress={() => router.push("/(tabs)/planner/expense-tracker/plan-builder" as never)}
        />
      ) : (
        <>
          <ExpenseTrackerSegmentedControl
            value={mode}
            options={[...analyticsModes]}
            onChange={setMode}
          />

          <View {...chartPanResponder.panHandlers}>
            <GlassCard style={styles.chartCard}>
              <View style={styles.chartCardHeader}>
                <View>
                  <Text style={styles.chartTitle}>{mode === "actual" ? "Actual spendings" : mode === "planned" ? "Planned allocation" : "Variance view"}</Text>
                  <Text style={styles.chartSubtitle}>{getAnalyticsModeDescription(mode)}</Text>
                </View>
              </View>

              <View style={styles.chartBody}>
                <ExpenseTrackerCategoryRadialChart
                  data={categoryMetrics.map((item) => ({
                    domainId: item.domainId,
                    categoryId: item.categoryId,
                    category: item.categoryName,
                    total: item.amount,
                    percentage: item.percentage
                  }))}
                  totalLabel={formatAmount(Math.abs(totalLabel), currency)}
                  centerLabel={mode === "variance" ? "Variance" : mode === "planned" ? "Planned" : "Actual"}
                />
                <View style={styles.chartLegend}>
                  {topLegendItems.map((item) => {
                    const visuals = getExpenseTrackerVisual({ domainId: item.domainId, categoryId: item.categoryId });
                    return (
                      <View key={item.key} style={styles.legendRow}>
                        <View style={styles.legendLabelWrap}>
                          <View style={[styles.legendDot, { backgroundColor: visuals.color }]} />
                          <Text style={styles.legendLabel}>{item.categoryName}</Text>
                        </View>
                        <Text style={styles.legendValue}>{item.percentage.toFixed(1)}%</Text>
                      </View>
                    );
                  })}
                </View>
              </View>
            </GlassCard>
          </View>

          <View style={styles.breakdownWrap}>
            {categoryMetrics.map((item) => {
              const visuals = getExpenseTrackerVisual({ domainId: item.domainId, categoryId: item.categoryId });
              const expanded = expandedCategory === item.key;
              return (
                <GlassCard key={item.key} style={styles.categoryCard}>
                  <Pressable
                    style={styles.categoryCardPressable}
                    onPress={() => {
                      LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
                      setExpandedCategory((current) => current === item.key ? null : item.key);
                    }}
                  >
                    <View style={styles.categoryMainRow}>
                      <View style={[styles.categoryIconWrap, { backgroundColor: `${visuals.color}22` }]}>
                        <Ionicons name={visuals.icon as keyof typeof Ionicons.glyphMap} size={18} color={visuals.color} />
                      </View>

                      <View style={styles.categoryContentColumn}>
                        <Text style={styles.categoryName}>{item.categoryName}</Text>
                        <View style={styles.progressTrackWrap}>
                          <View style={[styles.progressTrack, { backgroundColor: `${visuals.color}22` }]}>
                            <View style={[styles.progressFill, { width: `${Math.max(item.percentage, 4)}%`, backgroundColor: visuals.color }]} />
                          </View>
                        </View>
                      </View>

                      <View style={styles.categoryMetricsColumn}>
                        <Text style={styles.categoryMetricPrimary}>{formatAmount(item.amount, currency)}</Text>
                        <Text style={styles.categoryMetricSecondary}>{item.transactionCount} tx</Text>
                      </View>
                    </View>
                  </Pressable>

                  {expanded ? (
                    <View style={styles.subcategoryPreview}>
                      {item.subcategories.slice(0, 4).map((subcategory) => (
                        <View key={`${subcategory.subcategoryId ?? subcategory.subcategoryName}`} style={styles.subcategoryRow}>
                          <Text style={styles.subcategoryName}>{subcategory.subcategoryName}</Text>
                          <Text style={styles.subcategoryAmount}>{formatAmount(subcategory.amount, currency)}</Text>
                        </View>
                      ))}
                      <Pressable
                        style={styles.drillDownButton}
                        onPress={() =>
                          router.push({
                            pathname: "/(tabs)/planner/expense-tracker/category/[categoryId]",
                            params: {
                              categoryId: String(item.categoryId ?? item.key),
                              planId: selectedPlan.id,
                              mode
                            }
                          })
                        }
                      >
                        <Text style={styles.drillDownLabel}>Open category drill-down</Text>
                      </Pressable>
                    </View>
                  ) : null}
                </GlassCard>
              );
            })}
          </View>
        </>
      )}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1
  },
  scrollContent: {
    gap: spacing[16]
  },
  planHeaderDropdown: {
    width: "100%"
  },
  chartCard: {
    gap: spacing[16]
  },
  chartCardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  chartTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  chartSubtitle: {
    marginTop: 4,
    color: palette.textSecondary,
    ...typography.body2
  },
  chartBody: {
    flexDirection: "row",
    gap: spacing[16],
    alignItems: "center"
  },
  chartLegend: {
    flex: 1,
    gap: 12
  },
  legendRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  legendLabelWrap: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    flex: 1
  },
  legendDot: {
    width: 10,
    height: 10,
    borderRadius: 999
  },
  legendLabel: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  legendValue: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  breakdownWrap: {
    gap: spacing[12]
  },
  categoryCard: {
    gap: spacing[12]
  },
  categoryCardPressable: {
    gap: spacing[12]
  },
  categoryMainRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  categoryIconWrap: {
    width: 44,
    height: 44,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center"
  },
  categoryContentColumn: {
    flex: 1,
    gap: 8,
    justifyContent: "center"
  },
  categoryName: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  progressTrackWrap: {
    width: "72%"
  },
  progressTrack: {
    height: 10,
    borderRadius: 999,
    overflow: "hidden"
  },
  progressFill: {
    height: "100%",
    borderRadius: 999
  },
  categoryMetricsColumn: {
    width: 90,
    alignItems: "flex-end",
    gap: 2
  },
  categoryMetricPrimary: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700",
    textAlign: "right"
  },
  categoryMetricSecondary: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "right"
  },
  subcategoryPreview: {
    gap: spacing[12]
  },
  subcategoryRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  subcategoryName: {
    color: palette.textSecondary,
    ...typography.body2
  },
  subcategoryAmount: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  drillDownButton: {
    minHeight: 40,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    alignItems: "center",
    justifyContent: "center"
  },
  drillDownLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  }
});

