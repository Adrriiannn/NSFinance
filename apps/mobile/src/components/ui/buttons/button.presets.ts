import type { TextStyle, ViewStyle } from "react-native";
import { borders, controls, opacity, palette, radius, sizing, spacing, surfaces, typography } from "../../../theme/tokens";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "destructive" | "icon" | "compact" | "pillAction";

type ButtonPreset = {
  container: ViewStyle;
  label: TextStyle;
  activityColor: string;
  iconOnly?: boolean;
};

const baseContainer: ViewStyle = {
  borderWidth: borders.width.thin,
  alignItems: "center",
  justifyContent: "center",
  flexDirection: "row",
  gap: spacing[8]
};

const baseLabel: TextStyle = {
  ...typography.buttonLabel,
  textAlign: "center"
};

export const buttonPresets: Record<ButtonVariant, ButtonPreset> = {
  primary: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.standard,
      borderRadius: radius.hero,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: controls.primaryFill,
      borderColor: controls.primaryBorder
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    },
    activityColor: palette.textPrimary
  },
  secondary: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.standard,
      borderRadius: radius.hero,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: surfaces.section,
      borderColor: palette.borderStrong
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    },
    activityColor: palette.textPrimary
  },
  ghost: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.standard,
      borderRadius: radius.hero,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "transparent",
      borderColor: "transparent"
    },
    label: {
      ...baseLabel,
      color: palette.primaryGlow
    },
    activityColor: palette.primaryGlow
  },
  destructive: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.standard,
      borderRadius: radius.hero,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "rgba(244, 104, 119, 0.16)",
      borderColor: "rgba(244, 104, 119, 0.34)"
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    },
    activityColor: palette.textPrimary
  },
  icon: {
    container: {
      ...baseContainer,
      width: sizing.button.heights.icon,
      height: sizing.button.heights.icon,
      borderRadius: radius.medium,
      backgroundColor: surfaces.fieldStrong,
      borderColor: palette.border
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    },
    activityColor: palette.textPrimary,
    iconOnly: true
  },
  compact: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.compact,
      borderRadius: radius.hero,
      paddingHorizontal: sizing.button.horizontalPadding.compact,
      backgroundColor: surfaces.section,
      borderColor: palette.border
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary,
      ...typography.caption,
      fontWeight: "700"
    },
    activityColor: palette.textPrimary
  },
  pillAction: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.pillAction,
      borderRadius: radius.pill,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "rgba(47, 107, 255, 0.22)",
      borderColor: "rgba(127, 174, 255, 0.4)"
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    },
    activityColor: palette.textPrimary
  }
};

export const buttonStateStyles = {
  pressed: {
    transform: [{ scale: controls.pressedScale }],
    opacity: opacity.pressed
  },
  disabled: {
    opacity: opacity.disabled
  }
} as const;
