import { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { palette, spacing, surfaces, typography } from "../../theme/tokens";

export type ConnectionStatus =
  | "not_connected"
  | "opening_bank"
  | "awaiting_consent"
  | "connected_pending_sync"
  | "syncing_data"
  | "synced"
  | "failed"
  | "reauth_required";

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
    color: palette.textMuted,
    helper: "Connect your bank to import linked accounts, balances, and transactions."
  },
  opening_bank: {
    label: "Opening bank",
    color: palette.caution,
    helper: "Launching the secure TrueLayer consent page."
  },
  awaiting_consent: {
    label: "Awaiting consent",
    color: palette.caution,
    helper: "Finish the secure bank consent flow in your browser."
  },
  connected_pending_sync: {
    label: "Connected",
    color: palette.success,
    helper: "Your bank connection is confirmed. Initial sync is about to start."
  },
  syncing_data: {
    label: "Syncing data",
    color: palette.success,
    helper: "Your bank is connected. We are importing account details and transactions now."
  },
  synced: {
    label: "Synced",
    color: palette.success,
    helper: "Connection is active and account data is up to date."
  },
  failed: {
    label: "Sync failed",
    color: palette.negative,
    helper: "Connection exists but the data sync failed. Retry sync."
  },
  reauth_required: {
    label: "Reconnect required",
    color: palette.negative,
    helper: "Provider access expired or was interrupted. Reconnect your bank."
  }
};

const animatedStatuses = new Set<ConnectionStatus>([
  "opening_bank",
  "awaiting_consent",
  "syncing_data"
]);

export function ConnectionStatusIndicator({ status, helperText }: ConnectionStatusIndicatorProps) {
  const config = statusConfig[status];
  const [dotCount, setDotCount] = useState(1);

  useEffect(() => {
    if (!animatedStatuses.has(status)) {
      setDotCount(1);
      return;
    }

    const interval = setInterval(() => {
      setDotCount((current) => (current >= 3 ? 1 : current + 1));
    }, 540);

    return () => clearInterval(interval);
  }, [status]);

  const label = animatedStatuses.has(status) ? `${config.label}${".".repeat(dotCount)}` : config.label;

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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.field,
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
