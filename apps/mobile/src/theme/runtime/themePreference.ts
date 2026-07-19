import * as SecureStore from "expo-secure-store";
import { Appearance, type ColorSchemeName } from "react-native";
import {
  resolveSeasonalThemeId,
  toLocalCalendarDate,
  type LocalCalendarDate
} from "../seasonal/irishSeasonalCalendar";
import { isThemePackId, seasonalPackFallback, type ThemePackId } from "./themePacks";

// Theme preference model (THEME-002):
// - system: follow the OS scheme between the light and dark packs
// - fixed:  pin one pack (light, dark, or any future custom pack)
// - automatic: rotate seasonal/commemorative packs on the Irish calendar
export type ThemePreference =
  | { kind: "system" }
  | { kind: "fixed"; themeId: ThemePackId }
  | { kind: "automatic" };

// Legacy three-state mode retained as an adapter for existing call sites
// until the picker redesign exposes the full preference model directly.
export type ThemeMode = "light" | "dark" | "system";
export type ResolvedThemeName = ThemePackId;

const THEME_MODE_STORAGE_KEY = "nsfinance.theme.mode";
const PREFERENCE_ENCODING_VERSION = "v2";

export function encodeThemePreference(preference: ThemePreference): string {
  switch (preference.kind) {
    case "system":
      return `${PREFERENCE_ENCODING_VERSION}:system`;
    case "automatic":
      return `${PREFERENCE_ENCODING_VERSION}:automatic`;
    case "fixed":
      return `${PREFERENCE_ENCODING_VERSION}:fixed:${preference.themeId}`;
  }
}

export function decodeThemePreference(rawValue: string | null | undefined): ThemePreference {
  if (!rawValue) {
    return { kind: "system" };
  }

  const normalized = rawValue.trim();

  if (normalized.startsWith(`${PREFERENCE_ENCODING_VERSION}:`)) {
    const body = normalized.slice(PREFERENCE_ENCODING_VERSION.length + 1);

    if (body === "system") {
      return { kind: "system" };
    }

    if (body === "automatic") {
      return { kind: "automatic" };
    }

    if (body.startsWith("fixed:")) {
      const themeId = body.slice("fixed:".length);
      if (isThemePackId(themeId)) {
        return { kind: "fixed", themeId };
      }
    }

    return { kind: "system" };
  }

  // Legacy plain values written before the preference model existed.
  const legacy = normalized.toLowerCase();
  if (legacy === "light" || legacy === "dark") {
    return { kind: "fixed", themeId: legacy };
  }

  return { kind: "system" };
}

export function getStoredThemePreferenceSync(): ThemePreference {
  try {
    return decodeThemePreference(SecureStore.getItem(THEME_MODE_STORAGE_KEY));
  } catch {
    return { kind: "system" };
  }
}

export async function persistThemePreference(preference: ThemePreference): Promise<void> {
  try {
    await SecureStore.setItemAsync(THEME_MODE_STORAGE_KEY, encodeThemePreference(preference));
  } catch {
    // Best-effort persistence only.
  }
}

export function resolveThemePackId(
  preference: ThemePreference,
  colorScheme: ColorSchemeName = Appearance.getColorScheme(),
  localDate: LocalCalendarDate = toLocalCalendarDate(new Date())
): ThemePackId {
  switch (preference.kind) {
    case "fixed":
      return preference.themeId;
    case "automatic":
      return seasonalPackFallback[resolveSeasonalThemeId(localDate)];
    case "system":
      return colorScheme === "light" ? "light" : "dark";
  }
}

// ---------------------------------------------------------------------------
// Legacy adapter surface (consumed by the provider/menu until the picker
// redesign). New code should use the preference model above.
// ---------------------------------------------------------------------------

export function themeModeFromPreference(preference: ThemePreference): ThemeMode {
  if (preference.kind === "fixed") {
    return preference.themeId === "light" || preference.themeId === "dark"
      ? preference.themeId
      : "system";
  }

  // Automatic reports as "system" to legacy call sites, which only need to
  // know the preference is not a pinned base appearance.
  return "system";
}

export function preferenceFromThemeMode(mode: ThemeMode): ThemePreference {
  if (mode === "light" || mode === "dark") {
    return { kind: "fixed", themeId: mode };
  }

  return { kind: "system" };
}

export function getStoredThemeModeSync(): ThemeMode {
  return themeModeFromPreference(getStoredThemePreferenceSync());
}

export async function persistThemeMode(mode: ThemeMode): Promise<void> {
  await persistThemePreference(preferenceFromThemeMode(mode));
}

export function resolveThemeName(
  mode: ThemeMode,
  colorScheme: ColorSchemeName = Appearance.getColorScheme()
): ResolvedThemeName {
  return resolveThemePackId(preferenceFromThemeMode(mode), colorScheme);
}

export function cycleThemeMode(mode: ThemeMode): ThemeMode {
  if (mode === "dark") {
    return "light";
  }

  if (mode === "light") {
    return "system";
  }

  return "dark";
}
