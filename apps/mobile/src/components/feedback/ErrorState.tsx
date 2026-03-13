import { StyleSheet, Text } from "react-native";
import { PrimaryButton } from "../ui/PrimaryButton";
import { GlassCard } from "../ui/GlassCard";
import { palette, spacing, typography } from "../../theme/tokens";

type ErrorStateProps = {
  title?: string;
  message?: string;
  onRetry?: () => void;
  retryLabel?: string;
  debugDetail?: string;
  showDebugDetail?: boolean;
};

export function ErrorState({
  title = "Something went wrong",
  message = "We couldn't load this section.",
  onRetry,
  retryLabel = "Retry",
  debugDetail,
  showDebugDetail
}: ErrorStateProps) {
  const shouldShowDebug = Boolean(debugDetail) && (showDebugDetail ?? __DEV__);

  return (
    <GlassCard style={styles.card}>
      <Text style={styles.title}>{title}</Text>
      <Text style={styles.message}>{message}</Text>
      {shouldShowDebug ? <Text style={styles.debug}>{debugDetail}</Text> : null}
      {onRetry ? <PrimaryButton label={retryLabel} onPress={onRetry} /> : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    gap: spacing[12]
  },
  title: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  message: {
    color: palette.textSecondary,
    ...typography.body
  },
  debug: {
    color: palette.caution,
    ...typography.caption
  }
});
