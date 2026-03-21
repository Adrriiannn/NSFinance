import { colors } from "../tokens/colors";

export const lightTheme = {
  name: "light",
  isDark: false,
  colors: {
    canvas: "#F4F7FC",
    elevatedCanvas: colors.white,
    surface: {
      level0: "#FFFFFF",
      level1: "#FFFFFF",
      level2: "#F8FBFF",
      field: "#F5F8FE",
      fieldStrong: "#EDF3FD",
      tabBar: "rgba(255, 255, 255, 0.98)",
      floating: "#FFFFFF",
      muted: "#EEF4FF"
    },
    text: {
      primary: "#142338",
      secondary: "#51627F",
      muted: "#7284A1",
      inverse: colors.white
    },
    border: {
      subtle: "rgba(20, 35, 56, 0.08)",
      strong: "rgba(20, 35, 56, 0.14)",
      focus: colors.blue500,
      divider: "rgba(20, 35, 56, 0.08)"
    },
    action: {
      primary: colors.blue600,
      primaryStrong: colors.blue500,
      primaryGlow: colors.blue400,
      secondary: "#EEF4FF",
      secondaryStrong: "#E4EDFC",
      ghost: "transparent",
      destructive: colors.red500
    },
    status: {
      success: colors.green500,
      successSurface: "rgba(28, 197, 131, 0.1)",
      warning: colors.orange500,
      warningSurface: "rgba(255, 154, 102, 0.12)",
      danger: colors.red500,
      dangerSurface: "rgba(244, 104, 119, 0.12)",
      info: colors.blue500,
      infoSurface: "rgba(47, 107, 255, 0.1)"
    },
    accent: {
      cyan: colors.cyan400,
      amber: colors.amber500
    },
    overlay: {
      strong: "rgba(12, 18, 28, 0.34)",
      soft: "rgba(12, 18, 28, 0.22)"
    },
    money: {
      positive: "rgba(43, 154, 110, 0.82)",
      negative: "rgba(212, 102, 118, 0.8)"
    }
  }
} as const;
