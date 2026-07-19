import { type SemanticTheme } from "../semantic";
import { themePacks } from "./themePacks";
import { getStoredThemePreferenceSync, resolveThemePackId } from "./themePreference";

let runtimeThemeSnapshot: SemanticTheme =
  themePacks[resolveThemePackId(getStoredThemePreferenceSync())].theme;

export function getRuntimeThemeSnapshot(): SemanticTheme {
  return runtimeThemeSnapshot;
}

export function setRuntimeThemeSnapshot(nextTheme: SemanticTheme): void {
  runtimeThemeSnapshot = nextTheme;
}
