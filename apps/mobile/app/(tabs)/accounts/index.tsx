import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { AnimatedCurrencyText } from "../../../src/components/ui/AnimatedCurrencyText";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SectionHeader } from "../../../src/components/ui/SectionHeader";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { useAccountsQuery } from "../../../src/features/accounts/useAccounts";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { formatCurrency, formatMonthYear } from "../../../src/lib/format";
import { getFloatingTabBarContentInset } from "../../../src/theme/insets";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

type MonthRange = {
  startMonth: number;
  startYear: number;
  endMonth: number;
  endYear: number;
};

type RangeTarget = "primary" | "secondary";
type RangeStep = "start" | "end";

const monthNames = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec"
];

const defaultRanges = () => {
  const now = new Date();
  const thisMonth = now.getMonth();
  const thisYear = now.getFullYear();
  const prev = new Date(thisYear, thisMonth - 1, 1);

  return {
    primary: {
      startMonth: thisMonth,
      startYear: thisYear,
      endMonth: thisMonth,
      endYear: thisYear
    } as MonthRange,
    secondary: {
      startMonth: prev.getMonth(),
      startYear: prev.getFullYear(),
      endMonth: prev.getMonth(),
      endYear: prev.getFullYear()
    } as MonthRange
  };
};

function monthRangeToDates(range: MonthRange) {
  const start = new Date(range.startYear, range.startMonth, 1, 0, 0, 0, 0);
  const end = new Date(range.endYear, range.endMonth + 1, 0, 23, 59, 59, 999);
  return { start, end };
}

function rangeLabel(range: MonthRange) {
  const start = new Date(range.startYear, range.startMonth, 1);
  const end = new Date(range.endYear, range.endMonth, 1);

  const startLabel = formatMonthYear(start);
  const endLabel = formatMonthYear(end);
  if (startLabel === endLabel) {
    return startLabel;
  }

  return `${startLabel} - ${endLabel}`;
}

function computeRangeSpend(transactions: { bookedAtUtc: string; amount: number }[], range: MonthRange) {
  const { start, end } = monthRangeToDates(range);

  return Math.abs(
    transactions
      .filter((item) => {
        const bookedAt = new Date(item.bookedAtUtc);
        return bookedAt >= start && bookedAt <= end && item.amount < 0;
      })
      .reduce((sum, item) => sum + item.amount, 0)
  );
}

function monthYearOptions() {
  const current = new Date().getFullYear();
  return Array.from({ length: 8 }, (_, index) => current - 5 + index);
}

