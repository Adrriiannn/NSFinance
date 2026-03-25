import { colors } from "../tokens/colors";

export const lightTheme = {
  name: "light",
  isDark: false,
  colors: {
    canvas: "#F7F6F3",
    elevatedCanvas: "#F2F0EC",
    surface: {
      level0: "#FFFFFF",
      level1: "#FBFAF7",
      level2: "#F3F1EC",
      field: "#F6F4EF",
      fieldStrong: "#EEEAE3",
      tabBar: "rgba(255, 255, 255, 0.98)",
      floating: "#FFFFFF",
      muted: "#F3F1EC"
    },
    text: {
      primary: "#111111",
      secondary: "#3F3F3F",
      muted: "#6B6B6B",
      inverse: colors.white
    },
    border: {
      subtle: "rgba(184, 94, 0, 0.14)",
      strong: "rgba(184, 94, 0, 0.36)",
      focus: "#B85E00",
      divider: "rgba(17, 17, 17, 0.08)"
    },
    action: {
      primary: "#B85E00",
      primaryStrong: "#A44C00",
      primaryGlow: "#CC6D08",
      secondary: "#FFFFFF",
      secondaryStrong: "#EEEAE3",
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
} as const;
