import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo } from "react";
import { Alert, ScrollView, StyleSheet, Text, View } from "react-native";
import * as FileSystem from "expo-file-system/legacy";
import * as Sharing from "expo-sharing";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SectionHeader } from "../../../src/components/ui/SectionHeader";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { HeaderShell } from "../../../src/layout/appHeader";
import { useAccountDetailQuery } from "../../../src/features/accounts/useAccounts";
import { useBankConnectionsQuery } from "../../../src/features/banking/useBanking";
import { useAccountTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { formatCurrency, formatDate } from "../../../src/lib/format";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { getFloatingTabBarContentInset } from "../../../src/theme/insets";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

function formatUtcDate(isoDate: string) {
  const date = new Date(isoDate);
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  const day = String(date.getUTCDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function formatUtcTime(isoDate: string) {
  const date = new Date(isoDate);
  const hours = String(date.getUTCHours()).padStart(2, "0");
  const minutes = String(date.getUTCMinutes()).padStart(2, "0");
  return `${hours}:${minutes}`;
}

function formatDateTime(isoDate?: string | null) {
  if (!isoDate) {
    return "Not synced yet";
  }

  const parsed = new Date(isoDate);
  if (Number.isNaN(parsed.getTime())) {
    return "Not synced yet";
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  }).format(parsed);
}

function csvCell(value: string | number | null | undefined) {
  const normalized = value === null || value === undefined ? "" : String(value);
  return `"${normalized.replace(/"/g, '""')}"`;
}

function formatExportTimestamp(now = new Date()) {
  const day = String(now.getDate()).padStart(2, "0");
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const year = String(now.getFullYear()).slice(-2);
  const hours = String(now.getHours()).padStart(2, "0");
  const minutes = String(now.getMinutes()).padStart(2, "0");
  return `${day}-${month}-${year}_${hours}-${minutes}`;
}

function toSafeAccountSlug(accountName: string) {
  return accountName
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .replace(/-{2,}/g, "-");
}

export default function AccountDetailsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const params = useLocalSearchParams<{ id?: string }>();
  const accountId = params.id ?? "";
  const plannerStore = usePlannerStore();

  const accountQuery = useAccountDetailQuery(accountId);
  const transactionsQuery = useAccountTransactionsQuery(accountId);
  const connectionsQuery = useBankConnectionsQuery();
  const latestConnection = useMemo(() => {
    const list = connectionsQuery.data ?? [];
    return [...list].sort((left, right) => {
      const leftStamp = Date.parse(left.lastSuccessfulSyncUtc ?? left.updatedUtc);
      const rightStamp = Date.parse(right.lastSuccessfulSyncUtc ?? right.updatedUtc);
      return rightStamp - leftStamp;
    })[0] ?? null;
  }, [connectionsQuery.data]);

  if (!accountId) {
    return (
      <ScreenContainer>
        <EmptyState
          title="Account not found"
          message="We could not identify the selected account."
          actionLabel="Back to accounts"
          onActionPress={() => router.replace("/(tabs)/accounts")}
        />
      </ScreenContainer>
    );
  }

  const isInitialLoading =
    (accountQuery.isLoading && !accountQuery.data) ||
    (transactionsQuery.isLoading && !transactionsQuery.data) ||
    (connectionsQuery.isLoading && !connectionsQuery.data);

  const error = accountQuery.error ?? transactionsQuery.error ?? connectionsQuery.error;
  const account = accountQuery.data;
  const transactions = transactionsQuery.data ?? [];
  const listBottomInset = Math.max(
    spacing[12],
    getFloatingTabBarContentInset(insets.bottom, spacing[12])
  );

  const exportCsv = async () => {
    if (!account) {
      return;
    }

    try {
      const header = "Date,Time,Transaction,Account,Category,Amount,Currency,Notes";
      const rows = transactions.map((item) => {
        const annotation = plannerStore.annotations[item.id];
        const category =
          annotation?.category ??
          item.categoryName ??
          (item.direction === "Income" ? "Income" : "Uncategorized");
        const amount = Number(item.amount).toFixed(2);
        const notes = annotation?.notes ?? "";

        return [
          csvCell(formatUtcDate(item.bookedAtUtc)),
          csvCell(formatUtcTime(item.bookedAtUtc)),
          csvCell(item.description),
          csvCell(item.accountName),
          csvCell(category),
          csvCell(amount),
          csvCell(item.currency),
          csvCell(notes)
        ].join(",");
      });
      const csvText = [header, ...rows].join("\n");

      const directory = FileSystem.cacheDirectory ?? FileSystem.documentDirectory;
      if (!directory) {
        throw new Error("No writable directory available");
      }

      const accountSlug = toSafeAccountSlug(account.name) || "account";
      const timestamp = formatExportTimestamp();
      const fileUri = `${directory}nsfinance-${accountSlug}-transactions-${timestamp}.csv`;
      await FileSystem.writeAsStringAsync(fileUri, csvText, {
        encoding: FileSystem.EncodingType.UTF8
      });

      if (await Sharing.isAvailableAsync()) {
        await Sharing.shareAsync(fileUri, {
          mimeType: "text/csv",
          dialogTitle: "Export transactions"
        });
      } else {
        Alert.alert("CSV exported", `Saved to ${fileUri}`);
      }
    } catch (caughtError) {
      const message = caughtError instanceof Error ? caughtError.message : "Unknown export error";
      Alert.alert("Export failed", message);
    }
  };

  return (
    <ScreenContainer scrollable={false} contentStyle={styles.content}>
      <View style={styles.headerTopBar}>
        <HeaderShell preset="secondaryDetail" title="Account details" />
      </View>

      <ScrollView
        contentContainerStyle={[styles.scrollContent, { paddingBottom: listBottomInset }]}
        showsVerticalScrollIndicator={false}
        bounces={false}
      >
        {isInitialLoading ? (
          <View style={styles.loadingWrap}>
            <SkeletonBlock style={{ height: 168, borderRadius: 6 }} />
            <SkeletonBlock style={{ height: 130, borderRadius: 6 }} />
            <SkeletonBlock style={{ height: 130, borderRadius: 6 }} />
          </View>
        ) : error ? (
          <ErrorState
            title="Could not load account"
            message={error.message}
            onRetry={() => {
              void Promise.all([accountQuery.refetch(), transactionsQuery.refetch()]);
            }}
          />
        ) : account ? (
          <>
            <GlassCard style={styles.accountCard}>
            <Text style={styles.accountName}>{account.name}</Text>
            <Text style={styles.accountBalance}>
              {formatCurrency(account.currentBalance, account.currency)}
            </Text>
              <View style={styles.accountMetaWrap}>
                <Text style={styles.accountMeta}>Account ID: {account.id}</Text>
                <Text style={styles.accountMeta}>Type: {account.type}</Text>
                <Text style={styles.accountMeta}>Currency: {account.currency}</Text>
                <Text style={styles.accountMeta}>Created: {formatDate(account.createdUtc)}</Text>
                <Text style={styles.accountMeta}>
                  Connected bank: {latestConnection?.providerDisplayName ?? "Not linked yet"}
                </Text>
                <Text style={styles.accountMeta}>
                  Connection provider: {latestConnection?.provider ?? "TrueLayer"}
                </Text>
                <Text style={styles.accountMeta}>
                  Last synced at: {formatDateTime(latestConnection?.lastSuccessfulSyncUtc ?? latestConnection?.lastSyncAttemptedUtc)}
                </Text>
              </View>
            </GlassCard>

            <View style={styles.primaryActions}>
              <PrimaryButton label="Export to CSV" onPress={() => void exportCsv()} />
              <PrimaryButton
                label="Get Help"
                onPress={() => router.push("/(tabs)/accounts/support")}
              />
            </View>

            <SectionHeader title="Recent transactions" />
            <View style={styles.transactionsWrap}>
              {transactions.length > 0 ? (
                transactions.slice(0, 12).map((transaction, index) => (
                  <TransactionRow
                    key={transaction.id}
                    transaction={transaction}
                    index={index}
                    onPress={() => router.push(`/(tabs)/activity/${transaction.id}` as never)}
                  />
                ))
              ) : (
                <EmptyState
                  title="No transactions yet"
                  message="Add a transaction to this account to populate the export and details view."
                />
              )}
            </View>
          </>
        ) : null}
      </ScrollView>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding,
    paddingBottom: 0
  },
  headerTopBar: {
    marginBottom: spacing[16],
    backgroundColor: "transparent",
    zIndex: 20,
    elevation: 20
  },
  scrollContent: {
    gap: spacing[16]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  accountCard: {
    gap: spacing[12]
  },
  accountName: {
    color: palette.textPrimary,
    ...typography.title1
  },
  accountBalance: {
    color: palette.textPrimary,
    ...typography.displayL,
    fontVariant: ["tabular-nums"]
  },
  accountMetaWrap: {
    gap: spacing[4]
  },
  accountMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  primaryActions: {
    gap: spacing[12]
  },
  transactionsWrap: {
    gap: spacing[12]
  },
  loadingWrap: {
    gap: spacing[12]
  }
});

