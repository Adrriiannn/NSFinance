import { useEffect, useMemo, useState } from "react";
import { Text, View } from "react-native";
import { EmptyState } from "../../../src/components/ui/feedback/EmptyState";
import { Card } from "../../../src/components/ui/cards/Card";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { HeaderShell } from "../../../src/layout/appHeader";
import { useFinancialCommitmentsQuery, useRecurringPaymentsQuery } from "../../../src/features/banking/useBanking";
import { buildUpcomingCommitmentRows } from "../../../src/features/banking/commitmentPresentation";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { buildRecurringPaymentForecast } from "../../../src/features/planner/forecasting";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type { BankRecurringPaymentsDto } from "../../../src/types/api";

function formatCountdown(daysUntilDue: number) {
  if (daysUntilDue <= 0) {
    return "today";
  }

  return `in ${daysUntilDue} day${daysUntilDue === 1 ? "" : "s"}`;
}

type ProviderRecurringPayment = {
  id: string;
  label: string;
  amount: number | null;
  currency: string | null;
  nextPaymentDateUtc: string | null;
  source: "direct_debit" | "standing_order";
};

function normalizeProviderRecurringPayments(data: BankRecurringPaymentsDto | undefined): ProviderRecurringPayment[] {
  if (!data) {
    return [];
  }

  const directDebits = data.directDebits.map((entry) => ({
    id: `dd-${entry.id}`,
    label: entry.merchantName || entry.reference || entry.accountDisplayName || "Direct debit",
    amount: entry.nextPaymentAmount,
    currency: entry.nextPaymentCurrency,
    nextPaymentDateUtc: entry.nextPaymentDateUtc,
    source: "direct_debit" as const
  }));

  const standingOrders = data.standingOrders.map((entry) => ({
    id: `so-${entry.id}`,
    label: entry.payeeName || entry.reference || entry.accountDisplayName || "Standing order",
    amount: entry.nextPaymentAmount,
    currency: entry.nextPaymentCurrency,
    nextPaymentDateUtc: entry.nextPaymentDateUtc,
    source: "standing_order" as const
  }));

  return [...directDebits, ...standingOrders].sort((left, right) => {
    const leftStamp = left.nextPaymentDateUtc ? Date.parse(left.nextPaymentDateUtc) : Number.MAX_SAFE_INTEGER;
    const rightStamp = right.nextPaymentDateUtc ? Date.parse(right.nextPaymentDateUtc) : Number.MAX_SAFE_INTEGER;
    return leftStamp - rightStamp;
  });
}

function computeDaysUntilDue(nextPaymentDateUtc: string | null, now: Date) {
  if (!nextPaymentDateUtc) {
    return 0;
  }

  const dueDate = new Date(nextPaymentDateUtc);
  if (Number.isNaN(dueDate.getTime())) {
    return 0;
  }

  const diffMs = dueDate.getTime() - now.getTime();
  return Math.max(0, Math.ceil(diffMs / (24 * 60 * 60 * 1000)));
}

