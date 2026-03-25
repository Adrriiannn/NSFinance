import type { ReactNode } from "react";
import { Pressable } from "react-native";
import type { StyleProp, TextStyle, ViewStyle } from "react-native";
import { AppText } from "../text/AppText";
import { palette } from "../../../theme/tokens";
import { chipPresets, chipSelectedStyle, chipToneStyles, type ChipTone, type ChipVariant } from "./chip.presets";

type ChipProps = {
  label: string;
  variant?: ChipVariant;
  tone?: ChipTone;
  selected?: boolean;
  icon?: ReactNode;
  onPress?: () => void;
  style?: StyleProp<ViewStyle>;
  labelStyle?: StyleProp<TextStyle>;
};

export function Chip({
  label,
  variant = "filter",
  tone = "default",
  selected = false,
  icon,
  onPress,
  style,
  labelStyle
}: ChipProps) {
  const preset = chipPresets[variant];

  return (
    <Pressable
      disabled={!onPress}
      onPress={onPress}
      style={({ pressed }) => [
        preset.container,
        chipToneStyles[tone],
        selected ? chipSelectedStyle : null,
        style,
        pressed ? { opacity: 0.9 } : null
      ]}
    >
      {icon}
      <AppText
        preset={variant === "compact" ? "caption" : "fieldLabel"}
        style={[preset.label, selected ? { color: palette.textPrimary } : null, labelStyle]}
      >
        {label}
      </AppText>
    </Pressable>
  );
}
