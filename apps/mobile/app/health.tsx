import { useEffect, useState } from "react";
import { Pressable, StyleSheet, Text } from "react-native";
import { Card } from "../src/components/Card";
import { Screen } from "../src/components/Screen";
import { fetchApiHealth } from "../src/lib/api";
import { apiBaseUrl } from "../src/lib/config";
import { palette, radius, spacing } from "../src/theme/tokens";

type HealthState = {
  status: "idle" | "loading" | "ok" | "error";
  message: string;
};

export default function HealthScreen() {
  const [state, setState] = useState<HealthState>({
    status: "idle",
    message: "Tap refresh to check API status."
  });

  const loadHealth = async () => {
    try {
      setState({ status: "loading", message: "Checking API..." });
      const health = await fetchApiHealth();
      setState({
        status: "ok",
        message: `${health.status} at ${new Date(health.timestampUtc).toLocaleString()}`
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unknown error";
      setState({ status: "error", message });
    }
  };

  useEffect(() => {
    void loadHealth();
  }, []);

  return (
    <Screen>
      <Text style={styles.title}>API Health</Text>
      <Text style={styles.subtitle}>Base URL: {apiBaseUrl}</Text>

      <Card>
        <Text
          style={[
            styles.status,
            state.status === "ok" ? styles.statusOk : null,
            state.status === "error" ? styles.statusError : null
          ]}
        >
          {state.status.toUpperCase()}
        </Text>
        <Text style={styles.message}>{state.message}</Text>
      </Card>

      <Pressable style={styles.button} onPress={loadHealth}>
        <Text style={styles.buttonText}>Refresh Health</Text>
      </Pressable>
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
  status: {
    fontSize: 12,
    fontWeight: "700",
    letterSpacing: 1,
    color: palette.textSecondary
  },
  statusOk: {
    color: palette.success
  },
  statusError: {
    color: palette.danger
  },
  message: {
    marginTop: spacing.sm,
    color: palette.textPrimary
  },
  button: {
    backgroundColor: palette.accent,
    borderRadius: radius.sm,
    paddingVertical: spacing.sm
  },
  buttonText: {
    textAlign: "center",
    color: palette.surface,
    fontWeight: "600"
  }
});
