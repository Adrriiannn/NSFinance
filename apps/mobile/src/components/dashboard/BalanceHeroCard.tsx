import { LinearGradient } from "expo-linear-gradient";
import { useMemo } from "react";
import { StyleSheet, Text, View } from "react-native";
import { AnimatedCurrencyText } from "../ui/AnimatedCurrencyText";
import { gradients, palette, radius, shadows, spacing, typography } from "../../theme/tokens";

type BalanceHeroCardProps = {
  totalBalance: number;
  accountCount: number;
  transactionCount: number;
};

export function BalanceHeroCard({
  totalBalance,
  accountCount,
  transactionCount
}: BalanceHeroCardProps) {
  const subtitle = useMemo(
    () => `${accountCount} accounts | ${transactionCount} transactions`,
    [accountCount, transactionCount]
  );

  return (
    <LinearGradient colors={gradients.hero} style={styles.card}>
      <View style={styles.glowDot} />
      <View style={styles.topRow}>
        <Text style={styles.label}>Total balance</Text>
        <View style={styles.badge}>
          <Text style={styles.badgeText}>Live</Text>
        </View>
      </View>

      <AnimatedCurrencyText
        value={totalBalance}
        currency="EUR"
        style={styles.balance}
        baseColor={palette.textPrimary}
      />
      <Text style={styles.subtitle}>{subtitle}</Text>
    </LinearGradient>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.hero,
    borderWidth: 1,
    borderColor: "rgba(226,236,255,0.24)",
    overflow: "hidden",
    padding: spacing[20],
    ...shadows.floating
  },
  glowDot: {
    position: "absolute",
    top: -36,
    right: -24,
    width: 140,
    height: 140,
    borderRadius: 70,
    backgroundColor: "rgba(110,168,255,0.18)"
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
    borderRadius: 999,
    backgroundColor: "rgba(4,11,23,0.35)"
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
  }
});
