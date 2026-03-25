import type { ViewStyle } from "react-native";
import { borders, palette, radius, shadows, sizing, surfaces } from "../../../theme/tokens";

export type CardVariant =
  | "default"
  | "elevated"
  | "hero"
  | "insight"
  | "compact"
  | "outlined"
  | "panel"
  | "panelTitled"
  | "listRow"
  | "heroPanel"
  | "outlinedMuted";

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
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard
  },
  elevated: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard,
    ...shadows.soft
  },
  hero: {
    ...baseCard,
    minHeight: sizing.card.minHeights.hero,
    borderRadius: radius.medium,
    padding: sizing.card.padding.hero,
    ...shadows.soft
  },
  insight: {
    ...baseCard,
    minHeight: sizing.card.minHeights.insight,
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard,
    backgroundColor: surfaces.field
  },
  compact: {
    ...baseCard,
    minHeight: sizing.card.minHeights.compact,
    borderRadius: radius.medium,
    padding: sizing.card.padding.compact
  },
  outlined: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard,
    backgroundColor: surfaces.section
  },
  panel: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard
  },
  panelTitled: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard
  },
  listRow: {
    ...baseCard,
    minHeight: sizing.card.minHeights.compact,
    borderRadius: radius.medium,
    padding: sizing.card.padding.compact,
    backgroundColor: surfaces.field
  },
  heroPanel: {
    ...baseCard,
    minHeight: sizing.card.minHeights.hero,
    borderRadius: radius.medium,
    padding: sizing.card.padding.hero
  },
  outlinedMuted: {
    ...baseCard,
    minHeight: sizing.card.minHeights.standard,
    borderRadius: radius.medium,
    padding: sizing.card.padding.standard,
    backgroundColor: surfaces.muted
  }
};

export const cardStateStyles = {
  pressed: {
    opacity: 0.94,
    transform: [{ scale: 0.992 }]
  }
} as const;
