import { colors } from "../tokens/colors";
import type { SemanticButtonStates, SemanticTheme } from "./types";

const lightColors = {
  canvas: "#F7F6F3",
  elevatedCanvas: "#F2F0EC",
  surface0: colors.white,
  surface1: "#FBFAF7",
  surface2: "#F3F1EC",
  field: "#F6F4EF",
  fieldStrong: "#EEEAE3",
  textPrimary: "#111111",
  textSecondary: "#3F3F3F",
  textMuted: "#6B6B6B"
} as const;

const actionColors = {
  primary: "#B85E00",
  primaryStrong: "#A44C00",
  primaryGlow: "#CC6D08",
  secondary: colors.white,
  secondaryStrong: lightColors.fieldStrong,
  ghost: "transparent",
  destructive: colors.danger
} as const;

const onActionColors = {
  primary: colors.white,
  secondary: lightColors.textPrimary,
  ghost: actionColors.primary,
  destructive: lightColors.textPrimary,
  disabled: lightColors.textMuted
} as const;

const disabledButtonState = {
  background: lightColors.fieldStrong,
  border: "rgba(17, 17, 17, 0.08)",
  foreground: onActionColors.disabled
} as const;

const primaryButtonStates = {
  idle: {
    background: actionColors.primary,
    border: actionColors.primary,
    foreground: onActionColors.primary
  },
  active: {
    background: actionColors.primaryStrong,
    border: actionColors.primaryStrong,
    foreground: onActionColors.primary
  },
  disabled: disabledButtonState,
  loading: {
    background: actionColors.primary,
    border: actionColors.primary,
    foreground: onActionColors.primary
  }
} as const satisfies SemanticButtonStates;

const secondaryButtonStates = {
  idle: {
    background: actionColors.secondary,
    border: "rgba(184, 94, 0, 0.14)",
    foreground: onActionColors.secondary
  },
  active: {
    background: actionColors.secondaryStrong,
    border: actionColors.primary,
    foreground: onActionColors.secondary
  },
  disabled: disabledButtonState,
  loading: {
    background: actionColors.secondary,
    border: "rgba(184, 94, 0, 0.14)",
    foreground: onActionColors.secondary
  }
} as const satisfies SemanticButtonStates;

const ghostButtonStates = {
  idle: {
    background: actionColors.ghost,
    border: actionColors.ghost,
    foreground: onActionColors.ghost
  },
  active: {
    background: "rgba(184, 94, 0, 0.12)",
    border: "rgba(184, 94, 0, 0.24)",
    foreground: onActionColors.ghost
  },
  disabled: {
    background: actionColors.ghost,
    border: actionColors.ghost,
    foreground: onActionColors.disabled
  },
  loading: {
    background: actionColors.ghost,
    border: actionColors.ghost,
    foreground: onActionColors.ghost
  }
} as const satisfies SemanticButtonStates;

const destructiveButtonStates = {
  idle: {
    background: "rgba(226, 90, 90, 0.12)",
    border: actionColors.destructive,
    foreground: onActionColors.destructive
  },
  active: {
    background: "rgba(226, 90, 90, 0.18)",
    border: colors.red400,
    foreground: onActionColors.destructive
  },
  disabled: disabledButtonState,
  loading: {
    background: "rgba(226, 90, 90, 0.12)",
    border: actionColors.destructive,
    foreground: onActionColors.destructive
  }
} as const satisfies SemanticButtonStates;

export const lightTheme = {
  name: "light",
  isDark: false,
  colors: {
    canvas: lightColors.canvas,
    elevatedCanvas: lightColors.elevatedCanvas,
    surface: {
      level0: lightColors.surface0,
      level1: lightColors.surface1,
      level2: lightColors.surface2,
      field: lightColors.field,
      fieldStrong: lightColors.fieldStrong,
      tabBar: "rgba(255, 255, 255, 0.98)",
      floating: colors.white,
      muted: lightColors.surface2
    },
    text: {
      primary: lightColors.textPrimary,
      secondary: lightColors.textSecondary,
      muted: lightColors.textMuted,
      inverse: colors.white
    },
    border: {
      subtle: "rgba(184, 94, 0, 0.14)",
      strong: "rgba(184, 94, 0, 0.36)",
      focus: lightColors.textPrimary,
      divider: "rgba(17, 17, 17, 0.08)"
    },
    action: {
      ...actionColors,
      button: {
        primary: primaryButtonStates,
        secondary: secondaryButtonStates,
        ghost: ghostButtonStates,
        destructive: destructiveButtonStates,
        icon: secondaryButtonStates,
        compact: secondaryButtonStates,
        pillAction: secondaryButtonStates
      }
    },
    onAction: onActionColors,
    status: {
      success: colors.success,
      successSurface: "rgba(29, 186, 114, 0.12)",
      warning: colors.warning,
      warningSurface: "rgba(240, 180, 76, 0.12)",
      danger: colors.danger,
      dangerSurface: "rgba(226, 90, 90, 0.12)",
      info: colors.info,
      infoSurface: "rgba(154, 154, 154, 0.1)"
    },
    accent: {
      primary: "#B85E00",
      primaryStrong: "#A44C00",
      cyan: "#CC6D08",
      amber: "#D77A1A"
    },
    overlay: {
      strong: "rgba(12, 12, 12, 0.18)",
      soft: "rgba(12, 12, 12, 0.1)"
    },
    money: {
      positive: colors.success,
      negative: colors.danger
    }
  }
} as const satisfies SemanticTheme;
