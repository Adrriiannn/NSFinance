import { useState, type ReactNode } from "react";
import { ActivityIndicator, Pressable, StyleSheet, View } from "react-native";
import type { AccessibilityProps, StyleProp, TextStyle, ViewStyle } from "react-native";
import { AppText } from "../text/AppText";
import { resolveButtonVisualState } from "./button.states";
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
  accessibilityState,
  ...props
}: ButtonProps) {
  const { buttonPresets, buttonStateStyles } = useButtonPresetStyles();
  const preset = buttonPresets[variant];
  const isDisabled = disabled || isLoading;
  const [isFocused, setIsFocused] = useState(false);

  const getVisualState = (isPressed: boolean) =>
    resolveButtonVisualState({
      isLoading,
      isDisabled: disabled,
      isFocused,
      isPressed
    });

  return (
    <Pressable
      {...props}
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityRole="button"
      accessibilityState={{
        ...accessibilityState,
        busy: isLoading || accessibilityState?.busy,
        disabled: isDisabled || accessibilityState?.disabled
      }}
      disabled={isDisabled}
      onBlur={() => setIsFocused(false)}
      onFocus={() => setIsFocused(true)}
      onPress={onPress}
      style={({ pressed }) => {
        const visualState = getVisualState(pressed);
        const statePreset = preset.states[visualState];

        return [
          preset.container,
          style,
          statePreset.container,
          visualState === "active" && isFocused ? buttonStateStyles.focused : null,
          visualState === "active" && pressed ? buttonStateStyles.pressed : null
        ];
      }}
    >
      {({ pressed }) => {
        const statePreset = preset.states[getVisualState(pressed)];

        return isLoading ? (
          <ActivityIndicator color={statePreset.activityColor} />
        ) : (
          <View style={styles.content}>
            {icon}
            {preset.iconOnly ? null : (
              <AppText
                preset="buttonLabel"
                allowFontScaling
                maxFontSizeMultiplier={2}
                numberOfLines={2}
                style={[styles.label, preset.label, labelStyle, statePreset.label]}
              >
                {label ?? ""}
              </AppText>
            )}
            {trailingIcon}
          </View>
        );
      }}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    minWidth: 0,
    maxWidth: "100%",
    flexShrink: 1
  },
  label: {
    minWidth: 0,
    flexShrink: 1
  }
});
