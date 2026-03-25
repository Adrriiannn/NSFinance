import { Platform } from "react-native";
import { useMemo } from "react";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { navigation } from "./tokens";

const ANDROID_NAV_BUTTONS_MIN_INSET = 24;
const BOTTOM_SCROLL_EXTRA_GAP = 6;
const BOTTOM_ACTION_STANDARD_EXTRA_GAP = 6;
const BOTTOM_ACTION_TIGHT_EXTRA_GAP = 2;
const BOTTOM_DRAWER_EXTRA_GAP = 2;

export type RuntimeBottomInsetPolicy = {
  rawBottomInset: number;
  effectiveBottomInset: number;
  hasBottomSystemInset: boolean;
  bottomContentInset: number;
  bottomScrollableInset: number;
  bottomActionInset: number;
  bottomActionInsetTight: number;
  bottomDrawerInset: number;
  bottomFloatingInset: number;
};

export function getEffectiveBottomSystemInset(bottomSafeArea: number) {
  if (Platform.OS !== "android") {
    return bottomSafeArea;
  }

  return bottomSafeArea >= ANDROID_NAV_BUTTONS_MIN_INSET ? bottomSafeArea : 0;
}

export function resolveRuntimeBottomInsetPolicy(bottomSafeArea: number): RuntimeBottomInsetPolicy {
  const effectiveBottomInset = getEffectiveBottomSystemInset(bottomSafeArea);
  const hasBottomSystemInset = effectiveBottomInset > 0;

  return {
    rawBottomInset: bottomSafeArea,
    effectiveBottomInset,
    hasBottomSystemInset,
    bottomContentInset: effectiveBottomInset,
    bottomScrollableInset: hasBottomSystemInset ? effectiveBottomInset + BOTTOM_SCROLL_EXTRA_GAP : 0,
    bottomActionInset: hasBottomSystemInset ? effectiveBottomInset + BOTTOM_ACTION_STANDARD_EXTRA_GAP : 0,
    bottomActionInsetTight: hasBottomSystemInset ? effectiveBottomInset + BOTTOM_ACTION_TIGHT_EXTRA_GAP : 0,
    bottomDrawerInset: hasBottomSystemInset ? effectiveBottomInset + BOTTOM_DRAWER_EXTRA_GAP : 0,
    bottomFloatingInset: effectiveBottomInset
  };
}

export function useRuntimeBottomInsetPolicy(): RuntimeBottomInsetPolicy {
  const insets = useSafeAreaInsets();

  return useMemo(
    () => resolveRuntimeBottomInsetPolicy(insets.bottom),
    [insets.bottom]
  );
}

export function getFloatingTabBarInset(bottomSafeArea: number, extra = 0) {
  const { effectiveBottomInset } = resolveRuntimeBottomInsetPolicy(bottomSafeArea);
  return (
    Math.max(effectiveBottomInset, 8) +
    navigation.floatingTabBarHeight +
    extra
  );
}

export function getFloatingTabBarContentInset(bottomSafeArea: number, visualGap = 8) {
  const { effectiveBottomInset } = resolveRuntimeBottomInsetPolicy(bottomSafeArea);
  return (
    Math.max(effectiveBottomInset, 8) +
    navigation.floatingTabBarHeight +
    visualGap
  );
}

export function getFloatingFabOffset(bottomSafeArea: number, extra = 0) {
  return (
    getFloatingTabBarInset(bottomSafeArea, 0) +
    navigation.floatingFabLift +
    extra
  );
}
