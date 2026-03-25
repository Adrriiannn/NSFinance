import { StyleSheet, Text, View } from "react-native";
import { formatShortDate } from "../../lib/format";
import type { AccountDto } from "../../types/api";
import { palette, spacing, typography } from "../../theme/tokens";
import { AmountText } from "../ui/AmountText";
import { GlassCard } from "../ui/GlassCard";
import { AccountProviderBadge } from "./AccountProviderBadge";

type AccountCardProps = {
  account: AccountDto;
  onPress?: () => void;
  compact?: boolean;
};

export function AccountCard({ account, onPress, compact = false }: AccountCardProps) {
  return (
    <GlassCard onPress={onPress} style={compact ? styles.compactCard : undefined}>
      <View style={styles.topRow}>
        <View style={styles.titleWrap}>
          <Text style={styles.name}>{account.name}</Text>
          <Text style={styles.meta}>
            {account.type} | {account.currency} | {account.transactionCount} transactions
          </Text>
        </View>
        <AccountProviderBadge account={account} compact />
      </View>

      <AmountText
        amount={account.currentBalance}
        currency={account.currency}
        style={styles.amount}
      />

      <Text style={styles.footer}>Opened {formatShortDate(account.createdUtc)}</Text>
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  compactCard: {
    width: 250
  },
  topRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  titleWrap: {
    flex: 1
  },
  name: {
    color: palette.textPrimary,
    ...typography.title2
  },
  meta: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.caption
  },
  amount: {
    marginTop: spacing[16],
    ...typography.displayL
  },
  footer: {
    marginTop: spacing[12],
    color: palette.textSecondary,
    ...typography.caption
  }
});