export default function AccountsTabScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const accountsQuery = useAccountsQuery();
  const [selectedAccountId, setSelectedAccountId] = useState("");
  const [selectorVisible, setSelectorVisible] = useState(false);
  const [rangeModalVisible, setRangeModalVisible] = useState(false);
  const [rangeTarget, setRangeTarget] = useState<RangeTarget>("primary");
  const [rangeStep, setRangeStep] = useState<RangeStep>("start");
  const [ranges, setRanges] = useState(defaultRanges());

  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);
  const selectedAccount =
    accounts.find((item) => item.id === selectedAccountId) ?? accounts[0] ?? null;

  const accountTransactionsQuery = useTransactionsQuery(selectedAccount?.id);

  const isInitialLoading = accountsQuery.isLoading && !accountsQuery.data;
  const recentActivity = useMemo(
    () => (accountTransactionsQuery.data ?? []).slice(0, 5),
    [accountTransactionsQuery.data]
  );

  const comparison = useMemo(() => {
    const transactions = accountTransactionsQuery.data ?? [];
    const primary = computeRangeSpend(transactions, ranges.primary);
    const secondary = computeRangeSpend(transactions, ranges.secondary);
    const delta = primary - secondary;

    return {
      primary,
      secondary,
      delta
    } as const;
  }, [accountTransactionsQuery.data, ranges]);

  const years = useMemo(() => monthYearOptions(), []);

  useEffect(() => {
    if (!selectedAccountId && accounts.length > 0) {
      setSelectedAccountId(accounts[0].id);
    }
  }, [accounts, selectedAccountId]);

  useEffect(() => {
    if (!selectedAccount?.id) {
      return;
    }

    setRanges(defaultRanges());
  }, [selectedAccount?.id]);

  const listBottomInset = Math.max(
    spacing[8],
    getFloatingTabBarContentInset(insets.bottom, spacing[8])
  );

  const openRangeEditor = (target: RangeTarget) => {
    setRangeTarget(target);
    setRangeStep("start");
    setRangeModalVisible(true);
  };

  const handleYearChange = (year: number) => {
    setRanges((current) => {
      const next = { ...current };
      if (rangeStep === "start") {
        next[rangeTarget] = {
          ...next[rangeTarget],
          startYear: year
        };
      } else {
        next[rangeTarget] = {
          ...next[rangeTarget],
          endYear: year
        };
      }

      return next;
    });
  };

  const handleMonthChange = (month: number) => {
    setRanges((current) => {
      const next = { ...current };
      if (rangeStep === "start") {
        next[rangeTarget] = {
          ...next[rangeTarget],
          startMonth: month
        };
      } else {
        next[rangeTarget] = {
          ...next[rangeTarget],
          endMonth: month
        };
      }

      return next;
    });

    if (rangeStep === "start") {
      setRangeStep("end");
      return;
    }

    setRangeModalVisible(false);
  };

  const activeRange = ranges[rangeTarget];
  const selectedYear = rangeStep === "start" ? activeRange.startYear : activeRange.endYear;
  const comparisonSummary =
    Math.abs(comparison.delta) < 0.01
      ? "You spent the same amount in both selected periods."
      : comparison.delta > 0
        ? `You have spent ${formatCurrency(comparison.delta, selectedAccount?.currency ?? "EUR")} more in the selected primary period than in the comparison period.`
        : `You have spent ${formatCurrency(Math.abs(comparison.delta), selectedAccount?.currency ?? "EUR")} less in the selected primary period than in the comparison period.`;

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
    >
      {isInitialLoading ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={{ height: 54, borderRadius: 14 }} />
          <SkeletonBlock style={{ height: 156, borderRadius: 18 }} />
          <SkeletonBlock style={{ height: 150, borderRadius: 18 }} />
        </View>
      ) : accountsQuery.isError ? (
        <ErrorState
          title="Could not load accounts"
          message={accountsQuery.error.message}
          onRetry={() => {
            void accountsQuery.refetch();
          }}
        />
      ) : !selectedAccount ? (
        <EmptyState
          title="No accounts created"
          message="Add an account to start tracking and comparing activity."
          actionLabel="Create account"
          onActionPress={() => router.push("/modals/add-account")}
        />
      ) : (
        <ScrollView
          contentContainerStyle={[styles.scrollContent, { paddingBottom: listBottomInset }]}
          showsVerticalScrollIndicator={false}
          bounces={false}
        >
          <Pressable style={styles.accountSelector} onPress={() => setSelectorVisible(true)}>
            <Text style={styles.accountSelectorText}>{selectedAccount.name}</Text>
            <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
          </Pressable>

          <GlassCard style={styles.heroCard}>
            <Text style={styles.heroType}>{selectedAccount.type} account</Text>
            <AnimatedCurrencyText
              value={selectedAccount.currentBalance}
              currency={selectedAccount.currency}
              style={styles.heroBalance}
              baseColor={palette.textPrimary}
            />
            <Text style={styles.heroMeta}>{selectedAccount.currency}</Text>
          </GlassCard>

          <View style={styles.actionGrid}>
            <ActionItem
              label="Connect Bank"
              icon="link-outline"
              onPress={() => Alert.alert("Connect Bank", "Bank linking entry point placeholder.")}
            />
            <ActionItem
              label="Move"
              icon="swap-horizontal-outline"
              onPress={() =>
                Alert.alert("Move", "Transfer flow placeholder for upcoming bank/deeplink logic.")
              }
            />
            <ActionItem
              label="Details"
              icon="information-circle-outline"
              onPress={() =>
                router.push({
                  pathname: "/(tabs)/accounts/[id]",
                  params: { id: selectedAccount.id }
                })
              }
            />
            <ActionItem
              label="Get Help"
              icon="help-circle-outline"
              onPress={() => router.push("/(tabs)/accounts/support")}
            />
          </View>

          <SectionHeader
            title="Monthly comparison"
            actionLabel="Update ranges"
            onActionPress={() => openRangeEditor("primary")}
          />
          <GlassCard style={styles.comparisonCard}>
            <View style={styles.comparisonRangeRow}>
              <Pressable style={styles.rangeButton} onPress={() => openRangeEditor("primary")}>
                <Text style={styles.rangeLabel}>Primary range</Text>
                <Text style={styles.rangeValue}>{rangeLabel(ranges.primary)}</Text>
              </Pressable>
              <Pressable style={styles.rangeButton} onPress={() => openRangeEditor("secondary")}>
                <Text style={styles.rangeLabel}>Compare against</Text>
                <Text style={styles.rangeValue}>{rangeLabel(ranges.secondary)}</Text>
              </Pressable>
            </View>
            <View style={styles.comparisonValues}>
              <View style={styles.comparisonValueBlock}>
                <AnimatedCurrencyText
                  value={-comparison.primary}
                  currency={selectedAccount.currency}
                  style={styles.comparisonAmount}
                  baseColor={palette.textPrimary}
                />
                <Text style={styles.comparisonMeta}>Primary period spend</Text>
                <Text style={styles.comparisonRangeLabel}>{rangeLabel(ranges.primary)}</Text>
              </View>
              <View style={styles.comparisonValueBlock}>
                <AnimatedCurrencyText
                  value={-comparison.secondary}
                  currency={selectedAccount.currency}
                  style={styles.comparisonAmount}
                  baseColor={palette.textPrimary}
                />
                <Text style={styles.comparisonMeta}>Comparison period spend</Text>
                <Text style={styles.comparisonRangeLabel}>{rangeLabel(ranges.secondary)}</Text>
              </View>
            </View>
            <Text style={styles.comparisonSummary}>{comparisonSummary}</Text>
          </GlassCard>

          <SectionHeader
            title="Recent activity"
            actionLabel="Open feed"
            onActionPress={() => router.push("/(tabs)/activity")}
          />
          <View style={styles.recentWrap}>
            {recentActivity.length > 0 ? (
              recentActivity.map((transaction, index) => (
                <TransactionRow
                  key={transaction.id}
                  transaction={transaction}
                  index={index}
                  onPress={() =>
                    router.push({
                      pathname: "/(tabs)/activity",
                      params: {
                        focusTransactionId: transaction.id,
                        focusNonce: Date.now().toString()
                      }
                    })
                  }
                />
              ))
            ) : (
              <EmptyState
                title="No account activity yet"
                message="Transactions for this account will appear here."
              />
            )}
          </View>
        </ScrollView>
      )}

      <Modal
        visible={selectorVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setSelectorVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setSelectorVisible(false)}>
          <Pressable style={styles.modalSheet} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Select account</Text>
            <ScrollView contentContainerStyle={styles.modalList} showsVerticalScrollIndicator={false}>
              {accounts.map((account) => (
                <Pressable
                  key={account.id}
                  style={({ pressed }) => [
                    styles.modalItem,
                    selectedAccount?.id === account.id ? styles.modalItemActive : null,
                    pressed ? styles.modalItemPressed : null
                  ]}
                  onPress={() => {
                    setSelectedAccountId(account.id);
                    setSelectorVisible(false);
                  }}
                >
                  <Text style={styles.modalItemTitle}>{account.name}</Text>
                  <Text style={styles.modalItemMeta}>
                    {account.type} | {formatCurrency(account.currentBalance, account.currency)}
                  </Text>
                </Pressable>
              ))}

              <View style={styles.modalDivider} />
              <Pressable
                style={({ pressed }) => [styles.createAccountItem, pressed ? styles.modalItemPressed : null]}
                onPress={() => {
                  setSelectorVisible(false);
                  router.push("/modals/add-account");
                }}
              >
                <View style={styles.createAccountTextWrap}>
                  <Text style={styles.createAccountTitle}>Create an account</Text>
                  <Text style={styles.createAccountBody}>Create a new account</Text>
                </View>
                <Ionicons name="add-circle-outline" size={20} color={palette.accent} />
              </Pressable>
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>

      <Modal
        visible={rangeModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setRangeModalVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setRangeModalVisible(false)}>
          <Pressable style={styles.modalSheet} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Pick comparison ranges</Text>
            <Text style={styles.rangeInstructionLabel}>
              {rangeTarget === "primary" ? "Primary range" : "Comparison range"}
            </Text>
            <Text style={styles.rangeInstructionText}>
              {rangeStep === "start" ? "Pick the start date" : "Pick the end date"}
            </Text>

            <Text style={styles.editorLabel}>Year</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.yearRow}>
              {years.map((year) => (
                <Pressable
                  key={year}
                  style={[styles.yearChip, selectedYear === year ? styles.yearChipActive : null]}
                  onPress={() => {
                    handleYearChange(year);
                  }}
                >
                  <Text style={styles.yearChipText}>{year}</Text>
                </Pressable>
              ))}
            </ScrollView>

            <Text style={styles.editorLabel}>Month</Text>
            <View style={styles.monthGrid}>
              {monthNames.map((month, index) => {
                const selectedMonth = rangeStep === "start" ? activeRange.startMonth : activeRange.endMonth;
                return (
                  <Pressable
                    key={month}
                    style={[styles.monthChip, selectedMonth === index ? styles.monthChipActive : null]}
                    onPress={() => handleMonthChange(index)}
                  >
                    <Text style={styles.monthChipText}>{month}</Text>
                  </Pressable>
                );
              })}
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </ScreenContainer>
  );
}

