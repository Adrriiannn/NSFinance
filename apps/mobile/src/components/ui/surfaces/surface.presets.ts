import { useMemo } from "react";
import type { ViewStyle } from "react-native";
import { useThemeTokens } from "../../../theme/tokens";

export function useSurfacePresets() {
  const { borders, palette, radius, shadows, sizing, spacing, surfaces, zIndex } = useThemeTokens();

  return useMemo(
    () => ({
      overlay: {
        flex: 1,
        backgroundColor: palette.overlay
      } as ViewStyle,
      modalSheet: {
        borderTopLeftRadius: radius.medium,
        borderTopRightRadius: radius.medium,
        borderWidth: borders.width.thin,
        borderColor: palette.border,
        backgroundColor: surfaces.sheet,
        paddingHorizontal: spacing[20],
        paddingTop: spacing[20],
        paddingBottom: spacing[24],
        gap: spacing[16],
        ...shadows.soft
      } as ViewStyle,
      modalHandle: {
        alignSelf: "center",
        width: sizing.modalSheet.handleWidth,
        height: sizing.modalSheet.handleHeight,
        borderRadius: radius.small,
        backgroundColor: palette.borderStrong
      } as ViewStyle,
      dialog: {
        width: "100%",
        maxWidth: 360,
        borderRadius: radius.medium,
        borderWidth: borders.width.thin,
        borderColor: palette.border,
        backgroundColor: surfaces.sheet,
        padding: spacing[20],
        gap: spacing[16],
        ...shadows.soft
      } as ViewStyle,
      fab: {
        minHeight: sizing.fab.extendedHeight,
        borderRadius: radius.medium,
        borderWidth: borders.width.thin,
        borderColor: palette.borderStrong,
        backgroundColor: surfaces.floating,
        ...shadows.soft
      } as ViewStyle,
      fabCompact: {
        width: sizing.fab.size,
        height: sizing.fab.size,
        paddingHorizontal: 0
      } as ViewStyle,
      tabBarShell: {
        minHeight: sizing.tabBar.height,
        borderTopLeftRadius: radius.small,
        borderTopRightRadius: radius.small,
        borderBottomLeftRadius: 0,
        borderBottomRightRadius: 0,
        borderWidth: borders.width.thin,
        borderColor: palette.border,
        backgroundColor: surfaces.tabBar,
        flexDirection: "row",
        alignItems: "center",
        paddingHorizontal: spacing[8],
        paddingVertical: spacing[8],
        ...shadows.none
      } as ViewStyle,
      tabBarDocked: {
        position: "absolute",
        left: 0,
        right: 0,
        bottom: -2,
        zIndex: zIndex.tabBar
      } as ViewStyle
    }),
    [borders, palette, radius, shadows, sizing, spacing, surfaces, zIndex]
  );
}
