import type { ComponentType } from "react";
import { themes, type SemanticTheme } from "../semantic";
import {
  autumnTheme,
  easterTheme,
  springTheme,
  summerTheme,
  winterTheme
} from "../semantic/seasonalThemes";
import { EasterDecorativeLayer } from "../decorations/EasterDecorativeLayer";
import type { SeasonalThemeId } from "../seasonal/irishSeasonalCalendar";

// Theme pack registry (THEME-001). A pack couples an identity with a concrete
// semantic theme. Seasonal identities without a dedicated pack yet resolve to
// an existing pack through the fallback map below, so Automatic rotation works
// end to end and upgrades pack-by-pack without touching callers.

export type ThemePackId =
  | "light"
  | "dark"
  | "spring"
  | "summer"
  | "autumn"
  | "winter"
  | "easter";

export type ThemePack = {
  id: ThemePackId;
  displayName: string;
  appearance: "light" | "dark";
  theme: SemanticTheme;
  // Optional full-screen decorative layer rendered behind all content.
  // Contractually non-interactive and low-opacity: it may never obscure
  // financial content, authentication, or confirmation surfaces.
  DecorationComponent?: ComponentType;
};

export const themePacks: Record<ThemePackId, ThemePack> = {
  light: {
    id: "light",
    displayName: "Light",
    appearance: "light",
    theme: themes.light
  },
  dark: {
    id: "dark",
    displayName: "Dark",
    appearance: "dark",
    theme: themes.dark
  },
  spring: {
    id: "spring",
    displayName: "Spring",
    appearance: "light",
    theme: springTheme
  },
  summer: {
    id: "summer",
    displayName: "Summer",
    appearance: "light",
    theme: summerTheme
  },
  autumn: {
    id: "autumn",
    displayName: "Autumn",
    appearance: "dark",
    theme: autumnTheme
  },
  winter: {
    id: "winter",
    displayName: "Winter",
    appearance: "dark",
    theme: winterTheme
  },
  easter: {
    id: "easter",
    displayName: "Easter",
    appearance: "light",
    theme: easterTheme,
    DecorationComponent: EasterDecorativeLayer
  }
};

export function isThemePackId(value: string): value is ThemePackId {
  return Object.hasOwn(themePacks, value);
}

// Pack for each seasonal identity; commemorative occasions use a base
// appearance until their decorated packs ship.
export const seasonalPackFallback: Record<SeasonalThemeId, ThemePackId> = {
  spring: "spring",
  summer: "summer",
  autumn: "autumn",
  winter: "winter",
  stPatricks: "spring",
  easter: "easter",
  halloween: "autumn",
  christmas: "winter"
};
