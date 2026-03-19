import { navigation, spacing } from "../theme/tokens";

export const CONTENT_FRAME_HORIZONTAL_PADDING = spacing[12];
export const CONTENT_FRAME_HEADER_GAP = spacing[10];
export const CONTENT_FRAME_DOCK_GAP = spacing[12];
export const CONTENT_FRAME_PLAIN_BOTTOM_GAP = spacing[8];

export function getDockAwareContentBottomInset(_bottomSafeArea: number) {
  return navigation.floatingTabBarHeight + CONTENT_FRAME_DOCK_GAP;
}

export function getPlainContentBottomInset(bottomSafeArea: number) {
  return Math.max(bottomSafeArea, CONTENT_FRAME_PLAIN_BOTTOM_GAP);
}
