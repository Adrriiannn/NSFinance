import type { ReactNode } from "react";
import { ActivityIndicator, Pressable, StyleSheet, View } from "react-native";
import type { AccessibilityProps, StyleProp, TextStyle, ViewStyle } from "react-native";
import { AppText } from "../text/AppText";
import { useButtonPresetStyles, type ButtonVariant } from "./button.presets";

type ButtonProps = AccessibilityProps & {
  label?: string;
  variant?: ButtonVariant;
  icon?: ReactNode;
  trailingIcon?: ReactNode;
  onPress?: () => void;
  disabled?: boolean;
  isLoading?: boolean;
  style?: StyleProp<ViewStyle>;
  labelStyle?: StyleProp<TextStyle>;
};

export function Button({
  label,
  variant = "primary",
  icon,
  trailingIcon,
  onPress,
  disabled = false,
  isLoading = false,
  style,
  labelStyle,
  accessibilityLabel,
  ...props
}: ButtonProps) {
  const { buttonPresets, buttonStateStyles } = useButtonPresetStyles();
  const preset = buttonPresets[variant];
  const isDisabled = disabled || isLoading;

  return (
    <Pressable
      {...props}
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityRole="button"
      disabled={isDisabled}
      onPress={onPress}
      style={({ pressed }) => [
        preset.container,
        style,
        isDisabled ? buttonStateStyles.disabled : null,
        pressed ? buttonStateStyles.pressed : null
      ]}
    >
      {isLoading ? (
        <ActivityIndicator color={preset.activityColor} />
      ) : (
        <View style={styles.content}>
          {icon}
          {preset.iconOnly ? null : (
            <AppText
              preset="buttonLabel"
              numberOfLines={1}
              adjustsFontSizeToFit
              minimumFontScale={0.9}
              style={[styles.label, preset.label, labelStyle]}
            >
              {label ?? ""}
            </AppText>
          )}
          {trailingIcon}
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    minWidth: 0
  },
  label: {
    minWidth: 0,
    flexShrink: 1
  }
});
