import { themes, type SemanticTheme } from "../semantic";
import type { SeasonalThemeId } from "../seasonal/irishSeasonalCalendar";

// Theme pack registry (THEME-001). A pack couples an identity with a concrete
// semantic theme. Until dedicated seasonal packs ship, seasonal identities
// resolve to a base appearance through the fallback map below, so Automatic
// rotation works today and upgrades pack-by-pack without touching callers.

export type ThemePackId = "light" | "dark";

export type ThemePack = {
  id: ThemePackId;
  displayName: string;
  appearance: "light" | "dark";
  theme: SemanticTheme;
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
  }
};

export function isThemePackId(value: string): value is ThemePackId {
  return Object.hasOwn(themePacks, value);
}

// Base appearance for each seasonal identity until its dedicated pack ships.
// Bright occasions lean light; the darker half of the year leans dark.
export const seasonalPackFallback: Record<SeasonalThemeId, ThemePackId> = {
  spring: "light",
  summer: "light",
  autumn: "dark",
  winter: "dark",
  stPatricks: "light",
  easter: "light",
  halloween: "dark",
  christmas: "dark"
};
