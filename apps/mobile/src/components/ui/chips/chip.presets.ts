import type { TextStyle, ViewStyle } from "react-native";
import { borders, palette, radius, sizing, spacing, surfaces, typography } from "../../../theme/tokens";

export type ChipVariant = "filter" | "status" | "info" | "compact" | "metric";
export type ChipTone = "default" | "success" | "warning" | "danger" | "info";

const baseChip: ViewStyle = {
  borderWidth: borders.width.thin,
  borderColor: palette.border,
  flexDirection: "row",
  alignItems: "center",
  justifyContent: "center",
  gap: spacing[6]
};

const baseLabel: TextStyle = {
  ...typography.caption,
  color: palette.textSecondary
};

export const chipPresets: Record<ChipVariant, { container: ViewStyle; label: TextStyle }> = {
  filter: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.standard,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.chip.horizontalPadding.standard,
      backgroundColor: surfaces.section
    },
    label: baseLabel
  },
  status: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.standard,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.chip.horizontalPadding.standard,
      backgroundColor: surfaces.section
    },
    label: {
      ...baseLabel,
      fontWeight: "500"
    }
  },
  info: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.standard,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.chip.horizontalPadding.standard,
      backgroundColor: surfaces.field
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    }
  },
  compact: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.compact,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.chip.horizontalPadding.compact,
      backgroundColor: surfaces.section
    },
    label: {
      ...baseLabel,
      fontSize: 11,
      lineHeight: 14
    }
  },
  metric: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.large,
      borderRadius: radius.medium,
      paddingHorizontal: sizing.chip.horizontalPadding.large,
      backgroundColor: surfaces.fieldStrong
    },
    label: {
      ...baseLabel,
      color: palette.textPrimary
    }
  }
};

export const chipToneStyles: Record<ChipTone, ViewStyle> = {
  default: {
    backgroundColor: surfaces.section
  },
  success: {
    backgroundColor: "rgba(29, 186, 114, 0.12)",
    borderColor: "rgba(29, 186, 114, 0.36)"
  },
  warning: {
    backgroundColor: "rgba(240, 180, 76, 0.12)",
    borderColor: "rgba(240, 180, 76, 0.34)"
  },
  danger: {
    backgroundColor: "rgba(226, 90, 90, 0.12)",
    borderColor: "rgba(226, 90, 90, 0.34)"
  },
  info: {
    backgroundColor: "rgba(154, 154, 154, 0.1)",
    borderColor: "rgba(154, 154, 154, 0.26)"
  }
};

export const chipSelectedStyle: ViewStyle = {
  borderColor: "rgba(242, 140, 40, 0.52)",
  backgroundColor: "rgba(242, 140, 40, 0.18)"
};
