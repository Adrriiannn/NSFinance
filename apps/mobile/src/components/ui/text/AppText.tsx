import { useMemo, type ReactNode } from "react";
import type { StyleProp, TextProps, TextStyle } from "react-native";
import { Platform, StyleSheet, Text } from "react-native";
import { useThemeTokens } from "../../../theme/tokens";
import { useTextPresets, type TextPresetName } from "./text.presets";

type AppTextTone = "default" | "secondary" | "positive" | "negative" | "accent";

type AppTextProps = TextProps & {
  children: ReactNode;
  preset?: TextPresetName;
  tone?: AppTextTone;
  style?: StyleProp<TextStyle>;
};

export function AppText({
  children,
  preset = "body",
  tone = "default",
  style,
  allowFontScaling,
  maxFontSizeMultiplier,
  ...props
}: AppTextProps) {
  const { palette } = useThemeTokens();
  const textPresets = useTextPresets();
  const toneStyles = useMemo<Record<AppTextTone, TextStyle>>(
    () => ({
      default: { color: palette.textPrimary },
      secondary: { color: palette.textSecondary },
      positive: { color: palette.success },
      negative: { color: palette.negative },
      accent: { color: palette.accent }
    }),
    [palette]
  );

  return (
    <Text
      {...props}
      allowFontScaling={allowFontScaling ?? false}
      maxFontSizeMultiplier={maxFontSizeMultiplier ?? 1}
      style={[styles.base, textPresets[preset], toneStyles[tone], style]}
    >
      {children}
    </Text>
  );
}

const styles = StyleSheet.create({
  base: {
    minWidth: 0,
    ...(Platform.OS === "android"
      ? {
          includeFontPadding: false
        }
      : null)
  }
});
