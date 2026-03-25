import type { TextStyle, ViewStyle } from "react-native";
import { borders, palette, radius, shadows, sizing, spacing, surfaces, typography } from "../../../theme/tokens";

export type FeedbackTone = "error" | "success" | "warning" | "info";

export const bannerPresets: Record<FeedbackTone, ViewStyle> = {
  error: {
    backgroundColor: "rgba(226, 90, 90, 0.12)",
    borderColor: "rgba(226, 90, 90, 0.52)"
  },
  success: {
    backgroundColor: "rgba(29, 186, 114, 0.12)",
    borderColor: "rgba(29, 186, 114, 0.42)"
  },
  warning: {
    backgroundColor: "rgba(240, 180, 76, 0.12)",
    borderColor: "rgba(240, 180, 76, 0.42)"
  },
  info: {
    backgroundColor: "rgba(154, 154, 154, 0.1)",
    borderColor: "rgba(154, 154, 154, 0.34)"
  }
};

export const feedbackPresets = {
  banner: {
    borderRadius: radius.medium,
    borderWidth: borders.width.thin,
    paddingHorizontal: spacing[16],
    paddingVertical: spacing[12],
    gap: spacing[6]
  } as ViewStyle,
  bannerTitle: {
    color: palette.textPrimary,
    ...typography.label
  } as TextStyle,
  bannerMessage: {
    color: palette.textSecondary,
    ...typography.body2
  } as TextStyle,
  snackbar: {
    borderRadius: radius.medium,
    borderWidth: borders.width.thin,
    minHeight: sizing.button.heights.compact + 2,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[16],
    ...shadows.floating
  } as ViewStyle,
  emptyState: {
    borderRadius: radius.medium,
    borderWidth: borders.width.thin,
    borderColor: palette.border,
    backgroundColor: surfaces.muted,
    padding: spacing[20],
    alignItems: "center",
    gap: spacing[8]
  } as ViewStyle,
  emptyStateOrb: {
    width: 56,
    height: 56,
    borderRadius: radius.medium,
    backgroundColor: "rgba(242, 140, 40, 0.16)"
  } as ViewStyle,
  skeleton: {
    borderRadius: radius.small,
    backgroundColor: palette.cardSurfaceMuted
  } as ViewStyle
};
