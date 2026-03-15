import { useLocalSearchParams } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { ExpenseTrackerMiniAppScreen } from "../../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { EmptyState } from "../../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../../src/components/ui/GlassCard";
import { useExpensePlanning } from "../../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { useExpenseTrackerEntriesQuery, useExpenseTrackerTaxonomyQuery } from "../../../../../src/features/expenseTracker/useExpenseTracker";
import {
  buildExpensePlanCategoryMetrics,
  buildExpensePlanTaxonomyLookup,
  formatExpensePlanPeriod
} from "../../../../../src/features/expenseTracker/expensePlanningUtils";
import type { ExpenseAnalyticsMode } from "../../../../../src/features/expenseTracker/expensePlanningTypes";
import { palette, spacing, typography } from "../../../../../src/theme/tokens";

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

export default function ExpensePlanCategoryDetailScreen() {
  const params = useLocalSearchParams<{ categoryId?: string; planId?: string; mode?: string }>();
  const planId = typeof params.planId === "string" ? params.planId : "";
  const categoryId = typeof params.categoryId === "string" ? params.categoryId : "";
  const mode = params.mode === "planned" || params.mode === "variance" ? params.mode : "actual";
  const { getPlanById } = useExpensePlanning();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();

  const plan = getPlanById(planId);
  const taxonomyLookup = buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []);
  const metrics = plan ? buildExpensePlanCategoryMetrics(plan, entriesQuery.data ?? [], taxonomyLookup, mode as ExpenseAnalyticsMode) : [];
  const selectedMetric = metrics.find((item) => String(item.categoryId ?? item.key) === categoryId) ?? null;
  const currency = entriesQuery.data?.[0]?.currency ?? "EUR";

  if (!plan || !selectedMetric) {
    return (
      <ExpenseTrackerMiniAppScreen title="Category detail">
        <EmptyState title="Category detail unavailable" message="Pick a plan category from Analytics to inspect it here." />
      </ExpenseTrackerMiniAppScreen>
    );
  }

  const matchingEntries = (entriesQuery.data ?? []).filter((entry) => selectedMetric.entryIds.includes(entry.id));

  return (
    <ExpenseTrackerMiniAppScreen title="Category detail">
      <GlassCard style={styles.heroCard}>
        <Text style={styles.categoryTitle}>{selectedMetric.categoryName}</Text>
        <Text style={styles.categoryMeta}>{plan.title} • {formatExpensePlanPeriod(plan.startDate, plan.endDate)}</Text>
        <View style={styles.metricRow}>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Total</Text>
            <Text style={styles.metricValue}>{formatAmount(selectedMetric.amount, currency)}</Text>
          </View>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Share</Text>
            <Text style={styles.metricValue}>{selectedMetric.percentage.toFixed(1)}%</Text>
          </View>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Transactions</Text>
            <Text style={styles.metricValue}>{selectedMetric.transactionCount}</Text>
          </View>
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Subcategory breakdown</Text>
        <View style={styles.listWrap}>
          {selectedMetric.subcategories.map((subcategory) => (
            <View key={`${subcategory.subcategoryId ?? subcategory.subcategoryName}`} style={styles.listRow}>
              <View>
                <Text style={styles.listTitle}>{subcategory.subcategoryName}</Text>
                <Text style={styles.listMeta}>{subcategory.percentage.toFixed(1)}%</Text>
              </View>
              <Text style={styles.listAmount}>{formatAmount(subcategory.amount, currency)}</Text>
            </View>
          ))}
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Matching transactions</Text>
        {matchingEntries.length === 0 ? (
          <Text style={styles.emptyText}>No completed transactions matched this category for the selected plan period.</Text>
        ) : (
          <View style={styles.listWrap}>
            {matchingEntries.map((entry) => (
              <View key={entry.id} style={styles.listRow}>
                <View>
                  <Text style={styles.listTitle}>{entry.merchant ?? entry.title}</Text>
                  <Text style={styles.listMeta}>{entry.subcategoryName ?? entry.categoryName ?? "Unplanned"}</Text>
                </View>
                <Text style={styles.listAmount}>{formatAmount(entry.amount, entry.currency)}</Text>
              </View>
            ))}
          </View>
        )}
      </GlassCard>
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    gap: spacing[12]
  },
  categoryTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  categoryMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  metricRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  metricBlock: {
    flex: 1,
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
  sectionCard: {
    gap: spacing[16]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  listWrap: {
    gap: spacing[12]
  },
  listRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  listTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  listMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  listAmount: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  emptyText: {
    color: palette.textSecondary,
    ...typography.body2
  }
});

