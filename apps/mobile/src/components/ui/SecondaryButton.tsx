import { Pressable, StyleSheet, Text } from "react-native";
import { palette, radius, spacing, surfaces, typography } from "../../theme/tokens";

type SecondaryButtonProps = {
  label: string;
  onPress: () => void;
  disabled?: boolean;
};

export function SecondaryButton({
  label,
  onPress,
  disabled = false
}: SecondaryButtonProps) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [
        styles.button,
        disabled ? styles.disabled : null,
        pressed ? styles.pressed : null
      ]}
    >
      <Text
        style={styles.label}
        numberOfLines={2}
        adjustsFontSizeToFit
        minimumFontScale={0.88}
      >
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 54,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.section,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[16]
  },
  label: {
    color: palette.textPrimary,
    ...typography.button,
    fontWeight: "600",
    textAlign: "center"
  },
  pressed: {
    transform: [{ scale: 0.985 }],
    opacity: 0.9
  },
  disabled: {
    opacity: 0.55
  }
});
