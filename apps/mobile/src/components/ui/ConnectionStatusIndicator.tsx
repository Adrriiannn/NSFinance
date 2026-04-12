import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../theme/tokens";

export type ConnectionStatus =
  | "not_connected"
  | "opening_bank"
  | "awaiting_consent"
  | "connected_pending_sync"
  | "syncing_data"
  | "import_complete_enrichment_queued"
  | "organizing_transactions"
  | "sync_taking_longer_than_expected"
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
    helper: "Launching secure bank authorization."
  },
  awaiting_consent: {
    label: "Awaiting consent",
    color: palette.caution,
    helper: "Finish authorization in your browser."
  },
  connected_pending_sync: {
    label: "Connection secured",
    color: palette.success,
    helper: "Your account connection is secured. Import is starting."
  },
  syncing_data: {
    label: "Importing activity",
    color: palette.success,
    helper: "Fetching balances and transactions."
  },
  import_complete_enrichment_queued: {
    label: "Import complete",
    color: palette.success,
    helper: "Organization is queued and will continue in the background."
  },
  organizing_transactions: {
    label: "Organizing transactions",
    color: palette.success,
    helper: "NSFinance is categorizing and linking your activity."
  },
  sync_taking_longer_than_expected: {
    label: "Provider delay",
    color: palette.caution,
    helper: "This bank is taking longer than usual. NSFinance keeps retrying."
  },
  synced: {
    label: "Everything is ready",
    color: palette.success,
    helper: "Your connection is active and activity is organized."
  },
  failed: {
    label: "Sync needs attention",
    color: palette.negative,
    helper: "Connection exists but import did not complete. Reconnect or retry."
  },
  reauth_required: {
    label: "Reconnect required",
    color: palette.negative,
    helper: "Provider access expired or was interrupted. Reconnect to resume syncing."
  }
};

const animatedStatuses = new Set<ConnectionStatus>([
  "opening_bank",
  "awaiting_consent",
  "syncing_data",
  "organizing_transactions"
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

const styles = createRuntimeStyleSheet(() => ({
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
}));

