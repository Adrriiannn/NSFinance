import { useLocalSearchParams, useRouter } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { AmountText } from "../../../src/components/ui/AmountText";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { HeaderShell } from "../../../src/layout/appHeader";
import { useTransactionDetailQuery } from "../../../src/features/transactions/useTransactions";
import { formatDate, formatTime } from "../../../src/lib/format";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function PlannerTransactionDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string }>();
  const transactionId = params.id ?? "";
  const transactionQuery = useTransactionDetailQuery(transactionId);
  const plannerStore = usePlannerStore();
  const annotation = transactionId ? plannerStore.annotations[transactionId] : undefined;

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <HeaderShell preset="secondaryDetail" title="Transaction detail" />

      {!transactionId ? (
        <ErrorState
          title="Transaction not found"
          message="No transaction was selected."
        />
      ) : transactionQuery.isLoading && !transactionQuery.data ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={{ height: 148, borderRadius: 18 }} />
          <SkeletonBlock style={{ height: 196, borderRadius: 18 }} />
        </View>
      ) : transactionQuery.isError ? (
        <ErrorState
          title="Could not load transaction"
          message={transactionQuery.error.message}
          onRetry={() => {
            void transactionQuery.refetch();
          }}
        />
      ) : transactionQuery.data ? (
        <>
          <GlassCard style={styles.heroCard}>
            <Text style={styles.transactionName}>{transactionQuery.data.description}</Text>
            <AmountText
              amount={transactionQuery.data.amount}
              currency={transactionQuery.data.currency}
              appearance="transaction"
              style={styles.transactionAmount}
            />
            <Text style={styles.transactionSubtitle}>
              {transactionQuery.data.accountName} | {formatDate(transactionQuery.data.bookedAtUtc)}
            </Text>
          </GlassCard>

          <GlassCard style={styles.detailCard}>
            <DetailLine label="Account" value={transactionQuery.data.accountName} />
            <DetailLine label="Date" value={formatDate(transactionQuery.data.bookedAtUtc)} />
            <DetailLine label="Time" value={formatTime(transactionQuery.data.bookedAtUtc)} />
            <DetailLine
              label="Category"
              value={annotation?.category ?? transactionQuery.data.categoryName ?? "Uncategorized"}
            />
            <DetailLine label="Place" value="Location enrichment coming soon" />
            <DetailLine label="Reason" value={annotation?.reason || "Not provided"} />
            <DetailLine label="Notes" value={annotation?.notes || "Not provided"} />
          </GlassCard>

          <View style={styles.actions}>
            <SecondaryButton label="Back" onPress={() => router.back()} />
          </View>
        </>
      ) : null}
    </ScreenContainer>
  );
}

function DetailLine({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailLine}>
      <Text style={styles.detailLabel}>{label}</Text>
      <Text style={styles.detailValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: {},
  heroCard: {
    gap: spacing[8]
  },
  transactionName: {
    color: palette.textPrimary,
    ...typography.title2
  },
  transactionAmount: {
    color: palette.textPrimary,
    ...typography.displayL,
    fontVariant: ["tabular-nums"]
  },
  transactionSubtitle: {
    color: palette.textSecondary,
    ...typography.body2
  },
  detailCard: {
    gap: spacing[8]
  },
  detailLine: {
    gap: spacing[4]
  },
  detailLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  detailValue: {
    color: palette.textPrimary,
    ...typography.body1
  },
  actions: {
    gap: spacing[12]
  },
  loadingWrap: {
    gap: spacing[12]
  }
});

