import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { LayoutAnimation, PanResponder, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { PlanningHubCategoryRadialChart } from "../../../src/components/planningHub/PlanningHubCategoryRadialChart";
import { PlanningHubShell } from "../../../src/components/planningHub/PlanningHubShell";
import { PlanningHubSegmentedControl } from "../../../src/components/planningHub/PlanningHubSegmentedControl";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import {
  PLANNING_HUB_CONTENT_PADDING_X,
  PLANNING_HUB_CONTENT_TOP_GAP,
  getPlanningHubContentBottomInset
} from "../../../src/components/planningHub/planningHubLayout";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import { useExpenseTrackerEntriesQuery, useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import {
  buildExpensePlanCategoryMetrics,
  buildExpensePlanComputed,
  buildExpensePlanTaxonomyLookup,
  formatExpensePlanPeriod
} from "../../../src/features/expenseTracker/expensePlanningUtils";
import type { ExpenseAnalyticsMode } from "../../../src/features/expenseTracker/expensePlanningTypes";
import { getExpenseTrackerVisual } from "../../../src/features/expenseTracker/expenseTrackerModels";
import { HeaderDropdownSlot, HeaderShell } from "../../../src/layout/appHeader";
import { palette, spacing, typography } from "../../../src/theme/tokens";
import type { ExpenseTaxonomyDomainDto, ExpenseTrackerEntryDto } from "../../../src/types/api";

const analyticsModes = [
  { label: "Actual", value: "actual" },
  { label: "Planned", value: "planned" },
  { label: "Variance", value: "variance" },
  { label: "Savings", value: "savings" }
] as const;

type PlanningAnalyticsMode = ExpenseAnalyticsMode | "savings";
type SavingsMonthMetric = {
  key: string;
  label: string;
  amount: number;
  percentage: number;
  transactionCount: number;
  color: string;
  sortKey: number;
};

const analyticsModeOrder: PlanningAnalyticsMode[] = ["actual", "planned", "variance", "savings"];
const SAVINGS_DOMAIN_MATCH = ["saving", "invest"];
const SAVINGS_MONTH_COLORS = [
  "#58D2E6",
  "#4EA8FF",
  "#39C6A8",
  "#7DD3FC",
  "#60A5FA",
  "#2DD4BF",
  "#38BDF8",
  "#5EEAD4",
  "#34D399",
  "#22D3EE",
  "#7DD3FC",
  "#67E8F9"
];
const monthLabelFormatter = new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric" });

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

function normalizeToken(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
}

function isSavingsDomainLabel(domainName?: string | null) {
  const normalized = normalizeToken(domainName);
  return SAVINGS_DOMAIN_MATCH.every((fragment) => normalized.includes(fragment));
}

function buildSavingsMonthMetrics(
  entries: ExpenseTrackerEntryDto[],
  domains: ExpenseTaxonomyDomainDto[]
): SavingsMonthMetric[] {
  const savingsDomainIds = new Set(
    domains
      .filter((domain) => isSavingsDomainLabel(domain.name))
      .map((domain) => domain.id)
  );

  const byMonth = new Map<string, { amount: number; transactionCount: number; sortKey: number }>();

  entries.forEach((entry) => {
    if (entry.status !== "completed") {
      return;
    }

    const isSavingsDomain =
      (entry.domainId !== null && savingsDomainIds.has(entry.domainId)) ||
      (entry.domainId === null && isSavingsDomainLabel(entry.domainName));

    if (!isSavingsDomain) {
      return;
    }

    const occurredAt = new Date(entry.occurredAtUtc);
    if (Number.isNaN(occurredAt.getTime())) {
      return;
    }

    const year = occurredAt.getUTCFullYear();
    const month = occurredAt.getUTCMonth();
    const monthStart = Date.UTC(year, month, 1);
    const key = `${year}-${String(month + 1).padStart(2, "0")}`;
    const existing = byMonth.get(key) ?? {
      amount: 0,
      transactionCount: 0,
      sortKey: monthStart
    };
    existing.amount += Math.abs(entry.amount);
    existing.transactionCount += 1;
    byMonth.set(key, existing);
  });

  const totalSavings = Array.from(byMonth.values()).reduce((sum, item) => sum + item.amount, 0);
  const denominator = totalSavings > 0 ? totalSavings : 1;

  return Array.from(byMonth.entries())
    .sort((left, right) => right[1].sortKey - left[1].sortKey)
    .map(([key, item], index) => ({
      key,
      label: monthLabelFormatter.format(new Date(item.sortKey)),
      amount: Number(item.amount.toFixed(2)),
      percentage: Number(((item.amount / denominator) * 100).toFixed(1)),
      transactionCount: item.transactionCount,
      color: SAVINGS_MONTH_COLORS[index % SAVINGS_MONTH_COLORS.length],
      sortKey: item.sortKey
    }));
}

function getAnalyticsModeDescription(mode: PlanningAnalyticsMode) {
  if (mode === "planned") {
    return "Your planned expenses.";
  }

  if (mode === "variance") {
    return "The gap between planned and actual spending.";
  }

  if (mode === "savings") {
    return "Monthly savings built from your Savings & Investments transactions.";
  }

  return "Your actual recorded expenses.";
}

export default function PlanningHubAnalyticsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const { plans } = useExpensePlanning();
  const [mode, setMode] = useState<PlanningAnalyticsMode>("actual");
  const [selectedPlanId, setSelectedPlanId] = useState<string | null>(null);
  const [expandedCategory, setExpandedCategory] = useState<string | null>(null);
  const modeRef = useRef<PlanningAnalyticsMode>("actual");

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
  const isSavingsMode = mode === "savings";
  const currency = entriesQuery.data?.[0]?.currency ?? "EUR";
  const taxonomyLookup = useMemo(
    () => buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []),
    [taxonomyQuery.data?.domains]
  );
  const computed = selectedPlan && !isSavingsMode
    ? buildExpensePlanComputed(selectedPlan, entriesQuery.data ?? [], taxonomyLookup)
    : null;
  const categoryMetrics = useMemo(
    () =>
      selectedPlan && !isSavingsMode
        ? buildExpensePlanCategoryMetrics(selectedPlan, entriesQuery.data ?? [], taxonomyLookup, mode)
        : [],
    [entriesQuery.data, isSavingsMode, mode, selectedPlan, taxonomyLookup]
  );
  const savingsMonthMetrics = useMemo(
    () => buildSavingsMonthMetrics(entriesQuery.data ?? [], taxonomyQuery.data?.domains ?? []),
    [entriesQuery.data, taxonomyQuery.data?.domains]
  );
  const savingsTopMonths = useMemo(
    () => [...savingsMonthMetrics].sort((left, right) => right.amount - left.amount).slice(0, 3),
    [savingsMonthMetrics]
  );
  const savingsTotal = useMemo(
    () => savingsMonthMetrics.reduce((sum, month) => sum + month.amount, 0),
    [savingsMonthMetrics]
  );
  const topLegendItems = categoryMetrics.slice(0, 3);
  const totalLabel = isSavingsMode
    ? savingsTotal
    : mode === "planned"
      ? computed?.expectedTotal ?? 0
      : mode === "actual"
        ? computed?.actualTotal ?? 0
        : computed?.varianceAmount ?? 0;
  const showPlanEmptyState = !selectedPlan && !isSavingsMode;

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
    <PlanningHubShell>
      <View style={styles.screen}>
      <HeaderShell
        preset="primaryTwoRowSelector"
        includeTopInset
        bleedHorizontal={PLANNING_HUB_CONTENT_PADDING_X}
        title="Analytics"
        secondRow={
          <HeaderDropdownSlot
            title="Current plan"
            value={isSavingsMode ? "Savings overview" : selectedPlan?.title ?? null}
            placeholder="Select plan"
            containerStyle={styles.planHeaderDropdown}
            options={plans.map((plan) => ({
              label: `${plan.title} • ${formatExpensePlanPeriod(plan.startDate, plan.endDate)}`,
              value: plan.id
            }))}
            onChange={(value) => setSelectedPlanId(value)}
            disabled={isSavingsMode}
          />
        }
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
      <PlanningHubSegmentedControl
        value={mode}
        options={[...analyticsModes]}
        onChange={setMode}
      />

      {showPlanEmptyState ? (
        <EmptyState
          title="No plans to analyse yet"
          message="Create a plan first, then this screen will compare actual, planned, and variance views."
          actionLabel="Create plan"
          onActionPress={() => router.push("/(tabs)/planning/builder" as never)}
        />
      ) : (
        <>
          <View {...chartPanResponder.panHandlers}>
            <GlassCard style={styles.chartCard}>
              <View style={styles.chartCardHeader}>
                <View>
                  <Text style={styles.chartTitle}>
                    {isSavingsMode
                      ? "Savings distribution"
                      : mode === "actual"
                        ? "Actual spendings"
                        : mode === "planned"
                          ? "Planned allocation"
                          : "Variance view"}
                  </Text>
                  <Text style={styles.chartSubtitle}>{getAnalyticsModeDescription(mode)}</Text>
                </View>
              </View>

              <View style={styles.chartBody}>
                <PlanningHubCategoryRadialChart
                  data={
                    isSavingsMode
                      ? savingsMonthMetrics.map((month) => ({
                          domainId: null,
                          categoryId: null,
                          category: month.key,
                          total: month.amount,
                          percentage: month.percentage,
                          color: month.color
                        }))
                      : categoryMetrics.map((item) => ({
                          domainId: item.domainId,
                          categoryId: item.categoryId,
                          category: item.categoryName,
                          total: item.amount,
                          percentage: item.percentage
                        }))
                  }
                  totalLabel={formatAmount(Math.abs(totalLabel), currency)}
                  centerLabel={isSavingsMode ? "Saved" : mode === "variance" ? "Variance" : mode === "planned" ? "Planned" : "Actual"}
                />
                <View style={styles.chartLegend}>
                  {isSavingsMode ? (
                    savingsTopMonths.map((month, index) => (
                      <View key={month.key} style={styles.topMonthChip}>
                        <View style={[styles.legendDot, { backgroundColor: month.color }]} />
                        <View style={styles.topMonthChipContent}>
                          <Text style={styles.topMonthChipTitle}>{`${index + 1}. ${month.label}`}</Text>
                          <Text style={styles.topMonthChipAmount}>{formatAmount(month.amount, currency)}</Text>
                        </View>
                      </View>
                    ))
                  ) : (
                    topLegendItems.map((item) => {
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
                    })
                  )}
                </View>
              </View>
            </GlassCard>
          </View>

          {isSavingsMode ? (
            savingsMonthMetrics.length ? (
              <View style={styles.breakdownWrap}>
                {savingsMonthMetrics.map((month) => (
                  <GlassCard key={month.key} style={styles.monthCard}>
                    <View style={styles.monthRow}>
                      <View style={styles.monthLabelWrap}>
                        <View style={[styles.legendDot, { backgroundColor: month.color }]} />
                        <Text style={styles.monthLabel}>{month.label}</Text>
                      </View>
                      <View style={styles.monthMetricsColumn}>
                        <Text style={styles.monthAmount}>{formatAmount(month.amount, currency)}</Text>
                        <Text style={styles.monthMeta}>
                          {month.percentage.toFixed(1)}% • {month.transactionCount} tx
                        </Text>
                      </View>
                    </View>
                    <View style={styles.monthProgressTrack}>
                      <View
                        style={[
                          styles.monthProgressFill,
                          {
                            width: `${Math.max(month.percentage, 4)}%`,
                            backgroundColor: month.color
                          }
                        ]}
                      />
                    </View>
                  </GlassCard>
                ))}
              </View>
            ) : (
              <EmptyState
                title="No savings tracked yet"
                message="Tag transactions under Savings & Investments to populate this savings breakdown."
              />
            )
          ) : (
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
                          <Text style={styles.categoryMetricSecondary}>{item.percentage.toFixed(1)}%</Text>
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
                        <Text style={styles.drillDownLabel}>Transactions shown inline in this section.</Text>
                      </View>
                    ) : null}
                  </GlassCard>
                );
              })}
            </View>
          )}
        </>
      )}
      </ScrollView>
      </View>
    </PlanningHubShell>
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
  topMonthChip: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    paddingHorizontal: spacing[10],
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  topMonthChipContent: {
    flex: 1
  },
  topMonthChipTitle: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  topMonthChipAmount: {
    color: palette.textSecondary,
    ...typography.caption
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
  monthCard: {
    gap: spacing[10]
  },
  monthRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  monthLabelWrap: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8],
    flex: 1
  },
  monthLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  monthMetricsColumn: {
    alignItems: "flex-end",
    gap: 2
  },
  monthAmount: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  monthMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  monthProgressTrack: {
    height: 10,
    borderRadius: 999,
    backgroundColor: "rgba(226,236,255,0.12)",
    overflow: "hidden"
  },
  monthProgressFill: {
    height: "100%",
    borderRadius: 999
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



