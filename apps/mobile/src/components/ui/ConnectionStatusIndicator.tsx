import { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";

export type ConnectionStatus =
  | "not_connected"
  | "connecting"
  | "connected"
  | "sync_failed"
  | "reconnect_required";

type ConnectionStatusIndicatorProps = {
  status: ConnectionStatus;
  helperText?: string;
};

const statusConfig: Record<
  ConnectionStatus,
  { label: string; color: string; helper: string }
> = {
  not_connected: {
    label: "Not connected",
    color: "rgba(200,210,228,0.46)",
    helper: "Connect your bank to import linked accounts, balances, and transactions."
  },
  connecting: {
    label: "Connecting",
    color: palette.caution,
    helper: "Authorizing secure bank connection. Finish consent in your browser."
  },
  connected: {
    label: "Connected",
    color: palette.success,
    helper: "Connection is active. Initial sync may still be starting; run sync to refresh latest account data."
  },
  sync_failed: {
    label: "Sync failed",
    color: palette.negative,
    helper: "Connection exists but sync failed. Retry sync."
  },
  reconnect_required: {
    label: "Reconnect required",
    color: palette.negative,
    helper: "Provider access expired or revoked. Reconnect your bank."
  }
};

export function ConnectionStatusIndicator({ status, helperText }: ConnectionStatusIndicatorProps) {
  const config = statusConfig[status];
  const [dotCount, setDotCount] = useState(1);

  useEffect(() => {
    if (status !== "connecting") {
      setDotCount(1);
      return;
    }

    const interval = setInterval(() => {
      setDotCount((current) => (current >= 3 ? 1 : current + 1));
    }, 540);

    return () => clearInterval(interval);
  }, [status]);

  const label = status === "connecting" ? `Connecting${".".repeat(dotCount)}` : config.label;

  return (
    <View style={styles.wrap}>
      <View style={styles.row}>
        <View style={[styles.ledGlow, { shadowColor: config.color }]}>
          <View style={[styles.led, { backgroundColor: config.color }]} />
        </View>
        <Text style={styles.label}>{label}</Text>
      </View>
      <Text style={styles.helper}>{helperText ?? config.helper}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    gap: spacing[8]
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  ledGlow: {
    width: 18,
    height: 18,
    borderRadius: 9,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(9,19,33,0.85)",
    shadowOpacity: 0.58,
    shadowRadius: 9,
    shadowOffset: { width: 0, height: 0 },
    elevation: 7
  },
  led: {
    width: 12,
    height: 12,
    borderRadius: 6
  },
  label: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  helper: {
    color: palette.textSecondary,
    ...typography.caption
  }
});

