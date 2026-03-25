import * as SecureStore from "expo-secure-store";
import { Appearance, type ColorSchemeName } from "react-native";

export type ThemeMode = "light" | "dark" | "system";
export type ResolvedThemeName = "light" | "dark";

const THEME_MODE_STORAGE_KEY = "nsfinance.theme.mode";

const validThemeModes: ThemeMode[] = ["light", "dark", "system"];

function normalizeThemeMode(rawValue: string | null | undefined): ThemeMode {
  if (!rawValue) {
    return "system";
  }

  const normalized = rawValue.trim().toLowerCase();
  if (validThemeModes.includes(normalized as ThemeMode)) {
    return normalized as ThemeMode;
  }

  return "system";
}

export function getStoredThemeModeSync(): ThemeMode {
  try {
    return normalizeThemeMode(SecureStore.getItem(THEME_MODE_STORAGE_KEY));
  } catch {
    return "system";
  }
}

export async function persistThemeMode(mode: ThemeMode): Promise<void> {
  try {
    await SecureStore.setItemAsync(THEME_MODE_STORAGE_KEY, mode);
  } catch {
    // Best-effort persistence only.
  }
}

export function resolveThemeName(
  mode: ThemeMode,
  colorScheme: ColorSchemeName = Appearance.getColorScheme()
): ResolvedThemeName {
  if (mode === "light" || mode === "dark") {
    return mode;
  }

  return colorScheme === "light" ? "light" : "dark";
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

