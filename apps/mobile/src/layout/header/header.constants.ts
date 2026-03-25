import type { TextStyle, ViewStyle } from "react-native";
import { palette, spacing, surfaces, zIndex } from "../../theme/tokens";

export const HEADER_CONSTANTS = {
  rowHeight: 56,
  secondRowHeight: 44,
  compactContentMinHeight: 56,
  touchTarget: 44,
  paddingX: 20,
  contentGap: 12,
  rowGap: 8,
  titleSubtitleGap: 4,
  leadingSlotWidth: 44,
  trailingSlotWidth: 44,
  iconSize: 20,
  iconButtonRadius: 6,
  titleMaxWidthDefault: "56%",
  titleMaxWidthCentered: "62%",
  subtitleMaxWidth: "72%",
  inlineButtonHeight: 36,
  inlineButtonRadius: 6,
  dropdownHeight: 36,
  dropdownRadius: 6,
  searchHeight: 36,
  searchRadius: 6,
  stickyDividerHeight: 1,
  stickyElevatedOpacity: 0.94,
  scrollTransitionDuration: 160,
  zIndex: zIndex.tabBar + 5,
  blurOrTintOpacity: 0.22,
  greetingTitleMaxWidth: "72%",
  greetingSubtitleMaxWidth: "100%",
  greetingTitleSubtitleGap: 2
} as const;

export const HEADER_TYPOGRAPHY = {
  headerTitle: {
    color: palette.textPrimary,
    fontSize: 17,
    lineHeight: 22,
    fontWeight: "600",
    fontFamily: "Inter-SemiBold"
  } satisfies TextStyle,
  headerSubtitle: {
    color: palette.textSecondary,
    fontSize: 12,
    lineHeight: 16,
    fontWeight: "400",
    fontFamily: "Inter-Regular"
  } satisfies TextStyle,
  headerCenteredTitle: {
    color: palette.textPrimary,
    fontSize: 17,
    lineHeight: 22,
    fontWeight: "600",
    fontFamily: "Inter-SemiBold",
    textAlign: "center"
  } satisfies TextStyle,
  headerGreetingTitle: {
    color: palette.textPrimary,
    fontSize: 22,
    lineHeight: 26,
    fontWeight: "600",
    fontFamily: "Inter-SemiBold"
  } satisfies TextStyle,
  headerDropdownText: {
    color: palette.textPrimary,
    fontSize: 14,
    lineHeight: 18,
    fontWeight: "500",
    fontFamily: "Inter-Medium"
  } satisfies TextStyle,
  headerButtonText: {
    color: palette.textPrimary,
    fontSize: 13,
    lineHeight: 16,
    fontWeight: "500",
    fontFamily: "Inter-Medium"
  } satisfies TextStyle
} as const;

export const HEADER_SURFACES = {
  shell: {
    backgroundColor: surfaces.app
  } satisfies ViewStyle,
  divider: {
    backgroundColor: `rgba(242,140,40,${HEADER_CONSTANTS.blurOrTintOpacity})`
  } satisfies ViewStyle,
  iconButton: {
    width: HEADER_CONSTANTS.touchTarget,
    height: HEADER_CONSTANTS.touchTarget,
    borderRadius: HEADER_CONSTANTS.iconButtonRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(17,17,17,0.96)",
    alignItems: "center",
    justifyContent: "center"
  } satisfies ViewStyle,
  compactButton: {
    minHeight: HEADER_CONSTANTS.inlineButtonHeight,
    borderRadius: HEADER_CONSTANTS.inlineButtonRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: 14,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  } satisfies ViewStyle,
  inputSlot: {
    minHeight: HEADER_CONSTANTS.dropdownHeight,
    borderRadius: HEADER_CONSTANTS.dropdownRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  } satisfies ViewStyle
} as const;

export const HEADER_PAGE_PRESETS = {
  Home: "primaryGreeting",
  Accounts: "primaryTwoRowSelector",
  Activity: "primaryTwoRowSearch",
  Cashflow: "primaryDefault",
  Calendar: "primaryDefault",
  "NS Companion": "primaryDefault",
  Plans: "primaryDefault",
  Analytics: "primaryTwoRowSelector",
  Categories: "primaryTwoRowSearch"
} as const;

export const HEADER_FOUNDATION_NOTE = {
  constantsPath: "apps/mobile/src/layout/header/header.constants.ts",
  presetsPath: "apps/mobile/src/layout/header/header.presets.ts"
} as const;
