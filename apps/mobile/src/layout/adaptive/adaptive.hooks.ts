import { createContext, useContext } from "react";
import { useWindowDimensions } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { getEffectiveBottomSystemInset } from "../../theme/insets";
import {
  ADAPTIVE_HEIGHT_BREAKPOINTS,
  ADAPTIVE_TOKENS,
  ADAPTIVE_WIDTH_BREAKPOINTS
} from "./adaptive.constants";
import type {
  AdaptiveHeightClass,
  AdaptiveLayoutContextValue,
  AdaptiveLayoutMetrics,
  AdaptiveWidthClass
} from "./adaptive.types";

export const AdaptiveLayoutContext = createContext<AdaptiveLayoutContextValue | null>(null);

function resolveWidthClass(usableWidth: number): AdaptiveWidthClass {
  if (usableWidth < ADAPTIVE_WIDTH_BREAKPOINTS.regular) {
    return "compact";
  }

  if (usableWidth < ADAPTIVE_WIDTH_BREAKPOINTS.expanded) {
    return "regular";
  }

  return "expanded";
}

function resolveHeightClass(usableHeight: number): AdaptiveHeightClass {
  if (usableHeight < ADAPTIVE_HEIGHT_BREAKPOINTS.regular) {
    return "compact";
  }

  if (usableHeight < ADAPTIVE_HEIGHT_BREAKPOINTS.tall) {
    return "regular";
  }

  return "tall";
}

export function useAdaptiveLayoutMetrics(): AdaptiveLayoutMetrics {
  const { width, height } = useWindowDimensions();
  const safeAreaInsets = useSafeAreaInsets();
  const effectiveBottomInset = getEffectiveBottomSystemInset(safeAreaInsets.bottom);
  const usableWidth = width - safeAreaInsets.left - safeAreaInsets.right;
  const usableHeight = height - safeAreaInsets.top - safeAreaInsets.bottom;
  const widthClass = resolveWidthClass(usableWidth);
  const heightClass = resolveHeightClass(usableHeight);
  const contentHorizontalPadding = ADAPTIVE_TOKENS.contentPaddingX[widthClass];
  const maxContentWidth = Math.min(
    ADAPTIVE_TOKENS.maxContentWidth[widthClass],
    Math.max(usableWidth, 320)
  );
  const planningHubButtonSize = ADAPTIVE_TOKENS.planningHub.size[widthClass];
  const planningHubOverlap = Math.round(
    planningHubButtonSize * ADAPTIVE_TOKENS.planningHub.overlapRatio
  );
  const planningHubLift = planningHubButtonSize - planningHubOverlap;
  const tabBarHeight = ADAPTIVE_TOKENS.tabBar.baseHeight;
  const tabBarMargins = {
    horizontal: ADAPTIVE_TOKENS.tabBar.outerMarginX[widthClass],
    bottom: ADAPTIVE_TOKENS.tabBar.outerMarginBottom[heightClass] + effectiveBottomInset
  };
  const floatingAssistantRightMargin = ADAPTIVE_TOKENS.fab.rightMargin[widthClass];
  const floatingAssistantGapAboveTabBar = ADAPTIVE_TOKENS.fab.gapAboveTabBar;
  const floatingAssistantBottomOffset =
    ADAPTIVE_TOKENS.tabBar.baseHeight + floatingAssistantGapAboveTabBar + effectiveBottomInset;
  const contentBottomInset =
    Math.max(effectiveBottomInset, 8) +
    ADAPTIVE_TOKENS.tabBar.baseHeight +
    ADAPTIVE_TOKENS.contentReserve.comfortSpacing;

  return {
    safeAreaInsets,
    screenWidth: width,
    screenHeight: height,
    usableWidth,
    usableHeight,
    widthClass,
    heightClass,
    contentHorizontalPadding,
    maxContentWidth,
    sectionGap: ADAPTIVE_TOKENS.sectionGap[heightClass],
    headerTopGap: ADAPTIVE_TOKENS.header.topGap[heightClass],
    headerBottomGap: ADAPTIVE_TOKENS.header.bottomGap[heightClass],
    headerTitleGap: ADAPTIVE_TOKENS.header.titleGap[heightClass],
    headerActionSize: ADAPTIVE_TOKENS.header.actionSize,
    tabBarHeight,
    tabBarRadius: ADAPTIVE_TOKENS.tabBar.radius,
    tabBarMargins,
    planningHubButtonSize,
    planningHubOverlap,
    planningHubLift,
    floatingAssistantSize: ADAPTIVE_TOKENS.fab.size,
    floatingAssistantRightMargin,
    floatingAssistantGapAboveTabBar,
    floatingAssistantBottomOffset,
    floatingAssistantDockedVisibleWidth: ADAPTIVE_TOKENS.fab.dockedVisibleWidth,
    floatingAssistantDockAnimationDurationMs: ADAPTIVE_TOKENS.fab.dockAnimationDurationMs,
    floatingAssistantSwipeToDockThreshold: ADAPTIVE_TOKENS.fab.swipeToDockThreshold,
    contentBottomInset
  };
}

export function useAdaptiveShell() {
  const context = useContext(AdaptiveLayoutContext);

  if (!context) {
    throw new Error("useAdaptiveShell must be used within AdaptiveAppShell.");
  }

  return context;
}

export function useOptionalAdaptiveShell() {
  return useContext(AdaptiveLayoutContext);
}
