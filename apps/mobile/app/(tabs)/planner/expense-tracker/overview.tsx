import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useMemo } from "react";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { ExpenseTrackerJournalItem } from "../../../../src/components/expenseTracker/ExpenseTrackerJournalItem";
import { ExpenseTrackerMiniAppScreen } from "../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { AnimatedCurrencyText } from "../../../../src/components/ui/AnimatedCurrencyText";
import { EmptyState } from "../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import {
  buildExpenseTrackerPeriodComparison,
  filterEntriesForPeriod
} from "../../../../src/features/expenseTracker/expenseTrackerAnalytics";
import { groupExpenseTrackerEntries } from "../../../../src/features/expenseTracker/expenseTrackerUtils";
import { useExpenseTrackerEntriesQuery } from "../../../../src/features/expenseTracker/useExpenseTracker";
import { useExpenseTrackerPeriod } from "../../../../src/features/expenseTracker/ExpenseTrackerPeriodContext";
import { palette, spacing, typography } from "../../../../src/theme/tokens";

const shortcuts = [
  { label: "Add expense", icon: "add-circle-outline", route: "/(tabs)/planner/expense-tracker/add" },
  { label: "Planned", icon: "time-outline", route: "/(tabs)/planner/expense-tracker/add?defaultStatus=planned" },
  { label: "Subscriptions", icon: "repeat-outline", route: "/(tabs)/planner/expense-tracker/add?focusDomainId=280&recurring=true" },
  { label: "Categories", icon: "grid-outline", route: "/(tabs)/planner/expense-tracker/graphs" },
  { label: "Bills", icon: "receipt-outline", route: "/(tabs)/planner/expense-tracker/add?focusDomainId=140" },
  { label: "Recurring", icon: "refresh-outline", route: "/(tabs)/planner/expense-tracker/add?recurring=true" }
] as const;

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

function formatComparisonIndicator(amount: number, currency: string) {
  if (amount === 0) {
    return formatAmount(0, currency);
  }

  const absolute = formatAmount(Math.abs(amount), currency);
  return `${amount > 0 ? "-" : "+"}${absolute}`;
}

