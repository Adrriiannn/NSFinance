import type { TextStyle, ViewStyle } from "react-native";
import { borders, palette, radius, sizing, spacing, surfaces, typography } from "../../../theme/tokens";

export const rowPresets = {
  container: {
    minHeight: sizing.row.heights.standard,
    borderRadius: radius.medium,
    borderWidth: borders.width.thin,
    borderColor: palette.border,
    backgroundColor: surfaces.section,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  } as ViewStyle,
  compact: {
    minHeight: sizing.row.heights.compact,
    paddingVertical: spacing[10]
  } as ViewStyle,
  dense: {
    minHeight: sizing.row.heights.compact,
    paddingHorizontal: spacing[10],
    paddingVertical: spacing[10]
  } as ViewStyle,
  selectable: {
    opacity: 0.94
  } as ViewStyle,
  divider: {
    height: borders.width.hairline,
    backgroundColor: palette.border
  } as ViewStyle,
  leadingIcon: {
    width: 34,
    height: 34,
    borderRadius: 17,
    alignItems: "center",
    justifyContent: "center"
  } as ViewStyle,
  title: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  } as TextStyle,
  subtitle: {
    color: palette.textSecondary,
    ...typography.body2
  } as TextStyle,
  trailing: {
    color: palette.textSecondary,
    ...typography.caption
  } as TextStyle
};
