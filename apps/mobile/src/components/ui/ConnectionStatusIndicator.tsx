import { StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";

export type ConnectionStatus = "not_started" | "connecting" | "success" | "failed";

type ConnectionStatusIndicatorProps = {
  status: ConnectionStatus;
};

const statusConfig: Record<
  ConnectionStatus,
  { label: string; color: string; helper: string }
> = {
  not_started: {
    label: "Not connected",
    color: "rgba(200,210,228,0.46)",
    helper: "Connect your bank to import account name, type, currency, and balance."
  },
  connecting: {
    label: "Connecting",
    color: palette.caution,
    helper: "Authorizing secure bank connection..."
  },
  success: {
    label: "Connected",
    color: palette.success,
    helper: "Connection succeeded. Review the imported details and save."
  },
  failed: {
    label: "Connection failed",
    color: palette.negative,
    helper: "Connection did not complete. Retry to continue."
  }
};

export function ConnectionStatusIndicator({ status }: ConnectionStatusIndicatorProps) {
  const config = statusConfig[status];

  return (
    <View style={styles.wrap}>
      <View style={styles.row}>
        <View style={[styles.led, { backgroundColor: config.color }]} />
        <Text style={styles.label}>{config.label}</Text>
      </View>
      <Text style={styles.helper}>{config.helper}</Text>
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

