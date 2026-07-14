import { radius, spacing, sizing } from "../../theme/tokens";
import type { AdaptiveHeightClass, AdaptiveWidthClass } from "./adaptive.types";

export const ADAPTIVE_WIDTH_BREAKPOINTS = {
  regular: 390,
  expanded: 520
} as const;

export const ADAPTIVE_HEIGHT_BREAKPOINTS = {
  regular: 760,
  tall: 900
} as const;

export const ADAPTIVE_TOKENS = {
  contentPaddingX: {
    compact: spacing[12],
    regular: spacing[12],
    expanded: spacing[12]
  } as Record<AdaptiveWidthClass, number>,
  maxContentWidth: {
    compact: 480,
    regular: 560,
    expanded: 640
  } as Record<AdaptiveWidthClass, number>,
  header: {
    topGap: {
      compact: spacing[10],
      regular: spacing[16],
      tall: spacing[20]
    } as Record<AdaptiveHeightClass, number>,
    bottomGap: {
      compact: spacing[14],
      regular: spacing[16],
      tall: spacing[20]
    } as Record<AdaptiveHeightClass, number>,
    titleGap: {
      compact: spacing[4],
      regular: spacing[6],
      tall: spacing[8]
    } as Record<AdaptiveHeightClass, number>,
    actionSize: sizing.iconButton.standard
  },
  sectionGap: {
    compact: spacing[16],
    regular: spacing[20],
    tall: spacing[24]
  } as Record<AdaptiveHeightClass, number>,
  tabBar: {
    baseHeight: sizing.tabBar.height,
    radius: radius.hero,
    outerMarginX: {
      compact: spacing[12],
      regular: spacing[16],
      expanded: spacing[20]
    } as Record<AdaptiveWidthClass, number>,
    outerMarginBottom: {
      compact: spacing[8],
      regular: spacing[12],
      tall: spacing[16]
    } as Record<AdaptiveHeightClass, number>,
    innerPaddingX: spacing[8],
    innerPaddingTop: spacing[10],
    labelGap: spacing[4]
  },
  fab: {
    size: sizing.fab.size,
    rightMargin: {
      compact: spacing[12],
      regular: spacing[16],
      expanded: spacing[20]
    } as Record<AdaptiveWidthClass, number>,
    gapAboveTabBar: spacing[12],
    dockedVisibleWidth: 24,
    dockAnimationDurationMs: 220,
    swipeToDockThreshold: 28
  },
  contentReserve: {
    comfortSpacing: spacing[16]
  },
  menuTriggerSize: 42
} as const;
