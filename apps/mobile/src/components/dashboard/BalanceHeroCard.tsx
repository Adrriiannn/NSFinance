import { useMemo } from "react";
import { Text, View } from "react-native";
import { AnimatedCurrencyText } from "../ui/AnimatedCurrencyText";
import { AccountProviderBadge } from "../accounts/AccountProviderBadge";
import type { AccountDto } from "../../types/api";
import { palette, radius, shadows, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../theme/tokens";

type BalanceHeroCardProps = {
  totalBalance: number;
  currency: string;
  accountCount?: number;
  transactionCount?: number;
  title?: string;
  subtitleOverride?: string;
  badgeLabel?: string | null;
  currencyNote?: string | null;
  providerBranding?: Pick<
    AccountDto,
    "providerId" | "providerDisplayName" | "providerIconUrl" | "providerLogoUrl"
  > | null;
};

export function BalanceHeroCard({
  totalBalance,
  currency,
  accountCount = 0,
  transactionCount = 0,
  title = "Total balance",
  subtitleOverride,
  badgeLabel = "Live",
  currencyNote,
  providerBranding = null
}: BalanceHeroCardProps) {
  const subtitle = useMemo(
    () => subtitleOverride ?? `${accountCount} accounts | ${transactionCount} transactions`,
    [accountCount, subtitleOverride, transactionCount]
  );

  return (
    <View style={styles.card}>
      <View style={styles.topRow}>
        <Text style={styles.label}>{title}</Text>
        {providerBranding ? (
          <AccountProviderBadge account={providerBranding} />
        ) : badgeLabel ? (
          <View style={styles.badge}>
            <Text style={styles.badgeText}>{badgeLabel}</Text>
          </View>
        ) : null}
      </View>

      <AnimatedCurrencyText
        value={totalBalance}
        currency={currency}
        style={styles.balance}
        baseColor={palette.textPrimary}
      />
      <Text style={styles.subtitle}>{subtitle}</Text>
      {currencyNote ? <Text style={styles.currencyNote}>{currencyNote}</Text> : null}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  card: {
    borderRadius: radius.hero,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    padding: spacing[20],
    ...shadows.soft
  },
  topRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    minHeight: 38
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption
  },
  badge: {
    minWidth: 58,
    height: 38,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.fieldStrong,
    paddingHorizontal: spacing[10],
    alignItems: "center",
    justifyContent: "center"
  },
  badgeText: {
    color: palette.accent,
    ...typography.caption
  },
  balance: {
    marginTop: spacing[12],
    color: palette.textPrimary,
    ...typography.displayXL,
    fontVariant: ["tabular-nums"]
  },
  subtitle: {
    marginTop: spacing[8],
    color: palette.textSecondary,
    ...typography.body2
  },
  currencyNote: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.caption
  }
}));