function ActionItem({
  label,
  icon,
  onPress
}: {
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
  onPress: () => void;
}) {
  return (
    <Pressable onPress={onPress} style={({ pressed }) => [styles.actionItem, pressed ? styles.actionItemPressed : null]}>
      <Ionicons name={icon} size={18} color={palette.accent} />
      <Text style={styles.actionText}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding,
    paddingBottom: 0
  },
  scrollContent: {
    gap: spacing[16]
  },
  accountSelector: {
    minHeight: 46,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: spacing[12]
  },
  accountSelectorText: {
    color: palette.textPrimary,
    ...typography.title2
  },
  heroCard: {
    gap: spacing[8]
  },
  heroType: {
    color: palette.textSecondary,
    ...typography.caption
  },
  heroBalance: {
    color: palette.textPrimary,
    ...typography.displayL
  },
  heroMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  actionGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  actionItem: {
    width: "48.7%",
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    minHeight: 52,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  actionItemPressed: {
    opacity: 0.88
  },
  actionText: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  comparisonCard: {
    gap: spacing[12]
  },
  comparisonRangeRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  rangeButton: {
    flex: 1,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.78)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[4]
  },
  rangeLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rangeValue: {
    color: palette.textPrimary,
    ...typography.body2
  },
  comparisonValues: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  comparisonValueBlock: {
    flex: 1,
    alignItems: "center",
    gap: spacing[4]
  },
  comparisonAmount: {
    color: palette.textPrimary,
    ...typography.title2,
    fontWeight: "700",
    textAlign: "center"
  },
  comparisonMeta: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "center"
  },
  comparisonRangeLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "center"
  },
  comparisonSummary: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  recentWrap: {
    gap: spacing[12]
  },
  loadingWrap: {
    gap: spacing[12]
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.74)",
    justifyContent: "flex-end"
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    padding: spacing[16],
    gap: spacing[12],
    maxHeight: "80%"
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  modalList: {
    gap: spacing[8],
    paddingBottom: spacing[8]
  },
  modalDivider: {
    height: 1,
    backgroundColor: "rgba(220,232,255,0.12)",
    marginVertical: spacing[4]
  },
  modalItem: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.75)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[4]
  },
  modalItemActive: {
    borderColor: palette.primaryGlow
  },
  modalItemPressed: {
    opacity: 0.88
  },
  modalItemTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  modalItemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  createAccountItem: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.75)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  createAccountTextWrap: {
    flex: 1,
    gap: spacing[4]
  },
  createAccountTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  createAccountBody: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rangeInstructionLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rangeInstructionText: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  editorLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  yearRow: {
    gap: spacing[8]
  },
  yearChip: {
    borderRadius: 999,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8]
  },
  yearChipActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.28)"
  },
  yearChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  monthGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  monthChip: {
    width: "23%",
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    alignItems: "center",
    justifyContent: "center",
    minHeight: 36
  },
  monthChipActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.28)"
  },
  monthChipText: {
    color: palette.textPrimary,
    ...typography.caption
  }
});
