import { ReactNode } from "react";
import { StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";
import { TertiaryButton } from "./TertiaryButton";

type SectionHeaderProps = {
  title: string;
  subtitle?: string;
  actionLabel?: string;
  onActionPress?: () => void;
  trailing?: ReactNode;
};

export function SectionHeader({
  title,
  subtitle,
  actionLabel,
  onActionPress,
  trailing
}: SectionHeaderProps) {
  return (
    <View style={styles.row}>
      <View>
        <Text style={styles.title}>{title}</Text>
        {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
      </View>

      {trailing}

      {!trailing && actionLabel ? (
        <TertiaryButton label={actionLabel} onPress={onActionPress ?? (() => undefined)} />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  title: {
    ...typography.title2,
    color: palette.textPrimary
  },
  subtitle: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.body2
  }
});
