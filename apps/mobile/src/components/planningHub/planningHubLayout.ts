import {
  CONTENT_FRAME_HEADER_GAP,
  CONTENT_FRAME_HORIZONTAL_PADDING,
  getDockAwareContentBottomInset
} from "../../layout/contentFrame";

export const PLANNING_HUB_CONTENT_PADDING_X = CONTENT_FRAME_HORIZONTAL_PADDING;
export const PLANNING_HUB_CONTENT_TOP_GAP = CONTENT_FRAME_HEADER_GAP;

export function getPlanningHubContentBottomInset(bottomSafeArea: number) {
  return getDockAwareContentBottomInset(bottomSafeArea);
}
