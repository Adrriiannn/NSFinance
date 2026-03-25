import { gradients } from "../tokens/gradients";
import { shadows } from "../tokens/shadows";
import { getStoredThemeModeSync, resolveThemeName } from "../runtime/themePreference";
import { darkTheme } from "./dark";
import { lightTheme } from "./light";

export type SemanticTheme = typeof darkTheme | typeof lightTheme;

export const themes = {
  light: lightTheme,
  dark: darkTheme
} as const;

const startupThemeName = resolveThemeName(getStoredThemeModeSync());

export const activeTheme = themes[startupThemeName];

export { gradients, shadows };
