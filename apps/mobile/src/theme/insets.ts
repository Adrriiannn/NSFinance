import { navigation } from "./tokens";

export function getFloatingTabBarInset(bottomSafeArea: number, extra = 0) {
  return (
    Math.max(bottomSafeArea, 8) +
    navigation.floatingTabBarOffset +
    navigation.floatingTabBarHeight +
    navigation.floatingTabBarBreathingRoom +
    extra
  );
}

export function getFloatingTabBarContentInset(bottomSafeArea: number, visualGap = 8) {
  return (
    Math.max(bottomSafeArea, 8) +
    navigation.floatingTabBarOffset +
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
