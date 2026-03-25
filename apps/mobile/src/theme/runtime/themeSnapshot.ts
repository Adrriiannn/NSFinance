import { themes, type SemanticTheme } from "../semantic";
import { getStoredThemeModeSync, resolveThemeName } from "./themePreference";

let runtimeThemeSnapshot: SemanticTheme = themes[resolveThemeName(getStoredThemeModeSync())];

export function getRuntimeThemeSnapshot(): SemanticTheme {
  return runtimeThemeSnapshot;
}

export function setRuntimeThemeSnapshot(nextTheme: SemanticTheme): void {
  runtimeThemeSnapshot = nextTheme;
}
