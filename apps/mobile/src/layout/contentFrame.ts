import { resolveRuntimeBottomInsetPolicy } from "../theme/insets";
import { navigation, spacing } from "../theme/tokens";

export const CONTENT_FRAME_HORIZONTAL_PADDING = spacing[12];
export const CONTENT_FRAME_HEADER_GAP = spacing[10];
export const CONTENT_FRAME_DOCK_GAP = spacing[12];
export const CONTENT_FRAME_PLAIN_BOTTOM_GAP = 0;

export function getDockAwareContentBottomInset(bottomSafeArea: number) {
  const policy = resolveRuntimeBottomInsetPolicy(bottomSafeArea);
  return (
    policy.bottomContentInset +
    navigation.floatingTabBarHeight +
    CONTENT_FRAME_DOCK_GAP
  );
}

export function getPlainContentBottomInset(bottomSafeArea: number) {
  const policy = resolveRuntimeBottomInsetPolicy(bottomSafeArea);
  return policy.bottomContentInset + CONTENT_FRAME_PLAIN_BOTTOM_GAP;
}
