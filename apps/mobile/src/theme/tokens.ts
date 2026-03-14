export const palette = {
  appBackground: "#050D19",
  elevatedBackground: "#0C192B",
  cardSurface: "#13263D",
  cardSurfaceMuted: "#162D48",
  glassSurface: "rgba(18, 36, 58, 0.88)",
  tabBarSurface: "rgba(10, 20, 34, 0.94)",
  border: "rgba(213, 229, 255, 0.14)",
  borderStrong: "rgba(213, 229, 255, 0.24)",
  primary: "#2F6BFF",
  primaryGlow: "#7FAEFF",
  accent: "#6FD7FF",
  textPrimary: "#E2ECFF",
  textSecondary: "#A7B6D1",
  success: "#1CC583",
  caution: "#FF9A66",
  negative: "#F46877",
  overlay: "rgba(3, 9, 18, 0.76)"
} as const;

export const surfaces = {
  app: palette.appBackground,
  section: "rgba(12, 25, 43, 0.7)",
  card: palette.glassSurface,
  floating: "rgba(13, 26, 44, 0.92)",
  sheet: "rgba(12, 25, 43, 0.98)",
  tabBar: palette.tabBarSurface
} as const;

export const spacing = {
  4: 4,
  8: 8,
  12: 12,
  16: 16,
  20: 20,
  24: 24,
  32: 32,
  40: 40
} as const;

export const radius = {
  small: 12,
  medium: 18,
  large: 24,
  hero: 28
} as const;

export const layout = {
  screenHorizontalPadding: spacing[20],
  screenTopPadding: spacing[20],
  sectionGap: spacing[20],
  listGap: spacing[12],
  cardPadding: spacing[16]
} as const;

export const controls = {
  primaryHeight: 52,
  fieldHeight: 52,
  iconButtonSize: 42,
  compactRadius: 14,
  fieldRadius: radius.medium,
  buttonRadius: radius.medium,
  controlSurface: "rgba(18,36,58,0.78)",
  controlSurfaceMuted: "rgba(18,36,58,0.68)",
  controlSurfaceStrong: "rgba(18,36,58,0.84)",
  primaryFill: "rgba(47,107,255,0.92)",
  primaryBorder: "rgba(127,174,255,0.4)",
  activeFill: "rgba(47,107,255,0.26)",
  activeBorder: "rgba(127,174,255,0.36)",
  pressedScale: 0.985
} as const;

export const navigation = {
  floatingTabBarHeight: 74,
  floatingTabBarSideInset: 16,
  floatingTabBarOffset: 6,
  floatingTabBarBreathingRoom: 12,
  floatingTabBarContentGap: 12,
  floatingFabLift: 14,
  floatingFabClearance: 66
} as const;

export const shadows = {
  soft: {
    shadowColor: "#000000",
    shadowOpacity: 0.18,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 8 },
    elevation: 7
  },
  floating: {
    shadowColor: "#000000",
    shadowOpacity: 0.28,
    shadowRadius: 18,
    shadowOffset: { width: 0, height: 12 },
    elevation: 11
  },
  glow: {
    shadowColor: palette.primaryGlow,
    shadowOpacity: 0.2,
    shadowRadius: 14,
    shadowOffset: { width: 0, height: 8 },
    elevation: 7
  }
} as const;

export const typography = {
  displayXL: {
    fontSize: 42,
    lineHeight: 48,
    fontWeight: "700" as const
  },
  displayL: {
    fontSize: 32,
    lineHeight: 38,
    fontWeight: "700" as const
  },
  title1: {
    fontSize: 24,
    lineHeight: 30,
    fontWeight: "700" as const
  },
  title2: {
    fontSize: 18,
    lineHeight: 24,
    fontWeight: "600" as const
  },
  body1: {
    fontSize: 15,
    lineHeight: 22,
    fontWeight: "500" as const
  },
  body2: {
    fontSize: 14,
    lineHeight: 20,
    fontWeight: "400" as const
  },
  caption: {
    fontSize: 12,
    lineHeight: 16,
    fontWeight: "500" as const
  },
  button: {
    fontSize: 15,
    lineHeight: 20,
    fontWeight: "600" as const
  },
  display: {
    fontSize: 42,
    lineHeight: 48,
    fontWeight: "700" as const
  },
  title: {
    fontSize: 24,
    lineHeight: 30,
    fontWeight: "700" as const
  },
  sectionTitle: {
    fontSize: 18,
    lineHeight: 24,
    fontWeight: "600" as const
  },
  body: {
    fontSize: 15,
    lineHeight: 22,
    fontWeight: "500" as const
  },
  bodyStrong: {
    fontSize: 15,
    lineHeight: 22,
    fontWeight: "600" as const
  }
} as const;

export const motion = {
  quick: 140,
  standard: 220,
  slow: 320
} as const;

export const gradients = {
  hero: ["#1E4DD9", "#295FED", "#122A4E"] as const,
  accountCard: ["rgba(127,174,255,0.14)", "rgba(111,215,255,0.06)"] as const
};
