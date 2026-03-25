import { colors } from "../tokens/colors";

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
      subtle: "rgba(242, 140, 40, 0.18)",
      strong: "rgba(242, 140, 40, 0.32)",
      focus: "rgba(242, 140, 40, 0.52)",
      divider: "rgba(242, 140, 40, 0.2)"
    },
    action: {
      primary: colors.accent500,
      primaryStrong: colors.accent600,
      primaryGlow: colors.accent400,
      secondary: colors.surfaceRaised,
      secondaryStrong: colors.surfaceRaisedStrong,
      ghost: "transparent",
      destructive: colors.danger
    },
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
      primary: colors.accent500,
      primaryStrong: colors.accent600,
      cyan: colors.accent500,
      amber: colors.accent300
    },
    overlay: {
      strong: "rgba(0, 0, 0, 0.72)",
      soft: "rgba(0, 0, 0, 0.58)"
    },
    money: {
      positive: colors.success,
      negative: colors.danger
    }
  }
} as const;
