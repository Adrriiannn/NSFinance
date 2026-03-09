import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import {
  Animated,
  Modal,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View
} from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { SpendTrendGraph } from "../../../src/components/planner/SpendTrendGraph";
import { AnimatedCurrencyText } from "../../../src/components/ui/AnimatedCurrencyText";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { useDashboardSummaryQuery } from "../../../src/features/dashboard/useDashboardSummaryQuery";
import {
  buildPlannerSuggestions
} from "../../../src/features/planner/plannerInsights";
import {
  hasSeenCompanionTooltip,
  markCompanionTooltipSeen
} from "../../../src/features/planner/chatHistory";
import {
  getEssentialTransactions,
  getNecessitiesSummary
} from "../../../src/features/planner/necessityMetrics";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { useEntranceAnimation } from "../../../src/hooks/useEntranceAnimation";
import { formatCurrency, formatMonthYear } from "../../../src/lib/format";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

type MonthRange = {
  startMonth: number;
  startYear: number;
  endMonth: number;
  endYear: number;
};

type RangeTarget = "primary" | "secondary";
type RangeStep = "start" | "end";

const monthNames = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec"
];

const defaultRanges = () => {
  const now = new Date();
  const thisMonth = now.getMonth();
  const thisYear = now.getFullYear();
  const prev = new Date(thisYear, thisMonth - 1, 1);

  return {
    primary: {
      startMonth: thisMonth,
      startYear: thisYear,
      endMonth: thisMonth,
      endYear: thisYear
    } as MonthRange,
    secondary: {
      startMonth: prev.getMonth(),
      startYear: prev.getFullYear(),
      endMonth: prev.getMonth(),
      endYear: prev.getFullYear()
    } as MonthRange
  };
};

function monthRangeToDates(range: MonthRange) {
  const start = new Date(range.startYear, range.startMonth, 1, 0, 0, 0, 0);
  const end = new Date(range.endYear, range.endMonth + 1, 0, 23, 59, 59, 999);
  return { start, end };
}

function monthYearOptions() {
  const current = new Date().getFullYear();
  return Array.from({ length: 8 }, (_, index) => current - 5 + index);
}

function computeRangeSpend(transactions: { bookedAtUtc: string; amount: number }[], range: MonthRange) {
  const { start, end } = monthRangeToDates(range);

  return Math.abs(
    transactions
      .filter((item) => {
        const bookedAt = new Date(item.bookedAtUtc);
        return bookedAt >= start && bookedAt <= end && item.amount < 0;
      })
      .reduce((sum, item) => sum + item.amount, 0)
  );
}

function buildDailySpendSeries(transactions: { bookedAtUtc: string; amount: number }[], range: MonthRange) {
  const { start, end } = monthRangeToDates(range);
  const dayMs = 1000 * 60 * 60 * 24;
  const totalDays = Math.max(
    1,
    Math.floor((end.getTime() - start.getTime()) / dayMs) + 1
  );
  const dailyTotals = Array.from({ length: totalDays }, () => 0);

  transactions.forEach((transaction) => {
    if (transaction.amount >= 0) {
      return;
    }

    const bookedAt = new Date(transaction.bookedAtUtc);
    if (bookedAt < start || bookedAt > end) {
      return;
    }

    const dayIndex = Math.floor((bookedAt.getTime() - start.getTime()) / dayMs);
    dailyTotals[dayIndex] = (dailyTotals[dayIndex] ?? 0) + Math.abs(transaction.amount);
  });

  const cumulative: number[] = [];
  let running = 0;
  dailyTotals.forEach((value) => {
    running += value;
    cumulative.push(Number(running.toFixed(2)));
  });

  return cumulative;
}

function rangeLabel(range: MonthRange) {
  const start = new Date(range.startYear, range.startMonth, 1);
  const end = new Date(range.endYear, range.endMonth, 1);
  const startLabel = formatMonthYear(start);
  const endLabel = formatMonthYear(end);
  return startLabel === endLabel ? startLabel : `${startLabel} - ${endLabel}`;
}

