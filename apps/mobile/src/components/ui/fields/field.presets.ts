import { useMemo } from "react";
import type { TextStyle, ViewStyle } from "react-native";
import { useThemeTokens } from "../../../theme/tokens";

export type FieldVariant = "text" | "search" | "select" | "currency" | "multiline";

export function useFieldPresets() {
  const { borders, palette, radius, sizing, spacing, surfaces, typography } = useThemeTokens();

  return useMemo(
    () => ({
      wrapper: {
        gap: spacing[8]
      } as ViewStyle,
      wrapperCompact: {
        gap: spacing[4]
      } as ViewStyle,
      label: {
        color: palette.textSecondary,
        ...typography.fieldLabel
      } as TextStyle,
      helper: {
        color: palette.textSecondary,
        ...typography.helper
      } as TextStyle,
      error: {
        color: palette.negative,
        ...typography.helper
      } as TextStyle,
      container: {
        minHeight: sizing.field.heights.standard,
        borderRadius: radius.medium,
        borderWidth: borders.width.thin,
        borderColor: palette.border,
        backgroundColor: surfaces.field,
        paddingHorizontal: spacing[16],
        flexDirection: "row",
        alignItems: "center",
        gap: spacing[8]
      } as ViewStyle,
      containerDense: {
        minHeight: sizing.field.heights.dense
      } as ViewStyle,
      containerFocused: {
        borderColor: palette.borderStrong,
        backgroundColor: surfaces.fieldStrong
      } as ViewStyle,
      containerError: {
        borderColor: palette.negative
      } as ViewStyle,
      input: {
        flex: 1,
        minWidth: 0,
        color: palette.textPrimary,
        ...typography.body1,
        paddingVertical: spacing[10],
        paddingRight: spacing[2],
        includeFontPadding: false
      } as TextStyle,
      multilineInput: {
        minHeight: sizing.field.multilineMinHeight,
        paddingTop: spacing[14],
        textAlignVertical: "top"
      } as TextStyle,
      affix: {
        color: palette.textSecondary,
        ...typography.body2
      } as TextStyle,
      action: {
        width: 32,
        height: 32,
        borderRadius: radius.small,
        alignItems: "center",
        justifyContent: "center"
      } as ViewStyle
    }),
    [borders, palette, radius, sizing, spacing, surfaces, typography]
  );
}
