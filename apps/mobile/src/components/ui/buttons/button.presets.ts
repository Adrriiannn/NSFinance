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
      borderRadius: radius.medium,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: controls.primaryFill,
      borderColor: controls.primaryBorder
    },
    label: {
      ...baseLabel,
      color: "#FFFFFF"
    },
    activityColor: "#FFFFFF"
  },
  secondary: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.standard,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "#141414",
      borderColor: "rgba(255, 255, 255, 0.08)"
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
      borderRadius: radius.medium,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "transparent",
      borderColor: "transparent"
    },
    label: {
      ...baseLabel,
      color: palette.accent
    },
    activityColor: palette.accent
  },
  destructive: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.standard,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "rgba(226, 90, 90, 0.12)",
      borderColor: "rgba(226, 90, 90, 0.52)"
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
      backgroundColor: "#141414",
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
      borderRadius: radius.medium,
      paddingHorizontal: sizing.button.horizontalPadding.compact,
      backgroundColor: "#141414",
      borderColor: palette.border
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary,
      ...typography.caption,
      fontWeight: "500"
    },
    activityColor: palette.textPrimary
  },
  pillAction: {
    container: {
      ...baseContainer,
      minHeight: sizing.button.heights.pillAction,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.button.horizontalPadding.standard,
      backgroundColor: "#141414",
      borderColor: palette.border
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
