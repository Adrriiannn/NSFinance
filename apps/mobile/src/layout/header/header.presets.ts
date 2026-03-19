import type { HeaderPresetConfig, HeaderPresetName } from "./header.types";

export const headerPresets: Record<HeaderPresetName, HeaderPresetConfig> = {
  primaryDefault: {
    name: "primaryDefault",
    leading: "menu",
    titleMode: "centered",
    titleVariant: "default",
    hasSecondRow: false,
    preserveTrailingSlot: true
  },
  primaryGreeting: {
    name: "primaryGreeting",
    leading: "menu",
    titleMode: "leading",
    titleVariant: "greeting",
    hasSecondRow: false,
    preserveTrailingSlot: true
  },
  primaryTwoRowSelector: {
    name: "primaryTwoRowSelector",
    leading: "menu",
    titleMode: "centered",
    titleVariant: "default",
    hasSecondRow: true,
    preserveTrailingSlot: true
  },
  primaryTwoRowSearch: {
    name: "primaryTwoRowSearch",
    leading: "menu",
    titleMode: "centered",
    titleVariant: "default",
    hasSecondRow: true,
    preserveTrailingSlot: true
  },
  secondaryDetail: {
    name: "secondaryDetail",
    leading: "back",
    titleMode: "centered",
    titleVariant: "default",
    hasSecondRow: false,
    preserveTrailingSlot: true
  }
} as const;

