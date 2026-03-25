import type { ReactNode } from "react";
import type { StyleProp, TextProps, TextStyle } from "react-native";
import { Text } from "react-native";
import { palette } from "../../../theme/tokens";
import { textPresets, type TextPresetName } from "./text.presets";

type AppTextTone = "default" | "secondary" | "positive" | "negative" | "accent";

type AppTextProps = TextProps & {
  children: ReactNode;
  preset?: TextPresetName;
  tone?: AppTextTone;
  style?: StyleProp<TextStyle>;
};

const toneStyles: Record<AppTextTone, TextStyle> = {
  default: { color: palette.textPrimary },
  secondary: { color: palette.textSecondary },
  positive: { color: palette.success },
  negative: { color: palette.negative },
  accent: { color: palette.accent }
};

export function AppText({
  children,
  preset = "body",
  tone = "default",
  style,
  ...props
}: AppTextProps) {
  return (
    <Text {...props} style={[textPresets[preset], toneStyles[tone], style]}>
      {children}
    </Text>
  );
}
