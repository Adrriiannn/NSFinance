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
      borderRadius: radius.pill,
      paddingHorizontal: sizing.chip.horizontalPadding.standard,
      backgroundColor: surfaces.section
    },
    label: baseLabel
  },
  status: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.standard,
      borderRadius: radius.pill,
      paddingHorizontal: sizing.chip.horizontalPadding.standard,
      backgroundColor: surfaces.section
    },
    label: {
      ...baseLabel,
      fontWeight: "700"
    }
  },
  info: {
    container: {
      ...baseChip,
      minHeight: sizing.chip.heights.standard,
      borderRadius: radius.pill,
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
      borderRadius: radius.pill,
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
      borderRadius: radius.pill,
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
    backgroundColor: "rgba(28, 197, 131, 0.16)",
    borderColor: "rgba(28, 197, 131, 0.32)"
  },
  warning: {
    backgroundColor: "rgba(255, 154, 102, 0.16)",
    borderColor: "rgba(255, 154, 102, 0.3)"
  },
  danger: {
    backgroundColor: "rgba(244, 104, 119, 0.16)",
    borderColor: "rgba(244, 104, 119, 0.3)"
  },
  info: {
    backgroundColor: "rgba(47, 107, 255, 0.2)",
    borderColor: "rgba(127, 174, 255, 0.4)"
  }
};

export const chipSelectedStyle: ViewStyle = {
  borderColor: "rgba(127, 174, 255, 0.62)",
  backgroundColor: "rgba(47, 107, 255, 0.28)"
};
