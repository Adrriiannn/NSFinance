import { Pressable, StyleSheet, Text } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";

type TertiaryButtonProps = {
  label: string;
  onPress: () => void;
  disabled?: boolean;
};

export function TertiaryButton({ label, onPress, disabled = false }: TertiaryButtonProps) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [styles.button, disabled ? styles.disabled : null, pressed ? styles.pressed : null]}
    >
      <Text style={styles.label}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    paddingHorizontal: spacing[4],
    paddingVertical: spacing[4]
  },
  label: {
    color: palette.primaryGlow,
    ...typography.caption
  },
  pressed: {
    opacity: 0.72
  },
  disabled: {
    opacity: 0.48
  }
});