export default function PlannerScreen() {
  const router = useRouter();
  const dashboardQuery = useDashboardSummaryQuery();
  const transactionsQuery = useTransactionsQuery();
  const plannerStore = usePlannerStore();
  const heroAnimation = useEntranceAnimation(30);
  const sectionAnimation = useEntranceAnimation(120);
  const [showCompanionTip, setShowCompanionTip] = useState(false);
  const [comparisonVisible, setComparisonVisible] = useState(false);
  const [ranges, setRanges] = useState(defaultRanges());
  const [pickerTarget, setPickerTarget] = useState<RangeTarget>("primary");
  const [pickerStep, setPickerStep] = useState<RangeStep>("start");
  const [pickerVisible, setPickerVisible] = useState(false);

  useEffect(() => {
    let mounted = true;
    let timer: ReturnType<typeof setTimeout> | null = null;

    const load = async () => {
      const seen = await hasSeenCompanionTooltip();
      if (!mounted || seen) {
        return;
      }

      setShowCompanionTip(true);
      timer = setTimeout(() => {
        setShowCompanionTip(false);
        void markCompanionTooltipSeen();
      }, 5000);
    };

    void load();

    return () => {
      mounted = false;
      if (timer) {
        clearTimeout(timer);
      }
    };
  }, []);

  const openCompanion = async () => {
    setShowCompanionTip(false);
    await markCompanionTooltipSeen();
    router.push("/(tabs)/planner/companion");
  };

  const isLoading =
    (dashboardQuery.isLoading && !dashboardQuery.data) ||
    (transactionsQuery.isLoading && !transactionsQuery.data) ||
    !plannerStore.isReady;
  const refreshing =
    (dashboardQuery.isRefetching || transactionsQuery.isRefetching) && !isLoading;
  const error = dashboardQuery.error ?? transactionsQuery.error;
  const transactions = useMemo(() => transactionsQuery.data ?? [], [transactionsQuery.data]);
  const suggestions = buildPlannerSuggestions({
    dashboard: dashboardQuery.data,
    transactions,
    necessities: plannerStore.necessities,
    annotations: plannerStore.annotations
  });

  const essentialTransactions = useMemo(
    () => getEssentialTransactions(transactions, plannerStore.annotations),
    [plannerStore.annotations, transactions]
  );
  const necessitiesSummary = useMemo(
    () =>
      getNecessitiesSummary({
        necessities: plannerStore.necessities,
        essentialTransactions,
        annotations: plannerStore.annotations
      }),
    [essentialTransactions, plannerStore.annotations, plannerStore.necessities]
  );

  const categoryPreview = useMemo(() => {
    const totals = new Map<string, number>();

    transactions.forEach((transaction) => {
      const annotation = plannerStore.annotations[transaction.id];
      if (!annotation?.category || transaction.amount >= 0) {
        return;
      }

      totals.set(
        annotation.category,
        (totals.get(annotation.category) ?? 0) + Math.abs(transaction.amount)
      );
    });

    return plannerStore.categoryCatalog.Expense
      .map((category) => ({
        category,
        spend: totals.get(category) ?? 0
      }))
      .sort((a, b) => b.spend - a.spend)
      .slice(0, 3);
  }, [plannerStore.annotations, plannerStore.categoryCatalog.Expense, transactions]);

  const unassignedCount = useMemo(
    () => transactions.filter((tx) => !plannerStore.annotations[tx.id]?.category).length,
    [plannerStore.annotations, transactions]
  );

  const years = useMemo(() => monthYearOptions(), []);
  const primarySeries = useMemo(
    () => buildDailySpendSeries(transactions, ranges.primary),
    [ranges.primary, transactions]
  );
  const secondarySeries = useMemo(
    () => buildDailySpendSeries(transactions, ranges.secondary),
    [ranges.secondary, transactions]
  );

  const comparisonTotals = useMemo(() => {
    const primary = computeRangeSpend(transactions, ranges.primary);
    const secondary = computeRangeSpend(transactions, ranges.secondary);
    const delta = primary - secondary;
    return { primary, secondary, delta };
  }, [ranges.primary, ranges.secondary, transactions]);

  const activeRange = ranges[pickerTarget];
  const selectedYear = pickerStep === "start" ? activeRange.startYear : activeRange.endYear;

  const openPicker = (target: RangeTarget) => {
    setPickerTarget(target);
    setPickerStep("start");
    setPickerVisible(true);
  };

  const handleYearSelect = (year: number) => {
    setRanges((current) => {
      const next = { ...current };
      next[pickerTarget] =
        pickerStep === "start"
          ? { ...next[pickerTarget], startYear: year }
          : { ...next[pickerTarget], endYear: year };
      return next;
    });
  };

  const handleMonthSelect = (month: number) => {
    setRanges((current) => {
      const next = { ...current };
      next[pickerTarget] =
        pickerStep === "start"
          ? { ...next[pickerTarget], startMonth: month }
          : { ...next[pickerTarget], endMonth: month };
      return next;
    });

    if (pickerStep === "start") {
      setPickerStep("end");
      return;
    }

    setPickerVisible(false);
  };

  const comparisonSentence =
    Math.abs(comparisonTotals.delta) < 0.01
      ? "Both selected periods are level."
      : comparisonTotals.delta > 0
        ? `Primary period is ${formatCurrency(comparisonTotals.delta, "EUR")} higher than comparison.`
        : `Primary period is ${formatCurrency(Math.abs(comparisonTotals.delta), "EUR")} lower than comparison.`;

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
    >
      <View style={styles.topActionsBar}>
        <View style={styles.headerActionsRow}>
          <View style={styles.headerIconWrap}>
            <Pressable style={styles.sparkleButton} onPress={() => void openCompanion()}>
              <Ionicons name="sparkles-outline" size={20} color={palette.accent} />
            </Pressable>
            {showCompanionTip ? (
              <View style={styles.tooltip}>
                <Text style={styles.tooltipText}>Open NS Companion here</Text>
                <Pressable
                  onPress={() => {
                    setShowCompanionTip(false);
                    void markCompanionTooltipSeen();
                  }}
                >
                  <Ionicons name="close" size={12} color={palette.textSecondary} />
                </Pressable>
              </View>
            ) : null}
          </View>
          <View style={styles.headerRightSpacer} />
        </View>
      </View>

      <ScrollView
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
        bounces={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              void Promise.all([dashboardQuery.refetch(), transactionsQuery.refetch()]);
            }}
            tintColor={palette.textSecondary}
          />
        }
      >
        {isLoading ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={styles.loadingHero} />
          <SkeletonBlock style={styles.loadingCard} />
          <SkeletonBlock style={styles.loadingCard} />
          <SkeletonBlock style={styles.loadingCard} />
        </View>
      ) : error ? (
        <ErrorState
          title="Could not load planner"
          message={error.message}
          onRetry={() => {
            void Promise.all([dashboardQuery.refetch(), transactionsQuery.refetch()]);
          }}
        />
      ) : (
        <>
          <Animated.View style={heroAnimation}>
            <Pressable style={({ pressed }) => [styles.graphCard, pressed ? styles.cardPressed : null]} onPress={() => setComparisonVisible(true)}>
              <Text style={styles.graphLabel}>This month vs last month</Text>
              <SpendTrendGraph primarySeries={primarySeries} secondarySeries={secondarySeries} />
              <View style={styles.graphValuesRow}>
                <View style={styles.graphValueBlock}>
                  <AnimatedCurrencyText
                    value={-comparisonTotals.primary}
                    currency="EUR"
                    style={styles.graphValue}
                    baseColor={palette.textPrimary}
                  />
                  <Text style={styles.graphValueLabel}>This month</Text>
                </View>
                <View style={styles.graphValueBlock}>
                  <AnimatedCurrencyText
                    value={-comparisonTotals.secondary}
                    currency="EUR"
                    style={styles.graphValue}
                    baseColor={palette.textPrimary}
                  />
                  <Text style={styles.graphValueLabel}>Last month</Text>
                </View>
              </View>
              <View style={styles.legendRow}>
                <View style={styles.legendItem}>
                  <View style={[styles.legendDot, { backgroundColor: palette.success }]} />
                  <Text style={styles.legendText}>This month</Text>
                </View>
                <View style={styles.legendItem}>
                  <View style={[styles.legendDot, { backgroundColor: palette.negative }]} />
                  <Text style={styles.legendText}>Last month</Text>
                </View>
              </View>
              <Text style={styles.graphMeta}>{comparisonSentence}</Text>
            </Pressable>
          </Animated.View>

          <Animated.View style={sectionAnimation}>
            <View style={styles.pairedRow}>
              <Pressable
                style={({ pressed }) => [styles.sectionCardPressable, pressed ? styles.cardPressed : null]}
                onPress={() => router.push("/(tabs)/planner/necessities")}
              >
                <Text style={styles.sectionTitle}>Necessities</Text>
                <AnimatedCurrencyText
                  value={necessitiesSummary.total}
                  currency="EUR"
                  style={styles.sectionValue}
                  baseColor={palette.textPrimary}
                />
                <Text style={styles.sectionMeta}>Monthly baseline</Text>
                <Text style={styles.sectionHint}>
                  {necessitiesSummary.essentialsCount} essential | {necessitiesSummary.optionalCount} optional
                </Text>
              </Pressable>

              <Pressable
                style={({ pressed }) => [styles.sectionCardPressable, pressed ? styles.cardPressed : null]}
                onPress={() => router.push("/(tabs)/planner/categories")}
              >
                <Text style={styles.sectionTitle}>Category health</Text>
                {categoryPreview.some((item) => item.spend > 0) ? (
                  <View style={styles.categoryPreviewWrap}>
                    {categoryPreview.map((item) => (
                      <View key={item.category} style={styles.categoryRow}>
                        <Text style={styles.categoryLabel}>{item.category}</Text>
                        <AnimatedCurrencyText
                          value={-item.spend}
                          currency="EUR"
                          style={styles.categoryValue}
                          baseColor={palette.textSecondary}
                        />
                      </View>
                    ))}
                  </View>
                ) : (
                  <Text style={styles.sectionMeta}>Add transaction context to unlock category tracking.</Text>
                )}
                {unassignedCount > 0 ? (
                  <Pressable
                    style={({ pressed }) => [styles.unassignedItem, pressed ? styles.unassignedPressed : null]}
                    onPress={() =>
                      router.push({
                        pathname: "/(tabs)/planner/categories",
                        params: { filter: "unassigned" }
                      })
                    }
                  >
                    <Text style={styles.unassignedText}>{unassignedCount} unassigned transactions to review</Text>
                    <Ionicons name="chevron-forward" size={14} color={palette.textSecondary} />
                  </Pressable>
                ) : null}
              </Pressable>
            </View>
          </Animated.View>

          <Text style={styles.suggestionsTitle}>Suggestions</Text>
          <View style={styles.suggestionsWrap}>
            {suggestions.slice(0, 2).map((suggestion) => {
              const routesToUnassigned = suggestion.id === "unclassified";
              return (
                <Pressable
                  key={suggestion.id}
                  style={({ pressed }) => [styles.sectionCardPressable, pressed ? styles.cardPressed : null]}
                  onPress={() => {
                    if (!routesToUnassigned) {
                      return;
                    }

                    router.push({
                      pathname: "/(tabs)/planner/categories",
                      params: { filter: "unassigned" }
                    });
                  }}
                >
                  <Text style={styles.suggestionTitle}>{suggestion.title}</Text>
                  <Text style={styles.suggestionMessage}>{suggestion.message}</Text>
                </Pressable>
              );
            })}
            {suggestions.length === 0 ? (
              <GlassCard style={styles.sectionCard}>
                <Text style={styles.suggestionMessage}>
                  Suggestions will appear when more planning context is available.
                </Text>
              </GlassCard>
            ) : null}
          </View>
        </>
      )}
      </ScrollView>

      <Modal
        visible={comparisonVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setComparisonVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setComparisonVisible(false)}>
          <Pressable style={styles.modalSheet} onPress={() => undefined}>
            <View style={styles.modalHeader}>
              <Text style={styles.modalTitle}>Spend comparison</Text>
              <Pressable onPress={() => setComparisonVisible(false)}>
                <Ionicons name="close" size={18} color={palette.textSecondary} />
              </Pressable>
            </View>

            <SpendTrendGraph primarySeries={primarySeries} secondarySeries={secondarySeries} height={196} />

            <View style={styles.graphValuesRow}>
              <View style={styles.graphValueBlock}>
                <AnimatedCurrencyText
                  value={-comparisonTotals.primary}
                  currency="EUR"
                  style={styles.graphValue}
                  baseColor={palette.textPrimary}
                />
                <Text style={styles.graphValueLabel}>Primary range</Text>
              </View>
              <View style={styles.graphValueBlock}>
                <AnimatedCurrencyText
                  value={-comparisonTotals.secondary}
                  currency="EUR"
                  style={styles.graphValue}
                  baseColor={palette.textPrimary}
                />
                <Text style={styles.graphValueLabel}>Comparison range</Text>
              </View>
            </View>

            <View style={styles.legendRow}>
              <View style={styles.legendItem}>
                <View style={[styles.legendDot, { backgroundColor: palette.success }]} />
                <Text style={styles.legendText}>This month</Text>
              </View>
              <View style={styles.legendItem}>
                <View style={[styles.legendDot, { backgroundColor: palette.negative }]} />
                <Text style={styles.legendText}>Last month</Text>
              </View>
            </View>

            <View style={styles.comparisonRangeRow}>
              <Pressable style={styles.rangeButton} onPress={() => openPicker("primary")}>
                <Text style={styles.rangeLabel}>Primary range</Text>
                <Text style={styles.rangeValue}>{rangeLabel(ranges.primary)}</Text>
              </Pressable>
              <Pressable style={styles.rangeButton} onPress={() => openPicker("secondary")}>
                <Text style={styles.rangeLabel}>Compare against</Text>
                <Text style={styles.rangeValue}>{rangeLabel(ranges.secondary)}</Text>
              </Pressable>
            </View>

            <Text style={styles.comparisonSummary}>{comparisonSentence}</Text>

            {pickerVisible ? (
              <View style={styles.pickerWrap}>
                <Text style={styles.rangeInstructionLabel}>
                  {pickerTarget === "primary" ? "Primary range" : "Comparison range"}
                </Text>
                <Text style={styles.rangeInstructionText}>
                  {pickerStep === "start" ? "Pick the start date" : "Pick the end date"}
                </Text>

                <Text style={styles.editorLabel}>Year</Text>
                <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.yearRow}>
                  {years.map((year) => (
                    <Pressable
                      key={year}
                      style={[styles.yearChip, selectedYear === year ? styles.yearChipActive : null]}
                      onPress={() => handleYearSelect(year)}
                    >
                      <Text style={styles.yearChipText}>{year}</Text>
                    </Pressable>
                  ))}
                </ScrollView>

                <Text style={styles.editorLabel}>Month</Text>
                <View style={styles.monthGrid}>
                  {monthNames.map((month, index) => {
                    const selectedMonth =
                      pickerStep === "start" ? activeRange.startMonth : activeRange.endMonth;
                    return (
                      <Pressable
                        key={month}
                        style={[styles.monthChip, selectedMonth === index ? styles.monthChipActive : null]}
                        onPress={() => handleMonthSelect(index)}
                      >
                        <Text style={styles.monthChipText}>{month}</Text>
                      </Pressable>
                    );
                  })}
                </View>
              </View>
            ) : null}
          </Pressable>
        </Pressable>
      </Modal>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding,
    paddingBottom: 0
  },
  scrollContent: {
    gap: layout.sectionGap
  },
  topActionsBar: {
    marginBottom: spacing[16],
    alignItems: "flex-end",
    zIndex: 20,
    elevation: 20
  },
  headerIconWrap: {
    position: "relative"
  },
  headerActionsRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  headerRightSpacer: {
    width: 42,
    height: 42
  },
  sparkleButton: {
    width: 42,
    height: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center"
  },
  tooltip: {
    position: "absolute",
    top: 48,
    right: 0,
    minWidth: 160,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8],
    zIndex: 10
  },
  tooltipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  graphCard: {
    borderRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.82)",
    padding: 14,
    gap: 10
  },
  graphLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  graphMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  graphValuesRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  graphValueBlock: {
    flex: 1,
    gap: spacing[4]
  },
  graphValue: {
    ...typography.bodyStrong,
    fontVariant: ["tabular-nums"]
  },
  graphValueLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  legendRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[16]
  },
  legendItem: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6
  },
  legendDot: {
    width: 8,
    height: 8,
    borderRadius: 4
  },
  legendText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  pairedRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  sectionCardPressable: {
    flex: 1,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.82)",
    padding: spacing[12],
    gap: spacing[8]
  },
  sectionCard: {
    gap: spacing[8]
  },
  cardPressed: {
    opacity: 0.9,
    transform: [{ scale: 0.995 }]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  sectionValue: {
    color: palette.textPrimary,
    ...typography.title2,
    fontWeight: "700"
  },
  sectionMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  sectionHint: {
    color: palette.textSecondary,
    ...typography.caption
  },
  categoryPreviewWrap: {
    gap: spacing[8]
  },
  categoryRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  categoryLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  categoryValue: {
    color: palette.textSecondary,
    ...typography.caption
  },
  unassignedItem: {
    marginTop: spacing[4],
    borderRadius: 10,
    borderWidth: 1,
    borderColor: "rgba(220,232,255,0.14)",
    backgroundColor: "rgba(17,39,66,0.66)",
    minHeight: 34,
    paddingHorizontal: 10,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  unassignedPressed: {
    opacity: 0.88
  },
  unassignedText: {
    color: palette.textPrimary,
    ...typography.caption,
    flex: 1
  },
  suggestionsTitle: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  suggestionsWrap: {
    gap: spacing[12]
  },
  suggestionTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  suggestionMessage: {
    color: palette.textSecondary,
    ...typography.body2
  },
  loadingWrap: {
    gap: spacing[12]
  },
  loadingHero: {
    height: 186,
    borderRadius: 24
  },
  loadingCard: {
    height: 108,
    borderRadius: 18
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.74)",
    justifyContent: "flex-end"
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.99)",
    padding: spacing[16],
    gap: spacing[12],
    maxHeight: "85%"
  },
  modalHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  comparisonRangeRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  rangeButton: {
    flex: 1,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.78)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[4]
  },
  rangeLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rangeValue: {
    color: palette.textPrimary,
    ...typography.body2
  },
  comparisonSummary: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  pickerWrap: {
    gap: 10
  },
  rangeInstructionLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rangeInstructionText: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  editorLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  yearRow: {
    gap: spacing[8]
  },
  yearChip: {
    borderRadius: 999,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8]
  },
  yearChipActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.28)"
  },
  yearChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  monthGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  monthChip: {
    width: "23%",
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    alignItems: "center",
    justifyContent: "center",
    minHeight: 36
  },
  monthChipActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.28)"
  },
  monthChipText: {
    color: palette.textPrimary,
    ...typography.caption
  }
});