export default function ExpenseTrackerOverviewScreen() {
  const router = useRouter();
  const { period } = useExpenseTrackerPeriod();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const entries = entriesQuery.data ?? [];
  const currency = entries[0]?.currency ?? "EUR";

  const comparison = useMemo(() => buildExpenseTrackerPeriodComparison(entries, period), [entries, period]);
  const recentEntries = useMemo(
    () => filterEntriesForPeriod(entries, period).sort((left, right) => new Date(right.occurredAtUtc).getTime() - new Date(left.occurredAtUtc).getTime()),
    [entries, period]
  );
  const journalSections = useMemo(() => groupExpenseTrackerEntries(recentEntries), [recentEntries]);

  return (
    <ExpenseTrackerMiniAppScreen title="Overview">
      {entriesQuery.isError ? (
        <ErrorState
          title="Could not load expenses"
          message={entriesQuery.error.message}
          onRetry={() => {
            void entriesQuery.refetch();
          }}
        />
      ) : null}

      <GlassCard style={styles.comparisonCard}>
        <View style={styles.comparisonHeader}>
          <View style={styles.periodBlock}>
            <Text style={styles.comparisonRange}>{period.comparisonLabel}</Text>
          </View>
          <View style={styles.compareDivider} />
          <View style={[styles.periodBlock, styles.currentBlock]}>
            <Text style={styles.comparisonRange}>{period.label}</Text>
          </View>
        </View>

        <View style={styles.comparisonValuesRow}>
          <View style={styles.valueBlock}>
            <AnimatedCurrencyText
              value={comparison.previousTotal}
              currency={currency}
              style={[styles.periodValue, comparison.previousTotal > comparison.currentTotal ? styles.periodValueNegative : null]}
              baseColor={comparison.previousTotal > comparison.currentTotal ? palette.negative : palette.textPrimary}
            />
          </View>
          <View style={styles.valueBlockRight}>
            <AnimatedCurrencyText
              value={comparison.currentTotal}
              currency={currency}
              style={[styles.periodValue, comparison.currentTotal > comparison.previousTotal ? styles.periodValueNegative : null]}
              baseColor={comparison.currentTotal > comparison.previousTotal ? palette.negative : palette.textPrimary}
            />
            <Text
              style={[
                styles.differenceText,
                comparison.difference > 0 ? styles.differenceTextNegative : comparison.difference < 0 ? styles.differenceTextPositive : null
              ]}
            >
              {formatComparisonIndicator(comparison.difference, currency)}
            </Text>
          </View>
        </View>
      </GlassCard>

      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.shortcutRail}>
        {shortcuts.map((shortcut) => (
          <Pressable key={shortcut.label} style={styles.shortcutCard} onPress={() => router.push(shortcut.route as never)}>
            <View style={styles.shortcutIconWrap}>
              <Ionicons name={shortcut.icon as keyof typeof Ionicons.glyphMap} size={18} color={palette.textPrimary} />
            </View>
            <Text style={styles.shortcutLabel}>{shortcut.label}</Text>
          </Pressable>
        ))}
      </ScrollView>

      {journalSections.length === 0 ? (
        <EmptyState
          title="No spending in this period yet"
          message="Use the Add Expense page to log daily spending, bills, planned purchases, and subscriptions."
          actionLabel="Add expense"
          onActionPress={() => router.push("/(tabs)/planner/expense-tracker/add" as never)}
        />
      ) : (
        <View style={styles.sectionListWrap}>
          {journalSections.map((section) => (
            <View key={section.title} style={styles.groupWrap}>
              <View style={styles.groupHeader}>
                <Text style={styles.groupTitle}>{section.title}</Text>
                <Text style={styles.groupTotal}>{formatAmount(section.total, currency)}</Text>
              </View>
              <View style={styles.groupEntries}>
                {section.data.map((entry) => (
                  <ExpenseTrackerJournalItem
                    key={entry.id}
                    entry={entry}
                    onPress={() =>
                      router.push({
                        pathname: "/(tabs)/planner/expense-tracker/add",
                        params: { entryId: entry.id }
                      })
                    }
                  />
                ))}
              </View>
            </View>
          ))}
        </View>
      )}
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
  comparisonCard: {
    gap: spacing[16]
  },
  comparisonHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  periodBlock: {
    gap: 4
  },
  comparisonRange: {
    color: palette.textSecondary,
    ...typography.body2,
    fontWeight: "600"
  },
  currentBlock: {
    alignItems: "flex-end"
  },
  compareDivider: {
    flex: 1,
    height: 1,
    backgroundColor: palette.border
  },
  comparisonValuesRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  valueBlock: {
    flex: 1,
    gap: 6
  },
  valueBlockRight: {
    flex: 1,
    alignItems: "flex-end",
    gap: 6
  },
  periodValue: {
    ...typography.title2,
    fontWeight: "700"
  },
  periodValueNegative: {
    color: palette.negative
  },
  differenceText: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  differenceTextNegative: {
    color: palette.negative
  },
  differenceTextPositive: {
    color: palette.success
  },
  shortcutRail: {
    gap: spacing[4],
    paddingRight: spacing[8]
  },
  shortcutCard: {
    width: 86,
    gap: spacing[4],
    alignItems: "center"
  },
  shortcutIconWrap: {
    width: 44,
    height: 44,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(47,107,255,0.2)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.26)"
  },
  shortcutLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600",
    textAlign: "center"
  },
  sectionListWrap: {
    gap: spacing[16]
  },
  groupWrap: {
    gap: 10
  },
  groupHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  groupTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  groupTotal: {
    color: palette.textSecondary,
    ...typography.caption
  },
  groupEntries: {
    gap: 10
  }
});
