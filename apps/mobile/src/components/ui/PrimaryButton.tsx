import { ReactNode } from "react";
import { ActivityIndicator, Pressable, StyleSheet, Text } from "react-native";
import { LinearGradient } from "expo-linear-gradient";
import { palette, radius, spacing, typography } from "../../theme/tokens";

type PrimaryButtonProps = {
  label: string;
  onPress: () => void;
  icon?: ReactNode;
  isLoading?: boolean;
  disabled?: boolean;
};

export function PrimaryButton({
  label,
  onPress,
  icon,
  isLoading = false,
  disabled = false
}: PrimaryButtonProps) {
  const isDisabled = disabled || isLoading;

  return (
    <Pressable
      onPress={onPress}
      disabled={isDisabled}
      style={({ pressed }) => [
        styles.pressable,
        isDisabled ? styles.disabled : null,
        pressed ? styles.pressed : null
      ]}
    >
      <LinearGradient colors={["#2E6BFF", "#3B79FF", "#2459EB"]} style={styles.button}>
        {isLoading ? (
          <ActivityIndicator color={palette.textPrimary} />
        ) : (
          <>
            {icon}
            <Text style={styles.label}>{label}</Text>
          </>
        )}
      </LinearGradient>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  pressable: {
    borderRadius: radius.medium
  },
  button: {
    minHeight: 50,
    borderRadius: radius.medium,
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "row",
    gap: spacing[8],
    paddingHorizontal: spacing[16]
  },
  label: {
    color: palette.textPrimary,
    ...typography.button,
    fontWeight: "600"
  },
  pressed: {
    transform: [{ scale: 0.985 }],
    opacity: 0.96
  },
  disabled: {
    opacity: 0.58
  }
});
