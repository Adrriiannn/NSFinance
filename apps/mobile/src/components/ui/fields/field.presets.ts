import type { TextStyle, ViewStyle } from "react-native";
import { borders, palette, radius, sizing, spacing, surfaces, typography } from "../../../theme/tokens";

export type FieldVariant = "text" | "search" | "select" | "currency" | "multiline";

export const fieldPresets = {
  wrapper: {
    gap: spacing[8]
  },
  wrapperCompact: {
    gap: spacing[4]
  },
  label: {
    color: palette.textSecondary,
    ...typography.fieldLabel
  },
  helper: {
    color: palette.textSecondary,
    ...typography.helper
  },
  error: {
    color: palette.negative,
    ...typography.helper
  },
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
    color: palette.textPrimary,
    ...typography.body1,
    paddingVertical: spacing[10]
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
};
