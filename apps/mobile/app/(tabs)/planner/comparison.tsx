import { useMemo } from "react";
import { StyleSheet, Text } from "react-native";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { HeaderShell } from "../../../src/layout/appHeader";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { getMonthComparison } from "../../../src/features/planner/plannerInsights";
import { formatCurrency } from "../../../src/lib/format";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function PlannerComparisonScreen() {
  const transactionsQuery = useTransactionsQuery();
  const comparison = useMemo(
    () => getMonthComparison(transactionsQuery.data ?? []),
    [transactionsQuery.data]
  );

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <HeaderShell preset="secondaryDetail" title="Monthly comparison" />

      <GlassCard style={styles.card}>
        <Text style={styles.label}>This month</Text>
        <Text style={styles.primaryValue}>{formatCurrency(-comparison.thisMonthSpend, "EUR")}</Text>
      </GlassCard>

      <GlassCard style={styles.card}>
        <Text style={styles.label}>Last month</Text>
        <Text style={styles.secondaryValue}>{formatCurrency(-comparison.lastMonthSpend, "EUR")}</Text>
      </GlassCard>

      <GlassCard style={styles.card}>
        <Text style={styles.label}>Direction</Text>
        <Text
          style={[
            styles.direction,
            comparison.trend === "improved"
              ? styles.improved
              : comparison.trend === "worse"
                ? styles.worse
                : styles.neutral
          ]}
        >
          {comparison.trend === "improved"
            ? "Improved"
            : comparison.trend === "worse"
              ? "Worse"
              : "No major change"}
        </Text>
        <Text style={styles.delta}>Delta {formatCurrency(comparison.delta, "EUR")}</Text>
      </GlassCard>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {},
  card: {
    gap: spacing[8]
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption
  },
  primaryValue: {
    color: palette.textPrimary,
    ...typography.displayL
  },
  secondaryValue: {
    color: palette.textPrimary,
    ...typography.title1
  },
  direction: {
    ...typography.sectionTitle,
    fontWeight: "700"
  },
  improved: {
    color: palette.success
  },
  worse: {
    color: palette.negative
  },
  neutral: {
    color: palette.textSecondary
  },
  delta: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
