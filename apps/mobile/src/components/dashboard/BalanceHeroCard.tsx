import { useMemo } from "react";
import { StyleSheet, Text, View } from "react-native";
import { AnimatedCurrencyText } from "../ui/AnimatedCurrencyText";
import { palette, radius, shadows, spacing, surfaces, typography } from "../../theme/tokens";

type BalanceHeroCardProps = {
  totalBalance: number;
  currency: string;
  accountCount?: number;
  transactionCount?: number;
  title?: string;
  subtitleOverride?: string;
  badgeLabel?: string;
  currencyNote?: string | null;
};

export function BalanceHeroCard({
  totalBalance,
  currency,
  accountCount = 0,
  transactionCount = 0,
  title = "Total balance",
  subtitleOverride,
  badgeLabel = "Live",
  currencyNote
}: BalanceHeroCardProps) {
  const subtitle = useMemo(
    () => subtitleOverride ?? `${accountCount} accounts | ${transactionCount} transactions`,
    [accountCount, subtitleOverride, transactionCount]
  );

  return (
    <View style={styles.card}>
      <View style={styles.topRow}>
        <Text style={styles.label}>{title}</Text>
        <View style={styles.badge}>
          <Text style={styles.badgeText}>{badgeLabel}</Text>
        </View>
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

const styles = StyleSheet.create({
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
    alignItems: "center"
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption
  },
  badge: {
    paddingHorizontal: spacing[8],
    paddingVertical: spacing[4],
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.fieldStrong
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
});
