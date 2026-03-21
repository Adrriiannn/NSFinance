import { Platform } from "react-native";
import { navigation } from "./tokens";

const ANDROID_NAV_BUTTONS_MIN_INSET = 24;

export function getEffectiveBottomSystemInset(bottomSafeArea: number) {
  if (Platform.OS !== "android") {
    return bottomSafeArea;
  }

  return bottomSafeArea >= ANDROID_NAV_BUTTONS_MIN_INSET ? bottomSafeArea : 0;
}

export function getFloatingTabBarInset(bottomSafeArea: number, extra = 0) {
  const effectiveBottomInset = getEffectiveBottomSystemInset(bottomSafeArea);
  return (
    Math.max(effectiveBottomInset, 8) +
    navigation.floatingTabBarHeight +
    extra
  );
}

export function getFloatingTabBarContentInset(bottomSafeArea: number, visualGap = 8) {
  const effectiveBottomInset = getEffectiveBottomSystemInset(bottomSafeArea);
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
