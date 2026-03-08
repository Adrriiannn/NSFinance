import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { Alert, StyleSheet, Text, View } from "react-native";
import * as FileSystem from "expo-file-system/legacy";
import * as Sharing from "expo-sharing";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SectionHeader } from "../../../src/components/ui/SectionHeader";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { useAccountDetailQuery } from "../../../src/features/accounts/useAccounts";
import { useAccountTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { formatDate } from "../../../src/lib/format";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
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
  const params = useLocalSearchParams<{ id?: string }>();
  const accountId = params.id ?? "";
  const plannerStore = usePlannerStore();

  const accountQuery = useAccountDetailQuery(accountId);
  const transactionsQuery = useAccountTransactionsQuery(accountId);

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
    (transactionsQuery.isLoading && !transactionsQuery.data);

  const error = accountQuery.error ?? transactionsQuery.error;
  const account = accountQuery.data;
  const transactions = transactionsQuery.data ?? [];

  const exportCsv = async () => {
    if (!account) {
      return;
    }

    try {
      const header = "Date,Time,Transaction,Account,Category,Importance,Amount,Currency,Notes";
      const rows = transactions.map((item) => {
        const annotation = plannerStore.annotations[item.id];
        const category =
          annotation?.category ??
          item.categoryName ??
          (item.direction === "Income" ? "Income" : "Uncategorized");
        const importance = item.direction === "Expense" ? (annotation?.type ?? "") : "";
        const amount = Number(item.amount).toFixed(2);
        const notes = annotation?.notes ?? "";

        return [
          csvCell(formatUtcDate(item.bookedAtUtc)),
          csvCell(formatUtcTime(item.bookedAtUtc)),
          csvCell(item.description),
          csvCell(item.accountName),
          csvCell(category),
          csvCell(importance),
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
      const fileUri = `${directory}nsfintech-${accountSlug}-transactions-${timestamp}.csv`;
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
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Account details</Text>
        <View style={{ width: 42 }} />
      </View>

      {isInitialLoading ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={{ height: 168, borderRadius: 18 }} />
          <SkeletonBlock style={{ height: 130, borderRadius: 18 }} />
          <SkeletonBlock style={{ height: 130, borderRadius: 18 }} />
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
            <Text style={styles.accountBalance}>{account.currentBalance.toFixed(2)} {account.currency}</Text>
            <View style={styles.accountMetaWrap}>
              <Text style={styles.accountMeta}>Account ID: {account.id}</Text>
              <Text style={styles.accountMeta}>Type: {account.type}</Text>
              <Text style={styles.accountMeta}>Currency: {account.currency}</Text>
              <Text style={styles.accountMeta}>Created: {formatDate(account.createdUtc)}</Text>
              <Text style={styles.accountMeta}>Connected bank: Not linked yet</Text>
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
                  onPress={() =>
                    router.push({
                      pathname: "/modals/transaction-context",
                      params: { transactionId: transaction.id }
                    })
                  }
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
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding
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

