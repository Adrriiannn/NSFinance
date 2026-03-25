import { useMemo } from "react";
import type { TextStyle, ViewStyle } from "react-native";
import { useThemeTokens } from "../../../theme/tokens";

export type FeedbackTone = "error" | "success" | "warning" | "info";

export function useFeedbackPresets() {
  const { borders, palette, radius, sizing, spacing, surfaces, theme, typography } = useThemeTokens();

  return useMemo(() => {
    const bannerPresets: Record<FeedbackTone, ViewStyle> = {
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

    const snackbarTonePresetsDark: Record<FeedbackTone, ViewStyle> = {
      error: {
        backgroundColor: "#5A2626"
      },
      success: {
        backgroundColor: "#1E4F3A"
      },
      warning: {
        backgroundColor: "#5A4A22"
      },
      info: {
        backgroundColor: "#3E3E3E"
      }
    };

    const snackbarTonePresetsLight: Record<FeedbackTone, ViewStyle> = {
      error: {
        backgroundColor: "#F9DEDE"
      },
      success: {
        backgroundColor: "#D7F1E5"
      },
      warning: {
        backgroundColor: "#FDEBCF"
      },
      info: {
        backgroundColor: "#EAEAEA"
      }
    };

    const snackbarTonePresets: Record<FeedbackTone, ViewStyle> = theme.isDark
      ? snackbarTonePresetsDark
      : snackbarTonePresetsLight;

    const feedbackPresets = {
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
        borderWidth: 0,
        minHeight: sizing.button.heights.compact + 4,
        alignItems: "center",
        justifyContent: "center",
        paddingHorizontal: spacing[16]
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

    return {
      bannerPresets,
      snackbarTonePresets,
      feedbackPresets
    };
  }, [borders, palette, radius, sizing, spacing, surfaces, theme.isDark, typography]);
}
