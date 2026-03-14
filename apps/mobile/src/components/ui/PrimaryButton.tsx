import { ReactNode } from "react";
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { controls, palette, typography } from "../../theme/tokens";

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
      <View style={styles.button}>
        {isLoading ? (
          <ActivityIndicator color={palette.textPrimary} />
        ) : (
          <>
            {icon}
            <Text style={styles.label}>{label}</Text>
          </>
        )}
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  pressable: {
    borderRadius: controls.buttonRadius
  },
  button: {
    minHeight: controls.primaryHeight,
    borderRadius: controls.buttonRadius,
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "row",
    gap: 8,
    paddingHorizontal: 16,
    backgroundColor: controls.primaryFill,
    borderWidth: 1,
    borderColor: controls.primaryBorder
  },
  label: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  pressed: {
    transform: [{ scale: controls.pressedScale }],
    opacity: 0.96
  },
  disabled: {
    opacity: 0.58
  }
});
