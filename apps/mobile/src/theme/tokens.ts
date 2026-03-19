import { activeTheme, gradients, shadows } from "./semantic";
import { borders } from "./tokens/borders";
import { opacity } from "./tokens/opacity";
import { radius as radiusScale } from "./tokens/radius";
import { sizing } from "./tokens/sizing";
import { spacing } from "./tokens/spacing";
import { typography as typographyScale } from "./tokens/typography";
import { motion } from "./tokens/motion";
import { zIndex } from "./tokens/zIndex";

const theme = activeTheme;

export const palette = {
  appBackground: theme.colors.canvas,
  elevatedBackground: theme.colors.elevatedCanvas,
  cardSurface: theme.colors.surface.level1,
  cardSurfaceMuted: theme.colors.surface.fieldStrong,
  glassSurface: theme.colors.surface.level1,
  tabBarSurface: theme.colors.surface.tabBar,
  border: theme.colors.border.subtle,
  borderStrong: theme.colors.border.strong,
  primary: theme.colors.action.primary,
  primaryGlow: theme.colors.action.primaryGlow,
  accent: theme.colors.accent.cyan,
  textPrimary: theme.colors.text.primary,
  textSecondary: theme.colors.text.secondary,
  success: theme.colors.status.success,
  caution: theme.colors.status.warning,
  negative: theme.colors.status.danger,
  overlay: theme.colors.overlay.strong
} as const;

export const surfaces = {
  app: theme.colors.canvas,
  section: theme.colors.surface.level0,
  card: theme.colors.surface.level1,
  floating: theme.colors.surface.floating,
  sheet: theme.colors.surface.level2,
  tabBar: theme.colors.surface.tabBar,
  field: theme.colors.surface.field,
  fieldStrong: theme.colors.surface.fieldStrong,
  muted: theme.colors.surface.muted
} as const;

export const radius = {
  small: radiusScale.small,
  medium: radiusScale.large,
  large: radiusScale.xl,
  hero: radiusScale.hero,
  pill: radiusScale.pill,
  fab: radiusScale.fab
} as const;

export const layout = {
  screenHorizontalPadding: spacing[12],
  screenTopPadding: spacing[20],
  sectionGap: spacing[20],
  listGap: spacing[12],
  cardPadding: sizing.card.padding.standard
} as const;

export const controls = {
  primaryHeight: sizing.button.heights.standard,
  compactHeight: sizing.button.heights.compact,
  fieldHeight: sizing.field.heights.standard,
  denseFieldHeight: sizing.field.heights.dense,
  iconButtonSize: sizing.iconButton.standard,
  compactRadius: radiusScale.small,
  fieldRadius: radiusScale.medium,
  buttonRadius: radiusScale.hero,
  controlSurface: surfaces.field,
  controlSurfaceMuted: surfaces.field,
  controlSurfaceStrong: surfaces.fieldStrong,
  primaryFill: theme.colors.action.primary,
  primaryBorder: "rgba(127, 174, 255, 0.4)",
  activeFill: "rgba(47, 107, 255, 0.26)",
  activeBorder: "rgba(127, 174, 255, 0.36)",
  pressedScale: 0.985
} as const;

export const navigation = {
  floatingTabBarHeight: sizing.tabBar.height,
  floatingTabBarSideInset: 0,
  floatingTabBarOffset: 0,
  floatingTabBarBreathingRoom: 0,
  floatingTabBarContentGap: spacing[8],
  floatingFabLift: spacing[14],
  floatingFabClearance: 66
} as const;

export const typography = {
  displayXL: typographyScale.display,
  displayL: typographyScale.display,
  title1: typographyScale.screenTitle,
  title2: typographyScale.sectionTitle,
  body1: typographyScale.body,
  body2: typographyScale.secondary,
  caption: typographyScale.caption,
  button: typographyScale.buttonLabel,
  display: typographyScale.display,
  title: typographyScale.screenTitle,
  sectionTitle: typographyScale.sectionTitle,
  body: typographyScale.body,
  bodyStrong: {
    ...typographyScale.body,
    fontWeight: "600" as const
  },
  cardTitle: typographyScale.cardTitle,
  amount: typographyScale.amount,
  amountLarge: typographyScale.amountLarge,
  label: typographyScale.label,
  buttonLabel: typographyScale.buttonLabel,
  fieldLabel: typographyScale.fieldLabel,
  helper: typographyScale.helper
} as const;

export {
  theme,
  spacing,
  sizing,
  shadows,
  gradients,
  borders,
  opacity,
  motion,
  zIndex
};
