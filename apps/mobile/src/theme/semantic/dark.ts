import { alphaColors, colors } from "../tokens/colors";

export const darkTheme = {
  name: "dark",
  isDark: true,
  colors: {
    canvas: colors.navy950,
    elevatedCanvas: colors.navy850,
    surface: {
      level0: alphaColors.surfaceSection,
      level1: alphaColors.surfaceLevel1,
      level2: alphaColors.surfaceLevel2,
      field: alphaColors.surfaceField,
      fieldStrong: alphaColors.surfaceFieldStrong,
      tabBar: colors.navy900,
      floating: "rgba(13, 26, 44, 0.94)",
      muted: alphaColors.surfaceMuted
    },
    text: {
      primary: alphaColors.textPrimaryOnDark,
      secondary: alphaColors.textSecondaryOnDark,
      muted: colors.slate500,
      inverse: colors.ink
    },
    border: {
      subtle: alphaColors.borderSubtleOnDark,
      strong: alphaColors.borderStrongOnDark,
      focus: colors.blue400,
      divider: alphaColors.dividerOnDark
    },
    action: {
      primary: colors.blue600,
      primaryStrong: colors.blue500,
      primaryGlow: colors.blue400,
      secondary: alphaColors.surfaceSection,
      secondaryStrong: alphaColors.surfaceLevel1,
      ghost: "transparent",
      destructive: colors.red500
    },
    status: {
      success: colors.green500,
      successSurface: alphaColors.successSurface,
      warning: colors.orange500,
      warningSurface: alphaColors.warningSurface,
      danger: colors.red500,
      dangerSurface: alphaColors.dangerSurface,
      info: colors.blue400,
      infoSurface: alphaColors.infoSurface
    },
    accent: {
      cyan: colors.cyan400,
      amber: colors.amber500
    },
    overlay: {
      strong: alphaColors.overlayStrong,
      soft: alphaColors.overlaySoft
    },
    money: {
      positive: "rgba(104, 214, 164, 0.9)",
      negative: "rgba(255, 141, 153, 0.88)"
    }
  }
} as const;
