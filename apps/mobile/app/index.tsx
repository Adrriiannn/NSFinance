import { Link } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { Screen } from "../src/components/Screen";
import { Card } from "../src/components/Card";
import { palette, radius, spacing } from "../src/theme/tokens";

export default function IndexScreen() {
  return (
    <Screen>
      <View style={styles.hero}>
        <Text style={styles.brand}>NSFinTech</Text>
        <Text style={styles.subtitle}>Ireland-first personal finance companion</Text>
      </View>

      <Card>
        <Text style={styles.cardTitle}>Foundation Ready</Text>
        <Text style={styles.cardBody}>
          Mobile-first product shell with modular backend and worker scaffolding.
        </Text>
      </Card>

      <View style={styles.actions}>
        <Link style={styles.primaryAction} href="/dashboard">
          Open Dashboard
        </Link>
        <Link style={styles.secondaryAction} href="/health">
          API Health Check
        </Link>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: {
    marginBottom: spacing.lg
  },
  brand: {
    fontSize: 34,
    fontWeight: "700",
    color: palette.textPrimary,
    letterSpacing: 0.5
  },
  subtitle: {
    marginTop: spacing.xs,
    fontSize: 15,
    color: palette.textSecondary
  },
  cardTitle: {
    fontSize: 18,
    fontWeight: "600",
    color: palette.textPrimary,
    marginBottom: spacing.sm
  },
  cardBody: {
    color: palette.textSecondary,
    lineHeight: 22
  },
  actions: {
    marginTop: spacing.md,
    gap: spacing.sm
  },
  primaryAction: {
    textAlign: "center",
    color: palette.surface,
    backgroundColor: palette.accent,
    paddingVertical: spacing.sm,
    borderRadius: radius.sm,
    fontWeight: "600"
  },
  secondaryAction: {
    textAlign: "center",
    color: palette.textPrimary,
    borderColor: palette.border,
    borderWidth: 1,
    paddingVertical: spacing.sm,
    borderRadius: radius.sm,
    fontWeight: "600"
  }
});
