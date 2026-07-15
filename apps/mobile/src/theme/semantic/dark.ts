import { alphaColors, colors } from "../tokens/colors";
import type { SemanticButtonStates, SemanticTheme } from "./types";

const actionColors = {
  primary: colors.accent500,
  primaryStrong: colors.accent600,
  primaryGlow: colors.accent400,
  secondary: colors.surfaceRaised,
  secondaryStrong: colors.surfaceRaisedStrong,
  ghost: "transparent",
  destructive: colors.danger
} as const;

const onActionColors = {
  primary: colors.ink,
  secondary: colors.textPrimary,
  ghost: colors.accent500,
  destructive: colors.textPrimary,
  disabled: colors.textMuted
} as const;

const disabledButtonState = {
  background: colors.surfaceInputStrong,
  border: alphaColors.borderSubtleOnDark,
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
    background: colors.surfaceInput,
    border: alphaColors.borderSubtleOnDark,
    foreground: onActionColors.secondary
  },
  active: {
    background: colors.surfaceInputStrong,
    border: alphaColors.blueBorderStrong,
    foreground: onActionColors.secondary
  },
  disabled: disabledButtonState,
  loading: {
    background: colors.surfaceInput,
    border: alphaColors.borderSubtleOnDark,
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
    background: alphaColors.blueSubtle,
    border: alphaColors.blueBorder,
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
    background: alphaColors.dangerSurface,
    border: actionColors.destructive,
    foreground: onActionColors.destructive
  },
  active: {
    background: alphaColors.dangerSoft,
    border: colors.red400,
    foreground: onActionColors.destructive
  },
  disabled: disabledButtonState,
  loading: {
    background: alphaColors.dangerSurface,
    border: actionColors.destructive,
    foreground: onActionColors.destructive
  }
} as const satisfies SemanticButtonStates;

export const darkTheme = {
  name: "dark",
  isDark: true,
  colors: {
    canvas: colors.canvas,
    elevatedCanvas: colors.canvasRaised,
    surface: {
      level0: colors.surfaceBase,
      level1: colors.surfaceRaised,
      level2: colors.surfaceRaisedStrong,
      field: colors.surfaceInput,
      fieldStrong: colors.surfaceInputStrong,
      tabBar: colors.canvasRaised,
      floating: colors.surfaceRaisedStrong,
      muted: colors.surfaceList
    },
    text: {
      primary: colors.textPrimary,
      secondary: colors.textSecondary,
      muted: colors.textMuted,
      inverse: colors.ink
    },
    border: {
      subtle: alphaColors.borderSubtleOnDark,
      strong: alphaColors.borderStrongOnDark,
      focus: colors.white,
      divider: alphaColors.dividerOnDark
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
      successSurface: alphaColors.successSurface,
      warning: colors.warning,
      warningSurface: alphaColors.warningSurface,
      danger: colors.danger,
      dangerSurface: alphaColors.dangerSurface,
      info: colors.info,
      infoSurface: alphaColors.infoSurface
    },
    accent: {
      primary: colors.accent500,
      primaryStrong: colors.accent600,
      cyan: colors.accent500,
      amber: colors.accent300
    },
    overlay: {
      strong: alphaColors.overlayStrong,
      soft: alphaColors.overlaySoft
    },
    money: {
      positive: colors.success,
      negative: colors.danger
    }
  }
} as const satisfies SemanticTheme;
