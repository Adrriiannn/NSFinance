import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo } from "react";
import { Alert, Platform, ScrollView, Text, View } from "react-native";
import * as Sharing from "expo-sharing";
import { useMutation } from "@tanstack/react-query";
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
import { useCreateExportRequestMutation } from "../../../src/features/support/useSupport";
import { downloadExportRequestFile } from "../../../src/features/support/supportApi";
import { useAccountTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { formatCurrency, formatDate } from "../../../src/lib/format";
import { getFloatingTabBarContentInset } from "../../../src/theme/insets";
import { layout, palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

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

export default function AccountDetailsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const params = useLocalSearchParams<{ id?: string }>();
  const accountId = params.id ?? "";

  const accountQuery = useAccountDetailQuery(accountId);
  const transactionsQuery = useAccountTransactionsQuery(accountId);
  const connectionsQuery = useBankConnectionsQuery();
  const createExportMutation = useCreateExportRequestMutation();
  const downloadExportMutation = useMutation({
    mutationFn: async (requestId: string) => downloadExportRequestFile(requestId)
  });
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

  const exportExcel = async () => {
    if (!account) {
      return;
    }

    try {
      const exportRequest = await createExportMutation.mutateAsync({
        notes: `User exported account-level statement for ${account.name}.`,
        format: "xlsx",
        financialAccountId: account.id
      });

      const downloadResult = await downloadExportMutation.mutateAsync(exportRequest.id);

      if (Platform.OS === "android" && downloadResult.usedAndroidDownloadManager) {
        Alert.alert(
          "Excel downloading",
          "Your export is downloading to your Downloads folder. You can open it from notifications or Files."
        );
      } else if (Platform.OS === "android") {
        Alert.alert(
          "Excel downloaded",
          "Your export was downloaded to app storage. Use a preview/dev build for Download Manager notifications."
        );
      } else if (await Sharing.isAvailableAsync()) {
        await Sharing.shareAsync(downloadResult.uri, {
          mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
          dialogTitle: "Export statements"
        });
      } else {
        Alert.alert("Excel exported", `Saved to ${downloadResult.uri}`);
      }
    } catch (caughtError) {
      const message = caughtError instanceof Error ? caughtError.message : "Unknown export error.";
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
              <PrimaryButton
                label="Export to Excel"
                onPress={() => void exportExcel()}
                isLoading={createExportMutation.isPending || downloadExportMutation.isPending}
              />
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

const styles = createRuntimeStyleSheet(() => ({
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
}));


