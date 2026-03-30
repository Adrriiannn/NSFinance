import { Ionicons } from "@expo/vector-icons";
import { Pressable, Text, View } from "react-native";
import { AccountProviderBadge } from "../../../components/accounts/AccountProviderBadge";
import { formatCurrency } from "../../../lib/format";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";
import type { ActivityAccountSuggestion } from "../search/activitySearch.types";

type ActivityAccountSuggestionListProps = {
  accounts: ActivityAccountSuggestion[];
  onSelect: (account: ActivityAccountSuggestion) => void;
};

export function ActivityAccountSuggestionList({
  accounts,
  onSelect
}: ActivityAccountSuggestionListProps) {
  if (accounts.length === 0) {
    return (
      <View style={styles.emptyWrap}>
        <Text style={styles.emptyTitle}>No linked accounts yet</Text>
        <Text style={styles.emptyText}>Connect a bank account to filter activity by account.</Text>
      </View>
    );
  }

  return (
    <View style={styles.list}>
      {accounts.map((account) => (
        <Pressable
          key={account.id}
          onPress={() => onSelect(account)}
          style={({ pressed }) => [styles.row, pressed ? styles.rowPressed : null]}
        >
          <View style={styles.accountVisualWrap}>
            {account.providerId || account.providerDisplayName || account.providerIconUrl || account.providerLogoUrl ? (
              <AccountProviderBadge account={account} compact />
            ) : (
              <View style={styles.fallbackIconWrap}>
                <Ionicons name="wallet-outline" size={16} color={palette.textSecondary} />
              </View>
            )}
          </View>
          <View style={styles.copyWrap}>
            <Text style={styles.title} numberOfLines={1}>
              {account.name}
            </Text>
            <Text style={styles.hint} numberOfLines={1}>
              <Text style={styles.hintPrefix}>Type:</Text>{" "}
              <Text style={styles.hintValue}>{account.type}</Text>{" "}
              <Text style={styles.hintPrefix}>Balance:</Text>{" "}
              <Text style={styles.hintValue}>{formatCurrency(account.currentBalance, account.currency)}</Text>{" "}
              <Text style={styles.hintPrefix}>Transactions:</Text>{" "}
              <Text style={styles.hintValue}>{account.transactionCount}</Text>
            </Text>
          </View>
        </Pressable>
      ))}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  list: {
    gap: spacing[8]
  },
  row: {
    minHeight: 58,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  rowPressed: {
    opacity: 0.9
  },
  accountVisualWrap: {
    minWidth: 46
  },
  fallbackIconWrap: {
    width: 32,
    height: 32,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  copyWrap: {
    flex: 1,
    minWidth: 0,
    gap: 2
  },
  title: {
    color: palette.textPrimary,
    ...typography.body2
  },
  hint: {
    ...typography.caption
  },
  hintPrefix: {
    color: palette.accent,
    fontWeight: "500"
  },
  hintValue: {
    color: palette.textSecondary
  },
  emptyWrap: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: 2
  },
  emptyTitle: {
    color: palette.textPrimary,
    ...typography.body2
  },
  emptyText: {
    color: palette.textSecondary,
    ...typography.caption
  }
}));
