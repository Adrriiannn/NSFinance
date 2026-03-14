import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { LayoutAnimation, Platform, Pressable, StyleSheet, Text, UIManager, View } from "react-native";
import { ExpenseTrackerCategoryRadialChart } from "../../../../src/components/expenseTracker/ExpenseTrackerCategoryRadialChart";
import { ExpenseTrackerMiniAppScreen } from "../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { EmptyState } from "../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import {
  buildExpenseTrackerCategoryBreakdown,
  buildExpenseTrackerPeriodSummary
} from "../../../../src/features/expenseTracker/expenseTrackerAnalytics";
import { useExpenseTrackerPeriod } from "../../../../src/features/expenseTracker/ExpenseTrackerPeriodContext";
import { useExpenseTrackerEntriesQuery } from "../../../../src/features/expenseTracker/useExpenseTracker";
import { expenseTrackerCategoryOptions } from "../../../../src/features/expenseTracker/expenseTrackerModels";
import { palette, radius, spacing, typography } from "../../../../src/theme/tokens";

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

function formatDateTime(occurredAtUtc: string) {
  return new Date(occurredAtUtc).toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit"
  });
}

function tintColor(hex: string, alpha: number) {
  const normalized = hex.replace("#", "");
  if (normalized.length !== 6) {
    return `rgba(226,236,255,${alpha})`;
  }

  const red = Number.parseInt(normalized.slice(0, 2), 16);
  const green = Number.parseInt(normalized.slice(2, 4), 16);
  const blue = Number.parseInt(normalized.slice(4, 6), 16);
  return `rgba(${red},${green},${blue},${alpha})`;
}

export default function ExpenseTrackerGraphsScreen() {
  const router = useRouter();
  const { period } = useExpenseTrackerPeriod();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const entries = entriesQuery.data ?? [];
  const currency = entries[0]?.currency ?? "EUR";
  const [expandedCategory, setExpandedCategory] = useState<string | null>(null);

  useEffect(() => {
    if (Platform.OS === "android" && UIManager.setLayoutAnimationEnabledExperimental) {
      UIManager.setLayoutAnimationEnabledExperimental(true);
    }
  }, []);

  const categoryBreakdown = useMemo(
    () => buildExpenseTrackerCategoryBreakdown(entries, period),
    [entries, period]
  );
  const summary = useMemo(
    () => buildExpenseTrackerPeriodSummary(entries, period),
    [entries, period]
  );
  const topLegendItems = categoryBreakdown.slice(0, 3);

  return (
    <ExpenseTrackerMiniAppScreen title="Graphs">
      {entriesQuery.isError ? (
        <ErrorState
          title="Could not load graphs"
          message={entriesQuery.error.message}
          onRetry={() => {
            void entriesQuery.refetch();
          }}
        />
      ) : null}

      {categoryBreakdown.length === 0 ? (
        <EmptyState
          title="No completed expenses in this period"
          message="Once you log completed expenses, this page will break down your category mix and top spending drivers."
          actionLabel="Add expense"
          onActionPress={() => router.push("/(tabs)/planner/expense-tracker/add" as never)}
        />
      ) : (
        <>
          <GlassCard style={styles.chartCard}>
            <View style={styles.chartCardHeader}>
              <View>
                <Text style={styles.chartTitle}>Expense share</Text>
                <Text style={styles.chartSubtitle}>Category mix for {period.label}</Text>
              </View>
            </View>

            <View style={styles.chartBody}>
              <ExpenseTrackerCategoryRadialChart
                data={categoryBreakdown}
                totalLabel={formatAmount(summary.completedTotal, currency)}
              />
              <View style={styles.chartLegend}>
                {topLegendItems.map((item) => {
                  const categoryOption = expenseTrackerCategoryOptions.find((option) => option.value === item.category);
                  return (
                    <View key={item.category} style={styles.legendRow}>
                      <View style={styles.legendLabelWrap}>
                        <View style={[styles.legendDot, { backgroundColor: categoryOption?.color ?? palette.primaryGlow }]} />
                        <Text style={styles.legendLabel}>{item.category}</Text>
                      </View>
                      <Text style={styles.legendValue}>{item.percentage.toFixed(1)}%</Text>
                    </View>
                  );
                })}
              </View>
            </View>
          </GlassCard>

          <View style={styles.breakdownWrap}>
            {categoryBreakdown.map((item) => {
              const categoryOption = expenseTrackerCategoryOptions.find((option) => option.value === item.category);
              const categoryColor = categoryOption?.color ?? palette.primaryGlow;
              const expanded = expandedCategory === item.category;
              return (
                <GlassCard key={item.category} style={styles.categoryCard}>
                  <Pressable
                    style={styles.categoryCardPressable}
                    onPress={() => {
                      LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
                      setExpandedCategory((current) => (current === item.category ? null : item.category));
                    }}
                  >
                    <View style={styles.categoryMainRow}>
                      <View style={[styles.categoryIconWrap, { backgroundColor: tintColor(categoryColor, 0.18) }]}>
                        <Ionicons
                          name={(categoryOption?.icon ?? "ellipse-outline") as keyof typeof Ionicons.glyphMap}
                          size={18}
                          color={categoryColor}
                        />
                      </View>

                      <View style={styles.categoryContentColumn}>
                        <Text style={styles.categoryName}>{item.category}</Text>
                        <View style={styles.progressTrack}>
                          <View
                            style={[
                              styles.progressTrackTint,
                              { backgroundColor: tintColor(categoryColor, 0.18) }
                            ]}
                          />
                          <View
                            style={[
                              styles.progressFill,
                              {
                                width: `${Math.max(item.percentage, 4)}%`,
                                backgroundColor: categoryColor
                              }
                            ]}
                          />
                        </View>
                      </View>

                      <View style={styles.categoryMetricsColumn}>
                        <Text style={styles.categoryMetricPrimary}>{item.count}</Text>
                        <Text style={styles.categoryMetricSecondary}>{item.percentage.toFixed(1)}%</Text>
                      </View>
                    </View>
                  </Pressable>

                  {expanded ? (
                    <View style={styles.transactionList}>
                      {item.entries.map((entry) => (
                        <View key={entry.id} style={styles.transactionRow}>
                          <View style={styles.transactionTextWrap}>
                            <Text style={styles.transactionTitle}>{entry.merchant ?? entry.title}</Text>
                            <Text style={styles.transactionMeta}>{formatDateTime(entry.occurredAtUtc)}</Text>
                          </View>
                          <Text style={styles.transactionAmount}>{formatAmount(entry.amount, entry.currency)}</Text>
                        </View>
                      ))}
                    </View>
                  ) : null}
                </GlassCard>
              );
            })}
          </View>
        </>
      )}
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
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
    gap: 10,
    justifyContent: "center"
  },
  categoryName: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  categoryMetricsColumn: {
    width: 64,
    alignItems: "flex-end",
    gap: 2
  },
  categoryMetricPrimary: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  categoryMetricSecondary: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  progressTrack: {
    height: 10,
    borderRadius: 999,
    overflow: "hidden",
    position: "relative"
  },
  progressTrackTint: {
    ...StyleSheet.absoluteFillObject,
    borderRadius: 999
  },
  progressFill: {
    height: 10,
    borderRadius: 999
  },
  transactionList: {
    gap: 10,
    paddingTop: spacing[4]
  },
  transactionRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12],
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.56)",
    paddingHorizontal: spacing[12],
    paddingVertical: 10
  },
  transactionTextWrap: {
    flex: 1
  },
  transactionTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  transactionMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  transactionAmount: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  }
});
