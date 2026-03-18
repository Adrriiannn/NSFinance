import type { TextStyle, ViewStyle } from "react-native";
import { borders, palette, radius, shadows, sizing, spacing, surfaces, typography } from "../../../theme/tokens";

export type FeedbackTone = "error" | "success" | "warning" | "info";

export const bannerPresets: Record<FeedbackTone, ViewStyle> = {
  error: {
    backgroundColor: "rgba(71, 22, 22, 0.94)",
    borderColor: "rgba(255, 120, 120, 0.54)"
  },
  success: {
    backgroundColor: "rgba(10, 58, 40, 0.92)",
    borderColor: "rgba(28, 197, 131, 0.5)"
  },
  warning: {
    backgroundColor: "rgba(72, 50, 12, 0.92)",
    borderColor: "rgba(255, 177, 77, 0.46)"
  },
  info: {
    backgroundColor: "rgba(12, 34, 68, 0.94)",
    borderColor: "rgba(127, 174, 255, 0.52)"
  }
};

export const feedbackPresets = {
  banner: {
    borderRadius: radius.large,
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
    borderRadius: radius.pill,
    borderWidth: borders.width.thin,
    minHeight: sizing.button.heights.compact + 2,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[16],
    ...shadows.floating
  } as ViewStyle,
  emptyState: {
    borderRadius: radius.large,
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
    borderRadius: 28,
    backgroundColor: "rgba(110, 168, 255, 0.24)"
  } as ViewStyle,
  skeleton: {
    borderRadius: radius.small,
    backgroundColor: palette.cardSurfaceMuted
  } as ViewStyle
};
