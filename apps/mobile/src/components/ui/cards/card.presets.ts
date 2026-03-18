import type { ViewStyle } from "react-native";
import { borders, palette, radius, shadows, sizing, surfaces } from "../../../theme/tokens";

export type CardVariant = "default" | "elevated" | "hero" | "insight" | "compact" | "outlined";

const baseCard: ViewStyle = {
  backgroundColor: surfaces.card,
  borderWidth: borders.width.thin,
  borderColor: palette.border,
  overflow: "hidden"
};

export const cardPresets: Record<CardVariant, ViewStyle> = {
  default: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.large,
    padding: sizing.card.padding.standard,
    ...shadows.soft
  },
  elevated: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.large,
    padding: sizing.card.padding.standard,
    ...shadows.raised
  },
  hero: {
    ...baseCard,
    minHeight: sizing.card.minHeights.hero,
    borderRadius: radius.hero,
    padding: sizing.card.padding.hero,
    ...shadows.raised
  },
  insight: {
    ...baseCard,
    minHeight: sizing.card.minHeights.insight,
    borderRadius: radius.large,
    padding: sizing.card.padding.standard,
    backgroundColor: surfaces.field,
    ...shadows.soft
  },
  compact: {
    ...baseCard,
    minHeight: sizing.card.minHeights.compact,
    borderRadius: radius.medium,
    padding: sizing.card.padding.compact,
    ...shadows.soft
  },
  outlined: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.large,
    padding: sizing.card.padding.standard,
    backgroundColor: "transparent"
  }
};

export const cardStateStyles = {
  pressed: {
    opacity: 0.94,
    transform: [{ scale: 0.992 }]
  },
  topEdge: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    height: 1,
    backgroundColor: "rgba(226, 236, 255, 0.22)"
  }
} as const;
