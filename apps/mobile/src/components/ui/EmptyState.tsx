import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, radius, spacing, typography } from "../../theme/tokens";

type EmptyStateProps = {
  title: string;
  message: string;
  actionLabel?: string;
  onActionPress?: () => void;
};

export function EmptyState({
  title,
  message,
  actionLabel,
  onActionPress
}: EmptyStateProps) {
  return (
    <View style={styles.card}>
      <View style={styles.orb} />
      <Text style={styles.title}>{title}</Text>
      <Text style={styles.message}>{message}</Text>

      {actionLabel ? (
        <Pressable onPress={onActionPress} style={({ pressed }) => [styles.action, pressed ? styles.pressed : null]}>
          <Text style={styles.actionText}>{actionLabel}</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.large,
    borderColor: palette.border,
    borderWidth: 1,
    backgroundColor: "rgba(17,39,66,0.68)",
    padding: spacing[20],
    alignItems: "center"
  },
  orb: {
    width: 54,
    height: 54,
    borderRadius: 27,
    backgroundColor: "rgba(110,168,255,0.24)",
    marginBottom: spacing[12]
  },
  title: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  message: {
    marginTop: spacing[8],
    color: palette.textSecondary,
    textAlign: "center",
    ...typography.body
  },
  action: {
    marginTop: spacing[16],
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    paddingHorizontal: spacing[16],
    paddingVertical: spacing[8]
  },
  actionText: {
    color: palette.textPrimary,
    ...typography.body
  },
  pressed: {
    opacity: 0.9
  }
});
