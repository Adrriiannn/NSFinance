import { StyleSheet, Text, View } from "react-native";
import { palette, radius, spacing, surfaces, typography } from "../../theme/tokens";

type StatPillProps = {
  label: string;
  value: string;
};

export function StatPill({ label, value }: StatPillProps) {
  return (
    <View style={styles.pill}>
      <Text style={styles.value}>{value}</Text>
      <Text style={styles.label}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: {
    flex: 1,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.section,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12]
  },
  value: {
    color: palette.textPrimary,
    ...typography.title2,
    fontVariant: ["tabular-nums"]
  },
  label: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.caption
  }
});