export default function CashflowUpcomingPaymentsScreen() {
  const transactionsQuery = useTransactionsQuery();
  const recurringPaymentsQuery = useRecurringPaymentsQuery();
  const commitmentsQuery = useFinancialCommitmentsQuery();
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
  const providerRecurring = useMemo(
    () =>
      normalizeProviderRecurringPayments(recurringPaymentsQuery.data).map((item) => ({
        ...item,
        daysUntilDue: computeDaysUntilDue(item.nextPaymentDateUtc, clockNow)
      })),
    [clockNow, recurringPaymentsQuery.data]
  );
  const providerNext7Days = useMemo(
    () => providerRecurring.filter((item) => item.daysUntilDue <= 7),
    [providerRecurring]
  );
  const commitmentRows = useMemo(
    () => buildUpcomingCommitmentRows(commitmentsQuery.data?.items, clockNow.getTime()),
    [clockNow, commitmentsQuery.data?.items]
  );
  const commitmentsNext7Days = useMemo(() => {
    const horizonMs = clockNow.getTime() + 7 * 24 * 60 * 60 * 1000;
    return commitmentRows.filter(
      (row) => row.nextDateUtc !== null && Date.parse(row.nextDateUtc) <= horizonMs
    );
  }, [clockNow, commitmentRows]);

  return (
    <ScreenContainer
      contentStyle={styles.content}
      withBottomTabOffset
      bottomInsetOffset={spacing[12]}
    >
      <HeaderShell preset="secondaryDetail" title="Upcoming payments" />

      <Card style={styles.summaryCard}>
        <Text style={styles.summaryTitle}>Next 7 days</Text>
        {commitmentsNext7Days.length > 0 ? (
          commitmentsNext7Days.map((commitment) => (
            <View key={commitment.id} style={styles.paymentRow}>
              <Text style={styles.paymentLabel}>
                {commitment.label} <Text style={styles.sourceLabel}>({commitment.sourceLabel})</Text>
              </Text>
              <Text style={styles.paymentMeta}>
                {commitment.amountText} {commitment.whenText}
                {commitment.isStale ? " · may be out of date" : ""}
              </Text>
            </View>
          ))
        ) : providerNext7Days.length > 0 ? (
          providerNext7Days.map((payment) => (
            <View key={payment.id} style={styles.paymentRow}>
              <Text style={styles.paymentLabel}>
                {payment.label}{" "}
                <Text style={styles.sourceLabel}>
                  ({payment.source === "direct_debit" ? "direct debit" : "standing order"})
                </Text>
              </Text>
              <Text style={styles.paymentMeta}>
                {payment.amount !== null && payment.currency
                  ? new Intl.NumberFormat("en-GB", {
                      style: "currency",
                      currency: payment.currency
                    }).format(payment.amount)
                  : "Amount pending"}{" "}
                {formatCountdown(payment.daysUntilDue)}
              </Text>
            </View>
          ))
        ) : forecast.next7Days.length > 0 ? (
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
          <Text style={styles.emptyHint}>
            No provider recurring commitments found in the next 7 days.
          </Text>
        )}
      </Card>

      <Text style={styles.sectionTitle}>Forecast for the rest of the month</Text>
      {commitmentRows.length === 0 && providerRecurring.length === 0 && forecast.restOfMonth.length === 0 ? (
        <EmptyState
          title="No recurring payments detected"
          message="As transaction history grows, recurring payment forecasts will appear here."
        />
      ) : commitmentRows.length > 0 ? (
        <View style={styles.listWrap}>
          {commitmentRows.map((commitment) => (
            <Card key={commitment.id} style={styles.itemCard}>
              <Text style={styles.itemTitle}>{commitment.label}</Text>
              <Text style={styles.itemMeta}>
                {commitment.amountText} · {commitment.whenText}
                {commitment.isStale ? " · may be out of date" : ""}
              </Text>
              <Text style={styles.itemMeta}>
                {commitment.accountDisplayName} · {commitment.sourceLabel}
              </Text>
            </Card>
          ))}
        </View>
      ) : (
        <View style={styles.listWrap}>
          {providerRecurring.map((payment) => (
            <Card key={payment.id} style={styles.itemCard}>
              <Text style={styles.itemTitle}>{payment.label}</Text>
              <Text style={styles.itemMeta}>
                {payment.amount !== null && payment.currency
                  ? new Intl.NumberFormat("en-GB", {
                      style: "currency",
                      currency: payment.currency
                    }).format(payment.amount)
                  : "Amount pending"}{" "}
                · {payment.nextPaymentDateUtc ? formatCountdown(payment.daysUntilDue) : "date pending"}
              </Text>
              <Text style={styles.itemMeta}>
                Source: {payment.source === "direct_debit" ? "Direct debit" : "Standing order"}
              </Text>
            </Card>
          ))}
          {providerRecurring.length === 0
            ? forecast.restOfMonth.map((payment) => (
                <Card key={payment.id} style={styles.itemCard}>
                  <Text style={styles.itemTitle}>{payment.label}</Text>
                  <Text style={styles.itemMeta}>
                    {new Intl.NumberFormat("en-GB", {
                      style: "currency",
                      currency: payment.currency
                    }).format(payment.amount)}{" "}
                    {formatCountdown(payment.daysUntilDue)}
                  </Text>
                  <Text style={styles.itemMeta}>Estimated cadence: {payment.cadenceLabel}</Text>
                </Card>
              ))
            : null}
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
  sourceLabel: {
    color: palette.textSecondary,
    ...typography.caption
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

