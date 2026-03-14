import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Animated,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { CheckSpendingsCard } from "../../../src/components/accounts/CheckSpendingsCard";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { AnimatedCurrencyText } from "../../../src/components/ui/AnimatedCurrencyText";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { useMainTabSwipeNavigation } from "../../../src/components/layout/useHorizontalSiblingSwipe";
import { SectionHeader } from "../../../src/components/ui/SectionHeader";
import { SelectField } from "../../../src/components/ui/SelectField";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { TabEmptyStateCard } from "../../../src/components/ui/TabEmptyStateCard";
import { TextField } from "../../../src/components/ui/TextField";
import {
  useAccountsQuery,
  useDeleteAccountMutation,
  useUpdateAccountMutation
} from "../../../src/features/accounts/useAccounts";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { formatCurrency } from "../../../src/lib/format";
import { getFloatingTabBarContentInset } from "../../../src/theme/insets";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";
import type { AccountType } from "../../../src/types/api";

const accountTypeOptions: { label: string; value: AccountType }[] = [
  { label: "Current", value: "Current" },
  { label: "Savings", value: "Savings" },
  { label: "Credit", value: "Credit" },
  { label: "Cash", value: "Cash" },
  { label: "Other", value: "Other" }
];


export default function AccountsTabScreen() {
  const router = useRouter();
  const { gestureHandlers, animatedStyle } = useMainTabSwipeNavigation("/(tabs)/accounts");
  const params = useLocalSearchParams<{ selectedAccountId?: string; focusNonce?: string }>();
  const insets = useSafeAreaInsets();
  const accountsQuery = useAccountsQuery();
  const updateAccountMutation = useUpdateAccountMutation();
  const deleteAccountMutation = useDeleteAccountMutation();
  const handledSelectedAccountRef = useRef<string | null>(null);
  const [selectedAccountId, setSelectedAccountId] = useState("");
  const [selectorVisible, setSelectorVisible] = useState(false);
  const [editModalVisible, setEditModalVisible] = useState(false);
  const [editedName, setEditedName] = useState("");
  const [editedType, setEditedType] = useState<AccountType>("Current");

  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);
  const requestedSelectedAccountId =
    typeof params.selectedAccountId === "string" ? params.selectedAccountId : "";
  const focusNonce = typeof params.focusNonce === "string" ? params.focusNonce : "";
  const focusKey = requestedSelectedAccountId
    ? `${requestedSelectedAccountId}:${focusNonce}`
    : "";
  const selectedAccount =
    accounts.find((item) => item.id === selectedAccountId) ?? accounts[0] ?? null;

  const accountTransactionsQuery = useTransactionsQuery(selectedAccount?.id);
  const isInitialLoading = accountsQuery.isLoading && !accountsQuery.data;
  const recentActivity = useMemo(
    () => (accountTransactionsQuery.data ?? []).slice(0, 5),
    [accountTransactionsQuery.data]
  );

  useEffect(() => {
    if (!selectedAccountId && accounts.length > 0) {
      setSelectedAccountId(accounts[0].id);
    }
  }, [accounts, selectedAccountId]);

  useEffect(() => {
    if (!requestedSelectedAccountId || handledSelectedAccountRef.current === focusKey) {
      return;
    }

    if (accounts.length === 0) {
      return;
    }

    handledSelectedAccountRef.current = focusKey;
    if (accounts.some((item) => item.id === requestedSelectedAccountId)) {
      setSelectedAccountId(requestedSelectedAccountId);
    }
  }, [accounts, focusKey, requestedSelectedAccountId]);

  const listBottomInset = Math.max(
    spacing[8],
    getFloatingTabBarContentInset(insets.bottom, spacing[8])
  );

  const openEditModal = (accountToEdit?: (typeof accounts)[number] | null) => {
    const target = accountToEdit ?? selectedAccount;
    if (!target) {
      return;
    }

    setSelectedAccountId(target.id);
    setEditedName(target.name);
    setEditedType(target.type);
    setEditModalVisible(true);
  };

  const openEditFromSelector = (account: (typeof accounts)[number]) => {
    setSelectorVisible(false);
    setTimeout(() => {
      openEditModal(account);
    }, 120);
  };

  const submitEdit = async () => {
    if (!selectedAccount || !editedName.trim()) {
      return;
    }

    await updateAccountMutation.mutateAsync({
      accountId: selectedAccount.id,
      payload: {
        name: editedName.trim(),
        type: editedType
      }
    });

    setEditModalVisible(false);
  };

  const confirmDelete = () => {
    if (!selectedAccount) {
      return;
    }

    Alert.alert(
      "Delete account?",
      "Deleting this account will remove all data associated with it.",
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Delete",
          style: "destructive",
          onPress: () => {
            void (async () => {
              await deleteAccountMutation.mutateAsync(selectedAccount.id);
              setEditModalVisible(false);
              setSelectedAccountId("");
            })();
          }
        }
      ]
    );
  };

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
      gestureHandlers={gestureHandlers}
    >
      <Animated.View style={[styles.tabStage, animatedStyle]}>
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
        <>
          <View style={styles.selectorTopBar}>
            <View style={styles.selectorRow}>
              <View style={styles.accountSelectorPlaceholder} />
              <View style={styles.selectorActions}>
                <Pressable
                  style={styles.companionButton}
                  onPress={() => router.push("/companion" as never)}
                >
                  <MaterialCommunityIcons name="robot-happy-outline" size={20} color="#4FE3D5" />
                </Pressable>
                <View style={styles.selectorRightSpacer} />
              </View>
            </View>
          </View>
          <TabEmptyStateCard
            title="No connected accounts"
            subtitle="Connect your bank to start tracking balances and spending."
            ctaLabel="Connect bank"
            onCtaPress={() => router.push("/modals/add-account")}
            verticalSpacingMode="tab-aligned"
          />
        </>
      ) : (
        <>
          <View style={styles.selectorTopBar}>
            <View style={styles.selectorRow}>
              <Pressable style={styles.accountSelector} onPress={() => setSelectorVisible(true)}>
                <Text style={styles.accountSelectorText} numberOfLines={1}>
                  {selectedAccount.name}
                </Text>
                <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
              </Pressable>
              <View style={styles.selectorActions}>
                <Pressable
                  style={styles.companionButton}
                  onPress={() => router.push("/companion" as never)}
                >
                  <MaterialCommunityIcons name="robot-happy-outline" size={20} color="#4FE3D5" />
                </Pressable>
                <View style={styles.selectorRightSpacer} />
              </View>
            </View>
          </View>

          <ScrollView
            contentContainerStyle={[styles.scrollContent, { paddingBottom: listBottomInset }]}
            showsVerticalScrollIndicator={false}
            bounces={false}
          >
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
                label="Send money"
                icon="link-outline"
                onPress={() => router.push("/modals/send-money")}
              />
              <ActionItem
                label="Move money"
                icon="swap-horizontal-outline"
                onPress={() => router.push("/modals/move-money")}
              />
              <ActionItem
                label="Details"
                icon="document-text-outline"
                onPress={() => router.push(`/(tabs)/accounts/${selectedAccount.id}` as never)}
              />
              <ActionItem
                label="Get Help"
                icon="help-circle-outline"
                onPress={() => router.push("/(tabs)/accounts/support")}
              />
            </View>

            <CheckSpendingsCard
              transactions={accountTransactionsQuery.data ?? []}
              currency={selectedAccount.currency}
            />

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
        </>
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
                <View
                  key={account.id}
                  style={styles.modalItemRow}
                >
                  <Pressable
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
                  <Pressable
                    onPress={() => openEditFromSelector(account)}
                    style={({ pressed }) => [
                      styles.modalItemEditButton,
                      pressed ? styles.modalItemPressed : null
                    ]}
                  >
                    <Text style={styles.modalItemEditText}>Edit</Text>
                  </Pressable>
                </View>
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
                  <Text style={styles.createAccountTitle}>Connect bank</Text>
                  <Text style={styles.createAccountBody}>Connect a financial institution</Text>
                </View>
                <Ionicons name="link-outline" size={20} color={palette.accent} />
              </Pressable>
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>

      <Modal
        visible={editModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setEditModalVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setEditModalVisible(false)}>
          <Pressable style={styles.modalSheet} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Edit account</Text>

            <TextField
              label="Account name"
              value={editedName}
              onChangeText={setEditedName}
              placeholder="Account name"
            />

            <SelectField
              label="Account type"
              value={editedType}
              options={accountTypeOptions}
              onChange={(value) => setEditedType(value as AccountType)}
            />

            <PrimaryButton
              label="Save changes"
              onPress={() => void submitEdit()}
              isLoading={updateAccountMutation.isPending}
              disabled={!editedName.trim()}
            />

            <Pressable
              onPress={confirmDelete}
              style={({ pressed }) => [
                styles.deleteButton,
                pressed ? styles.modalItemPressed : null
              ]}
            >
              <Text style={styles.deleteButtonText}>Delete account</Text>
            </Pressable>
            <Text style={styles.deleteWarning}>
              Deleting this account will remove all data associated with it.
            </Text>
          </Pressable>
        </Pressable>
      </Modal>

      </Animated.View>
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
  tabStage: {
    flex: 1
  },
  scrollContent: {
    gap: spacing[16]
  },
  selectorTopBar: {
    marginBottom: spacing[16],
    backgroundColor: "transparent",
    zIndex: 20,
    elevation: 20
  },
  selectorRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  accountSelector: {
    flexGrow: 1,
    flexShrink: 1,
    maxWidth: "74%",
    minHeight: 42,
    maxHeight: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: spacing[12]
  },
  accountSelectorPlaceholder: {
    flexGrow: 1,
    flexShrink: 1,
    maxWidth: "74%",
    minHeight: 42,
    maxHeight: 42
  },
  accountSelectorText: {
    flex: 1,
    marginRight: spacing[8],
    color: palette.textPrimary,
    ...typography.title2
  },
  selectorActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  companionButton: {
    width: 42,
    height: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center"
  },
  selectorRightSpacer: {
    width: 42,
    height: 42
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
    maxHeight: "84%"
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  modalList: {
    gap: spacing[8],
    paddingBottom: spacing[8]
  },
  modalItemRow: {
    flexDirection: "row",
    alignItems: "stretch",
    gap: spacing[8]
  },
  modalDivider: {
    height: 1,
    backgroundColor: "rgba(220,232,255,0.12)",
    marginVertical: spacing[4]
  },
  modalItem: {
    flex: 1,
    minHeight: 74,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.75)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    justifyContent: "center",
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
  modalItemEditButton: {
    minWidth: 68,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.92)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12],
    alignSelf: "stretch"
  },
  modalItemEditText: {
    color: palette.primaryGlow,
    ...typography.caption,
    fontWeight: "700"
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
  deleteButton: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "rgba(244,104,119,0.6)",
    backgroundColor: "rgba(90,16,30,0.45)",
    minHeight: 42,
    justifyContent: "center",
    alignItems: "center"
  },
  deleteButtonText: {
    color: palette.negative,
    ...typography.body2,
    fontWeight: "700"
  },
  deleteWarning: {
    color: palette.textSecondary,
    ...typography.caption
  },
  
});
