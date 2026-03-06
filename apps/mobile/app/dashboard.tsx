import { StyleSheet, Text, View } from "react-native";
import { Card } from "../src/components/Card";
import { Screen } from "../src/components/Screen";
import { palette, spacing } from "../src/theme/tokens";

export default function DashboardScreen() {
  return (
    <Screen>
      <Text style={styles.title}>Dashboard</Text>
      <Text style={styles.subtitle}>Authenticated shell placeholder</Text>

      <Card>
        <Text style={styles.cardTitle}>Accounts</Text>
        <Text style={styles.cardBody}>No linked financial accounts yet.</Text>
      </Card>

      <Card>
        <Text style={styles.cardTitle}>Transactions</Text>
        <Text style={styles.cardBody}>Recent activity will appear here.</Text>
      </Card>

      <Card>
        <Text style={styles.cardTitle}>Goals</Text>
        <Text style={styles.cardBody}>Savings and budget goals will be tracked here.</Text>
      </Card>
    </Screen>
  );
}

const styles = StyleSheet.create({
  title: {
    fontSize: 30,
    fontWeight: "700",
    color: palette.textPrimary
  },
  subtitle: {
    marginTop: spacing.xs,
    marginBottom: spacing.lg,
    color: palette.textSecondary
  },
  cardTitle: {
    fontSize: 17,
    fontWeight: "600",
    color: palette.textPrimary,
    marginBottom: spacing.xs
  },
  cardBody: {
    color: palette.textSecondary,
    lineHeight: 21
  }
});
