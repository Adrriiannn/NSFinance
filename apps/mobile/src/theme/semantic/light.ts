import { colors } from "../tokens/colors";

export const lightTheme = {
  name: "light",
  isDark: false,
  colors: {
    canvas: "#F8F6F3",
    elevatedCanvas: "#FFFFFF",
    surface: {
      level0: "#FFFFFF",
      level1: "#FCFAF7",
      level2: "#F7F3EE",
      field: "#F7F3EE",
      fieldStrong: "#F2ECE3",
      tabBar: "rgba(255, 255, 255, 0.98)",
      floating: "#FFFFFF",
      muted: "#F5F0E8"
    },
    text: {
      primary: "#222222",
      secondary: "#525252",
      muted: "#767676",
      inverse: colors.white
    },
    border: {
      subtle: "rgba(217, 119, 6, 0.24)",
      strong: "rgba(217, 119, 6, 0.36)",
      focus: colors.accent500,
      divider: "rgba(217, 119, 6, 0.2)"
    },
    action: {
      primary: colors.accent500,
      primaryStrong: colors.accent600,
      primaryGlow: colors.accent400,
      secondary: "#FCFAF7",
      secondaryStrong: "#F5F0E8",
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
      strong: "rgba(0, 0, 0, 0.4)",
      soft: "rgba(0, 0, 0, 0.24)"
    },
    money: {
      positive: colors.success,
      negative: colors.danger
    }
  }
} as const;
