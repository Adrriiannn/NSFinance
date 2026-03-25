import { useEffect, useMemo, useState } from "react";
import { Text, View } from "react-native";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { HeaderShell } from "../../../src/layout/appHeader";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { buildRecurringPaymentForecast } from "../../../src/features/planner/forecasting";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

function formatCountdown(daysUntilDue: number) {
  if (daysUntilDue <= 0) {
    return "today";
  }

  return `in ${daysUntilDue} day${daysUntilDue === 1 ? "" : "s"}`;
}

export default function CashflowUpcomingPaymentsScreen() {
  const transactionsQuery = useTransactionsQuery();
  const [clockNow, setClockNow] = useState(() => new Date());

  useEffect(() => {
    const interval = setInterval(() => {
      setClockNow(new Date());
    }, 60_000);

    return () => clearInterval(interval);
  }, []);

  const forecast = useMemo(
    () => buildRecurringPaymentForecast(transactionsQuery.data ?? [], clockNow),
    [clockNow, transactionsQuery.data]
  );

  return (
    <ScreenContainer
      contentStyle={styles.content}
      withBottomTabOffset
      bottomInsetOffset={spacing[12]}
    >
      <HeaderShell preset="secondaryDetail" title="Upcoming payments" />

      <GlassCard style={styles.summaryCard}>
        <Text style={styles.summaryTitle}>Next 7 days</Text>
        {forecast.next7Days.length > 0 ? (
          forecast.next7Days.map((payment) => (
            <View key={payment.id} style={styles.paymentRow}>
              <Text style={styles.paymentLabel}>{payment.label}</Text>
              <Text style={styles.paymentMeta}>
                {new Intl.NumberFormat("en-GB", {
                  style: "currency",
                  currency: payment.currency
                }).format(payment.amount)}{" "}
                {formatCountdown(payment.daysUntilDue)}
              </Text>
            </View>
          ))
        ) : (
          <Text style={styles.emptyHint}>No recurring payments detected in the next 7 days.</Text>
        )}
      </GlassCard>

      <Text style={styles.sectionTitle}>Forecast for the rest of the month</Text>
      {forecast.restOfMonth.length === 0 ? (
        <EmptyState
          title="No recurring payments detected"
          message="As transaction history grows, recurring payment forecasts will appear here."
        />
      ) : (
        <View style={styles.listWrap}>
          {forecast.restOfMonth.map((payment) => (
            <GlassCard key={payment.id} style={styles.itemCard}>
              <Text style={styles.itemTitle}>{payment.label}</Text>
              <Text style={styles.itemMeta}>
                {new Intl.NumberFormat("en-GB", {
                  style: "currency",
                  currency: payment.currency
                }).format(payment.amount)}{" "}
                {formatCountdown(payment.daysUntilDue)}
              </Text>
              <Text style={styles.itemMeta}>Estimated cadence: {payment.cadenceLabel}</Text>
            </GlassCard>
          ))}
        </View>
      )}
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {},
  summaryCard: {
    gap: spacing[8]
  },
  summaryTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  paymentRow: {
    gap: spacing[4]
  },
  paymentLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  paymentMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  emptyHint: {
    color: palette.textSecondary,
    ...typography.caption
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  listWrap: {
    gap: spacing[12]
  },
  itemCard: {
    gap: spacing[8]
  },
  itemTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  itemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  }
}));

