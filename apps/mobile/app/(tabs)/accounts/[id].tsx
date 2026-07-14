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
import { resolveAccountBalancePresentation } from "../../../src/features/accounts/accountBalancePresentation";
import {
  useBankConnectionsQuery,
  useLinkedBankAccountsQuery,
  useLinkedBankCardsQuery
} from "../../../src/features/banking/useBanking";
import { useCreateExportRequestMutation } from "../../../src/features/support/useSupport";
import { downloadExportRequestFile } from "../../../src/features/support/supportApi";
import { useTransactionPageQuery } from "../../../src/features/transactions/useTransactionPageQuery";
import { formatCurrency } from "../../../src/lib/format";
import { getFloatingTabBarContentInset } from "../../../src/theme/insets";
import { layout, palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type { LinkedBankAccountDto } from "../../../src/types/api";

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

function extractAccountNumberLines(metadataJson?: string | null) {
  if (!metadataJson) {
    return [] as { label: string; value: string }[];
  }

  try {
    const parsed = JSON.parse(metadataJson) as Record<string, unknown>;
    const lines: { label: string; value: string }[] = [];
    const read = (key: string) => {
      const value = parsed[key];
      return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
    };

    const iban = read("iban");
    if (iban) {
      lines.push({ label: "IBAN", value: formatIban(iban) });
    }

    const number = read("number");
    if (number) {
      lines.push({ label: "Account number", value: number });
    }

    const sortCode = read("sort_code");
    if (sortCode) {
      lines.push({ label: "Sort code", value: sortCode });
    }

    const bic = read("swift_bic") ?? read("bic");
    if (bic) {
      lines.push({ label: "BIC/SWIFT", value: bic });
    }

    return lines;
  } catch {
    return [];
  }
}

function normalizeText(value?: string | null) {
  if (!value) {
    return null;
  }

  const normalized = value.trim().replace(/\s+/g, " ");
  return normalized.length > 0 ? normalized : null;
}

function looksLikeConnectedIdentity(candidate: string, connectedFullName?: string | null) {
  const normalizedConnected = normalizeText(connectedFullName);
  if (!normalizedConnected) {
    return false;
  }

  const tokenize = (value: string) =>
    value
      .toLowerCase()
      .split(" ")
      .map((token) => token.trim())
      .filter((token) => token.length > 0)
      .sort();

  const candidateTokens = tokenize(candidate);
  const connectedTokens = tokenize(normalizedConnected);
  if (candidateTokens.length < 2 || candidateTokens.length !== connectedTokens.length) {
    return false;
  }

  return candidateTokens.every((token, index) => token === connectedTokens[index]);
}

function buildAccountFallback(accountType?: string | null, currency?: string | null) {
  const normalizedType = accountType?.trim().toLowerCase();
  const friendlyType =
    normalizedType === "transaction" || normalizedType === "current" || normalizedType === "checking"
      ? "current account"
      : normalizedType === "savings"
        ? "savings account"
        : normalizedType === "credit"
          ? "credit account"
          : normalizedType === "loan"
            ? "loan account"
            : "account";

  const resolvedCurrency = normalizeText(currency)?.toUpperCase() ?? "EUR";
  return `${resolvedCurrency} ${friendlyType}`;
}

function resolveAccountDisplayTitle(
  linkedAccount: LinkedBankAccountDto | null,
  accountName?: string | null,
  connectedFullName?: string | null
) {
  const linkedDisplayName = normalizeText(linkedAccount?.displayName);
  if (linkedDisplayName && !looksLikeConnectedIdentity(linkedDisplayName, connectedFullName)) {
    return linkedDisplayName;
  }

  const accountDisplayName = normalizeText(accountName);
  if (accountDisplayName && !looksLikeConnectedIdentity(accountDisplayName, connectedFullName)) {
    return accountDisplayName;
  }

  return buildAccountFallback(linkedAccount?.accountType, linkedAccount?.currency);
}

function formatIban(value: string) {
  const compact = value.replace(/\s+/g, "").toUpperCase();
  if (compact.length <= 4) {
    return compact;
  }

  const groups = compact.match(/.{1,4}/g);
  return groups ? groups.join(" ") : compact;
}

function findLinkedAccountForFinancialAccount(
  linkedAccounts: LinkedBankAccountDto[] | undefined,
  financialAccountId: string | undefined
) {
  if (!linkedAccounts || !financialAccountId) {
    return null;
  }

  return linkedAccounts.find((item) => item.financialAccountId === financialAccountId) ?? null;
}

export default function AccountDetailsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const params = useLocalSearchParams<{ id?: string }>();
  const accountId = params.id ?? "";

  const accountQuery = useAccountDetailQuery(accountId);
  const transactionsQuery = useTransactionPageQuery({ accountId, pageSize: 12 });
  const connectionsQuery = useBankConnectionsQuery();
  const linkedAccountsQuery = useLinkedBankAccountsQuery();
  const linkedCardsQuery = useLinkedBankCardsQuery();
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

  const error =
    accountQuery.error ??
    transactionsQuery.error ??
    connectionsQuery.error ??
    linkedAccountsQuery.error ??
    linkedCardsQuery.error;
  const account = accountQuery.data;
  const balance = account ? resolveAccountBalancePresentation(account) : null;
  const linkedAccount = findLinkedAccountForFinancialAccount(linkedAccountsQuery.data, account?.id);
  const accountConnection = linkedAccount
    ? (connectionsQuery.data ?? []).find((item) => item.id === linkedAccount.connectionId) ?? latestConnection
    : latestConnection;
  const relatedCards = linkedAccount
    ? (linkedCardsQuery.data ?? []).filter((card) => {
      if (card.connectionId !== linkedAccount.connectionId) {
        return false;
      }

      if (!card.providerAccountId || !linkedAccount.providerAccountId) {
        return true;
      }

      return card.providerAccountId === linkedAccount.providerAccountId;
    })
    : [];
  const accountNumberLines = extractAccountNumberLines(linkedAccount?.accountNumberMetadataJson);
  const accountTitle = resolveAccountDisplayTitle(
    linkedAccount,
    account?.name,
    accountConnection?.connectedFullName
  );
  const transactions = transactionsQuery.data?.items ?? [];
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
          "Your export was downloaded to app storage. Use a custom Android build for Download Manager notifications."
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
              <Text style={styles.accountName}>{accountTitle}</Text>
              <Text style={styles.accountBalance}>
                {balance?.current === null
                  ? "Balance unavailable"
                  : formatCurrency(balance?.current ?? account.currentBalance, balance?.currency ?? account.currency)}
              </Text>
              <View style={styles.accountMetaWrap}>
                <Text style={styles.sectionTitle}>Account info</Text>
                <Text style={styles.accountMeta}>Type: {account.type}</Text>
                <Text style={styles.accountMeta}>Currency: {account.currency}</Text>
                {balance?.available !== null && balance?.available !== undefined ? (
                  <Text style={styles.accountMeta}>
                    Available: {formatCurrency(balance.available, balance.currency)}
                  </Text>
                ) : null}
                {balance?.overdraft !== null && balance?.overdraft !== undefined ? (
                  <Text style={styles.accountMeta}>
                    Overdraft: {formatCurrency(balance.overdraft, balance.currency)}
                  </Text>
                ) : null}
                <Text style={styles.accountMeta}>
                  Balance source: {balance?.source === "provider_snapshot"
                    ? "Bank snapshot"
                    : balance?.source === "manual_ledger"
                      ? "Account activity"
                      : balance?.source === "legacy_current_balance"
                        ? "Previous API response"
                        : "Unavailable"}
                </Text>
                {balance?.asOf ? (
                  <Text style={styles.accountMeta}>Balance updated: {formatDateTime(balance.asOf)}</Text>
                ) : null}
                {balance?.freshness === "stale" ? (
                  <Text style={styles.accountMeta}>Balance may be out of date</Text>
                ) : null}
                {accountNumberLines.map((line) => (
                  <Text key={line.label} style={styles.accountMeta}>
                    {line.label}: {line.value}
                  </Text>
                ))}
                <Text style={[styles.sectionTitle, styles.connectionSectionTitle]}>Connection info</Text>
                {accountConnection?.connectedFullName ? (
                  <Text style={styles.accountMeta}>Connected as: {accountConnection.connectedFullName}</Text>
                ) : null}
                <Text style={styles.accountMeta}>
                  Connected bank: {accountConnection?.providerDisplayName ?? "Not linked yet"}
                </Text>
                <Text style={styles.accountMeta}>
                  Connection provider: {accountConnection?.provider ?? "TrueLayer"}
                </Text>
                <Text style={styles.accountMeta}>
                  Last synced at: {formatDateTime(accountConnection?.lastSuccessfulSyncUtc ?? accountConnection?.lastSyncAttemptedUtc)}
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
            {relatedCards.length > 0 ? (
              <GlassCard style={styles.recurringCard}>
                <Text style={styles.sectionTitle}>Linked cards</Text>
                {relatedCards.map((card) => (
                  <View key={card.id} style={styles.recurringRow}>
                    <Text style={styles.recurringTitle}>{card.displayName}</Text>
                    <Text style={styles.accountMeta}>
                      {[card.cardNetwork, card.cardType, card.cardNumberLastFour ? `**** ${card.cardNumberLastFour}` : null]
                        .filter(Boolean)
                        .join(" | ")}
                    </Text>
                    <Text style={styles.accountMeta}>
                      Balance:{" "}
                      {card.latestCurrent !== null
                        ? formatCurrency(card.latestCurrent, card.currency)
                        : "Unavailable"}
                    </Text>
                  </View>
                ))}
              </GlassCard>
            ) : null}

            <SectionHeader title="Recent transactions" />
            <View style={styles.transactionsWrap}>
              {transactions.length > 0 ? (
                transactions.map((transaction, index) => (
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
    ...typography.bodyStrong
  },
  accountBalance: {
    color: palette.textPrimary,
    ...typography.amount,
    fontVariant: ["tabular-nums"]
  },
  accountMetaWrap: {
    gap: spacing[4]
  },
  accountMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  connectionSectionTitle: {
    marginTop: spacing[8]
  },
  recurringCard: {
    gap: spacing[8]
  },
  recurringRow: {
    gap: spacing[2]
  },
  recurringTitle: {
    color: palette.textPrimary,
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


